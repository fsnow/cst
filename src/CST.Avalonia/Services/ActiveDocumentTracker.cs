using System;
using System.Collections.Generic;
using System.Linq;
using Dock.Model.Core;
using Serilog;

namespace CST.Avalonia.Services;

/// <summary>
/// Which documents the user has most recently worked in, most recent first. (#621)
///
/// <para>
/// Window-level commands — zoom, ⌘W, ⌘G, ⌘E, ⌘D, ⌘F — have to know which document to act on, and
/// <see cref="DocumentTargetResolver"/> answers that from Avalonia's keyboard focus. That works until focus
/// is inside a CEF WebView, which hosts a native surface Avalonia never sees; resolution then falls through
/// to "the first document dock in tree order", which in a split is simply the wrong pane. Since books, the
/// PDF viewer and the Welcome page are ALL browsers, that is most of the reader's surface.
/// </para>
///
/// <para>
/// This holds the signal Avalonia cannot supply, fed from three places that CAN see an interaction: the
/// dock model's own activation event, CEF's focus callback, and Avalonia focus transitions. What it stores
/// is deliberately thin — it never decides anything, it only records order.
/// </para>
///
/// <para>
/// <b>Everything is recorded, tools included.</b> Filtering happens when the list is READ, by asking the
/// caller's layout which of these dockables it contains. That single rule buys three things at once: a
/// document whose <c>Owner</c> is not yet set on its first activation is still recorded; a tool activation
/// can never clobber the document history, because tools simply fail the containment test; and one flat
/// list serves every window, since each window's own layout selects its own entries. Keying by window was
/// the obvious alternative and is a trap — Dock's <c>FindRoot</c> returns the NEAREST root, the inner
/// <c>WindowLayout</c>, while every caller holds the outer <c>Root</c>, so the keys would never match.
/// </para>
///
/// <para>
/// Entries are weak. A closed tab is evicted explicitly, but the weak reference is what guarantees this can
/// never be the thing keeping a disposed ViewModel — and its WebView — alive for the session.
/// </para>
/// </summary>
public class ActiveDocumentTracker
{
    /// <summary>
    /// How much history to keep. Deep enough to survive a detour through other tabs and back, shallow
    /// enough that "most recent" still means something. The read side walks it in order and stops at the
    /// first entry the asking layout contains, so extra depth costs a few reference comparisons.
    /// </summary>
    private const int Capacity = 8;

    private readonly List<WeakReference<IDockable>> _recent = new();
    private readonly object _gate = new();

    /// <summary>
    /// Records an interaction. Safe to call redundantly — the same dockable twice in a row is one entry,
    /// which matters because the three feeds overlap by design: clicking a tab raises both an activation
    /// and an Avalonia focus change, and clicking a book's text raises a CEF focus callback that may follow
    /// either.
    /// </summary>
    /// <param name="source">
    /// Which feed reported this, for the log. The feeds overlap, and when two of them disagree about what
    /// the user just clicked — which is the failure #621 hit in a three-way split — the document alone does
    /// not say who was wrong. (#621)
    /// </param>
    public void Note(IDockable? dockable, string source = "?")
    {
        if (dockable == null) return;

        bool changed;
        lock (_gate)
        {
            changed = _recent.Count == 0 || !(_recent[0].TryGetTarget(out var head) && ReferenceEquals(head, dockable));

            // Reference identity, not Id: splits produce several docks sharing the id "MainDocumentDock",
            // and two books of the same text would compare equal on anything less exact.
            _recent.RemoveAll(w => !w.TryGetTarget(out var t) || ReferenceEquals(t, dockable));
            _recent.Insert(0, new WeakReference<IDockable>(dockable));
            if (_recent.Count > Capacity) _recent.RemoveRange(Capacity, _recent.Count - Capacity);
        }

        // Only when the front of the history actually moves. The three feeds overlap, so logging every
        // note would emit several lines per click and say nothing the first one didn't.
        if (changed)
            Log.ForContext<ActiveDocumentTracker>()
                .Debug("Active document is now {Dockable} (via {Source})", dockable.Id, source);
    }

    /// <summary>
    /// Drops a dockable from the history — called when a tab closes, so the list cannot offer a document
    /// that no longer exists. Not strictly required (the resolver's containment check rejects a closed
    /// dockable anyway, and the weak reference eventually clears), but doing it at the close makes the
    /// list's contents match reality at the moment reality changes, rather than eventually.
    /// </summary>
    public void Forget(IDockable? dockable)
    {
        if (dockable == null) return;

        lock (_gate)
        {
            _recent.RemoveAll(w => !w.TryGetTarget(out var t) || ReferenceEquals(t, dockable));
        }
    }

    /// <summary>
    /// The live history, most recent first. Snapshotted, so a caller can walk it while a feed writes.
    /// </summary>
    public IReadOnlyList<IDockable> Recent
    {
        get
        {
            lock (_gate)
            {
                // Prune here as well as on write: a session that opens and closes documents without ever
                // being asked for the history would otherwise accumulate dead references up to Capacity.
                _recent.RemoveAll(w => !w.TryGetTarget(out _));
                return _recent
                    .Select(w => w.TryGetTarget(out var t) ? t : null)
                    .Where(d => d != null)
                    .Cast<IDockable>()
                    .ToList();
            }
        }
    }

    /// <summary>Forgets everything. For tests; the app has no reason to call it.</summary>
    internal void Clear()
    {
        lock (_gate) _recent.Clear();
    }
}
