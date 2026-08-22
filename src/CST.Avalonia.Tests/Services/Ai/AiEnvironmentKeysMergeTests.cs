using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.Services.Ai.Credentials;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// Two sources for one variable: this process's environment, and the login shell's. (#817)
///
/// <para>The rule these tests hold in place is that the PROCESS always wins and the shell only fills gaps.
/// Reversing it is a one-line change that no other test would notice, and it would let a forgotten
/// <c>.zprofile</c> export shadow a <c>launchctl setenv</c> the reader made to correct it — an app
/// authenticating with a key its owner believes they have replaced, which is the class of failure #714 exists
/// to prevent.</para>
/// </summary>
public sealed class AiEnvironmentKeysMergeTests
{
    /// <summary>A settled snapshot, standing in for a probe that has already finished.</summary>
    private sealed class FakeShell : IShellEnvironment
    {
        private readonly Dictionary<string, string> _values;
        public FakeShell(params (string Name, string Value)[] values) =>
            _values = values.ToDictionary(v => v.Name, v => v.Value, StringComparer.Ordinal);

        public bool Primed { get; private set; }
        public void Prime() => Primed = true;
        public Task Completion => Task.CompletedTask;
        public string? TryRead(string variableName) =>
            _values.TryGetValue(variableName, out var v) ? v : null;
        public event EventHandler? Probed;
        public void RaiseProbed() => Probed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>A probe that has not answered yet, which is what every read sees for the first seconds.</summary>
    private sealed class PendingShell : IShellEnvironment
    {
        private readonly TaskCompletionSource _tcs = new();
        public void Prime() { }
        public Task Completion => _tcs.Task;
        public string? TryRead(string variableName) => null;
        public event EventHandler? Probed;
        public void Finish() { _tcs.SetResult(); Probed?.Invoke(this, EventArgs.Empty); }
    }

    private static AiProviderPreset Preset(string id, params string[] envNames) =>
        new(id, id.ToUpperInvariant(), ChatProviderKind.OpenAiCompatible, "https://example.invalid/v1",
            new AiCredentialMethod[] { new AiCredentialMethod.Env(envNames) },
            Array.Empty<AiInputPrompt>());

    private static AiEnvironmentKeys Keys(
        IShellEnvironment? shell, params (string Name, string? Value)[] processEnvironment)
    {
        var map = processEnvironment.ToDictionary(e => e.Name, e => e.Value, StringComparer.Ordinal);
        return new AiEnvironmentKeys(name => map.TryGetValue(name, out var v) ? v : null, shell);
    }

    [Fact]
    public void The_process_environment_wins_over_the_shell_for_the_same_variable()
    {
        var keys = Keys(
            new FakeShell(("OPENAI_API_KEY", "sk-from-profile")),
            ("OPENAI_API_KEY", "sk-from-launchctl"));

        Assert.Equal("sk-from-launchctl", keys.Read("OPENAI_API_KEY"));
    }

    [Fact]
    public void The_shell_supplies_a_variable_the_process_does_not_have()
    {
        var keys = Keys(new FakeShell(("CEREBRAS_API_KEY", "csk-profile")));

        // The bug in one line: launched from Finder, this is the only place the key exists.
        Assert.Equal("csk-profile", keys.Read("CEREBRAS_API_KEY"));
    }

    [Fact]
    public void An_empty_process_variable_does_not_mask_the_shell()
    {
        var keys = Keys(new FakeShell(("OPENAI_API_KEY", "sk-profile")), ("OPENAI_API_KEY", ""));

        // Documented consequence rather than an oversight. Empty is "absent" everywhere in this file, so
        // `OPENAI_API_KEY= app` does not withhold a profile key; declining to opt in is what does.
        Assert.Equal("sk-profile", keys.Read("OPENAI_API_KEY"));
    }

    [Fact]
    public void The_presets_declared_order_still_decides_which_variable_a_provider_uses()
    {
        // GOOGLE_API_KEY is declared first, and it is the shell that has it; GEMINI_API_KEY is later, and the
        // process has it. Source precedence answers "which value for this name"; catalogue precedence answers
        // "which name for this preset". Conflating them — merging per preset instead of per variable — makes
        // the process's later-ranked variable win, which is the catalogue's decision quietly overruled.
        var keys = Keys(
            new FakeShell(("GOOGLE_API_KEY", "profile-google")),
            ("GEMINI_API_KEY", "process-gemini"));

        var preset = Preset("google", "GOOGLE_API_KEY", "GOOGLE_GENERATIVE_AI_API_KEY", "GEMINI_API_KEY");

        Assert.Equal("GOOGLE_API_KEY", keys.VariableFor(preset));
        Assert.Equal("profile-google", keys.ValueFor(preset));
    }

    [Fact]
    public void A_probe_still_running_reads_exactly_as_this_app_always_did()
    {
        var keys = Keys(new PendingShell(), ("OPENAI_API_KEY", "sk-process"));

        Assert.Equal("sk-process", keys.Read("OPENAI_API_KEY"));
        Assert.Null(keys.Read("CEREBRAS_API_KEY"));
    }

    [Fact]
    public void Discovery_finds_a_preset_whose_key_only_the_shell_can_see()
    {
        var keys = Keys(new FakeShell(("GROQ_API_KEY", "gsk-profile")));

        var found = keys.Discover(new[] { Preset("groq", "GROQ_API_KEY"), Preset("openai", "OPENAI_API_KEY") });

        var one = Assert.Single(found);
        Assert.Equal("groq", one.PresetId);
        Assert.Equal("GROQ_API_KEY", one.VariableName);
    }

    [Fact]
    public async Task Readiness_is_already_settled_when_there_is_no_shell_to_wait_for()
    {
        // Awaited on the chat send path and before a model listing. On Windows, and on every launch that did
        // not prime a probe, that await must cost nothing at all.
        var keys = Keys(shell: null, ("OPENAI_API_KEY", "sk"));
        Assert.True(keys.Ready.IsCompleted);
        await keys.Ready;
    }

    [Fact]
    public void A_landing_probe_tells_the_panels_the_environment_now_reads_differently()
    {
        var shell = new FakeShell(("CEREBRAS_API_KEY", "csk"));
        var keys = Keys(shell);
        var told = 0;
        keys.Changed += (_, _) => told++;

        shell.RaiseProbed();

        // Without this, a panel built before the probe landed keeps saying "not configured" for the rest of
        // the session — to a reader whose key IS set, in the variable the app is about to use. Completion
        // cannot serve here: read before anything primed, it reports "already settled". (fable)
        Assert.Equal(1, told);
    }

    [Fact]
    public void Readiness_is_pending_while_a_probe_is()
    {
        var pending = new PendingShell();
        var keys = Keys(pending);

        Assert.False(keys.Ready.IsCompleted);
        pending.Finish();
        Assert.True(keys.Ready.IsCompleted);
    }
}
