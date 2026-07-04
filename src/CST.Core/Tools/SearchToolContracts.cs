using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CST.Conversion;

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
        /// Opt-in, heavier: the individual hit positions of a term within one book (for highlighting or
        /// offset-level work). Keyed by the stable <paramref name="termIpe"/> from a prior search.
        /// </summary>
        Task<IReadOnlyList<TermHit>> GetTermHitsAsync(
            string bookId, string termIpe, CancellationToken ct = default);
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
    /// <param name="TermIpe">The stable IPE form — use this as the key for <c>GetTermHitsAsync</c>.</param>
    public sealed record SearchTermResult(
        string Term,
        string TermIpe,
        int TotalCount,
        IReadOnlyList<BookHitSummary> Books);

    /// <summary>A term's occurrence count within one book (no positions — call <c>GetTermHitsAsync</c> for those).</summary>
    public sealed record BookHitSummary(
        string BookId,
        string BookName,
        int Count);

    /// <summary>A single hit position of a term within a book.</summary>
    /// <param name="IsPrimaryTerm">True for the main term; false for context words (two-color highlighting).</param>
    public sealed record TermHit(
        int WordIndex,
        int StartOffset,
        int EndOffset,
        bool IsPrimaryTerm,
        string? WordIpe);
}
