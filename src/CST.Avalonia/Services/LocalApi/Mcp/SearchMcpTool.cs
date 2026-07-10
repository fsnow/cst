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
            + "an adjacent phrase. Each match's term is the co-occurring words; pass it to 'occurrences' to read it.")]
        public static async Task<SearchToolResult> SearchAsync(
            ISearchTool search,
            [Description("The word or pattern to search for, in any script (romanized Latin accepted).")]
            string query,
            [Description("How to interpret the query. Exact = exact inflected form; Wildcard = * (any run) and ? (one char); Regex.")]
            SearchToolMode mode = SearchToolMode.Exact,
            [Description("Page size: how many matching term-forms to return per page. No fixed ceiling; page with skip.")]
            int maxTerms = 100,
            [Description("Script for returned Pali terms.")]
            OutputScript outputScript = OutputScript.Latin,
            [Description("When true, include each term's per-book breakdown; when false, only bookCount (lighter).")]
            bool includeBooks = false,
            [Description("How many matching term-forms to skip (paging). Re-request with skip += maxTerms while hasMore is true.")]
            int skip = 0,
            CancellationToken ct = default)
        {
            var request = new SearchToolRequest(
                Query: query ?? string.Empty,
                Mode: mode,
                MaxTerms: maxTerms,
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
            + "window; a quoted run is an adjacent phrase.")]
        public static async Task<IReadOnlyList<Occurrence>> OccurrencesAsync(
            ISearchTool search,
            [Description("The book's id (file name), e.g. from a search result's per-book breakdown or the 'books' tool.")]
            string bookId,
            [Description("The term to locate in context, in any script (e.g. a term from a prior 'search').")]
            string term,
            [Description("How to interpret the term: Exact, Wildcard (* and ?), or Regex (expands per word).")]
            SearchToolMode mode = SearchToolMode.Exact,
            [Description("Script for the returned snippet text.")]
            OutputScript outputScript = OutputScript.Latin,
            [Description("How many occurrences to skip (paging).")]
            int skip = 0,
            [Description("How many occurrences to return.")]
            int take = 50,
            [Description("Include apparatus/variant readings (braced) in the snippet.")]
            bool includeVariantReadings = false,
            CancellationToken ct = default)
        {
            var request = new OccurrenceRequest(
                BookId: bookId ?? string.Empty,
                Term: term ?? string.Empty,
                IncludeVariantReadings: includeVariantReadings,
                OutputScript: McpScript.ToScript(outputScript),
                Skip: skip,
                Take: take,
                Mode: mode);
            return await search.GetOccurrencesAsync(request, ct).ConfigureAwait(false);
        }
    }
}
