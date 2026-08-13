# Chaṭṭha Saṅgāyana Tipiṭaka (CST) Reader

CST Reader is a cross-platform application for reading and searching Pāli texts. The current main branch contains CST 5.0, built on .NET 10 and Avalonia UI — a ground-up rewrite of the Windows-only CST4.

**Status: 5.0.0-beta.5.** This is the milestone release: CST 5 now matches or exceeds CST4 in features, apart from interface localization (see [Known Gaps](#known-gaps)). It is also the first release with Windows builds, alongside macOS.

CST Reader presents the Tipiṭaka **in Pāli**, rendered in 14 scripts. It does not include translations of the texts; the built-in dictionaries give the meaning of individual words.

## Branch Overview

This repository contains multiple branches representing different stages of CST development:

- **`main`** (current): CST 5.0 — cross-platform reader on .NET 10 and Avalonia UI
- **`cst_4_2`**: CST 4.2 development branch featuring Lucene.NET 4.8 upgrade work (never released, but provided the foundation for the current search system)
- **`cst_4_1`**: CST 4.1 — the released Windows version (tagged `v4.1.0.3-2022-04-05`), .NET Framework with WinForms
- **`cst_4_0`**: CST 4.0 — previous stable Windows release (tagged `v4.0.0.15-2020-05-07`), also .NET Framework and WinForms

The legacy CST4 branches were Windows-only applications requiring Visual Studio 2022, WiX Toolset v3 for installer creation, and the separate tipitaka-xml repository for text data. Their sources are no longer on `main`; see [Legacy CST4 Development](#legacy-cst4-development) for how to read them.

One further branch, `experimental/cef-controlrecycling-workarounds`, is a 2025 spike rather than a stage of development. It is contained in `main`'s history and needs no attention.

## Features

### Core Application
- **Cross-Platform**: macOS (Apple Silicon and Intel) and Windows (x64 and ARM64)
- **IDE-Style Interface**: dock-based layout with resizable panels, tab management, and persistent session state
- **Session Restoration**: restores open books, search highlights, window positions, reading positions, the active tool tab, and the last dictionary lookup
- **Floating Windows**: float a book by dragging its tab out of the main window, and drag it back to re-dock
- **Dark Mode**: across all panels and book content, including color-inverted search highlights
- **Native Packaging**: notarized macOS `.dmg` installers, and per-user Windows `setup.exe` installers plus portable zips

### Text Display & Scripts
- **Multi-Script Support**: all 14 Pāli scripts for both display and search input (Devanagari, Latin, Bengali, Cyrillic, Gujarati, Gurmukhi, Kannada, Khmer, Malayalam, Myanmar, Sinhala, Telugu, Thai, Tibetan)
- **Global and Per-Tab Script Selection**: changing the script re-renders every open book; each tab also remembers its own setting
- **Font Management**: per-script UI font system with native font detection and real-time updates
- **Script Conversion Quality**: lossless round-trip conversion for 13 of the 14 scripts, verified by a validation framework. The Cyrillic exceptions are inherent to that transliteration scheme — it cannot distinguish certain vowel sequences — not a converter defect.

### Search
- **Full-Text Search**: Lucene.NET 4.8+ with position-based indexing across all 217 texts
- **Query Types**: exact, phrase (quoted), proximity (all-within-a-window), mixed/multiple-phrase, wildcard (`*`/`?`) and regular-expression — all working across the 14 scripts
- **Two-Color Highlighting**: distinct colors for the match anchor vs. the remaining matched words, with occurrence-by-occurrence navigation
- **Smart Filtering**: checkbox book filters with live counts and category management
- **Incremental Indexing**: only changed files are reprocessed
- **XML Updates**: GitHub integration with SHA-based change detection

### Dictionaries
- **Several sources in one panel**, with a picker: the two bundled VRI dictionaries — Childers' *A Dictionary of the Pali Language* (1875) and a Pāli-Hindi dictionary — plus the **Digital Pāḷi Dictionary (DPD)** and the **Dictionary of Pāli Proper Names (DPPN)**
- **Self-updating assets**: DPD and DPPN download and keep themselves current; this can be disabled
- **User-controlled**: choose which dictionaries appear, and in what order
- **Attribution**: each dictionary carries its own citation metadata
- **Morphology**: DPD-backed resolution from an inflected form to its lemma, with a lemma report (etymology, root, paradigm, frequency)

### Printing
Print a whole book, or print the current selection.

### Source Texts (View Source PDF)
- **Burmese edition PDFs**: the 1957 and 2010 editions, plus the extra-canonical (Anya) texts, in dockable tabs — rendered via CEF's PDFium, downloaded on demand and cached locally
- **Context-Aware Navigation**: opens the PDF at the page matching your position in the rendered book
- Each book offers only the editions it actually has

### AI and Agent Access (optional, off by default)
An optional loopback HTTP API and **MCP server** let an AI assistant search the corpus, read passages with their apparatus, use the dictionaries, resolve inflected forms to lemmas, and drive the reader's navigation — returning real references rather than recalled text. It binds only to the loopback interface, requires a per-session token, and stays off until enabled in Settings.

### Technical Architecture
- **Stack**: .NET 10, Avalonia UI 11.3, ReactiveUI, Dock.Avalonia, dependency injection
- **WebView Rendering**: WebViewControl-Avalonia (CEF) for book content and search highlighting
- **Testing**: 1,400+ tests covering unit, integration, and performance scenarios
- **Logging**: structured Serilog logging across all components

## Known Gaps

- **The interface is English only.** CST4 offers 24 interface languages; that work is still ahead and needs both a localization system and the translations themselves.
- **Book content fonts** are not yet user-configurable (UI fonts per script are).
- **Elevated idle CPU on macOS** (~30%), inherent to Avalonia's macOS event loop and amplified by CEF rather than specific to CST Reader.

## Development Setup

Development and testing are primarily on macOS; Windows builds and runs, and is validated per release.

### Prerequisites
- .NET 10 SDK
- Git access to this repository

### Build & Run
```bash
cd src/CST.Avalonia

dotnet build      # build
dotnet run        # run
```

```bash
dotnet test src/CST.Avalonia.Tests                              # full suite
dotnet test src/CST.Avalonia.Tests --filter "FullyQualifiedName~CstDockFactoryTests"   # one class
```

### macOS Packaging
```bash
cd src/CST.Avalonia
./package-macos.sh arm64     # Apple Silicon
./package-macos.sh x64       # Intel
./notarize-macos.sh arm64    # code sign, notarize, staple
```

Produces self-contained app bundles and DMG installers (requires `brew install create-dmg`).

### Windows Packaging
```powershell
cd src\CST.Avalonia
.\package-windows.ps1              # x64 (default)
.\package-windows.ps1 -Arch arm64  # ARM64
```

Produces a portable `.zip` and an Inno Setup `setup.exe` (per-user install, no UAC prompt). Requires Inno Setup 6.3+. Beta builds are unsigned, so Windows SmartScreen shows a warning on first run.

Both architectures cross-build from a single host; only *testing* requires a machine of the target architecture. See [docs/development/RELEASE_PROCESS.md](docs/development/RELEASE_PROCESS.md).

### Project Structure
```
src/CST.Avalonia/          # Main application (the working directory)
├── ViewModels/            # ReactiveUI ViewModels
├── Views/                 # Avalonia XAML views
├── Services/              # Core services, incl. the local API and MCP surface
├── Resources/             # App resources
├── xsl/                   # Per-script book stylesheets
└── dictionaries/          # Bundled dictionary data

src/CST.Avalonia.Tests/    # Test suite
src/CST.Core/              # Book catalog, source-PDF mappings, shared contracts
src/CST.Lucene/            # Search engine library
src/CST.Lexicon/           # Dictionary asset format
src/CST.Lemma/             # Lemma / morphology support
src/CST.ScriptValidation/  # Round-trip validation harness for the 14 script converters
src/CST.CharacterAnalysis/ # Finds characters in the corpus outside the standard Pāli set
docs/                      # Architecture, features, research, release process
```

The last two are standalone command-line tools, run by hand rather than referenced by the app.
`CST.ScriptValidation` converts Devanagari → IPE → target script → IPE and requires the two IPE forms to
match, which is the evidence behind the round-trip claim above; it has its own
[README](src/CST.ScriptValidation/README.md). `CST.CharacterAnalysis` scans the XML for characters outside
the expected Pāli inventory.

The legacy CST4 sources are no longer in the working tree — see [Legacy CST4 Development](#legacy-cst4-development).

## Text Data
The application uses Pāli text data from the separate [tipitaka-xml](https://github.com/VipassanaTech/tipitaka-xml) repository, downloaded automatically on first run and updated as needed.

## Legacy CST4 Development

The WinForms CST4 sources, the Visual Studio solution, the WiX installer and its font payload were removed
from `main` in August 2026. They were 40% of the tracked files in the repository and no part of the CST 5
build, so every search and review had to step around them. Nothing is lost — they remain in history, and are
tagged for direct access:

| Tag | What it holds |
|---|---|
| **`cst4-final`** | **The final CST4** — tip of `cst_4_1` (2022-04-08). Use this one. |
| `cst4-2-final` | The 4.2 line — a partial Lucene.NET 4.8 port and single-word highlighting. **The only CST4 code not reachable from `main`'s history.** |
| `cst4-main-final` | The exact files removed from `main`. Provenance for the removal commit, *not* authoritative — see below. |
| `cst-maui-final` | The MAUI/Blazor proof of concept (`src/CST.MAUI`) |

Read a file without checking anything out:

```bash
git show cst4-final:src/Cst4/FormBookDisplay.cs
git show cst4-final:src/Cst4/Reference/en/pali-english-dictionary.txt > dict.txt
git checkout cst4-final -- src/Cst4          # restore into the working tree if you need to build it
```

**Why `cst4-final` and not the copy that was on `main`.** No work on CST4 was intended after 4.1. The copy
carried on `main` nevertheless picked up three files of drift during CST5 work — including a *functional* edit
to two transliteration-table keys in `src/CST/Conversion/Latn2Deva.cs` — so it is not the shipped source.
`cst4-main-final` records what was deleted; `cst4-final` records what CST4 is.

CST4 documentation stays in the tree — [docs/reference/cst4/](docs/reference/cst4/), the
[parity checklist](docs/testing/CST4_PARITY_CHECKLIST.md), and the feature specs that cite CST4 behaviour. The
`src/Cst4/...` paths they reference resolve through the tags above.

The branches those tags sit on are listed under [Branch Overview](#branch-overview); `cst4-final` is the tip of
`cst_4_1`. Building CST4 needs Visual Studio 2022 and WiX Toolset v3 — see the README on the branch itself.

## Documentation & Roadmap

- **Documentation index:** [docs/README.md](docs/README.md) — architecture, implementation notes, feature specs, research, and the release process.
- **Roadmap / planned work:** tracked as [GitHub issues](https://github.com/fsnow/cst/issues) (filter by the `feature` / `enhancement` labels); detailed specs for several features live in [docs/features/planned/](docs/features/planned/).

## License
The texts are provided by the Vipassana Research Institute (VRI). See individual text files for specific attribution and licensing information.
