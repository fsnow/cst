using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace CST.Avalonia.Models;

/// <summary>
/// Pure (no I/O, no DI) validation, sanitization, and version-migration for <see cref="Settings"/>. Runs on
/// load so malformed/older settings files self-heal cleanly. Directly unit-testable. (#78)
/// </summary>
public static class SettingsValidator
{
    /// <summary>Current settings-file schema version. Bump + add a step in <see cref="Migrate"/> on a breaking change.</summary>
    public const string CurrentVersion = "1.0";

    private static readonly HashSet<string> ValidLogLevels = new(StringComparer.OrdinalIgnoreCase)
    {
        "Trace", "Debug", "Information", "Warning", "Error", "Critical", "None"
    };

    /// <summary>Upgrade an older / missing-version settings object to <see cref="CurrentVersion"/>. Returns notes.</summary>
    public static IReadOnlyList<string> Migrate(Settings settings)
    {
        var notes = new List<string>();
        if (settings == null) return notes;

        var v = (settings.Version ?? string.Empty).Trim();
        if (v.Length == 0)
        {
            settings.Version = CurrentVersion;
            notes.Add($"settings had no version; stamped {CurrentVersion}");
            return notes;
        }

        // --- ordered migration steps go here as the schema evolves ---

        if (ApplicationStateValidator.CompareVersions(v, CurrentVersion) > 0)
        {
            notes.Add($"settings version {v} is newer than supported {CurrentVersion}; reading as-is");
            return notes;
        }
        if (v != CurrentVersion)
        {
            settings.Version = CurrentVersion;
            notes.Add($"migrated settings {v} -> {CurrentVersion}");
        }
        return notes;
    }

    /// <summary>
    /// Repair invalid settings in place (bad paths, unknown log level, non-positive font sizes, stray repo
    /// fields, script-font keys that are not real scripts). Idempotent. Returns the fixes applied.
    /// </summary>
    public static IReadOnlyList<string> Sanitize(Settings settings)
    {
        var fixes = new List<string>();
        if (settings == null) return fixes;

        // Directory paths: a non-existent path is fine (created on demand), but one with illegal characters is
        // not - clear it so the app falls back to its default rather than throwing later.
        if (HasInvalidPathChars(settings.XmlBooksDirectory))
        {
            settings.XmlBooksDirectory = "";
            fixes.Add("cleared XmlBooksDirectory (contained invalid path characters)");
        }
        if (HasInvalidPathChars(settings.IndexDirectory))
        {
            settings.IndexDirectory = "";
            fixes.Add("cleared IndexDirectory (contained invalid path characters)");
        }

        // Developer log level
        var dev = settings.DeveloperSettings ??= new DeveloperSettings();
        if (string.IsNullOrWhiteSpace(dev.LogLevel) || !ValidLogLevels.Contains(dev.LogLevel))
        {
            var bad = dev.LogLevel;
            dev.LogLevel = "Information";
            fixes.Add($"log level '{bad}' -> Information");
        }

        // XML update repository fields
        var xml = settings.XmlUpdateSettings ??= new XmlUpdateSettings();
        if (string.IsNullOrWhiteSpace(xml.XmlRepositoryOwner)) { xml.XmlRepositoryOwner = "VipassanaTech"; fixes.Add("xml repo owner -> VipassanaTech"); }
        if (string.IsNullOrWhiteSpace(xml.XmlRepositoryName)) { xml.XmlRepositoryName = "tipitaka-xml"; fixes.Add("xml repo name -> tipitaka-xml"); }
        if (string.IsNullOrWhiteSpace(xml.XmlRepositoryBranch)) { xml.XmlRepositoryBranch = "main"; fixes.Add("xml repo branch -> main"); }

        // Fonts
        var fonts = settings.FontSettings ??= new FontSettings();
        if (fonts.LocalizationFontSize <= 0) { fonts.LocalizationFontSize = 12; fixes.Add("localization font size -> 12"); }
        fonts.ScriptFonts ??= new Dictionary<string, ScriptFontSetting>();

        // Drop any script-font key that is not a real Script enum name (magic-string guard, #78)
        foreach (var key in fonts.ScriptFonts.Keys.Where(k => !ScriptKeys.IsValidKey(k)).ToList())
        {
            fonts.ScriptFonts.Remove(key);
            fixes.Add($"removed unknown script-font key '{key}'");
        }
        // Replace any null setting (keys materialized first so we don't mutate the dict mid-enumeration).
        foreach (var key in fonts.ScriptFonts.Where(kv => kv.Value == null).Select(kv => kv.Key).ToList())
        {
            fonts.ScriptFonts[key] = new ScriptFontSetting();
            fixes.Add($"script-font '{key}' was null; reset to default");
        }
        // Clamp non-positive sizes (mutates the value object only, never the dictionary itself).
        foreach (var kvp in fonts.ScriptFonts)
        {
            if (kvp.Value.FontSize <= 0)
            {
                kvp.Value.FontSize = 12;
                fixes.Add($"script-font '{kvp.Key}' size -> 12");
            }
        }

        return fixes;
    }

    private static bool HasInvalidPathChars(string? path)
    {
        if (string.IsNullOrEmpty(path)) return false; // empty = "use default", not invalid
        return path.IndexOfAny(Path.GetInvalidPathChars()) >= 0;
    }
}
