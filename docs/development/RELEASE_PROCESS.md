# CST Reader Release Process

This document describes the complete process for releasing a new version of CST Reader.

**Last Updated:** August 2, 2026
**Current Version:** 5.0.0-beta.6 (in development; Beta 5 released 2026-07)

> **Clean-start status for Beta 6: NOT required from Beta 5.** No on-disk format change is expected
> between Beta 5 and Beta 6, so Beta 5 users upgrade in place. Only users coming from **Beta 4 or
> earlier** must delete the data directory (`~/Library/Application Support/CSTReader/` on macOS,
> `%APPDATA%\CSTReader\` on Windows) — the Beta 5 index-offset change (#53) is what they are crossing.
> **Re-confirm this before publishing:** if anything lands during Beta 6 development that changes the
> tokenizer, index format, or an on-disk layout, this flips to a mandatory clean start.
>
> General rule: whenever the tokenizer or index format changes, the clean-start instruction goes FIRST
> in the release notes — not buried in "Upgrade Notes." When it is *not* required, say so explicitly,
> because the beta line has trained users to wipe.

---

## Overview

The release process consists of six main steps:
1. **Build packages** — macOS (build + notarize on Caracara) and Windows (build on a Windows machine)
2. **Create a git tag** for the release
3. **Create the GitHub release AS A DRAFT**, with release notes
4. **Attach binaries** to the draft, then **publish** it (flip draft → published)
5. **Post-publish: update `welcome-updates.json`** to notify users
6. **Post-publish: update `README.md`** to the newly released version

### The draft → attach → publish order is load-bearing

Create the release as a **draft**, attach every artifact, and only then flip it to published. Two things
depend on that order:

- A published release with missing assets is visible to users, and the download links 404.
- **Nothing that points users at the release may be pushed before the release exists.**
  `welcome-updates.json` is fetched live by every running copy of the app, and its `downloadUrl` points at
  the release page. Push it while the release is still a draft and you have announced a release that
  users cannot download. Same for `README.md`.

So steps 5 and 6 are **post-publish by definition**, not merely last. See "Version strings: two timing
buckets" below for which files move when.

### Shipping matrix

Each release ships **four** builds. You need **one machine per OS, not one per architecture** — both
architectures cross-build fine from a single host of the right OS.

| Platform | Arch  | Build on              | Test on           | Artifacts |
|----------|-------|-----------------------|-------------------|-----------|
| macOS    | arm64 | Caracara (Intel)      | Egret / Kestrel   | `CST-Reader-arm64.dmg` (notarized) |
| macOS    | x64   | Caracara              | Caracara          | `CST-Reader-x64.dmg` (notarized) |
| Windows  | x64   | Placid (Win 10 x64 VM)| Kingfisher        | `CST-Reader-<ver>-win-x64-setup.exe`, `…-win-x64-portable.zip` |
| Windows  | arm64 | Placid                | Merlin (arm64 VM) | `CST-Reader-<ver>-win-arm64-setup.exe`, `…-win-arm64-portable.zip` |

**Building is cross-architecture; testing is not.** Nothing in either packaging pipeline is bound to
the host's architecture: `dotnet publish -r <rid>` pulls the target runtime pack from NuGet, the CEF
natives are ordinary NuGet packages, and Inno Setup only *packages* files (`ArchitecturesAllowed=arm64`
is metadata it writes, not code it runs). This is already how the macOS side works — Caracara is an
Intel Mac and builds the Apple Silicon DMG. Verified on the Windows side too: the complete win-x64
package (installer included) was built on an ARM64 host, with every native confirmed x64 by PE header.

What you *cannot* do is run an arm64 build on an x64 machine, so each arm64 artifact still needs a
machine of its own architecture to smoke-test — hence the separate "Test on" column.

Rough disk budget for a two-architecture Windows run: ~1 GB of publish trees, ~0.8 GB of artifacts in
`dist/`, and a NuGet cache that reaches ~5 GB (both CEF packages are 0.7 GB of that). Allow ~8 GB free.
Inno Setup's lzma2 compression is single-threaded and dominates the wall clock (~5 min per
architecture on a fast machine, longer on a small VM), so give the build VM as many cores as you can.

**The two Windows builds are not interchangeable.** `CST.Avalonia.csproj` selects a different CEF
native package per RID — `WebViewControl-Avalonia` for `win-x64`, `WebViewControl-Avalonia-ARM64` for
`win-arm64`. Publishing a RID without its matching package produces a build with **no `libcef.dll` at
all** (RID-mismatched natives are silently dropped) and every book view fails at CEF init.
`package-windows.ps1` guards against this explicitly, so let it fail rather than working around it.

---

## Toolchain Requirements

**Both build machines need the .NET 10 SDK** — Kestrel (build/test) and Caracara (release packaging).
As of Beta 5 the solution targets `net10.0` (.NET 9 reached end of support; see #48). Verify before building:

```bash
dotnet --list-sdks   # expect a 10.0.x entry
```

If missing, install via the official `.pkg` from https://dotnet.microsoft.com/download/dotnet/10.0
(Apple-Silicon installer on Kestrel, Intel installer on Caracara). The CST.MAUI POC additionally needs
`sudo dotnet workload install maui`, but it is not part of the shipping macOS app.

**Windows build machines** additionally need:

- **.NET 10 SDK for the machine's own architecture** — the arm64 SDK on an arm64 host, the x64 SDK on an
  x64 host. Verify with `dotnet --info`; the `RID:` line should read `win-x64` (or `win-arm64`). This is
  about the SDK running natively, *not* about which targets it can build: either SDK builds both
  `win-x64` and `win-arm64`, pulling the target runtime pack from NuGet.
- **Inno Setup 6.3+** for the installer step: `winget install JRSoftware.InnoSetup`. The script finds
  `ISCC.exe` on PATH or in the standard Program Files / `%LOCALAPPDATA%\Programs` locations. Without it
  the script warns and still produces the portable zip. 6.3+ is required for the `x64compatible` and
  `arm64` architecture identifiers used by `CST.Avalonia.iss`.
- **GitHub CLI** (`winget install GitHub.cli`) if you plan to create the release from Windows.

Windows builds are **unsigned** for beta — there is no Windows equivalent of the notarization step, so
users will see a SmartScreen warning ("More info" → "Run anyway"). A real code-signing certificate is a
pre-1.0 follow-up (#28).

---

## Pre-Release Checklist (Kestrel)

Before starting the release process, verify on Kestrel:

- [ ] .NET 10 SDK present (`dotnet --list-sdks` shows 10.0.x) — see Toolchain Requirements

- [ ] All version strings updated and consistent — see **Version strings: two timing buckets** below.
      By release time, bucket A should already be done (it is bumped at the *start* of the cycle);
      verify it rather than discovering it here. These all drifted before Beta 4.
- [ ] Build succeeds: `dotnet build`
- [ ] Tests pass: `dotnet test` (or acceptable skip rate documented)
- [ ] All changes committed and pushed to `main` branch
- [ ] No critical bugs or blockers

---

## Version strings: two timing buckets

Version strings do **not** all move at the same moment. Treating them as one list is what produced the
drift before Beta 4. There are two buckets, and the difference is whether the string describes *what is
being built* or *what has been released*.

### Bucket A — bump at the START of a development cycle

Do this **immediately after publishing** the previous release, not on the eve of the next one. From this
point every dev build self-identifies as the new version, which is what makes bug reports legible.

| File | What |
|---|---|
| `CST.Avalonia.csproj` | `Version`, `InformationalVersion`, `CFBundleVersion`, `CFBundleShortVersionString`, `AssemblyVersion`, `FileVersion` — **canonical source** |
| `Info.plist` | `CFBundleVersion` + `CFBundleShortVersionString` |
| `package-macos.sh` | helper-bundle `CFBundleVersion` / `CFBundleShortVersionString` |
| `Resources/welcome-content.html` | header version + footer version/date |
| `Services/WelcomeUpdateService.cs` | `CurrentAppVersion` default |
| `ViewModels/WelcomeViewModel.cs` | version fallback string |
| `CLAUDE.md` | status line ("Beta N in development") |
| `docs/development/RELEASE_PROCESS.md` | this file's `Current Version` + `Last Updated` + clean-start banner |

**`AssemblyVersion` / `FileVersion` are 4-part numerics and cannot hold a `-beta.N` suffix.** The
convention is that the **fourth field carries the beta number**: `5.0.0-beta.6` → `5.0.0.6`. Do not set
them to `5.0.0.0` and do not leave them behind — they are what Windows shows in file properties.

**The Windows packaging path cannot drift, by construction.** `package-windows.ps1` reads `<Version>`
straight out of `CST.Avalonia.csproj` and passes it to Inno Setup as `/DAppVersion`, so neither
`CST.Avalonia.iss` nor the script carries its own copy — bumping the csproj is enough. But the `.iss`
**`AppId` must NEVER change**: it keys uninstall/upgrade detection and the WinGet `ProductCode`. Changing
it strands every existing installation as a separate product.

### Bucket B — update AFTER the release is published

These announce the release to users. Pushing them early points people at something that does not exist.

| File | What | Why post-publish |
|---|---|---|
| `welcome-updates.json` (repo ROOT) | `currentVersion.beta`, `messages`, `announcements` | Fetched live by every running app; `downloadUrl` targets the release page |
| `README.md` | `**Status: 5.0.0-beta.N.**` + the surrounding description | It is the repo's front page; it should state the newest **published** version, never an unreleased one |

Update both **immediately after flipping the release from draft to published** — not days later. A
README still advertising the previous beta is the most visible form of release drift.

### Not version strings — do not bulk-replace

These contain the version text but must be left alone; a blind find-and-replace corrupts them:

- `Services/VersionComparer.cs` and `Tests/Services/VersionComparerTests.cs` — SemVer parsing **fixtures**
  (e.g. `"5.0.0-beta.5+abc1234"` testing build-metadata stripping). Version-agnostic by design.
- `Tests/ViewModels/WelcomeViewModelTests.cs` — constructed test data, not an assertion about the app's
  own version.
- `docs/architecture/LEMMA_EXPANSION.md` and similar — **historical statements** ("implemented in Beta 5")
  that stay true.

### Release-specific narrative — not a version string, but it goes stale

`Resources/welcome-content.html` also carries prose about the *particular* release: the "please start
clean" notice, the Focus Areas intro, and per-item blurbs like "Windows — brand new in this release".
Renumbering these mechanically produces false claims. Rewrite them as **content, close to release**, once
the cycle's actual changes are known — and check the clean-start notice against the banner at the top of
this document.

---

## Step 1a: Build, Notarize, and Staple macOS Packages (Caracara)

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

## Step 1b: Build Windows Packages

**Machine:** any Windows machine — Placid (the Windows 10 x64 VM on Caracara) builds **both**
architectures. Run the script once per architecture on that one host; it does not need to match the
target. See the shipping matrix above for why, and for what still has to be tested natively.

### Pull Latest Code

```powershell
git checkout main
git pull
```

### Build

```powershell
cd src\CST.Avalonia

# Both, on the same machine — the host's own architecture does not matter
.\package-windows.ps1              # -Arch x64 is the default
.\package-windows.ps1 -Arch arm64
```

Add `-NoInstaller` to produce only the portable zip (useful when Inno Setup isn't installed).

**What the script does:**
1. Reads `<Version>` from `CST.Avalonia.csproj` (aborts if absent, rather than mislabeling a release)
2. Cleans and runs `dotnet publish -c Release -r win-<arch> --self-contained`
3. Stages `xsl/` and `dictionaries/` beside the exe (the app seeds `%APPDATA%\CSTReader` from these on first run)
4. **Asserts `libcef.dll` is present** — this is the packaging failure that matters most; see the warning
   in the shipping matrix above
5. Writes a portable `.zip` (forward-slash entry names, streamed so the 178–205 MB `libcef.dll` isn't
   buffered in memory)
6. Runs Inno Setup to produce `setup.exe`, and prints its SHA256 for the WinGet manifest (#410)

Artifacts land in `src\CST.Avalonia\dist\`:
- `CST-Reader-<version>-win-<arch>-portable.zip` (~218 MB arm64 / ~230 MB x64)
- `CST-Reader-<version>-win-<arch>-setup.exe`

Save the printed **SHA256** of each `setup.exe` — you need both for the WinGet manifest.

### Test Installation

**Each installer must be tested on a machine of its own architecture** — this is the step that does not
cross-build. The arm64 installer will refuse to run on Placid (`ArchitecturesAllowed=arm64`), so test it
on Merlin; test the x64 one on Kingfisher or Placid itself.

There is no notarization to verify, so the smoke test is the whole check:

1. Run `setup.exe`. It is a per-user install (`PrivilegesRequired=lowest`), so there should be **no UAC
   prompt**; it installs to `%LOCALAPPDATA%\Programs\CST Reader`.
2. Expect a SmartScreen warning on first run — "More info" → "Run anyway". This is normal for unsigned
   beta builds.
3. Launch and confirm **a book actually opens**. This is the CEF check: if `libcef.dll` is missing or is
   the wrong architecture, the app window appears but every book view fails. Watch
   `%APPDATA%\CSTReader\logs\cst-avalonia-<date>.log` for `WebView initialized event fired`.
4. On first launch the app downloads the 217 XML books and builds the Lucene index — allow a few minutes
   before the app is usable.
5. Uninstall via Settings → Apps to confirm the uninstall entry works.

**ARM64 note:** the x64 installer is marked `x64compatible`, so it will also install on an ARM64 machine
and run under emulation. Both installers share an `AppId`, so installing the native arm64 build over an
emulated x64 install upgrades it in place.

**ARM64 development note:** `dotnet build` / `dotnet run` work normally on an ARM64 Windows box — no
`-r` needed. This is not automatic, though: `CefGlue.Common.props` hardcodes
`CefGlueTargetPlatform=win-x64` for *any* RID-less Windows build (it only honours `Platform=ARM64`,
which the SDK leaves at `AnyCPU`), which would wire x64 CEF paths into an arm64 process.
`src/CST.Avalonia/Directory.Build.props` compensates by pinning `RuntimeIdentifier=win-arm64` on ARM64
Windows hosts, early enough that CefGlue sees it. Note that dev output therefore lands in
`bin\<Config>\net10.0\win-arm64\`. An explicit `-r` always wins, so packaging is unaffected.

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

**IMPORTANT:** Before writing release notes, review all commits since the previous release:

```bash
# Review commit history
git log v5.0.0-beta.2..v5.0.0-beta.3 --oneline

# Get detailed commit messages
git log v5.0.0-beta.2..v5.0.0-beta.3 --format="%h %s%n%b"
```

This ensures accurate release notes based on actual changes, not assumptions.

```markdown
# CST Reader 5.0.0-beta.X

**Release Date:** <Month DD, YYYY>

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

### Windows Requirements
- Windows 10 1809 or later / Windows 11
- x64 or ARM64 processor

### Download
- **macOS, Apple Silicon (M1/M2/M3):** `CST-Reader-arm64.dmg`
- **macOS, Intel:** `CST-Reader-x64.dmg`
- **Windows, x64:** `CST-Reader-5.0.0-beta.X-win-x64-setup.exe`
- **Windows, ARM64:** `CST-Reader-5.0.0-beta.X-win-arm64-setup.exe`

Portable `.zip` builds are attached for both Windows architectures if you prefer no installer.

Not sure which Windows build you need? Settings → System → About → "System type". The x64 installer
also runs on ARM64 under emulation, but the ARM64 build is faster.

### First Launch — macOS
CST Reader is signed and notarized by Apple. On first launch macOS asks you to confirm you want to open
an app downloaded from the internet — click **Open**. That is the ordinary quarantine prompt, not a
warning.

> Do **not** tell users to go to Privacy & Security → "Open Anyway". That is the path for an app that
> FAILED the Gatekeeper check, and printing it implies the build is not properly signed. A stapled,
> notarized DMG does not need it — verified on Egret, which downloads from GitHub with quarantine applied
> and opens with only the standard confirmation. (Kestrel does demand Open Anyway, but that is its own
> machine-wide inability to validate any vendor's stapled ticket — see
> [NOTARIZATION_TICKET_ISSUE.md](../implementation/NOTARIZATION_TICKET_ISSUE.md) — not something users hit.)

### First Launch — Windows
These beta builds are not code-signed, so Windows SmartScreen will warn you:
1. Click **"More info"**
2. Click **"Run anyway"**

On first launch the app downloads the Tipiṭaka texts and builds its search index. This takes a few
minutes — the welcome page shows progress.

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

## Step 4: Attach Binary Files

Upload the macOS and Windows installers to the release. The Windows artifacts are produced on
different machines than the DMGs, so collect them all in one place first.

### Using GitHub Web Interface

1. In the draft release page, scroll to **"Attach binaries by dropping them here or selecting them"**
2. Drag and drop or select all six files:
   - `CST-Reader-arm64.dmg` (macOS, Apple Silicon)
   - `CST-Reader-x64.dmg` (macOS, Intel)
   - `CST-Reader-5.0.0-beta.X-win-x64-setup.exe`
   - `CST-Reader-5.0.0-beta.X-win-arm64-setup.exe`
   - `CST-Reader-5.0.0-beta.X-win-x64-portable.zip`
   - `CST-Reader-5.0.0-beta.X-win-arm64-portable.zip`
3. Wait for uploads to complete (the Windows artifacts are ~200–250 MB each)
4. Verify checksums if desired
5. Click **"Publish release"**

### Using GitHub CLI (Alternative)

```bash
# Attach binaries to draft release
gh release upload v5.0.0-beta.X \
  dist/CST-Reader-arm64.dmg \
  dist/CST-Reader-x64.dmg \
  dist/CST-Reader-5.0.0-beta.X-win-x64-setup.exe \
  dist/CST-Reader-5.0.0-beta.X-win-arm64-setup.exe \
  dist/CST-Reader-5.0.0-beta.X-win-x64-portable.zip \
  dist/CST-Reader-5.0.0-beta.X-win-arm64-portable.zip

# Publish the release
gh release edit v5.0.0-beta.X --draft=false
```

### Verify Release

1. Visit the release page: `https://github.com/fsnow/cst/releases/tag/v5.0.0-beta.X`
2. Verify:
   - All four platform builds are attached (2 DMG + 2 Windows installers, plus the portable zips)
   - Release notes are correct
   - Pre-release badge is shown (for betas)
   - Download links work

### Update the WinGet Manifest (#410)

Windows distribution also goes through WinGet (`fsnow.CSTReader`). The manifest needs one `Installers`
entry per architecture, each with the `InstallerUrl` pointing at the published GitHub release asset and
the `InstallerSha256` printed by `package-windows.ps1`:

```yaml
Installers:
  - Architecture: x64
    InstallerUrl: https://github.com/fsnow/cst/releases/download/v5.0.0-beta.X/CST-Reader-5.0.0-beta.X-win-x64-setup.exe
    InstallerSha256: <sha printed by package-windows.ps1 -Arch x64>
  - Architecture: arm64
    InstallerUrl: https://github.com/fsnow/cst/releases/download/v5.0.0-beta.X/CST-Reader-5.0.0-beta.X-win-arm64-setup.exe
    InstallerSha256: <sha printed by package-windows.ps1 -Arch arm64>
```

WinGet picks the right one per machine. The `ProductCode` is the Inno Setup `AppId` + `_is1` and must
stay stable across releases.

---

## Step 5: Update welcome-updates.json

The `welcome-updates.json` file controls update notifications shown in the app's welcome page.

### File Location

**`welcome-updates.json` in the REPOSITORY ROOT.** `WelcomeUpdateService` fetches exactly this URL:

```
https://raw.githubusercontent.com/fsnow/cst/main/welcome-updates.json
```

> A second, stale copy used to sit at `docs/welcome-updates.json` with an older schema. It was never
> fetched by anything, so editing it silently notified nobody. It has been deleted — if you find another
> copy, the root file is the live one.

### Format

The live schema (`schemaVersion: 1`) — note `currentVersion` is an object with `stable` and `beta`
channels, and per-version notices live under `messages`, keyed by the version the user is *running*:

```json
{
  "schemaVersion": 1,
  "lastUpdated": "2026-07-29T12:00:00Z",
  "currentVersion": {
    "stable": "4.5.0-this-version-is-not-real",
    "beta": "5.0.0-beta.5"
  },
  "messages": {
    "5.0.0-beta.4": {
      "type": "upgrade",
      "title": "New Beta Available - 5.0.0-beta.5",
      "content": "... Before installing, please delete the contents of your CST Reader data directory for a clean start.",
      "downloadUrl": "https://github.com/fsnow/cst/releases/tag/v5.0.0-beta.5"
    },
    "5.0.0-beta.5": {
      "type": "info",
      "title": "You're Running Beta 5",
      "content": "Thank you for testing CST Reader Beta 5! Please report any issues on GitHub."
    },
    "default": {
      "type": "warning",
      "title": "Outdated Version",
      "content": "A newer version is available.",
      "downloadUrl": "https://github.com/fsnow/cst/releases/latest"
    }
  },
  "announcements": [
    {
      "id": "2026-07-beta5-release",
      "date": "2026-07-29T00:00:00Z",
      "title": "CST Reader Beta 5 Released",
      "content": "...",
      "showUntil": "2026-10-31T00:00:00Z",
      "targetVersions": ["5.0.0-beta.1", "5.0.0-beta.2", "5.0.0-beta.3", "5.0.0-beta.4"]
    }
  ],
  "criticalNotices": []
}
```

`type` is one of `info`, `upgrade`, `warning`. A `messages` entry for the version being released should be
`info` ("You're Running Beta X"); entries for older versions should be `upgrade` and carry a `downloadUrl`.
`default` catches anything unlisted.

### Update Process

1. **Edit the file:**
   ```bash
   # Edit welcome-updates.json with new version info
   nano welcome-updates.json   # repo ROOT, not docs/
   ```

2. **Update these fields:**
   - `currentVersion.beta` - the new version (e.g. "5.0.0-beta.5")
   - `lastUpdated` - today, ISO 8601 with timezone
   - `messages` - an `info` entry for the new version, and `upgrade` entries (with `downloadUrl`) for each
     older version still in the wild
   - `announcements` - one entry for the release, with `targetVersions` listing the versions that should
     see it and a `showUntil` a few months out

3. **Commit and push:**
   ```bash
   git add welcome-updates.json
   git commit -m "Update welcome-updates.json for Beta 5 release"
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

## Step 6: Update README.md

**Do this in the same sitting as Step 5**, right after the release is published. `README.md` is the repo's
front page — for anyone arriving from GitHub search or a link, it *is* the product description, and a
stale version line there is the most visible release drift there is.

Update `README.md:5` and the surrounding paragraph:

```markdown
**Status: 5.0.0-beta.N.** <one or two sentences on what this release is>
```

It should name the newest **published** version — never the in-development one. (Bucket A above bumps the
app's own version at the start of a cycle; the README deliberately lags behind it until release day.)

Check the rest of the file at the same time: the "Known Gaps" section and any feature claims may have been
overtaken by the release you just shipped.

```bash
git add README.md
git commit -m "README: update for Beta N"
git push
```

---

## Post-Release Verification

After completing all steps, verify:

- [ ] Release appears on GitHub releases page and is **published, not still a draft**
- [ ] All six artifacts are downloadable (2 DMG + 2 setup.exe + 2 portable zip)
- [ ] Release is marked as pre-release (for betas)
- [ ] `welcome-updates.json` (repo ROOT) is committed and pushed — Step 5
- [ ] `README.md` states the version just published — Step 6
- [ ] App shows correct version when launched from DMG
- [ ] Update notification appears for users on older versions (test with old version if possible)

### Then open the next cycle

- [ ] **Bump Bucket A to the next version** (see "Version strings: two timing buckets"). Do it now, while
      the release is fresh — this is the step that has historically been skipped, and every dev build and
      bug report between here and the next release is mislabelled until it happens.
- [ ] Update this document's `Current Version`, `Last Updated`, and the clean-start banner for the new
      cycle (state explicitly whether a wipe is required, and from which version).

---

## Rollback Process

If critical issues are discovered after release:

1. **Delete the release** (not the tag) from GitHub
2. **Fix the issues** in the code
3. **Increment patch version** (e.g., beta.N → beta.N.1)
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

- **2026-08-02:** Opened the Beta 6 cycle. Split the version strings into **two timing buckets** — those
  bumped at the *start* of a cycle vs. those updated only *after* the release is published — after the
  old single list proved incomplete (`README.md` was missing entirely) and timing-blind. Added `README.md`
  as **Step 6**, documented the `AssemblyVersion`/`FileVersion` 4th-field convention, called out the
  fixtures that must NOT be bulk-replaced, made the **draft → attach → publish** ordering an explicit
  principle with the reason (nothing announcing a release may be pushed before it exists), added a
  "then open the next cycle" checklist, and replaced the Beta 5 mandatory-clean-start banner with the
  Beta 6 status (not required from Beta 5; required from Beta 4 or earlier).
- **2026-07-29:** Corrected Step 5 — the live file is `welcome-updates.json` in the repo ROOT, not
  `docs/`, and the documented JSON was an older schema. Deleted the stale `docs/` copy. Fixed the
  post-release checklist, which still spoke of two DMGs.
- **2026-07-27:** Build machines cross-compile; only testing needs native hardware (#521).
- **2026-07-25:** Windows release path added (four builds, InnoSetup, WinGet).
- **2025-11-22:** Initial release process documentation for Beta 3
