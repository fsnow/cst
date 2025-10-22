# CST Documentation

Welcome to the CST (Chaṭṭha Saṅgāyana Tipiṭaka) project documentation.

## Quick Links

- [Project Overview](architecture/OVERVIEW.md)
- [Current Features & Roadmap](../src/CST.Avalonia/CLAUDE.md)
- [Windows Port Planning](features/in-progress/windows/WINDOWS_SUPPORT.md)
- [Beta 3 Testing](testing/BETA_3_TESTING.md)

## Documentation Structure

### 📐 **architecture/** - System Design & Technical Architecture
Core technical documentation about how the system works:
- [Project Overview](architecture/OVERVIEW.md) - High-level project structure
- [Ideal Pali Encoding (IPE)](architecture/IDEAL_PALI_ENCODING.md) - Script-independent encoding system
- [Character Set Analysis](architecture/CHARACTER_SET_ANALYSIS.md) - Unicode character analysis

### 🔧 **implementation/** - Implementation Notes & Postmortems
Lessons learned, known issues, and technical challenges:
- [Cyrillic Encoding Limitation](implementation/CYRILLIC_ENCODING_LIMITATION.md) - Permanent encoding ambiguity
- [Avalonia High CPU on macOS](implementation/AVALONIA_HIGH_CPU.md) - Framework-level CPU usage issue
- [CEF Packaging](implementation/CEF_PACKAGING_ISSUE.md) - Chromium helper app bundling
- [Code Signing](implementation/CODE_SIGNING.md) - macOS Developer ID signing
- [Notarization](implementation/NOTARIZATION_TICKET_ISSUE.md) - Apple notarization troubleshooting

### ✨ **features/** - Feature Planning & Specifications
Feature documentation organized by status:

#### **features/implemented/** - Completed Features (Archival)
Historical planning documents for implemented features. Actual implementation may have diverged from original plans - see [IMPLEMENTATION_NOTES.md](features/implemented/IMPLEMENTATION_NOTES.md) for details.

- **search/** - Full-text search system
  - [Search Implementation](features/implemented/search/SEARCH_IMPLEMENTATION.md)
  - [Lucene Indexing](features/implemented/search/INDEXING_IMPLEMENTATION.md)
  - [Phrase & Proximity Search](features/implemented/search/PHRASE_PROXIMITY_SEARCH.md)

- **content/** - Content display and updates
  - [Dark Mode for Books](features/implemented/content/DARK_MODE_BOOKS.md)
  - [Dynamic Welcome Page](features/implemented/content/DYNAMIC_WELCOME_PAGE.md)
  - [Git-based XML Updates](features/implemented/content/GIT_XML_UPDATES.md)

- **navigation/** - Book navigation features
  - [Mul/Attha/Tika Buttons](features/implemented/navigation/MUL_ATTHA_TIKA_BUTTONS.md)
  - [Chapter Lists](features/implemented/navigation/CHAPTER_LISTS.md)

#### **features/in-progress/** - Current Development
Active work happening now:

- **windows/** - Windows 11 port
  - [Windows Support Plan](features/in-progress/windows/WINDOWS_SUPPORT.md)
  - [WindowsFontService Design](features/in-progress/windows/WINDOWS_FONT_SERVICE.md)

#### **features/planned/** - Future Features
Features planned for future implementation (from CST4 analysis):

- [Dictionaries](features/planned/DICTIONARIES.md) - Pali-English and Pali-Hindi dictionaries
- [Go To](features/planned/GO_TO.md) - Navigate to page/paragraph
- [Show Source PDF](features/planned/SHOW_SOURCE_PDF.md) - View Burmese CST PDFs
- [Localization Strategy](features/planned/LOCALIZATION_STRATEGY.md) - Multi-language UI
- [Vector Search](features/planned/VECTOR_SEARCH.md) - Semantic search (future exploration)

### 🔬 **research/** - Research & Exploration
Exploratory research and investigations:
- [Browser Embedding Options](research/BROWSER_EMBEDDING_OPTIONS.md) - WebView alternatives
- [Git Integration](research/GIT_INTEGRATION.md) - Git-based features

### 📚 **reference/** - Reference Materials
External resources and CST4 feature references:

- **cst4/** - CST4 feature reference documentation
  - [Lucene Indexing](reference/cst4/LUCENE_INDEXING.md)
  - [Search System](reference/cst4/SEARCH.md)
  - [State Management](reference/cst4/STATE_MANAGEMENT.md)
  - [Splash Screen](reference/cst4/SPLASH_SCREEN.md)

- **sinhala/** - Sinhala script reference PDFs
  - Error Corrections (2011)
  - Halanth Analysis (2007)
  - Font Samples (2014)

### 🧪 **testing/** - Test Plans & Results
- [Beta 3 Testing](testing/BETA_3_TESTING.md) - Beta 3 test results and notes

### 📝 **blog/** - Blog Posts & Articles
- [Git Hash Comparison for File Sync](blog/git-hash-compare.md) - Blog post on efficient GitHub file syncing

## Workflow: Feature Document Lifecycle

Documentation follows a clear workflow through the `features/` directory:

1. **Planning** → `features/planned/` - Feature researched, not yet started
2. **Active Development** → `features/in-progress/` - Currently being implemented
3. **Completed** → `features/implemented/` - Feature shipped, planning doc archived

This workflow makes it easy to see what's done, what's being worked on, and what's coming next.

## For Developers

### Implementing a New Feature
1. Check `features/planned/` for existing research
2. Create planning doc in `features/in-progress/[category]/`
3. Implement feature
4. Move doc to `features/implemented/[category]/`
5. Document any divergences in [IMPLEMENTATION_NOTES.md](features/implemented/IMPLEMENTATION_NOTES.md)

### Reporting Implementation Issues
Document in `implementation/` with:
- Clear description of the issue
- Root cause analysis
- Workarounds or solutions
- Whether it's permanent or fixable

### Researching New Ideas
Create exploration docs in `research/` - these may eventually move to `features/planned/`

## For Users

- **Current Features**: See [CLAUDE.md](../src/CST.Avalonia/CLAUDE.md#current-functionality)
- **Planned Features**: See [Outstanding Work](../src/CST.Avalonia/CLAUDE.md#outstanding-work)
- **Known Issues**: See [implementation/](implementation/)
- **Testing**: See [testing/](testing/)

## Contributing

When adding new documentation:
- Choose the appropriate category
- Use clear, descriptive filenames
- Include creation date and status in document headers
- Link related documents
