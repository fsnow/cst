using System;
using System.Globalization;
using CST.Avalonia.Services;
using CST.Conversion;

namespace CST.Avalonia.ViewModels;

/// <summary>
/// The toolbar zoom control's state, as a plain object. (#618)
///
/// <para>
/// This exists apart from <see cref="BookDisplayViewModel"/> for one reason: that VM cannot be constructed
/// in a test — it needs a <c>Book</c>, a dock factory and a WebView, and it starts loading in its
/// constructor. Everything the control does that could be wrong (which script an event belongs to, what a
/// typed string means, whether a step button should be live) therefore lives here, where a test can drive it
/// against a real <see cref="BookZoomService"/> and no browser at all. The VM keeps one of these and
/// forwards its notifications as property changes; the same pattern as
/// <c>BookFaceRenderDecisionTests</c> covers, one step up from static methods because this one has to hold a
/// subscription.
/// </para>
///
/// <para>
/// It deliberately does no dispatching. The service raises <see cref="IBookZoomService.ZoomChanged"/> on
/// whatever thread made the change, and marshalling is the VM's business — keeping <c>Dispatcher</c> out of
/// here is what lets the tests run headless.
/// </para>
/// </summary>
public sealed class BookZoomReadout : IDisposable
{
    private readonly IBookZoomService _service;
    private readonly Func<Script> _currentScript;
    private readonly Action _notify;
    private EventHandler<BookZoomChangedEventArgs>? _handler;

    /// <param name="currentScript">
    /// Read on every access rather than captured, because a book can change script at runtime and the
    /// readout has to follow it to the new script's zoom.
    /// </param>
    /// <param name="notify">Invoked when this book's readout has changed and the UI should re-read it.</param>
    public BookZoomReadout(IBookZoomService service, Func<Script> currentScript, Action notify)
    {
        _service = service;
        _currentScript = currentScript;
        _notify = notify;

        _handler = (_, args) =>
        {
            // Zoom is stored per script, so every open book hears every change. Only the ones showing that
            // script are affected — and they all are, including the other tabs, which is what keeps two
            // views of one script from disagreeing about what they are showing.
            if (AppliesTo(args.Script, _currentScript())) _notify();
        };
        _service.ZoomChanged += _handler;
    }

    /// <summary>The stored zoom for the book's current script.</summary>
    public double Zoom => _service.GetZoom(_currentScript());

    /// <summary>
    /// "125%" — what the entry box shows when it is not being typed into.
    ///
    /// The percentage alone, with no script name: zoom has been per script since #572, so a control that
    /// changes every open book in the script is not introducing that behaviour, only giving it a second
    /// surface. The scope is stated in the box's own tooltip instead of spent on toolbar width.
    /// </summary>
    public string Display => _service.FormatZoom(_currentScript());

    // Greyed at the ends of the ladder rather than silently doing nothing, which is the difference between
    // "you are at the limit" and "this button is broken". The service's own tolerance, not a tighter one of
    // our own: a value inside its "unchanged" band makes it return early without raising ZoomChanged, so a
    // button enabled on a finer comparison would be exactly the dead button this is here to avoid.
    public bool CanZoomIn => Zoom < BookZoomService.MaxZoom - BookZoomService.Epsilon;
    public bool CanZoomOut => Zoom > BookZoomService.MinZoom + BookZoomService.Epsilon;

    /// <summary>
    /// Call when the book's script changes. Nothing about the stored zoom changed, but which zoom this
    /// readout is showing did — miss this and the control keeps displaying the old script's value, which is
    /// worse than showing nothing because it looks authoritative.
    /// </summary>
    public void ScriptChanged() => _notify();

    /// <summary>Whether a change for <paramref name="changed"/> concerns a book displaying <paramref name="book"/>.</summary>
    public static bool AppliesTo(Script changed, Script book) => changed == book;

    /// <summary>
    /// Parses what the user typed into a zoom factor.
    ///
    /// <para>
    /// Lenient about the things people actually type — surrounding space, a percent sign, a decimal point —
    /// and silent about the rest: a failed parse leaves the stored zoom alone and the box is put back to the
    /// real value, so a typo can never render the book at a size nobody asked for.
    /// </para>
    ///
    /// <para>
    /// The result is rounded to a whole percent. That is <b>not</b> the ladder snapping #618 rules out — 115
    /// stays 115, well off any rung. It is so that what gets stored is what the box will display, since the
    /// readout shows whole percent; storing 115.4 while displaying 115% would leave the control quietly
    /// disagreeing with the setting behind it.
    /// </para>
    ///
    /// <para>
    /// Range is not checked here. <see cref="IBookZoomService.SetZoom"/> clamps, and clamping 500 to 300%
    /// answers the user's intent better than refusing it.
    /// </para>
    /// </summary>
    public static bool TryParseEntry(string? text, out double zoom)
    {
        zoom = 0;
        if (string.IsNullOrWhiteSpace(text)) return false;

        var trimmed = text.Trim().TrimEnd('%', ' ', '\t');
        // Both cultures: the box is seeded from an invariant-formatted string, but a user typing a decimal
        // uses their own separator. Invariant first so "115.5" means the same thing everywhere.
        if (!double.TryParse(trimmed, NumberStyles.Float, CultureInfo.InvariantCulture, out var percent) &&
            !double.TryParse(trimmed, NumberStyles.Float, CultureInfo.CurrentCulture, out percent))
            return false;

        if (double.IsNaN(percent) || double.IsInfinity(percent) || percent <= 0) return false;

        zoom = Math.Round(percent) / 100.0;
        return zoom > 0;
    }

    public void Dispose()
    {
        // The service is a singleton, so this subscription roots the VM — the same leak shape the View's
        // zoom and FontService subscriptions carry comments about.
        if (_handler != null) _service.ZoomChanged -= _handler;
        _handler = null;
    }
}
