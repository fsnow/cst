# CST Avalonia Project Status - November 2025

## Current Status: **BETA 4 IN DEVELOPMENT** 🚧

**Last Updated**: January 28, 2026
**Working Directory**: `[project-root]/src/CST.Avalonia`
**XML Books Location**: `/Users/fsnow/Library/Application Support/CSTReader/xml` (217 TEI XML book files)

## Project Overview

This project is a ground-up rewrite of the original WinForms-based CST4, built on Avalonia UI and .NET 9. The application is a cross-platform Pali text reader featuring a modern, dock-based IDE-style interface. The active codebase is now focused solely on the current architecture, with legacy and placeholder files moved to a separate directory for clarity.

CST stands for "Chaṭṭha Saṅgāyana Tipiṭaka". The texts we use are provided by the Vipassana Research Institute. 

Claude, do not use the word "Buddhist" in the application or any supporting documentation.

## Current Functionality

CST Reader is a modern, cross-platform Pali text reader featuring a complete implementation of the following systems:

### **Core Application Features**
- **Dock-Based IDE Interface**: Fully functional docking system with resizable panels, tab management, and persistent layout state
- **Complete Session Restoration**: Application saves and restores all open books, scripts, window positions, scroll positions, and search highlights across sessions
  - Scroll position captured every 200ms via scroll timer
  - Best anchor (paragraph/chapter/page) persists across float/unfloat operations
  - Deferred navigation for inactive tabs at startup
  - Immediate position capture on shutdown for latest accuracy
- **Button-Based Float/Unfloat**: Manual window lifecycle control prevents CEF crashes when floating/docking book tabs
- **Cross-Platform Build System**: Native macOS `.dmg` packages with proper application branding and menu integration
- **Code Signing & Security**: Developer ID signed applications and DMG installers for macOS without "damaged" application errors on first launch
- **Advanced Logging System**: Structured Serilog logging with configurable levels, unified across all components including CST.Lucene

### **Text Display & Script System**
- **Multi-Script Support**:
   - **Display**: All 14 Pali scripts supported (Devanagari, Latin, Bengali, Cyrillic, Gujarati, Gurmukhi, Kannada, Khmer, Malayalam, Myanmar, Sinhala, Telugu, Thai, Tibetan)
   - **Input**: All 14 scripts supported for search/dictionary input
   - **Indexing**: IPE (Ideal Pali Encoding) with Devanagari analyzers for accurate search
- **Per-Tab Script Selection**: Each book tab independently remembers and applies its script setting
- **Script Synchronization**: Search results and book tree automatically update display when global script changes
- **Script Conversion Validation & Quality Assurance**:
   - **Comprehensive Round-Trip Testing**: Automated validation tool tests Deva → IPE → Script → IPE → Script conversions for all 14 Pali scripts
   - **Corpus-Based Test Coverage**: 2,248 carefully selected words covering all unique syllable patterns and 140 medial independent vowel patterns found in the 217-book Pali corpus
   - **Multiple Testing Modes**:
     - `validate`: Round-trip conversion accuracy testing with HTML reports
     - `compare`: Compare CST converters against external converters (pnfo, Aksharamukha)
     - `analyze`: Deep analysis of individual problematic words
     - `extract`: Generate minimal test sets from full corpus
   - **Production Impact**: This validation framework enabled:
     - Rapid development of 5 missing input parsers (Bengali, Gujarati, Gurmukhi, Kannada, Malayalam)
     - Rapid development of 5 missing script converters (Khmer, Myanmar, Sinhala, Telugu, Tibetan)
     - Discovery and fix of longstanding bugs in converters/parsers
     - **99.96% success rate** across all 13 scripts (Thai, Myanmar, Latin, Bengali, Gujarati, Gurmukhi, Kannada, Malayalam, Tibetan, Sinhala, Khmer, Telugu, Cyrillic)
   - **Known Limitations**:
     - **Cyrillic**: ~15 failures (0.67%) due to inherent encoding ambiguity for consonant + аа patterns
     - **All Scripts**: 1 shared failure due to Latin transliteration ambiguity (`ग्ग्ह` vs `ग्घ` - "g+g+h" vs "g+gh") in likely erroneous source word
   - **Documentation**: See `src/CST.ScriptValidation/README.md` for complete usage guide and technical details

### **UI Font Management System**
- **Per-Script UI Font Configuration**: Complete font system for all 14 Pali scripts used in UI elements (search results, tree views, dropdowns) with individual font family and size settings
- **Native Font Detection**: macOS Core Text APIs detect and filter fonts compatible with each specific script
- **System Default Detection**: Shows actual system-chosen default fonts for each script (informational)
- **Real-Time Font Updates**: Font changes apply immediately across all UI locations without restart
- **DataTemplate Font Binding**: Custom FontHelper attached properties enable font settings in search results, tree views, and dropdowns
- **Font Settings Persistence**: All UI font preferences save and restore correctly across application sessions
    - **Note**: This system covers Pali script fonts in UI elements only; book content fonts and UI localization fonts are separate systems (see Outstanding Work)

### **Search System**
- **Full-Text Search Engine**: Complete Lucene.NET 4.8+ implementation with position-based indexing for all 217 Pali texts
- **Advanced Search Features**:
    - Single and multi-term exact searches with accurate counting
    - **Phrase Search**: Quoted terms (e.g., `"evaṃ me"`) find exact adjacent word sequences
    - **Proximity Search**: Unquoted multi-word searches find terms within adjustable word distance (default: 10 words)
    - **Two-Color Highlighting**: Blue background for primary search terms, green background for context words in proximity matches
    - **Tag Crossing Detection**: Proper XML structure preservation when highlights span CST's partial word bolding and quotation markup
    - Wildcard search (`*` and `?`) working in all 14 scripts, with expansion support in multi-word searches
    - Regular expression search support
    - Position-based highlighting with correct character offsets
- **Smart Book Filtering**:
    - Checkbox-based filter UI for Pitaka/Commentary categories
    - "Select All/None" quick actions for filter management
    - Live book count display based on current filter selection
    - Filter summary display when collapsed
- **Enhanced Search UI**:
    - Visual search elements (magnifying glass, clear button, progress indicator)
    - Two-column layout with terms list and book occurrences
    - Real-time search statistics and loading feedback
    - Keyboard shortcuts (Enter/Escape) and double-click navigation
- **Search Result Integration & Restoration**:
    - Persistent highlighting saved per-tab with search terms and positions
    - Full session restoration: search highlights, navigation UI, and hit counter restored on app restart
    - Single-term and multi-term searches both restore correctly with IsFirstTerm flags
    - Auto-navigation to first hit after restoration (CurrentHitIndex restoration planned for post-Beta 3)

### **Indexing & File Management**
- **Incremental Indexing System**: Smart indexing that only processes changed files, not entire 217-book corpus
- **Production-Ready Services**: Fully tested IndexingService and XmlFileDatesService with 62 comprehensive unit/integration/performance tests
- **Empty Index Handling**: Proper startup behavior when no search index exists yet
- **Index Integrity**: Fixed duplicate document issues, accurate search counts, proper document replacement

### **XML Update System**
- **GitHub API Integration**: Automatic file updates using Octokit.NET with repository configuration in Settings
- **SHA-Based Change Detection**: Only downloads files that have actually changed since local copies (avoids 1GB+ full downloads)
- **Enhanced File Tracking**: Nullable timestamps, proper state management, separation between download and indexing states
- **Optimized Startup Sequence**: Files updated before indexing to eliminate redundant work
- **Reduced Logging Noise**: 95% reduction in startup logging (300KB+ → 14KB) while preserving debug information

### **Dynamic Welcome Page System**
- **Version-Aware Messaging**: Displays version-specific messages (beta feedback, stable notifications, outdated warnings) based on user's current version
- **Automatic Announcements**: Shows targeted announcements and critical notices filtered by version and expiration dates
- **GitHub Integration**: Fetches update configuration from GitHub main branch with 24-hour local caching and offline fallback
- **Semantic Version Comparison**: Robust version parsing with support for pre-release identifiers and build metadata
- **External Link Handling**: Welcome page links (GitHub releases, documentation) open in system browser instead of internal navigation
- **Smart Caching**: Local cache with TTL management, graceful degradation when offline, and automatic refresh for stale data

### **User Interface Polish**
- **Mac-Style Book Tree Icons**: Dynamic folder icons (open/closed states) with document icons for individual books
- **Tab Overflow Fix**: Custom scrollbar styling prevents tab coverage when many books are open
- **Clean Settings Window**: Removed all non-functional placeholder settings, only displays working functionality
- **Application Branding**: Proper "CST Reader" branding in macOS menu bar, window titles, and bundle configuration
- **Visual Feedback**: Progress indicators, loading states, dynamic layouts, and proper iconography throughout UI
- **Welcome Page with Startup Progress**: Persistent welcome page displays status updates during startup for XML checking, downloading, and indexing operations (fully working on macOS)
- **Dark Mode Support**: Complete Dark Mode support for all UI panels (main window toolbar, book view toolbar/status bar, search panel, settings window) and book content area with proper FluentTheme integration
- **Dark Mode Book Content**: WebView book content displays with black background and white text when system is in Dark Mode, with proper color-inverted search highlighting
- **macOS Tahoe Glass Icon**: Application icon with proper transparency support for macOS Tahoe Glass interface, eliminating grey background artifacts

### **View Source PDF Feature**
- **PDF Display in Dockable Tabs**: View Burmese 1957 and 2010 edition PDFs directly in the application using CEF's built-in PDFium viewer
- **SharePoint Integration**: PDFs downloaded from SharePoint via Microsoft Graph API with Azure AD authentication
- **Context-Aware Page Navigation**: Automatically opens PDF to the correct page based on current Myanmar page in the book
- **Page Mapping**: Uses CST4's proven formula: `pdfPage = source.PageStart + (myanmarPage - 1)` with proper handling of volume.page format (e.g., "3.10")
- **Keyboard Shortcut**: Cmd+E triggers View Source 1957 (implemented via JavaScript interop due to CEF keyboard capture)
- **Toolbar Button**: "1957" button in book toolbar for mouse-based access
- **Local Caching**: Downloaded PDFs cached in `~/Library/Application Support/CSTReader/pdfs/` to avoid redundant downloads
- **Tab Lifecycle**: PDF tabs can be switched without crashing; WebView properly preserved across tab switches
- **Completed Source Mappings** (156+ books in Sources.cs):
    - **1957 Mula**: Vinaya (5), DN (3), MN (3), SN (5), AN (11), KN (14), Abhidhamma (12) = 53 books
    - **2010 Mula**: All texts mirroring 1957 structure = 40 books
    - **1957 Atthakatha**: All commentaries = 45 books
    - **1957 Tika**: DN (3), MN (3), SN (5), AN (4), KN-Netti (1), Vinaya (3), Abhidhamma (3) = 22 books
    - **Anya/Visuddhimagga**: Mula 1-2, Mahatika 1-2, Nidanakatha = 5 books
- **Mapping Methodology**: startPage verified by finding the large decorative book title heading with "နမော တဿ..." invocation
- **Known Issues**:
    - MN Tika 1 (s0201t) spans two PDF volumes; needs special handling to select PDF based on volume number in page marker (1.x vs 2.x)
    - Two 1957 Abhidhamma PDFs are 0 bytes in SharePoint (Dhammasangani, Yamaka-3)
- **Limitations**: Float button not yet implemented for PDF tabs, but PDFs can be dragged to other windows via the dock system

### **Technical Architecture**
- **Modern .NET 9**: Built on latest .NET with Avalonia UI 11.x for cross-platform desktop development
- **Reactive MVVM**: ReactiveUI-based ViewModels with proper lifecycle management and event handling
- **Dependency Injection**: Clean service architecture with Microsoft.Extensions.DI container
- **WebView Rendering**: Uses WebViewControl-Avalonia for book content display with search highlighting
- **Comprehensive Testing**: 65+ tests covering unit, integration, and performance scenarios with 100% pass rate

### **macOS Packaging & Distribution**
- **CEF WebView Packaging**: Four helper app bundles (Main, GPU, Plugin, Renderer) with proper Info.plist files and shell script launchers that ensure runtime dependencies are found
- **Developer ID Signing**: All components signed with Apple Developer ID Application certificate, including executables and dylibs
- **Hardened Runtime**: Applied with JIT and unsigned memory entitlements for .NET runtime compatibility
- **Notarization**: DMG packages fully notarized with automated build workflow
- **Production-Ready Distribution**: Apps launch without quarantine warnings or "damaged" errors on first launch

## Known Limitations

### **macOS CPU Usage**

CST Reader exhibits elevated CPU usage on macOS compared to typical desktop applications:

- **~30% CPU** with only the welcome page open (single WebView)
- **~60% CPU** with 3 books open (3 WebView instances)
- Main process typically uses **27-30%** when idle

**Root Cause**: This is a known limitation of Avalonia's macOS native backend (`libAvaloniaNative.dylib`). The framework's `Signaler` class creates a CFRunLoop observer that fires on every run loop iteration, causing continuous background processing. CEF (Chromium Embedded Framework) amplifies this effect by performing process monitoring and rendering checks on each callback.

**Comparison with Other Platforms**:
- macOS (M1): 6-10% idle (Avalonia baseline) vs Windows: 0.48% idle
- CST Reader's usage is proportional to the number of open WebView instances

**User Mitigation**:
- Close book tabs when not in use (each tab adds ~10% CPU)
- Keep welcome page open when idle (lowest CPU state)

**Technical Details**: See `markdown/notes/AVALONIA_HIGH_CPU.md` for complete analysis including CPU profiling data, stack traces, and references to Avalonia GitHub issues (#11070, #15894).

**Note**: This is an Avalonia framework limitation, not a CST Reader bug. Future improvements depend on Avalonia framework optimizations or alternative rendering approaches.

### **Multi-Word Search Limitation**

Multi-word and phrase search currently only work with 2 words. Searches with 3 or more words return no results.

**What Works**:
- Single word searches
- Two-word proximity search (e.g., `dhamma vinaya`)
- Two-word phrase search (e.g., `"evaṃ me"`)

**What Doesn't Work**:
- Three or more word proximity searches (e.g., `dhamma vinaya sangha`)
- Three or more word phrase searches (e.g., `"evaṃ me sutaṃ"`)

**Impact**: Users cannot search for common longer phrases like "evaṃ me sutaṃ" (Thus have I heard) or perform proximity searches with multiple concepts.

**Issue Tracking**: See issue #30 for technical details and debugging investigation.

**Priority**: High - This is a bug in core search functionality that significantly impacts usability.

## Outstanding Work

- **Full UI Localization System**:
    - **Multi-Language Support**: Implement complete localization for 20+ spoken languages (matching CST4 functionality)
    - **Runtime Language Switching**: Allow users to change UI language without restart
    - **String Tables**: Port existing string tables from CST4 for all supported languages
    - **Settings Integration**: Add language selection to Settings window
    - **Resource Management**: Implement proper resource loading system for localized strings
    - **Note**: This is separate from Pali script selection but often related in user preferences
- **UI Language Font System**:
    - **Localization Font Settings**: Separate font controls for ~20 UI languages (distinct from Pali script fonts)
    - **Font Discovery**: Detect available fonts suitable for each UI language script
    - **Note**: This is separate from Pali script fonts - refers to UI language localization fonts
- **Book Content Font Management System**:
    - **Current Limitation**: Book fonts are hardcoded in XSL stylesheets (e.g., `.book { font-size: 21pt; }`), requiring manual XSL editing for customization (as in CST4)
    - **Per-Script Book Fonts**: Implement user-configurable font families for book content display, separate from UI fonts
    - **Book Font Sizing**: Add font size controls for different book content elements (titles, paragraphs, footnotes, etc.)
    - **XSL Integration**: Dynamic XSL generation or CSS injection to apply user font preferences to WebView book content
    - **Style Customization**: Support for different paragraph styles tagged in TEI XML (matching CST4's XSL customization capabilities)
    - **Preview System**: Live preview of font changes in book display
    - **Note**: This is the third distinct font system - separate from both UI Pali script fonts and UI localization fonts
- **Custom Book Collections**: Implement user-defined book collection feature for targeted searches
- **UI Feedback During Operations**:
    - **Update History**: Track and display XML update history for transparency
- **Logging Cleanup**:
    - **Issue**: Application produces debug-splash.log file with excessive debug logging
    - **Goal**: Clean up and reduce verbose logging output during normal operation
    - **Impact**: Improves startup performance and reduces disk I/O
- **Test Suite Cleanup (Post Beta 3)**:
    - **Current Status**: 146 tests passing, 10 skipped for Beta 3 release
    - **Skipped Tests**: 10 tests with test infrastructure issues (not production bugs):
        - 3 MacFontService tests (testing removed/refactored internals via reflection)
        - 3 Incremental indexing tests (progress report async/timing issues)
        - 2 IndexingService initialization tests (mock settings service issues)
        - 1 DefaultIndexDirectory test (mock validation issue)
        - 1 InitialIndexing test (incomplete - needs all 217 XML files or mocked catalog)
    - **Goal**: Fix or rewrite skipped tests to achieve 100% pass rate
    - **Priority**: Medium - tests skipped due to infrastructure issues, actual functionality verified via manual testing
- **Book Display Features**:
    - **Show/Hide Footnotes Toggle**: Add footnote visibility control (check CST4 UI for exact naming)
    - **Show/Hide Search Hits Toggle**: Add search hit highlighting visibility control (check CST4 UI for exact naming)
- **Comprehensive Application State Restoration**:
    - **Current Status**: Book restoration works with search highlights, script, window positions, and scroll positions. CST4 had additional state features to review.
    - **Search Navigation State**: Save and restore CurrentHitIndex (which hit user was viewing, e.g., "50 of 53")
    - **CST4 Comparison Review**: Conduct post-Beta 3 review of CST4's state restoration features to identify missing functionality
    - **Full Parity Goal**: Match CST4's comprehensive session restoration for professional user experience
    - **Note**: Beta 3 successfully restores search highlights and navigation UI, but starts at hit 1 and top of document
- **Recent Books Feature**:
    - **File Menu Integration**: Add "Recent Books" submenu to File menu or main UI
    - **MRU List Display**: Show recently opened books with titles and last-opened dates
    - **Smart Tracking**: Automatically add books to recent list when opened
    - **Settings Integration**: Re-implement MaxRecentBooks setting to control list size
    - **Persistence**: Leverage existing ApplicationState.RecentBooks infrastructure
    - **User Experience**: Quick access to frequently used texts
    - **Note**: Partial backend exists - ApplicationState.Preferences.RecentBooks list, RecentBookItem model, AddRecentBook() method, but no UI integration and book tracking not implemented
- **Dictionary Feature**:
    - **English Dictionary**: Implement Pali-English dictionary lookup (matching CST4 functionality)
    - **Hindi Dictionary**: Implement Pali-Hindi dictionary lookup (matching CST4 functionality)
    - **Multi-Script Support**: Dictionary lookups should work with all 14 Pali scripts for input
    - **UI Integration**: Add dictionary panel or popup for word lookups
    - **Dictionary Data**: Port dictionary data files from CST4
    - **Context Menu**: Right-click word lookup integration
    - **Keyboard Shortcuts**: Quick dictionary access hotkeys
- **GoTo Feature**:
    - **Book Navigation**: Implement "Go To" dialog for jumping to specific book locations
    - **Page/Section Navigation**: Support navigation by page number, section, verse, or other CST reference systems
    - **UI Integration**: Add GoTo menu item and keyboard shortcut (matching CST4 functionality)
    - **Validation**: Validate user input and provide feedback for invalid references
    - **History**: Track recently visited locations for quick navigation
- **View Source Feature Enhancements**:
    - **Remaining Anya Mappings**: Map remaining Anya text folders to XML files (Sihaḷa, Leḍī, Buddha-vandanā, Vaṃsa, Byākaraṇa, Nīti, Pakiṇṇaka, Saṅgāyana-puccha)
    - **2010 Edition Support**: Add Cmd+Shift+E shortcut and toolbar button for Burmese 2010 edition PDFs (2010 Mula mappings complete; need 2010 Atthakatha/Tika if PDFs exist)
    - **MN Tika Volume Selection**: Implement special case for s0201t.tik.xml which spans two PDF volumes (use volume number from page marker)
    - **Float Support**: Implement proper float/unfloat for PDF tabs (currently can only drag to other windows)
- **Semantic Search with Vector Embeddings (Research)**:
    - **Phase 1 - Semantic Search**: Natural language question search with pre-calculated vector embeddings
    - **Zero-Cost Requirement**: All AI processing on user's local machine, no API costs
    - **Pre-Processing**: Convert 217 books to Latin script, extract paragraphs, generate embeddings
    - **Local Vector Database**: Bundle FAISS index with application for offline search
    - **Sentence Transformers**: Use multilingual model for query and document embeddings
    - **Rich Metadata**: Store book, chapter, paragraph, and page numbers with each chunk
    - **Phase 2 - Optional RAG**: For power users with GPU, optional local LLM download
    - **Research Status**: Feasibility study complete, requires proof-of-concept validation
    - **Note**: This is an exploratory feature from brainstorming sessions, not a CST4 port


## Technical Architecture

### **Project Structure**
```
CST.Avalonia/
├── ViewModels/
│   ├── LayoutViewModel.cs             # Main VM for the docking layout
│   ├── BookDisplayViewModel.cs        # VM for book tabs with search highlighting
│   ├── PdfDisplayViewModel.cs         # VM for PDF source document tabs
│   ├── OpenBookDialogViewModel.cs     # VM for the book selection tree
│   ├── SearchViewModel.cs             # VM for search panel with live results
│   └── SettingsViewModel.cs           # VM for the settings window
├── Views/
│   ├── SimpleTabbedWindow.cs          # The main application window
│   ├── BookDisplayView.axaml          # Book view with WebView rendering
│   ├── PdfDisplayView.axaml           # PDF view with WebView for PDFium rendering
│   ├── OpenBookPanel.axaml            # The book selection tree view
│   ├── SearchPanel.axaml              # Search UI with filters and results
│   └── SettingsWindow.axaml           # The settings window
├── Services/
│   ├── ApplicationStateService.cs     # Handles saving/loading session state
│   ├── SettingsService.cs             # Handles saving/loading user settings
│   ├── ScriptService.cs               # Manages script conversion
│   ├── FontService.cs                 # Manages per-script font settings
│   ├── IndexingService.cs             # Manages Lucene index lifecycle
│   ├── XmlFileDatesService.cs         # Tracks file changes for incremental indexing
│   ├── XmlUpdateService.cs            # GitHub API integration for XML file updates
│   ├── SearchService.cs               # Lucene search with position-based results
│   └── SharePointService.cs           # Microsoft Graph API for SharePoint PDF downloads
├── Converters/
│   └── FontHelper.cs                  # Custom attached properties for DataTemplate font binding
└── App.axaml.cs                       # DI configuration, startup logic, state restoration

CST.Core/
└── Sources.cs                         # PDF source mappings (book -> PDF path, page offsets)

CST.Avalonia_inactive/
└── ... (Contains placeholder/legacy files for Search, etc.)
```

### **Key Technologies**
- **Avalonia UI 11.x**
- **.NET 9.0**
- **Dock.Avalonia**
- **WebViewControl-Avalonia**: CEF-based WebView for book content and PDF display (PDFium)
- **ReactiveUI**
- **Microsoft.Extensions.DI**
- **Serilog**
- **Lucene.NET 4.8+**: Full-text search with position-based indexing
- **Octokit.NET**: GitHub API integration for XML file updates
- **Microsoft.Graph + Azure.Identity**: SharePoint PDF downloads via Graph API
- **xUnit + Moq**: Comprehensive test framework with 62 tests

## Build & Run Instructions

### **Development Build & Run**
```bash
# Navigate to project directory
cd [project-root]/src/CST.Avalonia

# Build project
dotnet build

# Run application
dotnet run

# Run all tests
dotnet test

# Run specific test category
dotnet test --filter "IndexingServiceTests"
dotnet test --filter "Performance"
dotnet test --filter "Integration"
```

### **macOS Production Packaging**
The `package-macos.sh` script creates production-ready macOS app bundles and DMG installers:

```bash
# Create Apple Silicon (M1/M2/M3) package
./package-macos.sh arm64

# Create Intel Mac package
./package-macos.sh x64

# Default (Apple Silicon)
./package-macos.sh
```

**Features:**
- Builds self-contained .NET 9 app bundles with all dependencies
- Creates proper macOS application structure with Info.plist and app icon
- Includes XSL stylesheets in app bundle Resources
- Generates launch script with environment configuration
- Automatically creates DMG installer if `create-dmg` is available (`brew install create-dmg`)
- Supports both Apple Silicon and Intel architectures

**Output:**
- Creates `CST Reader.app` bundle for direct distribution
- Optionally creates `CST-Reader-{arch}.dmg` installer in `dist/` directory

## Test Coverage

The project now includes comprehensive testing with **62 tests** covering:

- **Unit Tests (45)**: Core service functionality, error handling, edge cases
- **Integration Tests (11)**: Service interaction, dependency injection, workflows  
- **Performance Tests (6)**: Speed benchmarks, memory optimization, consistency

All tests maintain a **100% pass rate** and validate production readiness.

## Development Guidelines for Claude

### TodoWrite Usage - IMPORTANT

**ALWAYS use TodoWrite when:**
- Diagnosing issues with **multiple potential causes** (e.g., "this could be A, B, or C")
- Implementing features requiring **3+ distinct steps**
- Working through **any systematic checklist** (testing, deployment, debugging)
- The user provides **a list of tasks** to complete
- Starting work on **non-trivial, multi-step tasks**

**How to use TodoWrite effectively:**
1. **Create the list IMMEDIATELY** when you identify multiple steps/causes
2. **Work through ALL items systematically** - don't stop after the first fix
3. **Mark tasks complete as you finish them** - keep the user informed of progress
4. **Verify assumptions** - check for existing solutions before adding new ones

**Example - Multi-Cause Debugging:**
```
When I identify: "This hang is likely due to: 1) Network entitlements 2) Infinite timeout 3) DNS issues"
I should IMMEDIATELY create:
- [ ] Check if network entitlements exist
- [ ] Add network entitlements if missing
- [ ] Add timeouts to prevent infinite hangs
- [ ] Verify DNS resolution in packaged app

Then work through ALL items, not just the first one that seems to help.
```

**Why this matters:**
- Prevents incomplete fixes that leave users stuck
- Ensures systematic coverage of all potential issues
- Keeps both Claude and user aligned on progress
- Critical for release-blocking bugs where partial fixes waste time

### macOS Code Signing & Entitlements

**Required Entitlements** (in `package-macos.sh`):

```xml
<key>com.apple.security.cs.allow-jit</key>
<true/>  <!-- Required for .NET JIT compilation -->

<key>com.apple.security.cs.allow-unsigned-executable-memory</key>
<true/>  <!-- Required for .NET runtime -->

<key>com.apple.security.cs.disable-library-validation</key>
<true/>  <!-- Required to load .NET assemblies -->

<key>com.apple.security.network.client</key>
<true/>  <!-- Required for outgoing network connections (GitHub API, downloads) -->
```

**When Adding New Features:**
1. **Check if new entitlements are needed** - consult [Apple's Entitlement Key Reference](https://developer.apple.com/documentation/bundleresources/entitlements)
2. **Common additional entitlements you might need:**
   - `com.apple.security.network.server` - Incoming network connections
   - `com.apple.security.device.camera` - Camera access
   - `com.apple.security.device.microphone` - Microphone access
   - `com.apple.security.files.downloads.read-write` - Downloads folder access
   - `com.apple.security.print` - Printing capabilities
   - `com.apple.security.app-sandbox` - Enable App Sandbox (currently disabled)

3. **Test entitlements after packaging:**
   ```bash
   # Verify entitlements are embedded
   codesign -d --entitlements - "/Applications/CST Reader.app"
   ```

**Critical:** Notarized apps **WILL FAIL SILENTLY** without proper entitlements. Network calls will hang indefinitely (causing high CPU from retry loops) rather than showing clear errors.

## Documentation Structure

All project documentation is organized in `docs/` at the project root with a workflow-based structure. **Documentation follows the feature lifecycle**, moving through directories as implementation progresses.

### Directory Structure

```
docs/
├── README.md                    # Complete documentation index
├── architecture/                # System design & technical architecture
├── implementation/              # Postmortems, known issues, lessons learned
├── features/
│   ├── implemented/            # Completed features (archival reference)
│   ├── in-progress/            # Currently active development
│   └── planned/                # Future features from CST4 analysis
├── research/                    # Exploratory research
├── reference/                   # External materials & CST4 feature specs
│   ├── cst4/                   # CST4 implementation reference
│   └── sinhala/                # Sinhala script PDFs
├── testing/                     # Test plans & results
└── blog/                        # Blog posts & articles
```

### Documentation Workflow

**Feature documentation moves through three stages:**

1. **Planning** → `docs/features/planned/`
   - Feature researched and specified
   - Not yet started
   - Examples: Dictionary, Go To, Show Source PDF

2. **Active Development** → `docs/features/in-progress/`
   - Currently being implemented
   - Examples: Windows port, WindowsFontService

3. **Completed** → `docs/features/implemented/`
   - Feature shipped and working
   - Planning doc archived as historical reference
   - Examples: Search, Dark Mode, Dynamic Welcome Page
   - **Note**: Check `IMPLEMENTATION_NOTES.md` for divergences from original plans

### Quick Reference: Where to Put Documentation

**When creating new documentation:**

- **Feature planning** → `docs/features/planned/[FEATURE_NAME].md`
- **Active feature work** → Move to `docs/features/in-progress/[category]/`
- **Completed feature** → Move to `docs/features/implemented/[category]/`
- **Known issue/postmortem** → `docs/implementation/[ISSUE_NAME].md`
- **Architecture doc** → `docs/architecture/[TOPIC].md`
- **Research/exploration** → `docs/research/[TOPIC].md`
- **Test results** → `docs/testing/[TEST_NAME].md`

**When implementing a feature:**

1. Start in `docs/features/planned/` or create new planning doc in `in-progress/`
2. During implementation, keep doc in `in-progress/[category]/`
3. When shipped, move to `implemented/[category]/`
4. Document any divergences in `docs/features/implemented/IMPLEMENTATION_NOTES.md`

**CST4 Reference Documents:**

All CST4 feature analysis documents are preserved in `docs/reference/cst4/` to ensure feature parity between CST4 and CST.Avalonia. These documents are valuable references for Windows port and future development.

**For complete documentation catalog**, see [docs/README.md](../../docs/README.md).

## Next Steps

With View Source PDF (1957 edition) now committed and working, the immediate priorities are:

1. **Beta 4 Priority Items**:
   - Fix search hit restoration bug (highlighted search hits not restored when reopening books at startup)
   - Complete remaining Anya PDF mappings (8 folders with ~35 XML files)
2. **View Source Enhancements**: Add 2010 edition support with Cmd+Shift+E shortcut and toolbar button
3. **Custom Book Collections**: Implement user-defined book collection feature for targeted searches
4. **Search Navigation Enhancements**: Add keyboard shortcuts for search hit navigation (First/Previous/Next/Last)


