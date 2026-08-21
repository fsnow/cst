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

        // Exactly the shape of the real failure: valid JSON, one property this build cannot convert.
        await File.WriteAllTextAsync(SettingsPath,
            """{ "Version": "1.0", "XmlBooksDirectory": "/books", "FontSettings": 12345 }""");

        var reopened = await Loaded(_dir);

        Assert.Equal("/books", reopened.Settings.XmlBooksDirectory);
        Assert.Equal("/idx", reopened.Settings.IndexDirectory);
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

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
