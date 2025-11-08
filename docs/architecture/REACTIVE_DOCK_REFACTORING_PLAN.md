# Reactive Dock Refactoring Plan

**Purpose**: Migrate CST.Avalonia ViewModels from Context-based architecture to direct Document/Tool inheritance
**Date**: November 2025
**Status**: Planning Phase

## Current Architecture vs. Target Architecture

### Current: Two-Layer Architecture (Context Pattern)

```csharp
// CstDockFactory.cs - Current approach
public override RootDock CreateLayout()
{
    var searchViewModel = App.ServiceProvider.GetRequiredService<SearchViewModel>();

    // Dock Tool wraps the ViewModel in Context property
    var searchTool = new Tool
    {
        Id = "SearchTool",
        Title = "Search",
        Context = searchViewModel,  // ← ViewModel stored in Context
        CanPin = false,
        CanClose = false
    };

    // ...
}

// SearchViewModel.cs - Current
public class SearchViewModel : ViewModelBase  // ← Just ReactiveObject
{
    // Has ReactiveUI features but NO Dock features
    // No Id, Title, CanClose, etc.
}
```

**Problems with Current Approach:**
1. **Separation of Concerns**: Dock properties (Id, Title, CanClose) are separate from ViewModel logic
2. **No Direct Access**: ViewModel can't control its own docking behavior
3. **ControlRecycling Limitation**: `GetControlRecyclingId()` is on the Tool/Document wrapper, not the ViewModel
4. **State Management Complexity**: Two objects to serialize/deserialize (wrapper + context)
5. **Less Type Safety**: Context is `object?`, requires casting

### Target: Single-Layer Architecture (Direct Inheritance)

```csharp
// CstDockFactory.cs - After refactoring
public override RootDock CreateLayout()
{
    // ViewModel IS the Tool - no wrapper needed
    var searchViewModel = App.ServiceProvider.GetRequiredService<SearchViewModel>();
    searchViewModel.Id = "SearchTool";
    searchViewModel.Title = "Search";
    searchViewModel.CanPin = false;
    searchViewModel.CanClose = false;

    // Add directly to dock - no Tool wrapper
    var leftToolDock = new ToolDock
    {
        Id = "LeftToolDock",
        VisibleDockables = CreateList<IDockable>(searchViewModel)  // ← Direct
    };

    // ...
}

// SearchViewModel.cs - After refactoring
public class SearchViewModel : ReactiveTool  // ← Inherits from ReactiveTool
{
    // Has BOTH ReactiveUI AND Dock features
    // Can access/modify Id, Title, CanClose, etc.
    // Implements GetControlRecyclingId() for ControlRecycling support
}
```

**Benefits of Target Approach:**
1. **Unified Model**: ViewModel controls its own docking behavior
2. **Direct Property Access**: ViewModel can set its own Title, Id, CanClose dynamically
3. **ControlRecycling Compatible**: `GetControlRecyclingId()` works on the ViewModel itself
4. **Cleaner Serialization**: Single object to save/restore
5. **Type Safety**: No Context casting needed
6. **Matches Dock Samples**: Follows the pattern from Notepad sample

---

## ViewModels to Refactor

### Documents (extend ReactiveDocument)

#### 1. BookDisplayViewModel → ReactiveDocument

**Current:**
```csharp
public class BookDisplayViewModel : ViewModelBase
{
    private readonly Book _book;
    // No access to Dock properties
}
```

**After Refactoring:**
```csharp
public class BookDisplayViewModel : ReactiveDocument
{
    private readonly Book _book;

    public BookDisplayViewModel(Book book, ...)
    {
        _book = book;

        // Now can set Dock properties directly
        Id = $"Book_{book.Index}_{book.FileName}";  // Unique ID for ControlRecycling
        Title = book.Title;
        CanClose = true;   // Books can be closed
        CanFloat = true;   // Books can float to separate windows
        CanPin = false;    // Disable pinning

        // All existing ViewModel logic stays the same
        // ...
    }

    // Can dynamically update Title based on state
    public void UpdateTitle(string searchHighlight)
    {
        Title = $"{_book.Title} ({searchHighlight})";
    }
}
```

**Key Changes:**
- Extends `ReactiveDocument` instead of `ViewModelBase`
- Sets Dock properties in constructor
- Can dynamically update Title/CanClose/etc. based on state
- `GetControlRecyclingId()` returns unique book identifier

**Benefits:**
- ControlRecycling will cache Views by book ID (preserves scroll position!)
- Book can control its own close behavior
- Simplified factory code

#### 2. WelcomeViewModel → ReactiveDocument

**Current:**
```csharp
// Created in CstDockFactory
var welcomeDocument = new Document
{
    Id = "WelcomeDocument",
    Title = "Welcome",
    Context = new WelcomeViewModel(),
    CanClose = false
};
```

**After Refactoring:**
```csharp
public class WelcomeViewModel : ReactiveDocument
{
    public WelcomeViewModel()
    {
        Id = "WelcomeDocument";
        Title = "Welcome";
        CanClose = false;  // Prevent closing
        CanFloat = false;  // Prevent floating
        CanPin = false;    // Prevent pinning
    }

    // ... existing ViewModel logic
}
```

**Benefits:**
- Self-contained configuration
- No separate Document wrapper needed

### Tools (extend ReactiveTool)

#### 3. SearchViewModel → ReactiveTool

**Current:**
```csharp
public class SearchViewModel : ViewModelBase, IActivatableViewModel
{
    // No access to Dock properties
}
```

**After Refactoring:**
```csharp
public class SearchViewModel : ReactiveTool, IActivatableViewModel
{
    public SearchViewModel(...)
    {
        Id = "SearchTool";
        Title = "Search";
        CanPin = false;    // Prevent pinning (vertical text issues)
        CanClose = false;  // Keep search panel always available
        CanFloat = true;   // Allow floating to separate window
        CanDrag = true;    // Allow dragging

        // All existing ViewModel logic stays the same
        // ...
    }

    // Can dynamically update Title to show search status
    private void UpdateSearchStatus(int resultCount)
    {
        Title = resultCount > 0
            ? $"Search ({resultCount} results)"
            : "Search";
    }
}
```

**Benefits:**
- Search can show result count in tab title
- Self-configures docking behavior
- No wrapper Tool needed

#### 4. OpenBookDialogViewModel → ReactiveTool

**Current:**
```csharp
public class OpenBookDialogViewModel : ViewModelBase
{
    // Wrapped in Tool with Context
}
```

**After Refactoring:**
```csharp
public class OpenBookDialogViewModel : ReactiveTool
{
    public OpenBookDialogViewModel(...)
    {
        Id = "OpenBookTool";
        Title = "Select a Book";
        CanPin = false;
        CanClose = false;
        CanFloat = true;
        CanDrag = true;

        // ... existing ViewModel logic
    }
}
```

---

## Factory Refactoring

### CstDockFactory.cs Changes

#### Before: Creating Tools/Documents with Context

```csharp
public override RootDock CreateLayout()
{
    var openBookViewModel = App.ServiceProvider.GetRequiredService<OpenBookDialogViewModel>();
    var searchViewModel = App.ServiceProvider.GetRequiredService<SearchViewModel>();

    var openBookTool = new Tool
    {
        Id = "OpenBookTool",
        Title = "Select a Book",
        Context = openBookViewModel,
        CanPin = false,
        CanClose = false
    };

    var searchTool = new Tool
    {
        Id = "SearchTool",
        Title = "Search",
        Context = searchViewModel,
        CanPin = false,
        CanClose = false
    };

    var leftToolDock = new ToolDock
    {
        VisibleDockables = CreateList<IDockable>(openBookTool, searchTool)
    };

    // ...
}
```

#### After: Direct ViewModel Usage

```csharp
public override RootDock CreateLayout()
{
    // Get ViewModels - they ARE Tools/Documents now
    var openBookTool = App.ServiceProvider.GetRequiredService<OpenBookDialogViewModel>();
    var searchTool = App.ServiceProvider.GetRequiredService<SearchViewModel>();

    // No wrapper needed - add ViewModels directly
    var leftToolDock = new ToolDock
    {
        Id = "LeftToolDock",
        ActiveDockable = openBookTool,
        VisibleDockables = CreateList<IDockable>(openBookTool, searchTool),
        Alignment = Alignment.Left
    };

    // ...
}
```

**Lines of Code Saved:** ~50+ lines per factory method

#### OpenBook Method Changes

**Before:**
```csharp
public void OpenBook(Book book, ...)
{
    var bookViewModel = new BookDisplayViewModel(book, ...);

    var bookDocument = new Document
    {
        Id = $"Book_{book.Index}",
        Title = book.Title,
        Context = bookViewModel,  // ← Two objects
        CanClose = true
    };

    documentDock.VisibleDockables?.Add(bookDocument);
}
```

**After:**
```csharp
public void OpenBook(Book book, ...)
{
    var bookViewModel = new BookDisplayViewModel(book, ...);
    // ViewModel already configured itself in constructor

    documentDock.VisibleDockables?.Add(bookViewModel);  // ← One object
}
```

---

## ControlRecycling Integration

With ReactiveDocument/ReactiveTool, ControlRecycling works seamlessly:

### 1. Enable ControlRecycling in App.axaml

```xml
<!-- App.axaml -->
<Application.Resources>
  <ControlRecycling x:Key="ControlRecyclingKey" TryToUseIdAsKey="True" />
</Application.Resources>

<Application.Styles>
  <Style Selector="DockControl">
    <Setter Property="(ControlRecyclingDataTemplate.ControlRecycling)"
            Value="{StaticResource ControlRecyclingKey}" />
  </Style>
</Application.Styles>
```

### 2. Package Reference

```xml
<!-- CST.Avalonia.csproj -->
<PackageReference Include="Dock.Controls.Recycling" Version="11.3.0.15" />
```

### 3. ViewModels Already Support It

```csharp
// ReactiveDocument.cs (already implemented)
public class ReactiveDocument : ReactiveObject, IDocument
{
    private string _id = string.Empty;

    public string Id
    {
        get => _id;
        set => this.RaiseAndSetIfChanged(ref _id, value);
    }

    // This method is inherited and works automatically
    public string? GetControlRecyclingId() => _id;
}
```

**What This Means:**

When a user:
1. Opens "Dīgha Nikāya DN 1" → Creates BookDisplayView, caches it with ID "Book_0_dn1"
2. Closes the tab → View stays in cache
3. Opens "Majjhima Nikāya MN 1" → Creates BookDisplayView, caches it with ID "Book_20_mn1"
4. Reopens "Dīgha Nikāya DN 1" → **Retrieves cached view from step 1**
   - **Scroll position preserved**
   - **Search highlights preserved**
   - **WebView state preserved**

This is HUGE for user experience - no more losing position when switching books!

---

## Migration Strategy

### Phase 1: Create Base Classes ✅ (COMPLETE)
- [x] Create `ReactiveDocument` base class
- [x] Create `ReactiveTool` base class
- [x] Document usage in `REACTIVE_DOCK_VIEWMODELS.md`

### Phase 2: Add ControlRecycling Package
```bash
cd src/CST.Avalonia
dotnet add package Dock.Controls.Recycling --version 11.3.0.15
```

Update `App.axaml`:
```xml
<Application.Resources>
  <ControlRecycling x:Key="ControlRecyclingKey" TryToUseIdAsKey="True" />
</Application.Resources>

<Application.Styles>
  <Style Selector="DockControl">
    <Setter Property="(ControlRecyclingDataTemplate.ControlRecycling)"
            Value="{StaticResource ControlRecyclingKey}" />
  </Style>
</Application.Styles>
```

### Phase 3: Refactor ViewModels (One at a Time)

**Order of Migration** (least risky to most complex):

1. **WelcomeViewModel** (simplest - no dependencies)
   - Change: `ViewModelBase` → `ReactiveDocument`
   - Add: Constructor initialization of Dock properties
   - Test: Welcome page still displays

2. **OpenBookDialogViewModel** (isolated functionality)
   - Change: `ViewModelBase` → `ReactiveTool`
   - Add: Constructor initialization
   - Update: Factory to remove Tool wrapper
   - Test: Book tree displays, double-click opens books

3. **SearchViewModel** (moderate complexity)
   - Change: `ViewModelBase` → `ReactiveTool`
   - Keep: `IActivatableViewModel` interface
   - Add: Constructor initialization
   - Update: Factory to remove Tool wrapper
   - Test: Search works, results display, navigation works

4. **BookDisplayViewModel** (most complex - core functionality)
   - Change: `ViewModelBase` → `ReactiveDocument`
   - Add: Constructor initialization with unique IDs
   - Update: Factory OpenBook method
   - Test: Books open, WebView renders, search highlights work
   - **Verify**: ControlRecycling preserves scroll position!

### Phase 4: Update CstDockFactory

For each refactored ViewModel:
1. Remove `new Tool { Context = viewModel }` wrapper
2. Add ViewModel directly to VisibleDockables
3. Remove Context-related code

**Before/After Example:**
```csharp
// BEFORE
var vm = new SearchViewModel();
var tool = new Tool { Id = "Search", Context = vm };
dock.Add(tool);

// AFTER
var vm = new SearchViewModel();  // Sets Id/Title in constructor
dock.Add(vm);
```

### Phase 5: Update ApplicationStateService

State serialization needs to change:

**Current:**
```json
{
  "openBooks": [
    {
      "documentId": "Book_1",  // Wrapper Document ID
      "bookIndex": 1,
      "searchTerms": [...],
      // ...
    }
  ]
}
```

**After Refactoring:**
```json
{
  "openBooks": [
    {
      "viewModelId": "Book_1_dn1",  // ViewModel's own ID
      "bookIndex": 1,
      "searchTerms": [...],
      // ...
    }
  ]
}
```

Key changes:
- Look for ViewModels directly in VisibleDockables
- No more Context property extraction
- Use ViewModel's Id property directly

### Phase 6: Testing Checklist

For each refactored ViewModel:

- [ ] Compiles without errors
- [ ] ViewModel displays in correct dock location
- [ ] All existing functionality works (commands, bindings, events)
- [ ] Tab title updates correctly
- [ ] Close behavior works as expected
- [ ] Float/dock operations work
- [ ] **ControlRecycling works** (reopen tab preserves state)
- [ ] State saves correctly
- [ ] State restores correctly on app restart
- [ ] No regressions in other features

---

## Breaking Changes & Compatibility

### ViewModelBase Removal

Some ViewModels might rely on `ViewModelBase`:

```csharp
// Check if any code depends on this
public class ViewModelBase : ReactiveObject { }
```

**Solution**: ReactiveDocument/ReactiveTool already extend ReactiveObject, so no functionality is lost.

### Context Property Access

Any code that accesses `Context`:

```csharp
// Old pattern - will break
if (dockable is Document doc && doc.Context is BookDisplayViewModel vm)
{
    // ...
}
```

**Replace with:**
```csharp
// New pattern - direct cast
if (dockable is BookDisplayViewModel vm)
{
    // ...
}
```

**Search & Replace Strategy:**
```bash
# Find all Context accesses
rg "\.Context\s+is\s+" src/CST.Avalonia
rg "doc\.Context" src/CST.Avalonia
rg "tool\.Context" src/CST.Avalonia

# Review and replace each occurrence
```

---

## Expected Outcomes

### Code Reduction
- **Estimated LOC Removed**: 200-300 lines
  - Tool/Document wrapper creation: ~100 lines
  - Context property casting: ~50 lines
  - Duplicate property assignments: ~50-100 lines

### Performance Improvements
- **ControlRecycling**: View reuse eliminates re-creation overhead
- **Memory**: Fewer wrapper objects (Document + Context → just ViewModel)
- **State Management**: Single object serialization instead of dual

### User Experience Improvements
- **Preserved Scroll Position**: Books remember where you were
- **Preserved Search Highlights**: Highlights survive tab switches
- **Preserved WebView State**: No flicker/reload when switching tabs
- **Faster Tab Switching**: Cached views render instantly

### Code Quality Improvements
- **Type Safety**: No more Context casting
- **Cleaner Architecture**: Single-responsibility ViewModels
- **Easier Testing**: ViewModels are complete units
- **Better Intellisense**: Direct property access

---

## Risk Assessment

### Low Risk
- WelcomeViewModel refactoring (isolated, simple)
- OpenBookDialogViewModel refactoring (well-defined interface)
- Adding ControlRecycling package (non-breaking addition)

### Medium Risk
- SearchViewModel refactoring (complex ViewModel, but well-tested)
- Factory method updates (straightforward but touches many locations)

### High Risk
- BookDisplayViewModel refactoring (most complex ViewModel)
- ApplicationStateService updates (serialization changes)

**Mitigation Strategy:**
1. Refactor one ViewModel at a time
2. Test thoroughly before moving to next
3. Keep git commits small and atomic
4. Use feature branch for entire refactoring
5. Have rollback plan (git revert) if issues arise

---

## Success Metrics

After refactoring, verify:

1. **Functionality Preservation**
   - [ ] All 217 books open correctly
   - [ ] Search works with all modes (exact, wildcard, regex, phrase, proximity)
   - [ ] Navigation works (next/prev hit, chapter list, linked books)
   - [ ] Script switching works
   - [ ] State save/restore works

2. **ControlRecycling Verification**
   - [ ] Open book, scroll to middle, close tab
   - [ ] Open different book
   - [ ] Reopen first book → **scroll position preserved**
   - [ ] Search results → close tab → reopen → **highlights preserved**

3. **Performance Metrics**
   - [ ] Tab switch time < 100ms (instant with cached views)
   - [ ] Memory usage stable (no leaks from wrapper objects)
   - [ ] State save/restore time unchanged or improved

4. **Code Quality**
   - [ ] No `Context` property access remaining
   - [ ] All `Tool`/`Document` wrappers removed from factory
   - [ ] Code compiles with 0 warnings (existing warnings OK)

---

## Next Steps

1. **Add ControlRecycling Package**
   ```bash
   dotnet add package Dock.Controls.Recycling --version 11.3.0.15
   ```

2. **Update App.axaml** with ControlRecycling configuration

3. **Create Feature Branch**
   ```bash
   git checkout -b refactor/reactive-dock-viewmodels
   ```

4. **Start with WelcomeViewModel** (simplest case)
   - Change base class
   - Update factory
   - Test thoroughly
   - Commit

5. **Move to OpenBookDialogViewModel**
   - Follow same pattern
   - Verify book tree works
   - Commit

6. **Continue with SearchViewModel and BookDisplayViewModel**

7. **Update ApplicationStateService** for new architecture

8. **Full regression testing**

9. **Merge to main**

---

## References

- [Reactive Dock ViewModels Documentation](REACTIVE_DOCK_VIEWMODELS.md)
- [Dock Notepad Patterns Analysis](DOCK_NOTEPAD_PATTERNS.md)
- [Dock ControlRecycling Guide](https://github.com/wieslawsoltes/Dock/blob/master/docs/dock-control-recycling.md)
- Notepad Sample: `/Users/fsnow/github/wieslawsoltes/Dock/samples/Notepad/`
