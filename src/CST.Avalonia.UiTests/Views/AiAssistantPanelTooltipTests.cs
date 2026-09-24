using System;
using System.Linq;
using Avalonia;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CST.Avalonia.UiTests.TestSupport;
using CST.Avalonia.ViewModels;
using CST.Avalonia.Views;
using Xunit;

namespace CST.Avalonia.UiTests.Views;

/// <summary>
/// The Assistant panel's top-row buttons show their tooltips on hover while disabled. Avalonia hides a disabled
/// control's tooltip by default, and all three are disabled on a new or short conversation, which is exactly when a
/// reader hovers to learn what they are. Hovered for real: a disabled control gets no pointer events of its own,
/// so a test that only read the attached property would not show the tooltip opens.
/// </summary>
public class AiAssistantPanelTooltipTests
{
    [AvaloniaTheory]
    [InlineData("assistant.sessions", "Conversations")]
    [InlineData("assistant.compact", "Summarise all but the last 4 turns")]
    [InlineData("assistant.new-conversation", "New conversation")]
    public void Hovering_a_disabled_top_row_button_opens_its_tooltip(string automationId, string tip)
    {
        var (window, button) = Show(automationId);
        try
        {
            Assert.False(button.IsEnabled, "Precondition: an empty panel leaves this button disabled.");
            Assert.Equal(tip, ToolTip.GetTip(button));

            Assert.True(Hover(window, button), "The tooltip did not open over the disabled button.");
        }
        finally
        {
            window.Close();
        }
    }

    // The contrast: without ShowOnDisabled the same hover opens nothing, so the case above is the attribute's doing.
    [AvaloniaFact]
    public void Without_ShowOnDisabled_the_same_hover_opens_nothing()
    {
        var (window, button) = Show("assistant.compact");
        try
        {
            ToolTip.SetShowOnDisabled(button, false);
            Assert.False(Hover(window, button));
        }
        finally
        {
            window.Close();
        }
    }

    private static (Window Window, Button Button) Show(string automationId)
    {
        var panel = new AiAssistantPanel { DataContext = new AiAssistantViewModel(null, null, null, null) };
        var window = StyleProbe.Show(panel, ThemeVariant.Light);
        var button = panel.GetVisualDescendants().OfType<Button>()
            .Single(b => AutomationProperties.GetAutomationId(b) == automationId);
        return (window, button);
    }

    private static bool Hover(Window window, Button button)
    {
        ToolTip.SetShowDelay(button, 0);
        StyleProbe.Pump(window);
        var centre = button.TranslatePoint(new Point(button.Bounds.Width / 2, button.Bounds.Height / 2), window)!.Value;
        window.MouseMove(new Point(1, window.Bounds.Height - 1));
        StyleProbe.Pump(window);
        window.MouseMove(centre);
        for (var i = 0; i < 5; i++)
        {
            Dispatcher.UIThread.RunJobs();
            AvaloniaHeadlessPlatform.ForceRenderTimerTick();
        }
        return ToolTip.GetIsOpen(button);
    }
}
