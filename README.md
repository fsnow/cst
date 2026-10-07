# Chaṭṭha Saṅgāyana Tipiṭaka (CST) Reader

CST Reader is a cross-platform application for reading and searching the Pāli Tipiṭaka. CST Reader 5 is a ground-up rewrite of the Windows-only CST4, built on .NET 10 and Avalonia UI.

**Status: 5.0.0-beta.8 (October 2026).** CST 5 matches or exceeds CST4 in features, apart from interface localization (see [Known Gaps](#known-gaps)). It adds an AI Assistant you can ask about the text you are reading, with your choice of AI provider; Beta 8 keeps your conversations with it, and Digital Pāḷi Dictionary entries now show the word's root.

CST Reader presents the Tipiṭaka **in Pāli**, rendered in 14 scripts. It does not include translations of the texts; the built-in dictionaries give the meaning of individual words. If you connect an AI model, the optional [AI Assistant](#ai) can translate a passage you select — a translation written by that model on request, not one supplied with the app.

## Download

Get the [latest release](https://github.com/fsnow/cst/releases/latest):

- **macOS 12 or later** — a notarized `.dmg` for Apple Silicon or Intel.
- **Windows 10 or later** — `setup.exe` or a portable zip, for x64 or ARM64. These builds are unsigned, so SmartScreen warns on first run: choose **More info**, then **Run anyway**.

## Features

### Reading
- **Dockable layout**: arrange books and tools side by side in resizable, tabbed panes, and split the reading area to read books next to each other
- **Session Restoration**: restores open books, search highlights, window positions, reading positions, the active tool tab, and the last dictionary lookup
- **Floating Windows**: float a book, the dictionary or a source PDF by dragging its tab out of the main window, and drag it back to re-dock
- **Go To and page references**: jump to a paragraph, or to a VRI, Myanmar, PTS or Thai page; each book shows its current page in every numbering it has
- **Dark Mode**: across all panels and book content, including search highlights recoloured for dark backgrounds

### Text Display & Scripts
- **Multi-Script Support**: the texts in 14 scripts, for both display and search input (Devanagari, Latin, Bengali, Cyrillic, Gujarati, Gurmukhi, Kannada, Khmer, Malayalam, Myanmar, Sinhala, Telugu, Thai, Tibetan)
- **Global and Per-Tab Script Selection**: changing the script re-renders every open book; each tab also remembers its own setting
- **Fonts**: a font per script, for book text and for the interface
- **Find and Zoom**: Find in Page (⌘/Ctrl+F) within the book you are reading, and text zoom remembered per script
- **Script Conversion Quality**: lossless round-trip conversion for 13 of the 14 scripts, verified by a validation framework. The Cyrillic exceptions are inherent to that transliteration scheme — it cannot distinguish certain vowel sequences — not a converter defect.

### Search
- **Full-Text Search**: Lucene.NET 4.8+ with position-based indexing across all 217 texts
- **Query Types**: exact, phrase (quoted), proximity (all-within-a-window), mixed/multiple-phrase, wildcard (`*`/`?`) and regular-expression — all working across the 14 scripts
- **Two-Color Highlighting**: distinct colors for the match anchor vs. the remaining matched words, with occurrence-by-occurrence navigation
- **Filtering**: limit a search by Piṭaka (Vinaya, Sutta, Abhidhamma) and commentary level (Mūla, Aṭṭhakathā, Ṭīkā); results list each matching word and each book with its count

### Dictionaries
- **Several sources in one panel**, with a picker: the two bundled VRI dictionaries — Childers' *A Dictionary of the Pali Language* (1875) and a Pāli-Hindi dictionary — plus the **Digital Pāḷi Dictionary (DPD)** and the **Dictionary of Pāli Proper Names (DPPN)**
- **Self-updating assets**: DPD and DPPN download and keep themselves current; this can be disabled
- **User-controlled**: choose which dictionaries appear, and in what order
- **Attribution**: each dictionary carries its own citation metadata
- **Morphology**: DPD-backed resolution from an inflected form to its lemma, with a lemma report (etymology, root, attested declension or conjugation, frequency)
- **Roots**: a DPD entry shows the word's root, and a root can be looked up directly — `√var` finds √var 1 and √var 2 with the words built on each

### Printing
Print a whole book, or print the current selection.

### Source Texts (View Source PDF)
- **Burmese edition PDFs**: the 1957 and 2010 editions, plus the extra-canonical (Anya) texts, in dockable tabs — downloaded on demand and kept locally
- **Context-Aware Navigation**: opens the PDF at the page matching your position in the rendered book
- Each book offers only the editions it actually has

### AI
No AI feature is available until it is turned on in **Settings ▸ AI**.

**AI Assistant.** Select a passage and ask about it — a word-by-word breakdown, an explanation, or a translation. The answer is written by the AI model you connect, so its quality depends on that model. Each answer is labelled with the passage it was asked about, and conversations are kept, several at a time, by name. Bring your own provider, from the [models.dev](https://models.dev) catalogue or a local runner such as Ollama or LM Studio; keys are stored in the macOS Keychain or Windows DPAPI.

**Access for AI clients.** Coding agents such as Claude Code, and chat clients such as Claude Desktop, can use the app to search the corpus, read passages, use the dictionaries and drive the reader's navigation — answering with real references rather than recalled text. Turn it on under **Settings ▸ AI ▸ Access for AI Clients**, which gives a sample prompt for coding agents and a ready-made MCP configuration for chat clients. The app listens only on the loopback interface.

## Known Gaps

- **The interface is English only.** CST4 offers 24 interface languages; that work is still ahead and needs both a localization system and the translations themselves.
- **Text zoom can leak between scripts.** Zooming one book can re-render a book in another script at the wrong size; setting the zoom again on that book corrects it ([#959](https://github.com/fsnow/cst/issues/959)).
- **Very long selections and the AI Assistant.** A selection longer than roughly 1,100 characters in a non-Latin script (about 2,900 in Latin) is more than can be passed to the AI Assistant; the answer then covers the surrounding passage instead, and says so ([#827](https://github.com/fsnow/cst/issues/827)).
- **Elevated idle CPU on macOS**, from Avalonia's macOS event loop amplified by CEF rather than specific to CST Reader; Beta 6 reduced it, but it is not yet fixed ([#523](https://github.com/fsnow/cst/issues/523)).

## Text Data
The application uses Pāli text data from the separate [tipitaka-xml](https://github.com/VipassanaTech/tipitaka-xml) repository. The texts download automatically on first run and stay current: when a text is corrected, only the changed books are downloaded and re-indexed.

## Development Setup

Development and testing are primarily on macOS; the Windows builds are tested on Windows, x64 and ARM64, before each release.

### Prerequisites
- .NET 10 SDK

### Build & Run
From the repository root:

```bash
dotnet build src/CST.Avalonia
dotnet run --project src/CST.Avalonia
```

On an **Intel Mac**, add `-r osx-x64` to both. Without a runtime identifier the build picks the Apple Silicon WebView package, and the app stops at startup.

```bash
dotnet test src/CST.Avalonia.Tests                              # main suite
dotnet test src/CST.Avalonia.UiTests                           # headless Avalonia suite
dotnet test src/CST.Avalonia.Tests --filter "FullyQualifiedName~CstDockFactoryTests"   # one class
```

### Technical Architecture
- **Stack**: .NET 10, Avalonia UI 11.3, ReactiveUI, Dock.Avalonia, dependency injection
- **WebView Rendering**: WebViewControl-Avalonia (CEF) for book content and search highlighting, and CEF's PDFium for the source PDFs
- **Testing**: 3,200+ tests covering unit, integration, and performance scenarios, plus a headless Avalonia suite for styles
- **Logging**: structured Serilog logging across all components

### macOS Packaging
```bash
cd src/CST.Avalonia
./package-macos.sh arm64     # Apple Silicon
./package-macos.sh x64       # Intel
./notarize-macos.sh arm64    # notarize and staple
./notarize-macos.sh x64
```

Produces self-contained app bundles and DMG installers (requires `brew install create-dmg`). Packaging needs a **Developer ID Application** certificate in the keychain and stops without one; pass `--unsigned` for a local test build. Notarizing reads `APPLE_ID`, `APPLE_APP_PASSWORD` (an app-specific password) and optionally `APPLE_TEAM_ID` from the environment.

### Windows Packaging
```powershell
cd src\CST.Avalonia
.\package-windows.ps1              # x64 (default)
.\package-windows.ps1 -Arch arm64  # ARM64
```

Produces a portable `.zip` and an Inno Setup `setup.exe` (per-user install, no UAC prompt). Requires Inno Setup 6.3+. The builds are not code-signed.

Both architectures cross-build from one host of the matching OS — both DMGs on one Mac, both Windows packages on one Windows machine; only *testing* requires a machine of the target architecture. See [docs/development/RELEASE_PROCESS.md](docs/development/RELEASE_PROCESS.md).

### Project Structure
```
src/CST.Avalonia/          # Main application
├── ViewModels/            # ReactiveUI ViewModels
├── Views/                 # Avalonia XAML views
├── Services/              # Core services, incl. the local API and MCP surface
├── Resources/             # App resources
├── xsl/                   # Book stylesheet (one for all scripts)
└── dictionaries/          # Bundled dictionary data

src/CST.Avalonia.Tests/    # Test suite
src/CST.Avalonia.UiTests/  # Headless Avalonia tests (own process; see its .csproj)
src/CST.Core/              # Script conversion, book catalog, passage reading, source-PDF mappings, shared contracts
src/CST.Lucene/            # Search engine library
src/CST.Lexicon/           # Dictionary asset format
src/CST.Lemma/             # Lemma / morphology support
src/CST.ScriptValidation/  # Round-trip validation harness for the 14 script converters
src/CST.CharacterAnalysis/ # Finds characters in the corpus outside the standard Pāli set
docs/                      # Architecture, features, research, release process
```

`CST.ScriptValidation` and `CST.CharacterAnalysis` are standalone command-line tools, run by hand rather than referenced by the app.
`CST.ScriptValidation` converts Devanagari → IPE → target script → IPE and requires the two IPE forms to
match, which is the evidence behind the round-trip claim above; it has its own
[README](src/CST.ScriptValidation/README.md). `CST.CharacterAnalysis` scans the XML for characters outside
the expected Pāli inventory.

The legacy CST4 sources are no longer in the working tree — see [Legacy CST4 Development](#legacy-cst4-development).

## Legacy CST4 Development

CST4 lives on these branches:

- **`cst_4_1`**: CST 4.1 — the released Windows version (tagged `4.1.0.3-2022-04-05`), .NET Framework with WinForms
- **`cst_4_0`**: CST 4.0 — the previous Windows release (tagged `v4.0.0.15-2020-05-07`), also .NET Framework and WinForms
- **`cst_4_2`**: CST 4.2 development branch featuring Lucene.NET 4.8 upgrade work (never released, but provided the foundation for the current search system)

They were Windows-only applications built with Visual Studio (Visual Studio 2019 for the released 4.1) and WiX Toolset v3 for installer creation, reading their text data from the separate tipitaka-xml repository.

The WinForms CST4 sources, the Visual Studio solution, the WiX installer and its font payload were removed
from `main` in August 2026. They remain in history, and are tagged for direct access:

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

**Why `cst4-final` and not the copy that was on `main`.** The copy on `main` picked up three files of drift
after 4.1 — including a *functional* edit to two transliteration-table keys in `src/CST/Conversion/Latn2Deva.cs`
— so it is not the shipped source. `cst4-main-final` records what was deleted; `cst4-final` records what CST4 is.

CST4 documentation stays in the tree — [docs/reference/cst4/](docs/reference/cst4/), the
[parity checklist](docs/testing/CST4_PARITY_CHECKLIST.md), and the feature specs that cite CST4 behaviour. The
`src/Cst4/...` paths they reference resolve through the tags above.

CST4 4.1 was built with Visual Studio 2019 and WiX Toolset v3; the solution is `src/CST.sln`.

## Documentation & Roadmap

- **Documentation index:** [docs/README.md](docs/README.md) — architecture, implementation notes, feature specs, research, and the release process.
- **Roadmap / planned work:** tracked as [GitHub issues](https://github.com/fsnow/cst/issues) (filter by the `feature` / `enhancement` labels); detailed specs for several features live in [docs/features/planned/](docs/features/planned/).

## License
The Pāli texts are provided by the Vipassana Research Institute (VRI), in the [tipitaka-xml](https://github.com/VipassanaTech/tipitaka-xml) repository.
