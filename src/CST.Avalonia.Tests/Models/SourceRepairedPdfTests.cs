using System.Collections.Generic;
using System.Linq;
using CST;
using Xunit;

namespace CST.Avalonia.Tests.Models
{
    /// <summary>
    /// The repaired source PDFs (#1136): each has a placeholder page for every print page its scan lacks,
    /// its pages in print order, and drops pages scanned twice. A repaired file's hash gets its own mapping,
    /// the latest; the original and every earlier version with the same pages keep the addSource mapping.
    /// </summary>
    public class SourceRepairedPdfTests
    {
        private static Sources.Source Repaired(string book, Sources.SourceType type) =>
            Sources.Inst.GetSource(book, type)!.Latest;

        [Theory]
        // Read by eye in the repaired copies (2026-11-26): the real page after each placeholder or
        // dropped repeat, and the pages around the reordered vin13t leaf.
        [InlineData("vin02m3.mul.xml", 284, 294)]
        [InlineData("s0510m2.mul.xml", 216, 231)]
        [InlineData("s0505m.mul.xml", 279, 400)]   // Suttanipāta opening, after placeholders for 276-278
        [InlineData("s0504m.mul.xml", 220, 341)]   // the 99-page repeat is kept
        [InlineData("s0515m.mul.xml", 216, 220)]
        [InlineData("s0519m.mul.xml", 158, 164)]
        [InlineData("s0301m.mul.xml", 156, 193)]
        [InlineData("abh03m11.mul.xml", 63, 68)]
        [InlineData("s0402a.att.xml", 249, 267)]
        [InlineData("s0513a4.att.xml", 232, 238)]
        [InlineData("s0519a.att.xml", 238, 418)]
        [InlineData("vin13t.nrf.xml", 47, 61)]
        [InlineData("vin13t.nrf.xml", 48, 62)]
        [InlineData("vin13t.nrf.xml", 59, 73)]
        [InlineData("vin13t.nrf.xml", 443, 457)]
        [InlineData("abh05t.nrf.xml", 35, 322)]
        [InlineData("vin05t.nrf.xml", 213, 228)]
        [InlineData("vin09t.nrf.xml", 418, 436)]   // after placeholders for 416-417, which the scan lacks
        [InlineData("vin09t.nrf.xml", 451, 469)]
        public void Repaired1957Pdf_OpensThePrintPage(string book, int print, int pdf) =>
            Assert.Equal(pdf, Repaired(book, Sources.SourceType.Burmese1957).PdfPageFor(print));

        [Theory]
        // Read by eye in the repaired 2010 copies (2026-11-26): the real page after each placeholder,
        // and the page after the dropped repeat in Paṭṭhāna-5.
        [InlineData("s0203m.mul.xml", 204, 214)]
        [InlineData("s0504m.mul.xml", 246, 274)]
        [InlineData("s0506m.mul.xml", 122, 142)]
        [InlineData("abh01m.mul.xml", 140, 162)]
        [InlineData("abh03m7.mul.xml", 286, 320)]
        [InlineData("abh03m8.mul.xml", 380, 410)]
        [InlineData("abh03m11.mul.xml", 113, 123)]
        public void Repaired2010Pdf_OpensThePrintPage(string book, int print, int pdf) =>
            Assert.Equal(pdf, Repaired(book, Sources.SourceType.Burmese2010).PdfPageFor(print));

        [Theory]
        // Read by eye in the repaired Añña copies (2026-11-26): the real page after each placeholder
        // or dropped repeat. e0804n keeps one copy of its repeated 269; e1007n keeps its re-shots.
        [InlineData("e0906n.nrf.xml", 28, 35)]
        [InlineData("e0201n.nrf.xml", 110, 221)]
        [InlineData("e0401n.nrf.xml", 86, 93)]
        [InlineData("e0602n.nrf.xml", 176, 179)]
        [InlineData("e0702n.nrf.xml", 162, 177)]
        [InlineData("e0703n.nrf.xml", 216, 222)]
        [InlineData("e0703n.nrf.xml", 254, 260)]
        [InlineData("e0804n.nrf.xml", 353, 361)]
        [InlineData("e0804n.nrf.xml", 375, 383)]
        [InlineData("e0813n.nrf.xml", 117, 149)]
        [InlineData("e1007n.nrf.xml", 27, 38)]
        public void RepairedAnyaPdf_OpensThePrintPage(string book, int print, int pdf) =>
            Assert.Equal(pdf, Repaired(book, Sources.SourceType.BurmeseAnya).PdfPageFor(print));

        private static IEnumerable<Sources.Source> AllMappedSources() =>
            Books.Inst.Select(b => b.FileName).SelectMany(f =>
                Sources.Inst.GetAvailableSources(f).Select(t => Sources.Inst.GetSource(f, t)!));

        [Fact]
        public void ARepairedPdfNeedsNoMissingPagesOrOverrides()
        {
            // A repaired copy has a placeholder for every page the scan lacks and its pages in print
            // order, so a book on it maps by PageStart (plus any repeat deliberately kept).
            var repaired = 0;
            foreach (var s in AllMappedSources().Where(s => s.Latest != s))
            {
                repaired++;
                Assert.Empty(s.Latest.MissingPages);
                Assert.Empty(s.Latest.PageOverrides);
            }
            Assert.Equal(62, repaired);
        }

        [Fact]
        public void TheOriginalScansKeepTheirMapping()
        {
            // vin13t: both SharePoint versions of the original have the same pages (the second, of
            // 2026-11-17, changed only metadata); the repaired file puts the scan's 48, 47 in print order.
            var s = Sources.Inst.GetSource("vin13t.nrf.xml", Sources.SourceType.Burmese1957)!;
            foreach (var original in new[] { "RwBmROq0lVfqIvdRIB8d2RPgdTg=", "Y/967oP2IctGHKHw51LVUEn8sfk=" })
            {
                Assert.Same(s, s.ForVersion(original));
                Assert.Equal(60, s.ForVersion(original).PdfPageFor(48));
            }
            Assert.Equal(62, s.ForVersion("8dpTHx/4GZ/UuovEyXg7OWOvrvk=").PdfPageFor(48));
            Assert.Equal(62, s.ForVersion(null).PdfPageFor(48));
        }

        [Fact]
        public void SiblingBooksOnARepairedPdfShareOneStartPage()
        {
            // Books that continue one page run in one volume no longer absorb each other's gaps.
            foreach (var group in new[]
            {
                new[] { "s0510m2.mul.xml", "s0511m.mul.xml", "s0512m.mul.xml" },
                new[] { "s0519m.mul.xml", "s0520m.nrf.xml" },
                new[] { "s0301m.mul.xml", "s0302m.mul.xml" },
                new[] { "s0403a.att.xml", "s0404a.att.xml" },
                new[] { "s0403t.tik.xml", "s0404t.tik.xml" },
            })
            {
                var starts = group.Select(b => Repaired(b, Sources.SourceType.Burmese1957).PageStart).Distinct();
                Assert.Single(starts);
            }
        }
    }
}
