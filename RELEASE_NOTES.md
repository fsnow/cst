# CST Reader 5.0.0-beta.4

**Release Date:** June 2026

## What's New

### New Features
- **View Source PDF** — view the Burmese 1957 and 2010 edition source PDFs in dockable tabs (rendered via PDFium), opening to the page that matches your current spot in the book.
- **Go To navigation (Cmd+G)** — jump to a page/paragraph in the active book.
- **All 14 scripts for search input** — enter queries in any of the 14 supported Pali scripts.

### Search
- **Phrase, proximity, and mixed queries** — quoted phrases of any length, all-within-a-window proximity, and combinations of phrases + loose words; plus wildcard (`*`/`?`) and regular-expression search across all scripts.
- **Two-color hit highlighting** with occurrence-by-occurrence Next/Prev navigation; search highlights and navigation state restored across sessions.

### Stability & Quality
- **Dock subsystem hardening** — every dock now gets a stable unique id, the document-area "spine" is protected from accidental collapse, tool panels are recreated on demand (Show Search / Select-a-Book always work), and the View menu Hide/Show/Toggle now function. Upgraded to Dock 11.3.6.5 and Avalonia 11.3.6.
- A floated book's **tab title** stays in the current script (no longer reverts to Devanagari).
- **Window geometry** on restore is validated against the currently-connected screens (no more off-screen windows after a monitor change).
- **Search-result cache** is bounded; **resource leaks** (search-index readers, per-tab view models) fixed.
- **Script conversion** improvements: a direct IPE → Devanagari converter, converters re-encoded as valid UTF-8, and expanded round-trip + vowel-hiatus validation coverage.

### Bug Fixes
- Welcome-page startup banner no longer sticks on a fast startup. (#38)
- XML updates no longer index changed files twice, and no longer silently skip re-indexing a downloaded file. (#40)
- Floating window no longer disappears when re-docking another window.
- Corrected the Suttanipāta source-PDF start page.

## Known Issues
- **Intermittent crash when *dragging* a floated book back to the main window (#39).** If you drag a floated book's tab into the main window to unfloat it (instead of using the unfloat button) and then switch tabs, the app can crash. **Workaround: use the unfloat toolbar button** to bring a floated book back — it is reliable. A proper fix is planned for the next release.

## Installation

### macOS Requirements
- macOS 11.0 (Big Sur) or later
- Apple Silicon (M1/M2/M3/M4) or Intel processor

### Download
- **Apple Silicon:** `CST-Reader-arm64.dmg`
- **Intel:** `CST-Reader-x64.dmg`

### First Launch
If you see a security warning: open **System Settings → Privacy & Security**, scroll down, and click **Open Anyway**.

## Upgrade Note
**Before running Beta 4, delete the contents of `~/Library/Application Support/CSTReader/`** for a clean start with the new version.

---

**Full changelog:** https://github.com/fsnow/cst/compare/v5.0.0-beta.3...v5.0.0-beta.4
