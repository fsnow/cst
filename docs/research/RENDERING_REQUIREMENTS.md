# CST Reader Book Rendering Requirements

**Date:** November 7, 2025
**Purpose:** Complete requirements specification for evaluating alternative rendering engines
**Context:** Before evaluating alternatives to CEF/WebView, capture ALL requirements to ensure no features are missed

---

## Table of Contents

1. [Data Input & Size Constraints](#1-data-input--size-constraints)
2. [Text Formatting Requirements](#2-text-formatting-requirements)
3. [Anchor System Requirements](#3-anchor-system-requirements)
4. [Bidirectional Chapter Dropdown](#4-bidirectional-chapter-dropdown)
5. [GoTo Feature Requirements](#5-goto-feature-requirements)
6. [Status Bar Tracking Requirements](#6-status-bar-tracking-requirements)
7. [Search Hit Navigation](#7-search-hit-navigation)
8. [Show/Hide Toggles](#8-showhide-toggles)
9. [Dark Mode Support](#9-dark-mode-support)
10. [Script Conversion Requirements](#10-script-conversion-requirements)
11. [Copy/Paste and Text Selection Requirements](#11-copypaste-and-text-selection-requirements)
12. [Linked Book Navigation (Mula/Atthakatha/Tika)](#12-linked-book-navigation-mulaatthakathatika)
13. [Performance Requirements](#13-performance-requirements)
14. [Keyboard Shortcuts](#14-keyboard-shortcuts)
15. [Multi-Window Support (Floating Books)](#15-multi-window-support-floating-books)
16. [Session Restoration Requirements](#16-session-restoration-requirements)
17. [JavaScript Bridge Requirements (Current Implementation)](#17-javascript-bridge-requirements-current-implementation)
18. [Alternative Rendering Engine Evaluation Checklist](#18-alternative-rendering-engine-evaluation-checklist)
19. [Summary: Critical Non-Negotiable Requirements](#19-summary-critical-non-negotiable-requirements)
20. [Next Steps](#20-next-steps)

---

## 1. Data Input & Size Constraints

### Source Format
- **Input:** TEI-encoded XML files (217 books total)
- **Location:** `/Users/fsnow/Library/Application Support/CSTReader/xml`
- **Transformation:** XML → XSLT → HTML (current pipeline)

### Size Constraints
**Maximum book size:** 3.6 MB (vin11t.nrf.xml)
**Typical sizes:**
- Large books: 2-3.6 MB (20 books)
- Medium books: 500 KB - 2 MB (most books)
- Small books: < 500 KB

**Critical Requirement:** Rendering engine must handle 3.6 MB HTML documents efficiently
- Fast initial load
- Smooth scrolling
- No memory leaks on repeated script changes
- Instant tab switching (with ControlRecycling)

**Why this matters:** Previous browser control selection was limited by this constraint

---

## 2. Text Formatting Requirements

### From XSL Stylesheets (tipitaka-*.xsl)

#### Paragraph Styles
All TEI `<p>` elements with `@rend` attribute must render correctly:

| TEI Element | CSS Class | Description | Example |
|-------------|-----------|-------------|---------|
| `<p rend="bodytext">` | `.bodytext` | Standard paragraph | `font-size: 12pt; text-indent: 2em` |
| `<p rend="hangnum">` | `.hangnum` | Hanging number paragraph | `margin-bottom: -14.4pt; text-indent: 2em` |
| `<p rend="unindented">` | `.unindented` | No indent | `font-size: 12pt` |
| `<p rend="indent">` | `.indent` | Extra indent | `text-indent: 2em; margin-left: 3em` |
| `<p rend="centre">` / `<trailer rend="centre">` | `.centered` | Centered text | `text-align:center` |

#### Heading Styles
Multiple heading levels with different sizes and formatting:

| TEI Element | CSS Class | Description | Font Size |
|-------------|-----------|-------------|-----------|
| `<p rend="nikaya">` | `.nikaya` | Top-level heading | 24pt, bold, centered |
| `<p rend="book">` / `<head rend="book">` | `.book` | Book title | 21pt, bold, centered |
| `<p rend="chapter">` / `<head rend="chapter">` | `.chapter` | Chapter heading | 18pt, bold, centered |
| `<p rend="title">` / `<head rend="title">` | `.title` | Section title | 12pt, bold, centered |
| `<p rend="subhead">` | `.subhead` | Subsection | 12pt, bold, centered |
| `<p rend="subsubhead">` / `<head rend="subsubhead">` | `.subsubhead` | Sub-subsection | 12pt, bold, centered |

#### Gatha (Verse) Formatting
Pali verses with specific indentation patterns:

| TEI Element | CSS Class | Description |
|-------------|-----------|-------------|
| `<p rend="gatha1">` | `.gatha1` | First line | `margin-left: 4em; margin-bottom: 0em` |
| `<p rend="gatha2">` | `.gatha2` | Second line | `margin-left: 4em; margin-bottom: 0em` |
| `<p rend="gatha3">` | `.gatha3` | Third line | `margin-left: 4em; margin-bottom: 0em` |
| `<p rend="gathalast">` | `.gathalast` | Last line | `margin-left: 4em; margin-bottom: 0.5cm` |

#### Inline Formatting

| TEI Element | CSS Class | Description | Style |
|-------------|-----------|-------------|-------|
| `<hi rend="bold">` | `.bld` | Bold text | `font-weight: bold` |
| `<hi rend="paranum">` | `.paranum` | Paragraph numbers | `font-weight: bold` |
| `<hi rend="dot">` | (none) | Dot above character | Passthrough |
| `<note>` | `.note` | Footnotes | `color: blue` (light mode), `#7aa2f7` (dark mode), wrapped in `[brackets]` |

#### Search Highlighting
**Two-color highlighting system** (critical for phrase/proximity search):

| TEI Element | CSS Class | Purpose | Light Mode | Dark Mode |
|-------------|-----------|---------|------------|-----------|
| `<hi rend="hit" id="hit{N}">` | `.hit` | Primary search term | `background: blue; color: white` | `background: #0066cc; color: white` |
| `<hi rend="context">` | `.context` | Context words (proximity) | `background: green; color: white` | `background: #2d7a2d; color: white` |

**Requirements:**
- Each hit must have unique ID (`hit1`, `hit2`, etc.) for navigation
- Support scrolling to specific hit by ID
- Support toggling hit visibility (show/hide)

#### Font Families
Default font stack (overrideable by user in future):
```css
font-family: "Times Ext Roman", "Indic Times", "Doulos SIL", Tahoma, "Arial Unicode MS", Gentium;
```

**Requirement:** Must support complex script rendering (Devanagari ligatures, Myanmar stacking, etc.)

---

## 3. Anchor System Requirements

### Page Break Anchors
**TEI:** `<pb ed="V" n="1.0023">`
**HTML:** `<a name="V1.0023"></a>`

**Required formats:**
- VRI edition: `V{volume}.{page}` (e.g., `V1.0023`)
- Myanmar edition: `M{volume}.{page}`
- PTS edition: `P{volume}.{page}`
- Thai edition: `T{volume}.{page}`
- Other editions: `O{volume}.{page}`

**Parsing requirement:** Convert anchor names to display format
- `V1.0023` → "1.23" (strip leading zeros from page)
- `V0.0001` → "1" (strip "0." for volume 0)

### Paragraph Anchors
**TEI:** `<p n="123">`
**HTML:** `<a name="para123"></a>`

**For Multi books:**
```xml
<div type="book" id="an4">
  <p n="123">
```
**Generates TWO anchors:**
- `<a name="para123"></a>` (simple)
- `<a name="para123_an4"></a>` (with book code)

**Requirement:** GoTo feature must support navigation to:
- Paragraph by number: `para123`
- Paragraph with book code: `para123_an4`
- Page references: `V1.23`, `M1.45`, etc.

### Chapter/Section Anchors
**TEI:** `<div id="chapter123">`
**HTML:** `<a name="chapter123"></a>`

**Requirement:** Chapter dropdown navigation must jump to these anchors

---

## 4. Bidirectional Chapter Dropdown

### Current Implementation
- **UI:** ComboBox in BookDisplayView.axaml
- **Data Source:** `Chapters` ObservableCollection (loaded from ChapterListsService)
- **ViewModel:** `SelectedChapter` property with ReactiveUI binding

### Bidirectional Requirements

**User → View (Dropdown selection changes scroll position)**
1. User selects chapter from dropdown
2. `SelectedChapter` property updates
3. ViewModel fires `NavigateToChapterRequested` event
4. View scrolls to chapter anchor in HTML
5. JavaScript detects scroll and updates current position (next requirement)

**View → User (Scrolling updates dropdown)**
1. User scrolls in book content
2. JavaScript detects scroll position
3. JavaScript calls `window.cstChapterTracking.updateCurrentChapter()`
4. C# callback: `BookDisplayView.OnChapterChanged(string chapterId)`
5. ViewModel: `UpdateCurrentChapter(chapterId)` updates `SelectedChapter`
6. UI dropdown updates automatically (ReactiveUI binding)

**Critical:** Must prevent navigation loops
- `_updatingChapterFromScroll` flag prevents dropdown changes from triggering scroll

**Alternative Rendering Requirement:**
Any rendering engine must support this bidirectional binding pattern:
- Scroll position → callback to C# → update dropdown
- Dropdown change → C# scroll command → update view position

---

## 5. GoTo Feature Requirements

From CLAUDE.md Outstanding Work:

### Navigation Types
**Must support navigation to:**

1. **Paragraph by number**
   - Input: "123" → navigate to `para123` anchor
   - Multi-book: "123_an4" → navigate to `para123_an4` anchor

2. **Page references**
   - VRI: "1.23" → navigate to `V1.0023` anchor
   - Myanmar: "2.45" → navigate to `M2.0045` anchor
   - PTS, Thai, Other: Similar format

3. **Chapter/Section**
   - Input: Chapter name or ID → navigate to div anchor

### UI Requirements
- Dialog or input box for user entry
- Validation of input format
- Error messages for invalid references
- History of recent GoTo locations

### Technical Requirement
**Any rendering engine must support:**
- Programmatic scroll to arbitrary anchor by name
- Ability to find anchor by partial match (e.g., user types "1.23", find "V1.0023")

---

## 6. Status Bar Tracking Requirements

### Real-Time Position Detection
**As user scrolls, status bar must show:**

| Field | Source | Format | Example |
|-------|--------|--------|---------|
| VRI Page | First visible `<a name="V*">` | Parse "V1.0023" → "1.23" | "1.23" |
| Myanmar Page | First visible `<a name="M*">` | Parse "M2.0045" → "2.45" | "2.45" |
| PTS Page | First visible `<a name="P*">` | Parse "P3.0012" → "3.12" | "3.12" |
| Thai Page | First visible `<a name="T*">` | Parse "T1.0089" → "1.89" | "1.89" |
| Other Page | First visible `<a name="O*">` | Parse "O2.0001" → "2.1" | "2.1" |
| Paragraph | First visible `<a name="para*">` | Parse "para123" → "123" | "123" |

### Current Implementation (JavaScript Bridge)
```javascript
window.cstStatusUpdate = {
    updateStatus: function() {
        // Find first visible anchor for each type
        var vriAnchor = findFirstVisibleAnchor('V');
        var myanmarAnchor = findFirstVisibleAnchor('M');
        // ... etc

        // Call C# callback
        window.external.invoke(JSON.stringify({
            type: 'STATUS_UPDATE',
            vri: vriAnchor,
            myanmar: myanmarAnchor,
            // ... etc
        }));
    }
};

// Triggered on scroll with debouncing
window.addEventListener('scroll', debounce(function() {
    window.cstStatusUpdate.updateStatus();
}, 100));
```

### Alternative Rendering Requirement
Must support either:
1. **JavaScript bridge** for scroll position detection (like current)
2. **Native scroll events** with ability to query visible elements
3. **Manual position tracking** (less ideal - may be laggy)

---

## 7. Search Hit Navigation

### Current Implementation
**JavaScript object:** `window.cstSearchHighlights`

```javascript
window.cstSearchHighlights = {
    hits: [],  // Array of <span class="hit"> elements
    currentIndex: 0,

    init: function() {
        this.hits = Array.from(document.querySelectorAll('span.hit'));
    },

    navigateToHit: function(index) {
        if (index < 1 || index > this.hits.length) return;
        var hit = this.hits[index - 1];
        hit.scrollIntoView({ behavior: 'smooth', block: 'center' });
    },

    showHits: function(visible) {
        // Toggle background color via CSS manipulation
    }
};
```

### Requirements
**Triggered by C# commands:**
- First Hit button → `navigateToHit(1)`
- Previous Hit button → `navigateToHit(currentIndex - 1)`
- Next Hit button → `navigateToHit(currentIndex + 1)`
- Last Hit button → `navigateToHit(totalHits)`

**Features:**
- Smooth scrolling to hit element
- Center hit in viewport
- Support show/hide hits (toggle background color)

### Alternative Rendering Requirement
Must support:
1. **Finding elements by ID** (`hit1`, `hit2`, etc.)
2. **Scrolling element into view** with centering
3. **Dynamic CSS manipulation** (show/hide highlights)

---

## 8. Show/Hide Toggles

### Footnote Visibility
**JavaScript:**
```javascript
window.cstSearchHighlights.showFootnotes = function(visible) {
    getStyleClass('note').style.display = (visible ? 'inline' : 'none');
};
```

**Requirement:** Dynamically toggle CSS `display` property for `.note` class

### Search Hit Visibility
**JavaScript:**
```javascript
window.cstSearchHighlights.showHits = function(visible) {
    var hitClass = getStyleClass('hit');
    if (visible) {
        hitClass.style.backgroundColor = 'blue';
        hitClass.style.color = 'white';
    } else {
        hitClass.style.backgroundColor = 'white';
        hitClass.style.color = 'black';
    }
};
```

**Requirement:** Dynamically toggle CSS background/color for `.hit` class

### Alternative Rendering Requirement
Must support dynamic CSS class manipulation without full re-render

---

## 9. Dark Mode Support

### System Integration
**Requirement:** Detect system dark mode preference
- macOS: `@media (prefers-color-scheme: dark)`
- Windows: Similar system query

### Color Schemes
**Light Mode:**
```css
body { background: white; color: black; }
.note { color: blue; }
.hit { background-color: blue; color: white; }
.context { background-color: green; color: white; }
```

**Dark Mode:**
```css
body { background: black; color: white; }
.note { color: #7aa2f7; }  /* Lighter blue */
.hit { background-color: #0066cc; color: white; }  /* Darker blue */
.context { background-color: #2d7a2d; color: white; }  /* Darker green */
```

### Alternative Rendering Requirement
Must support:
1. Detecting system color scheme
2. Applying different styles based on scheme
3. Hot-swapping when user changes system preference (no restart)

---

## 10. Script Conversion Requirements

### Script Switching
**User can change script per-tab:**
- Current script: Devanagari
- User selects: Thai
- Application must:
  1. Convert XML from Devanagari → Thai
  2. Re-apply XSL transformation
  3. Re-render HTML
  4. **Preserve scroll position** (scroll to same VRI page anchor)

### Position Preservation
**Current implementation:**
1. Before script change: Get current page anchor (`GetCurrentPageAnchor()`)
   - Example: "V1.0023"
2. Convert XML and regenerate HTML
3. After render: Scroll to saved anchor (`ScrollToPageAnchor("V1.0023")`)

**Critical:** Script changes are frequent (user testing different scripts)
- Must be fast (< 1 second for 3.6 MB book)
- Must not leak memory on repeated changes
- Must preserve exact reading position

### Alternative Rendering Requirement
Must support:
1. Complete content replacement without control recreation
2. Programmatic scroll to anchor after replacement
3. Efficient repeated re-rendering (no memory leaks)

---

## 11. Copy/Paste and Text Selection Requirements

### Copy Selection
**User triggers:**
- Menu: Edit → Copy
- Keyboard: Cmd+C (macOS) / Ctrl+C (Windows)
- Context menu: Right-click → Copy

**Requirement:**
- Copy selected text to system clipboard
- Preserve text formatting (optional: could be plain text)
- Handle complex scripts correctly (Devanagari ligatures, Myanmar stacking)

### Select All
**User triggers:**
- Menu: Edit → Select All
- Keyboard: Cmd+A (macOS) / Ctrl+A (Windows)

**Requirement:**
- Select entire book content
- Visual selection highlight

### Get Selected Text (Dictionary Feature)
**From CLAUDE.md Outstanding Work - Dictionary Feature:**

**User workflow:**
1. User selects a Pali word in book content
2. User triggers dictionary lookup:
   - Right-click → "Look up in Dictionary"
   - Keyboard shortcut (TBD)
   - Menu: Tools → Dictionary Lookup
3. Application gets selected text programmatically
4. Dictionary panel opens with lookup results

**Requirements:**
- **Programmatic access to selection:** Must be able to get selected text as string (not just clipboard)
- **Plain text extraction:** Strip formatting tags (`<hi>`, `<note>`, etc.)
- **Script-aware:** Selected text is in current script (Devanagari, Thai, etc.)
- **Word boundaries:** For double-click selection, respect Pali word boundaries
- **Empty selection handling:** Gracefully handle case where nothing is selected

**Technical requirement:**
```csharp
// Must support something like:
string selectedText = bookView.GetSelectedText();
if (!string.IsNullOrEmpty(selectedText))
{
    // Look up in Pali-English or Pali-Hindi dictionary
    dictionaryPanel.LookupWord(selectedText, currentScript);
}
```

**Current implementation (CEF WebView):**
```csharp
// Uses JavaScript bridge to get selection
var script = @"
    (function() {
        var selection = window.getSelection();
        return selection ? selection.toString() : '';
    })();
";
var result = await _webView.EvaluateScriptAsync(script);
```

**Difference from Copy/Paste:**
- Copy: User explicitly puts text on clipboard (OS-level operation)
- Dictionary: Application programmatically queries current selection (no clipboard)

**Why this matters:**
- User may want to look up multiple words without repeatedly copying
- Selection should remain active after lookup (no clipboard pollution)
- Dictionary may need to clean up text (remove footnotes, formatting)

### Alternative Rendering Requirement
Must support:
1. **Native clipboard integration** for Copy/SelectAll:
   - For Avalonia controls: Built-in `Copy()` and `SelectAll()` commands
   - For custom rendering: Implement clipboard API manually

2. **Programmatic selection access** for Dictionary:
   - For Avalonia controls: `TextBlock.SelectedText` property
   - For custom rendering: Track selection state and provide getter
   - For HTML renderer: JavaScript bridge or selection API

---

## 12. Linked Book Navigation (Mula/Atthakatha/Tika)

### Feature Overview
**CST organizes Pali texts in three commentary levels:**
- **Mula** (root text) - Original canonical texts
- **Atthakatha** (commentary) - Traditional commentaries on root texts
- **Tika** (sub-commentary) - Commentaries on the commentaries

**User workflow:**
1. User is reading a Mula text at paragraph 123
2. User clicks "Open Atthakatha" button
3. Application opens the linked commentary book
4. Application navigates to the corresponding paragraph in commentary
5. **User's reading position is preserved across books**

### Current Implementation
**UI:** Three buttons in BookDisplayView toolbar
- "Mula" button (enabled when `HasMula == true`)
- "Attha" button (enabled when `HasAtthakatha == true`)
- "Tika" button (enabled when `HasTika == true`)

**Book metadata:**
```csharp
public class Book {
    public int MulaIndex { get; set; }        // Index of linked Mula book
    public int AtthakathaIndex { get; set; }  // Index of linked Atthakatha
    public int TikaIndex { get; set; }        // Index of linked Tika
    public BookType BookType { get; set; }    // Whole, Multi, Split
}
```

### Technical Requirements

#### Step 1: Get Current Reading Position
**Must determine current paragraph anchor:**
```csharp
string currentParagraph = await GetCurrentParagraphAnchorAsync();
// Returns: "para123" or "para123_an4" (for Multi books)
```

**Methods:**
1. **Preferred:** Track continuously via scroll events (current implementation)
   - JavaScript detects scroll position
   - Calls C# callback with current paragraph
   - ViewModel maintains `CurrentParagraph` property
2. **Fallback:** Query on-demand via JavaScript
   - Find first visible `<a name="para*">` anchor
   - Extract paragraph number

#### Step 2: Handle Book Type Conversions
**Different book types require anchor transformation:**

| Source Book Type | Target Book Type | Transformation | Example |
|------------------|------------------|----------------|---------|
| Whole | Whole | Direct mapping | `para123` → `para123` |
| Multi | Whole | Strip book code | `para123_an4` → `para123` |
| Whole | Multi | May need book code | `para123` → `para123_an4` (context-dependent) |
| Whole | Split | Complex mapping | Requires special handling |
| Split | Whole | Complex mapping | Requires special handling |

**Code example (from BookDisplayViewModel.cs:1449-1468):**
```csharp
if (sourceBook.BookType == BookType.Multi && targetBook.BookType == BookType.Whole)
{
    // Extract base paragraph number without book code
    // "para123_an4" → "para123"
    if (currentAnchor.Contains("_"))
    {
        var parts = currentAnchor.Split('_');
        currentAnchor = parts[0];
    }
}
```

#### Step 3: Open Target Book and Navigate
**Application must:**
1. Open target book in new tab
2. Wait for content to load
3. Navigate to calculated paragraph anchor
4. Show target book to user

### Why This Is a Distinct Requirement

**While it uses the same underlying capabilities as other features, it has unique challenges:**

1. **Cross-book position preservation** - Not just scrolling within a book, but maintaining context across different books
2. **Book type conversion logic** - Complex rules for transforming anchors between book types
3. **User expectation** - Users expect seamless navigation between related texts (this is a core CST workflow)
4. **Paragraph numbering system** - The entire TEI XML paragraph numbering (`@n` attributes) is designed to support this feature

### Rendering Requirements

**Any rendering engine must support:**

1. **Get current paragraph anchor** (one of):
   - Continuous tracking via scroll events → C# callback
   - On-demand query via API call
   - Manual tracking in C# (for native rendering)

2. **Navigate to paragraph anchor in newly loaded book:**
   - Same requirement as GoTo feature (Section 5)
   - Must work after content loads (async operation)

3. **Fast content loading:**
   - User clicks button, new book opens, navigation happens
   - Should feel instant (< 1 second total)
   - Related to ControlRecycling for instant tab switches

### Alternative Rendering Evaluation

**For HTML-based rendering:**
- ✅ Easy - JavaScript can detect current paragraph and scroll to target
- ✅ Already implemented with current CEF approach

**For native Avalonia rendering:**
- ⚠️ Moderate - Must track scroll position and find first visible paragraph
- ⚠️ Must implement anchor-based navigation
- ✅ No book type conversion logic changes (that's in C# already)

**For HtmlRenderer.Avalonia:**
- ⚠️ Unknown - Need to verify it supports scroll event callbacks
- ⚠️ Need to verify it can programmatically scroll to anchors

### Related Features
This feature relies on the same underlying capabilities as:
- **Status bar paragraph tracking** (Section 6) - Both need current paragraph
- **GoTo navigation** (Section 5) - Both need to navigate to paragraph anchors
- **Session restoration** (Section 15) - Same position preservation concept

But it's **uniquely important** because:
- It's a primary workflow in Pali text study
- CST4 users expect this feature
- The paragraph numbering system exists specifically to support it
- Book type conversions add complexity not present in other features

---

## 13. Performance Requirements

### Initial Load
- **Target:** < 500ms for 3.6 MB book (largest)
- **Measurement:** Time from LoadHtml() call to content visible
- **Current:** ~300-500ms with CEF WebView

### Scrolling Performance
- **Target:** 60 FPS smooth scrolling
- **No stutter** when scrolling through large books
- **Fast anchor jumps:** < 100ms to scroll to specific anchor

### Memory Usage
- **Per book tab:** < 100 MB (including rendered content)
- **No leaks:** Repeated script changes should not accumulate memory
- **Cleanup:** Properly dispose resources when tab closes

### Tab Switching (with ControlRecycling)
- **Target:** Instant (0ms perceived delay)
- **Requirement:** Preserve scroll position across tab switches
- **Critical:** This is a major UX feature - **cannot be lost**

**Why ControlRecycling is critical:**
- Large books (3.6 MB) take 300-500ms to render
- Without recycling: Visible scroll animation on every tab switch (jarring UX)
- With recycling: View instance preserved, instant switching

---

## 14. Keyboard Shortcuts

### Required Shortcuts
| Action | macOS | Windows/Linux | Handled By |
|--------|-------|---------------|------------|
| Copy | Cmd+C | Ctrl+C | View (native or custom) |
| Select All | Cmd+A | Ctrl+A | View (native or custom) |
| Find | Cmd+F | Ctrl+F | Application (opens search panel) |
| Next Hit | Cmd+G | Ctrl+G | Application (search navigation) |
| Previous Hit | Cmd+Shift+G | Ctrl+Shift+G | Application (search navigation) |

**Current issue:** CEF WebView captures keyboard events
- Application has to intercept at tunnel level
- Complex event handling logic to route to correct handler

**Alternative Rendering Requirement:**
- Native controls: Keyboard handling is simpler
- Custom rendering: Must implement keyboard event routing

---

## 15. Multi-Window Support (Floating Books)

### Dock.Avalonia Integration
**User can:**
- Float book tab to separate window
- Drag tab back to main window
- Resize floating windows
- Close floating windows

**Current issue:** CEF crashes when moving between windows
- Native window handles cannot be reparented
- ControlRecycling + CEF = incompatible

**Alternative Rendering Requirement:**
Must support:
1. **View reparenting** (moving between windows) OR
2. **View recreation** with state preservation (scroll position, highlights)

**Ideal:** Avalonia native controls support reparenting automatically

---

## 16. Session Restoration Requirements

### What Must Restore
**On application restart:**
- Open book tabs (by filename and script)
- Search highlights (search terms and positions)
- Scroll positions (VRI page anchors)
- Selected chapter in dropdown
- Window positions and sizes

**Current implementation:**
- `ApplicationStateService` saves state to JSON
- Search terms/positions restored ✅
- **Scroll position NOT restored** (known limitation - planned for Beta 4)

**Alternative Rendering Requirement:**
Must support:
1. Saving current scroll position as anchor name or pixel offset
2. Restoring scroll position after content loads

---

## 17. JavaScript Bridge Requirements (Current Implementation)

### C# → JavaScript Calls
**WebView.ExecuteScript(script):**
- Navigate to hit: `window.cstSearchHighlights.navigateToHit(5);`
- Scroll to anchor: `document.querySelector('a[name="para123"]').scrollIntoView();`
- Show/hide hits: `window.cstSearchHighlights.showHits(false);`
- Show/hide footnotes: `window.cstSearchHighlights.showFootnotes(true);`

### JavaScript → C# Callbacks
**window.external.invoke() or custom bridge:**
```javascript
// Status update
window.external.invoke(JSON.stringify({
    type: 'STATUS_UPDATE',
    tabId: '__TAB_ID__',
    vri: 'V1.0023',
    myanmar: 'M2.0045',
    para: 'para123'
}));

// Chapter changed
window.external.invoke(JSON.stringify({
    type: 'CHAPTER_CHANGED',
    tabId: '__TAB_ID__',
    chapterId: 'chapter123'
}));
```

**C# handler:**
```csharp
public void OnJavaScriptCallback(string json) {
    var message = JsonSerializer.Deserialize<BridgeMessage>(json);
    switch (message.Type) {
        case "STATUS_UPDATE":
            _viewModel.UpdatePageReferences(...);
            break;
        case "CHAPTER_CHANGED":
            _viewModel.UpdateCurrentChapter(message.ChapterId);
            break;
    }
}
```

### Alternative Rendering Requirement
**If using native Avalonia controls:**
- No JavaScript bridge needed
- Use C# events and properties directly
- Simpler, more robust, easier to debug

**If using HTML renderer (like HtmlRenderer.Avalonia):**
- May need alternative callback mechanism
- Or implement features in C# (scroll tracking, etc.)

---

## 18. Alternative Rendering Engine Evaluation Checklist

Use this checklist to evaluate any proposed rendering solution:

### Basic Rendering
- [ ] Supports all paragraph styles (bodytext, centered, gatha, etc.)
- [ ] Supports all inline formatting (bold, footnotes, highlights)
- [ ] Supports font families for complex scripts
- [ ] Handles 3.6 MB HTML documents efficiently
- [ ] Smooth 60 FPS scrolling
- [ ] Dark mode support with system detection

### Navigation
- [ ] Scroll to arbitrary anchor by name
- [ ] Find element by ID for search hit navigation
- [ ] Bidirectional chapter dropdown (scroll updates dropdown, dropdown scrolls view)
- [ ] GoTo feature: Navigate to paragraph, page, or chapter
- [ ] Linked book navigation (Mula/Atthakatha/Tika with position preservation)

### Status Tracking
- [ ] Detect first visible anchor during scroll (for VRI/Myanmar/PTS/Thai/Other pages)
- [ ] Detect current paragraph during scroll
- [ ] Detect current chapter during scroll
- [ ] Call C# callbacks with updated positions

### Search Features
- [ ] Two-color highlighting (blue for hits, green for context)
- [ ] Navigate to specific hit by index
- [ ] Show/hide hits dynamically
- [ ] Show/hide footnotes dynamically

### User Interaction
- [ ] Copy selected text to clipboard
- [ ] Select all text
- [ ] Programmatically get selected text (for dictionary lookup)
- [ ] Keyboard shortcuts (Cmd+C, Cmd+A, etc.)
- [ ] Handle focus correctly

### Performance
- [ ] Initial load < 500ms for 3.6 MB document
- [ ] Instant tab switching (with ControlRecycling or equivalent)
- [ ] No memory leaks on repeated script changes
- [ ] < 100 MB memory per tab

### Integration
- [ ] Works with Dock.Avalonia (float/unfloat without crashes)
- [ ] ControlRecycling compatible (or provides equivalent instant switching)
- [ ] Session restore (scroll position, highlights)
- [ ] Script switching with position preservation

### Development
- [ ] Simple API (compared to JavaScript bridge)
- [ ] Easy to debug (no CEF blackbox)
- [ ] Good documentation
- [ ] Active maintenance

### Size & Deployment
- [ ] Small app bundle (not 200 MB like CEF)
- [ ] Cross-platform (Windows, macOS, Linux)
- [ ] No external dependencies (or minimal)

---

## 19. Summary: Critical Non-Negotiable Requirements

**These features MUST work in any rendering solution:**

1. **Instant tab switching** (ControlRecycling or equivalent) - Major UX feature
2. **3.6 MB document support** - Largest book must render efficiently
3. **Bidirectional chapter dropdown** - User expects this from CST4
4. **Search hit navigation** - Core functionality
5. **Two-color highlighting** - Phrase/proximity search depends on this
6. **Status bar position tracking** - Must show current page/paragraph
7. **GoTo navigation** - Planned feature, must be possible
8. **Dark mode** - Already implemented, must preserve
9. **Script switching with position preservation** - Users frequently change scripts
10. **Floating windows** (Dock.Avalonia) - Must not crash
11. **All 14 Pali scripts** - Complex script rendering (Devanagari, Myanmar, etc.)
12. **Copy/paste** - Basic user expectation
13. **Get selected text** - Required for dictionary lookup feature (planned)
14. **Linked book navigation** - Mula/Atthakatha/Tika with position preservation (core CST workflow)

**If an alternative cannot meet ALL of these, it's not viable.**

---

## 20. Next Steps

With these requirements documented, we can now evaluate:
1. **HtmlRenderer.Avalonia** - Can it handle our HTML/CSS?
2. **Native Avalonia rendering** - Can we implement all features in pure C#?
3. **Avalonia Accelerate NativeWebView** - Does commercial solution preserve all features?
4. **Other alternatives** - Any other options?

Each evaluation should check against the complete requirements in Section 17.
