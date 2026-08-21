using System;
using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Models;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai;
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
        public bool IsAvailable => true;
        public string? Unavailable => null;
        public string? Get(string connectionId, string name) =>
            Keys.GetValueOrDefault(Account(connectionId, name));
        public bool Set(string connectionId, string name, string secret)
        { Keys[Account(connectionId, name)] = secret; return true; }
        public bool Delete(string connectionId, string name) => Keys.Remove(Account(connectionId, name));
    }

    private static (AiModelPickerViewModel Picker, AiConnectionService Service, FakeCredentialStore Keys) Make()
    {
        var settings = new Settings();
        var svc = new Mock<ISettingsService>();
        svc.SetupGet(s => s.Settings).Returns(settings);
        var keys = new FakeCredentialStore();
        var service = new AiConnectionService(svc.Object, keys);
        return (new AiModelPickerViewModel(service), service, keys);
    }

    private static AiConnectionDraft Draft(string name, params AiModelEntry[] models) =>
        new(name, ChatProviderKind.OpenAiCompatible, "http://localhost:8000/v1",
            models, new Dictionary<string, string>(), new Dictionary<string, string>());

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

    /// <summary>A connection whose URL still has an unanswered placeholder cannot send anything, which is a
    /// fact rather than a guess.</summary>
    [Fact]
    public void An_unfinished_connection_is_disabled_with_the_reason()
    {
        var (picker, service, keys) = Make();
        service.Add("azure-ish", new AiConnectionDraft(
            "Half-built", ChatProviderKind.OpenAiCompatible,
            "https://{resourceName}.openai.azure.com/openai/v1",
            new[] { new AiModelEntry("a", "A") },
            new Dictionary<string, string>(), new Dictionary<string, string>()));

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
}
