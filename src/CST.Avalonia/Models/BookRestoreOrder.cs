using System.Collections.Generic;
using System.Linq;

namespace CST.Avalonia.Models;

/// <summary>
/// The order saved books are reopened in. (#623)
///
/// <para>
/// Its own type, small as it is, because the ordering is not a property of the list it is applied to.
/// <c>ApplicationState.BookWindows</c> is maintained by remove-then-add
/// (<c>ApplicationStateService.UpdateBookWindowState</c>), so its natural order is <b>last-touched</b> — read
/// the file and the books appear in the order they were last scrolled, not the order they sat in. Restoring
/// in list order therefore shuffled the tabs, and no amount of correctness elsewhere would have fixed it.
/// </para>
///
/// <para>
/// Restoration flattens every book into the main window's document dock beside the Welcome page. Restoring
/// the LAYOUT — which window, which split — is deliberately out of scope, so a single flat index is the whole
/// of what has to survive.
/// </para>
/// </summary>
internal static class BookRestoreOrder
{
    /// <summary>
    /// Saved books in the order they should be reopened.
    ///
    /// <para>
    /// <b>Stable, and that is load-bearing.</b> Not every entry is necessarily written by the same save: an
    /// entry can carry an index from an earlier walk, so two books can share one. LINQ's <c>OrderBy</c> is
    /// documented as a stable sort, which makes a tie fall back to the persisted list order rather than to
    /// an arbitrary one — the same two books come back in the same two places on every launch. Swapping in
    /// an unstable sort would reintroduce the shuffle this exists to remove, and only intermittently.
    /// </para>
    /// </summary>
    internal static List<BookWindowState> Apply(IEnumerable<BookWindowState>? books) =>
        books is null
            ? new List<BookWindowState>()
            : books.OrderBy(b => b.TabIndex).ToList();
}
