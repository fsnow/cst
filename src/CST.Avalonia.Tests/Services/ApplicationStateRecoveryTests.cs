using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// What happens to <c>application-state.json</c> when it cannot be read. (#877, #879)
///
/// <para>The settings side has had this since #785 — preserve the unreadable file, walk the backups, and
/// make the defaults save leave no backup — and its sibling never received any of it. The loss is a session
/// rather than hand-built configuration, which is why it took longer to matter, not why it does not.</para>
///
/// <para>These are the first tests to exercise this service's load path at all: it had no directory seam, so
/// the existing state tests could only reach the validators. The asymmetry in the tests mirrored the
/// asymmetry in the code, and hid it.</para>
/// </summary>
public sealed class ApplicationStateRecoveryTests : IDisposable
{
    private readonly string _dir;
    private readonly string _statePath;
    private readonly string _backupDir;

    public ApplicationStateRecoveryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"app-state-recovery-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _statePath = Path.Combine(_dir, "application-state.json");
        _backupDir = Path.Combine(_dir, "app-state-backups");
    }

    private ApplicationStateService Service() =>
        new(NullLogger<ApplicationStateService>.Instance, _dir);

    private string[] Backups() =>
        Directory.Exists(_backupDir) ? Directory.GetFiles(_backupDir, "application-state-*.json") : [];

    /// <summary>A state file naming one open book, so a restore can be told from defaults.</summary>
    private static string StateNaming(string book) =>
        $$"""
        { "version": "1.0", "bookWindows": [ { "bookFileName": "{{book}}", "bookIndex": 1 } ] }
        """;

    private static string? OpenBook(ApplicationState state) =>
        state.BookWindows.FirstOrDefault()?.BookFileName;

    // ---- the unreadable file itself (#877a) ------------------------------------------------------------

    /// <summary>
    /// A state file that cannot be read is kept, not overwritten.
    ///
    /// <para>Nothing else keeps it: the very next save — the 60s timer, or the shutdown
    /// <c>ForceSaveAsync</c>, which saves unconditionally — writes over the only copy, and the reader's
    /// session is gone with no way back even by hand.</para>
    /// </summary>
    [Fact]
    public async Task An_unreadable_state_file_is_kept_beside_the_defaults()
    {
        File.WriteAllText(_statePath, "{ this is not json");

        using var service = Service();
        await service.LoadStateAsync();

        var kept = Directory.GetFiles(_dir, "application-state.unreadable-*.json");
        Assert.Single(kept);
        Assert.Contains("this is not json", File.ReadAllText(kept[0]));
    }

    // ---- the defaults save must not become the newest backup (#877b) -----------------------------------

    /// <summary>
    /// The defaults installed after an unreadable file leave no backup behind.
    ///
    /// <para>A backup is written before <b>every</b> save, and <c>TryLoadFromBackupAsync</c> stops at the
    /// first file that deserializes — so without this the defaults become the newest backup and win every
    /// subsequent recovery, with the reader's real session one file below, perfectly readable, never
    /// reached. The expected trigger is a persisted-type change, which breaks the primary file and the
    /// recent backups together, so this is the ordinary case rather than a corner of it.</para>
    /// </summary>
    [Fact]
    public async Task The_defaults_save_after_an_unreadable_file_leaves_no_backup()
    {
        Directory.CreateDirectory(_backupDir);
        File.WriteAllText(_statePath, "{ broken");

        using var service = Service();
        await service.LoadStateAsync();
        Assert.Empty(Backups());          // nothing to recover from, so defaults were installed

        Assert.True(await service.SaveStateAsync());

        Assert.Empty(Backups());
    }

    /// <summary>And the save after that one does back up again — the suppression is for exactly one save,
    /// not a permanent stop, or the reader would silently lose their backup history from then on.</summary>
    [Fact]
    public async Task The_next_save_after_the_defaults_backs_up_again()
    {
        File.WriteAllText(_statePath, "{ broken");

        using var service = Service();
        await service.LoadStateAsync();
        await service.SaveStateAsync();
        await service.SaveStateAsync();

        Assert.Single(Backups());
    }

    // ---- the deserialize-to-null branch (#877c) --------------------------------------------------------

    /// <summary>
    /// A state file that deserializes to null takes the recovery path like any other unreadable file.
    ///
    /// <para><b>The finding that prompted this described the trigger wrongly and it is worth recording.</b>
    /// It called this the empty-file case; an empty or whitespace file does not deserialize to null, it
    /// throws ("The input does not contain any JSON tokens") and has always been caught and recovered — the
    /// test below pins that. The only content that reaches this branch is a file holding the literal token
    /// <c>null</c>, which nothing in the app writes.</para>
    ///
    /// <para>So this is not a bug fixed; it is a divergent path removed. The branch used to report "load
    /// failed" and return without trying the backups, which is a way of giving up that no other unreadable
    /// file gets. Routing it through the same recovery costs nothing and leaves one behaviour instead of
    /// two.</para>
    /// </summary>
    [Fact]
    public async Task A_state_file_holding_literal_null_is_recovered_from_a_backup()
    {
        Directory.CreateDirectory(_backupDir);
        File.WriteAllText(Path.Combine(_backupDir, "application-state-2026-08-28-10-00-00-000.json"),
            StateNaming("dn1.xml"));
        File.WriteAllText(_statePath, "null");

        using var service = Service();
        await service.LoadStateAsync();

        Assert.Equal("dn1.xml", OpenBook(service.Current));
    }

    /// <summary>The case the finding meant: an empty file. It throws rather than deserializing to null, so
    /// it was always recovered — pinned here because the finding claimed otherwise, and the next reader of
    /// that document deserves to find the claim already settled.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public async Task An_empty_state_file_is_recovered_from_a_backup(string content)
    {
        Directory.CreateDirectory(_backupDir);
        File.WriteAllText(Path.Combine(_backupDir, "application-state-2026-08-28-10-00-00-000.json"),
            StateNaming("dn1.xml"));
        File.WriteAllText(_statePath, content);

        using var service = Service();
        await service.LoadStateAsync();

        Assert.Equal("dn1.xml", OpenBook(service.Current));
    }

    /// <summary>The ordinary recovery still works, and returns the session rather than defaults.</summary>
    [Fact]
    public async Task An_unreadable_state_file_is_recovered_from_a_backup()
    {
        Directory.CreateDirectory(_backupDir);
        File.WriteAllText(Path.Combine(_backupDir, "application-state-2026-08-28-10-00-00-000.json"),
            StateNaming("mn1.xml"));
        File.WriteAllText(_statePath, "{ broken");

        using var service = Service();
        await service.LoadStateAsync();

        Assert.Equal("mn1.xml", OpenBook(service.Current));
    }

    // ---- the window preserve-aside opens (#877, fable review) ------------------------------------------

    /// <summary>
    /// A restore is written back to the primary file immediately, not left to the 60s timer.
    ///
    /// <para>Preserving the unreadable file is what makes this necessary, so porting preserve-aside without
    /// it is worse than porting neither. The restore lives only in memory and does not mark the state dirty,
    /// so nothing was scheduled to write it at all — and the primary file has just been moved away. A crash
    /// in that window sends the next launch down the no-file path.</para>
    /// </summary>
    [Fact]
    public async Task A_restore_is_written_back_to_the_primary_file_at_once()
    {
        Directory.CreateDirectory(_backupDir);
        File.WriteAllText(Path.Combine(_backupDir, "application-state-2026-08-28-10-00-00-000.json"),
            StateNaming("sn1.xml"));
        File.WriteAllText(_statePath, "{ broken");

        using var service = Service();
        await service.LoadStateAsync();

        Assert.True(File.Exists(_statePath));
        Assert.Contains("sn1.xml", File.ReadAllText(_statePath));
    }

    /// <summary>
    /// No primary file but backups present is a recovery, not a first run.
    ///
    /// <para>The belt to the write-back's braces: the crash that lands here has already happened, and calling
    /// it a first run loses the session a second time — then this process's own first save writes defaults as
    /// the newest backup, poisoning the walk that would have found it.</para>
    /// </summary>
    [Fact]
    public async Task A_missing_state_file_with_backups_present_is_recovered_not_treated_as_a_first_run()
    {
        Directory.CreateDirectory(_backupDir);
        File.WriteAllText(Path.Combine(_backupDir, "application-state-2026-08-28-10-00-00-000.json"),
            StateNaming("an1.xml"));

        using var service = Service();
        await service.LoadStateAsync();

        Assert.Equal("an1.xml", OpenBook(service.Current));
    }

    /// <summary>A genuine first run is still a first run — the guard above must not turn an empty backup
    /// directory into a recovery attempt that reports something it did not find.</summary>
    [Fact]
    public async Task A_genuine_first_run_is_still_a_first_run()
    {
        using var service = Service();
        await service.LoadStateAsync();

        Assert.Empty(service.Current.BookWindows);
        Assert.False(File.Exists(_statePath));
    }

    /// <summary>
    /// A restored session DOES become the newest backup — only defaults must not.
    ///
    /// <para>Without this, hoisting the <c>_backupNextSave = false</c> above the backup walk would pass every
    /// other test here while quietly suppressing the backup of the one state worth keeping.</para>
    /// </summary>
    [Fact]
    public async Task The_write_back_after_a_restore_does_leave_a_backup()
    {
        Directory.CreateDirectory(_backupDir);
        File.WriteAllText(Path.Combine(_backupDir, "application-state-2026-08-28-10-00-00-000.json"),
            StateNaming("kn1.xml"));
        File.WriteAllText(_statePath, "{ broken");

        using var service = Service();
        await service.LoadStateAsync();

        Assert.Equal(2, Backups().Length);
        Assert.Contains(Backups(), b => File.ReadAllText(b).Contains("kn1.xml"));
    }

    // ---- a change landing mid-save (#879) --------------------------------------------------------------

    /// <summary>
    /// A change made while a save is in flight is not wiped by that save.
    ///
    /// <para><c>SaveStateAsync</c> serializes its snapshot before the first await; the callers used to clear
    /// the dirty flag only after the write returned, so a <c>MarkDirty</c> landing in between was discarded —
    /// the change was not in the snapshot and was no longer marked, so it stayed unsaved until something
    /// else happened to mark the state dirty again. A crash or force-quit in that window lost it.</para>
    ///
    /// <para>Deterministic despite reading like a race: the snapshot and the clear both happen synchronously,
    /// before <c>ForceSaveAsync</c> can return its task, so the <c>MarkDirty</c> below is always the
    /// mid-save one.</para>
    /// </summary>
    [Fact]
    public async Task A_change_made_during_a_save_leaves_the_state_dirty()
    {
        using var service = Service();
        service.MarkDirty();

        var save = service.ForceSaveAsync();
        service.MarkDirty();                  // lands after the snapshot, before the write finishes
        Assert.True(await save);

        Assert.True(service.IsDirty);
    }

    /// <summary>The ordinary save still clears it — otherwise the fix above would mean saving forever.
    /// </summary>
    [Fact]
    public async Task A_save_with_nothing_else_happening_clears_the_flag()
    {
        using var service = Service();
        service.MarkDirty();

        Assert.True(await service.ForceSaveAsync());

        Assert.False(service.IsDirty);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
