using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Services;
using CST.Conversion;
using CST.Lemma;

namespace CST.Avalonia.Services.LocalApi.Lemma;

// Agent-facing response shapes for the lemma endpoints, plus mapping from the internal service results.
// IPE never leaks: forms/lemmas are emitted only in the requested output script.

/// <summary>One lemma a surface form can belong to.</summary>
public sealed record LemmaCandidateDto(long LemmaId, string Lemma, string? Pos, string? Gloss, string? DerivedFrom);

/// <summary>Response for GET /v1/lemma/{form} — a surface form's candidate lemmas (back-lookup).</summary>
public sealed record LemmaLookupResponse(string Form, IReadOnlyList<LemmaCandidateDto> Candidates, string? Note);

/// <summary>One attested surface form of a lemma with its corpus count.</summary>
public sealed record LemmaFormDto(string Form, int Count, int BookCount);

/// <summary>Response for GET /v1/forms/{lemmaId} — a lemma's attested paradigm with counts (forward expansion).</summary>
public sealed record LemmaFormsResponse(
    long LemmaId, string Lemma, string? Pos, string? Gloss, string? DerivedFrom,
    IReadOnlyList<LemmaFormDto> Forms, int TotalOccurrences,
    int AttestedFormCount, int CandidateFormCount, bool ExpansionCapped,
    IReadOnlyList<LemmaCandidateDto> RelatedLemmas, string? Note);

/// <summary>One ranked alternative split of a compound/sandhi word (parts in the output script).</summary>
public sealed record WordSplitDto(int Rank, IReadOnlyList<string> Parts);

/// <summary>Response for GET /v1/deconstruct/{word} — DPD's ranked candidate splits of a compound/sandhi
/// word (rank 0 = best; splits are ALTERNATIVES, not sequential parts), plus any direct lemma(s) the whole
/// word is itself a headword of.</summary>
public sealed record DeconstructResponse(
    string Word,
    IReadOnlyList<WordSplitDto> Splits,
    IReadOnlyList<LemmaCandidateDto> DirectLemmas,
    string? Note);

internal static class LemmaApi
{
    // Convert a lemma citation form (IAST, may carry a trailing homonym number) to the output script.
    private static string Script(string lemma, Script outputScript)
        => outputScript == CST.Conversion.Script.Latin ? lemma : ScriptConverter.Convert(lemma, CST.Conversion.Script.Latin, outputScript);

    private static string? ScriptOrNull(string? lemma, Script outputScript)
        => string.IsNullOrEmpty(lemma) ? lemma : Script(lemma!, outputScript);

    public static LemmaLookupResponse ToLookup(string form, FormResolution res, Script outputScript)
    {
        var candidates = res.Candidates
            .Select(c => new LemmaCandidateDto(c.LemmaId, Script(c.Lemma, outputScript), c.Pos, c.Gloss, ScriptOrNull(c.DerivedFrom, outputScript)))
            .ToList();
        string? note = candidates.Count > 1
            ? "This form is a homograph (multiple lemmas). Pick the intended lemmaId, then GET /v1/forms/{lemmaId} "
              + "for its attested paradigm with counts."
            : null;
        return new LemmaLookupResponse(form, candidates, note);
    }

    public static DeconstructResponse ToDeconstruct(string word, WordDeconstruction res, Script outputScript)
    {
        var splits = res.Splits
            .Select(s => new WordSplitDto(s.Rank, s.Parts.Select(p => Script(p, outputScript)).ToList()))
            .ToList();
        var direct = res.DirectLemmas
            .Select(c => new LemmaCandidateDto(c.LemmaId, Script(c.Lemma, outputScript), c.Pos, c.Gloss, ScriptOrNull(c.DerivedFrom, outputScript)))
            .ToList();
        // Echo the caller's word as-is (it is already in the output script), matching ToLookup — no redundant
        // Latin-round-trip conversion. (fable review)
        return new DeconstructResponse(word, splits, direct, DeconstructNote(splits, direct, res.Scope));
    }

    // Guidance for the NULL / not-found case (REST 404 and MCP no-result), so a missing word isn't reported as
    // a flat "not found": point at the lemma fallback, and on a non-full asset note that full-scope coverage is
    // needed for compounds (the mid/lean asset that dropped the split is exactly where this matters). (fable review)
    public static string DeconstructNotFoundNote(string word, string? scope)
    {
        var head = $"No sandhi split recorded for '{word}'. It may be a simple word or a dictionary headword — try GET /v1/lemma/{word}.";
        return NeedsFullHint(scope)
            ? head + $" (This asset, scope '{scope}', records splits only for enclitic forms; install the full-scope DPD-lemma asset for full compound deconstruction.)"
            : head;
    }

    // The scope is "" (never null) when the asset's meta omits it (DpdLemmaMeta.Scope is a non-null string), so
    // guard on empty, not null — else an unknown-scope asset would print a bare "scope ''" hint. (fable review)
    private static bool NeedsFullHint(string? scope) => !string.IsNullOrEmpty(scope) && scope != "full";

    // Agent guidance: how to read the splits (ranked ALTERNATIVES, ambiguous, per-part homographs) and how to
    // continue the chain (part -> /v1/lemma -> /v1/dictionary). Scope-aware: a non-full asset only records
    // enclitic splits, so an empty result may just mean "install the full-scope asset".
    private static string DeconstructNote(IReadOnlyList<WordSplitDto> splits, IReadOnlyList<LemmaCandidateDto> direct, string? scope)
    {
        if (splits.Count == 0)
        {
            var head = direct.Count > 0
                ? "No sandhi split — this word is itself a dictionary headword (see directLemmas)."
                : "No deconstruction recorded for this word.";
            return NeedsFullHint(scope)
                ? head + $" (asset scope '{scope}' records splits only for enclitic forms; the full-scope DPD-lemma asset covers all compounds.)"
                : head;
        }
        var lead = splits.Count > 1
            ? "This word has MULTIPLE candidate splits, ranked by DPD (rank 0 = best). They are ALTERNATIVE analyses, not sequential parts — Pāli sandhi is ambiguous, so pick the plausible one; the top rank is not guaranteed correct."
            : "Each entry in a split's `parts` is a surface form.";
        return lead + " Resolve a part with GET /v1/lemma/{part} for its lemma(s) (a part is often a HOMOGRAPH — don't assume one sense), then POST /v1/dictionary/lookup for glosses."
            + (direct.Count > 0 ? " This word is ALSO a dictionary headword in its own right (see directLemmas)." : string.Empty);
    }

    public static LemmaFormsResponse ToForms(LemmaSearchResult res, Script outputScript, bool familyUnion = false)
    {
        var forms = res.AttestedForms.Select(f => new LemmaFormDto(f.Form, f.Count, f.BookCount)).ToList();
        var related = res.RelatedLemmas
            .Select(c => new LemmaCandidateDto(c.LemmaId, Script(c.Lemma, outputScript), c.Pos, c.Gloss, ScriptOrNull(c.DerivedFrom, outputScript)))
            .ToList();
        int synthetic = res.CandidateFormCount - res.AttestedFormCount;
        string note = $"Counts are corpus occurrences (from the index). {res.AttestedFormCount} of "
            + $"{res.CandidateFormCount} DPD candidate forms occur; the other {synthetic} are synthetic "
            + "(never attested). totalOccurrences is the grand total for this "
            + (familyUnion ? "whole word family (de-duplicated union)." : "lemma.")
            + (res.ExpansionCapped ? " NOTE: the form set was capped — narrow the query." : string.Empty)
            // The "assemble a conjugation" guidance only makes sense on the DEFAULT (family:false) response — on a
            // family:true response the forms above ALREADY union the whole family, so re-fetching would duplicate.
            + (related.Count > 0 && !familyUnion
                ? " relatedLemmas = the OTHER lemmas of this word-family, each with its pos — a verb's participles / "
                  + "absolutive / infinitive are SEPARATE lemmaIds and are NOT in the forms above. To assemble a whole "
                  + "CONJUGATION, GET /v1/forms/{lemmaId} for the VERBAL-pos relatedLemmas and union their forms "
                  + "(family:true instead unions the ENTIRE family — including deverbal NOUNS — which is broader). Do NOT "
                  + "sum totalOccurrences across relatedLemmas; they can share surface tokens (double-count)."
                : string.Empty);
        return new LemmaFormsResponse(
            res.Lemma.LemmaId, Script(res.Lemma.Lemma, outputScript), res.Lemma.Pos, res.Lemma.Gloss,
            ScriptOrNull(res.Lemma.DerivedFrom, outputScript),
            forms, res.TotalOccurrences, res.AttestedFormCount, res.CandidateFormCount, res.ExpansionCapped, related, note);
    }
}
