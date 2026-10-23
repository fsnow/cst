using System;
using System.IO;
using System.Text.RegularExpressions;
using System.Xml;
using System.Xml.Xsl;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// A hanging paragraph number and the paragraph it hangs beside render as one grid row. (#575)
///
/// <para>[observed] The old layout gave the number a fixed 2em and pulled the next paragraph up beside it with a
/// negative margin, so a number wider than 2em was printed over the verse: four digits in Latin, two in
/// Malayalam, every number in front of body text. The grid sizes the number's column to the number.</para>
///
/// <para>Each paragraph keeps its own &lt;p&gt; and its exact class: BookDisplayView's page script finds body
/// paragraphs by matching <c>className</c> exactly.</para>
/// </summary>
public class BookStylesheetHangnumTests
{
    private const string Book =
        "<TEI.2><text><body><div id=\"dn1\" n=\"dn1\" type=\"book\">" +
        "<p rend=\"hangnum\" n=\"1204\"><hi rend=\"paranum\">1204</hi><hi rend=\"dot\">.</hi></p>" +
        "<p rend=\"gatha1\">VERSE-A</p>" +
        "<p rend=\"gathalast\">VERSE-B</p>" +
        "<p rend=\"hangnum\" n=\"7\"><hi rend=\"paranum\">7</hi><hi rend=\"dot\">.</hi></p>" +
        "<p rend=\"hangnum\" n=\"8\"><hi rend=\"paranum\">8</hi><hi rend=\"dot\">.</hi></p>" +
        "<p rend=\"bodytext\">PROSE-C</p>" +
        "</div></body></text></TEI.2>";

    private static string Render()
    {
        var xsl = new XslCompiledTransform();
        xsl.Load(Path.Combine(RepoRoot(), "src", "CST.Avalonia", "xsl", "tipitaka.xsl"));
        var doc = new XmlDocument();
        doc.LoadXml(Book);
        var args = new XsltArgumentList();
        args.AddParam("bookFontFamily", "", "serif");
        using var writer = new StringWriter();
        xsl.Transform(doc, args, writer);
        return Regex.Replace(writer.ToString(), @"\s+", " ");
    }

    private static int Count(string s, string part) => Regex.Matches(s, Regex.Escape(part)).Count;

    [Fact]
    public void Every_hanging_number_is_a_grid_row()
    {
        var html = Render();

        Assert.Equal(3, Count(html, "<div class=\"hanggrid\">"));
    }

    [Fact]
    public void The_paragraph_after_a_number_is_in_its_row_once_with_its_own_class()
    {
        var html = Render();

        Assert.Matches(new Regex(
            "<div class=\"hanggrid\"> ?<p class=\"hangnum\">.*?1204.*?</p> ?<p class=\"gatha1\">VERSE-A</p> ?</div>"), html);
        Assert.Equal(1, Count(html, "VERSE-A"));
        // Only the first paragraph joins the row; the rest of the stanza follows it as before.
        Assert.Matches(new Regex("VERSE-A</p> ?</div> ?<p class=\"gathalast\">VERSE-B</p>"), html);
    }

    [Fact]
    public void A_number_followed_by_another_number_stands_alone()
    {
        var html = Render();

        Assert.Matches(new Regex(
            "<div class=\"hanggrid\"> ?<p class=\"hangnum\">(?:(?!</p>).)*7(?:(?!</p>).)*</p> ?</div>"), html);
    }

    [Fact]
    public void Body_text_after_a_number_joins_its_row()
    {
        var html = Render();

        Assert.Matches(new Regex("<p class=\"hangnum\">(?:(?!</p>).)*8(?:(?!</p>).)*</p> ?<p class=\"bodytext\">PROSE-C</p> ?</div>"), html);
        Assert.Equal(1, Count(html, "PROSE-C"));
    }

    [Fact]
    public void The_paragraph_anchors_are_kept()
    {
        var html = Render();

        Assert.Equal(1, Count(html, "name=\"para1204\""));
        Assert.Equal(1, Count(html, "name=\"para1204_dn1\""));
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
