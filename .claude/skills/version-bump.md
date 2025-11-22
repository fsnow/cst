# Version Bump Skill

Automate version number updates across all CST Reader project files when preparing a new release.

## Usage

When the user requests a version bump (e.g., "bump version to beta 4" or "update version to 5.0.1"), this skill:

1. Validates the target version format (semantic versioning)
2. Finds and updates all files containing version strings
3. Updates "Last Updated" dates to current date
4. Shows a diff of changes for review
5. Commits changes with appropriate message

## Files to Update

The following files contain version strings that must be kept in sync:

### 1. CLAUDE.md
- **Location:** `src/CST.Avalonia/CLAUDE.md`
- **Updates Required:**
  - Line ~6: `**Last Updated**: Month DD, YYYY`
  - Line ~5: `**Current Version:** X.X.X-beta.X`
  - Header comment: Date reference
  - Any version references in "Next Steps" or feature descriptions

### 2. welcome-content.html
- **Location:** `src/CST.Avalonia/Resources/welcome-content.html`
- **Updates Required:**
  - Header version display (search for `<h1>` or version badge)
  - Footer version display
  - Any beta notice version references
  - Date references (month/year)

### 3. CST.Avalonia.csproj
- **Location:** `src/CST.Avalonia/CST.Avalonia.csproj`
- **Updates Required:**
  - `<Version>X.X.X-beta.X</Version>`
  - `<AssemblyVersion>X.X.X.X</AssemblyVersion>`
  - `<FileVersion>X.X.X.X</FileVersion>`
  - `<InformationalVersion>X.X.X-beta.X</InformationalVersion>`

## Implementation Steps

### Step 1: Validate Version Format

Parse the user's version request and validate it follows semantic versioning:
- Format: `MAJOR.MINOR.PATCH` or `MAJOR.MINOR.PATCH-PRERELEASE`
- Examples: `5.0.0`, `5.0.0-beta.4`, `5.0.1-rc.1`

If version format is invalid, ask the user to clarify.

### Step 2: Read Current Files

Read all three files to capture current state before making changes.

### Step 3: Update CLAUDE.md

- Update `**Last Updated**:` to current date (e.g., "November 22, 2025")
- Update `**Current Version:**` to new version
- Check "Next Steps" section for any version-specific references
- Preserve exact markdown formatting

### Step 4: Update welcome-content.html

- Update header version display
- Update footer version display
- Update any beta notice version references
- Update month/year in dates
- Preserve exact HTML formatting

### Step 5: Update CST.Avalonia.csproj

- Update `<Version>` tag
- Update `<AssemblyVersion>` (convert beta.X to numeric: beta.4 → 5.0.0.4)
- Update `<FileVersion>` (same as AssemblyVersion)
- Update `<InformationalVersion>` (full semantic version with prerelease)
- Preserve exact XML formatting

### Step 6: Show Diff

Use git diff or display before/after snippets for each file to show changes.

### Step 7: Commit Changes

Create a commit with message:
```
Bump version to X.X.X-beta.X

Update version strings across all project files:
- CLAUDE.md: Update version and last updated date
- welcome-content.html: Update header/footer versions
- CST.Avalonia.csproj: Update assembly version properties
```

## Version Conversion Rules

For `AssemblyVersion` and `FileVersion` in .csproj:
- Stable releases: `5.0.0` → `5.0.0.0`
- Beta releases: `5.0.0-beta.4` → `5.0.0.4`
- RC releases: `5.0.0-rc.2` → `5.0.0.102` (100 + rc number)
- Alpha releases: `5.0.0-alpha.3` → `5.0.0.203` (200 + alpha number)

## Error Handling

- If files are not found, report error and exit
- If version format is invalid, ask user to clarify
- If git status shows uncommitted changes, warn user before committing
- If version already exists as a git tag, warn user about duplicate

## Example Execution

```
User: "Bump version to beta 4"

Skill executes:
1. Parse version: "5.0.0-beta.4"
2. Read 3 files
3. Update CLAUDE.md (version + date)
4. Update welcome-content.html (header/footer + date)
5. Update CST.Avalonia.csproj (4 version properties)
6. Show diffs for review
7. Commit: "Bump version to 5.0.0-beta.4"
8. Report success with file changes summary
```

## Notes

- Always preserve exact formatting (indentation, line breaks, quotes)
- Update dates to current date when bumping version
- Handle both pre-release and stable versions
- The skill should be idempotent (safe to run multiple times)
- If uncertain about any change, ask the user before proceeding
