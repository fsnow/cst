using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CST.Avalonia.Tests.TestSupport;
using Xunit;

namespace CST.Avalonia.Tests.Integration
{
    /// <summary>
    /// End-to-end integration tests over the REAL surface-C stack (real <c>LocalApiServer</c> + real
    /// <c>SearchService</c> + real tools + a tiny real Lucene index). These cover the DI/JSON/routing SEAMS the
    /// mocked endpoint tests bypass - the class of bug several cold-agent findings turned out to be. All tests
    /// share one server (built once) and run serially, because building the fixture index mutates global
    /// <c>Books</c> DocId state.
    /// </summary>
    public class LocalApiIntegrationTests : IAsyncLifetime
    {
        private LocalApiTestServer _api = null!;

        public async Task InitializeAsync() => _api = await LocalApiTestServer.StartAsync();
        public async Task DisposeAsync() => await _api.DisposeAsync();

        private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

        [Fact]
        public async Task All_core_endpoints_respond()
        {
            using var http = _api.Http();
            foreach (var path in new[] { "/v1/status", "/v1/books", "/v1/scripts" })
            {
                var resp = await http.GetAsync(path);
                Assert.True(resp.IsSuccessStatusCode, $"GET {path} -> {(int)resp.StatusCode}");
            }
            // convert + search over real JSON binding.
            Assert.Equal(HttpStatusCode.OK,
                (await http.PostAsync("/v1/convert", Json("{\"text\":\"dhamma\",\"outputScript\":\"Devanagari\"}"))).StatusCode);
            Assert.Equal(HttpStatusCode.OK,
                (await http.PostAsync("/v1/search", Json("{\"query\":\"dhamma\"}"))).StatusCode);
        }

        [Fact]
        public async Task Requires_the_bearer_token()
        {
            using var http = new HttpClient { BaseAddress = new System.Uri(_api.BaseUrl) };
            var resp = await http.GetAsync("/v1/scripts");
            Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
        }

        [Fact]
        public async Task Unknown_filter_key_is_400_through_real_json()
        {
            // The mock-bypassed seam bug: unknown JSON filter keys must fail loud, not bind to defaults.
            using var http = _api.Http();
            var resp = await http.PostAsync("/v1/search",
                Json("{\"query\":\"dhamma\",\"filter\":{\"commentaryLevel\":\"Mula\"}}"));
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task Convert_to_Ipe_is_rejected_400_through_real_json()
        {
            using var http = _api.Http();
            var resp = await http.PostAsync("/v1/convert", Json("{\"text\":\"dhamma\",\"outputScript\":\"Ipe\"}"));
            Assert.Equal(HttpStatusCode.BadRequest, resp.StatusCode);
        }

        [Fact]
        public async Task Search_returns_the_indexed_term_with_bookCount_and_books_opt_in()
        {
            using var http = _api.Http();
            var resp = await http.PostAsync("/v1/search", Json("{\"query\":\"dhamma\",\"mode\":\"Exact\"}"));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());

            var terms = doc.RootElement.GetProperty("terms");
            Assert.True(terms.GetArrayLength() >= 1, "the indexed term should be found");
            var t = terms[0];
            Assert.True(t.GetProperty("totalCount").GetInt32() >= 2);     // dhamma occurs in both fixture books
            Assert.Equal(2, t.GetProperty("bookCount").GetInt32());       // ...across 2 books
            Assert.Equal(0, t.GetProperty("books").GetArrayLength());     // per-book breakdown is opt-in

            // Opt in -> the breakdown appears.
            var withBooks = await http.PostAsync("/v1/search",
                Json("{\"query\":\"dhamma\",\"mode\":\"Exact\",\"includeBooks\":true}"));
            using var doc2 = JsonDocument.Parse(await withBooks.Content.ReadAsStringAsync());
            Assert.Equal(2, doc2.RootElement.GetProperty("terms")[0].GetProperty("books").GetArrayLength());
        }

        [Fact]
        public async Task Occurrences_single_word_wildcard_expands_not_empty()
        {
            // batch-9 regression: a single-word wildcard on /v1/occurrences must EXPAND (dhamm* -> dhamma),
            // not do a literal term lookup that silently returns []. Exercised over the real search engine.
            using var http = _api.Http();
            var resp = await http.PostAsync("/v1/occurrences",
                Json($"{{\"bookId\":\"{_api.MulaBook}\",\"term\":\"dhamm*\",\"mode\":\"Wildcard\"}}"));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            using var doc = JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
            Assert.True(doc.RootElement.GetArrayLength() >= 1, "single-word wildcard should return occurrences");
        }
    }
}
