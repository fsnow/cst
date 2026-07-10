using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Search;
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
                Skip = request.Skip,
                // No per-book breakdown requested => let the engine take counts straight from the index (no
                // postings) when the search is also unfiltered.
                CountsOnly = !request.IncludeBooks,
                ProximityDistance = request.ProximityDistance,
                Filter = MapFilter(request.Filter)
                // IsPhrase / IsMultiWord are derived by SearchService from the query text.
            };

            var result = await _search.SearchAsync(query, ct).ConfigureAwait(false);

            var terms = result.Terms.Select(t => new SearchTermResult(
                Term: ScriptConverter.Convert(t.Term, Script.Ipe, request.OutputScript),
                TotalCount: t.TotalCount,
                // Per-book breakdown is opt-in (it's the payload weight); bookCount is always cheap to include.
                Books: request.IncludeBooks
                    ? t.Occurrences.Select(o => new BookHitSummary(
                        BookId: o.Book?.FileName ?? string.Empty,
                        // Nav paths are stored Devanagari; romanize to the requested script like everything else. (#186 cold test)
                        BookName: ScriptConverter.Convert(o.Book?.LongNavPath ?? string.Empty, Script.Devanagari, request.OutputScript),
                        Count: o.Count)).ToList()
                    : (IReadOnlyList<BookHitSummary>)Array.Empty<BookHitSummary>(),
                // Counts-only path sets BookCount from the index (Occurrences empty); the postings path leaves
                // BookCount 0, so fall back to the per-book count it computed.
                BookCount: t.BookCount > 0 ? t.BookCount : t.Occurrences.Count)).ToList();

            // Guard the silent-empty footgun: '*'/'?' are literal outside Wildcard mode, so an Exact query
            // containing them matches no term and returns [] with no hint. Surface it in the note. (#186 cold test)
            var q = request.Query ?? string.Empty;
            string? modeNote = request.Mode == SearchToolMode.Exact && (q.Contains('*') || q.Contains('?'))
                ? "Query contains wildcard characters ('*'/'?') but mode is Exact, so they were matched literally "
                  + "(and match no term). Set mode:\"Wildcard\" to expand them."
                : null;

            return new SearchToolResult(
                Terms: terms,
                TotalTermCount: result.TotalTermCount,
                TotalOccurrenceCount: result.TotalOccurrenceCount,
                // The counts-only fast path (IncludeBooks=false) doesn't enumerate books, so its distinct-book
                // union is 0 — report null instead of a misleading 0. (Desktop MCP friction report)
                TotalBookCount: request.IncludeBooks ? result.TotalBookCount : (int?)null,
                // `truncated` means ONLY that the pattern overflowed the expansion cap (narrow it) — a normal
                // large result is `hasMore:true, truncated:false` (page it). Don't leak the UI's page-cap
                // "refine your search" message as the note; only the genuine cap message.
                Truncated: result.ExpansionCapped,
                HasMore: result.HasMore,
                Note: modeNote ?? (result.ExpansionCapped ? result.TruncationMessage : null));
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
            var dir = _settings.Settings?.XmlBooksDirectory;
            if (string.IsNullOrEmpty(dir)) return Array.Empty<Occurrence>();
            var path = Path.Combine(dir, request.BookId);
            if (!File.Exists(path)) return Array.Empty<Occurrence>();

            // Route to the EXPANDING path (GetMultiWordPositionsAsync) for anything that needs term expansion or
            // co-occurrence: a phrase, 2+ units, OR a single Wildcard/Regex word — which must be EXPANDED, not
            // looked up literally (a literal `avijj*` lookup matches no index term and silently returns []). Only
            // a single EXACT word takes the fast literal single-term path. (AI_INTEGRATION.md §6.1)
            var mode = MapMode(request.Mode);
            var units = MultiWordSearch.ParseUnits(request.Term ?? string.Empty);
            bool multiUnit = units.Count > 1 || (units.Count == 1 && units[0].IsPhrase);

            List<List<SnippetMark>> perOccurrence;
            if (multiUnit || mode != SearchMode.Exact)
            {
                var hits = await _search.GetMultiWordPositionsAsync(
                    request.BookId, request.Term ?? string.Empty, mode, request.ProximityDistance, ct).ConfigureAwait(false);
                perOccurrence = hits
                    .Select(h => h.Select(tp => new SnippetMark(tp.StartOffset, tp.EndOffset, tp.IsFirstTerm)).ToList())
                    .OrderBy(AnchorStart)
                    .ToList();
            }
            else
            {
                var positions = await _search.GetTermPositionsAsync(request.BookId, request.Term ?? string.Empty, ct).ConfigureAwait(false);
                perOccurrence = positions
                    .OrderBy(p => p.StartOffset)
                    .Select(p => new List<SnippetMark> { new SnippetMark(p.StartOffset, p.EndOffset, true) })
                    .ToList();
            }
            if (perOccurrence.Count == 0) return Array.Empty<Occurrence>();

            // Char offsets index the decoded (BOM-stripped) UTF-16 text — read it the same way.
            string xml = await File.ReadAllTextAsync(path, Encoding.Unicode, ct).ConfigureAwait(false);
            var markers = BookMarkers.Build(xml);
            var opts = new SnippetOptions(
                OutputScript: request.OutputScript,
                IncludeVariantReadings: request.IncludeVariantReadings,
                MinChars: request.MinChars ?? 60,
                MaxChars: request.MaxChars ?? 320);

            string rawBookName = Books.Inst
                .FirstOrDefault(b => string.Equals(b.FileName, request.BookId, StringComparison.OrdinalIgnoreCase))
                ?.LongNavPath ?? request.BookId;
            // Nav paths are stored Devanagari; romanize to the requested script. (#186 cold test)
            string bookName = ScriptConverter.Convert(rawBookName, Script.Devanagari, request.OutputScript);

            var occurrences = new List<Occurrence>();
            foreach (var marks in perOccurrence.Skip(request.Skip).Take(request.Take))
            {
                ct.ThrowIfCancellationRequested();
                var s = TeiSnippetExtractor.Extract(xml, marks, markers, opts);
                occurrences.Add(new Occurrence(
                    BookId: request.BookId,
                    BookName: bookName,
                    Snippet: s.Snippet,
                    HitStart: s.HitStart,
                    HitLength: s.HitLength,
                    Refs: new OccurrenceRefs(s.ParagraphNumber, s.ParagraphBookCode, s.Pages),
                    IncludedVariants: s.IncludedVariants,
                    // The anchor's char offset — a unique locator to read the exact passage via /v1/passage
                    // (paragraph numbers repeat within a book). (#186 cold test)
                    Cursor: AnchorStart(marks),
                    Highlights: s.Highlights
                        .Select(h => new OccurrenceHighlight(h.Start, h.Length, h.IsAnchor)).ToList()));
            }
            return occurrences;

            static int AnchorStart(List<SnippetMark> m)
            {
                var a = m.FirstOrDefault(x => x.IsAnchor);
                return a?.Start ?? (m.Count > 0 ? m[0].Start : 0);
            }
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
