using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services.Ai.Credentials;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// Turning the login-shell lookup off and on again, and having somewhere to say what happened. (#820)
///
/// <para>The rule underneath every test here: turning it OFF releases the values rather than merely ceasing
/// to consult them. An app that kept a reader's keys in memory after being told to stop reading them would
/// obey the instruction and miss its point — and that is what forces re-enabling to run a second shell, since
/// there is deliberately nothing left to restore.</para>
/// </summary>
public sealed class ShellEnvironmentToggleTests
{
    private sealed class CountingRunner : IShellProbeRunner
    {
        private readonly Func<int, ShellProbeResult> _answer;
        public int Runs { get; private set; }

        public CountingRunner(Func<int, ShellProbeResult>? answer = null) =>
            _answer = answer ?? (_ => ShellProbeResult.Ok(
                Encoding.UTF8.GetBytes("\0OPENAI_API_KEY=sk-profile\0")));

        public ShellProbeResult Run(ShellProbeAttempt attempt) => _answer(++Runs);
    }

    private static ShellEnvironment Probe(
        IShellProbeRunner runner, bool supported = true, string shell = "/bin/zsh",
        params string[] wanted)
    {
        var names = wanted.Length > 0 ? wanted : new[] { "OPENAI_API_KEY" };
        return new ShellEnvironment(
            _ => Task.FromResult<IReadOnlyCollection<string>>(names),
            runner, logger: null, supported: supported, shellPath: shell);
    }

    [Fact]
    public async Task Forgetting_releases_what_the_probe_found()
    {
        var shell = Probe(new CountingRunner());
        shell.Prime();
        await shell.Completion;
        Assert.Equal("sk-profile", shell.TryRead("OPENAI_API_KEY"));

        shell.Forget();

        // Not "stops being offered while staying in memory". The reader said stop reading my environment.
        Assert.Null(shell.TryRead("OPENAI_API_KEY"));
        Assert.Equal(ShellEnvironmentState.NotRun, shell.Status.State);
    }

    [Fact]
    public async Task Re_enabling_reads_the_shell_again_rather_than_restoring_a_cache()
    {
        var runner = new CountingRunner();
        var shell = Probe(runner);

        shell.Prime();
        await shell.Completion;
        Assert.Equal(1, runner.Runs);

        shell.Forget();
        shell.Prime();
        await shell.Completion;

        // Two shells, because Forget kept nothing to restore. That is the cost of the guarantee above, and
        // it also means a reader who has just edited ~/.zshrc can pick it up without relaunching.
        Assert.Equal(2, runner.Runs);
        Assert.Equal("sk-profile", shell.TryRead("OPENAI_API_KEY"));
    }

    [Fact]
    public async Task A_second_generation_can_find_something_the_first_did_not()
    {
        // The re-read has to be a real one: a generation that returned the previous answer would make the
        // toggle useless to the reader who fixed their profile and toggled to pick it up.
        var runner = new CountingRunner(run => run == 1
            ? ShellProbeResult.Ok(Encoding.UTF8.GetBytes("\0IRRELEVANT=x\0"))
            : ShellProbeResult.Ok(Encoding.UTF8.GetBytes("\0OPENAI_API_KEY=sk-added\0")));
        var shell = Probe(runner);

        shell.Prime();
        await shell.Completion;
        Assert.Null(shell.TryRead("OPENAI_API_KEY"));

        shell.Forget();
        shell.Prime();
        await shell.Completion;

        Assert.Equal("sk-added", shell.TryRead("OPENAI_API_KEY"));
    }

    [Fact]
    public async Task Priming_twice_without_forgetting_still_runs_one_shell()
    {
        // The collapse guarantee has to survive becoming a generation — it is what stops the startup gate and
        // the Providers tab from running two shells between them.
        var runner = new CountingRunner();
        var shell = Probe(runner);

        shell.Prime();
        shell.Prime();
        await shell.Completion;
        shell.Prime();

        Assert.Equal(1, runner.Runs);
    }

    [Fact]
    public void Forgetting_tells_the_surfaces_that_showed_the_rows()
    {
        var shell = Probe(new CountingRunner());
        var told = 0;
        shell.Probed += (_, _) => told++;

        shell.Forget();

        // Same signal that told them to start showing rows. Without it a tab keeps offering keys from a
        // snapshot that no longer exists, and adopting one would fail with no explanation.
        Assert.Equal(1, told);
    }

    // ---- the states that had nowhere to be reported --------------------------------------------------

    [Fact]
    public void Nothing_asked_for_yet_is_its_own_state()
    {
        Assert.Equal(ShellEnvironmentState.NotRun, Probe(new CountingRunner()).Status.State);
    }

    [Fact]
    public void Windows_reports_unsupported_rather_than_not_run()
    {
        var shell = Probe(new CountingRunner(), supported: false);
        shell.Prime();
        Assert.Equal(ShellEnvironmentState.Unsupported, shell.Status.State);
    }

    [Fact]
    public async Task A_shell_skipped_by_name_says_so_and_names_it()
    {
        var shell = Probe(new CountingRunner(), shell: "/usr/local/bin/nu");
        shell.Prime();
        await shell.Completion;

        // The one failure a reader can act on, so it must be distinguishable from "you have no keys".
        Assert.Equal(ShellEnvironmentState.ShellNotSupported, shell.Status.State);
        Assert.Equal("nu", shell.Status.ShellName);
    }

    [Fact]
    public async Task A_timeout_and_an_empty_environment_are_different_states()
    {
        var timedOut = Probe(new CountingRunner(_ => ShellProbeResult.Timeout()));
        timedOut.Prime();
        await timedOut.Completion;

        var empty = Probe(new CountingRunner(_ => ShellProbeResult.Ok(
            Encoding.UTF8.GetBytes("\0IRRELEVANT=x\0"))));
        empty.Prime();
        await empty.Completion;

        // Both leave the found-keys section empty, and before this they were indistinguishable on screen —
        // which is #817's own complaint one layer up. One means "fix your profile", the other means
        // "there is nothing here to find".
        Assert.Equal(ShellEnvironmentState.TimedOut, timedOut.Status.State);
        Assert.Equal(ShellEnvironmentState.Completed, empty.Status.State);
        Assert.Equal(0, empty.Status.RetainedCount);
    }

    [Fact]
    public async Task A_successful_probe_reports_how_many_it_kept_and_never_which()
    {
        var shell = Probe(
            new CountingRunner(_ => ShellProbeResult.Ok(
                Encoding.UTF8.GetBytes("\0OPENAI_API_KEY=sk\0GROQ_API_KEY=gsk\0APPLE_APP_PASSWORD=abcd\0"))),
            wanted: new[] { "OPENAI_API_KEY", "GROQ_API_KEY" });

        shell.Prime();
        await shell.Completion;

        Assert.Equal(ShellEnvironmentState.Completed, shell.Status.State);
        Assert.Equal(2, shell.Status.RetainedCount);

        // The status is rendered on screen and written to a log. A variable name in it would be the first
        // place this feature leaked one.
        var rendered = shell.Status.ToString();
        Assert.DoesNotContain("OPENAI_API_KEY", rendered);
        Assert.DoesNotContain("APPLE_APP_PASSWORD", rendered);
    }

    // ---- the gate ------------------------------------------------------------------------------------

    [Fact]
    public void The_readers_switch_turns_the_startup_probe_off()
    {
        var settings = new Settings();
        settings.Ai.Enabled = true;
        Assert.True(CST.Avalonia.App.ShouldPrimeShellEnvironment(settings));

        settings.Ai.ReadLoginShellEnvironment = false;
        Assert.False(CST.Avalonia.App.ShouldPrimeShellEnvironment(settings));
    }

    [Fact]
    public void The_switch_defaults_on_because_defaulting_off_would_rebuild_the_bug()
    {
        // The reader this serves does not know their key is invisible — that IS #817. Requiring them to find
        // a checkbox in order to discover a problem they cannot see puts the bug back with an extra step.
        Assert.True(new Settings().Ai.ReadLoginShellEnvironment);
    }
}
