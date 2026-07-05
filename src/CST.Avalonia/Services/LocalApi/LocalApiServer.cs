using System;
using System.Linq;
using System.Net;
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

        private WebApplication? _app;

        /// <summary>The base URL once started (e.g. <c>http://127.0.0.1:52344</c>), or null if not running.</summary>
        public string? BaseUrl { get; private set; }

        /// <summary>The per-session bearer token once started, or null if not running.</summary>
        public string? Token { get; private set; }

        public bool IsRunning => _app != null;

        public LocalApiServer(string appVersion, string handshakeDirectory, Serilog.ILogger logger)
        {
            _appVersion = appVersion;
            _handshakeDirectory = handshakeDirectory;
            _logger = logger.ForContext<LocalApiServer>();
        }

        public async Task StartAsync(CancellationToken ct = default)
        {
            if (_app != null) return;

            string token = GenerateToken();

            var builder = WebApplication.CreateSlimBuilder();
            builder.Logging.ClearProviders(); // don't spam stdout; the app logs via Serilog
            builder.WebHost.ConfigureKestrel(k => k.Listen(IPAddress.Loopback, 0)); // 127.0.0.1:ephemeral

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
                if (!IsAuthorized(context, token))
                {
                    context.Response.StatusCode = StatusCodes.Status401Unauthorized;
                    return;
                }
                await next();
            });

            app.MapGet("/" + ApiVersion + "/status",
                () => Results.Json(new StatusResponse(_appVersion, ApiVersion, "ok")));

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

        private sealed record StatusResponse(string App, string Api, string Status);
    }
}
