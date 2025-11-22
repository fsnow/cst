# CST Reader Release Process

This document describes the complete process for releasing a new version of CST Reader.

**Last Updated:** November 22, 2025
**Current Version:** 5.0.0-beta.3

---

## Overview

The release process consists of five main steps:
1. **Build and notarize packages** on Caracara (macOS-specific security)
2. **Create a git tag** for the release
3. **Create a GitHub release** with release notes
4. **Attach binary files** (DMG installers) to the release
5. **Update welcome-updates.json** to notify users

---

## Pre-Release Checklist (Kestrel)

Before starting the release process, verify on Kestrel:

- [ ] All version strings updated and consistent:
  - `CLAUDE.md` - header, status, dates
  - `Resources/welcome-content.html` - header and footer versions
  - `CST.Avalonia.csproj` - assembly version properties
- [ ] Build succeeds: `dotnet build`
- [ ] Tests pass: `dotnet test` (or acceptable skip rate documented)
- [ ] All changes committed and pushed to `main` branch
- [ ] No critical bugs or blockers

---

## Step 1: Build, Notarize, and Staple Packages (Caracara)

**Machine:** Caracara (macOS build machine with notarization credentials)

### Pull Latest Code

```bash
# Ensure Caracara has latest code
git checkout main
git pull
```

### Build Packages

Build both Apple Silicon and Intel DMG installers:

```bash
# Build Apple Silicon package
./package-macos.sh arm64

# Build Intel package
./package-macos.sh x64
```

Packages are created in the `dist/` directory:
- `dist/CST-Reader-arm64.dmg`
- `dist/CST-Reader-x64.dmg`

### Notarize and Staple

After building, run the notarization script:

```bash
# Notarize and staple both DMG files
./notarize-macos.sh arm64
./notarize-macos.sh x64
```

**What this does:**
1. **Code signs** the DMG with Apple Developer ID
2. **Uploads** DMG to Apple's notarization service
3. **Waits** for Apple to scan and approve (~2-5 minutes)
4. **Staples** the notarization ticket to the DMG

**Important:** This step requires:
- Apple Developer ID certificate installed on Caracara
- App-specific password for notarization API
- Network connectivity to Apple's servers

### Verify Notarization

After stapling completes, verify:

```bash
# Check notarization status
spctl -a -vv -t install dist/CST-Reader-arm64.dmg
spctl -a -vv -t install dist/CST-Reader-x64.dmg
```

Should show: `accepted` and `source=Notarized Developer ID`

### Test Installation

Test both DMGs on Caracara (or Egret for arm64):

```bash
# Mount the DMG
open dist/CST-Reader-arm64.dmg

# Drag to Applications and launch
# Should open without security warnings
```

**If you see "damaged" or security warnings:**
- Notarization failed or wasn't stapled correctly
- Check notarization logs: `xcrun notarytool log <submission-id>`
- Re-run notarization script

---

## Step 2: Create Git Tag

**Machine:** Any (Kestrel, Caracara, or local)

Tags mark specific points in repository history as release versions.

### Create and Push Tag

```bash
# Ensure you're on main branch with latest changes
git checkout main
git pull

# Create annotated tag (replace X with version)
git tag -a v5.0.0-beta.X -m "Release Beta X"

# Push tag to GitHub
git push origin v5.0.0-beta.X
```

### Verify Tag

Check that the tag appears on GitHub:
- Navigate to: `https://github.com/fsnow/cst/tags`
- Verify your new tag is listed

**Note:** Use annotated tags (`-a`) rather than lightweight tags for releases, as they include tagger information and date.

---

## Step 3: Create GitHub Release

Releases on GitHub provide user-friendly download pages and release notes.

### Using GitHub Web Interface

1. Navigate to: `https://github.com/fsnow/cst/releases`
2. Click **"Draft a new release"**
3. Fill in release information:
   - **Tag:** Select the tag you just created (e.g., `v5.0.0-beta.3`)
   - **Release title:** "CST Reader 5.0.0-beta.3" (or appropriate version)
   - **Description:** Write release notes (see template below)
   - **Pre-release:** Check this box for beta releases
4. **DO NOT publish yet** - wait until binaries are attached

### Using GitHub CLI (Alternative)

```bash
# Create draft release
gh release create v5.0.0-beta.X \
  --title "CST Reader 5.0.0-beta.X" \
  --notes-file RELEASE_NOTES.md \
  --draft \
  --prerelease
```

### Release Notes Template

```markdown
# CST Reader 5.0.0-beta.X

**Release Date:** November XX, 2025

## What's New

### New Features
- [Feature 1 description]
- [Feature 2 description]

### Improvements
- [Improvement 1]
- [Improvement 2]

### Bug Fixes
- [Fix 1]
- [Fix 2]

## Known Issues

- [Known issue 1 with workaround if available]
- [Known issue 2]

## Installation

### macOS Requirements
- macOS 11.0 (Big Sur) or later
- Apple Silicon (M1/M2/M3) or Intel processor

### Download
- **Apple Silicon (M1/M2/M3):** `CST-Reader-arm64.dmg`
- **Intel Macs:** `CST-Reader-x64.dmg`

### First Launch
After installation, if you see a security warning:
1. Open System Settings → Privacy & Security
2. Scroll down and click "Open Anyway"
3. Confirm when prompted

## Upgrade Notes

**For Beta 2 and earlier users:** Please delete the contents of `~/Library/Application Support/CSTReader/` before running Beta 3 to ensure a clean start.

## Feedback

Found a bug or have a suggestion?
- **GitHub Issues:** https://github.com/fsnow/cst/issues
- **Email:** help@tipitaka.org

---

**Full Changelog:** https://github.com/fsnow/cst/compare/v5.0.0-beta.2...v5.0.0-beta.3
```

---

## Step 3: Attach Binary Files

Upload the DMG installers to the release.

### Using GitHub Web Interface

1. In the draft release page, scroll to **"Attach binaries by dropping them here or selecting them"**
2. Drag and drop or select both DMG files:
   - `CST-Reader-arm64.dmg` (Apple Silicon)
   - `CST-Reader-x64.dmg` (Intel)
3. Wait for uploads to complete
4. Verify checksums if desired
5. Click **"Publish release"**

### Using GitHub CLI (Alternative)

```bash
# Attach binaries to draft release
gh release upload v5.0.0-beta.X \
  dist/CST-Reader-arm64.dmg \
  dist/CST-Reader-x64.dmg

# Publish the release
gh release edit v5.0.0-beta.X --draft=false
```

### Verify Release

1. Visit the release page: `https://github.com/fsnow/cst/releases/tag/v5.0.0-beta.X`
2. Verify:
   - Both DMG files are attached
   - Release notes are correct
   - Pre-release badge is shown (for betas)
   - Download links work

---

## Step 4: Update welcome-updates.json

The `welcome-updates.json` file controls update notifications shown in the app's welcome page.

### File Location

The file should be stored in the repository at a location accessible via GitHub raw content URL (e.g., `docs/welcome-updates.json` or similar).

### Format

```json
{
  "latestVersion": "5.0.0-beta.3",
  "releaseDate": "2025-11-22",
  "downloadUrl": "https://github.com/fsnow/cst/releases/tag/v5.0.0-beta.3",
  "minimumVersion": "5.0.0-beta.1",
  "announcements": [
    {
      "id": "beta3-release",
      "title": "Beta 3 Released",
      "content": "CST Reader Beta 3 is now available with scroll position restoration and improved session management.",
      "date": "2025-11-22",
      "showUntil": "2025-12-31",
      "targetVersions": ["5.0.0-beta.1", "5.0.0-beta.2"],
      "severity": "info"
    }
  ],
  "criticalNotices": [],
  "versionMessages": {
    "5.0.0-beta.3": {
      "type": "info",
      "title": "You're Running Beta 3",
      "content": "Thank you for testing Beta 3! Please report any issues on GitHub."
    },
    "5.0.0-beta.2": {
      "type": "warning",
      "title": "Beta 3 Available",
      "content": "A newer beta is available with scroll position restoration and other improvements.",
      "downloadUrl": "https://github.com/fsnow/cst/releases/tag/v5.0.0-beta.3"
    }
  }
}
```

### Update Process

1. **Edit the file:**
   ```bash
   # Edit welcome-updates.json with new version info
   nano docs/welcome-updates.json  # or use your editor
   ```

2. **Update these fields:**
   - `latestVersion` - Set to new version (e.g., "5.0.0-beta.3")
   - `releaseDate` - Set to today's date (ISO format)
   - `downloadUrl` - Point to new release page
   - Add announcement for the new release
   - Update `versionMessages` for new and previous versions

3. **Commit and push:**
   ```bash
   git add docs/welcome-updates.json
   git commit -m "Update welcome-updates.json for Beta 3 release"
   git push
   ```

### Verification

The app fetches this file from GitHub main branch. After pushing:

1. Wait a few minutes for GitHub CDN to update
2. Launch the app
3. Verify the welcome page shows correct version information
4. Test with an older version to verify update notification appears

**Note:** The app caches the file for 24 hours, so immediate updates may not appear without clearing the cache at `~/Library/Application Support/CSTReader/cache/`.

---

## Post-Release Verification

After completing all steps, verify:

- [ ] Release appears on GitHub releases page
- [ ] Both DMG files are downloadable
- [ ] Release is marked as pre-release (for betas)
- [ ] welcome-updates.json is committed and pushed
- [ ] App shows correct version when launched from DMG
- [ ] Update notification appears for users on older versions (test with old version if possible)

---

## Rollback Process

If critical issues are discovered after release:

1. **Delete the release** (not the tag) from GitHub
2. **Fix the issues** in the code
3. **Increment patch version** (e.g., beta.3 → beta.3.1)
4. **Follow release process again** with new version

**Do not reuse version numbers** - each release should have a unique version.

---

## Version Numbering

CST Reader follows semantic versioning with pre-release identifiers:

- **Format:** `MAJOR.MINOR.PATCH-PRERELEASE`
- **Example:** `5.0.0-beta.3`

**Pre-release progression:**
- Alpha releases: `5.0.0-alpha.1`, `5.0.0-alpha.2`, ...
- Beta releases: `5.0.0-beta.1`, `5.0.0-beta.2`, ...
- Release candidates: `5.0.0-rc.1`, `5.0.0-rc.2`, ...
- Stable release: `5.0.0`

**Patch releases:**
- For urgent fixes to a beta: `5.0.0-beta.3.1`
- For fixes to stable: `5.0.1`

---

## Release Cadence

**Beta Releases:**
- As needed when significant features are complete
- Typically 2-4 weeks between betas
- All features should be tested before beta release

**Stable Releases:**
- When all planned features are complete and tested
- No critical bugs
- Documentation complete
- Multiple successful beta cycles

---

## Automation Opportunities

Future improvements to automate this process:

1. **GitHub Actions workflow** to:
   - Validate version strings match
   - Run tests automatically
   - Create release draft automatically on tag push
   - Generate release notes from commits

2. **Version bump script** to:
   - Update all version strings in one command
   - Create commit and tag automatically

3. **welcome-updates.json generator** to:
   - Generate update file from release metadata
   - Validate JSON format

See `docs/development/PROPOSED_CLAUDE_SKILLS.md` for skill ideas that could help with releases.

---

## Emergency Hotfix Process

For critical bugs in production:

1. Create hotfix branch from release tag:
   ```bash
   git checkout -b hotfix/5.0.0-beta.3.1 v5.0.0-beta.3
   ```

2. Fix the bug and test thoroughly

3. Update version to patch release (e.g., `5.0.0-beta.3.1`)

4. Merge to main:
   ```bash
   git checkout main
   git merge hotfix/5.0.0-beta.3.1
   ```

5. Follow normal release process with new patch version

6. Delete hotfix branch after release

---

## Troubleshooting

### Tag already exists
```bash
# Delete local tag
git tag -d v5.0.0-beta.X

# Delete remote tag (careful!)
git push origin :refs/tags/v5.0.0-beta.X

# Recreate tag
git tag -a v5.0.0-beta.X -m "Release Beta X"
git push origin v5.0.0-beta.X
```

### Release shows wrong files
- Delete the release (not the tag)
- Recreate the release with correct files

### welcome-updates.json not updating
- Check the file is on main branch
- Clear app cache: `rm -rf ~/Library/Application Support/CSTReader/cache/`
- Wait for GitHub CDN to update (can take 5-10 minutes)

---

## References

- **GitHub Releases Docs:** https://docs.github.com/en/repositories/releasing-projects-on-github
- **Semantic Versioning:** https://semver.org/
- **Git Tagging:** https://git-scm.com/book/en/v2/Git-Basics-Tagging
- **GitHub CLI:** https://cli.github.com/manual/gh_release

---

## Document History

- **2025-11-22:** Initial release process documentation for Beta 3
