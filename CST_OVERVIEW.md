# CST (Chaṭṭha Saṅgāyana Tipiṭaka) Overview

## Project Structure

This repository contains two main applications for reading and searching Buddhist Pali texts:

### CST4 (Original WinForms Application)
- **Location**: `/src/Cst4/`
- **Technology**: .NET Framework 4.8, WinForms, MDI architecture
- **Platform**: Windows only
- **Key Features**:
  - Hierarchical book tree with 217 Buddhist texts
  - Multi-script support (Devanagari, Latin, Thai, Myanmar, etc.)
  - Full-text search using Lucene.NET
  - Page reference tracking (VRI, Myanmar, PTS, Thai editions)
  - MDI (Multiple Document Interface) for multiple book windows

### CST Avalonia (Cross-Platform Migration)
- **Location**: `/src/CST.Avalonia/`
- **Technology**: .NET 9.0, AvaloniaUI, ReactiveUI
- **Platform**: Windows, macOS, Linux
- **Status**: Phase 1 complete, advanced features in development
- **Key Features Implemented**:
  - Modern tabbed interface (replacing MDI)
  - CefGlue browser integration for text display
  - Script conversion system
  - Search highlighting with navigation
  - Page reference status bar (recently completed)
  - Smart cache management

## Core Components

### Shared Core Library
- **Location**: `/src/CST.Core/`
- **Purpose**: Shared data models and conversion logic
- **Key Classes**:
  - `Book.cs`: Book metadata and structure
  - `Books.cs`: Book collection management
  - Script converters (Devanagari ↔ Latin, etc.)

### Data Sources
- **XML Files**: `/Users/fsnow/Cloud-Drive/Projects/CST_UnitTestData/Xml/`
  - 217 Tipitaka book files in XML format
  - Organized hierarchically (Vinaya, Sutta, Abhidhamma)
- **XSL Stylesheets**: `/src/Cst4/Xsl/`
  - Transform XML to HTML for display
  - Script-specific formatting rules

### Search System
- **CST.Lucene**: Full-text search implementation (needs porting to .NET 9)
- **Current State**: Hard-coded search terms in Avalonia version
- **TODO**: Port CST.Lucene project and implement FormSearch dialog

## Key Technical Concepts

### Page References
- Buddhist texts have multiple edition page numbers (VRI, Myanmar, PTS, Thai)
- Page anchors in HTML: `<a name="V1.0023">`, `<a name="M0.0001">`
- Dynamic tracking of current page based on scroll position
- Format: "VRI: 1.23   Myanmar: 1.1   PTS: 1.5   Thai: 1.10"

### Script Conversion
- Pali text can be displayed in 13+ scripts
- Real-time conversion between scripts
- Dual dropdown system: default script + per-book override

### Search Highlighting
- XML `<hi>` tags inserted for search terms
- XSL transforms to HTML `<span class="hit">`
- Blue highlighting for hits, red for current hit
- Navigation: First/Previous/Next/Last buttons

## Migration Status

### ✅ Completed
- Core infrastructure and data integration
- Book tree navigation and display
- Script conversion system
- Search highlighting and navigation
- Page reference tracking
- UI polish (CST4 arrow buttons, clean interface)

### 🚧 In Progress
- Real search functionality (currently hard-coded)
- Settings dialog
- Localization support

### 📋 TODO
- Port CST.Lucene to .NET 9
- Implement FormSearch dialog
- Multi-script search support
- Complete state restoration
- Git integration for updates

## Important Files

### CST4 Key Files
- `FormBookDisplay.cs`: Main book display window
- `FormSelectBook.cs`: Book selection tree
- `FormSearch.cs`: Search dialog
- `Resources.resx`: Localized strings

### CST Avalonia Key Files
- `BookDisplayView.axaml.cs`: CefGlue browser with JavaScript bridge
- `BookDisplayViewModel.cs`: Book display logic and page tracking
- `SimpleTabbedWindow.cs`: Main IDE-style window
- `OpenBookPanel.axaml`: Book selection tree

## Build Instructions

### CST Avalonia
```bash
cd /Users/fsnow/github/fsnow/cst/src/CST.Avalonia
dotnet build
dotnet run
```

### CST4 (Windows only)
Open `Cst4.sln` in Visual Studio and build

## Architecture Notes

### JavaScript-to-C# Communication
- CefGlue's `ExecuteJavaScript()` doesn't return values
- Solution: Use `document.title` as communication channel
- Format: `CST_PAGE_REFS:VRI=V1.0023|MYANMAR=M1.0001|...`

### Threading
- ReactiveUI with custom `ReactiveExceptionHandler`
- UI updates via `Dispatcher.UIThread.Post()`
- Thread-safe navigation and updates

### Cache Management
- Process-ID based cache directories
- Automatic cleanup of orphaned caches
- Multi-instance safe

## Development Guidelines

1. **Maintain CST4 Compatibility**: Match original UI/UX where possible
2. **Cross-Platform First**: Test on Windows, macOS, and Linux
3. **Localization Ready**: Use LocalizationService for all UI strings
4. **Performance**: Handle large documents (1MB+ HTML) efficiently
5. **State Persistence**: Save/restore user preferences and window state