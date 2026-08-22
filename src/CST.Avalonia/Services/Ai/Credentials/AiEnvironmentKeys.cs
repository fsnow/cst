using System;
using System.Collections.Generic;
using System.Linq;
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
}

/// <inheritdoc />
public sealed class AiEnvironmentKeys : IAiEnvironmentKeys
{
    private readonly Func<string, string?> _read;

    public AiEnvironmentKeys() : this(Environment.GetEnvironmentVariable) { }

    /// <summary>Test seam. The real reader is <see cref="Environment.GetEnvironmentVariable(string)"/>.</summary>
    internal AiEnvironmentKeys(Func<string, string?> read) => _read = read;

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
            if (!string.IsNullOrWhiteSpace(Read(name))) return name;
        }
        return null;
    }

    /// <inheritdoc />
    public string? ValueFor(AiProviderPreset preset)
    {
        var name = VariableFor(preset);
        return name is null ? null : Read(name);
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
    private string? Read(string name)
    {
        try
        {
            var value = _read(name);
            return string.IsNullOrWhiteSpace(value) ? null : value;
        }
        catch
        {
            // A platform that refuses to read the environment is "no key", not a crash on the settings screen.
            return null;
        }
    }
}
