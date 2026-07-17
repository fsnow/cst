using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Services;
using CST.Avalonia.Services.LocalApi.Lemma;
using CST.Conversion;
using ModelContextProtocol.Server;

namespace CST.Avalonia.Services.LocalApi.Mcp
{
    /// <summary>
    /// MCP tools over the DPD-lemma dataset — <c>lemma_lookup</c> (a surface form → its candidate lemmas) and
    /// <c>lemma_forms</c> (a lemma → its ATTESTED paradigm with corpus counts). The two hops of lemma search:
    /// resolve an inflected word to a lemma, disambiguate if it's a homograph, then get every attested form
    /// with counts — no hand-built wildcard/regex, no synthetic-vs-attested guessing. (#247)
    /// </summary>
    [McpServerToolType]
    internal sealed class LemmaMcpTool
    {
        // IPE must never be an output script (it is the internal encoding); fall back to Latin.
        private static Script Safe(Script s) => s is Script.Ipe or Script.Unknown ? Script.Latin : s;

        [McpServerTool(Name = "lemma_lookup")]
        [Description("Resolve a Pali SURFACE FORM (an inflected word — e.g. one from a 'search' result) to its "
            + "candidate DICTIONARY LEMMAS (from the Digital Pali Dictionary). Returns each candidate's lemmaId, "
            + "lemma, part-of-speech, gloss, and derived_from. A form is often a HOMOGRAPH (several lemmas); pick "
            + "the intended lemmaId, then call 'lemma_forms' for its full attested paradigm. `script` is BOTH the "
            + "input form's script and the output script (default Latin).")]
        public static LemmaLookupResponse LemmaLookup(
            ILemmaSearchService lemma,
            [Description("The surface form to resolve, written in `script`.")] string form,
            [Description("Script for the input form and the output (default Latin).")] Script script = Script.Latin)
        {
            var s = Safe(script);
            var res = lemma.ResolveWord(form, s);
            return res is null
                ? new LemmaLookupResponse(form, Array.Empty<LemmaCandidateDto>(), "No lemma resolves this form.")
                : LemmaApi.ToLookup(form, res, s);
        }

        [McpServerTool(Name = "lemma_forms")]
        [Description("Given a lemmaId (from 'lemma_lookup'), return its ATTESTED PARADIGM: every inflected surface "
            + "form that OCCURS in the corpus, each with its occurrence count and book count, plus the grand "
            + "total. Answers 'give me the whole declension/conjugation of X and how often each form occurs' in "
            + "ONE call — no wildcard/regex, no homograph confusion (the lemma was already chosen). Counts come "
            + "from the corpus index; DPD candidate forms that never occur are omitted (synthetic). `family:true` "
            + "widens to the whole derived_from word family. `script` sets the output script (default Latin).")]
        public static async Task<LemmaFormsResponse> LemmaForms(
            ILemmaSearchService lemma,
            [Description("The lemma id from 'lemma_lookup'.")] long lemmaId,
            [Description("Widen to the whole derived_from word family (default false).")] bool family = false,
            [Description("Output script (default Latin).")] Script script = Script.Latin,
            CancellationToken ct = default)
        {
            var s = Safe(script);
            var res = await lemma.ExpandAndSearchAsync(lemmaId, family, null, s, ct).ConfigureAwait(false);
            return res is null
                ? new LemmaFormsResponse(lemmaId, string.Empty, null, null, null,
                    Array.Empty<LemmaFormDto>(), 0, 0, 0, false, $"Unknown lemmaId {lemmaId}.")
                : LemmaApi.ToForms(res, s);
        }

        [McpServerTool(Name = "sandhi_split")]
        [Description("Deconstruct a Pali COMPOUND or SANDHI word into its constituent parts, using the Digital "
            + "Pali Dictionary's deconstructor. Returns DPD's RANKED alternative splits (rank 0 = best; the "
            + "splits are ALTERNATIVE analyses, NOT sequential pieces — Pali sandhi is ambiguous and the top "
            + "rank is not guaranteed correct), plus any direct lemma(s) if the whole word is itself a headword "
            + "(e.g. sammāsambuddho). This is the word->parts step only: each part is a surface form, so resolve "
            + "it with 'lemma_lookup' (a part is often a HOMOGRAPH — don't assume one sense) and look up its "
            + "gloss. `script` is BOTH the input and output script (default Latin).")]
        public static DeconstructResponse SandhiSplit(
            ILemmaSearchService lemma,
            [Description("The compound/sandhi word to deconstruct, written in `script`.")] string word,
            [Description("Script for the input word and the output (default Latin).")] Script script = Script.Latin)
        {
            var s = Safe(script);
            var res = lemma.Deconstruct(word, s);
            return res is null
                ? new DeconstructResponse(word, Array.Empty<WordSplitDto>(), Array.Empty<LemmaCandidateDto>(),
                    LemmaApi.DeconstructNotFoundNote(word, lemma.Meta?.Scope))
                : LemmaApi.ToDeconstruct(word, res, s);
        }
    }
}
