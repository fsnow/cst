using CST.Avalonia.Services;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// Feed A of the interaction history (#621): the dock model's own activation signal.
///
/// <para>
/// One subscription in <c>CstDockFactory</c>'s constructor is supposed to cover every dock in every window,
/// including docks a split creates later and docks in floating windows. That claim rests entirely on a
/// framework behaviour — Dock's <c>ActiveDockable</c> SETTER calling <c>InitActiveDockable</c>, which raises
/// <c>ActiveDockableChanged</c> on the owning factory. If a Dock upgrade ever moves that call, the feed goes
/// quiet with nothing to indicate it, and targeting silently reverts to the first-dock guess this fixes.
/// These tests are the tripwire.
/// </para>
/// </summary>
public class ActiveDocumentFeedTests
{
    private sealed class TestDocument : Document
    {
    }

    private static (CstDockFactory Factory, ActiveDocumentTracker Tracker, DocumentDock Dock, TestDocument A, TestDocument B)
        Build()
    {
        var factory = new CstDockFactory();
        var tracker = new ActiveDocumentTracker();
        factory.DocumentTracker = tracker;

        var a = new TestDocument { Id = "a", Title = "A" };
        var b = new TestDocument { Id = "b", Title = "B" };
        var dock = new DocumentDock
        {
            Id = "MainDocumentDock",
            Factory = factory,
            VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable> { a, b }
        };

        return (factory, tracker, dock, a, b);
    }

    [Fact]
    public void Activating_a_tab_records_it()
    {
        var (_, tracker, dock, a, _) = Build();

        dock.ActiveDockable = a;

        Assert.Same(a, Assert.Single(tracker.Recent));
    }

    [Fact]
    public void Successive_activations_build_the_history_in_order()
    {
        var (_, tracker, dock, a, b) = Build();

        dock.ActiveDockable = a;
        dock.ActiveDockable = b;

        Assert.Equal(new IDockable[] { b, a }, tracker.Recent);
    }

    [Fact]
    public void A_dock_created_after_the_factory_is_covered_too()
    {
        // The whole reason the subscription is on the FACTORY rather than per-dock: splits and floating
        // windows create docks long after the constructor ran, and each would otherwise need wiring that
        // someone has to remember.
        var (factory, tracker, _, _, _) = Build();

        var later = new TestDocument { Id = "later", Title = "Later" };
        var newDock = new DocumentDock
        {
            Id = "MainDocumentDock",
            Factory = factory,
            VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable> { later }
        };

        newDock.ActiveDockable = later;

        Assert.Same(later, Assert.Single(tracker.Recent));
    }

    [Fact]
    public void The_activation_event_carries_the_dockable_that_became_active()
    {
        // Pins the shape of the event args the feed reads, not just that something fired — reading the
        // wrong member would record null and fail silently, which is the same as the feed not existing.
        var factory = new CstDockFactory();
        IDockable? seen = null;
        factory.ActiveDockableChanged += (_, e) => seen = e.Dockable;

        var doc = new TestDocument { Id = "x", Title = "X" };
        var dock = new DocumentDock
        {
            Id = "MainDocumentDock",
            Factory = factory,
            VisibleDockables = new System.Collections.ObjectModel.ObservableCollection<IDockable> { doc }
        };
        dock.ActiveDockable = doc;

        Assert.Same(doc, seen);
    }
}
