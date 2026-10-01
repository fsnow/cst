using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.VisualTree;

namespace CST.Avalonia.Views;

/// <summary>
/// Keeps a window's scroll positions when the reader switches to another application and back. (#972)
///
/// <para>[observed] Deactivation clears the window's focus; re-activation restores it to the last-focused control,
/// and <c>ScrollViewer.BringIntoViewOnFocusChange</c> (true by default) scrolls that control into view. Scrolling
/// with the wheel never moves focus, so a reader who clicked the Providers search box and then wheeled down the
/// list came back to find the list scrolled up to the box. [fsnow] reproduced it that way.</para>
///
/// <para><b>Bracketing the jump, and only the jump.</b> Avalonia 11.3.6's <c>WindowBase.HandleActivated</c> raises
/// <c>Activated</c>, THEN restores focus (whose bring-into-view is synchronous), THEN sets <c>IsActive</c>. So the
/// offsets are noted in <c>Activated</c> and put back when <c>IsActive</c> turns true: nothing else happens in
/// between. A scroll made while the window was inactive (a trackpad over a background window) is kept, and so is
/// the scroll from the click that re-activates it, which arrives after. An earlier version noted the offsets on
/// <c>Deactivated</c> and restored them in a posted job, and undid both (review of #1022).</para>
///
/// <para><b>Restored, not disabled.</b> Turning BringIntoViewOnFocusChange off would also stop Tab from scrolling
/// to a control below the visible area.</para>
/// </summary>
public sealed class ScrollPositionsAcrossActivation
{
    private readonly List<(ScrollViewer Viewer, Vector Offset)> _saved = new();

    public static void Attach(Window window)
    {
        var keeper = new ScrollPositionsAcrossActivation();
        window.Activated += (_, _) => keeper.Save(window);
        window.PropertyChanged += (_, e) =>
        {
            if (e.Property == WindowBase.IsActiveProperty && e.GetNewValue<bool>()) keeper.Restore();
        };
    }

    public void Save(Visual root)
    {
        _saved.Clear();
        foreach (var viewer in root.GetVisualDescendants().OfType<ScrollViewer>())
            if (viewer.IsEffectivelyVisible)
                _saved.Add((viewer, viewer.Offset));
    }

    public void Restore()
    {
        foreach (var (viewer, offset) in _saved)
            if (viewer.IsEffectivelyVisible && viewer.Offset != offset)
                viewer.Offset = offset;
        _saved.Clear();
    }
}
