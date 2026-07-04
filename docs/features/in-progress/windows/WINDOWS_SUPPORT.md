# Windows Support — Plan & Reality Check

**Rewritten:** 2026-07-04 (supersedes the Oct 2025 draft, which was written early in the project and was over-optimistic — see "How this doc changed").
**Target:** Beta 5 goal; a focused-weekend MVP is plausible **if the two POCs below pass**.
**Status:** Not started in code. Two make-or-break POCs must run first.
**Stack:** .NET 10 + Avalonia 11 + WebViewControl-Avalonia (CefGlue/Chromium).

---

## TL;DR

The codebase *is* reasonably well-isolated for cross-platform, but two things the original draft dismissed are the actual work, and both are unproven on Windows:

1. **WebView/CEF lifecycle** — not just "does it render," but does **float/unfloat/dock re-parenting** survive? The whole dock subsystem is built around **macOS-specific SIGSEGV-on-reparent workarounds**. "Same CefGlue package" does not imply "same native window-reparenting behavior." → **POC-1.**
2. **Menus are XAML `NativeMenu`, but their Windows rendering + partial wiring is unverified.** The View/Tools menus live in `App.axaml:93` and `SimpleTabbedWindow.axaml:13-35` as `NativeMenu.Menu` (they **predate #20**, so the volunteer's build had them). On macOS that becomes the system menu bar; off-macOS Avalonia typically needs a `<NativeMenuBar/>` control to render it in-window — and there **isn't one**. Tools items (Go To/Look Up/Search Selection/View Source) are `Click`-wired in XAML; the View toggles (Select a Book/Search/Dictionary) are wired + checkmarked in `App.axaml.cs` `SetupWindowMenuEvents`, which is **`IsMacOS()`-gated**. Gestures are all `cmd+…` (Windows wants `ctrl`). So POC-2 is likely **"add a `<NativeMenuBar/>` + un-gate the View toggles + cmd→ctrl,"** not "build a menu." → **POC-2.**

Everything else (paths, fonts, packaging, project file) is genuinely low-risk plumbing. **WindowsFontService (#29) is NOT needed for MVP** — the existing `FontManager.Current.SystemFonts` fallback is fine; defer it.

**Honest estimate:** if both POCs pass, a single-window MVP (launch → open a book → search → persist state) is a weekend. If POC-1 reveals reparenting SIGSEGVs like macOS had, that alone is a multi-day investigation and the weekend goal slips.

---

## Prior art — issue #20 (existence proof + the exact csproj fix)

[#20](https://github.com/fsnow/cst/issues/20) (2025-09-25, still open, labeled `windows`) documents a VRI volunteer getting the app to **build and launch the GUI on Windows**. It identifies the *same* root cause this doc does — WebView packages macOS-only + the empty-`RuntimeIdentifier` fallback pulling the ARM64 mac package — and gives the fix:

```xml
<!-- ADD, alongside the existing osx conditions (do NOT remove them) -->
<PackageReference Include="WebViewControl-Avalonia" Version="3.120.10" Condition="'$(RuntimeIdentifier)' == 'win-x64'" />
<PackageReference Include="WebViewControl-Avalonia" Version="3.120.10" Condition="'$(RuntimeIdentifier)' == 'win-arm64'" />
```

**This meaningfully de-risks POC-1's core:** CEF rendering on Windows is *reported working*, not hypothetical. Three caveats keep us honest:
1. **The fix was never merged.** The current `.csproj` still has only the macOS conditions (`:76-79`); #20 is documentation, not a committed change.
2. **Git timeline (checked against #20's 2025-09-25 date) — the dock work is the real post-#20 change, not the menus:**
   - **Dock float/unfloat SIGSEGV workarounds are post-#20** (`9193829` 2025-11-13 "Fix CEF crashes with button-based float/unfloat", then `791cf69` 2026-01-28, `65b67cb`/#193 2026-07-03). New macOS-specific reparenting complexity the volunteer never ran — the real post-#20 regression risk (POC-1's reparent half).
   - **The XAML `NativeMenu` predates #20** (`d099a04` 2025-08-01), so the volunteer's build already had it — which reconciles "full functionality": the menu existed then, and on macOS `NativeMenu.Menu` becomes the system menu bar. The surface only *expanded* post-#20 (`21ce7b0`/#79 2026-06-28, `324a583`/#197 2026-07-03). So menus are **not** a clean post-#20 regression; POC-2 is Windows *rendering + partial wiring* of a menu that already exists (add `<NativeMenuBar/>`, un-gate View toggles, cmd→ctrl), not a rebuild.
3. **Don't copy #20's "updated fallback" verbatim.** It swapped the empty-RID fallback from the `-ARM64` package to the plain package. On an Apple-Silicon dev box, `dotnet run` with no explicit RID resolves the `-ARM64` native — a straight swap can break macOS dev builds. **Add** the Windows conditions; leave the macOS-ARM64 fallback intact (or make it platform-aware).

---

## How this doc changed (why the old draft was wrong)

The Oct 2025 draft was written weeks into the project (which began 2025-06-29 as a Claude Code learning exercise) by models that were, frankly, not up to the task — the same provenance as much of the code we've spent recent weeks fixing. Treat its confident ✅/🎉 claims with suspicion. Concrete corrections:

- Said **.NET 9**; we're on **.NET 10**.
- Claimed **"WebView works as-is, no code changes, blocker eliminated."** Half-true: the *managed API* is the same, but the fragile part is native window re-parenting under the dock, which is untested on Windows and was the single hardest thing to get right on macOS.
- Claimed **"all paths are cross-platform ✅."** In this session alone we fixed **two** Windows-breaking `file://` URL bugs (#162 PDF, #222 book HTML). Assume more lurk.
- **Never mentioned the menu/shortcut surface at all.** It's XAML `NativeMenu` (predating #20); on Windows the open question is rendering (`<NativeMenuBar/>`) + un-gating the View toggles + cmd→ctrl — not a rebuild, but the draft ignored it entirely.
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

**Why:** the menus already exist as XAML `NativeMenu.Menu` — the app menu in `App.axaml:93`, and the **View** (Select a Book/Search/Dictionary) + **Tools** (Go To `cmd+g`, Look Up `cmd+d`, Search Selection `cmd+f`, View Source `cmd+e`/`cmd+shift+e`) menus in `SimpleTabbedWindow.axaml:13-35`. Tools items are `Click`-wired in XAML (so they should work once the bar shows); the View toggles' behavior + checkmarks come from `SetupWindowMenuEvents` (`App.axaml.cs:973`), which — with `SetupNativeMenuEvents` (`:953`) — runs **only under `OperatingSystem.IsMacOS()`** (`:73-76, 153-158`). Two Windows problems: (a) there is **no `<NativeMenuBar/>` control**, which Avalonia needs to render a `NativeMenu` in-window off-macOS, so the bar may not appear at all; (b) gestures are `cmd+…`, which Windows won't bind.

**Steps:**
1. Build/launch on Windows (after the csproj fix) and **look: does any menu bar render?** That one observation sets the scope.
2. If not, add a `<NativeMenuBar/>` to the window layout (macOS ignores it; Windows/Linux render it in-window).
3. Un-gate the View-toggle wiring for Windows (extend `SetupWindowMenuEvents`/`SetupNativeMenuEvents` past the `IsMacOS()` check — verify checkmark sync).
4. Convert `cmd+…` gestures to `ctrl` (or Avalonia's platform primary-modifier); audit for other hardcoded `cmd`/meta.

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

### 3. Menus & shortcuts — XAML `NativeMenu` + gated wiring (POC-2)
- Menus are **XAML**, not programmatic: `App.axaml:93` (app menu) and `SimpleTabbedWindow.axaml:13-35` (View + Tools). Tools items are `Click`-wired in XAML → should work on Windows once the bar renders. View items (Select a Book/Search/Dictionary) get behavior + checkmarks from `SetupWindowMenuEvents` (`App.axaml.cs:973`) / `SetupNativeMenuEvents` (`:953`), called **only under `IsMacOS()`** (`:73-76, 153-158`); the `_selectBook/_search/_dictionary` lists (`:56-58`) are that checkmark-sync bookkeeping.
- **Windows work:** add a `<NativeMenuBar/>` so the menu renders in-window (verify first — it may not appear without it); un-gate the View-toggle wiring for Windows; convert `cmd+…` gestures to `ctrl`.

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
- [x] ~~Correct **NuGet package id/version**~~ — per #20: plain `WebViewControl-Avalonia` **3.120.10** for both `win-x64` and `win-arm64`. Confirm 3.120.10 is still current before pinning.
- [ ] **POC-2:** in-window `NativeMenuBar` (Option A) vs a Windows `<Menu>` (Option B); pick one.
- [ ] `Cmd+…` → `Ctrl+…` gesture strategy (platform primary-modifier vs branched strings); audit for other hardcoded `Cmd`/meta.
- [ ] Base-Windows-11 font validation across all 14 scripts (no manual font install).
- [ ] Path/URL audit for remaining Windows-hostile string building.
- [ ] Verify `%APPDATA%\CSTReader` writability for index/logs/XML; Lucene index builds and reads on Windows.

## Risk table (honest)
| Area | Risk | Why |
|---|---|---|
| WebView reparenting (float/unfloat/dock) | **High** | macOS needed bespoke SIGSEGV workarounds; Windows untested — POC-1 |
| Menu/shortcut surface | **Low-Med** | XAML `NativeMenu` predates #20; Windows needs `<NativeMenuBar/>` + un-gate View toggles + cmd→ctrl — verify what renders first |
| Windows path/URL bugs | **Medium** | 2 found & fixed this session; more likely lurk |
| CEF init / render on Windows | **Low-Med** | #20 reports a working Windows GUI; still verify subprocess ships in publish output + works on current HEAD |
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
