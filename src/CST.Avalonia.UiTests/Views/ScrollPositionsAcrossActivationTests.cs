using System;
using System.Reflection;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Platform;
using Avalonia.Threading;
using CST.Avalonia.Views;
using Xunit;

namespace CST.Avalonia.UiTests.Views;

/// <summary>
/// A window keeps its scroll positions across an app switch. (#972)
///
/// <para><b>Driven through the real activation path.</b> A second headless window's <c>Activate</c> does not
/// deactivate the first, but the window's platform callbacks can be invoked directly - <c>LostFocus</c> and
/// <c>Deactivated</c>, then <c>Activated</c> - and that runs Avalonia's own <c>WindowBase.HandleActivated</c>:
/// <c>Activated</c>, then the focus restore with its synchronous bring-into-view (the jump), then
/// <c>IsActive</c>. These callbacks are platform API, not app API; if a future Avalonia renames them this file is
/// what breaks, and the comment on <see cref="ScrollPositionsAcrossActivation"/> is what to re-check.</para>
/// </summary>
public class ScrollPositionsAcrossActivationTests
{
    [AvaloniaFact]
    public void Without_the_keeper_coming_back_jumps_to_the_focused_control()
    {
        var (window, viewer, _) = Show(attach: false);
        try
        {
            viewer.Offset = new Vector(0, 1000);
            Pump();

            SwitchAwayAndBack(window);

            Assert.Equal(0, viewer.Offset.Y);   // the #972 jump, reproduced through the real path
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void With_the_keeper_coming_back_keeps_the_position()
    {
        var (window, viewer, _) = Show(attach: true);
        try
        {
            viewer.Offset = new Vector(0, 1000);
            Pump();

            SwitchAwayAndBack(window);

            // Synchronously: the restore happens inside activation, so no frame shows the wrong offset.
            Assert.Equal(1000, viewer.Offset.Y);
        }
        finally
        {
            window.Close();
        }
    }

    // A trackpad scroll over a background window reaches it. Coming back must keep that scroll, not the one from
    // before the reader left (review of #1022: the first version restored the position saved on deactivation).
    [AvaloniaFact]
    public void A_scroll_made_while_inactive_is_kept()
    {
        var (window, viewer, _) = Show(attach: true);
        try
        {
            viewer.Offset = new Vector(0, 1000);
            Pump();

            Callback(window, "LostFocus");
            Callback(window, "Deactivated");
            Pump();

            viewer.Offset = new Vector(0, 2000);
            Pump();

            Callback(window, "Activated");

            Assert.Equal(2000, viewer.Offset.Y);
        }
        finally
        {
            window.Close();
        }
    }

    private static void SwitchAwayAndBack(Window window)
    {
        Callback(window, "LostFocus");
        Callback(window, "Deactivated");
        Pump();
        Callback(window, "Activated");
    }

    // The platform callbacks are not visible outside Avalonia, so they are read by reflection off the platform
    // interfaces: LostFocus is ITopLevelImpl's, Activated and Deactivated are IWindowBaseImpl's.
    private static void Callback(Window window, string name)
    {
        var impl = window.PlatformImpl ?? throw new InvalidOperationException("The window has no platform impl.");
        var property = typeof(IWindowBaseImpl).GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                       ?? typeof(ITopLevelImpl).GetProperty(name, BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
                       ?? throw new InvalidOperationException($"No platform callback named {name}: re-check against this Avalonia version.");
        var action = property.GetValue(impl) as Action
                     ?? throw new InvalidOperationException($"{name} is not wired: Avalonia did not register the callback.");
        action();
    }

    private static (Window Window, ScrollViewer Viewer, TextBox Box) Show(bool attach)
    {
        var box = new TextBox();
        var stack = new StackPanel();
        stack.Children.Add(box);
        stack.Children.Add(new Border { Height = 3000 });
        var viewer = new ScrollViewer { Content = stack };
        var window = new Window { Width = 400, Height = 300, Content = viewer };
        if (attach) ScrollPositionsAcrossActivation.Attach(window);
        window.Show();
        Pump();
        box.Focus();
        Pump();
        return (window, viewer, box);
    }

    private static void Pump()
    {
        for (var i = 0; i < 3; i++) Dispatcher.UIThread.RunJobs();
    }
}
