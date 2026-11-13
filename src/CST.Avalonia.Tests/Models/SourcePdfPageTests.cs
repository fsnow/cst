using System;
using System.IO;
using System.Linq;
using CST;
using CST.Navigation;
using CST.Search;
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
                "s0510m2.mul.xml", "s0511m.mul.xml", "s0512m.mul.xml", "s0514a2.att.xml",
                "s0514a3.att.xml", "vin02t.tik.xml",
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
            Assert.Equal(23, seen);
        }

        [Fact]
        public void PagesTheScanHoldsTwice_PushLaterPagesBack()
        {
            // The 1957 abh03m8 scan runs 93, 94, 95, 94, 95, 96: two extra pages after print page 95.
            var s = new Sources.Source(Sources.SourceType.Burmese1957, 28, "x.pdf", extraPagesAfter: new[] { 95, 95 });
            Assert.Equal(28 + 92, s.PdfPageFor(93));
            Assert.Equal(28 + 93, s.PdfPageFor(94));   // the first copy
            Assert.Equal(28 + 94, s.PdfPageFor(95));   // the first copy
            Assert.Equal(28 + 97, s.PdfPageFor(96));   // after both repeats
            Assert.Empty(new Sources.Source(Sources.SourceType.Burmese1957, 1, "x.pdf").ExtraPagesAfter);
        }

        [Fact]
        public void MissingAndExtraPagesCombine()
        {
            var s = new Sources.Source(Sources.SourceType.Burmese1957, 10, "x.pdf", new[] { 20 }, extraPagesAfter: new[] { 30 });
            Assert.Equal(10 + 18, s.PdfPageFor(19));
            Assert.Equal(10 + 19, s.PdfPageFor(21));   // one skipped
            Assert.Equal(10 + 30, s.PdfPageFor(31));   // one skipped, one extra: they cancel
        }

        [Fact]
        public void Abh03m8_1957_AccountsForItsRepeatedPages()
        {
            var s = Sources.Inst.GetSource("abh03m8.mul.xml", Sources.SourceType.Burmese1957)!;
            Assert.Equal(new[] { 95, 95 }, s.ExtraPagesAfter);
            Assert.Empty(Sources.Inst.GetSource("abh03m8.mul.xml", Sources.SourceType.Burmese2010)!.ExtraPagesAfter);
        }

        [Fact]
        public void S0504m_1957_LandsOnThePagesTheScanActuallyHas()
        {
            // Checked by hand: print 219 is PDF 241, the repeat begins with print 120 at PDF 242,
            // print 220 is PDF 341, and the run is right up to print 274.
            var s = Sources.Inst.GetSource("s0504m.mul.xml", Sources.SourceType.Burmese1957)!;
            Assert.Equal(241, s.PdfPageFor(219));
            Assert.Equal(341, s.PdfPageFor(220));
            Assert.Equal(395, s.PdfPageFor(274));
            Assert.Equal(396, s.PdfPageFor(275));   // labelled "279" in the scan

            // print 276-278 are not in the scan, so the Suttanipāta opens on the next PDF page
            var sn = Sources.Inst.GetSource("s0505m.mul.xml", Sources.SourceType.Burmese1957)!;
            Assert.Equal(397, sn.PdfPageFor(279));
        }

        [Fact]
        public void MissingPagesPropagateToSiblingBooksSharingOnePdf()
        {
            // A missing page belongs to the PDF, not the book. Where several books share one scan and
            // one continuous page sequence, a gap found in the book that CONTAINS it shifts every
            // sibling that follows it — but the gap is only visible in that one book's XML, so the
            // seeding pass missed the siblings. (Apadāna-2 page 186, found in QA.)
            foreach (var book in new[] { "s0511m.mul.xml", "s0512m.mul.xml" })
            {
                var s = Sources.Inst.GetSource(book, Sources.SourceType.Burmese2010);
                Assert.NotNull(s);
                Assert.Equal(new[] { 186 }, s!.MissingPages);
            }

            // s0404a continues s0403a's sequence in the same PDF, so it carries that book's blanks too.
            var an = Sources.Inst.GetSource("s0404a.att.xml", Sources.SourceType.Burmese1957);
            Assert.Equal(new[] { 86, 144, 256, 286, 342 }, an!.MissingPages);

            // The counter-case: s0404t's PageStart (18, against s0403t's 19) ALREADY absorbs the blank
            // at 148. Adding it would double-count and land a page early. Pinned so it stays empty.
            var tik = Sources.Inst.GetSource("s0404t.tik.xml", Sources.SourceType.Burmese1957);
            Assert.Equal(new[] { 266 }, tik!.MissingPages);
            Assert.Equal(18, tik.PageStart);
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

        [Fact]
        public void TheSihalaBooksAreMappedByTheirOtherPages()
        {
            // They have no Myanmar page markers, only ed="O", so a Myanmar mapping left their PDF button
            // greyed out for good. (#1100)
            foreach (var book in new[] { "e1201n.nrf.xml", "e1203n.nrf.xml", "e1205n.nrf.xml", "e1209n.nrf.xml" })
            {
                var s = Sources.Inst.GetSource(book, Sources.SourceType.BurmeseAnya);
                Assert.NotNull(s);
                Assert.Equal(PageEdition.Other, s!.PageEdition);
            }
            Assert.Equal(PageEdition.Myanmar,
                Sources.Inst.GetSource("s0101m.mul.xml", Sources.SourceType.Burmese1957)!.PageEdition);
        }

        [Fact]
        public void EveryMappedBookCarriesThePageMarkersItsSourceCountsIn()
        {
            // A source whose page numbering the book lacks has a PDF button that can never turn on: the page
            // it waits for never resolves. (#1100) Reads the installed corpus; nothing to check without one.
            var dir = Environment.GetEnvironmentVariable("CST_XML_DIR")
                      ?? Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", "Library/Application Support/CSTReader/xml");
            if (!Directory.Exists(dir))
                return;

            int checkedSources = 0;
            var lacking = new System.Collections.Generic.List<string>();
            foreach (var file in Directory.GetFiles(dir, "*.xml").OrderBy(f => f, StringComparer.Ordinal))
            {
                var name = Path.GetFileName(file);
                var types = Sources.Inst.GetAvailableSources(name);
                if (types.Count == 0)
                    continue;
                var editions = BookMarkers.Build(File.ReadAllText(file)).Editions();
                foreach (var t in types)
                {
                    var edition = Sources.Inst.GetSource(name, t)!.PageEdition;
                    checkedSources++;
                    if (!editions.Contains(edition))
                        lacking.Add($"{name} {t}: no {edition} pages");
                }
            }
            Assert.Empty(lacking);
            Assert.True(checkedSources > 0);
        }
    }
}
