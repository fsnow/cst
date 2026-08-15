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
    /// #558: a misnamed body key must be REJECTED and NAMED, on every endpoint.
    ///
    /// <para>
    /// Found in a cold-agent round by an agent making the mistake naturally, which is what those runs are for.
    /// The surface had two different bad reactions to one user error, and the asymmetry pointed the wrong way:
    /// sending <c>highlight</c> instead of <c>terms</c> to <c>navigate</c> returned 200 with
    /// <c>highlights: 0</c> and no note, while the SAME response for the CORRECT key explained itself. The
    /// agent's own mistake got the worse diagnostic of the two, and the friction reports show agents reasoning
    /// onward from it rather than retrying.
    /// </para>
    ///
    /// <para>
    /// These run against the real server, because the defect lived in the JSON/binding seam that a mocked
    /// endpoint test steps straight over.
    /// </para>
    /// </summary>
    [Collection("LocalApiIntegration")]
    public class UnknownRequestKeyTests : IAsyncLifetime
    {
        private LocalApiTestServer _api = null!;

        public async Task InitializeAsync() => _api = await LocalApiTestServer.StartAsync();
        public async Task DisposeAsync() => await _api.DisposeAsync();

        private static StringContent Json(string body) => new(body, Encoding.UTF8, "application/json");

        private async Task<(HttpStatusCode Status, string Body)> Post(string path, string body)
        {
            using var http = _api.Http();
            var resp = await http.PostAsync(path, Json(body));
            return (resp.StatusCode, await resp.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task A_plausible_wrong_key_is_rejected_rather_than_ignored()
        {
            // The issue's own example is `highlight` for `terms` on /v1/navigate, which this fixture does not
            // map (navigate needs a presentation service, i.e. a reader window). The defect was never
            // navigate-specific though - it was the JSON default applying to the whole surface - so it is
            // driven here through an endpoint the harness does expose.
            var (status, body) = await Post("/v1/search", "{\"query\":\"dhamma\",\"highlight\":\"metta\"}");

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains("highlight", body);
        }

        [Fact]
        public async Task The_offending_key_is_named_and_the_valid_ones_listed()
        {
            var (_, body) = await Post("/v1/search", "{\"query\":\"dhamma\",\"nosuchkey\":1}");

            using var doc = JsonDocument.Parse(body);
            var error = doc.RootElement.GetProperty("error").GetString()!;

            Assert.Contains("'nosuchkey'", error);
            Assert.Contains("query", error);          // a key that WOULD have worked, offered back
        }

        [Fact]
        public async Task The_error_uses_the_same_shape_as_every_other_failure_on_this_surface()
        {
            // /v1/occurrences already answered an unknown BOOK with {"error":"..."}. An unknown KEY answering
            // with an empty body was the inconsistency; both are now the same shape.
            var (_, body) = await Post("/v1/occurrences",
                "{\"bookId\":\"nope.xml\",\"nosuchkey\":true}");

            using var doc = JsonDocument.Parse(body);
            Assert.True(doc.RootElement.TryGetProperty("error", out var e));
            Assert.False(string.IsNullOrWhiteSpace(e.GetString()));
        }

        [Theory]
        [InlineData("/v1/occurrences", "{\"bookId\":\"a.xml\",\"term\":\"x\",\"nosuchkey\":1}")]
        [InlineData("/v1/passage", "{\"bookId\":\"a.xml\",\"nosuchkey\":1}")]
        public async Task Every_endpoint_rejects_an_unknown_key_rather_than_dropping_it(string path, string body)
        {
            // The default that caused this applied to the whole surface, not to navigate alone - so the fix
            // has to be checked across it, or the next endpoint added inherits the old behaviour.
            var (status, text) = await Post(path, body);

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains("nosuchkey", text);
        }

        [Fact]
        public async Task A_correct_body_is_unaffected()
        {
            // The point is to reject what was already broken, not to narrow what works.
            using var http = _api.Http();
            var resp = await http.PostAsync("/v1/search", Json("{\"query\":\"dhamma\"}"));

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        [Fact]
        public async Task A_bad_key_INSIDE_the_filter_is_named_too()
        {
            // The issue's SECOND reported case, and the one a top-level-only check silently leaves broken:
            // ToolBookFilter refuses unknown members itself, so binding already rejected this - with an
            // empty 400, which is the exact diagnostic this change exists to replace.
            var (status, body) = await Post("/v1/search",
                "{\"query\":\"dhamma\",\"filter\":{\"nosuchkey\":true}}");

            Assert.Equal(HttpStatusCode.BadRequest, status);

            using var doc = JsonDocument.Parse(body);
            var error = doc.RootElement.GetProperty("error").GetString()!;

            Assert.Contains("filter.nosuchkey", error);
            // The keys listed are the FILTER's, not the request's - naming top-level keys here would send
            // the caller looking in the wrong place.
            Assert.Contains("mula", error);
            Assert.DoesNotContain("query", error);
        }

        [Fact]
        public async Task A_valid_filter_still_works()
        {
            using var http = _api.Http();
            var resp = await http.PostAsync("/v1/search",
                Json("{\"query\":\"dhamma\",\"filter\":{\"mula\":true}}"));

            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
        }

        [Theory]
        [InlineData("/v1/convert", "{\"text\":\"dhamma\",\"outputScript\":\"Devanagari\",\"nosuchkey\":1}")]
        [InlineData("/v1/dictionary/lookup", "{\"language\":\"en\",\"query\":\"dhamma\",\"nosuchkey\":1}")]
        public async Task The_remaining_mapped_endpoints_name_the_key_too(string path, string body)
        {
            // The rejection is global (the Disallow backstop), but the NAMING depends on each entry in
            // ContractFor. A typo'd entry regresses that endpoint to the bodiless 400 with every other test
            // still green, so each mapped endpoint is asserted rather than assumed. (fable review)
            var (status, text) = await Post(path, body);

            Assert.Equal(HttpStatusCode.BadRequest, status);
            Assert.Contains("nosuchkey", text);
        }

        [Fact]
        public async Task An_unauthenticated_docs_path_does_not_reach_the_body_check()
        {
            // /docs is deliberately unauthenticated so a cold agent can orient itself. Matching the contract
            // by path SUFFIX made POST /docs/search select the search contract, so an unauthenticated caller
            // could make the server buffer an arbitrary body. Matching the full path closes it. (fable review)
            using var http = new HttpClient { BaseAddress = new System.Uri(_api.BaseUrl) };   // NO token

            var resp = await http.PostAsync("/docs/search", Json("{\"nosuchkey\":1}"));

            Assert.NotEqual(HttpStatusCode.BadRequest, resp.StatusCode);   // not our unknown-key answer
            Assert.Empty(await resp.Content.ReadAsStringAsync());
        }

        [Fact]
        public async Task A_trailing_slash_or_odd_casing_still_gets_the_named_error()
        {
            // Route matching tolerates both; an Ordinal suffix match did not, so these fell through to the
            // bodiless 400 this exists to remove. (fable review)
            foreach (var path in new[] { "/v1/search/", "/v1/Search" })
            {
                var (status, body) = await Post(path, "{\"query\":\"dhamma\",\"nosuchkey\":1}");

                Assert.Equal(HttpStatusCode.BadRequest, status);
                Assert.Contains("nosuchkey", body);
            }
        }
    }
}
