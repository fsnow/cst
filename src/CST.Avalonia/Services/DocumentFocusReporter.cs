using Avalonia;
using Avalonia.VisualTree;
using Dock.Model.Core;
using Microsoft.Extensions.DependencyInjection;

namespace CST.Avalonia.Services;

/// <summary>
/// Feed C of the interaction history (#621): Avalonia focus transitions, from a window-level GotFocus
/// handler.
///
/// <para>
/// The other two feeds each have a blind spot this covers. The dock model's activation event does not fire
/// when the user clicks the tab of the pane that is ALREADY active — nothing about the model changed — and
/// CEF's focus callback only speaks for clicks that land inside a browser. Between them sits a real case:
/// click the already-active tab of the second split, then move focus to a tool, and without this the next
/// ⌘W would close a tab in the other pane.
/// </para>
///
/// <para>
/// Kept out of <see cref="ActiveDocumentTracker"/> so that type stays free of Avalonia, which is what lets
/// the history be tested headlessly.
/// </para>
/// </summary>
internal static class DocumentFocusReporter
{
    /// <summary>
    /// Records the dockable owning whatever just took focus. The same DataContext walk
    /// <c>SimpleTabbedWindow.ResolveFocusedDockable</c> performs, run from the focus event's source rather
    /// than by asking the FocusManager afterwards.
    /// </summary>
    public static void NoteFocus(object? source)
    {
        if (source is not Visual element) return;

        while (element != null)
        {
            if (element is StyledElement { DataContext: IDockable dockable })
            {
                // A browser-hosting view speaks for itself through CEF, not through Avalonia — see
                // ShouldReport.
                if (!ShouldReport(element))
                {
                    Serilog.Log.ForContext(typeof(DocumentFocusReporter))
                        .Debug("Avalonia focus ignored (browser view owns its focus): {Element} -> {Dockable}",
                            element.GetType().Name, dockable.Id);
                    return;
                }

                // Everything else is recorded, tools included — the tracker filters on read, by asking
                // each window's layout what it contains.
                App.ServiceProvider?.GetService<ActiveDocumentTracker>()?.Note(dockable, "avalonia-focus");
                return;
            }

            element = element.GetVisualParent();
        }
    }

    /// <summary>
    /// Whether an Avalonia focus landing counts as an interaction.
    ///
    /// <para>
    /// It does not when it lands on a browser-hosting document view. Those views are focusable and are what
    /// Avalonia focuses when a window is activated or a layout rebuilt, so they emit a focus event naming
    /// whichever document held focus LAST — a stale answer that arrives looking exactly like a fresh one,
    /// within milliseconds of the correct CEF report, and wins whenever it happens to land second.
    /// </para>
    ///
    /// <para>
    /// Nothing is lost by ignoring them, because those are precisely the documents whose real focus CEF
    /// reports directly (<c>CstWebView.BrowserGotFocus</c>). What Avalonia still speaks for — and what this
    /// feed exists for — is everything else: a tab-strip click, which raises no activation when the tab is
    /// already active in its own split, and the toolbar controls and tool panes.
    /// </para>
    /// </summary>
    internal static bool ShouldReport(object? focusedElement) => focusedElement is not IBrowserDocumentView;
}
