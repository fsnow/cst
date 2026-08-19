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
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><image xlink:href="http://evil.example/x.png"/></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><image href="file:///etc/passwd"/></svg>""")]
    [InlineData("""<svg xmlns="http://www.w3.org/2000/svg" viewBox="0 0 10 10"><path fill="url(https://evil.example/g)" d="M0,0H1"/></svg>""")]
    public void A_document_reaching_off_the_machine_is_refused(string svg)
    {
        Assert.Equal("references a remote resource", AiLogoImages.Screen(Write("remote.svg", svg)));
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
