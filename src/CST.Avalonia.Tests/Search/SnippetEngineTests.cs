using CST.Conversion;
using CST.Navigation;
using CST.Search;
using Xunit;

namespace CST.Avalonia.Tests.Search
{
    /// <summary>
    /// Unit tests for the pure snippet engine (BookMarkers + TeiSnippetExtractor). Fixtures use ASCII
    /// placeholder "words" plus \u0964 (danda) / \u0965 (double danda) boundary escapes -- no literal
    /// Devanagari in source -- and request Script.Devanagari output so conversion is identity and the
    /// windowing/cleaning structure can be asserted exactly.
    /// </summary>
    public class SnippetEngineTests
    {
        // paranum "Q" + dot ".", three prose sentences, a footnote inside the hit sentence, two page editions.
        private const string Prose =
            "<TEI.2><text><body>" +
            "<div id=\"an1\" n=\"an1\" type=\"book\">" +
            "<pb ed=\"V\" n=\"1.0001\"/>" +
            "<pb ed=\"M\" n=\"2.0005\"/>" +
            "<p rend=\"bodytext\" n=\"7\">" +
            "<hi rend=\"paranum\">Q</hi><hi rend=\"dot\">.</hi> " +
            "aaa bbb ccc\u0964 " +
            "ddd eee TARGET <note>VAR\u0964z</note> fff\u0964 " +
            "ggg hhh iii\u0964" +
            "</p></div></body></text></TEI.2>";

        private static int HitStart(string xml, string term) => xml.IndexOf(term, System.StringComparison.Ordinal);

        private static SnippetOptions Deva(int min = 1, int max = 4000, bool notes = false) =>
            new(OutputScript: Script.Devanagari, IncludeVariantReadings: notes, MinChars: min, MaxChars: max);

        [Fact]
        public void Markers_report_paragraph_and_all_editions_at_hit()
        {
            var markers = BookMarkers.Build(Prose);
            var (num, code, pages) = markers.RefsAt(HitStart(Prose, "TARGET"));

            Assert.Equal(7, num);
            Assert.Equal("an1", code);
            Assert.Contains(pages, p => p.Edition == PageEdition.Vri && p.Volume == 1 && p.Number == 1);
            Assert.Contains(pages, p => p.Edition == PageEdition.Myanmar && p.Volume == 2 && p.Number == 5);
        }

        [Fact]
        public void Prose_snippet_is_the_hit_sentence_only()
        {
            var markers = BookMarkers.Build(Prose);
            int hs = HitStart(Prose, "TARGET");
            var r = TeiSnippetExtractor.Extract(Prose, hs, 6, markers, Deva(min: 1));

            Assert.Contains("TARGET", r.Snippet);
            Assert.Contains("ddd", r.Snippet);
            Assert.Contains("fff", r.Snippet);
            Assert.DoesNotContain("aaa", r.Snippet);   // neighbor sentence, floor off
            Assert.DoesNotContain("ggg", r.Snippet);   // neighbor sentence
            Assert.DoesNotContain("VAR", r.Snippet);   // footnote excluded by default
            Assert.DoesNotContain("{", r.Snippet);     // ...and no apparatus braces when notes are off
            Assert.DoesNotContain("Q", r.Snippet);     // paranum marker not in window
        }

        [Fact]
        public void Hit_range_points_at_the_term_in_the_snippet()
        {
            var markers = BookMarkers.Build(Prose);
            int hs = HitStart(Prose, "TARGET");
            var r = TeiSnippetExtractor.Extract(Prose, hs, 6, markers, Deva(min: 1));

            Assert.Equal("TARGET", r.Snippet.Substring(r.HitStart, r.HitLength));
        }

        [Fact]
        public void Variant_readings_included_on_request()
        {
            var markers = BookMarkers.Build(Prose);
            int hs = HitStart(Prose, "TARGET");
            var with = TeiSnippetExtractor.Extract(Prose, hs, 6, markers, Deva(min: 1, notes: true));

            Assert.Contains("VAR", with.Snippet);
            // Included apparatus is delimited by curly braces (absent from the corpus) so a consumer can tell
            // it from base text; the note content sits inside a `{...}`.
            int open = with.Snippet.IndexOf('{');
            int close = with.Snippet.IndexOf('}');
            Assert.True(open >= 0 && close > open, "note should be wrapped in { }");
            Assert.Contains("VAR", with.Snippet.Substring(open, close - open));
        }

        [Fact]
        public void Floor_widens_to_neighbor_sentences_and_still_strips_paranum()
        {
            var markers = BookMarkers.Build(Prose);
            int hs = HitStart(Prose, "TARGET");
            var r = TeiSnippetExtractor.Extract(Prose, hs, 6, markers, Deva(min: 200));

            Assert.Contains("aaa", r.Snippet);   // previous sentence pulled in
            Assert.Contains("ggg", r.Snippet);   // next sentence pulled in
            Assert.DoesNotContain("Q", r.Snippet); // paranum still stripped
            Assert.Equal("TARGET", r.Snippet.Substring(r.HitStart, r.HitLength));
        }

        // --- verse ----------------------------------------------------------

        private const string Verse =
            "<body><div id=\"x\" type=\"book\">" +
            "<p rend=\"gatha1\" n=\"10\">line one alpha\u0965</p>" +
            "<p rend=\"gatha2\">line two TARGET beta\u0965</p>" +
            "<p rend=\"gathalast\">line three gamma\u0965</p>" +
            "</div></body>";

        [Fact]
        public void Verse_hit_includes_one_line_each_side()
        {
            var markers = BookMarkers.Build(Verse);
            int hs = HitStart(Verse, "TARGET");
            var r = TeiSnippetExtractor.Extract(Verse, hs, 6, markers, Deva());

            Assert.Contains("one", r.Snippet);    // line before
            Assert.Contains("TARGET", r.Snippet); // hit line
            Assert.Contains("three", r.Snippet);  // line after
            Assert.Equal("TARGET", r.Snippet.Substring(r.HitStart, r.HitLength));
        }
    }
}
