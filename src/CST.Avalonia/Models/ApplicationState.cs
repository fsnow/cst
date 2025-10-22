using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using CST.Conversion;

namespace CST.Avalonia.Models;

/// <summary>
/// Modern replacement for CST4 AppState - uses JSON serialization for better debugging
/// </summary>
public class ApplicationState
{
    /// <summary>
    /// File format version for backward compatibility and migrations
    /// </summary>
    public string Version { get; set; } = "1.0";
    
    /// <summary>
    /// Timestamp when state was last saved
    /// </summary>
    public DateTime LastSaved { get; set; } = DateTime.UtcNow;

    // Main Window State
    public MainWindowState MainWindow { get; set; } = new();

    // Open Book Dialog State  
    public OpenBookDialogState OpenBookDialog { get; set; } = new();

    // Search Dialog State
    public SearchDialogState SearchDialog { get; set; } = new();

    // Dictionary Dialog State
    public DictionaryDialogState DictionaryDialog { get; set; } = new();

    // Open Book Windows (tabs)
    public List<BookWindowState> BookWindows { get; set; } = new();

    // Application Preferences
    public ApplicationPreferences Preferences { get; set; } = new();
}

/// <summary>
/// Main window state (position, size, etc.)
/// </summary>
public class MainWindowState
{
    public WindowState WindowState { get; set; } = WindowState.Normal;
    public double Width { get; set; } = 1024;
    public double Height { get; set; } = 768;
    public double? X { get; set; }
    public double? Y { get; set; }
    public bool IsMaximized { get; set; }
}

/// <summary>
/// Open Book dialog state including tree expansion
/// </summary>
public class OpenBookDialogState
{
    public bool IsVisible { get; set; }
    public double Width { get; set; } = 900;
    public double Height { get; set; } = 700;
    public double? X { get; set; }
    public double? Y { get; set; }

    /// <summary>
    /// Tree expansion states stored as boolean array for easy JSON debugging
    /// Each boolean corresponds to a tree node in traversal order
    /// </summary>
    public List<bool> TreeExpansionStates { get; set; } = new();

    /// <summary>
    /// Version of tree structure when states were saved (for validation)
    /// </summary>
    public int TreeVersion { get; set; }

    /// <summary>
    /// Total number of nodes when state was saved (for validation)
    /// </summary>
    public int TotalNodeCount { get; set; }

    /// <summary>
    /// Selected book path for restoring selection
    /// </summary>
    public string? SelectedBookPath { get; set; }
}

/// <summary>
/// Search dialog state
/// </summary>
public class SearchDialogState
{
    public bool IsVisible { get; set; }
    public double Width { get; set; } = 600;
    public double Height { get; set; } = 400;
    public double? X { get; set; }
    public double? Y { get; set; }

    // Search Parameters
    public string SearchTerms { get; set; } = string.Empty;
    public int ContextDistance { get; set; } = 50;
    public List<int> SelectedBooks { get; set; } = new();
    public List<int> SelectedWords { get; set; } = new();

    // Search Options
    public bool SearchVinaya { get; set; } = true;
    public bool SearchSutta { get; set; } = true;
    public bool SearchAbhidhamma { get; set; } = true;
    public bool SearchMula { get; set; } = true;
    public bool SearchAtthakatha { get; set; } = true;
    public bool SearchTika { get; set; } = true;
    public bool SearchOtherTexts { get; set; } = true;
    public bool SearchAll { get; set; } = true;
    public int SearchUse { get; set; }
    public int BookCollectionSelected { get; set; }
}

/// <summary>
/// Dictionary dialog state
/// </summary>
public class DictionaryDialogState
{
    public bool IsVisible { get; set; }
    public double Width { get; set; } = 500;
    public double Height { get; set; } = 400;
    public double? X { get; set; }
    public double? Y { get; set; }

    public string UserText { get; set; } = string.Empty;
    public int SelectedWordIndex { get; set; }
    public int LanguageIndex { get; set; }
}

/// <summary>
/// Individual book window/tab state
/// </summary>
public class BookWindowState
{
    /// <summary>
    /// Unique identifier for this book window instance (allows multiple instances of same book)
    /// </summary>
    public string WindowId { get; set; } = Guid.NewGuid().ToString();

    /// <summary>
    /// Index of the book in Books.Inst collection
    /// </summary>
    public int BookIndex { get; set; }

    /// <summary>
    /// Book file name for additional validation
    /// </summary>
    public string BookFileName { get; set; } = string.Empty;

    /// <summary>
    /// Script used for this book display
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Script BookScript { get; set; } = Script.Devanagari;

    /// <summary>
    /// Window position and size (for future windowed mode)
    /// </summary>
    public double Width { get; set; } = 800;
    public double Height { get; set; } = 600;
    public double? X { get; set; }
    public double? Y { get; set; }
    public WindowState WindowState { get; set; } = WindowState.Normal;

    /// <summary>
    /// Display options
    /// </summary>
    public bool ShowFootnotes { get; set; } = true;
    public bool ShowSearchTerms { get; set; }

    /// <summary>
    /// Search terms for highlighting (if any) - IPE encoded
    /// </summary>
    public List<string> SearchTerms { get; set; } = new();

    /// <summary>
    /// Document ID in Lucene index for this book (needed for search highlighting)
    /// </summary>
    public int? DocId { get; set; }

    /// <summary>
    /// Precomputed search term positions with IsFirstTerm flags (for two-color highlighting)
    /// </summary>
    public List<TermPosition> SearchPositions { get; set; } = new();

    /// <summary>
    /// Tab order index
    /// </summary>
    public int TabIndex { get; set; }

    /// <summary>
    /// Whether this tab is currently selected
    /// </summary>
    public bool IsSelected { get; set; }
}

/// <summary>
/// Application-wide preferences
/// </summary>
public class ApplicationPreferences
{
    /// <summary>
    /// Current script for Pali text display
    /// </summary>
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Script CurrentScript { get; set; } = Script.Devanagari;

    /// <summary>
    /// UI language (for future localization)
    /// </summary>
    public string InterfaceLanguage { get; set; } = "en";

    /// <summary>
    /// Recently opened books (MRU list)
    /// </summary>
    public List<RecentBookItem> RecentBooks { get; set; } = new();

    /// <summary>
    /// Maximum number of recent books to remember
    /// </summary>
    public int MaxRecentBooks { get; set; } = 10;
}

/// <summary>
/// Recent book item for MRU list
/// </summary>
public class RecentBookItem
{
    public int BookIndex { get; set; }
    public string BookFileName { get; set; } = string.Empty;
    public string DisplayName { get; set; } = string.Empty;
    public DateTime LastOpened { get; set; } = DateTime.UtcNow;
}

/// <summary>
/// Window state enumeration
/// </summary>
public enum WindowState
{
    Normal,
    Minimized,
    Maximized
}