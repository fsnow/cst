using System.Linq;
using Dock.Model.Controls;
using Dock.Model.Core;

namespace CST.Avalonia.Services;

/// <summary>
/// Works out which document a window-level command (Cmd+W, Cmd+G, Cmd+E, Cmd+D, Cmd+F) should act on.
///
/// The handlers used to walk the layout and take the FIRST DocumentDock they found, then its
/// ActiveDockable. In a split layout that is simply the left/top pane regardless of where the user is
/// working, so a shortcut pressed in the second split acted on the first split's tab - closing the wrong
/// book, or opening Go To for it. (#443)
///
/// Resolution now follows focus, which is what the user means by "this book", and falls back to the old
/// first-dock behaviour when nothing usable is focused (a fresh layout, or focus sitting on a tool).
/// </summary>
public static class DocumentTargetResolver
{
    /// <summary>
    /// The document the user is working in: the focused document, else the active document of the dock
    /// that holds focus, else the active document of the first document dock in the layout.
    /// </summary>
    /// <param name="preferred">
    /// A dockable the caller believes the user is working in - normally derived from real Avalonia keyboard
    /// focus (see SimpleTabbedWindow.ResolveFocusedDockable). Used when it sits inside a document dock.
    /// Dock's own <c>RootDock.FocusedDockable</c> is never populated in this app - it is null even right
    /// after clicking a tab - so it cannot be the source of truth on its own.
    /// </param>
    public static IDockable? ResolveActiveDocument(IDock? layout, IDockable? preferred = null)
    {
        if (layout == null)
            return null;

        if (preferred != null && FindDocumentDockContaining(layout, preferred) is { } preferredDock)
            return preferred is IDock ? preferredDock.ActiveDockable : preferred;

        var focused = layout.FocusedDockable;

        // What makes something a document here is WHERE it lives, not what it implements: in Dock 11.3.6.5
        // the Tool class implements IDocument as well as ITool, so `focused is IDocument` matches the
        // Search and Dictionary tools too (DocumentTargetResolverTests guards this). Ask the layout instead
        // - find the document dock whose subtree holds the focused dockable.
        //
        // This also handles stale focus for free: FocusedDockable is not cleared when a tab closes, and a
        // dockable that is no longer in the layout is contained by nothing, so it falls through.
        if (focused != null)
        {
            var owningDock = FindDocumentDockContaining(layout, focused);
            if (owningDock != null)
            {
                // Focus on the dock itself (rather than on one of its tabs) means "its current tab".
                return focused is IDock ? owningDock.ActiveDockable : focused;
            }
        }

        return FindFirstDocumentDock(layout)?.ActiveDockable;
    }

    /// <summary>
    /// The first document dock in the layout, in tree order. This is the pre-#443 resolution and remains
    /// the fallback; prefer <see cref="ResolveActiveDocument"/> for anything the user aims at.
    /// </summary>
    public static IDock? FindFirstDocumentDock(IDock? dock)
    {
        if (dock == null)
            return null;

        if (dock.Id == "MainDocumentDock" || dock is IDocumentDock)
            return dock;

        if (dock.VisibleDockables == null)
            return null;

        foreach (var child in dock.VisibleDockables.OfType<IDock>())
        {
            var result = FindFirstDocumentDock(child);
            if (result != null)
                return result;
        }

        return null;
    }

    // The document dock whose subtree contains the target (the target may be the dock itself).
    private static IDock? FindDocumentDockContaining(IDock dock, IDockable target)
    {
        var isDocumentDock = dock.Id == "MainDocumentDock" || dock is IDocumentDock;
        if (isDocumentDock && Contains(dock, target))
            return dock;

        if (dock.VisibleDockables == null)
            return null;

        foreach (var child in dock.VisibleDockables.OfType<IDock>())
        {
            var result = FindDocumentDockContaining(child, target);
            if (result != null)
                return result;
        }

        return null;
    }

    private static bool Contains(IDock dock, IDockable target)
    {
        if (ReferenceEquals(dock, target))
            return true;

        if (dock.VisibleDockables == null)
            return false;

        foreach (var dockable in dock.VisibleDockables)
        {
            if (ReferenceEquals(dockable, target))
                return true;
            if (dockable is IDock childDock && Contains(childDock, target))
                return true;
        }

        return false;
    }
}
