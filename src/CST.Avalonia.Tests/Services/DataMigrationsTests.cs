using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

// #564: #522 renamed the bundled dictionary ids (en -> vri-childers, hi -> vri-hindi) but nothing removed
// the superseded directories, and seeding only writes what is MISSING - so an install that already had
// them keeps both generations and the Settings Dictionary tab lists every entry twice. The affected
// installs are development/source builds from 2026-07-01..07-27; no released build ever seeded en/hi.
//
// This migration DELETES user-visible content, so the guards matter far more than the happy path: most of
// these tests pin the cases where it must decline to act.
public class DataMigrationsTests : IDisposable
{
    private const string TxtName = "dict.txt";
    private const string ShippedContent = "word\ndefinition\n";

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

    private void SeededDictionary(string id, string content = ShippedContent)
    {
        var dir = DataDict(id);
        File.WriteAllText(Path.Combine(dir, TxtName), content);
        File.WriteAllText(Path.Combine(dir, "source.json"), "{}");
    }

    // Redundancy is judged by byte-equality against what the app SHIPS, so the bundled copy has to carry
    // the same file and content the seeded copy would.
    private void BundledDictionary(string id, string content = ShippedContent)
    {
        var dir = Path.Combine(_bundled, id);
        Directory.CreateDirectory(dir);
        File.WriteAllText(Path.Combine(dir, TxtName), content);
        File.WriteAllText(Path.Combine(dir, "source.json"), "{}");
    }

    private static bool Applied(ApplicationState s) =>
        s.AppliedDataMigrations.Contains("2026-08-retire-en-hi-dictionary-ids");

    private string DataPath(string id) => Path.Combine(_data, "dictionaries", id);

    // ===== the happy path =====

    [Fact]
    public void RemovesRetiredIds_WhenTheyMatchWhatTheAppShips()
    {
        SeededDictionary("en");
        SeededDictionary("hi");
        SeededDictionary("vri-childers");
        SeededDictionary("vri-hindi");
        BundledDictionary("vri-childers");
        BundledDictionary("vri-hindi");
        var state = new ApplicationState();

        DataMigrations.Run(state, Context());

        Assert.False(Directory.Exists(DataPath("en")));
        Assert.False(Directory.Exists(DataPath("hi")));
        Assert.True(Directory.Exists(DataPath("vri-childers")));
        Assert.True(Directory.Exists(DataPath("vri-hindi")));
        Assert.True(Applied(state));
    }

    [Fact]
    public void RemovesRetiredId_EvenWhenTheReplacementIsNotInTheDataDirectoryYet()
    {
        // Judged against the BUNDLED copy, so this does not depend on DictionaryService having seeded first.
        SeededDictionary("en");
        BundledDictionary("vri-childers");
        var state = new ApplicationState();

        DataMigrations.Run(state, Context());

        Assert.False(Directory.Exists(DataPath("en")));
    }

    [Fact]
    public void RemovesRetiredId_WhenOnlySourceJsonDiffers()
    {
        // source.json is app-owned metadata that seeding already refreshes from the bundle, so a difference
        // there is ours, not the user's - it must not block the cleanup.
        SeededDictionary("en");
        File.WriteAllText(Path.Combine(DataPath("en"), "source.json"), "{\"displayName\":\"older\"}");
        BundledDictionary("vri-childers");
        var state = new ApplicationState();

        DataMigrations.Run(state, Context());

        Assert.False(Directory.Exists(DataPath("en")));
    }

    // ===== the guards that stop this deleting real content =====

    [Fact]
    public void KeepsRetiredId_WhenItHoldsAUserAddedDictionaryFile()
    {
        // The loader merges EVERY *.txt in a dictionary directory, so a glossary dropped into en/ is live
        // content. An earlier draft whitelisted "any .txt" and would have deleted it.
        SeededDictionary("en");
        File.WriteAllText(Path.Combine(DataPath("en"), "my-glossary.txt"), "mine\nown notes\n");
        BundledDictionary("vri-childers");
        var state = new ApplicationState();

        var notes = DataMigrations.Run(state, Context());

        Assert.True(File.Exists(Path.Combine(DataPath("en"), "my-glossary.txt")));
        Assert.Contains(notes, n => n.Contains("my-glossary.txt") && n.Contains("does not ship"));
        // Declining is not finishing: recording it would freeze the duplicate listing in permanently, even
        // after the user removes the glossary.
        Assert.False(Applied(state));
    }

    [Fact]
    public void KeepsRetiredId_WhenTheDictionaryFileWasEditedInPlace()
    {
        // Seeding is write-if-missing precisely so an existing install's data is never clobbered, so an
        // edited dictionary keeps its edits - and the shipped replacement does not have them.
        SeededDictionary("en", content: "word\nMY EDITED DEFINITION\n");
        BundledDictionary("vri-childers", content: ShippedContent);
        var state = new ApplicationState();

        var notes = DataMigrations.Run(state, Context());

        Assert.True(Directory.Exists(DataPath("en")));
        Assert.Contains(notes, n => n.Contains("differs from the shipped copy"));
        Assert.False(Applied(state));
    }

    [Fact]
    public void KeepsRetiredId_WhenTheAppDoesNotShipAReplacement()
    {
        // Removing it here would delete the only copy of that dictionary.
        SeededDictionary("en");
        var state = new ApplicationState();

        var notes = DataMigrations.Run(state, Context());

        Assert.True(Directory.Exists(DataPath("en")));
        Assert.Contains(notes, n => n.Contains("does not ship"));
        Assert.False(Applied(state));
    }

    [Fact]
    public void KeepsRetiredId_WhenItHoldsASubdirectory()
    {
        SeededDictionary("en");
        Directory.CreateDirectory(Path.Combine(DataPath("en"), "extra"));
        BundledDictionary("vri-childers");
        var state = new ApplicationState();

        var notes = DataMigrations.Run(state, Context());

        Assert.True(Directory.Exists(DataPath("en")));
        Assert.Contains(notes, n => n.Contains("subdirectory"));
        Assert.False(Applied(state));
    }

    // ===== runner contract: retry vs record =====

    [Fact]
    public void DoesNotRecordAMigrationThatDefers()
    {
        // Without sight of the bundled dictionaries we cannot tell "superseded" from "the only copy". That
        // is an environment problem, and recording it would forfeit the cleanup permanently.
        SeededDictionary("en");
        var state = new ApplicationState();

        var notes = DataMigrations.Run(state, Context(withBundled: false));

        Assert.True(Directory.Exists(DataPath("en")));
        Assert.False(Applied(state));
        Assert.Contains(notes, n => n.Contains("deferring"));
    }

    [Fact]
    public void RetriesADeferredMigration_AndSucceedsOnceTheEnvironmentIsReady()
    {
        SeededDictionary("en");
        var state = new ApplicationState();

        DataMigrations.Run(state, Context(withBundled: false));
        Assert.False(Applied(state));
        Assert.True(Directory.Exists(DataPath("en")));

        BundledDictionary("vri-childers");
        DataMigrations.Run(state, Context());

        Assert.True(Applied(state));
        Assert.False(Directory.Exists(DataPath("en")));
    }

    [Fact]
    public void AThrowingMigrationIsNotRecordedAndDoesNotBlockTheOthers()
    {
        var state = new ApplicationState();
        var ran = new List<string>();
        var migrations = new[]
        {
            new DataMigrations.Migration("boom", "throws",
                (_, _) => throw new InvalidOperationException("nope")),
            new DataMigrations.Migration("after", "runs anyway", (_, notes) =>
            {
                ran.Add("after");
                notes.Add("after ran");
                return DataMigrations.Outcome.Done;
            }),
        };

        var notes = DataMigrations.Run(state, Context(), migrations);

        Assert.Contains("after", ran);
        Assert.DoesNotContain("boom", state.AppliedDataMigrations);
        Assert.Contains("after", state.AppliedDataMigrations);
        Assert.Contains(notes, n => n.Contains("FAILED") && n.Contains("nope"));
    }

    [Fact]
    public void RunsEachMigrationOnlyOnce()
    {
        SeededDictionary("en");
        BundledDictionary("vri-childers");
        var state = new ApplicationState();

        DataMigrations.Run(state, Context());
        var idsAfterFirst = state.AppliedDataMigrations.ToList();

        // Re-created here: a second pass must neither re-record nor act again.
        SeededDictionary("en");
        var notes = DataMigrations.Run(state, Context());

        Assert.Equal(idsAfterFirst, state.AppliedDataMigrations);
        Assert.Empty(notes);
        Assert.True(Directory.Exists(DataPath("en")));
    }

    // ===== idempotency and edge cases =====

    [Fact]
    public void IsIdempotent_WhenTheRecordIsLostButTheWorkIsDone()
    {
        // A data directory restored from backup, or a state file rolled back, can present an
        // already-migrated tree with no record of it. Re-running must be harmless.
        SeededDictionary("vri-childers");
        BundledDictionary("vri-childers");
        var state = new ApplicationState();

        DataMigrations.Run(state, Context());

        Assert.True(Directory.Exists(DataPath("vri-childers")));
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
    public void ToleratesANullMigrationList()
    {
        // A hand-edited state file can null the property out.
        var state = new ApplicationState { AppliedDataMigrations = null! };

        DataMigrations.Run(state, Context());

        Assert.NotNull(state.AppliedDataMigrations);
        Assert.True(Applied(state));
    }

    [Fact]
    public void MigrationIdsAreUniqueAndStable()
    {
        var ids = DataMigrations.All.Select(m => m.Id).ToList();
        Assert.Equal(ids.Count, ids.Distinct(StringComparer.Ordinal).Count());
        Assert.All(ids, id => Assert.False(string.IsNullOrWhiteSpace(id)));
    }

    [Fact]
    public void CleansUpOnALaterLaunch_OnceTheReasonForKeepingIsGone()
    {
        // The point of not recording a decline: the user removes their added file, and the next launch
        // finishes the job instead of the duplicate listing being frozen in forever.
        SeededDictionary("en");
        var glossary = Path.Combine(DataPath("en"), "my-glossary.txt");
        File.WriteAllText(glossary, "my own notes");
        BundledDictionary("vri-childers");
        var state = new ApplicationState();

        DataMigrations.Run(state, Context());
        Assert.True(Directory.Exists(DataPath("en")));
        Assert.False(Applied(state));

        File.Delete(glossary);
        DataMigrations.Run(state, Context());

        Assert.False(Directory.Exists(DataPath("en")));
        Assert.True(Applied(state));
    }

    [Theory]
    [InlineData(".DS_Store")]
    [InlineData("Thumbs.db")]
    [InlineData("desktop.ini")]
    [InlineData(".localized")]
    public void RemovesRetiredId_DespiteOperatingSystemJunkFiles(string junk)
    {
        // A dictionaries folder that was ever browsed in Finder or Explorer picks these up. Treating them
        // as user content would make the migration a permanent no-op on exactly the developer installs it
        // targets.
        SeededDictionary("en");
        File.WriteAllText(Path.Combine(DataPath("en"), junk), "junk");
        BundledDictionary("vri-childers");
        var state = new ApplicationState();

        DataMigrations.Run(state, Context());

        Assert.False(Directory.Exists(DataPath("en")));
        Assert.True(Applied(state));
    }

    [Fact]
    public void ADeferredMigrationDoesNotBlockLaterOnes()
    {
        var state = new ApplicationState();
        var migrations = new[]
        {
            new DataMigrations.Migration("defers", "not ready", (_, notes) =>
            {
                notes.Add("deferred on purpose");
                return DataMigrations.Outcome.Retry;
            }),
            new DataMigrations.Migration("after", "runs anyway", (_, notes) =>
            {
                notes.Add("after ran");
                return DataMigrations.Outcome.Done;
            }),
        };

        DataMigrations.Run(state, Context(), migrations);

        Assert.DoesNotContain("defers", state.AppliedDataMigrations);
        Assert.Contains("after", state.AppliedDataMigrations);
    }

    [Fact]
    public void FailureNotesCarryTheSharedMarkerTheLoggerKeysOff()
    {
        // App.RunDataMigrationsAsync raises these to Error by matching DataMigrations.FailureMarker.
        var state = new ApplicationState();
        var migrations = new[]
        {
            new DataMigrations.Migration("boom", "throws",
                (_, _) => throw new InvalidOperationException("nope")),
        };

        var notes = DataMigrations.Run(state, Context(), migrations);

        Assert.Contains(notes, n => n.Contains(DataMigrations.FailureMarker, StringComparison.Ordinal));
    }

    [Fact]
    public void PartialSuccess_DeletesTheCleanDirectory_KeepsTheOther_AndRecordsNothing()
    {
        // The mixed case: one retired id is redundant, the other is not. The successful half must still
        // happen, and the migration must stay unrecorded so the kept half is re-evaluated later.
        SeededDictionary("en");
        File.WriteAllText(Path.Combine(DataPath("en"), "my-glossary.txt"), "my own notes");
        SeededDictionary("hi");
        BundledDictionary("vri-childers");
        BundledDictionary("vri-hindi");
        var state = new ApplicationState();

        DataMigrations.Run(state, Context());

        Assert.False(Directory.Exists(DataPath("hi")));   // clean: removed
        Assert.True(Directory.Exists(DataPath("en")));    // has user content: kept
        Assert.False(Applied(state));                     // unrecorded, so en/ gets another look
    }

    [Fact]
    public void PartialSuccess_ReRunIsANoOpForTheHalfAlreadyDone()
    {
        // Because nothing was recorded, the whole migration runs again next launch. The already-deleted
        // directory must not produce a second note or any further action.
        SeededDictionary("en");
        var glossary = Path.Combine(DataPath("en"), "my-glossary.txt");
        File.WriteAllText(glossary, "my own notes");
        SeededDictionary("hi");
        BundledDictionary("vri-childers");
        BundledDictionary("vri-hindi");
        var state = new ApplicationState();

        DataMigrations.Run(state, Context());

        var second = DataMigrations.Run(state, Context());
        Assert.DoesNotContain(second, n => n.Contains("'hi'"));

        // ...and once the blocker is gone, the remaining half completes and the migration is recorded.
        File.Delete(glossary);
        DataMigrations.Run(state, Context());

        Assert.False(Directory.Exists(DataPath("en")));
        Assert.True(Applied(state));
    }
}
