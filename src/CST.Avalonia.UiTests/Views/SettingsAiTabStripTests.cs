using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.ViewModels;
using CST.Avalonia.Views;
using Xunit;

namespace CST.Avalonia.UiTests.Views;

/// <summary>
/// Settings → AI: the General / Providers / Models strip stays in view while a tab's body scrolls. (#986)
///
/// <para>[fsnow]: "you can't scroll down and do a category switch — because the General/Provider/Model headings
/// scroll out of view." The whole right-hand pane used to be one scroll viewer with the tab control inside it, so
/// the strip scrolled away with the content. Now the window's viewer does not scroll the AI category
/// (<see cref="SettingsViewModel.ContentScrollBarVisibility"/>), which bounds the tab control's height, and each
/// tab scrolls its own body.</para>
///
/// <para>The full <see cref="SettingsViewModel"/> needs app services, so the window's viewer is set here the way
/// that binding sets it, and the AI content is placed directly.</para>
/// </summary>
public class SettingsAiTabStripTests
{
    [AvaloniaFact]
    public void Scrolling_a_tab_leaves_the_tab_strip_where_it_was()
    {
        var (window, outer, tabs, body) = ShowAi(ScrollBarVisibility.Disabled);
        try
        {
            Assert.True(body.Extent.Height > body.Viewport.Height,
                "Precondition: the General tab is taller than the window, so there is something to scroll.");
            Assert.True(outer.Extent.Height <= outer.Viewport.Height + 0.5,
                "The window's own viewer still has the AI category to scroll, so the strip will scroll with it.");

            var strip = tabs.GetVisualDescendants().OfType<TabItem>().First();
            var before = strip.TranslatePoint(new Point(0, 0), window)!.Value.Y;

            body.Offset = new Vector(0, 200);
            Pump(window);

            Assert.True(body.Offset.Y > 0, "The tab's own viewer did not scroll.");
            Assert.Equal(before, strip.TranslatePoint(new Point(0, 0), window)!.Value.Y);
        }
        finally
        {
            window.Close();
        }
    }

    // The contrast: with the window's viewer scrolling the category, as it did before, the whole tab control is
    // in its extent - the arrangement the strip scrolled away in.
    [AvaloniaFact]
    public void With_the_window_viewer_scrolling_the_tab_control_scrolls_inside_it()
    {
        var (window, outer, _, _) = ShowAi(ScrollBarVisibility.Auto);
        try
        {
            Assert.True(outer.Extent.Height > outer.Viewport.Height);
        }
        finally
        {
            window.Close();
        }
    }

    private static (Window Window, ScrollViewer Outer, TabControl Tabs, ScrollViewer Body) ShowAi(ScrollBarVisibility outerMode)
    {
        var settings = new FakeSettings();
        settings.Settings.Ai.Enabled = true;
        settings.Settings.Ai.Chat.Enabled = true;

        var window = new SettingsWindow { Width = 900, Height = 420 };
        window.Show();
        Pump(window);

        var outer = window.FindControl<ScrollViewer>("SettingsScroll")!;
        outer.VerticalScrollBarVisibility = outerMode;
        var host = outer.GetVisualDescendants().OfType<ContentControl>().First(c => c.DataTemplates.Count > 3);
        host.Content = new AiSettingsViewModel(settings);
        Pump(window);

        var tabs = host.GetVisualDescendants().OfType<TabControl>().First();
        var body = tabs.GetVisualDescendants().OfType<ScrollViewer>().First();
        return (window, outer, tabs, body);
    }

    private static void Pump(Window window)
    {
        Dispatcher.UIThread.RunJobs();
        window.UpdateLayout();
        Dispatcher.UIThread.RunJobs();
    }

    private sealed class FakeSettings : ISettingsService
    {
        public Settings Settings { get; } = new();
        public Task LoadSettingsAsync() => Task.CompletedTask;
        public Task SaveSettingsAsync() => Task.CompletedTask;
        public void RequestSave() { }
        public Task FlushPendingSaveAsync() => Task.CompletedTask;
        public void UpdateSetting<T>(string propertyName, T value) { }
        public string GetSettingsFilePath() => "";
    }
}
