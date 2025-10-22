# Beta 3 Testing Checklist

## Search Testing

### 1. Side-by-Side Results vs CST4
- **Objective**: Verify search results match CST4 exactly
- **Test Cases**:
  - Single word searches (common and rare terms)
  - Multi-word searches (exact phrases)
  - Wildcard searches
  - Regular expression searches
- **Method**:
  - Run identical searches in both CST4 and CST Reader Beta 3
  - Compare hit counts per book
  - Compare total hit counts
  - Verify no books are missing results
- **Expected Result**: Results should match exactly

### 2. Partially Bold Words
- **Objective**: Ensure search highlighting works correctly when search terms span CST's partial word bolding markup
- **Background**: CST XML uses `<b>` tags to bold parts of words (e.g., `<b>M</b>ahāsi` for "Mahāsi" where "M" is bold)
- **Test Cases**:
  - Search for words that contain both bold and non-bold segments
  - Verify highlighting spans across `<b>` tag boundaries
  - Check that bold formatting is preserved while highlighting is applied
- **Example Search Terms**:
  - Look for words with initial caps that might be bolded
  - Compound words where prefix might be bold
- **Expected Result**:
  - Search highlighting should work seamlessly across bold boundaries
  - Original bold formatting should remain intact
  - No broken highlighting or missing text

### 3. Words with Quotes in the Middle
- **Objective**: Ensure search highlighting works correctly when search terms span CST's quotation markup
- **Background**: CST XML uses quotation tags (e.g., `<q>...</q>`) that can split words
- **Test Cases**:
  - Search for words that contain quotation marks in the middle
  - Verify highlighting spans across quotation tag boundaries
  - Check that quotation formatting is preserved while highlighting is applied
- **Example Search Terms**:
  - Words with embedded quotes (if such cases exist in the texts)
  - Terms that might span quotation boundaries
- **Expected Result**:
  - Search highlighting should work seamlessly across quote boundaries
  - Original quotation formatting should remain intact
  - No broken highlighting or missing text

## Other Beta 3 Testing

### Icon Testing
- **Test**: Verify new squircle icon displays correctly in macOS
  - No white edges in dark mode
  - No white edges in light mode
  - Proper transparency at all icon sizes (16x16 through 1024x1024)
  - Correct display in Finder, Dock, About dialog, etc.

### Dark Mode Testing
- **Test**: Verify complete Dark Mode support
  - Book content displays with black background and white text in Dark Mode
  - Search highlighting is visible and properly color-inverted
  - All UI panels respect system Dark Mode setting
  - No visual glitches or incorrect colors

### Session Restoration
- **Test**: Verify search hit restoration works
  - Open multiple books with search hits highlighted
  - Quit application
  - Relaunch application
  - **Expected**: All books reopen with search hits still highlighted
  - **Current Status**: ⚠️ KNOWN BUG - Search hits not restored on startup

## Test Environment
- **macOS Version**:
- **CST Reader Version**: 5.0.0-beta.3
- **CST4 Version**: (for comparison testing)
- **Test Date**:

## Notes
- Document any discrepancies found during testing
- Capture screenshots of any issues
- Note performance differences if any
