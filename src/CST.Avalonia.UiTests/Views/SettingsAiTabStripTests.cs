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
            ShowScrolled(w, ShortCategory());
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

    /// <summary>
    /// The tab's viewer spans the pane, as SettingsScroll does, so its scroll bar is at the window's edge and the
    /// wheel works anywhere in the pane - not only over the cards (review of #1019). Wide, so the page's natural
    /// width is well short of the pane and a host sized to its content would show.
    /// </summary>
    [AvaloniaFact]
    public void A_tabs_viewer_spans_the_pane_in_a_wide_window()
    {
        var w = Open(width: 1600);
        try
        {
            ShowAi(w);
            var tabs = w.Tabbed.GetVisualDescendants().OfType<TabControl>().Single();
            var body = Assert.IsType<ScrollViewer>(tabs.Items.OfType<TabItem>().First().Content);
            var cards = Assert.IsType<StackPanel>(body.Content);

            double Right(Control c) => c.TranslatePoint(new Point(c.Bounds.Width, 0), w.Window)!.Value.X;
            Assert.True(Right(body) - Right(cards) > 100,
                "Precondition: at this width the cards are well short of the pane.");
            Assert.True(Right(w.Tabbed) - Right(tabs) <= w.Tabbed.Padding.Right + 0.5,
                $"The tab control ends at x={Right(tabs)}, short of the pane's edge at x={Right(w.Tabbed)}.");
            Assert.True(Right(tabs) - Right(body) <= tabs.Padding.Right + 0.5,
                $"The tab's viewer ends at x={Right(body)}, short of the tab control's edge at x={Right(tabs)}.");
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
            ShowScrolled(w, ShortCategory());

            AssertArrangedAtDesiredSize(w.Scrolled);
        }
        finally
        {
            w.Window.Close();
        }
    }

    private sealed record Hosts(Window Window, ScrollViewer Scroll, ContentControl Scrolled, ContentControl Tabbed);

    private static Hosts Open(double width = 900)
    {
        var window = new SettingsWindow { Width = width, Height = 600 };
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

    // SHORTER than the window, like Pali Script Fonts (680x519 at 900x700). The stale layout the first version of
    // this fix produced appears only when the previous page fits in the viewport: the viewer then hands the next
    // page the same rect and Avalonia skips its arrange. A page taller than the window changes the rect and
    // forces a fresh arrange, so it could never show that bug (measured by the review of #1019).
    private static Control ShortCategory() => new Border { Width = 400, Height = 300 };

    // Arranged at the size it asked for - or, along a stretched axis, at the size of the panel it fills. Anything
    // else is a size kept from an earlier category, which clips part of the page.
    private static void AssertArrangedAtDesiredSize(Control host)
    {
        var pane = ((Control)host.GetVisualParent()!).Bounds.Size;
        var width = host.HorizontalAlignment == global::Avalonia.Layout.HorizontalAlignment.Stretch
            ? pane.Width : host.DesiredSize.Width;
        var height = host.VerticalAlignment == global::Avalonia.Layout.VerticalAlignment.Stretch
            ? pane.Height : host.DesiredSize.Height;
        Assert.True(Math.Abs(host.Bounds.Width - width) < 0.5 && Math.Abs(host.Bounds.Height - height) < 0.5,
            $"{host.Name} is arranged at {host.Bounds.Size} but should be {width} x {height}: it kept an earlier " +
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
