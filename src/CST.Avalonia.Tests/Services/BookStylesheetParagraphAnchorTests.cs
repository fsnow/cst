using System;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Xsl;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// The paragraph anchors the book stylesheet emits. (#447)
///
/// <para>[observed] A printed paragraph number is not unique within a book: 102 corpus files repeat one,
/// because the numbering restarts per section. <c>para{n}</c> and <c>para{n}_{book div}</c> cannot tell
/// those repeats apart; <c>para{n}_{deepest div}</c> resolves 96% of them in the marked books.</para>
/// </summary>
public class BookStylesheetParagraphAnchorTests
{
    // Khuddakapāṭha-shaped: the numbering restarts in each chapter.
    private const string Book =
        "<TEI.2><text><body>" +
        "<p rend=\"bodytext\" n=\"9\">BEFORE-ANY-DIV</p>" +
        "<div id=\"kn1\" n=\"kn1\" type=\"book\">" +
        "<p rend=\"centre\" n=\"8\">IN-BOOK-DIV-ONLY</p>" +
        "<div id=\"kn1_1\" n=\"kn1_1\" type=\"chapter\"><head rend=\"chapter\">ONE</head>" +
        "<p rend=\"bodytext\" n=\"1\">CH1-P1</p>" +
        "</div>" +
        "<div id=\"kn1_2\" n=\"kn1_2\" type=\"chapter\"><head rend=\"chapter\">TWO</head>" +
        "<p rend=\"bodytext\" n=\"1\">CH2-P1</p>" +
        "<p rend=\"hangnum\" n=\"2\"><hi rend=\"paranum\">2</hi></p>" +
        "<p rend=\"gatha1\" n=\"3\">CH2-VERSE</p>" +
        "<div id=\"kn1_2_1\" n=\"kn1_2_1\" type=\"vagga\">" +
        "<p rend=\"bodytext\" n=\"4\">NESTED</p>" +
        // A div with no id inside a section: the section is still the nearest div that HAS one.
        "<div type=\"peyyala\"><p rend=\"bodytext\" n=\"5\">IN-IDLESS-DIV</p></div>" +
        "<p rend=\"indent\" n=\"6\">INDENTED</p>" +
        "<p rend=\"gatha1\" n=\"7\">VERSE-NOT-IN-A-ROW</p>" +
        "</div>" +
        "</div>" +
        "</div></body></text></TEI.2>";

    private static string Render()
    {
        var xsl = new XslCompiledTransform();
        xsl.Load(StylesheetPath());
        var doc = new XmlDocument();
        doc.LoadXml(Book);
        var args = new XsltArgumentList();
        args.AddParam("bookFontFamily", "", "serif");
        using var writer = new StringWriter();
        xsl.Transform(doc, args, writer);
        return writer.ToString();
    }

    private static string[] Names(string html) =>
        Regex.Matches(html, "<a name=\"(para[^\"]*)\"").Select(m => m.Groups[1].Value).ToArray();

    [Fact]
    public void A_paragraph_in_a_section_gets_all_three_forms()
    {
        var names = Names(Render());

        Assert.Contains("para1", names);
        Assert.Contains("para1_kn1", names);
        Assert.Contains("para1_kn1_1", names);
        Assert.Contains("para1_kn1_2", names);
    }

    [Fact]
    public void The_section_form_tells_repeated_numbers_apart()
    {
        var names = Names(Render());

        // The bare and book-div forms repeat; the section form does not.
        Assert.Equal(2, names.Count(n => n == "para1"));
        Assert.Equal(2, names.Count(n => n == "para1_kn1"));
        Assert.Equal(1, names.Count(n => n == "para1_kn1_1"));
        Assert.Equal(1, names.Count(n => n == "para1_kn1_2"));
    }

    [Fact]
    public void A_nested_paragraph_gets_one_anchor_per_enclosing_level_outermost_first()
    {
        var names = Names(Render());

        // The page script saves the LAST anchor at a position, so the deepest has to come last.
        Assert.Equal(new[] { "para4", "para4_kn1", "para4_kn1_2", "para4_kn1_2_1" },
            names.Where(n => n == "para4" || n.StartsWith("para4_")).ToArray());
    }

    [Fact]
    public void A_paragraph_directly_in_the_book_div_gets_no_third_form()
    {
        var names = Names(Render());

        // The book-div form already names its only enclosing div.
        Assert.Equal(new[] { "para8", "para8_kn1" }, names.Where(n => n.StartsWith("para8")).ToArray());
    }

    [Fact]
    public void A_paragraph_outside_every_div_keeps_only_the_bare_form()
    {
        var names = Names(Render());

        Assert.Equal(new[] { "para9" }, names.Where(n => n.StartsWith("para9")).ToArray());
    }

    [Fact]
    public void Hanging_numbers_and_the_paragraph_beside_them_get_the_section_form_too()
    {
        var names = Names(Render());

        Assert.Contains("para2_kn1_2", names);   // the hangnum template
        Assert.Contains("para3_kn1_2", names);   // the paragraph in its row (mode="hang")
    }

    [Fact]
    public void A_div_without_an_id_is_skipped_for_the_nearest_one_that_has_one()
    {
        var names = Names(Render());

        // BookMarkers reads the section the same way, so the anchor it promises is one the page has.
        Assert.Contains("para5_kn1_2_1", names);
    }

    [Fact]
    public void Indented_paragraphs_and_verses_outside_a_hanging_row_get_anchors()
    {
        // 13 numbered rend="indent" and 18 numbered rend="gatha1" paragraphs in the corpus had none at all.
        var names = Names(Render());

        Assert.Contains("para6_kn1_2_1", names);
        Assert.Contains("para7_kn1_2_1", names);
    }

    private static string StylesheetPath() => Path.Combine(RepoRoot(), "src", "CST.Avalonia", "xsl", "tipitaka.xsl");

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
