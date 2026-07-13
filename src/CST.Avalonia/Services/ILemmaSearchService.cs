using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Conversion;
using CST.Lemma;

namespace CST.Avalonia.Services;

/// <summary>One attested surface form of a lemma, with its corpus count from the search index.</summary>
/// <param name="Ipe">The matched index term (IPE).</param>
/// <param name="Form">The form in the requested output script.</param>
/// <param name="Count">Occurrences in the corpus.</param>
/// <param name="BookCount">Number of books the form occurs in.</param>
public sealed record LemmaSearchForm(string Ipe, string Form, int Count, int BookCount);

/// <summary>
/// Result of forward-expanding a lemma and searching the corpus for its forms. The candidate forms come
/// from DPD (synthetic paradigm); <see cref="AttestedForms"/> are the ones that actually occur (count &gt; 0),
/// so <c>CandidateFormCount - AttestedFormCount</c> is the synthetic-only remainder.
/// </summary>
public sealed record LemmaSearchResult(
    LemmaCandidate Lemma,
    IReadOnlyList<LemmaSearchForm> AttestedForms,
    int TotalOccurrences,
    int CandidateFormCount,
    int AttestedFormCount,
    bool ExpansionCapped);

/// <summary>
/// The lemma-search seam: back-lookup a word to its candidate lemmas, then forward-expand a chosen lemma
/// and search the corpus for its (attested) forms. Called in-process by the GUI and (later) the local API.
/// Degrades to "off" (<see cref="IsAvailable"/> == false, null results) when no DPD-lemma asset is present.
/// </summary>
public interface ILemmaSearchService
{
    bool IsAvailable { get; }

    DpdLemmaMeta? Meta { get; }

    /// <summary>Back-lookup: any-script word → candidate lemmas (null if unresolvable). The word is normalized to IAST for the DPD lookup.</summary>
    FormResolution? ResolveWord(string word, Script sourceScript = Script.Ipe);

    /// <summary>Forward-expand a chosen lemma (optionally its derived_from family) and search the corpus for the forms.</summary>
    Task<LemmaSearchResult?> ExpandAndSearchAsync(
        long lemmaId,
        bool includeFamily = false,
        BookFilter? filter = null,
        Script outputScript = Script.Latin,
        CancellationToken ct = default);
}
