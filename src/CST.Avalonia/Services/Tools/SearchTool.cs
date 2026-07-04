using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Conversion;
using CST.Tools;

namespace CST.Avalonia.Services.Tools
{
    /// <summary>
    /// <see cref="ISearchTool"/> over the app's <see cref="ISearchService"/> (AI_INTEGRATION.md surface C).
    /// Maps the transport-agnostic tool request/response to/from the internal search models and projects IPE
    /// terms to the requested output script. Headless — <see cref="ISearchService"/> is reference-counted and
    /// thread-safe (SRCH-6/11), so this is safe to call from a local HTTP handler.
    /// </summary>
    public sealed class SearchTool : ISearchTool
    {
        private readonly ISearchService _search;

        public SearchTool(ISearchService search) => _search = search;

        public async Task<SearchToolResult> SearchAsync(SearchToolRequest request, CancellationToken ct = default)
        {
            var query = new SearchQuery
            {
                QueryText = request.Query ?? string.Empty,
                Mode = MapMode(request.Mode),
                PageSize = request.MaxTerms,
                ProximityDistance = request.ProximityDistance,
                Filter = MapFilter(request.Filter)
                // IsPhrase / IsMultiWord are derived by SearchService from the query text.
            };

            var result = await _search.SearchAsync(query, ct).ConfigureAwait(false);

            var terms = result.Terms.Select(t => new SearchTermResult(
                Term: ScriptConverter.Convert(t.Term, Script.Ipe, request.OutputScript),
                TermIpe: t.Term,
                TotalCount: t.TotalCount,
                Books: t.Occurrences.Select(o => new BookHitSummary(
                    BookId: o.Book?.FileName ?? string.Empty,
                    BookName: o.Book?.LongNavPath ?? string.Empty,
                    Count: o.Count)).ToList())).ToList();

            return new SearchToolResult(
                Terms: terms,
                TotalTermCount: result.TotalTermCount,
                TotalOccurrenceCount: result.TotalOccurrenceCount,
                TotalBookCount: result.TotalBookCount,
                Truncated: result.ResultsTruncated,
                Note: result.TruncationMessage);
        }

        public async Task<IReadOnlyList<string>> CompleteTermsAsync(
            string prefix, int limit = 50, Script outputScript = Script.Latin, CancellationToken ct = default)
        {
            var terms = await _search.GetAllTermsAsync(prefix ?? string.Empty, limit, ct).ConfigureAwait(false);
            // GetAllTermsAsync returns terms in the app's *current display* script; re-project to the
            // requested output script (auto-detecting the input) so the tool is independent of app state.
            return terms.Select(t => ScriptConverter.Convert(t, Script.Unknown, outputScript)).ToList();
        }

        public async Task<IReadOnlyList<TermHit>> GetTermHitsAsync(
            string bookId, string termIpe, CancellationToken ct = default)
        {
            var positions = await _search.GetTermPositionsAsync(bookId, termIpe, ct).ConfigureAwait(false);
            return positions.Select(p => new TermHit(
                WordIndex: p.WordIndex,
                StartOffset: p.StartOffset,
                EndOffset: p.EndOffset,
                IsPrimaryTerm: p.IsFirstTerm,
                WordIpe: p.Word)).ToList();
        }

        private static SearchMode MapMode(SearchToolMode mode) => mode switch
        {
            SearchToolMode.Exact => SearchMode.Exact,
            SearchToolMode.Wildcard => SearchMode.Wildcard,
            SearchToolMode.Regex => SearchMode.Regex,
            _ => SearchMode.Exact
        };

        private static BookFilter MapFilter(ToolBookFilter? f)
        {
            if (f == null) return new BookFilter();
            return new BookFilter
            {
                IncludeVinaya = f.Vinaya,
                IncludeSutta = f.Sutta,
                IncludeAbhidhamma = f.Abhidhamma,
                IncludeMula = f.Mula,
                IncludeAttha = f.Atthakatha,
                IncludeTika = f.Tika,
                IncludeOther = f.Other
            };
        }
    }
}
