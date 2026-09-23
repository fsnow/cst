using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Media;
using Avalonia.VisualTree;
using Avalonia.Headless.XUnit;
using Avalonia.LogicalTree;
using Avalonia.Styling;
using CST.Avalonia.UiTests.TestSupport;
using CST.Avalonia.ViewModels;
using CST.Avalonia.Views;
using Xunit;

namespace CST.Avalonia.UiTests.Views;

/// <summary>
/// The Assistant panel's own button styles: icon buttons dim when disabled (#850), and link buttons are quiet
/// text (#997).
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

    // The Compact icon (#998) rides on the same rule; with nothing to summarise it is disabled.
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void The_compact_icon_dims_when_there_is_nothing_to_summarise(string themeVariant)
    {
        var (window, plus) = Show(themeVariant);
        try
        {
            var compact = ((Control)plus.Parent!).GetLogicalChildren().OfType<Button>()
                .Single(b => AutomationProperties.GetAutomationId(b) == "assistant.compact");
            Assert.False(compact.IsEnabled, "Precondition: an empty conversation has nothing to compact.");
            var icon = compact.GetLogicalChildren().OfType<PathIcon>().Single();

            StyleProbe.AssertStyled(icon, PathIcon.ForegroundProperty,
                StyleProbe.Brush(icon, DisabledForeground),
                "The disabled Compact icon keeps its ordinary foreground.");
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

    /// <summary>
    /// The session rows' Save / Cancel / Delete / Keep are <c>Button.Link</c>. That class was styled only
    /// inside AiProvidersView and AiModelsView, each scoped to itself, so here it matched nothing and the four
    /// rendered as filled Fluent buttons (review of #1009). The rows live in a flyout template, so this probes
    /// the rule with a Link button placed in the panel itself: same UserControl.Styles, same selector.
    /// </summary>
    [AvaloniaTheory]
    [InlineData("Light")]
    [InlineData("Dark")]
    public void The_panel_styles_link_buttons(string themeVariant)
    {
        var (window, plus) = Show(themeVariant);
        try
        {
            var link = new Button { Content = "Keep", Classes = { "Link" } };
            ((DockPanel)plus.FindAncestorOfType<Border>()!.Parent!).Children.Insert(0, link);
            DockPanel.SetDock(link, global::Avalonia.Controls.Dock.Top);
            StyleProbe.Pump(window);

            StyleProbe.AssertStyled(link, TemplatedControl.BorderThicknessProperty, new Thickness(0),
                "A Button.Link in the Assistant panel keeps Fluent's border: the panel has no Button.Link rule.");
            // Background rather than Foreground: the Link foreground is an App.axaml resource, which this
            // harness does not load (see HeadlessStyleTestApp).
            StyleProbe.AssertStyled(link, TemplatedControl.BackgroundProperty, Brushes.Transparent,
                "A Button.Link in the Assistant panel keeps Fluent's fill: the panel has no Button.Link rule.");
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
