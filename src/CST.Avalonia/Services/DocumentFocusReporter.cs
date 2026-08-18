using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
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
    /// Records the dockable owning whatever just took focus, if the focus change was one the USER made.
    /// </summary>
    public static void NoteFocus(object? source, NavigationMethod method)
    {
        if (!ShouldReport(method)) return;

        var dockable = ResolveDockable(source);
        if (dockable == null) return;

        // Everything the user focuses is recorded, tools included — the tracker filters on read, by asking
        // each window's layout what it contains.
        App.TryGetService<ActiveDocumentTracker>()?.Note(dockable, "avalonia-focus");
    }

    /// <summary>
    /// Whether a focus change represents an interaction, judged by <b>what caused it</b>. (#635)
    ///
    /// <para>
    /// Activating a window makes Avalonia restore focus to whichever element held it last — a stale answer
    /// that arrives looking exactly like a fresh one, milliseconds from CEF's report of the browser the
    /// user actually clicked, and wins whenever it lands second. #634 suppressed those by the TYPE of
    /// element the focus landed on, which was the wrong key: the echo is defined by its cause, and an
    /// activation restore onto a tab item slipped straight through, reproducing the bug it was meant to
    /// fix in a layout where a tab had focus last.
    /// </para>
    ///
    /// <para>
    /// Avalonia already carries the cause. <c>FocusManager.SetFocusScope</c> — the activation restore —
    /// calls <c>Focus(focused)</c> with no navigation method, i.e. <see cref="NavigationMethod.Unspecified"/>,
    /// while a pointer press focuses with <see cref="NavigationMethod.Pointer"/> and keyboard navigation
    /// with <see cref="NavigationMethod.Tab"/> or <see cref="NavigationMethod.Directional"/>. Reading that
    /// distinguishes the restore from a click with no timer and no flag, on any landing element.
    /// </para>
    ///
    /// <para>
    /// Programmatic focus the app performs itself is <c>Unspecified</c> <i>by default</i> and is then also
    /// ignored, which is correct: opening a document raises the dock model's own activation, and that feed
    /// is the one that should speak for it. But this reads the METHOD, not the origin, so a programmatic
    /// call that names a user-ish method is reported as one — <c>OpenBookPanel.axaml.cs:60</c> already does
    /// this, focusing a list item with <see cref="NavigationMethod.Directional"/>. Harmless there, since
    /// tools are filtered on read and the worst case is one of the eight history slots; but a future
    /// programmatic <c>Focus(Tab)</c> or <c>Focus(Directional)</c> on a DOCUMENT would be a phantom
    /// interaction this rule exists to exclude. Pass <see cref="NavigationMethod.Unspecified"/> from any
    /// focus call the user did not make. (fable review)
    /// </para>
    /// </summary>
    internal static bool ShouldReport(NavigationMethod method) =>
        method is NavigationMethod.Pointer or NavigationMethod.Tab or NavigationMethod.Directional;

    /// <summary>
    /// Keeps Avalonia's idea of focus in step with the browser's, when CEF reports that one took it. (#633)
    ///
    /// <para>
    /// Avalonia cannot see a click that lands on a browser's native surface, so its focus record stays on
    /// whatever was focused before — and on window activation it RESTORES that record. Two things then go
    /// wrong: the resolver's first and most trusted tier, live Avalonia focus, names a document the user is
    /// not in; and the restore keeps resurrecting it, so the staleness is self-renewing. Measured: the same
    /// book was released at deactivation three times in a row while the user was reading two others.
    /// </para>
    ///
    /// <para>
    /// Clearing focus instead was tried first and CANNOT work: <c>IFocusManager.ClearFocus()</c> is
    /// <c>Focus(null)</c>, which clears the current focus but not the per-scope element the restore reads —
    /// that lives in a private attached property only a successful focus overwrites. Aligning is the
    /// operation the framework actually offers.
    /// </para>
    ///
    /// <para>
    /// Safe against the CEF focus hazard, and measured rather than assumed: focus on the document VIEW and
    /// keyboard focus in its browser coexist. In the same run, Avalonia focused a BookDisplayView and the
    /// in-page relay went on delivering keystrokes from the browser — so this records where focus is
    /// without taking it from anywhere.
    /// </para>
    /// </summary>
    public static void AlignFocusWithBrowser(IInputElement? documentView)
    {
        if (documentView is not { } element) return;

        // No focus manager means no TopLevel, i.e. the view is detached — a late callback during float or
        // unfloat teardown. Return rather than fall through: Focus() would be a no-op there anyway, but
        // "skip when already focused" silently becoming "always call" is the kind of inversion that is
        // invisible until it is not. (fable review)
        var focusManager = (element as Visual)?.FindAncestorOfType<TopLevel>()?.FocusManager;
        if (focusManager == null) return;
        if (ReferenceEquals(focusManager.GetFocusedElement(), element)) return;

        // Through the element, because IFocusManager exposes only ClearFocus and GetFocusedElement.
        // NavigationMethod.Unspecified on purpose: this is not the user moving focus, and Feed C must not
        // record it as one.
        //
        // The RESULT IS CHECKED. Focus() returns false for an element that is not Focusable, and the whole
        // point of this method is a side effect on state nothing else reads back — so a silent false leaves
        // the document's focus record permanently stale while the code reads as though it were maintained.
        // WelcomeView shipped in exactly that state: Focusable sat on its inner web view rather than its
        // root, so aligning the Welcome tab did nothing and ⌘W, resolving from live focus, acted on whatever
        // book was open before it. (fable review)
        if (!element.Focus(NavigationMethod.Unspecified))
            Serilog.Log.ForContext(typeof(DocumentFocusReporter))
                .Warning("Focus alignment did nothing for {Element} - is it Focusable?", element.GetType().Name);
    }

    /// <summary>
    /// The dockable owning <paramref name="source"/>: the same DataContext walk
    /// <c>SimpleTabbedWindow.ResolveFocusedDockable</c> performs, run from the focus event's source rather
    /// than by asking the FocusManager afterwards.
    ///
    /// <para>
    /// Separated from <see cref="NoteFocus"/> so a test can drive the walk itself — which element the
    /// predicate ends up seeing is exactly what #635 turned on, and it had no coverage.
    /// </para>
    /// </summary>
    internal static IDockable? ResolveDockable(object? source)
    {
        // Nullable walker rather than a pattern-declared non-nullable: GetVisualParent() returns Visual?,
        // and assigning that to a non-nullable local was the whole of CS8600. A non-Visual source simply
        // never enters the loop, which is what the old early return did.
        var element = source as Visual;

        while (element != null)
        {
            if (element is StyledElement { DataContext: IDockable dockable })
                return dockable;

            element = element.GetVisualParent();
        }

        return null;
    }
}
