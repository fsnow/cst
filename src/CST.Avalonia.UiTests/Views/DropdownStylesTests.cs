using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Styling;
using CST.Avalonia.UiTests.TestSupport;
using Xunit;

namespace CST.Avalonia.UiTests.Views;

/// <summary>
/// The click that closes an open dropdown also reaches the control under it. (#1067)
///
/// <para><b>[fsnow]</b>: "some double clicks on the tree did not work". With a chapter list open, the first
/// click of a double-click on the book tree was spent closing the list. Asked how widely to change that:
/// "all dropdowns". The rules are in <c>Styles/DropdownStyles.axaml</c>.</para>
/// </summary>
public class DropdownStylesTests
{
    [AvaloniaFact]
    public void A_click_that_closes_a_combo_box_reaches_the_button_under_it()
    {
        var combo = new ComboBox
        {
            ItemsSource = new[] { "one", "two", "three" },
            Width = 150,
            HorizontalAlignment = HorizontalAlignment.Left,
            VerticalAlignment = VerticalAlignment.Top
        };
        var clicks = 0;
        var button = new Button
        {
            Content = "under",
            Width = 120,
            Height = 40,
            HorizontalAlignment = HorizontalAlignment.Right,
            VerticalAlignment = VerticalAlignment.Bottom
        };
        button.Click += (_, _) => clicks++;
        var panel = new Grid { Children = { combo, button } };

        var window = StyleProbe.Show(panel, ThemeVariant.Light);
        try
        {
            combo.IsDropDownOpen = true;
            StyleProbe.Pump(window);
            Assert.True(combo.IsDropDownOpen, "Precondition: the dropdown is open.");

            var point = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
            // Headless hit-testing reads the last COMPOSED frame, and Pump only runs jobs and layout. Without a
            // frame the click can hit nothing - which passed alone and failed once other tests ran first
            // (review of #1068).
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
            window.MouseDown(point, MouseButton.Left);
            window.MouseUp(point, MouseButton.Left);
            StyleProbe.Pump(window);

            Assert.False(combo.IsDropDownOpen, "The click outside did not close the dropdown.");
            Assert.Equal(1, clicks);
        }
        finally
        {
            window.Close();
        }
    }

    // Each rule reaches its popup: a selector that matches nothing compiles and does nothing (#655).
    [AvaloniaFact]
    public void Combo_box_and_auto_complete_popups_pass_the_dismissing_click_through()
    {
        var combo = new ComboBox { ItemsSource = new[] { "one" } };
        var auto = new AutoCompleteBox { ItemsSource = new[] { "one" } };
        var window = StyleProbe.Show(new StackPanel { Children = { combo, auto } }, ThemeVariant.Light);
        try
        {
            Assert.True(StyleProbe.TemplatePart<Popup>(combo, "PART_Popup").OverlayDismissEventPassThrough,
                "ComboBox's popup still consumes the click that closes it.");
            Assert.True(StyleProbe.TemplatePart<Popup>(auto, "PART_Popup").OverlayDismissEventPassThrough,
                "AutoCompleteBox's popup still consumes the click that closes it.");
        }
        finally
        {
            window.Close();
        }
    }
}
