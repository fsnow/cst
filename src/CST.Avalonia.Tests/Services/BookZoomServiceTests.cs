using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Conversion;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Services;

// #572: book-text zoom is the ONLY stored size control for book content — #574 flattened the stylesheets to
// one shared ladder and #42 was narrowed to font faces, so nothing else sizes the text. That makes the
// arithmetic here load-bearing: a wrong CEF level silently renders every book at the wrong size, and a
// clamping hole can render it at zero.
public class BookZoomServiceTests
{
    private static (BookZoomService svc, Mock<ISettingsService> settings) Create()
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Settings).Returns(new Settings());
        return (new BookZoomService(settings.Object), settings);
    }

    // ---- Defaults and round-tripping -------------------------------------------------------------

    [Fact]
    public void DefaultZoom_IsExactlyOne_ForEveryScript()
    {
        var (svc, _) = Create();
        // 1.0 must mean "the shipped stylesheet ladder, untouched". Anything else and a fresh install
        // renders at a size nobody chose.
        foreach (var script in System.Enum.GetValues<Script>())
            Assert.Equal(1.0, svc.GetZoom(script));
    }

    [Fact]
    public void Zoom_SurvivesAJsonRoundTrip()
    {
        // The issue flagged that ApplicationState's serializer uses WhenWritingDefault, which would drop a
        // value. Zoom lives in Settings instead, whose serializer does not — this pins that difference,
        // because a silently dropped zoom would reset the user's calibration on every restart.
        var settings = new Settings();
        settings.FontSettings.ScriptFonts["Devanagari"].BookZoom = 1.25;

        var json = JsonSerializer.Serialize(settings);
        var back = JsonSerializer.Deserialize<Settings>(json)!;

        Assert.Equal(1.25, back.FontSettings.ScriptFonts["Devanagari"].BookZoom);
        Assert.Contains("BookZoom", json);
    }

    [Fact]
    public void MissingBookZoomInJson_DeserializesToOne()
    {
        // Every settings.json written before this feature lacks the property. Those users must land on
        // 100%, not 0% (a blank page) — which is what a bare `default(double)` would give.
        var json = """{"FontSettings":{"ScriptFonts":{"Latin":{"FontFamily":"","FontSize":12}}}}""";
        var back = JsonSerializer.Deserialize<Settings>(json)!;
        Assert.Equal(1.0, back.FontSettings.ScriptFonts["Latin"].BookZoom);
    }

    // ---- Stepping ---------------------------------------------------------------------------------

    [Fact]
    public void ZoomIn_ThenZoomOut_ReturnsToTheStartingRung()
    {
        var (svc, _) = Create();
        var start = svc.GetZoom(Script.Latin);
        svc.ZoomIn(Script.Latin);
        Assert.NotEqual(start, svc.GetZoom(Script.Latin));
        svc.ZoomOut(Script.Latin);
        Assert.Equal(start, svc.GetZoom(Script.Latin));
    }

    [Fact]
    public void Stepping_ClampsAtBothEnds_WithoutWrapping()
    {
        var (svc, _) = Create();
        for (int i = 0; i < 50; i++) svc.ZoomIn(Script.Latin);
        Assert.Equal(BookZoomService.MaxZoom, svc.GetZoom(Script.Latin));

        for (int i = 0; i < 50; i++) svc.ZoomOut(Script.Latin);
        Assert.Equal(BookZoomService.MinZoom, svc.GetZoom(Script.Latin));
    }

    [Fact]
    public void EveryRung_IsReachableByStepping_AndTheLadderIsStrictlyAscending()
    {
        // A duplicated or out-of-order rung would make one press a no-op, which reads as a dead key.
        var ladder = BookZoomService.ZoomLadder;
        Assert.Equal(ladder.OrderBy(x => x).ToList(), ladder.ToList());
        Assert.Equal(ladder.Distinct().Count(), ladder.Count);

        var (svc, _) = Create();
        for (int i = 0; i < 50; i++) svc.ZoomOut(Script.Latin);

        var visited = new List<double> { svc.GetZoom(Script.Latin) };
        for (int i = 0; i < ladder.Count; i++)
        {
            var next = svc.ZoomIn(Script.Latin);
            if (next != visited[^1]) visited.Add(next);
        }
        Assert.Equal(ladder.ToList(), visited);
    }

    [Fact]
    public void AnOffLadderStoredValue_StepsInTheRequestedDirection()
    {
        // A hand-edited settings file, or a value written by a build with a different ladder. Snapping to
        // the NEAREST rung could move the text the opposite way from the key that was pressed.
        var (svc, settings) = Create();
        settings.Object.Settings.FontSettings.ScriptFonts["Latin"].BookZoom = 1.13;

        Assert.True(svc.ZoomIn(Script.Latin) > 1.13);
        settings.Object.Settings.FontSettings.ScriptFonts["Latin"].BookZoom = 1.13;
        Assert.True(svc.ZoomOut(Script.Latin) < 1.13);
    }

    [Fact]
    public void ResetZoom_ReturnsToOne_FromEitherDirection()
    {
        var (svc, _) = Create();
        svc.ZoomIn(Script.Latin); svc.ZoomIn(Script.Latin);
        Assert.Equal(1.0, svc.ResetZoom(Script.Latin));

        svc.ZoomOut(Script.Latin); svc.ZoomOut(Script.Latin);
        Assert.Equal(1.0, svc.ResetZoom(Script.Latin));
    }

    // ---- Per-script isolation ---------------------------------------------------------------------

    [Fact]
    public void ZoomIsPerScript_AndDoesNotLeakBetweenThem()
    {
        // The whole point of the per-script decision: zoom calibrates for the FACE a script resolves to, so
        // a Devanagari adjustment must not follow the reader into a Latin book.
        var (svc, _) = Create();
        svc.ZoomIn(Script.Devanagari);
        svc.ZoomIn(Script.Devanagari);

        Assert.True(svc.GetZoom(Script.Devanagari) > 1.0);
        Assert.Equal(1.0, svc.GetZoom(Script.Latin));
    }

    [Fact]
    public void ChangingZoom_RaisesZoomChanged_ForThatScriptOnly()
    {
        var (svc, _) = Create();
        var events = new List<BookZoomChangedEventArgs>();
        svc.ZoomChanged += (_, e) => events.Add(e);

        svc.ZoomIn(Script.Thai);

        var evt = Assert.Single(events);
        Assert.Equal(Script.Thai, evt.Script);
        Assert.Equal(svc.GetZoom(Script.Thai), evt.Zoom);
    }

    [Fact]
    public void SteppingAtTheCeiling_RaisesNothingAndSavesNothing()
    {
        // Holding Cmd+ at the top would otherwise fire an event and schedule a save per repeat, waking every
        // open book to re-apply a value that did not change.
        var (svc, settings) = Create();
        for (int i = 0; i < 50; i++) svc.ZoomIn(Script.Latin);

        settings.Invocations.Clear();
        var events = 0;
        svc.ZoomChanged += (_, _) => events++;

        svc.ZoomIn(Script.Latin);

        Assert.Equal(0, events);
        Assert.DoesNotContain(settings.Invocations, i => i.Method.Name == nameof(ISettingsService.RequestSave));
    }

    [Fact]
    public void ResetZoom_AlwaysNotifies_EvenWhenAlreadyAtOneHundredPercent()
    {
        // The browser's real zoom can differ from what we stored — a Ctrl+scroll that landed before the
        // bridge script was injected, a failed injection, or a macOS trackpad pinch (page scale, which we
        // do not manage). In all of those the stored value says 100% while the text is not, so an early
        // return would make "Actual Size" a dead key precisely when it is needed. CEF's SetZoomLevel(0)
        // also clears page scale, so this is the only thing that undoes a pinch. (fable review)
        var (svc, settings) = Create();
        Assert.Equal(1.0, svc.GetZoom(Script.Latin));   // already at the default

        var events = new List<BookZoomChangedEventArgs>();
        svc.ZoomChanged += (_, e) => events.Add(e);
        settings.Invocations.Clear();

        svc.ResetZoom(Script.Latin);

        var evt = Assert.Single(events);
        Assert.Equal(1.0, evt.Zoom);
        // Notifies, but must NOT dirty the settings file — nothing actually changed.
        Assert.DoesNotContain(settings.Invocations, i => i.Method.Name == nameof(ISettingsService.RequestSave));
    }

    [Fact]
    public void ChangingZoom_RequestsADebouncedSave()
    {
        var (svc, settings) = Create();
        svc.ZoomIn(Script.Latin);
        // RequestSave, not SaveSettingsAsync: a burst of Cmd+ presses must coalesce into one write.
        settings.Verify(s => s.RequestSave(), Times.Once);
        settings.Verify(s => s.SaveSettingsAsync(), Times.Never);
    }

    [Fact]
    public void AScriptMissingFromSettings_IsSeededRatherThanSilentlyIgnored()
    {
        // A settings file written before a script existed has no entry for it. Dropping the change would
        // make that script permanently unzoomable, with no error.
        var (svc, settings) = Create();
        settings.Object.Settings.FontSettings.ScriptFonts.Remove("Thai");

        var zoom = svc.ZoomIn(Script.Thai);

        Assert.True(zoom > 1.0);
        Assert.Equal(zoom, svc.GetZoom(Script.Thai));
    }

    // ---- Clamping ---------------------------------------------------------------------------------

    [Theory]
    [InlineData(0.0)]        // what a WhenWritingDefault serializer hands back for an omitted double
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void NonsenseStoredValues_ReadBackAsOneHundredPercent(double stored)
    {
        // A zoom of 0 would set a CEF level of -infinity and blank the book entirely.
        var (svc, settings) = Create();
        settings.Object.Settings.FontSettings.ScriptFonts["Latin"].BookZoom = stored;
        Assert.Equal(1.0, svc.GetZoom(Script.Latin));
    }

    [Theory]
    [InlineData(99.0)]
    [InlineData(0.001)]
    public void OutOfRangeStoredValues_AreClampedIntoTheLadder(double stored)
    {
        var (svc, settings) = Create();
        settings.Object.Settings.FontSettings.ScriptFonts["Latin"].BookZoom = stored;
        Assert.InRange(svc.GetZoom(Script.Latin), BookZoomService.MinZoom, BookZoomService.MaxZoom);
    }

    // ---- CEF level conversion ---------------------------------------------------------------------

    [Fact]
    public void OneHundredPercent_IsCefLevelZero()
    {
        // Chromium's scale is logarithmic with 1.0 at level 0. Passing the raw factor instead would make
        // 125% render at 1.2^1.25 = ~245%.
        Assert.Equal(0.0, BookZoomService.ToCefZoomLevel(1.0), 10);
    }

    [Fact]
    public void CefLevelConversion_RoundTrips_ForEveryRung()
    {
        foreach (var rung in BookZoomService.ZoomLadder)
        {
            var level = BookZoomService.ToCefZoomLevel(rung);
            Assert.Equal(rung, BookZoomService.FromCefZoomLevel(level), 10);
        }
    }

    [Fact]
    public void CefLevel_MatchesChromiumsOwnStepOf1Point2()
    {
        // Chromium defines factor = 1.2^level, so a factor of exactly 1.2 must be level 1.
        Assert.Equal(1.0, BookZoomService.ToCefZoomLevel(1.2), 10);
        Assert.Equal(-1.0, BookZoomService.ToCefZoomLevel(1.0 / 1.2), 10);
    }

    [Theory]
    [InlineData(0.0)]
    [InlineData(-1.0)]
    [InlineData(double.NaN)]
    public void CefLevelConversion_DoesNotProduceInfinityOrNaN_ForBadInput(double bad)
    {
        var level = BookZoomService.ToCefZoomLevel(bad);
        Assert.False(double.IsNaN(level) || double.IsInfinity(level));
        Assert.Equal(0.0, level, 10);   // falls back to 100%
    }

    // ---- Readout ----------------------------------------------------------------------------------

    [Fact]
    public void IsZoomed_IsFalseAtOneHundredPercent_AndTrueOffIt()
    {
        var (svc, _) = Create();
        Assert.False(svc.IsZoomed(Script.Latin));
        svc.ZoomIn(Script.Latin);
        Assert.True(svc.IsZoomed(Script.Latin));
        svc.ResetZoom(Script.Latin);
        Assert.False(svc.IsZoomed(Script.Latin));
    }

    [Fact]
    public void FormatZoom_RendersWholePercentages()
    {
        var (svc, settings) = Create();
        Assert.Equal("100%", svc.FormatZoom(Script.Latin));

        settings.Object.Settings.FontSettings.ScriptFonts["Latin"].BookZoom = 1.25;
        Assert.Equal("125%", svc.FormatZoom(Script.Latin));

        // 0.67 must not render as "67.000000001%" or similar under any locale.
        settings.Object.Settings.FontSettings.ScriptFonts["Latin"].BookZoom = 0.67;
        Assert.Equal("67%", svc.FormatZoom(Script.Latin));
    }
}
