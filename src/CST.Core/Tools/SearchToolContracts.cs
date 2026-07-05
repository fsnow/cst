using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CST.Conversion;
using CST.Search;

namespace CST.Tools
{
    /// <summary>
    /// The search tool exposed to agents (AI_INTEGRATION.md surface C). A transport-agnostic, headless
    /// wrapper over the app's search service: an agent gets clean terms → book hits, never Lucene segments.
    /// Implementations live in the app layer and map internal results to these DTOs. All Pāli text is
    /// returned in <see cref="SearchToolRequest.OutputScript"/> (default romanized Latin); every term also
    /// carries its stable IPE form as an unambiguous cross-script key.
    /// </summary>
    public interface ISearchTool
    {
        /// <summary>Run a search and return matching terms with per-book occurrence counts.</summary>
        Task<SearchToolResult> SearchAsync(SearchToolRequest request, CancellationToken ct = default);

        /// <summary>Autocomplete: terms in the index beginning with <paramref name="prefix"/>.</summary>
        Task<IReadOnlyList<string>> CompleteTermsAsync(
            string prefix, int limit = 50, Script outputScript = Script.Latin, CancellationToken ct = default);

        /// <summary>
        /// "Search results in context" (the concordance/KWIC bridge): each occurrence of a term in one book
        /// as a hit-centered <b>snippet</b> plus citation refs (paragraph number + per-edition pages), paged.
        /// Keyed by the stable <paramref name="OccurrenceRequest.TermIpe"/> from a prior search. This is the
        /// data behind both the agent loop (scan → cite/fetch) and the human concordance panel.
        /// </summary>
        Task<IReadOnlyList<Occurrence>> GetOccurrencesAsync(OccurrenceRequest request, CancellationToken ct = default);
    }

    /// <summary>How the query text is interpreted.</summary>
    public enum SearchToolMode
    {
        /// <summary>Whole-word exact match.</summary>
        Exact,
        /// <summary>Wildcards: <c>*</c> (any run) and <c>?</c> (one char).</summary>
        Wildcard,
        /// <summary>Regular expression.</summary>
        Regex
    }

    /// <summary>Which parts of the corpus to search. Defaults include everything.</summary>
    public sealed record ToolBookFilter(
        bool Vinaya = true,
        bool Sutta = true,
        bool Abhidhamma = true,
        bool Mula = true,
        bool Atthakatha = true,
        bool Tika = true,
        bool Other = true);

    /// <summary>A search request. The query may be in any script; it is normalized to IPE internally.</summary>
    public sealed record SearchToolRequest(
        string Query,
        SearchToolMode Mode = SearchToolMode.Exact,
        ToolBookFilter? Filter = null,
        int MaxTerms = 100,
        int ProximityDistance = 10,
        Script OutputScript = Script.Latin);

    /// <summary>Search results: matching terms with per-book counts (token-frugal — positions are opt-in).</summary>
    public sealed record SearchToolResult(
        IReadOnlyList<SearchTermResult> Terms,
        int TotalTermCount,
        int TotalOccurrenceCount,
        int TotalBookCount,
        bool Truncated,
        string? Note = null);

    /// <summary>One matching term and the books it occurs in.</summary>
    /// <param name="Term">The term in the requested output script.</param>
    /// <param name="TermIpe">The stable IPE form — use this as the key for <c>GetOccurrencesAsync</c>.</param>
    public sealed record SearchTermResult(
        string Term,
        string TermIpe,
        int TotalCount,
        IReadOnlyList<BookHitSummary> Books);

    /// <summary>A term's occurrence count within one book (call <c>GetOccurrencesAsync</c> for the snippets).</summary>
    public sealed record BookHitSummary(
        string BookId,
        string BookName,
        int Count);

    /// <summary>A request for a term's in-context occurrences within one book, paged.</summary>
    /// <param name="MinChars">Prose snippet floor (rendered chars); null uses the engine default.</param>
    /// <param name="MaxChars">Prose snippet ceiling (rendered chars); null uses the engine default.</param>
    public sealed record OccurrenceRequest(
        string BookId,
        string TermIpe,
        bool IncludeVariantReadings = false,
        Script OutputScript = Script.Latin,
        int Skip = 0,
        int Take = 50,
        int? MinChars = null,
        int? MaxChars = null);

    /// <summary>Citation refs at a hit: the paragraph (with Multi sub-book code) and the page in each edition.</summary>
    public sealed record OccurrenceRefs(
        int? ParagraphNumber,
        string? ParagraphBookCode,
        IReadOnlyList<SnippetPageRef> Pages);

    /// <summary>
    /// One occurrence of a term, shown in context: a hit-centered snippet (term at
    /// <c>[HitStart, HitStart+HitLength)</c>, romanized) plus its citation refs.
    /// </summary>
    public sealed record Occurrence(
        string BookId,
        string BookName,
        string Snippet,
        int HitStart,
        int HitLength,
        OccurrenceRefs Refs,
        bool IncludedVariants);
}
