# Proposed Claude Skills for CST Project

This document describes Claude Skills that would streamline common development tasks for the CST Reader project.

## Overview

Claude Skills are specialized tools that can be invoked to perform complex, multi-step tasks autonomously. These proposed skills address repetitive workflows encountered during CST development and release cycles.

---

## 1. Version Bump Skill ✅ IMPLEMENTED

**Status:** Implemented in `.claude/skills/version-bump.md` (November 22, 2025)

**Purpose:** Automate version number updates across all project files when preparing a new release.

**Use Case:** When releasing a new beta (e.g., Beta 3 → Beta 4), multiple files need version updates:
- `CLAUDE.md` - header date, status banner, "Next Steps" section
- `Resources/welcome-content.html` - header version, footer version
- `CST.Avalonia.csproj` - assembly version properties
- Potentially `welcome-updates.json` after release

**How It Should Work:**
1. Accept target version as parameter (e.g., "5.0.0-beta.4")
2. Find all files containing version strings
3. Update each location with the new version
4. Update dates to current date
5. Show a diff of changes for review
6. Optionally commit changes with appropriate message

**Implementation Notes:**
- Should validate version format (semantic versioning)
- Should handle both pre-release (beta.X) and stable versions
- Should update "Last Updated" dates automatically
- Must preserve exact formatting in each file type (XML, markdown, HTML)

**Example Usage:**
```
User: "Bump version to beta 4"
Skill: Finds and updates all version references, shows diff, commits
```

---

## 2. Release Checklist Generator Skill

**Purpose:** Generate a comprehensive, context-aware release checklist based on the current project state.

**Use Case:** Before releasing a beta, ensure all necessary steps are completed. The checklist should adapt based on:
- Current git state
- Test results
- Documentation status
- Platform (Kestrel vs Caracara)

**How It Should Work:**
1. Analyze current project state:
   - Git status (uncommitted changes?)
   - Test results (any failures?)
   - Version strings (all consistent?)
   - Documentation (CLAUDE.md up to date?)
2. Generate checklist with TodoWrite
3. Include platform-specific steps (building on Caracara, notarization, etc.)
4. Track progress as steps complete

**Checklist Categories:**
- Pre-release verification (versions, tests, git status)
- Build & packaging (platform-specific)
- Testing (DMG installation, smoke tests)
- Release creation (tag, GitHub release, binaries)
- Post-release (welcome-updates.json, announcements)

**Implementation Notes:**
- Should detect which machine (Kestrel vs Caracara) and adjust steps
- Should check if notarization is needed (macOS)
- Should verify all version strings match before proceeding
- Should integrate with existing test infrastructure

**Example Usage:**
```
User: "Generate release checklist for Beta 3"
Skill: Analyzes project, creates TodoWrite checklist with all necessary steps
```

---

## 3. Feature Documentation Sync Skill

**Purpose:** Audit implemented features against CLAUDE.md documentation to find discrepancies.

**Use Case:** Over time, documentation can drift from actual implementation. This skill identifies:
- Features documented as "Outstanding Work" that are actually implemented
- Features implemented but not documented
- Outdated feature descriptions

**How It Should Work:**
1. Parse CLAUDE.md sections:
   - "Current Functionality"
   - "Outstanding Work"
   - "Known Limitations"
2. Scan codebase for evidence of features:
   - Search for related classes/methods
   - Check git history for feature implementation
   - Look for related test files
3. Generate report with findings:
   - Features to move from Outstanding → Implemented
   - Undocumented features to add
   - Outdated descriptions needing updates
4. Optionally update CLAUDE.md with corrections

**Implementation Notes:**
- Should understand CST project structure (ViewModels, Services, Views)
- Should check test files for feature coverage
- Should review git log for feature additions
- Should suggest specific documentation edits

**Example Usage:**
```
User: "Check if documentation is in sync with implementation"
Skill: Analyzes code vs docs, reports discrepancies, suggests updates
```

---

## 4. Welcome Page Preview Skill

**Purpose:** Build and preview the welcome page HTML with current content and styling.

**Use Case:** When updating welcome page content or styles, see changes without running the full app.

**How It Should Work:**
1. Read `Resources/welcome-content.html`
2. Process any dynamic content (version numbers, dates)
3. Inline the Buddha image as base64 (or use placeholder)
4. Generate standalone HTML file
5. Open in default browser for preview
6. Watch for changes and auto-refresh (optional)

**Implementation Notes:**
- Should handle both light and dark mode CSS
- Should inject sample version check banners (up-to-date, update available, outdated)
- Should work without needing actual GitHub API calls
- Could include sample announcements for testing layout

**Example Usage:**
```
User: "Preview the welcome page"
Skill: Generates standalone HTML with test data, opens in browser
```

---

## 5. Session State Audit Skill

**Purpose:** Verify that session state saving/restoration matches documentation and user expectations.

**Use Case:** Ensure that what we claim to save/restore is actually being saved/restored.

**How It Should Work:**
1. Analyze `ApplicationState.cs` model:
   - What properties exist?
   - What's actually being serialized?
2. Trace save/restore call sites:
   - `ApplicationStateService.SaveStateAsync()`
   - `ApplicationStateService.LoadStateAsync()`
   - Where are properties being set?
3. Compare against documentation (CLAUDE.md "Session Restoration" section)
4. Generate report:
   - Properties saved but not documented
   - Properties documented but not saved
   - Properties that should be saved but aren't

**Implementation Notes:**
- Should understand the ApplicationState model hierarchy
- Should trace through ViewModel state capture
- Should check for properties that exist but aren't serialized
- Should verify restoration actually happens on app startup

**Example Usage:**
```
User: "Audit session state implementation"
Skill: Analyzes code, compares to docs, reports what's actually saved/restored
```

---

## 6. Test Suite Health Report Skill

**Purpose:** Generate a comprehensive report on test suite health and coverage.

**Use Case:** Understand test suite status, identify problematic tests, track improvements over time.

**How It Should Work:**
1. Run full test suite
2. Categorize results:
   - Passing tests by category (unit, integration, performance)
   - Skipped tests with reasons
   - Historical pass rate trends (if git history available)
3. Identify patterns:
   - Tests that pass individually but fail in suite
   - Flaky tests (pass/fail inconsistently)
   - Slow tests (>1s execution time)
4. Generate markdown report with:
   - Pass rate statistics
   - Skip reasons breakdown
   - Recommendations for fixes
   - Priority order for addressing skipped tests

**Implementation Notes:**
- Should parse xUnit test results
- Should track test execution times
- Should understand CST test categories (Services, Integration, BugFix, etc.)
- Could suggest which skipped tests are easiest to fix first

**Example Usage:**
```
User: "Generate test suite health report"
Skill: Runs tests, analyzes results, generates detailed markdown report
```

---

## 7. Dependency Update Checker Skill

**Purpose:** Check for outdated NuGet packages and evaluate update safety.

**Use Case:** Keep dependencies current while avoiding breaking changes.

**How It Should Work:**
1. Scan all `.csproj` files for package references
2. Check NuGet for newer versions
3. Categorize updates:
   - Patch updates (1.2.3 → 1.2.4) - usually safe
   - Minor updates (1.2.0 → 1.3.0) - review needed
   - Major updates (1.0.0 → 2.0.0) - breaking changes likely
4. Check release notes for breaking changes
5. Generate report with:
   - Safe updates (patches)
   - Updates needing review (minor)
   - Major updates with breaking change warnings
   - Dependencies with security vulnerabilities

**Implementation Notes:**
- Should understand semantic versioning
- Should fetch release notes from NuGet/GitHub
- Should check for known security vulnerabilities
- Should group related packages (e.g., all Avalonia.* packages)

**Example Usage:**
```
User: "Check for dependency updates"
Skill: Scans packages, checks versions, generates update report
```

---

## Implementation Priority

Based on frequency of use and impact:

1. **High Priority:**
   - Release Checklist Generator (used every release)
   - ~~Version Bump (used every release)~~ ✅ **COMPLETED**

2. **Medium Priority:**
   - Feature Documentation Sync (periodic maintenance)
   - Test Suite Health Report (periodic maintenance)

3. **Low Priority:**
   - Welcome Page Preview (occasional use)
   - Session State Audit (one-time or rare use)
   - Dependency Update Checker (monthly/quarterly)

---

## Notes for Implementation

- These skills should be implemented as separate skill files in `.claude/skills/` directory
- Each skill should have clear inputs/outputs documented
- Skills should use TodoWrite for tracking multi-step processes
- Skills should ask for confirmation before making destructive changes
- Skills should generate reports in markdown for easy review

---

## Future Skill Ideas

Other potential skills to consider:

- **Windows Port Tracker:** Track progress of Windows-specific features vs macOS
- **Script Conversion Validator:** Test all 14 Pali scripts for conversion accuracy
- **Performance Profiler:** Analyze and report on performance bottlenecks
- **Bundle Size Analyzer:** Track DMG size over time, identify bloat
- **Documentation Generator:** Auto-generate API docs from code comments

