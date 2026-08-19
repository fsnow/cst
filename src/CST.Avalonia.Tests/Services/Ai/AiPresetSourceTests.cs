using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services.Ai;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// #737: presets come from the catalogue plus a small hand-kept table, and carry a STATE — because two of
/// the three outcomes otherwise arrive as an empty list, and the loud one reads as a broken feature (#739).
/// </summary>
public class AiPresetSourceTests
{
    private sealed class FakeCatalog : IModelsDevCatalog
    {
        public CatalogResult Result { get; set; } =
            new(new Dictionary<string, CatalogProvider>(), CatalogSource.Network);
        public int Refreshes { get; private set; }

        public Task<CatalogResult> GetAsync(CancellationToken ct = default) => Task.FromResult(Result);
        public Task RefreshAsync(bool force = false, CancellationToken ct = default)
        { Refreshes++; return Task.CompletedTask; }
    }

    private static Dictionary<string, CatalogProvider> Catalogue(params CatalogProvider[] p) =>
        p.ToDictionary(x => x.Id, x => x);

    // ---- the inclusion rule ---------------------------------------------------------------------------

    [Fact]
    public void A_catalogue_provider_with_a_base_url_becomes_a_preset()
    {
        var built = AiPresetSource.Build(Catalogue(
            new CatalogProvider("acme", "Acme", "https://api.acme.test/v1", Env: new[] { "ACME_API_KEY" })));

        var acme = Assert.Single(built.Where(p => p.Id == "acme"));
        Assert.Equal("https://api.acme.test/v1", acme.BaseUrl);
        Assert.Equal(ChatProviderKind.OpenAiCompatible, acme.Kind);
        Assert.Contains("ACME_API_KEY", acme.EnvironmentVariables);
    }

    /// <summary>
    /// A provider with no base URL is skipped, not emitted broken. Recording a URL we do not have would
    /// produce a row that looks configured and fails at send time — the failure shape #728 and #735 removed.
    /// </summary>
    [Fact]
    public void A_catalogue_provider_without_a_base_url_is_skipped()
    {
        var built = AiPresetSource.Build(Catalogue(
            new CatalogProvider("sdk-only", "SDK Only", Api: null, Npm: "@ai-sdk/sdk-only")));

        Assert.DoesNotContain(built, p => p.Id == "sdk-only");
    }

    /// <summary>
    /// The case that would have dropped OpenAI and Anthropic. models.dev omits their `api` field because a
    /// dedicated package carries the URL — so the hand-kept table supplies it, and must win.
    /// </summary>
    [Fact]
    public void The_hand_kept_table_supplies_providers_the_catalogue_leaves_without_a_url()
    {
        var built = AiPresetSource.Build(Catalogue(
            new CatalogProvider("openai", "OpenAI", Api: null, Npm: "@ai-sdk/openai"),
            new CatalogProvider("anthropic", "Anthropic", Api: null, Npm: "@ai-sdk/anthropic")));

        var openai = Assert.Single(built.Where(p => p.Id == "openai"));
        Assert.Equal("https://api.openai.com/v1", openai.BaseUrl);

        var anthropic = Assert.Single(built.Where(p => p.Id == "anthropic"));
        Assert.Equal(ChatProviderKind.Anthropic, anthropic.Kind);
    }

    /// <summary>A hand-kept entry wins over a catalogue entry of the same id: it carries an auth shape the
    /// catalogue cannot describe. Azure is the case — `api-key`, no scheme.</summary>
    [Fact]
    public void A_hand_kept_entry_wins_over_the_catalogue()
    {
        var built = AiPresetSource.Build(Catalogue(
            new CatalogProvider("azure", "Azure", "https://wrong.example/v1")));

        var azure = Assert.Single(built.Where(p => p.Id == "azure"));
        Assert.Equal("api-key", azure.AuthHeaderName);
        Assert.Null(azure.AuthScheme);
        Assert.Contains("{resourceName}", azure.BaseUrl);
    }

    /// <summary>
    /// Cohere posts to `/chat` on its own protocol. It must not appear, and structurally cannot: every
    /// hand-kept entry carries an explicit Kind and the enum has two members, so there is no value to write.
    /// </summary>
    [Fact]
    public void A_provider_speaking_a_third_protocol_is_not_emitted()
    {
        var built = AiPresetSource.Build(Catalogue(
            new CatalogProvider("cohere", "Cohere", Api: null, Npm: "@ai-sdk/cohere")));

        Assert.DoesNotContain(built, p => p.Id == "cohere");
    }

    // ---- local runners --------------------------------------------------------------------------------

    /// <summary>Ollama is not in models.dev at all, and never will be — its models are whatever the reader
    /// pulled onto that machine.</summary>
    [Fact]
    public void Local_runners_appear_even_with_an_empty_catalogue()
    {
        var built = AiPresetSource.Build(new Dictionary<string, CatalogProvider>());

        Assert.Contains(built, p => p.Id == "ollama");
        Assert.Contains(built, p => p.Id == "lmstudio");
        Assert.All(built.Where(p => p.BaseUrl.Contains("localhost")), p => Assert.False(p.RequiresKey));
    }

    // ---- ordering -------------------------------------------------------------------------------------

    /// <summary>Alphabetical, and only alphabetical. A reader reads "first" as "best", so any hand-arranged
    /// order would be a claim we refuse to make (#670/#681).</summary>
    [Fact]
    public void Presets_are_ordered_alphabetically()
    {
        var built = AiPresetSource.Build(Catalogue(
            new CatalogProvider("zeta", "Zeta", "https://z.test/v1"),
            new CatalogProvider("alpha", "Alpha", "https://a.test/v1")));

        var names = built.Select(p => p.DisplayName).ToList();
        Assert.Equal(names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase), names);
    }

    // ---- state ----------------------------------------------------------------------------------------

    [Fact]
    public async Task A_loaded_catalogue_reports_ready()
    {
        var catalog = new FakeCatalog
        {
            Result = new CatalogResult(
                Catalogue(new CatalogProvider("acme", "Acme", "https://api.acme.test/v1")),
                CatalogSource.Network),
        };
        var source = new AiPresetSource(catalog);

        await source.EnsureLoadedAsync();

        Assert.Equal(AiPresetState.Ready, source.State);
        Assert.Null(source.Problem);
    }

    /// <summary>The snapshot is a legitimate source: a fresh offline install genuinely has those providers
    /// and can add them. Reporting it as a failure would put a retry button in front of a working list.</summary>
    [Fact]
    public async Task The_build_time_snapshot_reports_ready_not_unavailable()
    {
        var catalog = new FakeCatalog
        {
            Result = new CatalogResult(
                Catalogue(new CatalogProvider("acme", "Acme", "https://api.acme.test/v1")),
                CatalogSource.Snapshot),
        };
        var source = new AiPresetSource(catalog);

        await source.EnsureLoadedAsync();

        Assert.Equal(AiPresetState.Ready, source.State);
    }

    /// <summary>
    /// The rider that is easy to get wrong: "Unavailable" names the HOSTED catalogue as missing, not the
    /// section as empty. Ollama and LM Studio need no network to be useful, which makes them exactly the
    /// wrong thing to hide when the network is down.
    /// </summary>
    [Fact]
    public async Task Unavailable_still_returns_the_local_runners()
    {
        var catalog = new FakeCatalog
        {
            Result = new CatalogResult(
                new Dictionary<string, CatalogProvider>(), CatalogSource.None,
                Problem: "Couldn't reach the provider list."),
        };
        var source = new AiPresetSource(catalog);

        await source.EnsureLoadedAsync();

        Assert.Equal(AiPresetState.Unavailable, source.State);
        Assert.NotNull(source.Problem);
        Assert.Contains(source.Presets, p => p.Id == "ollama");
        Assert.Contains(source.Presets, p => p.Id == "lmstudio");
    }

    [Fact]
    public async Task Nothing_loaded_yet_reports_loading()
    {
        var source = new AiPresetSource(new FakeCatalog());

        Assert.Equal(AiPresetState.Loading, source.State);
        Assert.NotEmpty(source.Presets);   // local runners are available before any load
        await Task.CompletedTask;
    }

    /// <summary>A stale-but-usable list is Ready with a sentence — not a retry-me failure.</summary>
    [Fact]
    public async Task A_stale_copy_is_ready_and_still_says_so()
    {
        var catalog = new FakeCatalog
        {
            Result = new CatalogResult(
                Catalogue(new CatalogProvider("acme", "Acme", "https://api.acme.test/v1")),
                CatalogSource.Cache, Problem: "Couldn't reach the provider list. Showing the last copy."),
        };
        var source = new AiPresetSource(catalog);

        await source.EnsureLoadedAsync();

        Assert.Equal(AiPresetState.Ready, source.State);
        Assert.NotNull(source.Problem);
    }

    [Fact]
    public async Task Refresh_forces_a_fetch_and_announces_the_change()
    {
        var catalog = new FakeCatalog();
        var source = new AiPresetSource(catalog);
        int raised = 0;
        source.PresetsChanged += (_, _) => raised++;

        await source.RefreshAsync();

        Assert.Equal(1, catalog.Refreshes);
        Assert.Equal(1, raised);
    }

    // ---- the wire protocol comes from the catalogue, not from an assumption (fable review) -------------

    /// <summary>
    /// The finding that would have shipped eight broken presets. Eight catalogue providers carry a perfectly
    /// valid `api` URL and declare `@ai-sdk/anthropic` — minimax, thinkingmachines, kimi-for-coding and
    /// others — so that URL serves Anthropic Messages, not `/chat/completions`. Probed live:
    /// `/anthropic/v1/chat/completions` returns 404 while `/anthropic/v1/messages` returns 401.
    /// </summary>
    [Fact]
    public void A_provider_declaring_the_anthropic_sdk_is_emitted_as_anthropic()
    {
        var built = AiPresetSource.Build(Catalogue(
            new CatalogProvider("minimax", "MiniMax", "https://api.minimax.io/anthropic/v1",
                Npm: "@ai-sdk/anthropic")));

        var minimax = Assert.Single(built.Where(p => p.Id == "minimax"));
        Assert.Equal(ChatProviderKind.Anthropic, minimax.Kind);
    }

    /// <summary>Deliberately not a general package-to-kind mapping: OpenRouter ships its own provider package
    /// and is genuinely OpenAI-compatible, so only the one package whose protocol we can name is special.</summary>
    [Fact]
    public void Another_vendors_own_sdk_package_does_not_change_the_protocol()
    {
        var built = AiPresetSource.Build(Catalogue(
            new CatalogProvider("openrouter", "OpenRouter", "https://openrouter.ai/api/v1",
                Npm: "@openrouter/ai-sdk-provider")));

        Assert.Equal(ChatProviderKind.OpenAiCompatible,
            built.Single(p => p.Id == "openrouter").Kind);
    }

    /// <summary>Endpoints whose URL parses fine but which our resolution addresses wrongly. Both were
    /// confirmed by probing rather than reasoned about; revisit when #742 lands.</summary>
    [Theory]
    [InlineData("github-copilot", "https://api.githubcopilot.com")]
    [InlineData("perplexity-agent", "https://api.perplexity.ai/v1")]
    public void An_endpoint_we_would_address_wrongly_is_not_emitted(string id, string api)
    {
        var built = AiPresetSource.Build(Catalogue(new CatalogProvider(id, id, api)));

        Assert.DoesNotContain(built, p => p.Id == id);
    }

    /// <summary>
    /// The guard was dead code: subtracting only the local runners left the count permanently above zero,
    /// because Build always seeds the whole hand-kept table. A models.dev reshape leaving every record
    /// templated would then have reported Ready with a dozen presets and no Problem — a silent collapse.
    /// </summary>
    [Fact]
    public async Task A_catalogue_that_yields_no_hosted_presets_reports_unavailable()
    {
        var catalog = new FakeCatalog
        {
            // Parses fine, plenty of records, every one unusable: templated against host-expanded variables.
            Result = new CatalogResult(
                Catalogue(Enumerable.Range(0, 60)
                    .Select(i => new CatalogProvider($"p{i}", $"P{i}", "https://${HOST}/v1"))
                    .ToArray()),
                CatalogSource.Network),
        };
        var source = new AiPresetSource(catalog);

        await source.EnsureLoadedAsync();

        Assert.Equal(AiPresetState.Unavailable, source.State);
        Assert.Contains(source.Presets, p => p.Id == "ollama");

        // Reached and unusable is not the same situation as unreachable, and must not borrow its sentence -
        // that would send a reader to check their network over a problem that is ours.
        Assert.NotNull(source.Problem);
        Assert.DoesNotContain("reach", source.Problem!, System.StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>Ties fall back to insertion order, which is the source document's order — the last path by
    /// which models.dev's own ordering could decide what a reader sees first (#670/#681).</summary>
    [Fact]
    public void A_shared_display_name_is_ordered_by_id_not_by_document_order()
    {
        var forward = AiPresetSource.Build(Catalogue(
            new CatalogProvider("zzz", "Same Name", "https://z.test/v1"),
            new CatalogProvider("aaa", "Same Name", "https://a.test/v1")));

        var tied = forward.Where(p => p.DisplayName == "Same Name").Select(p => p.Id).ToList();
        Assert.Equal(new[] { "aaa", "zzz" }, tied);
    }

    /// <summary>Before the first load, a caller WITH the service must not see fewer providers than one
    /// without it — that window is an HTTP timeout long when the cache is stale and the host is hung.</summary>
    [Fact]
    public void Presets_are_seeded_from_the_snapshot_before_any_load()
    {
        var source = new AiPresetSource(new FakeCatalog());

        Assert.Equal(AiPresetState.Loading, source.State);
        Assert.True(source.Presets.Count > 100,
            $"seeded with only {source.Presets.Count}; AddFromPreset would refuse known providers");
    }
}
