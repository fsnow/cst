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

    /// <summary>A single lemma's metadata, or null if the id is unknown.</summary>
    LemmaCandidate? GetLemma(long lemmaId);
}
