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

    // The single canonical set of log levels the app supports, in increasing severity. These are
    // Serilog LogEventLevel names — exactly what the startup parser (App.ParseLogLevel) and the live
    // reconfigure (SettingsViewModel.ReconfigureLogger) actually parse — and the Settings UI presents
    // this same list. Keeping one source prevents the STATE-4 drift where the validator whitelisted
    // Microsoft.Extensions.Logging names ("Critical"/"Trace"/"None") that neither the UI nor the
    // parsers used, so a user-chosen "Fatal" failed validation and reverted to Information every
    // restart. (STATE-4)
    public static readonly string[] LogLevels = { "Debug", "Information", "Warning", "Error", "Fatal" };

    private static readonly HashSet<string> ValidLogLevels = new(LogLevels, StringComparer.OrdinalIgnoreCase);

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

        // AI settings: an explicit "ai": null (or "ai": {"localApi": null}) in a hand-edited settings.json
        // overwrites the `= new()` initializer and deserializes to null, so AiSettingsViewModel's ctor NREs on
        // ai.LocalApi.Enabled and ShowSettingsWindow's catch swallows it — the Settings window then never opens
        // again, and Settings is the only repair path. Repair like every other section. (#319 A7-2)
        if (settings.Ai == null) { settings.Ai = new AiSettings(); fixes.Add("ai settings were null; reset to default"); }
        if (settings.Ai.LocalApi == null) { settings.Ai.LocalApi = new LocalApiSettings(); fixes.Add("ai.localApi settings were null; reset to default"); }

        // (The historical local-API Port/Token scrub is gone with those fields, removed in #280: the port is
        // ephemeral and the token per-session, both held only in local-api.json. A stale value left in an old
        // settings.json is now an unknown property the deserializer ignores.)

        // XML update repository fields
        var xml = settings.XmlUpdateSettings ??= new XmlUpdateSettings();
        if (string.IsNullOrWhiteSpace(xml.XmlRepositoryOwner)) { xml.XmlRepositoryOwner = "VipassanaTech"; fixes.Add("xml repo owner -> VipassanaTech"); }
        if (string.IsNullOrWhiteSpace(xml.XmlRepositoryName)) { xml.XmlRepositoryName = "tipitaka-xml"; fixes.Add("xml repo name -> tipitaka-xml"); }
        if (string.IsNullOrWhiteSpace(xml.XmlRepositoryBranch)) { xml.XmlRepositoryBranch = "main"; fixes.Add("xml repo branch -> main"); }

        // Dictionary-asset update repository fields — same null/blank repair as XML, so a hand-edited
        // "dpdUpdateSettings": null can't NRE DpdUpdateSettingsViewModel's ctor and lock the Settings window
        // shut (the #319 A7-2 failure mode), and a blanked owner/name can't dead-end the background check. (#468)
        var dpd = settings.DpdUpdateSettings ??= new DpdUpdateSettings();
        var dpdDefaults = new DpdUpdateSettings();
        if (string.IsNullOrWhiteSpace(dpd.RepositoryOwner)) { dpd.RepositoryOwner = dpdDefaults.RepositoryOwner; fixes.Add($"dictionary repo owner -> {dpdDefaults.RepositoryOwner}"); }
        if (string.IsNullOrWhiteSpace(dpd.RepositoryName)) { dpd.RepositoryName = dpdDefaults.RepositoryName; fixes.Add($"dictionary repo name -> {dpdDefaults.RepositoryName}"); }

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
