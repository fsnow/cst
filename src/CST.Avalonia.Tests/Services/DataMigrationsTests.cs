using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

// #564: #522 renamed the bundled dictionary ids (en -> vri-childers, hi -> vri-hindi) but nothing removed
// the superseded directories, and seeding only writes what is MISSING - so an install carried over from
// beta 5 held both generations and the Settings Dictionary tab listed every entry twice.
//
// This migration DELETES user-visible content, so the guards matter more than the happy path: most of
// these tests pin the cases where it must decline to act.
public class DataMigrationsTests : IDisposable
{
    private readonly string _root;
    private readonly string _data;
    private readonly string _bundled;

    public DataMigrationsTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cst-migrations-" + Guid.NewGuid().ToString("n"));
        _data = Path.Combine(_root, "data");
        _bundled = Path.Combine(_root, "bundled");
        Directory.CreateDirectory(_data);
        Directory.CreateDirectory(_bundled);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private DataMigrations.Context Context(bool withBundled = true) => new()
    {
        DataDirectory = _data,
        BundledDictionariesDirectory = withBundled ? _bundled : null,
    };

    private string DataDict(string id)
    {
        var dir = Path.Combine(_data, "dictionaries", id);
        Directory.CreateDirectory(dir);
        return dir;
    }

    private void SeededDictionary(string id, string txtName = "dict.txt")
    {
        var dir = DataDict(id);
        File.WriteAllText(Path.Combine(dir, txtName), "word\ndefinition\n");
        File.WriteAllText(Path.Combine(dir, "source.json"), "{}");
    }

    private void BundledDictionary(string id)
    {
        Directory.CreateDirectory(Path.Combine(_bundled, id));
    }

    private static bool Applied(ApplicationState s) =>
        s.AppliedDataMigrations.Contains("2026-08-retire-en-hi-dictionary-ids");

    [Fact]
    public void RemovesRetiredIds_WhenTheAppShipsTheirReplacements()
    {
        SeededDictionary("en");
        SeededDictionary("hi");
        SeededDictionary("vri-childers");
        SeededDictionary("vri-hindi");
        BundledDictionary("vri-childers");
        BundledDictionary("vri-hindi");
        var state = new ApplicationState();

        DataMigrations.Run(state, Context());

        Assert.False(Directory.Exists(Path.Combine(_data, "dictionaries", "en")));
        Assert.False(Directory.Exists(Path.Combine(_data, "dictionaries", "hi")));
        Assert.True(Directory.Exists(Path.Combine(_data, "dictionaries", "vri-childers")));
        Assert.True(Directory.Exists(Path.Combine(_data, "dictionaries", "vri-hindi")));
        Assert.True(Applied(state));
    }

    [Fact]
    public void RemovesRetiredId_EvenBeforeTheReplacementHasBeenSeeded()
    {
        // The case that actually matters: on the first launch after upgrading, migrations run before
        // DictionaryService seeds, so the replacement is not in the data directory yet. Keying off the
        // BUNDLED copy is what makes the duplicate disappear that same session instead of the next one.
        SeededDictionary("en");
        BundledDictionary("vri-childers");
        var state = new ApplicationState();

        DataMigrations.Run(state, Context());

        Assert.False(Directory.Exists(Path.Combine(_data, "dictionaries", "en")));
    }

    [Fact]
    public void KeepsRetiredId_WhenTheAppDoesNotShipAReplacement()
    {
        // Removing it here would delete the only copy of that dictionary.
        SeededDictionary("en");
        var state = new ApplicationState();

        var notes = DataMigrations.Run(state, Context());

        Assert.True(Directory.Exists(Path.Combine(_data, "dictionaries", "en")));
        Assert.Contains(notes, n => n.Contains("does not ship"));
    }

    [Fact]
    public void KeepsRetiredId_WhenItHoldsFilesWeDidNotSeed()
    {
        SeededDictionary("en");
        File.WriteAllText(Path.Combine(_data, "dictionaries", "en", "notes.md"), "mine");
        BundledDictionary("vri-childers");
        var state = new ApplicationState();

        var notes = DataMigrations.Run(state, Context());

        Assert.True(Directory.Exists(Path.Combine(_data, "dictionaries", "en")));
        Assert.Contains(notes, n => n.Contains("did not seed"));
    }

    [Fact]
    public void KeepsRetiredId_WhenItHoldsASubdirectory()
    {
        SeededDictionary("en");
        Directory.CreateDirectory(Path.Combine(_data, "dictionaries", "en", "extra"));
        BundledDictionary("vri-childers");
        var state = new ApplicationState();

        DataMigrations.Run(state, Context());

        Assert.True(Directory.Exists(Path.Combine(_data, "dictionaries", "en")));
    }

    [Fact]
    public void DoesNothing_WhenTheBundledDictionariesCannotBeFound()
    {
        // Without sight of what the app ships we cannot tell "superseded" from "the only copy".
        SeededDictionary("en");
        var state = new ApplicationState();

        var notes = DataMigrations.Run(state, Context(withBundled: false));

        Assert.True(Directory.Exists(Path.Combine(_data, "dictionaries", "en")));
        Assert.Contains(notes, n => n.Contains("bundled dictionaries not found"));
    }

    [Fact]
    public void RunsEachMigrationOnlyOnce()
    {
        SeededDictionary("en");
        BundledDictionary("vri-childers");
        var state = new ApplicationState();

        DataMigrations.Run(state, Context());
        var idsAfterFirst = state.AppliedDataMigrations.ToList();

        // A second pass must not re-record the id (and, re-created here, must not act again either).
        SeededDictionary("en");
        var notes = DataMigrations.Run(state, Context());

        Assert.Equal(idsAfterFirst, state.AppliedDataMigrations);
        Assert.Empty(notes);
        Assert.True(Directory.Exists(Path.Combine(_data, "dictionaries", "en")));
    }

    [Fact]
    public void IsIdempotent_WhenTheRecordIsLostButTheWorkIsDone()
    {
        // A data directory restored from backup, or a state file rolled back, can present an already-migrated
        // tree with no record of it. Re-running must be harmless rather than destructive.
        SeededDictionary("vri-childers");
        BundledDictionary("vri-childers");
        var state = new ApplicationState();

        DataMigrations.Run(state, Context());

        Assert.True(Directory.Exists(Path.Combine(_data, "dictionaries", "vri-childers")));
        Assert.True(Applied(state));
    }

    [Fact]
    public void ToleratesAMissingDictionariesDirectory()
    {
        var state = new ApplicationState();

        var notes = DataMigrations.Run(state, Context());

        Assert.True(Applied(state));
        Assert.Contains(notes, n => n.Contains("nothing to do"));
    }

    [Fact]
    public void MigrationIdsAreUniqueAndStable()
    {
        var ids = DataMigrations.All.Select(m => m.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }
}
