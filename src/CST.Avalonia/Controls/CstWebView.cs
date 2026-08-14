using System;
using Avalonia.Threading;
using WebViewControl;

namespace CST.Avalonia.Controls;

/// <summary>
/// A <see cref="WebView"/> that reports when its BROWSER takes focus. (#621)
///
/// <para>
/// Window-level commands resolve their target from Avalonia's keyboard focus, and a CEF WebView hosts a
/// native surface: a click inside a book, a PDF or the Welcome page never reaches Avalonia, so focus
/// resolution comes back empty and the command falls through to "the first document dock in tree order" —
/// the wrong pane in any split. This is the only signal that can see such a click.
/// </para>
///
/// <para>
/// <b>Verified on macOS before the design was built on it</b>, because the assumption was not safe: the
/// PDF's content lives in an internal plugin frame that has already defeated one mechanism here — the
/// injected JS keydown relay is inert there (#518, measured on Windows). A logging build showed
/// <c>OnGotFocus</c> firing for the book body, the PDF body, and alternating correctly between them, one
/// event per click. It is reported at the browser level, above the plugin frame, which is why it survives
/// where the in-page listener does not.
/// </para>
///
/// <para>
/// Two things that probe also settled, and that this class is shaped by: <c>OnLostFocus</c> never fired at
/// all, so nothing may depend on being told focus LEFT; and <c>OnSetFocus</c> returned "suppress" every
/// time while focus was granted regardless, so it is not a usable predictor. Only <c>OnGotFocus</c> is
/// overridden here.
/// </para>
/// </summary>
public class CstWebView : WebView
{
    /// <summary>
    /// Raised when the browser takes focus. Marshalled to the UI thread: on macOS these arrived on it
    /// already, but that is CEF's business, not a guarantee, and the handler touches app state.
    /// </summary>
    public event Action? BrowserGotFocus;

    protected override void OnGotFocus()
    {
        var handler = BrowserGotFocus;
        if (handler != null) Dispatcher.UIThread.Post(() => handler());
        base.OnGotFocus();
    }
}
