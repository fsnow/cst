using System.Runtime.InteropServices;
using CST.Avalonia.ViewModels;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// The About box's build identification (#746).
///
/// <para>Beta 6 ships four builds across two operating systems, and the whole point of the version block is
/// that a tester can answer "which one are you running?" without guessing. So these tests are about the
/// answer being <i>right</i> and <i>honest</i>: the version comes from the assembly, an unrecorded commit
/// stays absent rather than becoming a plausible-looking placeholder, and an emulated architecture says so.
/// </para>
/// </summary>
public class AboutViewModelTests
{
    [Fact]
    public void Version_drops_the_build_metadata()
    {
        // What the SDK stamps on a git build. The '+sha' is not part of the version anyone quotes.
        var vm = new AboutViewModel("5.0.0-beta.6+58e08bb3317de502d38ab98a49af76d41bf8116d",
            "macOS 26.5.1", Architecture.Arm64, Architecture.Arm64, ".NET 10.0.9");

        Assert.Equal("5.0.0-beta.6", vm.Version);
        Assert.Equal("58e08bb", vm.Commit);
        Assert.True(vm.HasCommit);
    }

    [Fact]
    public void A_build_with_no_recorded_commit_shows_none()
    {
        // A source-archive build has no git metadata. Showing nothing is the honest answer; a placeholder
        // would read as a real commit and send someone looking for it.
        var vm = new AboutViewModel("5.0.0-beta.6", "macOS 26.5.1",
            Architecture.Arm64, Architecture.Arm64, ".NET 10.0.9");

        Assert.Equal("5.0.0-beta.6", vm.Version);
        Assert.Null(vm.Commit);
        Assert.False(vm.HasCommit);
    }

    [Fact]
    public void A_missing_version_says_unknown_rather_than_a_literal()
    {
        // No hardcoded "5.0.0-beta.6" fallback anywhere in the About box: a literal would be one more string
        // to remember at release time, and a stale one would misidentify the build it was meant to identify.
        var vm = new AboutViewModel(null, "macOS 26.5.1",
            Architecture.Arm64, Architecture.Arm64, ".NET 10.0.9");

        Assert.Equal("unknown", vm.Version);
    }

    [Fact]
    public void Platform_names_the_os_and_the_architecture()
    {
        var vm = new AboutViewModel("5.0.0-beta.6", "Microsoft Windows 10.0.26100",
            Architecture.X64, Architecture.X64, ".NET 10.0.9");

        Assert.Equal("Microsoft Windows 10.0.26100 · x64", vm.Platform);
    }

    [Fact]
    public void An_emulated_process_names_both_architectures()
    {
        // The x64 download under Rosetta on an Apple Silicon Mac. Reporting only one of the two sends the
        // investigation to the wrong build — which is exactly the mistake this box exists to prevent.
        Assert.Equal("x64 on arm64", AboutViewModel.DescribeArchitecture(Architecture.X64, Architecture.Arm64));
        Assert.Equal("arm64", AboutViewModel.DescribeArchitecture(Architecture.Arm64, Architecture.Arm64));
    }

    [Theory]
    [InlineData(null, null)]
    [InlineData("", null)]
    [InlineData("5.0.0-beta.6", null)]
    // A '+' with nothing usable after it: shorter than an abbreviated sha, so there is nothing to show.
    [InlineData("5.0.0-beta.6+", null)]
    [InlineData("5.0.0-beta.6+abc12", null)]
    [InlineData("5.0.0-beta.6+abcdef1", "abcdef1")]
    public void ShortCommit_only_answers_when_there_is_a_commit(string? informational, string? expected)
    {
        Assert.Equal(expected, AboutViewModel.ShortCommit(informational));
    }

    [Fact]
    public void BuildSummary_is_one_pasteable_line()
    {
        // The copy button's payload. It has to stand alone in a bug report, so it carries the app name too.
        var vm = new AboutViewModel("5.0.0-beta.6+58e08bb3317de502d38ab98a49af76d41bf8116d",
            "macOS 26.5.1", Architecture.Arm64, Architecture.Arm64, ".NET 10.0.9");

        Assert.Equal("CST Reader 5.0.0-beta.6 (58e08bb) · macOS 26.5.1 · arm64 · .NET 10.0.9", vm.BuildSummary);
    }

    [Fact]
    public void The_default_constructor_reads_this_build()
    {
        // The constructor the window actually uses. It reaches for the assembly and RuntimeInformation and
        // nothing else — no service provider, no settings — so it still works when the app is in the state
        // that made someone open the About box in the first place.
        var vm = new AboutViewModel();

        Assert.NotEqual("unknown", vm.Version);
        Assert.DoesNotContain("+", vm.Version);
        Assert.NotEmpty(vm.Platform);
        Assert.NotEmpty(vm.Runtime);
    }

    [Fact]
    public void VRI_is_credited_first()
    {
        // #746 is explicit: the app is a reader for VRI's corpus, and that belongs at the top rather than
        // among the libraries.
        var vm = new AboutViewModel();

        Assert.Equal("Vipassana Research Institute", vm.Credits[0].Name);
    }
}
