using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
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
            // Nav path stored in Devanagari (as the real catalog is), to verify bookName is romanized on output.
            book = new Book { FileName = "s0101m.mul.xml", LongNavPath = "\u0938\u0941\u0924\u094d\u0924" };
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
                ExpansionCapped = true,   // agent-facing `truncated` maps from ExpansionCapped, not the page cap
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
                MaxTerms: 42, ProximityDistance: 3, Skip: 7));

            Assert.NotNull(captured);
            Assert.Equal("dhamma", captured!.QueryText);
            Assert.Equal(SearchMode.Wildcard, captured.Mode);
            Assert.Equal(42, captured.PageSize);
            Assert.Equal(7, captured.Skip);
            Assert.Equal(3, captured.ProximityDistance);
            Assert.True(captured.Filter.IncludeSutta);
            Assert.False(captured.Filter.IncludeAbhidhamma);
        }

        [Fact]
        public async Task SearchAsync_books_opt_in_bookCount_and_hasMore_always_present()
        {
            var book1 = new Book { FileName = "s0101m.mul.xml", LongNavPath = "\u0938" };
            var book2 = new Book { FileName = "s0102m.mul.xml", LongNavPath = "\u0938" };
            var searchResult = new SearchResult
            {
                Terms = new List<MatchingTerm>
                {
                    new MatchingTerm
                    {
                        Term = "abc", TotalCount = 10,
                        Occurrences = new List<BookOccurrence>
                        {
                            new BookOccurrence { Book = book1, Count = 6 },
                            new BookOccurrence { Book = book2, Count = 4 },
                        }
                    }
                },
                TotalTermCount = 1, TotalOccurrenceCount = 10, TotalBookCount = 2,
                HasMore = true
            };
            var mock = new Mock<ISearchService>();
            mock.Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(searchResult);
            var tool = new SearchTool(mock.Object, Settings());

            // Default: the per-book breakdown is omitted, but bookCount and hasMore are always present.
            var lean = await tool.SearchAsync(new SearchToolRequest("q"));
            var t = Assert.Single(lean.Terms);
            Assert.Null(t.Books);                      // null (not []) when the per-book breakdown wasn't requested
            Assert.Equal(2, t.BookCount);
            Assert.True(lean.HasMore);

            // Opt in: the full per-book breakdown comes back.
            var full = await tool.SearchAsync(new SearchToolRequest("q", IncludeBooks: true));
            Assert.Equal(2, Assert.Single(full.Terms).Books.Count);
        }

        [Fact]
        public async Task SearchAsync_maps_result_and_projects_term_script()
        {
            var mock = new Mock<ISearchService>();
            mock.Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(OneTermResult(out var book));

            var tool = new SearchTool(mock.Object, Settings());
            // OutputScript defaults to Latin; opt into the per-book breakdown to verify bookName romanization.
            var result = await tool.SearchAsync(new SearchToolRequest("q", IncludeBooks: true));

            Assert.Equal(1, result.ReturnedTermCount);
            Assert.True(result.Truncated);
            Assert.Equal("capped", result.Note);

            var term = Assert.Single(result.Terms);
            Assert.Equal(ScriptConverter.Convert("abc", Script.Ipe, Script.Latin), term.Term);

            var hit = Assert.Single(term.Books);
            Assert.Equal(book.FileName, hit.BookId);
            Assert.False(string.IsNullOrEmpty(hit.BookName));
            Assert.DoesNotContain(hit.BookName, c => c >= '\u0900' && c <= '\u097F'); // bookName romanized, not Devanagari
            Assert.Equal(7, hit.Count);
        }

        [Fact]
        public async Task SearchAsync_flags_wildcard_chars_used_in_Exact_mode()
        {
            var mock = new Mock<ISearchService>();
            mock.Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SearchResult());
            var tool = new SearchTool(mock.Object, Settings());

            // Exact mode + '*' -> literal, matches nothing: the note must warn instead of silently returning [].
            var flagged = await tool.SearchAsync(new SearchToolRequest("dhamm*", SearchToolMode.Exact));
            Assert.NotNull(flagged.Note);
            Assert.Contains("Wildcard", flagged.Note);

            // Wildcard mode: no footgun note.
            var clean = await tool.SearchAsync(new SearchToolRequest("dhamm*", SearchToolMode.Wildcard));
            Assert.Null(clean.Note);
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

                var o = Assert.Single(occ.Occurrences);
                Assert.Equal(1, occ.Total);          // one record...
                Assert.Equal(1, occ.InstanceTotal);  // ...and one raw hit (no co-location to fold)
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
                Assert.Equal(hit, o.Cursor);   // unique locator = the hit's char offset (for /v1/passage)
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task GetOccurrencesAsync_multiword_marks_each_cooccurring_word()
        {
            var dir = Path.Combine(Path.GetTempPath(), "cst-occ-mw-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string book = "s0101m.mul.xml";
                string xml =
                    "<body><div id=\"dn1\" type=\"book\">" +
                    "<pb ed=\"V\" n=\"1.0003\"/>" +
                    "<p rend=\"bodytext\" n=\"12\">aaa AVIJJA bbb ccc SANKHARA ddd\u0964</p>" +
                    "</div></body>";
                await File.WriteAllTextAsync(Path.Combine(dir, book), xml, Encoding.Unicode);

                int a = xml.IndexOf("AVIJJA", StringComparison.Ordinal);
                int sk = xml.IndexOf("SANKHARA", StringComparison.Ordinal);
                // A proximity hit: two co-occurring words, the first flagged as the navigable anchor.
                var hit = new List<TermPosition>
                {
                    new TermPosition { StartOffset = a, EndOffset = a + 6, IsFirstTerm = true, Word = "avijja" },
                    new TermPosition { StartOffset = sk, EndOffset = sk + 8, IsFirstTerm = false, Word = "sankhara" },
                };
                var search = new Mock<ISearchService>();
                search.Setup(s => s.GetMultiWordPositionsAsync(book, "avijja sankhara",
                        It.IsAny<SearchMode>(), It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<List<TermPosition>> { hit });

                var tool = new SearchTool(search.Object, Settings(dir));
                var occ = await tool.GetOccurrencesAsync(new OccurrenceRequest(
                    book, "avijja sankhara", OutputScript: Script.Devanagari, MinChars: 1, ProximityDistance: 5));

                var o = Assert.Single(occ.Occurrences);
                Assert.Equal(2, o.Highlights.Count);
                Assert.Equal(1, o.Highlights.Count(h => h.IsAnchor));   // exactly one navigable anchor
                Assert.True(o.Highlights[0].IsAnchor);                  // AVIJJA (first by offset) is the anchor
                // each highlight's snippet-local range points at its own word
                Assert.Equal("AVIJJA", o.Snippet.Substring(o.Highlights[0].Start, o.Highlights[0].Length));
                Assert.Equal("SANKHARA", o.Snippet.Substring(o.Highlights[1].Start, o.Highlights[1].Length));
                // cursor is the anchor's SOURCE offset (for /v1/passage)
                Assert.Equal(a, o.Cursor);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task GetOccurrencesAsync_colocated_hits_fold_into_one_record_but_instanceTotal_counts_raw()
        {
            // Finding #1 (Desktop MCP report): two hits in ONE sentence merge into a single snippet record with
            // two highlights, so `total` (records) is 1 while `instanceTotal` (raw hits) is 2 — which ties out to
            // search's per-book `count`. `total < instanceTotal` means folded hits, not dropped hits.
            var dir = Path.Combine(Path.GetTempPath(), "cst-occ-colo-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string book = "s0101m.mul.xml";
                string xml =
                    "<body><div id=\"dn1\" type=\"book\">" +
                    "<pb ed=\"V\" n=\"1.0003\"/>" +
                    "<p rend=\"bodytext\" n=\"12\">aaa TARGET bbb TARGET ccc\u0964</p>" +   // two hits, one sentence (danda escaped)
                    "</div></body>";
                await File.WriteAllTextAsync(Path.Combine(dir, book), xml, Encoding.Unicode);

                int h1 = xml.IndexOf("TARGET", StringComparison.Ordinal);
                int h2 = xml.IndexOf("TARGET", h1 + 1, StringComparison.Ordinal);
                var search = new Mock<ISearchService>();
                search.Setup(s => s.GetTermPositionsAsync(book, "tgt", It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<TermPosition>
                    {
                        new TermPosition { StartOffset = h1, EndOffset = h1 + 6, IsFirstTerm = true, Word = "tgt" },
                        new TermPosition { StartOffset = h2, EndOffset = h2 + 6, IsFirstTerm = true, Word = "tgt" },
                    });

                var tool = new SearchTool(search.Object, Settings(dir));
                var occ = await tool.GetOccurrencesAsync(
                    new OccurrenceRequest(book, "tgt", OutputScript: Script.Devanagari, MinChars: 1));

                var o = Assert.Single(occ.Occurrences);   // one merged record
                Assert.Equal(2, o.Highlights.Count);      // ...carrying both hits
                Assert.Equal(1, occ.Total);               // records to page over
                Assert.Equal(2, occ.InstanceTotal);       // raw hits (matches search's per-book count)
                Assert.Equal(1, occ.ReturnedCount);
                Assert.False(occ.HasMore);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task GetOccurrencesAsync_single_word_wildcard_uses_the_expanding_path()
        {
            var dir = Path.Combine(Path.GetTempPath(), "cst-occ-wc-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string book = "s0101m.mul.xml";
                string xml =
                    "<body><div id=\"dn1\" type=\"book\">" +
                    "<pb ed=\"V\" n=\"1.0003\"/>" +
                    "<p rend=\"bodytext\" n=\"12\">aaa AVIJJA bbb</p>" +
                    "</div></body>";
                await File.WriteAllTextAsync(Path.Combine(dir, book), xml, Encoding.Unicode);
                int a = xml.IndexOf("AVIJJA", StringComparison.Ordinal);

                var search = new Mock<ISearchService>();
                // A single-word WILDCARD must route to the expanding path (it has to expand `avij*`), NOT the
                // literal single-term lookup which would match no index term and silently return [].
                search.Setup(s => s.GetMultiWordPositionsAsync(book, "avij*", SearchMode.Wildcard,
                        It.IsAny<int>(), It.IsAny<CancellationToken>()))
                    .ReturnsAsync(new List<List<TermPosition>>
                    {
                        new List<TermPosition> { new TermPosition { StartOffset = a, EndOffset = a + 6, IsFirstTerm = true, Word = "avijja" } }
                    });

                var tool = new SearchTool(search.Object, Settings(dir));
                var occ = await tool.GetOccurrencesAsync(new OccurrenceRequest(
                    book, "avij*", OutputScript: Script.Devanagari, MinChars: 1, Mode: SearchToolMode.Wildcard));

                var o = Assert.Single(occ.Occurrences);
                Assert.Equal("AVIJJA", o.Snippet.Substring(o.HitStart, o.HitLength));
                search.Verify(s => s.GetTermPositionsAsync(It.IsAny<string>(), It.IsAny<string>(),
                    It.IsAny<CancellationToken>()), Times.Never);   // NOT the literal path
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task SearchAsync_pageable_result_is_hasMore_not_truncated()
        {
            var mock = new Mock<ISearchService>();
            var tool = new SearchTool(mock.Object, Settings());

            // A normal "more than one page" result: the UI page-cap is set, but it is NOT an expansion overflow.
            mock.Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SearchResult
                {
                    HasMore = true, ResultsTruncated = true,
                    TruncationMessage = "Showing the first 100 matching words - refine your search."
                });
            var paged = await tool.SearchAsync(new SearchToolRequest("dhamm*", SearchToolMode.Wildcard));
            Assert.True(paged.HasMore);
            Assert.False(paged.Truncated);   // paging, not overflow
            Assert.Null(paged.Note);         // the UI "refine" message is not leaked to the API

            // A genuine expansion overflow: truncated true, with the cap message as the note.
            mock.Setup(s => s.SearchAsync(It.IsAny<SearchQuery>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new SearchResult
                {
                    ExpansionCapped = true, ResultsTruncated = true,
                    TruncationMessage = "matched more than 5,000 forms"
                });
            var capped = await tool.SearchAsync(new SearchToolRequest("dhamm*", SearchToolMode.Wildcard));
            Assert.True(capped.Truncated);
            Assert.Contains("5,000", capped.Note);
        }

        [Fact]
        public async Task GetOccurrencesAsync_empty_when_no_positions()
        {
            var search = new Mock<ISearchService>();
            search.Setup(s => s.GetTermPositionsAsync(It.IsAny<string>(), It.IsAny<string>(), It.IsAny<CancellationToken>()))
                .ReturnsAsync(new List<TermPosition>());

            var tool = new SearchTool(search.Object, Settings("/nonexistent"));
            var occ = await tool.GetOccurrencesAsync(new OccurrenceRequest("x.xml", "t"));

            Assert.Empty(occ.Occurrences);
        }
    }
}
