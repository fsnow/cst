using System;
using System.IO;
using System.Text;
using System.Threading.Tasks;
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
    /// End-to-end tests for the passage tool: resolve a paragraph reference to a start position in a temp
    /// UTF-16 book, read a bounded window, report citation refs + cursors. Script.Devanagari (identity).
    /// </summary>
    public class PassageToolTests
    {
        private static ISettingsService Settings(string dir)
        {
            var m = new Mock<ISettingsService>();
            m.SetupGet(s => s.Settings).Returns(new Settings { XmlBooksDirectory = dir });
            return m.Object;
        }

        private const string Xml =
            "<body><div id=\"dn1\" type=\"book\">" +
            "<pb ed=\"V\" n=\"1.0001\"/>" +
            "<p rend=\"bodytext\" n=\"5\">alpha bravo\u0964 charlie delta\u0964 echo foxtrot\u0964</p>" +
            "</div></body>";

        [Fact]
        public async Task FetchPassage_by_paragraph_returns_window_with_refs()
        {
            var dir = Path.Combine(Path.GetTempPath(), "cst-pass-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            try
            {
                const string book = "s0101m.mul.xml";
                await File.WriteAllTextAsync(Path.Combine(dir, book), Xml, Encoding.Unicode);

                var tool = new PassageTool(Settings(dir));
                var r = await tool.FetchPassageAsync(new PassageRequest(
                    book, new NavigationReference.Paragraph(5), MaxChars: 15, OutputScript: Script.Devanagari));

                Assert.Contains("alpha", r.Text);
                Assert.Equal(5, r.ParagraphNumber);
                Assert.Equal("dn1", r.ParagraphBookCode);
                Assert.Equal("paragraph 5 (dn1)", r.NormalizedReference);
                Assert.Contains(r.Pages, p => p.Edition == PageEdition.Vri && p.Volume == 1 && p.Number == 1);
                Assert.NotNull(r.NextCursor);
            }
            finally
            {
                Directory.Delete(dir, recursive: true);
            }
        }

        [Fact]
        public async Task FetchPassage_missing_book_returns_empty()
        {
            var tool = new PassageTool(Settings("/nonexistent"));
            var r = await tool.FetchPassageAsync(new PassageRequest("x.xml", new NavigationReference.WholeBook()));

            Assert.Equal("", r.Text);
            Assert.Null(r.NextCursor);
        }

        [Fact]
        public async Task FetchPassage_rejects_a_bookId_outside_the_catalog_no_arbitrary_read()
        {
            // #301: a bookId that isn't an exact catalog file name must never be opened — an ABSOLUTE path makes
            // Path.Combine discard the corpus dir, so without the catalog guard this would read an arbitrary file.
            var dir = Path.Combine(Path.GetTempPath(), "cst-pass-trav-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(dir);
            var secret = Path.Combine(Path.GetTempPath(), "cst-secret-" + Guid.NewGuid().ToString("N") + ".txt");
            await File.WriteAllTextAsync(secret, "SECRETDATA", Encoding.Unicode);
            try
            {
                var tool = new PassageTool(Settings(dir));

                var abs = await tool.FetchPassageAsync(new PassageRequest(secret, new NavigationReference.WholeBook()));
                Assert.Equal("unknown book", abs.NormalizedReference);
                Assert.Equal("", abs.Text);                    // file never read
                Assert.DoesNotContain("SECRET", abs.Text);

                var rel = await tool.FetchPassageAsync(new PassageRequest("../../etc/hosts", new NavigationReference.WholeBook()));
                Assert.Equal("unknown book", rel.NormalizedReference);
            }
            finally
            {
                try { File.Delete(secret); } catch { }
                Directory.Delete(dir, recursive: true);
            }
        }
    }
}
