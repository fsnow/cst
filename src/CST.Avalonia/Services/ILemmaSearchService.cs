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

/// <summary>One alternative sandhi/compound split of a word: its constituent parts (IAST) and DPD's rank
/// (0 = best-ranked). The splits are ALTERNATIVES, not sequential pieces — Pāli sandhi is ambiguous, so DPD
/// returns several ranked candidate analyses. (sandhi decomposer, #383)</summary>
public sealed record WordSplit(int Rank, IReadOnlyList<string> Parts);

/// <summary>
/// Result of deconstructing a compound/sandhi word: DPD's ranked alternative <see cref="Splits"/>, plus any
/// <see cref="DirectLemmas"/> the whole word is itself a headword of (fall-through — a compound like
/// <c>sammāsambuddho</c> is stored as a lemma, not a split). Parts/lemmas are IAST; the caller converts to the
/// output script. <see cref="Scope"/> is the loaded asset's scope (only a <c>full</c> asset carries every
/// compound's split; <c>mid</c> carries only enclitics), for a scope-aware caller note.
/// </summary>
public sealed record WordDeconstruction(
    string Word,
    IReadOnlyList<LemmaCandidate> DirectLemmas,
    IReadOnlyList<WordSplit> Splits,
    string? Scope);

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

    /// <summary>Sandhi/compound deconstruction: any-script word → DPD's ranked alternative splits (parts as
    /// surface forms) + any direct lemma(s) the word is itself a headword of. Null when unresolvable or the
    /// asset carries no deconstructor. The word is normalized to IAST for the lookup; parts stay IAST for the
    /// caller to convert. This is the word→parts primitive only; resolve each part with <see cref="ResolveWord"/>
    /// for its lemma(s). (sandhi decomposer, #383)</summary>
    WordDeconstruction? Deconstruct(string word, Script sourceScript = Script.Ipe);

    /// <summary>Forward-expand a chosen lemma (optionally its derived_from family) and search the corpus for the forms.</summary>
    Task<LemmaSearchResult?> ExpandAndSearchAsync(
        long lemmaId,
        bool includeFamily = false,
        BookFilter? filter = null,
        Script outputScript = Script.Latin,
        CancellationToken ct = default);

    /// <summary>Expand and search the DE-DUPLICATED UNION of several lemmas' forms in ONE query — used to count
    /// a collapsed family row (the homonyms of one headword) without double-counting their shared forms. (#247)</summary>
    Task<LemmaSearchResult?> ExpandAndSearchSetAsync(
        IReadOnlyList<long> lemmaIds,
        Script outputScript = Script.Latin,
        CancellationToken ct = default);
}
