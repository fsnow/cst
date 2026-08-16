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

    // ---- The assistant's own container (#656) ----

    [Fact]
    public void EnsureRightToolDock_RecreatesUnderMainDock_WhenMissing()
    {
        // The assistant can be floated into its own window and that window closed, which takes the panel out
        // of the layout entirely. It is the only tool whose dock is on the right, so it needs its own
        // recreate path — and since the panel holds the whole session's transcript, having no way back lost
        // that too.
        var f = new CstDockFactory();
        var doc = new DocumentDock { Id = "MainDocumentDock", VisibleDockables = List() };
        var mainDock = new ProportionalDock { Id = "MainDock", VisibleDockables = List(doc) };
        var windowLayout = new RootDock { Id = "WindowLayout", VisibleDockables = List(mainDock) };
        var root = new RootDock { Id = "Root", VisibleDockables = List(windowLayout) };
        f._rootDock = root;
        f._mainDock = mainDock;

        var dock = f.EnsureRightToolDock();

        Assert.NotNull(dock);
        Assert.Equal("RightToolDock", dock!.Id);
        Assert.Contains(mainDock.VisibleDockables!, d => d.Id == "RightTools");
    }

    [Fact]
    public void EnsureRightToolDock_AppendsAfterTheDocuments()
    {
        // Right of the books, not left of them: it is inserted by position, and getting the ends confused
        // would put the assistant where the book tree lives.
        var f = new CstDockFactory();
        var doc = new DocumentDock { Id = "MainDocumentDock", VisibleDockables = List() };
        var mainDock = new ProportionalDock { Id = "MainDock", VisibleDockables = List(doc) };
        var windowLayout = new RootDock { Id = "WindowLayout", VisibleDockables = List(mainDock) };
        var root = new RootDock { Id = "Root", VisibleDockables = List(windowLayout) };
        f._rootDock = root;
        f._mainDock = mainDock;

        f.EnsureRightToolDock();

        var ids = mainDock.VisibleDockables!.Select(d => d.Id).ToList();
        Assert.True(ids.IndexOf("RightTools") > ids.IndexOf("MainDocumentDock"));
    }

    [Fact]
    public void EnsureRightToolDock_ReusesExisting_WhenPresent()
    {
        var f = new CstDockFactory();
        var existing = new ToolDock { Id = "RightToolDock", VisibleDockables = List() };
        var rightTools = new ProportionalDock { Id = "RightTools", VisibleDockables = List(existing) };
        var doc = new DocumentDock { Id = "MainDocumentDock", VisibleDockables = List() };
        var mainDock = new ProportionalDock { Id = "MainDock", VisibleDockables = List(doc, rightTools) };
        var windowLayout = new RootDock { Id = "WindowLayout", VisibleDockables = List(mainDock) };
        var root = new RootDock { Id = "Root", VisibleDockables = List(windowLayout) };
        f._rootDock = root;
        f._mainDock = mainDock;
        var before = mainDock.VisibleDockables!.Count;

        var dock = f.EnsureRightToolDock();

        Assert.Same(existing, dock);
        Assert.Equal(before, mainDock.VisibleDockables!.Count);
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
    public void A_recreated_assistant_column_gets_a_real_share_of_the_window()
    {
        // Reported: reopening it from the View menu brought it back about a quarter of an inch wide -- worse
        // than not coming back, since a reader who did not know to drag it would think the menu had failed.
        //
        // Closing empties the ToolDock, the cleanup pass collapses the wrapper and its splitter, and what is
        // left no longer sums to one -- so the framework rebalances around it. Re-inserting a dock that says
        // 0.18 into a row that has already been rebalanced gives it 18% of nothing in particular. The whole
        // row has to be restated, which is what this asserts.
        var f = new CstDockFactory();
        // The drifted state a hide leaves behind: two columns sharing the whole window between them.
        var doc = new DocumentDock { Id = "MainDocumentDock", Proportion = 0.75, VisibleDockables = List() };
        var leftTools = new ProportionalDock { Id = "LeftTools", Proportion = 0.25, VisibleDockables = List() };
        var mainDock = new ProportionalDock { Id = "MainDock", VisibleDockables = List(leftTools, doc) };
        var windowLayout = new RootDock { Id = "WindowLayout", VisibleDockables = List(mainDock) };
        var root = new RootDock { Id = "Root", VisibleDockables = List(windowLayout) };
        f._rootDock = root;
        f._mainDock = mainDock;

        f.EnsureRightToolDock();

        var assistant = mainDock.VisibleDockables!.First(d => d.Id == "RightTools");
        Assert.True(assistant.Proportion > 0.1, $"came back at {assistant.Proportion}");

        // And the row adds up, so nothing is left for the framework to guess about.
        var total = leftTools.Proportion + doc.Proportion + assistant.Proportion;
        Assert.Equal(1.0, total, 3);
    }

    [Fact]
    public void Hiding_the_assistant_hands_its_width_to_the_documents()
    {
        var f = new CstDockFactory();
        var doc = new DocumentDock { Id = "MainDocumentDock", Proportion = 0.57, VisibleDockables = List() };
        var leftTools = new ProportionalDock { Id = "LeftTools", Proportion = 0.25, VisibleDockables = List() };
        var mainDock = new ProportionalDock { Id = "MainDock", VisibleDockables = List(leftTools, doc) };
        var windowLayout = new RootDock { Id = "WindowLayout", VisibleDockables = List(mainDock) };
        var root = new RootDock { Id = "Root", VisibleDockables = List(windowLayout) };
        f._rootDock = root;
        f._mainDock = mainDock;

        // The assistant is already gone, as it is by the time the hide path rebalances.
        f.RebalanceMainDock();

        Assert.Equal(0.75, doc.Proportion, 3);
        Assert.Equal(1.0, leftTools.Proportion + doc.Proportion, 3);
    }

    [Fact]
    public void The_assistant_opens_narrower_than_the_documents()
    {
        // Reported: "the default width of the Assistant is too wide — often wider than the book area". Split
        // the documents into two books side by side and each gets half the middle column, so a quarter-width
        // assistant is exactly as wide as either book. The middle column has to stay wide enough that a split
        // book is still the widest thing on screen.
        var f = new CstDockFactory();
        f.EnsureRightToolDock();   // no MainDock: just proves the constant is not the old 0.25

        var doc = new DocumentDock { Id = "MainDocumentDock", VisibleDockables = List() };
        var mainDock = new ProportionalDock { Id = "MainDock", VisibleDockables = List(doc) };
        var windowLayout = new RootDock { Id = "WindowLayout", VisibleDockables = List(mainDock) };
        var root = new RootDock { Id = "Root", VisibleDockables = List(windowLayout) };
        f._rootDock = root;
        f._mainDock = mainDock;

        f.EnsureRightToolDock();
        var assistant = mainDock.VisibleDockables!.First(d => d.Id == "RightTools");

        Assert.True(assistant.Proportion < 0.25, $"assistant opens at {assistant.Proportion}");
        // Half the middle column, which is what a side-by-side book gets, must still beat it.
        var middle = 1.0 - 0.25 - assistant.Proportion;
        Assert.True(middle / 2 > assistant.Proportion, "a split book would be narrower than the assistant");
    }
}
