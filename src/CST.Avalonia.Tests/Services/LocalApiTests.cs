using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Threading.Tasks;
using CST.Avalonia.Services.LocalApi;
using Xunit;

namespace CST.Avalonia.Tests.Services
{
    public class LocalApiInfoTests
    {
        [Fact]
        public void Write_read_delete_roundtrip()
        {
            var dir = Path.Combine(Path.GetTempPath(), "cst-info-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                new LocalApiInfo(52344, "tok-abc", 4242).Write(dir);

                var read = LocalApiInfo.Read(dir);
                Assert.NotNull(read);
                Assert.Equal(52344, read!.Port);
                Assert.Equal("tok-abc", read.Token);
                Assert.Equal(4242, read.Pid);

                Assert.Contains("\"docs\"", File.ReadAllText(LocalApiInfo.PathIn(dir)));   // points to the orientation doc
                Assert.Equal("/llms.txt", read.Docs);

                if (!OperatingSystem.IsWindows())
                    Assert.Equal(UnixFileMode.UserRead | UnixFileMode.UserWrite,
                        File.GetUnixFileMode(LocalApiInfo.PathIn(dir)));

                LocalApiInfo.Delete(dir);
                Assert.Null(LocalApiInfo.Read(dir));
            }
            finally { Directory.Delete(dir, recursive: true); }
        }
    }

    /// <summary>
    /// Integration tests: start the real loopback server on an ephemeral port and exercise the security gate.
    /// A fresh server (own port + token) per test via <see cref="IAsyncLifetime"/>.
    /// </summary>
    public class LocalApiServerTests : IAsyncLifetime
    {
        private string _dir = null!;
        private LocalApiServer _server = null!;

        public async Task InitializeAsync()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cst-api-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
            _server = new LocalApiServer("5.0.0-test", _dir, Serilog.Log.Logger);
            await _server.StartAsync();
        }

        public async Task DisposeAsync()
        {
            await _server.StopAsync();
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        private HttpClient Client(bool withToken = true, string? origin = null)
        {
            var http = new HttpClient { BaseAddress = new Uri(_server.BaseUrl!) };
            if (withToken) http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _server.Token);
            if (origin != null) http.DefaultRequestHeaders.Add("Origin", origin);
            return http;
        }

        [Fact]
        public async Task Status_is_public_discovery_without_a_token()
        {
            // #306 A1-4: the unauthenticated root advertises /v1/status, so it must itself be reachable without a
            // token (it carries no secrets) — otherwise a cold agent following the pointer gets a 401.
            using var http = Client(withToken: false);
            var resp = await http.GetAsync("/v1/status");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        [Fact]
        public async Task Status_ok_with_the_token()
        {
            using var http = Client();
            var resp = await http.GetAsync("/v1/status");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Contains("5.0.0-test", await resp.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task An_authenticated_endpoint_requires_the_token()
        {
            // /v1/books is a real (non-discovery) endpoint, so it stands in for "everything past the auth gate".
            using var http = Client(withToken: false);
            var resp = await http.GetAsync("/v1/books");
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        [Fact]
        public async Task Wrong_token_is_rejected()
        {
            var http = new HttpClient { BaseAddress = new Uri(_server.BaseUrl!) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", "not-the-token");
            var resp = await http.GetAsync("/v1/books");
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        [Fact]
        public async Task Origin_header_is_rejected_even_with_token()
        {
            using var http = Client(origin: "http://evil.example");
            var resp = await http.GetAsync("/v1/status");
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        [Fact]
        public async Task Root_points_to_the_docs_without_a_token()
        {
            using var http = Client(withToken: false);
            var resp = await http.GetAsync("/");

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("/llms.txt", body);       // names where the orientation doc lives
            Assert.Contains("/v1/status", body);
        }

        [Fact]
        public async Task Llms_txt_is_served_without_a_token_and_is_version_stamped()
        {
            using var http = Client(withToken: false);
            var resp = await http.GetAsync("/llms.txt");

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("/v1/search", body);       // endpoint orientation
            Assert.Contains("Bearer", body);           // auth handshake
            Assert.Contains("5.0.0-test", body);       // version stamp
        }

        [Fact]
        public async Task Llms_txt_still_rejects_an_origin_header()
        {
            using var http = Client(withToken: false, origin: "http://evil.example");
            var resp = await http.GetAsync("/llms.txt");
            Assert.Equal(HttpStatusCode.Forbidden, resp.StatusCode);
        }

        [Fact]
        public async Task Concurrent_StartAsync_binds_a_single_server()
        {
            // #306 A1-8: two racing starts must not bind two Kestrels — the lifecycle lock + _app check make
            // the second call a no-op. A fresh server (not the fixture's already-started one).
            var dir = Path.Combine(Path.GetTempPath(), "cst-api-concstart-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var server = new LocalApiServer("5.0.0-test", dir, Serilog.Log.Logger);
            try
            {
                await Task.WhenAll(server.StartAsync(), server.StartAsync());
                Assert.True(server.IsRunning);
                Assert.NotNull(server.BaseUrl);
            }
            finally
            {
                await server.StopAsync();
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        [Fact]
        public void Handshake_file_advertises_port_and_token()
        {
            var info = LocalApiInfo.Read(_dir);
            Assert.NotNull(info);
            Assert.Equal(_server.Token, info!.Token);
            Assert.True(info.Port > 0);
            Assert.Equal(Environment.ProcessId, info.Pid);
        }

        [Fact]
        public async Task Stop_removes_the_handshake_file()
        {
            Assert.NotNull(LocalApiInfo.Read(_dir));
            await _server.StopAsync();
            Assert.Null(LocalApiInfo.Read(_dir));
            // Re-start so DisposeAsync's StopAsync is a no-op-safe call.
            await _server.StartAsync();
        }
    }
}
