# Implementation Notes

**Purpose**: This document tracks how actual implementations diverged from original planning documents.

**Last Updated**: October 22, 2025

## Overview

The planning documents in this `implemented/` directory represent the original design plans for completed features. However, during implementation, designs often evolve based on discoveries, technical constraints, or better approaches found during development.

This document captures those divergences to provide context for future developers reviewing the planning docs.

## Search System

### Search Implementation ([SEARCH_IMPLEMENTATION.md](search/SEARCH_IMPLEMENTATION.md))

**Original Plan**: Dockable search panel with live search and keyboard-first design

**Actual Implementation**: ✅ **Mostly as planned**
- Dockable panel implemented using Dock.Avalonia
- Live search with debouncing
- Keyboard navigation support
- Full integration with book display

**Notable Changes**:
- Added smart book filtering with checkboxes (not in original plan)
- Added two-column layout with terms list and book occurrences
- Filter summary display when collapsed (enhancement)

### Indexing Implementation ([INDEXING_IMPLEMENTATION.md](search/INDEXING_IMPLEMENTATION.md))

**Original Plan**: Lucene.NET indexing with position-based search

**Actual Implementation**: ✅ **Mostly as planned**
- Lucene.NET 4.8+ implementation
- Position-based indexing for all 217 texts
- Incremental indexing (only process changed files)

**Notable Changes**:
- Added comprehensive test suite (62 tests) not originally planned
- Implemented XmlFileDatesService for change tracking (enhancement)
- Added nullable timestamps and proper state management (improvement over plan)

### Phrase & Proximity Search ([PHRASE_PROXIMITY_SEARCH.md](search/PHRASE_PROXIMITY_SEARCH.md))

**Original Plan**: Multi-word search with phrase matching and proximity search

**Actual Implementation**: ✅ **As planned with enhancements**
- Exact phrase search with quotes (`"evaṃ me"`)
- Proximity search with configurable word distance
- Wildcard expansion in multi-word searches
- Two-color highlighting (blue for primary, green for context)

**Notable Changes**:
- Added tag crossing detection for CST's partial word bolding (not in plan)
- Enhanced to support all 14 Pali scripts for wildcard expansion
- Added IsFirstTerm flag for distinguishing primary vs context terms

## Content Features

### Dark Mode Books ([DARK_MODE_BOOKS.md](content/DARK_MODE_BOOKS.md))

**Original Plan**: CSS media query approach for dark mode book content

**Actual Implementation**: ✅ **As planned**
- WebView book content displays with black background in dark mode
- Color-inverted search highlighting
- FluentTheme integration for UI panels

**Notable Changes**:
- Extended to cover all UI panels (toolbar, status bar, settings) beyond original book content scope
- Added macOS Tahoe Glass icon transparency fix (discovered during implementation)

### Dynamic Welcome Page ([DYNAMIC_WELCOME_PAGE.md](content/DYNAMIC_WELCOME_PAGE.md))

**Original Plan**: Hybrid static/dynamic content with version-aware messaging

**Actual Implementation**: ✅ **As planned with enhancements**
- Static content embedded with app
- Dynamic content fetched from GitHub
- Version-aware announcements
- 24-hour local caching

**Notable Changes**:
- Added startup progress display (XML checking, downloading, indexing) - not in original plan
- Enhanced with external link handling (opens in system browser)
- Added semantic version comparison with pre-release support

### Git-based XML Updates ([GIT_XML_UPDATES.md](content/GIT_XML_UPDATES.md))

**Original Plan**: GitHub API integration for efficient file sync

**Actual Implementation**: ✅ **As planned**
- SHA-based change detection
- Only downloads changed files
- Octokit.NET integration
- Files updated before indexing

**Notable Changes**:
- Reduced logging noise by 95% (300KB+ → 14KB) - discovered during production use
- Enhanced file tracking with nullable timestamps (improvement over plan)
- Optimized startup sequence (not detailed in original plan)

## Navigation Features

### Mul/Attha/Tika Buttons ([MUL_ATTHA_TIKA_BUTTONS.md](navigation/MUL_ATTHA_TIKA_BUTTONS.md))

**Implementation Status**: ✅ **Implemented in CST Reader 5.0**

**Notes**: This feature was researched from CST4 and implemented in CST.Avalonia. The document serves as a reference for the feature specification from CST4.

### Chapter Lists ([CHAPTER_LISTS.md](navigation/CHAPTER_LISTS.md))

**Implementation Status**: ✅ **Implemented in CST Reader 5.0**

**Notes**: Chapter list data structures and navigation implemented based on CST4 analysis. The document serves as a reference for the XML format and CST4 implementation approach.

## General Patterns

### Testing
Most features received more comprehensive testing than originally planned:
- Unit tests, integration tests, and performance tests
- 100% pass rate maintained
- Test coverage not always detailed in planning docs

### Logging
Serilog integration added uniformly across features:
- Structured logging throughout
- Log level configuration
- Reduced noise in production

### Error Handling
More robust error handling than initially planned:
- Graceful degradation when services unavailable
- Offline fallbacks
- User-friendly error messages

### Cross-Platform Considerations
Planning docs were macOS-focused initially, but implementations included:
- Cross-platform API usage (`Environment.SpecialFolder`, etc.)
- Conditional compilation (`#if MACOS`)
- Platform-agnostic .NET 9 patterns

## Using These Notes

When reviewing planning documents:

1. **Start with the plan** - Understand the original design intent
2. **Check these notes** - See if implementation diverged
3. **Review actual code** - Code is the ultimate truth
4. **Update these notes** - If you discover unlisted divergences, add them here

## Contributing

When archiving a newly completed feature:

1. Move planning doc to appropriate `implemented/` subdirectory
2. Add section here documenting any divergences
3. Include date and reasons for changes
4. Link to relevant code or commits if helpful
