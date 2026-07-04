# Windows Support — Plan & Reality Check

**Rewritten:** 2026-07-04 (supersedes the Oct 2025 draft, which was written early in the project and was over-optimistic — see "How this doc changed").
**Target:** Beta 5 goal; a focused-weekend MVP is plausible **if the two POCs below pass**.
**Status:** Not started in code. Two make-or-break POCs must run first.
**Stack:** .NET 10 + Avalonia 11 + WebViewControl-Avalonia (CefGlue/Chromium).

---

## TL;DR

The codebase *is* reasonably well-isolated for cross-platform, but two things the original draft dismissed are the actual work, and both are unproven on Windows:

1. **WebView/CEF lifecycle** — not just "does it render," but does **float/unfloat/dock re-parenting** survive? The whole dock subsystem is built around **macOS-specific SIGSEGV-on-reparent workarounds**. "Same CefGlue package" does not imply "same native window-reparenting behavior." → **POC-1.**
2. **Menus & keyboard shortcuts are 100% macOS `NativeMenu`, gated by `OperatingSystem.IsMacOS()`.** On Windows today there is **no menu bar and no shortcuts** — meaning no way to open the book tree, search, dictionary, Go To, or View Source. This is real UI work, not a project-file tweak. → **POC-2 / build item.**

Everything else (paths, fonts, packaging, project file) is genuinely low-risk plumbing. **WindowsFontService (#29) is NOT needed for MVP** — the existing `FontManager.Current.SystemFonts` fallback is fine; defer it.

**Honest estimate:** if both POCs pass, a single-window MVP (launch → open a book → search → persist state) is a weekend. If POC-1 reveals reparenting SIGSEGVs like macOS had, that alone is a multi-day investigation and the weekend goal slips.

---

## How this doc changed (why the old draft was wrong)

The Oct 2025 draft was written weeks into the project (which began 2025-06-29 as a Claude Code learning exercise) by models that were, frankly, not up to the task — the same provenance as much of the code we've spent recent weeks fixing. Treat its confident ✅/🎉 claims with suspicion. Concrete corrections:

- Said **.NET 9**; we're on **.NET 10**.
- Claimed **"WebView works as-is, no code changes, blocker eliminated."** Half-true: the *managed API* is the same, but the fragile part is native window re-parenting under the dock, which is untested on Windows and was the single hardest thing to get right on macOS.
- Claimed **"all paths are cross-platform ✅."** In this session alone we fixed **two** Windows-breaking `file://` URL bugs (#162 PDF, #222 book HTML). Assume more lurk.
- **Never mentioned that menus/shortcuts are macOS-only.** This is arguably the biggest functional gap.
- Marked **WindowsFontService HIGH priority** while also admitting the fallback works — contradictory. It's a post-MVP enhancement (#29).
- "Phase 1 done ✅" — none of the csproj work has actually been done.

---

## The two POCs — do these FIRST, before any other work

### POC-1 — CEF renders **and re-parents** on Windows  ⭐ make-or-break
**Question:** Does WebViewControl-Avalonia initialize its CEF subprocess on Windows, render a book, **and** survive the dock's float→unfloat→re-dock cycle without crashing?

**Why it's the crux:** On macOS, carrying a live WebView across a re-parent SIGSEGVs; the fix was the controlled dispose-before-move + fresh-browser dance in the float/unfloat button paths (see `docs/architecture/DOCK_WEBVIEW_WORKAROUNDS.md`). Windows CEF may (a) just work, (b) work but need different disposal ordering, or (c) fail in a new way. We won't know until we try.

**Steps:**
1. Apply the csproj changes (below) for `win-x64`.
2. `dotnet run` on a Windows 11 machine/VM; confirm the CEF subprocess starts (watch logs / Task Manager for the CefGlue browser subprocess).
3. Open one book — confirm it paints, script conversion renders, search highlight works.
4. **Float the book window, unfloat it, re-dock it, drag-rearrange tabs.** Watch for crashes/blank WebViews.

**Pass:** book renders and the float/unfloat/re-dock cycle is stable (or stable enough that remaining glitches are cosmetic).
**Fail/partial:** any SIGSEGV-equivalent or blank-after-reparent → this becomes the project. Capture the exact repro and stop; don't paper over it.

**Unknowns to research alongside:** how WebViewControl-Avalonia locates/ships its **browser subprocess** on Windows (macOS uses a helper `.app` + `browser-subprocess-path` switch — see `App.axaml.cs:88-115`). On Windows the CefGlue native assets normally come from NuGet; verify they land in the publish output and that no equivalent of the helper-path switch is required.

### POC-2 — a usable menu/shortcut surface on Windows  ⭐ required for any usable build
**Question:** With native menus macOS-only, what's the minimum Windows menu/shortcut layer?

**Why:** `App.axaml.cs` builds the **View** menu (Select a Book / Search / Dictionary toggles) and **Tools/Book** menu (Go To `Cmd+G`, View Source 1957 `Cmd+E` / 2010 `Cmd+Shift+E`, Look Up `Cmd+D`, Search Selection `Cmd+F`) entirely as `NativeMenu`/`NativeMenuItem`, and calls `SetupNativeMenuEvents()` / `SetupWindowMenuEvents()` **only under `OperatingSystem.IsMacOS()`** (`App.axaml.cs:73-76, 153-158`). On Windows: nothing.

**Options to prototype:**
- **A — in-window `NativeMenuBar`:** Avalonia can render a `NativeMenu` as an in-window menu bar on Windows via a `<NativeMenuBar/>` in the window chrome. Lowest-churn if it works: reuse the same menu model, drop the `IsMacOS()` gate, render the bar in-window on Windows. Verify toggle/check state and gestures behave.
- **B — a Windows `<Menu>`** built in XAML, bound to the same commands. More code, full control.
- **Shortcuts:** the gestures are hardcoded `Cmd+…`. Windows needs `Ctrl+…`. Either switch to Avalonia's platform "primary modifier" or branch the gesture strings. Audit for any other hardcoded `Cmd`/meta assumptions.

**Pass:** on Windows you can open the book tree, search, dictionary, Go To, and View Source via menu and/or keyboard.

---

## Platform-seam inventory (the concrete touchpoints)

Every place that needs a Windows branch or verification. This is the real map — work from it.

### 1. Project file — `src/CST.Avalonia/CST.Avalonia.csproj`
- `TargetFramework` is `net10.0` (fine).
- **WebView refs are macOS-only and stale** (`:76-79`): pinned `3.120.9`, conditioned on `osx-arm64`/`osx-x64`, and the **dev fallback (`RuntimeIdentifier == ''`) pulls the macOS ARM64 package** — so a plain Windows build resolves the wrong native. Fix:
  - Add a `win-x64` (and optionally `win-arm64`) condition pulling `WebViewControl-Avalonia` (the non-ARM64 package id; confirm the correct Windows package id/variant on NuGet).
  - Bump to the current version (was going to be 3.120.10 — check latest).
  - Fix the empty-RID fallback so Windows dev builds don't grab the mac package.
- Add a `WINDOWS` `DefineConstants` block mirroring the `MACOS` one (`:24-25`), for future `#if WINDOWS`.
- Add `win-x64;win-arm64` to `RuntimeIdentifiers`.
- `Assets\cst.ico` already exists for the Windows app icon.
- **Line endings:** repo enforces LF via `.gitattributes`; set `git config core.autocrlf input` on the Windows dev box so you don't commit CRLF.

### 2. CEF init — `App.axaml.cs:79-121`
- macOS-only keychain switches (`use-mock-keychain`, etc.) under `IsMacOS()` — leave as-is.
- macOS-only `browser-subprocess-path` helper detection (`:88-115`) — harmless on Windows (the `.app/Contents/MacOS/` string check just fails), but confirm Windows doesn't need its own subprocess-path handling (POC-1).
- `WebView.Settings.PersistCache = false` (`:120`) is cross-platform.

### 3. Menus & shortcuts — `App.axaml.cs` (POC-2 above)
- `SetupNativeMenuEvents` (`:953`), `SetupWindowMenuEvents` (`:973`), the programmatic View/Tools menu construction (`~:1084-1140`), and the `_selectBook/_search/_dictionary` menu-item lists (`:56-58`) are all macOS-shaped and macOS-gated. Needs a Windows path.

### 4. Fonts — `Services/FontService.cs` (`#if MACOS` at `:9,21,25,33,135,156`)
- On non-macOS, already falls back to `FontManager.Current.SystemFonts` and returns `null` for system-default. **Good enough for MVP.**
- `MacFontService` is registered only on macOS (`App.axaml.cs:920`); `IFontService → FontService` is always registered (`:928`). A future `WindowsFontService` (DirectWrite) would follow the same DI seam — **that's #29, deferred.**
- Validate on a **base Windows 11** install that all 14 scripts render via defaults (Nirmala UI covers ~8 Indic; Myanmar Text; Leelawadee UI for Thai/Khmer; Microsoft Himalaya for Tibetan; Segoe UI for Latin/Cyrillic). Likely fine; confirm, don't assume.

### 5. Paths & URLs — audit, don't trust
- Fixed this session: `file://` concat in `PdfDisplayViewModel` (#162) and `BookDisplayView` (#222) → both now `new Uri(path).AbsoluteUri`.
- `XmlUpdateService` `targetPath + "/"` (`:193,373`) compares **GitHub API** paths (always `/`) — not a filesystem bug.
- **Action:** grep the app for remaining `"file://"`, string-built paths, and `'/'`/`'\\'` assumptions; anything feeding a WebView `LoadUrl` or a filesystem call on Windows is suspect. Corpus XML read is UTF-16-LE via `Encoding.Unicode` (cross-platform, already hardened).

### 6. Already platform-aware (no work)
- `WelcomeView.axaml.cs:148-156` already branches Windows/OSX/Linux.
- `Environment.GetFolderPath(SpecialFolder.ApplicationData)` → `%APPDATA%\CSTReader` on Windows. Verify the Lucene index, logs (Serilog sink path), and downloaded XML all land there and are writable.

### 7. Packaging — new `package-windows.ps1`
- Mirror `package-macos.sh` (clean RID-arg bash script): `dotnet publish -c Release -r win-x64 --self-contained` → zip a **portable folder/EXE** for the first release. Confirm the CefGlue native subprocess + assets are included in publish output (ties to POC-1). Installer (MSIX/Inno/WiX) and code signing are post-MVP.

---

## Sequenced plan

**Gate 0 — POCs (do first):** POC-1 (CEF render + reparent) and POC-2 (menu/shortcut layer). If POC-1 fails, stop and reassess scope; the weekend goal is off.

**MVP (single-window, if gates pass):**
1. csproj: win RID + WebView win ref + `WINDOWS` constant + fix empty-RID fallback.
2. Build & launch on Windows 11; Welcome page shows.
3. Menu/shortcut layer from POC-2 (at least: open book tree, search, dictionary, Go To).
4. Open a book, read it, script-switch, search + highlight.
5. State persistence to `%APPDATA%\CSTReader` across restart.
6. Font sanity on base Win11 (fallback).
7. Portable `package-windows.ps1`.

**MVP success =** compiles, launches, opens/reads a book, search works, state persists — **single window**. Do **not** gate MVP on flawless float/unfloat/dock-tearing.

**Explicitly deferred:** WindowsFontService/#29 (use fallback) · float/unfloat & multi-window dock robustness (expect follow-up work; track separately) · installer + code signing · Windows ARM64 · Linux.

---

## Research / POC checklist
- [ ] **POC-1:** CEF renders a book on Windows 11 **and** survives float/unfloat/re-dock. Capture repro if it fails.
- [ ] How WebViewControl-Avalonia ships/locates its **browser subprocess** on Windows; confirm it's in `dotnet publish` output.
- [ ] Correct **NuGet package id/version** for the Windows WebView (variant vs the `-ARM64` mac package; latest version).
- [ ] **POC-2:** in-window `NativeMenuBar` (Option A) vs a Windows `<Menu>` (Option B); pick one.
- [ ] `Cmd+…` → `Ctrl+…` gesture strategy (platform primary-modifier vs branched strings); audit for other hardcoded `Cmd`/meta.
- [ ] Base-Windows-11 font validation across all 14 scripts (no manual font install).
- [ ] Path/URL audit for remaining Windows-hostile string building.
- [ ] Verify `%APPDATA%\CSTReader` writability for index/logs/XML; Lucene index builds and reads on Windows.

## Risk table (honest)
| Area | Risk | Why |
|---|---|---|
| WebView reparenting (float/unfloat/dock) | **High** | macOS needed bespoke SIGSEGV workarounds; Windows untested — POC-1 |
| Menu/shortcut surface | **Medium-High** | 100% macOS-native today; real UI work, but well-understood |
| Windows path/URL bugs | **Medium** | 2 found & fixed this session; more likely lurk |
| CEF init / subprocess packaging | **Medium** | Different from macOS helper model; verify in publish output |
| Fonts | **Low** | Fallback works; Win11 defaults likely cover all 14 scripts |
| File paths / state dir | **Low** | `SpecialFolder` maps cleanly; just verify writability |
| Build system / Lucene | **Low** | .NET 10 + Lucene.NET are cross-platform |

## Open questions
1. Installer: portable EXE first (recommended), MSIX/Inno/WiX later?
2. Code signing: skip for MVP; ~$100-400/yr cert for v1.0.
3. Min OS: Windows 11 primary (Win10 EOL Oct 2025); Win10 21H2+ best-effort.
4. Windows ARM64: defer unless requested.

## Related
- `docs/architecture/DOCK_WEBVIEW_WORKAROUNDS.md` — the macOS reparenting workarounds POC-1 stress-tests.
- `docs/architecture/DOCK_SUBSYSTEM.md` — dock lifecycle constraints.
- #29 — WindowsFontService (DirectWrite), deferred.
- `WINDOWS_FONT_SERVICE.md` (this folder) — design notes for that deferred service.
