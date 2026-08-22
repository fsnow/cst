using System;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai.Credentials;
using CST.Avalonia.ViewModels;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// The Providers-tab control for reading keys from the login shell. (#820)
///
/// <para>Two things are being pinned. That the toggle actually reaches the service — a checkbox that changes
/// a setting and nothing else would look right and do nothing until the next launch. And that every outcome
/// of a probe produces a DIFFERENT sentence, because the whole reason for the control is that a skipped
/// shell, a timed-out profile and an environment with no keys in it were previously indistinguishable on
/// screen.</para>
/// </summary>
public sealed class ShellEnvironmentControlTests
{
    private sealed class FakeShell : IShellEnvironment
    {
        public int Primes { get; private set; }
        public int Forgets { get; private set; }
        public ShellEnvironmentStatus Status { get; set; } = ShellEnvironmentStatus.NotRun;

        public void Prime() => Primes++;
        public void Forget() { Forgets++; Probed?.Invoke(this, EventArgs.Empty); }
        public Task Completion => Task.CompletedTask;
        public string? TryRead(string variableName) => null;
        public event EventHandler? Probed;
    }

    private static (AiConnectionsViewModel Vm, FakeShell Shell, Settings Settings, Mock<ISettingsService> Service)
        Make(bool enabled = true)
    {
        var settings = new Settings();
        settings.Ai.ReadLoginShellEnvironment = enabled;
        var service = new Mock<ISettingsService>();
        service.SetupGet(s => s.Settings).Returns(settings);

        var shell = new FakeShell();
        var vm = new AiConnectionsViewModel(null, null, null, null, shell, service.Object);
        return (vm, shell, settings, service);
    }

    [Fact]
    public void Opening_the_tab_with_the_control_on_reads_the_shell()
    {
        var (_, shell, _, _) = Make(enabled: true);
        Assert.Equal(1, shell.Primes);
    }

    [Fact]
    public void Opening_the_tab_with_the_control_off_reads_nothing()
    {
        // The setting has to be honoured here as well as at startup, or the tab becomes a second way to
        // start a shell the reader has said they do not want.
        var (_, shell, _, _) = Make(enabled: false);
        Assert.Equal(0, shell.Primes);
    }

    [Fact]
    public void Unticking_releases_the_snapshot_and_is_written_down()
    {
        var (vm, shell, settings, service) = Make(enabled: true);

        vm.ReadLoginShellEnvironment = false;

        Assert.Equal(1, shell.Forgets);
        Assert.False(settings.Ai.ReadLoginShellEnvironment);
        service.Verify(s => s.RequestSave(), Times.Once);
    }

    [Fact]
    public void Re_ticking_reads_the_shell_again()
    {
        var (vm, shell, settings, _) = Make(enabled: false);

        vm.ReadLoginShellEnvironment = true;

        // Not "restores what it had" — Forget kept nothing. This is the second shell, and it is the point.
        Assert.Equal(1, shell.Primes);
        Assert.True(settings.Ai.ReadLoginShellEnvironment);
    }

    [Fact]
    public void Setting_it_to_what_it_already_is_does_nothing()
    {
        var (vm, shell, _, service) = Make(enabled: true);
        var primesAfterConstruction = shell.Primes;

        vm.ReadLoginShellEnvironment = true;

        Assert.Equal(primesAfterConstruction, shell.Primes);
        service.Verify(s => s.RequestSave(), Times.Never);
    }

    // ---- the sentences ---------------------------------------------------------------------------------

    private static string TextFor(ShellEnvironmentState state, string? shellName = "zsh", int retained = 0)
    {
        var (vm, shell, _, _) = Make(enabled: true);
        shell.Status = new ShellEnvironmentStatus(state, shellName, retained);
        return vm.ShellEnvironmentStatusText;
    }

    [Fact]
    public void Every_outcome_says_something_different()
    {
        var skipped = TextFor(ShellEnvironmentState.ShellNotSupported, "nu");
        var timedOut = TextFor(ShellEnvironmentState.TimedOut);
        var failed = TextFor(ShellEnvironmentState.Failed);
        var empty = TextFor(ShellEnvironmentState.Completed, retained: 0);
        var found = TextFor(ShellEnvironmentState.Completed, retained: 2);

        // This is the whole feature in one assertion. Before #820 all five of these produced an empty
        // section and no explanation, so a reader whose key was genuinely unreachable and a reader who
        // simply had no keys saw exactly the same screen.
        var all = new[] { skipped, timedOut, failed, empty, found };
        Assert.Equal(all.Length, new System.Collections.Generic.HashSet<string>(all).Count);
        Assert.All(all, sentence => Assert.False(string.IsNullOrWhiteSpace(sentence)));
    }

    [Fact]
    public void The_unsupported_shell_is_named_so_the_reader_can_act_on_it()
    {
        Assert.Contains("nu", TextFor(ShellEnvironmentState.ShellNotSupported, "nu"));
    }

    [Fact]
    public void One_key_is_not_reported_as_one_keys()
    {
        Assert.Contains("found 1 key.", TextFor(ShellEnvironmentState.Completed, retained: 1));
        Assert.Contains("found 3 keys.", TextFor(ShellEnvironmentState.Completed, retained: 3));
    }

    [Fact]
    public void Turning_it_off_stops_the_sentence_rather_than_leaving_a_stale_one()
    {
        var (vm, shell, _, _) = Make(enabled: true);
        shell.Status = new ShellEnvironmentStatus(ShellEnvironmentState.Completed, "zsh", 2);
        Assert.NotEqual("", vm.ShellEnvironmentStatusText);

        vm.ReadLoginShellEnvironment = false;

        // A sentence saying "found 2 keys" beside an unticked box would describe a snapshot that no longer
        // exists.
        Assert.Equal("", vm.ShellEnvironmentStatusText);
    }

    [Fact]
    public void With_no_shell_environment_at_all_the_control_is_not_offered()
    {
        var service = new Mock<ISettingsService>();
        service.SetupGet(s => s.Settings).Returns(new Settings());
        var vm = new AiConnectionsViewModel(null, null, null, null, null, service.Object);

        Assert.False(vm.ShowShellEnvironmentControl);
        Assert.Equal("", vm.ShellEnvironmentStatusText);
    }
}
