using System;
using System.Linq;
using Avalonia.Automation;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.VisualTree;
using CST.Avalonia.UiTests.TestSupport;
using CST.Avalonia.ViewModels;
using CST.Avalonia.Views;
using Xunit;

namespace CST.Avalonia.UiTests.Views;

/// <summary>
/// The Assistant panel's top-row buttons keep their tooltips while disabled. Avalonia hides a disabled control's
/// tooltip by default, and these are icon-only and disabled on a new or short conversation, which is exactly when
/// a reader hovers to learn what they are.
/// </summary>
public class AiAssistantPanelTooltipTests
{
    [AvaloniaTheory]
    [InlineData("assistant.sessions", "Conversations")]
    [InlineData("assistant.compact", "Summarise all but the last 4 turns")]
    [InlineData("assistant.new-conversation", "New conversation")]
    public void A_disabled_top_row_button_still_shows_its_tooltip(string automationId, string tip)
    {
        var panel = new AiAssistantPanel { DataContext = new AiAssistantViewModel(null, null, null, null) };
        var window = StyleProbe.Show(panel, ThemeVariant.Light);
        try
        {
            var button = panel.GetVisualDescendants().OfType<Button>()
                .Single(b => AutomationProperties.GetAutomationId(b) == automationId);

            Assert.False(button.IsEnabled, "Precondition: an empty panel leaves this button disabled.");
            Assert.True(ToolTip.GetShowOnDisabled(button),
                "Avalonia hides a disabled control's tooltip unless ToolTip.ShowOnDisabled is set.");
            Assert.Equal(tip, ToolTip.GetTip(button));
        }
        finally
        {
            window.Close();
        }
    }
}
