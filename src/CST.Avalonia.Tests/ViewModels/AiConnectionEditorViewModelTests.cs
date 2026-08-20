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
/// #691: the connection sheet — a custom endpoint, a preset that needs more than a key, or an edit.
/// </summary>
public class AiConnectionEditorViewModelTests
{
    private sealed class Harness
    {
        public AiConnectionService Service { get; }
        public FakeCredentialStore Keys { get; } = new();
        public bool? Closed { get; private set; }

        public Harness()
        {
            var settings = new Settings();
            var svc = new Mock<ISettingsService>();
            svc.SetupGet(s => s.Settings).Returns(settings);
            Service = new AiConnectionService(svc.Object, Keys);
        }

        public void Close(bool saved) => Closed = saved;

        public AiConnectionEditorViewModel Custom() =>
            AiConnectionEditorViewModel.ForCustom(Service, Keys, Close);

        public AiConnectionEditorViewModel Preset(string id) =>
            AiConnectionEditorViewModel.ForPreset(
                Service, Keys, Service.Presets.Single(p => p.Id == id), Close);

        public AiConnectionEditorViewModel Existing(string id) =>
            AiConnectionEditorViewModel.ForExisting(
                Service, Keys, Service.Connections.Single(c => c.Id == id), Close);
    }

    /// <summary>An in-memory keychain, so no test prompts for a real one.</summary>
    private sealed class FakeCredentialStore : IAiCredentialStore
    {
        public Dictionary<string, string> Stored { get; } = new(StringComparer.Ordinal);
        public bool IsAvailable => true;
        public string? Unavailable => null;
        public string? GetApiKey(string connectionId) => Stored.GetValueOrDefault(connectionId);
        public bool SetApiKey(string connectionId, string apiKey) { Stored[connectionId] = apiKey; return true; }
        public bool DeleteApiKey(string connectionId) => Stored.Remove(connectionId);
    }

    private static void Save(AiConnectionEditorViewModel vm) => vm.SaveCommand.Execute().Subscribe();

    // ---- identity --------------------------------------------------------------------------------------

    /// <summary>
    /// The id is defaulted from the host so a reader who does not care never has to invent one — but it stays
    /// a default. Deriving it permanently is the mistake: a URL-derived key changes the moment a port does,
    /// and the credential then looks rejected rather than lost.
    /// </summary>
    [Fact]
    public void The_id_is_suggested_from_the_host_until_one_is_typed()
    {
        var h = new Harness();
        var vm = h.Custom();

        vm.BaseUrl = "http://localhost:11434/v1";

        Assert.Equal("localhost", vm.Id);
    }

    [Fact]
    public void A_typed_id_is_never_overwritten_by_a_later_url_edit()
    {
        var h = new Harness();
        var vm = h.Custom();

        vm.Id = "my-box";
        vm.BaseUrl = "http://localhost:11434/v1";

        Assert.Equal("my-box", vm.Id);
    }

    [Fact]
    public void A_dotted_host_becomes_a_usable_slug()
    {
        var h = new Harness();
        var vm = h.Custom();

        vm.BaseUrl = "https://api.MyProvider.com/v1";

        Assert.Equal("api-myprovider-com", vm.Id);
    }

    /// <summary>Immutable once the credential is filed under it. An editable id would mean migrating the
    /// keychain account with every rename.</summary>
    [Fact]
    public void The_id_cannot_be_changed_once_the_connection_exists()
    {
        var h = new Harness();
        h.Service.Add("mine", Draft());

        var vm = h.Existing("mine");

        Assert.False(vm.IsIdEditable);
    }

    // ---- refusals are shown, not swallowed -------------------------------------------------------------

    /// <summary>A duplicate id would mean one connection inheriting another's credential, so the service's
    /// refusal has to reach the reader and the sheet has to stay open on the field that caused it.</summary>
    [Fact]
    public void A_duplicate_id_keeps_the_sheet_open_and_says_why()
    {
        var h = new Harness();
        h.Service.Add("mine", Draft());

        var vm = h.Custom();
        vm.Id = "mine";
        vm.BaseUrl = "http://localhost:9000/v1";
        Save(vm);

        Assert.True(vm.HasProblem);
        Assert.Contains("already", vm.Problem);
        Assert.Null(h.Closed);
        Assert.Single(h.Service.Connections);
    }

    /// <summary>A preset id is reserved, and taking it by hand collides with the built-in the reader might
    /// add later.</summary>
    [Fact]
    public void A_reserved_id_is_refused_with_the_services_own_sentence()
    {
        var h = new Harness();
        var vm = h.Custom();

        vm.Id = "openrouter";
        vm.BaseUrl = "http://localhost:9000/v1";
        Save(vm);

        Assert.True(vm.HasProblem);
        Assert.Contains("built-in", vm.Problem);
        Assert.Empty(h.Service.Connections);
    }

    /// <summary>An unanswered prompt means a base URL that keeps its placeholder, so the connection could
    /// never send anything. Refused at the sheet rather than created unusable.</summary>
    [Fact]
    public void A_preset_missing_its_answer_is_refused()
    {
        var h = new Harness();
        var vm = h.Preset("azure");

        Save(vm);

        Assert.True(vm.HasProblem);
        Assert.Null(h.Closed);
        Assert.Empty(h.Service.Connections);
    }

    // ---- saving ----------------------------------------------------------------------------------------

    [Fact]
    public void A_preset_with_its_answer_is_added_and_the_sheet_closes()
    {
        var h = new Harness();
        var vm = h.Preset("azure");

        vm.Inputs.Single(i => i.Key == "resourceName").Value = "my-resource";
        Save(vm);

        Assert.True(h.Closed);
        var added = Assert.Single(h.Service.Connections);
        Assert.Equal("https://my-resource.openai.azure.com/openai/v1", added.ResolvedBaseUrl);
        Assert.False(added.IsIncomplete);
    }

    [Fact]
    public void A_custom_endpoint_is_saved_with_its_models_and_headers()
    {
        var h = new Harness();
        var vm = h.Custom();

        vm.Id = "my-box";
        vm.DisplayName = "My box";
        vm.BaseUrl = "http://localhost:8000/v1";
        vm.Models[0].ModelId = "gemma4:12b-mlx";
        vm.Models[0].DisplayName = "Gemma4 12B MLX";
        vm.AddHeaderCommand.Execute().Subscribe();
        vm.Headers[0].Name = "X-Gateway";
        vm.Headers[0].Value = "token";
        Save(vm);

        var added = Assert.Single(h.Service.Connections);
        Assert.Equal("My box", added.DisplayName);
        Assert.Equal("gemma4:12b-mlx", added.Models.Single().Id);
        Assert.Equal("Gemma4 12B MLX", added.Models.Single().DisplayName);
        Assert.Equal("token", added.Headers["X-Gateway"]);
    }

    /// <summary>Blank rows are the natural state of a repeating form — one is offered on open — so they must
    /// be dropped rather than saved as a model whose id is the empty string.</summary>
    [Fact]
    public void Empty_rows_are_dropped_rather_than_saved()
    {
        var h = new Harness();
        var vm = h.Custom();

        vm.Id = "my-box";
        vm.BaseUrl = "http://localhost:8000/v1";
        vm.AddModelCommand.Execute().Subscribe();
        vm.AddHeaderCommand.Execute().Subscribe();
        Save(vm);

        var added = Assert.Single(h.Service.Connections);
        Assert.Empty(added.Models);
        Assert.Empty(added.Headers);
    }

    /// <summary>The display name is for humans and the id is for the wire; when only one is given, showing
    /// the id beats showing an empty row.</summary>
    [Fact]
    public void A_model_with_no_display_name_falls_back_to_its_id()
    {
        var h = new Harness();
        var vm = h.Custom();

        vm.Id = "my-box";
        vm.BaseUrl = "http://localhost:8000/v1";
        vm.Models[0].ModelId = "llama3.1:8b";
        Save(vm);

        Assert.Equal("llama3.1:8b", h.Service.Connections.Single().Models.Single().DisplayName);
    }

    /// <summary>
    /// Editing must not quietly re-enable what the reader turned off.
    ///
    /// <para>The sheet has no control for <c>Enabled</c> — that belongs on the Models tab (#692) — so the flag
    /// is carried through untouched. Rebuilding the model list from the form's visible fields alone would
    /// silently reset it, and the reader would find their pruned short list restored by an unrelated rename.</para>
    /// </summary>
    [Fact]
    public void An_edit_preserves_which_models_were_turned_off()
    {
        var h = new Harness();
        h.Service.Add("mine", Draft() with
        {
            Models = new List<AiModelEntry>
            {
                new("a", "A", true),
                new("b", "B", false),
            },
        });

        var vm = h.Existing("mine");
        vm.DisplayName = "Renamed";
        Save(vm);

        var saved = h.Service.Connections.Single();
        Assert.Equal("Renamed", saved.DisplayName);
        Assert.True(saved.Models.Single(m => m.Id == "a").Enabled);
        Assert.False(saved.Models.Single(m => m.Id == "b").Enabled);
    }

    /// <summary>
    /// An edit preserves what the provider published about each model.
    ///
    /// <para>The form shows an id, a name and nothing else, so rebuilding entries from its visible fields
    /// drops the context length, modalities and reasoning flag recorded when the model was promoted — and the
    /// per-turn picker's hover card goes blank because the reader renamed a connection. Exactly the shape of
    /// the auth-header reset found in the #689 review.</para>
    /// </summary>
    [Fact]
    public void An_edit_preserves_what_the_provider_published()
    {
        var h = new Harness();
        h.Service.Add("mine", Draft() with
        {
            Models = new List<AiModelEntry>
            {
                new("nvidia/nemotron", "Nemotron", true,
                    ContextLength: 1_000_000, SupportsReasoning: true, Inputs: "text, image"),
            },
        });

        var vm = h.Existing("mine");
        vm.DisplayName = "Renamed";
        Save(vm);

        var saved = Assert.Single(h.Service.Connections.Single().Models);
        Assert.Equal(1_000_000, saved.ContextLength);
        Assert.True(saved.SupportsReasoning);
        Assert.Equal("text, image", saved.Inputs);
    }

    /// <summary>A row the reader adds by hand has nothing published, and nothing is invented for it.</summary>
    [Fact]
    public void A_newly_typed_model_carries_no_published_facts()
    {
        var h = new Harness();
        var vm = h.Custom();

        vm.Id = "my-box";
        vm.BaseUrl = "http://localhost:8000/v1";
        vm.Models[0].ModelId = "typed";
        Save(vm);

        var saved = Assert.Single(h.Service.Connections.Single().Models);
        Assert.Null(saved.ContextLength);
        Assert.Null(saved.Inputs);
        Assert.Null(saved.SupportsReasoning);
    }

    [Fact]
    public void Cancelling_closes_without_saving()
    {
        var h = new Harness();
        var vm = h.Custom();
        vm.Id = "my-box";
        vm.BaseUrl = "http://localhost:8000/v1";

        vm.CancelCommand.Execute().Subscribe();

        Assert.False(h.Closed);
        Assert.Empty(h.Service.Connections);
    }

    // ---- what a named provider's sheet asks for --------------------------------------------------------

    /// <summary>
    /// A named provider's sheet asks for the key and nothing else.
    ///
    /// <para>Its model list is fetched once the connection exists (#674), so a model box here would make the
    /// reader decide whether they need to type an id <i>before</i> seeing what the provider returns — and for
    /// OpenRouter's four hundred it is a box nobody should ever fill in. The escape hatch for an id a listing
    /// omits lives on the Models tab, beside the listing that omitted it.</para>
    /// </summary>
    [Fact]
    public void A_named_providers_sheet_asks_for_the_key_and_nothing_else()
    {
        var vm = new Harness().Preset("openrouter");

        Assert.False(vm.IsFullForm);        // no id, display name, protocol or base URL to fill in
        Assert.False(vm.ShowHeaders);       // the preset carries whatever headers it needs
        Assert.False(vm.ShowModels);        // fetched, not typed
        Assert.Empty(vm.Models);
        Assert.True(vm.ShowKeyField);
        Assert.True(vm.ShowFixedEndpoint);  // shown, not asked - where the money goes
        Assert.Equal("https://openrouter.ai/api/v1", vm.FixedEndpoint);
    }

    /// <summary>
    /// A named provider whose key is required is not told the box is optional.
    ///
    /// <para>The two ways to reach that sheet are exhaustive: a provider needing no key shows no box, so any
    /// box on a preset sheet is a required one. Calling it optional contradicts the blurb three lines above it
    /// and invites the reader to save a connection that cannot answer.</para>
    /// </summary>
    [Fact]
    public void A_required_key_is_not_called_optional()
    {
        var vm = new Harness().Preset("deepseek");

        Assert.True(vm.ShowKeyField);
        Assert.False(vm.HasKeyHint);
    }

    /// <summary>A custom endpoint keeps the line: there the box genuinely is optional — a local runner needs
    /// no key, and a gateway may authenticate through a header instead.</summary>
    [Fact]
    public void A_custom_endpoint_keeps_the_optional_line()
    {
        var vm = new Harness().Custom();

        Assert.True(vm.ShowKeyField);
        Assert.True(vm.HasKeyHint);
        Assert.True(vm.ShowHeaders);        // the "header below" the line points at
    }

    /// <summary>A custom endpoint still asks, and must: it may publish no listing at all, which is the
    /// ordinary case for a local runner, and then typing is the only way in.</summary>
    [Fact]
    public void A_custom_endpoints_sheet_still_asks_for_models()
    {
        var vm = new Harness().Custom();

        Assert.True(vm.ShowModels);
        Assert.Single(vm.Models);
    }

    /// <summary>A local runner needs no key, and saying so beats an empty box the reader wonders about.</summary>
    [Fact]
    public void A_local_runner_is_not_asked_for_a_key()
    {
        var vm = new Harness().Preset("ollama");

        Assert.False(vm.ShowKeyField);
        Assert.True(vm.ShowFixedEndpoint);
    }

    /// <summary>The key reaches the store under the connection's own id, not under whichever one happened to
    /// be active — which is #678 arriving by a different route.</summary>
    [Fact]
    public void A_key_typed_on_a_presets_sheet_is_filed_under_that_preset()
    {
        var h = new Harness();
        var vm = h.Preset("openrouter");

        vm.ApiKeyEntry = "sk-or-secret";
        Save(vm);

        Assert.Equal("sk-or-secret", h.Keys.GetApiKey("openrouter"));
        Assert.Empty(h.Service.Connections.Single().Models);
    }

    /// <summary>The box exists to hand the key over, not to hold it.</summary>
    [Fact]
    public void The_key_box_is_cleared_once_the_key_is_stored()
    {
        var h = new Harness();
        var vm = h.Preset("openrouter");

        vm.ApiKeyEntry = "sk-or-secret";
        Save(vm);

        Assert.Equal("", vm.ApiKeyEntry);
    }

    /// <summary>Removing a stored key leaves the connection and its models untouched — the narrower of the
    /// two destructive actions.</summary>
    [Fact]
    public void Removing_a_stored_key_keeps_the_connection()
    {
        var h = new Harness();
        h.Service.Add("mine", Draft());
        h.Keys.SetApiKey("mine", "sk-secret");

        var vm = h.Existing("mine");
        Assert.True(vm.HasStoredKey);

        vm.RemoveKeyCommand.Execute().Subscribe();

        Assert.False(vm.HasStoredKey);
        Assert.Null(h.Keys.GetApiKey("mine"));
        Assert.Single(h.Service.Connections);
    }

    // ---- conditional prompts ---------------------------------------------------------------------------

    /// <summary>
    /// A prompt's <c>When</c> is re-evaluated as its trigger is typed, so a conditional field appears rather
    /// than hides by default. Azure wants a resource name <i>or</i> an explicit URL, and asking for both at
    /// once is wrong.
    /// </summary>
    [Fact]
    public void A_conditional_prompt_hides_once_its_condition_stops_holding()
    {
        var h = new Harness();
        var preset = new AiProviderPreset(
            "conditional", "Conditional", ChatProviderKind.OpenAiCompatible,
            "https://{host}/v1",
            new AiCredentialMethod[] { new AiCredentialMethod.Key() },
            new[]
            {
                new AiInputPrompt("mode", "Mode"),
                new AiInputPrompt("host", "Host", When: new AiPromptCondition(
                    "mode", AiConditionOperator.NotEquals, "auto")),
            });

        var vm = AiConnectionEditorViewModel.ForPreset(h.Service, h.Keys, preset, h.Close);
        var host = vm.Inputs.Single(i => i.Key == "host");

        Assert.True(host.IsVisible);

        vm.Inputs.Single(i => i.Key == "mode").Value = "auto";

        Assert.False(host.IsVisible);
    }

    private static AiConnectionDraft Draft(string name = "My box", string url = "http://localhost:8000/v1") =>
        new(name, ChatProviderKind.OpenAiCompatible, url,
            new List<AiModelEntry>(), new Dictionary<string, string>(), new Dictionary<string, string>());

    // ---- the auth shape must survive an edit (fable review) ----------------------------------------------

    /// <summary>
    /// Azure sends its credential in `api-key` with no scheme, and expects `Authorization` to be ABSENT. That
    /// shape is set by the preset and is not editable in this form — so a draft that omitted it silently reset
    /// the connection to Bearer, and every request after the first rename came back 401.
    ///
    /// <para>The failure was invisible from the editor: nothing on screen shows the auth shape, so the
    /// connection looked untouched while it had stopped working.</para>
    /// </summary>
    [Fact]
    public void Renaming_an_azure_connection_does_not_reset_its_auth_shape()
    {
        var h = new Harness();
        Save(h.Preset("azure").With(vm => vm.Inputs.Single().Value = "acme"));

        var before = h.Service.Connections.Single();
        Assert.Equal("api-key", before.AuthHeaderName);
        Assert.Null(before.AuthScheme);

        var edit = h.Existing("azure");
        edit.DisplayName = "Work Azure";
        Save(edit);

        var after = h.Service.Connections.Single();
        Assert.Equal("Work Azure", after.DisplayName);
        Assert.Equal("api-key", after.AuthHeaderName);
        Assert.Null(after.AuthScheme);
    }

    /// <summary>Adding a model to a preset connection goes through Update too, so it is the same hazard.</summary>
    [Fact]
    public void Adding_a_model_does_not_reset_the_auth_shape()
    {
        var h = new Harness();
        Save(h.Preset("azure").With(vm => vm.Inputs.Single().Value = "acme"));

        var edit = h.Existing("azure");
        edit.Models.Add(new AiModelRowViewModel(edit.Models) { ModelId = "gpt-4o" });
        Save(edit);

        var after = h.Service.Connections.Single();
        Assert.Equal("api-key", after.AuthHeaderName);
        Assert.Null(after.AuthScheme);
    }

    /// <summary>An ordinary bearer connection keeps its shape too — the fix must not simply pin every
    /// connection to Azure's.</summary>
    [Fact]
    public void An_ordinary_connection_keeps_bearer_through_an_edit()
    {
        var h = new Harness();
        Save(h.Preset("openrouter"));

        var edit = h.Existing("openrouter");
        edit.DisplayName = "OR";
        Save(edit);

        var after = h.Service.Connections.Single();
        Assert.Equal("Authorization", after.AuthHeaderName);
        Assert.Equal("Bearer", after.AuthScheme);
    }
}

internal static class EditorTestExtensions
{
    /// <summary>Small helper so a preset needing inputs can be configured inline.</summary>
    public static AiConnectionEditorViewModel With(
        this AiConnectionEditorViewModel vm, System.Action<AiConnectionEditorViewModel> configure)
    {
        configure(vm);
        return vm;
    }
}
