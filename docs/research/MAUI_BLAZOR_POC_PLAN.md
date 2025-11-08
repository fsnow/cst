# MAUI + Blazor Hybrid POC Plan

**Date:** November 8, 2025
**Goal:** Validate that .NET MAUI + Blazor Hybrid can replace Avalonia for CST Reader
**Timeline:** 1 week (5 days)
**Decision Point:** Go/No-Go on full MAUI migration

---

## POC Success Criteria

At the end of 1 week, we must answer these questions with **YES** or **NO**:

### Critical Questions (Must be YES to proceed)

#### A. WebView/Rendering Requirements (from RENDERING_REQUIREMENTS.md)

1. ✅/❌ **Can BlazorWebView render 3.6 MB HTML documents smoothly?**
   - Test with largest book (vin11t.nrf.xml → ~3.6 MB HTML)
   - Smooth scrolling at 60 FPS
   - Initial load < 1 second
   - **Why critical:** If large books don't work, entire approach fails

2. ✅/❌ **Does two-color search highlighting work?**
   - Blue highlights for search hits (`<span class="hit">`)
   - Green highlights for context words (`<span class="context">`)
   - CSS background colors render correctly
   - **Why critical:** Core search feature, requires CSS support

3. ✅/❌ **Can JavaScript bridge ALL required features?**
   - C# → JS: Execute JavaScript (scroll to anchor, toggle highlights)
   - JS → C#: Callbacks (scroll position, chapter changed)
   - Bidirectional communication reliable
   - **Why critical:** Many features depend on JS interop

4. ✅/❌ **Does scroll-to-anchor navigation work?**
   - Navigate to paragraph anchor (`para123`)
   - Navigate to page anchor (`V1.0023`)
   - Smooth scrolling to anchor
   - **Why critical:** Required for GoTo, chapter navigation, linked books

5. ✅/❌ **Can we track scroll position and find visible anchors?**
   - JavaScript detects scroll position
   - JavaScript finds first visible page anchor (V1.23, M2.45, etc.)
   - Callback to C# updates status bar
   - **Why critical:** Status bar tracking requirement

6. ✅/❌ **Do all paragraph styles render correctly?**
   - Headings (nikaya, book, chapter) - different sizes, bold, centered
   - Body text (bodytext, indent, centered)
   - Verses (gatha1, gatha2, gathalast) - specific indentation
   - Footnotes (blue in light mode, lighter blue in dark)
   - **Why critical:** Books must look correct

7. ✅/❌ **Do complex scripts render properly?**
   - Devanagari (ligatures)
   - Myanmar (stacking characters)
   - Thai (vowel marks)
   - All 14 Pali scripts work
   - **Why critical:** CST's primary purpose is Pali text

8. ✅/❌ **Does dark mode work?**
   - Detect system dark mode preference
   - Switch CSS (background, text colors, highlight colors)
   - Hot-swap without restart
   - **Why critical:** Already implemented feature, can't lose it

9. ✅/❌ **Can we copy/paste text?**
   - User selects text in BlazorWebView
   - Copy to system clipboard (Cmd+C / Ctrl+C)
   - Text includes correct Unicode characters
   - **Why critical:** Basic user expectation

10. ✅/❌ **Can we get selected text programmatically?**
    - JavaScript `window.getSelection()` works
    - Get selected text for dictionary lookup feature
    - **Why critical:** Planned dictionary feature requires this

11. ✅/❌ **Does script conversion preserve scroll position?**
    - Get current anchor before conversion (e.g., "V1.23")
    - Convert XML: Roman → Devanagari
    - Re-render HTML
    - Scroll to saved anchor
    - User stays at same reading position
    - **Why critical:** Users frequently change scripts while reading

#### B. Window Layout & Docking Requirements (THE ORIGINAL PROBLEM!)

12. ✅/❌ **Can we implement tabbed book interface?**
    - Open 5-10 books in tabs
    - Switch between tabs instantly (< 100ms perceived delay)
    - Each tab maintains scroll position
    - Each tab can have different script
    - **Why critical:** Core UI pattern

13. ✅/❌ **Can we float books to separate windows?**
    - "Float" tab opens new MAUI Window
    - Book renders in new window
    - Multiple floating windows work simultaneously
    - **Why critical:** The ControlRecycling crash problem we're trying to solve!

14. ✅/❌ **Do floating windows work reliably?**
    - No crashes when creating new window
    - No crashes when closing floating window
    - BlazorWebView works in multiple windows
    - Each window has independent scroll position
    - **Why critical:** Must solve the original CEF/ControlRecycling crash issue

15. ✅/❌ **Can we drag tabs to rearrange?**
    - Optional feature, but nice to have
    - Helps validate docking approach
    - **Why important:** Current Dock.Avalonia has this

16. ✅/❌ **Can we implement search panel (dockable)?**
    - Search panel alongside books
    - Panel can be shown/hidden
    - Panel can be docked left/right/bottom
    - **Why critical:** Tests docking system, not just tabs

#### C. Backend Integration Requirements

17. ✅/❌ **Can Lucene.NET integrate seamlessly?**
    - Call existing Lucene.NET code from Blazor
    - Search performance same as current app
    - Index location works on both platforms
    - **Why critical:** Keep valuable existing code

18. ✅/❌ **Do script converters work in MAUI?**
    - Reuse existing C# converter code
    - All 14 script conversions work
    - Performance acceptable (< 500ms for 3.6 MB book)
    - **Why critical:** Keep valuable existing code

19. ✅/❌ **Does XML/XSL transformation work?**
    - Reuse existing XSL stylesheets
    - Transformation produces correct HTML
    - All TEI XML elements handled
    - **Why critical:** Keep valuable existing code

#### D. Cross-Platform Requirements

20. ✅/❌ **Does it work on Windows?**
    - Build without errors
    - Run without crashes
    - All features work
    - Performance acceptable
    - **Why critical:** Primary target platform

21. ✅/❌ **Does it work on macOS?**
    - Build without errors
    - Run without crashes
    - All features work
    - Performance same as Windows
    - **Why critical:** Your development platform

### Important Questions (Should be YES, but not blockers)

22. ✅/❌ **Is development experience good?**
    - Hot reload works for Blazor components
    - Debugging is reasonable
    - Build times < 30 seconds
    - Error messages helpful

23. ✅/❌ **Can we implement session restoration?**
    - Save open tabs on exit
    - Restore tabs on startup
    - Restore scroll positions
    - **Why important:** Planned Beta 4 feature

24. ✅/❌ **Performance meets all targets?**
    - Initial load: < 1 second for largest book
    - Scrolling: 60 FPS smooth
    - Tab switching: < 100ms
    - Search: < 500ms for typical query
    - Memory: < 100 MB per book tab
    - **Why important:** User experience quality

25. ✅/❌ **Can we implement all status bar tracking?**
    - VRI page number (V1.23)
    - Myanmar page (M2.45)
    - PTS, Thai, Other pages
    - Current paragraph number
    - **Why important:** Required feature, tests JS interop thoroughness

---

## What to Build in POC

### Day 1-2: Foundation & Rendering
**Goal:** Prove BlazorWebView can handle CST books

**Build:**
```
MAUI App
├─ MainPage (MAUI native)
│   └─ BlazorWebView
│       └─ BookDisplay.razor
│           ├─ Load XML book
│           ├─ Apply XSL transformation (reuse existing)
│           ├─ Display HTML in BlazorWebView
│           └─ Test with largest book (3.6 MB)
```

**Test:**
- Load small book (< 500 KB)
- Load medium book (1-2 MB)
- Load largest book (vin11t.nrf.xml, 3.6 MB)
- Scroll performance
- Memory usage

**Files to Create:**
- `MauiProgram.cs` - App setup
- `MainPage.xaml` - Main window with BlazorWebView
- `Components/BookDisplay.razor` - Book rendering component
- `Services/XmlService.cs` - Reuse existing XML loading code
- `Services/XslService.cs` - Reuse existing XSL transformation

**Success Metric:**
- Largest book loads in < 1 second
- Smooth scrolling with no lag

---

### Day 3: Search Integration
**Goal:** Prove Lucene.NET integration works

**Build:**
```
Add to existing:
├─ Services/SearchService.cs (reuse existing Lucene.NET code)
├─ Components/SearchPanel.razor
│   ├─ Search input
│   ├─ Search button
│   ├─ Results list
│   └─ Call SearchService
└─ Update BookDisplay.razor
    ├─ Receive search results
    ├─ Highlight hits (blue background)
    ├─ Highlight context (green background)
    └─ Test two-color highlighting
```

**Test:**
- Simple search (single term)
- Phrase search (multiple terms)
- Two-color highlighting renders correctly
- Search performance (< 500ms for typical query)

**Success Metric:**
- Blue and green highlights visible
- Search results accurate (compared to current app)
- Performance acceptable

---

### Day 4: Tabs & Script Conversion
**Goal:** Prove multi-tab UI and script converters work

**Build:**
```
Update MainPage.xaml:
├─ TabView (MAUI control or custom)
│   ├─ Tab 1: Book A
│   ├─ Tab 2: Book B
│   └─ Tab 3: Book C (different script)
└─ Script selector dropdown
    └─ Change script → reload HTML
```

**Test:**
- Open 3 books in different tabs
- Switch between tabs instantly
- Each tab remembers scroll position
- Change script (Roman → Devanagari → Thai)
- Complex scripts render correctly (Myanmar, Devanagari)

**Files to Create:**
- `Services/ScriptConverterService.cs` - Reuse existing converters
- Update `BookDisplay.razor` - Add script switching
- Update `MainPage.xaml` - Add tabs

**Success Metric:**
- Tab switching feels instant
- Script conversion works
- All 14 scripts display correctly

---

### Day 5: Cross-Platform & JavaScript Interop
**Goal:** Prove it works on both platforms and JS interop works

**Build:**
```
Add JavaScript interop:
├─ wwwroot/bookInterop.js
│   ├─ getScrollPosition()
│   ├─ scrollToAnchor(anchorName)
│   └─ getCurrentChapter()
└─ BookDisplay.razor
    ├─ Call JS from C#
    └─ Call C# from JS (callbacks)
```

**Test:**
- Build on Windows
- Build on macOS
- Test JavaScript → C# callbacks
- Test C# → JavaScript calls
- Verify scroll position tracking works

**Success Metric:**
- Builds and runs on both platforms
- JavaScript interop works bidirectionally
- Can implement chapter dropdown logic

---

## What to SKIP in POC (Don't Build Yet)

These are important for the full app but **not needed to validate feasibility**:

### UI Features (Skip for now)
- ❌ Full docking system (just basic tabs is enough)
- ❌ Floating windows (test on Day 5 if time permits)
- ❌ Welcome screen
- ❌ Status bar with page numbers
- ❌ Dark mode (test CSS switch if time permits)
- ❌ Keyboard shortcuts
- ❌ Chapter dropdown (just test JS interop capability)
- ❌ GoTo dialog
- ❌ Search navigation (First/Previous/Next/Last buttons)
- ❌ Show/hide footnotes toggle
- ❌ Show/hide hits toggle

### Backend Features (Skip for now)
- ❌ All 217 books (just test with 3-5 books)
- ❌ Index building (use existing index if available)
- ❌ Session restoration
- ❌ Application state management
- ❌ Error handling / logging
- ❌ Settings / preferences

### Integration Features (Skip for now)
- ❌ Dictionary lookup
- ❌ Linked book navigation (Mula/Atthakatha/Tika)
- ❌ Export features
- ❌ Printing

**Why skip these?**
- They don't test technical feasibility
- They're features, not architectural risks
- If core rendering/search/tabs work, these will work too

---

## Technical Architecture (POC)

### Project Structure
```
CSTReader.MauiBlazor/
├─ Platforms/
│   ├─ Windows/
│   ├─ MacCatalyst/
│   └─ (skip Android, iOS for now)
├─ Components/
│   ├─ BookDisplay.razor
│   ├─ SearchPanel.razor
│   └─ Layout/MainLayout.razor
├─ Services/
│   ├─ IBookService.cs
│   ├─ BookService.cs (file I/O, XML loading)
│   ├─ ISearchService.cs
│   ├─ SearchService.cs (Lucene.NET wrapper)
│   ├─ IScriptConverterService.cs
│   └─ ScriptConverterService.cs (reuse existing)
├─ Models/
│   ├─ Book.cs
│   ├─ SearchResult.cs
│   └─ SearchHit.cs
├─ wwwroot/
│   ├─ css/
│   │   └─ book-styles.css (port existing CSS)
│   ├─ js/
│   │   └─ bookInterop.js (JS bridge)
│   └─ xsl/
│       └─ tipitaka-roman.xsl (copy existing)
├─ MainPage.xaml
├─ MauiProgram.cs
└─ CSTReader.MauiBlazor.csproj
```

### Key Dependencies
```xml
<Project Sdk="Microsoft.NET.Sdk.Razor">
  <PropertyGroup>
    <TargetFrameworks>net9.0-maccatalyst;net9.0-windows10.0.19041.0</TargetFrameworks>
    <!-- Skip Android/iOS for POC -->
  </PropertyGroup>

  <ItemGroup>
    <!-- MAUI -->
    <PackageReference Include="Microsoft.Maui.Controls" Version="9.0.x" />
    <PackageReference Include="Microsoft.AspNetCore.Components.WebView.Maui" Version="9.0.x" />

    <!-- Existing dependencies -->
    <PackageReference Include="Lucene.Net" Version="4.8.0" />
    <PackageReference Include="Lucene.Net.Analysis.Common" Version="4.8.0" />

    <!-- Might need for Blazor UI -->
    <PackageReference Include="MudBlazor" Version="7.x" /> <!-- Optional: for tabs/UI -->
  </ItemGroup>

  <!-- Copy existing code -->
  <ItemGroup>
    <Compile Include="..\CST.Avalonia\Services\ScriptConverter.cs" Link="Services\ScriptConverter.cs" />
    <Compile Include="..\CST.Avalonia\Services\Lucene\*.cs" Link="Services\Lucene\*.cs" />
    <Compile Include="..\CST.Avalonia\Models\*.cs" Link="Models\*.cs" />
  </ItemGroup>
</Project>
```

---

## Critical Risks to Validate

### Risk 1: BlazorWebView Performance with Large Documents
**Concern:** Will 3.6 MB HTML lag or crash?
**Test:** Load vin11t.nrf.xml on Day 1
**Mitigation:** If slow, try virtualization or chunking

### Risk 2: JavaScript Interop Reliability
**Concern:** Can we do bidirectional JS ↔ C# calls reliably?
**Test:** Day 5 - implement scroll position tracking
**Mitigation:** If callbacks flaky, use polling instead

### Risk 3: Complex Script Rendering
**Concern:** Myanmar, Devanagari ligatures might not work
**Test:** Day 4 - load book in Myanmar script
**Mitigation:** Font issues can be solved, but need to verify

### Risk 4: Tab Switching Performance
**Concern:** Switching tabs with large books might be slow
**Test:** Day 4 - rapid tab switching
**Mitigation:** May need view recycling or lazy loading

### Risk 5: Memory Usage
**Concern:** Multiple large books = high memory?
**Test:** Throughout - monitor Task Manager / Activity Monitor
**Mitigation:** Can unload tabs not in view

### Risk 6: macOS Build Issues
**Concern:** You're developing on macOS - will Windows build work?
**Test:** Day 5 - test on both platforms
**Mitigation:** MAUI should handle this, but verify

---

## Development Environment Setup

### Prerequisites

1. **.NET 9 SDK** (latest)
   ```bash
   dotnet --version  # Should be 9.0.x
   ```

2. **MAUI Workload**
   ```bash
   dotnet workload install maui
   ```

3. **IDE:**
   - **macOS:** Visual Studio 2022 for Mac (17.6+) or VS Code + C# DevKit
   - **Windows:** Visual Studio 2022 (17.8+)

4. **Xcode** (macOS)
   - Required for Mac Catalyst builds
   - Xcode 15+ recommended

5. **Windows SDK** (Windows)
   - Windows 10 SDK (10.0.19041.0) or higher
   - Installed automatically with Visual Studio

### Verification
```bash
# Check MAUI is installed
dotnet workload list

# Should see:
# maui                9.0.x
# maui-maccatalyst   9.0.x
# maui-windows       9.0.x
```

---

## POC Day-by-Day Breakdown

### Day 1: Project Setup & Basic Rendering

**Morning (2-3 hours):**
1. Create new MAUI Blazor Hybrid project
   ```bash
   dotnet new maui-blazor -n CSTReader.MauiBlazor
   ```
2. Configure for Desktop only (remove Android/iOS)
3. Run on macOS - verify "Hello World" works
4. Add BlazorWebView to MainPage

**Afternoon (3-4 hours):**
1. Copy one small XML book to project
2. Copy XSL transformation file
3. Create BookService to load XML
4. Create BookDisplay.razor component
5. Transform XML → HTML and display
6. Test with small book

**Evening (1-2 hours):**
1. Test with largest book (vin11t.nrf.xml)
2. Measure load time, memory usage
3. Test scrolling performance
4. Document any issues

**Deliverable:** App that can load and display a book

---

### Day 2: CSS Styling & Multiple Scripts

**Morning (2-3 hours):**
1. Copy existing CSS (book-styles.css)
2. Apply CSS to book display
3. Verify all paragraph styles render correctly
   - Headings (nikaya, book, chapter)
   - Body text (bodytext, centered)
   - Verses (gatha1, gatha2, gathalast)
4. Test footnotes rendering

**Afternoon (3-4 hours):**
1. Add script converter service
2. Add script selector UI (dropdown)
3. Test conversion: Roman → Devanagari
4. Test conversion: Roman → Myanmar
5. Test conversion: Roman → Thai
6. Verify complex script rendering

**Evening (1-2 hours):**
1. Test all 14 scripts if time permits
2. Verify Unicode rendering works
3. Check font rendering quality
4. Document any script issues

**Deliverable:** App with proper formatting and script conversion

---

### Day 3: Lucene.NET Search Integration

**Morning (2-3 hours):**
1. Copy Lucene.NET search code
2. Create SearchService wrapper
3. Point to existing index (or build small test index)
4. Test search from C# code (no UI yet)
5. Verify search results match current app

**Afternoon (3-4 hours):**
1. Create SearchPanel.razor component
2. Add search input and button
3. Connect to SearchService
4. Display search results list
5. Implement "load book with highlights" functionality

**Evening (2-3 hours):**
1. Add two-color highlighting to BookDisplay
   - Blue for search hits (`<span class="hit">`)
   - Green for context words (`<span class="context">`)
2. Test highlighting renders correctly
3. Test with various search queries
4. Verify performance

**Deliverable:** Working search with two-color highlights

---

### Day 4: Multi-Tab Interface

**Morning (3-4 hours):**
1. Research MAUI tab options:
   - Option A: MAUI Shell with TabBar
   - Option B: MudBlazor tabs (if using component library)
   - Option C: Custom Blazor tabs component
2. Implement chosen tab approach
3. Open 2-3 books in different tabs

**Afternoon (2-3 hours):**
1. Test tab switching performance
2. Verify each tab maintains scroll position
3. Add script selector per tab
4. Test: Book 1 in Roman, Book 2 in Devanagari

**Evening (2-3 hours):**
1. Stress test: Open 5-10 books
2. Monitor memory usage
3. Test rapid tab switching
4. Document tab behavior and performance

**Deliverable:** Multi-tab book reader

---

### Day 4.5: Floating Windows (THE CRITICAL TEST!)

**CRITICAL:** This is the whole reason we're considering MAUI - to fix the CEF ControlRecycling crash with floating windows. This must be tested thoroughly!

**Morning (3-4 hours): Basic Floating Windows**
1. Implement "Float to New Window" button for a tab
2. Create new MAUI Window:
   ```csharp
   var floatingWindow = new Window
   {
       Title = $"CST Reader - {book.Title}",
       Page = new ContentPage
       {
           Content = new BlazorWebView
           {
               HostPage = "wwwroot/index.html",
               RootComponents =
               {
                   new RootComponent
                   {
                       Selector = "#app",
                       ComponentType = typeof(BookDisplay),
                       Parameters = new Dictionary<string, object>
                       {
                           { "BookId", bookId }
                       }
                   }
               }
           }
       }
   };
   Application.Current.OpenWindow(floatingWindow);
   ```
3. Test: Open book in new window
4. Test: Close floating window
5. Test: Open multiple floating windows (3-5)

**Critical Tests:**
- ❌ **Does it crash?** (Like CEF did with ControlRecycling)
- ✅ **Does BlazorWebView work in new window?**
- ✅ **Can we have multiple windows with different books?**
- ✅ **Independent scroll positions per window?**

**Afternoon (2-3 hours): Window Lifecycle Tests**
1. **Test: Create and destroy windows repeatedly**
   - Open window, close window (repeat 10 times)
   - Monitor for memory leaks
   - Watch for crashes

2. **Test: Multiple windows simultaneously**
   - Open 5 floating windows
   - Different books in each
   - Scroll in each window independently
   - Search in different windows
   - Close windows in random order

3. **Test: Window focus and interaction**
   - Switch between main window and floating windows
   - Keyboard shortcuts work in all windows?
   - Copy/paste works in each window?

4. **Test: Script conversion in floating windows**
   - Open book in floating window
   - Change script (Roman → Devanagari)
   - Scroll position preserved?
   - Floating window updates correctly?

**Evening (2-3 hours): Stress Testing**
1. **Memory Leak Test:**
   - Open 10 floating windows
   - Monitor memory (Task Manager / Activity Monitor)
   - Close all windows
   - Memory returns to baseline?

2. **Crash Test:**
   - Rapid open/close of windows
   - Try to make it crash (like CEF did)
   - Close main window (do floating windows close?)
   - Close floating window (does main window survive?)

3. **Window State Test:**
   - Minimize/maximize floating windows
   - Move windows between monitors (if available)
   - Resize windows
   - Everything still works?

4. **Cross-Window Communication Test:**
   - Search in main window
   - Open result in floating window
   - Does it work?
   - Can windows communicate?

**Critical Success Criteria for Day 4.5:**

| Test | Expected Result | Pass/Fail |
|------|----------------|-----------|
| Open 5 floating windows | No crashes | ✅/❌ |
| BlazorWebView works in each window | Renders correctly | ✅/❌ |
| Close windows in any order | No crashes | ✅/❌ |
| Independent scroll positions | Each window independent | ✅/❌ |
| Rapid open/close (10x) | No memory leak, no crash | ✅/❌ |
| Script conversion in floating window | Works correctly | ✅/❌ |
| Windows survive main window minimize | Still accessible | ✅/❌ |

**If ALL tests pass:** ✅✅✅ **This is the GREEN LIGHT** - MAUI solves the ControlRecycling problem!

**If ANY test fails:** ⚠️ Need to investigate if it's solvable or a blocker

**Deliverable:** Proof that floating windows work reliably (or documentation of blockers)

---

### Day 5: Cross-Platform & JavaScript Interop

**Morning (2-3 hours):**
1. Create wwwroot/js/bookInterop.js
2. Implement basic JS functions:
   ```javascript
   function getScrollPosition() { ... }
   function scrollToAnchor(name) { ... }
   function getCurrentChapter() { ... }
   ```
3. Call JS from Blazor C#:
   ```csharp
   await JSRuntime.InvokeVoidAsync("scrollToAnchor", "para123");
   ```
4. Test JS → C# callbacks

**Afternoon (3-4 hours):**
1. If on macOS: Build for Windows
   - May need Windows VM or physical machine
   - Or skip and test on Day 6
2. If on Windows: Build for macOS
3. Test on both platforms
4. Compare behavior, performance

**Evening (2-3 hours):**
1. Test optional features if time:
   - Floating windows (new Window)
   - Dark mode (CSS variable switching)
2. Write up POC findings
3. Document issues and blockers
4. Make Go/No-Go recommendation

**Deliverable:** Full POC report with recommendation

---

## POC Evaluation Rubric

### Must Pass (All must be ✅ to proceed)

| Criteria | Pass/Fail | Notes |
|----------|-----------|-------|
| Large documents load < 1 sec | ✅/❌ | |
| Smooth scrolling (60 FPS) | ✅/❌ | |
| Two-color search highlights work | ✅/❌ | |
| Lucene.NET integration works | ✅/❌ | |
| Script converters work | ✅/❌ | |
| Multi-tab interface feasible | ✅/❌ | |
| Works on Windows | ✅/❌ | |
| Works on macOS | ✅/❌ | |

### Should Pass (Desirable but not blockers)

| Criteria | Pass/Fail | Notes |
|----------|-----------|-------|
| JavaScript interop reliable | ✅/❌ | Can work around if needed |
| Floating windows work | ✅/❌ | Nice to have |
| Memory usage < 200 MB per book | ✅/❌ | Optimization can come later |
| Hot reload works | ✅/❌ | Dev experience |

---

## Go/No-Go Decision Framework

### ✅ GO if:
- All "Must Pass" criteria are ✅
- At least 3 of 4 "Should Pass" criteria are ✅
- No major blockers discovered
- Team feels confident in MAUI/Blazor

### ❌ NO-GO if:
- Any "Must Pass" criteria is ❌
- Major performance issues (lag, crashes)
- JavaScript interop fundamentally broken
- Cross-platform doesn't work

### ⚠️ CONDITIONAL GO if:
- Most criteria pass but 1-2 issues found
- Issues have clear workarounds
- Team willing to invest in solutions

---

## Success Metrics Summary

At end of POC, we should be able to say:

> "We built a minimal CST Reader in MAUI + Blazor that can:
> - Load and display large Pali books (3.6 MB)
> - Search with Lucene.NET and highlight results (two colors)
> - Convert between scripts (all 14 work)
> - Open multiple books in tabs
> - Run on both Windows and macOS
> - Communicate between Blazor and JavaScript
>
> Performance is acceptable, no major blockers found.
> We recommend proceeding with full MAUI migration."

---

## What Happens After POC?

### If GO:
1. **Week 1-2:** Project structure, core services
2. **Week 3-4:** Full UI implementation (all views)
3. **Week 5-6:** Docking/tab system with MudBlazor or custom
4. **Week 7-8:** Feature completion (search navigation, GoTo, etc.)
5. **Week 9-10:** Testing, polish, documentation
6. **Week 11-12:** Beta release

**Total:** ~3 months to full migration

### If NO-GO:
1. Document why MAUI didn't work
2. Revisit other options:
   - Blazor Server (web app)
   - Electron.NET
   - Stay with Avalonia + custom rendering
3. Run POC for next-best option

### If CONDITIONAL:
1. Spend 1 more week solving specific issues
2. Re-evaluate with solutions in place
3. Make final Go/No-Go decision

---

## Docking System Implementation Options

Since you currently use Dock.Avalonia, you'll need a replacement in MAUI. Here are the options:

### Option 1: MAUI Shell + Native Tabs (Simplest)
```xml
<Shell>
    <TabBar>
        <Tab Title="Book 1"><BlazorWebView ... /></Tab>
        <Tab Title="Book 2"><BlazorWebView ... /></Tab>
    </TabBar>
    <FlyoutItem Title="Search Panel">
        <!-- Search UI -->
    </FlyoutItem>
</Shell>
```

**Pros:**
- ✅ Built-in to MAUI
- ✅ Native feel
- ✅ Easy tab management

**Cons:**
- ❌ Limited customization
- ❌ May not support all Dock.Avalonia features

**Test in POC:** Day 4

---

### Option 2: Blazor Component Library (MudBlazor/Radzen)

**MudBlazor Tabs:**
```razor
<MudTabs>
    <MudTabPanel Text="Book 1">
        <BookDisplay BookId="@book1Id" />
    </MudTabPanel>
    <MudTabPanel Text="Book 2">
        <BookDisplay BookId="@book2Id" />
    </MudTabPanel>
</MudTabs>
```

**MudBlazor Drawer (for search panel):**
```razor
<MudDrawer @bind-Open="@searchPanelOpen" Anchor="Anchor.Left">
    <SearchPanel />
</MudDrawer>
```

**Pros:**
- ✅ Rich UI components (tabs, drawers, dialogs)
- ✅ Material Design look
- ✅ All in Blazor/C#
- ✅ Active community

**Cons:**
- ⚠️ Third-party dependency (but free, open-source)
- ⚠️ May not have full docking like Dock.Avalonia

**Test in POC:** Day 4, can try if time permits

---

### Option 3: Custom Blazor Tabs + MAUI Windows

Build your own tab system in Blazor:

```razor
<!-- TabManager.razor -->
<div class="tab-bar">
    @foreach (var tab in OpenTabs)
    {
        <div class="tab @(tab.IsActive ? "active" : "")"
             @onclick="() => SelectTab(tab)">
            @tab.Title
            <button @onclick="() => FloatTab(tab)">⧉</button>
            <button @onclick="() => CloseTab(tab)">×</button>
        </div>
    }
</div>

<div class="tab-content">
    @if (ActiveTab != null)
    {
        <BookDisplay BookId="@ActiveTab.BookId" />
    }
</div>

@code {
    private List<TabInfo> OpenTabs = new();
    private TabInfo ActiveTab;

    private void FloatTab(TabInfo tab)
    {
        // Open new MAUI Window
        var floatingWindow = new Window { ... };
        Application.Current.OpenWindow(floatingWindow);
        OpenTabs.Remove(tab);
    }
}
```

**Pros:**
- ✅ Complete control
- ✅ No dependencies
- ✅ Custom behavior exactly as you want

**Cons:**
- ⚠️ More development effort
- ⚠️ Need to style yourself

**Test in POC:** Day 4, compare with Shell/MudBlazor

---

### Option 4: Hybrid - MAUI Controls + Blazor Content

```xml
<!-- MainPage.xaml -->
<ContentPage>
    <Grid>
        <Grid.RowDefinitions>
            <RowDefinition Height="Auto" />
            <RowDefinition Height="*" />
        </Grid.RowDefinitions>

        <!-- Native MAUI tab bar -->
        <HorizontalStackLayout Grid.Row="0">
            <Button Text="Book 1" Clicked="OnTab1Clicked" />
            <Button Text="Book 2" Clicked="OnTab2Clicked" />
        </HorizontalStackLayout>

        <!-- Blazor content -->
        <BlazorWebView Grid.Row="1" x:Name="blazorView">
            <RootComponents>
                <RootComponent Selector="#app" ComponentType="@typeof(BookDisplay)" />
            </RootComponents>
        </BlazorWebView>
    </Grid>
</ContentPage>
```

**Pros:**
- ✅ Native MAUI controls for chrome
- ✅ Blazor for content
- ✅ Best of both worlds

**Cons:**
- ⚠️ Two different UI paradigms
- ⚠️ Communication between MAUI and Blazor needed

**Test in POC:** Could try if other options don't work

---

### Recommendation for POC

**Start with Option 3 (Custom Blazor Tabs)** because:
1. Pure C# (no XAML)
2. Complete control
3. Easy to implement basic version
4. Can iterate quickly
5. Tests floating windows naturally

**If that doesn't work well:**
Try Option 2 (MudBlazor) - pre-built components might be faster

**Avoid Option 1 (Shell) for now:**
- Too restrictive
- May not support floating windows well

---

## Questions to Discuss Before Starting

### 1. Platform Priority
Q: Is Windows or macOS more important?
- **Answer:** ____________
- **Why it matters:** Should we develop primarily on Windows or Mac?

### 2. Performance Requirements
Q: What's acceptable for 3.6 MB book load time?
- **Answer:** ____________
- **Why it matters:** Sets performance baseline

### 3. UI Approach
Q: Are you open to using a Blazor component library (MudBlazor)?
- **Answer:** ____________
- **Why it matters:** Tabs/UI could be faster with library vs custom

### 4. Development Machine
Q: Will you be developing on macOS or Windows (or both)?
- **Answer:** ____________
- **Why it matters:** Determines primary platform and testing approach

### 5. Existing Code Location
Q: Where is your current CST Reader codebase?
- **Answer:** ____________ (this repo? `/Users/fsnow/github/fsnow/cst`)
- **Why it matters:** We'll copy services from existing Avalonia app

### 6. Books Location
Q: Where are the 217 XML books?
- **Answer:** ____________ (`/Users/fsnow/Library/Application Support/CSTReader/xml`?)
- **Why it matters:** Need to load books in POC

### 7. Lucene Index Location
Q: Do you have a pre-built Lucene index?
- **Answer:** ____________
- **Why it matters:** Can reuse index or need to build new one

### 8. Time Commitment
Q: Can you dedicate ~6-8 hours per day for 5 days?
- **Answer:** ____________
- **Why it matters:** POC timeline feasibility

### 9. Success Definition
Q: What does success look like to you personally?
- **Answer:** ____________
- **Why it matters:** Aligns expectations

### 10. Backup Plan
Q: If MAUI doesn't work, what's your preference?
- **Answer:** ____________ (Blazor Server? Electron.NET? Stay Avalonia?)
- **Why it matters:** Good to have plan B ready

---

## Ready to Start?

Once we discuss the questions above, we can:

1. **Set up development environment** (Day 0)
2. **Create initial project** (Day 1 morning)
3. **Start coding!**

Should we start with the questions, or do you want to just dive in and figure it out as we go?
