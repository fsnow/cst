using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using CST.Avalonia.Models.Ai;

namespace CST.Avalonia.Services.Ai.Credentials;

/// <summary>
/// A vendor API key found in the environment, and the variable it came from. (#714)
/// </summary>
/// <param name="PresetId">The preset whose declared variables matched.</param>
/// <param name="VariableName">
/// The variable holding it — the NAME only. The value is never carried in this record, never logged, and never
/// written anywhere: it is read at the moment of use and discarded. A discovered credential belongs to the
/// environment, and a copy in our keychain would go stale the moment the reader changed or unset the variable,
/// leaving the app authenticating with something the reader believes they have revoked.
/// </param>
public sealed record AiEnvironmentKey(string PresetId, string VariableName);

/// <summary>
/// Finds vendor API keys the reader's environment already holds — <c>OPENAI_API_KEY</c>,
/// <c>ANTHROPIC_API_KEY</c>, and the rest. (#714)
///
/// <para><b>Discovery is automatic; use is not.</b> This service only reports what is there. Nothing here
/// creates a connection or authenticates anything, and that separation is the whole point: OpenCode adopts an
/// environment key silently, which produced a connected provider the maintainer had not configured, from a
/// variable he had forgotten was set on his own machine — and then offered no way to disconnect it, because an
/// app cannot delete a credential it never stored. The opt-in step exists so that spending a reader's money is
/// something they chose.</para>
/// </summary>
public interface IAiEnvironmentKeys
{
    /// <summary>
    /// The variable currently holding a key for this preset, in the preset's declared precedence order, or
    /// null when none is set. Name only.
    /// </summary>
    string? VariableFor(AiProviderPreset preset);

    /// <summary>
    /// The key itself, read from the environment at the moment of use.
    ///
    /// <para>Deliberately not cached. A variable can change or be unset between one request and the next, and
    /// a cached copy would keep authenticating with a credential the reader thinks is gone. Reading twice
    /// costs a dictionary lookup.</para>
    /// </summary>
    string? ValueFor(AiProviderPreset preset);

    /// <summary>Every preset whose declared variables are satisfied right now.</summary>
    IReadOnlyList<AiEnvironmentKey> Discover(IEnumerable<AiProviderPreset> presets);

    /// <summary>
    /// Completes once every source this can read has been consulted. (#817)
    ///
    /// <para>Reads never block, so a caller that must not miss a key — one about to authenticate — awaits
    /// this first. Already complete unless a shell probe is actually in flight, so awaiting it costs nothing
    /// in the ordinary case and never starts a probe of its own.</para>
    /// </summary>
    Task Ready { get; }

    /// <summary>
    /// The value of one named variable, or null when it is unset or empty. (#714)
    ///
    /// <para>This is what an adopted connection reads, and it takes the NAME the reader consented to rather
    /// than re-deriving it from the preset. The catalogue's <c>env</c> lists are refreshed from models.dev:
    /// a reordering, or a newly added alias the reader happens to have set for something else entirely, would
    /// otherwise change which credential goes to the recorded endpoint — silently, with no second consent,
    /// which is the precise property this feature exists to prevent. (fable)</para>
    /// </summary>
    string? Read(string variableName);

    /// <summary>
    /// Raised when the set of readable variables has grown — today, when a shell probe lands. (#817)
    ///
    /// <para>Surfaces that render "no key is set" subscribe to this, because on a GUI launch that sentence can
    /// be wrong for the first few seconds of a session and nothing else would ever correct it.</para>
    /// </summary>
    event EventHandler? Changed;
}

/// <inheritdoc />
public sealed class AiEnvironmentKeys : IAiEnvironmentKeys
{
    private readonly Func<string, string?> _read;
    private readonly IShellEnvironment? _shell;

    public AiEnvironmentKeys(IShellEnvironment? shell = null)
        : this(Environment.GetEnvironmentVariable, shell) { }

    /// <summary>Test seam. The real reader is <see cref="Environment.GetEnvironmentVariable(string)"/>.</summary>
    internal AiEnvironmentKeys(Func<string, string?> read, IShellEnvironment? shell = null)
    {
        _read = read;
        _shell = shell;

        // Forwarded rather than exposing IShellEnvironment to the panels: what they need to know is that the
        // environment now reads differently, not that a shell was involved in making it so.
        if (_shell is not null)
            _shell.Probed += (_, _) => Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <inheritdoc />
    public event EventHandler? Changed;

    /// <inheritdoc />
    public Task Ready => _shell?.Completion ?? Task.CompletedTask;

    /// <inheritdoc />
    public string? Read(string variableName) =>
        string.IsNullOrWhiteSpace(variableName) ? null : ReadRaw(variableName);

    /// <inheritdoc />
    public string? VariableFor(AiProviderPreset preset)
    {
        if (preset is null) return null;

        // Precedence order is the preset's own: Google alone answers to GOOGLE_API_KEY,
        // GOOGLE_GENERATIVE_AI_API_KEY and GEMINI_API_KEY, and which one wins is the catalogue's decision,
        // not ours. First one set, wins.
        foreach (var name in preset.EnvironmentVariables)
        {
            if (string.IsNullOrWhiteSpace(name)) continue;
            if (!string.IsNullOrWhiteSpace(ReadRaw(name))) return name;
        }
        return null;
    }

    /// <inheritdoc />
    public string? ValueFor(AiProviderPreset preset)
    {
        var name = VariableFor(preset);
        return name is null ? null : ReadRaw(name);
    }

    /// <inheritdoc />
    public IReadOnlyList<AiEnvironmentKey> Discover(IEnumerable<AiProviderPreset> presets) =>
        (presets ?? Array.Empty<AiProviderPreset>())
            .Where(p => p is not null)
            .Select(p => (preset: p, variable: VariableFor(p)))
            .Where(t => t.variable is not null)
            .Select(t => new AiEnvironmentKey(t.preset.Id, t.variable!))
            .ToList();

    // Whitespace is not a credential. A variable exported empty — `export OPENAI_API_KEY=` in a shell profile,
    // or a CI runner that defines every name it knows — would otherwise read as a key that is present, and
    // offering to connect with it produces an authentication failure the reader cannot explain, from a
    // variable they did not know they had.
    //
    // TWO SOURCES, AND THE PROCESS ALWAYS WINS. (#817) The shell snapshot fills gaps; it never overrides. Per
    // variable, not per preset — which source supplies a name and which name a preset prefers are separate
    // questions, and keeping them separate leaves the catalogue's declared order (below) in charge of the
    // second one.
    //
    // Process-first because everything in the process environment is the more deliberate signal. It arrived
    // by launching from a terminal, by `launchctl setenv`, or inline for this one run — and that last case,
    // `OPENAI_API_KEY=test "…/MacOS/CST Reader"`, is the canonical override that probe-first would silently
    // defeat. A profile line is the stalest of the sources: letting a forgotten .zprofile export shadow a
    // launchctl correction is the OpenCode failure #714 exists to prevent, in miniature.
    //
    // It is also what keeps the late arrival honest — for VALUES, which is the claim worth making. A value
    // the reader already saw is never swapped underneath them by the probe.
    //
    // NAMES are a weaker guarantee, and the difference is worth stating rather than glossing. VariableFor
    // below walks the preset's declared order and takes the first that is set, so a preset whose earlier-
    // ranked variable exists only in the shell will change WHICH variable it offers when the probe lands —
    // GEMINI_API_KEY becoming GOOGLE_API_KEY, say. That is the catalogue's own precedence applied to a fuller
    // environment rather than a partial one, so the later answer is the more correct of the two; it is called
    // out because the row the reader consents to names a variable. (fable)
    //
    // One consequence, accepted: because empty is absent, `OPENAI_API_KEY= "…/MacOS/CST Reader"` does not MASK
    // a profile value — the gap-fill takes over. That follows from the whitespace rule above, and the real way
    // to withhold a key is not to opt in.
    private string? ReadRaw(string name)
    {
        try
        {
            var value = _read(name);
            if (!string.IsNullOrWhiteSpace(value)) return value;
        }
        catch
        {
            // A platform that refuses to read the environment is "no key", not a crash on the settings screen.
        }

        // Null until a probe has been primed AND has finished. Deliberately not awaited: this is called on the
        // UI thread while the Settings window is being built.
        var probed = _shell?.TryRead(name);
        return string.IsNullOrWhiteSpace(probed) ? null : probed;
    }
}
