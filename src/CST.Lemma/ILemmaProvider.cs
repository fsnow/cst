namespace CST.Lemma;

/// <summary>
/// Read access to a DPD-lemma asset (dpd-lemma.db): the two hops of lemma search —
/// <see cref="ResolveForm"/> (a surface form → its candidate lemmas) and
/// <see cref="ExpandLemma"/> (a lemma → its attested surface forms). Corpus-agnostic: no occurrence
/// counts (those come from the search index). Forms are IAST; callers convert to IPE to search.
/// A provider over a missing/unreadable asset reports <see cref="IsAvailable"/> == false and returns
/// null from every query, so the feature degrades gracefully to "off".
/// </summary>
public interface ILemmaProvider : IDisposable
{
    /// <summary>True when a usable DPD-lemma asset was opened.</summary>
    bool IsAvailable { get; }

    /// <summary>Asset provenance/version, or null when unavailable.</summary>
    DpdLemmaMeta? Meta { get; }

    /// <summary>Back-lookup: a surface form (IAST) → candidate lemmas. Null if the form isn't resolvable.</summary>
    FormResolution? ResolveForm(string form);

    /// <summary>Forward expansion: a lemma id → its attested surface forms (+ derived_from family when asked). Null if the id is unknown.</summary>
    LemmaExpansion? ExpandLemma(long lemmaId, bool includeFamily = false);

    /// <summary>Sandhi/compound deconstruction: a surface form (IAST) → its DPD deconstructor split(s) and any
    /// DIRECT lemma(s). Unlike <see cref="ResolveForm"/> this does NOT short-circuit when the form has no direct
    /// lemma, so pure-sandhi words (a split with no headword) are returned. Null when the asset carries no
    /// deconstructor column, or the form has neither a split nor a direct lemma. (sandhi decomposer, #383)</summary>
    FormDeconstruction? Deconstruct(string form);

    /// <summary>A single lemma's metadata, or null if the id is unknown.</summary>
    LemmaCandidate? GetLemma(long lemmaId);

    /// <summary>Report-grade detail (etymology/example/frequency/root) for one lemma; null if the id is unknown. Enriched fields are null on a non-report asset.</summary>
    LemmaDetail? GetDetail(long lemmaId);
}
