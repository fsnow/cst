using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;
using Avalonia.Threading;
using CST.Avalonia.Constants;
using CST.Avalonia.Models;
using CST.Conversion;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services;

/// <summary>
/// Application state service with JSON serialization for debugging and reliability
/// </summary>
public class ApplicationStateService : IApplicationStateService, IDisposable
{
    private readonly ILogger<ApplicationStateService> _logger;
    private readonly string _stateFilePath;
    private readonly string _backupDirectory;
    private readonly JsonSerializerOptions _jsonOptions;

    /// <summary>
    /// How application state is read and written. <b>Shared rather than mirrored</b> — a test that re-declares
    /// these cannot detect drift FROM them, which is the one thing it most needs to detect: change the naming
    /// policy or drop the enum converter and every real file on disk stops loading while a mirroring test suite
    /// stays green, because the fixture and the copy moved together. (#787)
    ///
    /// <para>The global <see cref="JsonStringEnumConverter"/> is load-bearing beyond the properties that carry
    /// their own converter attribute: <c>MainWindowState.WindowState</c> and <c>BookWindowState.WindowState</c>
    /// depend on it alone, and every real state file with a maximized window carries one.</para>
    /// </summary>
    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault,
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
    private readonly Timer _saveTimer;
    // Serializes concurrent saves (timer tick vs. shutdown ForceSave): they shared one .tmp path, so a
    // half-written tmp could be promoted over good state by File.Replace, and collided on backups. (STATE-2)
    private readonly System.Threading.SemaphoreSlim _saveLock = new(1, 1);

    public ApplicationState Current { get; private set; }
    public event Action<ApplicationState>? StateChanged;
    
    private bool _suppressStateChangedEvents = false;
    private bool _isDirty = false;

    /// <summary>Whether there are changes not yet written. Exposed for the tests that pin #879's ordering —
    /// the flag is cleared when the snapshot is TAKEN, so a change arriving mid-save survives.</summary>
    internal bool IsDirty { get { lock (_dirtyLock) return _isDirty; } }
    private readonly object _dirtyLock = new object();
    
    public void SetStateChangedEventsSuppression(bool suppress)
    {
        _suppressStateChangedEvents = suppress;
        _logger.LogDebug($"StateChanged events suppression: {suppress}");
    }
    
    private void FireStateChangedEvent()
    {
        if (!_suppressStateChangedEvents)
        {
            StateChanged?.Invoke(Current);
        }
        else
        {
            _logger.LogDebug("StateChanged event suppressed");
        }
    }

    public ApplicationStateService(ILogger<ApplicationStateService> logger)
        : this(logger, Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppConstants.AppDataDirectoryName))
    {
    }

    /// <summary>
    /// Test seam: point the service at a temp directory instead of the real state file, the same seam
    /// <c>SettingsService</c> has had. Its absence is why the load and recovery paths here had no coverage at
    /// all while the settings side accumulated three suites — the asymmetry in the tests mirrored the
    /// asymmetry in the code, and hid it. (#877)
    /// </summary>
    internal ApplicationStateService(ILogger<ApplicationStateService> logger, string appDataPath)
    {
        _logger = logger;

        Directory.CreateDirectory(appDataPath);
        Directory.CreateDirectory(Path.Combine(appDataPath, "app-state-backups"));
        
        _stateFilePath = Path.Combine(appDataPath, "application-state.json");
        _backupDirectory = Path.Combine(appDataPath, "app-state-backups");

        _jsonOptions = JsonOptions;

        Current = new ApplicationState();
        
        // Initialize timer for periodic state saving (every 60 seconds)
        _saveTimer = new Timer(60000); // 60 seconds
        _saveTimer.Elapsed += OnSaveTimerElapsed;
        _saveTimer.AutoReset = true;
        _saveTimer.Start();
        
        _logger.LogInformation("ApplicationStateService initialized with 60-second save timer");
    }
    
    /// <summary>
    /// Mark the state as dirty for later saving
    /// </summary>
    public void MarkDirty()
    {
        lock (_dirtyLock)
        {
            if (!_isDirty)
            {
                _isDirty = true;
                _logger.LogDebug("State marked as dirty");
            }
        }
    }
    
    /// <summary>
    /// Timer callback to save state if dirty
    /// </summary>
    private async void OnSaveTimerElapsed(object? sender, ElapsedEventArgs e)
    {
        bool shouldSave = false;
        lock (_dirtyLock)
        {
            shouldSave = _isDirty;
        }
        
        if (shouldSave)
        {
            _logger.LogDebug("Timer triggered: saving dirty state");
            // SaveStateAsync owns the flag now: it clears it when it takes the snapshot and re-sets it if
            // the write fails. Clearing it again here would undo exactly the fix — a MarkDirty that landed
            // mid-write would be wiped a second time. (#879)
            if (await SaveStateAsync())
                _logger.LogDebug("Timer save completed successfully");
            else
                _logger.LogWarning("Timer save failed - state remains dirty");
        }
    }

    public async Task<bool> LoadStateAsync()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                // No primary file is USUALLY a true first run — but not always, and the exception is the
                // recovery path's own worst moment. PreserveUnreadable moves the file aside, and if the app
                // is force-quit or crashes before the restore is written back, the next launch arrives here
                // with backups sitting right there and would call it a first run: the restore evaporates and
                // the reader loses the session a second time — then this process's own first save writes
                // defaults as the newest backup, poisoning the walk that would have found it. Preserving the
                // file is what opens this window, so porting preserve-aside without this is worse than
                // neither. The same guard SettingsService has. (#877, fable review)
                if (GetBackupFilePaths().Length > 0 && await TryLoadFromBackupAsync().ConfigureAwait(false))
                    return true;

                _logger.LogInformation("No state file found, using default state");
                return true;
            }

            var json = await File.ReadAllTextAsync(_stateFilePath);
            var state = JsonSerializer.Deserialize<ApplicationState>(json, _jsonOptions);
            
            if (state == null)
            {
                // Only a file holding the literal token `null` gets here — an empty or whitespace file
                // THROWS ("The input does not contain any JSON tokens") and has always taken the catch. So
                // this is not a bug being fixed: nothing writes such a file. It is a divergent path being
                // removed, because returning here gave up without trying the backups, which no other
                // unreadable file gets. Both cases are pinned in ApplicationStateRecoveryTests. (#877)
                _logger.LogWarning("State file deserialized to null");
                return await RecoverAsync().ConfigureAwait(false);
            }

            // Migrate older/missing-version files, then repair any invalid values in place (#78).
            var migrationNotes = ApplicationStateValidator.Migrate(state);
            foreach (var note in migrationNotes)
                _logger.LogInformation("State migration: {Note}", note);
            var stateFixes = ApplicationStateValidator.Sanitize(state);
            foreach (var fix in stateFixes)
                _logger.LogWarning("State sanitized: {Fix}", fix);

            Current = state;

            // If we upgraded or repaired anything, persist it so the on-disk file is brought up to date.
            if (migrationNotes.Count > 0 || stateFixes.Count > 0)
                MarkDirty();
            // Don't fire StateChanged on load to prevent infinite loops
            // Initial state will be handled separately
            
            _logger.LogInformation("Application state loaded successfully from {FilePath}", _stateFilePath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load application state from {FilePath}", _stateFilePath);
            return await RecoverAsync().ConfigureAwait(false);
        }
    }

    /// <summary>
    /// What to do with a state file we could not read: keep it, try the backups, and if nothing is
    /// recoverable install defaults in a way that does not destroy the next attempt. (#877)
    ///
    /// <para>Everything here is what <c>SettingsService</c> has done since #785 and this file never
    /// received. The loss is a session rather than hand-built configuration — open books, reading positions,
    /// tree expansion — which is why it took longer to matter, not why it does not.</para>
    /// </summary>
    private async Task<bool> RecoverAsync()
    {
        // Keep the file itself. Nothing else does: the very next save — the 60s timer, or the shutdown
        // ForceSaveAsync, which saves unconditionally — writes over the only copy, and the reader's session
        // is gone with no way back even by hand.
        PreserveUnreadable();

        if (await TryLoadFromBackupAsync().ConfigureAwait(false))
            return true;

        // Nothing recoverable. Defaults — and this save must NOT leave a backup.
        //
        // A backup is written before every save, so without the flag the defaults become the NEWEST backup;
        // TryLoadFromBackupAsync stops at the first file that deserializes, so from then on defaults win
        // every launch and the reader's real session — one file below, perfectly readable — is never
        // reached. The expected trigger is a persisted-type change, which breaks the primary file and the
        // recent backups together, so this is the ordinary case rather than a corner of it. SettingsService
        // guards exactly this and names the same trigger. (#877)
        _logger.LogWarning("Using default application state");
        Current = new ApplicationState();
        _backupNextSave = false;
        return false;
    }

    /// <summary>
    /// Whether the NEXT save should leave a backup. False for exactly one save: the defaults written when no
    /// backup could be read, which must not become the newest backup. (#877)
    /// </summary>
    private bool _backupNextSave = true;

    /// <summary>Move an unreadable state file aside under a timestamped name, so a save cannot destroy it.
    /// Milliseconds, like the backup names: two failed loads in one second must not collide. (#877)</summary>
    private void PreserveUnreadable()
    {
        try
        {
            if (!File.Exists(_stateFilePath)) return;

            var kept = Path.Combine(
                Path.GetDirectoryName(_stateFilePath)!,
                $"application-state.unreadable-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");

            File.Move(_stateFilePath, kept, overwrite: false);

            _logger.LogError(
                "The previous application state could not be read and has been kept at {Path}. "
                + "Nothing from it was deleted.", kept);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not preserve the unreadable application state file");
        }
    }

    // Serialize Current on the UI thread, where all mutations happen. Doing it here (not on the timer's
    // pool thread) is what prevents JsonSerializer from enumerating Current.BookWindows while the UI
    // mutates it (InvalidOperationException -> silently skipped save). If we're already on the UI thread
    // (the synchronous shutdown path: Dispose -> Wait), serialize inline so we don't dead-lock on Invoke. (STATE-2)
    private string SerializeCurrent()
    {
        if (Dispatcher.UIThread.CheckAccess())
            return JsonSerializer.Serialize(Current, _jsonOptions);

        try
        {
            return Dispatcher.UIThread.Invoke(() => JsonSerializer.Serialize(Current, _jsonOptions));
        }
        catch (Exception ex)
        {
            // UI thread unavailable (e.g. torn down during shutdown). Nothing mutates Current then, so
            // serializing inline off-thread is safe.
            _logger.LogDebug(ex, "UI-thread serialize unavailable; serializing inline");
            return JsonSerializer.Serialize(Current, _jsonOptions);
        }
    }

    public async Task<bool> SaveStateAsync()
    {
        // Snapshot to JSON *before* the first await, on the UI thread. This both avoids the
        // enumerate-during-mutation crash and keeps the shutdown path (Dispose -> Wait on the UI thread)
        // running inline here, so it never blocks the UI thread waiting on a marshalled Invoke. (STATE-2)
        Current.LastSaved = DateTime.UtcNow;
        string json;
        try
        {
            json = SerializeCurrent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to serialize application state");
            return false;
        }

        // The snapshot above is what this save will write, so THIS is the moment the dirty flag describes.
        // The callers used to clear it after the write returned, which discarded any MarkDirty that landed
        // in between: that change was not in the snapshot and was no longer marked, so it stayed unsaved
        // until something else happened to mark the state dirty again. Clearing here instead means a change
        // arriving mid-write leaves the state dirty and the next timer tick picks it up. Re-set on failure,
        // so a save that did not happen does not clear anything. (#879)
        lock (_dirtyLock) _isDirty = false;

        // One save at a time. ConfigureAwait(false) throughout so the synchronous shutdown wait
        // (Dispose -> task.Wait) can't deadlock by capturing the UI context. (#62, STATE-2)
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Back up the same snapshot we're about to write (no second serialize -> no second race) —
            // unless this is the defaults save that followed an unreadable file. See _backupNextSave. (#877)
            if (_backupNextSave) await WriteBackupAsync(json).ConfigureAwait(false);
            _backupNextSave = true;

            // Write to a temp file first, then atomically replace.
            var tempPath = _stateFilePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);

            if (File.Exists(_stateFilePath))
            {
                File.Replace(tempPath, _stateFilePath, null);
            }
            else
            {
                File.Move(tempPath, _stateFilePath);
            }

            _logger.LogInformation("Application state saved successfully to {FilePath}", _stateFilePath);
            // Don't fire StateChanged event on save - only on modifications
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save application state to {FilePath}", _stateFilePath);

            // Clean up temp file if it exists
            try
            {
                var tempPath = _stateFilePath + ".tmp";
                if (File.Exists(tempPath))
                {
                    File.Delete(tempPath);
                }
            }
            catch (Exception cleanupEx)
            {
                _logger.LogWarning(cleanupEx, "Failed to clean up temporary file");
            }

            // Nothing was written, so the state is still unsaved and must stay marked. (#879)
            lock (_dirtyLock) _isDirty = true;
            return false;
        }
        finally
        {
            _saveLock.Release();
        }
    }

    public void UpdateMainWindowState(MainWindowState mainWindowState)
    {
        Current.MainWindow = mainWindowState;
        FireStateChangedEvent();
        
        // Mark dirty for timer-based saving
        MarkDirty();
    }

    public void UpdateOpenBookDialogState(OpenBookDialogState dialogState)
    {
        Current.OpenBookDialog = dialogState;
        FireStateChangedEvent();
        MarkDirty(); // #62: was missing - dialog state could be lost on a non-graceful exit
    }

    public void UpdateSearchDialogState(SearchDialogState dialogState)
    {
        Current.SearchDialog = dialogState;
        FireStateChangedEvent();
        MarkDirty(); // #62
    }

    public void UpdateDictionaryDialogState(DictionaryDialogState dialogState)
    {
        Current.DictionaryDialog = dialogState;
        FireStateChangedEvent();
        MarkDirty(); // #62
    }

    public void UpdateBookWindowState(BookWindowState bookWindowState)
    {
        // Find by WindowId for unique instances - each WindowId should be unique
        // Remove the fallback to BookIndex to allow multiple copies of the same book
        var existing = Current.BookWindows.FirstOrDefault(w => 
            !string.IsNullOrEmpty(w.WindowId) && w.WindowId == bookWindowState.WindowId);
            
        if (existing != null)
        {
            Current.BookWindows.Remove(existing);
        }
        
        Current.BookWindows.Add(bookWindowState);
        FireStateChangedEvent();
        
        // Mark dirty for timer-based saving
        MarkDirty();
    }

    public void UpdateBookWindowScript(string windowId, Script newScript)
    {
        var existing = Current.BookWindows.FirstOrDefault(w => w.WindowId == windowId);
        if (existing != null)
        {
            existing.BookScript = newScript;
            FireStateChangedEvent();

            // Mark dirty for timer-based saving
            MarkDirty();
        }
    }

    // #224: persist the per-book Footnotes / search-highlight toggles when the user flips them, so saved
    // state stays in sync with the VM (otherwise the toggle is only captured at book-open time).
    public void UpdateBookWindowViewFlags(string windowId, bool showFootnotes, bool showSearchTerms)
    {
        var existing = Current.BookWindows.FirstOrDefault(w => w.WindowId == windowId);
        if (existing != null)
        {
            existing.ShowFootnotes = showFootnotes;
            existing.ShowSearchTerms = showSearchTerms;
            FireStateChangedEvent();
            MarkDirty();
        }
    }

    public void RemoveBookWindowStateByWindowId(string windowId)
    {
        var existing = Current.BookWindows.FirstOrDefault(w => w.WindowId == windowId);
        if (existing != null)
        {
            Current.BookWindows.Remove(existing);
            FireStateChangedEvent();
            
            // Mark dirty for timer-based saving
            MarkDirty();
        }
    }

    public void RemoveBookWindowState(int bookIndex)
    {
        var existing = Current.BookWindows.FirstOrDefault(w => w.BookIndex == bookIndex);
        if (existing != null)
        {
            Current.BookWindows.Remove(existing);
            FireStateChangedEvent();
            
            // Mark dirty for timer-based saving
            MarkDirty();
        }
    }

    public void UpdatePreferences(ApplicationPreferences preferences)
    {
        Current.Preferences = preferences;
        FireStateChangedEvent();
        MarkDirty(); // #62
    }

    public List<string> GetExpandedNodeKeys()
    {
        return new List<string>(Current.OpenBookDialog.ExpandedNodeKeys);
    }

    public void SetExpandedNodeKeys(List<string> expandedNodeKeys)
    {
        Current.OpenBookDialog.ExpandedNodeKeys = expandedNodeKeys ?? new List<string>();
        FireStateChangedEvent();
        MarkDirty(); // #62
    }


    public async Task ClearStateAsync()
    {
        try
        {
            if (File.Exists(_stateFilePath))
            {
                File.Delete(_stateFilePath);
                _logger.LogInformation("Application state file deleted");
            }

            Current = new ApplicationState();
            FireStateChangedEvent();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to clear application state");
        }
    }

    public Task<StateValidationResult> ValidateStateAsync()
    {
        return Task.FromResult(ApplicationStateValidator.Validate(Current));
    }

    // How many of the most recent backups to always keep (fine-grained recent recovery).
    private const int RecentBackupsToKeep = 8;
    // How many distinct recent days to keep one backup for (older recovery: "I broke it yesterday").
    private const int DailyBackupsToKeep = 14;

    // Pure retention policy (no I/O, so it's unit-testable): given backups newest-first with their
    // timestamps, return the paths to DELETE — keep the RecentBackupsToKeep newest, plus the newest
    // backup of each of the most recent DailyBackupsToKeep days. (STATE-7)
    internal static List<string> SelectBackupsToDelete(IReadOnlyList<(string path, DateTime when)> backupsNewestFirst)
    {
        var keep = new HashSet<string>();

        for (int i = 0; i < backupsNewestFirst.Count && i < RecentBackupsToKeep; i++)
            keep.Add(backupsNewestFirst[i].path);

        var seenDays = new HashSet<string>();
        foreach (var (path, when) in backupsNewestFirst)
        {
            if (seenDays.Count >= DailyBackupsToKeep && !seenDays.Contains(when.ToString("yyyy-MM-dd")))
                break;
            if (seenDays.Add(when.ToString("yyyy-MM-dd")))
                keep.Add(path); // first (newest) backup seen for this day
        }

        return backupsNewestFirst.Where(b => !keep.Contains(b.path)).Select(b => b.path).ToList();
    }

    public string[] GetBackupFilePaths()
    {
        try
        {
            return Directory.GetFiles(_backupDirectory, "application-state-*.json")
                .OrderByDescending(f => File.GetLastWriteTime(f))
                .ToArray();
        }
        catch
        {
            return Array.Empty<string>();
        }
    }

    // Public entry point (interface): serialize the current state and back it up. Callers on the save
    // path use WriteBackupAsync directly with the already-serialized snapshot.
    public Task<bool> CreateBackupAsync() => WriteBackupAsync(SerializeCurrent());

    private async Task<bool> WriteBackupAsync(string json)
    {
        try
        {
            // Millisecond resolution: two saves in the same second would otherwise collide on one path
            // and clobber a backup (or throw). (STATE-2)
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss-fff");
            var backupPath = Path.Combine(_backupDirectory, $"application-state-{timestamp}.json");

            await File.WriteAllTextAsync(backupPath, json).ConfigureAwait(false);

            // Tiered retention. A backup is written before EVERY save (60s timer, script changes,
            // shutdown), so a flat "keep newest 10" was fully churned out within ~10 minutes of use —
            // leaving no way to recover a state from earlier today or a previous session. Keep the newest
            // few for fine-grained recent recovery PLUS the newest backup of each recent day, so the set
            // spans days, not minutes. (STATE-7)
            var backups = GetBackupFilePaths()
                .Select(p => (path: p, when: File.GetLastWriteTime(p)))
                .ToList();
            foreach (var stale in SelectBackupsToDelete(backups))
            {
                try { File.Delete(stale); } catch { /* best effort */ }
            }

            _logger.LogDebug("Created state backup: {BackupPath}", backupPath);
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to create state backup");
            return false;
        }
    }

    private async Task<bool> TryLoadFromBackupAsync()
    {
        try
        {
            var backups = GetBackupFilePaths();
            if (backups.Length == 0)
                return false;

            foreach (var backupPath in backups)
            {
                try
                {
                    var json = await File.ReadAllTextAsync(backupPath);
                    var state = JsonSerializer.Deserialize<ApplicationState>(json, _jsonOptions);
                    
                    if (state != null)
                    {
                        // Migrate + sanitize the backup the same way as the primary file (#78).
                        ApplicationStateValidator.Migrate(state);
                        ApplicationStateValidator.Sanitize(state);

                        Current = state;
                        FireStateChangedEvent();

                        // The timestamp, and no claim beyond it: the newest READABLE backup can be older
                        // than the file that failed, so "nothing was lost" would be a guess stated as fact.
                        _logger.LogInformation(
                            "Application state was restored from the backup taken at {Taken} ({BackupPath}).",
                            File.GetLastWriteTime(backupPath), backupPath);

                        // Written back NOW, not left to the 60s timer. Until a primary file exists again the
                        // recovery lives only in memory, and the restore does not mark the state dirty — so
                        // nothing was scheduled to write it at all. A crash in that window sends the next
                        // launch down the no-file path with the primary already moved aside. A restore that
                        // has to survive a race to count is not a restore; the guard on that path is the
                        // belt, this is the braces. (#877, fable review)
                        //
                        // _backupNextSave is still true here, correctly: the restored state SHOULD become
                        // the newest backup. Only defaults must not.
                        await SaveStateAsync().ConfigureAwait(false);
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning(ex, "Failed to load backup: {BackupPath}", backupPath);
                }
            }

            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load from backup");
            return false;
        }
    }

    /// <summary>
    /// Force immediate save of state (for shutdown scenarios)
    /// </summary>
    public async Task<bool> ForceSaveAsync()
    {
        _logger.LogInformation("Force saving application state");
        // The flag is cleared inside SaveStateAsync, at the snapshot. See #879.
        return await SaveStateAsync();
    }
    
    public void Dispose()
    {
        _logger.LogInformation("Disposing ApplicationStateService - performing final save");
        
        // Stop the timer
        _saveTimer?.Stop();

        // Drain a save that is already in flight before deciding anything.
        //
        // The dirty flag is now cleared when the snapshot is taken (#879), so an in-flight write no longer
        // looks dirty — and this method used to drain it only by accident, by starting a redundant second
        // save whose Wait blocked on the semaphore. Without this the process can tear down mid-write. The
        // loss is bounded (temp file + File.Replace: an interrupted write loses that snapshot, never the
        // file), and production force-saves before Dispose anyway — but the drain was real behaviour and
        // should not disappear as a side effect of the flag fix. (fable review)
        try { if (_saveLock.Wait(TimeSpan.FromSeconds(5))) _saveLock.Release(); }
        catch (Exception ex) { _logger.LogWarning(ex, "Could not drain an in-flight state save"); }

        // Force save any dirty state before disposal
        bool shouldSave = false;
        lock (_dirtyLock)
        {
            shouldSave = _isDirty;
        }
        
        if (shouldSave)
        {
            // Synchronous save during disposal
            try
            {
                var task = SaveStateAsync();
                task.Wait(TimeSpan.FromSeconds(5)); // Wait up to 5 seconds
                if (task.IsCompletedSuccessfully)
                {
                    _logger.LogInformation("Final state save completed successfully");
                }
                else
                {
                    _logger.LogWarning("Final state save did not complete within timeout");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save state during disposal");
            }
        }
        
        _saveTimer?.Dispose();
        _saveLock.Dispose();
        _logger.LogInformation("ApplicationStateService disposed");
    }
}