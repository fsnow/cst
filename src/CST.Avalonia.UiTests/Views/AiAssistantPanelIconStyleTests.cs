using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using CST.Avalonia.UiTests.TestSupport;
using CST.Avalonia.ViewModels;
using CST.Avalonia.Views;
using Xunit;

namespace CST.Avalonia.UiTests.Views;

/// <summary>
/// The Assistant panel's icon buttons dim when disabled. (#850)
///
/// <para><b>The failure this exists for.</b> Fluent's PathIcon ControlTheme sets Foreground, which outranks the
/// dimmed foreground a disabled Button passes down by inheritance. The + (new conversation) button shipped for
/// review looking identical enabled and disabled, in both themes — and [fsnow] chose "Disabled when empty"
/// precisely so that a + which does nothing would not look live.</para>
/// </summary>
public class AiAssistantPanelIconStyleTests
{
    private const string DisabledForeground = "ButtonForegroundDisabled";

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void The_new_conversation_icon_dims_when_the_button_is_disabled(string themeVariant)
    {
        // No turns, so CanAsk && HasTurns is false: the + is disabled by its own binding.
        var (window, button) = Show(themeVariant);
        try
        {
            Assert.False(button.IsEnabled, "Precondition: an empty conversation leaves + disabled.");
            var icon = button.GetLogicalChildren().OfType<PathIcon>().Single();

            StyleProbe.AssertStyled(icon, PathIcon.ForegroundProperty,
                StyleProbe.Brush(icon, DisabledForeground),
                "The disabled + icon keeps its ordinary foreground. 'Button:disabled > PathIcon' in " +
                "AiAssistantPanel.axaml matched nothing, or a local Foreground on the icon is beating it.");
        }
        finally
        {
            window.Close();
        }
    }

    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void The_new_conversation_icon_is_not_dimmed_when_enabled(string themeVariant)
    {
        var (window, button) = Show(themeVariant);
        try
        {
            button.IsEnabled = true;
            StyleProbe.Pump(window);
            var icon = button.GetLogicalChildren().OfType<PathIcon>().Single();

            StyleProbe.AssertNotStyled(icon, PathIcon.ForegroundProperty,
                StyleProbe.Brush(icon, DisabledForeground),
                "The enabled + icon is dimmed: the rule is no longer confined to ':disabled'.");
        }
        finally
        {
            window.Close();
        }
    }

    private static (Window Window, Button Button) Show(string themeVariant)
    {
        var variant = themeVariant switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => throw new ArgumentOutOfRangeException(nameof(themeVariant), themeVariant, null)
        };

        var panel = new AiAssistantPanel { DataContext = new AiAssistantViewModel(null, null, null, null) };
        var window = StyleProbe.Show(panel, variant);
        var button = panel.FindControl<Button>("NewConversationButton")
                     ?? throw new InvalidOperationException("AiAssistantPanel has no Button named NewConversationButton.");
        return (window, button);
    }
}
