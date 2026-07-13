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
    int AttestedFormCount, int CandidateFormCount, bool ExpansionCapped, string? Note);

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

    public static LemmaFormsResponse ToForms(LemmaSearchResult res, Script outputScript)
    {
        var forms = res.AttestedForms.Select(f => new LemmaFormDto(f.Form, f.Count, f.BookCount)).ToList();
        int synthetic = res.CandidateFormCount - res.AttestedFormCount;
        string note = $"Counts are corpus occurrences (from the index). {res.AttestedFormCount} of "
            + $"{res.CandidateFormCount} DPD candidate forms occur; the other {synthetic} are synthetic "
            + "(never attested). totalOccurrences is the grand total for this lemma."
            + (res.ExpansionCapped ? " NOTE: the form set was capped — narrow the query." : string.Empty);
        return new LemmaFormsResponse(
            res.Lemma.LemmaId, Script(res.Lemma.Lemma, outputScript), res.Lemma.Pos, res.Lemma.Gloss,
            ScriptOrNull(res.Lemma.DerivedFrom, outputScript),
            forms, res.TotalOccurrences, res.AttestedFormCount, res.CandidateFormCount, res.ExpansionCapped, note);
    }
}
