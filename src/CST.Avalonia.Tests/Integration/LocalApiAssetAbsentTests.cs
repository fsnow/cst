using System.Net;
using System.Threading.Tasks;
using CST.Avalonia.Tests.TestSupport;
using Xunit;

namespace CST.Avalonia.Tests.Integration
{
    /// <summary>
    /// End-to-end coverage of the asset-ABSENT path: the assembled surface-C stack with NO dpd-cst-subset asset
    /// installed. Asserts the lemma/dictionary endpoints degrade cleanly (503, not 500/hang) and — the token-cost
    /// point — that the DPD doc regions are GATED OUT of llms.txt so an agent never discovers functionality that
    /// only 503s. Shares the serial "LocalApiIntegration" collection (fixture indexing mutates global Books).
    /// </summary>
    [Collection("LocalApiIntegration")]
    public class LocalApiAssetAbsentTests : IAsyncLifetime
    {
        private LocalApiTestServer _api = null!;

        public async Task InitializeAsync() => _api = await LocalApiTestServer.StartAsync(withLemmaAsset: false);
        public async Task DisposeAsync() => await _api.DisposeAsync();

        [Theory]
        [InlineData("/v1/lemma/dhamma")]
        [InlineData("/v1/forms/100")]
        [InlineData("/v1/deconstruct/dhamma")]
        [InlineData("/v1/lemma-report/100")]
        public async Task Lemma_endpoints_return_503_when_asset_absent(string path)
        {
            using var http = _api.Http();
            var resp = await http.GetAsync(path);
            // Mapped-but-unavailable: a clear 503 (the "dataset not installed" contract), never a 500 or a hang.
            Assert.Equal(HttpStatusCode.ServiceUnavailable, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.Contains("not installed", body);
        }

        [Fact]
        public async Task Llms_full_gates_out_the_dpd_sections_but_keeps_the_flat_dictionary()
        {
            using var http = _api.Http();
            var resp = await http.GetAsync("/llms-full.txt");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();

            // DPD/lemma endpoints are gated away so an agent doesn't spend tokens on 503-only functionality.
            Assert.DoesNotContain("/v1/deconstruct", body);
            Assert.DoesNotContain("/v1/lemma/{form}", body);
            Assert.DoesNotContain("/v1/forms/{lemmaId}", body);
            Assert.DoesNotContain("language:\"dpd\"", body);
            // The flat dictionary (en/hi) is unaffected — it works without the asset.
            Assert.Contains("/v1/dictionary/languages", body);
            Assert.Contains("/v1/dictionary/lookup", body);
        }

        [Fact]
        public async Task Dictionary_doc_slice_gates_out_dpd_but_keeps_flat()
        {
            using var http = _api.Http();
            var resp = await http.GetAsync("/docs/dictionary.md");
            Assert.Equal(HttpStatusCode.OK, resp.StatusCode);
            var body = await resp.Content.ReadAsStringAsync();
            Assert.DoesNotContain("/v1/deconstruct", body);
            Assert.DoesNotContain("/v1/lemma/{form}", body);
            Assert.Contains("/v1/dictionary/languages", body);
        }
    }
}
