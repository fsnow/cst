# CST Reader — UI Smoke Test

A printable, click-through checklist for validating a build on a target machine — especially a
**bare-metal Windows** box, where behavior can differ from the macOS dev machine. Run top to bottom,
marking each step **Pass / Fail / N/A** and jotting anything odd in **Notes**.

**How to use**
- Print it, or open side-by-side with the app.
- Do it on a **fresh install** first (no prior `%APPDATA%\CSTReader` / `~/Library/Application Support/CSTReader`), then optionally a second pass as a returning user.
- If a step fails, note the exact repro and, where relevant, grab the log (`%APPDATA%\CSTReader\logs\` on Windows). For anything display/window/GPU related, set logging to **Debug** first (Settings → Logging, or edit `settings.json`), reproduce, then attach the log.

**Legend** — ⊞ = **Windows-specific risk** (most likely to differ from macOS; test with extra care). ⭐ = make-or-break.

---

## Test environment (fill in)

| | |
|---|---|
| OS + version | |
| Display resolution + **scale %** (⊞ e.g. 1920×1080 @ 150%) | |
| App version / build (Help/About or installer name) | |
| Install method (installer / portable zip / `dotnet run`) | |
| Fresh install or returning user | |
| Date / tester | |

---

## 1. Install & first launch  ⊞

- [ ] Installer runs without admin/UAC (per-user) and completes. *(Or: portable zip unpacks and `CST.Avalonia.exe` runs.)*  ⊞
- [ ] SmartScreen "unknown publisher" is the only warning (expected, unsigned beta); "More info → Run anyway" launches it.  ⊞
- [ ] Start Menu shortcut exists; launches the app.  ⊞
- [ ] **First run downloads the corpus** (progress shown on the Welcome page: "Downloading … (N/217)").
- [ ] Corpus finishes, then **"Building search index"** progresses to 217, then the Welcome banner clears.
- [ ] Dictionary data (DPD) downloads/installs without error.
- [ ] No crash, no hang, CPU settles after startup.

## 2. Main window & window management  ⊞ ⭐

- [ ] **Window opens fully on-screen, centered, a normal size** (not oversized/edge-to-edge, not off-screen).  ⊞ *(regression check for #428)*
- [ ] Title bar shows **minimize / maximize / restore / close**, and the window can be **dragged, resized, maximized, and restored**.  ⊞
- [ ] Maximize then Restore returns to a sensible windowed size.
- [ ] Resize to the minimum — content stays usable (no clipped/overlapping controls).
- [ ] (Multi-monitor, if available) window behaves on the secondary display.

## 3. Select-a-Book tree

- [ ] The **Select a Book** panel shows the Tipiṭaka tree (Vinaya / Sutta / Abhidhamma …).
- [ ] Expand/collapse nodes; the tree scrolls.
- [ ] Double-click (or open) a book → it opens in a book tab.
- [ ] Open several books → multiple tabs; switching tabs shows the right book.

## 4. Book display & script conversion  ⊞ ⭐

- [ ] A book renders as formatted text (headings, paragraphs, page/paragraph markers) — **not** a blank page or a raw-XML / "XSL file not found" error.  ⊞ *(bundled-xsl regression check for #403)*
- [ ] Change **Pali Script** (toolbar dropdown) and confirm the open book re-renders in each — spot-check across families:  ⊞
  - [ ] Latin (Roman)
  - [ ] Devanagari
  - [ ] Bengali / Gujarati / Gurmukhi / Kannada / Malayalam / Telugu (Indic)
  - [ ] Sinhala
  - [ ] Myanmar
  - [ ] Thai
  - [ ] Khmer
  - [ ] Tibetan
  - [ ] Cyrillic
- [ ] Each script shows **real glyphs, no tofu (□) / missing-glyph boxes**, on the base OS with **no manually-installed fonts**.  ⊞
- [ ] Scroll a long book smoothly; the view doesn't go **blank/black** and stay that way (see §10).  ⊞
- [ ] Mul / Aṭṭhakathā / Ṭīkā (Attha/Tika) buttons navigate between the linked layers.
- [ ] Book linking (cross-references) opens the target.

## 5. Search & result highlighting  ⭐

- [ ] Open the **Search** panel; run a simple single-word search (e.g. a common Pāli word) → results appear with occurrence counts.
- [ ] Click a result → the book opens/scrolls to the hit with the term **highlighted**.
- [ ] Next/previous hit navigation moves the highlight through the book.
- [ ] Multi-word / phrase / wildcard search returns sensible results.
- [ ] Search works after switching Pali Script.

## 6. Dictionary & Look Up

- [ ] Open the **Dictionary** panel → it loads (English + Hindi entries available).
- [ ] Type a headword → definitions show.
- [ ] Select a word in a book → **Look Up in Dictionary** (menu or shortcut) brings the Dictionary forward with that word.  ⊞ *(shortcut may not fire on Windows — see §8)*
- [ ] DPD-backed lemma/deconstruction info appears where available.

## 7. Dock: panels, float, split, tabs  ⊞ ⭐

- [ ] Toggle the Select-a-Book / Search / Dictionary panels via the **View** menu — they show/hide.  ⊞
- [ ] Drag a book tab to **split** the dock (side-by-side) → both render.  ⊞
- [ ] **Float** a book into its own window → it renders; **unfloat** / re-dock it → still renders, no crash.  ⊞ ⭐ *(CEF re-parent — the highest macOS-fragility area; watch for blank views or crashes, #39)*
- [ ] Drag tabs to reorder / move between docks.
- [ ] Close a floating window; close a book tab — no crash, remaining books fine.

## 8. Menus & keyboard shortcuts  ⊞

- [ ] The in-window **menu bar** (View / Tools / Window) is visible and its items work **by clicking**.  ⊞ *(POC-2)*
- [ ] Tools ▸ Go To… , Look Up in Dictionary, Search for Selection, View Source — each **click** performs its action.  ⊞
- [ ] **Keyboard shortcuts** (with a book focused): try each and record which fire —  ⊞ *(#111; menu currently shows ⌘ glyphs — try the Ctrl equivalents)*
  - [ ] Ctrl+G → Go To
  - [ ] Ctrl+C → Copy selection
  - [ ] Ctrl+A → Select All
  - [ ] Alt+1 / Alt+2 → View Source (1957 / 2010)
  - [ ] Ctrl+D → Look Up ; Ctrl+F → Search for Selection
  - Notes (which worked / which didn't; was a book focused?): __________________________

## 9. Settings / Preferences  ⊞

- [ ] There is a **way to open Settings/Preferences** from the UI.  ⊞ *(known gap on Windows — no menu path yet; note how you got there, or that you couldn't)*
- [ ] Settings window opens; navigate the categories (Pali Script Fonts / Logging / XML Data Updates / AI / Directories / Configuration).
- [ ] **Configuration → Graphics → "Use hardware acceleration"** checkbox is present (Windows only) and toggling + restart takes effect.  ⊞ *(#401)*
- [ ] Change a Pali-script **font** for a script → the open book reflects it.
- [ ] Change the **Logging** level → new log lines appear at that level.
- [ ] Settings persist across a restart.

## 9a. AI Assistant — key storage  ⊞ ⭐

**Why this section exists.** An API key must never reach a file we write, and on Windows that promise is
kept by DPAPI over a file per secret — a code path **developed on macOS and exercised by tests that do not
run there**. The suite's 17 skips are these. Everything below was learned from real defects found on
2026-08-31 (#926, #925); each check is one of them.

Skip the whole section if the AI Assistant is off (Settings › AI). Turn it on and add one provider with a
key to run it — any provider, the key need not be valid for most of these.

**Where things live on Windows**

```
%APPDATA%\CSTReader\credentials\CST Reader — AI provider\<connection>.<name>.dpapi
```

One file per secret, e.g. `openrouter.primary.dpapi`. The separator is `.` on Windows and `:` on macOS —
`:` is not a legal filename character. On macOS these are Keychain items under the service
**"CST Reader — AI provider"**, visible in Keychain Access.

**The promise**

- [ ] After storing a key, open `%APPDATA%\CSTReader\settings.json` and search for it. **The key must not
      be there** — not the whole key, not a fragment.  ⭐
- [ ] Open the `.dpapi` file in a hex or text viewer. **The key must not be readable in it.**  ⊞ ⭐
- [ ] Restart the app. The provider still shows its key as stored, and a request still works.

**Removing**

- [ ] **Settings › AI › Providers › Edit › Remove stored key** → the sheet stops saying a key is stored, and
      the `.dpapi` file is gone.
- [ ] **Delete a whole connection** (row → Delete) → its `.dpapi` file is gone too. *(A leftover file is an
      orphan nothing can reach, and a connection later created with the same id would silently adopt it.)*
- [ ] Removing the last key leaves **no empty `credentials` sub-directory** behind.

**An unreadable key is not a missing key**  ⊞ ⭐ *(#926 — the defect this section was written for)*

On Windows a blob stops decrypting after an **administrator-initiated password reset**, which discards the
user's DPAPI master key. Simulate it by overwriting the file with junk:

```powershell
$f = "$env:APPDATA\CSTReader\credentials\CST Reader — AI provider\<connection>.primary.dpapi"
Copy-Item $f "$f.bak"                          # keep the real one
[IO.File]::WriteAllBytes($f, (1..64 | % { Get-Random -Max 256 }))
```

Restart the app, then check:

- [ ] The provider row badges **"Key locked"**, not "No key".  ⭐
- [ ] Its message says the key is **stored but could not be decrypted**, and to enter it again. It must
      **not** say no key is stored — that sends the reader to re-enter a key they may still have, and on
      macOS re-entering cannot work at all.
- [ ] The Edit sheet agrees: it must not say *"No key is stored for &lt;provider&gt;."*
- [ ] Sending a message reports the same thing, **not** *"No API key is stored… Add one in Settings."*
      *(The row and the composer disagreeing is the specific failure this was caught as.)*
- [ ] **The junk file is still there.** It is deliberately not deleted: "cannot decrypt now" is not "cannot
      decrypt ever" — a roaming profile can arrive later, and a domain account can recover after an admin
      reset. Deleting would turn a temporary state into permanent loss.
- [ ] **Entering the key again fixes it** on Windows — `Save` writes a fresh file, so replace works here.
      *(It does not on macOS, where the item's ACL is not its value. That platform difference is why the two
      messages differ; if the Windows message ever starts talking about authorizing, that is the bug.)*

Then `Move-Item "$f.bak" $f -Force` to restore, or just re-enter the key.

**Quiet at launch**  *(#925)*

- [ ] Set logging to **Debug**, restart, open Settings › AI › Providers, close it. In the log, count
      `Credential lookup` lines: expect **single digits**, and none at all before the first thing that
      needs a key. *(It was 114 for three connections across one launch and one Settings window. On Windows
      the cost is only wasted decryption; on macOS each one could raise a password dialog.)*
- [ ] No credential prompt of any kind appears at launch.

## 10. Graphics / rendering stability  ⊞ ⭐

- [ ] Over sustained use (open/close books, float/unfloat, switch tabs, scroll), the reader view **does not go black/blank and stay that way**.  ⊞ *(#401 — the virtualized-GPU stall; if it happens: does clicking/selecting text restore it? capture repro + whether hardware-accel is on)*
- [ ] If black-outs occur: turn **hardware acceleration off** (§9), restart, and confirm they stop.  ⊞

## 11. View Source PDF  ⊞

- [ ] Tools ▸ View Source (or Alt+1/2) on a book that has a source PDF → the **Burmese/CST PDF opens** to the right page.  ⊞ *(local `file://` path handling — a Windows path risk area)*
- [ ] PDF scrolls / pages correctly.

## 12. Updates (XML corpus & DPD dictionary)  ⊞

- [ ] With updates enabled, a later launch **checks for and applies** XML/DPD updates without error (status shown, "active after restart").
- [ ] (If testable) a DPD update while the app is running **installs and activates on the next launch** — no crash, no orphaned temp files.  ⊞ *(#394 staged-swap on Windows)*

## 13. State persistence across restart

- [ ] Open several books, set a script, resize/move the window, then **quit and relaunch**.
- [ ] Same books reopen, script is remembered, and the **window returns to its size/position** (not oversized/off-screen).  ⊞
- [ ] Scroll position within a book is roughly restored.

## 14. Shutdown & uninstall  ⊞

- [ ] Closing the main window exits cleanly (no lingering `CST.Avalonia` / `Xilium.CefGlue.BrowserProcess` in Task Manager).  ⊞
- [ ] Uninstall (Add/Remove Programs) removes the app; user data under `%APPDATA%\CSTReader` behavior is as expected.  ⊞

---

## Windows-specific risk map (the seams most likely to differ from macOS)

1. **Window sizing/position on scaled displays** (#428) — §2, §13.
2. **Bundled xsl/dictionaries found beside the exe** (#403) — §4 (blank page / "XSL file not found" if broken).
3. **14-script font rendering on base Windows** (no manual fonts) — §4.
4. **CEF re-parenting** on float/unfloat/split (#39) — §7.
5. **Black-view / GPU stall** under virtualized or quirky GPUs (#401) — §10.
6. **Keyboard shortcuts** (cmd→ctrl; does CEF eat keys?) (#111) — §8.
7. **Settings reachability** on Windows (no menu path yet) — §9.
8. **View-Source PDF `file://` paths** — §11.
9. **DPD staged-swap install** (#394) — §12.
10. **Install/uninstall + process cleanup** — §1, §14.
11. **SmartScreen / unsigned installer** friction — §1.
12. **Multi-monitor / DPI changes** — §2, §13.
13. **DPAPI key storage** (#579, #926) — §9a. The whole Windows credential path is developed on macOS and
    covered by tests that **skip** there, so this seam reaches Windows less exercised than any other in this
    list.

---

## Results summary

| Section | Pass | Fail | Notes |
|---|---|---|---|
| 1 Install & first launch | | | |
| 2 Window management | | | |
| 3 Book tree | | | |
| 4 Book display & scripts | | | |
| 5 Search | | | |
| 6 Dictionary | | | |
| 7 Dock float/split | | | |
| 8 Menus & shortcuts | | | |
| 9 Settings | | | |
| 9a AI key storage (DPAPI) | | | |
| 10 Rendering stability | | | |
| 11 View Source PDF | | | |
| 12 Updates | | | |
| 13 State persistence | | | |
| 14 Shutdown & uninstall | | | |

**Blocker(s):** ______________________________________________

**Overall:** ☐ ship-able beta ☐ needs fixes (list issues to file/reference)
