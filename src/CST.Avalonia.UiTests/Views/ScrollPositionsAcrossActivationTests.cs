using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using CST.Avalonia.Views;
using Xunit;

namespace CST.Avalonia.UiTests.Views;

/// <summary>
/// A window keeps its scroll positions across an app switch. (#972)
///
/// <para>The headless platform does not model window activation (a second window's Activate leaves the first
/// still active), so the switch itself cannot be driven here. What is tested is the keeper: the jump is reproduced
/// with <c>BringIntoView</c> on the focused control - what focus restoration does on re-activation - and the
/// keeper puts the offset back.</para>
/// </summary>
public class ScrollPositionsAcrossActivationTests
{
    [AvaloniaFact]
    public void Bringing_the_focused_control_back_into_view_is_undone()
    {
        var (window, viewer, box) = Show();
        try
        {
            viewer.Offset = new Vector(0, 1000);
            Pump();

            var keeper = new ScrollPositionsAcrossActivation();
            keeper.Save(window);

            box.BringIntoView();   // what re-activation's focus restore does
            Pump();
            Assert.Equal(0, viewer.Offset.Y);   // the #972 jump, reproduced

            keeper.Restore();
            Assert.Equal(1000, viewer.Offset.Y);
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaFact]
    public void A_viewer_that_did_not_move_is_left_alone()
    {
        var (window, viewer, _) = Show();
        try
        {
            viewer.Offset = new Vector(0, 1000);
            Pump();

            var keeper = new ScrollPositionsAcrossActivation();
            keeper.Save(window);
            var changes = 0;
            viewer.PropertyChanged += (_, e) => { if (e.Property == ScrollViewer.OffsetProperty) changes++; };

            keeper.Restore();
            Assert.Equal(0, changes);
        }
        finally
        {
            window.Close();
        }
    }

    private static (Window Window, ScrollViewer Viewer, TextBox Box) Show()
    {
        var box = new TextBox();
        var stack = new StackPanel();
        stack.Children.Add(box);
        stack.Children.Add(new Border { Height = 3000 });
        var viewer = new ScrollViewer { Content = stack };
        var window = new Window { Width = 400, Height = 300, Content = viewer };
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
