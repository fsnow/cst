using System.Collections.Generic;
using System.Text.Json.Serialization;
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
    /// returned in <see cref="SearchToolRequest.OutputScript"/> (default romanized Latin); a term returned by
    /// search is passed back verbatim (in that same script) to read it in context — the caller never handles
    /// the internal encoding.
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
        /// Pass the <c>Term</c> from a prior search (in whatever script it came back). This is the data behind
        /// both the agent loop (scan → cite/fetch) and the human concordance panel.
        /// </summary>
        Task<OccurrenceResult> GetOccurrencesAsync(OccurrenceRequest request, CancellationToken ct = default);
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

    /// <summary>
    /// Which parts of the corpus to search. Defaults include everything. The pitaka flags
    /// (<see cref="Vinaya"/>/<see cref="Sutta"/>/<see cref="Abhidhamma"/>) OR within their group, the text-class
    /// flags (<see cref="Mula"/>/<see cref="Atthakatha"/>/<see cref="Tika"/>) OR within theirs, and the two groups
    /// AND together — so e.g. commentaries-only is <c>{ mula:false, atthakatha:true, tika:true }</c>.
    /// <c>Disallow</c> unmapped members so a misnamed key (e.g. a guessed <c>commentaryLevel</c>) fails with a
    /// 400 rather than binding to all-defaults and silently returning the unfiltered corpus. (#186 cold test)
    /// </summary>
    [JsonUnmappedMemberHandling(JsonUnmappedMemberHandling.Disallow)]
    public sealed record ToolBookFilter(
        bool Vinaya = true,
        bool Sutta = true,
        bool Abhidhamma = true,
        bool Mula = true,
        bool Atthakatha = true,
        bool Tika = true,
        bool Other = true);

    /// <summary>A search request. The query may be in any script; it is normalized to IPE internally.</summary>
    /// <param name="MaxTerms">Page size: how many matching term-forms to return.</param>
    /// <param name="Skip">How many matching term-forms to skip before this page (0 = first page). Page by
    /// re-requesting with <c>Skip += MaxTerms</c> while the result's <c>HasMore</c> is true.</param>
    /// <param name="IncludeBooks">When true, each term carries its full per-book <c>books[]</c> breakdown;
    /// when false (default) it carries only <c>bookCount</c> — much lighter. Ask for books only when you need them.</param>
    public sealed record SearchToolRequest(
        string Query,
        SearchToolMode Mode = SearchToolMode.Exact,
        ToolBookFilter? Filter = null,
        int MaxTerms = 100,
        int ProximityDistance = 10,
        Script OutputScript = Script.Latin,
        int Skip = 0,
        bool IncludeBooks = false);

    /// <summary>Search results: matching terms (token-frugal — per-book breakdown and positions are opt-in).</summary>
    /// <param name="ReturnedTermCount">How many matching term-forms are IN THIS PAGE (= <c>Terms.Count</c>), NOT
    /// the corpus-wide match count. Use <c>HasMore</c> to page; there is no cheap whole-result term total.</param>
    /// <param name="ReturnedOccurrenceCount">Sum of <c>TotalCount</c> over the terms IN THIS PAGE (not the whole
    /// result set). Per-term <c>TotalCount</c> is corpus-wide; this page sum is not.</param>
    /// <param name="ReturnedBookCount">Distinct books over the terms IN THIS PAGE. <b>Null when
    /// <c>IncludeBooks</c> is false</b> — the counts-only fast path does not enumerate books, so the union is
    /// unavailable and reported as null (not a misleading 0). Per-term <c>BookCount</c> is always present.</param>
    /// <param name="HasMore">True if at least one more matching term exists after this page — request the next
    /// page with <c>Skip += MaxTerms</c>. This, not the page length, is the paging signal.</param>
    /// <param name="Truncated">The pattern overflowed the engine's expansion limit (very broad wildcard/regex);
    /// distinct from <see cref="HasMore"/>.</param>
    public sealed record SearchToolResult(
        IReadOnlyList<SearchTermResult> Terms,
        int ReturnedTermCount,
        int ReturnedOccurrenceCount,
        int? ReturnedBookCount,
        bool Truncated,
        bool HasMore = false,
        string? Note = null);

    /// <summary>One matching term and the books it occurs in.</summary>
    /// <param name="Term">The term in the requested output script — pass it back verbatim as
    /// <c>OccurrenceRequest.Term</c> to read it in context.</param>
    /// <param name="BookCount">How many books this term occurs in (always present — the spread, without the list).</param>
    /// <param name="Books">Per-book breakdown; <b>null</b> unless <c>SearchToolRequest.IncludeBooks</c> was true
    /// (null = "not requested", unambiguous vs. an empty result).</param>
    public sealed record SearchTermResult(
        string Term,
        int TotalCount,
        IReadOnlyList<BookHitSummary>? Books,
        int BookCount = 0);

    /// <summary>A term's occurrence count within one book (call <c>GetOccurrencesAsync</c> for the snippets).</summary>
    public sealed record BookHitSummary(
        string BookId,
        string BookName,
        int Count);

    /// <summary>A request for a term's (or a multi-word/proximity query's) in-context occurrences within one book, paged.</summary>
    /// <param name="Term">The query to locate, in any script. A single word (e.g. the romanized <c>Term</c> from a
    /// prior search), OR a multi-word/proximity query — space-separated words co-occur within
    /// <see cref="ProximityDistance"/>, a quoted run is an adjacent phrase. This is how a proximity hit is read in
    /// context (its <c>~</c>-joined search term does not round-trip). Converted to the internal encoding for lookup.</param>
    /// <param name="Mode">How to interpret <see cref="Term"/> — Exact / Wildcard / Regex (wildcard and regex expand per word).</param>
    /// <param name="ProximityDistance">For a multi-word <see cref="Term"/>, the co-occurrence window in words.</param>
    /// <param name="MinChars">Prose snippet floor (rendered chars); null uses the engine default.</param>
    /// <param name="MaxChars">Prose snippet ceiling (rendered chars); null uses the engine default.</param>
    public sealed record OccurrenceRequest(
        string BookId,
        string Term,
        bool IncludeVariantReadings = false,
        Script OutputScript = Script.Latin,
        int Skip = 0,
        int Take = 50,
        int? MinChars = null,
        int? MaxChars = null,
        SearchToolMode Mode = SearchToolMode.Exact,
        int ProximityDistance = 10);

    /// <summary>Citation refs at a hit: the paragraph (with Multi sub-book code) and the page in each edition.</summary>
    public sealed record OccurrenceRefs(
        int? ParagraphNumber,
        string? ParagraphBookCode,
        IReadOnlyList<SnippetPageRef> Pages);

    /// <summary>One marked span within a snippet, in snippet-local char offsets: <c>[Start, Start+Length)</c>.
    /// A single-word hit has one; a proximity/phrase hit has one per matched word, exactly one with
    /// <see cref="IsAnchor"/> (the navigable word — the others are the co-occurring context).</summary>
    public sealed record OccurrenceHighlight(int Start, int Length, bool IsAnchor);

    /// <summary>
    /// One occurrence shown in context: a hit-centered snippet plus its citation refs. For a single term the
    /// snippet marks one span; for a proximity/phrase hit it marks every co-occurring word, and
    /// <see cref="Highlights"/> gives all of them (with the one anchor flagged). <see cref="HitStart"/>/
    /// <see cref="HitLength"/> mirror the anchor for continuity.
    /// </summary>
    /// <param name="Cursor">The anchor's unique locator — pass it to <c>/v1/passage</c> as <c>cursor</c> to read
    /// the exact passage around <em>this</em> occurrence. (Paragraph numbers repeat within a book, so they are
    /// not a unique locator.)</param>
    public sealed record Occurrence(
        string BookId,
        string BookName,
        string Snippet,
        int HitStart,
        int HitLength,
        OccurrenceRefs Refs,
        bool IncludedVariants,
        int Cursor,
        IReadOnlyList<OccurrenceHighlight> Highlights);

    /// <summary>
    /// A page of a term's in-context occurrences within one book — the same envelope shape as
    /// <see cref="SearchToolResult"/>, so the tool family is consistent and an agent isn't left paging blind
    /// over a bare array. (Desktop MCP friction report)
    /// </summary>
    /// <param name="Occurrences">This page of occurrences (after <c>Skip</c>/<c>Take</c>).</param>
    /// <param name="ReturnedCount">How many occurrences are in this page (= <c>Occurrences.Count</c>).</param>
    /// <param name="Total">Total occurrences of the term in this book (before paging) — book-scoped.</param>
    /// <param name="HasMore">True if more occurrences remain after this page; request the next with
    /// <c>Skip += Take</c>.</param>
    public sealed record OccurrenceResult(
        IReadOnlyList<Occurrence> Occurrences,
        int ReturnedCount,
        int Total,
        bool HasMore);
}
