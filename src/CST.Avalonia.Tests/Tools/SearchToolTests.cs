using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CST;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Tools;
using CST.Conversion;
using CST.Navigation;
using CST.Tools;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Tools
{
    /// <summary>
    /// Unit tests for the surface-C search tool wrapper. ISearchService is mocked, so these assert the
    /// request/response mapping and script projection -- not search behavior. Term text uses ASCII placeholders
    /// with expected output computed inline (no hardcoded non-Latin). GetOccurrencesAsync is exercised
    /// end-to-end against a temp UTF-16 book file with Script.Devanagari output (identity conversion).
    /// </summary>
    public class SearchToolTests
    {
        private static ISettingsService Settings(string dir = "")
        {
            var m = new Mock<ISettingsService>();
            m.SetupGet(s => s.Settings).Returns(new Settings { XmlBooksDirectory = dir });
            return m.Object;
        }

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
                        Occurrences = new List<BookOccurrence> { new BookOccurrence { Book = book, Count = 7 } }
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

            var tool = new SearchTool(mock.Object, Settings());
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

            var tool = new SearchTool(mock.Object, Settings());
            var result = await tool.SearchAsync(new SearchToolRequest("q")); // OutputScript defaults to Latin

            Assert.Equal(1, result.TotalTermCount);
            Assert.True(result.Truncated);
            Assert.Equal("capped", result.Note);

            var term = Assert.Single(result.Terms);
            Assert.Equal(ScriptConverter.Convert("abc", Script.Ipe, Script.Latin), term.Term);

            var hit = Assert.Single(term.Books);
            Assert.Equal(book.FileName, hit.BookId);
            Assert.Equal(book.LongNavPath, hit.BookName);
            Assert.Equal(7, hit.Count);
        }

        [Fact]
        public async Task SearchAsync_null_filter_defaults_to_include_all()
        {
            SearchQuery? captured = null;
            var mock = new Mock<ISearchService>();
            mock.Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
                .Callback<SearchQuery, CancellationToken>((q, _) => captured = q)
                .ReturnsAsync(new SearchResult());

            var tool = new SearchTool(mock.Object, Settings());
            await tool.SearchAsync(new SearchToolRequest("q"));

            Assert.NotNull(captured);
            Assert.True(captured!.Filter.IncludeSutta && captured.Filter.IncludeVinaya
                        && captured.Filter.IncludeAbhidhamma && captured.Filter.IncludeMula);
        }

        [Fact]
        public async Task GetOccurrencesAsync_returns_snippet_with_citation_refs()
        {
            var dir = Path.Combine(Path.GetTempPath(), "cst-occ-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string book = "s0101m.mul.xml"; // a real catalog book, so BookName resolves
                string xml =
                    "<body><div id=\"dn1\" type=\"book\">" +
                    "<pb ed=\"V\" n=\"1.0003\"/>" +
                    "<p rend=\"bodytext\" n=\"12\">aaa bbb\u0964 ccc TARGET ddd\u0964 eee fff\u0964</p>" +
                    "</div></body>";
                await File.WriteAllTextAsync(Path.Combine(dir, book), xml, Encoding.Unicode);

                int hit = xml.IndexOf("TARGET", StringComparison.Ordinal);
                var search = new Mock<ISearchService>();
                search.Setup(s => s.GetTermPositionsAsync(book, "tgt", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<TermPosition>
                    {
                        new TermPosition { StartOffset = hit, EndOffset = hit + 6, IsFirstTerm = true, Word = "tgt" }
                    });

                var tool = new SearchTool(search.Object, Settings(dir));
                var occ = await tool.GetOccurrencesAsync(
                    new OccurrenceRequest(book, "tgt", OutputScript: Script.Devanagari, MinChars: 1));

                var o = Assert.Single(occ);
                Assert.Contains("TARGET", o.Snippet);
                Assert.Contains("ccc", o.Snippet);
                Assert.Contains("ddd", o.Snippet);
                Assert.DoesNotContain("aaa", o.Snippet);   // neighbor sentence, floor off
                Assert.Equal("TARGET", o.Snippet.Substring(o.HitStart, o.HitLength));
                Assert.Equal(12, o.Refs.ParagraphNumber);
                Assert.Equal("dn1", o.Refs.ParagraphBookCode);
                Assert.Contains(o.Refs.Pages, p => p.Edition == PageEdition.Vri && p.Volume == 1 && p.Number == 3);
                Assert.Equal(book, o.BookId);
                Assert.False(string.IsNullOrEmpty(o.BookName));
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task GetOccurrencesAsync_empty_when_no_positions()
        {
            var search = new Mock<ISearchService>();
            search.Setup(s => s.GetTermPositionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TermPosition>());

            var tool = new SearchTool(search.Object, Settings("/nonexistent"));
            var occ = await tool.GetOccurrencesAsync(new OccurrenceRequest("x.xml", "t"));

            Assert.Empty(occ);
        }
    }
}
