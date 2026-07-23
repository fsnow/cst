using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CST.Avalonia.Models;
using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.Models;

/// <summary>
/// Unit tests for the pure ApplicationState/Settings validators, migration, and the ScriptKeys enum guard (#78).
/// No I/O or DI - these exercise the load-time self-healing logic directly.
/// </summary>
public class StateValidationTests
{
    // ---------- ScriptKeys ----------

    [Theory]
    [InlineData(Script.Devanagari, "Devanagari")]
    [InlineData(Script.Latin, "Latin")]
    [InlineData(Script.Myanmar, "Myanmar")]
    public void ScriptKeys_Of_IsEnumName(Script script, string expected)
        => Assert.Equal(expected, ScriptKeys.Of(script));

    [Theory]
    [InlineData("Devanagari", true)]
    [InlineData("Thai", true)]
    [InlineData("NotAScript", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    [InlineData("7", false)]    // numeric form Enum.TryParse would otherwise accept
    [InlineData("-1", false)]
    [InlineData("devanagari", false)] // case-sensitive: keys are produced by ToString()
    public void ScriptKeys_IsValidKey(string? key, bool expected)
        => Assert.Equal(expected, ScriptKeys.IsValidKey(key));

    [Fact]
    public void ScriptKeys_TryParse_RoundTrips()
    {
        foreach (Script s in System.Enum.GetValues<Script>())
        {
            Assert.True(ScriptKeys.TryParse(ScriptKeys.Of(s), out var parsed));
            Assert.Equal(s, parsed);
        }
    }

    // ---------- ApplicationStateValidator.Migrate ----------

    [Fact]
    public void Migrate_EmptyVersion_StampsCurrent()
    {
        var state = new ApplicationState { Version = "" };
        var notes = ApplicationStateValidator.Migrate(state);
        Assert.Equal(ApplicationStateValidator.CurrentVersion, state.Version);
        Assert.NotEmpty(notes);
    }

    [Fact]
    public void Migrate_CurrentVersion_NoChange()
    {
        var state = new ApplicationState { Version = ApplicationStateValidator.CurrentVersion };
        var notes = ApplicationStateValidator.Migrate(state);
        Assert.Empty(notes);
        Assert.Equal(ApplicationStateValidator.CurrentVersion, state.Version);
    }

    [Fact]
    public void Migrate_OlderVersion_UpgradesToCurrent()
    {
        var state = new ApplicationState { Version = "0.9" };
        ApplicationStateValidator.Migrate(state);
        Assert.Equal(ApplicationStateValidator.CurrentVersion, state.Version);
    }

    [Fact]
    public void Migrate_NewerVersion_LeftAsIs()
    {
        var state = new ApplicationState { Version = "99.0" };
        var notes = ApplicationStateValidator.Migrate(state);
        Assert.Equal("99.0", state.Version); // forward-compatible read, not clobbered
        Assert.Contains(notes, n => n.Contains("newer"));
    }

    [Theory]
    [InlineData("1.0", "1.0", 0)]
    [InlineData("1.0", "1.1", -1)]
    [InlineData("2.0", "1.9", 1)]
    [InlineData("1", "1.0", 0)]
    [InlineData("1.2.3", "1.2", 1)]
    public void CompareVersions_Works(string a, string b, int sign)
        => Assert.Equal(sign, System.Math.Sign(ApplicationStateValidator.CompareVersions(a, b)));

    // ---------- ApplicationStateValidator.Sanitize ----------

    [Fact]
    public void Sanitize_FixesNonPositiveAndNanWindowSizes()
    {
        var state = new ApplicationState();
        state.MainWindow.Width = -5;
        state.MainWindow.Height = double.NaN;
        state.OpenBookDialog.Width = 0;
        state.DictionaryDialog.Height = double.PositiveInfinity;

        var fixes = ApplicationStateValidator.Sanitize(state);

        Assert.True(state.MainWindow.Width > 0);
        Assert.True(state.MainWindow.Height > 0);
        Assert.True(state.OpenBookDialog.Width > 0);
        Assert.True(state.DictionaryDialog.Height > 0);
        Assert.NotEmpty(fixes);
    }

    [Fact]
    public void Sanitize_ClearsNaNInfinityCoordinates()
    {
        var state = new ApplicationState();
        state.MainWindow.X = double.NaN;
        state.MainWindow.Y = double.NegativeInfinity;

        ApplicationStateValidator.Sanitize(state);

        Assert.Null(state.MainWindow.X);
        Assert.Null(state.MainWindow.Y);
    }

    [Fact]
    public void Sanitize_PreservesValidValues()
    {
        var state = new ApplicationState();
        state.MainWindow.Width = 1234;
        state.MainWindow.Height = 567;
        state.MainWindow.X = 100;
        state.MainWindow.Y = 50;

        var fixes = ApplicationStateValidator.Sanitize(state);

        Assert.Empty(fixes);
        Assert.Equal(1234, state.MainWindow.Width);
        Assert.Equal(567, state.MainWindow.Height);
        Assert.Equal(100, state.MainWindow.X);
        Assert.Equal(50, state.MainWindow.Y);
    }

    [Fact]
    public void Sanitize_RemovesBookWindowsWithNegativeIndex()
    {
        var state = new ApplicationState();
        state.BookWindows.Add(new BookWindowState { BookIndex = 5 });
        state.BookWindows.Add(new BookWindowState { BookIndex = -1 });
        state.BookWindows.Add(new BookWindowState { BookIndex = -99 });

        ApplicationStateValidator.Sanitize(state);

        Assert.Single(state.BookWindows);
        Assert.Equal(5, state.BookWindows[0].BookIndex);
    }

    [Fact]
    public void Sanitize_ClampsBookWindowHitAndTabIndices()
    {
        var state = new ApplicationState();
        state.BookWindows.Add(new BookWindowState { BookIndex = 0, CurrentHitIndex = 0, TotalHits = -3, TabIndex = -1, Width = -1 });

        ApplicationStateValidator.Sanitize(state);

        var w = state.BookWindows[0];
        Assert.Equal(1, w.CurrentHitIndex);
        Assert.Equal(0, w.TotalHits);
        Assert.Equal(0, w.TabIndex);
        Assert.True(w.Width > 0);
    }

    [Fact]
    public void Sanitize_FixesProximityAndMaxRecent()
    {
        var state = new ApplicationState();
        state.SearchDialog.ProximityDistance = -1;
        state.Preferences.MaxRecentBooks = -5;

        ApplicationStateValidator.Sanitize(state);

        Assert.True(state.SearchDialog.ProximityDistance >= 0);
        Assert.True(state.Preferences.MaxRecentBooks >= 0);
    }

    [Fact]
    public void Sanitize_TrimsRecentBooksToMax_AndDropsNegativeIndex()
    {
        var state = new ApplicationState();
        state.Preferences.MaxRecentBooks = 2;
        state.Preferences.RecentBooks.Add(new RecentBookItem { BookIndex = 1 });
        state.Preferences.RecentBooks.Add(new RecentBookItem { BookIndex = -1 }); // dropped
        state.Preferences.RecentBooks.Add(new RecentBookItem { BookIndex = 2 });
        state.Preferences.RecentBooks.Add(new RecentBookItem { BookIndex = 3 });
        state.Preferences.RecentBooks.Add(new RecentBookItem { BookIndex = 4 }); // over max -> trimmed

        ApplicationStateValidator.Sanitize(state);

        Assert.Equal(2, state.Preferences.RecentBooks.Count);
        Assert.DoesNotContain(state.Preferences.RecentBooks, r => r.BookIndex < 0);
    }

    [Fact]
    public void Bengali_display_script_survives_a_save_load_round_trip()
    {
        // #323 A9-1: Script.Bengali is enum value 0 = default(Script). With the app-state serializer's
        // WhenWritingDefault, currentScript/bookScript would be OMITTED and reload as their initializers, silently
        // resetting a Bengali choice every launch. [JsonIgnore(Never)] forces them out.
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        var state = new ApplicationState();
        state.Preferences.CurrentScript = Script.Bengali;
        state.BookWindows.Add(new BookWindowState { BookIndex = 0, BookScript = Script.Bengali });

        var json = JsonSerializer.Serialize(state, options);
        Assert.Contains("currentScript", json);   // NOT dropped despite being the enum default (0)
        Assert.Contains("bookScript", json);

        var round = JsonSerializer.Deserialize<ApplicationState>(json, options)!;
        Assert.Equal(Script.Bengali, round.Preferences.CurrentScript);
        Assert.Equal(Script.Bengali, round.BookWindows[0].BookScript);
    }

    [Fact]
    public void MaxRecentBooks_zero_survives_a_save_load_round_trip()
    {
        // #323 class: MaxRecentBooks' initializer is 10 but its CLR default is 0, so a deliberate 0 ("disable the
        // recent-books list") would be dropped by WhenWritingDefault and revert to 10 next launch without
        // [JsonIgnore(Never)].
        var options = new JsonSerializerOptions
        {
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

        var state = new ApplicationState();
        state.Preferences.MaxRecentBooks = 0;

        var json = JsonSerializer.Serialize(state, options);
        Assert.Contains("maxRecentBooks", json);   // NOT dropped despite being the CLR default (0)
        Assert.Equal(0, JsonSerializer.Deserialize<ApplicationState>(json, options)!.Preferences.MaxRecentBooks);
    }

    [Fact]
    public void Sanitize_repairs_a_null_Ai_or_null_LocalApi_section()
    {
        // #319 A7-2: an explicit "ai": null (or nested "localApi": null) in settings.json deserializes to null and
        // would NRE AiSettingsViewModel's ctor, permanently bricking the Settings window. Repair like every other
        // section.
        var s1 = new Settings();
        s1.Ai = null!;
        var fixes1 = SettingsValidator.Sanitize(s1);
        Assert.NotNull(s1.Ai);
        Assert.NotNull(s1.Ai.LocalApi);
        Assert.Contains(fixes1, f => f.Contains("ai settings were null"));

        var s2 = new Settings();
        s2.Ai.LocalApi = null!;
        SettingsValidator.Sanitize(s2);
        Assert.NotNull(s2.Ai.LocalApi);
    }

    [Fact]
    public void Sanitize_IsIdempotent()
    {
        var state = new ApplicationState();
        state.MainWindow.Width = -1;
        ApplicationStateValidator.Sanitize(state);
        var second = ApplicationStateValidator.Sanitize(state);
        Assert.Empty(second); // already clean
    }

    [Fact]
    public void Validate_ReportsRecoverableIssues()
    {
        var state = new ApplicationState { Version = "" };
        state.MainWindow.Width = -1;

        var result = ApplicationStateValidator.Validate(state);

        Assert.False(result.IsValid);
        Assert.True(result.CanRecover);
        Assert.NotEmpty(result.Warnings);
    }

    [Fact]
    public void Validate_CleanStateIsValid()
        => Assert.True(ApplicationStateValidator.Validate(new ApplicationState()).IsValid);

    // ---------- Round-trip: an old JSON state file upgrades cleanly ----------

    [Fact]
    public void OldStateJson_DeserializesMigratesAndSanitizes()
    {
        // A minimal, older-style file: no version, a bad window size, a stray negative-index book window.
        const string json = """
        {
          "mainWindow": { "width": -1, "height": 0 },
          "bookWindows": [ { "bookIndex": -1 }, { "bookIndex": 3, "bookFileName": "s0101m.mul.xml" } ]
        }
        """;
        var opts = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };
        var state = JsonSerializer.Deserialize<ApplicationState>(json, opts)!;

        ApplicationStateValidator.Migrate(state);
        ApplicationStateValidator.Sanitize(state);

        Assert.Equal(ApplicationStateValidator.CurrentVersion, state.Version);
        Assert.True(state.MainWindow.Width > 0 && state.MainWindow.Height > 0);
        Assert.Single(state.BookWindows);
        Assert.Equal(3, state.BookWindows[0].BookIndex);
        Assert.True(ApplicationStateValidator.Validate(state).IsValid);
    }

    // ---------- SettingsValidator ----------

    [Fact]
    public void Settings_Migrate_EmptyVersion_StampsCurrent()
    {
        var s = new Settings { Version = "" };
        SettingsValidator.Migrate(s);
        Assert.Equal(SettingsValidator.CurrentVersion, s.Version);
    }

    [Fact]
    public void Settings_Sanitize_FixesInvalidLogLevel()
    {
        var s = new Settings();
        s.DeveloperSettings.LogLevel = "Verbose"; // not a Microsoft log level
        SettingsValidator.Sanitize(s);
        Assert.Equal("Information", s.DeveloperSettings.LogLevel);
    }

    [Theory]
    [InlineData("Debug")]
    [InlineData("Warning")]
    [InlineData("information")] // case-insensitive accept
    public void Settings_Sanitize_KeepsValidLogLevel(string level)
    {
        var s = new Settings();
        s.DeveloperSettings.LogLevel = level;
        SettingsValidator.Sanitize(s);
        Assert.Equal(level, s.DeveloperSettings.LogLevel);
    }

    [Fact]
    public void Settings_Sanitize_ResetsEmptyRepoFields()
    {
        var s = new Settings();
        s.XmlUpdateSettings.XmlRepositoryOwner = "";
        s.XmlUpdateSettings.XmlRepositoryName = "   ";
        s.XmlUpdateSettings.XmlRepositoryBranch = "";
        SettingsValidator.Sanitize(s);
        Assert.False(string.IsNullOrWhiteSpace(s.XmlUpdateSettings.XmlRepositoryOwner));
        Assert.False(string.IsNullOrWhiteSpace(s.XmlUpdateSettings.XmlRepositoryName));
        Assert.False(string.IsNullOrWhiteSpace(s.XmlUpdateSettings.XmlRepositoryBranch));
    }

    [Fact]
    public void Settings_Sanitize_repairs_a_null_DpdUpdateSettings_and_blank_repo_fields()
    {
        // #468: a hand-edited "dpdUpdateSettings": null would NRE DpdUpdateSettingsViewModel's ctor and brick the
        // Settings window (the #319 A7-2 failure mode) — repair like every other section, plus blank repo fields.
        var s1 = new Settings();
        s1.DpdUpdateSettings = null!;
        var fixes1 = SettingsValidator.Sanitize(s1);
        Assert.NotNull(s1.DpdUpdateSettings);
        Assert.False(string.IsNullOrWhiteSpace(s1.DpdUpdateSettings.RepositoryOwner));
        Assert.False(string.IsNullOrWhiteSpace(s1.DpdUpdateSettings.RepositoryName));

        var s2 = new Settings();
        s2.DpdUpdateSettings.RepositoryOwner = "";
        s2.DpdUpdateSettings.RepositoryName = "   ";
        SettingsValidator.Sanitize(s2);
        Assert.False(string.IsNullOrWhiteSpace(s2.DpdUpdateSettings.RepositoryOwner));
        Assert.False(string.IsNullOrWhiteSpace(s2.DpdUpdateSettings.RepositoryName));
    }

    [Fact]
    public void Settings_Sanitize_ClampsNonPositiveFontSizes()
    {
        var s = new Settings();
        s.FontSettings.LocalizationFontSize = 0;
        s.FontSettings.ScriptFonts["Devanagari"].FontSize = -3;
        SettingsValidator.Sanitize(s);
        Assert.True(s.FontSettings.LocalizationFontSize > 0);
        Assert.True(s.FontSettings.ScriptFonts["Devanagari"].FontSize > 0);
    }

    [Fact]
    public void Settings_Sanitize_DropsUnknownScriptFontKeys()
    {
        var s = new Settings();
        s.FontSettings.ScriptFonts["Klingon"] = new ScriptFontSetting { FontSize = 12 };
        s.FontSettings.ScriptFonts["7"] = new ScriptFontSetting { FontSize = 12 };

        SettingsValidator.Sanitize(s);

        Assert.False(s.FontSettings.ScriptFonts.ContainsKey("Klingon"));
        Assert.False(s.FontSettings.ScriptFonts.ContainsKey("7"));
        Assert.True(s.FontSettings.ScriptFonts.ContainsKey("Devanagari")); // real keys kept
        Assert.All(s.FontSettings.ScriptFonts.Keys, k => Assert.True(ScriptKeys.IsValidKey(k)));
    }

    [Fact]
    public void Settings_Sanitize_ClearsPathWithInvalidChars()
    {
        var s = new Settings();
        s.XmlBooksDirectory = "/valid/path";          // kept (non-existence is fine)
        s.IndexDirectory = "bad\0path";               // NUL is an invalid path char
        SettingsValidator.Sanitize(s);
        Assert.Equal("/valid/path", s.XmlBooksDirectory);
        Assert.Equal("", s.IndexDirectory);
    }

    [Fact]
    public void Settings_DefaultIsClean()
        => Assert.Empty(SettingsValidator.Sanitize(new Settings()));

    // STATE-4: every level the UI offers is a level the validator accepts (one canonical list), so a
    // chosen level survives a load/save round-trip instead of being sanitized back to Information.
    [Theory]
    [InlineData("Debug")]
    [InlineData("Information")]
    [InlineData("Warning")]
    [InlineData("Error")]
    [InlineData("Fatal")]   // regressed before STATE-4: not in the validator's MEL-name whitelist
    public void Settings_Sanitize_KeepsSupportedLogLevel(string level)
    {
        var s = new Settings();
        s.DeveloperSettings.LogLevel = level;
        SettingsValidator.Sanitize(s);
        Assert.Equal(level, s.DeveloperSettings.LogLevel);
    }

    [Fact]
    public void Settings_Sanitize_RevertsUnknownLogLevel()
    {
        var s = new Settings();
        s.DeveloperSettings.LogLevel = "Critical"; // an MEL name the parsers never understood
        SettingsValidator.Sanitize(s);
        Assert.Equal("Information", s.DeveloperSettings.LogLevel);
    }

    [Fact]
    public void SettingsValidator_LogLevels_IsTheValidatedSet()
    {
        // The UI binds to SettingsValidator.LogLevels; each must pass Sanitize unchanged.
        foreach (var level in SettingsValidator.LogLevels)
        {
            var s = new Settings();
            s.DeveloperSettings.LogLevel = level;
            SettingsValidator.Sanitize(s);
            Assert.Equal(level, s.DeveloperSettings.LogLevel);
        }
    }

    [Fact]
    public void FontSettings_TryGetFont_TypedLookup()
    {
        var fs = new FontSettings();
        Assert.True(fs.TryGetFont(Script.Devanagari, out var deva));
        Assert.NotNull(deva);
        // A script with no entry (Ipe is not a display script) returns false.
        Assert.False(fs.TryGetFont(Script.Ipe, out _));
    }
}
