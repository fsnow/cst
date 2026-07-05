using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Services.LocalApi;
using CST.Tools;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Services
{
    /// <summary>
    /// Integration tests for the tool endpoints: a real loopback server with a mocked search tool (asserts
    /// routing + auth + JSON round-trip, not search behavior) and the real book catalog for /v1/books.
    /// </summary>
    public class LocalApiEndpointTests : IAsyncLifetime
    {
        private string _dir = null!;
        private LocalApiServer _server = null!;

        public async Task InitializeAsync()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cst-ep-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);

            var search = new Mock<ISearchTool>();
            search.Setup(t => t.SearchAsync(It.IsAny<SearchToolRequest>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SearchToolResult(
                    new List<SearchTermResult> { new("dhamma", 5, new List<BookHitSummary>()) },
                    TotalTermCount: 1, TotalOccurrenceCount: 5, TotalBookCount: 1, Truncated: false, Note: null));

            _server = new LocalApiServer("5.0.0-test", _dir, Serilog.Log.Logger, search.Object);
            await _server.StartAsync();
        }

        public async Task DisposeAsync()
        {
            await _server.StopAsync();
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        private HttpClient Authed()
        {
            var http = new HttpClient { BaseAddress = new Uri(_server.BaseUrl!) };
            http.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", _server.Token);
            return http;
        }

        private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

        [Fact]
        public async Task Search_endpoint_returns_the_mapped_result()
        {
            using var http = Authed();
            var resp = await http.PostAsync("/v1/search", Json("{\"query\":\"x\"}"));

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            Assert.Contains("dhamma", await resp.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task Tool_endpoint_still_requires_the_token()
        {
            using var http = new HttpClient { BaseAddress = new Uri(_server.BaseUrl!) };
            var resp = await http.PostAsync("/v1/search", Json("{\"query\":\"x\"}"));

            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        [Fact]
        public async Task Books_endpoint_lists_the_catalog_romanized()
        {
            using var http = Authed();
            var resp = await http.GetAsync("/v1/books");

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains(".xml", body);                                    // real book file names
            Assert.DoesNotContain(body, c => c >= '\u0900' && c <= '\u097F'); // names romanized, no Devanagari leaked
        }

        [Fact]
        public async Task Unprovided_tool_endpoint_is_not_mapped()
        {
            // No dictionary tool was supplied, so its endpoint doesn't exist.
            using var http = Authed();
            var resp = await http.GetAsync("/v1/dictionary/languages");

            Assert.Equal(HttpStatusCode.NotFound, resp.StatusCode);
        }
    }
}
