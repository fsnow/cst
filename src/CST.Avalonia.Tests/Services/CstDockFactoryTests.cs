using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CST.Avalonia.Services;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// Q1 dock-stabilization unit tests: (a) framework-created docks are never anonymous (id-stamping),
/// and (b) the invariant spine (Root/WindowLayout/MainDock/MainDocumentDock) is protected from cleanup
/// by REFERENCE — so framework clones that copy a spine id are NOT falsely protected.
/// See docs/architecture/DOCK_SUBSYSTEM.md.
/// </summary>
public class CstDockFactoryTests
{
    private static ObservableCollection<IDockable> List(params IDockable[] items) => new(items);

    // ---- Id-stamping: framework-created docks must never be born with an empty id ----

    [Fact]
    public void CreateProportionalDock_StampsNonEmptyId()
    {
        var d = new CstDockFactory().CreateProportionalDock();
        Assert.False(string.IsNullOrEmpty(d.Id));
    }

    [Fact]
    public void CreateToolDock_StampsNonEmptyId()
    {
        var d = new CstDockFactory().CreateToolDock();
        Assert.False(string.IsNullOrEmpty(d.Id));
    }

    [Fact]
    public void CreateDocumentDock_StampsNonEmptyId()
    {
        var d = new CstDockFactory().CreateDocumentDock();
        Assert.False(string.IsNullOrEmpty(d.Id));
    }

    [Fact]
    public void CreateRootDock_StampsNonEmptyId()
    {
        var d = new CstDockFactory().CreateRootDock();
        Assert.False(string.IsNullOrEmpty(d.Id));
    }

    [Fact]
    public void Create_ProducesUniqueIds()
    {
        var f = new CstDockFactory();
        var ids = new[]
        {
            f.CreateProportionalDock().Id,
            f.CreateProportionalDock().Id,
            f.CreateToolDock().Id,
            f.CreateToolDock().Id,
            f.CreateDocumentDock().Id,
            f.CreateDocumentDock().Id,
            f.CreateRootDock().Id,
            f.CreateRootDock().Id,
        };
        Assert.Equal(ids.Length, ids.Distinct().Count());
    }

    // ---- IsProtectedSpine: reference-based (only the registered original instances) ----

    [Fact]
    public void IsProtectedSpine_TrueForRegisteredInstance()
    {
        var f = new CstDockFactory();
        var mainDock = new ProportionalDock { Id = "MainDock" };
        f._spineDocks.Add(mainDock);
        Assert.True(f.IsProtectedSpine(mainDock));
    }

    [Fact]
    public void IsProtectedSpine_FalseForCloneWithSameId()
    {
        // A framework clone copies the id but is a different instance — must NOT be protected.
        var f = new CstDockFactory();
        var original = new DocumentDock { Id = "MainDocumentDock" };
        var clone = new DocumentDock { Id = "MainDocumentDock" };
        f._spineDocks.Add(original);
        Assert.True(f.IsProtectedSpine(original));
        Assert.False(f.IsProtectedSpine(clone));
    }

    [Fact]
    public void IsProtectedSpine_FalseForUnregisteredOrNull()
    {
        var f = new CstDockFactory();
        Assert.False(f.IsProtectedSpine(new ProportionalDock { Id = "MainDock" })); // not registered
        Assert.False(f.IsProtectedSpine(null));
    }

    // ---- IsEmptyDock must never flag a protected spine instance (prevents MainDock collapse) ----

    [Fact]
    public void IsEmptyDock_NonSpineSingleChild_IsRedundant_True()
    {
        var f = new CstDockFactory();
        var child = new DocumentDock { Id = "DocDock_x", VisibleDockables = List() };
        var parent = new ProportionalDock { Id = "PDock_random", VisibleDockables = List(child) };
        Assert.True(f.IsEmptyDock(parent));
    }

    [Fact]
    public void IsEmptyDock_RegisteredSpineSingleChild_False()
    {
        // The exact scenario reproduced live (June 2026): nested MainDock gone single-child must NOT collapse.
        var f = new CstDockFactory();
        var child = new DocumentDock { Id = "MainDocumentDock", VisibleDockables = List() };
        var mainDock = new ProportionalDock { Id = "MainDock", VisibleDockables = List(child) };
        f._spineDocks.Add(mainDock);
        Assert.False(f.IsEmptyDock(mainDock));
    }

    [Fact]
    public void IsEmptyDock_EmptyRegisteredMainDocumentDock_False()
    {
        var f = new CstDockFactory();
        var mdd = new DocumentDock { Id = "MainDocumentDock", VisibleDockables = List() };
        f._spineDocks.Add(mdd);
        Assert.False(f.IsEmptyDock(mdd)); // protected even with zero documents
    }

    [Fact]
    public void IsEmptyDock_ClonedEmptyMainDocumentDock_NotProtected_True()
    {
        // A cloned empty document dock (same id, different instance, from a document-area split) is
        // NOT protected and IS empty → cleanup may remove it. This is what stops clones accumulating.
        var f = new CstDockFactory();
        var original = new DocumentDock { Id = "MainDocumentDock", VisibleDockables = List() };
        f._spineDocks.Add(original);
        var clone = new DocumentDock { Id = "MainDocumentDock", VisibleDockables = List() };
        Assert.True(f.IsEmptyDock(clone));
    }

    [Fact]
    public void FindEmptySplits_ProtectedParentWithRedundantChild_MarksChildNotParent()
    {
        // Heal scenario reproduced live: after closing books, MainDock's only child is a redundant
        // single-child wrapper. Cleanup must collapse the WRAPPER (promoting MainDocumentDock up),
        // NOT try to remove the protected MainDock (which would just loop, refusing).
        var f = new CstDockFactory();
        var mdd = new DocumentDock { Id = "MainDocumentDock", VisibleDockables = List() };
        var wrapper = new ProportionalDock { Id = "PDock_wrapper", VisibleDockables = List(mdd) };
        var mainDock = new ProportionalDock { Id = "MainDock", VisibleDockables = List(wrapper) };
        f._spineDocks.Add(mainDock);
        f._spineDocks.Add(mdd);

        var result = new List<IDock>();
        f.FindEmptySplits(mainDock, result);

        Assert.Contains(wrapper, result);        // redundant wrapper marked for collapse
        Assert.DoesNotContain(mainDock, result); // protected spine NOT marked
    }

    [Fact]
    public void FindEmptySplits_RedundantWrapperDirectlyUnderRoot_IsCollapsed()
    {
        // Blind spot reproduced live: a single-child wrapper sitting directly under WindowLayout (a
        // RootDock) was never flattened, because the child-scan only ran for ProportionalDock parents.
        // Now its own redundancy is judged regardless of parent — so it collapses, promoting MainDock up.
        var f = new CstDockFactory();
        var mainDoc = new DocumentDock { Id = "MainDocumentDock", VisibleDockables = List() };
        var mainDock = new ProportionalDock { Id = "MainDock", VisibleDockables = List(mainDoc) };
        var wrapper = new ProportionalDock { Id = "PDock_wrap", VisibleDockables = List(mainDock) };
        var windowLayout = new RootDock { Id = "WindowLayout", VisibleDockables = List(wrapper) };
        f._spineDocks.Add(mainDock);
        f._spineDocks.Add(mainDoc);

        var result = new List<IDock>();
        f.FindEmptySplits(windowLayout, result);

        Assert.Contains(wrapper, result);        // redundant wrapper under the RootDock now collapses
        Assert.DoesNotContain(mainDock, result); // protected spine still safe
    }

    // ---- Q2: recreate-on-demand tool container (failure mode #4) ----

    [Fact]
    public void EnsureLeftToolDock_RecreatesUnderMainDock_WhenMissing()
    {
        var f = new CstDockFactory();
        var doc = new DocumentDock { Id = "MainDocumentDock", VisibleDockables = List() };
        var mainDock = new ProportionalDock { Id = "MainDock", VisibleDockables = List(doc) };
        var windowLayout = new RootDock { Id = "WindowLayout", VisibleDockables = List(mainDock) };
        var root = new RootDock { Id = "Root", VisibleDockables = List(windowLayout) };
        f._rootDock = root;
        f._mainDock = mainDock;

        var dock = f.EnsureLeftToolDock();

        Assert.NotNull(dock);
        Assert.Equal("LeftToolDock", dock!.Id);
        Assert.Contains(mainDock.VisibleDockables!, d => d.Id == "LeftTools"); // wrapper inserted under MainDock
    }

    [Fact]
    public void EnsureLeftToolDock_ReusesExisting_WhenPresent()
    {
        var f = new CstDockFactory();
        var existing = new ToolDock { Id = "LeftToolDock", VisibleDockables = List() };
        var leftTools = new ProportionalDock { Id = "LeftTools", VisibleDockables = List(existing) };
        var doc = new DocumentDock { Id = "MainDocumentDock", VisibleDockables = List() };
        var mainDock = new ProportionalDock { Id = "MainDock", VisibleDockables = List(leftTools, doc) };
        var windowLayout = new RootDock { Id = "WindowLayout", VisibleDockables = List(mainDock) };
        var root = new RootDock { Id = "Root", VisibleDockables = List(windowLayout) };
        f._rootDock = root;
        f._mainDock = mainDock;
        var before = mainDock.VisibleDockables!.Count;

        var dock = f.EnsureLeftToolDock();

        Assert.Same(existing, dock);                              // reused, not recreated
        Assert.Equal(before, mainDock.VisibleDockables!.Count);  // nothing inserted
    }
}
