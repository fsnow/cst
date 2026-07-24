using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CST.Avalonia.Models;
using CST.Conversion;

namespace CST.Avalonia.Services;

/// <summary>
/// The recently-opened-books (MRU) list (#44). Backs the File → "Open Recent" menu. A single shared
/// instance records each user-opened book and exposes the list; the menu rebuilds on <see cref="Changed"/>.
///
/// The list lives in <see cref="ApplicationPreferences.RecentBooks"/> (persisted with the rest of app state);
/// <see cref="ApplicationPreferences.MaxRecentBooks"/> caps it, and a value of 0 disables the list entirely.
/// </summary>
public sealed class RecentBooksService
{
    private readonly IApplicationStateService _state;

    /// <summary>Raised after the MRU list changes, so the (per-window) native menus can rebuild.</summary>
    public event EventHandler? Changed;

    public RecentBooksService(IApplicationStateService state) => _state = state;

    private ApplicationPreferences Prefs => _state.Current.Preferences;

    /// <summary>The MRU list, most-recent first.</summary>
    public IReadOnlyList<RecentBookItem> Items => Prefs.RecentBooks;

    /// <summary>
    /// Record a book as just-opened: move it to the front (de-duplicating by book index), stamp the time,
    /// and trim to <see cref="ApplicationPreferences.MaxRecentBooks"/>. When the cap is 0 the feature is
    /// disabled — the list is cleared and stays empty.
    /// </summary>
    public void Record(CST.Book book)
    {
        if (book == null)
            return;

        var list = Prefs.RecentBooks;
        // Promote, not duplicate. Match by index OR filename so a corpus reindex (indices shift between
        // releases) can't leave a stale twin of the same file. (Fable LOW-3)
        list.RemoveAll(r => r != null
            && (r.BookIndex == book.Index
                || (!string.IsNullOrEmpty(book.FileName) && r.BookFileName == book.FileName)));

        var max = Prefs.MaxRecentBooks;
        if (max <= 0)
        {
            // Disabled: don't grow the list. Clear only if it held stale entries (avoids a spurious Changed).
            if (list.Count > 0) { list.Clear(); MarkChanged(); }
            return;
        }

        list.Insert(0, new RecentBookItem
        {
            BookIndex = book.Index,
            BookFileName = book.FileName ?? string.Empty,
            DisplayName = DisplayName(book),
            LastOpened = DateTime.UtcNow
        });
        if (list.Count > max)
            list.RemoveRange(max, list.Count - max);

        MarkChanged();
    }

    /// <summary>Empty the MRU list (the menu's "Clear Menu" item).</summary>
    public void Clear()
    {
        if (Prefs.RecentBooks.Count == 0)
            return;
        Prefs.RecentBooks.Clear();
        MarkChanged();
    }

    /// <summary>Trim the list to the current cap (e.g. after the user lowers MaxRecentBooks in Settings).
    /// A cap of 0 clears it. Raises <see cref="Changed"/> only if something was removed.</summary>
    public void TrimToMax()
    {
        var list = Prefs.RecentBooks;
        var max = Math.Max(0, Prefs.MaxRecentBooks);
        if (list.Count <= max)
            return;
        list.RemoveRange(max, list.Count - max);
        MarkChanged();
    }

    private void MarkChanged()
    {
        _state.MarkDirty();   // persist via the state timer/shutdown save (STATE-2), like the other prefs
        Changed?.Invoke(this, EventArgs.Empty);
    }

    /// <summary>
    /// The menu label for a book, matching a book tab's title: the last segment of <c>LongNavPath</c>
    /// converted from Devanagari to <paramref name="script"/> (capitalized), falling back to the filename.
    /// (Kept in sync with BookDisplayViewModel.GetBookDisplayName.)
    /// </summary>
    public static string DisplayName(CST.Book book, Script script = Script.Latin)
    {
        if (book == null)
            return string.Empty;
        if (!string.IsNullOrEmpty(book.LongNavPath))
        {
            var parts = book.LongNavPath.Split('/');
            var name = parts[^1];
            return script != Script.Devanagari
                ? ScriptConverter.Convert(name, Script.Devanagari, script, true)
                : name;
        }
        return !string.IsNullOrEmpty(book.FileName)
            ? Path.GetFileNameWithoutExtension(book.FileName)
            : "Unknown Book";
    }
}
