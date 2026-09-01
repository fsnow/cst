using System;
using System.Collections.Generic;
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
using CST.Avalonia.Services.Presentation;
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
        private readonly Services.Ai.IAiContextBundler? _contextBundler;   // surface B context assembly (#580)
        private readonly Services.Ai.IReaderStateService? _readerState;    // what the reader is showing (#593)
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
            string? xmlBooksDirectory = null,
            Services.Presentation.IPresentationService? presentation = null,
            ISearchService? searchService = null,
            Func<bool>? isRemoteControlAllowed = null,
            Services.Ai.IAiContextBundler? contextBundler = null,
            Services.Ai.IReaderStateService? readerState = null)
        {
            _contextBundler = contextBundler;
            _readerState = readerState;
            // Default the consent predicate to DENY: a caller that forgets to pass it must not accidentally
            // grant an agent control of the user's window. (#187)
            _navigate = presentation is null
                ? null
                : new NavigateService(presentation, searchService, isRemoteControlAllowed ?? (() => false),
                                      xmlBooksDirectory);
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

        // Surface E (#187): the shared navigate implementation (consent gate + highlight resolution +
        // presentation), or null when there is no reader to drive.
        private readonly NavigateService? _navigate;

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
                services.GetService<ISettingsService>()?.Settings?.XmlBooksDirectory,
                services.GetService<Services.Presentation.IPresentationService>(),
                services.GetService<ISearchService>(),
                // Read live so a Settings toggle applies without restarting the server. (#187)
                () => services.GetService<ISettingsService>()?.Settings?.Ai?.RemoteControlAllowed ?? false,
                services.GetService<Services.Ai.IAiContextBundler>(),
                services.GetService<Services.Ai.IReaderStateService>());

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

                // AN UNKNOWN BODY KEY IS AN ERROR, NOT SOMETHING TO SKIP PAST. System.Text.Json's default is to
                // drop what it cannot map, which turned a caller's typo into a silent no-op: `navigate` with
                // "highlight" instead of "terms" opened the book, highlighted nothing, and returned
                // highlights:0 with no note — while the SAME response for the correct key explains itself. The
                // agent's own mistake got the worse diagnostic of the two, and agents reason onward from it
                // rather than retrying. (#558)
                //
                // Safe here in a way it would not be for a public API: this is loopback-only, and an agent
                // reads its contract (llms.txt) from the SAME running instance it then calls, so a client
                // cannot be newer than the server it is talking to. The MCP surface is unaffected — those
                // tools bind to the tool interfaces through DI, never over HTTP.
                o.SerializerOptions.UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow;
            });

            // MCP surface (#191): expose the read tool set over the Streamable HTTP transport at /mcp — MCP is
            // just another transport on the same tool layer /v1 uses, not a proxy. A chat client connects via the
            // app's --mcp-bridge relay; code-capable agents keep hitting /v1 directly. Registered only when the
            // MCP permission is on (#278 Phase 4) — separate from the /v1 surface. Tool groups are registered only
            // when their backing service is present (mirroring the /v1 wiring); 'books' needs no service.
            //
            // SDK 2.0.0 implements MCP 2026-07-28 (#530). The transport is STATELESS BY DEFAULT there (SEP-2567):
            // no session is minted, no Mcp-Session-Id is issued, the standalone SSE endpoint is off, and
            // server/discover replaces the initialize handshake — with the SDK falling back for down-level
            // clients, so a mixed ecosystem is its problem rather than ours. Nothing below opts into it; it is
            // simply the default, and a wire-level test pins that so a later SDK bump can't quietly revert us to
            // session affinity. Stateless also disables sampling, elicitation and roots — we use none of them.
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
                if (_navigate is { } mcpNavigate)
                {
                    builder.Services.AddSingleton(mcpNavigate);
                    mcp.WithTools<NavigateMcpTool>(McpJson);
                }
                // Expose llms.txt as an MCP resource — an MCP client has no base URL to "fetch /llms.txt", so give
                // it the same version-stamped orientation as a readable resource. (Desktop MCP friction report)
                mcp.WithResources(new[] { BuildLlmsResource() });
            }

            // Concurrency cap (#279): the API runs IN-PROCESS with the Avalonia UI and Kestrel is otherwise
            // unbounded, so a subagent fan-out (or Chat + Cowork + Code at once) can saturate the thread pool and
            // starve the UI — and, because Claude Desktop is one-error-and-done, a single load-induced timeout
            // permanently kills that client's session. Gate the heavy tool CALLS (POSTs to /v1 + /mcp) to
            // ~ProcessorCount-1 concurrent and QUEUE the rest (FIFO). GETs — discovery and books — are left
            // unlimited. Queue is deep because a 503 rejection would itself be the fatal one-error; we queue
            // rather than reject under realistic load.
            //
            // The original rationale for exempting GETs was also to stop a long-lived MCP SSE stream from holding
            // a permit forever. That stream is gone since the 2026-07-28 stateless core (#530) — the standalone
            // SSE endpoint is disabled — so ALL MCP traffic is now POST, including the cheap discovery and
            // tools/list calls that used to ride the session. They take a permit briefly; the cap is on
            // concurrency, not rate, so this costs nothing but is worth knowing when reading the numbers.
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

            // AFTER the security gate and the concurrency cap, deliberately. This reads the whole body into
            // memory, so it must not run for a request that is about to be rejected as unauthorized or
            // queued behind the 1024-deep limiter - the cap exists because this Kestrel shares a process
            // with the UI. (fable review)
            //
            // A REJECTED BODY MUST SAY WHY, so the body is checked HERE rather than left to model binding.
            // With UnmappedMemberHandling.Disallow set above, an unknown key already makes binding fail — but
            // minimal APIs answer that internally with a 400 carrying NO BODY, which is the second half of
            // #558: the caller learns only that something was unacceptable. .NET 10 has no ThrowOnBadRequest
            // switch to route it out to middleware, so this inspects the body first and answers in the same
            // { error } shape every other failure on this surface uses, naming the offending key and the ones
            // that would have worked. Binding's own rejection stays as the backstop for anything missed.
            app.Use(async (context, next) =>
            {
                if (HttpMethods.IsPost(context.Request.Method) &&
                    ContractFor(context.Request.Path) is { } contract)
                {
                    context.Request.EnableBuffering();
                    string body;
                    using (var reader = new StreamReader(
                               context.Request.Body, Encoding.UTF8, leaveOpen: true))
                        body = await reader.ReadToEndAsync();
                    context.Request.Body.Position = 0;   // rewind for the real binder

                    if (UnknownKeyIn(body, contract) is { } bad)
                    {
                        context.Response.StatusCode = StatusCodes.Status400BadRequest;
                        await context.Response.WriteAsJsonAsync(new
                        {
                            error = $"Unknown key '{bad.Path}' in the request body. "
                                  + $"Valid keys: {ValidKeysFor(bad.Container)}."
                        });
                        return;
                    }
                }

                await next();
            });


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

            // Record start time alongside the pid so a crashed instance's recycled pid can't be mistaken for a
            // live one (#351).
            new LocalApiInfo(port, token, Environment.ProcessId, ProcessIdentity.CurrentStartToken())
                .Write(_handshakeDirectory);
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

                // Delete the handshake file FIRST, before the port actually closes (#529). Discovery is the
                // file, not a fixed port: --mcp-bridge reads local-api.json on every spawn (#278). Removing it
                // up front means a client that polls stops finding a live endpoint slightly early, which costs
                // nothing; the other order leaves a window where the file still advertises a port that is
                // already refusing connections, and a bridge that reads it there fails with a confusing
                // connection error instead of the plain "not running" it should see.
                //
                // NOTE: this ORDER is not covered by a test - the suite asserts the file is gone once StopAsync
                // returns, which holds either way. Verified by reading, and left documented rather than pinned.
                LocalApiInfo.Delete(_handshakeDirectory);

                try { await _app.StopAsync(); } catch (Exception ex) { _logger.Warning(ex, "Local API stop error"); }
                await _app.DisposeAsync();
                _app = null;
                BaseUrl = null;
                Token = null;
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

        // ---- Naming a rejected body key (#558) ----------------------------------------------------------

        /// <summary>
        /// The first body key the contract does not declare, as a dotted path, or null when every key maps.
        ///
        /// <para><b>Nested objects are checked too</b>, because the issue's second reported case IS a nested
        /// one: <c>{"query":"…","filter":{"nosuchkey":true}}</c>. <c>ToolBookFilter</c> already refuses
        /// unknown members on its own, so binding rejected that body — with an EMPTY 400, which is precisely
        /// the diagnostic this change exists to replace. Checking only the top level would have left half of
        /// #558 unfixed while claiming otherwise in llms.txt. (fable review)</para>
        ///
        /// <para>Case-insensitive, matching the binder's own behaviour, so this can never reject something
        /// binding would have accepted.</para>
        /// </summary>
        private static (string Path, Type Container)? UnknownKeyIn(string body, Type contract)
        {
            if (string.IsNullOrWhiteSpace(body)) return null;

            try
            {
                using var doc = JsonDocument.Parse(body);
                return doc.RootElement.ValueKind != JsonValueKind.Object
                    ? null                                   // not an object: binding's to judge, not ours
                    : FirstUnknown(doc.RootElement, contract, prefix: "");
            }
            catch (JsonException)
            {
                return null;   // malformed JSON is binding's to report, not a naming problem
            }
        }

        // Returns the offending key's dotted path AND the contract that should have declared it, so the
        // message lists the keys valid AT THAT LEVEL - naming the top-level ones for a bad filter sub-key
        // would send the caller looking in the wrong place.
        private static (string Path, Type Container)? FirstUnknown(JsonElement obj, Type contract, string prefix)
        {
            var properties = contract.GetProperties(BindingFlags.Public | BindingFlags.Instance);

            foreach (var prop in obj.EnumerateObject())
            {
                var match = properties.FirstOrDefault(p => string.Equals(
                    JsonNamingPolicy.CamelCase.ConvertName(p.Name), prop.Name, StringComparison.OrdinalIgnoreCase));

                if (match == null) return (prefix + prop.Name, contract);

                // Recurse into a nested contract object. Only into types of ours: a string, a number or a
                // collection has no key set to check, and reflecting over a framework type would invent
                // "valid keys" nobody can send.
                if (prop.Value.ValueKind == JsonValueKind.Object && IsRequestContract(match.PropertyType))
                {
                    var nested = FirstUnknown(prop.Value, Nullable.GetUnderlyingType(match.PropertyType)
                                                          ?? match.PropertyType,
                                              prefix + prop.Name + ".");
                    if (nested != null) return nested;
                }
            }

            return null;
        }

        private static bool IsRequestContract(Type type)
        {
            var t = Nullable.GetUnderlyingType(type) ?? type;
            return t.IsClass && t != typeof(string) && t.Namespace?.StartsWith("CST", StringComparison.Ordinal) == true;
        }

        /// <summary>
        /// The body keys a route accepts, read from the request contract itself so this cannot drift out of
        /// step with the endpoint the way a hand-maintained list would.
        /// </summary>
        private static string ValidKeysFor(Type contract) =>
            string.Join(", ", contract.GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Select(p => JsonNamingPolicy.CamelCase.ConvertName(p.Name))
                .OrderBy(n => n, StringComparer.Ordinal));

        /// <summary>
        /// The request contract behind a POST route, matched on the FULL path.
        ///
        /// <para>Suffix matching was wrong twice over. <c>/docs</c> is deliberately unauthenticated so a cold
        /// agent can orient itself, and <c>EndsWith("/search")</c> matched <c>POST /docs/search</c> — letting
        /// an unauthenticated caller reach the body-buffering below. It also missed the real route whenever
        /// the path varied harmlessly, <c>/v1/search/</c> or <c>/v1/Search</c>, silently dropping back to the
        /// bodiless 400 this exists to remove. (fable review)</para>
        ///
        /// <para>Every /v1 POST endpoint is listed: the default that caused #558 applied to the whole
        /// surface, so a partial map would leave the next endpoint dropping keys exactly as before.</para>
        /// </summary>
        private static Type? ContractFor(PathString path)
        {
            if (!path.HasValue) return null;
            var p = path.Value!.TrimEnd('/');
            const string v = "/" + ApiVersion;

            return p.Equals(v + "/search", StringComparison.OrdinalIgnoreCase) ? typeof(SearchToolRequest)
                 : p.Equals(v + "/occurrences", StringComparison.OrdinalIgnoreCase) ? typeof(OccurrenceRequest)
                 : p.Equals(v + "/dictionary/lookup", StringComparison.OrdinalIgnoreCase) ? typeof(DictionaryRequest)
                 : p.Equals(v + "/passage", StringComparison.OrdinalIgnoreCase) ? typeof(PassageHttpRequest)
                 : p.Equals(v + "/ai/context-preview", StringComparison.OrdinalIgnoreCase) ? typeof(ContextPreviewRequest)
                 : p.Equals(v + "/convert", StringComparison.OrdinalIgnoreCase) ? typeof(ConvertRequest)
                 : p.Equals(v + "/navigate", StringComparison.OrdinalIgnoreCase) ? typeof(NavigateRequest)
                 : p.Equals(v + "/forms", StringComparison.OrdinalIgnoreCase) ? typeof(LemmaFormsUnionRequest)
                 : null;
        }

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

            if (_contextBundler is { } bundler && _readerState is { } readerState)
            {
                // What surface B would send a model for WHAT THE READER IS LOOKING AT — no model call, no key,
                // nothing leaves the machine. The body carries only what the user chooses; book, position and
                // selection come from live app state, because the input derivation is the part most worth
                // previewing (#593). A preview that accepted them as parameters would skip the scroll-derived
                // position and the WebView selection round-trip while appearing to validate the whole path.
                app.MapPost(v + "/ai/context-preview",
                    async (ContextPreviewRequest req, CancellationToken ct) =>
                {
                    // No focus signal, and stated rather than defaulted (#938). An HTTP or MCP caller is
                    // an outside agent: it has no click to remember, so several open book windows genuinely
                    // are ambiguous and AmbiguousBookWindow below is the right answer for it.
                    var state = await readerState.GetCurrentAsync(
                        Services.Ai.ReaderFocusSignal.None, ct);
                    if (state.State is not { } reader)
                    {
                        // Refusals, never fallbacks: an unknown position must not read from the book start, or
                        // the answer is a confident, app-cited response about a passage the user is not looking
                        // at, with no signal that anything went wrong. (AI_SURFACE_B.md §6)
                        var (message, reason) = state.Problem switch
                        {
                            Services.Ai.ReaderStateProblem.PositionUnknown =>
                                ("The reading position could not be determined.", "position-unknown"),
                            Services.Ai.ReaderStateProblem.AmbiguousInMultiBook =>
                                ("This is a multi-book volume, where a paragraph number needs a sub-book code " +
                                 "the reader does not report — so the passage cannot be identified unambiguously.",
                                 "ambiguous-multi-book"),
                            Services.Ai.ReaderStateProblem.AmbiguousBookWindow =>
                                ("More than one book window is active and none can be shown to be the one in use.",
                                 "ambiguous-book-window"),
                            _ => ("No book is open.", "no-book-open"),
                        };
                        return Results.Json(new { error = message, reason }, statusCode: 409);
                    }

                    var task = ParseTask(req.Task);
                    if (task is null)
                        return Results.BadRequest(new { error = $"Unknown task '{req.Task}'.", reason = "unknown-task" });

                    try
                    {
                        var bundle = await bundler.BuildAsync(
                            new Services.Ai.AiContextRequest(
                                task.Value,
                                reader.BookId,
                                req.OutputLanguage ?? "English",
                                new NavigationReference.Paragraph(reader.Paragraph),
                                reader.SelectionText,
                                req.UserQuestion,
                                // Carried, not collapsed: a selection the reader could not read is a different
                                // state from no selection, and the preview exists to show the real input. (#581)
                                reader.SelectionUnavailable),
                            ct);

                        return Results.Json(bundle);
                    }
                    catch (Services.Ai.AiContextException ex)
                    {
                        // Ordinary data states reach here, not just bugs: a paragraph the marker index does not
                        // carry (ranged `@n` like "16-26" is not indexed at all — 86 of the 217 books contain
                        // some, #444), or a catalogued book whose XML was never downloaded. Every other route on
                        // this surface answers such states with shaped JSON; letting this one throw would give an
                        // agent a bare 500, and llms.txt promises a 409.
                        _logger.Debug("Context preview could not assemble a bundle: {Reason}", ex.Message);
                        return Results.Json(
                            new
                            {
                                error = "The passage the reader is on could not be read.",
                                reason = "passage-unavailable",
                            },
                            statusCode: 409);
                    }
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

            // Navigate (#187) — the ONLY endpoint that acts on the user's window rather than just reading the
            // corpus, so it is gated behind explicit remote-control consent. It is mapped unconditionally (when a
            // reader is wired) and answers 403 when consent is off, so an agent gets an actionable "ask the user
            // to turn this on" instead of a 404 it would read as "this build has no navigate".
            if (_navigate is { } navigate)
            {
                app.MapPost(v + "/navigate", async (NavigateRequest req, CancellationToken ct) =>
                {
                    var (outcome, response) = await navigate.NavigateAsync(req, ct);
                    // Always serialize the FULL response, including on failures: llms.txt tells agents to check
                    // `presented`, so every outcome must actually carry it. (fable MED-4)
                    int status = outcome switch
                    {
                        NavigateOutcome.ConsentDenied => StatusCodes.Status403Forbidden,
                        NavigateOutcome.UnknownBook => StatusCodes.Status404NotFound,
                        // The arguments can't be satisfied — retrying them unchanged never will.
                        NavigateOutcome.InvalidRequest => StatusCodes.Status400BadRequest,
                        // The app's STATE prevented it (no reader window, duplicate open): retry later, don't
                        // "fix" the arguments.
                        NavigateOutcome.NotPresented => StatusCodes.Status409Conflict,
                        _ => StatusCodes.Status200OK
                    };
                    return Results.Json(response, statusCode: status);
                });
            }

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

        private static Services.Ai.AiTask? ParseTask(string? task) => task?.ToLowerInvariant() switch
        {
            "explain" => Services.Ai.AiTask.Explain,
            "translate" => Services.Ai.AiTask.Translate,
            "grammar" => Services.Ai.AiTask.Grammar,
            "wordbyword" or "word-by-word" => Services.Ai.AiTask.WordByWord,
            _ => null,
        };

        /// <summary>Body for /v1/ai/context-preview — only what the user chooses; the rest is live app state.</summary>
        private sealed record ContextPreviewRequest(
            string Task, string? UserQuestion = null, string? OutputLanguage = null);

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
