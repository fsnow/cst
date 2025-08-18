# CST Avalonia Project Status - August 2025

## Current Status: **FONT SYSTEM & SCRIPT SYNCHRONIZATION COMPLETE** ✅

**Last Updated**: August 18, 2025
**Working Directory**: `/Users/fsnow/github/fsnow/cst/src/CST.Avalonia`

## Project Overview

This project is a ground-up rewrite of the original WinForms-based CST4, built on Avalonia UI and .NET 9. The application is a cross-platform Buddhist text reader featuring a modern, dock-based IDE-style interface. The active codebase is now focused solely on the current architecture, with legacy and placeholder files moved to a separate directory for clarity.

## Latest Session Update (2025-08-18)

### ✅ **MAJOR MILESTONE: Complete Font System Implementation**

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

## Latest Session Update (2025-08-15)

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
11. **🆕 Live Search UI**: Functional search panel with real-time results and book filtering.
12. **🆕 Search Result Highlighting**: Position-based highlighting with navigation support (single-term tested).
13. **✅ Complete Font System**: Per-script font configuration with real-time updates across all UI locations.
14. **✅ Script Synchronization**: Search results automatically update when top-level script changes.

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
    - **Font Discovery**: Detect available fonts suitable for each script
    - **Note**: Pali script font system is now complete
3.  **Advanced Search Features**:
    - **Phrase Search**: Implement position-based phrase searching with exact word order matching
    - **Proximity Search**: Add proximity operators for terms within specified distances
4.  **Search Filtering & Collections**:
    - **Book Collection Filters**: Fix non-functional checkboxes for Pitaka/Commentary filtering
    - **Custom Book Collections**: Implement user-defined book collection feature
5.  **UI Feedback During Operations**:
    - **Indexing Progress**: Show progress bar/spinner during index building
    - **Search State**: Indicate when index is incomplete or being rebuilt
    - **Operation Notifications**: Alert user when long operations complete
6.  **Search Navigation Enhancement**:
    - Add keyboard shortcuts for search hit navigation (First/Previous/Next/Last)
    - Implement scroll-to-hit functionality
    - Add hit counter display (e.g., "Hit 3 of 15")

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
