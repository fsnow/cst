# Window and Dock Architecture

**Last Updated:** November 23, 2025

This document explains CST.Avalonia's window and docking architecture, which is critical for understanding how to access active books, handle menu events, and work with both main and floating windows.

## Key Architectural Difference: Two Window Types

CST.Avalonia uses **two completely different window architectures** that must be handled separately:

### 1. Main Window (`SimpleTabbedWindow`)

**File:** `/src/CST.Avalonia/Views/SimpleTabbedWindow.cs`

**Architecture:**
- Uses `LayoutViewModel` as the window's DataContext
- Contains a `DockControl` whose DataContext is also the `LayoutViewModel`
- The `LayoutViewModel.Layout` property contains the root dock structure
- Access pattern: `window.DataContext as LayoutViewModel`

**Example - Finding Active Book:**
```csharp
// In SimpleTabbedWindow or when you have a reference to it
var layoutViewModel = this.DataContext as LayoutViewModel;
if (layoutViewModel?.Factory is CstDockFactory factory)
{
    var dockControl = this.FindDescendantOfType<DockControl>();
    if (dockControl?.DataContext is LayoutViewModel vm)
    {
        var documentDock = FindDocumentDockInLayout(vm.Layout);
        var activeBook = documentDock?.ActiveDockable as BookDisplayViewModel;
    }
}
```

### 2. Floating Windows (`CstHostWindow`)

**File:** `/src/CST.Avalonia/Services/CstHostWindow.cs`

**Architecture:**
- Implements `IHostWindow` interface from Dock.Avalonia
- Does NOT use `LayoutViewModel` as DataContext
- Contains a `DockControl` but its DataContext is **null**
- The window itself has a `Layout` property (from `IHostWindow`)
- Access pattern: `window as CstHostWindow`, then use `hostWindow.Layout`

**Example - Finding Active Book:**
```csharp
// When working with a floating window
if (window is CstHostWindow hostWindow)
{
    var documentDock = FindDocumentDockInLayout(hostWindow.Layout);
    var activeBook = documentDock?.ActiveDockable as BookDisplayViewModel;
}
```

## Critical Mistake to Avoid

❌ **DO NOT** assume all windows have the same structure:
```csharp
// This ONLY works in main window, NOT floating windows
var dockControl = window.FindDescendantOfType<DockControl>();
var layoutViewModel = dockControl.DataContext as LayoutViewModel; // NULL in floating windows!
```

✅ **DO** check the window type first:
```csharp
if (window is CstHostWindow hostWindow)
{
    // Use hostWindow.Layout directly
    var layout = hostWindow.Layout;
}
else if (window is SimpleTabbedWindow mainWindow)
{
    // Use LayoutViewModel from DataContext
    var layoutViewModel = mainWindow.DataContext as LayoutViewModel;
    var layout = layoutViewModel?.Layout;
}
```

## Dock Hierarchy Structure

Both window types use the same dock hierarchy once you have the root layout:

```
RootDock (IDock)
└── WindowLayout (IDock)
    └── MainDock (ProportionalDock)
        ├── LeftDock (ProportionalDock) - Contains panels
        │   ├── OpenBookPanel (ITool)
        │   └── SearchPanel (ITool)
        └── DocumentDock (DocumentDock) - Contains book tabs
            ├── WelcomeDocument (WelcomeViewModel)
            ├── BookDisplayViewModel (book 1)
            ├── BookDisplayViewModel (book 2)
            └── BookDisplayViewModel (book 3) ← ActiveDockable
```

**Key Points:**
- `DocumentDock.ActiveDockable` is the currently focused tab
- `DocumentDock.VisibleDockables` contains all open tabs
- `BookDisplayViewModel` implements `ReactiveDocument` and is added directly to DocumentDock
- The `ActiveDockable` can be a `WelcomeViewModel` or `BookDisplayViewModel`

## Finding the Active Book (Universal Pattern)

```csharp
private BookDisplayViewModel? FindActiveBook(Window window)
{
    IDock? layout = null;

    // Get the layout based on window type
    if (window is CstHostWindow hostWindow)
    {
        layout = hostWindow.Layout;
    }
    else if (window.DataContext is LayoutViewModel layoutViewModel)
    {
        layout = layoutViewModel.Layout;
    }

    if (layout == null)
        return null;

    // Find DocumentDock in the layout hierarchy
    var documentDock = FindDocumentDockInLayout(layout) as DocumentDock;

    // Get the active book
    return documentDock?.ActiveDockable as BookDisplayViewModel;
}

private IDock? FindDocumentDockInLayout(IDock? dock)
{
    if (dock is DocumentDock documentDock)
        return documentDock;

    if (dock?.VisibleDockables != null)
    {
        foreach (var dockable in dock.VisibleDockables)
        {
            if (dockable is IDock childDock)
            {
                var result = FindDocumentDockInLayout(childDock);
                if (result != null)
                    return result;
            }
        }
    }

    return null;
}
```

## Menu Handling: Main vs Floating Windows

### Main Window Menus

**Defined in:** `SimpleTabbedWindow.axaml` using `<NativeMenu.Menu>`

```xml
<NativeMenu.Menu>
    <NativeMenu>
        <NativeMenuItem Header="Tools">
            <NativeMenu>
                <NativeMenuItem Header="Go To..." Click="OnGoToMenuItemClick" Gesture="cmd+g" />
            </NativeMenu>
        </NativeMenuItem>
    </NativeMenu>
</NativeMenu.Menu>
```

**Event handler in:** `SimpleTabbedWindow.cs` code-behind

```csharp
private void OnGoToMenuItemClick(object? sender, EventArgs e)
{
    // Direct access since we're in the main window
    var layoutViewModel = this.DataContext as LayoutViewModel;
    // ... find active book and show dialog
}
```

### Floating Window Menus

**Defined in:** `CstHostWindow.cs` in `SetupViewMenu()` method

```csharp
private void SetupViewMenu()
{
    var nativeMenu = new NativeMenu();

    var toolsMenuItem = new NativeMenuItem
    {
        Header = "Tools",
        Menu = new NativeMenu()
    };

    var goToItem = new NativeMenuItem
    {
        Header = "Go To...",
        Gesture = KeyGesture.Parse("Cmd+G")
    };

    toolsMenuItem.Menu.Add(goToItem);
    nativeMenu.Add(toolsMenuItem);

    NativeMenu.SetMenu(this, nativeMenu);

    // Event handlers wired up later by App.SetupFloatingWindowMenu()
}
```

**Event wiring in:** `App.axaml.cs` in `SetupFloatingWindowMenu()`

This method is called when a floating window is created (from `CstDockFactory.cs` line 1708):

```csharp
public void SetupFloatingWindowMenu(Window window)
{
    var windowMenu = NativeMenu.GetMenu(window);
    foreach (var item in windowMenu)
    {
        if (item is NativeMenuItem toolsMenuItem && toolsMenuItem.Header == "Tools")
        {
            var toolsMenu = toolsMenuItem.Menu;
            foreach (var toolItem in toolsMenu)
            {
                if (toolItem is NativeMenuItem toolSubItem && toolSubItem.Header == "Go To...")
                {
                    toolSubItem.Click += (s, e) =>
                    {
                        OnGoToMenuItemClickFromFloatingWindow(window);
                    };
                }
            }
        }
    }
}

private void OnGoToMenuItemClickFromFloatingWindow(Window floatingWindow)
{
    // Must use CstHostWindow pattern
    if (floatingWindow is CstHostWindow hostWindow)
    {
        var layout = hostWindow.Layout;
        // ... find active book in this layout
    }
}
```

## Common Patterns and Solutions

### Pattern 1: Execute Action on Active Book

```csharp
// Works for both main and floating windows
public void ExecuteActionOnActiveBook(Window window, Action<BookDisplayViewModel> action)
{
    var activeBook = FindActiveBook(window); // Use universal pattern above
    if (activeBook != null)
    {
        action(activeBook);
    }
    else
    {
        Log.Warning("No active book found in window");
    }
}
```

### Pattern 2: Add Menu Item to Both Window Types

**Step 1:** Add to main window XAML (`SimpleTabbedWindow.axaml`)
```xml
<NativeMenuItem Header="My Feature" Click="OnMyFeatureClick" Gesture="cmd+f" />
```

**Step 2:** Add handler to main window code-behind (`SimpleTabbedWindow.cs`)
```csharp
private void OnMyFeatureClick(object? sender, EventArgs e)
{
    var activeBook = FindActiveBook(this);
    activeBook?.DoSomething();
}
```

**Step 3:** Add to floating window menu (`CstHostWindow.cs` in `SetupViewMenu()`)
```csharp
var myFeatureItem = new NativeMenuItem
{
    Header = "My Feature",
    Gesture = KeyGesture.Parse("Cmd+F")
};
toolsMenuItem.Menu.Add(myFeatureItem);
```

**Step 4:** Wire up event in `App.axaml.cs` in `SetupFloatingWindowMenu()`
```csharp
else if (toolSubItem.Header == "My Feature")
{
    toolSubItem.Click += (s, e) =>
    {
        OnMyFeatureClickFromFloatingWindow(window);
    };
}
```

**Step 5:** Add handler in `App.axaml.cs`
```csharp
private void OnMyFeatureClickFromFloatingWindow(Window window)
{
    var activeBook = FindActiveBook(window); // Universal pattern
    activeBook?.DoSomething();
}
```

### Pattern 3: Handling Float/Unfloat Events

When a book is floated:
- A new `CstHostWindow` is created
- The book's `BookDisplayViewModel` is moved to the new window's DocumentDock
- Event handlers must be re-subscribed (handled by `CstDockFactory`)

When a book is unfloated (redocked):
- The `BookDisplayViewModel` is moved back to the main window's DocumentDock
- The `CstHostWindow` is closed
- Main window event handlers should still work (they're persistent)

**Important:** If you need to maintain state or subscriptions across float/unfloat operations, subscribe to events on the `BookDisplayViewModel` itself, not on the window or dock structures.

## Debugging Tips

### Check What Window Type You Have

```csharp
Log.Information("Window type: {Type}", window.GetType().Name);
// SimpleTabbedWindow = main window
// CstHostWindow = floating window
```

### Check DockControl DataContext

```csharp
var dockControl = window.FindDescendantOfType<DockControl>();
Log.Information("DockControl found: {Found}", dockControl != null);
Log.Information("DockControl.DataContext type: {Type}",
    dockControl?.DataContext?.GetType().Name ?? "null");
// Main window: "LayoutViewModel"
// Floating window: "null"
```

### Check Layout Structure

```csharp
if (window is CstHostWindow hostWindow)
{
    Log.Information("HostWindow.Layout type: {Type}",
        hostWindow.Layout?.GetType().Name ?? "null");
}
else if (window.DataContext is LayoutViewModel layoutViewModel)
{
    Log.Information("LayoutViewModel.Layout type: {Type}",
        layoutViewModel.Layout?.GetType().Name ?? "null");
}
```

### Trace the Dock Hierarchy

```csharp
private void LogDockHierarchy(IDock? dock, int level = 0)
{
    if (dock == null) return;

    var indent = new string(' ', level * 2);
    Log.Information("{Indent}{Type} - ID: {Id}", indent, dock.GetType().Name, dock.Id);

    if (dock.VisibleDockables != null)
    {
        foreach (var dockable in dock.VisibleDockables)
        {
            Log.Information("{Indent}  - {Type}: {Title}", indent,
                dockable.GetType().Name, dockable.Title ?? "no title");

            if (dockable is IDock childDock)
            {
                LogDockHierarchy(childDock, level + 1);
            }
        }
    }
}
```

## When Floating Windows Were Introduced

The floating window feature was added in Beta 3 with the "Button-Based Float/Unfloat" implementation. Key commits:
- Initial implementation with manual lifecycle control
- CEF crash prevention when floating/docking
- Event subscription/resubscription patterns

## Related Files

**Main Window:**
- `/src/CST.Avalonia/Views/SimpleTabbedWindow.axaml` - Window definition
- `/src/CST.Avalonia/Views/SimpleTabbedWindow.cs` - Code-behind

**Floating Windows:**
- `/src/CST.Avalonia/Services/CstHostWindow.cs` - Floating window implementation
- `/src/CST.Avalonia/Services/CstDockFactory.cs` - Creates/manages both window types

**Layout & Dock:**
- `/src/CST.Avalonia/ViewModels/LayoutViewModel.cs` - Main window layout
- `/src/CST.Avalonia/ViewModels/BookDisplayViewModel.cs` - Book tab (ReactiveDocument)

**Application:**
- `/src/CST.Avalonia/App.axaml.cs` - Menu wiring for floating windows

## Summary: Quick Reference

| Aspect | Main Window | Floating Window |
|--------|-------------|-----------------|
| **Type** | `SimpleTabbedWindow` | `CstHostWindow` |
| **DataContext** | `LayoutViewModel` | `null` |
| **Layout Access** | `((LayoutViewModel)window.DataContext).Layout` | `((CstHostWindow)window).Layout` |
| **DockControl.DataContext** | `LayoutViewModel` | `null` |
| **Menu Definition** | XAML (`<NativeMenu.Menu>`) | Code (`SetupViewMenu()`) |
| **Menu Event Wiring** | Code-behind (`SimpleTabbedWindow.cs`) | `App.SetupFloatingWindowMenu()` |
| **Active Book Access** | Via `LayoutViewModel` | Via `CstHostWindow.Layout` |

**Golden Rule:** Always check window type first, then use the appropriate access pattern!
