using System.Collections.ObjectModel;
using System.Linq;
using CST.Avalonia.Services;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// Unit coverage for CstDockFactory's floating-window title tracking (#284/#322/#357). Exercises the
/// testable core (WireHostWindowTitle / ComputeFloatingWindowTitle) via the IHostWindowTitleTarget seam,
/// so no real Avalonia Window is needed. See #425.
/// </summary>
public class CstDockFactoryTitleTrackingTests
{
    private static ObservableCollection<IDockable> List(params IDockable[] items) => new(items);

    private sealed class FakeTitleTarget : IHostWindowTitleTarget
    {
        public IDock? Layout { get; set; }
        public string Title { get; private set; } = "";
        public void SetTitle(string? title) => Title = title ?? "";
    }

    // RootDock -> DocumentDock (active) holding the given leaf documents; the first is the active tab.
    private static (RootDock root, DocumentDock docDock) BuildLayout(params Document[] docs)
    {
        var docDock = new DocumentDock { VisibleDockables = List(docs.Cast<IDockable>().ToArray()), ActiveDockable = docs.FirstOrDefault() };
        var root = new RootDock { VisibleDockables = List(docDock), ActiveDockable = docDock };
        return (root, docDock);
    }

    // ---- ComputeFloatingWindowTitle: active-leaf title, "+N" count, empty fallback ----

    [Fact]
    public void Compute_SingleLeaf_UsesItsTitle()
    {
        var (root, _) = BuildLayout(new Document { Title = "Dīgha 1" });
        Assert.Equal("Dīgha 1", new CstDockFactory().ComputeFloatingWindowTitle(root));
    }

    [Fact]
    public void Compute_MultipleLeaves_UsesActiveTitlePlusCount()
    {
        var a = new Document { Title = "A" };
        var b = new Document { Title = "B" };
        var (root, docDock) = BuildLayout(a, b);
        docDock.ActiveDockable = b;
        Assert.Equal("B  +1", new CstDockFactory().ComputeFloatingWindowTitle(root));
    }

    [Fact]
    public void Compute_NoLeaves_ReturnsDefault()
    {
        var root = new RootDock { VisibleDockables = List(new DocumentDock { VisibleDockables = List() }) };
        Assert.Equal("CST Reader", new CstDockFactory().ComputeFloatingWindowTitle(root));
    }

    // ---- #357 regression: a leaf dragged into an existing float gets its own Title subscription ----

    [Fact]
    public void WiredHost_ReflectsActiveLeafTitle_Initially()
    {
        var (root, _) = BuildLayout(new Document { Title = "A" });
        var factory = new CstDockFactory();
        var host = new FakeTitleTarget { Layout = root };

        factory.WireHostWindowTitle(host);

        Assert.Equal("A", host.Title);
    }

    // Repro of #357: float [A] -> drag B in -> close A (float now holds only B) -> retitle B (as a global
    // script switch would). Before #424 the dragged-in leaf B had no Title subscription, so the retitle was
    // dropped and the window title stayed "B". After the fix, re-wiring on the tab change subscribes B.
    [Fact]
    public void DraggedInLeaf_RetitledAfterOriginalTabClosed_UpdatesHostTitle()
    {
        var a = new Document { Title = "A" };
        var b = new Document { Title = "B" };
        var (root, docDock) = BuildLayout(a);
        var factory = new CstDockFactory();
        var host = new FakeTitleTarget { Layout = root };

        factory.WireHostWindowTitle(host);
        Assert.Equal("A", host.Title);

        docDock.VisibleDockables!.Add(b);       // drag B in (CollectionChanged -> re-wire subscribes B)
        docDock.ActiveDockable = b;             // B becomes the active tab
        docDock.VisibleDockables!.Remove(a);    // close A's tab; float now holds only B
        Assert.Equal("B", host.Title);

        b.Title = "B (new script)";             // the later in-place retitle #357 dropped
        Assert.Equal("B (new script)", host.Title);
    }

    // ---- Re-wiring on tab changes must replace, not accumulate, the subscription set ----

    [Fact]
    public void RepeatedTabChanges_DoNotAccumulateSubscriptions()
    {
        var a = new Document { Title = "A" };
        var (root, docDock) = BuildLayout(a);
        var factory = new CstDockFactory();
        var host = new FakeTitleTarget { Layout = root };

        factory.WireHostWindowTitle(host);
        var baseline = factory._titleSubscriptions[host].Count;

        for (int i = 0; i < 5; i++)
        {
            var tmp = new Document { Title = "tmp" };
            docDock.VisibleDockables!.Add(tmp);     // add -> re-wire
            docDock.VisibleDockables!.Remove(tmp);  // remove -> re-wire
        }

        // Same final layout => same subscription count. If re-wire appended instead of replacing, this grows.
        Assert.Equal(baseline, factory._titleSubscriptions[host].Count);
    }
}
