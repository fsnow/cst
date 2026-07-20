using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.RateLimiting;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using System.Text.Json;
using System.Text.Json.Serialization;
using CST;
using CST.Conversion;
using CST.Navigation;
using CST.Tools;
using CST.Avalonia.Services.LocalApi.Lemma;
using CST.Avalonia.Services.LocalApi.Mcp;
using ModelContextProtocol.Server;
using Serilog;

namespace CST.Avalonia.Services.LocalApi
{
    /// <summary>
    /// The opt-in loopback API server that exposes the corpus tools to agents (AI_INTEGRATION.md surface C).
    /// Binds <c>127.0.0.1</c> on an ephemeral port, mints a per-session bearer token, and advertises both via
    /// <see cref="LocalApiInfo"/> (<c>local-api.json</c>). Every request must carry the token and must not
    /// carry an <c>Origin</c> header (no browsers) — the honest threat model is that this stops browser-origin
    /// attacks (rebinding/CSRF), not a malicious same-user local process, which is the OS's boundary.
    /// This PR is the secure skeleton: only <c>/v1/status</c>. Tool endpoints follow.
    /// A startable/stoppable host so live enable/disable can be wired later; for now it's gated at launch.
    /// </summary>
    public sealed class LocalApiServer : IAsyncDisposable
    {
        private const string ApiVersion = "v1";

        // JSON for MCP tool results/params: camelCase + string enums (e.g. page editions, pitaka, scripts),
        // so results are agent-readable like the /v1 surface.
        private static readonly JsonSerializerOptions McpJson = new(JsonSerializerDefaults.Web)
        {
            // The MCP SDK freezes these options; a reflection-based resolver must be set first (non-AOT app).
            TypeInfoResolver = new System.Text.Json.Serialization.Metadata.DefaultJsonTypeInfoResolver(),
            Converters = { new JsonStringEnumConverter() }
        };

        private readonly string _appVersion;
        private readonly string _handshakeDirectory;
        private readonly Serilog.ILogger _logger;
        private readonly ISearchTool? _search;
        private readonly IDictionaryTool? _dictionary;
        private readonly IPassageTool? _passage;
        private readonly IScriptTool? _script;
        private readonly ILemmaSearchService? _lemma;   // DPD-lemma back-lookup + forward-expansion (may be null / asset-absent)
        private readonly ILemmaReportService? _lemmaReport;   // the rendered lemma dossier
        private readonly int _port;              // fixed loopback port, or <= 0 for ephemeral
        private readonly string? _configuredToken; // persisted bearer token, or null to generate one
        private readonly bool _restApiEnabled;   // map the /v1 REST tool endpoints
        private readonly bool _mcpEnabled;       // register + map the /mcp MCP surface

        private WebApplication? _app;

        // Serialize Start/Stop so a live enable/disable toggle (promised in the class doc) can't run two
        // check-then-act starts concurrently and leave two Kestrels bound. (#306 A1-8)
        private readonly SemaphoreSlim _lifecycleLock = new(1, 1);

        /// <summary>The base URL once started (e.g. <c>http://127.0.0.1:52344</c>), or null if not running.</summary>
        public string? BaseUrl { get; private set; }

        /// <summary>The per-session bearer token once started, or null if not running.</summary>
        public string? Token { get; private set; }

        public bool IsRunning => _app != null;

        public LocalApiServer(
            string appVersion, string handshakeDirectory, Serilog.ILogger logger,
            ISearchTool? search = null, IDictionaryTool? dictionary = null, IPassageTool? passage = null,
            IScriptTool? script = null, ILemmaSearchService? lemma = null, ILemmaReportService? lemmaReport = null,
            int port = 0, string? token = null, bool restApiEnabled = true, bool mcpEnabled = true,
            string? xmlBooksDirectory = null)
        {
            _appVersion = appVersion;
            _handshakeDirectory = handshakeDirectory;
            _logger = logger.ForContext<LocalApiServer>();
            _search = search;
            _dictionary = dictionary;
            _passage = passage;
            _script = script;
            _lemma = lemma;
            _lemmaReport = lemmaReport;
            _port = port;
            _configuredToken = token;
            _restApiEnabled = restApiEnabled;
            _mcpEnabled = mcpEnabled;
            _xmlBooksDirectory = xmlBooksDirectory;
        }

        // Corpus dir, used once at startup to prime the Multi-book sub-book codes (#266); null → codes stay empty.
        private readonly string? _xmlBooksDirectory;

        /// <summary>
        /// Build a server by resolving EVERY tool adapter from the DI container. This is the single place the
        /// tools are gathered, so a forgotten tool (the /v1/scripts-404 class of bug, where a registered tool
        /// was simply not passed to the server) is caught by one composition test instead of shipping. The app
        /// and the test both go through here.
        /// </summary>
        public static LocalApiServer FromServiceProvider(
            IServiceProvider services, string appVersion, string handshakeDirectory, Serilog.ILogger logger,
            int port = 0, string? token = null, bool restApiEnabled = true, bool mcpEnabled = true)
            => new LocalApiServer(appVersion, handshakeDirectory, logger,
                services.GetService<ISearchTool>(),
                services.GetService<IDictionaryTool>(),
                services.GetService<IPassageTool>(),
                services.GetService<IScriptTool>(),
                services.GetService<ILemmaSearchService>(),
                services.GetService<ILemmaReportService>(), port, token, restApiEnabled, mcpEnabled,
                services.GetService<ISettingsService>()?.Settings?.XmlBooksDirectory);

        public async Task StartAsync(CancellationToken ct = default)
        {
            await _lifecycleLock.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                await StartCoreAsync(ct).ConfigureAwait(false);
            }
            finally { _lifecycleLock.Release(); }
        }

        private async Task StartCoreAsync(CancellationToken ct)
        {
            if (_app != null) return;

            // Reuse the persisted token when supplied (stable config across launches), else mint one. (#275)
            string token = string.IsNullOrEmpty(_configuredToken) ? ApiToken.Generate() : _configuredToken!;

            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders(); // don't spam stdout; the app logs via Serilog
            // ...but bridge ASP.NET's own logs (500s, pipeline faults) into Serilog at Warning+, so a server-side
            // failure isn't silently swallowed. (#306 A1-7)
            builder.Logging.AddSerilog(Serilog.Log.Logger, dispose: false);
            builder.Logging.SetMinimumLevel(LogLevel.Warning);
            // Fixed loopback port when configured (so a client config stays valid), else ephemeral. (#275)
            builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, _port > 0 ? _port : 0));
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                o.SerializerOptions.Converters.Add(new ScriptJsonConverter()); // reject Ipe/Unknown outputScript (before the enum factory)
                o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()); // "Latin" not 3, for other enums
            });

            // MCP surface (#191): expose the read tool set over the Streamable HTTP transport at /mcp — MCP is
            // just another transport on the same tool layer /v1 uses, not a proxy. A chat client connects via the
            // app's --mcp-bridge relay; code-capable agents keep hitting /v1 directly. Registered only when the
            // MCP permission is on (#278 Phase 4) — separate from the /v1 surface. Tool groups are registered only
            // when their backing service is present (mirroring the /v1 wiring); 'books' needs no service.
            if (_mcpEnabled)
            {
                var mcp = builder.Services.AddMcpServer().WithHttpTransport();
                mcp.WithTools<BooksMcpTool>(McpJson);
                if (_search is { } mcpSearch)
                {
                    builder.Services.AddSingleton(mcpSearch);
                    mcp.WithTools<SearchMcpTool>(McpJson);
                }
                if (_passage is { } mcpPassage)
                {
                    builder.Services.AddSingleton(mcpPassage);
                    mcp.WithTools<PassageMcpTool>(McpJson);
                }
                if (_script is { } mcpScript)
                {
                    builder.Services.AddSingleton(mcpScript);
                    mcp.WithTools<ScriptMcpTool>(McpJson);
                }
                if (_dictionary is { } mcpDictionary)
                {
                    builder.Services.AddSingleton(mcpDictionary);
                    mcp.WithTools<DictionaryMcpTool>(McpJson);
                }
                if (_lemma is { IsAvailable: true } mcpLemma)
                {
                    builder.Services.AddSingleton(mcpLemma);
                    mcp.WithTools<LemmaMcpTool>(McpJson);
                }
                // Expose llms.txt as an MCP resource — an MCP client has no base URL to "fetch /llms.txt", so give
                // it the same version-stamped orientation as a readable resource. (Desktop MCP friction report)
                mcp.WithResources(new[] { BuildLlmsResource() });
            }

            // Concurrency cap (#279): the API runs IN-PROCESS with the Avalonia UI and Kestrel is otherwise
            // unbounded, so a subagent fan-out (or Chat + Cowork + Code at once) can saturate the thread pool and
            // starve the UI — and, because Claude Desktop is one-error-and-done, a single load-induced timeout
            // permanently kills that client's session. Gate the heavy tool CALLS (POSTs to /v1 + /mcp) to
            // ~ProcessorCount-1 concurrent and QUEUE the rest (FIFO). GETs — discovery, books, and the long-lived
            // MCP SSE stream — are left unlimited so a stream can't hold a permit forever. Queue is deep because a
            // 503 rejection would itself be the fatal one-error; we queue rather than reject under realistic load.
            int toolPermits = Math.Max(1, Environment.ProcessorCount - 1);
            builder.Services.AddRateLimiter(options =>
            {
                options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(ctx =>
                    HttpMethods.IsPost(ctx.Request.Method)
                        ? RateLimitPartition.GetConcurrencyLimiter("tool-calls", _ => new ConcurrencyLimiterOptions
                        {
                            PermitLimit = toolPermits,
                            QueueProcessingOrder = QueueProcessingOrder.OldestFirst,
                            QueueLimit = 1024,
                        })
                        : RateLimitPartition.GetNoLimiter<string>("unlimited"));
            });

            var app = builder.Build();
            bool started = false;
            try
            {

            // Security gate: no browsers, loopback host only, valid bearer token.
            app.Use(async (context, next) =>
            {
                if (context.Request.Headers.ContainsKey("Origin"))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden; // reject browser-origin requests
                    return;
                }
                if (!IsLoopbackHost(context.Request.Host.Host))
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return;
                }
                // Discovery (llms.txt / docs) is unauthenticated so an agent can bootstrap: read the docs,
                // learn the handshake, then authenticate. It carries no secrets. Everything else needs the token.
                if (!IsDiscoveryPath(context.Request.Path) && !IsAuthorized(context, token))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                await next();
            });

            // Apply the concurrency cap AFTER the security gate, so unauthorized requests never consume a permit.
            app.UseRateLimiter();

            // Unauthenticated root pointer, so an agent that connects via local-api.json isn't left staring at
            // an empty "/" — it names where the docs and status live. (Cold-agent test finding.)
            app.MapGet("/", () => Results.Json(
                new RootResponse("CST Reader local API", _appVersion, ApiVersion, "/llms.txt", "/" + ApiVersion + "/status")));

            app.MapGet("/" + ApiVersion + "/status",
                () => Results.Json(new StatusResponse(_appVersion, ApiVersion, "ok")));

            // Unauthenticated front door: the agent's orientation (endpoints, conventions, auth handshake).
            // Version-stamped so it can't be mistaken for a different build's surface.
            app.MapGet("/llms.txt", () => Results.Text(BuildThinIndex(), "text/markdown; charset=utf-8"));
            // Progressive discovery (#259): the whole document in one fetch, and per-topic slices — all from
            // the single llms.txt source. Unauthenticated, like /llms.txt (see IsDiscoveryPath).
            app.MapGet("/llms-full.txt", () => Results.Text(BuildLlmsText(), "text/markdown; charset=utf-8"));
            app.MapGet("/docs/{topic}.md", (string topic) =>
            {
                var doc = BuildDocSlice(topic);
                return doc is null
                    ? Results.NotFound(new { error =
                        $"Unknown docs topic '{topic}'. Available: {string.Join(", ", LayeredDocs.Topics.Select(t => t.Topic))}." })
                    : Results.Text(doc, "text/markdown; charset=utf-8");
            });

            if (_restApiEnabled)
                MapToolEndpoints(app);

            // Streamable HTTP MCP endpoint (read tool set + llms.txt resource). Behind the same security
            // middleware as everything else: requires the bearer token and rejects Origin-bearing requests.
            // Mapped only when the MCP permission is on (#278 Phase 4). (#191)
            if (_mcpEnabled)
                app.MapMcp("/mcp");

            // Prime the Multi-book sub-book codes for the `books` catalog (#266) — parses the 7 Multi books once
            // off the shared cache. Best-effort: a missing/unreadable corpus just leaves those codes empty.
            await MultiBookCodes.PrimeAsync(_xmlBooksDirectory, ct);

            await app.StartAsync(ct);
            started = true;

            int port = ResolvePort(app);
            _app = app;
            Token = token;
            BaseUrl = $"http://127.0.0.1:{port}";

            new LocalApiInfo(port, token, Environment.ProcessId).Write(_handshakeDirectory);
            _logger.Information("Local API listening on {BaseUrl} (rest={Rest}, mcp={Mcp})",
                BaseUrl, _restApiEnabled, _mcpEnabled);
            }
            catch (Exception ex)
            {
                // A throw anywhere between Build() and Write() (ResolvePort, port already taken, handshake write)
                // must not leak the built host or orphan a *running* Kestrel with _app still null. (#306 A1-5)
                _logger.Error(ex, "Local API failed to start; cleaning up");
                if (started) { try { await app.StopAsync().ConfigureAwait(false); } catch { /* best-effort */ } }
                await app.DisposeAsync().ConfigureAwait(false);
                _app = null;
                BaseUrl = null;
                Token = null;
                throw;
            }
        }

        public async Task StopAsync()
        {
            await _lifecycleLock.WaitAsync().ConfigureAwait(false);
            try
            {
                if (_app == null) return;
                try { await _app.StopAsync(); } catch (Exception ex) { _logger.Warning(ex, "Local API stop error"); }
                await _app.DisposeAsync();
                _app = null;
                BaseUrl = null;
                Token = null;
                LocalApiInfo.Delete(_handshakeDirectory);
                _logger.Information("Local API stopped");
            }
            finally { _lifecycleLock.Release(); }
        }

        public async ValueTask DisposeAsync()
        {
            await StopAsync().ConfigureAwait(false);
            _lifecycleLock.Dispose();
        }

        private static bool IsLoopbackHost(string host) =>
            host is "127.0.0.1" or "localhost" or "[::1]" or "::1";

        private static Script ParseScript(string? name) =>
            Enum.TryParse<Script>(name, ignoreCase: true, out var script)
                && Enum.IsDefined(script)                         // reject undefined ordinals like "99" (→ empty output)
                && script is not (Script.Ipe or Script.Unknown)   // never expose the internal IPE font encoding
                ? script : Script.Latin;

        private static bool BookExists(string? bookId) =>
            !string.IsNullOrEmpty(bookId) &&
            Books.Inst.Any(b => string.Equals(b.FileName, bookId, StringComparison.OrdinalIgnoreCase));

        private static bool IsDiscoveryPath(PathString path) =>
            !path.HasValue || path == "/"
            || path.Equals("/llms.txt", StringComparison.OrdinalIgnoreCase)
            // The progressive-discovery docs are the same public orientation content as /llms.txt, so they
            // carry no secrets and must not 401 a cold agent following the pointer. (#259, cf. #306 A1-6)
            || path.Equals("/llms-full.txt", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/docs", StringComparison.OrdinalIgnoreCase)
            || path.Equals("/" + ApiVersion + "/status", StringComparison.OrdinalIgnoreCase);

        // The version-stamped FULL llms.txt body (markers stripped), served at GET /llms-full.txt and as the
        // MCP llms.txt resource. /llms.txt itself serves the thin index (BuildThinIndex). Single source (the
        // embedded resource), so none of the three can drift. Version-stamped per build.
        // Whether the DPD/lemma docs should be served: the asset must be installed (same contract as the
        // endpoints/MCP tools). Absent → GateDpd drops the <!--dpd--> regions so agents don't discover 503-only
        // functionality. Evaluated per request (restart-to-activate: the provider opens the file once at startup).
        private bool DpdDocsAvailable => _lemma?.IsAvailable == true;

        private string BuildLlmsText()
        {
            var body = ReadResource("LocalApi.llms.txt")
                ?? "# CST Reader Local API\n\n(llms.txt resource missing)\n";
            body = LayeredDocs.GateDpd(body, DpdDocsAvailable);
            // Strip the progressive-discovery region markers; the full document is the monolith. (#259)
            return $"<!-- CST Reader {_appVersion} | API {ApiVersion} -->\n" + LayeredDocs.StripMarkers(body);
        }

        // The thin index served at /llms.txt (#259): the monolith minus every topic region, plus the pointer.
        private string BuildThinIndex()
        {
            var body = ReadResource("LocalApi.llms.txt")
                ?? "# CST Reader Local API\n\n(llms.txt resource missing)\n";
            body = LayeredDocs.GateDpd(body, DpdDocsAvailable);
            return $"<!-- CST Reader {_appVersion} | API {ApiVersion} -->\n" + LayeredDocs.ThinIndex(body);
        }

        // A per-topic slice of the SAME source (#259) — the concatenation of that topic's marked regions,
        // stamped like the full doc. Null for an unknown topic. Single-source, so it can't drift.
        private string? BuildDocSlice(string topic)
        {
            var raw = ReadResource("LocalApi.llms.txt");
            if (raw is not null) raw = LayeredDocs.GateDpd(raw, DpdDocsAvailable);
            var slice = raw is null ? null : LayeredDocs.Slice(raw, topic);
            return slice is null ? null : $"<!-- CST Reader {_appVersion} | API {ApiVersion} -->\n" + slice;
        }

        // The same orientation doc as an MCP resource, so a Streamable-HTTP client (which has no base URL to
        // "fetch /llms.txt") can read it. Built from a closure over the stamped text — no DI/static needed.
        private McpServerResource BuildLlmsResource()
        {
            string text = BuildLlmsText();
            return McpServerResource.Create(
                () => text,
                new McpServerResourceCreateOptions
                {
                    UriTemplate = "cst:///llms.txt",
                    Name = "llms.txt",
                    Title = "CST Reader local API — orientation (llms.txt)",
                    Description = "Orientation for the CST Reader local API: query modes (Exact = exact inflected "
                        + "form; Wildcard/Regex), sandhi/compound guidance, the output scripts, apparatus "
                        + "conventions, paging, and the tool set. Read this first.",
                    MimeType = "text/markdown",
                });
        }

        private static string? ReadResource(string endsWith)
        {
            var assembly = typeof(LocalApiServer).Assembly;
            var name = assembly.GetManifestResourceNames()
                .FirstOrDefault(n => n.EndsWith(endsWith, StringComparison.OrdinalIgnoreCase));
            if (name is null) return null;
            using var stream = assembly.GetManifestResourceStream(name);
            if (stream is null) return null;
            using var reader = new StreamReader(stream);
            return reader.ReadToEnd();
        }

        private static bool IsAuthorized(HttpContext context, string token)
        {
            string header = context.Request.Headers.Authorization.ToString();
            if (!header.StartsWith("Bearer ", StringComparison.Ordinal)) return false;
            // Constant-time compare so a wrong token can't be timed out char by char.
            return CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(header.Substring("Bearer ".Length)),
                Encoding.UTF8.GetBytes(token));
        }

        private static int ResolvePort(WebApplication app)
        {
            var addresses = app.Services.GetRequiredService<IServer>()
                .Features.Get<IServerAddressesFeature>()!.Addresses;
            return new Uri(addresses.First()).Port;
        }

        // Map the surface-C tool endpoints for whichever tools were provided. Each is a thin adapter over an
        // already-tested tool; the tools themselves keep the corpus formats behind the boundary.
        private void MapToolEndpoints(WebApplication app)
        {
            string v = "/" + ApiVersion;

            if (_search is { } search)
            {
                app.MapPost(v + "/search",
                    async (SearchToolRequest req, CancellationToken ct) => Results.Json(await search.SearchAsync(req, ct)));
                app.MapPost(v + "/occurrences", async (OccurrenceRequest req, CancellationToken ct) =>
                    BookExists(req.BookId)
                        ? Results.Json(await search.GetOccurrencesAsync(req, ct))
                        : Results.NotFound(new { error = $"Unknown book '{req.BookId}'." }));
            }

            if (_dictionary is { } dictionary)
            {
                app.MapGet(v + "/dictionary/languages", () => Results.Json(dictionary.Languages));
                app.MapPost(v + "/dictionary/lookup",
                    async (DictionaryRequest req, CancellationToken ct) => Results.Json(await dictionary.LookupAsync(req, ct)));
            }

            if (_passage is { } passage)
            {
                app.MapPost(v + "/passage", async (PassageHttpRequest req, CancellationToken ct) =>
                {
                    if (!BookExists(req.BookId))
                        return Results.NotFound(new { error = $"Unknown book '{req.BookId}'." });
                    NavigationReference reference = req.Paragraph is int n
                        ? new NavigationReference.Paragraph(n, req.BookCode)
                        : new NavigationReference.WholeBook();
                    var pr = new PassageRequest(req.BookId, reference, req.Cursor, req.MaxChars,
                        req.OutputScript, req.IncludeFootnotes, req.StructuredNotes);
                    return Results.Json(await passage.FetchPassageAsync(pr, ct));
                });
            }

            if (_script is { } scriptTool)
            {
                app.MapGet(v + "/scripts", () => Results.Json(scriptTool.Scripts));
                app.MapPost(v + "/convert",
                    (ConvertRequest req) => Results.Json(scriptTool.Convert(req)));
            }

            // Book catalog — agents need book ids to call the other tools. Always available (no service
            // needed). Nav-path names are stored Devanagari; romanize to the requested script (Latin default,
            // like every other endpoint) via ?script=. (#186 cold-agent test: names came back Devanagari.)
            app.MapGet(v + "/books", (string? script, string? pitaka, string? commentaryLevel, int? skip, int? take) =>
            {
                var outputScript = ParseScript(script);
                Pitaka? p = Enum.TryParse<Pitaka>(pitaka, ignoreCase: true, out var pp) ? pp : null;
                CommentaryLevel? cl = Enum.TryParse<CommentaryLevel>(commentaryLevel, ignoreCase: true, out var cc) ? cc : null;
                // Filter (pitaka / commentary level) + paging so the 217-book catalog can't overflow a caller. (#191 Cowork)
                return Results.Json(BookCatalog.List(outputScript, p, cl, skip ?? 0, take ?? BookCatalog.DefaultTake));
            });

            // Lemma search (#247, DPD-lemma). Two hops: back-lookup a surface form to its candidate lemmas,
            // then forward-expand a chosen lemma to its ATTESTED paradigm WITH corpus counts (counts from the
            // index, not DPD; a synthetic form returns 0). `script` sets both the input form's script and the
            // output script (default Latin). Mapped only when the DPD-lemma asset is present.
            if (_lemma is { } lemma)
            {
                IResult LemmaUnavailable() => Results.Json(
                    new { error = "The DPD-lemma dataset is not installed; lemma search is unavailable." }, statusCode: 503);

                app.MapGet(v + "/lemma/{form}", (string form, string? script) =>
                {
                    if (!lemma.IsAvailable) return LemmaUnavailable();
                    var outputScript = ParseScript(script);
                    var res = lemma.ResolveWord(form, outputScript);
                    return res is null
                        ? Results.NotFound(new { error = $"No lemma resolves the form '{form}'." })
                        : Results.Json(LemmaApi.ToLookup(form, res, outputScript));
                });

                app.MapGet(v + "/forms/{lemmaId:long}", async (long lemmaId, bool? family, string? script, CancellationToken ct) =>
                {
                    if (!lemma.IsAvailable) return LemmaUnavailable();
                    var outputScript = ParseScript(script);
                    var res = await lemma.ExpandAndSearchAsync(lemmaId, family ?? false, null, outputScript, ct);
                    return res is null
                        ? Results.NotFound(new { error = $"Unknown lemmaId {lemmaId}." })
                        : Results.Json(LemmaApi.ToForms(res, outputScript, family ?? false));
                });

                // UNION of several lemmas' forms as ONE de-duplicated count — a scoped set like a CONJUGATION (pass
                // the verbal-pos relatedLemmas of a verb). Reuses the same union plumbing the report uses. (#247)
                app.MapPost(v + "/forms", async (LemmaFormsUnionRequest req, CancellationToken ct) =>
                {
                    if (!lemma.IsAvailable) return LemmaUnavailable();
                    var ids = (req.LemmaIds ?? System.Array.Empty<long>()).Distinct().Take(LemmaApi.MaxUnionLemmas).ToList();
                    if (ids.Count == 0) return Results.BadRequest(new { error = "lemmaIds is required (a non-empty array)." });
                    var res = await lemma.ExpandAndSearchSetAsync(ids, ParseScript(req.Script), ct);
                    return res is null
                        ? Results.NotFound(new { error = "None of the given lemmaIds are known." })
                        : Results.Json(LemmaApi.ToFormsUnion(res, ids));
                });

                // Sandhi/compound deconstruction: a word -> its ranked constituent-part splits (DPD deconstructor).
                // The word->parts primitive only; the caller composes part -> /v1/lemma -> /v1/dictionary. (#383)
                app.MapGet(v + "/deconstruct/{word}", (string word, string? script) =>
                {
                    if (!lemma.IsAvailable) return LemmaUnavailable();
                    var outputScript = ParseScript(script);
                    var res = lemma.Deconstruct(word, outputScript);
                    return res is null
                        ? Results.NotFound(new { error = LemmaApi.DeconstructNotFoundNote(word, lemma.Meta?.Scope) })
                        : Results.Json(LemmaApi.ToDeconstruct(word, res, outputScript));
                });
            }

            // Lemma dossier (rendered HTML). The GUI renders it in-process; this endpoint gives agents/humans
            // the same report. `script` selects the render script (default Latin). HTML only (no IPE leak).
            if (_lemmaReport is { } report)
            {
                app.MapGet(v + "/lemma-report/{lemmaId:long}", async (long lemmaId, string? script, CancellationToken ct) =>
                {
                    // Same asset-absent contract as the sibling lemma endpoints: a 503 JSON, not a bare 404.
                    if (!report.IsAvailable) return Results.Json(
                        new { error = "The DPD-lemma dataset is not installed; lemma search is unavailable." }, statusCode: 503);
                    var rep = await report.BuildAsync(lemmaId, ct);
                    return rep is null
                        ? Results.NotFound(new { error = $"Unknown lemmaId {lemmaId}." })
                        : Results.Content(LemmaReportRenderer.Render(rep, ParseScript(script)), "text/html; charset=utf-8");
                });
            }

            // Surface which tool groups got wired, so a missing DI hand-off (e.g. a null IScriptTool leaving
            // /v1/scripts + /v1/convert unmapped -> 404) is visible in the log instead of only at call time.
            _logger.Information(
                "Local API tools wired: search={Search} dictionary={Dictionary} passage={Passage} script={Script} lemma={Lemma}",
                _search != null, _dictionary != null, _passage != null, _script != null,
                _lemma is { IsAvailable: true });
        }

        private sealed record RootResponse(string Name, string App, string Api, string Docs, string Status);

        private sealed record StatusResponse(string App, string Api, string Status);

        // Flat request for /v1/passage — avoids polymorphic JSON for NavigationReference. Paragraph (or none =
        // whole book) unless a Cursor from a prior response is supplied to page forward/backward.
        private sealed record PassageHttpRequest(
            string BookId,
            int? Paragraph = null,
            string? BookCode = null,
            int? Cursor = null,
            int MaxChars = 1200,
            Script OutputScript = Script.Latin,
            bool IncludeFootnotes = false,
            bool StructuredNotes = false);
    }
}
