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
            Assert.Contains("ggg", window.Text);        // two sentences of context behind it
            Assert.Contains("fff", window.Text);
            Assert.DoesNotContain("eee", window.Text);  // and only two
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

            // And it was redirected rather than spent: what lies behind is the previous section, which is not
            // this passage's context. The bound holds in BOTH directions.
            Assert.DoesNotContain("hhh", window.Text);
        }

        [Fact]
        public void A_selection_opening_a_section_gets_its_context_from_ahead()
        {
            // There is nothing behind a selection that opens a section, and the previous sutta is not an
            // answer. Under the character budget this was a redistribution question — the unspendable
            // backward half went forward. Under sentence counts it is simpler: the backward walk finds no
            // boundary inside the section and yields nothing, the forward walk still takes its two. (#672)
            const string longTail =
                "<body><div id=\"b\" type=\"book\">" +
                "<div id=\"s1\" type=\"sutta\"><p rend=\"bodytext\" n=\"1\">aaa। bbb।</p></div>" +
                "<div id=\"s2\" type=\"sutta\"><p rend=\"bodytext\" n=\"2\">" +
                "ccc। ddd। eee। fff। ggg। hhh। iii। jjj। kkk।" +
                "</p></div></div></body>";
            var markers = BookMarkers.Build(longTail);
            var (from, to) = Span(longTail, "ccc");

            // "ccc" opens s2, so the backward walk has nothing to find inside the section.
            var window = TeiPassageReader.ReadWindowAroundSelection(
                longTail, from, to, maxChars: 40, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Contains("ccc", window.Text);
            Assert.Contains("ddd", window.Text);          // two sentences ahead
            Assert.Contains("eee", window.Text);
            Assert.DoesNotContain("fff", window.Text);    // and only two
            Assert.DoesNotContain("bbb", window.Text);    // still never backwards across the boundary
        }

        [Fact]
        public void The_backward_walk_cannot_run_away_when_there_is_no_punctuation_behind_it()
        {
            // Originally found by Fable against the character budget; the hazard survives the move to
            // sentence counts (#672) and so does the guard. Front matter — a title, a nikaya heading — carries
            // no danda at all, so "back two sentences" has no boundary to find and would walk to the start of
            // the section, which in a book with no div markup is position 0. SentenceScanCap bounds each hop.
            var filler = new string('x', 6000);
            var xml = $"<body><p rend=\"bodytext\" n=\"1\">{filler} target। tail।</p></body>";
            var markers = BookMarkers.Build(xml);
            var (from, to) = Span(xml, "target");

            var window = TeiPassageReader.ReadWindowAroundSelection(
                xml, from, to, maxChars: 40_000, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Contains("target", window.Text);
            // Bounded rather than exact: the scan cap is per hop, and the window may overshoot to finish the
            // sentence it ends inside. What must not happen is the whole 2,000-character filler coming back.
            // The scan cap is 2,000 characters per hop — derived from the corpus's own sentence lengths — so
            // the window is bounded near that, not by the 6,000-character run it sits in.
            Assert.True(window.Text.Length < 3000, $"window ran away: {window.Text.Length} chars");
        }

        [Fact]
        public void The_sentence_the_selection_sits_in_is_finished_even_with_no_budget_left()
        {
            // Also Fable's: WalkForward's hard cap is 1.5x ITS budget, so once the backward snap has spent
            // the whole shortfall the forward cap is zero and it returned after a single character — cutting
            // mid-sentence, which is the one thing this class promises never to do, in the case where the
            // reader most needs the sentence whole because it is the subject of the request.
            const string xml =
                "<body><p rend=\"bodytext\" n=\"1\">aaaa bbbb cccc dddd। ee ff gg hh ii jj kk।</p></body>";
            var markers = BookMarkers.Build(xml);
            var (from, to) = Span(xml, "ee");

            var window = TeiPassageReader.ReadWindowAroundSelection(
                xml, from, to, maxChars: 5, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Contains("ee", window.Text);
            Assert.Contains("kk", window.Text);   // the sentence runs to its end
        }

        [Fact]
        public void A_selection_at_the_very_end_of_a_section_still_respects_its_boundary()
        {
            // The boundary case that does occur: the last words of a section, where the forward walk runs
            // straight into the closing tags and the backward one has the whole section behind it.
            var markers = BookMarkers.Build(TwoSections);
            var (from, to) = Span(TwoSections, "www");

            var window = TeiPassageReader.ReadWindowAroundSelection(
                TwoSections, from, to, maxChars: 400, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Contains("www", window.Text);
            Assert.Contains("xxx", window.Text);        // its own section, behind it
            Assert.Contains("yyy", window.Text);
            Assert.DoesNotContain("zzz", window.Text);  // two sentences, not the whole section
            Assert.DoesNotContain("hhh", window.Text);  // never back into the previous one
        }

        [Fact]
        public void A_fallback_window_is_cited_from_its_own_section_not_the_one_before()
        {
            // Found by Fable, and the reason the first attempt at this did not work: every div in the real
            // corpus opens with a <head> whose TEXT precedes the section's first <p n=...> marker. Skipping
            // tags cannot get past text, so the window start stayed behind the marker and the citation was
            // resolved from the last marker BEHIND it — the previous sutta's. The passage came back citing a
            // range opening in a section it contained none of, which is worse than citing nothing.
            //
            // The backward fallback fires here because "second" has no danda behind it inside its own section.
            const string withHeads =
                "<body><div id=\"b\" type=\"book\">\r\n" +
                "<div id=\"s1\" type=\"sutta\">\r\n<head rend=\"chapter\">Chapter One</head>\r\n" +
                "<p rend=\"bodytext\" n=\"7\">old one\u0964 old two\u0964</p></div>\r\n" +
                "<div id=\"s2\" type=\"sutta\">\r\n<head rend=\"chapter\">Chapter Two</head>\r\n" +
                "<p rend=\"bodytext\" n=\"8\">first line\u0964 second line\u0964 third line\u0964</p></div>\r\n" +
                "</div></body>";
            var markers = BookMarkers.Build(withHeads);
            var (from, to) = Span(withHeads, "second line");

            var window = TeiPassageReader.ReadWindowAroundSelection(
                withHeads, from, to, maxChars: 4000, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Equal(8, window.ParagraphNumber);
            Assert.DoesNotContain("old", window.Text);        // never back into the previous sutta
            Assert.Contains("second line", window.Text);
        }

        // ---- The selection cap (#672) -------------------------------------------------------------------

        [Fact]
        public void A_selection_past_the_cap_is_cut_and_SAYS_it_was_cut()
        {
            // The ctrl+A case. Cutting is the lesser evil; cutting SILENTLY is not — an answer about the first
            // 20,000 characters, captioned as being about everything the reader selected, is the failure this
            // class avoids everywhere else.
            var body = string.Concat(Enumerable.Repeat("word\u0964 ", 6000));   // ~36,000 chars
            var xml = $"<body><p rend=\"bodytext\" n=\"1\">{body}</p></body>";
            var markers = BookMarkers.Build(xml);

            var window = TeiPassageReader.ReadWindowAroundSelection(
                xml, xml.IndexOf(body, System.StringComparison.Ordinal), xml.Length - 14,
                maxChars: 100_000, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.True(window.SelectionTruncated);
            Assert.True(window.Text.Length <= TeiPassageReader.MaxSelectionChars + 4000,
                $"cap did not bind: {window.Text.Length} chars");
        }

        [Fact]
        public void A_selection_within_the_cap_is_not_reported_as_cut()
        {
            var markers = BookMarkers.Build(TwoSections);
            var (from, to) = Span(TwoSections, "ccc");

            var window = TeiPassageReader.ReadWindowAroundSelection(
                TwoSections, from, to, maxChars: 400, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.False(window.SelectionTruncated);
        }

        [Fact]
        public void The_cap_never_stops_inside_a_note()
        {
            // Found by Fable. RawForwardCap counts characters and lands wherever it lands; inside a <note>
            // that leaves an unbalanced brace in supposedly brace-free text, renders the note's tail as base
            // text, and loses the note from the structured list — and the dandas in that tail are then read as
            // base-text sentence ends, because NoteRegions only sees notes that START in the range it is given.
            var filler = string.Concat(Enumerable.Repeat("aa\u0964 ", 30));
            var noteBody = string.Concat(Enumerable.Repeat("nn\u0964 ", 60));
            var xml = $"<body><p rend=\"bodytext\" n=\"1\">{filler}<note>{noteBody}</note> tail\u0964</p></body>";
            var markers = BookMarkers.Build(xml);
            int from = xml.IndexOf(filler, System.StringComparison.Ordinal);

            // A cap that lands part-way through the note.
            var window = TeiPassageReader.ReadWindowAroundSelection(
                xml, from, xml.Length - 14, maxChars: 100_000, includeVariants: true,
                outputScript: Script.Devanagari, markers,
                selectionCap: filler.Length + (noteBody.Length / 2));

            Assert.True(window.SelectionTruncated);
            // Balanced or absent, never half-open.
            Assert.Equal(
                window.Text.Count(c => c == '{'),
                window.Text.Count(c => c == '}'));
        }

        // ---- Locating the selection in the raw XML -----------------------------------------------------

        [Fact]
        public void A_selection_is_located_in_the_raw_xml()
        {
            const string xml = "<body><p rend=\"bodytext\" n=\"1\">alpha bravo charlie delta।</p></body>";

            var span = TeiPassageReader.LocateSelection(xml, 0, xml.Length, "bravo charlie");

            Assert.NotNull(span);
            Assert.Equal("bravo charlie", xml.Substring(span!.Value.Start, span.Value.End - span.Value.Start));
        }

        [Fact]
        public void A_selection_matches_across_markup_the_renderer_drops()
        {
            // A paragraph number or a note sits in the middle of what the reader dragged across. The renderer
            // drops both, so the selection text has no trace of them and a naive substring search fails on
            // exactly the selections most likely to be made — the ones starting at the top of a paragraph.
            const string xml =
                "<body><p rend=\"bodytext\" n=\"1\">" +
                "<hi rend=\"paranum\">12</hi><hi rend=\"dot\">.</hi>alpha <note>vl (si)</note>bravo charlie।" +
                "</p></body>";

            var span = TeiPassageReader.LocateSelection(xml, 0, xml.Length, "alpha bravo");

            Assert.NotNull(span);
            var raw = xml.Substring(span!.Value.Start, span.Value.End - span.Value.Start);
            Assert.StartsWith("alpha", raw);
            Assert.EndsWith("bravo", raw);
        }

        [Fact]
        public void A_selection_matches_across_line_wrapping()
        {
            // The XML is wrapped; the DOM hands back a single space. Comparing raw whitespace would miss.
            const string xml = "<body><p rend=\"bodytext\" n=\"1\">alpha\n    bravo charlie।</p></body>";

            Assert.NotNull(TeiPassageReader.LocateSelection(xml, 0, xml.Length, "alpha bravo"));
        }

        [Fact]
        public void A_selection_outside_the_searched_region_is_not_located()
        {
            // The bound is what keeps a formulaic phrase — this corpus repeats them verbatim — from matching
            // somewhere the reader has never been.
            const string xml =
                "<body><p rend=\"bodytext\" n=\"1\">alpha bravo।</p>" +
                "<p rend=\"bodytext\" n=\"2\">alpha bravo।</p></body>";
            var second = xml.LastIndexOf("alpha", System.StringComparison.Ordinal);

            // Searching only the second paragraph finds the second occurrence, not the first.
            var span = TeiPassageReader.LocateSelection(xml, second - 5, xml.Length, "alpha bravo");

            Assert.NotNull(span);
            Assert.True(span!.Value.Start >= second - 5);
        }

        [Fact]
        public void A_selection_opening_a_section_never_draws_context_from_the_one_before()
        {
            // The reported case. The selection was the first sentence of the first paragraph of a sutta, and
            // the context reached back into the previous sutta -- rule 4, "do not cross a div boundary,
            // because that gets into a different section/sutta/book".
            const string twoSuttas =
                "<body><div id=\"b\" type=\"book\">" +
                "<div id=\"s1\" type=\"sutta\"><p rend=\"bodytext\" n=\"330\">" +
                "old one\u0964 old two\u0964 old three\u0964 old four\u0964</p></div>" +
                "<div id=\"s2\" type=\"sutta\"><p rend=\"bodytext\" n=\"331\">" +
                "opening line\u0964 second line\u0964 third line\u0964</p></div>" +
                "</div></body>";
            var markers = BookMarkers.Build(twoSuttas);
            var (from, to) = Span(twoSuttas, "opening line");

            var window = TeiPassageReader.ReadWindowAroundSelection(
                twoSuttas, from, to, maxChars: 400, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Contains("opening line", window.Text);
            Assert.Contains("third line", window.Text);      // the unspendable backward half goes forward
            Assert.DoesNotContain("old four", window.Text);  // and never back across the boundary
            Assert.DoesNotContain("old one", window.Text);
        }

        [Fact]
        public void Attribute_text_is_never_matchable()
        {
            // A selection opening "331." matched the n="331" of the paragraph's own <p> element and returned
            // a span beginning inside markup. The needle here appears ONLY inside an attribute, so a match of
            // any kind proves the scanner is reading markup as text.
            const string xml =
                "<body><p rend=\"bodytext\" n=\"331\">alpha bravo\u0964</p></body>";

            Assert.Null(TeiPassageReader.LocateSelection(xml, 0, xml.Length, "bodytext"));
            Assert.Null(TeiPassageReader.LocateSelection(xml, 0, xml.Length, "331"));

            // And real text is still found.
            Assert.NotNull(TeiPassageReader.LocateSelection(xml, 0, xml.Length, "alpha bravo"));
        }

        [Fact]
        public void Punctuation_cannot_stop_a_selection_being_located()
        {
            // The reported failure, in miniature. The reader renders through a path that turns the danda into
            // a period, and the XML has the danda -- so a 276-character selection spanning several sentences
            // could not be found in the very paragraph it came from, and the window silently fell back to the
            // scroll position.
            const string xml = "<body><p rend=\"bodytext\" n=\"1\">alpha bravo\u0964 charlie delta\u0964</p></body>";

            // As the reader would hand it back: periods, not dandas.
            var span = TeiPassageReader.LocateSelection(xml, 0, xml.Length, "alpha bravo. charlie");

            Assert.NotNull(span);
            var raw = xml.Substring(span!.Value.Start, span.Value.End - span.Value.Start);
            Assert.StartsWith("alpha", raw);
            Assert.EndsWith("charlie", raw);
        }

        [Fact]
        public void Text_that_is_not_there_is_not_located()
        {
            const string xml = "<body><p rend=\"bodytext\" n=\"1\">alpha bravo।</p></body>";

            Assert.Null(TeiPassageReader.LocateSelection(xml, 0, xml.Length, "charlie delta"));
            Assert.Null(TeiPassageReader.LocateSelection(xml, 0, xml.Length, "   "));
            Assert.Null(TeiPassageReader.LocateSelection(xml, 0, xml.Length, ""));
        }

        [Fact]
        public void A_located_selection_is_inside_the_window_built_around_it()
        {
            // The two halves joined: locate, then build. This is the shape the bundler uses.
            const string xml =
                "<body><div id=\"b\" type=\"book\"><div id=\"s\" type=\"sutta\">" +
                "<p rend=\"bodytext\" n=\"1\">one। two। three। four। five। six।</p>" +
                "</div></div></body>";
            var markers = BookMarkers.Build(xml);

            var span = TeiPassageReader.LocateSelection(xml, 0, xml.Length, "four");
            Assert.NotNull(span);

            var window = TeiPassageReader.ReadWindowAroundSelection(
                xml, span!.Value.Start, span.Value.End, maxChars: 20, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Contains("four", window.Text);
        }

        [Theory]
        [InlineData(0)]
        [InlineData(1)]
        public void Degenerate_spans_do_not_throw(int maxChars)
        {
            // A public method has to survive its own degenerate inputs: an empty selection, one at position
            // zero, one past the end, and a budget of nothing. None of these is reachable from the panel; all
            // of them are reachable from a caller reading the signature.
            var markers = BookMarkers.Build(TwoSections);

            foreach (var (from, to) in new[] { (0, 0), (0, 1), (TwoSections.Length, TwoSections.Length),
                                               (TwoSections.Length + 50, TwoSections.Length + 90) })
            {
                var window = TeiPassageReader.ReadWindowAroundSelection(
                    TwoSections, from, to, maxChars, includeVariants: false,
                    outputScript: Script.Devanagari, markers);

                Assert.NotNull(window.Text);
            }
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
                Xml, from, to, maxChars: 400, includeVariants: false,
                outputScript: Script.Devanagari, markers);

            Assert.Equal(5, window.ParagraphNumber);
            Assert.Contains(window.Pages, p => p.Edition == PageEdition.Vri && p.Number == 1);
        }
    
        // ---- akṣara-aware cuts (#871) -------------------------------------------------------------------

        /// <summary>
        /// One Devanagari word in a paragraph with no danda, so the count-based cuts are the only ones that
        /// can fire. Rendered whole this is "ka buddhīkakakaka"; cut between the conjunct and its vowel sign
        /// it reads "ka buddha", which is why this bug is worth a test at all.
        /// </summary>
        private const string NoDanda =
            "<body><div id=\"dn1\" type=\"book\">" +
            "<p rend=\"bodytext\" n=\"5\">" +
            // ka + space + b-u-d-virama-dh-ii, then four more ka.
            "\u0915 \u092C\u0941\u0926\u094D\u0927\u0940\u0915\u0915\u0915\u0915</p>" +
            "</div></body>";

        /// <summary>Where the paragraph's TEXT begins. The paragraph marker points at the opening tag, which
        /// is zero-width forwards but not a position to do character arithmetic from.</summary>
        private static int TextStart(string xml) => xml.IndexOf('\u0915');

        /// <summary>
        /// The hard cap backs off a half-cut akṣara instead of inventing a word.
        ///
        /// <para>The budget ran out between the <c>dh</c> (U+0927) and its <c>ī</c> sign (U+0940), leaving a
        /// bare consonant that <c>Deva2Latn</c> then gave its inherent 'a' — so the window ended "…buddha"
        /// where the text says "buddhī". Not visible damage: a different, entirely plausible Pāli word, in
        /// the surface an agent quotes the corpus from.</para>
        /// </summary>
        [Fact]
        public void The_hard_cap_does_not_cut_an_aksara_in_half()
        {
            var markers = BookMarkers.Build(NoDanda);

            // maxChars 5 puts the hard cap (maxChars + maxChars/2) exactly on the vowel sign.
            var w = TeiPassageReader.ReadWindow(NoDanda, TextStart(NoDanda), maxChars: 5,
                includeVariants: false, outputScript: Script.Latin, markers);

            Assert.Equal("ka bu", w.Text);
            Assert.DoesNotContain("buddha", w.Text);
        }

        /// <summary>
        /// Paging across the cut stitches back to the text, which is the reason the fix retreats rather than
        /// advances: the window end IS the next window's cursor, so moving it back puts the whole akṣara at
        /// the head of the next page. Left where the count landed, page two opened on an orphaned vowel sign
        /// ("īkakakaka") and no amount of stitching recovered "buddhī".
        /// </summary>
        [Fact]
        public void Paging_across_a_half_cut_aksara_stitches_back_to_the_word()
        {
            var markers = BookMarkers.Build(NoDanda);

            var w1 = TeiPassageReader.ReadWindow(NoDanda, TextStart(NoDanda), maxChars: 5,
                includeVariants: false, outputScript: Script.Latin, markers);
            var w2 = TeiPassageReader.ReadWindow(NoDanda, w1.NextCursor!.Value, maxChars: 50,
                includeVariants: false, outputScript: Script.Latin, markers);

            Assert.StartsWith("ddhī", w2.Text);
            Assert.Equal("ka buddhīkakakaka", w1.Text + w2.Text);
        }

        /// <summary>
        /// A window narrower than the akṣara it starts on keeps the raw cut rather than coming back empty.
        ///
        /// <para>Retreating to nothing would make the window's end its own <c>NextCursor</c>, and a client
        /// paging forward would never advance — a hang is worse than the boundary letter this fix exists to
        /// get right.</para>
        /// </summary>
        [Fact]
        public void A_window_narrower_than_one_aksara_still_advances()
        {
            var markers = BookMarkers.Build(NoDanda);
            int start = TextStart(NoDanda) + 4;             // onto the d of the d-virama-dh conjunct

            var w = TeiPassageReader.ReadWindow(NoDanda, start, maxChars: 1, includeVariants: false,
                outputScript: Script.Latin, markers);

            Assert.NotEmpty(w.Text);
            Assert.True(w.NextCursor > start);
        }

        /// <summary>
        /// The previous-page cursor opens on an akṣara start too. With no sentence boundary behind it the
        /// backward walk falls back to a raw character count, which used to open the previous page on the
        /// orphaned vowel sign ("īkakakaka").
        /// </summary>
        [Fact]
        public void The_previous_page_cursor_does_not_open_mid_aksara()
        {
            var markers = BookMarkers.Build(NoDanda);

            var w = TeiPassageReader.ReadWindow(NoDanda, TextStart(NoDanda) + 11, maxChars: 4,
                includeVariants: false, outputScript: Script.Latin, markers);

            var prev = TeiPassageReader.ReadWindow(NoDanda, w.PrevCursor!.Value, maxChars: 50,
                includeVariants: false, outputScript: Script.Latin, markers);

            Assert.StartsWith("ddhī", prev.Text);
            Assert.False(prev.Text.StartsWith("ī"));        // the stranded matra
        }

        /// <summary>
        /// The selection path's cut is akṣara-aware as well.
        ///
        /// <para>It is <c>ExtendToSentenceEnd</c> that has to do this, not the selection clamp: every
        /// selection end is passed through the sentence extension, so the clamp's own cut never ends a
        /// window. Where a danda is near, the extension lands on it and there was never a problem; where none
        /// is — front matter, headings, a verse index, the danda-free text this reader already documents
        /// having to cope with — the extension gives up on its 4,000-character cap, and that cap is a raw
        /// count like any other.</para>
        /// </summary>
        [Fact]
        public void The_sentence_extension_cap_does_not_cut_an_aksara_in_half()
        {
            // Filler so the 4,000-char extension cap lands exactly on the vowel sign, and no danda anywhere
            // for it to prefer.
            const int Cap = 4000;
            string filler = new string('\u0915', Cap - 4);
            string xml =
                "<body><p rend=\"bodytext\" n=\"1\">"
                + filler + "\u092C\u0941\u0926\u094D\u0927\u0940" + new string('\u0915', 20)
                + "</p></body>";

            var markers = BookMarkers.Build(xml);
            int from = TextStart(xml);

            var w = TeiPassageReader.ReadWindowAroundSelection(
                xml, from, from + 1, maxChars: 100000, includeVariants: false,
                outputScript: Script.Latin, markers, contextSentences: 0);

            Assert.EndsWith("kabu", w.Text);
            Assert.DoesNotContain("buddha", w.Text);
        }
    }
}
