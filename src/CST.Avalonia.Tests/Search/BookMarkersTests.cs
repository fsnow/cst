using CST.Search;
using Xunit;

namespace CST.Avalonia.Tests.Search
{
    public class BookMarkersTests
    {
        [Fact]
        public void DistinctBookCodes_lists_each_sub_book_once_in_order()
        {
            // A Multi book: paragraphs live under <div type="book" id="..."> sub-books (#266).
            var xml =
                "<div type=\"book\" id=\"an5\"><p n=\"1\">a</p><p n=\"2\">b</p></div>" +
                "<div type=\"book\" id=\"an6\"><p n=\"1\">c</p></div>" +
                "<div type=\"book\" id=\"an7\"><p n=\"1\">d</p><p n=\"2\">e</p></div>";

            var codes = BookMarkers.Build(xml).DistinctBookCodes();

            Assert.Equal(new[] { "an5", "an6", "an7" }, codes);   // first-appearance order, de-duplicated
        }

        [Fact]
        public void DistinctBookCodes_is_empty_for_a_non_multi_book()
        {
            // No enclosing book div → paragraphs carry no sub-book code.
            var xml = "<div type=\"chapter\" id=\"c1\"><p n=\"1\">a</p><p n=\"2\">b</p></div>";
            Assert.Empty(BookMarkers.Build(xml).DistinctBookCodes());
        }

        // ---- Ranged paragraph @n (#446) ------------------------------------------------------------

        // 3,618 paragraphs across 87 of the 217 corpus files carry a non-integer @n. They used to fail
        // int.TryParse and be dropped from the index, which did not produce a MISSING citation but a WRONG
        // one: the lookup answered with the last paragraph it had indexed, i.e. the one before the block.

        [Theory]
        [InlineData("21", 21, 21)]           // the ordinary case, 97% of the corpus
        [InlineData("16-26", 16, 26)]        // a full range - vin02t.tik.xml
        [InlineData("196-7", 196, 197)]      // ABBREVIATED tail: 196..197, not 196..7
        [InlineData("266-7-8", 266, 268)]    // vin11t.nrf.xml; the next numbered paragraph there is 269
        [InlineData("292-3-6", 292, 296)]    // s0105t.nrf.xml; followed by 297
        [InlineData("18-19-20", 18, 20)]     // e0812n.nrf.xml, unabbreviated list form
        public void A_paragraph_number_spans_every_form_the_corpus_uses(string n, int first, int last)
        {
            Assert.True(BookMarkers.TryParseParagraphSpan(n, out var f, out var l));
            Assert.Equal(first, f);
            Assert.Equal(last, l);
        }

        [Fact]
        public void The_abbreviated_tail_is_expanded_not_taken_literally()
        {
            // The distinction the whole rule turns on. Read literally, "196-7" is a span from 196 down to 7
            // and would swallow 189 paragraphs; read as an abbreviation it is 196..197. 1,518 paragraphs use
            // this form, so getting it backwards would be worse than the bug being fixed.
            BookMarkers.TryParseParagraphSpan("196-7", out var f, out var l);

            Assert.Equal(196, f);
            Assert.Equal(197, l);
            Assert.Equal(2, l - f + 1);
        }

        [Theory]
        [InlineData("179-", 179, 179)]   // e0812n.nrf.xml - a real corpus typo; 179 is still a real paragraph
        public void A_trailing_hyphen_keeps_its_number_rather_than_being_dropped(string n, int first, int last)
        {
            // Discarding it would reintroduce exactly the defect this fixes: a paragraph missing from the
            // index means the NEXT lookup answers with an earlier one and calls it correct.
            Assert.True(BookMarkers.TryParseParagraphSpan(n, out var f, out var l));
            Assert.Equal(first, f);
            Assert.Equal(last, l);
        }

        [Theory]
        [InlineData("-")]        // e1207n.nrf.xml - a bare hyphen, no number at all
        [InlineData("")]
        [InlineData("   ")]
        [InlineData(null)]
        [InlineData("iv")]       // no digits: not a paragraph number we understand
        public void A_value_with_no_number_is_not_indexed(string? n)
        {
            Assert.False(BookMarkers.TryParseParagraphSpan(n, out _, out _));
        }

        [Fact]
        public void A_hit_inside_a_ranged_paragraph_is_no_longer_attributed_to_the_one_before_it()
        {
            // The reported bug, as a sequence. vin02t.tik.xml runs 13, 15, 16-26, 35 - so every hit inside
            // the range used to be reported as paragraph 15, a different paragraph, with nothing to signal
            // that anything had been approximated.
            var xml = "<p n=\"13\">a</p><p n=\"15\">b</p><p n=\"16-26\">TARGET</p><p n=\"35\">d</p>";
            var markers = BookMarkers.Build(xml);

            var pos = xml.IndexOf("TARGET");

            Assert.Equal(16, markers.RefsAt(pos).Number);   // was 15
        }

        [Fact]
        public void Every_number_inside_a_range_addresses_the_paragraph_that_contains_it()
        {
            // PositionOfParagraph returned -1 for any N inside a range, so addressing into these 3,618
            // paragraphs failed outright rather than landing slightly off.
            var xml = "<p n=\"15\">b</p><p n=\"16-26\">TARGET</p><p n=\"35\">d</p>";
            var markers = BookMarkers.Build(xml);
            var expected = xml.IndexOf("<p n=\"16-26\"");

            foreach (var n in new[] { 16, 20, 26 })
                Assert.Equal(expected, markers.PositionOfParagraph(n));

            Assert.Equal(-1, markers.PositionOfParagraph(27));   // past the range - still not there
            Assert.Equal(-1, markers.PositionOfParagraph(14));   // between 13 and 15 - still not there
        }

        [Fact]
        public void A_ranged_paragraph_keeps_its_sub_book_code()
        {
            // Multi books resolve a paragraph number within a sub-book; a ranged paragraph must not lose that
            // qualification on the way into the index.
            var xml = "<div type=\"book\" id=\"an5\"><p n=\"16-26\">TARGET</p></div>";
            var markers = BookMarkers.Build(xml);

            var refs = markers.RefsAt(xml.IndexOf("TARGET"));

            Assert.Equal(16, refs.Number);
            Assert.Equal("an5", refs.BookCode);
            Assert.Equal(xml.IndexOf("<p"), markers.PositionOfParagraph(20, "an5"));
            Assert.Equal(-1, markers.PositionOfParagraph(20, "an6"));
        }

        [Fact]
        public void A_backwards_span_is_read_as_a_typo_and_keeps_only_its_opening_number()
        {
            // abh05t.nrf.xml carries n="706-608" between 703-705 and 709-710, so the intent is 706-708 and
            // the 6 is a slip. Swapping the ends would invent a 99-paragraph block from a digit error, and
            // that block would then shadow every real paragraph from 608 to 705.
            Assert.True(BookMarkers.TryParseParagraphSpan("706-608", out var f, out var l));

            Assert.Equal(706, f);
            Assert.Equal(706, l);
        }

        [Fact]
        public void A_standalone_paragraph_beats_a_range_that_merely_covers_its_number()
        {
            // In 36 corpus files a ranged paragraph spans numbers that also exist in their own right —
            // abh03a.att.xml has 1,445 such overlaps. Handing those to the range would be worse than the -1
            // this returned before the fix: confidently wrong rather than absent.
            var xml = "<p n=\"7-10\">RANGE</p><p n=\"8\">EXACT</p>";
            var markers = BookMarkers.Build(xml);

            Assert.Equal(xml.IndexOf("<p n=\"8\""), markers.PositionOfParagraph(8));   // not the range
            Assert.Equal(xml.IndexOf("<p n=\"7-10\""), markers.PositionOfParagraph(9)); // only inside it
        }
    }
}
