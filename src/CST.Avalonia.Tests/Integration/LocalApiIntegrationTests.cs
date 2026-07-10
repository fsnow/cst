using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using CST.Avalonia.Tests.TestSupport;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
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

        private static async Task<JsonDocument> PostDoc(HttpClient http, string path, string body)
        {
            var resp = await http.PostAsync(path, Json(body));
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            return JsonDocument.Parse(await resp.Content.ReadAsStringAsync());
        }

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
        public async Task Search_countsOnly_matches_the_postings_path_and_books_are_opt_in()
        {
            using var http = _api.Http();

            // Default request => the COUNTS-ONLY fast path (index stats, no postings, no per-book breakdown).
            using var lean = await PostDoc(http, "/v1/search", "{\"query\":\"dhamma\",\"mode\":\"Exact\"}");
            var leanTerm = lean.RootElement.GetProperty("terms")[0];
            int leanTotal = leanTerm.GetProperty("totalCount").GetInt32();
            int leanBooks = leanTerm.GetProperty("bookCount").GetInt32();
            Assert.Equal(2, leanBooks);                                       // dhamma occurs in both fixture books
            Assert.True(leanTotal >= 2);
            Assert.Equal(0, leanTerm.GetProperty("books").GetArrayLength());  // per-book breakdown omitted
            // Page-level totalBookCount is NULL in the counts-only path (no book union available) - not a
            // misleading 0. (Desktop MCP friction report)
            Assert.Equal(JsonValueKind.Null, lean.RootElement.GetProperty("totalBookCount").ValueKind);

            // includeBooks => the POSTINGS path, with the per-book breakdown. Counts must AGREE across paths.
            using var full = await PostDoc(http, "/v1/search",
                "{\"query\":\"dhamma\",\"mode\":\"Exact\",\"includeBooks\":true}");
            var fullTerm = full.RootElement.GetProperty("terms")[0];
            Assert.Equal(2, fullTerm.GetProperty("books").GetArrayLength());
            Assert.Equal(leanTotal, fullTerm.GetProperty("totalCount").GetInt32());
            Assert.Equal(leanBooks, fullTerm.GetProperty("bookCount").GetInt32());
            // With includeBooks, totalBookCount is the real distinct-book union over the page.
            Assert.Equal(2, full.RootElement.GetProperty("totalBookCount").GetInt32());
        }

        [Fact]
        public async Task Search_pages_over_terms_without_a_cache_collision()
        {
            using var http = _api.Http();
            // maxTerms:1 => one term per page. Page 2 must be a DIFFERENT term - the regression is that the
            // cache key now includes skip/pageSize, so page 2 no longer returns the cached page 1.
            using var p1 = await PostDoc(http, "/v1/search",
                "{\"query\":\"*\",\"mode\":\"Wildcard\",\"maxTerms\":1,\"skip\":0}");
            using var p2 = await PostDoc(http, "/v1/search",
                "{\"query\":\"*\",\"mode\":\"Wildcard\",\"maxTerms\":1,\"skip\":1}");

            Assert.True(p1.RootElement.GetProperty("hasMore").GetBoolean());
            var term1 = p1.RootElement.GetProperty("terms")[0].GetProperty("term").GetString();
            var term2 = p2.RootElement.GetProperty("terms")[0].GetProperty("term").GetString();
            Assert.NotEqual(term1, term2);
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

        [Fact]
        public async Task Mcp_endpoint_is_gated_and_serves_the_search_tool()
        {
            // Gate: /mcp sits behind the same bearer middleware as /v1 - no token => 401 (it is mounted and
            // guarded, not a 404). This is what keeps a browser (which also can't set the header) out.
            using (var noAuth = new HttpClient { BaseAddress = new System.Uri(_api.BaseUrl) })
            {
                var resp = await noAuth.PostAsync("/mcp", Json("{}"));
                Assert.Equal(HttpStatusCode.Unauthorized, resp.StatusCode);
            }

            // Happy path: drive the REAL MCP client over the Streamable HTTP transport with the bearer, exactly
            // as Claude Desktop's mcp-remote bridge does. Proves the initialize -> list -> call handshake
            // survives our Origin-reject + bearer gate, and that the one wired tool actually runs.
            await using var client = await ConnectMcpAsync();

            var tools = await client.ListToolsAsync();
            Assert.Contains(tools, t => t.Name == "search");

            var result = await client.CallToolAsync(
                "search", new Dictionary<string, object?> { ["query"] = "dhamma" });

            Assert.NotEqual(true, result.IsError);
            // The matched term round-trips as romanized "dhamma" - assert it in whichever channel the SDK used
            // (text content block and/or structured content).
            var text = string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text));
            var structured = result.StructuredContent?.GetRawText() ?? string.Empty;
            Assert.Contains("dhamma", text + structured);
        }

        // Connects the real MCP client over Streamable HTTP with the bearer, as Claude Desktop's mcp-remote
        // bridge does.
        private async Task<McpClient> ConnectMcpAsync()
        {
            var transport = new HttpClientTransport(new HttpClientTransportOptions
            {
                Endpoint = new System.Uri(_api.BaseUrl + "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + _api.Token },
            });
            return await McpClient.CreateAsync(transport);
        }

        private static string ToolText(ModelContextProtocol.Protocol.CallToolResult r) =>
            string.Concat(r.Content.OfType<TextContentBlock>().Select(b => b.Text))
            + (r.StructuredContent?.GetRawText() ?? string.Empty);

        [Fact]
        public async Task Mcp_lists_the_read_tool_set()
        {
            await using var client = await ConnectMcpAsync();
            var names = (await client.ListToolsAsync()).Select(t => t.Name).ToHashSet();
            // The read tools wired by the test server (search+occurrences via ISearchTool, passage, script,
            // books). Dictionary is not wired in the test server, so it is (correctly) absent.
            foreach (var expected in new[] { "search", "occurrences", "passage", "books", "convert", "scripts" })
                Assert.Contains(expected, names);
        }

        [Fact]
        public async Task Mcp_occurrences_and_passage_read_the_corpus()
        {
            await using var client = await ConnectMcpAsync();

            // occurrences: hit-in-context snippet for a term in a specific book. Pass the mode/outputScript
            // ENUMS as strings to exercise the closed-enum schema binding (Desktop MCP friction: bare strings).
            var occ = await client.CallToolAsync("occurrences",
                new Dictionary<string, object?>
                {
                    ["bookId"] = _api.MulaBook, ["term"] = "dhamma",
                    ["mode"] = "Exact", ["outputScript"] = "Latin",
                });
            Assert.NotEqual(true, occ.IsError);
            Assert.Contains("dhamma", ToolText(occ), System.StringComparison.OrdinalIgnoreCase);

            // passage: read the book's text (romanized), turning a bookId + count into readable source.
            var pass = await client.CallToolAsync("passage",
                new Dictionary<string, object?> { ["bookId"] = _api.MulaBook });
            Assert.NotEqual(true, pass.IsError);
            Assert.Contains("dhamma", ToolText(pass), System.StringComparison.OrdinalIgnoreCase);
        }

        [Fact]
        public async Task Mcp_books_tool_resolves_ids_to_names()
        {
            await using var client = await ConnectMcpAsync();
            var books = await client.CallToolAsync("books", new Dictionary<string, object?>());
            Assert.NotEqual(true, books.IsError);
            // The fixture's mula book (a real catalog file name) is listed - so an agent can resolve a bookId.
            Assert.Contains(_api.MulaBook, ToolText(books));
        }

        [Fact]
        public async Task Mcp_exposes_the_llms_txt_resource()
        {
            await using var client = await ConnectMcpAsync();
            // An MCP client has no base URL to "fetch /llms.txt" - it must be discoverable + readable as a resource.
            var resources = await client.ListResourcesAsync();
            Assert.Contains(resources, r => r.Uri == "cst:///llms.txt");

            var read = await client.ReadResourceAsync("cst:///llms.txt");
            var body = string.Concat(read.Contents.OfType<TextResourceContents>().Select(c => c.Text));
            Assert.Contains("CST Reader", body);   // the version stamp / orientation doc
        }
    }
}
