using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>#466: the dictionary meaning WebView host-document builder.</summary>
public class DictionaryHtmlRendererTests
{
    private const string Sep = "<hr/>";   // DictionaryService.MeaningSeparator
    private static string Id(string s) => s; // identity link-display

    [Fact]
    public void Render_WrapsFragmentInCspHostDocument()
    {
        var html = DictionaryHtmlRenderer.Render("a town", Id, Sep, "Arial", 11);

        Assert.Contains("<!doctype html>", html);
        // Strict CSP: nothing loads, no scripts.
        Assert.Contains("default-src 'none'", html);
        Assert.DoesNotContain("script-src", html);
        Assert.Contains("a town", html);
    }

    [Fact]
    public void Render_UsesPointUnitsForFontSize_NotPx()
    {
        var html = DictionaryHtmlRenderer.Render("x", Id, Sep, "Arial", 11);
        Assert.Contains("font-size: 11pt", html);
        Assert.DoesNotContain("font-size: 11px", html);
    }

    [Fact]
    public void Render_TransformsSeeTagIntoCstSeeLink()
    {
        var html = DictionaryHtmlRenderer.Render("see <see>nagara</see> here", Id, Sep, "Arial", 11);

        Assert.Contains($"href=\"{DictionaryHtmlRenderer.SeeScheme}nagara\"", html);
        Assert.Contains("class=\"see\"", html);
        Assert.DoesNotContain("<see>", html);
    }

    [Fact]
    public void Render_SeeLinkText_UsesTheDisplayMapping()
    {
        // linkDisplay converts the Latin target to a display form; the link TEXT shows that, the HREF keeps
        // the original target for lookup.
        var html = DictionaryHtmlRenderer.Render("<see>nagara</see>", t => t.ToUpperInvariant(), Sep, "Arial", 11);

        Assert.Contains(">NAGARA</a>", html);                                   // display text
        Assert.Contains($"href=\"{DictionaryHtmlRenderer.SeeScheme}nagara\"", html); // original target
    }

    [Fact]
    public void Render_SplitsMergedDefinitionsOnSeparatorIntoRules()
    {
        var html = DictionaryHtmlRenderer.Render($"first{Sep}second", Id, Sep, "Arial", 11);

        Assert.Contains("first", html);
        Assert.Contains("second", html);
        Assert.Contains("class=\"def-sep\"", html);   // a rule between the two definitions
    }

    [Fact]
    public void Render_NullOrEmpty_ProducesEmptyBodyButValidDocument()
    {
        var html = DictionaryHtmlRenderer.Render(null, Id, Sep, "Arial", 11);
        Assert.Contains("<body></body>", html);
    }

    [Fact]
    public void Render_EscapesTheFontFamily()
    {
        var html = DictionaryHtmlRenderer.Render("x", Id, Sep, "Weird\"Font", 11);
        Assert.DoesNotContain("Weird\"Font", html);   // the raw quote must be encoded
        Assert.Contains("Weird&quot;Font", html);
    }
}
