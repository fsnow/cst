# .NET MAUI + Blazor Hybrid POC Results

**Date**: November 9, 2025
**Status**: POC Completed - Not Recommended for Production
**Recommendation**: Do not proceed with MAUI+Blazor approach due to critical docking/floating window limitations

---

## Executive Summary

We conducted a proof-of-concept to evaluate .NET MAUI + Blazor Hybrid as a potential cross-platform replacement for the current Avalonia-based CST Reader architecture. The POC successfully demonstrated core functionality (book loading, script conversion, rendering) but revealed **critical architectural limitations** in implementing IDE-style docking and floating windows.

**Key Finding**: MAUI BlazorWebView does not support `window.open()`, which prevents JavaScript docking libraries from creating true floating windows. The workaround approaches would result in significantly degraded UX compared to the native Dock.Avalonia experience.

---

## POC Objectives

From `RENDERING_REQUIREMENTS.md`, we aimed to validate:

1. ✅ Load and display large XML books (tested with Vinaya Pitaka)
2. ✅ Multi-script conversion (14 scripts via ScriptConverter)
3. ✅ CSS-based rendering with custom stylesheets
4. ❌ **IDE-style docking with draggable panels**
5. ❌ **Floating windows for books**
6. ⏸️  Lucene.NET search integration (deferred)
7. ⏸️  Cross-platform validation Mac/Windows (partial - macOS only)

---

## What Was Accomplished

### Day 1: Book Loading and Display
**Completed**: November 8, 2025

- Created CST.MAUI project using `dotnet new maui-blazor`
- Implemented `BookService.cs` for XML loading and XSL transformation
- Reused CST.Core libraries (CST.dll, CST.Conversion.dll)
- Successfully loaded and rendered Vinaya Pitaka (s0101m.mul.xml) on macOS
- Verified HTML rendering within BlazorWebView (WKWebView on macOS)

**Files**:
- `src/CST.MAUI/Services/BookService.cs`
- `src/CST.MAUI/Components/Pages/BookDisplay.razor`
- `src/CST.MAUI/Components/Pages/Home.razor`

### Day 2: Script Conversion and CSS Styling
**Completed**: November 8, 2025

- Implemented script selector dropdown (14 scripts)
- Integrated `ScriptConverter.ConvertBook()` from CST.Conversion
- Added XSL transform caching for performance
- Fixed toolbar styling (fixed position at top)
- Resolved BlazorWebView focus bug with JavaScript blur workaround

**Technical Details**:
- XML books stored in Devanagari, converted on-demand
- Script conversion happens before XSL transform (matching CST.Avalonia:700-706)
- Fixed toolbar CSS with z-index for proper layering

**Issue Discovered**: Dropdown becomes unresponsive after script change until focus moves away - workaround implemented via `JSRuntime.InvokeVoidAsync("eval", "document.getElementById('script-selector')?.blur()")`

### Day 3: Docking Framework Research
**Completed**: November 9, 2025

Conducted extensive research on docking frameworks for MAUI/Blazor:

#### MAUI Native Options
- ❌ **DockLayout** (CommunityToolkit.Maui): Static positioning only (Top/Bottom/Left/Right), no dragging, no tabs, no floating
  - Similar to WPF's DockPanel
  - Examined sample at `~/github/CommunityToolkit/Maui/samples/`
  - Confirmed: NOT IDE-style docking

#### Blazor Commercial Options
- ❌ Telerik DockManager ($1,299/dev)
- ❌ Syncfusion Docking Manager ($995/dev)
- ❌ Infragistics DockManager (enterprise pricing)
- **Ruling**: Violates open-source requirement

#### Blazor Open-Source Options
- ❌ No mature MIT/Apache-licensed alternatives found

#### JavaScript Docking Libraries
- ✅ **GoldenLayout** (MIT license, 6.5k stars)
  - Uses `window.open()` for popout windows
  - Last updated August 2024
  - https://github.com/golden-layout/golden-layout

- ✅ **Dockview** (MIT license, zero dependencies)
  - Two modes: Floating Groups (DIVs) and Popout Groups (`window.open()`)
  - TypeScript-based, actively maintained
  - https://github.com/mathuo/dockview

---

## Critical Findings

### 1. BlazorWebView Does Not Support window.open()

**GitHub Issues**:
- [#20622](https://github.com/dotnet/maui/issues/20622): "Blazor Hybrid - Allow window.open() from JavaScript to automatically open a WebView"
- [#16317](https://github.com/dotnet/maui/issues/16317): "window.open('', '_blank') fails for BlazorWebView"

**Impact**:
- JavaScript `window.open()` calls return null or do nothing
- Affects both WKWebView (macOS) and WebView2 (Windows)
- GoldenLayout popout windows: **Will not work**
- Dockview popout groups: **Will not work**

### 2. Drag-to-Float is Not Feasible

**Technical Limitation**:
1. JavaScript can detect drag beyond WebView bounds
2. JavaScript could call C# via JSInterop to create MAUI Window
3. But the drag gesture is already broken at that point
4. New window appears, but user must start a new drag operation

**Comparison**:
- CST.Avalonia (Dock.Avalonia): Native controls, seamless drag-to-float within same process
- CST.MAUI (Dockview): JavaScript/C# boundary breaks gesture continuity

### 3. Workaround Approaches Evaluated

#### Option A: In-Window Docking Only (Dockview Floating Groups)
- ✅ Works: Draggable panels, tabs, splits within main window
- ❌ Floating "windows" are positioned DIVs, not true OS windows
- ❌ Cannot move floated panels to other monitors
- ❌ Cannot Alt+Tab to floated books
- **Assessment**: 60% of desired functionality

#### Option B: Hybrid JavaScript + MAUI Windows
- ✅ Dockview for in-window docking
- ✅ C# creates MAUI Windows via JSInterop for true floating
- ❌ No drag-to-float (explicit "pop out" button required)
- ❌ Complex state synchronization between windows
- ❌ Additional engineering complexity vs Dock.Avalonia
- **Assessment**: 80% of desired functionality, 3x implementation cost

#### Option C: Explicit Popout Actions (VS Code style)
- Right-click tab → "Move to New Window"
- Double-click title bar to float
- Dedicated "pop out" icon on tabs
- **Assessment**: Acceptable UX for some apps, degraded for power users

---

## Architecture Comparison

### CST.Avalonia (Current)
```
SimpleTabbedWindow.axaml
  └─ DockControl (Dock.Avalonia, MIT license)
      ├─ DocumentDock
      │   └─ CefSharp WebBrowser (per tab)
      └─ ToolDock
          └─ SearchView, etc.
```

**Floating Windows**:
- Native Avalonia Window class
- Dock.Avalonia handles drag-to-float automatically
- Full OS window management (Alt+Tab, multi-monitor, etc.)

### CST.MAUI + Blazor (POC)
```
MainPage.xaml
  └─ BlazorWebView (WKWebView on macOS, WebView2 on Windows)
      └─ Blazor Components (HTML/CSS)
          ├─ BookDisplay.razor (per route)
          └─ Potential: Dockview (JavaScript)
              └─ DIV-based "floating" panels
```

**Floating Windows** (hypothetical):
- Manual MAUI Window creation via C# + JSInterop
- No automatic drag-to-float
- State synchronization required
- Each window needs its own BlazorWebView

---

## Positive Findings

### What Worked Well

1. **BlazorWebView Performance**: Large books (Vinaya Pitaka) rendered smoothly
2. **Look and Feel**: User feedback: "I like the look-and-feel of this MAUI app better than CST.Avalonia"
3. **Code Reuse**: All CST.Core libraries worked without modification
4. **Development Experience**: Hot reload, modern web dev tools
5. **CSS Control**: Full control over book rendering styles
6. **Script Conversion**: ScriptConverter integration seamless

### Technical Advantages
- Modern web rendering (WebView2 = Chromium on Windows)
- Easier CSS customization than CefSharp
- Smaller app bundle size potential
- Better web standards compliance

---

## Limitations Discovered

### Critical (Blockers)
1. **No `window.open()` support** - Prevents JavaScript docking library popouts
2. **No drag-to-float** - Fundamental UX degradation vs Dock.Avalonia
3. **No open-source IDE-style docking** - Would require building custom solution

### Minor Issues Resolved
1. Dropdown focus bug - workaround implemented
2. Script selector visual hierarchy - CSS adjustments made
3. Window resize locking - determined to be expected behavior during reload

### Not Yet Tested
1. Windows cross-platform validation
2. Lucene.NET search integration
3. Multi-tab performance with many large books
4. Memory usage with multiple BlazorWebViews

---

## Recommendation

### Primary Recommendation: **Do Not Proceed** with MAUI + Blazor

**Rationale**:
1. Docking/floating windows are a **core requirement** for CST Reader
2. The MAUI+Blazor architecture cannot match Dock.Avalonia's native UX
3. Workarounds would require significant custom development
4. Final UX would still be inferior to current Avalonia implementation
5. No clear path to Linux support (original motivation for replacement)

### If Linux Support is Required

Consider these alternatives (in priority order):

1. **Avalonia on Linux** (revisit viability)
   - Check if Dock.Avalonia + CefSharp work on modern Linux distros
   - May need to switch from CefSharp to alternative browser control
   - See: `ALTERNATIVE_RENDERING_ENGINES.md`

2. **Electron** (if willing to use Chromium architecture)
   - Native support for multi-window, drag-to-float
   - HTML/CSS/JavaScript for UI
   - C# backend via .NET process or port to Node.js
   - Mature IDE-style docking libraries available

3. **Qt for .NET** (if native UI required)
   - Cross-platform including Linux
   - Native docking framework
   - C# bindings available
   - More mature than MAUI on Linux

---

## Alternative Paths Forward (If Proceeding Despite Recommendation)

### Minimal Viable Product Approach
If you still want to use MAUI+Blazor despite limitations:

1. **Phase 1**: Single-window multi-tab (no floating)
   - Use simple tab control or basic Dockview layout
   - 80% of users may not need floating windows
   - Defer floating until MAUI adds `window.open()` support

2. **Phase 2**: Explicit popout (if user feedback demands it)
   - Implement "Move to New Window" menu action
   - Accept that it's not drag-to-float
   - Document as known limitation

3. **Phase 3**: Monitor MAUI roadmap
   - Watch GitHub issue #20622 for `window.open()` support
   - If added, integrate GoldenLayout or Dockview popouts

### Custom Docking Implementation
- Estimate: 4-6 weeks of development
- Build lightweight Blazor component for tabs and panels
- Skip floating windows entirely
- Risks: Reinventing wheel, maintenance burden

---

## POC Code Location

All POC code is in the `cst` repository:

```
src/CST.MAUI/
├── Components/
│   └── Pages/
│       ├── BookDisplay.razor       # Book viewer with script selector
│       └── Home.razor               # Landing page with book list
├── Services/
│   └── BookService.cs              # XML loading, XSL transforms, script conversion
├── wwwroot/                        # XSL files, CSS (reused from CST.Avalonia)
└── CST.MAUI.csproj

docs/research/
├── RENDERING_REQUIREMENTS.md       # Original requirements
├── ALTERNATIVE_RENDERING_ENGINES.md
└── MAUI_BLAZOR_POC_RESULTS.md     # This document
```

**Git Status**: Changes are uncommitted in experimental branch.

---

## Conclusion

The MAUI + Blazor POC successfully demonstrated that:
- ✅ Core book rendering functionality is viable
- ✅ Script conversion and XSL transforms work well
- ✅ Performance is acceptable for large books
- ✅ UI/UX appearance is modern and polished

However, the **critical limitation in IDE-style docking** makes this approach unsuitable for CST Reader's requirements. The workarounds would result in a degraded user experience compared to the current Avalonia implementation.

**Final Verdict**: MAUI + Blazor is not the right architecture for CST Reader at this time. Recommend exploring Avalonia on Linux or alternative frameworks that provide native multi-window docking support.

---

## Appendix: Research Links

### MAUI BlazorWebView Issues
- https://github.com/dotnet/maui/issues/20622
- https://github.com/dotnet/maui/issues/16317
- https://github.com/dotnet/maui/issues/7930

### JavaScript Docking Libraries
- GoldenLayout: https://github.com/golden-layout/golden-layout
- Dockview: https://dockview.dev/ | https://github.com/mathuo/dockview

### CommunityToolkit.Maui
- DockLayout Sample: https://github.com/CommunityToolkit/Maui/blob/main/samples/CommunityToolkit.Maui.Sample/Pages/Layouts/DockLayoutPage.xaml
- Cloned to: `~/github/CommunityToolkit/Maui/`
