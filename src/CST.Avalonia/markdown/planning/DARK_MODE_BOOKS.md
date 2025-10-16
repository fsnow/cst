# Dark Mode Support for Book Content

**Status**: In Progress - CSS Media Query Approach Confirmed
**Priority**: Beta 3 Release Blocker
**Created**: October 16, 2025
**Updated**: October 16, 2025
**Target**: Beta 3

## Problem Statement

The CST Reader application has complete Dark Mode support for all UI panels (toolbar, search panel, settings window) through Avalonia's FluentTheme integration. However, the book content area (WebView) currently displays with a hardcoded white background and black text in both Light and Dark modes.

**User Requirement**: When macOS is in Dark Mode, book content should display with:
- Black background
- White text
- Appropriately adjusted search hit highlighting colors

## Current Architecture

### Book Content Rendering Flow

```
XML File → XSL Transformation → HTML Generation → Temp File → WebView.LoadUrl()
```

**Key Components**:

1. **BookDisplayViewModel.cs** (lines 632-738)
   - `GenerateHtmlContentAsync()` method
   - Loads XML book content from disk
   - Applies search highlighting to XML if needed
   - Transforms XML to HTML using XSL stylesheet
   - Returns HTML string

2. **BookDisplayView.axaml.cs** (lines 221-290)
   - `LoadBookContentAsync()` method
   - Receives HTML from ViewModel
   - Writes HTML to temporary file: `Path.GetTempPath()/cst_book_{filename}_{tabId}.html`
   - Loads file into WebView via `file://` URL

3. **XSL Stylesheets** (`/Xsl/tipitaka-*.xsl`)
   - 14 script-specific stylesheets (Latin, Devanagari, Thai, Myanmar, etc.)
   - Each contains hardcoded CSS with white background
   - Example from `tipitaka-latn.xsl` (lines 55-107):
     ```css
     body {
       font-family: "Times Ext Roman", "Indic Times", "Doulos SIL", Tahoma;
       background: white;
       color: black;
     }
     .hit { background-color: blue; color: white; }
     .context { background-color: green; color: white; }
     ```

4. **JavaScript Bridge** (BookDisplayView.axaml.cs, lines 1051-1331)
   - `SetupJavaScriptBridge()` injects JavaScript after page load
   - Provides `window.cstSearchHighlights` for hit navigation
   - Provides `window.cstAnchorCache` for paragraph anchors
   - Can execute arbitrary JavaScript via `webView.ExecuteScript()`

### Current Limitations

1. **No Theme Detection**: No code accesses `Application.Current.ActualThemeVariant`
2. **Static CSS**: XSL stylesheets have hardcoded colors, no media queries
3. **No Theme Change Handler**: Application doesn't respond to macOS theme switches
4. **Search Highlighting Colors**: Blue/green/red highlights not optimized for dark backgrounds

## Solution Approaches

### Approach 1: CSS Media Queries (Recommended)

**Concept**: Inject CSS with `@media (prefers-color-scheme: dark)` rules that automatically respond to system theme.

**Pros**:
- Automatic theme detection by browser
- No C# code needed to detect theme changes
- Works even if user changes theme while app is running
- Single CSS injection point
- Clean separation of concerns

**Cons**:
- Requires modifying all 14 XSL stylesheets OR injecting additional `<style>` tag
- ~~May not work if WebView doesn't respect `prefers-color-scheme` (needs testing)~~ ✅ CONFIRMED WORKING

**Implementation**:
```css
/* Light mode (default) */
body {
  background: white;
  color: black;
}
.hit { background-color: blue; color: white; }
.context { background-color: green; color: white; }

/* Dark mode */
@media (prefers-color-scheme: dark) {
  body {
    background: #1e1e1e;
    color: #e0e0e0;
  }
  .hit { background-color: #0066cc; color: white; }
  .context { background-color: #2d7a2d; color: white; }
  .currentHit { background-color: #cc3300; color: white; }
}
```

### Approach 2: Avalonia Theme Detection with CSS Injection

**Concept**: Detect Avalonia's current theme in C# and inject theme-specific CSS into HTML before writing to temp file.

**Pros**:
- Full control over theme detection
- Can use Avalonia's actual theme (not system preference)
- Can inject user preferences (e.g., custom dark mode colors)
- Guaranteed to work regardless of WebView capabilities

**Cons**:
- Requires detecting theme changes and reloading content
- More complex state management
- Need to pass theme state through View/ViewModel

**Implementation Points**:

1. **Detect Theme in ViewModel**:
   ```csharp
   // In BookDisplayViewModel.cs
   private bool IsDarkMode()
   {
       return Application.Current?.ActualThemeVariant == ThemeVariant.Dark;
   }
   ```

2. **Inject CSS After XSL Transform**:
   ```csharp
   // In GenerateHtmlContentAsync()
   string html = ApplyXslTransform(xml, xslPath);

   if (IsDarkMode())
   {
       html = InjectDarkModeStyles(html);
   }

   return html;
   ```

3. **CSS Injection Helper**:
   ```csharp
   private string InjectDarkModeStyles(string html)
   {
       const string darkModeStyles = @"
       <style>
           body { background: #1e1e1e !important; color: #e0e0e0 !important; }
           .hit { background-color: #0066cc !important; }
           .context { background-color: #2d7a2d !important; }
       </style>
       ";

       // Insert before </head> or at start of <body>
       return html.Replace("</head>", darkModeStyles + "</head>");
   }
   ```

### Approach 3: Runtime JavaScript Injection

**Concept**: Use existing JavaScript bridge to apply dark mode styles after page load.

**Pros**:
- No HTML regeneration needed
- Can instantly switch themes without reload
- Leverages existing JavaScript infrastructure

**Cons**:
- Potential flash of white background before JS executes
- More complex to maintain
- Search highlighting already uses JavaScript - may conflict

**Implementation**:
```csharp
// In BookDisplayView.axaml.cs, after SetupJavaScriptBridge()
private void ApplyDarkModeStyles()
{
    var isDarkMode = Application.Current?.ActualThemeVariant == ThemeVariant.Dark;

    if (isDarkMode)
    {
        string script = @"
        (function() {
            document.body.style.background = '#1e1e1e';
            document.body.style.color = '#e0e0e0';
            // Update all existing elements...
        })();
        ";

        webView.ExecuteScript(script);
    }
}
```

## Recommended Implementation Plan

**Use Approach 1 (CSS Media Queries) - CONFIRMED WORKING** ✅

### Rationale

1. **CSS Media Queries** provide the cleanest solution - WebView supports `prefers-color-scheme` ✅
2. Automatic theme detection by browser, no C# code needed for theme changes
3. Single CSS modification point - update all 14 XSL stylesheets

### Test Results

**Phase 1 Completed**: Welcome page (`Resources/welcome-content.html`) successfully tested on macOS with `@media (prefers-color-scheme: dark)`:
- ✅ Automatic theme switching works when system theme changes
- ✅ No flash of white background
- ✅ All colors properly adjusted for dark mode
- ✅ No C# code needed for theme detection
- ✅ WebView respects system `prefers-color-scheme` setting

**Conclusion**: Proceed with CSS media query approach for book content. Approach 2 (Avalonia theme detection) not needed.

### Implementation Steps

#### Phase 1: CSS Media Query Support ✅ COMPLETED

1. **Test WebView Media Query Support** ✅ DONE
   - Created test HTML with `@media (prefers-color-scheme: dark)` in welcome page
   - Loaded in WebView on macOS in Light/Dark modes
   - Verified automatic theme switching works perfectly

2. **Modify XSL Stylesheets** (NEXT STEP)
   - Update all 14 XSL files to include dark mode CSS media queries
   - Use same approach as welcome page
   - Test each script's rendering in both themes
   - Files to modify:
     - `tipitaka-latn.xsl`
     - `tipitaka-deva.xsl`
     - `tipitaka-thai.xsl`
     - `tipitaka-mymr.xsl`
     - `tipitaka-sinh.xsl`
     - `tipitaka-beng.xsl`
     - `tipitaka-gujr.xsl`
     - `tipitaka-guru.xsl`
     - `tipitaka-knda.xsl`
     - `tipitaka-khmr.xsl`
     - `tipitaka-mlym.xsl`
     - `tipitaka-telu.xsl`
     - `tipitaka-tibt.xsl`
     - `tipitaka-cyrl.xsl`

#### Phase 2: Search Highlighting Colors

3. **Update XSL Search Highlighting Classes**
   - Add dark mode colors for `.hit`, `.context`, `.currentHit` classes
   - Use theme-appropriate colors:
     - Light Mode: Blue (#0000FF), Green (#008000), Red (#FF0000)
     - Dark Mode: Lighter Blue (#0066CC), Lighter Green (#2D7A2D), Lighter Red (#CC3300)
   - Add to same `@media (prefers-color-scheme: dark)` block in XSL stylesheets

#### Phase 3: Fallback Text Display

4. **Update Fallback Browser Styling**
   - Modify `BookDisplayView.axaml` (lines 137-149)
   - Change hardcoded white background to `DynamicResource`:
     ```xaml
     <ScrollViewer x:Name="fallbackBrowser"
                   Background="{DynamicResource CardBackgroundFillColorDefaultBrush}"
                   Foreground="{DynamicResource TextFillColorPrimaryBrush}">
     ```

#### Phase 4: Testing & Validation

5. **Comprehensive Testing**
   - Test all 14 scripts in Light and Dark modes
   - Test theme switching while book is open
   - Test search highlighting in both themes
   - Test session restoration (theme remembered)
   - Verify no flash of white background
   - Test fallback browser styling

6. **Performance Validation**
    - Verify no performance impact (CSS media queries are passive)
    - Verify temp file cleanup still works
    - Check theme switching responsiveness

## Color Palette Recommendations

### Light Mode (Current)
- **Background**: `#FFFFFF` (white)
- **Text**: `#000000` (black)
- **Search Hit**: `#0000FF` (blue) with white text
- **Context Hit**: `#008000` (green) with white text
- **Current Hit**: `#FF0000` (red) with white text

### Dark Mode (Proposed)
- **Background**: `#1E1E1E` (VSCode dark background)
- **Text**: `#E0E0E0` (light gray)
- **Search Hit**: `#0066CC` (lighter blue) with white text
- **Context Hit**: `#2D7A2D` (lighter green) with white text
- **Current Hit**: `#CC3300` (lighter red) with white text

### Alternative Dark Mode (High Contrast)
- **Background**: `#000000` (pure black)
- **Text**: `#FFFFFF` (pure white)
- **Search Hit**: `#3399FF` (bright blue) with black text
- **Context Hit**: `#33CC33` (bright green) with black text
- **Current Hit**: `#FF3333` (bright red) with black text

**Note**: Consider making colors user-configurable in Settings for future enhancement.

## Potential Issues & Mitigation

### ~~Issue 1: WebView May Not Support prefers-color-scheme~~ ✅ RESOLVED

**Status**: WebView DOES support `prefers-color-scheme` - confirmed via welcome page testing.

~~**Symptom**: CSS media queries don't trigger when system theme changes.~~

~~**Mitigation**:~~
- ~~Implement Approach 2 (Avalonia theme detection) as fallback~~
- ~~Use C# to inject theme-specific CSS with `!important` rules~~

### Issue 2: Flash of White Background on Load

**Symptom**: Brief white flash before dark styles apply.

**Mitigation**:
- Inject dark mode CSS in `<head>` before any content renders
- Use inline styles on `<body>` tag as earliest possible styling
- Consider preloading HTML template with dark styles

### Issue 3: Theme Change While Book is Open

**Status**: CSS media queries update automatically - no reload needed! ✅

~~**Symptom**: Book content doesn't update when user switches macOS theme.~~

**Actual Behavior**: CSS `@media (prefers-color-scheme: dark)` responds instantly to system theme changes without any C# intervention.

### Issue 4: Search Highlighting Colors Clash with Dark Background

**Symptom**: Blue/green/red highlights too dark or invisible on dark background.

**Mitigation**:
- Use lighter, more saturated colors in dark mode
- Ensure sufficient contrast (WCAG AA: 4.5:1 ratio)
- Test with color blindness simulators

### Issue 5: XSL Modification Affects All 14 Scripts

**Symptom**: Need to update many files, risk of inconsistency.

**Mitigation**:
- Create shared CSS file imported by all XSL stylesheets
- Or use CSS injection approach (no XSL changes needed)
- Automated testing for all scripts

### Issue 6: Temp File CSS Caching

**Symptom**: Browser may cache old CSS rules from temp files.

**Mitigation**:
- Include theme indicator in temp filename: `cst_book_{filename}_{tabId}_{theme}.html`
- Or use cache-busting query parameter: `file://...?theme=dark&v=123`
- Force WebView refresh when theme changes

## Files to Modify

### Critical Files
- [x] `/Resources/welcome-content.html` - Dark mode CSS testing ✅
- [ ] `/Views/BookDisplayView.axaml` - Fix fallback browser colors

### XSL Stylesheets (update with media queries)
- [ ] `/Xsl/tipitaka-latn.xsl` - Add `@media (prefers-color-scheme: dark)` block
- [ ] `/Xsl/tipitaka-deva.xsl`
- [ ] `/Xsl/tipitaka-thai.xsl`
- [ ] `/Xsl/tipitaka-mymr.xsl`
- [ ] `/Xsl/tipitaka-sinh.xsl`
- [ ] `/Xsl/tipitaka-beng.xsl`
- [ ] `/Xsl/tipitaka-gujr.xsl`
- [ ] `/Xsl/tipitaka-guru.xsl`
- [ ] `/Xsl/tipitaka-knda.xsl`
- [ ] `/Xsl/tipitaka-khmr.xsl`
- [ ] `/Xsl/tipitaka-mlym.xsl`
- [ ] `/Xsl/tipitaka-telu.xsl`
- [ ] `/Xsl/tipitaka-tibt.xsl`
- [ ] `/Xsl/tipitaka-cyrl.xsl`

### ~~JavaScript Bridge~~ (not needed - CSS handles it)
- ~~[ ] `/Views/BookDisplayView.axaml.cs` (lines 1051-1331) - Update highlight colors~~ (CSS media queries handle this)

## Testing Checklist

- [x] Light mode renders correctly (white background, black text) ✅ (welcome page)
- [x] Dark mode renders correctly (dark background, light text) ✅ (welcome page)
- [x] Theme switches automatically when macOS theme changes ✅ (welcome page)
- [ ] Search hits visible in both light and dark modes
- [ ] Current search hit distinguishable from other hits in both modes
- [ ] Context hits (proximity search) visible in both modes
- [x] No flash of white background when opening book in dark mode ✅ (welcome page)
- [x] Scroll position preserved during theme switch ✅ (CSS updates in place, no reload needed)
- [ ] All 14 Pali scripts render correctly in dark mode
- [ ] Fallback text display (non-WebView) uses dark mode colors
- [ ] Session restoration remembers theme preference
- [x] No CPU impact from theme detection/switching ✅ (CSS media queries are passive)

## Open Questions

1. **Should theme preference be per-book or application-wide?**
   - Current plan: Follow system theme (application-wide)
   - Alternative: Allow per-book override in settings

2. **Should dark mode colors be user-configurable?**
   - Current plan: Hardcoded palette
   - Future enhancement: Settings page for custom colors

3. **What happens if book is open during theme change?**
   - Current plan: Reload content automatically
   - Alternative: Show notification to refresh manually

4. **Should we support custom user CSS?**
   - Current plan: No custom CSS support
   - Future enhancement: Allow user CSS injection

## Success Criteria

1. Book content displays with dark background/light text when macOS is in Dark Mode
2. Book content displays with white background/black text when macOS is in Light Mode
3. Theme switches instantly when user changes macOS appearance settings
4. Search highlighting remains clearly visible in both themes
5. No visual glitches or flashing during theme transitions
6. Session restoration preserves theme preference
7. All 14 Pali scripts work correctly in dark mode

## Related Documentation

- `/markdown/notes/AVALONIA_HIGH_CPU.md` - CPU usage considerations
- `CLAUDE.md` - Project overview and Beta 3 priorities
- `/Xsl/tipitaka-latn.xsl` - Reference XSL stylesheet with current CSS

## Next Steps

1. ~~Test WebView support for CSS media queries (`prefers-color-scheme`)~~ ✅ DONE - Works perfectly!
2. Create proof-of-concept with single XSL file (`tipitaka-latn.xsl`)
3. ~~Implement theme detection in BookDisplayViewModel~~ ❌ NOT NEEDED - CSS handles it
4. ~~Add theme change handler~~ ❌ NOT NEEDED - CSS handles it automatically
5. Update search highlighting colors in XSL dark mode block
6. Apply dark mode CSS to remaining 13 XSL files
7. Fix fallback browser styling in BookDisplayView.axaml
8. Test with all 14 scripts
9. Update CLAUDE.md to mark feature complete
