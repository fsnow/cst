using CST.Search;
using Xunit;

namespace CST.Avalonia.Tests.Search
{
    /// <summary>
    /// #310 A4-2: TagName("&lt;/note&gt;") is "note" too, so Clean's note-strip branch used to fire for the CLOSE
    /// tag and call SkipSubtree on it — entering depth 1 and consuming to the NEXT close tag (or the whole rest of
    /// the range), leaking the note tail as base text and silently dropping everything after. A close tag must be
    /// zero-width. ASCII-only fixtures (Devanagari output would be identity here; these assert the raw cleaned text).
    /// </summary>
    public class TeiTextNoteBoundaryTests
    {
        [Fact]
        public void Clean_starting_inside_a_note_treats_the_close_tag_as_zero_width_and_keeps_following_text()
        {
            // The empirical repro from the review: begin INSIDE the first note's content.
            const string xml = "<note>variant one</note> ccc ddd <note>variant two</note> eee";
            int start = xml.IndexOf("ant one", System.StringComparison.Ordinal);   // mid-note

            string cleaned = TeiText.Clean(xml, start, xml.Length, includeNotes: false);

            // Old behavior returned just "ant one" (note tail leaked, everything after dropped). The following
            // base text ("ccc ddd", "eee") must now survive; the SECOND, fully-enclosed note is still stripped.
            Assert.Contains("ccc ddd", cleaned);
            Assert.Contains("eee", cleaned);
            Assert.DoesNotContain("variant two", cleaned);
        }

        [Fact]
        public void Clean_still_strips_a_fully_enclosed_note()
        {
            const string xml = "aaa <note>VAR</note> bbb";
            string cleaned = TeiText.Clean(xml, 0, xml.Length, includeNotes: false);
            Assert.DoesNotContain("VAR", cleaned);
            Assert.Contains("aaa", cleaned);
            Assert.Contains("bbb", cleaned);
        }

        [Fact]
        public void CountNotesIntersecting_counts_a_note_the_window_opens_midway_through()
        {
            // Window [lo, hi) starts inside note1 and ends inside note2 -> both intersect, even though neither
            // STARTS in the window. (#310 A4-15)
            const string xml = "xx <note>one</note> mid <note>two</note> yy";
            int lo = xml.IndexOf("ne</note>", System.StringComparison.Ordinal);   // inside note1
            int hi = xml.IndexOf("wo</note>", System.StringComparison.Ordinal);   // inside note2

            Assert.Equal(2, TeiText.CountNotesIntersecting(xml, scanFrom: 0, lo, hi));
        }
    }
}
