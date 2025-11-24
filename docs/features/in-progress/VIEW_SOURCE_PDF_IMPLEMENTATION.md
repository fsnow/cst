# View Source PDF - Implementation Plan for CST.Avalonia

**Status**: Planning
**Date**: November 23, 2025
**Related CST4 Documentation**: `docs/features/planned/SHOW_SOURCE_PDF.md`

## 1. Executive Summary

This document outlines the implementation plan for the "View Source" feature that displays Burmese CST PDFs corresponding to the currently viewed Pali text. The implementation will leverage CEF's built-in PDF viewer (PDFium) and follow the same architectural patterns established by BookDisplayViewModel.

## 2. Technical Foundations

### 2.1 CEF PDF Support

**Confirmed Capability**: CEF includes built-in PDF viewing via Chromium's PDFium library.

- **No External Dependencies**: PDFs render natively without plugins
- **Fragment Support**: Standard URL fragments work (`#page=123`)
- **Extension-Based**: Uses PDF extension implementation (enabled by default)
- **Disable Flags**: Can be disabled via `--disable-pdf-extension` flag (we won't disable)

**Sources**:
- [CEF Forum - PDF viewer discussion](https://www.magpcss.org/ceforum/viewtopic.php?f=10&t=11107)
- [CEF Issues - PDF viewer implementation](https://bitbucket.org/chromiumembedded/cef/issues/1565/re-implement-pdf-viewer-using-out-of)
- [PSPDFKit CEF Guide](https://www.nutrient.io/guides/windows/other-languages/cef-chromiumembeddedframework/)

### 2.2 URL Migration

**Issue**: CST4's hardcoded URLs (tipitaka.org/_Source/) now redirect to SharePoint.

**Evidence**:
```
Old: https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/...
New: https://vipassanatrust-my.sharepoint.com/:f:/g/personal/help_tipitaka_org/...
Status: 301 Moved Permanently
```

**Solution**: Update all URLs in `CST.Core/Sources.cs` to new SharePoint locations.

**Impact**: All 50+ PDF URLs must be verified and updated.

## 3. Architecture Design

### 3.1 Component Overview

```
PdfDisplayViewModel (new)
├── Inherits: ReactiveDocument
├── Contains: WebView control for PDF rendering
├── Manages: Page navigation, toolbar state, float/unfloat
└── Services: SettingsService (for state persistence)

PdfDisplayView.axaml (new)
├── WebView control (docked to fill)
├── Simplified toolbar (no script/chapter/search controls)
├── Status bar (page info, loading indicator)
└── Float/Unfloat buttons (same as BookDisplayView)

BookDisplayViewModel (modified)
├── Add keyboard shortcuts: Ctrl+Q, Ctrl+E
├── Capture current Myanmar page from status bar
└── Request PDF opening via event/service
```

### 3.2 Code Reuse Strategy

**High Reuse (80%+ similar to BookDisplayViewModel)**:
- ReactiveDocument inheritance pattern
- WebView lifecycle management
- Float/Unfloat button implementation
- Toolbar/status bar structure
- IsFloating state management
- Dock.Avalonia integration

**Simplified (features removed)**:
- No script selection (PDFs are fixed format)
- No chapter navigation (PDF has its own bookmarks)
- No search highlighting (CEF PDF viewer has built-in search)
- No linked book navigation (Mula/Aṭṭha/Ṭīkā buttons)

**New Functionality**:
- Page offset calculation (PageStart + CurrentMyanmarPage)
- PDF URL construction with #page= fragment
- Source type selection (Burmese1957 vs Burmese2010)

### 3.3 Float/Unfloat CEF Crash Prevention

**Critical Requirement**: Use the same button-based float/unfloat approach as BookDisplayViewModel.

**Rationale**: BookDisplayViewModel.cs:76-82 demonstrates this pattern was implemented specifically to prevent CEF crashes during drag-based floating operations.

**Implementation**:
```csharp
// PdfDisplayViewModel
private bool _isFloating = false;
private WebViewLifecycleOperation _webViewLifecycleOperation = WebViewLifecycleOperation.None;
private WebViewState? _savedWebViewState = null;

// Float/Unfloat buttons in toolbar
public ReactiveCommand<Unit, Unit> FloatWindowCommand { get; }
public ReactiveCommand<Unit, Unit> UnfloatWindowCommand { get; }
```

## 4. Implementation Details

### 4.1 Update Sources.cs

**File**: `src/CST.Core/Sources.cs`

**Current Status**: 50+ hardcoded URLs pointing to old tipitaka.org domain (redirecting to SharePoint).

**Task**:
1. Verify new SharePoint URLs for all 50+ PDF files
2. Test that SharePoint URLs allow direct PDF access (not OneDrive web view)
3. Update all `addSource()` calls with new base URLs
4. Consider extracting base URL to constant for maintainability

**Example Update**:
```csharp
// OLD
addSource("s0101m.mul.xml", SourceType.Burmese1957, 19,
    "https://tipitaka.org/_Source/01%20-%20Burmese-CST/1957%20edition/2%20-%20Mula%20-%20Sutta/Sīlakkhandhavaggapāḷi.pdf");

// NEW (needs verification)
addSource("s0101m.mul.xml", SourceType.Burmese1957, 19,
    "https://vipassanatrust-my.sharepoint.com/...full-path.../Sīlakkhandhavaggapāḷi.pdf");
```

**Concern**: SharePoint URLs may require authentication or have special embedding restrictions. Need to test direct PDF access.

### 4.2 Create PdfDisplayViewModel

**File**: `src/CST.Avalonia/ViewModels/PdfDisplayViewModel.cs`

**Responsibilities**:
- Inherit from ReactiveDocument for docking system integration
- Manage WebView control for PDF rendering
- Handle float/unfloat operations with CEF crash prevention
- Track current PDF source (filename, URL, page offset)
- Provide toolbar state (page info, loading status)
- Persist state for session restoration

**Constructor Signature**:
```csharp
public PdfDisplayViewModel(
    string bookFilename,
    Sources.SourceType sourceType,
    int targetPage,
    ISettingsService? settingsService = null,
    CstDockFactory? dockFactory = null)
{
    // bookFilename: e.g., "s0101m.mul.xml"
    // sourceType: Burmese1957 or Burmese2010
    // targetPage: Calculated page number in PDF
}
```

**Key Properties**:
```csharp
// Display properties
public string PdfUrl { get; }  // Full URL with #page= fragment
public string BookTitle { get; }  // Derived from filename
public string StatusText { get; }  // "Viewing Burmese 1957 Edition - Page 23"
public bool IsLoading { get; }

// WebView lifecycle (same as BookDisplayViewModel)
public bool IsFloating { get; }
public WebViewLifecycleOperation WebViewLifecycleOperation { get; }
public WebViewState? SavedWebViewState { get; }

// Commands
public ReactiveCommand<Unit, Unit> FloatWindowCommand { get; }
public ReactiveCommand<Unit, Unit> UnfloatWindowCommand { get; }
```

**Page Calculation Logic**:
```csharp
private string BuildPdfUrl(string bookFilename, Sources.SourceType sourceType, int currentMyanmarPage)
{
    var source = Sources.Inst.GetSource(bookFilename, sourceType);
    if (source == null)
        return string.Empty;

    // CST4 formula: finalPage = source.PageStart + (currentMPage - 1)
    int pdfPage = source.PageStart + (currentMyanmarPage - 1);

    // Build URL with page fragment
    return $"{source.Url}#page={pdfPage}";
}
```

### 4.3 Create PdfDisplayView.axaml

**File**: `src/CST.Avalonia/Views/PdfDisplayView.axaml`

**Structure**: Simplified version of BookDisplayView.axaml

**Layout**:
```xml
<UserControl>
  <DockPanel>
    <!-- Simplified Toolbar -->
    <StackPanel DockPanel.Dock="Top">
      <!-- Source Type Indicator (read-only label) -->
      <TextBlock Text="Burmese 1957 Edition" />

      <!-- Spacer -->
      <Border HorizontalAlignment="Stretch" />

      <!-- Float/Unfloat Buttons (right-aligned) -->
      <Button Command="{Binding FloatWindowCommand}"
              IsVisible="{Binding !IsFloating}">↗</Button>
      <Button Command="{Binding UnfloatWindowCommand}"
              IsVisible="{Binding IsFloating}">↙</Button>
    </StackPanel>

    <!-- Status Bar -->
    <Grid DockPanel.Dock="Bottom">
      <TextBlock Text="{Binding StatusText}" />
      <ProgressBar IsIndeterminate="True"
                   IsVisible="{Binding IsLoading}" />
    </Grid>

    <!-- WebView for PDF -->
    <wv:WebView x:Name="webView"
                IsVisible="{Binding IsWebViewAvailable}"
                Focusable="True" />
  </DockPanel>
</UserControl>
```

**Differences from BookDisplayView**:
- **Removed**: Script selector, chapter dropdown, search navigation, linked book buttons
- **Kept**: Float/unfloat buttons, status bar, loading indicator
- **Simplified**: Toolbar only shows source type and window controls

### 4.4 Add Keyboard Shortcuts to BookDisplayView

**File**: `src/CST.Avalonia/Views/BookDisplayView.axaml.cs` (code-behind)

**Requirement**: Capture Ctrl+Q and Ctrl+E when BookDisplayView has focus.

**Implementation Options**:

**Option A - KeyDown Handler in View** (Recommended):
```csharp
// BookDisplayView.axaml.cs
private void BookDisplayView_KeyDown(object sender, KeyEventArgs e)
{
    if (e.KeyModifiers == KeyModifiers.Control)
    {
        if (e.Key == Key.Q)
        {
            (DataContext as BookDisplayViewModel)?.ShowSource1957Command.Execute(null);
            e.Handled = true;
        }
        else if (e.Key == Key.E)
        {
            (DataContext as BookDisplayViewModel)?.ShowSource2010Command.Execute(null);
            e.Handled = true;
        }
    }
}
```

**Option B - WebView JavaScript KeyPress** (CST4 approach):
```csharp
// Inject JavaScript to capture keypress in WebView content
await webView.ExecuteScriptAsync(@"
    document.addEventListener('keydown', function(e) {
        if (e.ctrlKey && e.key === 'q') {
            window.chrome.webview.postMessage({action: 'showSource', edition: '1957'});
        }
    });
");
```

**Recommendation**: Use Option A (Avalonia KeyDown) for simplicity and consistency with Avalonia's input model.

### 4.5 Implement ShowSource Logic in BookDisplayViewModel

**File**: `src/CST.Avalonia/ViewModels/BookDisplayViewModel.cs`

**New Commands**:
```csharp
public ReactiveCommand<Unit, Unit> ShowSource1957Command { get; }
public ReactiveCommand<Unit, Unit> ShowSource2010Command { get; }

// In constructor
ShowSource1957Command = ReactiveCommand.Create(() => ShowSource(Sources.SourceType.Burmese1957));
ShowSource2010Command = ReactiveCommand.Create(() => ShowSource(Sources.SourceType.Burmese2010));
```

**ShowSource Implementation**:
```csharp
private void ShowSource(Sources.SourceType sourceType)
{
    // Get current Myanmar page from status bar
    if (_myanmarPage == "*" || !int.TryParse(_myanmarPage, out int currentPage))
    {
        _logger.Warning("Cannot show source: Myanmar page number not available");
        return;
    }

    // Get source info from Sources.cs
    var source = Sources.Inst.GetSource(_book.Filename, sourceType);
    if (source == null)
    {
        _logger.Warning($"No {sourceType} source available for {_book.Filename}");
        return;
    }

    // Calculate target PDF page (CST4 formula)
    int pdfPage = source.PageStart + (currentPage - 1);

    // Request PDF window to be opened
    OpenPdfRequested?.Invoke(_book.Filename, sourceType, pdfPage);
}

// Event for requesting PDF window
public event Action<string, Sources.SourceType, int>? OpenPdfRequested;
```

**Myanmar Page Tracking**: Already implemented in BookDisplayViewModel.cs:58-62 via `_myanmarPage` field.

### 4.6 Wire Up PDF Display in CstDockFactory

**File**: `src/CST.Avalonia/ViewModels/Dock/CstDockFactory.cs`

**New Method**:
```csharp
public PdfDisplayViewModel CreatePdfDisplay(
    string bookFilename,
    Sources.SourceType sourceType,
    int targetPage)
{
    var viewModel = new PdfDisplayViewModel(
        bookFilename,
        sourceType,
        targetPage,
        _settingsService,
        this);

    // Generate unique ID for docking system
    viewModel.Id = $"PdfDisplay_{Guid.NewGuid()}";
    viewModel.Title = $"{GetBookTitle(bookFilename)} - {sourceType}";

    return viewModel;
}

private string GetBookTitle(string filename)
{
    // Extract readable title from book catalog
    // Example: "s0101m.mul.xml" -> "Sīlakkhandhavaggapāḷi"
    return _bookCatalog?.GetBookByFilename(filename)?.Title ?? filename;
}
```

**Integration with SimpleTabbedWindow**:
```csharp
// SimpleTabbedWindow.axaml.cs
private void BookDisplay_OpenPdfRequested(string filename, Sources.SourceType sourceType, int page)
{
    var pdfViewModel = _dockFactory.CreatePdfDisplay(filename, sourceType, page);

    // Add to center dock
    var centerDock = _layoutViewModel.Layout.ActiveDockable as IDock;
    _dockFactory.AddDockable(centerDock, pdfViewModel);
    _dockFactory.SetActiveDockable(pdfViewModel);
}
```

### 4.7 Session State Persistence

**File**: `src/CST.Avalonia/Services/ApplicationStateService.cs`

**New Model Classes**:
```csharp
// Add to ApplicationState.cs
public class PdfWindowState
{
    public string BookFilename { get; set; } = "";
    public string SourceType { get; set; } = "Burmese1957";  // Serialized enum
    public int CurrentPage { get; set; }
    public bool IsFloating { get; set; }
    public double X { get; set; }
    public double Y { get; set; }
    public double Width { get; set; }
    public double Height { get; set; }
}

// Add to ApplicationState
public List<PdfWindowState> OpenPdfWindows { get; set; } = new();
```

**Serialization Logic**:
```csharp
// In ApplicationStateService
public void SavePdfWindowState(PdfDisplayViewModel vm)
{
    var state = new PdfWindowState
    {
        BookFilename = vm.BookFilename,
        SourceType = vm.SourceType.ToString(),
        CurrentPage = vm.CurrentPage,
        IsFloating = vm.IsFloating,
        // ... bounds
    };

    _applicationState.OpenPdfWindows.Add(state);
    SaveState();
}

public void RestorePdfWindows(CstDockFactory factory)
{
    foreach (var state in _applicationState.OpenPdfWindows)
    {
        var sourceType = Enum.Parse<Sources.SourceType>(state.SourceType);
        var vm = factory.CreatePdfDisplay(
            state.BookFilename,
            sourceType,
            state.CurrentPage);

        // Restore window position/floating state
        // ...
    }
}
```

## 5. Implementation Sequence

### Phase 1: Foundation (Day 1)
1. ✅ Update Sources.cs with new SharePoint URLs
2. ✅ Verify PDF direct access works (not OneDrive web view)
3. ✅ Create basic PdfDisplayViewModel structure

### Phase 2: UI Components (Day 2)
4. ✅ Create PdfDisplayView.axaml with simplified toolbar
5. ✅ Implement float/unfloat buttons with CEF crash prevention
6. ✅ Wire up WebView PDF rendering

### Phase 3: Integration (Day 3)
7. ✅ Add keyboard shortcuts to BookDisplayView
8. ✅ Implement ShowSource logic in BookDisplayViewModel
9. ✅ Wire up CstDockFactory PDF creation
10. ✅ Connect OpenPdfRequested event in SimpleTabbedWindow

### Phase 4: Polish (Day 4)
11. ✅ Add session state persistence for PDF windows
12. ✅ Test page navigation accuracy
13. ✅ Verify float/unfloat state restoration
14. ✅ Test with multiple simultaneous PDF windows

## 6. Testing Strategy

### 6.1 Manual Testing Checklist

**Basic Functionality**:
- [ ] Open book in CST.Avalonia
- [ ] Navigate to specific Myanmar page (e.g., page 5)
- [ ] Press Ctrl+Q → PDF opens to correct page
- [ ] Verify page calculation: PDF page = PageStart + (MyanmarPage - 1)

**Float/Unfloat**:
- [ ] Float PDF window → WebView lifecycle managed correctly
- [ ] Unfloat PDF window → Returns to main window tab
- [ ] No CEF crashes during float/unfloat operations

**Multiple PDFs**:
- [ ] Open 3+ different books
- [ ] Show source for each → Each PDF in separate tab
- [ ] All PDFs render correctly side-by-side

**Session Restoration**:
- [ ] Open 2 books with PDF sources visible
- [ ] Restart application
- [ ] All PDF windows restore to correct pages
- [ ] Floating state preserved

### 6.2 Edge Cases

**Missing Source Data**:
- [ ] Book with no PDF source → Show graceful error message
- [ ] Myanmar page = "*" → Cannot calculate page, show warning

**URL Issues**:
- [ ] SharePoint authentication required → Detect and notify user
- [ ] PDF file not found (404) → Show error in WebView
- [ ] Network offline → WebView shows offline message

**CEF Behavior**:
- [ ] PDF with #page=999 (beyond end) → CEF handles gracefully
- [ ] PDF with Unicode filename → URL encoding works
- [ ] PDF with invalid page fragment → CEF ignores fragment

## 7. Known Risks & Mitigation

### Risk 1: SharePoint Authentication

**Problem**: SharePoint URLs may require authentication or only work in OneDrive web interface.

**Mitigation**:
1. Test direct PDF URL access in CEF before updating all URLs
2. If auth required, investigate SharePoint direct download links
3. Fallback: Host PDFs on tipitaka.org with proper redirects

### Risk 2: PDF Fragment Support in CEF

**Problem**: Some PDF viewers don't support #page= fragments.

**Mitigation**:
1. Test fragment navigation in CEF WebView
2. If not supported, investigate CEF PDF plugin APIs for page navigation
3. Fallback: Open PDF at page 1, show instruction to user

### Risk 3: Myanmar Page Availability

**Problem**: Myanmar page number may not be available for all books or at all scroll positions.

**Mitigation**:
1. Use best-effort page number from status bar
2. If "*", default to page 1 with warning log
3. Consider using VRI page or other page reference as fallback

## 8. Future Enhancements

### Post-Beta 4 Features

**Two-Way Sync** (Advanced):
- Detect PDF page changes via CEF JavaScript injection
- Update BookDisplayView scroll position to match PDF page
- Bidirectional navigation between text and source

**Annotation Support** (Research):
- Allow user to add bookmarks/notes in PDF
- Persist annotations in application state
- Display annotation indicators in BookDisplayView

**Alternative Sources**:
- Add Burmese2010 edition URLs (currently missing)
- Support VRI print edition PDFs if available
- Allow user to configure custom PDF sources

## 9. Documentation Updates

After implementation, update:
- `CLAUDE.md`: Move from Outstanding Work → Current Functionality
- Create `docs/features/implemented/ui/VIEW_SOURCE_PDF.md` (postmortem)
- Update user documentation with Ctrl+Q/Ctrl+E shortcuts
- Add troubleshooting section for SharePoint URL issues

## 10. SharePoint Authentication Blocker (November 23, 2025)

**Status**: ⚠️ **BLOCKED** - Waiting for VRI to provide web-accessible PDF URLs

**Test Results**:
- Created PDF test utility (Tools → Test PDF Access in CST.Avalonia)
- Tested SharePoint URL: `https://vipassanatrust-my.sharepoint.com/:b:/r/personal/help_tipitaka_org/Documents/_Source/...`
- **Result**: Microsoft login challenge required (403 Forbidden in CEF)
- **Confirmed**: SharePoint requires authentication, even with sharing links
- **User's browser**: Has active Microsoft login session, masking the issue
- **Clean browser test (Safari)**: Also shows login challenge

**VRI Request**: User has asked VRI to move PDFs to publicly accessible web location

**Next Steps**:
1. Wait for VRI to provide new public URLs
2. Test new URLs with PDF test utility (Tools menu)
3. Verify `#page=` fragment support works
4. Update all 50+ URLs in Sources.cs
5. Continue with View Source implementation

**Alternative Hosting Options** (if VRI delays):
- GitHub Releases (unlimited bandwidth for open source)
- Internet Archive (permanent hosting)
- DigitalOcean Spaces / AWS S3 (low-cost CDN)
- Self-hosted on tipitaka.org server

## 11. Open Questions

1. **Burmese2010 Edition**: Where are the 2010 edition PDFs? Do we defer this until URLs are available?
2. **Page Fragment Fallback**: If #page= doesn't work with new URLs, what's the best user experience?
3. **PDF Download Option**: Should we provide a "Download PDF" button for offline viewing?

## 11. References

- **CST4 Implementation**: `docs/features/planned/SHOW_SOURCE_PDF.md`
- **BookDisplayViewModel**: `src/CST.Avalonia/ViewModels/BookDisplayViewModel.cs:76-120`
- **ReactiveDocument**: `src/CST.Avalonia/ViewModels/Dock/ReactiveDocument.cs`
- **Float/Unfloat Logic**: BookDisplayViewModel implements CEF crash prevention
- **CEF PDF Support**: [CEF Forum Discussion](https://www.magpcss.org/ceforum/viewtopic.php?f=10&t=11107)
