using System;
using System.ComponentModel;
using System.Threading;
using System.Threading.Tasks;
using CST.Conversion;
using CST.Tools;
using ModelContextProtocol.Server;

namespace CST.Avalonia.Services.LocalApi.Mcp
{
    /// <summary>
    /// Increment 1 of the MCP surface (#191): ONE tool — <c>search</c> — exposed over the in-app Streamable
    /// HTTP transport at <c>/mcp</c>, so a BYO-MCP chat client (Claude Desktop via the <c>mcp-remote</c> bridge)
    /// can be test-connected before the full read set is built out. The MCP surface is deliberately thin: it
    /// wraps the same <see cref="ISearchTool"/> the <c>/v1</c> HTTP routes use — MCP is just another transport on
    /// the shared tool layer, not a proxy — and its tool descriptions point at the served <c>/llms.txt</c> for
    /// domain context rather than duplicating the contract. (AI_INTEGRATION.md §7)
    /// </summary>
    [McpServerToolType]
    internal sealed class SearchMcpTool
    {
        [McpServerTool(Name = "search")]
        [Description("Search the Pali Tipitaka corpus for a word or pattern; returns matching term-forms with "
            + "per-book occurrence counts. Fetch /llms.txt from this same server for query modes, scripts, "
            + "corpus filtering, and paging.")]
        public static async Task<SearchToolResult> SearchAsync(
            ISearchTool search,
            [Description("The word or pattern to search for, in any script (romanized Latin accepted).")]
            string query,
            [Description("How to interpret the query: Exact, Wildcard (* and ?), or Regex.")]
            string mode = "Exact",
            [Description("Page size: how many matching term-forms to return.")]
            int maxTerms = 100,
            [Description("Script for returned Pali terms (e.g. Latin, Devanagari, Thai). Default Latin.")]
            string outputScript = "Latin",
            [Description("When true, include each term's per-book breakdown; when false, only bookCount.")]
            bool includeBooks = false,
            [Description("How many matching term-forms to skip (paging). Re-request with skip += maxTerms while hasMore is true.")]
            int skip = 0,
            CancellationToken ct = default)
        {
            var request = new SearchToolRequest(
                Query: query ?? string.Empty,
                Mode: ParseMode(mode),
                MaxTerms: maxTerms,
                OutputScript: ParseScript(outputScript),
                Skip: skip,
                IncludeBooks: includeBooks);
            return await search.SearchAsync(request, ct).ConfigureAwait(false);
        }

        private static SearchToolMode ParseMode(string? mode) =>
            Enum.TryParse<SearchToolMode>(mode, ignoreCase: true, out var m) ? m : SearchToolMode.Exact;

        // Never expose the internal IPE font encoding or Unknown; fall back to Latin like the HTTP surface.
        private static Script ParseScript(string? name) =>
            Enum.TryParse<Script>(name, ignoreCase: true, out var s)
                && s is not (Script.Ipe or Script.Unknown) ? s : Script.Latin;
    }
}
