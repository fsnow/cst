# Button-Based Float Approach: Solving CEF + ControlRecycling Crash

**Date:** November 10, 2025
**Branch:** `experimental/cef-controlrecycling-workarounds`
**Status:** 🧪 **Research / Proof-of-Concept**
**Related:** [CONTROL_RECYCLING_CEF_CRASH.md](../implementation/CONTROL_RECYCLING_CEF_CRASH.md)

## Executive Summary

**The Idea:** Hybrid approach that preserves both instant tab switching AND floating windows by separating drag operations from programmatic float operations:

- **Tool panels** → Full drag freedom + ControlRecycling (no CEF, safe)
- **Document tabs** → Drag to reorder only + ControlRecycling (same window, potentially safe)
- **Document floating** → Buttons that bypass ControlRecycling (manual lifecycle, no crash)

**Why This Could Work:**
1. ControlRecycling is only unsafe when **CEF views** detach/reattach across windows
2. Tool panels (no CEF) can safely use ControlRecycling for all operations
3. Tab reordering within same DocumentDock may not trigger window-level detachment
4. Button-triggered operations give us full control over WebView lifecycle
5. We can manually destroy/recreate WebView for float operations

**Key Advantage:** This is the **only free solution** that has the potential to preserve both features.

---

## The Problem (Quick Recap)

From [CONTROL_RECYCLING_CEF_CRASH.md](../implementation/CONTROL_RECYCLING_CEF_CRASH.md):

**Current State:**
- ControlRecycling enabled → ✅ Instant tab switching, ❌ Floating crashes
- ControlRecycling disabled → ✅ Floating works, ❌ Visible scrolling on tab switch

**Root Cause:**
- CEF cannot survive being reparented between windows on macOS
- When dragging to float, ControlRecycling tries to move existing CEF-based view to new window
- CEF detects detachment and aggressively disposes browser process → crash
- **Note:** This is specific to CEF/WebView controls - regular Avalonia controls work fine with ControlRecycling

**Why Other Solutions Don't Work:**
- Avalonia NativeWebView with `BeginReparenting()` → ❌ Requires commercial license
- Disable ControlRecycling entirely → ❌ Loses instant tab switching
- Manual WebView lifecycle everywhere → ❌ Complex, fragile, high maintenance burden

---

## The Solution: Hybrid Drag + Button Approach

### Core Concept

**Separate two different operations:**

1. **Drag-to-reorder** (within same window) → Uses ControlRecycling
   - Dragging tabs left/right to reorder
   - No window detachment, view stays in same DocumentDock
   - ControlRecycling keeps view alive → instant switching preserved

2. **Float/unfloat** (across windows) → Bypasses ControlRecycling
   - Triggered by explicit buttons (not drag)
   - Manually destroy old WebView before operation
   - Create new WebView in target window after operation
   - No ControlRecycling involvement → no crash

### Architecture Overview

```
┌─────────────────────────────────────────────────────────────┐
│ Main Window                                                 │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ DocumentDock (ControlRecycling ENABLED)                 │ │
│ │ ┌──────────┐ ┌──────────┐ ┌──────────┐                 │ │
│ │ │ Book 1   │ │ Book 2   │ │ Book 3   │                 │ │
│ │ │ [Float]🔼│ │ [Float]🔼│ │ [Float]🔼│                 │ │
│ │ └──────────┘ └──────────┘ └──────────┘                 │ │
│ │     ↕ Drag to reorder (ControlRecycling safe)          │ │
│ └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                           ↑ [Float]🔼 button
                           │ (Manual WebView lifecycle)
                           ↓
┌─────────────────────────────────────────────────────────────┐
│ Floating Window                                             │
│ ┌─────────────────────────────────────────────────────────┐ │
│ │ DocumentDock (ControlRecycling ENABLED)                 │ │
│ │ ┌──────────┐                                            │ │
│ │ │ Book 2   │                                            │ │
│ │ │ [Dock]🔽 │                                            │ │
│ │ └──────────┘                                            │ │
│ └─────────────────────────────────────────────────────────┘ │
└─────────────────────────────────────────────────────────────┘
                           ↑ [Dock]🔽 button
                           │ (Manual WebView lifecycle)
                           └─ Back to main window
```

### Technical Implementation

#### 1. Prevent Drag-to-Float for Documents

**File:** `Services/CstDockFactory.cs`

When creating BookDisplayViewModel:
```csharp
public void OpenBook(Book book, string? anchor = null, Script? bookScript = null)
{
    var bookDisplayViewModel = new BookDisplayViewModel(
        book,
        _applicationStateService,
        /* ... other params ... */
    );

    // ✅ Allow dragging (for tab reordering)
    bookDisplayViewModel.CanDrag = true;

    // ❌ Prevent drag-to-float (will use buttons instead)
    bookDisplayViewModel.CanFloat = false;

    // Add to document dock
    AddDocumentToLayout(bookDisplayViewModel);
}
```

**What this does:**
- `CanDrag = true` → User can drag tabs to reorder them
- `CanFloat = false` → Dragging won't create floating window
- Existing `FloatDockable()` check (line 1161) respects this and rejects drag-to-float

#### 2. Add Float/Unfloat Buttons

**File:** `Views/BookDisplayView.axaml`

Add buttons to book toolbar:
```xml
<UserControl>
    <DockPanel>
        <!-- Existing toolbar -->
        <StackPanel DockPanel.Dock="Top" Orientation="Horizontal">
            <!-- Existing buttons: Attha, Tika, etc. -->

            <!-- NEW: Float/Unfloat buttons -->
            <Button Name="FloatButton"
                    Content="Float Window"
                    Command="{Binding FloatWindowCommand}"
                    ToolTip.Tip="Open this book in a separate window"
                    IsVisible="{Binding !IsFloating}" />

            <Button Name="UnfloatButton"
                    Content="Dock to Main"
                    Command="{Binding UnfloatWindowCommand}"
                    ToolTip.Tip="Move this book back to main window"
                    IsVisible="{Binding IsFloating}" />
        </StackPanel>

        <!-- WebView content -->
        <wv:WebView x:Name="webView" ... />
    </DockPanel>
</UserControl>
```

#### 3. Implement Manual WebView Lifecycle

**File:** `ViewModels/BookDisplayViewModel.cs`

Add commands and state tracking:
```csharp
public class BookDisplayViewModel : ReactiveDocument
{
    private bool _isFloating;
    private WebViewState? _savedWebViewState;

    public bool IsFloating
    {
        get => _isFloating;
        set => this.RaiseAndSetIfChanged(ref _isFloating, value);
    }

    public ReactiveCommand<Unit, Unit> FloatWindowCommand { get; }
    public ReactiveCommand<Unit, Unit> UnfloatWindowCommand { get; }

    public BookDisplayViewModel(/* ... */)
    {
        FloatWindowCommand = ReactiveCommand.Create(FloatWindow);
        UnfloatWindowCommand = ReactiveCommand.Create(UnfloatWindow);
    }

    private void FloatWindow()
    {
        // 1. Save WebView state before destroying
        _savedWebViewState = new WebViewState
        {
            HtmlContent = _currentHtmlContent,
            ScrollPosition = _currentScrollPosition,
            SearchHighlights = _searchHighlights?.ToList(),
            BookScript = BookScript
        };

        // 2. Signal to View to dispose WebView
        WebViewLifecycleOperation = WebViewOperation.PrepareForFloat;

        // 3. Request factory to float this dockable
        //    (Factory will be injected or accessed via service)
        _dockFactory.FloatDockableWithoutRecycling(this);

        // 4. After float, signal to recreate WebView
        WebViewLifecycleOperation = WebViewOperation.RestoreAfterFloat;

        // 5. Restore state
        RestoreWebViewState(_savedWebViewState);

        // 6. Update UI
        IsFloating = true;
    }

    private void UnfloatWindow()
    {
        // Similar process but moving back to main window
        _savedWebViewState = new WebViewState { /* ... */ };
        WebViewLifecycleOperation = WebViewOperation.PrepareForUnfloat;
        _dockFactory.UnfloatDockableWithoutRecycling(this);
        WebViewLifecycleOperation = WebViewOperation.RestoreAfterUnfloat;
        RestoreWebViewState(_savedWebViewState);
        IsFloating = false;
    }
}

public class WebViewState
{
    public string? HtmlContent { get; set; }
    public int ScrollPosition { get; set; }
    public List<SearchHighlight>? SearchHighlights { get; set; }
    public Script BookScript { get; set; }
}

public enum WebViewOperation
{
    None,
    PrepareForFloat,
    RestoreAfterFloat,
    PrepareForUnfloat,
    RestoreAfterUnfloat
}
```

#### 4. Handle WebView Lifecycle in View

**File:** `Views/BookDisplayView.axaml.cs`

Respond to lifecycle signals:
```csharp
public partial class BookDisplayView : UserControl
{
    private IDisposable? _lifecycleSubscription;

    public BookDisplayView()
    {
        InitializeComponent();

        this.WhenActivated(disposables =>
        {
            // Subscribe to lifecycle operations
            _lifecycleSubscription = ViewModel
                .WhenAnyValue(vm => vm.WebViewLifecycleOperation)
                .Subscribe(operation =>
                {
                    switch (operation)
                    {
                        case WebViewOperation.PrepareForFloat:
                        case WebViewOperation.PrepareForUnfloat:
                            DisposeWebView();
                            break;

                        case WebViewOperation.RestoreAfterFloat:
                        case WebViewOperation.RestoreAfterUnfloat:
                            RecreateWebView();
                            break;
                    }
                })
                .DisposeWith(disposables);
        });
    }

    private void DisposeWebView()
    {
        if (_webView != null)
        {
            _logger.Information("Disposing WebView for window operation: {BookId}", ViewModel?.Book.Id);

            // Unhook events
            _webView.Navigated -= OnNavigated;
            _webView.TitleChanged -= OnTitleChanged;

            // Dispose CEF browser
            _webView.Dispose();
            _webView = null;
            _isBrowserInitialized = false;
        }
    }

    private void RecreateWebView()
    {
        _logger.Information("Recreating WebView after window operation: {BookId}", ViewModel?.Book.Id);

        // Create new WebView instance
        _webView = new WebViewControl.WebView
        {
            IsVisible = true,
            Focusable = true,
            ZIndex = 1
        };

        // Re-attach events
        _webView.Navigated += OnNavigated;
        _webView.TitleChanged += OnTitleChanged;

        // Add to visual tree
        // (Implementation depends on how BookDisplayView is structured)
        _webViewContainer.Child = _webView;

        _isBrowserInitialized = false;
    }
}
```

#### 5. Factory Methods for Non-Recycling Float

**File:** `Services/CstDockFactory.cs`

Add methods that bypass ControlRecycling:
```csharp
public void FloatDockableWithoutRecycling(BookDisplayViewModel bookVm)
{
    _logger.Information("FloatDockableWithoutRecycling called for: {BookId}", bookVm.Book.Id);

    // At this point, ViewModel has already disposed its WebView
    // We can safely use the standard float operation

    // Temporarily mark as can float (for this operation only)
    var originalCanFloat = bookVm.CanFloat;
    bookVm.CanFloat = true;

    try
    {
        // Call standard float (will create new window)
        FloatDockable(bookVm);
    }
    finally
    {
        // Restore original setting
        bookVm.CanFloat = originalCanFloat;
    }

    // ViewModel will now recreate WebView in new window context
}

public void UnfloatDockableWithoutRecycling(BookDisplayViewModel bookVm)
{
    _logger.Information("UnfloatDockableWithoutRecycling called for: {BookId}", bookVm.Book.Id);

    // Find main document dock
    var mainDocDock = FindDocumentDock();
    if (mainDocDock == null)
    {
        _logger.Error("Cannot unfloat - main document dock not found");
        return;
    }

    // Get current floating window
    var floatingWindow = FindFloatingWindowForDockable(bookVm);

    // Move dockable back to main window
    AddDockable(mainDocDock, bookVm);

    // Set as active
    SetActiveDockable(bookVm);
    SetFocusedDockable(mainDocDock, bookVm);

    // Close floating window if now empty
    if (floatingWindow != null)
    {
        CheckForEmptyFloatingWindows();
    }

    // ViewModel will now recreate WebView in main window context
}

private CstHostWindow? FindFloatingWindowForDockable(IDockable dockable)
{
    foreach (var hostWindow in HostWindows.OfType<CstHostWindow>())
    {
        if (hostWindow.Layout is IDock dock)
        {
            if (ContainsDockable(dock, dockable))
            {
                return hostWindow;
            }
        }
    }
    return null;
}

private bool ContainsDockable(IDock dock, IDockable target)
{
    return dock.VisibleDockables?.Any(d => d.Id == target.Id) == true;
}
```

#### 6. Selective ControlRecycling (Optional Enhancement)

**File:** `App.axaml`

Keep ControlRecycling enabled globally - it's only problematic for CEF views crossing windows:

```xml
<Application.Resources>
    <!-- ControlRecycling still enabled globally -->
    <ControlRecycling x:Key="ControlRecyclingKey" />
</Application.Resources>

<Style Selector="DockControl">
    <!-- ControlRecycling applied to all docks -->
    <Setter Property="(ControlRecyclingDataTemplate.ControlRecycling)"
            Value="{StaticResource ControlRecyclingKey}" />
</Style>

<!--
Note: ControlRecycling will attempt to reuse views, BUT:
- Document drag-to-float is blocked by CanFloat = false
  (Prevents CEF views from crossing window boundaries via drag)
- Document button-float manually destroys/recreates WebView
  (Bypasses ControlRecycling entirely for window-crossing operations)
- Tool panels have no CEF, so ControlRecycling is 100% safe for them
  (They can be dragged, floated, docked freely - no CEF lifecycle issues)
- Tab reordering within same DocumentDock may be safe (needs testing)
  (CEF views stay in same window, may not trigger detachment)
-->
```

---

## Implementation Plan

### Phase 1: Disable Drag-to-Float (1-2 hours)

**Goal:** Prevent crash by disabling drag-to-float for documents

1. ✅ Modify `CstDockFactory.OpenBook()`:
   - Set `bookDisplayViewModel.CanFloat = false`
   - Keep `bookDisplayViewModel.CanDrag = true`

2. ✅ Test:
   - Try dragging book tab to float → Should be rejected
   - Try dragging book tab to reorder → Should work
   - Keep ControlRecycling enabled

**Deliverable:** Books cannot be floated by drag, but can still be reordered

### Phase 2: Test Tab Reordering with ControlRecycling (2-4 hours)

**Critical Unknown:** Does tab reordering within same DocumentDock trigger detach/reattach?

1. ✅ Add extensive logging to `BookDisplayView.axaml.cs`:
   - `OnAttachedToVisualTree`
   - `OnDetachedFromVisualTree`
   - Track window parent changes

2. ✅ Test scenarios:
   - Open 3 books
   - Drag middle tab to left position
   - Drag left tab to right position
   - Switch between tabs rapidly
   - Monitor logs for detach/reattach events

3. ✅ Analyze results:
   - **If NO detach/reattach** → ControlRecycling is safe for tab reordering! ✅
   - **If detach/reattach occurs** → Need to disable ControlRecycling for documents ❌

**Deliverable:** Confirmation whether ControlRecycling can be used for document tabs

### Phase 3: Add Float/Unfloat Buttons (4-6 hours)

**Goal:** Add UI for floating operations

1. ✅ Add buttons to `BookDisplayView.axaml`:
   - Float button (visible when in main window)
   - Unfloat/Dock button (visible when in floating window)

2. ✅ Add commands to `BookDisplayViewModel`:
   - `FloatWindowCommand`
   - `UnfloatWindowCommand`
   - `IsFloating` property

3. ✅ Add state tracking:
   - Detect when document is in floating window vs main window
   - Update button visibility based on state

**Deliverable:** UI buttons that trigger commands (not yet functional)

### Phase 4: Implement Manual WebView Lifecycle (8-12 hours)

**Goal:** Destroy/recreate WebView for float operations

1. ✅ Add lifecycle operations to `BookDisplayViewModel`:
   - `WebViewLifecycleOperation` property
   - `WebViewState` class for saving state
   - Float/unfloat command implementation

2. ✅ Implement disposal in `BookDisplayView.axaml.cs`:
   - `DisposeWebView()` method
   - Unhook events, dispose CEF browser

3. ✅ Implement recreation in `BookDisplayView.axaml.cs`:
   - `RecreateWebView()` method
   - Create new WebView instance programmatically
   - Re-attach to visual tree

4. ✅ Handle state restoration:
   - Save HTML content, scroll position, highlights
   - Restore after WebView recreation

**Challenge:** XAML-declared WebView vs programmatic creation
- May need to use `ContentControl` or `ContentPresenter` with dynamic content
- Or move WebView creation entirely to code-behind

**Deliverable:** WebView can be destroyed and recreated without crash

### Phase 5: Factory Integration (4-6 hours)

**Goal:** Connect buttons to actual float operations

1. ✅ Add `FloatDockableWithoutRecycling()` to `CstDockFactory`
2. ✅ Add `UnfloatDockableWithoutRecycling()` to `CstDockFactory`
3. ✅ Add helper methods:
   - `FindFloatingWindowForDockable()`
   - `ContainsDockable()`

4. ✅ Inject factory into `BookDisplayViewModel` (or access via service)

**Deliverable:** Buttons trigger actual float/unfloat with manual WebView lifecycle

### Phase 6: Testing & Refinement (8-12 hours)

**Goal:** Validate all features work correctly

**Test Scenarios:**
1. Tab reordering with ControlRecycling
2. Float book to new window
3. Switch tabs in main window (instant switching)
4. Switch tabs in floating window (instant switching)
5. Unfloat book back to main window
6. Multiple floating windows
7. Close floating window
8. Search highlights survive float/unfloat
9. Scroll position survives float/unfloat
10. Script changes survive float/unfloat
11. Session restore with floating windows

**Edge Cases:**
- Float the active tab
- Float all books
- Float then immediately unfloat
- Close main window with floating windows open
- Application shutdown with floating windows

**Deliverable:** Stable, tested implementation

---

## Testing Strategy

### Critical Tests

#### 1. Tab Reordering Safety (CRITICAL - Determines if approach is viable)

```bash
# Test: Does tab reordering trigger detach/reattach?

1. Enable ControlRecycling
2. Set CanFloat = false on documents
3. Open 3 books
4. Add logging to BookDisplayView lifecycle events
5. Drag tab positions multiple times
6. Check logs for:
   - OnDetachedFromVisualTree events
   - Window parent changes
   - CEF disposal attempts

Expected: NO detachment events during reordering
If detachment occurs: Approach needs modification
```

#### 2. Manual Float Without Crash

```bash
# Test: Can we float after manual WebView disposal?

1. Open book with WebView
2. Click Float button
3. WebView disposes BEFORE float operation
4. Float operation completes
5. WebView recreates in new window
6. Book content displays correctly

Expected: No crash, book visible in floating window
```

#### 3. State Restoration After Float

```bash
# Test: Does book state survive float/unfloat?

1. Open book, scroll to middle, perform search
2. Float window
3. Verify: scroll position, search highlights, script
4. Unfloat window
5. Verify: all state still intact

Expected: Perfect state preservation
```

### Regression Tests

All existing features must continue working:

- ✅ Book display (all 14 scripts)
- ✅ Search with highlighting
- ✅ Search navigation (First/Prev/Next/Last)
- ✅ Script switching
- ✅ Dark mode
- ✅ Session restoration
- ✅ Attha/Tika linked books
- ✅ Tool panel docking (OpenBook, Search)

---

## Risks and Open Questions

### 🔴 High Risk: Tab Reordering May Trigger Detachment

**Question:** Does dragging tabs to reorder within same DocumentDock cause detach/reattach?

**Why it matters:**
- If YES → ControlRecycling unsafe for CEF views even during reordering → Approach fails to preserve instant switching
- If NO → ControlRecycling is safe for CEF tab reordering (same window) → Approach succeeds!

**Note:** Even if reordering triggers detachment, tool panels (no CEF) can still safely use ControlRecycling for all operations.

**How to verify:** Phase 2 testing with lifecycle logging

**Mitigation if YES:**
- Fall back to disabling ControlRecycling entirely
- Still get benefit of button-based float (no crash)
- Lose instant tab switching (original trade-off)

### 🟡 Medium Risk: Programmatic WebView Creation Complexity

**Challenge:** XAML-declared WebView is hard to replace at runtime

**Current structure:**
```xml
<wv:WebView x:Name="webView" ... />
```

**Potential solutions:**
1. Use `ContentControl` with dynamic content:
   ```xml
   <ContentControl x:Name="webViewContainer" />
   ```
   Then set `webViewContainer.Content = new WebView()` in code

2. Keep XAML WebView but hide/show:
   - Create new WebView programmatically when needed
   - Swap visibility between old and new
   - Dispose old after swap

3. Move entirely to code-behind:
   - Remove XAML WebView declaration
   - Create WebView in constructor
   - Add to visual tree programmatically

**Recommendation:** Start with ContentControl approach (most flexible)

### 🟡 Medium Risk: Visual Flicker During Float

**Issue:** User will briefly see empty space during WebView recreation

**Sequence:**
1. Old WebView disappears
2. Float operation completes
3. New WebView appears
4. Content loads and renders
5. Scroll animates to saved position

**Duration:** 200-500ms depending on book size

**Mitigation:**
- Show loading spinner during transition
- Pre-render new WebView in background (complex)
- Accept minor flicker as trade-off for no crash

### 🟢 Low Risk: Multiple Floating Windows

**Complexity:** Need to track which window each document is in

**Solution:**
- Factory already tracks `HostWindows` collection
- `FindFloatingWindowForDockable()` method handles lookup
- Should work with existing infrastructure

### 🟢 Low Risk: Session Restore with Floating Windows

**Challenge:** How to restore floating windows on app restart?

**Current behavior:** CST doesn't restore floating windows (they collapse back to main)

**Decision:** Keep current behavior (don't restore floating windows)
- Simpler implementation
- Matches many IDE behaviors
- User can re-float if desired

---

## Comparison with Other Solutions

| Aspect | Disable ControlRecycling | NativeWebView Migration | **Button-Based Float** |
|--------|-------------------------|------------------------|----------------------|
| **Instant Tab Switching** | ❌ Lost | ✅ Preserved | ✅ **Potentially preserved** (if reordering is safe) |
| **Floating Windows** | ✅ Works | ✅ Works | ✅ **Works** (via buttons) |
| **Development Time** | 1 hour | 2-4 weeks | **1-2 weeks** |
| **Cost** | Free | **💰 Commercial license** | **Free** |
| **Complexity** | Low | High | **Medium** |
| **Maintainability** | High | Medium | **Medium-High** |
| **UX Trade-offs** | Visible scrolling | None | **Different float mechanism (buttons vs drag)** |
| **Risk** | None | API migration risk | **Tab reordering safety unknown** |

**Why This Approach:**
- ✅ **Only free solution** with potential to preserve both features
- ✅ **Less complex** than full NativeWebView migration (no API changes)
- ✅ **Reversible** - can fall back to disabled ControlRecycling if reordering is unsafe
- ⚠️ **Different UX** - buttons instead of drag-to-float (but drag still works for tools)
- ⚠️ **Requires verification** - tab reordering safety is TBD

---

## User Experience Considerations

### UX Changes

**What users LOSE:**
- ❌ Drag-to-float for book windows (must use button instead)
- ⚠️ Brief visual flicker during float/unfloat (WebView recreation)

**What users KEEP:**
- ✅ Instant tab switching within same window (if reordering is safe)
- ✅ Floating book windows (via buttons)
- ✅ Drag-to-float for tool panels (no CEF, still works)
- ✅ Drag-to-reorder book tabs (within same window)

### UX Comparison with Other IDEs

Many professional IDEs use button-based float:

**Visual Studio Code:**
- Drag to split/rearrange ✅
- "Move to New Window" command for floating ✅
- Similar to our approach

**JetBrains IDEs (IntelliJ, PyCharm):**
- Drag to float ✅ (native controls, no CEF issues)
- Also has "Float Mode" toggle button
- Mixed approach

**Visual Studio:**
- Full drag-to-float ✅ (native controls)
- Complex docking system

**CST Reader (with this approach):**
- Drag to reorder within window ✅
- Button to float across windows ✅
- **Acceptable UX trade-off for open-source project**

---

## Success Criteria

This approach is **successful** if:

1. ✅ **No crashes** when floating/unfloating book windows
2. ✅ **Instant tab switching preserved** (if tab reordering doesn't trigger detach)
3. ✅ **Book state survives float operations** (scroll, highlights, script)
4. ✅ **All existing features work** (search, scripts, dark mode, etc.)
5. ✅ **Reasonable UX** (button-based float is acceptable)
6. ✅ **Maintainable code** (not overly complex or fragile)

This approach **fails** if:

1. ❌ Tab reordering triggers detach/reattach (ControlRecycling still unsafe)
2. ❌ WebView cannot be programmatically recreated
3. ❌ State restoration fails (scroll, highlights lost)
4. ❌ Too complex to maintain (fragile lifecycle management)

---

## Next Steps

### Immediate (This Branch)

1. **Phase 1:** Set `CanFloat = false` on BookDisplayViewModel (1 hour)
2. **Phase 2:** Test tab reordering with lifecycle logging (**CRITICAL TEST**, 2-4 hours)
3. **Decision point:** If reordering is safe, proceed with Phases 3-6. If not, reconsider approach.

### If Successful

1. Complete implementation (Phases 3-6)
2. Comprehensive testing
3. User documentation (new float button workflow)
4. Merge to main branch
5. Include in Beta 3 or Beta 4 release

### If Unsuccessful

**Fallback options:**
1. Disable ControlRecycling entirely (original Solution 1)
2. Research Avalonia educational/open-source licensing for NativeWebView
3. Accept crash as known limitation, document workaround (disable ControlRecycling manually)

---

## Technical References

### Dock.Avalonia APIs Used

```csharp
// From IFactory interface
void FloatDockable(IDockable dockable);              // Float to new window
void AddDockable(IDock dock, IDockable dockable);    // Add to dock
void MoveDockable(IDock source, IDock target,
                 IDockable sourceDockable,
                 IDockable? targetDockable);         // Move between docks

// From IDockable interface
bool CanFloat { get; set; }   // Allow/prevent floating
bool CanDrag { get; set; }    // Allow/prevent dragging
bool CanClose { get; set; }   // Allow/prevent closing
```

### WebView Lifecycle

```csharp
// Current WebView usage
webView.LoadHtml(string htmlContent);
webView.ExecuteScript(string jsCode);
webView.Navigated += OnNavigated;
webView.Dispose();

// New lifecycle management
1. Save state → WebViewState
2. Dispose old WebView → webView.Dispose()
3. Perform dock operation → FloatDockable() or AddDockable()
4. Create new WebView → new WebView()
5. Restore state → LoadHtml(), ExecuteScript()
```

### Logging Points

```csharp
// Critical events to log:
OnDetachedFromVisualTree()    // When view leaves visual tree
OnAttachedToVisualTree()      // When view enters visual tree
Window parent changes         // Track which window owns view
FloatDockable() calls         // When float operation starts
WebView disposal              // When CEF browser disposed
WebView creation              // When new CEF browser created
State save/restore            // Track state preservation
```

---

## Conclusion

This button-based float approach is **worth pursuing** because:

1. ✅ **Only free solution** that could preserve both features
2. ✅ **Builds on existing infrastructure** (no API migration)
3. ✅ **Clear failure points** (can verify viability quickly)
4. ✅ **Reversible** (can fall back to Solution 1 if needed)
5. ✅ **Acceptable UX trade-off** (buttons vs drag-to-float)

**The critical unknown** is whether tab reordering triggers detachment. Phase 2 testing will determine viability.

**If tab reordering is safe:** This becomes the **best solution** for CST Reader.

**If tab reordering is unsafe:** We still gain button-based floating (no crash) but lose instant switching.

---

**Document Status:** Ready for implementation
**Next Action:** Phase 1 - Set `CanFloat = false` and begin testing
**Decision Point:** After Phase 2 tab reordering tests
