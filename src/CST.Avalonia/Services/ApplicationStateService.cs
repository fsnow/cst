using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using System.Timers;
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
            "CST.Avalonia"
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

            // Validate loaded state
            var validation = await ValidateLoadedState(state);
            if (!validation.IsValid)
            {
                _logger.LogWarning("Loaded state validation failed: {Errors}", 
                    string.Join(", ", validation.Errors));
                
                if (!validation.CanRecover)
                {
                    _logger.LogError("State is corrupted and cannot be recovered, using default state");
                    return false;
                }
                
                // Apply fixes for recoverable issues
                ApplyStateFixes(state, validation);
            }

            Current = state;
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
            // Create backup before saving
            await CreateBackupAsync();

            // Update timestamp
            Current.LastSaved = DateTime.UtcNow;

            // Serialize to JSON with pretty formatting for debugging
            var json = JsonSerializer.Serialize(Current, _jsonOptions);
            
            // Write to temporary file first, then rename (atomic operation)
            var tempPath = _stateFilePath + ".tmp";
            await File.WriteAllTextAsync(tempPath, json);
            
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
    }

    public void UpdateSearchDialogState(SearchDialogState dialogState)
    {
        Current.SearchDialog = dialogState;
        FireStateChangedEvent();
    }

    public void UpdateDictionaryDialogState(DictionaryDialogState dialogState)
    {
        Current.DictionaryDialog = dialogState;
        FireStateChangedEvent();
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
    }

    public bool[] GetTreeExpansionStates()
    {
        return Current.OpenBookDialog.TreeExpansionStates.ToArray();
    }

    public void SetTreeExpansionStates(bool[] states, int treeVersion, int totalNodeCount)
    {
        Current.OpenBookDialog.TreeExpansionStates = states.ToList();
        Current.OpenBookDialog.TreeVersion = treeVersion;
        Current.OpenBookDialog.TotalNodeCount = totalNodeCount;
        FireStateChangedEvent();
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

    public async Task<StateValidationResult> ValidateStateAsync()
    {
        return await ValidateLoadedState(Current);
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
            await File.WriteAllTextAsync(backupPath, json);

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
                        var validation = await ValidateLoadedState(state);
                        if (validation.IsValid || validation.CanRecover)
                        {
                            if (!validation.IsValid)
                                ApplyStateFixes(state, validation);
                                
                            Current = state;
                            FireStateChangedEvent();
                            
                            _logger.LogInformation("Loaded application state from backup: {BackupPath}", backupPath);
                            return true;
                        }
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

    private async Task<StateValidationResult> ValidateLoadedState(ApplicationState state)
    {
        var result = new StateValidationResult { IsValid = true, CanRecover = true };
        var errors = new List<string>();
        var warnings = new List<string>();

        // Validate version compatibility
        if (string.IsNullOrEmpty(state.Version))
        {
            warnings.Add("Missing version information");
            result.IsValid = false;
        }

        // Validate main window state
        if (state.MainWindow.Width <= 0 || state.MainWindow.Height <= 0)
        {
            warnings.Add("Invalid main window dimensions");
            result.IsValid = false;
        }

        // Validate book window states
        foreach (var bookWindow in state.BookWindows)
        {
            if (bookWindow.BookIndex < 0)
            {
                warnings.Add($"Invalid book index: {bookWindow.BookIndex}");
                result.IsValid = false;
            }
        }

        // Check for tree state consistency
        if (state.OpenBookDialog.TreeExpansionStates.Count != state.OpenBookDialog.TotalNodeCount && 
            state.OpenBookDialog.TotalNodeCount > 0)
        {
            warnings.Add("Tree expansion state count mismatch - tree structure may have changed");
            result.IsValid = false;
        }

        result.Errors = errors.ToArray();
        result.Warnings = warnings.ToArray();

        if (!result.IsValid && warnings.Count > 0 && errors.Count == 0)
        {
            result.SuggestedAction = "State has warnings but can be recovered with fixes";
        }
        else if (errors.Count > 0)
        {
            result.CanRecover = false;
            result.SuggestedAction = "State is corrupted - recommend clearing and starting fresh";
        }

        return result;
    }

    private void ApplyStateFixes(ApplicationState state, StateValidationResult validation)
    {
        // Fix invalid window dimensions
        if (state.MainWindow.Width <= 0) state.MainWindow.Width = 1024;
        if (state.MainWindow.Height <= 0) state.MainWindow.Height = 768;

        // Remove invalid book windows
        state.BookWindows.RemoveAll(w => w.BookIndex < 0);

        // Clear tree expansion states if count mismatch
        if (state.OpenBookDialog.TreeExpansionStates.Count != state.OpenBookDialog.TotalNodeCount)
        {
            state.OpenBookDialog.TreeExpansionStates.Clear();
            state.OpenBookDialog.TotalNodeCount = 0;
            state.OpenBookDialog.TreeVersion = 0;
        }

        _logger.LogInformation("Applied fixes to application state");
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