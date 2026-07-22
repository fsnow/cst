using CST.Avalonia.Services;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// #458: cross-window drag of a book/PDF must dispose its live CEF browser before the re-parent, or the
/// SIGSEGV returns. The dispose itself needs a live View (GUI-only), but the classification it hinges on —
/// "is this move crossing into another window's layout?" — is <see cref="CstDockFactory.GetRootOwner"/>,
/// a pure ownership walk that these tests pin down.
/// </summary>
public class CstDockFactoryCrossWindowTests
{
    private sealed class TestDocument : Document { }

    // A document nested: root → documentDock → doc. Returns (root, doc).
    private static (RootDock Root, DocumentDock Dock, TestDocument Doc) BuildWindowLayout(string id)
    {
        var doc = new TestDocument { Id = id + "-doc", Title = id };
        var dock = new DocumentDock
        {
            Id = "MainDocumentDock",
            VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable> { doc }
        };
        var root = new RootDock
        {
            Id = id + "-root",
            VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable> { dock }
        };
        // Owner links are what GetRootOwner walks; the framework sets these on real adds.
        dock.Owner = root;
        doc.Owner = dock;
        return (root, dock, doc);
    }

    [Fact]
    public void GetRootOwner_ResolvesToTheDocumentsOwnWindowRoot()
    {
        var (root, _, doc) = BuildWindowLayout("A");
        Assert.Same(root, CstDockFactory.GetRootOwner(doc));
    }

    [Fact]
    public void GetRootOwner_FromADock_ResolvesToItsRoot()
    {
        var (root, dock, _) = BuildWindowLayout("A");
        Assert.Same(root, CstDockFactory.GetRootOwner(dock));
    }

    [Fact]
    public void GetRootOwner_TwoWindows_AreDistinct_SoAMoveBetweenThemIsCrossWindow()
    {
        var (rootA, _, docA) = BuildWindowLayout("A");
        var (rootB, _, docB) = BuildWindowLayout("B");

        var ownerA = CstDockFactory.GetRootOwner(docA);
        var ownerB = CstDockFactory.GetRootOwner(docB);

        Assert.NotSame(ownerA, ownerB);          // different windows → cross-window
        Assert.Same(rootA, ownerA);
        Assert.Same(rootB, ownerB);
    }

    [Fact]
    public void GetRootOwner_TwoDocksInTheSameWindow_ShareARoot_SoAMoveBetweenThemIsNotCrossWindow()
    {
        // A split within one window: two document docks under the same root.
        var (root, dockLeft, _) = BuildWindowLayout("A");
        var docRight = new TestDocument { Id = "A-doc2", Title = "A2" };
        var dockRight = new DocumentDock
        {
            Id = "MainDocumentDock",
            VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable> { docRight }
        };
        root.VisibleDockables!.Add(dockRight);
        dockRight.Owner = root;
        docRight.Owner = dockRight;

        Assert.Same(CstDockFactory.GetRootOwner(dockLeft), CstDockFactory.GetRootOwner(dockRight));
    }

    [Fact]
    public void GetRootOwner_ARootItself_ReturnsItself()
    {
        var (root, _, _) = BuildWindowLayout("A");
        Assert.Same(root, CstDockFactory.GetRootOwner(root));
    }

    [Fact]
    public void GetRootOwner_NullOrOwnerless_ReturnsNull()
    {
        Assert.Null(CstDockFactory.GetRootOwner(null));
        Assert.Null(CstDockFactory.GetRootOwner(new TestDocument { Id = "orphan" }));  // no Owner chain
    }
}
