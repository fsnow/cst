using CST.Avalonia.ViewModels;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels
{
    public class WelcomeViewModelTests
    {
        [Fact]
        public void SetStartupStatus_AfterCompleteStartup_IsIgnored()
        {
            // #38: status updates and CompleteStartup are posted to the UI thread at different priorities,
            // so a "Checking…" update can run AFTER CompleteStartup and re-show the banner permanently.
            // Once startup is complete, late updates must be ignored.
            var vm = new WelcomeViewModel();

            vm.SetStartupStatus("Checking search index...");
            Assert.True(vm.IsStartupInProgress);
            Assert.Equal("Checking search index...", vm.StartupStatusMessage);

            vm.CompleteStartup();
            Assert.False(vm.IsStartupInProgress);
            Assert.Equal("", vm.StartupStatusMessage);

            // The race: a late status update arriving after completion must NOT re-show the banner.
            vm.SetStartupStatus("Checking search index...");
            Assert.False(vm.IsStartupInProgress);
            Assert.Equal("", vm.StartupStatusMessage);
        }

        [Fact]
        public void SetStartupStatus_BeforeComplete_ShowsBanner()
        {
            // Normal pre-completion updates still work.
            var vm = new WelcomeViewModel();

            vm.SetStartupStatus("Loading settings...");
            Assert.True(vm.IsStartupInProgress);
            Assert.Equal("Loading settings...", vm.StartupStatusMessage);

            vm.SetStartupStatus("Pre-loading fonts...");
            Assert.True(vm.IsStartupInProgress);
            Assert.Equal("Pre-loading fonts...", vm.StartupStatusMessage);
        }
    }
}
