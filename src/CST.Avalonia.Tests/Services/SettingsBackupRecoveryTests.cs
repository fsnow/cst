using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// Settings survive a file this build cannot read. (#785)
///
/// <para><b>The incident.</b> A persisted type changed (#771) and a settings file written the previous day
/// stopped parsing. <c>SettingsService</c> answered that by replacing the settings with defaults and SAVING
/// them — so a configuration was not merely unavailable, it was destroyed, and a hand-built model list had to
/// be reconstructed by hand.</para>
///
/// <para><b>Why moving the file aside is not the fix.</b> It is necessary and it is not sufficient: a file
/// called <c>settings.unreadable-….json</c> in the application support directory is a rescue only someone who
/// wrote this code would ever find, and by the time a reader thought to look they would already have rebuilt
/// what they lost. Recovery has to be automatic, which is what <c>ApplicationStateService</c> has always done
/// and settings never did.</para>
/// </summary>
public sealed class SettingsBackupRecoveryTests : IDisposable
{
    private readonly string _dir;

    public SettingsBackupRecoveryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"settings-backup-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    private string SettingsPath => Path.Combine(_dir, "settings.json");

    private static async Task<SettingsService> Loaded(string dir)
    {
        var svc = new SettingsService(dir);
        await svc.LoadSettingsAsync();
        return svc;
    }

    // Establish a real backup the way the app does: save once.
    private async Task<SettingsService> WithOneGoodSave(string booksDirectory)
    {
        var svc = await Loaded(_dir);
        svc.Settings.XmlBooksDirectory = booksDirectory;
        svc.Settings.IndexDirectory = "/idx";
        await svc.SaveSettingsAsync();
        return svc;
    }

    [Fact]
    public async Task A_successful_save_leaves_a_backup_behind()
    {
        var svc = await WithOneGoodSave("/books");

        var backups = svc.GetBackupFilePaths();
        Assert.NotEmpty(backups);
        Assert.Contains("\"/books\"", await File.ReadAllTextAsync(backups[0]));
    }

    // The incident, replayed: the file becomes unreadable between sessions.
    [Fact]
    public async Task An_unreadable_file_is_recovered_from_the_backup_rather_than_defaulted()
    {
        await WithOneGoodSave("/books");

        // Unreadable as a DOCUMENT, so there is nothing to salvage and the backup is the only answer.
        //
        // This test used to write valid JSON with one unconvertible property, which is the incident's own
        // shape — and #803 now keeps such a file rather than replacing it with a previous save, which is the
        // better outcome and is asserted in TolerantSettingsLoadTests. The backup path is for what tolerance
        // cannot reach, and the fixture has to be that.
        await File.WriteAllTextAsync(SettingsPath, "{ torn write, not json");

        var reopened = await Loaded(_dir);

        Assert.Equal("/books", reopened.Settings.XmlBooksDirectory);
        Assert.Equal("/idx", reopened.Settings.IndexDirectory);
    }

    // The routing between the two, stated once: a file that can be partly read is partly read, and only what
    // cannot be read at all falls back to an older copy. The order matters because a backup is a PREVIOUS
    // save — recovering from one loses everything changed since, while keeping what parses loses only the
    // part genuinely unreadable. (#803)
    [Fact]
    public async Task A_partly_readable_file_is_kept_rather_than_replaced_by_an_older_backup()
    {
        await WithOneGoodSave("/books");

        // Everything the reader has now, plus one property this build cannot convert.
        await File.WriteAllTextAsync(SettingsPath,
            """
            { "Version": "1.0", "XmlBooksDirectory": "/new-books", "IndexDirectory": "/new-idx",
              "FontSettings": 12345 }
            """);

        var reopened = await Loaded(_dir);

        // The CURRENT values, not the backup's — the reader's recent changes survive.
        Assert.Equal("/new-books", reopened.Settings.XmlBooksDirectory);
        Assert.Equal("/new-idx", reopened.Settings.IndexDirectory);
    }

    [Fact]
    public async Task The_unreadable_file_is_kept_as_well_as_recovered_from()
    {
        await WithOneGoodSave("/books");
        await File.WriteAllTextAsync(SettingsPath, "{ not json at all");

        await Loaded(_dir);

        // Both halves: the reader gets their settings back AND the file that failed is still on disk, so the
        // cause remains diagnosable rather than being destroyed by the recovery.
        Assert.NotEmpty(Directory.GetFiles(_dir, "settings.unreadable-*.json"));
    }

    [Fact]
    public async Task A_backup_that_is_also_unreadable_is_skipped_for_an_older_one()
    {
        await WithOneGoodSave("/books");
        var good = (await Loaded(_dir)).GetBackupFilePaths().Single();

        // A newer backup that is itself broken — which is what a persisted-type change produces, since every
        // backup shares the primary file's shape.
        var newer = Path.Combine(Path.GetDirectoryName(good)!, "settings-99999999-235959-999.json");
        await File.WriteAllTextAsync(newer, "{ broken");
        await File.WriteAllTextAsync(SettingsPath, "{ broken too");

        var reopened = await Loaded(_dir);

        Assert.Equal("/books", reopened.Settings.XmlBooksDirectory);
    }

    // With nothing to recover from, the floor still has to hold.
    [Fact]
    public async Task With_no_backup_at_all_the_unreadable_file_is_still_kept_and_defaults_are_used()
    {
        await File.WriteAllTextAsync(SettingsPath, "{ broken");

        var svc = await Loaded(_dir);

        Assert.NotEmpty(Directory.GetFiles(_dir, "settings.unreadable-*.json"));
        Assert.False(string.IsNullOrEmpty(svc.Settings.XmlBooksDirectory));   // first-run defaulting ran
    }

    [Fact]
    public async Task An_ordinary_load_does_not_touch_the_backups()
    {
        await WithOneGoodSave("/books");
        var before = (await Loaded(_dir)).GetBackupFilePaths().Length;

        await Loaded(_dir);

        Assert.Equal(before, (await Loaded(_dir)).GetBackupFilePaths().Length);
        Assert.Empty(Directory.GetFiles(_dir, "settings.unreadable-*.json"));
    }

    // Fable's lead finding: RequestSave only ARMS a 750ms timer, so after a restore there was a window with
    // no primary file at all. A crash or force-quit inside it sent the next launch down the no-file branch,
    // which called it a first run — the recovery evaporating silently, and the reader losing everything a
    // second time. (#785)
    [Fact]
    public async Task A_restore_is_written_back_immediately_not_on_the_debounce_timer()
    {
        await WithOneGoodSave("/books");
        await File.WriteAllTextAsync(SettingsPath, "{ broken");

        await Loaded(_dir);

        // No waiting, no flush: by the time the load returns, the primary file is on disk again.
        Assert.True(File.Exists(SettingsPath));
        Assert.Contains("\"/books\"", await File.ReadAllTextAsync(SettingsPath));
    }

    // And the belt to that braces: even if the write-back never happened, backups are consulted rather than
    // the launch being treated as a first run.
    [Fact]
    public async Task A_missing_primary_file_with_backups_present_is_recovered_not_treated_as_a_first_run()
    {
        await WithOneGoodSave("/books");
        File.Delete(SettingsPath);

        var reopened = await Loaded(_dir);

        Assert.Equal("/books", reopened.Settings.XmlBooksDirectory);
        Assert.Equal("/idx", reopened.Settings.IndexDirectory);
    }

    // A genuine first run must still be a first run — the check above must not turn an empty backup directory
    // into a recovery attempt that reports something it did not do.
    [Fact]
    public async Task A_genuine_first_run_is_still_a_first_run()
    {
        var svc = await Loaded(_dir);

        Assert.Empty(svc.GetBackupFilePaths());
        Assert.False(string.IsNullOrEmpty(svc.Settings.XmlBooksDirectory));
        Assert.Equal("", svc.Settings.IndexDirectory);
    }

    // Fable's second finding: when the walk finds nothing, the defaults that follow must not become the
    // NEWEST backup. A persisted-type change breaks every backup at once — the expected case — so defaults
    // would bury a genuinely configured older backup that the walk stops before ever reaching.
    [Fact]
    public async Task Defaults_written_because_nothing_could_be_recovered_do_not_become_a_backup()
    {
        await File.WriteAllTextAsync(SettingsPath, "{ broken");

        var svc = await Loaded(_dir);
        await svc.SaveSettingsAsync();      // the debounced save, brought forward

        Assert.Empty(svc.GetBackupFilePaths());
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
