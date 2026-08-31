using System;
using CST.Conversion;
using CST.Avalonia.Services.Tools;
using CST.Search;
using Xunit;

namespace CST.Avalonia.Tests.Search
{
    /// <summary>
    /// Two ways a passage window could describe itself falsely: rendering a footnote's tail as base text
    /// (#913), and citing a paragraph range it does not contain (#914).
    ///
    /// <para>ASCII placeholder words and escaped dandas, Devanagari output so conversion is the identity and
    /// the text can be asserted exactly — the house style for this engine's tests.</para>
    /// </summary>
    public class PassageFidelityTests
    {
        // A note sits mid-paragraph. "sigla" stands in for the witness marker that trails a variant reading,
        // and is the fragment that must never appear as base text.
        private const string NoteXml =
            "<body><div id=\"dn1\" type=\"book\">" +
            "<p rend=\"bodytext\" n=\"5\">alpha bravo<note>variant sigla</note> charlie delta\u0964</p>" +
            "</div></body>";

        // The same shape, but with a note whose body outruns any small budget - and a danda inside it, which
        // is what lets WalkForward stop mid-note when the apparatus is rendered inline.
        private const string LongNoteXml =
            "<body><div id=\"dn1\" type=\"book\">" +
            "<p rend=\"bodytext\" n=\"5\">alpha bravo<note>one two three\u0964 four five six seven eight " +
            "nine ten eleven twelve thirteen fourteen sigla</note> charlie delta\u0964</p>" +
            "</div></body>";

        [Fact]
        public void A_note_too_long_for_the_budget_is_skipped_rather_than_opened_and_left_unclosed()
        {
            var markers = BookMarkers.Build(LongNoteXml);
            int open = LongNoteXml.IndexOf("<note>", StringComparison.Ordinal) + "<note>".Length;

            // Apparatus rendered inline, budget far smaller than the note: opening at the note start would
            // close before the note does, leaving a brace with nothing to match it.
            var w = TeiPassageReader.ReadWindow(LongNoteXml, open + 4, maxChars: 12,
                includeVariants: true, outputScript: Script.Devanagari, markers);

            Assert.Equal(w.Text.Split('{').Length, w.Text.Split('}').Length);
            Assert.DoesNotContain("sigla", w.Text);
        }

        // A danda INSIDE the note, positioned so a modest budget runs out while the walk is in the apparatus.
        private const string DandaInNoteXml =
            "<body><div id=\"dn1\" type=\"book\">" +
            "<p rend=\"bodytext\" n=\"5\">alpha bravo charlie<note>one two\u0964 three</note> delta echo\u0964 " +
            "foxtrot golf\u0964</p>" +
            "</div></body>";

        [Fact]
        public void A_danda_inside_a_note_does_not_end_the_window()
        {
            var markers = BookMarkers.Build(DandaInNoteXml);
            int start = markers.PositionOfParagraph(5);

            // Apparatus rendered inline, and a budget that is reached while the walk is inside the note, so
            // the note's own danda is the first boundary the walk meets after the budget.
            var w = TeiPassageReader.ReadWindow(DandaInNoteXml, start, maxChars: 22,
                includeVariants: true, outputScript: Script.Devanagari, markers);

            // Whatever it decides, it must not close BETWEEN the braces.
            Assert.Equal(w.Text.Split('{').Length, w.Text.Split('}').Length);
        }

        [Fact]
        public void The_hard_cap_ends_before_a_note_rather_than_inside_it()
        {
            var markers = BookMarkers.Build(LongNoteXml);
            int start = markers.PositionOfParagraph(5);

            // No danda anywhere before the cap falls, so the walk runs to the hard cap - which the boundary
            // guard cannot help with, because it is unconditional.
            var w = TeiPassageReader.ReadWindow(LongNoteXml, start, maxChars: 14,
                includeVariants: true, outputScript: Script.Devanagari, markers);

            Assert.Equal(w.Text.Split('{').Length, w.Text.Split('}').Length);
            Assert.Contains("alpha", w.Text);
        }

        private static int InsideTheNote(string xml)
        {
            int open = xml.IndexOf("<note>", StringComparison.Ordinal) + "<note>".Length;
            return open + "variant ".Length;   // mid-reading, before the sigla
        }

        [Fact]
        public void A_window_opening_inside_a_note_does_not_render_the_apparatus_as_base_text()
        {
            var markers = BookMarkers.Build(NoteXml);

            // Small budget: the sentence snap, when it runs at all, is rejected once the base text before the
            // note outruns the budget - so this is the state the window is actually left in.
            var w = TeiPassageReader.ReadWindow(NoteXml, InsideTheNote(NoteXml), maxChars: 20,
                includeVariants: false, outputScript: Script.Devanagari, markers);

            // The tail of a variant reading is apparatus. Emitted undelimited it reads as the text itself.
            Assert.DoesNotContain("sigla", w.Text);
        }

        [Fact]
        public void A_window_opening_inside_a_note_emits_no_unmatched_brace()
        {
            var markers = BookMarkers.Build(NoteXml);

            // structuredNotes promises text that is clean and quotable, with the apparatus lifted out into
            // Notes. SplitBracedNotes passes a closing brace through when it never saw an opening one.
            var w = TeiPassageReader.ReadWindow(NoteXml, InsideTheNote(NoteXml), maxChars: 20,
                includeVariants: true, outputScript: Script.Devanagari, markers, structuredNotes: true);

            Assert.DoesNotContain("}", w.Text);
            Assert.DoesNotContain("{", w.Text);
        }

        // Paragraph numbering restarts at the second section, so a window spanning the seam runs
        // 55 -> 1 -> 2 -> 57 in document order. "paragraphs 55-57" would name three paragraphs where the
        // window holds four, two of them numbered outside the range.
        private const string RestartXml =
            "<body>" +
            "<div id=\"dn1\" type=\"chapter\">" +
            "<p rend=\"bodytext\" n=\"55\">alpha\u0964</p>" +
            "</div>" +
            "<div id=\"dn2\" type=\"chapter\">" +
            "<p rend=\"bodytext\" n=\"1\">bravo\u0964</p>" +
            "<p rend=\"bodytext\" n=\"2\">charlie\u0964</p>" +
            "<p rend=\"bodytext\" n=\"57\">delta\u0964</p>" +
            "</div></body>";

        [Fact]
        public void Paragraph_numbering_that_restarts_is_not_a_contiguous_range()
        {
            var markers = BookMarkers.Build(RestartXml);

            // Positional, not numeric: 55 < 57, so any check that compares only the endpoints says yes.
            Assert.False(markers.ParagraphsRunContiguously(0, RestartXml.Length));
        }

        [Fact]
        public void Ascending_paragraph_numbers_within_one_section_are_a_contiguous_range()
        {
            var markers = BookMarkers.Build(RestartXml);

            int from = markers.PositionOfParagraph(1);
            int to = markers.PositionOfParagraph(57);

            // 1 -> 2 with nothing out of order between them.
            Assert.True(markers.ParagraphsRunContiguously(from, to));
        }

        [Fact]
        public void A_window_that_crosses_the_restart_reports_itself_as_discontinuous()
        {
            var markers = BookMarkers.Build(RestartXml);

            // From the paragraph itself, not position 0 - the window has to OPEN at 55 for its citation to
            // claim 55 as the start.
            int from = markers.PositionOfParagraph(55);
            var w = TeiPassageReader.ReadWindow(RestartXml, from, maxChars: 5000, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Equal(55, w.ParagraphNumber);
            Assert.Equal(57, w.EndParagraphNumber);
            Assert.False(w.ParagraphsContiguous);
        }

        [Fact]
        public void A_range_is_only_claimed_where_the_numbering_runs_straight_through()
        {
            // The citation surface B renders beside a generated answer, as the app's own attestation of what
            // the window covers. A range asserts everything between its ends.
            Assert.Equal("paragraphs 55-57 (dn1)",
                PassageTool.Describe(55, "dn1", 57, "dn1", contiguous: true));
        }

        [Fact]
        public void A_citation_across_a_restart_names_both_ends_and_says_it_is_not_a_range()
        {
            // "paragraphs 55-57" here would name three paragraphs where the window holds four, two of them
            // numbered outside the stated range - and would read as entirely ordinary while doing it.
            var cite = PassageTool.Describe(55, "dn1", 57, "dn2", contiguous: false);

            Assert.DoesNotContain("55-57", cite);
            Assert.Contains("paragraph 55 (dn1)", cite);
            Assert.Contains("paragraph 57 (dn2)", cite);
            Assert.Contains("not a continuous range", cite);
        }

        [Fact]
        public void Equal_paragraph_numbers_across_a_sub_book_break_are_not_collapsed_to_one_end()
        {
            // Numbering restarts per sub-book, so a Multi-book window can open at para 5 of one and close at
            // para 5 of the next. Collapsing on the numbers alone would cite "paragraph 5 (an5)" and lose
            // both the far sub-book and the fact that the two are not the same paragraph. (ultrareview)
            var cite = PassageTool.Describe(5, "an5", 5, "an6", contiguous: false);

            Assert.Contains("paragraph 5 (an5)", cite);
            Assert.Contains("paragraph 5 (an6)", cite);
            Assert.Contains("not a continuous range", cite);
        }

        [Fact]
        public void One_paragraph_is_never_described_as_a_range()
        {
            Assert.Equal("paragraph 55 (dn1)", PassageTool.Describe(55, "dn1", 55, "dn1"));
            Assert.Equal("paragraph 55", PassageTool.Describe(55, null));
            Assert.Equal("start of book", PassageTool.Describe(null, null));
        }

        [Fact]
        public void A_window_inside_one_section_reports_itself_as_continuous()
        {
            var markers = BookMarkers.Build(RestartXml);

            int from = markers.PositionOfParagraph(1);
            var w = TeiPassageReader.ReadWindow(RestartXml, from, maxChars: 5000, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.True(w.ParagraphsContiguous);
        }
    }
}
