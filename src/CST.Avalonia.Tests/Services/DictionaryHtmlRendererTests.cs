using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>#466: the dictionary meaning WebView host-document builder.</summary>
public class DictionaryHtmlRendererTests
{
    private static string Id(string s) => s; // identity link-display

    [Fact]
    public void Render_WrapsFragmentInCspHostDocument()
    {
        var html = DictionaryHtmlRenderer.Render("a town", Id, "Arial", 11);

        Assert.Contains("<!doctype html>", html);
        // Strict CSP: nothing loads, no scripts.
        Assert.Contains("default-src 'none'", html);
        Assert.DoesNotContain("script-src", html);
        Assert.Contains("a town", html);
    }

    [Fact]
    public void Render_UsesPointUnitsForFontSize_NotPx()
    {
        var html = DictionaryHtmlRenderer.Render("x", Id, "Arial", 11);
        Assert.Contains("font-size: 11pt", html);
        Assert.DoesNotContain("font-size: 11px", html);
    }

    [Fact]
    public void Render_TransformsSeeTagIntoCstSeeLink()
    {
        var html = DictionaryHtmlRenderer.Render("see <see>nagara</see> here", Id, "Arial", 11);

        Assert.Contains($"href=\"{DictionaryHtmlRenderer.SeeScheme}nagara\"", html);
        Assert.Contains("class=\"see\"", html);
        Assert.DoesNotContain("<see>", html);
    }

    [Fact]
    public void Render_SeeLinkText_UsesTheDisplayMapping()
    {
        // linkDisplay converts the Latin target to a display form; the link TEXT shows that, the HREF keeps
        // the original target for lookup.
        var html = DictionaryHtmlRenderer.Render("<see>nagara</see>", t => t.ToUpperInvariant(), "Arial", 11);

        Assert.Contains(">NAGARA</a>", html);                                   // display text
        Assert.Contains($"href=\"{DictionaryHtmlRenderer.SeeScheme}nagara\"", html); // original target
    }

    [Fact]
    public void Render_PassesHrThrough_DoesNotSplitContent()
    {
        // <hr/> is the flat-file merge sentinel AND real content in some HTML sources (DPPN). It must render
        // as-is (a rule), never trigger a special split that could mangle HTML fragments. (#466 Fable)
        var html = DictionaryHtmlRenderer.Render("first<hr/>second", Id, "Arial", 11);

        Assert.Contains("first<hr/>second", html);        // fragment passed through unchanged
        Assert.DoesNotContain("def-sep", html);           // no synthetic split marker
    }

    [Fact]
    public void Render_NullOrEmpty_ProducesEmptyBodyButValidDocument()
    {
        var html = DictionaryHtmlRenderer.Render(null, Id, "Arial", 11);
        Assert.Contains("<body></body>", html);
    }

    [Fact]
    public void Render_EscapesTheFontFamily()
    {
        var html = DictionaryHtmlRenderer.Render("x", Id, "Weird\"Font", 11);
        Assert.DoesNotContain("Weird\"Font", html);   // the raw quote must be encoded
        Assert.Contains("Weird&quot;Font", html);
    }
}
