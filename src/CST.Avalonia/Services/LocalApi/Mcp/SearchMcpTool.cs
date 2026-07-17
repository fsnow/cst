using System.Collections.Generic;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CST.Tools;
using ModelContextProtocol.Server;

namespace CST.Avalonia.Services.LocalApi.Mcp
{
    /// <summary>
    /// MCP tools over the app's <see cref="ISearchTool"/> — <c>search</c> (term-index) and <c>occurrences</c>
    /// (search results in context). MCP is another transport on the shared tool layer the <c>/v1</c> routes
    /// use, not a proxy. Enum params give a closed schema; descriptions are self-sufficient (no base-URL
    /// pointer, since an MCP client has none — deeper orientation is the <c>llms.txt</c> resource). (#191)
    /// </summary>
    [McpServerToolType]
    internal sealed class SearchMcpTool
    {
        [McpServerTool(Name = "search")]
        [Description("Search the Pali Tipitaka corpus's TERM INDEX for a word or pattern; returns matching "
            + "surface forms (term-forms), each with its total occurrence count and the number of books it "
            + "appears in. This is not full-text retrieval — use the 'occurrences' tool to read hits in context. "
            + "IMPORTANT: mode:Exact matches the EXACT inflected surface form, not a lemma or stem. The corpus "
            + "uses heavy sandhi, so a dictionary headword (e.g. 'satipatthana') may be rare while its inflected "
            + "forms (e.g. 'satipatthanam') are common. To find a stem's variations, use mode:Wildcard with a "
            + "trailing '*' (e.g. 'satipatthan*'). PHRASE/PROXIMITY (corpus-wide): a space-separated query "
            + "(e.g. 'ekayano ayam') finds those words co-occurring; wrap in quotes (e.g. '\"ekayano ayam\"') for "
            + "an adjacent phrase. Each match's term is the co-occurring words; pass it to 'occurrences' to read it. "
            + "TOP-LEVEL COUNTS are page-scoped (over the term-forms on THIS page): 'returnedOccurrenceCount' is a "
            + "SUM of their corpus frequencies, 'returnedBookCount' is the DISTINCT books they span (deduped, so it "
            + "is not that sum). 'hasMore' = more term-pages exist (page with skip); 'truncated' = the pattern hit "
            + "the term-expansion cap (narrow it) — two independent signals.")]
        public static async Task<SearchToolResult> SearchAsync(
            ISearchTool search,
            [Description("The word or pattern to search for, in any script (romanized Latin accepted).")]
            string query,
            [Description("How to interpret the query. Exact = exact inflected form; Wildcard = * (any run, a "
                + "prefix match) and ? (one char); Regex = .NET regex matched ANYWHERE in a term-form (substring) "
                + "— anchor with ^…$ to match the whole form, e.g. ^pañña.{0,3}$ (unanchored sweeps in compounds "
                + "and homographs like paññāsa='fifty').")]
            SearchToolMode mode = SearchToolMode.Exact,
            [Description("Page size: how many matching term-forms to return per page. No fixed ceiling; page with skip.")]
            int maxTerms = 100,
            [Description("Script for returned Pali terms.")]
            OutputScript outputScript = OutputScript.Latin,
            [Description("When true, include each term's per-book breakdown; when false, only bookCount (lighter).")]
            bool includeBooks = false,
            [Description("How many matching term-forms to skip (paging). Re-request with skip += maxTerms while hasMore is true.")]
            int skip = 0,
            [Description("Restrict the search to parts of the corpus. ALL flags default TRUE. Pitaka flags "
                + "(vinaya/sutta/abhidhamma) OR within their group; text-class flags (mula/atthakatha/tika) OR "
                + "within theirs; the two groups AND together. All-false in a group means NO constraint from that "
                + "group (not 'exclude all'), so set the flags you WANT. 'other' is separate + additive (unions the "
                + "non-canonical books). Commentaries-only = { mula:false, atthakatha:true, tika:true, other:false }; "
                + "to isolate ONLY 'other', set every other flag false. Omit for the whole corpus.")]
            ToolBookFilter? filter = null,
            [Description("For a multi-word/proximity query, the co-occurrence window in words (default 10). One "
                + "query already returns BOTH directions: 'a b' and 'b a' come back as separate forms with their "
                + "own counts, so do NOT re-query with the words swapped and sum (that double-counts).")]
            int proximityDistance = 10,
            CancellationToken ct = default)
        {
            var request = new SearchToolRequest(
                Query: query ?? string.Empty,
                Mode: mode,
                Filter: filter,
                MaxTerms: maxTerms,
                ProximityDistance: proximityDistance,
                OutputScript: McpScript.ToScript(outputScript),
                Skip: skip,
                IncludeBooks: includeBooks);
            return await search.SearchAsync(request, ct).ConfigureAwait(false);
        }

        [McpServerTool(Name = "occurrences")]
        [Description("Read a term's occurrences IN CONTEXT within one book: hit-centered snippets plus citation "
            + "refs (paragraph number + per-edition pages) and a cursor for reading the full passage. Pass a "
            + "term returned by 'search' (in the same script it came back) and a bookId from its per-book "
            + "breakdown or the 'books' tool. A space-separated multi-word term co-occurs within a proximity "
            + "window; a quoted run is an adjacent phrase. COUNTS: 'total' is the number of snippet RECORDS you "
            + "page over (Skip/Take are against it); co-located hits in one sentence merge into one record with "
            + "multiple highlights, so 'total' can be less than 'instanceTotal' (the raw hit count, which matches "
            + "search's per-book 'count'). total < instanceTotal means folded hits, NOT dropped hits. Each "
            + "occurrence's 'noteCount' is how many {…} apparatus notes (variant readings / editorial notes) fall "
            + "in its window, counted regardless of includeFootnotes — noteCount>0 means this hit HAS apparatus "
            + "(re-read with includeFootnotes:true to see it), so you needn't fetch twice to check.")]
        public static async Task<OccurrenceResult> OccurrencesAsync(
            ISearchTool search,
            [Description("The book's id (file name), e.g. from a search result's per-book breakdown or the 'books' tool.")]
            string bookId,
            [Description("The term to locate in context, in any script (e.g. a term from a prior 'search').")]
            string term,
            [Description("How to interpret the term: Exact, Wildcard (* and ?), or Regex — matched ANYWHERE in a "
                + "term-form (substring); anchor with ^…$ to match the whole form.")]
            SearchToolMode mode = SearchToolMode.Exact,
            [Description("Script for the returned snippet text.")]
            OutputScript outputScript = OutputScript.Latin,
            [Description("How many occurrences to skip (paging).")]
            int skip = 0,
            [Description("How many occurrences to return.")]
            int take = 50,
            [Description("Include the print-edition footnotes (the braced {…} apparatus — variant readings, "
                + "cross-references, editorial notes) in the snippet. ONLY the {…} apparatus is toggled — "
                + "parenthetical (…) source citations in the body text are always present; identical true/false "
                + "output means no {…} apparatus here, not 'no references'. Apparatus lives almost only in MULA "
                + "texts; commentaries carry ~none.")]
            bool includeFootnotes = false,
            [Description("For a multi-word/proximity term, the co-occurrence window in words (default 10).")]
            int proximityDistance = 10,
            [Description("Return the apparatus as DATA instead of inline braces: each snippet comes back "
                + "brace-free (clean, quotable Pāli, with the highlight offsets recomputed for it) and its notes "
                + "appear in 'notes' as { offset, text, reading, sigla } (reading/sigla filled only for a simple "
                + "'reading (sigla)' note). Use this to quote the base text and read variants separately.")]
            bool structuredNotes = false,
            CancellationToken ct = default)
        {
            var request = new OccurrenceRequest(
                BookId: bookId ?? string.Empty,
                Term: term ?? string.Empty,
                IncludeFootnotes: includeFootnotes,
                OutputScript: McpScript.ToScript(outputScript),
                Skip: skip,
                Take: take,
                Mode: mode,
                ProximityDistance: proximityDistance,
                StructuredNotes: structuredNotes);
            return await search.GetOccurrencesAsync(request, ct).ConfigureAwait(false);
        }
    }
}
