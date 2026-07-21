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
