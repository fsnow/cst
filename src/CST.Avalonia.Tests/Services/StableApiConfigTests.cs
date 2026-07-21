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
using CST.Conversion;
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
        {
            // The macOS `.app` bundle concept + these Unix fixture paths are macOS-only; on Windows the Path APIs
            // return backslashes and there is no bundle. Don't count as a failure off macOS. (#397)
            if (!OperatingSystem.IsMacOS()) return;
            Assert.Equal(expected, McpBridge.AppBundleFromExecutablePath(exe));
        }

        [Theory]
        // Dev / non-bundle layouts (e.g. `dotnet run`) have no .app ancestor -> launch-or-attach can't auto-launch.
        [InlineData("/Users/x/repo/src/CST.Avalonia/bin/Debug/net10.0/CST.Avalonia")]
        [InlineData("")]
        [InlineData(null)]
        public void AppBundleFromExecutablePath_is_null_outside_a_bundle(string? exe)
        {
            if (!OperatingSystem.IsMacOS()) return;   // macOS-only bundle parsing (#397)
            Assert.Null(McpBridge.AppBundleFromExecutablePath(exe));
        }

        [Fact]
        public void Write_creates_the_handshake_owner_only_and_leaves_no_temp()
        {
            // #303: the token file must be created 0600 (no world-readable window) and written atomically.
            var dir = Path.Combine(Path.GetTempPath(), "cst-hs-perms-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                new LocalApiInfo(51515, "SECRET", 123).Write(dir);
                var path = LocalApiInfo.PathIn(dir);

                Assert.Equal("SECRET", LocalApiInfo.Read(dir)!.Token);          // content round-trips
                Assert.Empty(Directory.GetFiles(dir, "*.tmp"));                 // atomic rename left no temp
                if (!OperatingSystem.IsWindows())
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite, File.GetUnixFileMode(path));
            }
            finally { try { Directory.Delete(dir, recursive: true); } catch { } }
        }

        [Fact]
        public void ReadLiveHandshake_ignores_a_stale_dead_pid_handshake()
        {
            // #302: a crash/kill -9 leaves local-api.json behind; attach must treat a dead-pid handshake as stale
            // (null) so it falls through to launch, not attach + immediately get killed by the pid-watcher.
            var dir = Path.Combine(Path.GetTempPath(), "cst-livehs-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                new LocalApiInfo(51515, "TOK", 2_000_000_000).Write(dir);   // pid far above OS max = not running
                Assert.Null(McpBridge.ReadLiveHandshake(dir));

                // A live pid with no recorded start time (0) → pid-only fallback, treated as live. This keeps a
                // handshake written before #351 (or by a build that couldn't read its own start time) usable.
                new LocalApiInfo(51515, "TOK", Environment.ProcessId).Write(dir);
                var info = McpBridge.ReadLiveHandshake(dir);
                Assert.NotNull(info);
                Assert.Equal(Environment.ProcessId, info!.Pid);

                // A live pid whose recorded start time is ours → verified live.
                new LocalApiInfo(51515, "TOK", Environment.ProcessId,
                    CST.Avalonia.Services.LocalApi.ProcessIdentity.CurrentStartToken()).Write(dir);
                Assert.NotNull(McpBridge.ReadLiveHandshake(dir));

                // #351: a live pid whose recorded start time is NOT ours = a recycled pid from a crashed
                // instance. Must be treated as stale (null) so attach falls through to launch, rather than
                // attaching to a dead loopback port.
                long impostorStart = CST.Avalonia.Services.LocalApi.ProcessIdentity.CurrentStartToken()
                                     - System.TimeSpan.FromHours(1).Ticks;
                new LocalApiInfo(51515, "TOK", Environment.ProcessId, impostorStart).Write(dir);
                Assert.Null(McpBridge.ReadLiveHandshake(dir));
            }
            finally { try { Directory.Delete(dir, recursive: true); } catch { } }
        }

        [Fact]
        public void LocalApiInfo_round_trips_the_start_time()
        {
            var dir = Path.Combine(Path.GetTempPath(), "cst-hsrt-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                new LocalApiInfo(52344, "tok", 4242, StartToken: 638_000_000_000_000_000L).Write(dir);
                var back = LocalApiInfo.Read(dir);
                Assert.NotNull(back);
                Assert.Equal(638_000_000_000_000_000L, back!.StartToken);

                // Absent from the JSON (a pre-#351 file) deserializes to 0, the pid-only sentinel.
                File.WriteAllText(LocalApiInfo.PathIn(dir), "{\"port\":1,\"token\":\"t\",\"pid\":2}");
                Assert.Equal(0, LocalApiInfo.Read(dir)!.StartToken);
            }
            finally { try { Directory.Delete(dir, recursive: true); } catch { } }
        }

        [Fact]
        public async Task Liveness_watcher_with_no_recorded_token_falls_back_to_pid_only_and_stays_quiet()
        {
            // A live pid + startToken 0 (a pre-#351 handshake) must degrade to the old pid-only check, not trip.
            using var onGone = new CancellationTokenSource();
            var watch = McpBridge.WatchProcessLivenessAsync(
                System.Environment.ProcessId, startToken: 0, onGone, pollMs: 20, onGone.Token);

            await Task.Delay(300);
            Assert.False(onGone.IsCancellationRequested);

            onGone.Cancel();
            await watch;
        }

        [Theory]
        // field 22 (starttime jiffies) is the value; comm may contain spaces and parentheses, which is the trap.
        // Tail after the last ')' starts at field 3 (state); starttime is field 22 = tail[19]. So 19 filler
        // tokens (fields 3–21) then the value, then any trailing fields.
        [InlineData("1234 (cst) S 1 1 1 0 -1 0 0 0 0 0 0 0 0 0 20 0 1 0 987654 0 0", 987654L)]
        [InlineData("42 (weird )name( x) R 1 1 1 0 -1 0 0 0 0 0 0 0 0 0 20 0 1 0 555 0 0", 555L)]
        public void Linux_proc_stat_start_jiffies_is_parsed_past_a_tricky_comm(string stat, long expected)
        {
            Assert.True(CST.Avalonia.Services.LocalApi.ProcessIdentity.TryParseLinuxStartJiffies(stat, out var j));
            Assert.Equal(expected, j);
        }

        [Theory]
        [InlineData("")]
        [InlineData("no closing paren here")]
        [InlineData("1 (short) S 1 2 3")]   // too few fields to reach field 22
        public void Linux_proc_stat_parse_rejects_malformed_input(string stat)
        {
            Assert.False(CST.Avalonia.Services.LocalApi.ProcessIdentity.TryParseLinuxStartJiffies(stat, out _));
        }

        [Fact]
        public async Task Liveness_watcher_trips_when_the_watched_process_is_gone()
        {
            // The GUI's pid isn't running -> the watcher cancels its token so the relay unwinds and the bridge
            // exits (one -> zero, never a lone bridge). A pid far above the OS max is guaranteed not running. (#278)
            const int deadPid = 2_000_000_000;
            using var onGone = new CancellationTokenSource();
            var watch = McpBridge.WatchProcessLivenessAsync(deadPid, startToken: 0, onGone, pollMs: 20, onGone.Token);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!onGone.IsCancellationRequested && sw.Elapsed < System.TimeSpan.FromSeconds(2))
                await Task.Delay(20);

            Assert.True(onGone.IsCancellationRequested);
            await watch;
        }

        [Fact]
        public async Task Liveness_watcher_stays_quiet_while_the_process_is_alive()
        {
            // Our own process is alive AND its recorded start time matches -> the watcher must NOT trip. (#278)
            using var onGone = new CancellationTokenSource();
            var watch = McpBridge.WatchProcessLivenessAsync(
                System.Environment.ProcessId,
                CST.Avalonia.Services.LocalApi.ProcessIdentity.CurrentStartToken(),
                onGone, pollMs: 20, onGone.Token);

            await Task.Delay(300);
            Assert.False(onGone.IsCancellationRequested);

            onGone.Cancel();   // stop the watcher
            await watch;
        }

        [Fact]
        public async Task Liveness_watcher_trips_when_a_live_pid_is_a_recycled_impostor()
        {
            // #351: the crash-recovery hazard. The recorded pid is LIVE (our own process), but the recorded
            // start time is NOT ours — as if the OS recycled a crashed instance's pid onto an unrelated process.
            // Identity, not bare pid liveness, must detect the mismatch and tear the bridge down.
            using var onGone = new CancellationTokenSource();
            long notOurStart = CST.Avalonia.Services.LocalApi.ProcessIdentity.CurrentStartToken() - System.TimeSpan.FromHours(1).Ticks;
            var watch = McpBridge.WatchProcessLivenessAsync(
                System.Environment.ProcessId, notOurStart, onGone, pollMs: 20, onGone.Token);

            var sw = System.Diagnostics.Stopwatch.StartNew();
            while (!onGone.IsCancellationRequested && sw.Elapsed < System.TimeSpan.FromSeconds(2))
                await Task.Delay(20);

            Assert.True(onGone.IsCancellationRequested);
            await watch;
        }

        [Fact]
        public void McpScript_ToScript_rejects_undefined_outputScript_ordinals()
        {
            // #304: the MCP SDK accepts integer enum values; OutputScript is 0-13 but Script has Ipe=15 — an
            // out-of-range value must be rejected, not mapped into Script.Ipe (IPE leak) or empty output.
            Assert.Equal(Script.Sinhala, McpScript.ToScript(OutputScript.Sinhala));   // defined → mapped by name
            Assert.Throws<System.ArgumentException>(() => McpScript.ToScript((OutputScript)15));  // Script.Ipe ordinal
            Assert.Throws<System.ArgumentException>(() => McpScript.ToScript((OutputScript)99));
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
