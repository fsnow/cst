using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

namespace CST.Avalonia.Views;

/// <summary>
/// Keeps a window's scroll positions when the reader switches to another application and back. (#972)
///
/// <para>[observed] Re-activating a window restores focus to its last-focused control, and
/// <c>ScrollViewer.BringIntoViewOnFocusChange</c> (true by default) scrolls that control into view. Scrolling with
/// the wheel never moves focus, so a reader who clicked the Providers search box and then wheeled down the list
/// came back to find the list scrolled up to the box. [fsnow] reproduced it that way.</para>
///
/// <para><b>Restored, not disabled.</b> Turning BringIntoViewOnFocusChange off would also stop Tab from scrolling
/// to a control below the visible area. Instead the offsets are noted when the window loses activation and put
/// back after it regains it - posted at Background priority, so they land after the focus restore and its scroll.
/// Only a viewer whose offset actually moved is touched.</para>
/// </summary>
public sealed class ScrollPositionsAcrossActivation
{
    private readonly List<(ScrollViewer Viewer, Vector Offset)> _saved = new();

    public static void Attach(Window window)
    {
        var keeper = new ScrollPositionsAcrossActivation();
        window.Deactivated += (_, _) => keeper.Save(window);
        window.Activated += (_, _) => Dispatcher.UIThread.Post(keeper.Restore, DispatcherPriority.Background);
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
