using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.Services.Ai.Credentials;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// Reading the variables a login shell exports, for the GUI launch that cannot see them. (#817)
///
/// <para>Nothing here spawns a shell. The probe runner is a seam precisely so that this suite does not depend
/// on which shell the machine running it uses, what that shell's profile does, or how long it takes — a test
/// that ran a real login shell would be measuring the developer's dotfiles.</para>
/// </summary>
public sealed class ShellEnvironmentTests
{
    /// <summary>A runner that answers from a script rather than a process, and counts what it was asked.</summary>
    private sealed class FakeRunner : IShellProbeRunner
    {
        private readonly Queue<ShellProbeResult> _answers;
        public List<ShellProbeAttempt> Attempts { get; } = new();

        public FakeRunner(params ShellProbeResult[] answers) => _answers = new Queue<ShellProbeResult>(answers);

        public ShellProbeResult Run(ShellProbeAttempt attempt)
        {
            Attempts.Add(attempt);
            return _answers.Count > 0 ? _answers.Dequeue() : ShellProbeResult.Failed();
        }
    }

    private static byte[] NulStream(params string[] assignments) =>
        Encoding.UTF8.GetBytes(string.Join("\0", assignments) + "\0");

    private static ShellEnvironment Probe(
        FakeRunner runner, string shell = "/bin/zsh", bool supported = true, params string[] wanted)
    {
        var names = wanted.Length > 0 ? wanted : new[] { "OPENAI_API_KEY" };
        return new ShellEnvironment(
            _ => Task.FromResult<IReadOnlyCollection<string>>(names),
            runner, logger: null, supported: supported, shellPath: shell);
    }

    // ---- the ladder, and what it does when a shell will not answer ------------------------------------

    [Fact]
    public async Task An_interactive_login_shell_is_asked_first()
    {
        var runner = new FakeRunner(ShellProbeResult.Ok(NulStream("OPENAI_API_KEY=sk-profile")));
        var shell = Probe(runner);

        shell.Prime();
        await shell.Completion;

        var attempt = Assert.Single(runner.Attempts);
        // -il, not -l: for bash, -l sources .bash_profile and -i sources .bashrc, and a reader may have used
        // either convention. Dropping -i silently loses every key exported from .bashrc or .zshrc.
        Assert.Equal(new[] { "-il", "-c", ShellEnvironment.ProbeCommand }, attempt.Arguments);
        Assert.Equal("sk-profile", shell.TryRead("OPENAI_API_KEY"));
    }

    [Fact]
    public async Task A_shell_that_refuses_the_interactive_flag_is_retried_as_a_plain_login_shell()
    {
        var runner = new FakeRunner(
            ShellProbeResult.Failed(),
            ShellProbeResult.Ok(NulStream("OPENAI_API_KEY=sk-second-try")));
        var shell = Probe(runner);

        shell.Prime();
        await shell.Completion;

        Assert.Equal(2, runner.Attempts.Count);
        Assert.Equal(new[] { "-l", "-c", ShellEnvironment.ProbeCommand }, runner.Attempts[1].Arguments);
        Assert.Equal("sk-second-try", shell.TryRead("OPENAI_API_KEY"));
    }

    [Fact]
    public async Task A_timeout_ends_the_probe_and_is_never_retried()
    {
        var runner = new FakeRunner(ShellProbeResult.Timeout(), ShellProbeResult.Ok(NulStream("OPENAI_API_KEY=sk")));
        var shell = Probe(runner);

        shell.Prime();
        await shell.Completion;

        // The retry exists for a shell that REJECTS the flags, not for one that hangs. A profile that hangs
        // once hangs twice, and retrying it doubles the time the app spends waiting on something it has
        // already decided to abandon.
        Assert.Single(runner.Attempts);
        Assert.Null(shell.TryRead("OPENAI_API_KEY"));
    }

    [Fact]
    public async Task A_probe_that_finds_nothing_completes_rather_than_faulting()
    {
        var shell = Probe(new FakeRunner(ShellProbeResult.Failed(), ShellProbeResult.Failed()));

        shell.Prime();
        await shell.Completion;

        // Completion is awaited on the chat send path. A faulted task there would throw inside the request
        // and report a shell problem as a provider problem.
        Assert.True(shell.Completion.IsCompletedSuccessfully);
        Assert.Null(shell.TryRead("OPENAI_API_KEY"));
    }

    [Theory]
    [InlineData("/usr/local/bin/nu")]
    [InlineData("/opt/homebrew/bin/nushell")]
    [InlineData("/bin/csh")]
    [InlineData("/bin/tcsh")]
    public async Task A_shell_whose_flags_mean_something_else_is_never_run(string shellPath)
    {
        var runner = new FakeRunner(ShellProbeResult.Ok(NulStream("OPENAI_API_KEY=sk")));
        var shell = Probe(runner, shellPath);

        shell.Prime();
        await shell.Completion;

        Assert.Empty(runner.Attempts);
    }

    [Fact]
    public async Task Windows_runs_nothing_at_all()
    {
        var runner = new FakeRunner(ShellProbeResult.Ok(NulStream("OPENAI_API_KEY=sk")));
        var shell = Probe(runner, supported: false);

        shell.Prime();
        await shell.Completion;

        Assert.Empty(runner.Attempts);
        Assert.True(shell.Completion.IsCompletedSuccessfully);
    }

    // ---- what reads do while it is running ------------------------------------------------------------

    [Fact]
    public void Reading_never_starts_a_probe()
    {
        var runner = new FakeRunner(ShellProbeResult.Ok(NulStream("OPENAI_API_KEY=sk")));
        var shell = Probe(runner);

        Assert.Null(shell.TryRead("OPENAI_API_KEY"));

        // The gate that decides whether to run a login shell is Prime, and it is deliberate. If a read could
        // trigger one, every consumer that merely LOOKS at the environment — the settings screen being built
        // on the UI thread — would start a shell as a side effect.
        Assert.Empty(runner.Attempts);
        Assert.True(shell.Completion.IsCompletedSuccessfully);
    }

    [Fact]
    public async Task Two_callers_priming_at_once_run_one_shell_between_them()
    {
        var gate = new ManualResetEventSlim(false);
        var runs = 0;
        var runner = new BlockingRunner(gate, () => Interlocked.Increment(ref runs));
        var shell = new ShellEnvironment(
            _ => Task.FromResult<IReadOnlyCollection<string>>(new[] { "OPENAI_API_KEY" }),
            runner, logger: null, supported: true, shellPath: "/bin/zsh");

        var primes = Enumerable.Range(0, 8).Select(_ => Task.Run(shell.Prime)).ToArray();
        await Task.WhenAll(primes);
        gate.Set();
        await shell.Completion;

        Assert.Equal(1, runs);
    }

    private sealed class BlockingRunner : IShellProbeRunner
    {
        private readonly ManualResetEventSlim _gate;
        private readonly Action _onRun;
        public BlockingRunner(ManualResetEventSlim gate, Action onRun) { _gate = gate; _onRun = onRun; }

        public ShellProbeResult Run(ShellProbeAttempt attempt)
        {
            _onRun();
            _gate.Wait(TimeSpan.FromSeconds(5));
            return ShellProbeResult.Ok(Encoding.UTF8.GetBytes("OPENAI_API_KEY=sk\0"));
        }
    }

    [Fact]
    public async Task Nothing_is_kept_when_no_variable_is_of_interest()
    {
        var runner = new FakeRunner(ShellProbeResult.Ok(NulStream("OPENAI_API_KEY=sk")));
        var shell = new ShellEnvironment(
            _ => Task.FromResult<IReadOnlyCollection<string>>(Array.Empty<string>()),
            runner, logger: null, supported: true, shellPath: "/bin/zsh");

        shell.Prime();
        await shell.Completion;

        // No keep-set means no filter means the whole environment retained, so this must not run at all.
        Assert.Empty(runner.Attempts);
        Assert.Null(shell.TryRead("OPENAI_API_KEY"));
    }

    [Fact]
    public async Task A_keep_set_that_cannot_be_resolved_leaves_the_probe_unrun()
    {
        var runner = new FakeRunner(ShellProbeResult.Ok(NulStream("OPENAI_API_KEY=sk")));
        var shell = new ShellEnvironment(
            _ => throw new InvalidOperationException("the catalogue is unavailable"),
            runner, logger: null, supported: true, shellPath: "/bin/zsh");

        shell.Prime();
        await shell.Completion;

        Assert.Empty(runner.Attempts);
        Assert.True(shell.Completion.IsCompletedSuccessfully);
    }
}

/// <summary>
/// Turning <c>env -0</c> output into the few variables this feature may keep. (#817)
///
/// <para>The filter is the security boundary, not a tidiness measure: a login shell's environment holds
/// session tokens, agent sockets and — on this project's own machines — an iCloud-wide app password, and
/// retaining it for the session would put all of it in a long-lived dictionary.</para>
/// </summary>
public sealed class ShellEnvParserTests
{
    private static IReadOnlyDictionary<string, string> Parse(string stdout, params string[] keep) =>
        ShellEnvParser.Parse(
            Encoding.UTF8.GetBytes(stdout),
            new HashSet<string>(keep, StringComparer.Ordinal));

    [Fact]
    public void A_name_outside_the_keep_set_does_not_survive_the_parse()
    {
        var parsed = Parse(
            "OPENAI_API_KEY=sk-wanted\0APPLE_APP_PASSWORD=abcd-efgh-ijkl-mnop\0SSH_AUTH_SOCK=/tmp/agent\0",
            "OPENAI_API_KEY");

        // The regression test for the whole design. Remove the filter and this fails, which is the only
        // automated warning anyone gets before an unrelated credential starts living in the app's heap for
        // the session and turning up in crash dumps.
        Assert.Equal(new[] { "OPENAI_API_KEY" }, parsed.Keys.ToArray());
        Assert.DoesNotContain("APPLE_APP_PASSWORD", parsed.Keys);
    }

    [Fact]
    public void Chatter_that_the_sentinel_separated_is_discarded()
    {
        // What the real stream looks like: chatter, the sentinel NUL, then env's own NUL-terminated output.
        var parsed = Parse(
            "Now using node v20.11.0 (npm v10.2.4)\nnvm: default=v20\0OPENAI_API_KEY=sk-real\0",
            "OPENAI_API_KEY", "default");

        // A banner containing '=' parses as an assignment to a naive splitter. "default=v20" is exactly the
        // shape nvm prints, and it is in the keep-set here to prove the NAME SHAPE check is what rejects it.
        Assert.Equal("sk-real", parsed["OPENAI_API_KEY"]);
        Assert.Single(parsed);
    }

    [Fact]
    public void Chatter_glued_to_the_first_variable_costs_exactly_that_variable()
    {
        // The stream WITHOUT the sentinel, which is what `env -0` alone produces: a profile's output has no
        // NUL after it, so the chatter and the first assignment are one chunk. This test does not assert a
        // fix — it pins the damage the sentinel exists to prevent, so that anyone who removes
        // ShellEnvironment.ProbeCommand's `printf` can see what it costs. Which variable is first is
        // environment-order dependent, so in the field this is one reader's key vanishing and nobody
        // else's. (fable)
        var glued = Parse(
            "Now using node v20.11.0\nGROQ_API_KEY=gsk-first\0OPENAI_API_KEY=sk-second\0",
            "GROQ_API_KEY", "OPENAI_API_KEY");

        Assert.False(glued.ContainsKey("GROQ_API_KEY"));
        Assert.Equal("sk-second", glued["OPENAI_API_KEY"]);
    }

    [Fact]
    public void The_probe_command_puts_a_sentinel_before_the_environment()
    {
        // Cheap, and it is the only thing standing between the test above and production.
        Assert.StartsWith("printf", ShellEnvironment.ProbeCommand);
        Assert.Contains(@"\0", ShellEnvironment.ProbeCommand);
        Assert.Contains("env -0", ShellEnvironment.ProbeCommand);
    }

    [Fact]
    public void A_value_containing_a_newline_survives_intact()
    {
        var parsed = Parse("ANTHROPIC_API_KEY=line-one\nline-two\0", "ANTHROPIC_API_KEY");

        // The entire reason for `env -0` rather than `env`. Split on newlines instead and this key is
        // truncated at "line-one" — a corrupted credential, which fails as an unexplainable 401.
        Assert.Equal("line-one\nline-two", parsed["ANTHROPIC_API_KEY"]);
    }

    [Fact]
    public void A_value_containing_an_equals_sign_is_not_split_twice()
    {
        var parsed = Parse("GROQ_API_KEY=gsk_aaa=bbb=ccc\0", "GROQ_API_KEY");
        Assert.Equal("gsk_aaa=bbb=ccc", parsed["GROQ_API_KEY"]);
    }

    [Fact]
    public void A_chunk_that_is_not_an_assignment_is_dropped_without_throwing()
    {
        var parsed = Parse("no-equals-here\0=leading-equals\0 SPACED=value\0OPENAI_API_KEY=sk\0",
            "OPENAI_API_KEY", "SPACED", " SPACED");

        Assert.Equal(new[] { "OPENAI_API_KEY" }, parsed.Keys.ToArray());
    }

    [Fact]
    public void An_empty_export_is_not_a_credential()
    {
        var parsed = Parse("OPENAI_API_KEY=\0OPENROUTER_API_KEY=   \0", "OPENAI_API_KEY", "OPENROUTER_API_KEY");

        // Same rule the process-environment reader applies: `export OPENAI_API_KEY=` in a profile must not
        // produce an offer to connect that then fails to authenticate.
        Assert.Empty(parsed);
    }

    /// <summary>
    /// A stream cut off mid-value must not yield a truncated key. (R3-4)
    ///
    /// <para><c>env -0</c> terminates every entry, the last one included, so a complete stream ends in NUL.
    /// Without one, the final entry was cut off — and "ANTHROPIC_API_KEY=sk-ant-parti" has a valid name and
    /// a plausible value, so every other check passes it. The symptom is a 401 from a key the reader knows
    /// is right, which is the hardest kind of wrong to diagnose from the outside.</para>
    ///
    /// <para><c>ShellEnvironment.WellFormed</c> makes the same test but is consulted only where the exit was
    /// never observed; a shell that exits 0 having written a truncated payload arrives here already judged
    /// a success.</para></summary>
    [Fact]
    public void A_value_cut_off_mid_write_is_discarded_rather_than_used()
    {
        var truncated = Parse("HOME=/Users/x\0ANTHROPIC_API_KEY=sk-ant-parti", "HOME", "ANTHROPIC_API_KEY");

        Assert.Equal("/Users/x", truncated["HOME"]);
        Assert.False(truncated.ContainsKey("ANTHROPIC_API_KEY"));
    }

    /// <summary>The same value, terminated, is kept — the rule is about the missing NUL, not about the last
    /// entry being suspicious.</summary>
    [Fact]
    public void A_terminated_final_value_is_kept()
    {
        var complete = Parse("HOME=/Users/x\0ANTHROPIC_API_KEY=sk-ant-whole\0", "HOME", "ANTHROPIC_API_KEY");

        Assert.Equal("sk-ant-whole", complete["ANTHROPIC_API_KEY"]);
    }

    /// <summary>A profile that prints its epilogue AFTER env leaves trailing chatter rather than a truncated
    /// entry. Dropping the last chunk costs nothing there — the chatter would fail the name-shape check
    /// anyway — and every complete variable before it survives, which is why this drops a chunk rather than
    /// rejecting the whole payload.</summary>
    [Fact]
    public void Trailing_chatter_does_not_cost_the_variables_before_it()
    {
        var withEpilogue = Parse("OPENAI_API_KEY=sk-real\0Goodbye from .zlogout", "OPENAI_API_KEY");

        Assert.Equal("sk-real", withEpilogue["OPENAI_API_KEY"]);
    }

    [Fact]
    public void Nothing_is_kept_from_an_empty_stream()
    {
        Assert.Empty(ShellEnvParser.Parse(Array.Empty<byte>(), new HashSet<string> { "OPENAI_API_KEY" }));
        Assert.Empty(ShellEnvParser.Parse(Encoding.UTF8.GetBytes("OPENAI_API_KEY=sk\0"), new HashSet<string>()));
    }
}
