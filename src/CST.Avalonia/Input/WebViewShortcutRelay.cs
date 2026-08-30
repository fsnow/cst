using System;
using Avalonia.Threading;
using CST.Avalonia.Views;
using Serilog;

namespace CST.Avalonia.Input;

/// <summary>
/// Forwards window-level keyboard shortcuts out of a WebView that would otherwise swallow them. (#518)
///
/// When CEF holds focus it takes the keystroke before Avalonia sees it, so a window <c>KeyBinding</c>
/// never fires. <c>BookDisplayView</c> has long solved this with its own JavaScript keydown capture, but
/// the Welcome page, the Dictionary meaning pane and the PDF viewer had no capture at all - so every
/// shortcut was dead while focus was in one of them. That is most visible on Welcome, which holds focus
/// at startup.
///
/// This is the same title-channel trick <c>BookDisplayView</c> uses, reduced to the shortcuts that make
/// sense outside a book: no View Source, no Go To, no Print, since those act on a book. Each message is
/// tagged with the view's id so a background view cannot act on a keystroke aimed at the visible one, and
/// carries a sequence number because an identical consecutive title does not raise <c>TitleChanged</c>.
///
/// Deliberately NOT macOS-guarded, unlike the window-level handlers. The <c>preventDefault</c> below
/// consumes the key equivalent before AppKit hands it to the NSMenu, so exactly one route still fires -
/// this is the mechanism <c>BookDisplayView</c> has shipped for the same combos since #443. What it does
/// change on macOS is <em>which</em> route: see the DICTIONARY/SEARCH cases below.
/// </summary>
public static class WebViewShortcutRelay
{
    public const string MessagePrefix = "CST_VIEW_SHORTCUT:";

    /// <summary>
    /// JavaScript to inject once the page has loaded. <paramref name="viewId"/> identifies the view in the
    /// messages it pushes back.
    ///
    /// <paramref name="includeFind"/> controls both find keys: plain Ctrl/Cmd+F (Find in Page, on the
    /// active BOOK) and Ctrl/Cmd+Shift+F (Search for Selection). The PDF viewer passes false; every other
    /// relaying view takes the default.
    ///
    /// <para><b>Why the PDF viewer opts out.</b> The source PDFs are page SCANS with no text layer, so
    /// there is nothing in them to find, now or ever — and its plugin frame does not deliver the keystroke
    /// to us in any case. (Confirmed by the maintainer, 2026-08-11 and again 2026-08-29.) Nor is there a
    /// Chromium find to leave it to: Chrome's find bar is browser chrome, not web content, so CEF ships
    /// <c>CefBrowserHost.Find</c> with no UI and WebViewControl does not surface even that. Find works in
    /// book tabs because <c>BookDisplayView.ShowFindBar</c> is ours.</para>
    ///
    /// <para><b>#570, and why plain Cmd+F is relayed after all.</b> #570 moved Search for Selection to
    /// Cmd+Shift+F and swallowed plain Cmd+F here, reasoning that none of the relaying views is a book.
    /// True, but find does not act on this view — it acts on the open book, which is what Cmd+F does from
    /// every other focus location in the window, the dictionary's own word list included. Swallowing it
    /// made the meaning pane the one place in the app where Cmd+F died (#846). It now forwards to the menu
    /// item's own handler, so it cannot resolve a different book than the word list does.</para>
    /// </summary>
    public static string BuildScript(string viewId, bool includeFind = true) => @"
        (function() {
            if (window.__cstViewShortcuts) { return; }   // idempotent: re-injection must not double-bind
            window.__cstViewShortcuts = true;

            function send(name) {
                document.title = '" + MessagePrefix + @"' + name
                    + '|VIEW:" + viewId + @"'
                    + '|SEQ:' + (window.__cstViewSeq = (window.__cstViewSeq || 0) + 1);
            }

            document.addEventListener('keydown', function(event) {
                if (!(event.metaKey || event.ctrlKey)) { return; }
                var k = (event.key || '').toLowerCase();
                var name = null;

                // Deliberately NOT forwarded: e/g/p and shift variants are book commands, and w is
                // handled per-view where a closable tab exists.

                if (k === 'o' && !event.shiftKey) { name = 'SELECT_BOOK'; }
                else if (k === 'd' && !event.shiftKey) { name = 'DICTIONARY'; }
                // #846: plain F finds in the active BOOK, not in this view. See BuildScript's docs.
                else if (k === 'f' && !event.shiftKey && " + (includeFind ? "true" : "false") + @") { name = 'FIND_IN_PAGE'; }
                else if (k === 'f' && event.shiftKey && " + (includeFind ? "true" : "false") + @") { name = 'SEARCH'; }
                else if (k === ',') { name = 'SETTINGS'; }

                if (name === null) { return; }

                // Suppress Chromium's own handling (Ctrl+O open-file, Ctrl+F find bar) as well as any
                // further propagation, so exactly one route acts on the keystroke.
                event.preventDefault();
                event.stopPropagation();
                if (event.repeat) { return; }
                send(name);
            }, true);   // capture phase, ahead of any page handlers
        })();
    ";

    /// <summary>
    /// Handles a title-channel message if it is one of ours and is addressed to <paramref name="viewId"/>.
    /// Returns true when handled. Safe to call from the CEF thread - the action is posted to the UI thread.
    /// </summary>
    public static bool TryHandle(string? title, string viewId, ILogger logger)
    {
        if (string.IsNullOrEmpty(title) || !title.StartsWith(MessagePrefix, StringComparison.Ordinal))
            return false;

        try
        {
            var parts = title.Split('|');
            var command = parts[0].Substring(MessagePrefix.Length);
            var messageViewId = parts.Length > 1 && parts[1].StartsWith("VIEW:", StringComparison.Ordinal)
                ? parts[1].Substring("VIEW:".Length)
                : "";

            if (messageViewId != viewId)
                return false;

            // Include the view id: all three relaying views otherwise log identically, which makes
            // "did the Dictionary pane's relay fire, or the Welcome page's?" unanswerable from the log.
            logger.Debug("*** VIEW SHORTCUT REQUESTED FROM JAVASCRIPT: {Command} (view {ViewId}) ***",
                command, viewId);

            // Runs on the CEF thread; every one of these touches the dock layout or shows a dialog. (BOOK-2)
            Dispatcher.UIThread.Post(() =>
            {
                switch (command)
                {
                    case "SELECT_BOOK":
                        SimpleTabbedWindow.RevealSelectBookPanel();
                        break;
                    case "SETTINGS":
                        _ = App.ShowSettingsWindow();
                        break;
                    // No book is focused in these views, so both simply reveal their tool with no selection.
                    //
                    // This is a deliberate behaviour change on macOS, where these keys were not previously
                    // dead: the native menu fired but its focus resolution "comes back empty and falls back
                    // to the first split's book" (the #443 wrong-book bug), so ⌘D/⌘F acted on some other
                    // book's selection. Revealing the tool with no selection is the honest answer from a
                    // view that has no book. Not a regression, but not a no-op either. (fable review)
                    case "DICTIONARY":
                        _ = SimpleTabbedWindow.LookUpInDictionaryAsync(null);
                        break;
                    case "SEARCH":
                        _ = SimpleTabbedWindow.SearchForSelectionAsync(null);
                        break;
                    // Unlike the two above, this one is NOT selection-driven and so loses nothing by
                    // arriving from a bookless view: it opens the find bar on the active book, exactly as
                    // the menu item does for every non-WebView focus location. (#846)
                    case "FIND_IN_PAGE":
                        SimpleTabbedWindow.ShowFindInActiveBook();
                        break;
                    default:
                        logger.Warning("Unknown view shortcut command: {Command}", command);
                        break;
                }
            });

            return true;
        }
        catch (Exception ex)
        {
            logger.Error("Error processing view shortcut from JavaScript | {Details}", ex.Message);
            return false;
        }
    }
}
