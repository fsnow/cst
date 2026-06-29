using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;
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

    public async Task<bool> SaveStateAsync()
    {
        try
        {
            // Create backup before saving. ConfigureAwait(false) throughout the save path so the
            // synchronous shutdown wait (Dispose -> task.Wait) can't deadlock by capturing the UI context. (#62)
            await CreateBackupAsync().ConfigureAwait(false);

            // Update timestamp
            Current.LastSaved = DateTime.UtcNow;

            // Serialize to JSON with pretty formatting for debugging
            var json = JsonSerializer.Serialize(Current, _jsonOptions);
            
            // Write to temporary file first, then rename (atomic operation)
            var tempPath = _stateFilePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json).ConfigureAwait(false);
            
            // Atomic replacement using File.Replace for true atomicity
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

    public void AddRecentBook(int bookIndex, string fileName, string displayName)
    {
        var recent = Current.Preferences.RecentBooks;
        
        // Remove existing entry if present
        var existing = recent.FirstOrDefault(r => r.BookIndex == bookIndex);
        if (existing != null)
            recent.Remove(existing);

        // Add to front
        recent.Insert(0, new RecentBookItem
        {
            BookIndex = bookIndex,
            BookFileName = fileName,
            DisplayName = displayName,
            LastOpened = DateTime.UtcNow
        });

        // Trim to max size
        while (recent.Count > Current.Preferences.MaxRecentBooks)
        {
            recent.RemoveAt(recent.Count - 1);
        }

        FireStateChangedEvent();
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

    public async Task<bool> CreateBackupAsync()
    {
        try
        {
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd-HH-mm-ss");
            var backupPath = Path.Combine(_backupDirectory, $"application-state-{timestamp}.json");
            
            var json = JsonSerializer.Serialize(Current, _jsonOptions);
            await File.WriteAllTextAsync(backupPath, json).ConfigureAwait(false);

            // Keep only last 10 backups
            var backups = GetBackupFilePaths();
            if (backups.Length > 10)
            {
                foreach (var oldBackup in backups.Skip(10))
                {
                    File.Delete(oldBackup);
                }
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
        _logger.LogInformation("ApplicationStateService disposed");
    }
}