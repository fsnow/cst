using System;
using System.Linq;
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
            private readonly System.Collections.Generic.Dictionary<string, string> _keys = new();

            public bool Available { get; set; } = true;
            public bool IsAvailable => Available;
            public string? Unavailable => Available ? null : "No secure storage in this build.";

            private static string Account(string connectionId, string name) => connectionId + ":" + name;

            public string? Get(string connectionId, string name) => Read(connectionId, name).Secret;

            public CST.Avalonia.Services.Ai.Credentials.CredentialRead Read(string connectionId, string name) =>
                !Available ? CST.Avalonia.Services.Ai.Credentials.CredentialRead.Unavailable
                : _keys.TryGetValue(Account(connectionId, name), out var k)
                    ? CST.Avalonia.Services.Ai.Credentials.CredentialRead.Found(k)
                    : CST.Avalonia.Services.Ai.Credentials.CredentialRead.NotStored;

            public bool Set(string connectionId, string name, string secret)
            {
                if (!Available) return false;
                _keys[Account(connectionId, name)] = secret;
                return true;
            }

            public bool Delete(string connectionId, string name)
            {
                // Mirrors the real store, which refuses rather than reports success when there is nowhere to
                // delete from - a fake that is more forgiving than the thing it stands for is how a bug gets
                // through green. (fable review)
                if (!Available) return false;
                _keys.Remove(Account(connectionId, name));
                return true;
            }
        }

        private static (AiSettingsViewModel Vm, Settings Settings, FakeCredentialStore Keys) MakeWithAssistant()
        {
            var settings = new Settings();
            var svc = new Mock<ISettingsService>();
            svc.SetupGet(s => s.Settings).Returns(settings);
            var keys = new FakeCredentialStore();
            return (new AiSettingsViewModel(svc.Object, keys, null), settings, keys);
        }

        // ---- The assistant (#585) ------------------------------------------------------------------





        /// <summary>The connection the single-provider fields edit. #689 made the model plural; these fields
        /// now write to the active connection rather than to scalar settings.</summary>
        private static CST.Avalonia.Models.AiConnectionRecord Active(CST.Avalonia.Models.Settings settings) =>
            settings.Ai.Chat.Connections.FirstOrDefault(
                c => c.Id == settings.Ai.Chat.ActiveConnectionId) ?? settings.Ai.Chat.Connections.First();






        // Eight tests were removed here with the single-provider Settings UI they exercised - a provider
        // dropdown, a base-URL box, a model box and one shared API-key box, all replaced by the Providers tab
        // (#691/#692/#693). Their invariants did not go with them:
        //   - keys kept per connection      -> AiConnectionServiceTests.Two_openai_compatible_endpoints_keep_separate_keys
        //   - the key never reaching settings, and the entry box being cleared
        //                                   -> AiConnectionEditorViewModelTests (the editor now owns key entry)
        //   - provider strings the resolver can parse
        //                                   -> ChatProviderResolverTests

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

        // ---- the connection tabs follow the assistant (#795) -------------------------------------------

        /// <summary>
        /// Providers and Models configure connections and model lists, and the assistant is their only
        /// consumer — nothing in the local API or MCP surfaces resolves a provider. With it off they are two
        /// tabs of settings for a feature that is not running.
        /// </summary>
        [Fact]
        public void The_connection_tabs_are_shown_only_while_the_assistant_is_on()
        {
            var (vm, _) = Make();

            vm.AiEnabled = true;
            vm.ChatEnabled = true;
            Assert.True(vm.ShowConnectionTabs);

            vm.ChatEnabled = false;
            Assert.False(vm.ShowConnectionTabs);
        }

        /// <summary>The master switch takes them too — everything under it is off when it is.</summary>
        [Fact]
        public void The_master_switch_hides_the_connection_tabs_as_well()
        {
            var (vm, _) = Make();
            vm.AiEnabled = true;
            vm.ChatEnabled = true;

            vm.AiEnabled = false;

            Assert.False(vm.ShowConnectionTabs);
        }

        /// <summary>
        /// The selection cannot be left pointing at a tab that is no longer there: a reader on Models who
        /// unticks the assistant would otherwise be looking at a blank pane with no tab header selected.
        /// </summary>
        [Fact]
        public void Hiding_the_tabs_moves_the_selection_off_them()
        {
            var (vm, _) = Make();
            vm.AiEnabled = true;
            vm.ChatEnabled = true;
            vm.SelectedTab = 2;              // Models

            vm.ChatEnabled = false;

            Assert.Equal(0, vm.SelectedTab);
        }

        /// <summary>
        /// HIDDEN, never cleared. Every connection, model list and stored key survives the assistant being
        /// switched off and comes back untouched — tidying up on the way past is how a settings file loses
        /// work that took a reader an evening to build. (#784 is the fresh reminder.)
        /// </summary>
        [Fact]
        public void Turning_the_assistant_off_preserves_the_providers_and_models()
        {
            var (vm, settings, keys) = MakeWithAssistant();

            // A configured provider with a hand-built model list, which is the work at stake.
            settings.Ai.Chat.Connections.Add(new CST.Avalonia.Models.AiConnectionRecord
            {
                Id = "groq",
                DisplayName = "Groq",
                BaseUrl = "https://api.groq.com/openai/v1",
                Models =
                {
                    new CST.Avalonia.Models.AiModelRecord { Id = "openai/gpt-oss-120b", Enabled = true },
                    new CST.Avalonia.Models.AiModelRecord { Id = "qwen/qwen3.6-27b", Enabled = true },
                },
            });
            keys.Set("groq", CST.Avalonia.Services.Ai.AiCredentialNames.Primary, "gsk-test");

            vm.AiEnabled = true;
            vm.ChatEnabled = true;

            vm.ChatEnabled = false;

            var after = settings.Ai.Chat.Connections.Single();
            Assert.Equal("groq", after.Id);
            Assert.Equal(
                new[] { "openai/gpt-oss-120b", "qwen/qwen3.6-27b" },
                after.Models.Select(m => m.Id));
            Assert.All(after.Models, m => Assert.True(m.Enabled));
            Assert.Equal("gsk-test", keys.Get("groq", CST.Avalonia.Services.Ai.AiCredentialNames.Primary));
        }
    }
}
