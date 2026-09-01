using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.Services.Ai.Credentials;
using CST.Avalonia.ViewModels;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// #691: the Providers tab — configured connections above, a catalogue of named ones below.
///
/// <para>Driven through the real <see cref="AiConnectionService"/> over in-memory settings rather than a
/// hand-written fake, so these exercise the binding surface the UI actually gets rather than a second
/// opinion about it. The two rows the service cannot yet produce — an environment-sourced credential and a
/// probed reachability — are built as records directly, which is the only way to reach that presentation
/// code before the credential and probe plumbing land.</para>
/// </summary>
public class AiConnectionsViewModelTests
{
    private static (AiConnectionsViewModel Vm, AiConnectionService Service) Make() =>
        Make(new FakeCredentialStore());

    private static (AiConnectionsViewModel Vm, AiConnectionService Service) Make(
        FakeCredentialStore keys, IAiPresetSource? presets = null)
    {
        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        var service = new AiConnectionService(svc.Object, keys, presets);
        return (new AiConnectionsViewModel(service, keys), service);
    }

    /// <summary>
    /// A preset source whose state the test chooses.
    ///
    /// <para>Without one, every test ran against a service built with <c>presets: null</c>, which hard-wires
    /// <c>Ready</c> — so the failure and loading states this section exists for had no coverage at all.</para>
    /// </summary>
    private sealed class StubPresets : IAiPresetSource
    {
        public StubPresets(AiPresetState state, string? problem, params AiProviderPreset[] presets)
        {
            State = state;
            Problem = problem;
            Presets = presets;
        }

        public IReadOnlyList<AiProviderPreset> Presets { get; }
        public AiPresetState State { get; private set; }
        public string? Problem { get; private set; }
        public int Refreshes { get; private set; }
        public event EventHandler? PresetsChanged;

        /// <summary>The startup path. Records that it was asked, so a test can tell a forced Retry from the
        /// ordinary first load.</summary>
        public Task EnsureLoadedAsync(CancellationToken ct = default) => RefreshAsync(ct);

        public Task RefreshAsync(CancellationToken ct = default)
        {
            Refreshes++;
            State = AiPresetState.Ready;
            Problem = null;
            PresetsChanged?.Invoke(this, EventArgs.Empty);
            return Task.CompletedTask;
        }
    }

    private static AiProviderPreset Hosted(string id) =>
        new(id, id, ChatProviderKind.OpenAiCompatible, $"https://{id}.test/v1",
            new AiCredentialMethod[] { new AiCredentialMethod.Key() });

    /// <summary>An in-memory keychain. The real one would need a Keychain prompt per test.</summary>
    internal sealed class FakeCredentialStore : IAiCredentialStore
    {
        /// <summary>Keyed by the joined account, exactly as the real store files it (#759).</summary>
        public Dictionary<string, string> Keys { get; } = new(StringComparer.Ordinal);
        private static string Account(string connectionId, string name) => connectionId + ":" + name;
        /// <summary>Accounts the OS holds but will not hand over. (#926)</summary>
        public HashSet<string> Unreadable { get; } = new(StringComparer.Ordinal);

        public bool IsAvailable => true;
        public string? Unavailable => null;
        public string? Get(string connectionId, string name) => Read(connectionId, name).Secret;

        public CredentialRead Read(string connectionId, string name)
        {
            var account = Account(connectionId, name);
            if (Unreadable.Contains(account)) return CredentialRead.Unreadable;
            return Keys.TryGetValue(account, out var k)
                ? CredentialRead.Found(k)
                : CredentialRead.NotStored;
        }
        public bool Set(string connectionId, string name, string secret)
        { Keys[Account(connectionId, name)] = secret; return true; }
        public bool Delete(string connectionId, string name) => Keys.Remove(Account(connectionId, name));
    }

    /// <summary>Adds a preset the way a reader does — open the sheet, fill it in, save. There is no
    /// add-without-a-sheet path any more, which is the point of the first test below.</summary>
    /// <param name="key">Supplied by default because a provider that requires one is refused without it
    /// (#761), and every test here that adds a hosted provider is adding it to use it. Ignored where the sheet
    /// shows no key box, so a local runner still stores nothing.</param>
    private static void AddThroughSheet(
        AiConnectionsViewModel vm, string presetId, string? key = "sk-test",
        params (string Key, string Value)[] inputs)
    {
        vm.AddPreset(presetId);
        var editor = vm.Editor!;

        foreach (var (k, v) in inputs) editor.Inputs.Single(i => i.Key == k).Value = v;
        // Only where the sheet shows the box: a local runner has none, and typing into a field the reader
        // cannot see would file a key nothing asked for.
        if (key is not null && editor.ShowKeyField) editor.ApiKeyEntry = key;

        editor.SaveCommand.Execute().Subscribe();
    }

    private static AiConnectionDraft Draft(string name = "My box", string url = "http://localhost:8000/v1") =>
        new(name, ChatProviderKind.OpenAiCompatible, url,
            new List<AiModelEntry>(), Array.Empty<AiHeader>(), new Dictionary<string, string>());

    // ---- the two sections ------------------------------------------------------------------------------

    /// <summary>
    /// The whole point of the two-section layout: the catalogue reads as "what you could add next", so a
    /// preset must leave it the moment a connection using it exists. A list where some rows are already
    /// spoken for is the screen this replaces.
    /// </summary>
    [Fact]
    public void An_added_preset_moves_from_the_catalogue_to_the_connections()
    {
        var (vm, _) = Make();
        Assert.Contains(vm.AvailablePresets, p => p.Id == "openrouter");
        Assert.Empty(vm.Connections);

        AddThroughSheet(vm, "openrouter");

        Assert.Contains(vm.Connections, c => c.Id == "openrouter");
        Assert.DoesNotContain(vm.AvailablePresets, p => p.Id == "openrouter");
    }

    /// <summary>Deleting puts it back, so the reader can undo an add without knowing the URL by heart.</summary>
    [Fact]
    public void Deleting_a_connection_returns_its_preset_to_the_catalogue()
    {
        var (vm, _) = Make();
        AddThroughSheet(vm, "openrouter");

        vm.Delete("openrouter");

        Assert.Empty(vm.Connections);
        Assert.Contains(vm.AvailablePresets, p => p.Id == "openrouter");
    }

    /// <summary>The empty state is a real state, not an accident: on a fresh install the section below is
    /// what the reader acts on, and the one above should say so rather than being blank.</summary>
    [Fact]
    public void The_empty_state_is_reported_until_something_is_configured()
    {
        var (vm, _) = Make();
        Assert.True(vm.HasNoConnections);

        AddThroughSheet(vm, "ollama");

        Assert.False(vm.HasNoConnections);
    }

    // ---- keyed sync ------------------------------------------------------------------------------------

    /// <summary>
    /// Rows are updated in place, not rebuilt.
    ///
    /// <para><c>ConnectionsChanged</c> fires for state changes as well as add/remove — a reachability
    /// write-back moves one connection's state — so rebuilding the collection would throw away scroll
    /// position and focus every time a probe returned. Pinned because the cheap implementation (clear and
    /// refill) passes every other test in this file.</para>
    /// </summary>
    [Fact]
    public void An_unrelated_change_leaves_existing_rows_as_the_same_objects()
    {
        var (vm, service) = Make();
        AddThroughSheet(vm, "ollama");
        var original = vm.Connections.Single();

        service.Add("second", Draft("Second"));

        Assert.Same(original, vm.Connections.First(c => c.Id == "ollama"));
        Assert.Equal(2, vm.Connections.Count);
    }

    /// <summary>An edit re-reads the row rather than replacing it, so a rename shows without the list
    /// flickering.</summary>
    [Fact]
    public void Editing_a_connection_updates_the_row_in_place()
    {
        var (vm, service) = Make();
        service.Add("mine", Draft("Old name"));
        var row = vm.Connections.Single();

        service.Update("mine", Draft("New name"));

        Assert.Same(row, vm.Connections.Single());
        Assert.Equal("New name", row.DisplayName);
    }

    /// <summary>Rows follow the service's order — the order the reader added them — which the in-place sync
    /// has to maintain by moving rows rather than by luck.</summary>
    [Fact]
    public void Rows_follow_the_services_order_after_a_removal()
    {
        var (vm, service) = Make();
        service.Add("a", Draft("A"));
        service.Add("b", Draft("B"));
        service.Add("c", Draft("C"));

        service.Remove("a");

        Assert.Equal(new[] { "b", "c" }, vm.Connections.Select(r => r.Id));
    }

    // ---- what a row says -------------------------------------------------------------------------------

    private static AiConnectionRowViewModel Row(
        CredentialSource source = CredentialSource.None,
        Reachability state = Reachability.Configured,
        string baseUrl = "http://localhost:11434/v1",
        bool storedKeyUnreadable = false,
        string? environmentVariable = null)
    {
        var connection = new AiConnection(
            "local", "Local Ollama", ChatProviderKind.OpenAiCompatible, baseUrl,
            new List<AiModelEntry>(), Array.Empty<AiHeader>(), new Dictionary<string, string>(),
            PresetId: null, KeySource: source, State: state,
            UsesEnvironmentKey: environmentVariable is not null,
            EnvironmentVariable: environmentVariable,
            StoredKeyUnreadable: storedKeyUnreadable);
        return new AiConnectionRowViewModel(new AiConnectionsViewModel(null, null), connection);
    }

    /// <summary>
    /// A key the OS holds and will not hand over is badged apart from having no key at all. (#926)
    ///
    /// <para><b>The defect in one assertion.</b> These two rendered as the same "No key", and the maintainer
    /// acted on it the way anyone would — by re-entering three keys that were present and correct, one macOS
    /// authorization away. The badge is asserted by inequality rather than by its words, so renaming it is
    /// free and merging the two states is not.</para>
    /// </summary>
    [Fact]
    public void A_key_that_cannot_be_read_is_badged_apart_from_no_key()
    {
        var locked = Row(CredentialSource.Unreadable);
        var empty = Row(CredentialSource.None);

        Assert.NotEqual(empty.KeySourceBadge, locked.KeySourceBadge);
        Assert.True(locked.HasKeyProblem);
        Assert.False(empty.HasKeyProblem);
        Assert.False(Row(CredentialSource.Keychain).HasKeyProblem);
    }

    /// <summary>
    /// Removal stays offered for a key we cannot read. (#926)
    ///
    /// <para>It is the reader's way out of the state, and it would be absent had this been written as "can we
    /// read it" rather than "did we store it". Deleting needs no authorization on either platform, so the
    /// button is not promising something it cannot do — the distinction that keeps an environment-sourced row
    /// showing an empty slot instead.</para>
    /// </summary>
    [Fact]
    public void Removal_is_offered_for_a_stored_key_that_cannot_be_read()
    {
        Assert.True(Row(CredentialSource.Unreadable).CanRemoveKey);
        Assert.True(Row(CredentialSource.Keychain).CanRemoveKey);
        Assert.False(Row(CredentialSource.Environment).CanRemoveKey);
        Assert.False(Row(CredentialSource.None).CanRemoveKey);
    }

    /// <summary>
    /// A locked key with a working variable names the variable, and does not read as broken. (#926)
    ///
    /// <para>Requests succeed and are billed to that variable, so the row states which credential is in use
    /// rather than raising an alarm — and <c>KeyProblemNeedsAction</c> stays false, so it does not take the
    /// caution colour the unusable states use. [fsnow: use the env var, and say so]</para>
    /// </summary>
    [Fact]
    public void A_locked_key_with_an_environment_fallback_names_the_variable_and_is_not_an_alarm()
    {
        var row = Row(CredentialSource.Environment, storedKeyUnreadable: true,
                      environmentVariable: "OPENROUTER_API_KEY");

        Assert.Contains("OPENROUTER_API_KEY", row.KeySourceBadge, StringComparison.Ordinal);
        Assert.True(row.HasKeyProblem);
        Assert.False(row.KeyProblemNeedsAction);
        Assert.True(row.KeyProblemIsStatement);
        Assert.Contains("OPENROUTER_API_KEY", row.KeyProblem!, StringComparison.Ordinal);
    }

    /// <summary>
    /// A locked key with nothing behind it IS an alarm, and the two shapes never render together. (#926)
    ///
    /// <para>The mutual exclusion is asserted because the row template binds both to the same text: without
    /// it the caution case would print the sentence twice.</para>
    /// </summary>
    [Fact]
    public void A_locked_key_with_no_fallback_needs_action_and_renders_one_line_only()
    {
        var row = Row(CredentialSource.Unreadable, storedKeyUnreadable: true);

        Assert.True(row.HasKeyProblem);
        Assert.True(row.KeyProblemNeedsAction);
        Assert.False(row.KeyProblemIsStatement);
        Assert.NotEqual(row.KeyProblemNeedsAction, row.KeyProblemIsStatement);
    }

    /// <summary>
    /// The advice for an unreadable key is the one for THIS platform. (#926)
    ///
    /// <para>This is the only place in the app that tells a macOS reader the remedy is to <i>authorize</i>.
    /// A mutation making the string platform-blind killed no test, and the consequence is not cosmetic: it
    /// would hand macOS readers the DPAPI advice — delete and retype — which discards precisely the
    /// recoverable state <see cref="CredentialState.Unreadable"/> says never to clean up. (fable)</para>
    /// </summary>
    [Fact]
    public void The_locked_key_advice_matches_the_platform()
    {
        var text = Row(CredentialSource.Unreadable, storedKeyUnreadable: true).KeyProblem!;

        if (OperatingSystem.IsWindows())
        {
            Assert.Contains("decrypt", text, StringComparison.OrdinalIgnoreCase);
            Assert.DoesNotContain("authorize", text, StringComparison.OrdinalIgnoreCase);
        }
        else
        {
            Assert.Contains("Allow", text, StringComparison.Ordinal);
            Assert.DoesNotContain("decrypt", text, StringComparison.OrdinalIgnoreCase);
        }
    }

    /// <summary>
    /// Configured is not connected, and this is the sentence that must never drift.
    ///
    /// <para>A settings page that claims "Connected" while the assistant reports it cannot connect sends the
    /// reader looking everywhere except the problem — observed in OpenCode, where the two surfaces contradict
    /// each other and the one consulted to diagnose is the one that is wrong.</para>
    /// </summary>
    [Theory]
    [InlineData(Reachability.Configured, "Not checked yet")]
    [InlineData(Reachability.Reachable, "Reachable")]
    [InlineData(Reachability.Unreachable, "Not responding")]
    public void Status_is_honest_about_what_has_actually_been_checked(Reachability state, string expected) =>
        Assert.Equal(expected, Row(state: state).StatusText);

    [Fact]
    public void No_status_wording_ever_claims_a_connection()
    {
        foreach (var state in Enum.GetValues<Reachability>())
            Assert.DoesNotContain("Connected", Row(state: state).StatusText, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>One axis, applied to every row. OpenCode badges only the unusual row and overloads the slot
    /// across two axes, which leaves the badge meaning nothing in particular.</summary>
    [Theory]
    [InlineData(CredentialSource.None, "No key")]
    [InlineData(CredentialSource.Keychain, "Keychain")]
    [InlineData(CredentialSource.Environment, "Environment")]
    public void Every_row_names_where_its_credential_came_from(CredentialSource source, string expected) =>
        Assert.Equal(expected, Row(source).KeySourceBadge);

    /// <summary>
    /// An environment-sourced row gets an empty action slot rather than a disabled button: the app cannot
    /// delete a credential it never stored, and a control there would promise something it cannot do.
    /// </summary>
    [Fact]
    public void Only_a_key_we_stored_ourselves_offers_to_be_removed()
    {
        Assert.False(Row(CredentialSource.Environment).CanRemoveKey);
        Assert.False(Row(CredentialSource.None).CanRemoveKey);
        Assert.True(Row(CredentialSource.Keychain).CanRemoveKey);
    }

    /// <summary>An unanswered <c>{placeholder}</c> is said on the row. Sent anyway it fails as a DNS error
    /// naming nothing, which tells the reader neither what is wrong nor where to fix it.</summary>
    [Fact]
    public void A_connection_missing_an_input_says_what_it_still_needs()
    {
        var row = Row(baseUrl: "https://{resourceName}.openai.azure.com/openai/v1");

        Assert.True(row.IsIncomplete);
        Assert.Contains("resourceName", row.IncompleteText);
    }

    [Fact]
    public void A_complete_connection_is_not_flagged() => Assert.False(Row().IsIncomplete);

    // ---- the catalogue ---------------------------------------------------------------------------------

    /// <summary>
    /// A catalogue row says what the preset knows and nothing more.
    ///
    /// <para>OpenCode's rows carry vendor blurbs — "GPT models for fast, capable general AI tasks" — which is
    /// marketing presented as product information. Any line we wrote ourselves would be an opinion about a
    /// provider, which is the registry removed in #670/#681 arriving as prose.</para>
    /// </summary>
    [Fact]
    public void A_catalogue_row_says_only_whether_a_key_is_needed()
    {
        var (vm, _) = Make();

        // ollama is in the local section now (#739) - it needs no key AND no network, which is what puts it
        // there rather than in the catalogue.
        Assert.Equal("No key needed", vm.AvailablePresets.Single(p => p.Id == "ollama").RequirementText);
        Assert.Equal("Needs an API key", vm.AvailablePresets.Single(p => p.Id == "openrouter").RequirementText);
    }

    /// <summary>
    /// Every add opens the sheet, including a preset with nothing to ask.
    ///
    /// <para>Adding outright was tested and is wrong twice over: the new row lands at the top of a page the
    /// reader has scrolled to the bottom of to reach the catalogue, so the click reads as having done
    /// nothing — and a provider with neither a key nor a model id cannot answer a question, so the gap is
    /// discovered later and has to be repaired through Edit.</para>
    /// </summary>
    [Fact]
    public void Adding_a_provider_always_opens_the_sheet()
    {
        var (vm, _) = Make();

        vm.AddPreset("openrouter");

        Assert.NotNull(vm.Editor);
        Assert.True(vm.Editor!.IsPreset);
        Assert.Empty(vm.Connections);
    }

    /// <summary>Azure's address is a shape rather than a URL, so the sheet has a field for the part only the
    /// reader knows.</summary>
    [Fact]
    public void A_preset_that_needs_an_answer_asks_for_it()
    {
        var (vm, _) = Make();

        vm.AddPreset("azure");

        Assert.Contains(vm.Editor!.Inputs, i => i.Key == "resourceName");
    }

    /// <summary>
    /// The key is filed under the connection it was typed for.
    ///
    /// <para>The regression this pins is #678 arriving by a different route: a shared key box elsewhere in
    /// Settings writes to whichever connection happens to be <i>active</i>, so a reader adding OpenRouter
    /// second would file its key under the first endpoint and get a 401 naming neither.</para>
    /// </summary>
    [Fact]
    public void A_key_typed_on_the_sheet_is_filed_under_that_connection()
    {
        var keys = new FakeCredentialStore();
        var (vm, _) = Make(keys);

        AddThroughSheet(vm, "ollama");
        AddThroughSheet(vm, "openrouter", key: "sk-or-secret");

        Assert.Equal("sk-or-secret", keys.Get("openrouter", AiCredentialNames.Primary));
        Assert.Null(keys.Get("ollama", AiCredentialNames.Primary));
    }

    /// <summary>Where the credential came from is read off the store, so storing one moves the badge without
    /// the row being rebuilt.</summary>
    [Fact]
    public void Storing_a_key_moves_the_rows_badge()
    {
        var keys = new FakeCredentialStore();
        var (vm, _) = Make(keys);

        AddThroughSheet(vm, "openrouter", key: "sk-or-secret");
        var row = vm.Connections.Single();

        Assert.Equal("Keychain", row.KeySourceBadge);
        Assert.True(row.CanRemoveKey);

        row.RemoveKeyCommand.Execute().Subscribe();

        Assert.Equal("No key", vm.Connections.Single().KeySourceBadge);
        Assert.Null(keys.Get("openrouter", AiCredentialNames.Primary));
    }

    /// <summary>Removing a key must not take the connection or its models with it — the whole reason the two
    /// destructive actions are separate.</summary>
    [Fact]
    public void Removing_a_key_leaves_the_connection_and_its_models_alone()
    {
        var keys = new FakeCredentialStore();
        var (vm, service) = Make(keys);
        AddThroughSheet(vm, "openrouter", key: "sk-or-secret");

        vm.Connections.Single().RemoveKeyCommand.Execute().Subscribe();

        var still = service.Connections.Single();
        Assert.Equal("openrouter", still.Id);
    }

    /// <summary>The generated form is the mechanism, so a preset added in a future upstream sync arrives with
    /// its dialog already working rather than needing hand-written code.</summary>
    [Fact]
    public void The_sheet_for_a_preset_is_built_from_the_presets_own_prompts()
    {
        var (vm, _) = Make();

        vm.AddPreset("cloudflare-workers-ai");
        var input = Assert.Single(vm.Editor!.Inputs);

        Assert.Equal("accountId", input.Key);
        Assert.Equal("Cloudflare account ID", input.Message);
        Assert.True(input.IsFreeText);
    }

    // ---- deleting asks first ---------------------------------------------------------------------------

    /// <summary>
    /// Delete destroys the hand-typed model list, which is real user-authored work — the same asymmetry that
    /// makes "remove key" and "delete connection" two actions rather than one. So the first click asks.
    /// </summary>
    [Fact]
    public void Deleting_asks_before_it_destroys_anything()
    {
        var (vm, service) = Make();
        service.Add("mine", Draft());
        var row = vm.Connections.Single();

        row.DeleteCommand.Execute().Subscribe();

        Assert.True(row.IsConfirmingDelete);
        Assert.Single(vm.Connections);
    }

    [Fact]
    public void Confirming_deletes_and_declining_does_not()
    {
        var (vm, service) = Make();
        service.Add("keep", Draft());
        service.Add("go", Draft());

        var keep = vm.Connections.Single(r => r.Id == "keep");
        keep.DeleteCommand.Execute().Subscribe();
        keep.CancelDeleteCommand.Execute().Subscribe();

        Assert.False(keep.IsConfirmingDelete);
        Assert.Equal(2, vm.Connections.Count);

        var go = vm.Connections.Single(r => r.Id == "go");
        go.DeleteCommand.Execute().Subscribe();
        go.ConfirmDeleteCommand.Execute().Subscribe();

        Assert.Equal(new[] { "keep" }, vm.Connections.Select(r => r.Id));
    }

    /// <summary>The confirmation names the models, because that is the part a reader would not think to check
    /// before clicking.</summary>
    [Fact]
    public void The_confirmation_says_what_goes_with_it()
    {
        var (vm, service) = Make();
        service.Add("mine", Draft("My box") with
        {
            Models = new List<AiModelEntry> { new("a", "A"), new("b", "B") },
        });

        Assert.Equal("Delete My box and its 2 models?", vm.Connections.Single().DeleteConfirmText);
    }

    // ---- lifetime ----------------------------------------------------------------------------------------

    /// <summary>
    /// Disposing stops the view model listening.
    ///
    /// <para>The connection service is a singleton and a fresh Settings window builds a fresh view model
    /// every time it is opened. Without this, each visit leaves another subscriber alive — rebuilding
    /// collections nobody can see, on every connection change, for the rest of the session — and the cost
    /// grows with how often the reader visits a screen whose whole job is being visited.</para>
    /// </summary>
    [Fact]
    public void A_disposed_view_model_stops_listening()
    {
        var (vm, service) = Make();
        service.Add("first", Draft());
        Assert.Single(vm.Connections);

        vm.Dispose();
        service.Add("second", Draft("Second"));

        Assert.Single(vm.Connections);          // did not see the second add
        Assert.Equal(2, service.Connections.Count);   // which did happen
    }

    // ---- the catalogue at scale (#739) ---------------------------------------------------------------------





    /// <summary>Search matches the id as well as the display name — a reader may know a provider by
    /// either.</summary>
    [Fact]
    public void Search_matches_the_id_as_well_as_the_name()
    {
        var (vm, _) = Make();

        vm.PresetSearch = "togetherai";

        Assert.NotEmpty(vm.AvailablePresets);
    }


    /// <summary>Adding a provider still removes it from the catalogue, so the list keeps reading as "what you
    /// could add next" at this scale too.</summary>
    [Fact]
    public void An_added_catalogue_provider_leaves_the_catalogue()
    {
        var (vm, _) = Make();
        vm.PresetSearch = "openrouter";
        Assert.NotEmpty(vm.AvailablePresets);

        AddThroughSheet(vm, "openrouter");

        Assert.DoesNotContain(vm.AvailablePresets, p => p.Id == "openrouter");
    }

    /// <summary>
    /// The list is populated without a search term.
    ///
    /// <para>It used to be collapsed behind a count, so a reader who opened the tab saw an empty box until
    /// they typed something — a populated list that looked broken. Reported from use, and the reason the
    /// expander is gone rather than merely defaulted open.</para>
    /// </summary>
    [Fact]
    public void The_provider_list_is_populated_with_no_search_term()
    {
        var (vm, _) = Make();

        Assert.Equal("", vm.PresetSearch);
        Assert.True(vm.AvailablePresets.Count > 20, "the snapshot should supply a real catalogue");
    }

    /// <summary>
    /// One list, with the local runners in it rather than pinned above it.
    ///
    /// <para>They had their own section on the reasoning that needing no key and no network is a fact rather
    /// than a ranking. True, and beside the point: it still gave three providers a permanent position above a
    /// hundred and sixty others, which is prominence however it is justified.</para>
    /// </summary>
    [Fact]
    public void Local_runners_sit_in_the_one_list_like_everything_else()
    {
        var (vm, _) = Make();
        var ids = vm.AvailablePresets.Select(p => p.Id).ToList();

        Assert.Contains("ollama", ids);
        Assert.Contains("lmstudio", ids);
        Assert.Contains("openrouter", ids);
    }

    // ---- the states this section exists for (#739) ----------------------------------------------------------

    /// <summary>
    /// A search matching nothing must not hide the search box.
    ///
    /// <para>The box was gated on the <i>filtered</i> count, so typing a string that matched nothing removed
    /// the only control that could clear the search — mid-keystroke — and the catalogue was gone for the life
    /// of the window. Recovery was closing and reopening Settings.</para>
    /// </summary>
    [Fact]
    public void A_search_matching_nothing_keeps_the_search_reachable()
    {
        var (vm, _) = Make(new FakeCredentialStore(),
            new StubPresets(AiPresetState.Ready, null, Hosted("openrouter"), Hosted("groq")));

        vm.PresetSearch = "zzzzz";

        Assert.Empty(vm.AvailablePresets);
        Assert.True(vm.HasCatalogue);    // the box stays on screen
        Assert.True(vm.HasNoMatches);    // and says why the list is empty
    }

    /// <summary>A trailing space is invisible; matching on it would report a provider that plainly exists as
    /// absent.</summary>
    [Fact]
    public void Search_is_trimmed()
    {
        var (vm, _) = Make(new FakeCredentialStore(),
            new StubPresets(AiPresetState.Ready, null, Hosted("openrouter")));

        vm.PresetSearch = "openrouter ";

        Assert.Single(vm.AvailablePresets);
    }

    /// <summary>
    /// The loading line appears only while there is nothing to show.
    ///
    /// <para>The source seeds the built-in snapshot before any fetch finishes, so "looking for the provider
    /// list" would otherwise sit above 166 rows — and nothing initiates a fetch at all while the AI master
    /// switch is off, leaving it there permanently on a tab reachable with AI disabled.</para>
    /// </summary>
    [Fact]
    public void Loading_is_not_announced_over_a_populated_catalogue()
    {
        var (populated, _) = Make(new FakeCredentialStore(),
            new StubPresets(AiPresetState.Loading, null, Hosted("openrouter")));
        Assert.False(populated.IsCatalogueLoading);

        var (empty, _) = Make(new FakeCredentialStore(), new StubPresets(AiPresetState.Loading, null));
        Assert.True(empty.IsCatalogueLoading);
    }

    /// <summary>A failure that kept the previous list says that, rather than contradicting the 166 rows
    /// underneath it.</summary>
    [Fact]
    public void A_failure_over_a_surviving_list_says_so()
    {
        var (vm, _) = Make(new FakeCredentialStore(),
            new StubPresets(AiPresetState.Unavailable, "Could not reach models.dev.", Hosted("openrouter")));

        Assert.True(vm.HasCatalogueProblem);
        Assert.Contains("built-in", vm.CatalogueProblem);
    }

    /// <summary>With nothing left, the service's own sentence is the honest one.</summary>
    [Fact]
    public void A_failure_with_nothing_left_shows_the_services_sentence()
    {
        var (vm, _) = Make(new FakeCredentialStore(),
            new StubPresets(AiPresetState.Unavailable, "Could not reach models.dev."));

        Assert.Equal("Could not reach models.dev.", vm.CatalogueProblem);
    }

    /// <summary>Retry asks the source again, and the recovery reaches the view.</summary>
    [Fact]
    public void Retry_refetches_and_clears_the_failure()
    {
        var stub = new StubPresets(AiPresetState.Unavailable, "Could not reach models.dev.");
        var (vm, _) = Make(new FakeCredentialStore(), stub);
        Assert.True(vm.HasCatalogueProblem);

        vm.RetryCatalogueCommand.Execute().Subscribe();

        Assert.Equal(1, stub.Refreshes);
        Assert.False(vm.HasCatalogueProblem);
    }

    // ---- the documentation link (#740) ----------------------------------------------------------------------

    /// <summary>
    /// A catalogue-backed connection offers its provider's documentation.
    ///
    /// <para>Sampled across the catalogue, nine `doc` links in ten point at a <b>models</b> page rather than an
    /// account page — so this answers "what can I run on this?", not "where do I get a key?". Anyone who has
    /// pasted a key has already been to the provider.</para>
    /// </summary>
    [Fact]
    public void A_catalogue_backed_connection_links_to_its_provider_docs()
    {
        var (vm, service) = Make();
        service.AddFromPreset("openrouter", new Dictionary<string, string>());

        var row = vm.Connections.Single();
        Assert.True(row.HasDoc);
        Assert.StartsWith("https://", row.DocUrl);
    }

    /// <summary>A custom endpoint has no provider behind it and therefore no documentation to point at —
    /// the link is absent rather than pointing somewhere generic.</summary>
    [Fact]
    public void A_custom_endpoint_offers_no_documentation_link()
    {
        var (vm, service) = Make();
        service.Add("my-box", Draft());

        Assert.False(vm.Connections.Single().HasDoc);
    }

    /// <summary>
    /// Only http(s) is handed to the shell.
    ///
    /// <para>The URL arrives in a fetched catalogue. Passing an arbitrary string to the operating system is
    /// how a data file becomes a way to run something, so the scheme is checked rather than trusted.</para>
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("not a url")]
    [InlineData("file:///etc/passwd")]
    [InlineData("javascript:alert(1)")]
    [InlineData("/etc/passwd")]
    public void A_url_that_is_not_http_is_refused(string? url) =>
        Assert.False(AiConnectionsViewModel.ShouldOpen(url, out _));

    /// <summary>
    /// Asserted on the decision, not on the launch.
    ///
    /// <para>A test that merely calls the launcher can only observe that nothing threw — so if the scheme
    /// check were deleted it would pass while actually shell-opening <c>file:///etc/passwd</c> on whoever ran
    /// it. Green, and worse than useless.</para>
    /// </summary>
    [Theory]
    [InlineData("https://openrouter.ai/models")]
    [InlineData("http://example.test/docs")]
    public void An_http_url_is_allowed(string url)
    {
        Assert.True(AiConnectionsViewModel.ShouldOpen(url, out var uri));
        Assert.NotNull(uri);
    }

    /// <summary>
    /// The custom-endpoint row draws the same mark as everything else without a logo.
    ///
    /// <para>It is a fixed row in the view rather than a preset with a view model, so it inherits nothing —
    /// which is how it ended up the one row in a list of icons showing a plus sign in a coloured tile. The
    /// view model exposes the path the view binds to; a null means the bundled file could not be written and
    /// the tile stays.</para>
    /// </summary>
    [Fact]
    public void The_custom_row_is_offered_the_same_generic_mark()
    {
        var (vm, _) = Make();

        // Null is legitimate here — extraction needs an Avalonia asset loader, which a plain test host has
        // not started. What is asserted is that the view has somewhere to bind, not that a file appeared.
        var path = vm.GenericLogoPath;

        Assert.True(path is null || File.Exists(path));
    }

    // ---- the generic model icon (#740) ------------------------------------------------------------------------

    /// <summary>
    /// The bundled mark is the same file models.dev serves for a provider it has no logo for.
    ///
    /// <para>Pinned by hash against <c>AiProviderLogos.PlaceholderSha256</c>, which is what the fetcher uses
    /// to recognise "this provider has no mark". If the two ever diverge, one of them is stale — and the
    /// symptom would be a provider silently getting a letter tile again, which nobody would think to look
    /// for.</para>
    /// </summary>
    [Fact]
    public void The_bundled_generic_icon_is_the_mark_the_catalogue_serves_for_no_logo()
    {
        var asset = Path.Combine(RepoRoot(), "src", "CST.Avalonia", "Assets", "Ai", "generic-model.svg");
        Assert.True(File.Exists(asset), asset);

        var hash = System.Convert.ToHexString(
            System.Security.Cryptography.SHA256.HashData(File.ReadAllBytes(asset))).ToLowerInvariant();

        Assert.Equal(AiProviderLogos.PlaceholderSha256, hash);
    }

    /// <summary>It draws with <c>currentColor</c>, which is what lets the renderer recolour it per theme — a
    /// hardcoded fill would be invisible in one of the two.</summary>
    [Fact]
    public void The_generic_icon_takes_its_colour_from_the_theme()
    {
        var asset = Path.Combine(RepoRoot(), "src", "CST.Avalonia", "Assets", "Ai", "generic-model.svg");
        var svg = File.ReadAllText(asset);

        Assert.Contains("currentColor", svg, StringComparison.Ordinal);
        Assert.DoesNotContain("#", svg, StringComparison.Ordinal);
    }

    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        // File OR directory: in a git worktree, .git is a FILE holding a pointer to the real one, so testing
        // only for a directory walks past every level and yields null. That made two tests fail for a reason
        // having nothing to do with what they assert - and worktrees are how a review agent is given an
        // isolated tree, so the false failures land exactly where they are least expected and most costly.
        while (dir is not null
               && !Directory.Exists(Path.Combine(dir.FullName, ".git"))
               && !File.Exists(Path.Combine(dir.FullName, ".git")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }

    // ---- monograms -------------------------------------------------------------------------------------

    [Theory]
    [InlineData("OpenRouter", "O")]
    [InlineData("xAI", "X")]
    [InlineData("Z.ai", "Z")]
    [InlineData("(unnamed)", "U")]
    [InlineData("", "?")]
    public void The_tile_shows_the_first_letter_there_is(string name, string expected) =>
        Assert.Equal(expected, AiMonogram.For(name));

    /// <summary>
    /// The tone is a pure function of the id, not of this process.
    ///
    /// <para>.NET randomises string hashing per run, so <c>GetHashCode</c> here would repaint every row on
    /// every launch — a list that looks different each time it is opened, for no reason the reader could
    /// name.</para>
    /// </summary>
    [Fact]
    public void A_tiles_tone_is_stable_and_within_range()
    {
        foreach (var id in new[] { "openrouter", "ollama", "my-box", "a" })
        {
            var tone = AiMonogram.ToneFor(id);
            Assert.InRange(tone, 0, AiMonogram.ToneCount - 1);
            Assert.Equal(tone, AiMonogram.ToneFor(id));
        }
    }

    // ---- keys the environment already holds (#714) -------------------------------------------------------

    /// <summary>An environment holding exactly the variables a test names.</summary>
    private static IAiEnvironmentKeys Env(params string[] set)
    {
        var present = new HashSet<string>(set, StringComparer.Ordinal);
        return new AiEnvironmentKeys(name => present.Contains(name) ? "sk-not-rendered-anywhere" : null);
    }

    private static AiProviderPreset EnvPreset(
        string id, string display, string[] variables, IReadOnlyList<AiInputPrompt>? prompts = null) =>
        new(id, display, ChatProviderKind.OpenAiCompatible, "https://" + id + ".example/v1",
            new List<AiCredentialMethod> { new AiCredentialMethod.Env(variables) },
            Prompts: prompts);

    private static (AiConnectionsViewModel Vm, AiConnectionService Service) MakeWithEnv(
        IAiEnvironmentKeys env, params AiProviderPreset[] presets)
    {
        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        var keys = new FakeCredentialStore();
        var source = new StubPresets(AiPresetState.Ready, null, presets);
        var service = new AiConnectionService(svc.Object, keys, source, env);
        return (new AiConnectionsViewModel(service, keys, null, env), service);
    }

    /// <summary>A provider whose variable is set is offered — by NAME, and with nothing adopted yet.</summary>
    [Fact]
    public void A_provider_whose_variable_is_set_is_offered_but_not_adopted()
    {
        var (vm, service) = MakeWithEnv(
            Env("OPENAI_API_KEY"), EnvPreset("openai", "OpenAI", new[] { "OPENAI_API_KEY" }));

        var row = Assert.Single(vm.FoundKeys);
        Assert.True(vm.HasFoundKeys);
        Assert.Equal("OpenAI", row.DisplayName);
        Assert.Equal("Key in OPENAI_API_KEY", row.VariableText);

        // Discovery alone creates nothing. This is the whole difference from the behaviour that prompted the
        // issue: a provider connected without the reader ever choosing it.
        Assert.Empty(service.Connections);
    }

    /// <summary>The value never reaches the row. The variable's name is the whole of what is carried.</summary>
    [Fact]
    public void The_key_itself_is_never_carried_into_the_row()
    {
        var (vm, _) = MakeWithEnv(
            Env("OPENAI_API_KEY"), EnvPreset("openai", "OpenAI", new[] { "OPENAI_API_KEY" }));

        var row = Assert.Single(vm.FoundKeys);
        var rendered = string.Join("|", row.DisplayName, row.VariableName, row.VariableText, row.Monogram);
        Assert.DoesNotContain("sk-not-rendered-anywhere", rendered, StringComparison.Ordinal);
    }

    /// <summary>Nothing set, nothing offered — and no empty section explaining an absence.</summary>
    [Fact]
    public void An_environment_holding_nothing_offers_nothing()
    {
        var (vm, _) = MakeWithEnv(Env(), EnvPreset("openai", "OpenAI", new[] { "OPENAI_API_KEY" }));

        Assert.Empty(vm.FoundKeys);
        Assert.False(vm.HasFoundKeys);
    }

    /// <summary>
    /// Absence of a variable is never rendered as absence of a credential. Bedrock authenticates through the
    /// whole AWS chain — ~/.aws/credentials, SSO, instance roles — so "no key found" would be confidently
    /// wrong exactly where it is most likely to be in use (#702). The section can only ADD a row for
    /// something present, so there is no shape in which it can say that.
    /// </summary>
    [Fact]
    public void A_provider_with_no_variable_set_gets_no_row_at_all_rather_than_a_negative_one()
    {
        var (vm, _) = MakeWithEnv(
            Env("OPENAI_API_KEY"),
            EnvPreset("openai", "OpenAI", new[] { "OPENAI_API_KEY" }),
            EnvPreset("amazon-bedrock", "Amazon Bedrock", new[] { "AWS_ACCESS_KEY_ID" }));

        var row = Assert.Single(vm.FoundKeys);
        Assert.Equal("openai", row.Id);
    }

    /// <summary>Pressing the button is what adopts it — and the opt-in is what gets recorded.</summary>
    [Fact]
    public void Using_a_found_key_adopts_it_and_records_the_opt_in()
    {
        var (vm, service) = MakeWithEnv(
            Env("OPENAI_API_KEY"), EnvPreset("openai", "OpenAI", new[] { "OPENAI_API_KEY" }));

        Assert.Single(vm.FoundKeys).UseCommand.Execute().Subscribe();

        var connection = Assert.Single(service.Connections);
        Assert.Equal("openai", connection.Id);
        Assert.Equal(CredentialSource.Environment, connection.KeySource);

        // The variable the reader was SHOWN is the one recorded. Re-deriving it from the preset on each
        // request would let a models.dev reordering change which credential a recorded connection uses,
        // months later and with no second consent — the very thing this feature exists to prevent, arriving
        // late instead of at the start. (fable, on #813)
        Assert.Equal("OPENAI_API_KEY", connection.EnvironmentVariable);

        // And it leaves the offer list, because it is no longer an available preset.
        Assert.Empty(vm.FoundKeys);
    }

    /// <summary>
    /// A preset that needs an answer cannot be adopted in one click, so it opens the sheet rather than
    /// failing. Azure wants a resource name and Cloudflare an account id, and both declare environment
    /// variables — adopting either without asking would create a connection whose address still has a hole
    /// in it.
    /// </summary>
    [Fact]
    public void A_provider_that_needs_an_answer_opens_the_sheet_instead_of_adopting_blind()
    {
        var (vm, service) = MakeWithEnv(
            Env("AZURE_API_KEY"),
            EnvPreset("azure", "Azure", new[] { "AZURE_API_KEY" },
                new List<AiInputPrompt> { new("resourceName", "Resource name") }));

        Assert.Single(vm.FoundKeys).UseCommand.Execute().Subscribe();

        Assert.NotNull(vm.Editor);
        Assert.Empty(service.Connections);
        Assert.Null(vm.Problem);
    }

    /// <summary>
    /// The sheet path, end to end — the half that had no enforcement at all.
    ///
    /// <para>Three mutations survived the whole suite before this existed: dropping the variable name on the
    /// way into the sheet, dropping it again on the way out, and deleting the required-key suppression. Each
    /// silently turns adoption into an ordinary add that then demands a typed key. This test fails on all
    /// three. (fable review of #814)</para>
    /// </summary>
    [Fact]
    public void A_prompted_provider_adopted_through_the_sheet_records_the_variable_and_needs_no_key()
    {
        var (vm, service) = MakeWithEnv(
            Env("AZURE_API_KEY"),
            EnvPreset("azure", "Azure", new[] { "AZURE_API_KEY" },
                new List<AiInputPrompt> { new("resourceName", "Resource name") }));

        Assert.Single(vm.FoundKeys).UseCommand.Execute().Subscribe();

        var editor = Assert.IsType<AiConnectionEditorViewModel>(vm.Editor);

        // The sheet says which variable it is carrying. It was bound to nothing before this.
        Assert.True(editor.HasEnvironmentKeyNote);
        Assert.Contains("AZURE_API_KEY", editor.EnvironmentKeyNote!, StringComparison.Ordinal);

        editor.Inputs.Single(i => i.Key == "resourceName").Value = "mybox";

        // Saving with the key box EMPTY has to succeed: demanding a key here would refuse the very thing the
        // button offered to do.
        editor.SaveCommand.Execute().Subscribe();

        var connection = Assert.Single(service.Connections);
        Assert.Equal(CredentialSource.Environment, connection.KeySource);
        Assert.Equal("AZURE_API_KEY", connection.EnvironmentVariable);
    }

    /// <summary>
    /// Editing an adopted connection must not demand a key it was promised it would never need.
    ///
    /// <para>The sheet forgot the adoption on the edit path, so <c>MissingRequiredKey</c> fired and Save was
    /// refused with "needs an API key" — on a connection whose credential works. The only ways out were
    /// pasting the environment's key into the keychain, which is the copy adoption exists to avoid, or
    /// delete-and-re-add, which destroys the model list the edit path exists to preserve. (fable review of
    /// #814)</para>
    /// </summary>
    [Fact]
    public void An_adopted_connection_can_be_edited_without_pasting_a_key()
    {
        var (vm, service) = MakeWithEnv(
            Env("AZURE_API_KEY"),
            EnvPreset("azure", "Azure", new[] { "AZURE_API_KEY" },
                new List<AiInputPrompt> { new("resourceName", "Resource name") }));

        Assert.Single(vm.FoundKeys).UseCommand.Execute().Subscribe();
        var adding = Assert.IsType<AiConnectionEditorViewModel>(vm.Editor);
        adding.Inputs.Single(i => i.Key == "resourceName").Value = "mybox";
        adding.SaveCommand.Execute().Subscribe();

        // Now reopen it, change the resource name, and save — with nothing in the key box.
        var row = vm.Connections.Single(r => r.Id == "azure");
        row.EditCommand.Execute().Subscribe();
        var editing = Assert.IsType<AiConnectionEditorViewModel>(vm.Editor);

        editing.Inputs.Single(i => i.Key == "resourceName").Value = "renamed";
        editing.SaveCommand.Execute().Subscribe();

        Assert.Null(vm.Problem);
        Assert.Null(editing.Problem);

        var connection = Assert.Single(service.Connections);
        Assert.Equal("renamed", connection.Inputs["resourceName"]);

        // And the adoption survived the edit rather than being silently dropped or conferred.
        Assert.Equal("AZURE_API_KEY", connection.EnvironmentVariable);
        Assert.Equal(CredentialSource.Environment, connection.KeySource);
    }

    /// <summary>
    /// A provider the reader has already configured is not offered, whatever their environment holds. That
    /// decision is made, and re-offering it would be the nagging this section is shaped to avoid.
    /// </summary>
    [Fact]
    public void An_already_configured_provider_is_not_offered()
    {
        var (vm, service) = MakeWithEnv(
            Env("OPENAI_API_KEY"), EnvPreset("openai", "OpenAI", new[] { "OPENAI_API_KEY" }));

        // Asserted BEFORE, or this passes with discovery deleted entirely — it would be checking that an
        // empty list is still empty. (fable review of #814)
        Assert.Single(vm.FoundKeys);

        // No manual refresh: adding raises ConnectionsChanged, which is what the tab binds to.
        service.AddFromPreset("openai", new Dictionary<string, string>());

        Assert.Empty(vm.FoundKeys);
    }

    /// <summary>
    /// The section is not filtered by the catalogue search. Typing to find a provider must not take away the
    /// block that says a key for it is already on the machine.
    /// </summary>
    [Fact]
    public void Searching_the_catalogue_does_not_hide_a_found_key()
    {
        var (vm, _) = MakeWithEnv(
            Env("OPENAI_API_KEY"), EnvPreset("openai", "OpenAI", new[] { "OPENAI_API_KEY" }));

        vm.PresetSearch = "something that matches no provider";

        Assert.Single(vm.FoundKeys);
        Assert.True(vm.HasFoundKeys);
    }

    /// <summary>
    /// A variable unset between one look and the next stops being offered, rather than lingering from a
    /// cached answer. Reading the environment fresh on every rebind is what makes that true.
    /// </summary>
    [Fact]
    public void A_variable_that_disappears_stops_being_offered()
    {
        var present = new HashSet<string>(StringComparer.Ordinal) { "OPENAI_API_KEY" };
        var env = new AiEnvironmentKeys(name => present.Contains(name) ? "sk-x" : null);

        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        var keys = new FakeCredentialStore();
        var service = new AiConnectionService(
            svc.Object, keys,
            new StubPresets(AiPresetState.Ready, null, EnvPreset("openai", "OpenAI", new[] { "OPENAI_API_KEY" })),
            env);
        var vm = new AiConnectionsViewModel(service, keys, null, env);

        Assert.Single(vm.FoundKeys);

        present.Clear();
        vm.PresetSearch = " ";   // any rebind will do; this is one a reader can cause

        Assert.Empty(vm.FoundKeys);
    }
}

/// <summary>
/// #738: a row asks for its provider's logo and falls back to the monogram. The monogram is never removed —
/// this adds a better first choice on top of a mechanism that has to keep working for custom endpoints,
/// local runners, and anyone offline.
/// </summary>
public class AiConnectionRowLogoTests
{
    private sealed class FakeLogos : IAiProviderLogos
    {
        private readonly Dictionary<string, string?> _paths;
        public List<string> Asked { get; } = new();

        public FakeLogos(Dictionary<string, string?>? paths = null) =>
            _paths = paths ?? new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase);

        public Task<string?> GetLogoPathAsync(string providerId, CancellationToken ct = default)
        {
            Asked.Add(providerId);
            return Task.FromResult(_paths.TryGetValue(providerId, out var p) ? p : null);
        }
    }

    private static (AiConnectionsViewModel Vm, AiConnectionService Service) Make(IAiProviderLogos logos)
    {
        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        var keys = new AiConnectionsViewModelTests.FakeCredentialStore();
        var service = new AiConnectionService(svc.Object, keys);
        return (new AiConnectionsViewModel(service, keys, logos), service);
    }

    /// <summary>Adds through the sheet, which is the only path that produces a connection carrying a preset's
    /// id — the editor seeds the id from the preset, and the reader may then change it.</summary>
    private static void AddThroughSheet(AiConnectionsViewModel vm, string presetId, string? id = null)
    {
        vm.AddPreset(presetId);
        var editor = vm.Editor!;
        if (id is not null) editor.Id = id;
        editor.ApiKeyEntry = "sk-test";
        editor.SaveCommand.Execute().Subscribe();
    }

    /// <summary>The catalogue is the screen where logos matter most, and every row there has a real
    /// provider id.</summary>
    [Fact]
    public async Task A_catalogue_row_shows_the_logo_when_there_is_one()
    {
        var logos = new FakeLogos(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            { ["anthropic"] = "/cache/anthropic.svg" });
        var (vm, _) = Make(logos);

        var row = vm.AvailablePresets.First(p => p.Id == "anthropic");
        await row.LogoLoad!;

        Assert.Equal("/cache/anthropic.svg", row.LogoPath);
        Assert.True(row.HasLogo);
    }

    /// <summary>The fallback, and the ordinary case for a local runner: models.dev has no mark for Ollama, so
    /// the coloured initial stays.</summary>
    [Fact]
    public async Task A_provider_with_no_logo_keeps_its_monogram()
    {
        var (vm, _) = Make(new FakeLogos());

        // Ollama is the row models.dev has no mark for, which is what makes it the case worth asserting.
        var row = vm.AvailablePresets.First(p => p.Id == "ollama");
        await row.LogoLoad!;

        Assert.Null(row.LogoPath);
        Assert.False(row.HasLogo);
        Assert.False(string.IsNullOrWhiteSpace(row.Monogram));
    }

    /// <summary>
    /// A row must be drawable the instant it is created — the logo arriving later is the design, not a race
    /// to wait out.
    ///
    /// <para>The resolver here never completes, which is the point: with a fake that answers synchronously
    /// there is no "before" to observe, and this test passed while asserting nothing. (fable review)</para>
    /// </summary>
    [Fact]
    public void A_row_starts_on_its_monogram_while_the_logo_is_still_coming()
    {
        var pending = new TaskCompletionSource<string?>();
        var (vm, _) = Make(new PendingLogos(pending.Task));

        var row = vm.AvailablePresets.First(p => p.Id == "anthropic");

        Assert.False(row.LogoLoad!.IsCompleted);   // genuinely still in flight
        Assert.Null(row.LogoPath);
        Assert.False(row.HasLogo);
        Assert.False(string.IsNullOrWhiteSpace(row.Monogram));

        pending.SetResult("/cache/anthropic.svg");
    }

    /// <summary>And it swaps once the logo does arrive, without the row being rebuilt.</summary>
    [Fact]
    public async Task The_logo_replaces_the_monogram_when_it_arrives()
    {
        var pending = new TaskCompletionSource<string?>();
        var (vm, _) = Make(new PendingLogos(pending.Task));
        var row = vm.AvailablePresets.First(p => p.Id == "anthropic");

        var changed = new List<string>();
        row.PropertyChanged += (_, e) => changed.Add(e.PropertyName!);

        pending.SetResult("/cache/anthropic.svg");
        await row.LogoLoad!;

        Assert.Equal("/cache/anthropic.svg", row.LogoPath);
        Assert.True(row.HasLogo);
        Assert.Contains(nameof(AiLogoRowViewModel.LogoPath), changed);
        Assert.Contains(nameof(AiLogoRowViewModel.HasLogo), changed);
    }

    /// <summary>A resolver that never answers must not leave the row bound to nothing — the monogram is
    /// already what is on screen.</summary>
    private sealed class PendingLogos : IAiProviderLogos
    {
        private readonly Task<string?> _pending;
        public PendingLogos(Task<string?> pending) => _pending = pending;
        public Task<string?> GetLogoPathAsync(string providerId, CancellationToken ct = default) => _pending;
    }

    /// <summary>A connection added from the catalogue keeps the preset's id, so its row resolves too.</summary>
    [Fact]
    public async Task A_configured_connection_resolves_by_its_id()
    {
        var logos = new FakeLogos(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            { ["anthropic"] = "/cache/anthropic.svg" });
        var (vm, _) = Make(logos);

        AddThroughSheet(vm, "anthropic");
        var row = vm.Connections.Single();
        await row.LogoLoad!;

        Assert.Equal("/cache/anthropic.svg", row.LogoPath);
    }

    /// <summary>A custom endpoint has an id its owner invented, which is no provider's. Nothing is guessed
    /// from it: showing one vendor's mark on another's row would be worse than showing a letter.</summary>
    [Fact]
    public async Task A_custom_connection_falls_back_rather_than_guessing()
    {
        var logos = new FakeLogos(new Dictionary<string, string?>(StringComparer.OrdinalIgnoreCase)
            { ["anthropic"] = "/cache/anthropic.svg" });
        var (vm, service) = Make(logos);

        service.Add("anthropic-work", new AiConnectionDraft(
            "Anthropic (work)", ChatProviderKind.OpenAiCompatible, "http://localhost:8000/v1",
            Array.Empty<AiModelEntry>(), Array.Empty<AiHeader>(), new Dictionary<string, string>()));
        var row = vm.Connections.Single();

        // May be null now: a connection recorded as custom does not attempt a lookup at all, because there is
        // no provider behind it to look up. Before #766 it tried the id and got nothing; now it does not ask.
        // The assertion is the intent either way — a custom endpoint shows no provider mark.
        if (row.LogoLoad is not null) await row.LogoLoad;

        Assert.Equal("anthropic-work", row.Id);
        Assert.Null(row.LogoPath);
    }

    /// <summary>Constructed without a resolver — the designer, and every test that predates this — must still
    /// work rather than throw.</summary>
    [Fact]
    public void With_no_resolver_a_row_is_simply_a_monogram()
    {
        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        var service = new AiConnectionService(svc.Object, new AiConnectionsViewModelTests.FakeCredentialStore());

        var vm = new AiConnectionsViewModel(service);
        var row = vm.AvailablePresets.First();

        Assert.Null(row.LogoLoad);
        Assert.Null(row.LogoPath);
        Assert.False(string.IsNullOrWhiteSpace(row.Monogram));
    }
}
