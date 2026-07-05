using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Conversion;
using CST.Search;
using CST.Tools;

namespace CST.Avalonia.Services.Tools
{
    /// <summary>
    /// <see cref="ISearchTool"/> over the app's <see cref="ISearchService"/> (AI_INTEGRATION.md surface C).
    /// Maps the transport-agnostic tool request/response to/from the internal search models and projects IPE
    /// terms to the requested output script. <see cref="GetOccurrencesAsync"/> adds "search results in
    /// context" — snippets + citation refs — by reading the book XML and running the snippet engine. Headless —
    /// <see cref="ISearchService"/> is reference-counted and thread-safe (SRCH-6/11), safe from an HTTP handler.
    /// </summary>
    public sealed class SearchTool : ISearchTool
    {
        private readonly ISearchService _search;
        private readonly ISettingsService _settings;

        public SearchTool(ISearchService search, ISettingsService settings)
        {
            _search = search;
            _settings = settings;
        }

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

        public async Task<IReadOnlyList<Occurrence>> GetOccurrencesAsync(
            OccurrenceRequest request, CancellationToken ct = default)
        {
            var positions = await _search.GetTermPositionsAsync(request.BookId, request.Term, ct)
                .ConfigureAwait(false);
            if (positions.Count == 0) return Array.Empty<Occurrence>();

            var dir = _settings.Settings?.XmlBooksDirectory;
            if (string.IsNullOrEmpty(dir)) return Array.Empty<Occurrence>();
            var path = Path.Combine(dir, request.BookId);
            if (!File.Exists(path)) return Array.Empty<Occurrence>();

            // Char offsets index the decoded (BOM-stripped) UTF-16 text — read it the same way.
            string xml = await File.ReadAllTextAsync(path, Encoding.Unicode, ct).ConfigureAwait(false);
            var markers = BookMarkers.Build(xml);
            var opts = new SnippetOptions(
                OutputScript: request.OutputScript,
                IncludeVariantReadings: request.IncludeVariantReadings,
                MinChars: request.MinChars ?? 60,
                MaxChars: request.MaxChars ?? 320);

            string bookName = Books.Inst
                .FirstOrDefault(b => string.Equals(b.FileName, request.BookId, StringComparison.OrdinalIgnoreCase))
                ?.LongNavPath ?? request.BookId;

            var occurrences = new List<Occurrence>();
            foreach (var p in positions.OrderBy(p => p.StartOffset).Skip(request.Skip).Take(request.Take))
            {
                ct.ThrowIfCancellationRequested();
                var s = TeiSnippetExtractor.Extract(xml, p.StartOffset, p.EndOffset - p.StartOffset, markers, opts);
                occurrences.Add(new Occurrence(
                    BookId: request.BookId,
                    BookName: bookName,
                    Snippet: s.Snippet,
                    HitStart: s.HitStart,
                    HitLength: s.HitLength,
                    Refs: new OccurrenceRefs(s.ParagraphNumber, s.ParagraphBookCode, s.Pages),
                    IncludedVariants: s.IncludedVariants));
            }
            return occurrences;
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
