# View Menu Panel Management

**Status**: Implemented
**Date**: October 26, 2025
**Version**: Beta 3

## Overview

Implemented a View menu system that allows users to show/hide the "Select a Book" and "Search" panels from any window (main or floating). This feature is essential for power users with multi-monitor setups who want to float panels out or temporarily hide them to maximize screen space.

## User-Facing Features

### View Menu Items
- **"Select a Book"**: Toggle visibility of the book selection tree panel
- **"Search"**: Toggle visibility of the search panel

### Behavior
- Menu items appear in both main window and all floating windows
- Checkmarks indicate current panel visibility state
- Clicking a checked item hides the panel
- Clicking an unchecked item shows/restores the panel
- Panels can be floated out to separate windows after restoration
- Empty windows/docks are automatically cleaned up

### Supported Workflows
1. **Hide/Show in main window**: Collapse left pane when both panels hidden
2. **Float and close**: Close floating window → checkmark updates → reopen via menu
3. **Float and separate**: Float both panels → drag one out to second window → close either window
4. **Restore after hide**: Hide panels → restore via menu → panels are fully functional (selectable, floatable)

## Technical Implementation

### Architecture

The implementation spans multiple components:

1. **LayoutViewModel** (core logic): Manages panel visibility state and dock manipulation
2. **App.axaml.cs**: Handles menu event wiring and checkmark synchronization
3. **SimpleTabbedWindow.axaml**: Defines View menu for main window
4. **CstHostWindow**: Defines View menu for floating windows
5. **CstDockFactory**: Integrates with window close events

### Critical Implementation Details

#### 1. Factory-Based Dockable Management

**CRITICAL**: Always use Factory methods, never directly manipulate collections.

```csharp
// ❌ WRONG - Creates non-functional panels
leftToolDock.VisibleDockables?.Add(openBookTool);
leftToolDock.ActiveDockable = openBookTool;

// ✅ CORRECT - Properly initialized panels
_factory.AddDockable(leftToolDock, openBookTool);
_factory.SetActiveDockable(openBookTool);
_factory.SetFocusedDockable(leftToolDock, openBookTool);
```

**Why**: Dock.Avalonia's Factory methods perform essential initialization that makes:
- Tabs selectable (show blue highlight when clicked)
- Panels draggable/floatable
- Context menu and other interactions work correctly

Without Factory initialization, panels appear but are non-interactive.

#### 2. Factory Property Assignment

**CRITICAL**: Newly created Tool objects must have their Factory property set.

```csharp
var openBookTool = new Tool
{
    Id = "OpenBookTool",
    Title = "Select a Book",
    Context = openBookViewModel,
    CanPin = false,
    CanClose = false,
    Factory = _factory  // ← REQUIRED for floating
};
```

**Why**: The Factory property enables the docking framework to create floating windows when tools are dragged out.

#### 3. Panel Visibility State Management

Global visibility tracking with per-window menu synchronization:

```csharp
// LayoutViewModel maintains global state
public bool IsSelectBookPanelVisible { get; private set; }
public bool IsSearchPanelVisible { get; private set; }
public event EventHandler? PanelVisibilityChanged;

// App.axaml.cs tracks menu items across all windows
private List<NativeMenuItem> _selectBookMenuItems = new List<NativeMenuItem>();
private List<NativeMenuItem> _searchMenuItems = new List<NativeMenuItem>();

// Update all menu checkmarks when visibility changes
private void OnPanelVisibilityChanged(object? sender, EventArgs e)
{
    if (sender is LayoutViewModel layoutViewModel)
    {
        foreach (var menuItem in _selectBookMenuItems)
        {
            menuItem.IsChecked = layoutViewModel.IsSelectBookPanelVisible;
        }
        foreach (var menuItem in _searchMenuItems)
        {
            menuItem.IsChecked = layoutViewModel.IsSearchPanelVisible;
        }
    }
}
```

**Design Decision**: Use global visibility state (if panel exists anywhere, it's considered "visible") rather than per-window state. This keeps the UI simple and predictable for users.

#### 4. Empty Dock/Window Cleanup

Remove empty containers to avoid blank UI areas:

```csharp
// After removing a tool from ToolDock
if (parentDock.VisibleDockables.Count == 0)
{
    // Check if in floating window
    if (IsLayoutEmpty(floatingWindow.Layout))
    {
        floatingWindow.Close();
        return;
    }

    // Otherwise remove from main layout
    if (parentDock.Id == "LeftToolDock")
    {
        mainDock?.VisibleDockables?.Remove(parentDock);
    }
}
```

**Why**: Prevents blank left pane in main window and empty floating windows.

## Edge Cases Handled

### 1. Prevent Duplicate Panels
When recreating ToolDock, only add the requested panel, not other panels that might be floating elsewhere:

```csharp
// ✅ CORRECT - Only add requested panel
var toolsToAdd = new List<IDockable> { openBookTool };

// ❌ WRONG - Would duplicate panels from floating windows
var searchTool = FindTool("SearchTool");  // Might be in floating window!
if (searchTool != null)
{
    toolsToAdd.Add(searchTool);  // Creates duplicate
}
```

### 2. Window Close Detection
When user closes floating window with X button, update visibility state:

```csharp
// CstDockFactory.CloseHostWindow()
HostWindows.Remove(hostWindow);

// Critical: Update panel visibility after removing window
if (App.MainWindow?.DataContext is LayoutViewModel layoutViewModel)
{
    layoutViewModel.UpdatePanelVisibility();
}
```

**Why**: Without this, checkmarks remain checked after window close, and panels can't be reopened.

### 3. Menu State for Floating Windows
Each floating window gets its own View menu, but all menus share the same event handlers:

```csharp
// CstHostWindow.SetupViewMenu() creates menu structure
// App.SetupFloatingWindowMenu() wires up events and adds to tracking lists
public void SetupFloatingWindowMenu(Window window)
{
    var windowMenu = NativeMenu.GetMenu(window);
    // Find View menu items and add to tracking lists
    // Wire up same toggle event handlers as main window
}
```

### 4. Empty Layout Detection
Recursive check to determine if floating window is empty:

```csharp
private bool IsLayoutEmpty(IDockable? dockable)
{
    if (dockable is IDock dock)
    {
        if (dock.VisibleDockables == null || dock.VisibleDockables.Count == 0)
            return true;

        foreach (var child in dock.VisibleDockables)
        {
            if (!IsLayoutEmpty(child))
                return false;
        }
        return true;
    }
    return false;  // Leaf dockable (Tool, Document) is not empty
}
```

**Why**: Floating window's Layout is a root dock, not the ToolDock directly. Need recursive check.

## Related Files

### Core Implementation
- **LayoutViewModel.cs** (lines 209-605)
  - `ShowSelectBookPanel()` / `ShowSearchPanel()`: Create/restore panels
  - `HideSelectBookPanel()` / `HideSearchPanel()`: Remove panels
  - `ToggleSelectBookPanel()` / `ToggleSearchPanel()`: Toggle visibility
  - `RemoveToolFromLayout()`: Handle removal with cleanup
  - `UpdatePanelVisibility()`: Sync state with actual layout
  - Helper methods: `FindTool()`, `FindToolDock()`, `IsLayoutEmpty()`

### Menu Integration
- **App.axaml.cs** (lines 247-326)
  - Menu item tracking lists
  - `SetupMainWindowMenu()`: Wire up main window View menu
  - `SetupFloatingWindowMenu()`: Wire up floating window View menus
  - `OnPanelVisibilityChanged()`: Update all menu checkmarks

### UI Definitions
- **SimpleTabbedWindow.axaml** (lines 12-23): Main window View menu structure
- **CstHostWindow.cs** (lines 50-79): Floating window View menu structure

### Factory Integration
- **CstDockFactory.cs** (lines 2109-2115): Call `UpdatePanelVisibility()` on window close

## Lessons Learned

1. **Always use Factory methods**: Direct collection manipulation creates broken dockables
2. **Factory property is required**: Without it, panels can't be floated
3. **Recursive searches needed**: Floating windows have complex layout hierarchies
4. **Global vs per-window state**: Chose global visibility state for simplicity
5. **Menu tracking across windows**: Need Lists, not single references
6. **Empty container cleanup**: Prevents confusing blank UI areas
7. **Initialization matters**: `SetActiveDockable()` and `SetFocusedDockable()` required for interactivity

## Testing Checklist

- [ ] Hide both panels → left pane collapses
- [ ] Show panels → left pane appears with both panels
- [ ] Hide/show individual panels → works correctly
- [ ] Float panel → close window → checkmark updates
- [ ] Reopen panel after floating → panel appears in main window
- [ ] Float and separate to 2 windows → close either → state correct
- [ ] Restored panels are selectable (blue highlight on click)
- [ ] Restored panels can be floated out
- [ ] Empty floating windows close automatically
- [ ] Checkmarks sync across all windows
- [ ] Menu works from both main and floating windows

## Future Considerations

- Consider per-window visibility state if users want panels in multiple windows simultaneously
- Add keyboard shortcuts (Cmd+1, Cmd+2) for toggling panels
- Persist panel visibility state across app sessions
- Add "Reset Layout" that restores default panel arrangement
