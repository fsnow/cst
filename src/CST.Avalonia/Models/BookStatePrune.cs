using System.Collections.Generic;
using System.Linq;

namespace CST.Avalonia.Models;

/// <summary>
/// Which persisted book entries no longer correspond to an open book. (#624)
///
/// <para>
/// The point of pruning is to stop <c>BookWindows</c> being maintained by <b>event bookkeeping</b> and make it
/// <b>derived</b> from what actually exists. Dock uses the same removal operation for "moved" and "closed"
/// (<c>SplitToWindow</c> removes before re-adding; a cross-dock <c>MoveDockable</c> does the same), so no
/// removal event can classify itself — which is exactly how #623 happened, with the guess wired into four
/// call sites, three of them wrong. A set derived from the live docks cannot be wrong about it, because it
/// never asks.
/// </para>
///
/// <para>
/// Pure, and separate from the walk that feeds it, because the interesting cases are about <b>which set is
/// sampled when</b> rather than about threading — and that is testable here without a dock, a view model or
/// a browser. Same reasoning as <see cref="BookRestoreOrder"/>.
/// </para>
/// </summary>
internal static class BookStatePrune
{
    /// <summary>
    /// The <c>WindowId</c>s to remove: persisted entries with no live book.
    ///
    /// <para>
    /// <b>An empty live set prunes nothing.</b> Not an optimisation — it is the guard that stops this from
    /// destroying a session on every launch. At startup the layout is built with only the Welcome page, which
    /// makes it the active dockable, which fires the tab-change save. That save walks real docks and finds no
    /// books, and it runs BEFORE the restore path has copied the entries it is about to reopen. Pruning on an
    /// empty live set would therefore delete the whole saved session, every time, before anything could read
    /// it.
    /// </para>
    ///
    /// <para>
    /// Nothing is lost by the guard: with no books open, every removal has already gone through
    /// <c>CloseDockable</c>, which deletes immediately. A prune has nothing to contribute in that state.
    /// </para>
    /// </summary>
    internal static List<string> Vanished(
        IEnumerable<BookWindowState>? persisted,
        IReadOnlyCollection<string>? liveWindowIds)
    {
        if (persisted is null || liveWindowIds is null || liveWindowIds.Count == 0)
            return new List<string>();

        var live = new HashSet<string>(liveWindowIds, System.StringComparer.Ordinal);

        return persisted
            .Where(b => !string.IsNullOrEmpty(b.WindowId) && !live.Contains(b.WindowId))
            .Select(b => b.WindowId)
            .Distinct(System.StringComparer.Ordinal)
            .ToList();
    }
}
