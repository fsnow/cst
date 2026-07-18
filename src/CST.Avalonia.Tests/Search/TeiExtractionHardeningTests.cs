using System;
using CST.Conversion;
using CST.Search;
using Xunit;

namespace CST.Avalonia.Tests.Search
{
    /// <summary>
    /// #313 (A4-10..13): edge-case robustness in the extraction core. ASCII-only fixtures; Devanagari output is
    /// identity here, so the raw structure is asserted directly.
    /// </summary>
    public class TeiExtractionHardeningTests
    {
        private static SnippetOptions Deva(int min, int max) =>
            new(OutputScript: Script.Devanagari, IncludeFootnotes: false, MinChars: min, MaxChars: max);

        [Fact]
        public void Clean_treats_a_non_structural_hi_as_zero_width()
        {
            // #313 A4-10: <hi rend="bold"> mid-word is intra-word to the tokenizer — Clean must not inject a space.
            const string xml = "sa<hi rend=\"bold\">mmb</hi> foo";
            string cleaned = TeiText.Clean(xml, 0, xml.Length, includeNotes: false);
            Assert.Contains("sammb", cleaned);
            Assert.DoesNotContain("sa mmb", cleaned);
        }

        [Fact]
        public void Extract_with_no_marks_does_not_throw()
        {
            // #313 A4-11: the empty-marks fallback (a zero-width mark at 0) feeds FindLastPOpen(xml, 0), which used
            // to call LastIndexOf(-1) and throw.
            const string xml = "no paragraph tags here just text";
            var markers = BookMarkers.Build(xml);
            var ex = Record.Exception(() => TeiSnippetExtractor.Extract(xml, Array.Empty<SnippetMark>(), markers, Deva(1, 400)));
            Assert.Null(ex);
        }

        [Fact]
        public void Extract_without_a_paragraph_and_a_tight_window_does_not_throw()
        {
            // #313 A4-12: with no enclosing <p>, pEnd == xml.Length, so SnapToSpace's backward scan can start at
            // pos == length and index xml[length]. A late hit + a MaxChars below the visible length forces it.
            const string xml = "alpha bravo charlie delta echo foxtrot golf hotel india juliet kilo";
            var markers = BookMarkers.Build(xml);
            int hit = xml.IndexOf("juliet", StringComparison.Ordinal);
            var ex = Record.Exception(() =>
                TeiSnippetExtractor.Extract(xml, hit, "juliet".Length, markers, Deva(1, 60)));
            Assert.Null(ex);
        }

        [Fact]
        public void Extract_does_not_throw_when_a_pb_tag_starts_the_buffer()
        {
            // #313 A4-11 loop residual: a "<p"-prefixed non-<p> tag (<pb/>) at position 0 must not make
            // FindLastPOpen search before index 0 (LastIndexOf(..,-1)) and throw.
            const string xml = "<pb ed=\"V\" n=\"1\"/>alpha TARGET beta gamma\u0964";
            var markers = BookMarkers.Build(xml);
            int hit = xml.IndexOf("TARGET", StringComparison.Ordinal);
            var ex = Record.Exception(() =>
                TeiSnippetExtractor.Extract(xml, hit, "TARGET".Length, markers, Deva(1, 400)));
            Assert.Null(ex);
        }

        [Fact]
        public void ReadWindow_with_int_max_maxchars_returns_the_whole_text_not_one_char()
        {
            // #313 A4-13: hardCap = maxChars + maxChars/2 overflowed negative at maxChars=int.MaxValue, so the walk
            // returned after a single char. Computed in long now.
            const string xml = "<p>alpha beta gamma delta epsilon</p>";
            var markers = BookMarkers.Build(xml);
            var w = TeiPassageReader.ReadWindow(xml, 0, int.MaxValue, includeVariants: false, Script.Devanagari, markers);
            Assert.Contains("alpha", w.Text);
            Assert.Contains("epsilon", w.Text);
        }

        [Fact]
        public void Snippet_hit_inside_a_note_does_not_leak_the_notes_post_hit_tail()
        {
            // #355: the hit ("TARGET") is itself footnote text; with IncludeFootnotes=false the note's PRE-hit
            // content is stripped, but its POST-hit tail ("variant reading sii syaa") used to leak as base text
            // because the suffix Clean started mid-note. The base text after the note must still be preserved (#310).
            const string xml = "<p>base one two <note>TARGET variant reading sii syaa</note> three four five।</p>";
            var markers = BookMarkers.Build(xml);
            int hit = xml.IndexOf("TARGET", StringComparison.Ordinal);

            var r = TeiSnippetExtractor.Extract(xml, hit, "TARGET".Length, markers, Deva(1, 400));

            Assert.Contains("base one two", r.Snippet);          // pre-note base text
            Assert.Contains("TARGET", r.Snippet);                // the hit (note text) still renders
            Assert.Contains("three four five", r.Snippet);       // post-note base text preserved (#310)
            Assert.DoesNotContain("variant reading", r.Snippet); // the leaked note tail — gone (#355)
            Assert.DoesNotContain("syaa", r.Snippet);
        }

        [Fact]
        public void Snippet_hit_inside_a_note_still_shows_the_note_as_apparatus_when_footnotes_included()
        {
            // With footnotes ON the note is rendered as braced apparatus, so its tail is DELIMITED (not leaked as
            // base text) — the #355 clip only applies when braces are off. This pins that the fix is one-sided.
            const string xml = "<p>base one two <note>TARGET variant reading</note> three four five।</p>";
            var markers = BookMarkers.Build(xml);
            int hit = xml.IndexOf("TARGET", StringComparison.Ordinal);
            var opts = new SnippetOptions(Script.Devanagari, IncludeFootnotes: true, MinChars: 1, MaxChars: 400);

            var r = TeiSnippetExtractor.Extract(xml, hit, "TARGET".Length, markers, opts);

            Assert.Contains("TARGET", r.Snippet);
            Assert.Contains("three four five", r.Snippet);
            Assert.Contains("variant reading", r.Snippet);       // present, but as apparatus (braces)
        }
    }
}
