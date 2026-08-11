using System;
using Avalonia.Threading;
using Xilium.CefGlue;
using Xilium.CefGlue.Common.Handlers;

namespace CST.Avalonia.Services;

/// <summary>Result of a find-in-page pass: how many matches, and which one is active.</summary>
public class FindResultEventArgs : EventArgs
{
    public FindResultEventArgs(int identifier, int count, int activeMatchOrdinal, CefRectangle selectionRect, bool finalUpdate)
    {
        Identifier = identifier;
        Count = count;
        ActiveMatchOrdinal = activeMatchOrdinal;
        SelectionRect = selectionRect;
        FinalUpdate = finalUpdate;
    }

    /// <summary>
    /// Chromium's request id for this search. Increases across searches, so a reply carrying an id below
    /// the newest one seen belongs to a search that has already been superseded and must be discarded —
    /// otherwise a late final from the previous query installs its total against the current one.
    /// </summary>
    public int Identifier { get; }

    /// <summary>Total matches found. Rises as Chromium scans; only trustworthy once <see cref="FinalUpdate"/>.</summary>
    public int Count { get; }

    /// <summary>1-based index of the highlighted match, or 0 when there is none.</summary>
    public int ActiveMatchOrdinal { get; }

    /// <summary>
    /// Where the active match sits in the viewport. Not currently read — it was needed by an overlay find
    /// bar, which turned out not to render over this WebView, so the bar is docked instead. Kept because it
    /// is the only positional information Chromium offers about a match.
    /// </summary>
    public CefRectangle SelectionRect { get; }

    /// <summary>True on the last callback for this search. Earlier ones are partial.</summary>
    public bool FinalUpdate { get; }
}

/// <summary>
/// Receives Chromium's find-in-page results for one book. (#570)
///
/// <para>
/// Attached via <c>BaseCefBrowser.FindHandler</c>, which is a public setter on a public type — so unlike
/// reaching the browser itself, no reflection is involved here. <see cref="CefBrowserAccess"/> handles the
/// one reflection hop needed to get the browser.
/// </para>
///
/// <para>
/// <b>Callbacks arrive on a CEF thread</b>, so everything is marshalled to the UI thread before the event is
/// raised. Subscribers touch view state and must not have to know that.
/// </para>
///
/// <para>
/// Chromium reports <b>incrementally</b> while it is still scanning the document, so <c>count</c> climbs
/// across several callbacks for a single search and only the one with <c>finalUpdate</c> set is
/// authoritative. Consumers that render the count without honouring that flag show a number visibly ticking
/// upward after every keystroke.
/// </para>
/// </summary>
public class CstFindHandler : FindHandler
{
    private readonly Action<FindResultEventArgs> _onResult;

    public CstFindHandler(Action<FindResultEventArgs> onResult) => _onResult = onResult;

    protected override void OnFindResult(CefBrowser browser, int identifier, int count,
        CefRectangle selectionRect, int activeMatchOrdinal, bool finalUpdate)
    {
        // CEF 120's Find() no longer takes an identifier, so we cannot correlate against one we issued —
        // but Chromium still reports one, and it increases across searches, which is enough to recognise a
        // reply from a search that has since been superseded. (fable review)
        var args = new FindResultEventArgs(identifier, count, activeMatchOrdinal, selectionRect, finalUpdate);
        Dispatcher.UIThread.Post(() => _onResult(args));
    }
}
