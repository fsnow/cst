using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// #623: books floated into a separate window, or dragged to a second split, were not reopened at startup.
///
/// <para>
/// Two defects, one on top of the other. The visible one was that the saved state was <b>deleted on a
/// move</b>: Dock uses the same removal for "moved" and "closed", so cleanup wired to removal events
/// destroyed the state of a book the user had merely dragged. Underneath it, the save walk only ever visited
/// the <b>first</b> document dock in the main window — so even with the deletes gone, a moved book's entry
/// would survive but never update again, reopening at a stale reading position with a tab index frozen at
/// zero.
/// </para>
///
/// <para>
/// The second defect is what these tests pin, because it is the one with a pure, reachable seam. The deletion
/// sites need a live <c>BookDisplayViewModel</c> (services and a CEF View), so they are covered by the
/// documented invariant in DOCK_WEBVIEW_WORKAROUNDS.md §E.1 and by manual verification, not from here.
/// </para>
/// </summary>
public class CstDockFactoryBookStateScopeTests
{
    private sealed class TestDockFactory : CstDockFactory
    {
        private readonly List<IDock> _floating = new();
        public void SetFloatingLayouts(params IDock[] layouts) { _floating.Clear(); _floating.AddRange(layouts); }
        internal override IEnumerable<IDock> GetFloatingLayouts() => _floating;
    }

    private static DocumentDock Dock(string id, params IDockable[] docs)
    {
        var dock = new DocumentDock { Id = id, VisibleDockables = new ObservableCollection<IDockable>(docs) };
        foreach (var d in docs) d.Owner = dock;
        return dock;
    }

    private static RootDock Root(string id, params IDockable[] children)
    {
        var root = new RootDock { Id = id, VisibleDockables = new ObservableCollection<IDockable>(children) };
        foreach (var c in children) c.Owner = root;
        return root;
    }

    private static Document Doc(string id) => new() { Id = id };

    // ---- The walk ---------------------------------------------------------------------------------

    [Fact]
    public void The_walk_reaches_a_second_split_in_the_main_window()
    {
        // FindDocumentDock returns the FIRST document dock and stops. A book dragged into a split beside it
        // was therefore never re-saved, so its position and tab index froze where it left.
        var f = new TestDockFactory();
        var left = Dock("LeftDock", Doc("a"));
        var right = Dock("RightDock", Doc("b"));
        f._context = Root("MainRoot", left, right);

        var docks = f.CollectAllBookDocks();

        Assert.Contains(left, docks);
        Assert.Contains(right, docks);
    }

    [Fact]
    public void The_walk_reaches_floating_windows()
    {
        var f = new TestDockFactory();
        var main = Dock("MainDock", Doc("a"));
        var floated = Dock("FloatDock", Doc("b"));
        f._context = Root("MainRoot", main);
        f.SetFloatingLayouts(Root("FloatRoot", floated));

        var docks = f.CollectAllBookDocks();

        Assert.Contains(main, docks);
        Assert.Contains(floated, docks);
    }

    [Fact]
    public void The_walk_finds_docks_nested_below_splitters()
    {
        // A real split is not a flat sibling — Dock nests ProportionalDocks. A non-recursive walk would see
        // the outer container and miss every document dock inside it.
        var f = new TestDockFactory();
        var inner = Dock("InnerDock", Doc("a"));
        var middle = Root("Splitter", inner);
        f._context = Root("MainRoot", middle);

        Assert.Contains(inner, f.CollectAllBookDocks());
    }

    [Fact]
    public void Main_window_docks_come_before_floating_ones()
    {
        // The tab order persisted for restore is a single counter over this walk, so the walk order IS the
        // restored order. Main-window books ahead of floated ones is the rule; what matters for the test is
        // that it is deterministic rather than incidental.
        var f = new TestDockFactory();
        var main = Dock("MainDock", Doc("a"));
        var floated = Dock("FloatDock", Doc("b"));
        f._context = Root("MainRoot", main);
        f.SetFloatingLayouts(Root("FloatRoot", floated));

        var docks = f.CollectAllBookDocks();

        Assert.True(docks.IndexOf(main) < docks.IndexOf(floated));
    }

    [Fact]
    public void Multiple_floating_windows_are_all_walked_in_order()
    {
        var f = new TestDockFactory();
        var one = Dock("Float1Dock", Doc("a"));
        var two = Dock("Float2Dock", Doc("b"));
        f._context = Root("MainRoot");
        f.SetFloatingLayouts(Root("Float1Root", one), Root("Float2Root", two));

        var docks = f.CollectAllBookDocks();

        Assert.Equal(new[] { one, two }, docks);
    }

    [Fact]
    public void A_layout_with_no_document_docks_yields_nothing_rather_than_throwing()
    {
        var f = new TestDockFactory();
        f._context = Root("MainRoot");

        Assert.Empty(f.CollectAllBookDocks());
    }

    // ---- Restore order ----------------------------------------------------------------------------

    private static BookWindowState Book(string id, int tabIndex) =>
        new() { WindowId = id, TabIndex = tabIndex, BookIndex = 0 };

    [Fact]
    public void Books_are_restored_in_tab_order_not_in_list_order()
    {
        // BookWindows is maintained by remove-then-add, so its natural order is LAST-TOUCHED: scrolling the
        // first tab moves it to the end of the list. Restoring in list order shuffled the tabs on every
        // launch.
        var saved = new[] { Book("third", 2), Book("first", 0), Book("second", 1) };

        Assert.Equal(new[] { "first", "second", "third" },
            BookRestoreOrder.Apply(saved).Select(b => b.WindowId));
    }

    [Fact]
    public void A_tie_falls_back_to_the_persisted_order_rather_than_an_arbitrary_one()
    {
        // Entries need not all come from the same save, so two can share an index. A stable sort makes that
        // deterministic; an unstable one would reintroduce the shuffle intermittently, which is far worse to
        // diagnose than doing it every time.
        var saved = new[] { Book("a", 1), Book("b", 1), Book("c", 1) };

        Assert.Equal(new[] { "a", "b", "c" }, BookRestoreOrder.Apply(saved).Select(b => b.WindowId));
    }

    [Fact]
    public void An_empty_or_null_set_restores_nothing()
    {
        Assert.Empty(BookRestoreOrder.Apply(null));
        Assert.Empty(BookRestoreOrder.Apply(new List<BookWindowState>()));
    }

    [Fact]
    public void Every_saved_book_survives_the_ordering()
    {
        // The ordering must not be a filter. Losing a book here would look exactly like the bug being fixed.
        var saved = Enumerable.Range(0, 20).Select(i => Book($"b{i}", 19 - i)).ToList();

        Assert.Equal(20, BookRestoreOrder.Apply(saved).Count);
    }
}
