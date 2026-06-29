using System;
using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Services;

namespace CST.Avalonia.Models;

/// <summary>
/// Pure (no I/O, no DI) validation, sanitization, and version-migration for <see cref="ApplicationState"/>,
/// so older/corrupt state files upgrade and self-heal cleanly on load. Directly unit-testable. (#78)
/// </summary>
public static class ApplicationStateValidator
{
    /// <summary>
    /// Current on-disk state schema version. Bump this and add an ordered step in <see cref="Migrate"/> when a
    /// breaking schema change needs older files transformed.
    /// </summary>
    public const string CurrentVersion = "1.0";

    // Model defaults, kept here so sanitization restores the same values the constructors use.
    private const double MainW = 1400, MainH = 900;
    private const double OpenBookW = 900, OpenBookH = 700;
    private const double DictW = 500, DictH = 400;
    private const double BookW = 800, BookH = 600;

    /// <summary>
    /// Upgrade an older / missing-version state to <see cref="CurrentVersion"/>. Returns human-readable notes
    /// (for logging). A version newer than we understand is left untouched (forward-compatible read).
    /// </summary>
    public static IReadOnlyList<string> Migrate(ApplicationState state)
    {
        var notes = new List<string>();
        if (state == null) return notes;

        var v = (state.Version ?? string.Empty).Trim();
        if (v.Length == 0)
        {
            state.Version = CurrentVersion;
            notes.Add($"state had no version; stamped {CurrentVersion}");
            return notes;
        }

        // --- ordered migration steps go here as the schema evolves, e.g.:
        //   if (v == "1.0") { /* transform */ v = "1.1"; notes.Add("migrated 1.0 -> 1.1"); }

        if (CompareVersions(v, CurrentVersion) > 0)
        {
            notes.Add($"state version {v} is newer than supported {CurrentVersion}; reading as-is");
            return notes;
        }

        if (v != CurrentVersion)
        {
            state.Version = CurrentVersion;
            notes.Add($"migrated state {v} -> {CurrentVersion}");
        }
        return notes;
    }

    /// <summary>
    /// Repair invalid values in place (NaN/Infinity/non-positive sizes, out-of-range indices, stray entries).
    /// Idempotent and safe to run on every load. Returns the list of fixes applied (for logging).
    /// </summary>
    public static IReadOnlyList<string> Sanitize(ApplicationState state)
    {
        var fixes = new List<string>();
        if (state == null) return fixes;

        // Main window
        var mw = state.MainWindow ??= new MainWindowState();
        if (IsBadSize(mw.Width)) { mw.Width = MainW; fixes.Add($"main window width -> {MainW}"); }
        if (IsBadSize(mw.Height)) { mw.Height = MainH; fixes.Add($"main window height -> {MainH}"); }
        if (IsBadCoord(mw.X)) { mw.X = null; fixes.Add("main window X cleared (NaN/Infinity)"); }
        if (IsBadCoord(mw.Y)) { mw.Y = null; fixes.Add("main window Y cleared (NaN/Infinity)"); }

        // Open Book dialog
        var ob = state.OpenBookDialog ??= new OpenBookDialogState();
        if (IsBadSize(ob.Width)) { ob.Width = OpenBookW; fixes.Add($"open-book dialog width -> {OpenBookW}"); }
        if (IsBadSize(ob.Height)) { ob.Height = OpenBookH; fixes.Add($"open-book dialog height -> {OpenBookH}"); }
        if (IsBadCoord(ob.X)) { ob.X = null; fixes.Add("open-book dialog X cleared"); }
        if (IsBadCoord(ob.Y)) { ob.Y = null; fixes.Add("open-book dialog Y cleared"); }

        // Dictionary dialog
        var dd = state.DictionaryDialog ??= new DictionaryDialogState();
        if (IsBadSize(dd.Width)) { dd.Width = DictW; fixes.Add($"dictionary dialog width -> {DictW}"); }
        if (IsBadSize(dd.Height)) { dd.Height = DictH; fixes.Add($"dictionary dialog height -> {DictH}"); }
        if (IsBadCoord(dd.X)) { dd.X = null; fixes.Add("dictionary dialog X cleared"); }
        if (IsBadCoord(dd.Y)) { dd.Y = null; fixes.Add("dictionary dialog Y cleared"); }

        // Search dialog
        var sd = state.SearchDialog ??= new SearchDialogState();
        if (sd.ProximityDistance < 0) { sd.ProximityDistance = 10; fixes.Add("search proximity distance -> 10"); }

        // Book windows
        state.BookWindows ??= new List<BookWindowState>();
        int removed = state.BookWindows.RemoveAll(w => w == null || w.BookIndex < 0);
        if (removed > 0) fixes.Add($"removed {removed} book window(s) with a negative book index");
        foreach (var w in state.BookWindows)
        {
            if (IsBadSize(w.Width)) { w.Width = BookW; fixes.Add($"book window '{w.BookFileName}' width -> {BookW}"); }
            if (IsBadSize(w.Height)) { w.Height = BookH; fixes.Add($"book window '{w.BookFileName}' height -> {BookH}"); }
            if (IsBadCoord(w.X)) w.X = null;
            if (IsBadCoord(w.Y)) w.Y = null;
            if (string.IsNullOrEmpty(w.WindowId)) { w.WindowId = Guid.NewGuid().ToString(); fixes.Add("book window assigned a new id"); }
            if (w.CurrentHitIndex < 1) { w.CurrentHitIndex = 1; fixes.Add("book window hit index -> 1"); }
            if (w.TotalHits < 0) { w.TotalHits = 0; fixes.Add("book window total hits -> 0"); }
            if (w.TabIndex < 0) { w.TabIndex = 0; fixes.Add("book window tab index -> 0"); }
        }

        // Preferences
        var pref = state.Preferences ??= new ApplicationPreferences();
        if (pref.MaxRecentBooks < 0) { pref.MaxRecentBooks = 10; fixes.Add("max recent books -> 10"); }
        if (string.IsNullOrWhiteSpace(pref.InterfaceLanguage)) { pref.InterfaceLanguage = "en"; fixes.Add("interface language -> en"); }
        pref.RecentBooks ??= new List<RecentBookItem>();
        int recRemoved = pref.RecentBooks.RemoveAll(r => r == null || r.BookIndex < 0);
        if (recRemoved > 0) fixes.Add($"removed {recRemoved} recent book(s) with a negative index");
        if (pref.RecentBooks.Count > pref.MaxRecentBooks)
        {
            int trim = pref.RecentBooks.Count - pref.MaxRecentBooks;
            pref.RecentBooks.RemoveRange(pref.MaxRecentBooks, trim);
            fixes.Add($"trimmed {trim} recent book(s) over the {pref.MaxRecentBooks} max");
        }

        return fixes;
    }

    /// <summary>
    /// Non-mutating report used by <c>IApplicationStateService.ValidateStateAsync</c>. Every issue we detect is
    /// repairable by <see cref="Sanitize"/>, so <see cref="StateValidationResult.CanRecover"/> is always true
    /// (true corruption surfaces earlier, as a deserialization failure).
    /// </summary>
    public static StateValidationResult Validate(ApplicationState state)
    {
        if (state == null)
            return new StateValidationResult { IsValid = false, CanRecover = false, Errors = new[] { "state is null" } };

        var warnings = new List<string>();
        if (string.IsNullOrEmpty(state.Version)) warnings.Add("missing version information");
        if (state.MainWindow != null && (IsBadSize(state.MainWindow.Width) || IsBadSize(state.MainWindow.Height)))
            warnings.Add("invalid main window dimensions");
        if (state.BookWindows != null && state.BookWindows.Any(w => w == null || w.BookIndex < 0))
            warnings.Add("one or more book windows have an invalid book index");
        if (state.Preferences != null && state.Preferences.MaxRecentBooks < 0)
            warnings.Add("invalid max recent books");

        var result = new StateValidationResult
        {
            IsValid = warnings.Count == 0,
            CanRecover = true,
            Warnings = warnings.ToArray()
        };
        if (!result.IsValid)
            result.SuggestedAction = "State has recoverable issues; sanitization will repair them on load.";
        return result;
    }

    private static bool IsBadSize(double v) => double.IsNaN(v) || double.IsInfinity(v) || v <= 0;
    private static bool IsBadCoord(double? v) => v.HasValue && (double.IsNaN(v.Value) || double.IsInfinity(v.Value));

    /// <summary>Compare dotted numeric versions ("1", "1.0", "1.2.3"). Non-numeric parts sort as 0.</summary>
    internal static int CompareVersions(string a, string b)
    {
        int[] pa = ParseVersion(a), pb = ParseVersion(b);
        int n = Math.Max(pa.Length, pb.Length);
        for (int i = 0; i < n; i++)
        {
            int x = i < pa.Length ? pa[i] : 0;
            int y = i < pb.Length ? pb[i] : 0;
            if (x != y) return x.CompareTo(y);
        }
        return 0;
    }

    private static int[] ParseVersion(string v) =>
        (v ?? string.Empty).Split('.').Select(p => int.TryParse(p, out var n) ? n : 0).ToArray();
}
