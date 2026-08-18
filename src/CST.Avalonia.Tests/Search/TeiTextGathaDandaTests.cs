using CST.Conversion;
using CST.Search;
using Xunit;

namespace CST.Avalonia.Tests.Search
{
    /// <summary>
    /// #680: in verse a single danda ends a pada and reads ";" in Latin, while a double ends the verse and
    /// reads "."; the double dandas around *namo tassa* are dropped. Those rules are written against markup,
    /// so #669 could not apply them in Convert (which receives tag-stripped text) and flattened every stop to
    /// a period — showing a model every line of a gatha ending identically. Clean still has the tags, so the
    /// decision is made there instead.
    ///
    /// Devanagari is written with \uXXXX escapes throughout, never literal glyphs.
    /// </summary>
    public class TeiTextGathaDandaTests
    {
        private const char Danda = '\u0964';
        private const char DoubleDanda = '\u0965';

        // Trimmed the way TeiPassageReader trims: a closing tag leaves a space, which is not what is under
        // test here.
        private static string Render(string xml) =>
            TeiText.Convert(TeiText.Clean(xml, 0, xml.Length, includeNotes: false, Script.Latin), Script.Latin)
                .Trim();

        [Fact]
        public void In_a_gatha_a_single_danda_is_a_semicolon_and_a_double_is_a_period()
        {
            string xml = $"<p rend=\"gatha1\">pada one{Danda} pada two{DoubleDanda}</p>";

            Assert.Equal("pada one; pada two.", Render(xml));
        }

        [Fact]
        public void Every_gatha_rend_variant_counts_as_verse()
        {
            // The reader matches rend="gatha[a-z0-9]*", so gathalast and gatha2 are verse as much as gatha1.
            foreach (var rend in new[] { "gatha1", "gatha2", "gatha3", "gathalast" })
            {
                string xml = $"<p rend=\"{rend}\">pada{Danda}</p>";
                Assert.Equal("pada;", Render(xml));
            }
        }

        [Fact]
        public void In_prose_both_dandas_are_periods()
        {
            string xml = $"<p rend=\"bodytext\">first{Danda} second{DoubleDanda}</p>";

            Assert.Equal("first. second.", Render(xml));
        }

        [Fact]
        public void A_paragraph_with_no_rend_is_prose()
        {
            string xml = $"<p>plain{Danda}</p>";

            Assert.Equal("plain.", Render(xml));
        }

        [Fact]
        public void Namo_tassa_loses_its_double_dandas_but_keeps_a_single_one()
        {
            // rend="centre" is the namo tassa paragraph; the reader shows no stop there.
            string xml = $"<p rend=\"centre\">namo tassa{DoubleDanda}</p>";
            Assert.Equal("namo tassa", Render(xml));

            string single = $"<p rend=\"centre\">namo tassa{Danda}</p>";
            Assert.Equal("namo tassa.", Render(single));
        }

        [Fact]
        public void A_dropped_double_danda_does_not_leave_a_double_space()
        {
            // The mark sits between words with a space on each side; removing it must close the gap to one.
            string xml = $"<p rend=\"centre\">assa {DoubleDanda} bhagava</p>";

            Assert.Equal("assa bhagava", Render(xml));
        }

        [Fact]
        public void A_window_that_begins_mid_verse_is_still_punctuated_as_verse()
        {
            // The regression this guards: a snippet window usually starts mid-paragraph, so the opening <p>
            // is behind `start` and the walk never sees it. Without looking backwards, every such snippet
            // would be punctuated as prose.
            string xml = $"<p rend=\"gatha1\">first pada{Danda} second pada{Danda}</p>";
            int start = xml.IndexOf("second", System.StringComparison.Ordinal);

            string text = TeiText.Convert(
                TeiText.Clean(xml, start, xml.Length, includeNotes: false, Script.Latin), Script.Latin).Trim();

            Assert.Equal("second pada;", text);
        }

        [Fact]
        public void A_window_beginning_after_a_closed_verse_is_prose_again()
        {
            string xml = $"<p rend=\"gatha1\">verse{Danda}</p><p rend=\"bodytext\">prose{Danda}</p>";
            int start = xml.IndexOf("prose", System.StringComparison.Ordinal);

            string text = TeiText.Convert(
                TeiText.Clean(xml, start, xml.Length, includeNotes: false, Script.Latin), Script.Latin).Trim();

            Assert.Equal("prose.", text);
        }

        [Fact]
        public void A_verse_and_the_prose_after_it_are_punctuated_differently_in_one_range()
        {
            // A passage spans paragraphs, which is why a single "is this verse?" flag on the range would not
            // have worked — the decision has to be made per mark, as the walk passes each <p>.
            string xml = $"<p rend=\"gatha1\">pada{Danda}</p><p rend=\"bodytext\">sentence{Danda}</p>";

            Assert.Equal("pada; sentence.", Render(xml));
        }

        [Fact]
        public void Non_Latin_output_keeps_the_dandas_for_its_own_converter()
        {
            // Clean must not impose Latin punctuation on a script that has its own marks.
            string xml = $"<p rend=\"gatha1\">pada{Danda}</p>";

            string cleaned = TeiText.Clean(xml, 0, xml.Length, includeNotes: false, Script.Devanagari);

            Assert.Contains(Danda, cleaned);
            Assert.DoesNotContain(';', cleaned);
        }

        [Fact]
        public void The_default_overload_is_unchanged_for_callers_that_only_measure_length()
        {
            // VisibleLen and the older tests call Clean without a script; that path must still be identity.
            string xml = $"<p rend=\"gatha1\">pada{Danda}</p>";

            Assert.Contains(Danda, TeiText.Clean(xml, 0, xml.Length, includeNotes: false));
        }

        [Theory]
        [InlineData("<p rend=\"gatha1\">x", "gatha1")]
        [InlineData("<p>x", "")]
        [InlineData("<p rend=\"centre\">x", "centre")]
        public void ParagraphRendAt_reads_the_enclosing_paragraph(string prefix, string expected)
        {
            Assert.Equal(expected, TeiText.ParagraphRendAt(prefix + "yyy", prefix.Length + 1));
        }

        [Fact]
        public void ParagraphRendAt_is_not_fooled_by_other_tags_beginning_with_p()
        {
            // <paranum> and <pb> both start "<p"; only "<p " or "<p>" is a paragraph.
            const string xml = "<p rend=\"gatha1\"><hi rend=\"paranum\">12</hi><pb ed=\"V\" n=\"1\"/>text";

            Assert.Equal("gatha1", TeiText.ParagraphRendAt(xml, xml.Length - 1));
        }

        [Fact]
        public void ParagraphRendAt_returns_nothing_outside_a_paragraph()
        {
            const string xml = "<p rend=\"gatha1\">verse</p>between";

            Assert.Equal("", TeiText.ParagraphRendAt(xml, xml.Length - 1));
        }
    }
}
