using System;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.ViewModels;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels
{
    /// <summary>
    /// The AI settings view-model gating, focused on the case that had no coverage and a latent bug: an
    /// MCP-only configuration. navigate is offered over BOTH the REST and MCP surfaces, so "allow remote
    /// control" must be reachable whenever EITHER runs — keying it to the REST flag alone would grey it out for
    /// an MCP-only user whose navigate works fine, telling them to enable a box already ticked. (#280, #440)
    /// </summary>
    public class AiSettingsViewModelTests
    {
        private static (AiSettingsViewModel Vm, Settings Settings) Make()
        {
            var settings = new Settings();
            var svc = new Mock<ISettingsService>();
            svc.SetupGet(s => s.Settings).Returns(settings);
            return (new AiSettingsViewModel(svc.Object), settings);
        }

        [Fact]
        public void The_two_server_surfaces_are_independent()
        {
            var (vm, settings) = Make();

            vm.LocalApiEnabled = false;
            vm.McpEnabled = true;

            // Turning off REST must NOT turn off MCP — the #318 workaround that coupled them is gone.
            Assert.False(settings.Ai.LocalApi.Enabled);
            Assert.True(settings.Ai.LocalApi.EnableMcpServer);
        }

        /// <summary>
        /// An in-memory stand-in for the OS credential store. The real one talks to the Keychain, which a
        /// test must never touch — running the suite is not an acceptable way to lose a developer's key.
        /// </summary>
        private sealed class FakeCredentialStore : CST.Avalonia.Services.Ai.IAiCredentialStore
        {
            private readonly System.Collections.Generic.Dictionary<CST.Avalonia.Services.Ai.ChatProviderKind, string> _keys = new();

            public bool Available { get; set; } = true;
            public bool IsAvailable => Available;
            public string? Unavailable => Available ? null : "No secure storage in this build.";

            public string? GetApiKey(CST.Avalonia.Services.Ai.ChatProviderKind provider) =>
                Available && _keys.TryGetValue(provider, out var k) ? k : null;

            public bool SetApiKey(CST.Avalonia.Services.Ai.ChatProviderKind provider, string apiKey)
            {
                if (!Available) return false;
                _keys[provider] = apiKey;
                return true;
            }

            public bool DeleteApiKey(CST.Avalonia.Services.Ai.ChatProviderKind provider)
            {
                _keys.Remove(provider);
                return true;
            }
        }

        private static (AiSettingsViewModel Vm, Settings Settings, FakeCredentialStore Keys) MakeWithAssistant()
        {
            var settings = new Settings();
            var svc = new Mock<ISettingsService>();
            svc.SetupGet(s => s.Settings).Returns(settings);
            var keys = new FakeCredentialStore();
            return (new AiSettingsViewModel(svc.Object, keys, null, null), settings, keys);
        }

        // ---- The assistant (#585) ------------------------------------------------------------------

        [Fact]
        public void The_API_key_is_never_written_to_settings()
        {
            var (vm, settings, keys) = MakeWithAssistant();

            vm.ApiKeyEntry = "sk-ant-secret-value";
            vm.SaveApiKeyCommand.Execute().Subscribe();

            // The whole reason #579 exists. settings.json is hand-edited, screenshotted and attached to bug
            // reports; a key that reaches it has reached all three. Serialized in full so no field is missed.
            var json = System.Text.Json.JsonSerializer.Serialize(settings);
            Assert.DoesNotContain("sk-ant-secret-value", json);
            Assert.Equal("sk-ant-secret-value", keys.GetApiKey(CST.Avalonia.Services.Ai.ChatProviderKind.Anthropic));
        }

        [Fact]
        public void The_entry_box_is_cleared_once_the_key_is_stored()
        {
            var (vm, _, _) = MakeWithAssistant();

            vm.ApiKeyEntry = "sk-ant-secret-value";
            vm.SaveApiKeyCommand.Execute().Subscribe();

            // It exists to hand the key over, not to hold it: a key left sitting in a control is one
            // screenshot away from being published.
            Assert.Equal("", vm.ApiKeyEntry);
        }

        [Fact]
        public void Keys_are_kept_per_provider()
        {
            var (vm, _, keys) = MakeWithAssistant();

            vm.SelectedProvider = vm.ProviderChoices[0];
            vm.ApiKeyEntry = "claude-key";
            vm.SaveApiKeyCommand.Execute().Subscribe();

            vm.SelectedProvider = vm.ProviderChoices[1];
            vm.ApiKeyEntry = "other-key";
            vm.SaveApiKeyCommand.Execute().Subscribe();

            // The ordinary case for someone comparing a hosted model against a local one — storing the
            // second must not silently replace the first.
            Assert.Equal("claude-key", keys.GetApiKey(CST.Avalonia.Services.Ai.ChatProviderKind.Anthropic));
            Assert.Equal("other-key", keys.GetApiKey(CST.Avalonia.Services.Ai.ChatProviderKind.OpenAiCompatible));
        }

        [Fact]
        public void When_storage_is_unavailable_the_reason_is_shown_rather_than_advice_that_cannot_help()
        {
            var (vm, _, keys) = MakeWithAssistant();
            keys.Available = false;

            vm.SelectedProvider = vm.ProviderChoices[0];

            // "Add a key in Settings" is the wrong instruction when this build cannot store one — it sends
            // the user to the screen they are already on. The store knows why; the UI repeats it.
            Assert.False(vm.CanStoreKeys);
            Assert.Contains("No secure storage", vm.KeyStatus);
        }

        [Fact]
        public void Choosing_a_provider_records_the_string_the_resolver_parses()
        {
            var (vm, settings, _) = MakeWithAssistant();

            vm.SelectedProvider = vm.ProviderChoices[1];

            // The stored value has to be one ChatProviderResolver.TryParseKind accepts, or the UI would
            // configure a provider the app then reports as unknown.
            Assert.True(CST.Avalonia.Services.Ai.ChatProviderResolver.TryParseKind(
                settings.Ai.Chat.Provider, out var kind));
            Assert.Equal(CST.Avalonia.Services.Ai.ChatProviderKind.OpenAiCompatible, kind);
        }

        [Fact]
        public void Every_offered_provider_round_trips_through_the_resolver()
        {
            var (vm, settings, _) = MakeWithAssistant();

            // Pins the whole list, not just the one a test happened to pick: adding a third choice with a
            // spelling the resolver does not know would be invisible until a user selected it.
            foreach (var choice in vm.ProviderChoices)
            {
                vm.SelectedProvider = choice;
                Assert.True(CST.Avalonia.Services.Ai.ChatProviderResolver.TryParseKind(
                    settings.Ai.Chat.Provider, out var kind), $"unparseable: {settings.Ai.Chat.Provider}");
                Assert.Equal(choice.Kind, kind);
            }
        }

        [Fact]
        public void The_model_is_stored_verbatim_and_blank_means_unset()
        {
            var (vm, settings, _) = MakeWithAssistant();

            vm.Model = "  claude-sonnet-4-5  ";
            Assert.Equal("claude-sonnet-4-5", settings.Ai.Chat.Model);

            vm.Model = "   ";
            // Null rather than whitespace: the resolver tests IsNullOrWhiteSpace, but a stored "   " would
            // read as configured to anything that only checks for null.
            Assert.Null(settings.Ai.Chat.Model);
        }

        [Fact]
        public void An_empty_answer_language_falls_back_rather_than_asking_for_nothing()
        {
            var (vm, settings, _) = MakeWithAssistant();

            vm.AnswerLanguage = "   ";

            // "Translate into ''" is not a request a model can satisfy; the bundle requires a language.
            Assert.Equal("English", settings.Ai.Chat.AnswerLanguage);
        }

        [Fact]
        public void Remote_control_is_reachable_with_only_the_MCP_surface_on()
        {
            var (vm, _) = Make();
            vm.AiEnabled = true;

            vm.LocalApiEnabled = false;
            vm.McpEnabled = false;
            Assert.False(vm.RemoteControlEnabled);   // no surface running → not reachable

            vm.McpEnabled = true;
            Assert.True(vm.RemoteControlEnabled);    // MCP alone is enough (#440)
        }

        [Fact]
        public void Remote_control_needs_the_master_switch_regardless_of_surface()
        {
            var (vm, _) = Make();
            vm.LocalApiEnabled = true;
            vm.McpEnabled = true;

            vm.AiEnabled = false;
            Assert.False(vm.RemoteControlEnabled);   // master off overrides everything
        }

        [Fact]
        public void Toggling_either_surface_updates_remote_control_reachability()
        {
            var (vm, _) = Make();
            vm.AiEnabled = true;
            vm.McpEnabled = false;

            bool raised = false;
            vm.PropertyChanged += (_, e) =>
            {
                if (e.PropertyName == nameof(AiSettingsViewModel.RemoteControlEnabled)) raised = true;
            };

            vm.LocalApiEnabled = true;
            Assert.True(raised);   // the REST toggle must re-notify the remote-control gate, not just the MCP one
        }
    }
}
