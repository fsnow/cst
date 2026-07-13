using System.Collections.Generic;
using System.Linq;

namespace CST.Avalonia.Services.LocalApi.Lemma
{
    /// <summary>
    /// POC stub for <see cref="ILemmaProvider"/>. Returns canned data (verbatim from the real dpd.db
    /// v0.4.20260531 rows — see ~/dpd-poc/TASK1_dpd_inspection.md) for a couple of seeded forms so the
    /// endpoint wiring can be exercised end-to-end. Replace with a dpd.db-backed provider; the response
    /// shapes are the intended contract.
    /// </summary>
    public sealed class LemmaStubProvider : ILemmaProvider
    {
        private const string StubNote =
            "POC STUB — canned DPD v0.4.20260531 data for seeded forms only (pajānāti, paññāya, nappajānāti). " +
            "Replace LemmaStubProvider with a dpd.db-backed ILemmaProvider.";

        // Seeded lookup rows (form -> candidate lemmas), taken from the real DPD lookup table.
        private static readonly Dictionary<string, LemmaResponse> SeededForms = new()
        {
            ["pajānāti"] = new LemmaResponse("pajānāti", new[]
            {
                new LemmaCandidate(39702, "pajānāti", "pr", "pr 3rd sg",
                    "knows; knows clearly; understands; distinguishes", null),
            }, System.Array.Empty<string>(), StubNote),

            // The homograph — surface paññāya splits across many headwords, resolved by derived_from.
            ["paññāya"] = new LemmaResponse("paññāya", new[]
            {
                new LemmaCandidate(40070, "paññāya 1", "ger", "ger", "knowing; understanding", "pajānāti"),
                new LemmaCandidate(40071, "paññāya 2", "fem", "fem instr sg", "by wisdom; with intelligence", "paññā"),
                new LemmaCandidate(39994, "paññā 1", "fem", "fem instr/dat/abl/gen/loc sg",
                    "wisdom; knowledge; understanding; insight", "pajānāti"),
            }, System.Array.Empty<string>(), StubNote),

            ["nappajānāti"] = new LemmaResponse("nappajānāti", new[]
            {
                new LemmaCandidate(35708, "nappajānāti", "pr", "pr 3rd sg",
                    "does not know; does not clearly understand", "pajānāti"),
            }, System.Array.Empty<string>(), StubNote),

            // A sandhi example, to exercise the deconstructor field.
            ["sammappajānāti"] = new LemmaResponse("sammappajānāti", System.Array.Empty<LemmaCandidate>(),
                new[] { "sammappajāna + iti", "sammappajānā + iti", "samma + pajānāti" }, StubNote),
        };

        // Seeded paradigm (headword 39702) — a representative subset of the 34 attested pajān- forms.
        private static readonly string[] PajanatiForms =
        {
            "pajānāti", "pajānanti", "pajānāmi", "pajānāma", "pajānāsi", "pajānātha",
            "pajāneyya", "pajāneyyaṃ", "pajānissati", "pajānissāmi", "pajānāhi", "pajānātu", "pajāna",
        };

        // Seeded derived_from family for pajānāti — a representative subset of the 36 headwords.
        private static readonly LemmaCandidate[] PajanatiFamily =
        {
            new(39702, "pajānāti", "pr", "", "knows; understands", null),
            new(39703, "pajānitvā", "abs", "", "having clearly understood", "pajānāti"),
            new(40070, "paññāya 1", "ger", "", "knowing; understanding", "pajānāti"),
            new(40072, "paññāyati 1", "pr", "", "is clearly known; is evident", "pajānāti"),
            new(35708, "nappajānāti", "pr", "", "does not know", "pajānāti"),
            new(39994, "paññā 1", "fem", "", "wisdom; knowledge; understanding", "pajānāti"),
            new(40010, "paññāta 1", "pp", "", "well known; famous", "pajānāti"),
        };

        public LemmaResponse Resolve(string form) =>
            SeededForms.TryGetValue(form, out var r)
                ? r
                : new LemmaResponse(form, System.Array.Empty<LemmaCandidate>(),
                    System.Array.Empty<string>(),
                    StubNote + " (form not seeded in the stub)");

        public FormsResponse Forms(long lemmaId, bool includeFamily)
        {
            if (lemmaId == 39702)
            {
                return new FormsResponse(39702, "pajānāti",
                    PajanatiForms.Select(f => new FormEntry(f, null)).ToList(),
                    includeFamily ? PajanatiFamily : null,
                    StubNote + " Per-form Count is null in the stub — it comes from the Lucene index, not DPD.");
            }
            return new FormsResponse(lemmaId, "?", System.Array.Empty<FormEntry>(), null,
                StubNote + " (lemmaId not seeded in the stub)");
        }
    }
}
