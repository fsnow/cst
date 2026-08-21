using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Models;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// #689: the service the connections UI (#691/#692/#693) binds to. Settings-backed; no credential handling
/// yet, which is what lets the UI work start in parallel with the keychain re-keying.
/// </summary>
public class AiConnectionServiceTests
{
    private static (AiConnectionService Service, Settings Settings) Make()
    {
        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        return (new AiConnectionService(svc.Object), settings);
    }

    private static AiConnectionDraft Draft(string name = "My box", string url = "http://localhost:8000/v1") =>
        new(name, ChatProviderKind.OpenAiCompatible, url,
            new List<AiModelEntry>(), new Dictionary<string, string>(), new Dictionary<string, string>());

    // ---- ids ------------------------------------------------------------------------------------------

    /// <summary>The id becomes the credential's account name, so a duplicate would mean one connection
    /// quietly inheriting another's key. Refused explicitly rather than overwriting.</summary>
    [Fact]
    public void A_duplicate_id_is_refused()
    {
        var (service, _) = Make();
        Assert.True(service.Add("my-box", Draft()).Ok);

        var second = service.Add("my-box", Draft("Another"));

        Assert.False(second.Ok);
        Assert.Contains("already", second.Problem);
    }

    /// <summary>Preset ids are reserved: taking one by hand would collide with the built-in the reader might
    /// add later, and the collision would be a credential mix-up rather than a visible error.</summary>
    [Fact]
    public void A_preset_id_cannot_be_taken_by_a_custom_connection()
    {
        var (service, _) = Make();

        var result = service.Add("openrouter", Draft());

        Assert.False(result.Ok);
        Assert.Contains("built-in", result.Problem);
    }

    [Theory]
    [InlineData("My-Box")]      // uppercase
    [InlineData("my box")]      // space
    [InlineData("my.box")]      // dot
    [InlineData("-leading")]    // leading hyphen
    [InlineData("")]
    public void An_id_that_is_not_a_slug_is_refused(string id)
    {
        var (service, _) = Make();
        Assert.False(service.Add(id, Draft()).Ok);
    }

    // ---- presets --------------------------------------------------------------------------------------

    /// <summary>An added preset drops out of the available list, so the "add a provider" section always reads
    /// as what you could add next rather than a list where some rows are already spoken for.</summary>
    [Fact]
    public void An_added_preset_leaves_the_available_list()
    {
        var (service, _) = Make();
        Assert.Contains(service.AvailablePresets, p => p.Id == "openrouter");

        service.AddFromPreset("openrouter", new Dictionary<string, string>());

        Assert.DoesNotContain(service.AvailablePresets, p => p.Id == "openrouter");
        Assert.Contains(service.Connections, c => c.Id == "openrouter");
    }

    [Fact]
    public void A_preset_arrives_with_its_url_and_kind_filled_in()
    {
        var (service, _) = Make();

        var result = service.AddFromPreset("openrouter", new Dictionary<string, string>());

        Assert.True(result.Ok);
        Assert.Equal("https://openrouter.ai/api/v1", result.Connection!.BaseUrl);
        Assert.Equal(ChatProviderKind.OpenAiCompatible, result.Connection.Kind);
    }

    /// <summary>
    /// A preset whose URL is a template must not be addable without its inputs — the connection would keep its
    /// {placeholders} and could never send anything, failing later as a DNS error naming nothing.
    /// </summary>
    [Fact]
    public void A_templated_preset_is_refused_without_its_inputs()
    {
        var (service, _) = Make();

        var result = service.AddFromPreset("azure", new Dictionary<string, string>());

        Assert.False(result.Ok);
        Assert.Contains("resource name", result.Problem, System.StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void A_templated_preset_resolves_its_url_once_the_inputs_are_given()
    {
        var (service, _) = Make();

        var result = service.AddFromPreset("azure", new Dictionary<string, string> { ["resourceName"] = "acme" });

        Assert.True(result.Ok);
        Assert.False(result.Connection!.IsIncomplete);
        Assert.Equal("https://acme.openai.azure.com/openai/v1", result.Connection.ResolvedBaseUrl);
    }

    // ---- active selection ------------------------------------------------------------------------------

    [Fact]
    public void The_first_connection_added_becomes_active()
    {
        var (service, _) = Make();
        service.Add("first", Draft());
        service.Add("second", Draft());

        Assert.Equal("first", service.Active!.Id);
    }

    /// <summary>Selecting a model that the connection does not offer must fail rather than store a value that
    /// resolves to nothing at send time.</summary>
    [Fact]
    public void An_unknown_model_cannot_be_made_active()
    {
        var (service, _) = Make();
        service.Add("box", Draft());

        Assert.False(service.SetActive("box", "no-such-model").Ok);
    }

    [Fact]
    public void A_model_the_connection_offers_can_be_made_active()
    {
        var (service, _) = Make();
        service.Add("box", Draft() with { Models = new[] { new AiModelEntry("llama-3", "Llama 3") } });

        Assert.True(service.SetActive("box", "llama-3").Ok);
        Assert.Equal("llama-3", service.ActiveModelId);
    }

    /// <summary>
    /// Removing the active connection must not leave the pointer dangling. A stale id reads as "configured"
    /// to anything that only checks for null, which would present as a request going nowhere.
    /// </summary>
    [Fact]
    public void Removing_the_active_connection_moves_the_pointer()
    {
        var (service, settings) = Make();
        service.Add("first", Draft());
        service.Add("second", Draft());

        Assert.True(service.Remove("first").Ok);

        Assert.Equal("second", settings.Ai.Chat.ActiveConnectionId);
        Assert.Null(settings.Ai.Chat.ActiveModelId);
    }

    [Fact]
    public void Removing_the_last_connection_leaves_nothing_active()
    {
        var (service, settings) = Make();
        service.Add("only", Draft());

        service.Remove("only");

        Assert.Null(settings.Ai.Chat.ActiveConnectionId);
        Assert.Null(service.Active);
    }

    // ---- models ---------------------------------------------------------------------------------------

    /// <summary>Models arrive enabled. All-on is neutral; a pre-selected subset would be a quality verdict,
    /// which is the registry removed in #670/#681 wearing a toggle.</summary>
    [Fact]
    public void Models_are_enabled_by_default()
    {
        var (service, _) = Make();
        var result = service.Add("box", Draft() with
        {
            Models = new[] { new AiModelEntry("a", "A"), new AiModelEntry("b", "B") }
        });

        Assert.All(result.Connection!.Models, m => Assert.True(m.Enabled));
    }

    [Fact]
    public void A_model_can_be_switched_off_without_removing_it()
    {
        var (service, _) = Make();
        service.Add("box", Draft() with { Models = new[] { new AiModelEntry("a", "A") } });

        Assert.True(service.SetModelEnabled("box", "a", false).Ok);

        var model = Assert.Single(service.Connections.Single().Models);
        Assert.False(model.Enabled);
        Assert.Equal("a", model.Id);
    }

    // ---- change notification ---------------------------------------------------------------------------

    /// <summary>The UI rebinds on this; a mutation that does not raise it presents as a screen that has
    /// silently stopped matching what is stored.</summary>
    [Fact]
    public void Every_mutation_raises_the_change_event()
    {
        var (service, _) = Make();
        int raised = 0;
        service.ConnectionsChanged += (_, _) => raised++;

        service.Add("box", Draft() with { Models = new[] { new AiModelEntry("a", "A") } });
        service.Update("box", Draft("Renamed"));
        service.Add("other", Draft() with { Models = new[] { new AiModelEntry("a", "A") } });
        service.SetModelEnabled("other", "a", false);
        service.SetActive("other", "a");
        service.Remove("other");

        Assert.Equal(6, raised);
    }

    /// <summary>The id is immutable because the credential is filed under it; an update must not silently
    /// change it even if a draft carried one.</summary>
    [Fact]
    public void Updating_edits_everything_except_the_id()
    {
        var (service, _) = Make();
        service.Add("box", Draft("Original", "http://a/v1"));

        var result = service.Update("box", Draft("Renamed", "http://b/v1"));

        Assert.True(result.Ok);
        Assert.Equal("box", result.Connection!.Id);
        Assert.Equal("Renamed", result.Connection.DisplayName);
        Assert.Equal("http://b/v1", result.Connection.BaseUrl);
    }

    /// <summary>An in-memory credential store, keyed by connection id exactly as the real one now is.</summary>
    private sealed class Keys : IAiCredentialStore
    {
        private readonly Dictionary<string, string> _byConnection = new();
        public bool IsAvailable => true;
        public string? Unavailable => null;
        public string? GetApiKey(string connectionId) =>
            _byConnection.TryGetValue(connectionId, out var k) ? k : null;
        public bool SetApiKey(string connectionId, string apiKey) { _byConnection[connectionId] = apiKey; return true; }
        public bool DeleteApiKey(string connectionId) { _byConnection.Remove(connectionId); return true; }
    }

    private static (AiConnectionService Service, Settings Settings, Keys Keys) MakeWithKeys()
    {
        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        var keys = new Keys();
        return (new AiConnectionService(svc.Object, keys), settings, keys);
    }

    // ---- credentials, keyed per connection (#678) --------------------------------------------------------

    /// <summary>
    /// The defect #678 exists for, stated as a test. Two OpenAI-compatible endpoints previously shared one
    /// credential slot, because the store was keyed by a two-member provider-kind enum — so storing the second
    /// key silently replaced the first, and the reader got a 401 naming neither cause.
    /// </summary>
    [Fact]
    public void Two_openai_compatible_endpoints_keep_separate_keys()
    {
        var (service, _, keys) = MakeWithKeys();
        service.Add("openrouter-box", Draft("OpenRouter", "https://openrouter.ai/api/v1"));
        service.Add("local-ollama", Draft("Ollama", "http://localhost:11434/v1"));

        keys.SetApiKey("openrouter-box", "or-key");
        keys.SetApiKey("local-ollama", "ollama-key");

        Assert.Equal("or-key", keys.GetApiKey("openrouter-box"));
        Assert.Equal("ollama-key", keys.GetApiKey("local-ollama"));
    }

    /// <summary>A connection reports where its credential came from, so the UI can name the source and — for
    /// an environment-sourced one — offer no remove action rather than a button that would lie.</summary>
    [Fact]
    public void A_connection_reports_whether_a_key_is_stored_for_it()
    {
        var (service, _, keys) = MakeWithKeys();
        service.Add("box", Draft());

        Assert.Equal(CredentialSource.None, service.Connections.Single().KeySource);

        keys.SetApiKey("box", "k");

        Assert.Equal(CredentialSource.Keychain, service.Connections.Single().KeySource);
    }

    /// <summary>
    /// Removing a connection takes its credential with it. An orphaned key is unreachable and uncleanable —
    /// and worse, would be silently re-adopted by a later connection that happened to take the same id.
    /// </summary>
    [Fact]
    public void Removing_a_connection_removes_its_key()
    {
        var (service, _, keys) = MakeWithKeys();
        service.Add("box", Draft());
        keys.SetApiKey("box", "k");

        service.Remove("box");

        Assert.Null(keys.GetApiKey("box"));
    }

    // ---- reachability write-back (#673) ------------------------------------------------------------------

    /// <summary>
    /// The honest default. "Configured" means the endpoint has never been contacted, NOT that it works —
    /// conflating those is what lets a settings page claim "Connected" while the assistant reports it cannot
    /// connect, which is the contradiction #673 exists to remove.
    /// </summary>
    [Fact]
    public void A_new_connection_starts_as_configured_not_reachable()
    {
        var (service, _) = Make();
        service.Add("box", Draft());

        Assert.Equal(Reachability.Configured, service.Connections.Single().State);
    }

    [Fact]
    public void A_failed_request_marks_the_endpoint_unreachable()
    {
        var (service, _) = Make();
        service.Add("box", Draft());

        service.ReportReachability("box", reachable: false);

        Assert.Equal(Reachability.Unreachable, service.Connections.Single().State);
    }

    [Fact]
    public void A_successful_request_marks_it_reachable_again()
    {
        var (service, _) = Make();
        service.Add("box", Draft());
        service.ReportReachability("box", reachable: false);

        service.ReportReachability("box", reachable: true);

        Assert.Equal(Reachability.Reachable, service.Connections.Single().State);
    }

    /// <summary>The UI rebinds on the change event, so a state change nobody is told about is a screen that
    /// silently stops matching what the app knows — the whole defect, reintroduced.</summary>
    [Fact]
    public void A_reachability_change_raises_the_change_event()
    {
        var (service, _) = Make();
        service.Add("box", Draft());

        int raised = 0;
        service.ConnectionsChanged += (_, _) => raised++;

        service.ReportReachability("box", reachable: false);

        Assert.Equal(1, raised);
    }

    /// <summary>Reporting the same state twice must not churn the UI — a turn a second reports success would
    /// otherwise rebind every screen bound to the list.</summary>
    [Fact]
    public void Reporting_the_same_state_again_is_silent()
    {
        var (service, _) = Make();
        service.Add("box", Draft());
        service.ReportReachability("box", reachable: true);

        int raised = 0;
        service.ConnectionsChanged += (_, _) => raised++;
        service.ReportReachability("box", reachable: true);

        Assert.Equal(0, raised);
    }

    /// <summary>
    /// Reachability is per-session and deliberately not persisted: "unreachable" is a fact about a moment — a
    /// laptop that was offline, a runner not yet started — and storing it would greet the reader with a red
    /// endpoint on next launch that nothing clears until something happens to retry it.
    /// </summary>
    [Fact]
    public void Reachability_is_not_written_to_settings()
    {
        var (service, settings) = Make();
        service.Add("box", Draft());
        service.ReportReachability("box", reachable: false);

        var json = System.Text.Json.JsonSerializer.Serialize(settings);

        Assert.DoesNotContain("Unreachable", json);
        Assert.DoesNotContain("Reachab", json);
    }

    private static AiConnectionDraft WithModels(params AiModelEntry[] models) =>
        Draft() with { Models = models };

    // ---- recording what a listing carried (#728) ---------------------------------------------------------

    /// <summary>The mark follows the last good listing in both directions: set for what it dropped, cleared
    /// for what it carries again.</summary>
    [Fact]
    public void Marking_a_listing_sets_and_clears_in_step_with_it()
    {
        var (service, _) = Make();
        service.Add("box", WithModels(new AiModelEntry("a", "A"), new AiModelEntry("b", "B")));

        service.MarkListing("box", new[] { "a" });
        Assert.True(service.Connections.Single().Models.Single(m => m.Id == "b").Missing);

        service.MarkListing("box", new[] { "a", "b" });
        Assert.False(service.Connections.Single().Models.Single(m => m.Id == "b").Missing);
    }

    /// <summary>
    /// Recording a listing that says nothing new writes nothing and tells nobody.
    ///
    /// <para>The Models tab records the listing on every successful fetch, which happens whenever the tab is
    /// opened. Saving unconditionally would rewrite <c>settings.json</c> and rebuild the list the reader is
    /// looking at, every time, to say exactly what it said before.</para>
    /// </summary>
    [Fact]
    public void Recording_the_same_listing_twice_changes_nothing()
    {
        var (service, _) = Make();
        service.Add("box", WithModels(new AiModelEntry("a", "A"), new AiModelEntry("b", "B")));
        service.MarkListing("box", new[] { "a" });

        var raised = 0;
        service.ConnectionsChanged += (_, _) => raised++;
        service.MarkListing("box", new[] { "a" });

        Assert.Equal(0, raised);
    }

    /// <summary>An empty listing is no evidence, not total removal — a key without listing scope, or a
    /// gateway with no upstream configured, would otherwise mark every model the reader has.</summary>
    [Fact]
    public void An_empty_listing_marks_nothing()
    {
        var (service, _) = Make();
        service.Add("box", WithModels(new AiModelEntry("a", "A")));

        service.MarkListing("box", Array.Empty<string>());

        Assert.False(service.Connections.Single().Models.Single().Missing);
    }

    /// <summary>Promoting a model from a listing that carries it clears the mark too, so the two paths that
    /// write it cannot disagree.</summary>
    [Fact]
    public void Promoting_a_model_from_the_listing_clears_its_mark()
    {
        var (service, _) = Make();
        service.Add("box", WithModels(new AiModelEntry("a", "A")));
        service.MarkListing("box", new[] { "other" });

        service.EnableModel("box", "a", "A", true, new AiModelEntry("a", "A", ContextLength: 8192));

        Assert.False(service.Connections.Single().Models.Single().Missing);
    }

    // ---- the endpoint's measured path convention (#742) --------------------------------------------------

    [Fact]
    public void A_measured_path_convention_is_recorded_on_the_connection()
    {
        var (service, settings) = Make();
        service.Add("perplexity-custom", Draft(url: "https://api.perplexity.ai"));

        service.ReportEndpointVersioning("perplexity-custom", false);

        var record = settings.Ai.Chat.Connections.Single(c => c.Id == "perplexity-custom");
        Assert.False(record.UsesVersionSegment);
        Assert.False(service.Connections.Single(c => c.Id == "perplexity-custom").UsesVersionSegment);
    }

    /// <summary>
    /// The Models tab records this on every successful fetch, so re-reporting the same answer must not
    /// rewrite settings or raise ConnectionsChanged — a tab opened five times would otherwise churn both.
    /// </summary>
    [Fact]
    public void Re_reporting_the_same_convention_raises_nothing()
    {
        var (service, _) = Make();
        service.Add("my-box", Draft(url: "https://api.perplexity.ai"));
        service.ReportEndpointVersioning("my-box", false);

        var raised = 0;
        service.ConnectionsChanged += (_, _) => raised++;
        service.ReportEndpointVersioning("my-box", false);

        Assert.Equal(0, raised);
    }

    /// <summary>
    /// It is a fact about one URL. Retyping the base URL invalidates it, and keeping it would apply the old
    /// endpoint's convention to a new host — the failure being silent is the whole reason #742 exists.
    /// </summary>
    [Fact]
    public void Retyping_the_base_url_forgets_what_was_measured()
    {
        var (service, settings) = Make();
        service.Add("my-box", Draft(url: "https://api.perplexity.ai"));
        service.ReportEndpointVersioning("my-box", false);

        service.Update("my-box", Draft(url: "https://api.deepseek.com"));

        Assert.Null(settings.Ai.Chat.Connections.Single(c => c.Id == "my-box").UsesVersionSegment);
    }

    /// <summary>An edit that leaves the URL alone keeps it — renaming a connection is not a reason to
    /// re-probe an endpoint that has not moved.</summary>
    [Fact]
    public void An_edit_that_leaves_the_url_alone_keeps_what_was_measured()
    {
        var (service, settings) = Make();
        service.Add("my-box", Draft(url: "https://api.perplexity.ai"));
        service.ReportEndpointVersioning("my-box", false);

        service.Update("my-box", Draft(name: "Renamed", url: "https://api.perplexity.ai"));

        Assert.False(settings.Ai.Chat.Connections.Single(c => c.Id == "my-box").UsesVersionSegment);
    }
}
