using CST.Avalonia.ViewModels;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// Whether a search-highlight offset may be spliced at. (R8-6)
///
/// <para>Highlighting runs off Lucene offsets, which were computed against the XML as it was when the book
/// was last indexed. If the corpus is updated after that pass, the offsets still fall inside the file's
/// range but no longer point at the same characters — and an <c>&lt;hi&gt;</c> spliced into the middle of
/// <c>&lt;p n="331"&gt;</c> makes the whole document malformed, so <c>LoadXml</c> throws and the reader gets
/// "Error loading book" for a book that opens fine without a search.</para>
/// </summary>
public class HighlightOffsetGuardTests
{
    private const string Xml = """<p n="331">gacchati bhikkhu</p>""";

    [Fact]
    public void An_offset_in_text_is_spliceable()
    {
        // "gacchati" starts right after the closing '>' of the <p> tag.
        int inText = Xml.IndexOf("gacchati");

        Assert.False(BookDisplayViewModel.IsInsideTag(Xml, inText));
    }

    /// <summary>The case that produces the malformed document: an offset that has drifted into an attribute
    /// value.</summary>
    [Fact]
    public void An_offset_inside_a_tag_is_refused()
    {
        int insideAttribute = Xml.IndexOf("331");

        Assert.True(BookDisplayViewModel.IsInsideTag(Xml, insideAttribute));
    }

    /// <summary>A closing tag is markup too — a drifted end offset lands here just as easily.</summary>
    [Fact]
    public void An_offset_inside_a_closing_tag_is_refused()
    {
        int insideClose = Xml.IndexOf("</p>") + 2;   // between '/' and 'p'

        Assert.True(BookDisplayViewModel.IsInsideTag(Xml, insideClose));
    }

    /// <summary>A highlight legitimately SPANS whole tags — the &lt;hi&gt;-crossing cases downstream exist
    /// for exactly that — so the guard tests the endpoints, never the span. Text on both sides of a tag is
    /// spliceable at both ends.</summary>
    [Fact]
    public void Text_either_side_of_a_tag_is_spliceable()
    {
        const string spanning = """<p>eka <hi rend="bold">dve</hi> tini</p>""";

        Assert.False(BookDisplayViewModel.IsInsideTag(spanning, spanning.IndexOf("eka")));
        Assert.False(BookDisplayViewModel.IsInsideTag(spanning, spanning.IndexOf("tini")));
    }

    [Fact]
    public void Degenerate_positions_are_not_treated_as_markup()
    {
        Assert.False(BookDisplayViewModel.IsInsideTag(Xml, 0));
        Assert.False(BookDisplayViewModel.IsInsideTag("", 0));
        Assert.False(BookDisplayViewModel.IsInsideTag(Xml, Xml.Length + 10));
    }
}
