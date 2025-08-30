# CST Avalonia Project Status - August 2025

## Current Status: **SEARCH PANEL ENHANCEMENTS & UX IMPROVEMENTS** 🔍

**Last Updated**: August 30, 2025
**Working Directory**: `/Users/fsnow/github/fsnow/cst/src/CST.Avalonia`

## Project Overview

This project is a ground-up rewrite of the original WinForms-based CST4, built on Avalonia UI and .NET 9. The application is a cross-platform Buddhist text reader featuring a modern, dock-based IDE-style interface. The active codebase is now focused solely on the current architecture, with legacy and placeholder files moved to a separate directory for clarity.

CST stands for "Chaṭṭha Saṅgāyana Tipiṭaka".

## Latest Session Update (2025-08-29)

### ✅ **COMPLETED: Tab Overflow Bug Fix & UI Polish**

#### **Fixed: Tab Scrollbar Covering Issue**
- **Problem Solved**: Horizontal scrollbar no longer covers tabs when too many books are open
- **Solution**: Custom Avalonia styling with `RenderTransform` to position scrollbar below tabs
- **Visual Enhancement**: Thin VS Code-style scrollbar (3px height) with transparent background
- **Technical Implementation**: 
  - Created `/Styles/DockStyles.axaml` with `translate(0px, 12px)` transform
  - 12px offset ensures clearance for tall Devanagari ligatures
  - Hidden arrow buttons for cleaner appearance
- **Beta 1 Priority**: Resolved critical UI issue affecting usability with multiple open books

#### **Removed Non-Functional "+" Button**
- **Problem Solved**: Non-functional "+" button removed from document tab bar
- **Solution**: Set `CanCreateDocument = false` in `CstDockFactory.cs` at two locations
- **Implementation**: Used proper Dock.Avalonia API instead of CSS workarounds
- **Result**: Clean tab bar without confusing non-functional elements

**Files Added/Modified**:
- `/Styles/DockStyles.axaml` - Custom scrollbar styling with positioning
- `/App.axaml` - Added StyleInclude for DockStyles
- `/CST.Avalonia.csproj` - Added AvaloniaResource for style file
- `/Services/CstDockFactory.cs` - Disabled document creation button

## Previous Session Update (2025-08-27)

### ✅ **COMPLETED: Major Search Panel UX Improvements & Bug Fixes**

#### **Enhanced Search Input Experience**
- **Visual Search Elements**: Added magnifying glass icon and clear button (X) to search input
- **Progress Feedback**: Progress indicator shows during search operations, replacing clear button temporarily
- **Better Visual Hierarchy**: Clean, intuitive search interface with proper iconography

#### **Redesigned Book Filtering System**
- **Compact Filter UI**: Replaced toggle switches with checkboxes for more space-efficient layout
- **Quick Actions**: Added "Select All" and "Select None" buttons for rapid filter management
- **Smart Filter Display**: Shows filter summary when collapsed (e.g., "3 of 7 types selected")
- **Live Book Counter**: Displays book count indicator (e.g., "52 of 217 books") based on current filters
- **Removed Placeholder**: Eliminated non-functional "Book Collection" dropdown

#### **Critical Search Logic Bug Fixes**
- **Fixed Empty Filter Bug**: When no books are selected via filters, search now correctly returns no results (previously showed all results)
- **Fixed Zero-Count Terms**: Terms with 0 occurrences are no longer displayed in search results
- **Fixed BitArray Logic**: Implemented proper BitArray matching logic consistent with CST4 for accurate book filtering
- **Enhanced Search Service**: Updated SearchService.cs with proper book filtering validation

#### **UI Polish & Accessibility**
- **Dynamic Layout**: Book toolbar and status bar now have dynamic height for better space utilization
- **Icon Improvements**: Enhanced book tree icons for better visual feedback
- **Progress States**: Clear visual indication during search operations

#### **macOS Application Branding Fix**
- **Fixed "Avalonia Application" Issue**: macOS menu bar now correctly shows "CST Reader"
- **Unified Bundle Configuration**: Resolved conflicts between Info.plist and .csproj - both now use "CST Reader"
- **Consistent Window Titles**: Updated all window titles (main window, splash screen, dialogs) to "CST Reader"
- **Works Both Ways**: Application name appears correctly whether running via `dotnet run` or as installed .app bundle

**Files Added/Modified**:
- `/Services/SearchService.cs` - Enhanced book filtering logic and validation
- `/ViewModels/SearchViewModel.cs` - Added filter management, book counting, and UX improvements
- `/Views/SearchPanel.axaml` - Complete UI redesign with new filter layout and search input enhancements

## Previous Session Update (2025-08-25)

### ✅ **COMPLETED: Per-Script Font Detection & Selection System**

#### **Platform-Specific Font Detection (MacFontService)**
- **Native macOS Implementation**: Created `MacFontService.cs` using Core Text APIs to detect fonts that support each Pali script
- **Character-Set Based Approach**: Uses sample Pali characters for each script to find compatible fonts
- **P/Invoke Integration**: Direct calls to Core Foundation and Core Text frameworks for native font enumeration
- **Dynamic Font Lists**: Font dropdowns in settings now populate with only fonts that can display each specific script

**Technical Implementation**:
- Uses `CTFontDescriptorCreateMatchingFontDescriptors` with character set attributes
- Converts sample text "mahāsatipaṭṭhānasuttaṃ" to each script for font testing
- Returns unique font family names sorted alphabetically
- Falls back to standard fonts if no script-specific fonts found

#### **Font Selection System Debugging & Fixes**
- **Problem Solved**: Font selection persistence and UI update issues resolved through extensive debugging
- **Root Cause**: Multiple issues with font loading, selection state management, and UI synchronization
- **Solution**: Comprehensive fixes to font loading pipeline and selection persistence
- **Result**: Per-script font selection now works reliably for all script-specific UI elements

#### **System Default Font Detection Implementation**
- **Core Text API Usage**: Uses `CTFontCreateUIFontForLanguage` + `CTFontCreateForStringWithLanguage` to determine actual system font choices
- **Script-Specific Detection**: Converts sample Pali text to each script, then queries system for optimal font
- **Caching System**: Results cached per script to avoid repeated P/Invoke calls
- **Informational Display**: Shows "System Default (Devanagari Sangam MN)" style information in Settings UI
- **Non-Intrusive**: Completely separate from font selection logic - purely informational

**Files Added/Modified**:
- `/Services/Platform/Mac/MacFontService.cs` - Added system default font detection with P/Invoke calls
- `/ViewModels/SettingsViewModel.cs` - Added SystemDefaultFontName property and async loading
- `/Services/FontService.cs` - Added GetSystemDefaultFontForScriptAsync delegation
- `/Services/IFontService.cs` - Added GetSystemDefaultFontForScriptAsync interface method
- `/Views/SettingsWindow.axaml` - Added system default font information display below preview

#### **Complete Per-Script Font Selection Status**
✅ **Font Detection**: Native macOS font detection working for all 14 Pali scripts
✅ **Font Persistence**: Font settings save and restore correctly across application sessions
✅ **UI Synchronization**: Font dropdowns correctly show selected fonts when loading
✅ **Script-Specific Filtering**: Font lists show only fonts compatible with each specific script
✅ **Real-time Updates**: Font changes apply immediately to all relevant UI elements
✅ **System Default Detection**: Displays actual system default font name (e.g., "System Default (.SF Pro Text)") for informational purposes

## Latest Session Update (2025-08-18)

### ✅ **COMPLETED: Mac-Style Book Tree Icons & Logging Cleanup**

#### **Enhanced Book Tree Icons** 
- **Dynamic Icon States**: Smart folder icons that change based on expand/collapse state
  - 📁 Closed folder emoji for collapsed categories
  - 📂 Open folder emoji for expanded categories  
  - 📄 Document emoji for individual books
- **Custom Converter**: `CategoryIconConverter` for proper icon visibility logic
- **Improved UX**: Visual feedback when expanding/collapsing tree nodes

**Technical Implementation**:
- Custom multi-value converter handles folder open/closed logic
- Unicode emojis provide consistent cross-platform icons
- Future enhancement: Replace with proper Mac-style PNG/SVG icons when Avalonia SVG support improves

**Files Added/Modified**:
- `/Converters/CategoryIconConverter.cs` - Multi-value converter for icon state logic
- `/Views/OpenBookPanel.axaml` - Updated to use dynamic folder icons

#### **Production Logging Cleanup**
- **Structured Logging**: Replaced all Console.WriteLine with proper Serilog logging
- **Appropriate Log Levels**: Debug, Information, Warning, Error based on context
- **Reduced Log Volume**: Removed excessive timestamp logging for production use
- **Exception Handling**: Proper structured exception logging with context

**Major Files Cleaned Up**:
- ✅ `App.axaml.cs` (28+ console statements → structured logging)
- ✅ `SplashScreen.axaml.cs` (22 console statements → structured logging)
- ✅ `CstDockFactory.cs`, `SearchService.cs`, `CstHostWindow.cs` (previously completed)
- **Remaining**: ViewModels still need logging cleanup (BookDisplayViewModel, SearchPanel, etc.)

### ✅ **PREVIOUS MILESTONE: Complete Font System Implementation**

#### **Font System Infrastructure**
- **FontService**: Complete implementation for all 14 Pali scripts with caching fixes
- **Settings UI**: Per-script font family and size configuration
- **Settings Persistence**: Font settings save and restore correctly across sessions
- **Real-time Updates**: Font changes apply immediately across all UI locations

#### **DataTemplate Font Binding Solution**
**Problem Solved**: Font family settings now work correctly in DataTemplates (search results, tree items, chapter lists)

**Solution**: Custom attached properties implementation (`FontHelper.cs`)
```csharp
public static class FontHelper
{
    public static readonly AttachedProperty<string> DynamicFontFamilyProperty;
    public static readonly AttachedProperty<int> DynamicFontSizeProperty;
    // Handles property changes and applies fonts dynamically
}
```

**Implementation**: Applied to all Pali text display locations:
- Search results (Matching Terms and Books lists)
- Select a Book tree view
- Chapter dropdown in book tabs
- Book path in status bar (bottom right)
- Book tab titles (dynamic per-book script)

#### **Script Change Synchronization**
**New Feature**: Search results now automatically update script display when top-level script changes

**Implementation**: 
- `UpdateSearchResultDisplayText()` method in SearchViewModel
- Converts IPE terms to new script using `ScriptConverter.Convert()`
- Converts Devanagari book names to new script
- Proper property change notifications via `RaiseAndSetIfChanged()`

**Behavior**: When user changes script in top-level dropdown, both Select a Book tree AND search results update automatically

#### **Event System Fixes**
**Problem Solved**: SearchViewModel font events not triggering in real-time

**Root Cause**: Font event handlers were inside `WhenActivated` lifecycle, only working when search panel was visible/active

**Solution**: Moved font and script event handlers outside `WhenActivated` to ensure they always work
```csharp
// Setup font and script change handlers (outside of WhenActivated so they always work)
SetupFontAndScriptHandlers();
```

#### **Complete Font System Status**
✅ **Font Settings Applied Successfully to All UI Locations:**
1. **Book tab titles** - Uses per-book script fonts dynamically
2. **Book path in status bar** - Uses current script font settings  
3. **Search results (Matching Terms and Books)** - Uses current script font settings with real-time updates
4. **Select a Book tree** - Uses current script font settings
5. **Chapter list dropdown** - Uses current script font settings

✅ **Real-time Font Updates**: All locations update immediately when font settings change
✅ **Multi-script Support**: Different book tabs can use different scripts simultaneously
✅ **Script Change Updates**: Search results automatically convert script display like tree view

## Previous Session Update (2025-08-15)

### ✅ **Major Bug Fixes: Search & Script Conversion Issues Resolved**

#### **Fixed: Incremental Indexing Creating Duplicates**
- **Problem**: Search showing inflated counts (55 instead of 11) due to duplicate documents in index
- **Root Cause**: `BookIndexer.IndexBook` failed to delete old documents - "file" field was StoredField (not searchable)
- **Solution**: Changed "file" from StoredField to StringField in BookIndexer.cs
- **Result**: Search counts now accurate (11 for 'bhikkhusaṅghañca', 20 for 'bhikkhusaṅghena')

#### **Fixed: Devanagari Wildcard Search Failure**
- **Problem**: Wildcard searches like "a*" failed with KeyNotFoundException when script set to Devanagari
- **Root Cause**: Malformed dictionary entry in `Latn2Deva.cs` - had `"\u1E6D', 'h"` instead of `"\u1E6Dh"` for "ṭh"
- **Solution**: Fixed the dictionary entry syntax in CST.Core/Conversion/Latn2Deva.cs
- **Result**: All script conversions work correctly, wildcard searches function in all 14 scripts
- **Test Suite**: Created comprehensive ScriptConverterTests.cs validating round-trip conversions

#### **Search System Status**
- **✅ Basic Search**: Single and multi-term exact searches with accurate counting
- **✅ Wildcard Search**: Now works in all scripts including Devanagari
- **✅ Position-Based Highlighting**: Multi-term highlighting with correct offsets
- **✅ Script Conversion**: All 14 Pali scripts convert correctly
- **✅ Index Integrity**: Incremental indexing properly replaces documents

## Previous Session Update (2025-08-10)

### 🚀 **Search Implementation Phase 1 & 2 Complete**

#### Phase 1: Core SearchService ✅
- **SearchService Created**: Full Lucene.NET integration with position-based search
- **Search Models Defined**: SearchQuery, SearchResult, MatchingTerm, BookOccurrence, TermPosition
- **ISearchService Interface**: Clean async API for search operations
- **Core Features Implemented**:
  - Single-term exact/wildcard/regex search
  - Multi-term search (basic, phrase/proximity TODO)
  - Book filtering by collection (Vinaya, Sutta, etc.)
  - Position and offset retrieval for highlighting
  - Search result caching
  - Script conversion for display
- **DI Registration**: SearchService registered in App.axaml.cs

#### Phase 2: SearchViewModel ✅
- **MVVM Implementation**: Full ReactiveUI-based view model with IActivatableViewModel
- **Live Search**: Debounced search-as-you-type with 500ms throttle
- **Reactive Commands**: Search, Clear, and OpenBook commands
- **Collections Management**: Terms and Occurrences with automatic merging
- **Statistics Tracking**: Real-time word/occurrence counts
- **Event System**: OpenBookRequested event for book navigation with search terms

#### Phase 3: SearchPanel UI ✅
- **Mac-Native Design**: Dockable panel with modern Avalonia styling
- **Two-Column Layout**: Terms list and book occurrences with splitter
- **Search Controls**: Text input, mode selector (Exact/Wildcard/Regex), collection filters
- **Book Filters**: Expandable section with toggle switches for Pitaka/Commentary levels
- **Interactive Features**: Double-click to open book, keyboard shortcuts (Enter/Escape)
- **Status Display**: Real-time search statistics and loading indicator
- **Responsive UI**: Proper data binding and event handling
- **Build Status**: ✅ Compiles successfully

## Previous Session Updates (2025-08-09)

### ✅ **MAJOR MILESTONE: Incremental Indexing Bug Fixes Complete**
- **Bug Fix #1 - Incremental Detection**: Fixed issue where file changes weren't being detected for indexing by removing conditional check in `App.axaml.cs:line 86`
- **Bug Fix #2 - Settings Enhancement**: Added logic to save default index directory to settings when created for first time in `IndexingService.cs:lines 46-49`
- **Bug Fix #3 - Critical Performance Fix**: Fixed major bug where incremental indexing processed all 217 books instead of only changed files
  - **Root Cause**: `BookIndexer.cs:lines 79-88` was indexing all books with `DocId < 0`, not just the changed ones
  - **Solution**: Modified `IndexAll` method to only process books in the `changedFiles` list during incremental updates
  - **Impact**: Incremental indexing now properly handles single file changes without reprocessing entire corpus
  - **Test Validation**: Created `IncrementalIndexingOnlyChangedBooksTest` confirming fix works correctly

### ✅ **PREVIOUS MILESTONE: Complete Indexing System Implementation**
- **Full Index Implementation**: Successfully implemented all phases 1-3 of the indexing plan, with all 217 books indexed.
- **CST.Lucene Integration**: Modern Lucene.NET 4.8+ implementation with async support, progress reporting, and cross-platform compatibility.
- **Production Services**: `IndexingService` and `XmlFileDatesService` fully implemented with dependency injection integration.
- **Index Created**: Successfully created search index for all 217 Buddhist texts with position-based search support.

### ✅ **PREVIOUS MILESTONE: Comprehensive Testing & Optimization (Phase 4)**
- **62 Tests Implemented**: Complete test coverage with 100% pass rate including unit, integration, and performance tests.
- **Test Categories**:
  - **Unit Tests**: XmlFileDatesService, IndexingService, corruption recovery, multi-script support
  - **Integration Tests**: Full service integration, cross-service communication, lifecycle management
  - **Performance Tests**: Speed benchmarking, memory optimization, consistency validation
  - **Structure Tests**: Position-based search verification, index integrity, multi-document handling
- **Performance Validated**: All services meet performance targets (< 1s initialization, < 100ms validation, < 10MB memory)

### ✅ **PREVIOUS MILESTONE: Project Cleanup & Refactoring**
- **Dead Code Identified**: Analyzed the `.csproj` file to identify numerous files from a previous architectural approach that were excluded from the build.
- **Inactive Code Archived**: Created a new `CST.Avalonia_inactive` directory to house all legacy and placeholder files, clarifying the current state of the active project.
  - **Moved Files**: `BookService.cs`, `SearchService.cs`, the legacy `MainWindow.axaml` and its code-behind, and other unused View and ViewModel files were relocated.
- **Project File Cleanup**: Removed all obsolete `<Compile Remove>` and `<AvaloniaResource Remove>` tags from `CST.Avalonia.csproj`, making the project file clean and consistent with the active source tree.

### **Previously Completed Milestones**
- **Docking UI**: Migrated to `Dock.Avalonia` for a flexible, multi-pane IDE-style layout.
- **Full State Restoration**: The application saves and restores its complete state (open books, scripts, window size, etc.).
- **Settings Window**: A complete settings dialog is implemented and functional.
- **WebView Confirmed**: The project uses `WebViewControl-Avalonia` for rendering.

### **Key Code Analysis Findings:**

#### 1. **Architecture & Dependencies**
- **`CST.Avalonia.csproj` Analysis**:
  - The project file is now clean and only references active source code.
  - Placeholder and legacy files for features like the `SearchService` are no longer referenced and have been physically moved.
- **Service Configuration (`App.axaml.cs`)**:
  - **Active Services**: `IApplicationStateService`, `ISettingsService`, `IScriptService`, `ILocalizationService`, `IIndexingService`, `IXmlFileDatesService`, `ISearchService`.
  - **Search Integration**: SearchService fully integrated with DI and connected to the Lucene index.

## Current Functionality

### **Working Features**
1.  **Dock-Based UI**: Fully functional IDE-style interface.
2.  **Session Restore**: Application correctly reopens all previously opened books.
3.  **Per-Tab Script Selection**: Each book tab remembers its script setting.
4.  **Persistent Highlighting**: Search terms are saved per-tab and reapplied on restore.
5.  **Settings Dialog**: Functional settings window.
6.  **Advanced Logging**: Configurable Serilog implementation.
7.  **Cross-Platform Build**: `.dmg` packages are being built for macOS.
8.  **Full Indexing System**: Complete Lucene.NET search index for all 217 books with incremental updates.
9.  **Multi-Script Support**: 
    - **Display**: All 14 Pali scripts supported (Devanagari, Latin, Bengali, Cyrillic, Gujarati, Gurmukhi, Kannada, Khmer, Malayalam, Myanmar, Sinhala, Telugu, Thai, Tibetan)
    - **Input**: 9 scripts supported for search/dictionary (missing: Thai, Telugu, Tibetan, Khmer, Cyrillic)
    - **Indexing**: IPE (Internal Phonetic Encoding) with Devanagari analyzers
10. **Production-Ready Services**: Fully tested IndexingService and XmlFileDatesService with 62 comprehensive tests.
11. **✅ Enhanced Search UI**: Production-ready search panel with visual search elements, progress feedback, and redesigned filtering system.
12. **✅ Smart Book Filtering**: Checkbox-based filter UI with "Select All/None" actions, filter summaries, and live book counts.
13. **🆕 Search Result Highlighting**: Position-based highlighting with navigation support (single-term tested).
14. **✅ Complete Font System**: Per-script font configuration with real-time updates across all UI locations.
15. **✅ Per-Script Font Selection**: Native macOS font detection and selection working for all 14 Pali scripts with full persistence.
16. **✅ Script Synchronization**: Search results automatically update when top-level script changes.

## Outstanding Work (High Priority)

1.  **Missing Pali Script Input Parsers** (5 scripts need converters to IPE):
    - **Thai**: Thai script → IPE converter (Thai2Ipe or Thai2Deva)
    - **Telugu**: Telugu script → IPE converter (Telu2Ipe or Telu2Deva)
    - **Tibetan**: Tibetan script → IPE converter (Tibt2Ipe or Tibt2Deva)
    - **Khmer**: Khmer script → IPE converter (Khmr2Ipe or Khmr2Deva)
    - **Cyrillic**: Cyrillic script → IPE converter (Cyrl2Ipe or Cyrl2Deva)
    - **Note**: Display works for all 14 scripts, but input (search/dictionary) only works for 9
    - **Implementation**: May use direct Script→IPE or indirect Script→Deva→IPE path
2.  **UI Language Font System**: 
    - **Localization Font Settings**: Separate font controls for ~20 UI languages (distinct from Pali script fonts)
    - **Font Discovery**: Detect available fonts suitable for each UI language script
    - **Note**: This is separate from Pali script fonts - refers to UI language localization fonts
3.  **Advanced Search Features**:
    - **Phrase Search**: Implement position-based phrase searching with exact word order matching
    - **Proximity Search**: Add proximity operators for terms within specified distances
4.  **Search Filtering & Collections**:
    - **Custom Book Collections**: Implement user-defined book collection feature
5.  **UI Feedback During Operations**:
    - **Indexing Progress**: Show progress bar/spinner during index building
    - **Search State**: Indicate when index is incomplete or being rebuilt
    - **Operation Notifications**: Alert user when long operations complete
6.  **Automatic XML File Updates System**:
    - **Git-Based Update Mechanism**: Implement automatic Tipitaka XML file updates using GitHub REST API
    - **Core Components**:
      - `XmlUpdateService` with Octokit.net integration for GitHub API communication
      - Enhanced `file-dates.json` structure with commit hashes for change detection
      - Settings UI for update control (`EnableAutomaticUpdates`, repository configuration)
    - **Update Workflow**:
      - Check repository commit hash to detect changes (fast path)
      - Compare individual file hashes to identify changed files
      - Download only modified files to minimize bandwidth usage
      - Trigger automatic re-indexing after successful updates
    - **User Experience**: Background updates with progress notifications and user control
    - **Benefits**: No local git dependency, atomic updates, bandwidth-efficient (avoids 1GB+ full repository clones)
7.  **Book Display Features**:
    - **Show/Hide Footnotes Toggle**: Add footnote visibility control (check CST4 UI for exact naming)
    - **Show/Hide Search Hits Toggle**: Add search hit highlighting visibility control (check CST4 UI for exact naming)
    - **Search Hit Restoration**: Fix bug where highlighted search hits are not restored when reopening books at startup
8.  **Search Navigation Enhancement**:
    - Add keyboard shortcuts for search hit navigation (First/Previous/Next/Last)

## Outstanding Work for Beta 1 Release

The following items are prioritized for the upcoming Beta 1 release to ensure production readiness:

1.  **Book Display Bug Fixes** (from item #7 above):
    - Implement search hit restoration on startup
2.  **Automatic XML File Updates System** (from item #6 above):
    - Git-Based update mechanism with GitHub REST API integration

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
- **xUnit + Moq**: Comprehensive test framework with 62 tests

## Build & Run Instructions

```bash
# Navigate to project directory
cd /Users/fsnow/github/fsnow/cst/src/CST.Avalonia

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
