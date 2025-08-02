using System;
using System.Threading.Tasks;
using CST.Avalonia.Models;

namespace CST.Avalonia.Services;

/// <summary>
/// Service for managing application state persistence
/// Modern replacement for CST4 AppState with JSON serialization for better debugging
/// </summary>
public interface IApplicationStateService
{
    /// <summary>
    /// Current application state
    /// </summary>
    ApplicationState Current { get; }

    /// <summary>
    /// Event fired when application state changes
    /// </summary>
    event Action<ApplicationState>? StateChanged;

    /// <summary>
    /// Load application state from disk
    /// </summary>
    Task<bool> LoadStateAsync();

    /// <summary>
    /// Save current application state to disk
    /// </summary>
    Task<bool> SaveStateAsync();

    /// <summary>
    /// Update main window state
    /// </summary>
    void UpdateMainWindowState(MainWindowState mainWindowState);

    /// <summary>
    /// Update Open Book dialog state
    /// </summary>
    void UpdateOpenBookDialogState(OpenBookDialogState dialogState);

    /// <summary>
    /// Update search dialog state
    /// </summary>
    void UpdateSearchDialogState(SearchDialogState dialogState);

    /// <summary>
    /// Update dictionary dialog state
    /// </summary>
    void UpdateDictionaryDialogState(DictionaryDialogState dialogState);

    /// <summary>
    /// Add or update a book window state
    /// </summary>
    void UpdateBookWindowState(BookWindowState bookWindowState);

    /// <summary>
    /// Remove a book window state
    /// </summary>
    void RemoveBookWindowState(int bookIndex);

    /// <summary>
    /// Update application preferences
    /// </summary>
    void UpdatePreferences(ApplicationPreferences preferences);

    /// <summary>
    /// Get tree expansion states as boolean array (for debugging)
    /// </summary>
    bool[] GetTreeExpansionStates();

    /// <summary>
    /// Set tree expansion states from boolean array
    /// </summary>
    void SetTreeExpansionStates(bool[] states, int treeVersion, int totalNodeCount);

    /// <summary>
    /// Add book to recent books list
    /// </summary>
    void AddRecentBook(int bookIndex, string fileName, string displayName);

    /// <summary>
    /// Clear all state (equivalent to deleting app-state.dat)
    /// </summary>
    Task ClearStateAsync();

    /// <summary>
    /// Validate state file integrity
    /// </summary>
    Task<StateValidationResult> ValidateStateAsync();

    /// <summary>
    /// Get backup file paths for recovery
    /// </summary>
    string[] GetBackupFilePaths();

    /// <summary>
    /// Create backup of current state
    /// </summary>
    Task<bool> CreateBackupAsync();
    
    /// <summary>
    /// Control whether StateChanged events are suppressed (for restoration scenarios)
    /// </summary>
    void SetStateChangedEventsSuppression(bool suppress);
}

/// <summary>
/// Result of state validation
/// </summary>
public class StateValidationResult
{
    public bool IsValid { get; set; }
    public string[] Errors { get; set; } = Array.Empty<string>();
    public string[] Warnings { get; set; } = Array.Empty<string>();
    public bool CanRecover { get; set; }
    public string? SuggestedAction { get; set; }
}