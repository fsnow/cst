using CST.Avalonia.Services;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// #443: window-level shortcuts (Cmd+W / Cmd+G / Cmd+E / Cmd+D / Cmd+F) used to act on the FIRST
/// DocumentDock in the layout, so in a split they hit the left/top pane no matter where the user was
/// working. Resolution now follows focus, with the old first-dock behaviour as the fallback.
/// </summary>
public class DocumentTargetResolverTests
{
    private sealed class TestDocument : Document
    {
    }

    // root
    //  +-- documentDockA [docA1, docA2]   <- first in tree order
    //  +-- documentDockB [docB1]          <- the "second split"
    private static (RootDock Root, DocumentDock A, DocumentDock B, TestDocument A1, TestDocument A2, TestDocument B1)
        BuildSplitLayout()
    {
        var docA1 = new TestDocument { Id = "docA1", Title = "A1" };
        var docA2 = new TestDocument { Id = "docA2", Title = "A2" };
        var docB1 = new TestDocument { Id = "docB1", Title = "B1" };

        var dockA = new DocumentDock
        {
            Id = "MainDocumentDock",
            VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable> { docA1, docA2 },
            ActiveDockable = docA1
        };

        var dockB = new DocumentDock
        {
            // Real splits produce TWO docks both carrying this id - resolution must not depend on it.
            Id = "MainDocumentDock",
            VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable> { docB1 },
            ActiveDockable = docB1
        };

        var root = new RootDock
        {
            Id = "Root",
            VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable> { dockA, dockB }
        };

        return (root, dockA, dockB, docA1, docA2, docB1);
    }

    [Fact]
    public void ResolveActiveDocument_FocusInSecondSplit_ReturnsThatSplitsDocument()
    {
        var (root, _, _, _, _, docB1) = BuildSplitLayout();

        root.FocusedDockable = docB1;

        // The bug: this returned docA1, the first dock's active tab.
        Assert.Same(docB1, DocumentTargetResolver.ResolveActiveDocument(root));
    }

    // The `preferred` argument is what production actually passes: real Avalonia keyboard focus, since
    // Dock never populates RootDock.FocusedDockable in this app. These are the #443 bug as the app runs it.

    [Fact]
    public void ResolveActiveDocument_PreferredDocumentInSecondSplit_ReturnsIt()
    {
        var (root, _, _, _, _, docB1) = BuildSplitLayout();

        // Focus resolved from the visual tree: the user clicked the second split's tab or its book body.
        Assert.Same(docB1, DocumentTargetResolver.ResolveActiveDocument(root, docB1));
    }

    [Fact]
    public void ResolveActiveDocument_PreferredIsADocumentDock_ReturnsThatDocksActiveTab()
    {
        var (root, _, dockB, _, _, docB1) = BuildSplitLayout();

        // Focus landed on the pane itself (tab-strip background) rather than on a tab.
        Assert.Same(docB1, DocumentTargetResolver.ResolveActiveDocument(root, dockB));
    }

    [Fact]
    public void ResolveActiveDocument_PreferredNotInAnyDocumentDock_FallsBackToFirstDocumentDock()
    {
        var (root, _, _, docA1, _, _) = BuildSplitLayout();

        // A tool (Search/Dictionary/book tree) has focus - no better signal, so behave as before #443.
        var tool = new Tool { Id = "SearchTool", Title = "Search" };

        Assert.Same(docA1, DocumentTargetResolver.ResolveActiveDocument(root, tool));
    }

    [Fact]
    public void ResolveActiveDocument_PreferredFromAnotherWindowsLayout_FallsBackRatherThanActingOnIt()
    {
        var (root, _, _, docA1, _, _) = BuildSplitLayout();

        // Avalonia's FocusManager is app-global, so a floating window's handler can be handed a dockable
        // that lives in the MAIN window's layout. Containment is the only guard - it must hold, or a
        // shortcut in one window would act on another window's tab.
        var (otherRoot, _, _, _, _, foreignDoc) = BuildSplitLayout();
        Assert.NotNull(otherRoot);

        Assert.Same(docA1, DocumentTargetResolver.ResolveActiveDocument(root, foreignDoc));
    }

    [Fact]
    public void ResolveActiveDocument_PreferredWins_EvenWhenDockFocusPointsElsewhere()
    {
        var (root, _, _, _, docA2, docB1) = BuildSplitLayout();

        root.FocusedDockable = docA2;

        // Real keyboard focus is the better signal; Dock's own value is stale-at-best here.
        Assert.Same(docB1, DocumentTargetResolver.ResolveActiveDocument(root, docB1));
    }

    [Fact]
    public void ResolveActiveDocument_PreferredAlreadyClosed_FallsBackToFirstDocumentDock()
    {
        var (root, _, dockB, docA1, _, docB1) = BuildSplitLayout();

        dockB.VisibleDockables!.Remove(docB1);
        dockB.ActiveDockable = null;

        Assert.Same(docA1, DocumentTargetResolver.ResolveActiveDocument(root, docB1));
    }

    [Fact]
    public void ResolveActiveDocument_FocusOnSecondSplitsDock_ReturnsItsActiveDocument()
    {
        var (root, _, dockB, _, _, docB1) = BuildSplitLayout();

        // Focus can land on the dock rather than the document itself.
        root.FocusedDockable = dockB;

        Assert.Same(docB1, DocumentTargetResolver.ResolveActiveDocument(root));
    }

    [Fact]
    public void ResolveActiveDocument_FocusOnNonActiveTab_PrefersTheFocusedDocument()
    {
        var (root, _, _, _, docA2, _) = BuildSplitLayout();

        root.FocusedDockable = docA2;

        Assert.Same(docA2, DocumentTargetResolver.ResolveActiveDocument(root));
    }

    [Fact]
    public void ResolveActiveDocument_NoFocus_FallsBackToFirstDocumentDock()
    {
        var (root, _, _, docA1, _, _) = BuildSplitLayout();

        root.FocusedDockable = null;

        Assert.Same(docA1, DocumentTargetResolver.ResolveActiveDocument(root));
    }

    [Fact]
    public void ResolveActiveDocument_StaleFocusOnClosedDocument_FallsBackToFirstDocumentDock()
    {
        var (root, _, dockB, docA1, _, docB1) = BuildSplitLayout();

        // Focus names a document that has since been closed - FocusedDockable is not cleared for us.
        root.FocusedDockable = docB1;
        dockB.VisibleDockables!.Remove(docB1);
        dockB.ActiveDockable = null;

        Assert.Same(docA1, DocumentTargetResolver.ResolveActiveDocument(root));
    }

    [Fact]
    public void ResolveActiveDocument_FocusOnToolOutsideAnyDocumentDock_FallsBackToFirstDocumentDock()
    {
        var (root, _, _, docA1, _, _) = BuildSplitLayout();

        var tool = new Tool { Id = "SearchTool", Title = "Search" };
        var toolDock = new ToolDock
        {
            Id = "LeftToolDock",
            VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable> { tool },
            ActiveDockable = tool
        };
        root.VisibleDockables!.Insert(0, toolDock);
        root.FocusedDockable = tool;

        Assert.Same(docA1, DocumentTargetResolver.ResolveActiveDocument(root));
    }


    /// <summary>
    /// Guards the assumption the resolver is built on: in Dock 11.3.6.5 the Tool class implements
    /// IDocument as well as ITool, so a document CANNOT be identified by type - the resolver has to ask
    /// the layout which dock holds the dockable. If a Dock upgrade ever separates these, this test fails
    /// and the resolver could be simplified.
    /// </summary>
    [Fact]
    public void DockTool_AlsoImplementsIDocument_SoTypeTestsCannotIdentifyDocuments()
    {
        Assert.True(new Tool() is Dock.Model.Controls.IDocument);
    }

    [Fact]
    public void ResolveActiveDocument_NullLayout_ReturnsNull()
    {
        Assert.Null(DocumentTargetResolver.ResolveActiveDocument(null));
    }

    [Fact]
    public void FindFirstDocumentDock_NestedLayout_FindsDockBelowTheRoot()
    {
        var (root, dockA, _, _, _, _) = BuildSplitLayout();

        var wrapper = new ProportionalDock
        {
            Id = "Wrapper",
            VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable> { root }
        };

        Assert.Same(dockA, DocumentTargetResolver.FindFirstDocumentDock(wrapper));
    }
}
