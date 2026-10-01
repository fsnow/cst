using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
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
/// scroll out of view." AI is shown in <c>TabbedHost</c>, which does not scroll; each tab scrolls its own body.
/// Every other category stays in <c>SettingsScroll</c>.</para>
///
/// <para><b>Switched the way the view model switches.</b> Selecting a category changes both hosts' content and
/// visibility before one layout pass. The first version of this fix toggled the scroll viewer's own scrolling
/// instead, and in that pass Avalonia left the content arranged at the previous category's size: the bottom of
/// the General tab could not be reached (review of #1019). A test that set things up in a different order
/// passed against that broken layout, so these compare each host's arranged size with its desired size, and
/// check that the last control on the page can actually be scrolled into view.</para>
///
/// <para>The full <see cref="SettingsViewModel"/> needs app services, so the two hosts are set here as its
/// <see cref="SettingsViewModel.ScrolledContent"/> / <see cref="SettingsViewModel.TabbedContent"/> /
/// <see cref="SettingsViewModel.HasTabbedContent"/> set them.</para>
/// </summary>
public class SettingsAiTabStripTests
{
    [AvaloniaFact]
    public void Switching_to_AI_lays_it_out_at_its_own_size_and_the_strip_stays_while_General_scrolls()
    {
        var w = Open();
        try
        {
            ShowScrolled(w, TallCategory());
            ShowAi(w);

            AssertArrangedAtDesiredSize(w.Tabbed);

            var tabs = w.Tabbed.GetVisualDescendants().OfType<TabControl>().Single();
            Assert.True(tabs.Bounds.Height > 0, "The tab control was laid out with no height.");

            // The General tab's own viewer, by structure: the tab's content IS the viewer. Taking the first viewer
            // found would pick up an unrelated one (a text box's) if this one were removed.
            var body = Assert.IsType<ScrollViewer>(tabs.Items.OfType<TabItem>().First().Content);
            Assert.True(body.Extent.Height > body.Viewport.Height,
                "Precondition: the General tab is taller than the window, so there is something to scroll.");

            var strip = tabs.GetVisualDescendants().OfType<TabItem>().First();
            var stripY = strip.TranslatePoint(new Point(0, 0), w.Window)!.Value.Y;

            body.Offset = new Vector(0, body.Extent.Height);
            Pump(w.Window);

            Assert.Equal(stripY, strip.TranslatePoint(new Point(0, 0), w.Window)!.Value.Y);

            // The last control on the page must be inside the visible part of the tab's viewer at its end.
            var last = body.GetVisualDescendants().OfType<CheckBox>().Last(c => c.IsEffectivelyVisible);
            var lastBottom = last.TranslatePoint(new Point(0, last.Bounds.Height), w.Window)!.Value.Y;
            var bodyBottom = body.TranslatePoint(new Point(0, body.Bounds.Height), w.Window)!.Value.Y;
            Assert.True(lastBottom <= bodyBottom + 0.5,
                $"The last control ends at y={lastBottom} but the tab's visible area ends at y={bodyBottom}: " +
                "the bottom of the page cannot be reached.");
        }
        finally
        {
            w.Window.Close();
        }
    }

    [AvaloniaFact]
    public void Switching_back_from_AI_lays_the_other_category_out_at_its_own_size()
    {
        var w = Open();
        try
        {
            ShowAi(w);
            ShowScrolled(w, TallCategory());

            AssertArrangedAtDesiredSize(w.Scrolled);
            Assert.True(w.Scroll.Extent.Height > w.Scroll.Viewport.Height,
                "The other category no longer scrolls in the window's viewer.");
        }
        finally
        {
            w.Window.Close();
        }
    }

    private sealed record Hosts(Window Window, ScrollViewer Scroll, ContentControl Scrolled, ContentControl Tabbed);

    private static Hosts Open()
    {
        var window = new SettingsWindow { Width = 900, Height = 600 };
        window.Show();
        Pump(window);
        return new Hosts(window,
            window.FindControl<ScrollViewer>("SettingsScroll")!,
            window.FindControl<ContentControl>("ScrolledHost")!,
            window.FindControl<ContentControl>("TabbedHost")!);
    }

    // As SettingsViewModel does on selecting a category: both hosts change before one layout pass.
    private static void ShowAi(Hosts w)
    {
        var settings = new FakeSettings();
        settings.Settings.Ai.Enabled = true;
        settings.Settings.Ai.Chat.Enabled = true;

        w.Scrolled.Content = null;
        w.Scroll.IsVisible = false;
        w.Tabbed.Content = new AiSettingsViewModel(settings);
        w.Tabbed.IsVisible = true;
        Pump(w.Window);
    }

    private static void ShowScrolled(Hosts w, Control category)
    {
        w.Tabbed.Content = null;
        w.Tabbed.IsVisible = false;
        w.Scrolled.Content = category;
        w.Scroll.IsVisible = true;
        Pump(w.Window);
    }

    // Wider and taller than the window, like Pali Script Fonts: the case that was clipped after leaving AI.
    private static Control TallCategory() => new Border { Width = 2000, Height = 1500 };

    private static void AssertArrangedAtDesiredSize(Control host)
    {
        Assert.True(Math.Abs(host.Bounds.Width - host.DesiredSize.Width) < 0.5
                    && Math.Abs(host.Bounds.Height - host.DesiredSize.Height) < 0.5,
            $"{host.Name} is arranged at {host.Bounds.Size} but wants {host.DesiredSize}: it kept an earlier " +
            "category's size, and part of the page is clipped.");
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
