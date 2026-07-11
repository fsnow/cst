using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
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
        private readonly int _port;              // fixed loopback port, or <= 0 for ephemeral
        private readonly string? _configuredToken; // persisted bearer token, or null to generate one

        private WebApplication? _app;

        /// <summary>The base URL once started (e.g. <c>http://127.0.0.1:52344</c>), or null if not running.</summary>
        public string? BaseUrl { get; private set; }

        /// <summary>The per-session bearer token once started, or null if not running.</summary>
        public string? Token { get; private set; }

        public bool IsRunning => _app != null;

        public LocalApiServer(
            string appVersion, string handshakeDirectory, Serilog.ILogger logger,
            ISearchTool? search = null, IDictionaryTool? dictionary = null, IPassageTool? passage = null,
            IScriptTool? script = null, int port = 0, string? token = null)
        {
            _appVersion = appVersion;
            _handshakeDirectory = handshakeDirectory;
            _logger = logger.ForContext<LocalApiServer>();
            _search = search;
            _dictionary = dictionary;
            _passage = passage;
            _script = script;
            _port = port;
            _configuredToken = token;
        }

        /// <summary>
        /// Build a server by resolving EVERY tool adapter from the DI container. This is the single place the
        /// tools are gathered, so a forgotten tool (the /v1/scripts-404 class of bug, where a registered tool
        /// was simply not passed to the server) is caught by one composition test instead of shipping. The app
        /// and the test both go through here.
        /// </summary>
        public static LocalApiServer FromServiceProvider(
            IServiceProvider services, string appVersion, string handshakeDirectory, Serilog.ILogger logger,
            int port = 0, string? token = null)
            => new LocalApiServer(appVersion, handshakeDirectory, logger,
                services.GetService<ISearchTool>(),
                services.GetService<IDictionaryTool>(),
                services.GetService<IPassageTool>(),
                services.GetService<IScriptTool>(), port, token);

        public async Task StartAsync(CancellationToken ct = default)
        {
            if (_app != null) return;

            // Reuse the persisted token when supplied (stable config across launches), else mint one. (#275)
            string token = string.IsNullOrEmpty(_configuredToken) ? ApiToken.Generate() : _configuredToken!;

            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders(); // don't spam stdout; the app logs via Serilog
            // Fixed loopback port when configured (so a client config stays valid), else ephemeral. (#275)
            builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, _port > 0 ? _port : 0));
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                o.SerializerOptions.Converters.Add(new ScriptJsonConverter()); // reject Ipe/Unknown outputScript (before the enum factory)
                o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()); // "Latin" not 3, for other enums
            });

            // MCP surface (#191): expose the read tool set over the Streamable HTTP transport at /mcp — MCP is
            // just another transport on the same tool layer /v1 uses, not a proxy. A BYO-MCP chat client (Claude
            // Desktop via the mcp-remote bridge) connects here; code-capable agents keep hitting /v1 directly.
            // Tool groups are registered only when their backing service is present (mirroring the /v1 wiring);
            // 'books' needs no service, so /mcp is always available when the server runs.
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
            // Expose llms.txt as an MCP resource — an MCP client has no base URL to "fetch /llms.txt", so give
            // it the same version-stamped orientation as a readable resource. (Desktop MCP friction report)
            mcp.WithResources(new[] { BuildLlmsResource() });

            var app = builder.Build();

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

            // Unauthenticated root pointer, so an agent that connects via local-api.json isn't left staring at
            // an empty "/" — it names where the docs and status live. (Cold-agent test finding.)
            app.MapGet("/", () => Results.Json(
                new RootResponse("CST Reader local API", _appVersion, ApiVersion, "/llms.txt", "/" + ApiVersion + "/status")));

            app.MapGet("/" + ApiVersion + "/status",
                () => Results.Json(new StatusResponse(_appVersion, ApiVersion, "ok")));

            // Unauthenticated front door: the agent's orientation (endpoints, conventions, auth handshake).
            // Version-stamped so it can't be mistaken for a different build's surface.
            app.MapGet("/llms.txt", () => Results.Text(BuildLlmsText(), "text/markdown; charset=utf-8"));

            MapToolEndpoints(app);

            // Streamable HTTP MCP endpoint (read tool set + llms.txt resource). Behind the same security
            // middleware as everything else: requires the bearer token and rejects Origin-bearing requests.
            // Always mapped — the 'books' tool needs no backing service. (#191)
            app.MapMcp("/mcp");

            await app.StartAsync(ct);

            int port = ResolvePort(app);
            _app = app;
            Token = token;
            BaseUrl = $"http://127.0.0.1:{port}";

            new LocalApiInfo(port, token, Environment.ProcessId).Write(_handshakeDirectory);
            _logger.Information("Local API listening on {BaseUrl}", BaseUrl);
        }

        public async Task StopAsync()
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

        public ValueTask DisposeAsync() => new(StopAsync());

        private static bool IsLoopbackHost(string host) =>
            host is "127.0.0.1" or "localhost" or "[::1]" or "::1";

        private static Script ParseScript(string? name) =>
            Enum.TryParse<Script>(name, ignoreCase: true, out var script)
                && script is not (Script.Ipe or Script.Unknown)   // never expose the internal IPE font encoding
                ? script : Script.Latin;

        private static bool BookExists(string? bookId) =>
            !string.IsNullOrEmpty(bookId) &&
            Books.Inst.Any(b => string.Equals(b.FileName, bookId, StringComparison.OrdinalIgnoreCase));

        private static bool IsDiscoveryPath(PathString path) =>
            !path.HasValue || path == "/"
            || path.Equals("/llms.txt", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/docs", StringComparison.OrdinalIgnoreCase);

        // The version-stamped llms.txt body, served both at GET /llms.txt and as the MCP llms.txt resource
        // (single source, so the two can't drift). Version-stamped so it can't be mistaken for another build.
        private string BuildLlmsText()
        {
            var body = ReadResource("LocalApi.llms.txt")
                ?? "# CST Reader Local API\n\n(llms.txt resource missing)\n";
            return $"<!-- CST Reader {_appVersion} | API {ApiVersion} -->\n" + body;
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
                        req.OutputScript, req.IncludeFootnotes);
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

            // Surface which tool groups got wired, so a missing DI hand-off (e.g. a null IScriptTool leaving
            // /v1/scripts + /v1/convert unmapped -> 404) is visible in the log instead of only at call time.
            _logger.Information(
                "Local API tools wired: search={Search} dictionary={Dictionary} passage={Passage} script={Script}",
                _search != null, _dictionary != null, _passage != null, _script != null);
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
            bool IncludeFootnotes = false);
    }
}
