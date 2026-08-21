using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// The same question as <see cref="OlderSettingsFilesLoadTests"/>, for the other persisted store: can this
/// build load an <c>app-state.json</c> an earlier build wrote? (#787)
///
/// <para><b>Why this one needs saying twice.</b> <c>ApplicationStateService</c> looks protected, because a
/// failed load falls back through timestamped backups until one deserializes. But the backups are written in
/// the SAME shape as the primary file — so a persisted-type change breaks every one of them at once, and the
/// recovery walks a whole directory of files it also cannot read before defaulting. Backup recovery and this
/// test are complements, not alternatives.</para>
///
/// <para>These exercise deserialize + migrate + sanitize, which is what the service's load path does with the
/// file's bytes; the file IO around it is covered elsewhere.</para>
/// </summary>
public sealed class OlderApplicationStateFilesLoadTests
{
    // THE SERVICE'S OWN OPTIONS, not a copy of them. A mirrored copy cannot detect drift from the original —
    // change the naming policy or drop the enum converter and every real file on disk stops loading while this
    // suite stays green, because fixture and copy moved together. That is precisely the class of regression
    // these tests exist to catch, so mirroring would have made them decorative. (#787, fable)
    private static readonly JsonSerializerOptions Options = ApplicationStateService.JsonOptions;

    private static ApplicationState Load(string json)
    {
        var state = JsonSerializer.Deserialize<ApplicationState>(json, Options);
        Assert.NotNull(state);
        ApplicationStateValidator.Migrate(state!);
        ApplicationStateValidator.Sanitize(state!);
        return state!;
    }

    // The guard against the whole class silently passing: if the options above drifted from the service's, a
    // camelCase fixture would deserialize to defaults and every assertion below would still hold.
    [Fact]
    public void The_fixtures_really_deserialize_rather_than_yielding_defaults()
    {
        var state = Load("""{ "version": "1.0", "mainWindow": { "width": 1234 } }""");
        Assert.Equal(1234, state.MainWindow.Width);
    }

    // The riskiest thing in a REAL old state file, and nothing covered it: enum strings.
    //
    // BookScript, CurrentScript and SearchMode carry their own converter attributes, but
    // MainWindowState.WindowState and BookWindowState.WindowState depend on the GLOBAL
    // JsonStringEnumConverter in the service's options alone — and every real file whose window was maximized
    // carries one. Drop or reconfigure that converter and every such file throws on load, which destroys the
    // primary file AND invalidates every backup at once, since the backups share its shape. That is the exact
    // scenario this class exists to prevent, and hand-written minimal fixtures were blind to it. (#787, fable)
    [Fact]
    public void Enum_values_written_as_strings_still_load()
    {
        var state = Load("""
        {
          "version": "1.0",
          "mainWindow": { "width": 1400, "height": 900, "windowState": "Maximized" },
          "searchDialog": { "searchMode": "Regex" },
          "bookWindows": [ {
            "bookIndex": 3, "bookScript": "Thai", "windowState": "Maximized" } ]
        }
        """);

        Assert.Equal(CST.Avalonia.Models.WindowState.Maximized, state.MainWindow.WindowState);
        Assert.Equal(CST.Avalonia.Models.SearchMode.Regex, state.SearchDialog.SearchMode);
        var book = Assert.Single(state.BookWindows);
        Assert.Equal(CST.Conversion.Script.Thai, book.BookScript);
        Assert.Equal(CST.Avalonia.Models.WindowState.Maximized, book.WindowState);
    }

    [Fact]
    public void A_state_file_with_no_dictionary_or_ai_sections_loads()
    {
        // Sections are added over time; an older file simply lacks the newer ones.
        var state = Load("""
        {
          "version": "1.0",
          "mainWindow": { "width": 1400, "height": 900 }
        }
        """);

        Assert.Equal(1400, state.MainWindow.Width);
        Assert.NotNull(state.SettingsWindow);
        Assert.NotNull(state.DictionaryDialog);
    }

    [Fact]
    public void Properties_this_build_does_not_know_are_ignored_not_fatal()
    {
        // The likelier failure than corruption: a file written by a NEWER build, or restored from one.
        var state = Load("""
        {
          "version": "9.9",
          "mainWindow": { "width": 1400, "somethingNewer": { "a": [1, 2] } },
          "aFutureSection": { "b": "c" }
        }
        """);

        Assert.Equal(1400, state.MainWindow.Width);
    }

    [Fact]
    public void A_null_collection_does_not_come_back_null_for_the_next_thing_to_enumerate()
    {
        // #319's shape: a null section reaching code that enumerates it. Sanitize is what must absorb this,
        // and an explicit null is the case that skips converters and defaults alike.
        var state = Load("""
        {
          "version": "1.0",
          "dictionaryDialog": { "sourceOrder": null },
          "openBookDialog": { "expandedNodeKeys": null },
          "searchDialog": { "selectedTerms": null },
          "appliedDataMigrations": null,
          "bookWindows": [ { "bookIndex": 1, "searchTerms": null, "searchPositions": null } ],
          "mainWindow": { "width": 1400 }
        }
        """);

        // A property initializer runs when the object is constructed and is then OVERWRITTEN by an explicit
        // JSON null, so "= new()" on the model is no defence at all. Sanitize is what must absorb it.
        Assert.NotNull(state.DictionaryDialog.SourceOrder);
        Assert.Empty(state.DictionaryDialog.SourceOrder);
        Assert.NotNull(state.OpenBookDialog.ExpandedNodeKeys);
        Assert.NotNull(state.SearchDialog.SelectedTerms);
        Assert.NotNull(state.AppliedDataMigrations);
        Assert.NotNull(Assert.Single(state.BookWindows).SearchTerms);
        Assert.NotNull(Assert.Single(state.BookWindows).SearchPositions);
    }

    // A null ENTRY is the same mechanism one level in, and coalescing the list does nothing for it. The
    // dictionary picker dereferences pref.Id directly. (#787, fable)
    [Fact]
    public void Null_entries_inside_those_collections_are_dropped()
    {
        var state = Load("""
        {
          "version": "1.0",
          "dictionaryDialog": { "sourceOrder": [ null, { "id": null }, { "id": "dpd", "enabled": true } ] },
          "searchDialog": { "selectedTerms": [ null, "dhamma" ] },
          "openBookDialog": { "expandedNodeKeys": [ null, "n1" ] },
          "bookWindows": [ { "bookIndex": 1, "searchTerms": [ null, "x" ] } ]
        }
        """);

        var pref = Assert.Single(state.DictionaryDialog.SourceOrder);
        Assert.Equal("dpd", pref.Id);
        Assert.DoesNotContain(null, state.SearchDialog.SelectedTerms);
        Assert.DoesNotContain(null, state.OpenBookDialog.ExpandedNodeKeys);
        Assert.DoesNotContain(null, Assert.Single(state.BookWindows).SearchTerms);
    }

    [Fact]
    public void An_empty_object_loads_as_a_usable_default_state()
    {
        var state = Load("{}");

        Assert.NotNull(state.MainWindow);
        Assert.NotNull(state.DictionaryDialog.SourceOrder);
    }
}
