using CST.Avalonia.Views;
using Xunit;

namespace CST.Avalonia.Tests.Views;

// #572: the post-zoom position restore waits for the CACHE_BUILT that the in-page anchor-cache rebuild
// emits. Its correctness rests on ONE ordering property, which is otherwise invisible — it lives in the gap
// between a C# constant and a delay interpolated into an injected JavaScript string, with nothing in
// between to enforce it.
//
// The property: the in-page rebuild debounce must outlast the C# burst settle. The rebuild timer starts at
// the renderer's last resize, which necessarily postdates the last SetZoomLevel arriving — so if the
// debounce is the longer of the two, CACHE_BUILT can never arrive before the restore is waiting for it.
//
// An earlier revision had these the other way round (150 vs 250) and compensated with a build counter to
// detect an already-arrived signal. That was unsound: a build could START before the zoom and have its
// title reach C# after the counter was captured, satisfying the check while reflecting the OLD layout.
// Ordering the delays removes the hole instead of testing for it — but only while this holds.
public class ZoomReflowTimingTests
{
    [Fact]
    public void AnchorRebuildDebounce_OutlastsTheZoomSettle()
    {
        Assert.True(
            BookDisplayView.AnchorRebuildDebounceMs > BookDisplayView.ResizeSettleMs,
            $"AnchorRebuildDebounceMs ({BookDisplayView.AnchorRebuildDebounceMs}ms) must be greater than " +
            $"ResizeSettleMs ({BookDisplayView.ResizeSettleMs}ms). The post-zoom restore waits for the " +
            "CACHE_BUILT emitted by the in-page rebuild; if the rebuild can finish first, that signal " +
            "arrives with nothing waiting for it and the restore falls through to the backstop — restoring " +
            "against a layout that may still be reflowing, which is the reading-position drift of #571/#572.");
    }

    [Fact]
    public void TheOrderingHasUsefulMargin()
    {
        // Not just greater — greater by enough that ordinary scheduling jitter cannot invert it. A margin
        // this small would be a coincidence rather than a design.
        Assert.True(
            BookDisplayView.AnchorRebuildDebounceMs - BookDisplayView.ResizeSettleMs >= 25,
            "The gap between the rebuild debounce and the zoom settle is too small to survive timer jitter.");
    }
}
