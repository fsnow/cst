using System.Linq;
using CST.Avalonia.Services.LocalApi.Lemma;
using Xunit;

namespace CST.Avalonia.Tests.LocalApi
{
    // POC scaffold tests (#247 lemma map). These pin the RESPONSE SHAPE the endpoints return; the data is
    // stubbed. When a dpd.db-backed ILemmaProvider replaces the stub, these assertions describe the contract
    // a real provider must satisfy for the seeded forms.
    public class LemmaStubProviderTests
    {
        private readonly ILemmaProvider _p = new LemmaStubProvider();

        [Fact]
        public void Resolve_simple_form_returns_single_candidate()
        {
            var r = _p.Resolve("pajānāti");
            var c = Assert.Single(r.Candidates);
            Assert.Equal(39702, c.LemmaId);
            Assert.Equal("pajānāti", c.Lemma);
            Assert.Equal("pr", c.Pos);
        }

        [Fact]
        public void Resolve_homograph_returns_multiple_candidates_split_by_derivation()
        {
            var r = _p.Resolve("paññāya");
            Assert.True(r.Candidates.Count > 1, "paññāya is a homograph — expect multiple candidates");
            // The gerund sense derives from the verb; the noun sense derives from paññā — the split we want.
            Assert.Contains(r.Candidates, c => c.Pos == "ger" && c.DerivedFrom == "pajānāti");
            Assert.Contains(r.Candidates, c => c.DerivedFrom == "paññā");
        }

        [Fact]
        public void Resolve_sandhi_form_exposes_deconstructions()
        {
            var r = _p.Resolve("sammappajānāti");
            Assert.Contains("samma + pajānāti", r.Deconstructions);
        }

        [Fact]
        public void Resolve_unseeded_form_is_empty_but_well_formed()
        {
            var r = _p.Resolve("zzznotaword");
            Assert.Empty(r.Candidates);
            Assert.Contains("STUB", r.Note);
        }

        [Fact]
        public void Forms_returns_paradigm_and_family_only_when_requested()
        {
            var withFamily = _p.Forms(39702, includeFamily: true);
            Assert.NotEmpty(withFamily.Forms);
            Assert.All(withFamily.Forms, f => Assert.StartsWith("pajān", f.Form));
            Assert.NotNull(withFamily.Family);
            Assert.Contains(withFamily.Family!, c => c.Lemma == "pajānitvā");

            var noFamily = _p.Forms(39702, includeFamily: false);
            Assert.Null(noFamily.Family);
        }
    }
}
