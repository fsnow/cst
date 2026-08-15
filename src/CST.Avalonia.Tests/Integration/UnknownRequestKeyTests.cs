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
    }
}
