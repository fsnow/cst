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
    private readonly Timer _saveTimer;
    // Serializes concurrent saves (timer tick vs. shutdown ForceSave): they shared one .tmp path, so a
    // half-written tmp could be promoted over good state by File.Replace, and collided on backups. (STATE-2)
    private readonly System.Threading.SemaphoreSlim _saveLock = new(1, 1);

    public ApplicationState Current { get; private set; }
    public event Action<ApplicationState>? StateChanged;
    
    private bool _suppressStateChangedEvents = false;
    private bool _isDirty = false;
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
    {
        _logger = logger;
        
        // Use application data directory for state files
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppConstants.AppDataDirectoryName
        );
        
        Directory.CreateDirectory(appDataPath);
        Directory.CreateDirectory(Path.Combine(appDataPath, "app-state-backups"));
        
        _stateFilePath = Path.Combine(appDataPath, "application-state.json");
        _backupDirectory = Path.Combine(appDataPath, "app-state-backups");

        // Configure JSON serialization for readability
        _jsonOptions = new JsonSerializerOptions
        {
            WriteIndented = true,
            PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
            DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingDefault,
            Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
        };

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
            var success = await SaveStateAsync();
            if (success)
            {
                lock (_dirtyLock)
                {
                    _isDirty = false;
                }
                _logger.LogDebug("Timer save completed successfully");
            }
            else
            {
                _logger.LogWarning("Timer save failed - state remains dirty");
            }
        }
    }

    public async Task<bool> LoadStateAsync()
    {
        try
        {
            if (!File.Exists(_stateFilePath))
            {
                _logger.LogInformation("No state file found, using default state");
                return true;
            }

            var json = await File.ReadAllTextAsync(_stateFilePath);
            var state = JsonSerializer.Deserialize<ApplicationState>(json, _jsonOptions);
            
            if (state == null)
            {
                _logger.LogWarning("Failed to deserialize state file, using default state");
                return false;
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
            
            // Try to load from backup
            var backupLoaded = await TryLoadFromBackupAsync();
            if (!backupLoaded)
            {
                _logger.LogWarning("Using default application state");
                Current = new ApplicationState();
            }
            
            return backupLoaded;
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

        // One save at a time. ConfigureAwait(false) throughout so the synchronous shutdown wait
        // (Dispose -> task.Wait) can't deadlock by capturing the UI context. (#62, STATE-2)
        await _saveLock.WaitAsync().ConfigureAwait(false);
        try
        {
            // Back up the same snapshot we're about to write (no second serialize -> no second race).
            await WriteBackupAsync(json).ConfigureAwait(false);

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

                        _logger.LogInformation("Loaded application state from backup: {BackupPath}", backupPath);
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
        var success = await SaveStateAsync();
        if (success)
        {
            lock (_dirtyLock)
            {
                _isDirty = false;
            }
        }
        return success;
    }
    
    public void Dispose()
    {
        _logger.LogInformation("Disposing ApplicationStateService - performing final save");
        
        // Stop the timer
        _saveTimer?.Stop();
        
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