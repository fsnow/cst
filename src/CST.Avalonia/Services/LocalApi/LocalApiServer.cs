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

        private readonly string _appVersion;
        private readonly string _handshakeDirectory;
        private readonly Serilog.ILogger _logger;
        private readonly ISearchTool? _search;
        private readonly IDictionaryTool? _dictionary;
        private readonly IPassageTool? _passage;

        private WebApplication? _app;

        /// <summary>The base URL once started (e.g. <c>http://127.0.0.1:52344</c>), or null if not running.</summary>
        public string? BaseUrl { get; private set; }

        /// <summary>The per-session bearer token once started, or null if not running.</summary>
        public string? Token { get; private set; }

        public bool IsRunning => _app != null;

        public LocalApiServer(
            string appVersion, string handshakeDirectory, Serilog.ILogger logger,
            ISearchTool? search = null, IDictionaryTool? dictionary = null, IPassageTool? passage = null)
        {
            _appVersion = appVersion;
            _handshakeDirectory = handshakeDirectory;
            _logger = logger.ForContext<LocalApiServer>();
            _search = search;
            _dictionary = dictionary;
            _passage = passage;
        }

        public async Task StartAsync(CancellationToken ct = default)
        {
            if (_app != null) return;

            string token = GenerateToken();

            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders(); // don't spam stdout; the app logs via Serilog
            builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0)); // 127.0.0.1:ephemeral
            builder.Services.ConfigureHttpJsonOptions(o =>
            {
                o.SerializerOptions.PropertyNamingPolicy = JsonNamingPolicy.CamelCase;
                o.SerializerOptions.Converters.Add(new JsonStringEnumConverter()); // "Latin" not 3
            });

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
            app.MapGet("/llms.txt", () =>
            {
                var body = ReadResource("LocalApi.llms.txt")
                    ?? "# CST Reader Local API\n\n(llms.txt resource missing)\n";
                var stamped = $"<!-- CST Reader {_appVersion} | API {ApiVersion} -->\n" + body;
                return Results.Text(stamped, "text/markdown; charset=utf-8");
            });

            MapToolEndpoints(app);

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

        private static bool IsDiscoveryPath(PathString path) =>
            !path.HasValue || path == "/"
            || path.Equals("/llms.txt", StringComparison.OrdinalIgnoreCase)
            || path.StartsWithSegments("/docs", StringComparison.OrdinalIgnoreCase);

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

        private static string GenerateToken()
        {
            Span<byte> bytes = stackalloc byte[32];
            RandomNumberGenerator.Fill(bytes);
            return Convert.ToBase64String(bytes).Replace('+', '-').Replace('/', '_').TrimEnd('=');
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
                app.MapPost(v + "/occurrences",
                    async (OccurrenceRequest req, CancellationToken ct) => Results.Json(await search.GetOccurrencesAsync(req, ct)));
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
                    NavigationReference reference = req.Paragraph is int n
                        ? new NavigationReference.Paragraph(n, req.BookCode)
                        : new NavigationReference.WholeBook();
                    var pr = new PassageRequest(req.BookId, reference, req.Cursor, req.MaxChars,
                        req.OutputScript, req.IncludeVariantReadings);
                    return Results.Json(await passage.FetchPassageAsync(pr, ct));
                });
            }

            // Book catalog — agents need book ids to call the other tools. Always available (no service needed).
            app.MapGet(v + "/books", () => Results.Json(
                Books.Inst.Select(b => new BookSummary(
                    b.FileName, b.LongNavPath, b.ShortNavPath, b.Pitaka, b.Matn, b.BookType, b.DocId >= 0)).ToList()));
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
            bool IncludeVariantReadings = false);
    }
}
