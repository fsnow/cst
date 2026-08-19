using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Services.Ai;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// #738: logos are decoration, so every failure means "use the monogram" rather than an error. The subtle
/// part is that models.dev answers 200 for an unknown provider with a shared placeholder, so status cannot
/// distinguish a real logo from a stand-in.
/// </summary>
public class AiProviderLogosTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cst-738-" + Guid.NewGuid().ToString("N"));

    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private sealed class Stub : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _reply;
        public int Calls { get; private set; }
        public Stub(Func<HttpResponseMessage> reply) => _reply = reply;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        { Calls++; return Task.FromResult(_reply()); }
    }

    private static HttpResponseMessage Svg(byte[] body) =>
        new(HttpStatusCode.OK) { Content = new ByteArrayContent(body) };

    private static byte[] RealLogo => Encoding.UTF8.GetBytes("<svg><circle r='1'/></svg>");

    /// <summary>Stands in for the real models.dev placeholder. Its hash is injected into the service rather
    /// than the asset being reproduced, so the detection is exercised against bytes the test controls — the
    /// real hash cannot be searched for, and a test that quietly failed to find it would pass anyway by
    /// falling into the same null the fallback returns.</summary>
    private static byte[] PlaceholderStandIn =>
        Encoding.UTF8.GetBytes("<svg><!-- models.dev default --></svg>");

    private static string Sha256Of(byte[] bytes) =>
        Convert.ToHexString(SHA256.HashData(bytes)).ToLowerInvariant();

    private AiProviderLogos Make(HttpMessageHandler handler) =>
        new(new HttpClient(handler), _dir, "https://example.invalid/logos", Sha256Of(PlaceholderStandIn));

    [Fact]
    public async Task A_real_logo_is_returned_and_cached()
    {
        var stub = new Stub(() => Svg(RealLogo));
        var logos = Make(stub);

        var first = await logos.GetLogoPathAsync("openrouter");

        Assert.NotNull(first);
        Assert.True(File.Exists(first!));
        Assert.Equal(RealLogo, await File.ReadAllBytesAsync(first));
    }

    /// <summary>Asked once per settled answer, not per rebind — the connections list rebinds on every
    /// change.</summary>
    [Fact]
    public async Task A_second_request_does_not_hit_the_network()
    {
        var stub = new Stub(() => Svg(RealLogo));
        var logos = Make(stub);

        await logos.GetLogoPathAsync("openrouter");
        await logos.GetLogoPathAsync("openrouter");

        Assert.Equal(1, stub.Calls);
    }

    /// <summary>
    /// The case status codes cannot express. models.dev answers 200 with a shared placeholder for any
    /// unknown id — Ollama among them, since local runners are not catalogue providers. A generic grey mark
    /// says less than a coloured initial, so this must read as "no logo".
    /// </summary>
    [Fact]
    public async Task The_shared_placeholder_reads_as_no_logo()
    {
        var logos = Make(new Stub(() => Svg(PlaceholderStandIn)));

        Assert.Null(await logos.GetLogoPathAsync("ollama"));
    }

    /// <summary>And it is never written to disk: caching it would leave a provider that gains a logo next
    /// month showing nothing until somebody cleared the directory.</summary>
    [Fact]
    public async Task The_placeholder_is_not_cached()
    {
        var logos = Make(new Stub(() => Svg(PlaceholderStandIn)));

        await logos.GetLogoPathAsync("ollama");

        Assert.False(Directory.Exists(_dir) && Directory.GetFiles(_dir).Length > 0,
            "a placeholder was written to the cache");
    }

    [Fact]
    public async Task A_network_failure_falls_back_to_the_monogram()
    {
        var logos = Make(new Stub(() => throw new HttpRequestException("offline")));

        Assert.Null(await logos.GetLogoPathAsync("openrouter"));
    }

    [Fact]
    public async Task A_server_error_falls_back_to_the_monogram()
    {
        var logos = Make(new Stub(() => new HttpResponseMessage(HttpStatusCode.InternalServerError)));

        Assert.Null(await logos.GetLogoPathAsync("openrouter"));
    }

    /// <summary>A custom endpoint has no provider id at all, which is the commonest monogram case.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task No_provider_id_means_no_logo(string id)
    {
        var logos = Make(new Stub(() => Svg(RealLogo)));

        Assert.Null(await logos.GetLogoPathAsync(id));
    }

    /// <summary>An already-cached file is served without a request, which is what makes an offline start
    /// show logos at all.</summary>
    [Fact]
    public async Task A_cached_logo_is_used_without_a_request()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllBytesAsync(Path.Combine(_dir, "openrouter.svg"), RealLogo);
        var stub = new Stub(() => throw new HttpRequestException("should not be called"));

        var path = await Make(stub).GetLogoPathAsync("openrouter");

        Assert.NotNull(path);
        Assert.Equal(0, stub.Calls);
    }

    /// <summary>MIT asks that the notice accompany copies, and the cache holds copies — on the reader's disk,
    /// outside the bundle, where nothing else records their origin.</summary>
    [Fact]
    public async Task The_licence_lands_beside_the_cached_logos()
    {
        await Make(new Stub(() => Svg(RealLogo))).GetLogoPathAsync("openrouter");

        var notice = Path.Combine(_dir, "NOTICE.txt");
        Assert.True(File.Exists(notice));
        var text = await File.ReadAllTextAsync(notice);
        Assert.Contains("MIT License", text);
        Assert.Contains("models.dev", text);
    }

    /// <summary>No cached asset, no notice: an empty directory carrying a licence for nothing would be noise.</summary>
    [Fact]
    public async Task No_licence_is_written_when_nothing_is_cached()
    {
        await Make(new Stub(() => Svg(PlaceholderStandIn))).GetLogoPathAsync("ollama");

        Assert.False(File.Exists(Path.Combine(_dir, "NOTICE.txt")));
    }

    /// <summary>A connection id is whatever its owner typed, and it becomes both a URL segment and a file
    /// name. Nothing that is not a slug is asked for, which is what keeps a written file inside the cache.</summary>
    [Theory]
    [InlineData("../../etc/passwd")]
    [InlineData("..")]
    [InlineData("my/box")]
    [InlineData("my box")]
    [InlineData("-leading-dash")]
    [InlineData("openai\n")]     // `$` would have matched before the newline, naming a file that contains one
    [InlineData("OpenAI")]       // case-sensitive, like the id validators this mirrors
    public async Task An_id_that_is_not_a_slug_is_never_fetched(string id)
    {
        var stub = new Stub(() => Svg(RealLogo));

        Assert.Null(await Make(stub).GetLogoPathAsync(id));
        Assert.Equal(0, stub.Calls);
    }

    /// <summary>
    /// A failure is about this moment, not about this provider.
    ///
    /// <para>Remembering it would mean a reader who opened Settings before their wifi came up sees monograms
    /// for the rest of the session with no way to ask again.</para>
    /// </summary>
    [Fact]
    public async Task A_failure_is_retried_rather_than_remembered()
    {
        var offline = true;
        var logos = Make(new Stub(() => offline
            ? throw new HttpRequestException("offline")
            : Svg(RealLogo)));

        Assert.Null(await logos.GetLogoPathAsync("openrouter"));

        offline = false;
        Assert.NotNull(await logos.GetLogoPathAsync("openrouter"));
    }

    /// <summary>Whereas "this provider has no mark" is settled, and asking again would be a request per
    /// rebind that can only ever get the same answer.</summary>
    [Fact]
    public async Task A_definite_absence_is_only_asked_once()
    {
        var stub = new Stub(() => Svg(PlaceholderStandIn));
        var logos = Make(stub);

        await logos.GetLogoPathAsync("ollama");
        await logos.GetLogoPathAsync("ollama");

        Assert.Equal(1, stub.Calls);
    }

    /// <summary>
    /// The one that would have shipped a permanent wrong answer.
    ///
    /// <para>A captive portal answers every request with 200 and a login page. Without a content check that
    /// HTML is written into anthropic.svg and served from disk by every later session, turning a transient
    /// network condition into a permanent bad cache — the exact failure this class refuses even to remember
    /// for a session. A non-2xx cannot get this far, because GetByteArrayAsync throws on one.</para>
    /// </summary>
    [Fact]
    public async Task A_captive_portal_login_page_is_not_cached_as_a_logo()
    {
        var portal = Encoding.UTF8.GetBytes("<html><body>Sign in to Airport WiFi</body></html>");
        var logos = Make(new Stub(() => Svg(portal)));

        Assert.Null(await logos.GetLogoPathAsync("anthropic"));
        Assert.False(File.Exists(Path.Combine(_dir, "anthropic.svg")));
    }

    /// <summary>And it is not remembered either: the portal is weather, so the next session — or the next
    /// row — must be free to ask again.</summary>
    [Fact]
    public async Task A_non_svg_response_is_retried_rather_than_remembered()
    {
        var captive = true;
        var logos = Make(new Stub(() => captive
            ? Svg(Encoding.UTF8.GetBytes("<html>portal</html>"))
            : Svg(RealLogo)));

        Assert.Null(await logos.GetLogoPathAsync("anthropic"));

        captive = false;
        Assert.NotNull(await logos.GetLogoPathAsync("anthropic"));
    }

    /// <summary>
    /// A cache already poisoned — by a release before the check existed, or by a placeholder hash that has
    /// since drifted — heals rather than serving the bad copy forever.
    /// </summary>
    [Fact]
    public async Task A_poisoned_cache_entry_is_replaced_rather_than_served()
    {
        Directory.CreateDirectory(_dir);
        var poisoned = Path.Combine(_dir, "anthropic.svg");
        await File.WriteAllBytesAsync(poisoned, Encoding.UTF8.GetBytes("<html>portal</html>"));

        var path = await Make(new Stub(() => Svg(RealLogo))).GetLogoPathAsync("anthropic");

        Assert.NotNull(path);
        Assert.Equal(RealLogo, await File.ReadAllBytesAsync(path!));
    }

    /// <summary>Same, for a placeholder that reached the disk before it was recognised.</summary>
    [Fact]
    public async Task A_cached_placeholder_is_not_served_as_a_logo()
    {
        Directory.CreateDirectory(_dir);
        await File.WriteAllBytesAsync(Path.Combine(_dir, "ollama.svg"), PlaceholderStandIn);

        Assert.Null(await Make(new Stub(() => Svg(PlaceholderStandIn))).GetLogoPathAsync("ollama"));
    }

    /// <summary>An XML declaration or a licence comment ahead of the root element is ordinary in a real SVG
    /// and must not read as "not an SVG".</summary>
    [Theory]
    [InlineData("<?xml version=\"1.0\"?><svg/>")]
    [InlineData("<!-- Copyright --><svg/>")]
    [InlineData("\n  <svg/>")]
    [InlineData("<SVG/>")]
    public async Task A_real_svg_is_recognised_however_it_opens(string body)
    {
        var logos = Make(new Stub(() => Svg(Encoding.UTF8.GetBytes(body))));

        Assert.NotNull(await logos.GetLogoPathAsync("anthropic"));
    }

    /// <summary>The cache directory is documented as safe to delete. Deleting it must cost a refetch, not a
    /// row bound to a file that has gone.</summary>
    [Fact]
    public async Task A_deleted_cache_is_refetched_rather_than_returned()
    {
        var logos = Make(new Stub(() => Svg(RealLogo)));

        var first = await logos.GetLogoPathAsync("openrouter");
        Directory.Delete(_dir, recursive: true);

        var second = await logos.GetLogoPathAsync("openrouter");

        Assert.NotNull(second);
        Assert.True(File.Exists(second!));
        Assert.Equal(first, second);
    }

    /// <summary>Two rows can carry the same id — a preset row and the connection added from it — and load at
    /// the same moment. Neither may end up on a monogram because the other won a race to the same file.</summary>
    [Fact]
    public async Task Concurrent_loads_of_one_id_both_get_the_logo()
    {
        var logos = Make(new Stub(() => Svg(RealLogo)));

        var results = await Task.WhenAll(
            Enumerable.Range(0, 8).Select(_ => Task.Run(() => logos.GetLogoPathAsync("anthropic"))));

        Assert.All(results, r => Assert.NotNull(r));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    /// <summary>The measured value, pinned. A wrong constant means placeholders get cached and displayed as
    /// though they were logos — quiet, not loud — so only a deliberate edit should be able to change it.
    /// Re-derive by requesting any provider id that cannot exist.</summary>
    [Fact]
    public void The_recorded_placeholder_hash_is_the_measured_one()
    {
        Assert.Equal(
            "13bb0b37c627f6e5961487cd0159abc18dd87ff318668357c7c9990b42e7f32f",
            AiProviderLogos.PlaceholderSha256);
    }

    /// <summary>A real logo must not read as the placeholder, or every provider would lose its mark.</summary>
    [Fact]
    public void A_real_logo_is_not_mistaken_for_the_placeholder()
    {
        Assert.False(AiProviderLogos.IsPlaceholder(RealLogo, Sha256Of(PlaceholderStandIn)));
        Assert.True(AiProviderLogos.IsPlaceholder(PlaceholderStandIn, Sha256Of(PlaceholderStandIn)));
    }

    [Fact]
    public async Task An_empty_body_reads_as_no_logo()
    {
        var logos = Make(new Stub(() => Svg(Array.Empty<byte>())));

        Assert.Null(await logos.GetLogoPathAsync("openrouter"));
    }
}
