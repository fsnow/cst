using System.Collections.Generic;
using System.IO.Pipelines;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Services.LocalApi.Mcp;
using CST.Avalonia.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
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

        private static async Task<JsonDocument> GetDoc(HttpClient http, string path)
        {
            var resp = await http.GetAsync(path);
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
            Assert.Equal(JsonValueKind.Null, leanTerm.GetProperty("books").ValueKind);  // null (not []) when omitted
            // Page-level returnedBookCount is NULL in the counts-only path (no book union available) - not a
            // misleading 0. (Desktop MCP friction report)
            Assert.Equal(JsonValueKind.Null, lean.RootElement.GetProperty("returnedBookCount").ValueKind);

            // includeBooks => the POSTINGS path, with the per-book breakdown. Counts must AGREE across paths.
            using var full = await PostDoc(http, "/v1/search",
                "{\"query\":\"dhamma\",\"mode\":\"Exact\",\"includeBooks\":true}");
            var fullTerm = full.RootElement.GetProperty("terms")[0];
            Assert.Equal(2, fullTerm.GetProperty("books").GetArrayLength());
            Assert.Equal(leanTotal, fullTerm.GetProperty("totalCount").GetInt32());
            Assert.Equal(leanBooks, fullTerm.GetProperty("bookCount").GetInt32());
            // With includeBooks, returnedBookCount is the real distinct-book union over the page.
            Assert.Equal(2, full.RootElement.GetProperty("returnedBookCount").GetInt32());
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
        public async Task Search_does_corpus_wide_phrase_and_returns_a_round_trippable_term()
        {
            using var http = _api.Http();
            // "dhamma magga" is adjacent in the attha fixture book. A phrase query on the corpus-wide `search`
            // tool must find it (not just per-book `occurrences`), and the returned term must be space-joined
            // ("dhamma magga"), never the internal "~"-combo key. (Desktop MCP friction report)
            using var doc = await PostDoc(http, "/v1/search",
                "{\"query\":\"\\\"dhamma magga\\\"\",\"includeBooks\":true}");
            var term = doc.RootElement.GetProperty("terms")[0];
            var termText = term.GetProperty("term").GetString();
            Assert.Equal("dhamma magga", termText);              // readable + round-trippable, no '~'
            Assert.DoesNotContain("~", termText!);
            Assert.Equal(1, term.GetProperty("bookCount").GetInt32());
            Assert.Contains(_api.AtthaBook, term.GetProperty("books")[0].GetProperty("bookId").GetString());
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
            // /v1/occurrences now returns an envelope { occurrences, returnedCount, total, hasMore }, not a bare array.
            var occ = doc.RootElement.GetProperty("occurrences");
            Assert.True(occ.GetArrayLength() >= 1, "single-word wildcard should return occurrences");
            Assert.Equal(occ.GetArrayLength(), doc.RootElement.GetProperty("returnedCount").GetInt32());
            Assert.True(doc.RootElement.GetProperty("total").GetInt32() >= 1);
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

        // A typed tool return is serialized as JSON in the text content block; parse it for field assertions.
        private static JsonDocument ToolJson(ModelContextProtocol.Protocol.CallToolResult r) =>
            JsonDocument.Parse(string.Concat(r.Content.OfType<TextContentBlock>().Select(b => b.Text)));

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
        public async Task Mcp_books_tool_resolves_ids_to_names_and_pages()
        {
            await using var client = await ConnectMcpAsync();
            // Envelope { books, returnedCount, total, hasMore }; take:250 covers the whole catalog so the
            // fixture's mula book (a real catalog file name) is present - an agent can resolve a bookId.
            var books = await client.CallToolAsync("books", new Dictionary<string, object?> { ["take"] = 250 });
            Assert.NotEqual(true, books.IsError);
            Assert.Contains(_api.MulaBook, ToolText(books));
            using var booksDoc = ToolJson(books);
            int total = booksDoc.RootElement.GetProperty("total").GetInt32();
            Assert.True(total > 200, "the full catalog is 217 books");

            // Filtering narrows the catalog (the overflow fix) - Abhidhamma is a small subset.
            var abhi = await client.CallToolAsync("books",
                new Dictionary<string, object?> { ["pitaka"] = "Abhidhamma" });
            using var abhiDoc = ToolJson(abhi);
            int abhiTotal = abhiDoc.RootElement.GetProperty("total").GetInt32();
            Assert.True(abhiTotal > 0 && abhiTotal < total, "pitaka filter returns a proper subset");
        }

        [Fact]
        public async Task Books_endpoint_filters_and_pages()
        {
            using var http = _api.Http();
            // Paging: take:5 => a 5-book page over the full ~217-book catalog, hasMore true. (Cowork overflow fix)
            using var page = await GetDoc(http, "/v1/books?take=5");
            Assert.Equal(5, page.RootElement.GetProperty("returnedCount").GetInt32());
            Assert.Equal(5, page.RootElement.GetProperty("books").GetArrayLength());
            Assert.True(page.RootElement.GetProperty("total").GetInt32() > 200);
            Assert.True(page.RootElement.GetProperty("hasMore").GetBoolean());

            // Filter: pitaka=Abhidhamma is a small, all-Abhidhamma subset.
            using var abhi = await GetDoc(http, "/v1/books?pitaka=Abhidhamma&take=250");
            Assert.InRange(abhi.RootElement.GetProperty("total").GetInt32(), 1, 60);
            foreach (var b in abhi.RootElement.GetProperty("books").EnumerateArray())
                Assert.Equal("Abhidhamma", b.GetProperty("pitaka").GetString());
        }

        [Fact]
        public async Task Mcp_search_filter_scopes_to_book_class()
        {
            await using var client = await ConnectMcpAsync();
            // "dhamma" is in both fixture books (mula + attha). A mula-only filter (a NESTED object param over
            // MCP) must scope it to just the mula book - proving the MCP search tool now honors the filter.
            var filtered = await client.CallToolAsync("search", new Dictionary<string, object?>
            {
                ["query"] = "dhamma",
                ["includeBooks"] = true,
                ["filter"] = new Dictionary<string, object?>
                {
                    ["vinaya"] = true, ["sutta"] = true, ["abhidhamma"] = true,
                    ["mula"] = true, ["atthakatha"] = false, ["tika"] = false, ["other"] = true,
                },
            });
            Assert.NotEqual(true, filtered.IsError);
            using var doc = ToolJson(filtered);
            int bookCount = doc.RootElement.GetProperty("terms")[0].GetProperty("bookCount").GetInt32();
            Assert.Equal(1, bookCount);   // mula only; the attha book is excluded by the filter
        }

        [Fact]
        public async Task Mcp_bridge_relays_list_and_call_to_the_real_mcp()
        {
            // The --mcp-bridge relay (#278 Phase 1): an MCP client drives the bridge over in-memory pipes; the
            // bridge transparently forwards to the REAL /mcp. Proves the transport pump handshakes (initialize
            // through the relay) + round-trips list/call end-to-end.
            var toServer = new Pipe();   // client writes -> bridge(server) reads
            var toClient = new Pipe();   // bridge writes -> client reads

            var clientSide = new StreamServerTransport(
                toServer.Reader.AsStream(), toClient.Writer.AsStream(), "cst-reader-bridge", NullLoggerFactory.Instance);

            var httpOptions = new HttpClientTransportOptions
            {
                Endpoint = new System.Uri(_api.BaseUrl + "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + _api.Token },
            };

            using var cts = new CancellationTokenSource(System.TimeSpan.FromSeconds(30));
            var relay = McpBridge.RunAsync(clientSide, httpOptions, httpClient: null, cts.Token);

            var clientTransport = new StreamClientTransport(
                toServer.Writer.AsStream(), toClient.Reader.AsStream(), NullLoggerFactory.Instance);
            await using var client = await McpClient.CreateAsync(clientTransport, cancellationToken: cts.Token);

            // Handshake, list, and call all traverse the relay.
            var tools = await client.ListToolsAsync(cancellationToken: cts.Token);
            Assert.Contains(tools, t => t.Name == "search");

            var result = await client.CallToolAsync(
                "search", new Dictionary<string, object?> { ["query"] = "dhamma" }, cancellationToken: cts.Token);
            Assert.NotEqual(true, result.IsError);
            Assert.Contains("dhamma",
                string.Concat(result.Content.OfType<TextContentBlock>().Select(b => b.Text)));

            cts.Cancel();
            try { await relay; } catch { /* relay unwinds on cancel */ }
        }

        [Fact]
        public async Task Mcp_bridge_errors_fast_not_hangs_on_a_bad_token()
        {
            // must-have #2: when a forwarded request fails (here a 401 from a wrong token), the relay synthesizes
            // a JsonRpcError for the waiting client instead of leaving it to hang. (#278)
            var toServer = new Pipe();
            var toClient = new Pipe();
            var clientSide = new StreamServerTransport(
                toServer.Reader.AsStream(), toClient.Writer.AsStream(), "cst-reader-bridge", NullLoggerFactory.Instance);
            var httpOptions = new HttpClientTransportOptions
            {
                Endpoint = new System.Uri(_api.BaseUrl + "/mcp"),
                TransportMode = HttpTransportMode.StreamableHttp,
                AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer WRONG-TOKEN" },
            };
            using var cts = new CancellationTokenSource(System.TimeSpan.FromSeconds(30));
            var relay = McpBridge.RunAsync(clientSide, httpOptions, httpClient: null, cts.Token);

            var clientTransport = new StreamClientTransport(
                toServer.Writer.AsStream(), toClient.Reader.AsStream(), NullLoggerFactory.Instance);

            // The `initialize` request is forwarded, 401s, and comes back as a JsonRpcError -> CreateAsync fails
            // FAST (not a spawn-timeout hang). Assert it throws well before the 30s guard.
            var sw = System.Diagnostics.Stopwatch.StartNew();
            await Assert.ThrowsAnyAsync<System.Exception>(async () =>
            {
                await using var client = await McpClient.CreateAsync(clientTransport, cancellationToken: cts.Token);
            });
            Assert.True(sw.Elapsed < System.TimeSpan.FromSeconds(10), $"errored in {sw.Elapsed} — should be fast, not a hang");

            cts.Cancel();
            try { await relay; } catch { }
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
