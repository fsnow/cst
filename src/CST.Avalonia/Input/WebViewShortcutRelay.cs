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
    /// <paramref name="includeFind"/> controls Ctrl/Cmd+F. The PDF viewer passes false: Chromium's
    /// find-in-page is the one shortcut users genuinely want there, and intercepting it would trade that
    /// away for "Search for Selection" with no selection - a straight downgrade. (fable review)
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
                else if (k === 'f' && !event.shiftKey && " + (includeFind ? "true" : "false") + @") { name = 'SEARCH'; }
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
