using System;
using System.Globalization;
using System.Threading;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.ViewModels;
using CST.Conversion;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// The toolbar zoom control (#618), driven against a real <see cref="BookZoomService"/> and no browser.
///
/// <para>
/// <c>BookZoomServiceTests</c> already covers the ladder and the clamping, so nothing here re-tests those.
/// What is new in this feature is everything AROUND the number: which book an event concerns, what a typed
/// string means, and whether the readout follows a book that changes script — the last of which is the
/// difference between a correct readout and one that lies with authority.
/// </para>
/// </summary>
public class BookZoomReadoutTests
{
    private static (BookZoomService svc, Settings settings) CreateService()
    {
        var settings = new Settings();
        var mock = new Mock<ISettingsService>();
        mock.SetupGet(s => s.Settings).Returns(settings);
        return (new BookZoomService(mock.Object), settings);
    }

    /// <summary>A readout over a mutable script, with a count of how many times it asked for a refresh.</summary>
    private sealed class Harness : IDisposable
    {
        public readonly BookZoomService Service;
        public Script Script;
        public int Notifications;
        public readonly BookZoomReadout Readout;

        public Harness(Script script = Script.Devanagari)
        {
            (Service, _) = CreateService();
            Script = script;
            Readout = new BookZoomReadout(Service, () => Script, () => Notifications++);
        }

        public void Dispose() => Readout.Dispose();
    }

    // ---- What the control shows ------------------------------------------------------------------

    [Fact]
    public void An_unzoomed_book_reads_one_hundred_percent()
    {
        using var h = new Harness();
        Assert.Equal("100%", h.Readout.Display);
    }

    [Fact]
    public void A_zoomed_book_reads_its_percentage()
    {
        using var h = new Harness();
        h.Service.SetZoom(Script.Devanagari, 1.25);
        Assert.Equal("125%", h.Readout.Display);
    }

    [Fact]
    public void An_off_rung_value_is_displayed_as_typed_not_snapped()
    {
        using var h = new Harness();
        h.Service.SetZoom(Script.Devanagari, 1.15);
        Assert.Equal("115%", h.Readout.Display);
    }

    // ---- Which book an event concerns ------------------------------------------------------------

    [Fact]
    public void A_change_to_this_books_script_refreshes_the_readout()
    {
        using var h = new Harness();
        h.Service.SetZoom(Script.Devanagari, 1.25);
        Assert.Equal(1, h.Notifications);
    }

    [Fact]
    public void A_change_to_another_script_does_not()
    {
        using var h = new Harness(Script.Devanagari);
        h.Service.SetZoom(Script.Latin, 1.25);

        // Every open book hears every change — a Latin book's zoom must not repaint a Devanagari toolbar.
        Assert.Equal(0, h.Notifications);
        Assert.Equal("100%", h.Readout.Display);
    }

    [Fact]
    public void A_change_made_in_another_tab_of_the_same_script_reaches_this_one()
    {
        // Two readouts on one service is exactly two book tabs showing the same script. The second is what
        // a change made in the first has to reach, or the two tabs disagree about what they are showing.
        using var first = new Harness(Script.Myanmar);
        var secondNotifications = 0;
        using var second = new BookZoomReadout(first.Service, () => Script.Myanmar, () => secondNotifications++);

        first.Service.SetZoom(Script.Myanmar, 1.5);

        Assert.Equal(1, secondNotifications);
        Assert.Equal("150%", second.Display);
    }

    [Fact]
    public void Disposing_leaves_no_subscription_behind()
    {
        // The service is a singleton, so a subscription left behind roots a closed book's VM for the
        // session — and keeps refreshing a toolbar nobody is looking at.
        var h = new Harness();
        h.Readout.Dispose();

        h.Service.SetZoom(Script.Devanagari, 1.25);
        Assert.Equal(0, h.Notifications);
    }

    // ---- Following a book that changes script ----------------------------------------------------

    [Fact]
    public void Changing_the_books_script_re_reads_the_new_scripts_zoom()
    {
        using var h = new Harness(Script.Devanagari);
        h.Service.SetZoom(Script.Devanagari, 1.25);
        h.Service.SetZoom(Script.Myanmar, 1.75);

        h.Script = Script.Myanmar;
        h.Notifications = 0;
        h.Readout.ScriptChanged();

        // Nothing stored changed here — but which stored value this control is showing did.
        Assert.Equal("175%", h.Readout.Display);

        // And it must SAY so. Display re-reads the script on every access, so the assertion above passes
        // even if ScriptChanged does nothing at all — while the shipped box, which repaints only when the
        // VM raises property-changed, would sit on the old script's percentage until the next zoom event.
        // (fable review)
        Assert.Equal(1, h.Notifications);
    }

    [Fact]
    public void After_a_script_change_events_for_the_old_script_are_ignored()
    {
        using var h = new Harness(Script.Devanagari);
        h.Script = Script.Latin;
        h.Notifications = 0;

        h.Service.SetZoom(Script.Devanagari, 1.25);

        // The script is read on every event rather than captured at construction, which is what stops a
        // re-scripted book from tracking the script it used to show.
        Assert.Equal(0, h.Notifications);
    }

    // ---- The step buttons ------------------------------------------------------------------------

    [Fact]
    public void Step_buttons_are_live_in_the_middle_of_the_ladder()
    {
        using var h = new Harness();
        Assert.True(h.Readout.CanZoomIn);
        Assert.True(h.Readout.CanZoomOut);
    }

    [Fact]
    public void Each_step_button_goes_dead_at_its_own_end()
    {
        using var h = new Harness();

        h.Service.SetZoom(Script.Devanagari, BookZoomService.MaxZoom);
        Assert.False(h.Readout.CanZoomIn);
        Assert.True(h.Readout.CanZoomOut);

        h.Service.SetZoom(Script.Devanagari, BookZoomService.MinZoom);
        Assert.True(h.Readout.CanZoomIn);
        Assert.False(h.Readout.CanZoomOut);
    }

    // ---- What a typed string means ---------------------------------------------------------------

    [Theory]
    [InlineData("115", 1.15)]
    [InlineData("115%", 1.15)]
    [InlineData("  115 % ", 1.15)]
    [InlineData("100", 1.0)]      // the reset, typed — which is why there is no reset button
    [InlineData("50", 0.5)]
    [InlineData("300", 3.0)]
    public void The_things_people_actually_type_are_accepted(string text, double expected)
    {
        Assert.True(BookZoomReadout.TryParseEntry(text, out var zoom));
        Assert.Equal(expected, zoom, 6);
    }

    [Fact]
    public void A_fractional_percentage_is_rounded_to_what_the_box_can_display()
    {
        // NOT the ladder snapping #618 rules out — 115 stays 115, nowhere near a rung. This is so the
        // stored value and the displayed one cannot disagree, since the box shows whole percent.
        Assert.True(BookZoomReadout.TryParseEntry("115.4", out var down));
        Assert.Equal(1.15, down, 6);

        Assert.True(BookZoomReadout.TryParseEntry("115.6", out var up));
        Assert.Equal(1.16, up, 6);
    }

    [Fact]
    public void A_decimal_typed_in_the_users_own_culture_is_understood()
    {
        var original = Thread.CurrentThread.CurrentCulture;
        try
        {
            Thread.CurrentThread.CurrentCulture = new CultureInfo("de-DE");
            Assert.True(BookZoomReadout.TryParseEntry("115,6", out var zoom));
            Assert.Equal(1.16, zoom, 6);
        }
        finally
        {
            Thread.CurrentThread.CurrentCulture = original;
        }
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("abc")]
    [InlineData("%")]
    [InlineData("0")]
    [InlineData("-50")]
    public void Nonsense_is_rejected_rather_than_guessed_at(string? text)
    {
        // A rejected entry leaves the stored zoom alone; the caller puts the box back. Guessing here would
        // render the book at a size nobody asked for.
        Assert.False(BookZoomReadout.TryParseEntry(text, out _));
    }

    [Fact]
    public void An_out_of_range_entry_is_left_for_the_service_to_clamp()
    {
        // Parsing accepts it; clamping 500 to 300% answers the intent better than refusing to do anything.
        Assert.True(BookZoomReadout.TryParseEntry("500", out var zoom));
        Assert.Equal(5.0, zoom, 6);

        using var h = new Harness();
        Assert.Equal(BookZoomService.MaxZoom, h.Service.SetZoom(Script.Devanagari, zoom));
        Assert.Equal("300%", h.Readout.Display);
    }

    [Fact]
    public void A_typed_value_and_the_keyboard_coexist()
    {
        // The whole reason free entry needs no ladder awareness: from 115%, a step lands on the next
        // DEFINED rung in the direction of travel.
        using var h = new Harness();
        Assert.True(BookZoomReadout.TryParseEntry("115", out var typed));
        h.Service.SetZoom(Script.Devanagari, typed);

        Assert.Equal(1.25, h.Service.ZoomIn(Script.Devanagari), 6);
        Assert.Equal(1.10, h.Service.ZoomOut(Script.Devanagari), 6);
    }
}
