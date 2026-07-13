using System.Collections.Generic;

namespace CST.Avalonia.Services.LocalApi.Lemma
{
    // ---------------------------------------------------------------------------------------------
    // POC SCAFFOLD for the morphological lemma-map API (issue #247). STUB DATA ONLY — the wiring is
    // real (routes + DTOs + response shape), the resolver is canned. Drop-in point: replace
    // ILemmaProvider/LemmaStubProvider with a dpd.db-backed provider.
    //
    // The exact SQLite queries + schema are documented in ~/dpd-poc/TASK1_dpd_inspection.md. In short:
    //   (1) form -> candidates : SELECT headwords, grammar FROM lookup WHERE lookup_key = :form
    //   (2) headword -> gloss  : SELECT lemma_1,pos,stem,pattern,meaning_1 FROM dpd_headwords WHERE id=:id
    //   (3) headword -> forms  : SELECT lookup_key FROM lookup
    //                              WHERE json_valid(headwords) AND EXISTS(SELECT 1 FROM json_each(headwords) WHERE value=:id)
    //   (4) verb -> family     : SELECT id,lemma_1,pos,meaning_1 FROM dpd_headwords WHERE derived_from=:lemma OR id=:baseId
    //
    // Contract note: `lookup` gives the CANDIDATE lemmas for a surface form; it does not assign a
    // specific corpus occurrence to one candidate (per-occurrence disambiguation is the #247
    // extension). Per-form corpus counts come from OUR Lucene index, not DPD (null in the stub).
    // ---------------------------------------------------------------------------------------------

    /// <summary>One lemma a surface form could belong to (DPD headword + the analysis for this form).</summary>
    public sealed record LemmaCandidate(
        long LemmaId,          // dpd_headwords.id
        string Lemma,          // dpd_headwords.lemma_1 (with homonym number, e.g. "paññāya 1")
        string Pos,            // dpd_headwords.pos ("pr", "abs", "ger", "fem", "adj", "aor", ...)
        string Grammar,        // the inflection analysis of THIS form (lookup.grammar), e.g. "fem instr sg"
        string? Gloss,         // dpd_headwords.meaning_1
        string? DerivedFrom);  // dpd_headwords.derived_from — the key that regroups a scattered verb family

    /// <summary>Response for <c>GET /v1/lemma/{form}</c> — form → candidate lemmas + sandhi splits.</summary>
    public sealed record LemmaResponse(
        string Form,
        IReadOnlyList<LemmaCandidate> Candidates,
        IReadOnlyList<string> Deconstructions,  // lookup.deconstructor: ordered candidate sandhi splits
        string Note);

    /// <summary>One attested surface form of a lemma, with its corpus count (from our index; null in stub).</summary>
    public sealed record FormEntry(string Form, int? Count);

    /// <summary>Response for <c>GET /v1/forms/{lemmaId}</c> — a lemma's attested paradigm (+ optional family).</summary>
    public sealed record FormsResponse(
        long LemmaId,
        string Lemma,
        IReadOnlyList<FormEntry> Forms,            // strict paradigm of this one headword (query 3)
        IReadOnlyList<LemmaCandidate>? Family,     // derived_from family when ?family=true (query 4)
        string Note);

    /// <summary>The seam the POC fills with a real dpd.db-backed implementation.</summary>
    public interface ILemmaProvider
    {
        LemmaResponse Resolve(string form);
        FormsResponse Forms(long lemmaId, bool includeFamily);
    }
}
