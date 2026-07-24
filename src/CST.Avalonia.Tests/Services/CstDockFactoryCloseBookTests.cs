using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CST.Avalonia.Services;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Xunit;

namespace CST.Avalonia.Tests.Services;

// #494: close-by-id (CstDockFactory.CloseBook) must reach a book that lives in a FLOATING window, not just the
// main window — the old path searched/removed main-window-only. A real CstHostWindow needs an Avalonia platform
// the headless test host lacks, so we inject handcrafted floating layouts via the GetFloatingLayouts seam and
// seed the main tree via _context. Uses plain Dock Documents (BookDisplayViewModel needs live services); the
// book-specific CEF branches in CloseDockable are guarded and skipped for a plain Document.
public class CstDockFactoryCloseBookTests
{
    private sealed class TestDockFactory : CstDockFactory
    {
        private readonly List<IDock> _floating = new();
        public void SetFloatingLayouts(params IDock[] layouts) { _floating.Clear(); _floating.AddRange(layouts); }
        internal override IEnumerable<IDock> GetFloatingLayouts() => _floating;
    }

    // A DocumentDock holding the given documents, with Owner wired (CloseDockable removes by Owner).
    private static (RootDock root, DocumentDock dock) Window(string dockId, params Document[] docs)
    {
        var dock = new DocumentDock { Id = dockId, VisibleDockables = new ObservableCollection<IDockable>(docs) };
        foreach (var d in docs) d.Owner = dock;
        dock.ActiveDockable = docs.LastOrDefault();
        var root = new RootDock { Id = dockId + "Root", VisibleDockables = new ObservableCollection<IDockable> { dock } };
        dock.Owner = root;
        return (root, dock);
    }

    private static Document Doc(string id, bool canClose = true) => new() { Id = id, CanClose = canClose };

    [Fact]
    public void CloseBook_removes_a_book_in_a_floating_window()
    {
        var f = new TestDockFactory();
        var a = Doc("floatBook"); var b = Doc("otherBook");
        var (floatRoot, floatDock) = Window("FloatDock", a, b);
        floatDock.ActiveDockable = a;   // close the ACTIVE doc, so the neighbor-reactivation branch actually runs
        f._context = new RootDock { Id = "MainRoot", VisibleDockables = new ObservableCollection<IDockable>() };  // main has no such book
        f.SetFloatingLayouts(floatRoot);

        f.CloseBook("floatBook");

        Assert.DoesNotContain(a, floatDock.VisibleDockables);          // removed from the FLOATING dock
        Assert.Contains(b, floatDock.VisibleDockables);                // sibling untouched
        Assert.Equal(b, floatDock.ActiveDockable);                     // sibling activated after the active doc closed
    }

    [Fact]
    public void CloseBook_removes_a_book_in_the_main_window()
    {
        var f = new TestDockFactory();
        var a = Doc("mainBook"); var b = Doc("otherBook");
        var (mainRoot, mainDock) = Window("MainDock", a, b);
        f._context = mainRoot;

        f.CloseBook("mainBook");

        Assert.DoesNotContain(a, mainDock.VisibleDockables);
        Assert.Contains(b, mainDock.VisibleDockables);
    }

    [Fact]
    public void CloseBook_is_a_no_op_for_an_unknown_id()
    {
        var f = new TestDockFactory();
        var (mainRoot, mainDock) = Window("MainDock", Doc("book1"));
        f._context = mainRoot;

        f.CloseBook("does-not-exist");   // must not throw

        Assert.Single(mainDock.VisibleDockables);
    }

    [Fact]
    public void CloseBook_refuses_a_non_closable_dockable()
    {
        // CanClose=false (e.g. the Welcome tab) must survive — the old raw-remove path would have ripped it out.
        var f = new TestDockFactory();
        var welcome = Doc("WelcomeDocument", canClose: false);
        var (mainRoot, mainDock) = Window("MainDock", welcome, Doc("book1"));
        f._context = mainRoot;

        f.CloseBook("WelcomeDocument");

        Assert.Contains(welcome, mainDock.VisibleDockables);   // still present
    }
}
