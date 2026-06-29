using System;
using CST.Conversion;

namespace CST.Avalonia.Models;

/// <summary>
/// Single source of truth for the string keys used to index per-script settings (e.g.
/// <see cref="FontSettings.ScriptFonts"/>). The on-disk format keeps string keys for backward
/// compatibility, but every key must correspond to a <see cref="Script"/> enum member, so callers
/// go through here instead of sprinkling magic strings / raw <c>script.ToString()</c> calls. (#78)
/// </summary>
public static class ScriptKeys
{
    /// <summary>The canonical key for a script (its enum name, matching the persisted dictionary keys).</summary>
    public static string Of(Script script) => script.ToString();

    /// <summary>True if <paramref name="key"/> is exactly the name of a defined <see cref="Script"/> member.</summary>
    public static bool IsValidKey(string? key) => TryParse(key, out _);

    /// <summary>
    /// Parse a persisted key back to its <see cref="Script"/>. Case-sensitive and numeric-rejecting so that
    /// only real enum names (the values <see cref="Of"/> produces) round-trip.
    /// </summary>
    public static bool TryParse(string? key, out Script script)
    {
        script = default;
        if (string.IsNullOrEmpty(key) || char.IsDigit(key[0]) || key[0] == '-')
            return false; // reject the numeric forms Enum.TryParse would otherwise accept
        return Enum.TryParse(key, ignoreCase: false, out script) && Enum.IsDefined(typeof(Script), script);
    }
}
