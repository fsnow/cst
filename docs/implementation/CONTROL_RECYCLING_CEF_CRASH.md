# ControlRecycling + CEF WebView Floating Windows Crash Analysis

**Date:** November 6, 2025
**CST Version:** 5.0.0-beta.3
**Platform:** macOS (M1/M2/M3 Apple Silicon)
**Status:** 🔴 **Critical Bug - Blocking Beta 3 Release**

## Executive Summary

**Problem:** CST Reader crashes when floating book windows that contain CEF-based WebViews with ControlRecycling enabled.

**Root Cause:** CEF (Chromium Embedded Framework) cannot safely reparent between windows on macOS. When ControlRecycling attempts to move an existing BookDisplayView instance to a floating window, CEF detects the NSView detachment and aggressively disposes the browser process, leading to crashes when accessing invalidated native handles.

**Impact:**
- ✅ **Instant tab switching** works perfectly (ControlRecycling keeps views alive)
- ❌ **Floating book windows** crash reliably on macOS with exit code 139 (SIGSEGV)

**Critical Constraints:**
- Both features are **equally important** for CST Reader:
  - Instant tab switching is essential for UX (large books, no visible scrolling)
  - Floating windows is a core requirement (multi-document workflows)
- CST Reader is **open-source** → Must use free/open-source solutions

**Key Finding:**
The "proper" solution (Avalonia NativeWebView with BeginReparenting API) **requires Avalonia Accelerate commercial license**, making it unsuitable for this open-source project. There is **NO free solution that preserves both features**.

## Quick Reference: Solution Options

| Solution | Time | Instant Switching | Floating Windows | Cost | Complexity | Recommendation |
|----------|------|------------------|------------------|------|------------|----------------|
| **Disable ControlRecycling** | 1 hour | ❌ Lost | ✅ Fixed | Free | Low | ✅ **Only free option** |
| **Selective ControlRecycling** | 4-8 hours | ❌ Lost for books | ✅ Fixed | Free | Medium | ⚠️ Still loses main feature |
| **Migrate to NativeWebView** | 2-4 weeks | ✅ **Preserved** | ✅ **Fixed** | **💰 Paid** | High | ❌ **Requires commercial license** |
| **Manual WebView Lifecycle** | 1-2 weeks | ⚠️ Partial | ✅ Fixed | Free | Very High | ⚠️ Complex but free |
| **CEF Offscreen Rendering** | 2-3 months | ✅ Preserved | ✅ Fixed | Free | Extreme | ❌ Not feasible |

**Critical Constraint for Open-Source Project:**
- Avalonia NativeWebView requires **Avalonia Accelerate commercial license**
- CST Reader is open-source → Commercial licensing not viable
- Only free solution that fixes crash: **Disable ControlRecycling**

**Recommended Path for Open-Source Project:**
```
Beta 3 (Immediate):  Disable ControlRecycling → Fix crash, accept scroll UX degradation
Long-term (Future):  Either accept the trade-off OR explore manual lifecycle management
Alternative:         Research if Avalonia offers open-source licensing for educational projects
```

---

## Root Cause: Technical Deep Dive

### Why CST Crashes on macOS

**Sequence of Events:**

1. **User floats a book tab** → Dock.Avalonia creates new `CstHostWindow` (floating window)
2. **ControlRecycling attempts to reuse existing view** → Tries to move `BookDisplayView` instance to new window
3. **Avalonia detaches BookDisplayView from main window** → Triggers `DetachedFromVisualTree` event
4. **CEF detects NSView has no parent window** → On macOS, this signals view destruction
5. **CEF aggressively disposes browser process** → Releases all native resources immediately
6. **Avalonia re-attaches view to floating window** → Tries to use now-invalid CEF handles
7. **Access to disposed CEF resources** → **Crash:** `AvnNativeControlHostTopLevelAttachment::InitializeWithChildHandle` null pointer dereference

**Crash signature:**
```
Exception Type:    EXC_BAD_ACCESS (SIGSEGV)
Exception Subtype: KERN_INVALID_ADDRESS at 0x0000000000000000
Thread 0 Crashed:  CrBrowserMain
libcef.dylib       (CEF internal)
libAvaloniaNative.dylib AvnNativeControlHostTopLevelAttachment::InitializeWithChildHandle + 88
```

### Platform-Specific Behavior

#### macOS (Current CST Platform)
- **Most aggressive CEF disposal** - CEF immediately destroys browser when NSView detaches from window
- **No grace period** - Cannot reparent without CEF cooperation
- **This is why CST crashes reliably** on macOS

#### Windows
- **More lenient** - Can technically reparent using `SetParent()` API
- **Requires "parking control" pattern** - Temporary invisible window to hold control during moves
- **Still fragile** - Timing-dependent and requires CefSharp-specific APIs

#### Linux
- **Varies by window system** (X11 vs Wayland)
- Similar challenges to Windows but less documented

### Why ControlRecycling Conflicts with CEF

**ControlRecycling's Assumptions:**
```xml
<!-- CST's Current Configuration (App.axaml) -->
<Application.Resources>
    <!-- Assumes views can be safely moved between containers -->
    <ControlRecycling x:Key="ControlRecyclingKey" />
</Application.Resources>

<Style Selector="DockControl">
    <!-- Reuses View instances across docking operations -->
    <Setter Property="(ControlRecyclingDataTemplate.ControlRecycling)"
            Value="{StaticResource ControlRecyclingKey}" />
</Style>
```

**CEF's Reality:**
- CEF native handles are **permanently bound to parent window** at creation time
- CEF **cannot survive** being moved between native windows
- CEF **expects View lifecycle = Browser lifecycle** (disposal on detach)

**Result:** Fundamental architectural mismatch

---

## Solution 1: Disable ControlRecycling (Quick Fix)

### Implementation

**File:** `/src/CST.Avalonia/App.axaml`

```xml
<!-- BEFORE: ControlRecycling enabled (causes crashes) -->
<Application.Resources>
    <ControlRecycling x:Key="ControlRecyclingKey" />
</Application.Resources>

<Style Selector="DockControl">
    <Setter Property="(ControlRecyclingDataTemplate.ControlRecycling)"
            Value="{StaticResource ControlRecyclingKey}" />
</Style>

<!-- AFTER: ControlRecycling disabled (floating windows work) -->
<Application.Resources>
    <!-- REMOVED: ControlRecycling causes CEF crashes during window reparenting
         Views will be recreated on each tab switch instead of reused -->
</Application.Resources>

<Style Selector="DockControl">
    <!-- REMOVED: ControlRecycling binding
         Dock.Avalonia will destroy and recreate views when switching tabs -->
</Style>
```

### Pros & Cons

**Advantages:**
- ✅ **Immediate fix** - Change 5 lines, test, ship
- ✅ **Floating windows work perfectly** - No crashes
- ✅ **Low risk** - Well-understood behavior
- ✅ **Reversible** - Can re-enable later if better solution found

**Disadvantages:**
- ❌ **Lose instant tab switching** - Main UX feature that ControlRecycling provided
- ❌ **Visible scrolling on tab switch** - Books reload and scroll to saved position (slow/jarring with large books)
- ❌ **State loss** - Search highlights, scroll position, etc. must be manually saved/restored
- ❌ **Slightly higher memory usage** - More View instances created over session

### Migration Impact

**Code changes required:**
1. Comment out ControlRecycling in `App.axaml` (2 lines)
2. Remove DockControl style setter (1 line)
3. Test floating windows work without crashes
4. Test scroll position save/restore still works

**User-facing changes:**
- Floating book windows now work reliably
- Tab switching shows visible scroll animation (books are large)
- Known limitation documented in release notes

**Estimated effort:** 1 hour (changes + testing)

---

## Solution 2: Selective ControlRecycling (Partial Fix)

### Concept

Disable ControlRecycling **only for documents** (BookDisplayView with WebView), keep it enabled for **tool panels** (OpenBookPanel, SearchPanel without WebView).

### Implementation

```xml
<!-- App.axaml -->
<Application.Resources>
    <!-- ControlRecycling only for tool panels (no WebView) -->
    <ControlRecycling x:Key="ToolPanelRecycling" />
</Application.Resources>

<!-- Apply selectively based on dock type -->
<Style Selector="ToolDock">
    <!-- Tools can use ControlRecycling safely (no CEF) -->
    <Setter Property="(ControlRecyclingDataTemplate.ControlRecycling)"
            Value="{StaticResource ToolPanelRecycling}" />
</Style>

<!-- DocumentDock with books: NO ControlRecycling -->
<Style Selector="DocumentDock">
    <!-- No recycling - prevents CEF crash when floating -->
</Style>
```

### Pros & Cons

**Advantages:**
- ✅ **Fixes floating window crash** - Books no longer reuse views
- ✅ **Preserves instant switching for tool panels** - OpenBook/Search stay fast
- ✅ **Minimal user impact** - Only documents affected

**Disadvantages:**
- ❌ **Still lose instant tab switching for books** - Main use case unresolved
- ⚠️ **Requires selector testing** - Ensure style selectors work correctly with Dock.Avalonia's structure
- ⚠️ **Doesn't solve the core UX problem** - Books still scroll on tab switch

### When to Use

This is a **compromise solution** if:
- Tool panel performance is critical (many searches, frequent OpenBook usage)
- Acceptable to lose book tab switching speed
- Don't want to invest in full NativeWebView migration yet

**Estimated effort:** 4-8 hours (implementation + testing different selector approaches)

---

## Solution 3: Migrate to Avalonia NativeWebView (⚠️ Commercial License Required)

### ⚠️ **CRITICAL: Licensing Constraint**

**Avalonia NativeWebView is part of Avalonia Accelerate**, which requires a **commercial license**.

- **Availability:** Part of paid "Avalonia Accelerate" package
- **Licensing:** Commercial product, not free/open-source
- **CST Impact:** CST Reader is open-source → This solution is **NOT VIABLE** without purchasing license
- **Alternative:** Consider asking Avalonia team if educational/open-source licensing available

**This solution is documented for completeness but is likely not suitable for CST Reader.**

---

### Why This Would Solve Both Problems (If Licensed)

**Avalonia NativeWebView** includes a purpose-built API for handling window reparenting:

```csharp
// From Avalonia documentation
public IDisposable BeginReparenting(bool yieldOnLayoutBeforeExiting = true)
```

**What `BeginReparenting()` does:**
- Delays destruction of native control during parent window changes
- Returns `IDisposable` to control reparenting lifecycle
- Designed specifically for dock/float scenarios
- **Solves the exact problem CST is experiencing**

### Implementation Overview

#### Step 1: Replace WebView Package

```xml
<!-- CST.Avalonia.csproj -->

<!-- REMOVE: WebViewControl-Avalonia (CefGlue-based, no reparenting support) -->
<PackageReference Include="WebViewControl" Version="3.120.9" />

<!-- ADD: Avalonia Accelerate NativeWebView (built-in reparenting support) -->
<!-- ⚠️ REQUIRES COMMERCIAL LICENSE - Avalonia Accelerate paid subscription -->
<!-- Not free/open-source like CefGlue -->
<PackageReference Include="Avalonia.WebView" Version="11.x.x" />
```

#### Step 2: Update BookDisplayView.axaml

```xml
<!-- BEFORE: WebViewControl-Avalonia -->
<UserControl xmlns:wv="using:WebViewControl">
    <wv:WebView x:Name="webView"
                IsVisible="{Binding IsWebViewAvailable}"
                Focusable="True" />
</UserControl>

<!-- AFTER: Avalonia NativeWebView -->
<UserControl xmlns:av="using:Avalonia.Controls">
    <av:NativeWebView x:Name="webView"
                      IsVisible="{Binding IsWebViewAvailable}"
                      Focusable="True" />
</UserControl>
```

#### Step 3: Update BookDisplayView.axaml.cs API Calls

```csharp
// BEFORE: WebViewControl-Avalonia API
var webView = this.FindControl<WebViewControl.WebView>("webView");
webView.LoadHtml(htmlContent);
var currentUrl = webView.Url;
webView.ExecuteScript("window.scrollTo(0, " + position + ")");

// AFTER: Avalonia NativeWebView API
var webView = this.FindControl<Avalonia.Controls.NativeWebView>("webView");
webView.LoadHtmlString(htmlContent);  // Different method name
var currentUrl = webView.Url;
webView.EvaluateJavaScript("window.scrollTo(0, " + position + ")");  // Different method name
```

#### Step 4: Implement Reparenting Support

```csharp
// BookDisplayView.axaml.cs

private IDisposable? _reparentingScope = null;

protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
{
    // Check if being reparented (floating) vs closed
    var window = this.GetVisualRoot() as Window;
    if (window != null && IsBeingFloated())  // Custom detection logic
    {
        // Begin reparenting - prevents WebView destruction
        _reparentingScope = _webView?.BeginReparenting();
        _logger.Information("BookDisplayView entering reparenting mode (floating operation)");
    }

    base.OnDetachedFromVisualTree(e);
}

protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
{
    base.OnAttachedToVisualTree(e);

    // End reparenting - WebView now in new window context
    if (_reparentingScope != null)
    {
        _reparentingScope.Dispose();
        _reparentingScope = null;
        _logger.Information("BookDisplayView reparenting completed");
    }
}

private bool IsBeingFloated()
{
    // Detect if detachment is due to floating vs closing
    // Implementation depends on Dock.Avalonia's lifecycle events
    return _viewModel?.IsSelected == true;  // Simplified heuristic
}
```

### API Migration Checklist

**All locations where WebView APIs are used:**

| Current API (WebViewControl) | New API (NativeWebView) | Files Affected |
|------------------------------|-------------------------|----------------|
| `LoadHtml(string)` | `LoadHtmlString(string)` | BookDisplayView.axaml.cs |
| `ExecuteScript(string)` | `EvaluateJavaScript(string)` | BookDisplayView.axaml.cs (scroll, highlights) |
| `Navigated` event | `NavigationCompleted` event | BookDisplayView.axaml.cs |
| `TitleChanged` event | (Check NativeWebView events) | BookDisplayView.axaml.cs |
| `Url` property | `Url` property | (Same - verify behavior) |

### Testing Requirements

**Must verify after migration:**

1. **Core Book Display:**
   - ✅ Books load and render correctly
   - ✅ Scroll position saved/restored on tab switch
   - ✅ WebView initialization on startup

2. **Search Functionality:**
   - ✅ Search highlights render correctly (JavaScript injection)
   - ✅ Navigate between search hits works
   - ✅ Two-color highlighting preserved (blue/green)

3. **Script Conversion:**
   - ✅ All 14 Pali scripts display correctly
   - ✅ Script switching updates book content
   - ✅ Font rendering for each script

4. **Floating Windows:**
   - ✅ **No crashes when floating book windows**
   - ✅ **Book content visible in floated window**
   - ✅ **Can drag book back to main window**
   - ✅ Tab switching between floated and main books

5. **ControlRecycling:**
   - ✅ **Instant tab switching preserved** (views reused)
   - ✅ Scroll position doesn't reset on tab switch
   - ✅ Search highlights persist across tab switches

6. **Dark Mode:**
   - ✅ Book content respects dark mode
   - ✅ Inverted search highlights work

7. **Cross-Platform:**
   - ✅ macOS (M1/M2/M3 Apple Silicon)
   - ⚠️ Windows (test if available)
   - ⚠️ Linux (test if available)

### Pros & Cons

**Advantages (If Licensed):**
- ✅ **Solves both problems** - Instant tab switching AND floating windows both work
- ✅ **Official Avalonia support** - Purpose-built API for this exact scenario
- ✅ **Cross-platform** - Works on macOS, Windows, Linux
- ✅ **Long-term maintainability** - Part of Avalonia ecosystem
- ✅ **Future-proof** - Likely better maintained than third-party WebViewControl

**Disadvantages:**
- ❌ **🔴 COMMERCIAL LICENSE REQUIRED** - Avalonia Accelerate is a paid product
- ❌ **Not viable for open-source projects** - CST Reader cannot use without purchasing license
- ❌ **Significant development time** - 2-4 weeks for migration + testing
- ❌ **API changes** - All WebView usage must be updated
- ❌ **Testing burden** - Must regression test all book display features
- ❌ **Potential behavior differences** - Uses platform-native engines (Safari on macOS, Edge on Windows, WebKitGTK on Linux) instead of consistent Chromium
- ⚠️ **Documentation gaps** - Avalonia NativeWebView docs may be less comprehensive than WebViewControl
- ⚠️ **Embedded resource handling** - May need to adjust how HTML/CSS resources are loaded
- ⚠️ **Lost CEF control** - May not have same deep control as full CEF-based solution

### Migration Effort Estimate

**Development:**
- Package replacement: 30 minutes
- XAML updates: 1 hour
- API migration (LoadHtml, ExecuteScript, etc.): 4-6 hours
- Reparenting lifecycle implementation: 4-6 hours
- Code review and cleanup: 2 hours
- **Total development: 12-16 hours**

**Testing:**
- Unit test updates: 2-4 hours
- Manual testing (all book display features): 8-12 hours
- Regression testing (search, scripts, dark mode): 8-12 hours
- Cross-platform testing (if Windows/Linux available): 4-8 hours
- **Total testing: 22-36 hours**

**Grand Total: 34-52 hours (approximately 2-4 weeks at 20 hours/week)**

### Risk Assessment

**High Risk Areas:**
- JavaScript execution (search highlights, scroll position)
- Event handling differences (Navigated vs NavigationCompleted)
- Resource loading (embedded HTML/CSS/images)

**Mitigation:**
- Create feature parity checklist
- Test each book display feature incrementally
- Keep WebViewControl-Avalonia code in git history for reference

---

## Solution 4: Manual WebView Lifecycle Management (Advanced)

### Concept

Programmatically destroy and recreate WebView when window context changes, without using NativeWebView's built-in reparenting support.

### High-Level Implementation

```csharp
// BookDisplayView.axaml.cs

private string? _savedHtmlContent = null;
private int _savedScrollPosition = 0;
private Window? _currentWindow = null;

protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
{
    if (IsBeingFloated())
    {
        // Save state BEFORE disposing WebView
        _savedHtmlContent = GetCurrentHtml();
        _savedScrollPosition = GetScrollPosition();

        // Dispose WebView to release CEF native handles
        DisposeWebView();
        _logger.Information("WebView disposed before window reparenting");
    }

    base.OnDetachedFromVisualTree(e);
}

protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
{
    base.OnAttachedToVisualTree(e);

    var newWindow = this.GetVisualRoot() as Window;

    if (_currentWindow != null && !ReferenceEquals(_currentWindow, newWindow))
    {
        // Window changed - recreate WebView in new context
        RecreateWebView();

        // Restore saved state
        if (_savedHtmlContent != null)
        {
            LoadHtmlContent(_savedHtmlContent);
            ScrollToPosition(_savedScrollPosition);
        }

        _logger.Information("WebView recreated in new window context");
    }

    _currentWindow = newWindow;
}

private void DisposeWebView()
{
    if (_webView != null)
    {
        _webView.Navigated -= OnNavigationCompleted;
        _webView.TitleChanged -= OnTitleChanged;
        _webView.Dispose();
        _webView = null;
        _isBrowserInitialized = false;
    }
}

private void RecreateWebView()
{
    // Challenge: Can't easily recreate XAML-declared WebView at runtime
    // Would need to:
    // 1. Remove old WebView from visual tree
    // 2. Create new WebView instance programmatically
    // 3. Set all properties (IsVisible, Focusable, ZIndex, etc.)
    // 4. Add to visual tree at correct position
    // 5. Re-attach event handlers

    // This is complex and fragile with XAML-declared controls
}
```

### Challenges

**1. XAML vs Programmatic Control Creation**

XAML-declared controls are harder to replace dynamically:

```xml
<!-- Current CST approach: XAML-declared WebView -->
<wv:WebView x:Name="webView" ... />
```

**Problem:** Can't easily swap out this instance at runtime. Would need to:
- Use `ContentControl` or `ContentPresenter` with dynamic content
- Or move WebView creation entirely to code-behind
- Both approaches require significant refactoring

**2. Detecting Float vs Close**

```csharp
private bool IsBeingFloated()
{
    // How to distinguish between:
    // 1. Tab being floated to new window (need to preserve WebView)
    // 2. Tab being closed (can destroy WebView)
    // 3. Application shutting down (can destroy WebView)

    // Heuristics are fragile and may break with Dock.Avalonia updates
    return _viewModel?.IsSelected == true;  // Not reliable
}
```

**3. State Restoration Complexity**

Need to save and restore:
- HTML content (large - can be MB of text)
- Scroll position (easy)
- Search highlights with positions (complex)
- JavaScript state (browser history, etc.)
- CEF internal state (cookies, cache, etc.)

**4. Visual Flicker**

User will see:
- Old WebView disappears
- Brief empty space
- New WebView appears
- Content loads and renders
- Scroll animates to saved position

This defeats the purpose of ControlRecycling (instant switching).

### Pros & Cons

**Advantages:**
- ✅ Keeps current WebViewControl-Avalonia package (no API migration)
- ✅ Floating windows work (no crashes)
- ⚠️ Theoretically preserves scroll position (if restoration works)

**Disadvantages:**
- ❌ **Very complex** - Lifecycle management is error-prone
- ❌ **Fragile** - Easily breaks with Dock.Avalonia or Avalonia updates
- ❌ **Hard to maintain** - Future developers will struggle
- ❌ **Visible flicker** - Defeats instant switching benefit
- ❌ **Memory leak risks** - Incomplete disposal can leak CEF resources
- ❌ **State loss risks** - Restoration may fail, losing user context
- ❌ **Testing burden** - Many edge cases to cover

### Recommendation

**Do NOT use this approach unless Solutions 1-3 all fail.**

This is a last-resort workaround that adds significant complexity for marginal benefit. The development time (1-2 weeks) is similar to migrating to NativeWebView, but with far worse maintainability.

---

## Solution 5: CEF Offscreen Rendering (Not Recommended)

### Concept

Use CEF's Offscreen Rendering (OSR) mode to decouple rendering from native window hierarchy.

### How OSR Works

**Traditional mode:** CEF → Native HWND/NSView → Window hierarchy (fragile)
**OSR mode:** CEF → Bitmap buffer → App draws anywhere (flexible)

```csharp
// CEF initialization with OSR
CefWindowInfo windowInfo = new CefWindowInfo();
windowInfo.SetAsOffScreen();  // Enable OSR mode

CefSettings settings = new CefSettings();
settings.WindowlessRenderingEnabled = true;
```

**Rendering pipeline:**
1. CEF renders web page to memory buffer
2. Application receives `OnPaint` callback with bitmap data
3. Application copies bitmap to GPU texture
4. Application draws texture using DirectX/OpenGL/Metal
5. Result displayed on screen

### Why This Would Solve the Problem

**Benefits:**
- ✅ No native window handle to reparent
- ✅ Can move "WebView" freely between any windows
- ✅ Complete control over rendering pipeline
- ✅ Used successfully in game engines (ImGui + CEF)

### Why This Is Not Feasible for CST

**Massive Complexity:**

1. **Custom Rendering Pipeline:**
   - Need to implement Metal rendering for macOS
   - Handle bitmap copying from CEF to GPU
   - Manage texture lifecycle and memory
   - Handle resize, DPI scaling, etc.

2. **Input Handling:**
   - Manually forward mouse events to CEF
   - Manually forward keyboard events to CEF
   - Handle focus management
   - Handle scroll wheel, touch, gestures

3. **Performance:**
   - Additional CPU overhead (buffer copying)
   - May lose hardware acceleration benefits
   - Need to optimize rendering pipeline

4. **WebViewControl-Avalonia Limitations:**
   - Package may not expose OSR configuration
   - Would likely need to fork and modify package
   - Or switch to raw CefGlue/CefSharp (even more work)

**Effort Estimate:**
- Rendering pipeline: 4-6 weeks
- Input handling: 2-3 weeks
- Testing and optimization: 2-4 weeks
- **Total: 8-13 weeks (2-3 months)**

### Recommendation

**❌ Do NOT pursue this approach for CST Reader.**

The effort (2-3 months) far exceeds the benefit. This makes sense for game engines where rendering pipelines already exist, but not for a document reader application.

If you need this level of control, migrate to NativeWebView instead (2-4 weeks with proper APIs).

---

## Decision Framework: Which Solution to Choose?

### Priority Matrix

Use this matrix to decide based on project priorities:

| Priority | Best Solution | Rationale |
|----------|--------------|-----------|
| **Ship Beta 3 ASAP** (1-2 weeks) | Disable ControlRecycling | Quick fix, stable, reversible |
| **Minimize development time** | Disable ControlRecycling | 1 hour vs weeks for alternatives |
| **Preserve instant tab switching** | ❌ No free solution | NativeWebView requires paid license |
| **Minimize risk** | Disable ControlRecycling | Well-understood, low complexity |
| **Long-term maintainability** | Disable ControlRecycling | Simple, no licensing dependencies |
| **Best UX** (both features work) | ❌ Not achievable for free | Would require Avalonia Accelerate license |
| **Open-source requirement** | Disable ControlRecycling | Only viable free option |

### User Requirements Analysis

**CST Reader stated requirements:**
- ✅ Floating book windows are **mandatory** ("a requirement for the program")
- ✅ Instant tab switching is **critical** (slow scrolling is "unnatural" with large books)
- ✅ Both features are **equally important** (cannot compromise on either)
- ✅ Open-source project - Must use **free/open-source** solutions

**Given these constraints, there is NO free solution that preserves both features:**

→ **Avalonia NativeWebView** (Solution 3) - ❌ Requires commercial license
→ **Disable ControlRecycling** (Solution 1) - ✅ Free, but loses instant switching
→ **Manual WebView Lifecycle** (Solution 4) - ⚠️ Free but complex, partial instant switching

**For open-source CST Reader, the realistic options are:**
1. Accept UX trade-off (disable ControlRecycling)
2. Invest in complex manual lifecycle management
3. Research educational/open-source Avalonia licensing
4. Accept current crash as known limitation until better solution emerges

### Recommended Timeline

**For Beta 3 (Immediate - Next 1-2 Weeks):**

```
Option A: Ship with Compromise
1. Disable ControlRecycling (1 hour)
2. Test floating windows work (2-4 hours)
3. Document known limitation in release notes
4. Ship Beta 3 with stable floating windows

Trade-off: Books scroll on tab switch (UX degradation)
```

**For Beta 4/Future (Long-term):**

```
Option B: Explore Alternative Solutions
1. Research Avalonia Accelerate educational/open-source licensing
   - Contact Avalonia team about non-commercial use
   - CST is educational Pali text reader for open-source community

2. OR: Implement manual WebView lifecycle management (1-2 weeks)
   - Complex but keeps project free/open-source
   - May achieve partial instant switching

3. OR: Accept the UX trade-off permanently
   - Floating windows > instant tab switching
   - Document as known limitation

Result: Either gain proper solution OR accept trade-off
```

### Licensing Research

**Worth investigating:**
- Does Avalonia offer educational/non-commercial licensing for Avalonia Accelerate?
- CST Reader is a scholarly tool for Pali canon research
- Vipassana Research Institute provides texts for free
- Could Avalonia support this as educational use case?

**Contact:** Avalonia team via GitHub or official channels

---

## Implementation Guides

### Quick Start: Disable ControlRecycling

**File:** `/src/CST.Avalonia/App.axaml`

**Step 1: Comment out ControlRecycling resource**

```xml
<!-- BEFORE -->
<Application.Resources>
    <ControlRecycling x:Key="ControlRecyclingKey" />
</Application.Resources>

<!-- AFTER -->
<Application.Resources>
    <!-- REMOVED: ControlRecycling causes CEF crashes when floating books
         Views will be destroyed and recreated when switching tabs.
         Known limitation: Books will scroll visibly when switching tabs.
         Proper fix: Migrate to Avalonia NativeWebView with BeginReparenting (Beta 4) -->
</Application.Resources>
```

**Step 2: Remove ControlRecycling from DockControl style**

```xml
<!-- BEFORE -->
<Style Selector="DockControl">
    <Setter Property="(ControlRecyclingDataTemplate.ControlRecycling)"
            Value="{StaticResource ControlRecyclingKey}" />
</Style>

<!-- AFTER -->
<Style Selector="DockControl">
    <!-- REMOVED: ControlRecycling binding
         Dock will now destroy and recreate views when switching tabs.
         This prevents CEF crashes when floating book windows. -->
</Style>
```

**Step 3: Build and test**

```bash
cd /src/CST.Avalonia
dotnet build
dotnet run

# Test sequence:
# 1. Open two books
# 2. Float book 2 to new window (should NOT crash)
# 3. Drag book 2 back to main window (should NOT crash)
# 4. Switch between book 1 and book 2 tabs (should work, books will scroll)
```

**Step 4: Update release notes**

```markdown
# CST Reader Beta 3 Release Notes

## Fixed
- Fixed crash when floating book windows on macOS

## Known Limitations
- Tab switching now shows visible scroll animation due to technical limitations
  with Chromium Embedded Framework and window reparenting on macOS.
  This is a temporary limitation; a proper fix is planned for Beta 4.
```

**Time estimate:** 1 hour (changes + testing)

---

### Advanced: NativeWebView Migration

**Phase 1: Package Replacement (Day 1)**

```bash
# 1. Remove WebViewControl-Avalonia
dotnet remove package WebViewControl

# 2. No new package needed - NativeWebView is part of Avalonia
# Verify Avalonia.Controls includes NativeWebView
```

**Phase 2: XAML Updates (Day 1)**

Update all `.axaml` files:

```xml
<!-- File: BookDisplayView.axaml -->

<!-- BEFORE: WebViewControl-Avalonia -->
xmlns:wv="using:WebViewControl"
...
<wv:WebView x:Name="webView" ... />

<!-- AFTER: Avalonia NativeWebView -->
xmlns:av="using:Avalonia.Controls"
...
<av:NativeWebView x:Name="webView" ... />
```

**Phase 3: API Migration (Days 2-3)**

Create API compatibility layer:

```csharp
// File: WebViewHelpers.cs (new file)

public static class WebViewHelpers
{
    // Wrap NativeWebView with WebViewControl-like APIs
    public static void LoadHtml(this NativeWebView webView, string html)
    {
        webView.LoadHtmlString(html);
    }

    public static Task<string> ExecuteScriptAsync(this NativeWebView webView, string script)
    {
        return webView.EvaluateJavaScript(script);
    }

    // Add more compatibility methods as needed
}
```

Then update `BookDisplayView.axaml.cs`:

```csharp
// BEFORE
_webView.LoadHtml(htmlContent);

// AFTER (using compatibility layer)
_webView.LoadHtml(htmlContent);  // Still works via extension method
```

**Phase 4: Reparenting Lifecycle (Days 3-4)**

```csharp
// File: BookDisplayView.axaml.cs

private IDisposable? _reparentingScope = null;
private Window? _previousWindow = null;

protected override void OnDetachedFromVisualTree(VisualTreeAttachmentEventArgs e)
{
    var currentWindow = this.GetVisualRoot() as Window;

    // Check if this is a float operation (not closing)
    if (currentWindow != null && _viewModel?.IsSelected == true)
    {
        _reparentingScope = _webView?.BeginReparenting();
        _previousWindow = currentWindow;
        _logger.Information("BookDisplayView entering reparenting mode");
    }

    base.OnDetachedFromVisualTree(e);
}

protected override void OnAttachedToVisualTree(VisualTreeAttachmentEventArgs e)
{
    base.OnAttachedToVisualTree(e);

    var newWindow = this.GetVisualRoot() as Window;

    // Complete reparenting if window changed
    if (_reparentingScope != null && newWindow != null &&
        !ReferenceEquals(_previousWindow, newWindow))
    {
        _reparentingScope.Dispose();
        _reparentingScope = null;
        _previousWindow = null;
        _logger.Information("BookDisplayView reparenting completed");
    }
}
```

**Phase 5: Testing (Days 5-10)**

Systematic testing checklist:

```markdown
## Feature Testing Checklist

### Book Display
- [ ] Book loads and renders correctly
- [ ] All 14 Pali scripts display correctly
- [ ] Scroll position saved on tab switch
- [ ] Scroll position restored on tab switch
- [ ] WebView initializes on application startup
- [ ] HTML/CSS embedded resources load correctly

### Search
- [ ] Search highlights render (blue background)
- [ ] Context highlights render (green background)
- [ ] Navigate to first hit works
- [ ] Navigate to previous/next hit works
- [ ] Two-color highlighting preserved
- [ ] Highlights persist after tab switch

### Floating Windows
- [ ] Can float book to new window (NO CRASH)
- [ ] Book content visible in floated window
- [ ] Can drag book back to main window (NO CRASH)
- [ ] Can switch tabs between floated and main books
- [ ] Instant tab switching preserved (no visible scroll)
- [ ] Search highlights survive float/unfloat

### Script Conversion
- [ ] Can switch between all 14 scripts
- [ ] Book content updates when script changes
- [ ] Fonts render correctly for each script
- [ ] Search results update to new script

### Dark Mode
- [ ] Book content respects system dark mode
- [ ] Text is white on black background
- [ ] Search highlights inverted (correct colors)
- [ ] Readable in dark mode

### Session Restoration
- [ ] Open books restored on app restart
- [ ] Scroll positions restored
- [ ] Search highlights restored
- [ ] Floated windows NOT restored (expected)
```

**Phase 6: Deployment (Day 11)**

```bash
# Build for all platforms
dotnet publish -c Release -r osx-arm64
dotnet publish -c Release -r osx-x64

# Package macOS DMG
./package-macos.sh arm64

# Test packaged app
# ...

# Ship Beta 4
```

---

## Appendix: Research Sources

### GitHub Issues and Discussions

**CefSharp (WPF/WinForms CEF binding):**
- Issue #3076: "Crash after docking into another panel"
- Issue #334: "Moving WebView from one window to another crashes CefSharp3"
- Issue #2840: "Browser disappears when floating form to new window"
- Issue #2635: "CefSharp ParentFormMessageInterceptor crashes"

**Dock.Avalonia:**
- Repository: https://github.com/wieslawsoltes/Dock
- Issue #880: "Trying to create a floating window"
- No specific issues about WebView + floating crashes (suggesting users avoid this combination)

**Avalonia:**
- Discussion #11837: "Prevent destruction/re-creation of tab content in TabControl"
- Discussion #15801: "Whats the right way to detach a HWND from NativeControlHost?"
- Discussion #11069: NativeControlHost lifecycle management
- WebView docs: https://docs.avaloniaui.net/xpf/embedding/web-view

**WebViewControl-Avalonia:**
- Repository: https://github.com/OutSystems/WebView
- No open issues about floating window crashes

### CEF Forum Discussions

- "Invalid window handle (Delphi)": https://www.magpcss.org/ceforum/viewtopic.php?f=6&t=19373
- Multiple threads about CEF reparenting limitations on macOS
- Consensus: CEF aggressively disposes browser when NSView detaches from window

### Community Solutions

**RoyalApps WinFormsControlHost:**
- Repository: https://github.com/royalapplications/royalapps-community-avalonia
- Custom NativeControlHost that prevents control destruction during lifecycle
- Demonstrates advanced lifecycle management is possible

**ImGui + CEF Integration:**
- Uses CEF Offscreen Rendering mode
- Successfully implements dockable browser windows in game engines
- GitHub issue #1140 (ocornut/imgui)

### Alternative WebView Packages

**DotNetBrowser:**
- Website: https://www.teamdev.com/dotnetbrowser
- Commercial CEF-based solution ($1,499/developer)
- Advertises Avalonia support
- Unclear if it solves reparenting issues (proprietary)

**Avalonia NativeWebView:**
- Part of Avalonia Accelerate package (**commercial license required**)
- Official documentation: https://docs.avaloniaui.net/xpf/embedding/web-view
- `BeginReparenting()` API specifically for dock/float scenarios
- Not suitable for open-source projects without special licensing arrangement

### CST Codebase

**Files analyzed:**
- `src/CST.Avalonia/App.axaml` - ControlRecycling configuration
- `src/CST.Avalonia/Views/BookDisplayView.axaml` - WebView XAML declaration
- `src/CST.Avalonia/Views/BookDisplayView.axaml.cs` - WebView lifecycle management
- `src/CST.Avalonia/Services/CstDockFactory.cs` - Dock creation
- `src/CST.Avalonia/CST.Avalonia.csproj` - Package references

**Packages:**
- WebViewControl-Avalonia 3.120.9 (ARM64) - Current
- Dock.Avalonia 11.3.0.15 - Current
- Dock.Controls.Recycling 11.3.0.15 - Current (ControlRecycling)

---

## Conclusion

**The crash is solvable, but there is NO free solution that preserves both features:**

### For Beta 3 (Immediate):
**Disable ControlRecycling** → Ship with stable floating windows, accept visible scrolling on tab switches as known limitation.

### For Beta 4/Future (Long-term):
**Two realistic paths for open-source project:**

1. **Accept the UX trade-off** - Floating windows are more critical than instant switching
   - Simple, maintainable, no licensing issues
   - Document as known CEF limitation on macOS

2. **Implement manual WebView lifecycle** - Complex but keeps both features (partially)
   - 1-2 weeks development effort
   - High complexity, maintenance burden
   - May still have visible flicker

3. **Research Avalonia educational licensing** - Contact Avalonia team
   - CST is scholarly tool for Buddhist canon research
   - Non-commercial, educational use case
   - May qualify for special licensing

**This is not a fundamental CEF limitation** - it's an integration challenge. However, the "proper" solution (Avalonia NativeWebView) requires a **commercial license**, making it unsuitable for open-source CST Reader.

**Reality:** Given CST's open-source nature and that floating windows are mandatory, the most practical path is to **accept the scroll UX trade-off** and ship Beta 3 with ControlRecycling disabled.

---

**Document Status:** Research complete, ready for decision
**Next Step:** Review findings and choose implementation path
**Contact:** CST Development Team
