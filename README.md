# Chaṭṭha Saṅgāyana Tipiṭaka (CST) Reader

CST Reader is a cross-platform application for reading and searching Pali texts. The current main branch contains CST 5.0 built on modern .NET 10 and Avalonia UI, representing a complete ground-up rewrite from the legacy Windows-only CST4 versions.

## Branch Overview

This repository contains multiple branches representing different stages of CST development:

- **`main`** (current): CST 5.0 - Modern cross-platform reader built on .NET 10 and Avalonia UI with advanced search capabilities
- **`cst_4_2`**: CST 4.2 development branch featuring Lucene.NET 4.8 upgrade work (never released, but provided foundation for current search system)
- **`cst_4_1`**: CST 4.1 - The currently released Windows version (tagged as v4.1.0.3-2022-04-05) built on .NET Framework with WinForms
- **`cst_4_0`**: CST 4.0 - Previous stable Windows release (tagged as v4.0.0.15-2020-05-07) also using .NET Framework and WinForms
- **`cst_avalonia`**: Previous name for the current main branch (now obsolete)

The legacy CST4 branches (4.0, 4.1, 4.2) were Windows-only applications requiring Visual Studio 2022, WiX Toolset v3 for installer creation, and the separate tipitaka-xml repository for text data. These versions used WinForms and .NET Framework with basic Lucene search capabilities.

## Current CST 5.0 Features

CST Reader 5.0 is a modern, cross-platform Pali text reader featuring:

### Core Application
- **Cross-Platform**: Built on .NET 10 and Avalonia UI, supporting macOS (tested) with Windows and Linux compatibility
- **IDE-Style Interface**: Dock-based layout with resizable panels, tab management, and persistent session state
- **Complete Session Restoration**: Saves and restores open books, search highlights, window positions, and scroll positions across sessions
- **Floating Windows**: Book and PDF tabs can be floated into separate windows (button-based, to stay crash-safe with CEF on macOS)
- **Dark Mode**: Full dark-mode support across all panels and book content, including color-inverted search highlights
- **Native Packaging**: Production-ready macOS .dmg packages with proper application branding

### Text Display & Scripts
- **Multi-Script Support**: All 14 Pali scripts supported for both display and search input (Devanagari, Latin, Bengali, Cyrillic, Gujarati, Gurmukhi, Kannada, Khmer, Malayalam, Myanmar, Sinhala, Telugu, Thai, Tibetan)
- **Per-Tab Script Selection**: Each book tab remembers its script setting independently
- **Font Management**: Complete per-script UI font system with native font detection and real-time updates
- **Script Conversion Quality**: Lossless round-trip conversion for 13 of the 14 scripts, verified by a comprehensive validation framework; the few Cyrillic exceptions are an inherent limitation of that transliteration scheme (it cannot distinguish certain vowel sequences), not a converter defect

### Advanced Search System
- **Full-Text Search**: Lucene.NET 4.8+ implementation with position-based indexing for all 217 texts
- **Query Types**: Exact, phrase (quoted), proximity (all-within-a-window), and mixed/multiple-phrase queries; wildcard (`*`/`?`) and regular-expression search — all working across the 14 scripts
- **Two-Color Highlighting**: Distinct colors for the match anchor vs. remaining matched words, with occurrence-by-occurrence Next/Prev navigation
- **Smart Filtering**: Checkbox-based book filters with live counts and category management
- **Incremental Indexing**: Only processes changed files, not the entire text corpus
- **XML Updates**: Automatic GitHub integration for file updates with SHA-based change detection

### Source Texts (View Source PDF)
- **Burmese Edition PDFs**: View the Burmese 1957 and 2010 edition source PDFs in dockable tabs (rendered via CEF's PDFium), downloaded on demand from SharePoint and cached locally
- **Context-Aware Navigation**: Opens the PDF to the page matching the current page in the rendered book

### Technical Architecture
- **Modern Stack**: .NET 10, Avalonia UI 11.x, ReactiveUI, Dependency Injection
- **WebView Rendering**: Uses WebViewControl-Avalonia for book content with search highlighting
- **Comprehensive Testing**: 200+ tests covering unit, integration, and performance scenarios
- **Advanced Logging**: Structured Serilog logging across all components

## Development Setup (macOS)

**Note**: All development and testing has been performed on macOS. Cross-platform compatibility is designed-in but not yet tested on Windows/Linux.

### Prerequisites
- .NET 10 SDK
- macOS development environment
- Git access to this repository

### Build & Run
```bash
# Navigate to project directory
cd src/CST.Avalonia

# Build project
dotnet build

# Run application
dotnet run

# Run all tests
dotnet test
```

### macOS Packaging
Create production packages using the included script:
```bash
# Apple Silicon package (default)
./package-macos.sh

# Intel Mac package
./package-macos.sh x64

# Apple Silicon package (explicit)
./package-macos.sh arm64
```

The packaging script creates:
- Self-contained .NET 10 app bundles with all dependencies
- Proper macOS application structure with Info.plist and icons
- DMG installer files (requires `brew install create-dmg`)
- Support for both Apple Silicon and Intel architectures

### Project Structure
```
src/CST.Avalonia/          # Main application source
├── ViewModels/             # ReactiveUI ViewModels
├── Views/                  # Avalonia XAML views
├── Services/               # Core business logic services
├── Resources/              # App resources, XSL stylesheets
└── Tests/                  # Comprehensive test suite

src/CST.Lucene/            # Search engine library
docs/                       # Architecture, features, research, release process
```

## Text Data
The application requires Pali text data from the separate [tipitaka-xml](https://github.com/VipassanaTech/tipitaka-xml) repository. CST 5.0 includes automatic XML update functionality that downloads text files directly from GitHub as needed.

## Legacy CST4 Development
For working with the legacy Windows versions:
- **CST 4.1** (current release): Switch to `cst_4_1` branch, requires Visual Studio 2022, WiX Toolset v3
- **CST 4.2** (unreleased): Switch to `cst_4_2` branch for Lucene 4.8 development work
- **CST 4.0** (previous release): Switch to `cst_4_0` branch for older stable version

See individual branch READMEs for specific legacy build instructions.

## Documentation & Roadmap

- **Documentation index:** [docs/README.md](docs/README.md) — architecture, implementation notes, feature specs, research, and the release process.
- **Roadmap / planned work:** tracked as [GitHub issues](https://github.com/fsnow/cst/issues) (filter by the `feature` / `enhancement` labels); detailed specs for several features live in [docs/features/planned/](docs/features/planned/).

## License
The texts are provided by the Vipassana Research Institute (VRI). See individual text files for specific attribution and licensing information.


