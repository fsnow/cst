using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using System.Text.Json;
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
        public void McpClientConfig_is_valid_json_carrying_the_port_and_token()
        {
            string json = McpClientConfig.ClaudeDesktop(8765, "TOKEN123");
            using var doc = JsonDocument.Parse(json);   // must be valid JSON
            var args = doc.RootElement.GetProperty("mcpServers").GetProperty("cst-reader").GetProperty("args");
            string joined = string.Join(" ", args.EnumerateArray().Select(e => e.GetString()));
            Assert.Contains("mcp-remote", joined);
            Assert.Contains("http://127.0.0.1:8765/mcp", joined);
            Assert.Contains("Authorization: Bearer TOKEN123", joined);
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
