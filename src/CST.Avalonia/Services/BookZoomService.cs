using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using CST.Avalonia.Models;
using CST.Conversion;
using Serilog;

namespace CST.Avalonia.Services;

/// <summary>Raised when a script's book zoom changes, so every open book in that script can re-apply it.</summary>
public class BookZoomChangedEventArgs : EventArgs
{
    public BookZoomChangedEventArgs(Script script, double zoom) { Script = script; Zoom = zoom; }
    public Script Script { get; }
    public double Zoom { get; }
}

public interface IBookZoomService
{
    /// <summary>The stored zoom for a script, clamped to the supported range. 1.0 when nothing is stored.</summary>
    double GetZoom(Script script);

    /// <summary>Steps up the ladder. Returns the new zoom (unchanged at the ceiling).</summary>
    double ZoomIn(Script script);

    /// <summary>Steps down the ladder. Returns the new zoom (unchanged at the floor).</summary>
    double ZoomOut(Script script);

    /// <summary>Returns to 1.0 — the shipped stylesheet sizes exactly.</summary>
    double ResetZoom(Script script);

    /// <summary>
    /// Sets an arbitrary zoom, clamped to the supported range. Returns the value actually stored.
    ///
    /// <para>
    /// The one thing the keyboard structurally cannot do: <b>arrive at a value instead of traversing to
    /// it</b>. <see cref="ZoomIn"/> and <see cref="ZoomOut"/> walk the ladder a rung per press, which is
    /// right for a key you hold down and wrong for "make it 115%". A toolbar percentage is the surface that
    /// needs this; the ladder stays what it should be — the preset list and the step targets, not the set of
    /// legal values.
    /// </para>
    ///
    /// <para>
    /// A value between rungs is stored as given, not snapped. Chrome's PDF viewer is the model: land on 97%
    /// and the next <c>+</c> takes you to 100%, then on up the sequence. <see cref="ZoomIn"/> already
    /// behaves that way for any starting value, so nothing downstream has to care whether a zoom came from
    /// a key or a field.
    /// </para>
    /// </summary>
    double SetZoom(Script script, double zoom);

    /// <summary>True when this script is not at 100%, i.e. the readout should show something.</summary>
    bool IsZoomed(Script script);

    /// <summary>"125%" — for menus and the Settings readout.</summary>
    string FormatZoom(Script script);

    event EventHandler<BookZoomChangedEventArgs>? ZoomChanged;
}

/// <summary>
/// Owns per-script book-text zoom. (#572)
///
/// <para>
/// Zoom is the <b>only</b> stored size control for book text. #574 flattened all 14 stylesheets to one shared
/// ladder, and #42 was narrowed to font <i>face</i> selection, so there is deliberately no user-editable base
/// size — the two were redundant, and a live control beats typing a number into a dialog for a judgment that
/// can only be made by eye. Effective size is <c>shippedXslSize × zoom[script]</c>.
/// </para>
///
/// <para>
/// Per <i>script</i> rather than per tab or global: the stylesheet ladder is now identical across scripts, so
/// the only thing zoom compensates for is the metrics of whatever face that script resolves to — which is
/// exactly what the face setting is scoped to. A book switching script therefore picks up that script's zoom
/// along with its face.
/// </para>
///
/// <para>
/// This service is the model only. Pushing a level into a browser is <c>BookDisplayView</c>'s job, since that
/// is what owns a WebView; this type never touches CEF.
/// </para>
/// </summary>
public class BookZoomService : IBookZoomService
{
    /// <summary>
    /// The zoom ladder. Chromium's own ladder trimmed at both ends: a reader has no use for 25% (illegible)
    /// or 500% (a few words per screen), and every extra rung is one more press to cross. Kept at 10% steps
    /// through the 90–110 range because that is where face calibration actually happens — the coarser 25%
    /// jumps higher up are fine for "I want it bigger" but useless for "does this face look right".
    /// </summary>
    private static readonly double[] Ladder =
        { 0.50, 0.67, 0.75, 0.80, 0.90, 1.00, 1.10, 1.25, 1.50, 1.75, 2.00, 2.50, 3.00 };

    public static double MinZoom => Ladder[0];
    public static double MaxZoom => Ladder[^1];
    public const double DefaultZoom = 1.0;

    // Two ladder rungs can sit 0.05 apart, so the tolerance for "already on this rung" has to be well below
    // that. Also absorbs the float error from a JSON round-trip of 0.67.
    private const double Epsilon = 0.001;

    private readonly ISettingsService _settingsService;
    private readonly ILogger _logger;

    public BookZoomService(ISettingsService settingsService, ILogger? logger = null)
    {
        _settingsService = settingsService;
        _logger = logger ?? Log.ForContext<BookZoomService>();
    }

    public event EventHandler<BookZoomChangedEventArgs>? ZoomChanged;

    public double GetZoom(Script script)
    {
        var settings = _settingsService.Settings.FontSettings;
        if (settings.TryGetFont(script, out var setting) && setting != null)
            return Clamp(setting.BookZoom);

        // A script absent from the dictionary is normal for a settings file written before this script
        // existed, and for any key the FontSettings constructor does not seed.
        return DefaultZoom;
    }

    public double ZoomIn(Script script) => Step(script, +1);

    public double ZoomOut(Script script) => Step(script, -1);

    /// <summary>
    /// Returns to 1.0 — and always notifies, even when the stored value was already 1.0.
    ///
    /// That "pointless" notification is the whole value of the command. The browser's real zoom can drift
    /// away from what we stored: a Ctrl+scroll that landed before the bridge script was injected, a failed
    /// injection, or a macOS trackpad pinch (page-scale, which we deliberately do not manage). In every one
    /// of those states the text is not at 100% while the setting says it is, and an early return would make
    /// "Actual Size" a dead key exactly when the user needs it. CEF's SetZoomLevel(0) additionally resets
    /// page scale, so this is also the only thing that undoes a pinch. (fable review)
    /// </summary>
    public double ResetZoom(Script script) => SetZoom(script, DefaultZoom, alwaysNotify: true);

    /// <summary>
    /// Arbitrary zoom, for a percentage control. See the interface for why this is not a ladder operation.
    ///
    /// <para>
    /// Everything a typed value needs already existed behind the private overload — clamping, the
    /// already-there early return, the debounced save, and the per-script notification that re-zooms every
    /// open book in that script. This adds no mechanism; it opens the door to it.
    /// </para>
    /// </summary>
    public double SetZoom(Script script, double zoom) => SetZoom(script, zoom, alwaysNotify: false);

    public bool IsZoomed(Script script) => Math.Abs(GetZoom(script) - DefaultZoom) > Epsilon;

    public string FormatZoom(Script script) =>
        (GetZoom(script) * 100).ToString("0", CultureInfo.InvariantCulture) + "%";

    /// <summary>
    /// Moves <paramref name="direction"/> rungs from the current value. When the stored value is between
    /// rungs — a hand-edited settings file, or a value from a future build with a different ladder — this
    /// snaps to the next rung in the direction of travel rather than to the nearest, so a press always moves
    /// and always moves the way the user asked.
    /// </summary>
    private double Step(Script script, int direction)
    {
        var current = GetZoom(script);

        double target = direction > 0
            ? Ladder.FirstOrDefault(r => r > current + Epsilon, Ladder[^1])
            : Ladder.LastOrDefault(r => r < current - Epsilon, Ladder[0]);

        return SetZoom(script, target);
    }

    private double SetZoom(Script script, double zoom, bool alwaysNotify = false)
    {
        zoom = Clamp(zoom);
        var settings = _settingsService.Settings.FontSettings;

        if (!settings.TryGetFont(script, out var setting) || setting == null)
        {
            // Seed the entry rather than dropping the change: a script missing from the dictionary would
            // otherwise be permanently unzoomable, and silently so.
            setting = new ScriptFontSetting();
            settings.ScriptFonts[ScriptKeys.Of(script)] = setting;
        }

        var unchanged = Math.Abs(setting.BookZoom - zoom) <= Epsilon;
        if (unchanged && !alwaysNotify)
        {
            // Already there — at a ladder end this is every repeat press. Returning early keeps it from
            // scheduling a save and waking every open book for nothing.
            return zoom;
        }

        if (!unchanged)
        {
            setting.BookZoom = zoom;
            _settingsService.RequestSave();   // debounced: a burst of Cmd+ presses coalesces into one write
        }

        _logger.Information("Book zoom for {Script} set to {Zoom:P0}", script, zoom);
        ZoomChanged?.Invoke(this, new BookZoomChangedEventArgs(script, zoom));
        return zoom;
    }

    private static double Clamp(double zoom)
    {
        // Covers a hand-edited settings file and, importantly, 0.0 — which is what a serializer configured
        // with WhenWritingDefault would hand back for an omitted double. Settings does not use that setting
        // today, but a zoom of 0 would blank the text entirely, so it is not worth depending on.
        if (double.IsNaN(zoom) || zoom <= 0) return DefaultZoom;
        return Math.Clamp(zoom, MinZoom, MaxZoom);
    }

    /// <summary>
    /// Chromium expresses zoom logarithmically: <c>factor = 1.2^level</c>, so level 0 is 100%. This is what
    /// <c>CefBrowserHost.SetZoomLevel</c> takes — passing it a raw factor would set 125% to 1.2^1.25 ≈ 245%.
    /// </summary>
    public static double ToCefZoomLevel(double zoomFactor)
    {
        if (double.IsNaN(zoomFactor) || zoomFactor <= 0) zoomFactor = DefaultZoom;
        return Math.Log(zoomFactor) / Math.Log(1.2);
    }

    /// <summary>Inverse of <see cref="ToCefZoomLevel"/>, for reading a level back out of a browser.</summary>
    public static double FromCefZoomLevel(double zoomLevel) => Math.Pow(1.2, zoomLevel);

    /// <summary>The ladder, exposed for tests and for a Settings readout that wants to show the steps.</summary>
    public static IReadOnlyList<double> ZoomLadder => Ladder;
}
