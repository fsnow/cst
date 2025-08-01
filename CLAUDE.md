# CST Avalonia Migration - Project Status

## Current Status: **WEBVIEW MIGRATION COMPLETE** ✅

**Last Updated**: July 20, 2025
**Working Directory**: `/Users/fsnow/github/fsnow/cst/src/CST.Avalonia`

## Project Overview

The CST Avalonia migration project has successfully completed Phase 1 core implementation and is now advancing through feature development and UI refinement. The application demonstrates a fully functional cross-platform Buddhist text reader with modern IDE-style interface, replacing the original WinForms-based CST4.

## Latest Session Update (2025-07-20)

### ✅ **MAJOR MILESTONE: WebView Migration Complete**
- **Successfully migrated from CefGlue to OutSystems WebView** - Resolved all keyboard shortcut issues
- **Copy functionality restored** - Cmd+C/Ctrl+C now working perfectly via JavaScript bridge
- **Status bar fixed** - Page references and scroll tracking fully operational
- **Performance improved** - Cleaner architecture, better resource management

### **Key Accomplishments Today:**

#### 1. **WebView Migration** 
- **Removed CefGlue.Avalonia** dependency completely
- **Added OutSystems.WebView.Avalonia** (v1.0.13)
- **Updated all references** in BookDisplayView, SimpleTabbedWindow, Program.cs
- **Removed obsolete** CstSchemeHandlerFactory.cs and related CefGlue infrastructure
- **Fixed libcef.dylib conflicts** - Clean build with no native library conflicts

#### 2. **Keyboard Shortcuts Fixed**
- **Root Cause**: WebView captures keyboard events at native browser level
- **Solution**: JavaScript-based keyboard capture with document.title communication
- **Implementation**:
  - JavaScript `keydown` event listener with capture phase
  - Detects Cmd+C/Ctrl+C and Cmd+A/Ctrl+A combinations
  - Sends messages to C# via `document.title` 
  - C# executes `WebView.EditCommands.Copy()` and `SelectAll()`
- **Result**: Full keyboard shortcut functionality restored

#### 3. **Status Bar Functionality Restored**
- **Root Cause**: `_isBrowserInitialized` flag was never set after WebView navigation
- **Impact**: Scroll timer was running but all status updates were skipped
- **Fix**: Added `_isBrowserInitialized = true` in `OnNavigationCompleted`
- **Result**: Status bar now shows page references (VRI, Myanmar, PTS, etc.) and updates in real-time

## Recent Major Progress (Previous Sessions)

### ✅ **UI Polish & Navigation Enhancement**
- **CST4 Arrow Images**: Integrated original navigation button images with proper transparency
- **Status Bar Cleanup**: Removed unnecessary "Ready - 0 books available" status bar from Select Book panel
- **Cache Management**: Implemented smart CefGlue cache management with process isolation
- **Multi-Instance Safety**: Each application instance uses isolated cache directories
- **Thread Safety**: Fixed threading exceptions with global ReactiveUI exception handler
- **Highlight Navigation**: Fixed JavaScript bridge for search result navigation

### ✅ **Threading & Exception Handling**
- **Global Exception Handler**: Implemented ReactiveExceptionHandler for unhandled exceptions
- **Custom UI Scheduler**: Created AvaloniaUIThreadScheduler for proper UI thread operations
- **Thread-Safe Navigation**: All UI updates properly dispatched to UI thread
- **Crash Prevention**: Application no longer crashes on threading violations

### ✅ **Search Highlighting System**
- **JavaScript Bridge**: Complete implementation of search highlight navigation
- **Selector Fix**: Corrected JavaScript to target `span.hit` elements from XSLT output
- **Blue/Red Highlighting**: Proper CST4-style highlighting (blue for hits, red for current)
- **Navigation Working**: First/Previous/Next/Last buttons fully functional

### ✅ **Script Management Improvements**
- **Dual-Script Architecture**: Top-level script dropdown for default + per-book script control
- **Title Updates**: Book titles in tabs and status bar update with script changes
- **Script Name Cleanup**: Removed script suffixes from tab titles for cleaner UI
- **ShortNavPath Support**: Using Book.ShortNavPath for concise status bar display

## Major Accomplishments

### ✅ **Core Infrastructure Complete**
- **Avalonia 11.x** project structure with MVVM pattern
- **ReactiveUI** for reactive data binding and commands
- **Dependency injection** with Microsoft.Extensions.DI
- **JSON-based state persistence** system
- **Cross-platform compatibility** (Windows, macOS, Linux)

### ✅ **Data Integration Complete**
- **CST project ported** from .NET Framework 4.8 to .NET 9.0
- **Real CST data models** integrated (Books.cs, Book.cs, etc.)
- **Complete book tree** with 217 XML books properly organized
- **Tree state persistence** using BitArray for expansion states
- **Script conversion system** working across all supported scripts

### ✅ **UI Implementation Complete**
- **IDE-style tabbed interface** replacing MDI architecture
- **BookDisplayView** with CefGlue browser integration
- **OpenBookPanel** as comprehensive FormSelectBook replacement
- **Script dropdown** for Pali text language selection
- **Search highlighting** with navigation controls using CST4 arrow images
- **Clean UI** with unnecessary status bars removed

### ✅ **Text Rendering & Browser Integration**
- **OutSystems.WebView** successfully integrated (migrated from CefGlue)
- **XSL transformation pipeline** for book display
- **Multi-script support** (Devanagari, Latin, Thai, Myanmar, etc.)
- **Search highlighting** with XML `<hi>` tag insertion
- **JavaScript-based keyboard handling** for copy/paste operations
- **Real-time status bar updates** via JavaScript bridge

## Technical Architecture

### **Project Structure**
```
CST.Avalonia/
├── Views/
│   ├── SimpleTabbedWindow.cs          # Main IDE-style window with toolbar
│   ├── BookDisplayView.axaml          # Book display with WebView
│   ├── OpenBookPanel.axaml            # Book selection tree (FormSelectBook replacement)
│   └── [removed unused views]
├── ViewModels/
│   ├── BookDisplayViewModel.cs        # Book display with script conversion
│   ├── OpenBookDialogViewModel.cs     # Book tree management
│   └── [core ViewModels]
├── Services/
│   ├── ScriptService.cs               # Script management and conversion
│   ├── ApplicationStateService.cs     # State persistence
│   ├── LocalizationService.cs         # Multi-language support (basic)
│   └── TreeStateService.cs            # Tree expansion state management
├── Assets/Images/
│   ├── ArrowFirst.png                 # CST4 navigation button images
│   ├── ArrowPrevious.png              # (converted from BMP with transparency)
│   ├── ArrowNext.png
│   └── ArrowLast.png
└── Program.cs                         # Enhanced with smart cache management
```

### **Key Technologies**
- **AvaloniaUI 11.x** - Cross-platform UI framework
- **ReactiveUI** - Reactive MVVM pattern with custom exception handling
- **OutSystems.WebView.Avalonia** - WebView control for browser functionality
- **Lucene.NET 4.8.0-beta** - Full-text search (ready for integration)
- **Microsoft.Extensions.DI** - Dependency injection
- **.NET 9.0** - Modern .NET platform

## Critical Technical Solutions

### **1. WebView Keyboard Event Capture**
**Challenge**: WebView captures keyboard events at native browser level before Avalonia
**Solution**: 
- JavaScript `keydown` event listener with capture phase
- Intercepts Cmd+C/Ctrl+C before default browser behavior
- Communicates with C# via `document.title` messages
- C# handles copy using `WebView.EditCommands.Copy()`
- Also implemented Cmd+A/Ctrl+A for Select All functionality

### **2. Thread Safety & Exception Handling**
**Challenge**: Threading violations causing crashes
**Solution**:
- Global ReactiveUI exception handler (`ReactiveExceptionHandler`)
- Custom UI thread scheduler (`AvaloniaUIThreadScheduler`)
- All UI updates properly dispatched via `Dispatcher.UIThread.Post()`
- Graceful handling of cross-thread operations

### **3. Search Highlighting Navigation**
**Challenge**: JavaScript bridge not finding highlight elements
**Solution**:
- Fixed selector mismatch: `hi[rend="hit"]` → `span.hit` (XSLT transforms XML to HTML)
- Comprehensive JavaScript bridge with fallback selectors
- CST4-style highlighting: blue for hits, red for current hit
- Full navigation: First/Previous/Next/Last with arrow button images

### **4. Multi-Script UI Management**
**Challenge**: Dual-script dropdown architecture from CST4
**Solution**:
- Top-level script dropdown controls default script for new books
- Per-book script dropdown controls individual book display
- Reactive updates: tab titles and status bar reflect current script
- Clean tab titles without script name suffixes

### **5. CST4 UI Compatibility**
**Challenge**: Matching original CST4 appearance and behavior
**Solution**:
- Original arrow button images with ImageMagick transparency conversion
- "Select a Book" terminology matching FormSelectBook.resx
- Removed unnecessary status bars for cleaner interface
- ShortNavPath usage for concise book identification

## Current Functionality

### **Working Features**
1. **Book Tree Navigation** - Complete hierarchical tree from 217 XML books
2. **Tabbed Book Display** - IDE-style interface with multiple open books
3. **Script Conversion** - 13+ script support with proper XML handling
4. **Search Highlighting** - Term highlighting with full navigation controls
5. **Smart Cache Management** - No accumulation, multi-instance safe
6. **Thread-Safe Operations** - No more crashes, proper exception handling
7. **CST4-Style UI** - Original arrow images, clean interface

### **Recent Technical Fixes**
- ✅ **WebView Migration**: Successfully migrated from CefGlue to OutSystems WebView
- ✅ **Keyboard Shortcuts**: Cmd+C/Ctrl+C working via JavaScript event capture
- ✅ **Status Bar**: Fixed scroll tracking by setting `_isBrowserInitialized` flag
- ✅ **Threading Issues**: Global exception handler, custom UI scheduler
- ✅ **Navigation Buttons**: CST4 arrow images with transparency, JavaScript bridge working
- ✅ **UI Polish**: Removed status bars, cleaned up interface
- ✅ **Script Management**: Dual dropdowns working, tab/status updates

## Outstanding Work (Based on TODO Analysis)

### **High Priority Pending Tasks**
1. **Real Search Functionality** - Current search terms are hard-coded, need full Lucene integration
2. **CST.Lucene Project Port** - Need to port CST.Lucene from .NET Framework to .NET 9.0
3. **FormSearch Implementation** - Create functional search dialog replacing hard-coded terms
4. **Multi-Script Search** - Search highlighting currently only works properly in Latin
5. **State Restoration** - Basic infrastructure exists but comprehensive restoration incomplete
6. **Settings Dialog** - CST4 lacked this, new version needs comprehensive settings UI

### **Medium Priority Tasks**
1. **Localization Support** - Multi-language UI (basic service exists)
2. **UI Styling** - Match CST4 appearance more closely
3. **Performance Optimization** - Large document handling improvements
4. **File Change Detection** - Monitor XML files for changes, trigger re-indexing

### **Advanced Features (Future)**
1. **Git Integration** - Built-in client for pulling XML updates from GitHub
2. **Path Configuration** - Configurable locations for XML files, state, Lucene index
3. **Portable Mode** - All data in application directory option
4. **Export/Import** - Settings and state backup/restore

## File Locations

### **Key Implementation Files**
- `/Users/fsnow/github/fsnow/cst/src/CST.Avalonia/Views/BookDisplayView.axaml.cs` - WebView browser with JavaScript bridge
- `/Users/fsnow/github/fsnow/cst/src/CST.Avalonia/ViewModels/BookDisplayViewModel.cs` - Book display logic
- `/Users/fsnow/github/fsnow/cst/src/CST.Avalonia/Views/SimpleTabbedWindow.cs` - Main window with toolbar
- `/Users/fsnow/github/fsnow/cst/src/CST.Avalonia/Program.cs` - Application entry point
- `/Users/fsnow/github/fsnow/cst/src/CST.Avalonia/App.axaml.cs` - Exception handling setup

### **Data Sources**
- `/Users/fsnow/Cloud-Drive/Projects/CST_UnitTestData/Xml/` - Tipitaka XML files (217 books)
- `/Users/fsnow/github/fsnow/cst/src/Cst4/Xsl/` - XSL transformation files
- `/Users/fsnow/github/fsnow/cst/src/CST/` - Core CST data models and conversion

## Build & Run Instructions

```bash
# Navigate to project directory
cd /Users/fsnow/github/fsnow/cst/src/CST.Avalonia

# Build project
dotnet build

# Run application
dotnet run
```

### **Expected Behavior**
- Application starts with "Select a Book" panel on left
- Book selection opens tabs with search highlighting
- Cmd+C/Ctrl+C copies selected text
- Status bar shows page references (VRI, Myanmar, PTS, etc.)
- Arrow buttons navigate between search hits

## Next Session Priorities

### **Critical Next Steps**
1. **Port CST.Lucene Project** - Essential for real search functionality
2. **Implement FormSearch Dialog** - Replace hard-coded search terms
3. **Multi-Script Search Support** - Highlighting in all scripts, not just Latin
4. **Complete State Restoration** - Open tabs, search results, scroll positions
5. **Settings Dialog Implementation** - Configurable paths, fonts, preferences

### **Technical Debt**
1. **Hard-coded search terms** - Currently uses `["buddha", "dhamma"]` for all books
2. **Missing search dialog** - No way for users to input search terms
3. **Incomplete localization** - UI text not internationalized
4. **Limited settings** - No user preferences configuration

## Development Notes

### **Recent Architecture Improvements**
- **WebView Integration**: Clean migration from CefGlue to OutSystems WebView
- **JavaScript Bridge**: Robust communication for keyboard events and status updates
- **Exception Handling**: ReactiveUI global handler prevents crashes
- **UI Thread Safety**: Custom scheduler ensures proper threading

### **Performance Optimizations Made**
- WebView provides better performance than CefGlue
- JavaScript-based keyboard capture eliminates native event conflicts
- Efficient status bar updates via scroll timer
- Proper thread dispatching improves responsiveness

### **Cross-Platform Considerations**
- ImageMagick used for cross-platform image transparency conversion
- Process ID-based cache isolation works on all platforms
- Path handling normalized for Windows/macOS/Linux compatibility

---

## Quick Start for New Sessions

1. **Working Directory**: `/Users/fsnow/github/fsnow/cst/src/CST.Avalonia`
2. **Build**: `dotnet build`
3. **Run**: `dotnet run`
4. **Test Data**: XML files in `/Users/fsnow/Cloud-Drive/Projects/CST_UnitTestData/Xml/`
5. **Current Focus**: Search functionality development (move beyond hard-coded terms)

**Current Status**: WebView migration complete with full keyboard and status bar functionality. Application has solid foundation with excellent UI polish and technical robustness. Ready for search system development and advanced feature implementation.

The project demonstrates excellent technical foundation with modern architecture. The successful WebView migration resolves the last major browser-related issues, paving the way for completing the search functionality to achieve full CST4 feature parity.