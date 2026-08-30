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




    [Fact]
    public void The_assistant_is_off_when_neither_switch_is_on()
    {
        // Reported: "the panel shouldn't show unless that is checked". A panel for a feature the reader has
        // not enabled is an advertisement, and this one is worse than most -- its buttons decline every
        // request with an explanation, which reads as four broken buttons.
        //
        // No ServiceProvider in a unit-test host, so this asserts the safe direction: unknown means off. The
        // two-switch logic itself is exercised through the settings view model.
        Assert.False(CstDockFactory.AssistantEnabled());
    }


    [Fact]
    public void The_documents_get_every_share_the_tool_rail_does_not_take()
    {
        // One tool column, so the row is two members and the documents are whatever is left. Before #906 a
        // second column sat on the right and this sum had three terms; the assertion that matters is the
        // same either way — the row adds to one, with nothing left for the framework to guess about.

        var f = new CstDockFactory();
        var doc = new DocumentDock { Id = "MainDocumentDock", Proportion = 0.57, VisibleDockables = List() };
        var leftTools = new ProportionalDock { Id = "LeftTools", Proportion = 0.25, VisibleDockables = List() };
        var mainDock = new ProportionalDock { Id = "MainDock", VisibleDockables = List(leftTools, doc) };
        var windowLayout = new RootDock { Id = "WindowLayout", VisibleDockables = List(mainDock) };
        var root = new RootDock { Id = "Root", VisibleDockables = List(windowLayout) };
        f._rootDock = root;
        f._mainDock = mainDock;

        f.RebalanceMainDock();

        Assert.Equal(0.75, doc.Proportion, 3);
        Assert.Equal(1.0, leftTools.Proportion + doc.Proportion, 3);
    }


    /// <summary>
    /// A tool panel is recognised as one. (R9-1, #886)
    ///
    /// <para><b>This is the defect.</b> Both guards keeping tools out of the document tab area asked
    /// <c>is Tool</c> — Dock's Mvvm class — and no panel in this app derives from it. Every one is a
    /// <c>ReactiveTool</c>: a <c>ReactiveDockableBase</c> implementing <c>ITool</c>. So the guards were dead
    /// for the exact case they were written for, and centre-dropping Search or the Dictionary onto the
    /// document tabs docked it there as a tab.</para>
    ///
    /// <para>Against the old test this fails, which is the only reason it is worth having.</para>
    /// </summary>
    [Fact]
    public void A_ReactiveTool_is_recognised_as_a_tool()
    {
        Assert.True(CstDockFactory.IsToolDockable(new CST.Avalonia.ViewModels.Dock.ReactiveTool()));
    }

    /// <summary>
    /// A document is not, so books are untouched by the guards.
    ///
    /// <para>The half that must not regress: widening the test to catch tools must never start catching the
    /// documents the document dock exists to hold. <c>ReactiveDocument</c> implements <c>IDocument</c> only —
    /// whereas a <c>ReactiveTool</c> implements both, which is why this asks whether a thing IS a tool rather
    /// than whether it is not a document.</para>
    /// </summary>
    [Fact]
    public void A_ReactiveDocument_is_not_a_tool()
    {
        Assert.False(CstDockFactory.IsToolDockable(new CST.Avalonia.ViewModels.Dock.ReactiveDocument()));
    }

    /// <summary>A dock full of tools counts too — that half of the guard always worked, and must keep
    /// working.</summary>
    [Fact]
    public void A_tool_dock_is_recognised_as_a_tool_dockable()
    {
        Assert.True(CstDockFactory.IsToolDockable(new CstDockFactory().CreateToolDock()));
    }

    /// <summary>Null is not a tool, rather than an exception on the drag path.</summary>
    [Fact]
    public void Null_is_not_a_tool()
    {
        Assert.False(CstDockFactory.IsToolDockable(null));
    }

}
