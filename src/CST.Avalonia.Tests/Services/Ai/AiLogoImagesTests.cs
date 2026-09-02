using System;
using System.IO;
using System.Text;
using Avalonia.Media;
using CST.Avalonia.Services.Ai;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// #748: what may be handed to the SVG renderer.
///
/// <para>Rendering itself is not covered here: it needs Avalonia's rendering stack, and building an
/// <c>SvgImage</c> is thread-affine. The screening in front of it is plain file-and-string work, and is where
/// the consequential decisions live, so that is what these cover. The render path was verified in the running
/// app by rasterising to a bitmap and sampling pixels; see the PR.</para>
///
/// <para><b>The thread guard is not covered here, and cannot be.</b> With no Avalonia application running,
/// <c>Dispatcher.UIThread</c> binds to whichever thread first touches it — in a test process that is a pool
/// thread, so <c>CheckAccess()</c> returns true off-thread and the guard never trips. A test asserting it
/// would pass whether or not the guard existed.</para>
///
/// <para>Why screening exists at all: these files arrive over the network. The renderer expands XML entities
/// with no quota, so a few KB of nested declarations become gigabytes, and it fetches
/// <c>&lt;image href="https://…"&gt;</c> synchronously on the calling thread — which must be the UI thread.
/// Neither appears in any of today's logos. (fable review)</para>
/// </summary>
public class AiLogoImagesTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cst-748-" + Guid.NewGuid().ToString("N"));

    public AiLogoImagesTests() => Directory.CreateDirectory(_dir);

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string Write(string name, string content)
    {
        var path = Path.Combine(_dir, name);
        File.WriteAllText(path, content);
        return path;
    }

    /// <summary>The shape of a real logo: an SVG namespace whose value is itself an http URL, which must not
    /// read as "references a remote resource".</summary>
    [Fact]
    public void A_real_logo_passes()
    {
        var path = Write("anthropic.svg", """
            <svg width="24" height="24" viewBox="0 0 40 40" xmlns="http://www.w3.org/2000/svg">
            <path d="M26.9 9.8H22.1L30.7 31.7H35.4Z" fill="currentColor"/></svg>
            """);

        Assert.Null(AiLogoImages.Screen(path));
    }

    /// <summary>Billion laughs. A few KB of nested declarations expand to gigabytes, and the renderer sets no
    /// quota — so the cost is a frozen window long before the failure.</summary>
    [Fact]
    public void A_document_declaring_entities_is_refused()
    {
        var path = Write("bomb.svg", """
            <?xml version="1.0"?>
            <!DOCTYPE svg [<!ENTITY a "aaaaaaaaaa"><!ENTITY b "&a;&a;&a;&a;&a;&a;&a;&a;&a;&a;">]>
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><path d="&b;"/></svg>
            """);

        Assert.Equal("declares XML entities", AiLogoImages.Screen(path));
    }

    /// <summary>Fetched synchronously, on the UI thread, with the default 100-second timeout. A decorative
    /// icon has no business reaching the network at draw time.</summary>
    [Theory]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><image href="https://evil.example/x.png"/></svg>""")]
    // Declares xmlns:xlink, as every real file using the prefix does. Without the declaration this is not
    // well-formed XML at all, and is refused for that reason instead — covered separately below.
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg" xmlns:xlink="http://www.w3.org/1999/xlink" viewBox="0 0 10 10"><image xlink:href="http://evil.example/x.png"/></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><image href="file:///etc/passwd"/></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><path fill="url(https://evil.example/g)" d="M0,0H1"/></svg>""")]
    public void A_document_reaching_off_the_machine_is_refused(string svg)
    {
        Assert.Equal("references a remote resource", AiLogoImages.Screen(Write("remote.svg", svg)));
    }

    /// <summary>The bypass that shipped in c1fbb7a. (#930, fable review)
    ///
    /// <para>A <c>&lt;!--</c> inside one CDATA section and a <c>--&gt;</c> inside another let the
    /// comment-stripping regex delete everything between them — including a live remote
    /// <c>&lt;image&gt;</c> — so the file was ACCEPTED. The XML parser reads CDATA as literal text, so the
    /// element is real and the renderer fetches it, synchronously, on the UI thread. Confirmed with a
    /// loopback listener before this was fixed.</para></summary>
    [Fact]
    public void A_remote_image_hidden_between_two_CDATA_sections_is_still_refused()
    {
        var path = Write("cdata.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><desc><![CDATA[<!--]]></desc><image href="https://evil.example/x.png" width="10" height="10"/><desc><![CDATA[-->]]></desc></svg>
            """);

        Assert.Equal("references a remote resource", AiLogoImages.Screen(path));
    }

    /// <summary>A character reference spells the same URL without the letters being contiguous anywhere in
    /// the file. No raw-text search can see it; the parser resolves it before we look. (#930)</summary>
    [Fact]
    public void A_remote_url_written_as_character_references_is_still_refused()
    {
        var path = Write("charref.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><image href="&#104;ttp://evil.example/x.png" width="10" height="10"/></svg>
            """);

        Assert.Equal("references a remote resource", AiLogoImages.Screen(path));
    }

    /// <summary>CSS inside &lt;style&gt; fetches like any other reference, and it is text rather than an
    /// attribute.</summary>
    [Fact]
    public void A_remote_url_in_a_style_block_is_refused()
    {
        var path = Write("style.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><style>.a { fill: url(https://evil.example/g); }</style><path class="a" d="M0,0H1"/></svg>
            """);

        Assert.Equal("references a remote resource", AiLogoImages.Screen(path));
    }

    /// <summary>Text that is NOT style must stay readable — a description mentioning a project's home page
    /// is the false-positive class this whole issue was about.</summary>
    [Fact]
    public void A_web_address_in_a_description_is_not_a_remote_reference()
    {
        var path = Write("desc.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><desc>See https://example.com/about</desc><path d="M0,0H1"/></svg>
            """);

        Assert.Null(AiLogoImages.Screen(path));
    }

    /// <summary>A file the parser cannot read is refused rather than guessed at: its fetches cannot be
    /// enumerated, and a logo is decoration — the monogram is the honest answer.</summary>
    [Fact]
    public void A_file_that_is_not_well_formed_is_refused()
    {
        var path = Write("broken.svg", """
            <svg xmlns="http://www.w3.org/2000/svg"><image xlink:href="http://evil.example/x.png"/></svg>
            """);

        Assert.Equal("is not well-formed XML", AiLogoImages.Screen(path));
    }

    /// <summary>The SVG 1.1 doctype names a w3.org DTD. The renderer does not resolve it, and refusing over
    /// it cost <c>hpc-ai.svg</c> its mark — one of only two refusals across the whole 173-logo cache, both
    /// of them wrong (#930).</summary>
    [Fact]
    public void A_standard_doctype_is_not_a_remote_reference()
    {
        var path = Write("hpc-ai.svg", """
            <?xml version="1.0" encoding="UTF-8"?>
            <!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 40 40"><path d="M0,0H1"/></svg>
            """);

        Assert.Null(AiLogoImages.Screen(path));
    }

    /// <summary>Inkscape stamps its home page into a comment. That is the whole reason
    /// <c>regolo-ai.svg</c> was refused (#930); a comment is not drawn and cannot cause a fetch.</summary>
    [Fact]
    public void A_generator_comment_is_not_a_remote_reference()
    {
        var path = Write("regolo-ai.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 40 40">
            <!-- Created with Inkscape (http://www.inkscape.org/) -->
            <path d="M0,0H1"/></svg>
            """);

        Assert.Null(AiLogoImages.Screen(path));
    }

    /// <summary>The point of the guard survives the two exemptions: a real fetch is still refused even when
    /// the file also carries a doctype and a comment, which is what a fix that merely stopped looking would
    /// break.</summary>
    [Fact]
    public void A_real_remote_image_is_still_refused_alongside_a_doctype_and_a_comment()
    {
        var path = Write("hostile.svg", """
            <!DOCTYPE svg PUBLIC "-//W3C//DTD SVG 1.1//EN" "http://www.w3.org/Graphics/SVG/1.1/DTD/svg11.dtd">
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 40 40">
            <!-- Created with Inkscape (http://www.inkscape.org/) -->
            <image href="https://example.com/tracker.png"/></svg>
            """);

        Assert.Equal("references a remote resource", AiLogoImages.Screen(path));
    }

    /// <summary>An entity bomb hides in the doctype's internal subset, which this change strips. It must
    /// still be caught — the &lt;!ENTITY test reads the RAW text and runs first, and this pins that
    /// ordering (#930).</summary>
    [Fact]
    public void An_entity_in_the_internal_subset_is_still_caught_though_the_doctype_is_stripped()
    {
        var path = Write("bomb.svg", """
            <!DOCTYPE svg [ <!ENTITY lol "lol"> <!ENTITY lol2 "&lol;&lol;&lol;"> ]>
            <svg xmlns="http://www.w3.org/2000/svg"><text>&lol2;</text></svg>
            """);

        Assert.Equal("declares XML entities", AiLogoImages.Screen(path));
    }

    /// <summary>A fragment reference is internal and ordinary — one logo in the set uses one for a clip
    /// path.</summary>
    [Fact]
    public void An_internal_fragment_reference_is_allowed()
    {
        var path = Write("novita.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 40 40">
            <g clip-path="url(#clip0)"><path d="M0,0H1"/></g></svg>
            """);

        Assert.Null(AiLogoImages.Screen(path));
    }

    [Fact]
    public void An_oversized_file_is_refused()
    {
        var path = Write("huge.svg", new string('x', AiLogoImages.MaxBytes + 1));

        Assert.Contains("over the", AiLogoImages.Screen(path));
    }

    /// <summary>The largest real mark is under 3 KB, so the limit has to be far above anything genuine or it
    /// would be rejecting logos rather than payloads.</summary>
    [Fact]
    public void The_limit_is_far_above_any_real_logo()
    {
        Assert.True(AiLogoImages.MaxBytes > 100 * 1024);
    }

    [Theory]
    [InlineData("")]
    public void An_empty_file_is_refused(string content)
    {
        Assert.Equal("empty", AiLogoImages.Screen(Write("empty.svg", content)));
    }

    [Fact]
    public void A_missing_file_is_refused()
    {
        Assert.Equal("no such file", AiLogoImages.Screen(Path.Combine(_dir, "nope.svg")));
    }

    /// <summary>A logo naming its own colours must not be mistaken for something reaching off the machine.
    /// Four of the set do — 302ai, evroc, novita-ai, zenmux — and they are the ones that will not theme.</summary>
    [Fact]
    public void A_logo_with_literal_colours_passes()
    {
        var path = Write("zenmux.svg", """
            <svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 20 20">
            <path d="M0,0H1" fill="#F5F5F5"/><path d="M1,1H2" fill="black"/></svg>
            """);

        Assert.Null(AiLogoImages.Screen(path));
    }

    [Fact]
    public void No_path_means_no_image()
    {
        var images = new AiLogoImages();

        Assert.Null(images.Get(null, Color.FromRgb(0, 0, 0)));
        Assert.Null(images.Get("", Color.FromRgb(0, 0, 0)));
    }
}
