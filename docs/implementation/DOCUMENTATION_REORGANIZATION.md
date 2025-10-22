# CST Documentation Reorganization Proposal

**Date**: October 22, 2025
**Purpose**: Consolidate and organize markdown documentation across the CST project

## Current State Analysis

Documentation is currently scattered across three locations:

1. **`devdocs/`** - 18 files (CST4 research, general CST research, Sinhala PDFs)
2. **`markdown/notes/`** - 2 files (postmortem notes)
3. **`src/CST.Avalonia/markdown/`** - 11 files (implementation notes, planning)

**Problem**: Mixed status (completed/TODO/historical), unclear organization, duplication of purpose.

## Proposed New Structure

### Location: `docs/` (single top-level directory)

```
docs/
├── README.md                           # Documentation index
│
├── architecture/                       # Technical architecture & design
│   ├── OVERVIEW.md                     # CST project overview (from devdocs/cst/)
│   ├── IDEAL_PALI_ENCODING.md         # IPE encoding system (from devdocs/cst4/)
│   └── CHARACTER_SET_ANALYSIS.md      # Character set research (from devdocs/cst/)
│
├── implementation/                     # Implementation notes & postmortems
│   ├── CYRILLIC_ENCODING_LIMITATION.md     # Permanent encoding limitation
│   ├── AVALONIA_HIGH_CPU.md                # macOS CPU usage issue
│   ├── CEF_PACKAGING_ISSUE.md              # CEF helper app packaging
│   ├── CODE_SIGNING.md                     # macOS code signing notes
│   └── NOTARIZATION_TICKET_ISSUE.md        # Notarization troubleshooting
│
├── features/                           # Feature design & planning
│   ├── implemented/                    # Completed features (archival reference)
│   │   ├── search/
│   │   │   ├── SEARCH_IMPLEMENTATION.md            # Original search plan
│   │   │   ├── INDEXING_IMPLEMENTATION.md          # Lucene indexing plan
│   │   │   └── PHRASE_PROXIMITY_SEARCH.md          # Advanced search plan
│   │   ├── content/
│   │   │   ├── DARK_MODE_BOOKS.md                  # Dark mode implementation
│   │   │   ├── DYNAMIC_WELCOME_PAGE.md             # Welcome page system
│   │   │   └── GIT_XML_UPDATES.md                  # File sync system
│   │   └── IMPLEMENTATION_NOTES.md                 # Index of divergences from plans
│   │
│   ├── in-progress/                    # Current work
│   │   └── windows/
│   │       ├── WINDOWS_SUPPORT.md              # Windows port planning
│   │       └── WINDOWS_FONT_SERVICE.md         # Font system for Windows
│   │
│   └── planned/                        # Future features (from CST4 research)
│       ├── DICTIONARIES.md                     # Pali dictionary feature
│       ├── GO_TO.md                            # Navigate to page/paragraph
│       ├── SHOW_SOURCE_PDF.md                  # View Burmese PDFs
│       ├── LOCALIZATION_STRATEGY.md            # UI language system
│       ├── MUL_ATTHA_TIKA_BUTTONS.md          # Commentary navigation
│       ├── CHAPTER_LISTS.md                    # Book structure
│       └── VECTOR_SEARCH.md                    # Future: Semantic search
│
├── research/                           # Exploratory/research docs
│   ├── BROWSER_EMBEDDING_OPTIONS.md    # WebView alternatives research
│   └── GIT_INTEGRATION.md              # Git-based features exploration
│
├── reference/                          # Reference materials
│   ├── sinhala/                        # Sinhala script references
│   │   ├── Error_Corrections_2011-05-14.pdf
│   │   ├── Halanth_2007-05-11.pdf
│   │   └── New_Fonts_Sample_2014-11-07.pdf
│   └── CHAPTER_LISTS_REFERENCE.md      # CST4 chapter list format
│
├── testing/                            # Test plans & results
│   └── BETA_3_TESTING.md               # Beta 3 test results
│
└── blog/                               # Blog posts & articles
    └── git-hash-compare.md             # Blog post on Git sync technique
```

## Migration Details

### 1. Archive Completed Planning Documents

**Rationale**: Plans are valuable historical context, but implementation may have diverged. Keep them as archival reference.

**Action**:
- Move to `docs/features/implemented/`
- Add `IMPLEMENTATION_NOTES.md` listing any divergences from original plans
- Preserve creation dates and status headers

**Files**:
- `SEARCH_PLAN.md` → `docs/features/implemented/search/SEARCH_IMPLEMENTATION.md`
- `INDEXING_PLAN.md` → `docs/features/implemented/search/INDEXING_IMPLEMENTATION.md`
- `PHRASE_PROXIMITY_SEARCH_PLAN.md` → `docs/features/implemented/search/PHRASE_PROXIMITY_SEARCH.md`
- `DARK_MODE_BOOKS.md` → `docs/features/implemented/content/DARK_MODE_BOOKS.md`
- `DYNAMIC_WELCOME_PAGE.md` → `docs/features/implemented/content/DYNAMIC_WELCOME_PAGE.md`
- `GIT_XML_UPDATES_PLAN.md` → `docs/features/implemented/content/GIT_XML_UPDATES.md`

### 2. Delete Superseded/Obsolete Documents

**Files to Delete**:
- `SPLASH_SCREEN.md` - Analysis of another app's splash screen, no longer relevant (superseded by Dynamic Welcome Page)
- `SPLASH_SCREEN_IMPROVEMENTS.md` - TODO from Dec 2024, superseded by Welcome Page implementation

### 3. Convert CST4 Research to Feature Plans

**Rationale**: CST4 research documents are really feature specifications for future implementation.

**Action**: Move to `docs/features/planned/` and rename to remove `CST4_` prefix

**Files**:
- `CST4_DICTIONARIES.md` → `docs/features/planned/DICTIONARIES.md`
- `CST4_GO_TO.md` → `docs/features/planned/GO_TO.md`
- `CST4_SHOW_SOURCE_PDF.md` → `docs/features/planned/SHOW_SOURCE_PDF.md`
- `CST4_LOCALIZATION_STRATEGY.md` → `docs/features/planned/LOCALIZATION_STRATEGY.md`
- `CST4_MUL_ATTHA_TIKA_BUTTONS.md` → `docs/features/planned/MUL_ATTHA_TIKA_BUTTONS.md`

**Archive as Reference** (already implemented in Avalonia):
- `CST4_CHAPTER_LISTS.md` → `docs/reference/CHAPTER_LISTS_REFERENCE.md`
- `CST4_IDEAL_PALI_ENCODING.md` → `docs/architecture/IDEAL_PALI_ENCODING.md`
- `CST4_LUCENE_INDEXING.md` → DELETE (superseded by INDEXING_PLAN.md)
- `CST4_SEARCH.md` → DELETE (superseded by SEARCH_PLAN.md)
- `CST4_STATE_MANAGEMENT.md` → DELETE (already implemented, no unique info)
- `CST4_SPLASH_SCREEN.md` → DELETE (superseded by Dynamic Welcome Page)

### 4. Organize Implementation Notes

**Files**:
- `CYRILLIC_ENCODING_LIMITATION.md` → `docs/implementation/CYRILLIC_ENCODING_LIMITATION.md`
- `AVALONIA_HIGH_CPU.md` → `docs/implementation/AVALONIA_HIGH_CPU.md`
- `CEF_PACKAGING_ISSUE.md` → `docs/implementation/CEF_PACKAGING_ISSUE.md`
- `CODE_SIGNING.md` → `docs/implementation/CODE_SIGNING.md`
- `NOTARIZATION_TICKET_ISSUE.md` → `docs/implementation/NOTARIZATION_TICKET_ISSUE.md`

### 5. Move General Research/Architecture Docs

**Files**:
- `CST_OVERVIEW.md` → `docs/architecture/OVERVIEW.md`
- `CST_CHARACTER_SET_ANALYSIS.md` → `docs/architecture/CHARACTER_SET_ANALYSIS.md`
- `CST_BROWSER_EMBEDDING_OPTIONS.md` → `docs/research/BROWSER_EMBEDDING_OPTIONS.md`
- `CST_GIT_INTEGRATION.md` → `docs/research/GIT_INTEGRATION.md`
- `CST_VECTOR_SEARCH.md` → `docs/features/planned/VECTOR_SEARCH.md` (future feature)

### 6. Organize Reference Materials

**Files**:
- `devdocs/Sinhala/*.pdf` → `docs/reference/sinhala/*.pdf` (rename to remove spaces)

### 7. Testing Documentation

**Files**:
- `BETA_3_TESTING.md` → `docs/testing/BETA_3_TESTING.md`

### 8. Blog Posts

**Files**:
- `blog-post-git-hash-compare.md` → `docs/blog/git-hash-compare.md`

### 9. Create Documentation Index

**New File**: `docs/README.md`

```markdown
# CST Documentation

## Quick Links

- [Project Overview](architecture/OVERVIEW.md)
- [Outstanding Features](../src/CST.Avalonia/CLAUDE.md#outstanding-work)
- [Windows Port Planning](features/in-progress/windows/WINDOWS_SUPPORT.md)

## Documentation Structure

- **architecture/** - System design and technical architecture
- **implementation/** - Implementation notes and postmortems
- **features/** - Feature planning, implementation, and specifications
  - `implemented/` - Archival reference for completed features
  - `in-progress/` - Current development work
  - `planned/` - Future features (from CST4 analysis)
- **research/** - Exploratory research and investigations
- **reference/** - Reference materials and external resources
- **testing/** - Test plans and results
- **blog/** - Blog posts and articles

## Key Documents

### For Users
- [Beta 3 Testing Guide](testing/BETA_3_TESTING.md)

### For Developers
- [Implementation Notes Index](features/implemented/IMPLEMENTATION_NOTES.md)
- [Known Issues](implementation/)
- [Feature Roadmap](../src/CST.Avalonia/CLAUDE.md#outstanding-work)

### For Researchers
- [Ideal Pali Encoding (IPE) System](architecture/IDEAL_PALI_ENCODING.md)
- [Cyrillic Encoding Limitation](implementation/CYRILLIC_ENCODING_LIMITATION.md)
```

## Benefits of This Structure

### Clear Separation of Concerns
- **Architecture** - How the system works
- **Implementation** - What we learned building it
- **Features** - What we're building (past/present/future)
- **Research** - What we're exploring
- **Reference** - External materials

### Status is Clear from Location
- `features/implemented/` - Already done (archival)
- `features/in-progress/` - Working on now
- `features/planned/` - Future work (from CST4 analysis)

### Single Source of Truth
- No more `devdocs/` vs `markdown/` vs `src/CST.Avalonia/markdown/`
- Everything in `docs/` at project root

### Easy to Maintain
- Archive completed work to `features/implemented/`
- Planning docs stay in `features/planned/` until started
- Move to `features/in-progress/` when active
- Delete obsolete/superseded docs

### Better for New Contributors
- `docs/README.md` provides navigation
- Clear structure shows what's done vs TODO
- Implementation notes document learnings

## Migration Script

Would you like me to create a migration script that:
1. Creates the new `docs/` structure
2. Moves files with git history preservation (`git mv`)
3. Deletes obsolete files
4. Creates `docs/README.md` and `IMPLEMENTATION_NOTES.md`
5. Updates any internal cross-references

This can be done in phases:
- **Phase 1**: Create structure, move files (git preserves history)
- **Phase 2**: Update CLAUDE.md to reference new locations
- **Phase 3**: Clean up empty directories

## Alternative: Minimal Reorganization

If the above seems too disruptive, here's a minimal alternative:

```
docs/
├── README.md
├── cst4-research/          # CST4 feature analysis (TODO features only)
├── implementation-notes/   # Postmortems, known issues
├── planning-archive/       # Completed planning docs (archival)
├── planning-active/        # In-progress planning (Windows port)
└── reference/              # PDFs, external materials
```

This preserves more of the current structure while still providing clear organization.

## Recommendation

I recommend the **full reorganization** because:

1. **Clear feature status** - Know at a glance what's done/in-progress/planned
2. **Better for Windows port** - Organized reference for CST4 features
3. **Historical context** - Completed plans preserved as implementation reference
4. **Maintainable** - Clear rules for where new docs go

The migration can be done incrementally without disrupting current work.
