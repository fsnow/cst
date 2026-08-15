using System.Linq;
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

        // ---- The window's pages are its own, not just its first (#561) --------------------------------

        // A book whose text crosses a page break inside one paragraph, so a single window spans two pages.
        private const string CrossingXml =
            "<body><div id=\"dn1\" type=\"book\">" +
            "<pb ed=\"V\" n=\"1.0001\"/>" +
            "<p rend=\"bodytext\" n=\"5\">alpha bravo\u0964 charlie delta\u0964" +
            "<pb ed=\"V\" n=\"1.0002\"/> echo foxtrot\u0964 golf hotel\u0964</p>" +
            "</div></body>";

        [Fact]
        public void ReadWindow_reports_every_page_its_window_covers()
        {
            // THE #561 behaviour, asserted through the reader rather than through BookMarkers alone. Without
            // this the wiring is unguarded: reverting TeiPassageReader to RefsAt(readStart).Pages leaves the
            // whole suite green, because every other test of this behaviour calls PagesAcross directly and
            // the existing page assertion here uses Contains, which a single-page result satisfies. So the
            // agreement between /v1/occurrences and /v1/passage — the entire point of the fix — could
            // regress in a refactor with nothing to show for it. (fable review)
            var markers = BookMarkers.Build(CrossingXml);
            int start = markers.PositionOfParagraph(5);

            var window = TeiPassageReader.ReadWindow(CrossingXml, start, maxChars: 200, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Contains("echo", window.Text);          // the window really does reach past the break
            Assert.Equal(new[] { 1, 2 }, window.Pages.Select(p => p.Number));
        }

        [Fact]
        public void ReadWindow_reports_one_page_when_it_crosses_no_break()
        {
            // The other half: a window inside a single page must not gain pages it does not cover.
            var markers = BookMarkers.Build(CrossingXml);
            int start = markers.PositionOfParagraph(5);

            var window = TeiPassageReader.ReadWindow(CrossingXml, start, maxChars: 15, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.DoesNotContain("echo", window.Text);    // stops before the break
            Assert.Equal(1, Assert.Single(window.Pages).Number);
        }

        [Fact]
        public void The_page_at_a_hit_is_among_the_pages_of_the_window_that_contains_it()
        {
            // #561 stated as the invariant it actually is, at the level the two endpoints meet: whatever
            // /v1/occurrences cites for a hit must appear in what /v1/passage cites for a window covering it.
            var markers = BookMarkers.Build(CrossingXml);
            int hit = CrossingXml.IndexOf("echo");         // sits AFTER the page break
            int start = markers.PositionOfParagraph(5);

            var atHit = markers.RefsAt(hit).Pages;
            var window = TeiPassageReader.ReadWindow(CrossingXml, start, maxChars: 200, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Equal(2, Assert.Single(atHit).Number);  // the hit is on the SECOND page
            Assert.All(atHit, h => Assert.Contains(window.Pages, p =>
                p.Edition == h.Edition && p.Volume == h.Volume && p.Number == h.Number));
        }

        // ---- Windows built AROUND a selection (#649) --------------------------------------------------

        /// <summary>
        /// Two sections, so a window built near a boundary has somewhere wrong to expand into. Sentences are
        /// single words plus a danda, which makes rendered lengths easy to reason about exactly.
        /// </summary>
        private const string TwoSections =
            "<body><div id=\"b\" type=\"book\">" +
            "<div id=\"s1\" type=\"sutta\"><p rend=\"bodytext\" n=\"1\">" +
            "aaa। bbb। ccc। ddd। eee। fff। ggg। hhh।" +
            "</p></div>" +
            "<div id=\"s2\" type=\"sutta\"><p rend=\"bodytext\" n=\"2\">" +
            "zzz। yyy। xxx। www।" +
            "</p></div>" +
            "</div></body>";

        private static (int Start, int End) Span(string xml, string needle)
        {
            var at = xml.IndexOf(needle, System.StringComparison.Ordinal);
            Assert.True(at >= 0, $"fixture does not contain '{needle}'");
            return (at, at + needle.Length);
        }

        [Fact]
        public void A_window_around_a_selection_always_contains_the_selection()
        {
            // The invariant the whole revamp exists for. The window used to come from the reader's paragraph,
            // which is derived from SCROLL, so a selection near the bottom of the viewport routinely fell
            // outside the window built to explain it — and the app reported that as a caveat rather than as
            // the defect it was.
            var markers = BookMarkers.Build(TwoSections);

            foreach (var word in new[] { "aaa", "ddd", "hhh", "zzz", "www" })
            {
                var (from, to) = Span(TwoSections, word);
                var window = TeiPassageReader.ReadWindowAroundSelection(
                    TwoSections, from, to, maxChars: 20, includeVariants: false,
                    outputScript: Script.Devanagari, markers);

                Assert.Contains(word, window.Text);
            }
        }

        [Fact]
        public void Expansion_goes_roughly_half_backwards_and_half_forwards()
        {
            // "Centre it" is the whole point: a selection explained only by what follows it reads as though
            // the passage began there.
            var markers = BookMarkers.Build(TwoSections);
            var (from, to) = Span(TwoSections, "eee");

            var window = TeiPassageReader.ReadWindowAroundSelection(
                TwoSections, from, to, maxChars: 24, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Contains("eee", window.Text);
            Assert.Contains("ccc", window.Text);   // context behind
            Assert.Contains("ggg", window.Text);   // and ahead
        }

        [Fact]
        public void Expansion_never_crosses_a_div_into_the_next_section()
        {
            // Text from the next sutta is not this passage's context, however close it sits in the file.
            var markers = BookMarkers.Build(TwoSections);
            var (from, to) = Span(TwoSections, "hhh");

            var window = TeiPassageReader.ReadWindowAroundSelection(
                TwoSections, from, to, maxChars: 200, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Contains("hhh", window.Text);
            Assert.Contains("aaa", window.Text);        // the budget is spent backwards instead
            Assert.DoesNotContain("zzz", window.Text);  // never forwards past the boundary
        }

        [Fact]
        public void A_budget_the_backward_side_cannot_spend_is_handed_forward()
        {
            // A selection at the very start of a section has nowhere behind it. Losing that half would give
            // the first passage of every sutta half the context of every other passage.
            var markers = BookMarkers.Build(TwoSections);
            var (from, to) = Span(TwoSections, "zzz");

            var window = TeiPassageReader.ReadWindowAroundSelection(
                TwoSections, from, to, maxChars: 24, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Contains("zzz", window.Text);
            Assert.Contains("yyy", window.Text);
            Assert.Contains("xxx", window.Text);   // reached only because the backward half was redirected

            // And it was redirected rather than spent: what lies behind is the previous section, which is not
            // this passage's context. The bound holds in BOTH directions.
            Assert.DoesNotContain("hhh", window.Text);
        }

        [Fact]
        public void A_large_budget_cannot_reach_backwards_into_the_previous_section()
        {
            // The backward twin of the forward case. A budget big enough to swallow the previous sutta must
            // stop at the boundary and simply spend the rest forwards.
            var markers = BookMarkers.Build(TwoSections);
            var (from, to) = Span(TwoSections, "yyy");

            var window = TeiPassageReader.ReadWindowAroundSelection(
                TwoSections, from, to, maxChars: 400, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Contains("yyy", window.Text);
            Assert.Contains("zzz", window.Text);       // its own section, behind it
            Assert.Contains("www", window.Text);       // and ahead
            Assert.DoesNotContain("hhh", window.Text); // never the section before
            Assert.DoesNotContain("aaa", window.Text);
        }

        [Fact]
        public void A_selection_spanning_two_sections_is_kept_whole_and_each_side_expands_in_its_own()
        {
            // The reader dragged across the boundary, so both sides are what they are asking about and the
            // window must carry all of it. What must not cross is the EXPANSION: the backward floor comes
            // from the section holding the selection's start, the forward ceiling from the one holding its
            // end, so neither side reaches into a section the selection never touched.
            const string three =
                "<body><div id=\"b\" type=\"book\">" +
                "<div id=\"s1\" type=\"sutta\"><p rend=\"bodytext\" n=\"1\">aaa। bbb। ccc।</p></div>" +
                "<div id=\"s2\" type=\"sutta\"><p rend=\"bodytext\" n=\"2\">ddd। eee। fff।</p></div>" +
                "<div id=\"s3\" type=\"sutta\"><p rend=\"bodytext\" n=\"3\">ggg। hhh। iii।</p></div>" +
                "</div></body>";
            var markers = BookMarkers.Build(three);

            // Starts in section 1, ends in section 2.
            var from = three.IndexOf("ccc", System.StringComparison.Ordinal);
            var to = three.IndexOf("ddd", System.StringComparison.Ordinal) + 3;

            var window = TeiPassageReader.ReadWindowAroundSelection(
                three, from, to, maxChars: 400, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            // All of the selection, across the boundary.
            Assert.Contains("ccc", window.Text);
            Assert.Contains("ddd", window.Text);

            // Expansion within each side's own section.
            Assert.Contains("aaa", window.Text);   // backwards, inside section 1
            Assert.Contains("fff", window.Text);   // forwards, inside section 2

            // But never into a section the selection never touched.
            Assert.DoesNotContain("ggg", window.Text);
        }

        [Fact]
        public void A_selection_larger_than_the_budget_is_never_trimmed_to_fit()
        {
            // The subject of the request must not be cut to make room for its own context. Trimming it would
            // answer about part of what the reader pointed at while captioning it as all of it.
            var markers = BookMarkers.Build(TwoSections);
            var (from, _) = Span(TwoSections, "aaa");
            var (_, to) = Span(TwoSections, "hhh");

            var window = TeiPassageReader.ReadWindowAroundSelection(
                TwoSections, from, to, maxChars: 5, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Contains("aaa", window.Text);
            Assert.Contains("hhh", window.Text);
        }

        [Fact]
        public void A_book_with_no_div_markup_is_one_undivided_section()
        {
            // Structural markup covers part of the corpus and is still being added, so "no div" is an
            // ordinary state, not a defect — and it must not cost the passage its context.
            const string undivided =
                "<body><p rend=\"bodytext\" n=\"1\">aaa। bbb। ccc। ddd। eee।</p></body>";
            var markers = BookMarkers.Build(undivided);
            var (from, to) = Span(undivided, "ccc");

            var window = TeiPassageReader.ReadWindowAroundSelection(
                undivided, from, to, maxChars: 40, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Contains("aaa", window.Text);
            Assert.Contains("ccc", window.Text);
            Assert.Contains("eee", window.Text);
        }

        [Fact]
        public void The_window_still_reports_its_citation_and_pages()
        {
            // A selection-anchored window is still a window: whatever cites it must describe what was sent.
            var markers = BookMarkers.Build(Xml);
            var (from, to) = Span(Xml, "echo");

            var window = TeiPassageReader.ReadWindowAroundSelection(
                Xml, from, to, maxChars: 30, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Equal(5, window.ParagraphNumber);
            Assert.Contains(window.Pages, p => p.Edition == PageEdition.Vri && p.Number == 1);
        }
    }
}
