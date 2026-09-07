using CST.Avalonia.ViewModels;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// The dock flags that decide whether a PDF tab can leave its window. (#419)
///
/// <para>This is all a headless test can reach. The float itself needs a real <c>HostWindow</c> and the
/// <c>ControlRecycling</c> resource, so <c>SplitToWindow</c>, <c>PrepareCrossWindowMove</c> and
/// <c>DisposeAndEvictRecycledView</c> cannot be exercised here (#655) — the manual list on #419 is what
/// covers them. What this pins is the pair of flags, and the pair matters more than either alone.</para>
///
/// <para><b>Why <c>CanDrag</c> is asserted too.</b> It was never set, so it has always defaulted true, and
/// Dock's <c>ValidateDocument</c> gates a drop into an existing floating window on <c>CanDrag</c>/<c>CanDrop</c>
/// without consulting <c>CanFloat</c>. So a PDF's browser could already cross windows before #419, and the
/// funnel already had to handle it. Setting <c>CanDrag = false</c> later — copying <c>WelcomeViewModel</c>,
/// say — would silently disable dragging while leaving <c>CanFloat</c> true, which reads as "floating is
/// enabled" while the tab cannot be picked up.</para>
/// </summary>
public class PdfFloatFlagsTests
{
    private static PdfDisplayViewModel Vm() =>
        new("s0101m.mul.xml", CST.Sources.SourceType.Burmese1957, 1);

    [Fact]
    public void A_pdf_tab_can_float_into_its_own_window()
    {
        Assert.True(Vm().CanFloat);
    }

    [Fact]
    public void A_pdf_tab_can_be_dragged()
    {
        Assert.True(Vm().CanDrag);
    }

    [Fact]
    public void A_pdf_tab_can_still_be_closed()
    {
        // CloseDockable's PDF branch is what evicts the browser; a non-closable PDF would slip into the
        // "left in the closing window" path instead.
        Assert.True(Vm().CanClose);
    }
}
