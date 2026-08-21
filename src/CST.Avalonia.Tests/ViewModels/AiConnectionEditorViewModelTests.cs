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
        /// <summary>Keyed by the joined account, exactly as the real store files it (#759).</summary>
        public Dictionary<string, string> Stored { get; } = new(StringComparer.Ordinal);
        private static string Account(string connectionId, string name) => connectionId + ":" + name;
        public bool IsAvailable => true;
        public string? Unavailable => null;
        public string? Get(string connectionId, string name) =>
            Stored.GetValueOrDefault(Account(connectionId, name));
        public bool Set(string connectionId, string name, string secret)
        { Stored[Account(connectionId, name)] = secret; return true; }
        public bool Delete(string connectionId, string name) => Stored.Remove(Account(connectionId, name));
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
        vm.ApiKeyEntry = "sk-test";   // supplied, so this exercises the missing answer and not #761's refusal

        Save(vm);

        Assert.Contains("resource", vm.Problem, StringComparison.OrdinalIgnoreCase);
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
        vm.ApiKeyEntry = "sk-test";
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
        Assert.True(vm.ShowKeyField);       // the key, and on a preset sheet that is the whole form
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

    /// <summary>
    /// Editing a connection added from a key-requiring provider does not call the key optional either.
    ///
    /// <para>Gating on "is this a preset sheet" would leave the line on the sheet a reader is most likely to
    /// be reading it on: Edit is reached precisely because the key is missing or wrong, one click from where
    /// the contradiction was reported. The edit form carries no preset, so the requirement has to be
    /// recovered from the id — which is unambiguous, a custom connection being refused a preset's id.
    /// (fable review)</para>
    /// </summary>
    [Fact]
    public void Editing_a_named_provider_does_not_call_its_key_optional()
    {
        var h = new Harness();
        h.Service.AddFromPreset("deepseek", new Dictionary<string, string>());

        var vm = h.Existing("deepseek");

        Assert.True(vm.ShowKeyField);
        Assert.False(vm.HasKeyHint);
    }

    /// <summary>Editing a custom endpoint keeps the line: nothing about it requires a key.</summary>
    [Fact]
    public void Editing_a_custom_endpoint_keeps_the_optional_line()
    {
        var h = new Harness();
        Save(h.Custom().With(vm =>
        {
            vm.Id = "my-box";
            vm.BaseUrl = "http://localhost:1234/v1";
        }));

        Assert.True(h.Existing("my-box").HasKeyHint);
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

        Assert.Equal("sk-or-secret", h.Keys.Get("openrouter", AiCredentialNames.Primary));
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
        h.Keys.Set("mine", AiCredentialNames.Primary, "sk-secret");

        var vm = h.Existing("mine");
        Assert.True(vm.HasStoredKey);

        vm.RemoveKeyCommand.Execute().Subscribe();

        Assert.False(vm.HasStoredKey);
        Assert.Null(h.Keys.Get("mine", AiCredentialNames.Primary));
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

    // ---- a required key is required (#761) ---------------------------------------------------------------

    /// <summary>
    /// A provider that requires a key is not added without one.
    ///
    /// <para>What the save created otherwise looked exactly like a working connection — the provider's own
    /// logo and name on the Providers tab, its models on the next — and announced itself as a 401 later, at
    /// the moment the reader was trying to read something.</para>
    /// </summary>
    [Fact]
    public void A_named_provider_is_not_added_without_its_key()
    {
        var h = new Harness();
        var vm = h.Preset("deepseek");

        Save(vm);

        Assert.Empty(h.Service.Connections);
        Assert.Null(h.Closed);
        Assert.Contains("API key", vm.Problem);
    }

    /// <summary>With the key, the same sheet saves — the refusal is about the key alone.</summary>
    [Fact]
    public void A_named_provider_is_added_with_its_key()
    {
        var h = new Harness();

        Save(h.Preset("deepseek").With(vm => vm.ApiKeyEntry = "sk-test"));

        Assert.Single(h.Service.Connections);
        Assert.Equal("sk-test", h.Keys.Get("deepseek", AiCredentialNames.Primary));
    }

    /// <summary>Editing a connection whose key is already stored does not demand it again — the box is empty
    /// on every edit, and the key it would be asking for is the one already filed.</summary>
    [Fact]
    public void Editing_a_named_provider_with_a_stored_key_does_not_demand_it_again()
    {
        var h = new Harness();
        Save(h.Preset("deepseek").With(vm => vm.ApiKeyEntry = "sk-test"));

        var edit = h.Existing("deepseek");
        edit.DisplayName = "DS";
        Save(edit);

        Assert.Equal("DS", h.Service.Connections.Single().DisplayName);
        Assert.True(h.Closed);
    }

    /// <summary>A local runner is still added with nothing typed: it requires no key, so there is none to
    /// withhold.</summary>
    [Fact]
    public void A_local_runner_is_still_added_with_no_key()
    {
        var h = new Harness();

        Save(h.Preset("ollama"));

        Assert.Single(h.Service.Connections);
    }

    /// <summary>A custom endpoint is still added with no key: it may authenticate by header, or need nothing
    /// at all, which is exactly what its own hint says.</summary>
    [Fact]
    public void A_custom_endpoint_is_still_added_with_no_key()
    {
        var h = new Harness();

        Save(h.Custom().With(vm =>
        {
            vm.Id = "my-box";
            vm.BaseUrl = "http://localhost:1234/v1";
        }));

        Assert.Single(h.Service.Connections);
    }

    // ---- the auth shape must survive an edit (fable review) ----------------------------------------------

    /// <summary>
    /// Azure sends its credential in `api-key` with no scheme, and expects `Authorization` to be ABSENT. That
    /// shape is set by the preset and is not editable on either form — so a draft that omitted it silently
    /// reset the connection to Bearer, and every request after the first edit came back 401.
    ///
    /// <para>The failure was invisible from the editor: nothing on screen shows the auth shape, so the
    /// connection looked untouched while it had stopped working.</para>
    /// </summary>
    [Fact]
    public void Editing_an_azure_connection_does_not_reset_its_auth_shape()
    {
        var h = new Harness();
        Save(h.Preset("azure").With(vm =>
        {
            vm.Inputs.Single().Value = "acme";
            vm.ApiKeyEntry = "sk-test";   // required, and refused without one (#761)
        }));

        var before = h.Service.Connections.Single();
        Assert.Equal("api-key", before.AuthHeaderName);
        Assert.Null(before.AuthScheme);

        var edit = h.Existing("azure");
        edit.Inputs.Single().Value = "acme-2";
        Save(edit);

        var after = h.Service.Connections.Single();
        Assert.Contains("acme-2", after.ResolvedBaseUrl);
        Assert.Equal("api-key", after.AuthHeaderName);
        Assert.Null(after.AuthScheme);
    }

    /// <summary>An ordinary bearer connection keeps its shape too — the fix must not simply pin every
    /// connection to Azure's.</summary>
    [Fact]
    public void An_ordinary_connection_keeps_bearer_through_an_edit()
    {
        var h = new Harness();
        Save(h.Preset("openrouter").With(vm => vm.ApiKeyEntry = "sk-test"));

        var edit = h.Existing("openrouter");
        edit.ApiKeyEntry = "sk-replaced";
        Save(edit);

        var after = h.Service.Connections.Single();
        Assert.Equal("Authorization", after.AuthHeaderName);
        Assert.Equal("Bearer", after.AuthScheme);
    }

    // ---- an edit is the form the add was (#691) ----------------------------------------------------------

    /// <summary>
    /// Editing a connection added from the provider list opens the same form it was added on.
    ///
    /// <para>Reported as a surprise from use: the edit sheet offered protocol, address, models and headers,
    /// none of which are the reader's to override on a named provider — and that form was load-bearing for
    /// the auth-shape bug above. Where a provider stops behaving as its preset says, it can be added again as
    /// a custom endpoint.</para>
    /// </summary>
    [Fact]
    public void Editing_a_named_provider_shows_the_form_it_was_added_on()
    {
        var h = new Harness();
        Save(h.Preset("openrouter").With(vm => vm.ApiKeyEntry = "sk-test"));

        var edit = h.Existing("openrouter");

        Assert.False(edit.IsFullForm);      // no protocol or address to override
        Assert.False(edit.ShowModels);      // the Models tab owns those
        Assert.False(edit.ShowHeaders);     // the preset carries whatever it needs
        Assert.True(edit.ShowKeyField);
        Assert.Equal("Replace the OpenRouter API key", edit.Title);
    }

    /// <summary>
    /// What the narrowed form no longer shows, it still carries.
    ///
    /// <para>The draft is built from the form, so hiding the model and header rows without carrying their
    /// values would turn a key replacement into silent data loss — a model list built on the Models tab,
    /// gone because the reader pasted a new key.</para>
    /// </summary>
    [Fact]
    public void An_edit_keeps_what_the_form_no_longer_shows()
    {
        var h = new Harness();
        Save(h.Preset("openrouter").With(vm => vm.ApiKeyEntry = "sk-test"));

        var added = h.Service.Connections.Single();
        h.Service.Update(added.Id, new AiConnectionDraft(
            added.DisplayName, added.Kind, added.BaseUrl,
            new[] { new AiModelEntry("nvidia/nemotron", "Nemotron") },
            new Dictionary<string, string> { ["X-Gateway"] = "token" },
            added.Inputs, added.AuthHeaderName, added.AuthScheme));

        var edit = h.Existing("openrouter");
        edit.ApiKeyEntry = "sk-replaced";
        Save(edit);

        var after = h.Service.Connections.Single();
        Assert.Equal("nvidia/nemotron", after.Models.Single().Id);
        Assert.Equal("token", after.Headers["X-Gateway"]);
        Assert.Equal("https://openrouter.ai/api/v1", after.BaseUrl);
        Assert.Equal("sk-replaced", h.Keys.Get("openrouter", AiCredentialNames.Primary));
    }

    /// <summary>An edit updates the connection rather than adding a second one — the id is already taken, and
    /// the add path would be refused by its own collision check.</summary>
    [Fact]
    public void An_edit_updates_rather_than_adding_again()
    {
        var h = new Harness();
        Save(h.Preset("openrouter").With(vm => vm.ApiKeyEntry = "sk-test"));

        var edit = h.Existing("openrouter");
        edit.ApiKeyEntry = "sk-replaced";
        Save(edit);

        Assert.Single(h.Service.Connections);
        Assert.True(h.Closed);
    }

    /// <summary>An edit asks for the provider's inputs in the provider's own words, with the stored answer
    /// filled in — not the raw key it happens to be stored under.</summary>
    [Fact]
    public void An_edit_asks_for_inputs_in_the_presets_own_words()
    {
        var h = new Harness();
        Save(h.Preset("azure").With(vm =>
        {
            vm.Inputs.Single().Value = "acme";
            vm.ApiKeyEntry = "sk-test";
        }));

        var input = h.Existing("azure").Inputs.Single();

        Assert.Equal("acme", input.Value);
        Assert.Equal(h.Service.Presets.Single(p => p.Id == "azure").Prompts!.Single().Message, input.Message);
    }

    /// <summary>
    /// Correcting a model id does not carry the old id's "no longer listed" mark. (#728)
    ///
    /// <para>The path this exists for: the provider renames a model, the reader sees the mark, opens the
    /// editor and types the new id. Inheriting the mark would leave a corrected model still claiming to be
    /// gone — across sessions, until the Models tab was next opened and fetched.</para>
    /// </summary>
    [Fact]
    public void Retyping_a_marked_models_id_does_not_carry_the_mark()
    {
        var h = new Harness();
        h.Service.Add("mine", Draft());
        h.Service.EnableModel("mine", "old-name", "Old name", true);
        h.Service.MarkListing("mine", new[] { "new-name" });

        var vm = h.Existing("mine");
        vm.Models.Single(m => m.ModelId == "old-name").ModelId = "new-name";
        Save(vm);

        var stored = Assert.Single(h.Service.Connections.Single().Models);
        Assert.Equal("new-name", stored.Id);
        Assert.False(stored.Missing);
    }

    /// <summary>A mark survives an edit that leaves the id alone — it is the listing's word about that id,
    /// not a side effect of opening the sheet.</summary>
    [Fact]
    public void An_untouched_marked_model_keeps_its_mark_through_an_edit()
    {
        var h = new Harness();
        h.Service.Add("mine", Draft());
        h.Service.EnableModel("mine", "retired", "Retired", true);
        h.Service.MarkListing("mine", new[] { "something-else" });

        var vm = h.Existing("mine");
        vm.DisplayName = "My box, renamed";
        Save(vm);

        Assert.True(h.Service.Connections.Single().Models.Single(m => m.Id == "retired").Missing);
    }

    /// <summary>A custom endpoint keeps the whole form: nothing about it comes from a preset.</summary>
    [Fact]
    public void Editing_a_custom_endpoint_still_shows_the_full_form()
    {
        var h = new Harness();
        Save(h.Custom().With(vm =>
        {
            vm.Id = "my-box";
            vm.BaseUrl = "http://localhost:1234/v1";
        }));

        var edit = h.Existing("my-box");

        Assert.True(edit.IsFullForm);
        Assert.True(edit.ShowModels);
        Assert.True(edit.ShowHeaders);
        Assert.False(edit.IsIdEditable);   // except the id, which the credential is filed under
    }

    /// <summary>A local runner from the provider list has nothing to edit — no key, no questions — so its row
    /// offers no button rather than a sheet with a title and two buttons.</summary>
    [Fact]
    public void A_local_runner_has_nothing_to_edit()
    {
        var h = new Harness();
        Save(h.Preset("ollama"));

        Assert.Null(AiConnectionEditorViewModel.EditAction(h.Service, h.Service.Connections.Single()));
    }

    /// <summary>
    /// A plain hosted provider says "Replace key", because that is the whole sheet and the thing readers
    /// actually do — a key rotates, expires, or hits a daily cap.
    ///
    /// <para>Delete-and-re-add is not the substitute it is in OpenCode: deleting takes the reader's enabled
    /// models with it, and a re-fetched catalogue comes back all-off (#674), so rotating a key would cost
    /// them their short list.</para>
    /// </summary>
    [Fact]
    public void A_plain_hosted_provider_offers_to_replace_its_key()
    {
        var h = new Harness();
        Save(h.Preset("openrouter").With(vm => vm.ApiKeyEntry = "sk-test"));

        var connection = h.Service.Connections.Single();

        Assert.Equal("Replace key", AiConnectionEditorViewModel.EditAction(h.Service, connection));
        Assert.Equal("Replace the OpenRouter API key", h.Existing("openrouter").Title);
    }

    /// <summary>A provider that asks for something besides a key still says Edit — the sheet holds more than
    /// the key, so naming it after the key would be a lie.</summary>
    [Fact]
    public void A_provider_that_asks_for_more_than_a_key_still_says_edit()
    {
        var h = new Harness();
        Save(h.Preset("azure").With(vm =>
        {
            vm.Inputs.Single().Value = "acme";
            vm.ApiKeyEntry = "sk-test";
        }));

        Assert.Equal("Edit", AiConnectionEditorViewModel.EditAction(h.Service, h.Service.Connections.Single()));
        Assert.StartsWith("Edit", h.Existing("azure").Title);
    }

    /// <summary>A custom endpoint says Edit: every field on that form is the reader's.</summary>
    [Fact]
    public void A_custom_endpoint_says_edit()
    {
        var h = new Harness();
        Save(h.Custom().With(vm =>
        {
            vm.Id = "my-box";
            vm.BaseUrl = "http://localhost:1234/v1";
        }));

        Assert.Equal("Edit", AiConnectionEditorViewModel.EditAction(h.Service, h.Service.Connections.Single()));
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
