using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Services.Ai;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// #736: the catalogue must degrade, never disappear. Load order is cache → snapshot → network, and every
/// failure path keeps whatever was already available — because the alternative a reader sees is an empty
/// provider list, which reads as a broken feature rather than as a network problem (#739).
/// </summary>
public class ModelsDevCatalogTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cst-736-" + Guid.NewGuid().ToString("N"));

    public ModelsDevCatalogTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private string CachePath => Path.Combine(_dir, "models-dev.json");

    private const string TwoProviders = """
        {"openrouter":{"id":"openrouter","name":"OpenRouter","api":"https://openrouter.ai/api/v1",
          "env":["OPENROUTER_API_KEY"],"doc":"https://openrouter.ai/models"},
         "openai":{"id":"openai","name":"OpenAI","npm":"@ai-sdk/openai","env":["OPENAI_API_KEY"]}}
        """;

    /// <summary>A document large enough to clear the plausibility floor a fetch applies. The floor exists so
    /// an API error body cannot pose as a catalogue, so a network-success fixture has to be realistic-sized;
    /// a cache read applies no floor, which is why the two-provider fixture is still used there.</summary>
    private static string ManyProviders(int count = 60) =>
        "{" + string.Join(",", Enumerable.Range(0, count).Select(i =>
            $"\"p{i}\":{{\"id\":\"p{i}\",\"name\":\"Provider {i}\",\"api\":\"https://p{i}.example/v1\"}}")) + "}";

    private sealed class Stub : HttpMessageHandler
    {
        private readonly Func<HttpResponseMessage> _reply;
        public int Calls { get; private set; }
        public Stub(Func<HttpResponseMessage> reply) => _reply = reply;
        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage r, CancellationToken ct)
        { Calls++; return Task.FromResult(_reply()); }
    }

    private static HttpClient Client(Func<HttpResponseMessage> reply, out Stub stub)
    {
        stub = new Stub(reply);
        return new HttpClient(stub);
    }

    private static HttpResponseMessage Ok(string body) =>
        new(HttpStatusCode.OK) { Content = new StringContent(body) };

    // minimumProviders: 1 so the fixtures can stay two providers wide and readable. The floor's own
    // behaviour is tested by passing a real one — see the cache-floor tests below. (R4-5)
    private ModelsDevCatalog Make(HttpClient? http = null, int minimumProviders = 1) =>
        new(http ?? new HttpClient(new Stub(() => Ok(TwoProviders))), CachePath,
            "https://example.invalid/api.json", minimumProviders);

    /// <summary>
    /// The plausibility floor guarded only the network path; a sub-floor file on disk was served as the
    /// catalogue. (R4-5)
    ///
    /// <para>A hand-edited or externally truncated <c>models-dev.json</c> then became the provider list with
    /// no problem reported — <c>AiPresetSource</c>'s collapse guard fires only at zero hosted providers, and
    /// one is enough to clear it. Permanent while offline, self-healing only after a successful refetch.</para>
    /// </summary>
    [Fact]
    public async Task A_cache_with_implausibly_few_providers_is_not_served()
    {
        File.WriteAllText(CachePath, TwoProviders);

        // Offline, so the cache is the only thing that could answer.
        var offline = new HttpClient(new Stub(() => throw new HttpRequestException("offline")));
        var result = await Make(offline, minimumProviders: 50).GetAsync();

        Assert.NotEqual(CatalogSource.Cache, result.Source);
        Assert.False(File.Exists(CachePath));   // discarded, so a later refetch can heal it
    }

    /// <summary>And a cache that clears the floor is still preferred — the rule is a floor, not a
    /// rejection of caches.</summary>
    [Fact]
    public async Task A_cache_that_clears_the_floor_is_still_served()
    {
        File.WriteAllText(CachePath, TwoProviders);

        var offline = new HttpClient(new Stub(() => throw new HttpRequestException("offline")));
        var result = await Make(offline, minimumProviders: 2).GetAsync();

        Assert.Equal(CatalogSource.Cache, result.Source);
    }

    // ---- the floor ------------------------------------------------------------------------------------

    /// <summary>
    /// With no cache and no network, the app still has providers — from the snapshot compiled in at build
    /// time. This is the case that makes a fresh offline install usable with a local runner, which is the
    /// configuration this project has most reason to support.
    /// </summary>
    [Fact]
    public async Task With_no_cache_the_embedded_snapshot_is_used()
    {
        var result = await Make().GetAsync();

        Assert.Equal(CatalogSource.Snapshot, result.Source);
        Assert.True(result.Providers.Count > 100, $"snapshot looks wrong: {result.Providers.Count} providers");
        Assert.Contains("openrouter", result.Providers.Keys);
    }

    /// <summary>The snapshot is what #737 generates presets from, so the fields it needs must survive the
    /// round trip — not just the count.</summary>
    [Fact]
    public async Task The_snapshot_carries_the_fields_presets_need()
    {
        var result = await Make().GetAsync();
        var openrouter = result.Providers["openrouter"];

        Assert.Equal("openrouter", openrouter.Id);
        Assert.False(string.IsNullOrWhiteSpace(openrouter.Name));
        Assert.Equal("https://openrouter.ai/api/v1", openrouter.Api);
        Assert.NotNull(openrouter.Env);
        Assert.Contains("OPENROUTER_API_KEY", openrouter.Env!);
    }

    /// <summary>A provider with no `api` is NOT unsupported — models.dev omits the URL when a dedicated SDK
    /// carries it. Asserted here because reading it as "unsupported" would drop OpenAI and Anthropic (#737).</summary>
    [Fact]
    public async Task A_provider_without_an_api_url_is_still_carried()
    {
        var result = await Make().GetAsync();

        Assert.True(result.Providers.TryGetValue("openai", out var openai));
        Assert.Null(openai!.Api);
        Assert.NotNull(openai.Npm);
    }

    // ---- cache ---------------------------------------------------------------------------------------

    [Fact]
    public async Task A_cache_is_preferred_over_the_snapshot()
    {
        File.WriteAllText(CachePath, TwoProviders);

        var result = await Make().GetAsync();

        Assert.Equal(CatalogSource.Cache, result.Source);
        Assert.Equal(2, result.Providers.Count);
    }

    [Fact]
    public async Task A_refresh_writes_the_cache_and_reports_the_network()
    {
        var catalog = Make(Client(() => Ok(ManyProviders()), out _));

        await catalog.RefreshAsync(force: true);
        var result = await catalog.GetAsync();

        Assert.Equal(CatalogSource.Network, result.Source);
        Assert.Equal(60, result.Providers.Count);
        Assert.True(File.Exists(CachePath));
    }

    /// <summary>A fresh cache must not be re-fetched on every start; several launches in an hour should cost
    /// one request, not one each.</summary>
    [Fact]
    public async Task A_fresh_cache_is_not_refetched()
    {
        File.WriteAllText(CachePath, TwoProviders);
        var catalog = Make(Client(() => Ok(TwoProviders), out var stub));

        await catalog.RefreshAsync();

        Assert.Equal(0, stub.Calls);
    }

    [Fact]
    public async Task Force_refetches_even_when_the_cache_is_fresh()
    {
        File.WriteAllText(CachePath, TwoProviders);
        var catalog = Make(Client(() => Ok(ManyProviders()), out var stub));

        await catalog.RefreshAsync(force: true);

        Assert.Equal(1, stub.Calls);
    }

    // ---- failure keeps what we had --------------------------------------------------------------------

    /// <summary>
    /// The property the whole design rests on. models.dev being unreachable must degrade to "slightly stale",
    /// never to nothing — the reader may be about to configure a local runner that needs no network at all.
    /// </summary>
    [Fact]
    public async Task A_network_failure_keeps_the_previous_copy_and_says_so()
    {
        File.WriteAllText(CachePath, TwoProviders);
        var catalog = Make(new HttpClient(new Stub(() => throw new HttpRequestException("offline"))));

        await catalog.RefreshAsync(force: true);
        var result = await catalog.GetAsync();

        // Source and count asserted, not merely NotEmpty: this test would otherwise pass if the snapshot had
        // silently replaced the cache, which is the failure it exists to catch. (fable review)
        Assert.Equal(CatalogSource.Cache, result.Source);
        Assert.Equal(2, result.Providers.Count);
        Assert.NotNull(result.Problem);
        Assert.Contains("Couldn't reach", result.Problem!);
    }

    /// <summary>A 200 carrying an HTML error page is the realistic failure, not a 404 — so the shape is
    /// validated rather than the status code trusted.</summary>
    [Fact]
    public async Task A_200_that_is_not_the_catalogue_is_rejected()
    {
        File.WriteAllText(CachePath, TwoProviders);
        var catalog = Make(Client(() => Ok("<!doctype html><html>not json</html>"), out _));

        await catalog.RefreshAsync(force: true);
        var result = await catalog.GetAsync();

        Assert.Equal(CatalogSource.Cache, result.Source);
        Assert.NotNull(result.Problem);
    }

    /// <summary>
    /// The one path where a failure destroyed what we had. An API error body is VALID JSON that deserializes
    /// to a single record carrying an id — so a `Count > 0` guard accepted it, overwrote a good 192-provider
    /// cache, and became the answer for every subsequent start. A reader who then went offline was stuck with
    /// one provider. (fable review)
    /// </summary>
    [Fact]
    public async Task An_error_body_that_is_valid_json_does_not_replace_the_cache()
    {
        File.WriteAllText(CachePath, TwoProviders);

        // An explicit floor, because this test IS the floor: the error body parses to one record, so it
        // clears the harness default of 1 and would be accepted. (R4-5)
        var catalog = Make(Client(() => Ok("""{"error":{"id":"rate_limited","message":"slow down"}}"""), out _),
                           minimumProviders: 2);

        await catalog.RefreshAsync(force: true);
        var result = await catalog.GetAsync();

        Assert.Equal(CatalogSource.Cache, result.Source);
        Assert.Equal(2, result.Providers.Count);
        Assert.NotNull(result.Problem);

        // And the poison never reached disk, so the next start is not stuck with it either.
        Assert.Equal(2, ModelsDevCatalog.Parse(File.ReadAllText(CachePath))!.Count);
    }

    /// <summary>A hung endpoint surfaces as TaskCanceledException from HttpClient.Timeout. It must arrive as
    /// a Problem, not thrown — #739's retry button calls RefreshAsync directly. (fable review)</summary>
    [Fact]
    public async Task A_client_timeout_arrives_as_a_problem_rather_than_an_exception()
    {
        File.WriteAllText(CachePath, TwoProviders);
        var catalog = Make(new HttpClient(new Stub(() => throw new TaskCanceledException("timed out"))));

        await catalog.RefreshAsync(force: true);   // must not throw
        var result = await catalog.GetAsync();

        Assert.Equal(CatalogSource.Cache, result.Source);
        Assert.NotNull(result.Problem);
    }

    /// <summary>A corrupt cache is discarded rather than fatal, and the snapshot carries the session.</summary>
    [Fact]
    public async Task A_corrupt_cache_falls_back_to_the_snapshot_and_is_deleted()
    {
        File.WriteAllText(CachePath, "{ this is not json");

        var result = await Make().GetAsync();

        Assert.Equal(CatalogSource.Snapshot, result.Source);
        Assert.False(File.Exists(CachePath), "a cache that cannot be read should be removed, not left to fail again");
    }

    // ---- parsing -------------------------------------------------------------------------------------

    /// <summary>A record with no id cannot be used and signals a document that is not what we think it is.
    /// Skipped rather than carried, and its presence must not discard the good ones.</summary>
    [Fact]
    public void Records_without_an_id_are_skipped_not_fatal()
    {
        var parsed = ModelsDevCatalog.Parse(
            """{"good":{"id":"good"},"bad":{"name":"no id here"}}""");

        Assert.NotNull(parsed);
        Assert.Single(parsed!);
        Assert.Contains("good", parsed.Keys);
    }

    [Fact]
    public void Junk_parses_to_null_rather_than_throwing()
    {
        Assert.Null(ModelsDevCatalog.Parse("<!doctype html>"));
    }
}
