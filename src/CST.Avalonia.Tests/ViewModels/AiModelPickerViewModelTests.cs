using System;
using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Models;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai;
using System.Threading.Tasks;
using CST.Avalonia.Services.Ai.Credentials;
using CST.Avalonia.ViewModels;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// #693: the per-turn model chip in the Assistant's composer.
///
/// <para>The primitive the provider rework exists for — comparing two models on the same passage, one click
/// from the answer on screen rather than a trip to Settings.</para>
/// </summary>
public class AiModelPickerViewModelTests
{
    private sealed class FakeCredentialStore : IAiCredentialStore
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

    private static (AiModelPickerViewModel Picker, AiConnectionService Service, FakeCredentialStore Keys) Make(
        IAiEnvironmentKeys? environment = null)
    {
        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        var keys = new FakeCredentialStore();
        var service = new AiConnectionService(svc.Object, keys, null, environment);
        return (new AiModelPickerViewModel(service), service, keys);
    }

    /// <summary>An environment holding exactly one variable, for the adoption cases. (#926)</summary>
    private sealed class OneVariable : IAiEnvironmentKeys
    {
        private readonly string _name;
        private readonly string _value;

        internal OneVariable(string name, string value) { _name = name; _value = value; }

        public event EventHandler? Changed;
        public string? VariableFor(AiProviderPreset preset) => _name;
        public string? ValueFor(AiProviderPreset preset) => _value;
        public string? Read(string variableName) =>
            string.Equals(variableName, _name, StringComparison.Ordinal) ? _value : null;
        public IReadOnlyList<AiEnvironmentKey> Discover(IEnumerable<AiProviderPreset> presets) =>
            Array.Empty<AiEnvironmentKey>();
        public Task Ready => Task.CompletedTask;
    }

    private static AiConnectionDraft Draft(string name, params AiModelEntry[] models) =>
        new(name, ChatProviderKind.OpenAiCompatible, "http://localhost:8000/v1",
            models, Array.Empty<AiHeader>(), new Dictionary<string, string>());

    private static IEnumerable<AiPickerModelViewModel> AllModels(AiModelPickerViewModel picker) =>
        picker.Groups.SelectMany(g => g.Models);

    // ---- a model the provider dropped (#728) -------------------------------------------------------------

    /// <summary>
    /// A model the provider no longer lists is marked in the picker, and still pickable.
    ///
    /// <para>A listing is not authority over the reader's configuration, and the mark is only ever set from a
    /// fetch that succeeded — so it is worth saying and not worth acting on. Whether the request works is
    /// still the provider's answer to give.</para>
    /// </summary>
    [Fact]
    public void A_model_the_provider_dropped_is_marked_and_still_pickable()
    {
        var (picker, service, _) = Make();
        service.Add("mine", Draft("Mine", new AiModelEntry("retired", "Retired", Missing: true)));

        var model = AllModels(picker).Single();

        Assert.True(model.ShowMissingNote);
        Assert.True(model.IsUsable);
    }

    /// <summary>
    /// Its hover card stops describing it.
    ///
    /// <para>The published facts were cached (#726) so the card could show them without a fetch. Left in for
    /// a retired model, the app would confidently state a context window and a reasoning flag for something
    /// that no longer exists, in the same shape it describes real models — a worse failure than the silence
    /// the cache replaced. The card says the one thing still true.</para>
    /// </summary>
    [Fact]
    public void A_dropped_models_card_says_it_is_gone_rather_than_describing_it()
    {
        var (picker, service, _) = Make();
        service.Add("mine", Draft("Mine", new AiModelEntry(
            "retired", "Retired", true, ContextLength: 128_000, SupportsReasoning: true, Inputs: "text",
            Missing: true)));

        var facts = AllModels(picker).Single().Facts;

        Assert.Contains(facts, f => f.Label == "Status" && f.Value.Contains("Mine"));
        Assert.DoesNotContain(facts, f => f.Label is "Context" or "Reasoning" or "Inputs");
    }

    /// <summary>A reason it cannot be used at all outranks the mark: one message in that space, and the one
    /// that stops the request is the one to read.</summary>
    [Fact]
    public void A_reason_it_cannot_be_used_outranks_the_mark()
    {
        var (picker, service, _) = Make();
        service.AddFromPreset("openrouter", new Dictionary<string, string>());
        service.EnableModel("openrouter", "retired", "Retired", true);
        service.MarkListing("openrouter", new[] { "something-else" });

        var model = AllModels(picker).Single(m => m.ModelId == "retired");

        Assert.False(model.IsUsable);          // no key stored
        Assert.True(model.Missing);
        Assert.False(model.ShowMissingNote);   // so the mark stands down
    }

    // ---- what the list contains ------------------------------------------------------------------------

    /// <summary>
    /// Only the enabled subset. The full listing lives in Settings → Models; this is the short list the
    /// reader built there, which is what lets it be a flat grouped list with no virtualization.
    /// </summary>
    [Fact]
    public void Only_enabled_models_are_offered()
    {
        var (picker, service, _) = Make();
        service.Add("mine", Draft("Mine",
            new AiModelEntry("on", "On", true),
            new AiModelEntry("off", "Off", false)));

        Assert.Equal(new[] { "on" }, AllModels(picker).Select(m => m.ModelId));
    }

    /// <summary>Grouped by connection — what stops "the 8B I run locally" and "the hosted 70B" reading as
    /// interchangeable rows once several endpoints are configured.</summary>
    [Fact]
    public void Models_are_grouped_by_connection()
    {
        var (picker, service, _) = Make();
        service.Add("local", Draft("Local Ollama", new AiModelEntry("a", "A")));
        service.Add("hosted", Draft("Hosted", new AiModelEntry("b", "B")));

        Assert.Equal(new[] { "Local Ollama", "Hosted" }, picker.Groups.Select(g => g.DisplayName));
    }

    /// <summary>A connection with nothing enabled contributes no header — an empty group is a heading
    /// promising rows it does not have.</summary>
    [Fact]
    public void A_connection_with_nothing_enabled_is_absent()
    {
        var (picker, service, _) = Make();
        service.Add("empty", Draft("Empty", new AiModelEntry("x", "X", false)));
        service.Add("full", Draft("Full", new AiModelEntry("y", "Y")));

        Assert.Equal(new[] { "Full" }, picker.Groups.Select(g => g.DisplayName));
    }

    /// <summary>
    /// The chip appears as soon as one model is enabled, not two.
    ///
    /// <para>It used to require two, on the reasoning that a chip offering a single model cannot do anything.
    /// The chip is also the only place that says which model will answer — and while it was hidden at one
    /// enabled model, a reader with exactly one had no control anywhere that could configure the
    /// assistant.</para>
    /// </summary>
    [Fact]
    public void The_chip_appears_as_soon_as_one_model_is_enabled()
    {
        var (picker, service, _) = Make();
        Assert.False(picker.HasChoices);

        service.Add("mine", Draft("Mine", new AiModelEntry("a", "A")));

        Assert.True(picker.HasChoices);
    }

    // ---- choosing ---------------------------------------------------------------------------------------

    /// <summary>The chip shows the display name, not the wire id: nobody calls the thing they are talking to
    /// <c>nvidia/nemotron-nano-9b-v2</c>.</summary>
    [Fact]
    public void The_chip_shows_the_current_models_display_name()
    {
        var (picker, service, _) = Make();
        service.Add("mine", Draft("Mine",
            new AiModelEntry("nvidia/nemotron-nano-9b-v2", "Nemotron Nano 9B"),
            new AiModelEntry("other", "Other")));

        AllModels(picker).Single(m => m.ModelId == "nvidia/nemotron-nano-9b-v2")
            .SelectCommand.Execute().Subscribe();

        Assert.Equal("Nemotron Nano 9B", picker.CurrentLabel);
    }

    /// <summary>
    /// Choosing a model on another connection moves the connection with it.
    ///
    /// <para>Switching model across providers means switching base URL <i>and</i> credential; a picker that
    /// moved only the model id would send the second provider's model to the first provider's endpoint and
    /// fail confusingly.</para>
    /// </summary>
    [Fact]
    public void Choosing_a_model_on_another_connection_switches_the_connection_too()
    {
        var (picker, service, _) = Make();
        service.Add("local", Draft("Local", new AiModelEntry("a", "A")));
        service.Add("hosted", Draft("Hosted", new AiModelEntry("b", "B")));

        AllModels(picker).Single(m => m.ModelId == "b").SelectCommand.Execute().Subscribe();

        Assert.Equal("hosted", service.Active?.Id);
        Assert.Equal("b", service.ActiveModelId);
    }

    /// <summary>The current one is marked, so the reader can see what answered the last question without
    /// opening anything else.</summary>
    [Fact]
    public void The_current_model_is_marked()
    {
        var (picker, service, _) = Make();
        service.Add("mine", Draft("Mine", new AiModelEntry("a", "A"), new AiModelEntry("b", "B")));

        AllModels(picker).Single(m => m.ModelId == "b").SelectCommand.Execute().Subscribe();

        Assert.True(AllModels(picker).Single(m => m.ModelId == "b").IsCurrent);
        Assert.False(AllModels(picker).Single(m => m.ModelId == "a").IsCurrent);
    }

    /// <summary>Choosing closes the popup and clears the search, so the next open starts from the whole
    /// list rather than from whatever was typed last time.</summary>
    [Fact]
    public void Choosing_closes_the_popup_and_clears_the_search()
    {
        var (picker, service, _) = Make();
        service.Add("mine", Draft("Mine", new AiModelEntry("a", "Alpha"), new AiModelEntry("b", "Beta")));
        picker.IsOpen = true;
        picker.Search = "alp";

        AllModels(picker).Single().SelectCommand.Execute().Subscribe();

        Assert.False(picker.IsOpen);
        Assert.Equal("", picker.Search);
    }

    /// <summary>The panel is told, so a standing "not configured" notice reflects the new choice rather than
    /// waiting to be contradicted at send time.</summary>
    [Fact]
    public void Choosing_notifies_the_panel()
    {
        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        var service = new AiConnectionService(svc.Object);
        var told = 0;
        var picker = new AiModelPickerViewModel(service, () => told++);

        service.Add("mine", Draft("Mine", new AiModelEntry("a", "A")));
        AllModels(picker).Single().SelectCommand.Execute().Subscribe();

        Assert.Equal(1, told);
    }

    // ---- search ------------------------------------------------------------------------------------------

    [Fact]
    public void Search_matches_the_name_or_the_id()
    {
        var (picker, service, _) = Make();
        service.Add("mine", Draft("Mine",
            new AiModelEntry("nvidia/nemotron", "Nano"),
            new AiModelEntry("meta/llama", "Llama")));

        picker.Search = "nemotron";
        Assert.Equal(new[] { "nvidia/nemotron" }, AllModels(picker).Select(m => m.ModelId));

        picker.Search = "llama";
        Assert.Equal(new[] { "meta/llama" }, AllModels(picker).Select(m => m.ModelId));
    }

    [Fact]
    public void A_search_matching_nothing_says_so()
    {
        var (picker, service, _) = Make();
        service.Add("mine", Draft("Mine", new AiModelEntry("a", "A")));

        picker.Search = "zzz";

        Assert.True(picker.HasNothingMatching);
    }

    // ---- the hover card ----------------------------------------------------------------------------------

    /// <summary>
    /// The card names the model and its provider, always.
    ///
    /// <para>With several endpoints configured, "Gemma 4" alone does not say whether it is the local one —
    /// which is exactly the confusion the grouping exists to prevent, and the card should not undo it.</para>
    /// </summary>
    [Fact]
    public void The_hover_card_names_the_model_and_its_provider()
    {
        var (picker, service, _) = Make();
        service.Add("local", Draft("Local Ollama", new AiModelEntry("gemma4:12b-mlx", "Gemma4 12B MLX")));

        var facts = AllModels(picker).Single().Facts;

        Assert.Equal("Gemma4 12B MLX", facts.Single(f => f.Label == "Model").Value);
        Assert.Equal("Local Ollama", facts.Single(f => f.Label == "Provider").Value);
    }

    /// <summary>
    /// A field the provider never published gets no row.
    ///
    /// <para>OpenCode's equivalent card shows "Context 0" and "No reasoning" for a local model that published
    /// neither. That reads as fact and is not one — an absent row is the honest rendering of an absent
    /// field, and a local runner publishes nothing at all.</para>
    /// </summary>
    [Fact]
    public void Unpublished_facts_get_no_row_rather_than_a_zero()
    {
        var (picker, service, _) = Make();
        service.Add("local", Draft("Local Ollama", new AiModelEntry("gemma4:12b-mlx", "Gemma4 12B MLX")));

        var labels = AllModels(picker).Single().Facts.Select(f => f.Label).ToList();

        Assert.DoesNotContain("Context", labels);
        Assert.DoesNotContain("Reasoning", labels);
        Assert.DoesNotContain("Inputs", labels);
    }

    /// <summary>What the provider did publish is shown, in its own words.</summary>
    [Fact]
    public void Published_facts_become_rows()
    {
        var (picker, service, _) = Make();
        service.Add("openrouter-ish", Draft("OpenRouter",
            new AiModelEntry("nvidia/nemotron", "Nemotron", true,
                ContextLength: 1_000_000, SupportsReasoning: true, Inputs: "text, image")));

        var facts = AllModels(picker).Single().Facts;

        Assert.Equal("1,000,000", facts.Single(f => f.Label == "Context").Value);
        Assert.Equal("Allows reasoning", facts.Single(f => f.Label == "Reasoning").Value);
        Assert.Equal("text, image", facts.Single(f => f.Label == "Inputs").Value);
        Assert.Equal("nvidia/nemotron", facts.Single(f => f.Label == "Id").Value);
    }

    /// <summary>
    /// A provider that published a parameter list without reasoning in it says so; one that published no list
    /// at all says nothing.
    ///
    /// <para>Three states, not two. OpenCode renders both as "No reasoning", which turns a local runner's
    /// silence into an assertion about the model.</para>
    /// </summary>
    [Fact]
    public void Reasoning_distinguishes_published_no_from_no_answer()
    {
        var (picker, service, _) = Make();
        service.Add("mine", Draft("Mine",
            new AiModelEntry("said-yes", "Said yes", true, SupportsReasoning: true),
            new AiModelEntry("said-no", "Said no", true, SupportsReasoning: false),
            new AiModelEntry("said-nothing", "Said nothing", true)));

        string? Reasoning(string id) => AllModels(picker).Single(m => m.ModelId == id)
            .Facts.FirstOrDefault(f => f.Label == "Reasoning")?.Value;

        Assert.Equal("Allows reasoning", Reasoning("said-yes"));
        Assert.Equal("No reasoning", Reasoning("said-no"));
        Assert.Null(Reasoning("said-nothing"));
    }

    /// <summary>Rows read in the order a reader scans them, with the wire id last — the card has room for the
    /// context window in full rather than rounded to thousands.</summary>
    [Fact]
    public void The_card_reads_in_a_fixed_order()
    {
        var (picker, service, _) = Make();
        service.Add("mine", Draft("Mine",
            new AiModelEntry("nvidia/nemotron", "Nemotron", true,
                ContextLength: 1_000_000, SupportsReasoning: true, Inputs: "text")));

        Assert.Equal(
            new[] { "Model", "Provider", "Inputs", "Reasoning", "Context", "Id" },
            AllModels(picker).Single().Facts.Select(f => f.Label));
    }

    /// <summary>The wire id is shown only when it differs from the name — repeating the same string twice is
    /// noise, and a hand-typed model usually has no separate name.</summary>
    [Fact]
    public void The_id_row_is_omitted_when_it_repeats_the_name()
    {
        var (picker, service, _) = Make();
        service.Add("mine", Draft("Mine", new AiModelEntry("gemma4:12b", "gemma4:12b")));

        Assert.DoesNotContain(AllModels(picker).Single().Facts, f => f.Label == "Id");
    }

    // ---- what cannot be used, and why ---------------------------------------------------------------------

    /// <summary>
    /// A provider whose preset says it needs a key, with none stored, is shown disabled and says why.
    ///
    /// <para>Otherwise the send fails with a 401 that names neither the cause nor the connection — which is
    /// the failure this is here to prevent.</para>
    /// </summary>
    [Fact]
    public void A_provider_missing_a_required_key_is_disabled_with_the_reason()
    {
        var (picker, service, keys) = Make();
        service.AddFromPreset("openrouter", new Dictionary<string, string>());
        service.EnableModel("openrouter", "x/y", "X", true);

        var model = AllModels(picker).Single();
        Assert.False(model.IsUsable);
        Assert.Equal("no API key stored", model.Unusable);

        keys.Set("openrouter", AiCredentialNames.Primary, "sk-or-secret");
        picker.Refresh();

        Assert.True(AllModels(picker).Single().IsUsable);
    }

    /// <summary>
    /// A stored key the OS will not hand over disables the model, and says so in its own words. (#926)
    ///
    /// <para><b>This test did not exist and should have.</b> The branch it covers could be deleted with the
    /// whole suite still green: the fake gained an <c>Unreadable</c> set that nothing used, so an unreadable
    /// connection fell past the <c>KeySource == None</c> check and the model became silently usable — half
    /// the fix reverted, undetected. (fable)</para>
    ///
    /// <para>The reason is asserted as "not the missing-key sentence" rather than by its exact words: the
    /// wording is free to change, the distinction is not.</para>
    /// </summary>
    [Fact]
    public void A_model_whose_stored_key_cannot_be_read_is_unusable_for_that_reason()
    {
        var (picker, service, keys) = Make();
        service.AddFromPreset("openrouter", new Dictionary<string, string>());
        service.EnableModel("openrouter", "x/y", "X", true);
        keys.Set("openrouter", AiCredentialNames.Primary, "sk-or-secret");
        picker.Refresh();
        Assert.True(AllModels(picker).Single().IsUsable);

        keys.Unreadable.Add("openrouter:" + AiCredentialNames.Primary);
        picker.Refresh();

        var model = AllModels(picker).Single();
        Assert.False(model.IsUsable);
        Assert.NotEqual("no API key stored", model.Unusable);
        Assert.NotEqual("", model.Unusable);
    }

    /// <summary>
    /// A locked stored key with a working environment variable leaves the model usable. (#926)
    ///
    /// <para>The request path resolves <c>stored ?? environment</c>, so it sends perfectly well. Disabling it
    /// here would have the picker contradict the wire — the failure class <see cref="Reachability"/> exists
    /// to prevent one layer up, arriving from the credential side instead.</para>
    /// </summary>
    [Fact]
    public void A_locked_key_with_an_environment_fallback_leaves_the_model_usable()
    {
        var (picker, service, keys) = Make(new OneVariable("OPENROUTER_API_KEY", "sk-or-from-env"));
        service.AddFromPreset("openrouter", new Dictionary<string, string>(), "OPENROUTER_API_KEY");
        service.EnableModel("openrouter", "x/y", "X", true);
        keys.Set("openrouter", AiCredentialNames.Primary, "sk-or-secret");
        keys.Unreadable.Add("openrouter:" + AiCredentialNames.Primary);
        picker.Refresh();

        var model = AllModels(picker).Single();
        Assert.True(model.IsUsable);
        Assert.Equal("", model.Unusable);
    }

    /// <summary>
    /// A custom endpoint with no key is left alone.
    ///
    /// <para>Plenty need none — every local runner — and there is no fact saying otherwise, so nothing is
    /// claimed. Disabling on a hunch would be worse than letting the request explain itself.</para>
    /// </summary>
    [Fact]
    public void A_custom_endpoint_with_no_key_is_not_second_guessed()
    {
        var (picker, service, _) = Make();
        service.Add("my-ollama", Draft("My Ollama", new AiModelEntry("a", "A")));

        Assert.True(AllModels(picker).Single().IsUsable);
    }

    /// <summary>
    /// A connection whose URL still has an unanswered placeholder cannot send anything, which is a fact
    /// rather than a guess.
    ///
    /// <para><b>Built directly in settings rather than through the service</b>, because #767 taught the
    /// service to refuse this state on every save path — which is why the picker's guard now looks
    /// unreachable and is not. A hand-edited <c>settings.json</c> is a supported way in (the resolver's own
    /// comments say so, and #784 is the reminder of what happens when we assume otherwise), and a file
    /// written before #767 can hold exactly this. The service refusing to CREATE it does not mean nothing
    /// can be READING it.</para>
    /// </summary>
    [Fact]
    public void An_unfinished_connection_is_disabled_with_the_reason()
    {
        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        settings.Ai.Chat.Connections.Add(new CST.Avalonia.Models.AiConnectionRecord
        {
            Id = "azure-ish",
            DisplayName = "Half-built",
            BaseUrl = "https://{resourceName}.openai.azure.com/openai/v1",
            Models = { new CST.Avalonia.Models.AiModelRecord { Id = "a", DisplayName = "A", Enabled = true } },
        });
        settings.Ai.Chat.ActiveConnectionId = "azure-ish";

        var picker = new AiModelPickerViewModel(
            new AiConnectionService(svc.Object, new FakeCredentialStore()));

        var model = AllModels(picker).Single();
        Assert.False(model.IsUsable);
        Assert.Equal("not finished being set up", model.Unusable);
    }

    /// <summary>
    /// An unreachable connection is marked, not hidden.
    ///
    /// <para>The reader may be about to start their local runner; a provider that vanished from the picker
    /// because it was asleep would look like a lost configuration.</para>
    /// </summary>
    [Fact]
    public void An_unreachable_connection_is_marked_rather_than_hidden()
    {
        var (picker, service, _) = Make();
        service.Add("local", Draft("Local", new AiModelEntry("a", "A")));

        service.ReportReachability("local", reachable: false);

        var group = Assert.Single(picker.Groups);
        Assert.True(group.HasNote);
        Assert.Equal("not responding", group.Note);
        Assert.Single(group.Models);
    }

    // ---- the effort chip (#671) -------------------------------------------------------------------------

    private static (AiEffortPickerViewModel Picker, AiConnectionService Service, Settings Settings)
        MakeEffort(params string[] efforts)
    {
        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        var service = new AiConnectionService(svc.Object, new FakeCredentialStore());
        service.Add("mine", new AiConnectionDraft(
            "Mine", ChatProviderKind.OpenAiCompatible, "https://example.test/v1",
            new[] { new AiModelEntry("m", "M", ReasoningEfforts: efforts.Length == 0 ? null : efforts) },
            Array.Empty<AiHeader>(), new Dictionary<string, string>()));
        service.SetActive("mine", "m");
        return (new AiEffortPickerViewModel(service, svc.Object), service, settings);
    }

    /// <summary>
    /// Hidden where the model published no levels — which is most models. A chip that is always present but
    /// empty would imply the app knows something about a model it knows nothing about, and #671 is explicit
    /// that the alternative to publishing is not guessing: it is saying nothing.
    /// </summary>
    [Fact]
    public void The_effort_chip_is_hidden_when_the_model_publishes_no_levels()
    {
        var (picker, _, _) = MakeEffort();

        Assert.False(picker.HasChoices);
    }

    [Fact]
    public void The_effort_chip_offers_the_models_own_levels_and_nothing_else()
    {
        var (picker, _, _) = MakeEffort("low", "high", "max");

        Assert.True(picker.HasChoices);
        Assert.Equal(
            new[] { "Provider default", "low", "high", "max" },
            picker.Choices.Select(c => c.Label));
    }

    /// <summary>And with a published default, the levels alone.</summary>
    [Fact]
    public void The_effort_chip_offers_only_the_levels_when_the_default_is_known()
    {
        var (picker, _) = MakeEffortWithDefault("high", "low", "high", "max");

        Assert.Equal(new[] { "low", "high", "max" }, picker.Choices.Select(c => c.Label));
    }

    /// <summary>Provider default is first, sends nothing, and is where an untouched setting sits.</summary>
    [Fact]
    public void Provider_default_is_first_and_current_until_something_is_chosen()
    {
        var (picker, _, _) = MakeEffort("low", "high");

        var first = picker.Choices.First();
        Assert.Null(first.Value);
        Assert.True(first.IsCurrent);
        Assert.Equal("Effort: default", picker.CurrentLabel);
    }

    [Fact]
    public void Choosing_a_level_records_it_and_shows_it_on_the_chip()
    {
        var (picker, _, settings) = MakeEffort("low", "high");

        picker.Choices.Single(c => c.Label == "high").ChooseCommand.Execute().Subscribe();

        Assert.Equal("high", settings.Ai.Chat.ReasoningEffort);
        Assert.Equal("Effort: high", picker.CurrentLabel);
    }

    [Fact]
    public void Choosing_provider_default_again_clears_the_setting()
    {
        var (picker, _, settings) = MakeEffort("low", "high");
        picker.Choices.Single(c => c.Label == "high").ChooseCommand.Execute().Subscribe();

        picker.Choices.First().ChooseCommand.Execute().Subscribe();

        Assert.Null(settings.Ai.Chat.ReasoningEffort);
    }

    private static (AiEffortPickerViewModel Picker, Settings Settings) MakeEffortWithDefault(
        string theirDefault, params string[] efforts)
    {
        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        var service = new AiConnectionService(svc.Object, new FakeCredentialStore());
        service.Add("mine", new AiConnectionDraft(
            "Mine", ChatProviderKind.OpenAiCompatible, "https://example.test/v1",
            new[] { new AiModelEntry("m", "M", ReasoningEfforts: efforts,
                                     DefaultReasoningEffort: theirDefault) },
            Array.Empty<AiHeader>(), new Dictionary<string, string>()));
        service.SetActive("mine", "m");
        return (new AiEffortPickerViewModel(service, svc.Object), settings);
    }

    /// <summary>
    /// Where the provider says which level it applies, that level is ticked and there is no separate
    /// "Provider default" row: the reader wants to know what will happen, and a row saying "default" above a
    /// list containing the default says it twice and answers it once.
    /// </summary>
    [Fact]
    public void A_published_default_is_ticked_and_needs_no_extra_row()
    {
        var (picker, settings) = MakeEffortWithDefault("high", "high", "medium");

        Assert.Equal(new[] { "high", "medium" }, picker.Choices.Select(c => c.Label));
        Assert.True(picker.Choices.Single(c => c.Label == "high").IsCurrent);
        Assert.Equal("Effort: high", picker.CurrentLabel);
        Assert.Null(settings.Ai.Chat.ReasoningEffort);   // ticked, and still sending nothing
    }

    /// <summary>The tick describes the outcome, not the payload: nothing is sent until the reader moves it.</summary>
    [Fact]
    public void Ticking_the_published_default_does_not_start_sending_a_field()
    {
        var (_, settings) = MakeEffortWithDefault("high", "high", "medium");

        Assert.Null(settings.Ai.Chat.ReasoningEffort);
    }

    [Fact]
    public void Choosing_a_level_beside_the_published_default_moves_the_tick()
    {
        var (picker, settings) = MakeEffortWithDefault("high", "high", "medium");

        picker.Choices.Single(c => c.Label == "medium").ChooseCommand.Execute().Subscribe();

        Assert.Equal("medium", settings.Ai.Chat.ReasoningEffort);
        Assert.True(picker.Choices.Single(c => c.Label == "medium").IsCurrent);
        Assert.False(picker.Choices.Single(c => c.Label == "high").IsCurrent);
    }

    /// <summary>
    /// The same stale-choice case with a published default: the default level is ticked, not nothing — the
    /// wire drops the stale value and the provider applies its own, so that is what the flyout must say.
    /// </summary>
    [Fact]
    public void A_stale_choice_falls_back_to_the_published_default_being_ticked()
    {
        var (picker, settings) = MakeEffortWithDefault("high", "high", "medium");
        settings.Ai.Chat.ReasoningEffort = "max";   // real elsewhere, absent here
        picker.Rebuild();

        Assert.True(picker.Choices.Single(c => c.Label == "high").IsCurrent);
        Assert.False(picker.Choices.Single(c => c.Label == "medium").IsCurrent);
        Assert.Equal("Effort: high", picker.CurrentLabel);
    }

    /// <summary>
    /// Where the provider publishes no default there is no basis for ticking a level, so the extra row earns
    /// its place: something has to be current.
    /// </summary>
    [Fact]
    public void With_no_published_default_the_provider_default_row_remains()
    {
        var (picker, _, _) = MakeEffort("low", "high");

        Assert.Equal(new[] { "Provider default", "low", "high" }, picker.Choices.Select(c => c.Label));
        Assert.True(picker.Choices.First().IsCurrent);
    }

    /// <summary>
    /// A choice made on another model is outside this one's vocabulary, so the wire guard drops it and the
    /// provider applies its own default. The flyout has to say that: keying the default row off "is anything
    /// stored" left NO row ticked, showing an unmarked list for a setting that does have an effect. The chip
    /// label and the flyout must agree, because they describe the same fact. (fable review)
    /// </summary>
    [Fact]
    public void A_choice_from_another_models_vocabulary_leaves_provider_default_current()
    {
        var (picker, _, settings) = MakeEffort("low", "medium", "high");
        settings.Ai.Chat.ReasoningEffort = "max";   // real at DeepSeek, absent here
        picker.Rebuild();

        Assert.True(picker.Choices.First().IsCurrent);
        Assert.DoesNotContain(picker.Choices.Skip(1), c => c.IsCurrent);
        Assert.Equal("Effort: default", picker.CurrentLabel);
    }

    /// <summary>
    /// The one dynamic behaviour the chip exists for. Every other test here builds the picker after the world
    /// is already set up, so removing the ConnectionsChanged subscription would leave them all green while
    /// the chip went stale on every model switch — offering the previous model's levels. (fable review)
    /// </summary>
    [Fact]
    public void The_chip_follows_a_switch_to_a_model_with_different_levels()
    {
        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        var service = new AiConnectionService(svc.Object, new FakeCredentialStore());
        service.Add("mine", new AiConnectionDraft(
            "Mine", ChatProviderKind.OpenAiCompatible, "https://example.test/v1",
            new[]
            {
                new AiModelEntry("a", "A", ReasoningEfforts: new[] { "low", "high" }),
                new AiModelEntry("b", "B", ReasoningEfforts: new[] { "minimal", "medium" }),
            },
            Array.Empty<AiHeader>(), new Dictionary<string, string>()));
        service.SetActive("mine", "a");

        var picker = new AiEffortPickerViewModel(service, svc.Object);
        Assert.Equal(new[] { "Provider default", "low", "high" }, picker.Choices.Select(c => c.Label));

        service.SetActive("mine", "b");

        Assert.Equal(new[] { "Provider default", "minimal", "medium" }, picker.Choices.Select(c => c.Label));
    }

    /// <summary>And it disappears entirely when the reader switches to a model that published nothing.</summary>
    [Fact]
    public void The_chip_hides_itself_on_a_switch_to_a_model_with_no_levels()
    {
        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        var service = new AiConnectionService(svc.Object, new FakeCredentialStore());
        service.Add("mine", new AiConnectionDraft(
            "Mine", ChatProviderKind.OpenAiCompatible, "https://example.test/v1",
            new[]
            {
                new AiModelEntry("a", "A", ReasoningEfforts: new[] { "low", "high" }),
                new AiModelEntry("b", "B"),
            },
            Array.Empty<AiHeader>(), new Dictionary<string, string>()));
        service.SetActive("mine", "a");

        var picker = new AiEffortPickerViewModel(service, svc.Object);
        Assert.True(picker.HasChoices);

        service.SetActive("mine", "b");

        Assert.False(picker.HasChoices);
    }

}
