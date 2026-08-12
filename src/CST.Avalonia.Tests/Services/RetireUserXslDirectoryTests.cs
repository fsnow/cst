using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

// #42: the app used to copy its stylesheets into the data directory and render from there, so a user could
// edit them. Both reasons anyone did are now settings — the font face (#42) and the text size via per-script
// zoom (#572) — and the app renders from the single bundled stylesheet instead.
//
// The migration DELETES a directory, so the guards matter more than the happy path. It is also the reason
// the directory cannot simply be abandoned: the copy was written once on first run and never refreshed, so
// every existing install still held whatever shipped the day it was first launched. Files that look
// authoritative, are readable, and no longer affect anything are worse than no files at all.
public class RetireUserXslDirectoryTests : IDisposable
{
    private readonly string _root;
    private readonly string _data;

    public RetireUserXslDirectoryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cst-xsl-migration-" + Guid.NewGuid().ToString("n"));
        _data = Path.Combine(_root, "data");
        Directory.CreateDirectory(_data);
    }

    public void Dispose()
    {
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }

    private DataMigrations.Context Context() => new()
    {
        DataDirectory = _data,
        BundledDictionariesDirectory = null,
    };

    private string MakeXslDir(int fileCount = 14)
    {
        var dir = Path.Combine(_data, "xsl");
        Directory.CreateDirectory(dir);
        for (var i = 0; i < fileCount; i++)
            File.WriteAllText(Path.Combine(dir, $"tipitaka-{i:00}.xsl"), "<xsl:stylesheet/>");
        return dir;
    }

    private static ApplicationState FreshState() => new();

    private static IReadOnlyList<string> RunOnlyThisMigration(ApplicationState state, DataMigrations.Context ctx) =>
        DataMigrations.Run(state, ctx,
            DataMigrations.All.Where(m => m.Id == "2026-08-retire-user-xsl-directory"));

    [Fact]
    public void ItMovesTheDirectoryAsideAndSaysHowMuchItMoved()
    {
        var dir = MakeXslDir();
        var notes = RunOnlyThisMigration(FreshState(), Context());

        Assert.False(Directory.Exists(dir));
        Assert.True(Directory.Exists(Path.Combine(_data, "xsl-retired")));
        Assert.Contains(notes, n => n.Contains("14"));
        Assert.DoesNotContain(notes, n => n.Contains(DataMigrations.FailureMarker));
    }

    [Fact]
    public void UserEditsSurviveTheRetirement()
    {
        // THE point of moving rather than deleting. Every other assertion in this file is satisfied just as
        // well by a recursive delete, so without this the behaviour is unpinned and a revert stays green.
        var dir = MakeXslDir(1);
        File.WriteAllText(Path.Combine(dir, "tipitaka-00.xsl"), "<!-- a user's hand edits -->");

        RunOnlyThisMigration(FreshState(), Context());

        var moved = Path.Combine(_data, "xsl-retired", "tipitaka-00.xsl");
        Assert.True(File.Exists(moved), "The retired directory must still hold the user's files.");
        Assert.Equal("<!-- a user's hand edits -->", File.ReadAllText(moved));
    }

    [Fact]
    public void AnExistingRetiredCopyIsNeverOverwrittenOrDeleted()
    {
        // Both directories existing does NOT prove the current one is the stale one: a still-installed
        // older build recreates xsl/ on launch, so after a state-file restore the CURRENT directory can
        // hold the newer edits. Deleting either side would destroy the work this migration protects.
        Directory.CreateDirectory(Path.Combine(_data, "xsl-retired"));
        File.WriteAllText(Path.Combine(_data, "xsl-retired", "old.xsl"), "the earlier retirement");

        var dir = MakeXslDir(1);
        File.WriteAllText(Path.Combine(dir, "tipitaka-00.xsl"), "the newer edits");

        RunOnlyThisMigration(FreshState(), Context());

        Assert.Equal("the earlier retirement",
            File.ReadAllText(Path.Combine(_data, "xsl-retired", "old.xsl")));
        Assert.Equal("the newer edits",
            File.ReadAllText(Path.Combine(_data, "xsl-retired-2", "tipitaka-00.xsl")));
        Assert.False(Directory.Exists(dir));
    }

    [Fact]
    public void EverythingInTheDirectoryMovesWithIt_NotJustStylesheets()
    {
        // Whatever else was parked in there travels too rather than being left behind or dropped.
        var dir = MakeXslDir(2);
        File.WriteAllText(Path.Combine(dir, "notes.txt"), "a user's scratch file");

        RunOnlyThisMigration(FreshState(), Context());

        Assert.False(Directory.Exists(dir));
        Assert.Equal("a user's scratch file",
            File.ReadAllText(Path.Combine(_data, "xsl-retired", "notes.txt")));
    }

    [Fact]
    public void AFreshInstallIsANoOp()
    {
        // Nothing to remove, and it must still record as done rather than retrying every launch forever.
        var state = FreshState();
        var notes = RunOnlyThisMigration(state, Context());

        Assert.Contains("2026-08-retire-user-xsl-directory", state.AppliedDataMigrations!);
        Assert.DoesNotContain(notes, n => n.Contains(DataMigrations.FailureMarker));
    }

    [Fact]
    public void ItIsIdempotent()
    {
        // The recorded id is the primary guard, but a migration can meet a half-finished state from an
        // interrupted run or a data directory restored from backup.
        MakeXslDir();
        var state = FreshState();

        RunOnlyThisMigration(state, Context());
        var second = RunOnlyThisMigration(state, Context());

        Assert.DoesNotContain(second, n => n.Contains(DataMigrations.FailureMarker));
        Assert.False(Directory.Exists(Path.Combine(_data, "xsl")));
    }

    [Fact]
    public void ItRecordsItselfSoItDoesNotRunAgain()
    {
        MakeXslDir();
        var state = FreshState();
        RunOnlyThisMigration(state, Context());

        Assert.Single(state.AppliedDataMigrations!.Where(id => id == "2026-08-retire-user-xsl-directory"));
    }

    [Fact]
    public void ItTouchesNothingElseInTheDataDirectory()
    {
        // It deletes by a fixed relative path, but this is the assertion that would catch a future edit
        // widening that path.
        MakeXslDir(3);
        var dictionaries = Path.Combine(_data, "dictionaries");
        Directory.CreateDirectory(dictionaries);
        File.WriteAllText(Path.Combine(dictionaries, "keep-me.txt"), "user content");
        var settings = Path.Combine(_data, "settings.json");
        File.WriteAllText(settings, "{}");

        RunOnlyThisMigration(FreshState(), Context());

        Assert.True(File.Exists(Path.Combine(dictionaries, "keep-me.txt")));
        Assert.True(File.Exists(settings));
        Assert.False(Directory.Exists(Path.Combine(_data, "xsl")));
    }

    [Fact]
    public void ItIsRegisteredInTheMigrationList()
    {
        // Guards against the implementation existing but never being wired into DataMigrations.All, which
        // would leave every install carrying the stale directory with the tests still green.
        Assert.Contains(DataMigrations.All, m => m.Id == "2026-08-retire-user-xsl-directory");
    }
}
