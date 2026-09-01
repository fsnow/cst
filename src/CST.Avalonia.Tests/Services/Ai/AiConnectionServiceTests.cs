using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Models;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services.Ai.Credentials;
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
            new List<AiModelEntry>(), Array.Empty<AiHeader>(), new Dictionary<string, string>());

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

    // ---- duplicate model ids (#870) --------------------------------------------------------------------

    private static AiConnectionDraft DraftWith(params AiModelEntry[] models) =>
        new("My box", ChatProviderKind.OpenAiCompatible, "http://localhost:8000/v1",
            models, Array.Empty<AiHeader>(), new Dictionary<string, string>());

    /// <summary>
    /// Two rows with one id are refused on the way in, the way two headers with one name already were.
    ///
    /// <para>Nothing used to stop them: the sheet let a reader paste an id twice, the draft was copied
    /// verbatim into the record, and the models tab then threw <c>ArgumentException</c> keying its stored
    /// list — inside the Settings window's own construction, so the window would not open again. (#870)</para>
    /// </summary>
    [Fact]
    public void Two_models_with_one_id_are_refused_on_add()
    {
        var (service, settings) = Make();

        var result = service.Add("mine", DraftWith(
            new AiModelEntry("llama3.1:8b", "Llama"), new AiModelEntry("llama3.1:8b", "Llama again")));

        Assert.False(result.Ok);
        Assert.Contains("llama3.1:8b", result.Problem);
        Assert.Empty(settings.Ai.Chat.Connections);
    }

    /// <summary>The same refusal on the edit path — where it is likelier, since the sheet opens with the
    /// existing rows and a reader adds one more.</summary>
    [Fact]
    public void Two_models_with_one_id_are_refused_on_update()
    {
        var (service, settings) = Make();
        Assert.True(service.Add("mine", DraftWith(new AiModelEntry("a", "A"))).Ok);

        var result = service.Update("mine", DraftWith(
            new AiModelEntry("a", "A"), new AiModelEntry("a", "A again")));

        Assert.False(result.Ok);
        Assert.Single(settings.Ai.Chat.Connections.Single().Models);
    }

    /// <summary>Whitespace is what makes the pair, so it is trimmed before the comparison rather than after —
    /// the record stores trimmed ids, so an untrimmed match would let the pair through to the same crash.
    /// </summary>
    [Fact]
    public void A_padded_repeat_of_a_model_id_is_refused()
    {
        var (service, _) = Make();

        var result = service.Add("mine", DraftWith(
            new AiModelEntry("a", "A"), new AiModelEntry(" a ", "A padded")));

        Assert.False(result.Ok);
    }

    /// <summary>
    /// A listing id carrying whitespace does not append a second record on every toggle.
    ///
    /// <para><c>EnableModel</c> looked the id up untrimmed and stored it trimmed, so the padded id never
    /// matched what the previous toggle had written and each pass added another record under the same
    /// trimmed id — the editor's guard cannot see this route at all, since nobody typed it. (#870)</para>
    /// </summary>
    [Fact]
    public void A_padded_listing_id_does_not_append_a_second_model()
    {
        var (service, settings) = Make();
        Assert.True(service.Add("mine", DraftWith()).Ok);

        service.EnableModel("mine", " gpt-4o ", "GPT-4o", enabled: true);
        service.EnableModel("mine", " gpt-4o ", "GPT-4o", enabled: false);
        service.EnableModel("mine", " gpt-4o ", "GPT-4o", enabled: true);

        var model = Assert.Single(settings.Ai.Chat.Connections.Single().Models);
        Assert.Equal("gpt-4o", model.Id);
        Assert.True(model.Enabled);
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

    /// <summary>An in-memory credential store, keyed by the joined account exactly as the real one is.</summary>
    private sealed class Keys : IAiCredentialStore
    {
        private readonly Dictionary<string, string> _byAccount = new();

        /// <summary>The same joined spelling the real store uses, so a test that stores under one name and
        /// reads under another sees a miss rather than a hit (#759).</summary>
        private static string Account(string connectionId, string name) => connectionId + ":" + name;

        /// <summary>Accounts the OS holds but will not hand over — a Keychain item whose ACL names another
        /// binary, or a DPAPI blob that will not decrypt. The store can see it and cannot read it. (#926)</summary>
        internal HashSet<string> Unreadable { get; } = new(StringComparer.Ordinal);

        public bool IsAvailable => true;
        public string? Unavailable => null;
        public string? Get(string connectionId, string name) => Read(connectionId, name).Secret;

        public CredentialRead Read(string connectionId, string name)
        {
            var account = Account(connectionId, name);
            if (Unreadable.Contains(account)) return CredentialRead.Unreadable;
            return _byAccount.TryGetValue(account, out var k)
                ? CredentialRead.Found(k)
                : CredentialRead.NotStored;
        }
        public bool Set(string connectionId, string name, string secret)
        { _byAccount[Account(connectionId, name)] = secret; return true; }
        /// <summary>Accounts the OS will not delete — authorization is needed for that too. (#926)</summary>
        internal HashSet<string> Undeletable { get; } = new(StringComparer.Ordinal);

        public bool Delete(string connectionId, string name)
        {
            var account = Account(connectionId, name);
            if (Undeletable.Contains(account)) return false;
            _byAccount.Remove(account);
            return true;
        }
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

        keys.Set("openrouter-box", AiCredentialNames.Primary, "or-key");
        keys.Set("local-ollama", AiCredentialNames.Primary, "ollama-key");

        Assert.Equal("or-key", keys.Get("openrouter-box", AiCredentialNames.Primary));
        Assert.Equal("ollama-key", keys.Get("local-ollama", AiCredentialNames.Primary));
    }

    /// <summary>A connection reports where its credential came from, so the UI can name the source and — for
    /// an environment-sourced one — offer no remove action rather than a button that would lie.</summary>
    [Fact]
    public void A_connection_reports_whether_a_key_is_stored_for_it()
    {
        var (service, _, keys) = MakeWithKeys();
        service.Add("box", Draft());

        Assert.Equal(CredentialSource.None, service.Connections.Single().KeySource);

        keys.Set("box", AiCredentialNames.Primary, "k");

        Assert.Equal(CredentialSource.Keychain, service.Connections.Single().KeySource);
    }

    /// <summary>
    /// A stored key the OS will not hand over reports its own state, not "no key". (#926)
    ///
    /// <para><b>The defect.</b> <c>SourceFor</c> asked <c>Get</c>, which answered null for a declined
    /// authorization exactly as it did for an absent item — so a signed build reading keys a development
    /// build had stored reported three configured providers as having none, and the app's advice was to type
    /// them in again.</para>
    /// </summary>
    [Fact]
    public void A_key_the_os_will_not_hand_over_is_reported_as_unreadable_not_as_absent()
    {
        var (service, _, keys) = MakeWithKeys();
        service.Add("box", Draft());
        keys.Set("box", AiCredentialNames.Primary, "k");

        Assert.Equal(CredentialSource.Keychain, service.Connections.Single().KeySource);

        keys.Unreadable.Add("box:" + AiCredentialNames.Primary);

        Assert.Equal(CredentialSource.Unreadable, service.Connections.Single().KeySource);
    }

    /// <summary>
    /// An unreadable stored key does not fall through to an environment variable. (#926)
    ///
    /// <para>"Stored wins" exists so a key the reader typed is never quietly replaced by a variable they had
    /// forgotten was set — the maintainer was surprised by one of his own. An authorization he can still
    /// grant does not make his stored key stop counting, and reporting Environment here would have the app
    /// silently billing a different credential at the moment he is least able to tell.</para>
    /// </summary>
    [Fact]
    public void An_unreadable_stored_key_does_not_fall_through_to_the_environment()
    {
        var (service, settings, keys) = MakeWithKeys();
        service.Add("box", Draft());
        keys.Set("box", AiCredentialNames.Primary, "k");
        keys.Unreadable.Add("box:" + AiCredentialNames.Primary);

        var record = settings.Ai.Chat.Connections.Single();
        record.UsesEnvironmentKey = true;
        record.EnvironmentVariable = "BOX_API_KEY";

        Assert.Equal(CredentialSource.Unreadable, service.Connections.Single().KeySource);
    }

    /// <summary>
    /// A credential the OS refuses to delete is reported, and the connection still goes. (#926)
    ///
    /// <para><b>Observed.</b> Deleting a connection said it worked, the key stayed in the keychain, and
    /// adding the provider back walked straight into the orphan — the state <see cref="Remove"/>'s own
    /// comment exists to prevent: one "nothing can ever reach or clean up", which "would be silently
    /// re-adopted if someone later created a connection with the same id". Deleting a Keychain item needs
    /// authorization, so it can be refused, and the return value was discarded.</para>
    ///
    /// <para><b>Ok stays true.</b> Removing the connection is a settings edit that cannot fail, and it is the
    /// reader's to delete — refusing would trap them with a connection they no longer want. What changes is
    /// that the leftover is named instead of hidden.</para>
    /// </summary>
    [Fact]
    public void A_credential_that_cannot_be_deleted_is_reported_and_the_connection_still_goes()
    {
        var (service, settings, keys) = MakeWithKeys();
        service.Add("box", Draft());
        keys.Set("box", AiCredentialNames.Primary, "k");
        keys.Undeletable.Add("box:" + AiCredentialNames.Primary);

        var result = service.Remove("box");

        Assert.True(result.Ok);                       // the connection is gone
        Assert.Empty(settings.Ai.Chat.Connections);
        // Named, and FORMED. Asserting only NotNull let a `+ "…{displayName}…"` continuation ship - a
        // plain string, so the placeholder reached the reader verbatim. Every one of these sentences is
        // built by concatenation, so the brace check is the one that generalises. (#926)
        Assert.NotNull(result.Problem);
        Assert.Contains("My box", result.Problem!, StringComparison.Ordinal);
        Assert.DoesNotContain("{", result.Problem!, StringComparison.Ordinal);
        Assert.Equal("k", keys.Get("box", AiCredentialNames.Primary));
    }

    /// <summary>The ordinary path stays silent, so the report above is not simply always on.</summary>
    [Fact]
    public void A_removal_that_clears_its_credentials_reports_nothing()
    {
        var (service, _, keys) = MakeWithKeys();
        service.Add("box", Draft());
        keys.Set("box", AiCredentialNames.Primary, "k");

        var result = service.Remove("box");

        Assert.True(result.Ok);
        Assert.Null(result.Problem);
        Assert.Null(keys.Get("box", AiCredentialNames.Primary));
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
        keys.Set("box", AiCredentialNames.Primary, "k");

        service.Remove("box");

        Assert.Null(keys.Get("box", AiCredentialNames.Primary));
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

    // ---- secret headers (#771) --------------------------------------------------------------------------

    /// <summary>
    /// The delete sweep has to know every name, not just the primary one. A credential left behind is
    /// invisible by definition - nothing reads it, so nothing reports it - and it would be silently re-adopted
    /// if a connection with the same id were ever created again.
    /// </summary>
    [Fact]
    public void Removing_a_connection_deletes_its_secret_header_credentials_too()
    {
        var (service, _, keys) = MakeWithKeys();
        service.Add("gw", new AiConnectionDraft(
            "Gateway", ChatProviderKind.OpenAiCompatible, "https://gateway.example/v1",
            new List<AiModelEntry>(),
            new[] { new AiHeader("cf-aig-authorization", null, Secret: true) },
            new Dictionary<string, string>()));

        keys.Set("gw", AiCredentialNames.Primary, "sk-upstream");
        keys.Set("gw", AiCredentialNames.Header("cf-aig-authorization"), "cf-token-abc");

        service.Remove("gw");

        Assert.Null(keys.Get("gw", AiCredentialNames.Primary));
        Assert.Null(keys.Get("gw", AiCredentialNames.Header("cf-aig-authorization")));
    }

    /// <summary>
    /// Header names are richer than credential names: <c>x.y</c> and <c>x-y</c> are different headers that
    /// fold to one account, so one would overwrite the other's secret and the endpoint would authenticate
    /// with the wrong one - a 401 naming nothing, which is #678's symptom. Refused at the service because
    /// settings.json is hand-edited and the sheet is not the only way in.
    /// </summary>
    [Fact]
    public void Two_secret_headers_that_fold_to_one_credential_name_are_refused()
    {
        var (service, _, _) = MakeWithKeys();

        var result = service.Add("gw", new AiConnectionDraft(
            "Gateway", ChatProviderKind.OpenAiCompatible, "https://gateway.example/v1",
            new List<AiModelEntry>(),
            new[]
            {
                new AiHeader("x.y", null, Secret: true),
                new AiHeader("x-y", null, Secret: true),
            },
            new Dictionary<string, string>()));

        Assert.False(result.Ok);
        Assert.Contains("cannot both be secret", result.Problem);
        Assert.Empty(service.Connections);
    }

    /// <summary>The same two names are fine while only one is secret - they are different headers, and only
    /// the credential namespace is narrower than the header one.</summary>
    [Fact]
    public void Header_names_that_fold_together_are_allowed_while_only_one_is_secret()
    {
        var (service, _, _) = MakeWithKeys();

        var result = service.Add("gw", new AiConnectionDraft(
            "Gateway", ChatProviderKind.OpenAiCompatible, "https://gateway.example/v1",
            new List<AiModelEntry>(),
            new[]
            {
                new AiHeader("x.y", null, Secret: true),
                new AiHeader("x-y", "cosmetic", Secret: false),
            },
            new Dictionary<string, string>()));

        Assert.True(result.Ok);
    }

    // ---- an unfilled input is refused on every path (#767) ----------------------------------------------

    private static AiConnectionDraft Templated(string? resourceName) =>
        new("Azure OpenAI", ChatProviderKind.OpenAiCompatible,
            "https://{resourceName}.openai.azure.com/openai/v1",
            new List<AiModelEntry>(),
            Array.Empty<AiHeader>(),
            resourceName is null
                ? new Dictionary<string, string>()
                : new Dictionary<string, string> { ["resourceName"] = resourceName });

    /// <summary>
    /// The reported bug: clearing the resource name on the edit sheet saved cleanly, the sheet closed as
    /// though it had worked, and what was stored was a base URL still reading
    /// https://{resourceName}.openai.azure.com/openai/v1. The reader found out when a request went nowhere.
    /// </summary>
    [Fact]
    public void An_edit_that_empties_a_templated_input_is_refused()
    {
        var (service, _, _) = MakeWithKeys();
        service.Add("my-azure", Templated("my-resource"));

        var result = service.Update("my-azure", Templated(""));

        Assert.False(result.Ok);
        Assert.Contains("resource", result.Problem, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(
            "https://my-resource.openai.azure.com/openai/v1",
            service.Connections.Single().ResolvedBaseUrl);   // the good one is still there
    }

    /// <summary>Removing the answer entirely is the same state as blanking it.</summary>
    [Fact]
    public void An_edit_that_drops_a_templated_input_is_refused()
    {
        var (service, _, _) = MakeWithKeys();
        service.Add("my-azure", Templated("my-resource"));

        Assert.False(service.Update("my-azure", Templated(null)).Ok);
    }

    /// <summary>
    /// Add was missing the check too, which the issue did not report — found by reading the three paths side
    /// by side rather than by fixing only the one that was named. A custom endpoint typed with a brace in it
    /// saved just as quietly.
    /// </summary>
    [Fact]
    public void An_add_with_an_unfilled_placeholder_is_refused()
    {
        var (service, _, _) = MakeWithKeys();

        var result = service.Add("my-azure", Templated(null));

        Assert.False(result.Ok);
        Assert.Empty(service.Connections);
    }

    [Fact]
    public void A_filled_input_saves_on_both_paths()
    {
        var (service, _, _) = MakeWithKeys();

        Assert.True(service.Add("my-azure", Templated("first")).Ok);
        Assert.True(service.Update("my-azure", Templated("second")).Ok);
        Assert.Equal(
            "https://second.openai.azure.com/openai/v1",
            service.Connections.Single().ResolvedBaseUrl);
    }

    /// <summary>A base URL with no placeholders is unaffected — most connections are this.</summary>
    [Fact]
    public void A_plain_base_url_is_untouched_by_the_check()
    {
        var (service, _, _) = MakeWithKeys();

        Assert.True(service.Add("mine", Draft()).Ok);
        Assert.True(service.Update("mine", Draft("Renamed")).Ok);
    }

    // ---- a secret prompt answer (#777) ------------------------------------------------------------------

    /// <summary>A preset source holding exactly the presets a test names.</summary>
    private sealed class Presets : IAiPresetSource
    {
        public Presets(params AiProviderPreset[] presets) => Presets_ = presets;
        private AiProviderPreset[] Presets_ { get; }
        IReadOnlyList<AiProviderPreset> IAiPresetSource.Presets => Presets_;
        public AiPresetState State => AiPresetState.Ready;
        public string? Problem => null;
        public event System.EventHandler? PresetsChanged;
        public System.Threading.Tasks.Task EnsureLoadedAsync(System.Threading.CancellationToken ct = default)
            => System.Threading.Tasks.Task.CompletedTask;
        public System.Threading.Tasks.Task RefreshAsync(System.Threading.CancellationToken ct = default)
        {
            PresetsChanged?.Invoke(this, System.EventArgs.Empty);
            return System.Threading.Tasks.Task.CompletedTask;
        }
    }

    /// <summary>A gateway that wants a token in a HEADER — the legitimate destination for a secret.</summary>
    private static AiProviderPreset GatewayPreset() => new(
        Id: "gateway",
        DisplayName: "Gateway",
        Kind: ChatProviderKind.OpenAiCompatible,
        BaseUrl: "https://gateway.example/v1",
        Methods: new List<AiCredentialMethod> { new AiCredentialMethod.Key() },
        Prompts: new List<AiInputPrompt>
        {
            new("accountId", "Account id"),
            new("gatewayToken", "Gateway token", Secret: true),
        },
        Headers: new Dictionary<string, string> { ["cf-aig-authorization"] = "Bearer {gatewayToken}" });

    private static (AiConnectionService Service, Settings Settings, Keys Keys) MakeWith(AiProviderPreset preset)
    {
        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        var keys = new Keys();
        return (new AiConnectionService(svc.Object, keys, new Presets(preset)), settings, keys);
    }

    /// <summary>
    /// The defect #777 exists for. Every prompt answer used to go to <c>Inputs</c>, which is written to
    /// settings.json in the clear — so the moment a preset asked for a secret, the easy route leaked it.
    /// </summary>
    [Fact]
    public void A_secret_prompt_answer_goes_to_the_credential_store_and_not_the_settings_file()
    {
        var (service, settings, keys) = MakeWith(GatewayPreset());

        var result = service.AddFromPreset("gateway", new Dictionary<string, string>
        {
            ["accountId"] = "acct-123",
            ["gatewayToken"] = "sk-secret-value",
        });

        Assert.True(result.Ok);
        var record = Assert.Single(settings.Ai.Chat.Connections);

        // The identifier is in the file, as it should be.
        Assert.Equal("acct-123", record.Inputs["accountId"]);

        // The secret is NOT, under any key.
        Assert.DoesNotContain("gatewayToken", record.Inputs.Keys);
        Assert.DoesNotContain("sk-secret-value", record.Inputs.Values);

        // It is in the store, and the record says which key to look under.
        Assert.Equal("sk-secret-value", keys.Get("gateway", AiCredentialNames.Input("gatewayToken")));
        Assert.Equal(new[] { "gatewayToken" }, record.SecretInputs);
    }

    /// <summary>
    /// Removing a connection must take its input secret with it. An orphan in the keychain is invisible by
    /// definition — nothing reads it, so nothing reports it — and would be silently re-adopted by a later
    /// connection created under the same id. (#759)
    /// </summary>
    [Fact]
    public void Removing_a_connection_deletes_its_input_secret_too()
    {
        var (service, _, keys) = MakeWith(GatewayPreset());
        service.AddFromPreset("gateway", new Dictionary<string, string>
        {
            ["accountId"] = "acct-123",
            ["gatewayToken"] = "sk-secret-value",
        });

        service.Remove("gateway");

        Assert.Null(keys.Get("gateway", AiCredentialNames.Input("gatewayToken")));
    }

    /// <summary>
    /// A secret must not be substituted into a base URL. A URL reaches the provider's access logs, every
    /// proxy between, the Providers list, and the error sentences this code is careful to make name the
    /// endpoint — so refusing at the seam makes it unreachable rather than discouraged.
    /// </summary>
    [Fact]
    public void A_preset_that_would_put_a_secret_in_its_address_is_refused()
    {
        var leaky = new AiProviderPreset(
            Id: "leaky",
            DisplayName: "Leaky",
            Kind: ChatProviderKind.OpenAiCompatible,
            BaseUrl: "https://leaky.example/{gatewayToken}/v1",
            Methods: new List<AiCredentialMethod> { new AiCredentialMethod.Key() },
            Prompts: new List<AiInputPrompt> { new("gatewayToken", "Gateway token", Secret: true) });

        var (service, settings, keys) = MakeWith(leaky);

        var result = service.AddFromPreset("leaky", new Dictionary<string, string>
        {
            ["gatewayToken"] = "sk-secret-value",
        });

        Assert.False(result.Ok);
        Assert.Contains("must not go in a URL", result.Problem);

        // Refused means nothing was created and nothing was filed.
        Assert.Empty(settings.Ai.Chat.Connections);
        Assert.Null(keys.Get("leaky", AiCredentialNames.Input("gatewayToken")));
    }

    /// <summary>
    /// Where there is nowhere to store a secret, say so. Writing it to the plaintext file "for now" is the
    /// leak; doing that without telling the reader is the worse half. (#771's call, unchanged.)
    /// </summary>
    [Fact]
    public void A_secret_prompt_is_refused_where_there_is_no_credential_store()
    {
        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        var service = new AiConnectionService(svc.Object, credentials: null, new Presets(GatewayPreset()));

        var result = service.AddFromPreset("gateway", new Dictionary<string, string>
        {
            ["accountId"] = "acct-123",
            ["gatewayToken"] = "sk-secret-value",
        });

        Assert.False(result.Ok);
        Assert.Contains("no credential store", result.Problem);
        Assert.Empty(settings.Ai.Chat.Connections);
    }

    /// <summary>A preset with no secret prompt is unaffected, credential store or not.</summary>
    [Fact]
    public void A_plain_prompt_answer_still_goes_to_the_settings_file()
    {
        var (service, settings, _) = MakeWith(new AiProviderPreset(
            Id: "azure-like",
            DisplayName: "Azure-like",
            Kind: ChatProviderKind.OpenAiCompatible,
            BaseUrl: "https://{resourceName}.example/v1",
            Methods: new List<AiCredentialMethod> { new AiCredentialMethod.Key() },
            Prompts: new List<AiInputPrompt> { new("resourceName", "Resource name") }));

        var result = service.AddFromPreset(
            "azure-like", new Dictionary<string, string> { ["resourceName"] = "mybox" });

        Assert.True(result.Ok);
        var record = Assert.Single(settings.Ai.Chat.Connections);
        Assert.Equal("mybox", record.Inputs["resourceName"]);
        Assert.Null(record.SecretInputs);
    }

    /// <summary>
    /// No preset this build ships puts a secret prompt in its address. (#777)
    ///
    /// <para>The service refuses one at the point of save, which is what makes it unreachable for a reader.
    /// This is the other half: it fails the BUILD, so the mistake is caught by whoever writes the preset
    /// rather than by whoever tries to add it. The presets are hand-kept in one file and the fetched
    /// catalogue constructs no prompts at all, so this is a foot-gun we would hand ourselves — the kind worth
    /// nailing shut rather than remembering.</para>
    /// </summary>
    [Fact]
    public void No_shipped_preset_would_put_a_secret_in_its_address()
    {
        foreach (var preset in AiProviderPresets.All)
        {
            var secrets = (preset.Prompts ?? Array.Empty<AiInputPrompt>())
                .Where(prompt => prompt.Secret)
                .Select(prompt => prompt.Key)
                .ToHashSet(StringComparer.Ordinal);

            if (secrets.Count == 0) continue;

            foreach (var placeholder in AiTemplate.PlaceholdersIn(preset.BaseUrl))
                Assert.False(
                    secrets.Contains(placeholder),
                    $"{preset.Id} substitutes the secret '{placeholder}' into its base URL. A URL reaches "
                    + "access logs, the Providers list and every error that names the endpoint. Put it in a "
                    + "header template instead.");
        }
    }

    /// <summary>
    /// An input secret and a header secret of the same name occupy different accounts. A provider wanting a
    /// `token` input and an `X-Token` header on one connection is not exotic, and folding both to one name
    /// would have the second silently overwrite the first — the #771 collision from a direction that check
    /// does not cover.
    /// </summary>
    [Fact]
    public void An_input_secret_and_a_header_secret_of_the_same_name_do_not_collide()
    {
        Assert.NotEqual(AiCredentialNames.Input("token"), AiCredentialNames.Header("token"));
        Assert.NotEqual(AiCredentialNames.Input("token"), AiCredentialNames.Primary);
    }

    /// <summary>
    /// The login-shell probe answering is a change to what this service reports, so it must reach the UI.
    /// (#852)
    ///
    /// <para><b>The defect.</b> <c>SourceFor</c> consults the environment live, so an adopted connection's
    /// KeySource changes the instant the probe lands — and nothing was listening. The probe starts at launch
    /// and finishes a few hundred milliseconds later; by then the model picker had already asked, been told
    /// <c>None</c>, and greyed out every model as "no API key stored". Opening Settings &gt; Providers fixed
    /// it only as a side effect of constructing a view model that rebinds, so the Assistant announced itself
    /// unconfigured on every launch and the cure was to open a tab and change nothing.</para>
    ///
    /// <para>Asserted on the event rather than on KeySource: the value was always going to be right once
    /// asked again: what was missing was anything telling the UI to ask.</para>
    /// </summary>
    [Fact]
    public void The_environment_answering_later_reaches_the_connections_changed_event()
    {
        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        var env = new ChangingKeys();

        var service = new AiConnectionService(svc.Object, environmentKeys: env);

        var raised = 0;
        service.ConnectionsChanged += (_, _) => raised++;

        env.RaiseChanged();

        Assert.Equal(1, raised);
    }

    /// <summary>An environment-keys source that can announce that its answer has changed, as the real one
    /// does when the login-shell probe lands.</summary>
    private sealed class ChangingKeys : IAiEnvironmentKeys
    {
        public event EventHandler? Changed;
        public void RaiseChanged() => Changed?.Invoke(this, EventArgs.Empty);

        public string? VariableFor(AiProviderPreset preset) => null;
        public string? ValueFor(AiProviderPreset preset) => null;
        public string? Read(string variableName) => null;
        public IReadOnlyList<AiEnvironmentKey> Discover(IEnumerable<AiProviderPreset> presets) =>
            Array.Empty<AiEnvironmentKey>();
        public Task Ready => Task.CompletedTask;
    }
}
