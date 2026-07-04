using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CST;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Tools;
using CST.Conversion;
using CST.Tools;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Tools
{
    /// <summary>
    /// Unit tests for the surface-C search tool wrapper. The underlying ISearchService is mocked, so these
    /// assert the request/response mapping and script projection — not search behavior itself. Term text uses
    /// ASCII placeholders and expected script output is computed inline via ScriptConverter (no hardcoded
    /// non-Latin in source).
    /// </summary>
    public class SearchToolTests
    {
        private static SearchResult OneTermResult(out Book book)
        {
            book = new Book { FileName = "s0101m.mul.xml", LongNavPath = "Sutta > Digha > Silakkhandhavagga" };
            return new SearchResult
            {
                Terms = new List<MatchingTerm>
                {
                    new MatchingTerm
                    {
                        Term = "abc",          // stand-in IPE term (ASCII placeholder)
                        TotalCount = 7,
                        Occurrences = new List<BookOccurrence>
                        {
                            new BookOccurrence { Book = book, Count = 7 }
                        }
                    }
                },
                TotalTermCount = 1,
                TotalOccurrenceCount = 7,
                TotalBookCount = 1,
                ResultsTruncated = true,
                TruncationMessage = "capped"
            };
        }

        [Fact]
        public async Task SearchAsync_maps_request_to_query()
        {
            SearchQuery? captured = null;
            var mock = new Mock<ISearchService>();
            mock.Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
                .Callback<SearchQuery, CancellationToken>((q, _) => captured = q)
                .ReturnsAsync(OneTermResult(out _));

            var tool = new SearchTool(mock.Object);
            await tool.SearchAsync(new SearchToolRequest(
                "dhamma", SearchToolMode.Wildcard,
                new ToolBookFilter(Sutta: true, Abhidhamma: false),
                MaxTerms: 42, ProximityDistance: 3));

            Assert.NotNull(captured);
            Assert.Equal("dhamma", captured!.QueryText);
            Assert.Equal(SearchMode.Wildcard, captured.Mode);
            Assert.Equal(42, captured.PageSize);
            Assert.Equal(3, captured.ProximityDistance);
            Assert.True(captured.Filter.IncludeSutta);
            Assert.False(captured.Filter.IncludeAbhidhamma);
        }

        [Fact]
        public async Task SearchAsync_maps_result_and_projects_term_script()
        {
            var mock = new Mock<ISearchService>();
            mock.Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OneTermResult(out var book));

            var tool = new SearchTool(mock.Object);
            var result = await tool.SearchAsync(new SearchToolRequest("q")); // OutputScript defaults to Latin

            Assert.Equal(1, result.TotalTermCount);
            Assert.Equal(7, result.TotalOccurrenceCount);
            Assert.True(result.Truncated);
            Assert.Equal("capped", result.Note);

            var term = Assert.Single(result.Terms);
            Assert.Equal("abc", term.TermIpe);                                   // stable IPE preserved
            Assert.Equal(ScriptConverter.Convert("abc", Script.Ipe, Script.Latin), term.Term); // projected
            Assert.Equal(7, term.TotalCount);

            var hit = Assert.Single(term.Books);
            Assert.Equal(book.FileName, hit.BookId);
            Assert.Equal(book.LongNavPath, hit.BookName);
            Assert.Equal(7, hit.Count);
        }

        [Fact]
        public async Task GetTermHitsAsync_maps_positions()
        {
            var mock = new Mock<ISearchService>();
            mock.Setup(s => s.GetTermPositionsAsync("b.xml", "abc", It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TermPosition>
                {
                    new TermPosition { WordIndex = 2, StartOffset = 10, EndOffset = 15, IsFirstTerm = true, Word = "abc" }
                });

            var tool = new SearchTool(mock.Object);
            var hits = await tool.GetTermHitsAsync("b.xml", "abc");

            var h = Assert.Single(hits);
            Assert.Equal(2, h.WordIndex);
            Assert.Equal(10, h.StartOffset);
            Assert.Equal(15, h.EndOffset);
            Assert.True(h.IsPrimaryTerm);
            Assert.Equal("abc", h.WordIpe);
        }

        [Fact]
        public async Task SearchAsync_null_filter_defaults_to_include_all()
        {
            SearchQuery? captured = null;
            var mock = new Mock<ISearchService>();
            mock.Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
                .Callback<SearchQuery, CancellationToken>((q, _) => captured = q)
                .ReturnsAsync(new SearchResult());

            var tool = new SearchTool(mock.Object);
            await tool.SearchAsync(new SearchToolRequest("q"));

            Assert.NotNull(captured);
            Assert.True(captured!.Filter.IncludeSutta && captured.Filter.IncludeVinaya
                        && captured.Filter.IncludeAbhidhamma && captured.Filter.IncludeMula);
        }
    }
}
