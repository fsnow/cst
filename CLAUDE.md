# CST Avalonia Migration - Project Status

## Current Status: **PHASE 1 COMPLETE** ✅

**Last Updated**: July 1, 2025
**Working Directory**: `/Users/fsnow/github/fsnow/cst/src/CST.Avalonia`

## Project Overview

The CST Avalonia migration project has successfully completed Phase 1 implementation with a fully functional cross-platform Buddhist text application. The project has transitioned from the original WinForms-based CST4 to a modern AvaloniaUI-based application with reactive MVVM architecture.

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
- **BookService** with full book tree navigation
- **Application state persistence** with tree expansion states
- **Search service** with Lucene.NET integration

### ✅ **UI Implementation Complete**
- **IDE-style tabbed interface** replacing MDI architecture
- **BookDisplayView** with CefGlue browser integration
- **SearchView** with reactive search functionality
- **SelectBookView** with hierarchical book tree
- **Script conversion system** for multi-language Pali texts
- **Search highlighting** with navigation controls

### ✅ **Text Rendering & Browser Integration**
- **CefGlue.Avalonia** successfully integrated
- **XSL transformation pipeline** for book display
- **Multi-script support** (Devanagari, Latin, Thai, Myanmar, etc.)
- **Search highlighting** with XML `<hi>` tag insertion
- **2MB data URI limitation** solved with temporary file approach
- **Proper script conversion** using ScriptConverter.ConvertBook()

## Technical Architecture

### **Project Structure**
```
CST.Avalonia/
├── Views/
│   ├── SimpleTabbedWindow.axaml      # Main IDE-style window
│   ├── BookDisplayView.axaml         # Book display with CefGlue
│   ├── SearchView.axaml              # Search dialog
│   └── SelectBookView.axaml          # Book selection tree
├── ViewModels/
│   ├── SimpleTabbedWindowViewModel.cs # Main window VM
│   ├── BookDisplayViewModel.cs        # Book display VM
│   ├── SearchViewModel.cs             # Search VM
│   └── SelectBookViewModel.cs         # Book selection VM
├── Services/
│   ├── BookService.cs                 # Book data management
│   ├── SearchService.cs               # Lucene search integration
│   ├── ApplicationStateService.cs     # State persistence
│   └── LocalizationService.cs         # Multi-language support
└── Models/
    ├── BookNode.cs                    # Tree node model
    └── SearchResult.cs                # Search result model
```

### **Key Technologies**
- **AvaloniaUI 11.x** - Cross-platform UI framework
- **ReactiveUI** - Reactive MVVM pattern
- **CefGlue.Avalonia** - Embedded Chromium browser
- **Lucene.NET 4.8.0-beta** - Full-text search
- **Microsoft.Extensions.DI** - Dependency injection
- **.NET 9.0** - Modern .NET platform

## Critical Technical Solutions

### **1. CefGlue Browser Integration**
**Challenge**: Display complex formatted Buddhist texts with highlighting
**Solution**: 
- Embedded Chromium browser with HTML/CSS rendering
- XSL transformation pipeline for XML to HTML conversion
- Temporary file approach to bypass 2MB data URI limitation
- JavaScript bridge for highlight navigation

### **2. Script Conversion System**
**Challenge**: Multi-language Pali text display (13+ scripts)
**Solution**:
- `ScriptConverter.ConvertBook()` for proper XML handling
- Script-specific converters (Deva2Latn, Deva2Thai, etc.)
- LatinCapitalizer for proper capitalization and punctuation
- XSL files for script-specific rendering

### **3. Search Highlighting**
**Challenge**: Preserve XML structure while adding search highlights
**Solution**:
- XML `<hi>` tag insertion with `hit_N` IDs
- Recursive text node processing
- Reactive highlight navigation with current position tracking
- Search term highlighting across script conversions

### **4. State Persistence**
**Challenge**: Maintain application state across sessions
**Solution**:
- JSON-based state serialization
- BitArray pattern for tree expansion states
- Window position and size persistence
- Reactive state updates with automatic saving

## Current Functionality

### **Working Features**
1. **Book Tree Navigation** - Full hierarchical book tree from CST data
2. **Search Interface** - Lucene-powered search with results display
3. **Tabbed Book Display** - IDE-style interface with multiple open books
4. **Script Conversion** - 13+ script support with proper XML handling
5. **Search Highlighting** - Term highlighting with navigation controls
6. **State Persistence** - Application state saved/restored automatically
7. **Cross-Platform** - Runs on Windows, macOS, Linux

### **Recent Fixes**
- ✅ Fixed 2MB data URI limitation using temporary files
- ✅ Fixed XML corruption on script conversion
- ✅ Implemented proper CST4-style script conversion workflow
- ✅ Removed "Unknown" and "IPE" from script dropdown UI

## File Locations

### **Key Implementation Files**
- `/Users/fsnow/github/fsnow/cst/src/CST.Avalonia/Views/BookDisplayView.axaml.cs` - CefGlue browser integration
- `/Users/fsnow/github/fsnow/cst/src/CST.Avalonia/ViewModels/BookDisplayViewModel.cs` - Book display logic
- `/Users/fsnow/github/fsnow/cst/src/CST.Avalonia/Views/SimpleTabbedWindow.axaml` - Main window layout
- `/Users/fsnow/github/fsnow/cst/src/CST.Avalonia/Services/BookService.cs` - Book data management
- `/Users/fsnow/github/fsnow/cst/src/CST.Avalonia/Services/SearchService.cs` - Search functionality

### **Data Sources**
- `/Users/fsnow/Cloud-Drive/Projects/CST_UnitTestData/Xml/` - Buddhist text XML files
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

## Next Phase Priorities

### **Phase 2: Advanced Features**
1. **Dictionary Integration** - Port FormDictionary functionality
2. **Advanced Search** - Complex query builder
3. **Report Generation** - Statistical analysis and export
4. **Localization** - Multi-language UI support
5. **Performance Optimization** - Large document handling

### **Phase 3: Production Readiness**
1. **Testing Suite** - Unit and integration tests
2. **Documentation** - User guides and technical docs
3. **Deployment** - Cross-platform installers
4. **Accessibility** - Screen reader and keyboard support

## Development Notes

### **Architecture Decisions**
- **Tabbed Interface** over MDI for modern UX
- **CefGlue** over native text rendering for formatting flexibility
- **Temporary files** over data URIs for large content
- **JSON state** over binary formats for maintainability
- **ReactiveUI** for responsive data binding

### **Performance Considerations**
- Text rendering optimized for large documents
- Lazy loading of book content
- Efficient tree expansion state management
- Background XSL transformations

### **Cross-Platform Compatibility**
- CefGlue native dependencies included for all platforms
- Font handling abstracted for different OS requirements
- File path handling normalized for cross-platform usage

---

## Quick Start for New Sessions

1. **Working Directory**: `/Users/fsnow/github/fsnow/cst/src/CST.Avalonia`
2. **Build**: `dotnet build`
3. **Run**: `dotnet run`
4. **Test Data**: XML files in `/Users/fsnow/Cloud-Drive/Projects/CST_UnitTestData/Xml/`
5. **Key Entry Points**: `SimpleTabbedWindow.axaml`, `BookDisplayView.axaml`

The project is now in a stable state with core functionality complete and ready for advanced feature development.