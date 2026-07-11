using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Services.LocalApi;
using CST.Avalonia.Services.LocalApi.Mcp;
using ModelContextProtocol.Client;
using Xunit;

namespace CST.Avalonia.Tests.Services
{
    /// <summary>
    /// Stable local-API config (#275): a fixed port + persisted token so a BYO-MCP client keeps a static config
    /// across restarts. Covers the port-availability check, token generation, the Claude Desktop config helper,
    /// and that the server actually binds the configured port and reuses the given token.
    /// </summary>
    public class StableApiConfigTests
    {
        [Fact]
        public void PortAvailability_ephemeral_is_free_and_a_bound_port_is_not()
        {
            Assert.True(PortAvailability.IsAvailable(0));            // ephemeral is always "available"
            int p = PortAvailability.PickAvailable();
            Assert.InRange(p, 1, 65535);
            Assert.True(PortAvailability.IsAvailable(p));

            using var listener = new TcpListener(IPAddress.Loopback, p);
            listener.Start();
            Assert.False(PortAvailability.IsAvailable(p));           // now taken -> not available
        }

        [Fact]
        public void ApiToken_generates_distinct_urlsafe_tokens()
        {
            string a = ApiToken.Generate(), b = ApiToken.Generate();
            Assert.NotEqual(a, b);
            Assert.NotEmpty(a);
            Assert.DoesNotContain('+', a);   // URL-safe base64
            Assert.DoesNotContain('/', a);
            Assert.DoesNotContain('=', a);
        }

        [Fact]
        public void McpBridge_maps_the_handshake_to_the_mcp_endpoint()
        {
            // The --mcp-bridge entry reads local-api.json and points the relay at /mcp with the bearer,
            // explicit Streamable HTTP (not AutoDetect, which can fall back to disabled legacy SSE). (#278)
            var info = new LocalApiInfo(Port: 51515, Token: "TESTTOKEN", Pid: 999);
            var opts = McpBridge.BuildHttpOptions(info);
            Assert.Equal(new System.Uri("http://127.0.0.1:51515/mcp"), opts.Endpoint);
            Assert.Equal(HttpTransportMode.StreamableHttp, opts.TransportMode);
            Assert.Equal("Bearer TESTTOKEN", opts.AdditionalHeaders!["Authorization"]);
        }

        [Theory]
        // Inside the signed bundle the bridge exe is …/CST Reader.app/Contents/MacOS/CST.Avalonia -> the .app dir.
        [InlineData("/Applications/CST Reader.app/Contents/MacOS/CST.Avalonia", "/Applications/CST Reader.app")]
        [InlineData("/Users/x/Desktop/CST Reader.app/Contents/MacOS/CST.Avalonia", "/Users/x/Desktop/CST Reader.app")]
        public void AppBundleFromExecutablePath_finds_the_enclosing_app_bundle(string exe, string expected)
            => Assert.Equal(expected, McpBridge.AppBundleFromExecutablePath(exe));

        [Theory]
        // Dev / non-bundle layouts (e.g. `dotnet run`) have no .app ancestor -> launch-or-attach can't auto-launch.
        [InlineData("/Users/x/repo/src/CST.Avalonia/bin/Debug/net10.0/CST.Avalonia")]
        [InlineData("")]
        [InlineData(null)]
        public void AppBundleFromExecutablePath_is_null_outside_a_bundle(string? exe)
            => Assert.Null(McpBridge.AppBundleFromExecutablePath(exe));

        [Fact]
        public async Task Liveness_watcher_trips_when_the_watched_process_is_gone()
        {
            // The GUI's pid isn't running -> the watcher cancels its token so the relay unwinds and the bridge
            // exits (one -> zero, never a lone bridge). A pid far above the OS max is guaranteed not running. (#278)
            const int deadPid = 2_000_000_000;
            using var onGone = new CancellationTokenSource();
            var watch = McpBridge.WatchProcessLivenessAsync(deadPid, onGone, pollMs: 20, onGone.Token);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!onGone.IsCancellationRequested && sw.Elapsed < System.TimeSpan.FromSeconds(2))
                await Task.Delay(20);

            Assert.True(onGone.IsCancellationRequested);
            await watch;
        }

        [Fact]
        public async Task Liveness_watcher_stays_quiet_while_the_process_is_alive()
        {
            // Our own process is alive -> the watcher must NOT trip. (#278)
            using var onGone = new CancellationTokenSource();
            var watch = McpBridge.WatchProcessLivenessAsync(
                System.Environment.ProcessId, onGone, pollMs: 20, onGone.Token);

            await Task.Delay(300);
            Assert.False(onGone.IsCancellationRequested);

            onGone.Cancel();   // stop the watcher
            await watch;
        }

        [Fact]
        public void McpClientConfig_emits_the_bridge_command_with_no_secrets()
        {
            // #278 Phase 4: the Copy-config helper emits the --mcp-bridge config — spawn the app binary, no port,
            // no token (the relay reads the current local-api.json each spawn).
            const string command = "/Applications/CST Reader.app/Contents/MacOS/CST";
            string json = McpClientConfig.ClaudeDesktop(command);
            using var doc = JsonDocument.Parse(json);   // must be valid JSON
            var server = doc.RootElement.GetProperty("mcpServers").GetProperty("cst-reader");
            Assert.Equal(command, server.GetProperty("command").GetString());
            var args = server.GetProperty("args").EnumerateArray().Select(e => e.GetString()).ToArray();
            Assert.Equal(new[] { "--mcp-bridge" }, args);
            Assert.DoesNotContain("Bearer", json);      // no bearer token
            Assert.DoesNotContain("mcp-remote", json);  // not the old npx/mcp-remote snippet
        }

        [Fact]
        public async Task Mcp_endpoint_is_mounted_only_when_mcpEnabled()
        {
            // #278 Phase 4: /mcp is gated by its own permission. With it off the route is absent (404 past the auth
            // gate); with it on the route exists (any non-404 — the MCP handler answers/negotiates).
            var dir = Path.Combine(Path.GetTempPath(), "cst-mcpgate-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            const string token = "gate-token-under-test";

            async Task<HttpStatusCode> PostMcpAsync(bool mcpEnabled)
            {
                var server = new LocalApiServer("test", dir, Serilog.Log.Logger, port: 0, token: token, mcpEnabled: mcpEnabled);
                await server.StartAsync();
                try
                {
                    using var http = new HttpClient { BaseAddress = new Uri(server.BaseUrl!) };
                    http.DefaultRequestHeaders.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", token);
                    using var body = new StringContent(
                        "{\"jsonrpc\":\"2.0\",\"id\":1,\"method\":\"ping\"}", Encoding.UTF8, "application/json");
                    using var resp = await http.PostAsync("/mcp", body);
                    return resp.StatusCode;
                }
                finally { await server.StopAsync(); }
            }

            Assert.Equal(HttpStatusCode.NotFound, await PostMcpAsync(mcpEnabled: false));     // route not mapped
            Assert.NotEqual(HttpStatusCode.NotFound, await PostMcpAsync(mcpEnabled: true));   // route mapped (authorized)

            try { Directory.Delete(dir, recursive: true); } catch { }
        }

        [Fact]
        public async Task Server_binds_the_configured_port_and_reuses_the_token()
        {
            var dir = Path.Combine(Path.GetTempPath(), "cst-stablecfg-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            int port = PortAvailability.PickAvailable();
            const string token = "stable-token-under-test";
            var server = new LocalApiServer("test", dir, Serilog.Log.Logger, port: port, token: token);
            try
            {
                await server.StartAsync();
                Assert.Equal($"http://127.0.0.1:{port}", server.BaseUrl);   // fixed port, not ephemeral
                Assert.Equal(token, server.Token);                          // reused, not regenerated
            }
            finally
            {
                await server.StopAsync();
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }
    }
}
