# CST Avalonia Project Status - October 2025

## Current Status: **BETA 2 RELEASE READY** 🚀

**Last Updated**: October 4, 2025
**Working Directory**: `[project-root]/src/CST.Avalonia`

## Project Overview

This project is a ground-up rewrite of the original WinForms-based CST4, built on Avalonia UI and .NET 9. The application is a cross-platform Pali text reader featuring a modern, dock-based IDE-style interface. The active codebase is now focused solely on the current architecture, with legacy and placeholder files moved to a separate directory for clarity.

CST stands for "Chaṭṭha Saṅgāyana Tipiṭaka". The texts we use are provided by the Vipassana Research Institute. 

Claude, do not use the word "Buddhist" in the application or any supporting documentation.

## Current Functionality

CST Reader is a modern, cross-platform Pali text reader featuring a complete implementation of the following systems:

### **Core Application Features**
1. **Dock-Based IDE Interface**: Fully functional docking system with resizable panels, tab management, and persistent layout state
2. **Complete Session Restoration**: Application saves and restores all open books, scripts, window positions, and search highlights across sessions
3. **Cross-Platform Build System**: Native macOS `.dmg` packages with proper application branding and menu integration
4. **Code Signing & Security**: Developer ID signed applications and DMG installers for macOS without "damaged" application errors on first launch
5. **Advanced Logging System**: Structured Serilog logging with configurable levels, unified across all components including CST.Lucene

### **Text Display & Script System**
6. **Multi-Script Support**:
   - **Display**: All 14 Pali scripts supported (Devanagari, Latin, Bengali, Cyrillic, Gujarati, Gurmukhi, Kannada, Khmer, Malayalam, Myanmar, Sinhala, Telugu, Thai, Tibetan)
   - **Input**: 9 scripts supported for search/dictionary (missing: Thai, Telugu, Tibetan, Khmer, Cyrillic)
   - **Indexing**: IPE (Internal Phonetic Encoding) with Devanagari analyzers for accurate search
7. **Per-Tab Script Selection**: Each book tab independently remembers and applies its script setting
8. **Script Synchronization**: Search results and book tree automatically update display when global script changes

### **UI Font Management System**
9. **Per-Script UI Font Configuration**: Complete font system for all 14 Pali scripts used in UI elements (search results, tree views, dropdowns) with individual font family and size settings
10. **Native Font Detection**: macOS Core Text APIs detect and filter fonts compatible with each specific script
11. **System Default Detection**: Shows actual system-chosen default fonts for each script (informational)
12. **Real-Time Font Updates**: Font changes apply immediately across all UI locations without restart
13. **DataTemplate Font Binding**: Custom FontHelper attached properties enable font settings in search results, tree views, and dropdowns
14. **Font Settings Persistence**: All UI font preferences save and restore correctly across application sessions
    - **Note**: This system covers Pali script fonts in UI elements only; book content fonts and UI localization fonts are separate systems (see Outstanding Work)

### **Search System**
15. **Full-Text Search Engine**: Complete Lucene.NET 4.8+ implementation with position-based indexing for all 217 Pali texts
16. **Advanced Search Features**:
    - Single and multi-term exact searches with accurate counting
    - Wildcard search working in all 14 scripts
    - Regular expression search support
    - Position-based highlighting with correct character offsets
17. **Smart Book Filtering**:
    - Checkbox-based filter UI for Pitaka/Commentary categories
    - "Select All/None" quick actions for filter management
    - Live book count display based on current filter selection
    - Filter summary display when collapsed
18. **Enhanced Search UI**:
    - Visual search elements (magnifying glass, clear button, progress indicator)
    - Two-column layout with terms list and book occurrences
    - Real-time search statistics and loading feedback
    - Keyboard shortcuts (Enter/Escape) and double-click navigation
19. **Search Result Integration**: Persistent highlighting saved per-tab, search terms passed to book display

### **Indexing & File Management**
20. **Incremental Indexing System**: Smart indexing that only processes changed files, not entire 217-book corpus
21. **Production-Ready Services**: Fully tested IndexingService and XmlFileDatesService with 62 comprehensive unit/integration/performance tests
22. **Empty Index Handling**: Proper startup behavior when no search index exists yet
23. **Index Integrity**: Fixed duplicate document issues, accurate search counts, proper document replacement

### **XML Update System**
24. **GitHub API Integration**: Automatic file updates using Octokit.NET with repository configuration in Settings
25. **SHA-Based Change Detection**: Only downloads files that have actually changed since local copies (avoids 1GB+ full downloads)
26. **Enhanced File Tracking**: Nullable timestamps, proper state management, separation between download and indexing states
27. **Optimized Startup Sequence**: Files updated before indexing to eliminate redundant work
28. **Reduced Logging Noise**: 95% reduction in startup logging (300KB+ → 14KB) while preserving debug information

### **Dynamic Welcome Page System**
29. **Version-Aware Messaging**: Displays version-specific messages (beta feedback, stable notifications, outdated warnings) based on user's current version
30. **Automatic Announcements**: Shows targeted announcements and critical notices filtered by version and expiration dates
31. **GitHub Integration**: Fetches update configuration from GitHub main branch with 24-hour local caching and offline fallback
32. **Semantic Version Comparison**: Robust version parsing with support for pre-release identifiers and build metadata
33. **External Link Handling**: Welcome page links (GitHub releases, documentation) open in system browser instead of internal navigation
34. **Smart Caching**: Local cache with TTL management, graceful degradation when offline, and automatic refresh for stale data

### **User Interface Polish**
35. **Mac-Style Book Tree Icons**: Dynamic folder icons (open/closed states) with document icons for individual books
36. **Tab Overflow Fix**: Custom scrollbar styling prevents tab coverage when many books are open
37. **Clean Settings Window**: Removed all non-functional placeholder settings, only displays working functionality
38. **Application Branding**: Proper "CST Reader" branding in macOS menu bar, window titles, and bundle configuration
39. **Visual Feedback**: Progress indicators, loading states, dynamic layouts, and proper iconography throughout UI
40. **Splash Screen with Progress**: Beautiful Buddha teaching image shown at startup with status updates during XML checking, downloading, and indexing operations (fully working on macOS)

### **Technical Architecture**
41. **Modern .NET 9**: Built on latest .NET with Avalonia UI 11.x for cross-platform desktop development
42. **Reactive MVVM**: ReactiveUI-based ViewModels with proper lifecycle management and event handling
43. **Dependency Injection**: Clean service architecture with Microsoft.Extensions.DI container
44. **WebView Rendering**: Uses WebViewControl-Avalonia for book content display with search highlighting
45. **Comprehensive Testing**: 65+ tests covering unit, integration, and performance scenarios with 100% pass rate

## Beta 1 Release (September 20, 2025)

**CST Reader 5.0.0-beta.1** was released with core functionality but had code signing issues causing "damaged application" errors on launch.

## Beta 2 Release (October 4, 2025)

**CST Reader 5.0.0-beta.2** resolves all packaging and code signing issues. This release is fully functional and ready for distribution:

### **Release Highlights**
- **Cross-Platform DMG Packages**: Both Apple Silicon (M1/M2/M3/M4) and Intel Mac builds available
- **Dynamic Welcome System**: Version-aware messaging with GitHub integration for updates and announcements
- **Production-Ready Features**: All core functionality working including search, indexing, session restoration, and multi-script support
- **Comprehensive Testing**: 65+ automated tests ensure stability and reliability

### **Download & Installation**
- **GitHub Release**: https://github.com/fsnow/cst/releases/tag/v5.0.0-beta.2
- **Apple Silicon**: `CST-Reader-arm64.dmg` (178MB)
- **Intel Mac**: `CST-Reader-x64.dmg` (186MB)
- **Installation Note**: Applications are now fully code signed with Apple Developer ID certificate - no security warnings or quarantine removal required

### **Beta Testing Goals**
- Validate cross-platform compatibility and performance
- Gather user feedback on new dynamic welcome page system
- Test external link handling and update notification system
- Identify any remaining UI/UX issues before stable release

The beta release marks the completion of Phase 2 of the project roadmap, with all major systems now functional and ready for real-world testing.

## Beta 2 Development (October 2025)

**Status**: Complete and released

### **Critical Issues Resolved**

#### **1. CEF WebView Packaging (Completed)**
- **Problem**: Packaged app crashed on startup with "Unable to find SubProcess" error
- **Root Cause**: CEF requires 4 helper app bundles in `Contents/Frameworks/` on macOS, but we had the subprocess in `Contents/MacOS/CefGlueBrowserProcess/`
- **Solution**:
  - Created 4 CEF Helper bundles (Main, GPU, Plugin, Renderer) with proper Info.plist files
  - Each helper contains a shell script launcher that calls the actual .NET subprocess
  - Shell scripts change to `CefGlueBrowserProcess/` directory before executing to ensure runtime dependencies are found
- **Documentation**: See `markdown/notes/CEF_HELPER_PACKAGING.md`

#### **2. Code Signing & Notarization (Completed)**
- **Developer ID Signing**: All components signed with Apple Developer ID Application certificate
- **Hardened Runtime**: Applied to all executables and dylibs, including `CefGlueBrowserProcess/Xilium.CefGlue.BrowserProcess`
- **Entitlements**: JIT and unsigned memory entitlements for .NET runtime
- **DMG Signing**: Distribution packages fully signed and notarized
- **Automated Build**: Enhanced `package-macos.sh` script with integrated signing workflow
- **Result**: Apps launch without quarantine warnings or "damaged" errors

### **Beta 2 Release Goals**
- Eliminate "damaged" application errors on first launch
- Provide professional, signed DMG installers for seamless user experience
- Maintain all existing functionality from Beta 1
- Enable distribution through standard channels without security workarounds

### **Version Update Locations for Beta 2**
When preparing for Beta 2 release, the version number "5.0.0-beta.1" must be updated to "5.0.0-beta.2" in the following locations:

**Critical Files** (Must Update):
1. **`src/CST.Avalonia/CST.Avalonia.csproj`** - 4 occurrences:
   - Line 13: `<CFBundleVersion>`
   - Line 14: `<CFBundleShortVersionString>`
   - Line 18: `<Version>`
   - Line 21: `<InformationalVersion>`

2. **`src/CST.Avalonia/Info.plist`** - 2 occurrences:
   - Line 12: `<string>` under `CFBundleShortVersionString`
   - Line 14: `<string>` under `CFBundleVersion`

3. **`welcome-updates.json`** (root directory) - Update for Beta 2 announcements:
   - Line 6: Update `"beta":` to point to "5.0.0-beta.2"
   - Add new message block for "5.0.0-beta.2"
   - Update existing "5.0.0-beta.1" message if needed

4. **`src/CST.Avalonia/Services/WelcomeUpdateService.cs`**:
   - Line 33: Default version fallback (currently hardcoded to "5.0.0-beta.1")

**Documentation Files** (Should Update):
5. **`src/CST.Avalonia/CLAUDE.md`**:
   - Line 101: Beta 1 release version reference
   - Line 110: GitHub release URL
   - Add new Beta 2 release section

6. **`src/CST.Avalonia/Resources/welcome-content.html`** (Static fallback):
   - Line 184: Version display
   - Line 307: Footer version

**Note**: The version in compiled output files (`bin/Release/`) will be updated automatically when the project is rebuilt. Test files and markdown documentation references can remain as historical examples.

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

## Outstanding Work

1.  **Full UI Localization System**:
    - **Multi-Language Support**: Implement complete localization for 20+ spoken languages (matching CST4 functionality)
    - **Runtime Language Switching**: Allow users to change UI language without restart
    - **String Tables**: Port existing string tables from CST4 for all supported languages
    - **Settings Integration**: Add language selection to Settings window
    - **Resource Management**: Implement proper resource loading system for localized strings
    - **Note**: This is separate from Pali script selection but often related in user preferences
2.  **Missing Pali Script Input Parsers** (5 scripts need converters to IPE):
    - **Thai**: Thai script → IPE converter (Thai2Ipe or Thai2Deva)
    - **Telugu**: Telugu script → IPE converter (Telu2Ipe or Telu2Deva)
    - **Tibetan**: Tibetan script → IPE converter (Tibt2Ipe or Tibt2Deva)
    - **Khmer**: Khmer script → IPE converter (Khmr2Ipe or Khmr2Deva)
    - **Cyrillic**: Cyrillic script → IPE converter (Cyrl2Ipe or Cyrl2Deva)
    - **Note**: Display works for all 14 scripts, but input (search/dictionary) only works for 9
    - **Implementation**: May use direct Script→IPE or indirect Script→Deva→IPE path
3.  **UI Language Font System**:
    - **Localization Font Settings**: Separate font controls for ~20 UI languages (distinct from Pali script fonts)
    - **Font Discovery**: Detect available fonts suitable for each UI language script
    - **Note**: This is separate from Pali script fonts - refers to UI language localization fonts
4.  **Book Content Font Management System**:
    - **Current Limitation**: Book fonts are hardcoded in XSL stylesheets (e.g., `.book { font-size: 21pt; }`), requiring manual XSL editing for customization (as in CST4)
    - **Per-Script Book Fonts**: Implement user-configurable font families for book content display, separate from UI fonts
    - **Book Font Sizing**: Add font size controls for different book content elements (titles, paragraphs, footnotes, etc.)
    - **XSL Integration**: Dynamic XSL generation or CSS injection to apply user font preferences to WebView book content
    - **Style Customization**: Support for different paragraph styles tagged in TEI XML (matching CST4's XSL customization capabilities)
    - **Preview System**: Live preview of font changes in book display
    - **Import/Export**: Allow users to share book font configurations
    - **Note**: This is the third distinct font system - separate from both UI Pali script fonts (#9-14) and UI localization fonts (#3)
5.  **Advanced Search Features**:
    - **Phrase Search**: Implement position-based phrase searching with exact word order matching
    - **Proximity Search**: Add proximity operators for terms within specified distances
6.  **Search Filtering & Collections**:
    - **Custom Book Collections**: Implement user-defined book collection feature
7.  **UI Feedback During Operations**:
    - **Indexing Progress**: Show progress bar/spinner during index building
    - **Search State**: Indicate when index is incomplete or being rebuilt
    - **Operation Notifications**: Alert user when long operations complete
    - **XML Download Progress**: Show progress bar/notifications during file downloads
    - **Update History**: Track and display XML update history for transparency
8.  **Book Display Features**:
    - **Show/Hide Footnotes Toggle**: Add footnote visibility control (check CST4 UI for exact naming)
    - **Show/Hide Search Hits Toggle**: Add search hit highlighting visibility control (check CST4 UI for exact naming)
    - **Search Hit Restoration** ⭐ **BETA 3 PRIORITY**: Fix bug where highlighted search hits are not restored when reopening books at startup
    - **Splash Screen Issues**: Deferred post-Beta 2 - status text updates and automatic closing don't work in packaged apps (works fine in development)
9.  **Search Navigation Enhancement**:
    - Add keyboard shortcuts for search hit navigation (First/Previous/Next/Last)
10.  **Recent Books Feature**:
    - **File Menu Integration**: Add "Recent Books" submenu to File menu or main UI
    - **MRU List Display**: Show recently opened books with titles and last-opened dates
    - **Smart Tracking**: Automatically add books to recent list when opened
    - **Settings Integration**: Re-implement MaxRecentBooks setting to control list size
    - **Persistence**: Leverage existing ApplicationState.RecentBooks infrastructure
    - **User Experience**: Quick access to frequently used texts
    - **Note**: Partial backend exists - ApplicationState.Preferences.RecentBooks list, RecentBookItem model, AddRecentBook() method, but no UI integration and book tracking not implemented


## Technical Architecture

### **Project Structure**
```
CST.Avalonia/
├── ViewModels/
│   ├── LayoutViewModel.cs             # Main VM for the docking layout
│   ├── BookDisplayViewModel.cs        # VM for book tabs with search highlighting
│   ├── OpenBookDialogViewModel.cs     # VM for the book selection tree
│   ├── SearchViewModel.cs             # VM for search panel with live results
│   └── SettingsViewModel.cs           # VM for the settings window
├── Views/
│   ├── SimpleTabbedWindow.cs          # The main application window
│   ├── BookDisplayView.axaml          # Book view with WebView rendering
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
│   └── SearchService.cs               # Lucene search with position-based results
├── Converters/
│   └── FontHelper.cs                  # Custom attached properties for DataTemplate font binding
└── App.axaml.cs                       # DI configuration, startup logic, state restoration

CST.Avalonia_inactive/
└── ... (Contains placeholder/legacy files for Search, etc.)
```

### **Key Technologies**
- **Avalonia UI 11.x**
- **.NET 9.0**
- **`Dock.Avalonia`**
- **`WebViewControl-Avalonia`**
- **ReactiveUI**
- **Microsoft.Extensions.DI**
- **Serilog**
- **Lucene.NET 4.8+**: Full-text search with position-based indexing
- **Octokit.NET**: GitHub API integration for XML file updates
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

## Next Steps

With font system, script synchronization, and basic search functionality complete, the immediate priorities are:

1. **Missing Script Input Support**: Implement converters for Thai, Telugu, Tibetan, Khmer, and Cyrillic scripts to enable search input in all 14 Pali scripts
2. **Phrase & Proximity Search**: Implement position-based phrase and proximity searching using the existing term vector infrastructure  
3. **Search Filtering**: Fix book collection checkboxes and implement custom collection support for targeted searches
4. **Advanced Search Features**: Complete multi-term highlighting testing and add search navigation enhancements

The core infrastructure is robust with font management, script conversion, accurate search counting, and real-time UI updates all working correctly. Focus now shifts to completing script input support and advanced search features.
