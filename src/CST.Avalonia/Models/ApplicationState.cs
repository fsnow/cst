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
    public double Width { get; set; } = 1400;
    public double Height { get; set; } = 900;
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
    /// Path-keys of the expanded tree nodes (node identity = path of Devanagari text from the root).
    /// Identity-keyed rather than positional, so the saved expansion survives future tree additions or
    /// reordering. (#64)
    /// </summary>
    public List<string> ExpandedNodeKeys { get; set; } = new();

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
    // Every field is force-serialized (JsonIgnoreCondition.Never) so it round-trips exactly. The global
    // serializer uses WhenWritingDefault, which would otherwise drop a `false` filter from the JSON and
    // let it revert to the `= true` default on load - an unchecked book-type filter would come back
    // checked. (#87)

    // The user's query text, restored verbatim.
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public string SearchText { get; set; } = string.Empty;

    // Search mode (Exact is implicit when no special chars are present; the UI offers Wildcard/Regex).
    [JsonConverter(typeof(JsonStringEnumConverter))]
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public SearchMode SearchMode { get; set; } = SearchMode.Wildcard;

    // Proximity window (words) for multi-unit queries.
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public int ProximityDistance { get; set; } = 10;

    // Book-type filters (match SearchViewModel.Include*; all on by default).
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public bool IncludeVinaya { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public bool IncludeSutta { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public bool IncludeAbhidhamma { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public bool IncludeMula { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public bool IncludeAttha { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public bool IncludeTika { get; set; } = true;
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)] public bool IncludeOther { get; set; } = true;

    // Whether the "Include Text Types" box is expanded.
    [JsonIgnore(Condition = JsonIgnoreCondition.Never)]
    public bool IsTextTypesExpanded { get; set; }

    // IPE keys of the matching terms the user had selected (drives the occurrences list). Re-selected
    // after the restored query's search repopulates the term list.
    public List<string> SelectedTerms { get; set; } = new();
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

    /// <summary>Preferred definition-language code ("en"/"hi"); remembered across sessions (#25).
    /// A robust code rather than an index, so it survives changes to the available-language order.</summary>
    public string Language { get; set; } = "en";
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
    /// Script used for this book display. Forced to always serialize (#323 A9-1): Script.Bengali is enum value 0
    /// = default(Script), which the global WhenWritingDefault would DROP — so a per-book Bengali choice would be
    /// omitted and reload as the Devanagari initializer. Same trap the #224 bools fixed.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
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
    /// Display options (#224). Forced to always serialize: the global DefaultIgnoreCondition is
    /// WhenWritingDefault, which would DROP the type-default false — and since ShowFootnotes' initializer is
    /// true, an omitted false round-trips back to true, so "footnotes off" would never persist.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public bool ShowFootnotes { get; set; } = true;
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    public bool ShowSearchTerms { get; set; } = true;

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
    /// Current scroll position anchor for restoring position on startup
    /// (e.g., "para123", "V1.0023", "dn1_1", etc.)
    /// </summary>
    public string? CurrentAnchor { get; set; }

    /// <summary>
    /// Canonical reading-position token (#434) — the exact reading position (bracketing anchors + fraction),
    /// preferred over the coarse <see cref="CurrentAnchor"/> on restore. No migration is needed: beta-5 testers
    /// clean-start the app-data dir, so an old file simply has this null and falls back to CurrentAnchor.
    /// </summary>
    public ReadingPositionToken? ReadingPosition { get; set; }

    /// <summary>
    /// Which search hit the user was viewing (1-based), restored so navigation
    /// resumes at "N of Total" instead of resetting to "1 of Total".
    /// </summary>
    public int CurrentHitIndex { get; set; } = 1;

    /// <summary>
    /// Total search hits at save time (count of first-term positions).
    /// </summary>
    public int TotalHits { get; set; }

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
    /// Current script for Pali text display. Forced to always serialize (#323 A9-1): Script.Bengali is enum value
    /// 0 = default(Script), which the global WhenWritingDefault would DROP — so a Bengali choice would be omitted
    /// and reload as the Latin initializer, silently resetting every launch. Same trap the #224 bools fixed.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
    [JsonConverter(typeof(JsonStringEnumConverter))]
    public Script CurrentScript { get; set; } = Script.Latin;

    /// <summary>
    /// UI language (for future localization)
    /// </summary>
    public string InterfaceLanguage { get; set; } = "en";

    /// <summary>
    /// Recently opened books (MRU list)
    /// </summary>
    public List<RecentBookItem> RecentBooks { get; set; } = new();

    /// <summary>
    /// Maximum number of recent books to remember. Forced to always serialize (#323 A9-1 class): the initializer
    /// is 10 but the CLR default is 0, which the global WhenWritingDefault would DROP — so a deliberate 0 ("disable
    /// the recent-books list") would be omitted and silently revert to 10 next launch. Same trap the two Script
    /// enums and the #224 bools fixed.
    /// </summary>
    [System.Text.Json.Serialization.JsonIgnore(Condition = System.Text.Json.Serialization.JsonIgnoreCondition.Never)]
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