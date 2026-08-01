using System.Linq;
using CST;
using Xunit;

namespace CST.Avalonia.Tests.Models
{
    /// <summary>
    /// Mapping a printed page number to a page of the scanned source PDF. (#540)
    ///
    /// CST4 used PageStart + (page - 1), which assumes the scan holds every printed page. It does not:
    /// blank pages were passed over, so from the first skip onward every page is off by one, and the
    /// error accumulates with each further skip. <see cref="Sources.Source.MissingPages"/> records the
    /// skipped printed pages per PDF.
    /// </summary>
    public class SourcePdfPageTests
    {
        private static Sources.Source Src(int pageStart, params int[] missing) =>
            new(Sources.SourceType.Burmese1957, pageStart, "x.pdf", missing);

        [Fact]
        public void WithNoMissingPages_IsTheOriginalLinearFormula()
        {
            var s = Src(19);
            Assert.Equal(19, s.PdfPageFor(1));
            Assert.Equal(87, s.PdfPageFor(69));
        }

        [Fact]
        public void PagesBeforeTheBlankAreUnaffected()
        {
            var s = Src(19, 68);
            Assert.Equal(19, s.PdfPageFor(1));
            Assert.Equal(85, s.PdfPageFor(67));   // last page before the blank
        }

        [Fact]
        public void PagesAfterTheBlankShiftBackByOne()
        {
            // s0402a: print page 68 is blank, so Tikanipāta at Myanmar 2.0069 is PDF 86, not 87.
            var s = Src(19, 68);
            Assert.Equal(86, s.PdfPageFor(69));
        }

        [Fact]
        public void SkipsAccumulate()
        {
            // The same book's second blank at 248 makes Catukkanipāta (Myanmar 2.0249) off by two.
            var s = Src(19, 68, 248);
            Assert.Equal(86, s.PdfPageFor(69));    // one blank passed
            Assert.Equal(265, s.PdfPageFor(249));  // two blanks passed
        }

        [Fact]
        public void TheBlankPageItselfResolvesToTheFollowingPage()
        {
            // Nothing was scanned for print page 68, so the honest answer is where 68 would have been —
            // which is the page that follows it. Landing there beats refusing to open the PDF.
            var s = Src(19, 68);
            Assert.Equal(86, s.PdfPageFor(68));
            Assert.Equal(86, s.PdfPageFor(69));
        }

        [Fact]
        public void NeverReturnsAPageBeforeTheStartOfThePdf()
        {
            Assert.Equal(1, Src(19).PdfPageFor(-50));
        }

        [Fact]
        public void MissingPagesDefaultsToEmptyNotNull()
        {
            var s = new Sources.Source(Sources.SourceType.Burmese1957, 5, "x.pdf");
            Assert.NotNull(s.MissingPages);
            Assert.Empty(s.MissingPages);
            Assert.Equal(5, s.PdfPageFor(1));
        }

        [Fact]
        public void EverySeededArrayIsAscendingDistinctAndPositive()
        {
            // PdfPageFor stops counting at the first entry >= the target, so an unsorted array would
            // silently undercount. This guards hand-edits made during QA.
            var books = new[]
            {
                "abh03a.att.xml", "abh03m10.mul.xml", "abh03m11.mul.xml", "abh03m4.mul.xml",
                "abh03t.tik.xml", "s0201m.mul.xml", "s0402a.att.xml", "s0403a.att.xml",
                "s0403t.tik.xml", "s0404a.att.xml", "s0404t.tik.xml", "s0508a2.att.xml",
                "s0510m2.mul.xml", "s0514a2.att.xml", "s0514a3.att.xml", "vin02t.tik.xml",
            };
            var seen = 0;
            foreach (var book in books)
            {
                foreach (Sources.SourceType t in System.Enum.GetValues<Sources.SourceType>())
                {
                    var s = Sources.Inst.GetSource(book, t);
                    if (s is null || s.MissingPages.Length == 0) continue;
                    seen++;
                    Assert.All(s.MissingPages, p => Assert.True(p > 0, $"{book}/{t}: page {p}"));
                    Assert.Equal(s.MissingPages.OrderBy(p => p).ToArray(), s.MissingPages);
                    Assert.Equal(s.MissingPages.Distinct().Count(), s.MissingPages.Length);
                }
            }
            Assert.Equal(21, seen);
        }

        [Fact]
        public void TheSeedForAnAtthakathaMatchesTheGapsInItsPageMarkers()
        {
            // The seed came from holes in the XML's Myanmar page-break markers. Pinned so that a future
            // QA correction is a deliberate edit rather than an accident.
            var s = Sources.Inst.GetSource("s0402a.att.xml", Sources.SourceType.Burmese1957);
            Assert.NotNull(s);
            Assert.Equal(new[] { 68, 248 }, s!.MissingPages);
            Assert.Equal(19, s.PageStart);
        }
    }
}
