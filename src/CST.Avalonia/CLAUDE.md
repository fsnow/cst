# CST Avalonia Project Status - August 2025

## Current Status: **XML UPDATE SYSTEM BUG FIXES & OPTIMIZATION** 🔧

**Last Updated**: September 14, 2025
**Working Directory**: `[project-root]/src/CST.Avalonia`

## Project Overview

This project is a ground-up rewrite of the original WinForms-based CST4, built on Avalonia UI and .NET 9. The application is a cross-platform Buddhist text reader featuring a modern, dock-based IDE-style interface. The active codebase is now focused solely on the current architecture, with legacy and placeholder files moved to a separate directory for clarity.

CST stands for "Chaṭṭha Saṅgāyana Tipiṭaka".

## Current Functionality

CST Reader is a modern, cross-platform Buddhist text reader featuring a complete implementation of the following systems:

### **Core Application Features**
1. **Dock-Based IDE Interface**: Fully functional docking system with resizable panels, tab management, and persistent layout state
2. **Complete Session Restoration**: Application saves and restores all open books, scripts, window positions, and search highlights across sessions
3. **Cross-Platform Build System**: Native macOS `.dmg` packages with proper application branding and menu integration
4. **Advanced Logging System**: Structured Serilog logging with configurable levels, unified across all components including CST.Lucene

### **Text Display & Script System**
5. **Multi-Script Support**: 
   - **Display**: All 14 Pali scripts supported (Devanagari, Latin, Bengali, Cyrillic, Gujarati, Gurmukhi, Kannada, Khmer, Malayalam, Myanmar, Sinhala, Telugu, Thai, Tibetan)
   - **Input**: 9 scripts supported for search/dictionary (missing: Thai, Telugu, Tibetan, Khmer, Cyrillic)
   - **Indexing**: IPE (Internal Phonetic Encoding) with Devanagari analyzers for accurate search
6. **Per-Tab Script Selection**: Each book tab independently remembers and applies its script setting
7. **Script Synchronization**: Search results and book tree automatically update display when global script changes

### **UI Font Management System**
8. **Per-Script UI Font Configuration**: Complete font system for all 14 Pali scripts used in UI elements (search results, tree views, dropdowns) with individual font family and size settings
9. **Native Font Detection**: macOS Core Text APIs detect and filter fonts compatible with each specific script
10. **System Default Detection**: Shows actual system-chosen default fonts for each script (informational)
11. **Real-Time Font Updates**: Font changes apply immediately across all UI locations without restart
12. **DataTemplate Font Binding**: Custom FontHelper attached properties enable font settings in search results, tree views, and dropdowns
13. **Font Settings Persistence**: All UI font preferences save and restore correctly across application sessions
    - **Note**: This system covers Pali script fonts in UI elements only; book content fonts and UI localization fonts are separate systems (see Outstanding Work)

### **Search System**
14. **Full-Text Search Engine**: Complete Lucene.NET 4.8+ implementation with position-based indexing for all 217 Buddhist texts
15. **Advanced Search Features**: 
    - Single and multi-term exact searches with accurate counting
    - Wildcard search working in all 14 scripts 
    - Regular expression search support
    - Position-based highlighting with correct character offsets
16. **Smart Book Filtering**: 
    - Checkbox-based filter UI for Pitaka/Commentary categories
    - "Select All/None" quick actions for filter management
    - Live book count display based on current filter selection
    - Filter summary display when collapsed
17. **Enhanced Search UI**: 
    - Visual search elements (magnifying glass, clear button, progress indicator)
    - Two-column layout with terms list and book occurrences
    - Real-time search statistics and loading feedback
    - Keyboard shortcuts (Enter/Escape) and double-click navigation
18. **Search Result Integration**: Persistent highlighting saved per-tab, search terms passed to book display

### **Indexing & File Management** 
19. **Incremental Indexing System**: Smart indexing that only processes changed files, not entire 217-book corpus
20. **Production-Ready Services**: Fully tested IndexingService and XmlFileDatesService with 62 comprehensive unit/integration/performance tests
21. **Empty Index Handling**: Proper startup behavior when no search index exists yet
22. **Index Integrity**: Fixed duplicate document issues, accurate search counts, proper document replacement

### **XML Update System**
23. **GitHub API Integration**: Automatic file updates using Octokit.NET with repository configuration in Settings
24. **SHA-Based Change Detection**: Only downloads files that have actually changed since local copies (avoids 1GB+ full downloads)
25. **Enhanced File Tracking**: Nullable timestamps, proper state management, separation between download and indexing states 
26. **Optimized Startup Sequence**: Files updated before indexing to eliminate redundant work
27. **Reduced Logging Noise**: 95% reduction in startup logging (300KB+ → 14KB) while preserving debug information

### **User Interface Polish**
28. **Mac-Style Book Tree Icons**: Dynamic folder icons (open/closed states) with document icons for individual books
29. **Tab Overflow Fix**: Custom scrollbar styling prevents tab coverage when many books are open
30. **Clean Settings Window**: Removed all non-functional placeholder settings, only displays working functionality
31. **Application Branding**: Proper "CST Reader" branding in macOS menu bar, window titles, and bundle configuration
32. **Visual Feedback**: Progress indicators, loading states, dynamic layouts, and proper iconography throughout UI
33. **Splash Screen with Progress**: Beautiful Buddha teaching image shown at startup with status updates during XML checking, downloading, and indexing operations (fully working on macOS)

### **Technical Architecture**
34. **Modern .NET 9**: Built on latest .NET with Avalonia UI 11.x for cross-platform desktop development
35. **Reactive MVVM**: ReactiveUI-based ViewModels with proper lifecycle management and event handling
36. **Dependency Injection**: Clean service architecture with Microsoft.Extensions.DI container
37. **WebView Rendering**: Uses WebViewControl-Avalonia for book content display with search highlighting
38. **Comprehensive Testing**: 62 tests covering unit, integration, and performance scenarios with 100% pass rate

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
    - **Search Hit Restoration**: Fix bug where highlighted search hits are not restored when reopening books at startup
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

## Outstanding Work for Beta 2 Release

The following items are prioritized for the Beta 2 release to ensure production readiness:

1.  **Book Display Bug Fixes** (from item #7 above):
    - Implement search hit restoration on startup

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

## Next Steps

With font system, script synchronization, and basic search functionality complete, the immediate priorities are:

1. **Missing Script Input Support**: Implement converters for Thai, Telugu, Tibetan, Khmer, and Cyrillic scripts to enable search input in all 14 Pali scripts
2. **Phrase & Proximity Search**: Implement position-based phrase and proximity searching using the existing term vector infrastructure  
3. **Search Filtering**: Fix book collection checkboxes and implement custom collection support for targeted searches
4. **Advanced Search Features**: Complete multi-term highlighting testing and add search navigation enhancements

The core infrastructure is robust with font management, script conversion, accurate search counting, and real-time UI updates all working correctly. Focus now shifts to completing script input support and advanced search features.
