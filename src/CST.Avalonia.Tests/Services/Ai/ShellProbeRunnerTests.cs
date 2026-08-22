using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Text;
using CST.Avalonia.Services.Ai.Credentials;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// The one part of the probe that spawns a real process, tested against a real one. (#817)
///
/// <para>Everything else in this feature runs behind <see cref="IShellProbeRunner"/> precisely so it can be
/// tested without a shell. That leaves the runner itself uncovered, and it is where the interesting failures
/// live — pipes, exit codes, and children that outlive their parent. These use <c>/bin/sh</c> with a fixed
/// script rather than the reader's login shell, so they test this code and not somebody's dotfiles.</para>
/// </summary>
public sealed class ShellProbeRunnerTests
{
    private static readonly TimeSpan Budget = TimeSpan.FromSeconds(5);

    private static ShellProbeResult Run(string script) =>
        new ProcessShellProbeRunner().Run(
            new ShellProbeAttempt("/bin/sh", new[] { "-c", script }, Budget));

    private static IReadOnlyDictionary<string, string> Parse(ShellProbeResult result, params string[] keep) =>
        ShellEnvParser.Parse(result.Stdout, new HashSet<string>(keep, StringComparer.Ordinal));

    [UnixFact]
    public void A_real_shell_answers_with_its_environment()
    {
        var result = Run(ShellEnvironment.ProbeCommand);

        Assert.True(result.Succeeded);
        Assert.NotEmpty(Parse(result, "PATH", "HOME"));
    }

    [UnixFact]
    public void The_sentinel_saves_the_first_variable_from_a_chatty_profile()
    {
        // Both halves come from a REAL shell, so this compares what actually arrives rather than what a
        // hand-written stream is imagined to look like — the mistake in the first version of these tests.
        var withSentinel = Parse(Run("echo 'Now using node v20.11.0'; " + ShellEnvironment.ProbeCommand),
            AllNames(Run(ShellEnvironment.ProbeCommand)));
        var glued = Parse(Run("echo 'Now using node v20.11.0'; env -0"),
            AllNames(Run(ShellEnvironment.ProbeCommand)));

        // Without the sentinel the chatter is glued to the first assignment and that variable is lost; with
        // it, nothing is. This is the regression test for a bug that would have shipped as "one user's key is
        // invisible and nobody can reproduce it". (fable)
        Assert.Equal(withSentinel.Count - 1, glued.Count);
        Assert.True(withSentinel.Count > 0);
    }

    private static string[] AllNames(ShellProbeResult clean) =>
        Encoding.UTF8.GetString(clean.Stdout)
            .Split('\0')
            .Select(chunk => chunk.Split('=')[0])
            .Where(name => name.Length > 0 && (char.IsAsciiLetter(name[0]) || name[0] == '_'))
            .Distinct(StringComparer.Ordinal)
            .ToArray();

    [UnixFact]
    public void A_background_job_holding_the_pipe_does_not_turn_a_success_into_a_timeout()
    {
        var watch = Stopwatch.StartNew();

        // The shell exits immediately; the `sleep` inherits stdout and holds the write end open for 30
        // seconds. Waiting for end-of-stream here would block on a probe whose output is already in the
        // buffer — reporting a SUCCESS as a timeout, which the ladder treats as "give up permanently", and
        // parking a thread on the raw environment for the life of the app. (fable)
        var result = Run(ShellEnvironment.ProbeCommand + "; sleep 30 &");
        watch.Stop();

        Assert.True(result.Succeeded);
        Assert.False(result.TimedOut);
        Assert.NotEmpty(Parse(result, "PATH", "HOME"));
        Assert.True(watch.Elapsed < Budget, $"took {watch.ElapsedMilliseconds}ms, so it waited for EOF");
    }

    [UnixFact]
    public void A_shell_that_hangs_is_killed_and_reported_as_a_timeout()
    {
        var watch = Stopwatch.StartNew();
        var result = new ProcessShellProbeRunner().Run(
            new ShellProbeAttempt("/bin/sh", new[] { "-c", "sleep 60" }, TimeSpan.FromMilliseconds(400)));
        watch.Stop();

        Assert.True(result.TimedOut);
        Assert.False(result.Succeeded);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5));
    }

    [UnixFact]
    public void A_nonzero_exit_is_a_failure_rather_than_an_empty_success()
    {
        // The difference matters to the ladder: a failure is retried as a plain login shell, and a success
        // with no variables would end the probe having learned nothing.
        var result = Run("exit 3");

        Assert.False(result.Succeeded);
        Assert.False(result.TimedOut);
    }

    [UnixFact]
    public void A_shell_that_does_not_exist_is_a_failure_rather_than_a_crash()
    {
        var result = new ProcessShellProbeRunner().Run(
            new ShellProbeAttempt("/nonexistent/shell.invalid", new[] { "-c", "env -0" }, Budget));

        Assert.False(result.Succeeded);
    }

    [UnixFact]
    public void Output_below_the_cap_is_returned_whole()
    {
        // A megabyte is far more than any real environment and well under the cap, so this is the control
        // for the test below: large output on its own is not what gets refused.
        var result = Run("head -c 1000000 /dev/zero");

        Assert.True(result.Succeeded);
        Assert.Equal(1000000, result.Stdout.Length);
    }

    [UnixFact]
    public void A_runaway_profile_is_refused_rather_than_buffered()
    {
        var watch = Stopwatch.StartNew();

        // Eight megabytes, twice the cap. The reader stops at the cap, the shell blocks on the write it
        // cannot finish, and the attempt ends as a timeout — which the ladder treats as give up, and
        // degrading to the process environment is the right answer for a profile behaving like this.
        //
        // The first version of this test asserted `Stdout.Length <= MaxStdoutBytes` and could not fail:
        // removing the cap sends the run down the timeout path, where Stdout is empty and the assertion holds
        // anyway. Asserting on the OUTCOME is what makes it a real test.
        var result = new ProcessShellProbeRunner().Run(
            new ShellProbeAttempt("/bin/sh", new[] { "-c", "head -c 8000000 /dev/zero" },
                TimeSpan.FromSeconds(2)));
        watch.Stop();

        Assert.False(result.Succeeded);
        Assert.True(result.Stdout.Length <= ProcessShellProbeRunner.MaxStdoutBytes);
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10));
    }

    /// <summary>
    /// A shell that printed something and then failed is still a failure. (#824, review)
    ///
    /// <para>Found by adversarial review of the first version of the #824 fix, which snapshotted the exit at
    /// the moment either signal arrived. End-of-stream wins that race almost always — including with nothing
    /// interfering — so the exit code was discarded on nearly every probe and this returned success. The
    /// ladder above then took the empty parse as an answer and never retried with <c>-l</c>, which is the
    /// rung that finds keys in a profile the interactive shell bailed out of.</para>
    /// </summary>
    [UnixFact]
    public void A_shell_that_printed_and_then_failed_is_not_a_success()
    {
        var result = Run("echo 'restricted account, contact IT'; exit 3");

        Assert.False(result.Succeeded);
    }

    /// <summary>
    /// The sentinel on its own is not an environment. (#824, review)
    ///
    /// <para>The shape of a profile that mangles PATH until <c>env</c> is no longer findable: the probe
    /// writes its NUL and nothing follows. One byte passed the first version's length test, and reached the
    /// reader as a shell that exports nothing we recognise rather than a shell we failed to read.</para>
    /// </summary>
    [UnixFact]
    public void The_sentinel_alone_is_not_an_environment()
    {
        var result = Run("printf '\\0'; exit 127");

        Assert.False(result.Succeeded);
    }

    /// <summary>
    /// A shell that wrote a COMPLETE payload and then failed is still a failure. (#824, review)
    ///
    /// <para>This is the one that needs the exit-observation grace, and the reason the grace exists as
    /// something separate from the well-formedness check. A banner-and-exit-3 shell is caught by the payload
    /// not ending where <c>env -0</c> would end it; this one's payload is impeccable, so the only thing left
    /// to object with is the exit code — and end-of-stream beats the runtime's notice of the exit almost
    /// every time, so without a moment's grace there would be no exit code to object with.</para>
    ///
    /// <para>Confirmed by mutation: removing the grace makes this the only test that fails.</para>
    /// </summary>
    [UnixFact]
    public void A_complete_payload_from_a_failing_shell_is_still_refused()
    {
        var result = Run("printf 'A=1\\0'; exit 3");

        Assert.False(result.Succeeded);
    }
}
