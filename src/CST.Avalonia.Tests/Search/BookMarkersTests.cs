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
    }
}
