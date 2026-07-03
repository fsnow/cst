# Multi-Agent Code Review — 2026-07-01

**Method:** Nine parallel subsystem reviewers (Claude Fable 5), each reading its subsystem in full, primed with the project's known-intentional constraints (button-based CEF float/unfloat, two-level script control, permanent source PDFs, `\uXXXX` escapes) so those are not reported as defects. `VriDevToUni.cs` excluded per maintainer instruction.

**Status:** All nine subsystem reviews completed and consolidated below. The orchestrator's independent verification pass was deferred (session limit) — see [Outstanding work](#outstanding-work).

> **Verification caveat:** Findings are as reported by the subsystem reviewers, each grounded in quoted code with file:line. The planned independent verification pass by the orchestrator **did not run**. Before fixing any finding, re-verify it against the current source (every finding cites its location, so this is cheap). Severities are the reviewers'.

**Severity totals (all nine subsystems):** 17 High · 31 Medium · 32 Low

**Suggested fix order:**
1. SEC-1 (rotate/re-scope the SharePoint secret — independent of any code change) and SCRIPT-2 (hard-rule violation, trivial reword/delete)
2. DOCK-1, DOCK-2, BOOK-2 (crash-class, user-reachable in a couple of clicks/keys)
3. SRCH-1..4, STATE-1, STATE-2, NET-1, CORE-1, DICT-1, XCUT-1, SCRIPT-1, BOOK-1 (remaining Highs)
4. Quick user-visible win: SCRIPT-4 (book-tree counts render as `(\52\)`)
5. Mediums, then Lows / cleanups.

---

## Fix status (updated 2026-07-02)

All 80 findings, with severity, and the GitHub issue/PR when one exists. **✅ Fixed** = issue closed / merged to `main`; **🔧 In progress** = issue open (its PR/commits are linked from the issue); a blank status means no dedicated issue has been filed yet. **Verified** = the fix was independently re-reviewed against `main` by the original reviewing model (diff read, all call sites of the changed API traced, relevant tests run) — date noted.

| Finding | Level | Issue | PR | Status | Verified |
|---------|-------|-------|----|--------|----------|
| SEC-1   | High   | — | — | ⚠️ Won't fix. This "secret" needs to be obscured but not protected | |
| DOCK-1  | High   | [#176](https://github.com/fsnow/cst/issues/176) | — | ✅ Fixed (8e4982c) | author self-review (Fable, fix author); GUI verification pending |
| DOCK-2  | High   | [#177](https://github.com/fsnow/cst/issues/177) | — | ✅ Fixed (f8be2ab + 0aa1f93) | author self-review; verification found the rescue leaked the old WebView — fixed in 0aa1f93; GUI check pending |
| DOCK-3  | Medium | [#179](https://github.com/fsnow/cst/issues/179) | — | ✅ Fixed (b89344d) | author self-review (Fable, fix author); GUI verification pending |
| DOCK-4  | Medium | [#180](https://github.com/fsnow/cst/issues/180) | — | ✅ Fixed (0a6e6cf) | author self-review (Fable, fix author); GUI verification pending |
| DOCK-5  | Medium | [#182](https://github.com/fsnow/cst/issues/182) | — | ✅ Fixed (da282fb) | author self-review (Fable, fix author); GUI verification pending |
| DOCK-6  | Low    | [#183](https://github.com/fsnow/cst/issues/183) | — | ✅ Fixed (7341480) | author self-review (Fable, fix author); GUI verification pending |
| DOCK-7  | Low    | [#184](https://github.com/fsnow/cst/issues/184) | — | ✅ Fixed (ab87b13) | author self-review (Fable, fix author); GUI verification pending |
| DOCK-8  | Low    | [#185](https://github.com/fsnow/cst/issues/185) | — | ✅ Fixed (fd272d0) | author self-review (Fable, fix author); GUI verification pending |
| SRCH-1  | High   | [#126](https://github.com/fsnow/cst/issues/126) | [#128](https://github.com/fsnow/cst/pull/128) | ✅ Fixed | ✅ 2026-07-02 |
| SRCH-2  | High   | [#113](https://github.com/fsnow/cst/issues/113) | — | ✅ Fixed | ✅ 2026-07-02 |
| SRCH-3  | High   | [#115](https://github.com/fsnow/cst/issues/115) | — | ✅ Fixed | ✅ 2026-07-02 |
| SRCH-4  | High   | [#114](https://github.com/fsnow/cst/issues/114) | — | ✅ Fixed | ✅ 2026-07-02 (re-verified after 4cc4621 closed the reopen gaps) |
| SRCH-5  | Medium | [#150](https://github.com/fsnow/cst/issues/150) | — | ✅ Fixed | ✅ 2026-07-02 |
| SRCH-6  | Medium | [#157](https://github.com/fsnow/cst/issues/157) | [#158](https://github.com/fsnow/cst/pull/158) | ✅ Fixed | ✅ 2026-07-02 |
| SRCH-7  | Medium | [#145](https://github.com/fsnow/cst/issues/145) | — | ✅ Fixed | ✅ 2026-07-02 (re-verified after 5bad9bd closed the reopen gap) |
| SRCH-8  | Medium | [#146](https://github.com/fsnow/cst/issues/146) | — | ✅ Fixed | ✅ 2026-07-02 |
| SRCH-9  | Medium | — | — | ⚠️ Not a defect — `CST.Core/Any2Ipe.Convert` has real reverse-converter branches for all five scripts; the finding was assessed against the legacy `src/CST/` file (not in the app dependency graph). | ✅ 2026-07-02 assessment confirmed |
| SRCH-10 | Medium | [#147](https://github.com/fsnow/cst/issues/147) | — | ✅ Fixed | ✅ 2026-07-02 (re-verified after 1bd8bbe closed the visual-selection half) |
| SRCH-11 | Low    | [#144](https://github.com/fsnow/cst/issues/144) | [#149](https://github.com/fsnow/cst/pull/149) | ✅ Fixed | ✅ 2026-07-02 |
| SRCH-12 | Low    | [#154](https://github.com/fsnow/cst/issues/154) | [#155](https://github.com/fsnow/cst/pull/155) | ✅ Fixed | ✅ 2026-07-02 (residual note: multi-word per-slot expansion cap still pre-filter — low risk, on the table only) |
| SRCH-13 | Low    | [#148](https://github.com/fsnow/cst/issues/148) | — | ✅ Fixed | ✅ 2026-07-02 |
| SRCH-14 | Low    | [#151](https://github.com/fsnow/cst/issues/151) | [#152](https://github.com/fsnow/cst/pull/152) | ✅ Fixed | ✅ 2026-07-02 |
| STATE-1 | High   | [#130](https://github.com/fsnow/cst/issues/130) | — | ✅ Fixed | ✅ 2026-07-03 |
| STATE-2 | High   | [#132](https://github.com/fsnow/cst/issues/132) | — | ✅ Fixed | ✅ 2026-07-03 (non-blocking residuals noted: lock disposal race at exit, unlocked zero-caller CreateBackupAsync/ClearStateAsync) |
| STATE-3 | Medium | [#199](https://github.com/fsnow/cst/issues/199) | — | ✅ Fixed (17e4457) | ✅ 2026-07-03 (atomic write + corrupt-file defaulting; 3 regression tests) |
| STATE-4 | Medium | [#200](https://github.com/fsnow/cst/issues/200) | — | ✅ Fixed | ✅ 2026-07-03 (one canonical log-level list; 7 tests) |
| STATE-5 | Low    | [#207](https://github.com/fsnow/cst/issues/207) | — | ✅ Fixed | ✅ 2026-07-03 (Minimized→Normal on restore) |
| STATE-6 | Low    | — | — | | |
| STATE-7 | Low    | — | — | | |
| NET-1   | High   | [#129](https://github.com/fsnow/cst/issues/129) | [#131](https://github.com/fsnow/cst/pull/131) | ✅ Fixed | ✅ 2026-07-03 (caveat: pre-fix truncated PDFs never re-validated → follow-up #194) |
| NET-2   | Medium | [#137](https://github.com/fsnow/cst/issues/137) | [#139](https://github.com/fsnow/cst/pull/139) | ✅ Fixed (+7a3c572) | ✅ 2026-07-03 (incomplete on first pass — Unknown still rendered the green badge; renderer fixed + 2 tests in 7a3c572) |
| NET-3   | Medium | [#140](https://github.com/fsnow/cst/issues/140) | [#141](https://github.com/fsnow/cst/pull/141) | ✅ Fixed | ✅ 2026-07-03 (numeric-only pre-release tags deliberately unsupported — documented in f151202) |
| NET-4   | Low    | [#165](https://github.com/fsnow/cst/issues/165) | [#166](https://github.com/fsnow/cst/pull/166) | ✅ Fixed | |
| NET-5   | Low    | [#162](https://github.com/fsnow/cst/issues/162) | [#163](https://github.com/fsnow/cst/pull/163) | ✅ Fixed | |
| NET-6   | Low    | — | — | | |
| NET-7   | Low    | [#160](https://github.com/fsnow/cst/issues/160) | [#161](https://github.com/fsnow/cst/pull/161) | ✅ Fixed | |
| NET-8   | Low    | [#171](https://github.com/fsnow/cst/issues/171) | [#172](https://github.com/fsnow/cst/pull/172) | ✅ Fixed | |
| DICT-1  | High   | [#117](https://github.com/fsnow/cst/issues/117) | [#119](https://github.com/fsnow/cst/pull/119) | ✅ Fixed | ✅ 2026-07-03 |
| DICT-2  | Medium | [#124](https://github.com/fsnow/cst/issues/124) | — | ✅ Fixed (DICT-2/3/5/6 bundle) | ✅ 2026-07-03 |
| DICT-3  | Medium | [#124](https://github.com/fsnow/cst/issues/124) | — | ✅ Fixed | ✅ 2026-07-03 |
| DICT-4  | Medium | [#120](https://github.com/fsnow/cst/issues/120) | [#122](https://github.com/fsnow/cst/pull/122) | ✅ Fixed | ✅ 2026-07-03 |
| DICT-5  | Medium | [#124](https://github.com/fsnow/cst/issues/124) | — | ✅ Fixed | ✅ 2026-07-03 |
| DICT-6  | Low    | [#124](https://github.com/fsnow/cst/issues/124) | — | ✅ Fixed | |
| CORE-1  | High   | [#127](https://github.com/fsnow/cst/issues/127) | — | ✅ Fixed | ✅ 2026-07-03 |
| CORE-2  | Medium | — | — | | |
| CORE-3  | Medium | [#133](https://github.com/fsnow/cst/issues/133) | [#134](https://github.com/fsnow/cst/pull/134) | ✅ Fixed | ✅ 2026-07-03 |
| CORE-4  | Medium | [#123](https://github.com/fsnow/cst/issues/123) | [#125](https://github.com/fsnow/cst/pull/125) | ✅ Fixed (+213a65c) | ✅ 2026-07-03 (3 sites first pass; 4th site SettingsViewModel.cs:593 completed in 213a65c) |
| CORE-5  | Low    | — | — | | |
| CORE-6  | Low    | — | — | | |
| CORE-7  | Low    | — | — | | |
| CORE-8  | Low    | — | — | | |
| BOOK-1  | High   | [#136](https://github.com/fsnow/cst/issues/136) | — | ✅ Fixed (GUI-verified: renderer count returns to baseline across open/close) | ✅ 2026-07-03 (static + GUI; composition gaps found on rescue/float paths, fixed via #177 reopen + #193) |
| BOOK-2  | High   | [#118](https://github.com/fsnow/cst/issues/118) | — | ✅ Fixed | ✅ 2026-07-03 (copy/select-all branches confirmed addressed too) |
| BOOK-3  | Medium | [#153](https://github.com/fsnow/cst/issues/153) | — | ✅ Fixed | ✅ 2026-07-03 (all 5 construction sites seed the script) |
| BOOK-4  | Medium | [#156](https://github.com/fsnow/cst/issues/156) | — | 🔧 Reopened | ⚠️ Incomplete 2026-07-03: SEQ not applied to CST_LOOKUP_SEL / CST_GET_PARA_RESULT title writes — identical-consecutive still drops (2nd Cmd+D on same selection no-ops). Fragile CEF bridge; deferred to a GUI session. Details on #156. |
| BOOK-5  | Medium | [#159](https://github.com/fsnow/cst/issues/159) | — | ✅ Fixed | ✅ 2026-07-03 |
| BOOK-6  | Medium | [#164](https://github.com/fsnow/cst/issues/164) | — | ✅ Fixed | ✅ 2026-07-03 (corpus-confirmed on s0503m) |
| BOOK-7  | Medium | [#167](https://github.com/fsnow/cst/issues/167) | — | ⏸️ Deferred — needs GUI verification (restoration timing; touches #36) | |
| BOOK-8  | Low    | [#168](https://github.com/fsnow/cst/issues/168) | — | ✅ Fixed | |
| BOOK-9  | Low    | [#169](https://github.com/fsnow/cst/issues/169) | — | ✅ Fixed | |
| BOOK-10 | Low    | [#170](https://github.com/fsnow/cst/issues/170) | — | ✅ Fixed | |
| BOOK-11 | Low    | [#173](https://github.com/fsnow/cst/issues/173) | — | ✅ Fixed | |
| XCUT-1  | High   | [#138](https://github.com/fsnow/cst/issues/138) | — | ✅ Fixed | ✅ 2026-07-03 (hardening candidate: no double-run guard at handler entry) |
| XCUT-2  | Medium | — | — | | |
| XCUT-3  | Medium | — | — | | |
| XCUT-4  | Medium | — | — | | |
| XCUT-5  | Low    | — | — | | |
| XCUT-6  | Low    | — | — | | |
| XCUT-7  | Low    | — | — | | |
| SCRIPT-1  | High   | [#116](https://github.com/fsnow/cst/issues/116) | — | ✅ Fixed | ✅ 2026-07-03 |
| SCRIPT-2  | High   | [#143](https://github.com/fsnow/cst/issues/143) | — | ✅ Fixed | ✅ 2026-07-03 (incomplete on first pass — docs still had the term; reworded in 4037efc; review-doc quote + VRI dictionary data exempt, see #143) |
| SCRIPT-3  | Medium | [#201](https://github.com/fsnow/cst/issues/201) | — | ✅ Fixed | ✅ 2026-07-03 (ASCII-only digit validation; regression tests) |
| SCRIPT-4  | Medium | [#203](https://github.com/fsnow/cst/issues/203) | — | ✅ Fixed | 🔧 committed; GUI-confirm counts show (52) |
| SCRIPT-5  | Medium | — | — | | |
| SCRIPT-6  | Low    | — | — | ✅ Fixed | ✅ 2026-07-03 (already fixed by STATE-2 5906973; ScriptService uses MarkDirty) |
| SCRIPT-7  | Low    | [#202](https://github.com/fsnow/cst/issues/202) | — | ✅ Fixed | ✅ 2026-07-03 (CFArray released in finally; 6/6 tests) |
| SCRIPT-8  | Low    | — | — | | |
| SCRIPT-9  | Low    | [#204](https://github.com/fsnow/cst/issues/204) | — | ✅ Fixed | ✅ 2026-07-03 (dead _nodeCache + log spam removed) |
| SCRIPT-10 | Low    | [#205](https://github.com/fsnow/cst/issues/205) | — | ✅ Fixed | ✅ 2026-07-03 (RefreshTreeAsync guarded) |

**Progress:** 52 fixed, 0 reopened, 1 deferred (BOOK-7, needs GUI verification), 27 not yet filed (SRCH-9 among them, assessed not-a-defect). All 14 SRCH findings resolved (SRCH-9 = not-a-defect); all 8 DOCK findings fixed 2026-07-03 (#176-#185, committed per issue).
**Verification progress (paused 2026-07-03):** 34 verified + SRCH-9 assessment confirmed. Remaining to verify next session: **NET-4, NET-5, NET-7, NET-8, DICT-6, BOOK-8, BOOK-9, BOOK-10, BOOK-11** (the nine Lows). Also outstanding: BOOK-7 (deferred, scroll-restoration GUI work); DOCK-1..8 (author-self-reviewed, need the GUI checklist below); BOOK-4 reopened (#156, CEF-bridge SEQ, needs a GUI session).

Verification-found work fixed this cycle: SCRIPT-2 docs (4037efc), DOCK-2 rescue WebView leak (0aa1f93), float/unfloat WebView leak #193 (65b67cb), BOOK-1 cache-miss hardening (2adea4c), NET-2 badge (7a3c572), CORE-4 4th site (213a65c). Open follow-ups: **#194** (pre-fix truncated PDFs — design decision), **#195** (float/unfloat in-flight guard + rescue-failure eviction — pre-existing), **#156** (BOOK-4 reopened). The two WebView-leak fixes passed an independent adversarial review (both SOUND).

**High findings still without an issue:** SEC-1 (won't fix).

### GUI test checklist (DOCK + WebView fixes — author-self-reviewed, need a human pass)

Renderer-process count between steps (macOS). The process name differs by how you launch:
- **`dotnet run` (dev):** `ps ax -o args | grep "Xilium.CefGlue.BrowserProcess" | grep -- "--type=renderer" | grep -v grep | wc -l`
- **packaged `.app`:** `ps ax | grep "CST Reader Helper (Renderer)" | grep -v grep | wc -l`

Observed dev baseline (Welcome page + 1 restored book tab) = **3 renderers** (one per live WebView). Track the *delta* across a float/unfloat or close cycle, not the absolute number — a leak shows as the count climbing and never returning after the browser should be gone.

1. **DOCK-1 — search tab can't drag-float.** Search → double-click an occurrence (opens a `🔍` tab) → drag that tab out of the dock. Expected: it refuses to float (no separate window, no crash). Before the fix this SIGSEGV'd.
2. **#193 — float/unfloat doesn't leak browsers.** Open a book; note renderer count. Float it (button) → unfloat it (button); repeat ~5×. Expected: renderer count stays flat (roughly baseline), not +1 per cycle. Log should show `Disposing WebView…` + `Evicted recycled View…` each cycle.
3. **DOCK-2 / #177 — closing a floating window rescues its books.** Float one or more books into a window, then close that window with its red button. Expected: each book reappears as a tab in the main window (near its prior scroll position, same title/script/highlights), does NOT vanish, and does NOT ghost-reappear on next launch. Renderer count returns to the pre-float value.
4. **DOCK-4 — search-opened book is fully wired.** From a `🔍` search tab: click View Source (PDF) → the PDF opens (was silently dead before). Change that tab's script → the tab title updates (keeps the `🔍` prefix), doesn't go stale.
5. **DOCK-5 — hiding a panel collapses cleanly.** Hide the Search (or Select Book) panel via the View menu. Expected: the left strip collapses (no dead ~25%-wide gap); no unrelated floating window closes; re-showing the panel restores it.
6. **DOCK-6 — window move persists.** Move the main window (don't resize) → quit (Cmd+Q) and relaunch. Expected: it reopens at the moved position. Repeat resizing then quickly quitting: final size restores (no ~500ms staleness).
7. **BOOK-1 (regression re-confirm) — closing a tab frees its browser.** Open several books, close them one by one. Expected: renderer count drops by one per close, back to baseline.

If all pass, mark DOCK-1..8 + #177/#193 verified in the table and note "GUI-verified <date>".

Once the remaining findings are either fixed or filed as issues, this document should be archived/deleted per the hand-off note at the bottom.

---

## Security

### SEC-1 [High] Tenant-wide Graph client secret recoverable from public repo
**File:** `src/CST.Avalonia/Services/SharePointService.cs:33-42`; `src/CST.Avalonia/Services/SecretObfuscator.cs:14`
The XOR-obfuscated `TenantId`/`ClientId`/`ClientSecret` blobs and the key parts (`"CST"+"Reader"+"2025"+"Pali"`) are both in the public repo; the app registration has app-only `Files.Read.All`/`Sites.Read.All` per the file's own comment — i.e., read access to the entire tenant, not just the `_Source` PDFs. Obfuscation is fine against `strings` on a binary but does nothing once the source is public.
**Fix:** Treat the current secret as public: rotate it, and re-scope the Azure app to `Sites.Selected` (or a dedicated site/account holding only the PDFs), or front downloads with a proxy/pre-signed URLs. This is an ops action first, code second.

---

## Dock subsystem (`CstDockFactory.cs`, `LayoutViewModel.cs`, `SimpleTabbedWindow.cs`)

### DOCK-1 [High] Search-opened books are drag-floatable → CEF live-reparent SIGSEGV
**File:** `CstDockFactory.cs:398-419` (vs. the correct path at 530-537)
`OpenBookInNewTab` (used by search results, `SearchPanel.axaml.cs:116`) never sets `CanFloat = false`; the `BookDisplayViewModel` ctor defaults `CanFloat = true` (`BookDisplayViewModel.cs:141-142`). Dragging a search-opened book tab out of the dock floats a live CEF WebView across windows — the exact documented SIGSEGV the button-based float workaround exists to prevent.
**Repro:** search → double-click occurrence → drag the 🔍 tab out.
**Fix:** Set `CanDrag = true; CanFloat = false;` in `OpenBookInNewTab`; better, extract one shared book-document setup helper for both open paths (see DOCK-4).

### DOCK-2 [High] Closing a floating window with books silently drops them (rescue path doubly dead)
**File:** `CstDockFactory.cs:2815-2843` (+ `CstHostWindow.cs:68-75`)
`CloseHostWindow`'s "move documents back" branch never executes: (a) `hostWindow.Layout is DocumentDock` is never true (layout is always a `RootDock`), and (b) `OfType<Document>()` can't match books because `BookDisplayViewModel` derives from `ReactiveDocument : ReactiveDockableBase, IDocument`, not `Document` (elsewhere already fixed — `CheckForEmptyFloatingWindows:2038` deliberately uses `OfType<ReactiveDocument>()`). Result: red-button close of a floating window with a book → tab vanishes, VM never disposed (FontService subscription roots it — workarounds doc item J), and the stale persisted state resurrects the book next launch.
**Fix:** Use the existing `FindDocumentDockInLayout(hostWindow.Layout)` + `OfType<ReactiveDocument>()`, and move books back via the dispose-and-recreate pattern (`UnfloatDockableWithoutRecycling`-style), not a live collection move.

### DOCK-3 [Medium] In-flight `SaveAllBookWindowStatesAsync` resurrects a just-closed book's state
**File:** `CstDockFactory.cs:643-657`
The save loop snapshots `VisibleDockables.ToList()` then awaits a JS round-trip per book. Closing a tab mid-loop removes its state, then the loop's `SaveBookWindowState` re-adds it (`ApplicationStateService.UpdateBookWindowState` add-if-missing). Ghost tab on next launch.
**Fix:** Inside the loop, skip books no longer in `VisibleDockables` (or disposed); or make `UpdateBookWindowState` update-only.

### DOCK-4 [Medium] `OpenBookInNewTab` vs `OpenBook` drift: View Source dead + stale titles on search-opened books
**File:** `CstDockFactory.cs:423-449` vs 539-599
Search path never subscribes `OpenPdfRequested` (View Source silently no-ops), never adds the `DisplayTitle`→`Title` sync (script change leaves `🔍 <old title>` forever), never adds the `BookScript` state handler — and pre-adds the id to `_goToSubscribedBooks`, so the repair mechanism `EnsureBookEventSubscription` (2936-2978) permanently skips these books.
**Fix:** Extract one `WireBookDocument(vm)` helper used by both paths.

### DOCK-5 [Medium] `RemoveToolFromLayout` can close the wrong floating window; its main-window branch is dead
**File:** `LayoutViewModel.cs:309-341`
The empty-window loop closes the first window whose layout is empty without checking it contains `parentDock`, then `return`s (skipping remaining cleanup). The `MainDock` branch can never fire (`MainDock` isn't a direct child of `Root`; and `LeftToolDock`'s parent is `LeftTools`). Also bypasses `CleanupEmptySplits`, leaving a dead ~25%-wide strip when a left tool dock empties.
**Fix:** Verify containment before closing; delete the dead branch and call the factory's cleanup (`CleanupEmptySplits`/`RemoveDockable`).

### DOCK-6 [Low] Main-window position not saved on move; trailing resize dropped; shutdown never recaptures geometry
**File:** `SimpleTabbedWindow.cs:265-277, 362-372`; `App.axaml.cs:845-888` *(found independently by two reviewers)*
Saves trigger only on `Width`/`Height`/`WindowState` changes — `PositionChanged` is never subscribed (contrast `CstHostWindow.cs:59`); the 500 ms leading-edge throttle discards the final resize value; the shutdown path saves book states but never main-window geometry. Move-then-quit restores the old position.
**Fix:** Subscribe `PositionChanged`; call `SaveWindowState()` unconditionally (bypassing the throttle) from the shutdown save.

### DOCK-7 [Low] `FloatDockable` failure-recovery re-adds Tools into the DocumentDock
**File:** `CstDockFactory.cs:1290-1302`
The catch-block restore calls `AddDocumentToLayout(dockable)` even for tools, creating the exact Tool-in-DocumentDock state the collection-changed guard (148-170) then fights, looping 50 ms retries; the tool can end up nowhere.
**Fix:** Branch on type: tools → `EnsureLeftToolDock()` + `AddDockable`; documents → `AddDocumentToLayout`.

### DOCK-8 [Low] Dead code: `CreateWindowFrom(IDockWindow?)` (never called; fallback would leak a zombie host window) and unbound, unsafe `ResetLayout`
**File:** `CstDockFactory.cs:1843-1890`; `LayoutViewModel.cs:109-116`
Also: `CstHostWindow.cs:21/24` shadow `Title`/`Topmost` with plain properties — assignments never reach the native window (only `SetTitle` does). `ResetLayout`, if ever invoked, drops all book VMs without `Dispose()` and orphans floating windows.
**Fix:** Delete `CreateWindowFrom`; delete or harden `ResetLayout`; remove the shadowing properties.

---

## Search & indexing (`SearchService`, `IndexingService`, `SearchViewModel`, `CST.Lucene`)

### SRCH-1 [High] File-dates cache mutated at detection time → failed indexing marks books up-to-date forever
**File:** `XmlFileDatesService.cs:150`
`GetChangedBooksAsync` writes new timestamps into `_fileDates` while *detecting* changes; `SaveFileDatesAsync` persists the whole cache. If indexing then fails partway, any later successful save persists timestamps for books that were never indexed → silently stale search results until the file changes again.
**Fix:** Apply timestamps per book only after that book indexes successfully (return `(index, timestamp)` pairs; use the existing `UpdateFileDate`).

### SRCH-2 [High] Index reader disposed while concurrent searches still use it
*(independently reported by two reviewers)*
**File:** `SearchService.cs:69-71, 596, 669`
`GetIndexReader()` disposes the superseded reader immediately, no refcounting — a search holding the old reader gets `AlreadyClosedException` mid-enumeration when another call triggers a refresh after re-indexing. Compounding: `ExpandWildcard`/`ExpandRegex` read the mutable `_indexReader` field instead of the reader their search started with, so one search can mix two index generations.
**Fix:** Lucene refcounting (`IncRef`/`DecRef` in finally, or `SearcherManager`); pass the search's own reader into the expand methods.

### SRCH-3 [High] Pasted ZWJ/ZWNJ in roman-script queries → silent zero results
**File:** `Any2Ipe.cs:62` + `SearchService.cs:108-112`
Joiners are classified `Script.Unknown` and appended to the current run; `Latn2Ipe` passes them through verbatim, so the IPE term contains U+200C/U+200D and matches nothing (index-time `Deva2Ipe` strips them, so index terms are clean). Devanagari input is safe; Latin input is not. This is the app's known pasted-joiner bug class.
**Fix:** Strip `‌`/`‍` in `SearchAsync` (and `SearchViewModel.ParsedUnits`) before conversion. See also DICT-4 (same class in dictionary lookups) and CORE-3 (Cyrillic misrouting).

### SRCH-4 [High] `IndexWriter` never closed on indexing failure → `write.lock` held, all in-session retries fail
**File:** `CST.Lucene/BookIndexer.cs:37-106, 167-171`
`IndexAll` has no try/finally; an exception (or the early `return` at 71-72) leaves the writer open. The retry path constructs a new `BookIndexerAsync` (`IndexingService.cs:176`) → `LockObtainFailedException` until app restart. Also leaked: `FSDirectory` instances at `BookIndexer.cs:163` and `110`; `OpenIndexWriter(bool create)` ignores its parameter.
**Fix:** try/finally disposing writer + directories; fix or remove the ignored parameter.

### SRCH-5 [Medium] Search-result cache ignores display script → wrong-script matching-terms after a script change
**File:** `SearchService.cs:909-928, 96-100`
`GenerateCacheKey` omits the script; cached `SearchResult.DisplayTerm`s are baked in the script current at cache time.
**Fix:** Add `CurrentScript` to the cache key, or cache IPE and convert on the way out.

### SRCH-6 [Medium] IndexingService's highlighting reader never refreshed after re-indexing
**File:** `IndexingService.cs:233` (consumed by `BookDisplayViewModel.cs:980-991`)
Reopens only when null/refcount-dead — no `IsCurrent()` check. After a mid-session re-index, new-generation docIds are used against the stale reader → wrong term vectors (wrong highlight offsets) or out-of-range failures.
**Fix:** Mirror SearchService's `IsCurrent()` refresh (with SRCH-2's refcounting), or invalidate after `PerformIndexingAsync`.

### SRCH-7 [Medium] Two debounced pipelines run `ExecuteSearchAsync` concurrently; UI-update block never re-checks cancellation
**File:** `SearchViewModel.cs:141-150, 845-853, 613-614, 685-727`
Typing pipeline + filter/mode pipeline aren't serialized; a cancelled search whose `SearchAsync` already returned still runs its un-gated UI update → doubled/mixed term lists (the #87 symptom, fixed only for the restore path), clobbered `IsSearching`.
**Fix:** Capture the CTS locally and bail in the update block if cancelled; ideally merge triggers into one Rx stream with `.Switch()`.

### SRCH-8 [Medium] Live-search pipeline mutates UI-bound scalars on thread-pool threads
**File:** `SearchViewModel.cs:141-150, 616-617, 638-644`
`Throttle` with no scheduler → `ExecuteSearchAsync` starts off-thread and sets `IsSearching`/`StatusText`/`ValidationMessage` (all bound) before any dispatcher hop.
**Fix:** `.ObserveOn(RxApp.MainThreadScheduler)` after `Throttle` in both pipelines.

### SRCH-9 [Medium] Telugu/Thai/Tibetan/Khmer/Cyrillic search input silently returns zero results
**File:** `Any2Ipe.cs:33-56, 57-95`
`GetScript` classifies Telugu/Thai/Tibetan/Khmer but `Convert` has no branch for them (`return str` raw); Cyrillic isn't classified at all (falls into Latin — see CORE-3). No reverse (X→Deva/IPE) converters exist for these scripts. A user reading in Telugu who copies a word into search gets 0 results, no explanation.
**Fix:** Short-term, detect unconvertible script and surface a `ValidationMessage`; long-term, add reverse converters.

### SRCH-10 [Medium] VM's `WhenActivated` wiring is dead; view compensates via reflection; single-term auto-select doesn't populate Books pane
**File:** `SearchViewModel.cs:157-176, 706-710` + `SearchPanel.axaml.cs:236-247`
`SelectedTerms.CollectionChanged → UpdateOccurrences()` and the statistics subscription live inside a `WhenActivated` block the ctor comments admit never runs; the view invokes the private methods via reflection to compensate; single-result auto-select shows no selection and an empty Books list until manually clicked.
**Fix:** Move the subscriptions to ctor level (like the other #52/#57 rescues), make the methods internal, delete the reflection.

### SRCH-11 [Low] Corrupt-but-present index reported "valid" → search fails forever with no recovery
**File:** `IndexingService.cs:68-98`
Validity = "any `*.cfs` files exist". A torn index passes, indexing is skipped, every search throws. `IndexCorruptionTests` documents the mismatch (test named `...ReturnsFalse` asserts `True`).
**Fix:** `DirectoryReader.IndexExists` + cheap open attempt; on corruption, delete index **and reset the file-dates cache**, then full rebuild.

### SRCH-12 [Low] 500-term truncation applied before the book filter
**File:** `SearchService.cs:178, 208-219`
Broad wildcard + narrow book filter: the term budget is consumed by terms in unselected books; legitimate terms past the 500th are dropped while the displayed list looks short.
**Fix:** Count only terms that survive the filter toward the page limit.

### SRCH-13 [Low] Enter/Escape wired twice on the search box (KeyBinding + code-behind)
**File:** `SearchPanel.axaml:94-97` + `SearchPanel.axaml.cs:132-144`
Double execution risk; the bare `Execute().Subscribe()` has no error handler (RxApp default → crash).
**Fix:** Delete the code-behind handler.

### SRCH-14 [Low] `DevaXmlAnalyzer` violates Lucene's analyzer reuse contract (shared mutable tokenizer field)
**File:** `CST.Lucene/DevaXmlAnalyzer.cs:11, 26-34`
Safe only because indexing is single-threaded with one doc in flight; any concurrency change silently interleaves book texts.
**Fix:** Move per-document buffering into `DevaXmlTokenizer.Reset()`; drop the analyzer field. Refactor-when-touched.

---

## Startup, DI, state & settings

### STATE-1 [High] Saved log-level setting never applied at startup
**File:** `App.axaml.cs:915-916`
The probe constructs `SettingsService` and reads `.Settings` without `LoadSettingsAsync()` — the ctor never touches disk, so the branch always yields the default "Information". Persisted Debug/other choices silently never take effect (only `CST_LOG_LEVEL` env var works across launches).
**Fix:** Peek the file synchronously (small static reader), or `LoadSettingsAsync().GetAwaiter().GetResult()`, or a Serilog `LoggingLevelSwitch` set after settings load.

### STATE-2 [High] `ApplicationStateService.SaveStateAsync` unsynchronized: shared `.tmp` path + off-thread serialization of UI-mutated state
**File:** `ApplicationStateService.cs:186-238` (writers: timer at 105, `ForceSaveAsync` at 488, fire-and-forget in `ScriptService.cs:55` and `DictionaryViewModel.cs:113`)
Concurrent saves share one fixed temp path (a half-written tmp can be promoted over the good state file via `File.Replace`, defeating the atomic design), and the timer thread serializes `Current` while the UI thread mutates `Current.BookWindows` → `InvalidOperationException`, save silently skipped. `CreateBackupAsync` has the same issue plus second-resolution filename collisions.
**Fix:** `SemaphoreSlim(1,1)` around backup+serialize+replace; snapshot/serialize under the same discipline as mutations; consider `MarkDirty()` instead of forced saves from ScriptService/DictionaryViewModel.

### STATE-3 [Medium] `settings.json` written non-atomically; corrupt file loses first-run defaulting
*(independently reported by two reviewers; the sweep extends it — see XCUT-4)*
**File:** `SettingsService.cs:112-113` (load catch at 95-98 vs. no-file branch 76-92)
Direct overwrite → torn file on crash; on next load the catch only logs — the default `XmlBooksDirectory` branch doesn't run, so the app runs with `""` instead of the standard path, changing update/indexing behavior.
**Fix:** Reuse the temp+`File.Replace` pattern; on parse failure, fall through to first-run defaulting (or rename the corrupt file aside). Same pattern needed for the file-dates cache and chapter lists (XCUT-4).

### STATE-4 [Medium] "Fatal" log level offered by UI but rejected by validator → silently reverts every restart
**File:** `SettingsViewModel.cs:809` vs `SettingsValidator.cs:17-20`
UI list is Serilog names (`Fatal`); sanitizer whitelist is MEL names (`Critical`, no `Fatal`); validator-accepted `Trace`/`Critical`/`None` fall through to `Information` in both parse switches (`App.axaml.cs:917-925`, `SettingsViewModel.cs:874-882`). The sanitizer actively rewrites the user's file.
**Fix:** One canonical list (Serilog's) shared by both.

### STATE-5 [Low] Restoring `WindowState.Minimized` launches the app minimized
**File:** `SimpleTabbedWindow.cs:348` — coerce Minimized → Normal on restore (keep Maximized).

### STATE-6 [Low] Dead startup/persistence code (bundled)
- `SplashScreen.axaml.cs` (~700 lines) never instantiated; `ShouldShowSplashScreen()` parses a settings key that doesn't exist.
- `LoggingService.cs` zero references; would throw in ctor from a packaged app (`Directory.CreateDirectory("/logs")`), writes into the repo tree, duplicates Serilog.
- `ApplicationStateService.AddRecentBook` (line 347) no callers; fires `StateChanged` without `MarkDirty()` — persistence would be accidental if ever wired.
- `App.axaml.cs:263-269` `CloseRequested` handler: `_ = SaveApplicationStateAsync(); desktop.Shutdown();` — fire-and-forget save racing a forced shutdown; currently unreachable, data-loss landmine if wired. Make it awaited if kept.
**Fix:** Delete (or file issues), per item.

### STATE-7 [Low] Backup rotation covers only ~10 minutes; every save costs two full writes
**File:** `ApplicationStateService.cs:412-431`
Backup-per-save × keep-10 means all backups are from the current session; recovery can't reach past a logically-corrupt-but-parseable state.
**Fix:** Back up once per session on load, or tiered retention.

---

## Network & update services

### NET-1 [High] Partial/failed SharePoint PDF download permanently poisons the preserved PDF store
**File:** `SharePointService.cs:243-257` (cached check at 195-199)
Streams directly into the final path (no temp file, no atomic rename, no size/hash verification — `driveItem.Size` is fetched and unused); the exception path leaves the truncated PDF, and `if (File.Exists(localPath)) return localPath;` treats it as valid forever — including through the user's Refresh command. The XML path got exactly this treatment in #65; the PDF path never did. Since PDFs are the permanent preservation mechanism, corruption persists.
**Fix:** Download to `.part`, verify length (Graph also exposes `File.Hashes`), `File.Move` into place, delete partial on failure — mirror `XmlUpdateService`.

### NET-2 [Medium] Missing/unparseable "latest version" → false green "You're running the latest version"
**File:** `VersionComparer.cs:127-128`; `WelcomeUpdateService.cs:257-260`
`Compare` returns `Current` when either side is null/unparseable; a beta user checking after the beta channel field is removed post-GA gets the green badge though a stable upgrade exists.
**Fix:** Null beta channel → fall back to comparing against Stable; unparseable → a distinct "Unknown" result mapped to no banner.

### NET-3 [Medium] `VersionComparer.Compare` contradicts `SemanticVersion.CompareTo` on pre-release edges
**File:** `VersionComparer.cs:161-182` (regex at 81)
`5.0.0-alpha` vs `5.0.0-alpha.1`: `CompareTo` says older, classification falls through to `Current` → no update offered. Regex silently drops numeric-only pre-release (`5.0.0-1` parses as stable; `-beta1` without dot loses the `1`).
**Fix:** After `CompareTo < 0` with equal major.minor.patch, return `PatchOutdated` in the fallthrough; extend the regex if such tags are plausible.

### NET-4 [Low] XML update apply: temp dir on another volume (non-atomic move) + races with already-open book tabs
**File:** `XmlUpdateService.cs:355, 717-722` (same at 396-403)
`Path.GetTempPath()` staging → on Linux tmpfs the move degrades to copy+delete (truncated XML on crash); on Windows, `File.Move` over a file an open tab holds throws, aborting mid-loop (mixed-commit corpus until next launch's self-heal).
**Fix:** Stage in `.staging-<guid>` next to the destination (atomic same-volume rename); retry/defer moves while restore-reads are in flight.

### NET-5 [Low] PDF `file://` URL built by string concat — malformed on Windows, unescaped
**File:** `PdfDisplayViewModel.cs:208`
`$"file://{localPath}#page={pdfPage}"` → invalid on Windows (`file://C:\...`), spaces unescaped, any future `#` in a path truncates.
**Fix:** `new Uri(localPath).AbsoluteUri + $"#page={pdfPage}"`.

### NET-6 [Low] ~110 lines of dead first-run prompt code with a broken dialog-result pattern
**File:** `XmlUpdateService.cs:132-243` — no callers; contains an async-void click handler that can complete after `dialog.Close()` and a cross-thread result capture. Delete.

### NET-7 [Low] `CalculateGitBlobSha` duplicates the tested `GitBlobHash`
**File:** `XmlUpdateService.cs:770-782` — both correct today; two copies invite drift. Replace with `GitBlobHash.Compute`.

### NET-8 [Low] `WelcomeUpdateService` news up an undisposed `HttpClient` per instance, constructed outside DI
**File:** `WelcomeUpdateService.cs:35-37`; `CstDockFactory.cs:88` (`new WelcomeViewModel()`)
One instance per welcome document; each layout reset leaks another client. Register as DI singleton (it already accepts an injected `HttpClient`).

---

## Dictionary feature (#25, newest code)

### DICT-1 [High] Merged-headword separator `<hr/>` renders as literal text
**File:** `DictionaryService.cs:26` + `DictionaryViewModel.cs:197-219` (+ `DictionaryPanel.axaml.cs:56`)
Duplicate headwords' definitions are joined with `<hr/>`, but the renderer parses only `<see>` tags — 337 duplicate headwords in the English dictionary (e.g. `aṃso`, `akataññū`) display a literal `<hr/>` mid-definition. The oracle test pins the tag in (`DictionaryOracleTests.cs:63`).
**Fix:** Split on the separator in `ParseMeaning` and emit a visual divider, or change the separator to something the renderer handles; update the test.

### DICT-2 [Medium] Global script change leaves the dictionary panel in the old script/font
**File:** `DictionaryViewModel.cs:159-161`
No `ScriptChanged` subscription; `CurrentScriptFontFamily`/`Size` never raise change notifications — unlike `SearchViewModel` (cs:279-301), which it imitates.
**Fix:** Subscribe (unsubscribe on dispose), raise the font notifications, rebuild `DisplayWord`s, re-run `UpdateMeaning`.

### DICT-3 [Medium] Out-of-order lookup completion overwrites newer results with stale ones
**File:** `DictionaryViewModel.cs:96-99, 167-186`
Fire-and-forget lookups, no sequencing/cancellation: slow lookup #1 (first-time Hindi load under `_loadLock`) completes after fast #2 (cached English) and clobbers `Words`.
**Fix:** Apply results only if captured query+language still match current state (or sequence number / Rx `Switch()`).

### DICT-4 [Medium] Query not NFC-normalized; joiners leak into the IPE key for Latin runs
**File:** `DictionaryService.cs:132-135`
Pasted decomposed diacritics (`a`+U+0304) and ZWJ/ZWNJ in Latin runs survive into the lookup key → exact match silently lost (data files verified clean NFC). Same class as SRCH-3.
**Fix:** `query.Normalize(NormalizationForm.FormC)` + strip `‌`/`‍` in `LookupAsync`.

### DICT-5 [Medium] Panel's `WhenAnyValue` subscription never disposed on view teardown → dead panels leak per float/unfloat cycle
**File:** `DictionaryPanel.axaml.cs:14-35`
`_meaningSub` (rooted in the singleton VM) is disposed only in `DataContextChanged`; discarded panels stay reachable from the app-lifetime VM and keep running `BuildMeaning` against detached controls.
**Fix:** Dispose in `OnDetachedFromLogicalTree`/`OnUnloaded`; resubscribe on attach.

### DICT-6 [Low] Panel ctor's DI fallback is dead code that still throws
**File:** `DictionaryPanel.axaml.cs:20` + `DictionaryViewModel.cs:50-55`
`?? new DictionaryViewModel()` fires only when `App.ServiceProvider` is null, and that ctor immediately dereferences `App.ServiceProvider` → guaranteed throw (breaks the XAML previewer). Drop the fallback.

---

## CST.Core (`Books`, `Sources`, conversion routing)

### CORE-1 [High] `Books`/`Sources` lazy singletons + per-search `SetDocId` writes unsynchronized across threads
**File:** `CST.Core/Books.cs:10-21, 38-43`; `Sources.cs:7-18`
Unlocked lazy init raced by background indexing (`BookIndexerAsync` in `Task.Run`) vs UI startup restore → two `Books` instances possible, DocIds written to the losing one (`DocId == -1` breaks `allIndexed` and `FromDocId`). Steady-state: *every* search re-runs `EnsureDocIdsAsync` (`SearchService.cs:105, 888-900`) writing `booksByDocId` on pool threads — concurrent Dictionary write/write is undefined behavior in .NET.
**Fix:** `Lazy<Books>` or eager init; lock `SetDocId`/`FromDocId`; run `EnsureDocIdsAsync` once per reader generation instead of per search.

### CORE-2 [Medium] `TikaIndex = 99999` sentinel enables a Tika button that silently no-ops
**File:** `Books.cs:720, 764, 797` (consumer: `BookDisplayViewModel.cs:827, 1703, 1710`)
Three books (abh03m3, abh03m7, abh03m10) use 99999 where every other unlinked book uses −1; `HasTika = TikaIndex >= 0` enables the control, the bounds check nulls the target, click does nothing. Siblings link to 142 — possibly these should too (verify against CST4 intent); a programmatic check confirmed these are the only out-of-range links in the catalog.
**Fix:** Replace with −1 (or 142 if intended).

### CORE-3 [Medium] Script auto-detection never detects Cyrillic → Cyrillic input mis-routed as Latin
**File:** `Any2Ipe.cs:68-102` (duplicated in `Any2Deva.cs:68-102`)
No U+0400–04FF branch though `Convert(str, Script.Cyrillic)` exists three lines up; Cyrillic search/dictionary input passes through unconverted → zero results. Combining marks U+0300–036F (used by the Cyrillic scheme) should also join the surrounding run.
**Fix:** Add the range branch (+ combining-mark run handling); dedupe the two `GetScript` copies. Related: SRCH-9.

### CORE-4 [Medium] Culture-sensitive case conversions on the search/book-display paths
*(independently reported by two reviewers)*
**File:** `Latn2Ipe.cs:140` (and 109); `ScriptConverter.cs:150`; `LatinCapitalizer.cs:146`; also `SettingsViewModel.cs:593`
Turkish locale: `'I'.ToLower()` → dotless `ı` (no `latn2Ipe` entry → search terms with capital I find nothing); `Char.ToUpper('i')` → `İ` in title-cased navigation and capitalized Latin book text.
**Fix:** `ToLowerInvariant`/`ToUpperInvariant`/`Char.ToUpperInvariant` (DictionaryService already does). Note `Latn2Ipe.cs:103-104` declares `ConvertReference` a frozen oracle asserted equal to `Convert` — change both together (byte-identical for all Pāli Latin input outside tr/az locales, so corpus equality tests still hold).

### CORE-5 [Low] `Latn2Deva` is lowercase-only and doesn't fold case (asymmetric with `Latn2Ipe`)
**File:** `Latn2Deva.cs:11-43, 197-295`
Title-cased Latin input via the Deva pivot yields mixed-script garbage (`D` + देवनागरी). Current callers pre-lowercase; unguarded trap.
**Fix:** Lowercase at the top of `Convert`/`ConvertReference`.

### CORE-6 [Low] Six volume-2 PDF constants declared but never mapped (multi-volume books open vol. 1)
**File:** `Sources.cs:118, 129, 146, 150, 170, 188` vs mappings at 452, 464, 483, 486, 504, 524
Only s0201t carries a TODO. Mapping redesign is deferred on upstream (#76) — add matching TODOs to the other five so all volume-split cases are enumerated when #76 lands.

### CORE-7 [Low] Dead/confusing members (bundled)
- `BookHit.cs` — dead type (no references in active projects).
- `Books.PopulateBookList` public but ctor-only; second call would append 217 duplicates. Make private.
- `Latn2Deva.GetDevConsonants` — Hashtable-era null check unreachable on `Dictionary`; no callers.
- `LatinCapitalizer.DebugRegex` dead; `MarkCapitals:64` tests `nextIsCap` twice in one condition.
- `Sources` declared in the global namespace (no `namespace CST`).
- `Books` BitArray properties (2510-2557) have public setters; only getters used.

### CORE-8 [Low] Quadratic string building on run-splitting and capital-marking paths
**File:** `Any2Deva.cs:10-33` (identical in `Any2Ipe.cs:8-32`); `LatinCapitalizer.cs:59-75`
`run += c` / `deva += ...` per char; `MarkCapitals` rebuilds every text node of a 2–5 MB book char-by-char even when unchanged. Latent (inputs currently small/paragraph-sized) but on hot paths.
**Fix:** `StringBuilder`; in `MarkCapitals`, skip rebuild when no change.

---

## Book display (`BookDisplayView`, `BookDisplayViewModel`, `ChapterListsService`, C#↔JS bridge)

### BOOK-1 [High] Closing a book tab never disposes its WebView — one live CEF browser leaks per open/close cycle
**File:** `CstDockFactory.cs:1696-1714`; `BookDisplayView.axaml.cs:229-255`
`CloseDockable` disposes only the VM's Rx subscriptions; `BookDisplayViewModel.Dispose` explicitly leaves the WebView to the recycled View (`BookDisplayViewModel.cs:2129-2134`), `DisposeWebView()` is invoked only on window-change/float paths, and the recycled View sits in the app-lifetime `ControlRecycling` cache under a per-open GUID key that is never reused and never evicted (nothing calls the cache's `Remove`/`Clear`). Each open+close cycle permanently leaks a live CEF browser + rendered DOM + the multi-MB `HtmlContent` string.
**Fix:** In `CloseDockable`, dispose the View's WebView (expose `DisposeWebView` or add a `Shutdown` lifecycle op) and evict the entry from the recycling cache. Verify by counting Chromium Helper (Renderer) processes across repeated open+close.

### BOOK-2 [High] Cmd+E / Shift+Cmd+E inside the WebView mutates the dock layout on the CEF title-changed thread
**File:** `BookDisplayView.axaml.cs:1438-1473`
`OnTitleChanged` runs on a CEF (non-UI) thread; every other branch posts to the dispatcher, but the View Source branches execute `ShowSource1957Command.Execute().Subscribe()` synchronously → `OpenPdf` → `AddDocumentToLayout` mutates `documentDock.VisibleDockables`/`ActiveDockable` (Avalonia-bound) off the UI thread. Intermittent `InvalidOperationException`/visual corruption whenever the shortcut is used with WebView focus (the normal case).
**Fix:** Wrap both branches in `Dispatcher.UIThread.Post`; audit the `CST_COPY_REQUESTED`/`CST_SELECT_ALL_REQUESTED` branches (:1399, :1422), which call `EditCommands` off-thread too.

### BOOK-3 [Medium] Every book opened in a non-Devanagari script runs the full XML→convert→XSLT pipeline twice
**File:** `BookDisplayViewModel.cs:114, 125, 212-288, 304`; `CstDockFactory.cs:522-527`
The VM ctor creates a throwaway `new ScriptService()` (bypassing the DI singleton), so `_bookScript` always starts Devanagari; the factory then sets `BookScript`, firing the `.Skip(1)` subscription and a second full `LoadBookContentAsync` on top of the ctor's. Pure wasted CPU/GC (value-equal `HtmlContent` suppresses a visible reload) — worst at session restore with many tabs.
**Fix:** Pass the target script (or the DI `IScriptService`) into the ctor so the post-construction set is a no-op; delete the local `new ScriptService()`.

### BOOK-4 [Medium] Title-channel C#↔JS bridge drops messages (reads current title; identical consecutive messages never fire)
**File:** `BookDisplayView.axaml.cs:1167-1169, 1539-1541, 2240-2252`
`OnTitleChanged` reads `_webView?.Title` (latest value) rather than the triggering value, so rapid successive title writes coalesce; writing the same message twice fires no event at all. Concretely: the always-on JS keydown DEBUG logger (:1541) can overwrite a `CST_GET_PARA_RESULT:` before C# reads it → position saved from a stale anchor (float/unfloat, shutdown); two Cmd+C presses within one status tick → second copy silently does nothing (JS already `preventDefault`ed the native copy).
**Fix:** Append a monotonic sequence number to every title message; longer term, replace the channel with WebViewControl's `RegisterJavascriptObject` binding. At minimum, remove the per-keydown title logging from production.

### BOOK-5 [Medium] Chapter-anchor regex drift: nested chapters (two underscores) invisible to scroll-based chapter tracking
**File:** `BookDisplayView.axaml.cs:864` vs `:1713`
Two duplicated JS blocks drifted: the anchor cache (feeding `CHAPTER=` status updates) uses `(_\d+)?` while the init-only block uses `(_\d+)*`. Real data has ids like `an7_1_1`…`an7_1_5` (verified in chapter-lists.json): scrolling never selects them in the chapter dropdown (stays on the parent), and they can't be the position-save anchor.
**Fix:** `(_\d+)*` in the cache regex; delete the now-redundant `cstChapterTracking` block.

### BOOK-6 [Medium] Chapter headings include footnote text — `<note>` strip regex runs after tags are already gone
**File:** `ChapterListsService.cs:200-215`
`ExtractHeadingFromDiv` reads `headElement.InnerText` (markup stripped, note *text* kept), then applies a regex matching `<note>…</note>` tags — dead against tag-free text. Verified in corpus: `s0503m.mul.xml`'s chapter dropdown entry merges the footnote into the heading.
**Fix:** Remove `note` child nodes before reading text (clone → remove `.//note` → `InnerText`).

### BOOK-7 [Medium] Initial scroll/anchor restoration is a fixed-delay race that fails silently on slow loads
**File:** `BookDisplayViewModel.cs:669-689, 702-719`; `BookDisplayView.axaml.cs:1136-1161, 2074`
Restoration uses hard-coded delays (1000/300/500/2000 ms) instead of awaiting navigation completion; `ScrollToPageAnchor` silently returns when `!_isBrowserInitialized` — on slow loads the book opens at the top and the saved position is then overwritten by the next status update. (Related to #36 work.)
**Fix:** Keep a single `_pendingAnchorNavigation` executed from `OnNavigationCompleted` instead of delay-then-hope; drop the overlapping mechanisms.

### BOOK-8 [Low] Per-tab temp HTML files written synchronously on the UI thread and never deleted
**File:** `BookDisplayView.axaml.cs:517-528`
Each View writes `cst_book_{file}_{tabId}.html` (fresh name per View, including per float/unfloat) into `Path.GetTempPath()` with a blocking multi-MB `File.WriteAllText` on the UI thread; nothing deletes them.
**Fix:** Delete in `DisposeWebView`/on close; sweep stale `cst_book_*.html` at startup; write off-thread before posting `LoadUrl`.

### BOOK-9 [Low] `TotalHits` initialized to the number of search *terms*, not hits
**File:** `BookDisplayViewModel.cs:172-173`
If highlighting fails (no term vectors / index mismatch — both have explicit warning paths at :991-996), the toolbar shows "1 of 3" (3 = term count) with zero highlights and dead nav buttons.
**Fix:** Initialize to 0; set `HasSearchHighlights` only from the actual highlight-application result.

### BOOK-10 [Low] Dead/broken public bridge surface in BookDisplayView (bundled)
**File:** `BookDisplayView.axaml.cs:558, 2168-2207, 2301-2339, 2608-2627`; `BookDisplayViewModel.cs:1756-1770, 1812-1834`
`SetFootnoteVisibility` targets `.footnote` but the XSL emits `class="note"` (can never work); `GetSelectedTextAsync` references an undefined `window.cstTabId` and emits a title message no handler consumes; `OnBrowserInitialized` never subscribed; `_lastCapturedAnchor` never assigned; `CalculateNavigationAnchorAsync` has an unreachable branch (`currentAnchor = null` then null-check); private `GetCurrentParagraphAnchorAsync` uncalled. Grep-verified no external callers.
**Fix:** Delete; if footnote toggling is a planned feature, fix the `.note` selector when wiring it.

### BOOK-11 [Low] Anchor names interpolated into JS without escaping
**File:** `BookDisplayView.axaml.cs:1930-1938, 2088-2104`
Anchors are spliced into single-quoted JS literals/selectors. Inputs are internally generated today (no live exploit), but any anchor with a quote (hand-edited state file, future corpus anomaly) silently breaks the whole injected script.
**Fix:** JSON-encode the value (`JsonSerializer.Serialize(anchor)`) as the JS literal.

---

## Cross-cutting sweep (async/threading/IO/culture, active projects)

### XCUT-1 [High] Two exit paths force-shutdown without a completed state save
**File:** `App.axaml.cs:267-268`; `LayoutViewModel.cs:118-124`
The graceful shutdown sequence (recapture book states → `ForceSaveAsync` → `ServiceProvider.Dispose()` → settings flush) is wired only to `ShutdownRequested`, but two paths call `desktop.Shutdown()` directly (the *forced* variant — the handler never runs): the `CloseRequested` lambda (`_ = SaveApplicationStateAsync(); desktop.Shutdown();` — the fire-and-forget save races process exit) and `LayoutViewModel.ExitApplication` (no save at all). Note: the sweep traced `CloseRequested` to `OpenBookDialog.axaml:227`'s Close button, while the startup reviewer believed that dialog is never instantiated (SCRIPT-2 confirms the dialog is dead legacy UI) — either way it's a loaded gun; `ExitCommand` appears unbound today.
**Fix:** Route both through the `ShutdownRequested` sequence (await the save, dispose, then shut down — or call `desktop.TryShutdown()` so the existing handler runs). Overlaps STATE-6 item 4.

### XCUT-2 [Medium] `ChapterListsService` mutates a plain Dictionary and writes its JSON from concurrent book-load threads
*(independently reported by two reviewers)*
**File:** `ChapterListsService.cs:52-58, 99, 111, 117, 282` (caller: `BookDisplayViewModel.cs:774-784`)
Every `LoadChaptersAsync` runs `GetChapterList` inside `Task.Run`; on first run/after an XML update, several restored tabs generate concurrently → unsynchronized `Dictionary` writes (throw or corrupt) + overlapping non-atomic `File.WriteAllText` to `chapter-lists.json` (IOException swallowed at :288, partial file possible).
**Fix:** Lock (or `ConcurrentDictionary` + a lock around the save) covering `GetChapterList`/`Generate`/`LoadFromFile`/`ClearAll`.

### XCUT-3 [Medium] Settings font pre-load mutates UI-bound VM properties from a thread-pool thread via `async void`
**File:** `SettingsViewModel.cs:227-235, 308, 344, 375-400`
The ctor's fire-and-forget `Task.Run` loop calls `LoadAvailableFontsForScript` (async void) per script; all its property sets (`AvailableFonts`, `SelectedFontFamily` — including inside the catch) raise `PropertyChanged` off-thread into live ComboBox bindings; an exception escaping the catch's own setters would crash the process. (Same loop is one of the two concurrent-writer triggers in SCRIPT-1.)
**Fix:** Make it `async Task`, enumerate off-thread, marshal all `scriptVm` assignments via `Dispatcher.UIThread.InvokeAsync`, observe the task.

### XCUT-4 [Medium] Non-atomic writes: file-dates cache and chapter lists (same defect as STATE-3's settings.json)
**File:** `XmlFileDatesService.cs:211, 296`; `ChapterListsService.cs:282` (contrast the correct temp+`File.Replace` in `ApplicationStateService.cs:200-212`)
A torn file-dates cache loses `LastIndexedTimestamp`s → full 217-book re-index next startup; torn settings → silent revert to defaults including `XmlBooksDirectory`.
**Fix:** One shared atomic-write helper (temp + `File.Replace`) used by all three services.

### XCUT-5 [Low] Unguarded `async void` lambda on the BookScript change subscription
**File:** `BookDisplayViewModel.cs:214-287`
`.Subscribe(async script => { ... })` with no top-level try/catch: `GetCurrentPageAnchor()` (:226), the chapters-rebuild dispatcher block (:238-273), and the post-delay restore (:277-286) are unguarded — an exception there (e.g. script change racing tab teardown) is an unhandled async-void crash.
**Fix:** Wrap the body in try/catch with logging (matching `FloatDockableWithoutRecycling`).

### XCUT-6 [Low] Corpus XML read passes `Encoding.UTF8` for UTF-16-LE files — works only via BOM detection
**File:** `BookDisplayViewModel.cs:883`
`File.ReadAllText(xmlPath, Encoding.UTF8)` works today only because the FF FE BOM overrides the argument; a BOM-less file would decode to mojibake with no error, and the explicit UTF-8 argument misleads maintainers given the corpus-encoding hard rule.
**Fix:** Pass `Encoding.Unicode` + a comment citing the rule.

### XCUT-7 [Low] Culture-sensitive `StartsWith`/`EndsWith` in the indexing/token pipeline → locale-dependent index behavior
**File:** `CST.Lucene/DevaXmlTokenizer.cs:221, 235`; `IpeFilter.cs:35`; `BookIndexerAsync.cs:78-80`
Tokens can contain ZWJ/ZWNJ (in `wordChars`); ICU culture-sensitive comparison treats them as ignorable — e.g. `"…-‍".EndsWith("-")` is true and the `Substring` removes the ZWJ, not the hyphen — so tokens/offsets depend on locale/ICU version. Indexing must be deterministic.
**Fix:** `StringComparison.Ordinal` overloads (or direct char comparison) throughout tokenizer/filter.

---

## Scripts, fonts, localization & misc UI

### SCRIPT-1 [High] Unsynchronized Dictionary caches written concurrently from parallel font-loading tasks
**File:** `FontService.cs:93, 100-115`; `Platform/Mac/MacFontService.cs:280, 311`
Every startup, `PreloadFontsForAllScriptsAsync` launches 16 parallel tasks whose continuations write `_cachedFonts[script] = fonts;` on thread-pool threads; every Settings-open, ~14 overlapping writers hit `_systemDefaultFontCache[script] = ...` inside `Task.Run`. Concurrent writes to `Dictionary<K,V>` throw or permanently corrupt (infinite-loop-on-resize) → wrong/empty font lists or a hung thread, intermittently.
**Fix:** `ConcurrentDictionary` for both caches, or serialize the preload.

### SCRIPT-2 [High] Hard-rule violation: the word "Buddhist" in UI markup and a doc comment
**File:** `Views/OpenBookDialog.axaml:104` (`Text="Buddhist Text Collection"`); `ViewModels/OpenBookDialogViewModel.cs:23` (summary comment)
CLAUDE.md forbids the word anywhere in app UI or documentation; the string ships in the compiled binary even though the dialog window itself appears to be dead legacy UI (the live view is `OpenBookPanel`).
**Fix:** Reword ("Tipiṭaka Text Collection" / "Pāli texts"); better, delete the unused `OpenBookDialog.axaml(.cs)` window entirely (also removes the crash-capable `RefreshCommand` binding, SCRIPT-10, and the XCUT-1 `CloseRequested` trigger).

### SCRIPT-3 [Medium] Go To dialog accepts non-ASCII Unicode digits → broken or empty anchors
**File:** `GoToDialogViewModel.cs:175-176, 139, 205-212`
Validation uses `\d`/`char.IsDigit` (any Unicode Nd — e.g. Devanagari १२३ pasted from a book) but page parsing uses ASCII-only `int.TryParse`: paragraph mode builds `para१२३` (no such anchor, silent no-op); page mode returns `""` yet `Navigate()` still sets `DialogResult = true` and closes having done nothing. Known pasted-input bug class. No upper-bound validation either (`para99999` accepted, silent no-op).
**Fix:** `[0-9]` in the patterns (or normalize digits to ASCII first); consider range validation against the book's actual pages/paragraphs so OK disables.

### SCRIPT-4 [Medium] Book-tree category counts render with literal backslashes — malformed `StringFormat`
**File:** `Views/OpenBookPanel.axaml:93`
`Text="{Binding BookCount, StringFormat=(\\{0\\})}}"` — double-escaped braces plus a stray trailing `}`; verified in the compiled DLL: every category row in the always-visible panel shows `(\52\)` instead of `(52)`. (The dead `OpenBookDialog.axaml:142` has the correct form.)
**Fix:** `StringFormat=(\{0\})` and drop the extra `}`.

### SCRIPT-5 [Medium] Tree-state restore raced against state load with a fixed 100 ms delay; loss permanently clobbers saved expansion
**File:** `OpenBookDialogViewModel.cs:75-81, 462-499` (state replace at `ApplicationStateService.cs:160`)
The VM synchronizes with `LoadApplicationStateAsync` by sleeping 100 ms. On a slow load, restore reads the default empty `ExpandedNodeKeys` and skips; the user's first expand/collapse then saves the near-collapsed tree over the previously persisted expansion. The project's known restore-race pitfall class.
**Fix:** Deterministic signal — init from `App.InitializeFromLoadedState` (as ScriptService is, #81) or await a `StateLoaded` task on the state service.

### SCRIPT-6 [Low] `ScriptService` fires an immediate, unserialized `SaveStateAsync` per script change
**File:** `ScriptService.cs:55`
Every other mutator uses `MarkDirty()` + the debounced timer; this fire-and-forget full save races the timer save on the shared `.tmp` (one `File.Replace` throws — see STATE-2) and bypasses `_isDirty` bookkeeping.
**Fix:** Replace with `MarkDirty()` (state saves on shutdown anyway).

### SCRIPT-7 [Low] `MacFontService`: CF object leak on the exception path
**File:** `Platform/Mac/MacFontService.cs:399-431`
`matchingDescriptorsRef` (Create-rule CFArray) released only on the success path, not in `finally`; a throw in the extraction loop leaks it. One-shot-per-script and cached, so small.
**Fix:** Hoist the declaration and release in `finally` like the other refs.

### SCRIPT-8 [Low] `LocalizationService` is an inert stub with a thread-affinity flaw for when it's wired up
**File:** `LocalizationService.cs:63-76, 43-61`
`GetString` returns the key; nothing calls it in production. `ChangeCultureAsync` sets per-thread culture only (not `CultureInfo.DefaultThreadCurrentUICulture`), and `zh-CHS`/`zh-CHT` produce synthetic cultures that won't match `zh-Hans`/`zh-Hant` satellite resources.
**Fix:** When implementing localization: `DefaultThreadCurrentUICulture`, `zh-Hans`/`zh-Hant`, key→parent→invariant fallback.

### SCRIPT-9 [Low] Dead `_nodeCache` makes the "log once" guard a no-op
**File:** `OpenBookDialogViewModel.cs:33, 553-562`
Never written → the debug log fires for every node on every script change (hundreds of lines per switch).
**Fix:** Populate it or delete field + guard.

### SCRIPT-10 [Low] `RefreshCommand` wraps an unguarded async operation in `async void`
**File:** `OpenBookDialogViewModel.cs:63, 332-336`
`RefreshTreeAsync`/`BuildBookTreeAsync` have no try/catch; an exception crashes the process. Only reachable from the dead `OpenBookDialog.axaml` toolbar (hence Low).
**Fix:** try/catch like `InitializeAsync`, or remove with the dead dialog (SCRIPT-2).

---

## Verified-clean highlights (what the reviewers checked and did NOT flag)

- **Dock:** all documented CEF workarounds match the architecture docs; spine protection, `CleanupEmptySplits`, geometry clamp (#105), triple-Dispatcher proportion restore all correct; `async void` float/unfloat fully try/caught.
- **Search:** tokenizer offset convention (#53) consistent end-to-end; multi-word/phrase/proximity algorithms correct and well-tested; #59/#60/#61 fixes verified present; BOM-detected UTF-16-LE reads correct at index time.
- **State:** atomic temp+`File.Replace` state save (single-writer case); malformed-JSON → backup fallback → defaults with migration/sanitization well-tested; shutdown save paths (#67/#70/#56/#87) correct.
- **Network:** GitBlobHash semantics exactly right; every downloaded XML verified against tree SHA before touching the corpus; raw bytes written (UTF-16-LE preserved); single long-lived HttpClient with timeouts in XmlUpdateService; no retry storms; stuck-"checking..." latch (#38) correct.
- **Dictionary:** binary-search/prefix logic boundary-verified; strictly ordinal collation; data files clean (UTF-8, NFC, strict alternation); load path exception-safe; DI lifetimes consistent; packaging seed path correct.
- **CST.Core:** full catalog integrity verified programmatically (indices, links, reciprocity — the three 99999s are the only out-of-range links); #86 optimized converters match frozen references; converter lookup tables immutable/thread-safe; no catastrophic regexes.
- **Book display:** Lucene highlight offsets align with the BOM-detected UTF-16-LE decode end-to-end; hit-navigation indexing has no off-by-one (JS 0-based ↔ C# 1-based checked; context terms correctly excluded from counts); tab-ID filtering on every title message prevents cross-tab contamination; `ParsePage`/`ParseParagraph` match CST4 semantics; scroll-timer and Dispose paths correct as far as they go (the recycling-cache leak BOOK-1 is the exception).
- **Scripts/fonts:** MacFontService P/Invoke marshaling and CF release discipline correct (except SCRIPT-7); CoreText safe off the main thread; all-14-script conversion verified empirically via CST.Core; font fallback degrades gracefully; `OpenBookDialogViewModel.Dispose` unsubscribes both events; GoTo letter shortcuts match the spec; `dotnet build` succeeds with 0 warnings.
- **Cross-cutting sweep ran clean on:** blocking waits on live UI paths (CLI-only `GetAwaiter().GetResult()` in Program.cs is fine); the guarded async-void set (`OnSaveTimerElapsed`, float/unfloat, menu handlers); fire-and-forget targets (all have internal try/catch); empty catches (only around logging in SplashScreen); HttpClient lifetime in XmlUpdateService/WelcomeUpdateService instances themselves; per-tab event unsubscription; lock targets; `DateTime.Now` uses; no dead files at the project root (the CLI oddballs are reachable via Program.cs flags).
- **Test-coverage gaps noted (search):** no tests for `SearchService` itself (expansion, cache invalidation, reader refresh, filtering); no joiner-input tests; `IndexCorruptionTests` document non-recovery rather than assert recovery; nothing tests concurrent search/index interleaving.

---

## Outstanding work

All nine subsystem reviews are included above. Deferred (session limit):

1. **Independent verification pass** — the orchestrator did not re-verify the reviewers' findings against source (hence the caveat at the top). Each finding cites file:line, so verification is cheap to do finding-by-finding as fixes are picked up. Watch for the one known reviewer disagreement: whether `OpenBookDialog.axaml`'s Close button is reachable (XCUT-1 vs STATE-6/SCRIPT-2) — moot if the dead dialog is deleted as SCRIPT-2 recommends.
2. **Issue filing** — per project convention, bugs belong in GitHub issues; this document is a working hand-off for the fixing session, not a permanent backlog. Convert surviving findings to issues (or close them with fixes) and then archive/delete this file.
