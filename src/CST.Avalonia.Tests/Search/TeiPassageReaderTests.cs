using CST.Conversion;
using CST.Navigation;
using CST.Search;
using Xunit;

namespace CST.Avalonia.Tests.Search
{
    /// <summary>
    /// Tests for the bounded paged reading window. ASCII placeholder words + danda escapes; Script.Devanagari
    /// output (identity) so the window/paging can be asserted exactly.
    /// </summary>
    public class TeiPassageReaderTests
    {
        private const string Xml =
            "<body><div id=\"dn1\" type=\"book\">" +
            "<pb ed=\"V\" n=\"1.0001\"/>" +
            "<p rend=\"bodytext\" n=\"5\">alpha bravo\u0964 charlie delta\u0964 echo foxtrot\u0964 golf hotel\u0964</p>" +
            "</div></body>";

        [Fact]
        public void ReadWindow_is_bounded_and_pages_forward()
        {
            var markers = BookMarkers.Build(Xml);
            int start = markers.PositionOfParagraph(5);
            Assert.True(start >= 0);

            var w1 = TeiPassageReader.ReadWindow(Xml, start, maxChars: 15, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Contains("alpha", w1.Text);
            Assert.DoesNotContain("echo", w1.Text);        // beyond the budgeted window
            Assert.NotNull(w1.NextCursor);                 // more to read
            Assert.Equal(5, w1.ParagraphNumber);
            Assert.Contains(w1.Pages, p => p.Edition == PageEdition.Vri && p.Volume == 1 && p.Number == 1);

            // Page forward from the cursor.
            var w2 = TeiPassageReader.ReadWindow(Xml, w1.NextCursor!.Value, maxChars: 15, includeVariants: false,
                outputScript: Script.Devanagari, markers);
            Assert.Contains("echo", w2.Text);
            Assert.DoesNotContain("alpha", w2.Text);
        }

        [Fact]
        public void ReadWindow_drops_the_space_a_stripped_inline_tag_leaves_before_punctuation()
        {
            // A page-break tag sits between a word and its comma; stripping it must not leave "alpha ," (#292).
            const string xml =
                "<body><div id=\"dn1\" type=\"book\">" +
                "<pb ed=\"V\" n=\"1.0001\"/>" +
                "<p rend=\"bodytext\" n=\"5\">alpha<pb ed=\"V\" n=\"1.0002\"/>, bravo charlie\u0964</p>" +
                "</div></body>";
            var markers = BookMarkers.Build(xml);
            int start = markers.PositionOfParagraph(5);
            var w = TeiPassageReader.ReadWindow(xml, start, maxChars: 200, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Contains("alpha,", w.Text);        // punctuation hugs the word...
            Assert.DoesNotContain("alpha ,", w.Text);  // ...no stray space from the stripped <pb/>
        }

        [Fact]
        public void ReadWindow_wraps_included_notes_in_braces()
        {
            const string xml =
                "<body><div id=\"dn1\" type=\"book\">" +
                "<pb ed=\"V\" n=\"1.0001\"/>" +
                "<p rend=\"bodytext\" n=\"5\">alpha bravo <note>VAR (si)</note> charlie</p>" +
                "</div></body>";
            var markers = BookMarkers.Build(xml);
            int start = markers.PositionOfParagraph(5);

            var off = TeiPassageReader.ReadWindow(xml, start, maxChars: 10000, includeVariants: false,
                outputScript: Script.Devanagari, markers);
            Assert.DoesNotContain("VAR", off.Text);   // note stripped by default
            Assert.DoesNotContain("{", off.Text);
            Assert.Equal(1, off.NoteCount);            // ...but noteCount reports it's there (#293)

            var on = TeiPassageReader.ReadWindow(xml, start, maxChars: 10000, includeVariants: true,
                outputScript: Script.Devanagari, markers);
            int open = on.Text.IndexOf('{');
            int close = on.Text.IndexOf('}');
            Assert.True(open >= 0 && close > open, "note should be wrapped in { }");
            Assert.Contains("VAR", on.Text.Substring(open, close - open));
            Assert.Equal(1, on.NoteCount);             // same count whether or not it's rendered
        }

        [Fact]
        public void ReadWindow_structuredNotes_returns_brace_free_text_plus_parsed_notes()
        {
            const string xml =
                "<body><div id=\"dn1\" type=\"book\">" +
                "<pb ed=\"V\" n=\"1.0001\"/>" +
                "<p rend=\"bodytext\" n=\"5\">alpha bravo <note>varreading (si)</note> charlie</p>" +
                "</div></body>";
            var markers = BookMarkers.Build(xml);
            int start = markers.PositionOfParagraph(5);

            var w = TeiPassageReader.ReadWindow(xml, start, maxChars: 10000, includeVariants: false,
                outputScript: Script.Latin, markers, structuredNotes: true);

            // Text is clean/quotable: no braces, and the apparatus reading is NOT in the base text.
            Assert.DoesNotContain("{", w.Text);
            Assert.DoesNotContain("}", w.Text);
            Assert.DoesNotContain("varreading", w.Text);
            Assert.Contains("alpha bravo", w.Text);
            Assert.Contains("charlie", w.Text);

            // The note is returned as data, parsed into reading + sigla, anchored inside the text.
            var note = Assert.Single(w.Notes);
            Assert.Equal("varreading", note.Reading);
            Assert.Equal("si", note.Sigla);
            Assert.Contains("(si)", note.Text);
            Assert.InRange(note.Offset, 0, w.Text.Length);
        }

        [Fact]
        public void ReadWindow_structuredNotes_leaves_reading_sigla_null_for_a_non_variant_note()
        {
            const string xml =
                "<body><div id=\"dn1\" type=\"book\">" +
                "<p rend=\"bodytext\" n=\"5\">alpha <note>see also elsewhere</note> bravo</p>" +
                "</div></body>";
            var markers = BookMarkers.Build(xml);
            int start = markers.PositionOfParagraph(5);

            var w = TeiPassageReader.ReadWindow(xml, start, maxChars: 10000, includeVariants: false,
                outputScript: Script.Latin, markers, structuredNotes: true);

            var note = Assert.Single(w.Notes);
            Assert.Null(note.Reading);          // no "reading (sigla)" shape
            Assert.Null(note.Sigla);
            Assert.Contains("see also", note.Text);
        }

        [Fact]
        public void ReadWindow_structuredNotes_hugs_punctuation_after_an_excised_note()
        {
            // 41% of corpus notes are immediately followed by punctuation; excising the note must not leave a
            // stray space before it in the "clean, quotable" text. (#267 review, Defect 1)
            const string xml =
                "<body><div id=\"dn1\" type=\"book\">" +
                "<p rend=\"bodytext\" n=\"5\">alpha <note>varreading (si)</note>, bravo</p>" +
                "</div></body>";
            var markers = BookMarkers.Build(xml);
            int start = markers.PositionOfParagraph(5);

            var w = TeiPassageReader.ReadWindow(xml, start, maxChars: 10000, includeVariants: false,
                outputScript: Script.Latin, markers, structuredNotes: true);

            Assert.DoesNotContain(" ,", w.Text);          // no stray space before the comma
            Assert.Contains("alpha, bravo", w.Text);
            Assert.Equal("si", Assert.Single(w.Notes).Sigla);
        }

        [Fact]
        public void ReadWindow_structuredNotes_never_cuts_mid_note_even_with_footnotes()
        {
            // A tiny budget + includeVariants would let the window end inside a note; a structured window must
            // still return brace-free text (no unmatched '{'/'}'). (#267 review, Defect 2)
            const string xml =
                "<body><div id=\"dn1\" type=\"book\">" +
                "<p rend=\"bodytext\" n=\"5\">alpha bravo <note>a very long apparatus note that exceeds the budget (si)</note> charlie</p>" +
                "</div></body>";
            var markers = BookMarkers.Build(xml);
            int start = markers.PositionOfParagraph(5);

            var w = TeiPassageReader.ReadWindow(xml, start, maxChars: 15, includeVariants: true,
                outputScript: Script.Latin, markers, structuredNotes: true);

            Assert.DoesNotContain("{", w.Text);
            Assert.DoesNotContain("}", w.Text);
        }

        [Fact]
        public void ReadWindow_without_structuredNotes_has_no_notes()
        {
            const string xml =
                "<body><div id=\"dn1\" type=\"book\">" +
                "<p rend=\"bodytext\" n=\"5\">alpha <note>varreading (si)</note> bravo</p>" +
                "</div></body>";
            var markers = BookMarkers.Build(xml);
            int start = markers.PositionOfParagraph(5);

            var w = TeiPassageReader.ReadWindow(xml, start, maxChars: 10000, includeVariants: true,
                outputScript: Script.Latin, markers);   // structuredNotes defaults false
            Assert.Empty(w.Notes);
        }

        [Fact]
        public void ReadWindow_cursor_snaps_start_back_to_the_enclosing_sentence()
        {
            // A cursor from `occurrences` points AT the hit, mid-sentence. The window START must snap back to the
            // enclosing sentence so the hit is read with its head, not from the hit. (Desktop MCP friction, P1)
            var markers = BookMarkers.Build(Xml);
            int cursor = Xml.IndexOf("foxtrot", System.StringComparison.Ordinal);   // mid 3rd sentence "echo foxtrot"
            Assert.True(cursor > 0);

            // Without snapping, the window opens at the hit - the sentence head "echo" is lost.
            var raw = TeiPassageReader.ReadWindow(Xml, cursor, maxChars: 100, includeVariants: false,
                outputScript: Script.Devanagari, markers, snapStartToSentence: false);
            Assert.Contains("foxtrot", raw.Text);
            Assert.DoesNotContain("echo", raw.Text);

            // With snapping, the window starts at the sentence start "echo"...
            var snapped = TeiPassageReader.ReadWindow(Xml, cursor, maxChars: 100, includeVariants: false,
                outputScript: Script.Devanagari, markers, snapStartToSentence: true);
            Assert.Contains("echo", snapped.Text);
            Assert.Contains("foxtrot", snapped.Text);
            // ...but does NOT bleed back across the sentence boundary into the previous sentence.
            Assert.DoesNotContain("charlie", snapped.Text);
        }

        [Fact]
        public void ReadWindow_large_budget_reaches_end()
        {
            var markers = BookMarkers.Build(Xml);
            int start = markers.PositionOfParagraph(5);

            var w = TeiPassageReader.ReadWindow(Xml, start, maxChars: 10000, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Contains("hotel", w.Text);
            Assert.Null(w.NextCursor);                     // reached the end of the book
        }
    }
}
