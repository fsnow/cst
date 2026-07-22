# Dock Subsystem — Current Architecture (Spike)

**Status:** Spike / current-behavior map. Foundation for the planned dock-stabilization
overhaul. Line numbers are as of June 2026 and will drift.

**Why this exists:** The dock subsystem (Dock.Avalonia + `CstDockFactory` +
`LayoutViewModel` + the WebView views) is the most fragile part of the app. Real dock use
can produce degraded layouts (empty-id docks, a lost `LeftToolDock`) and native CEF crashes.
This document captures what the code does *today* so the redesign can be deliberate rather
than ad-hoc. The docking UI itself is a non-negotiable must-have.

---

## 1. Layout object model

Built once in `CstDockFactory.CreateLayout()` (~lines 43–248):

```
Root (RootDock, Id="Root")
└─ WindowLayout (RootDock, Id="WindowLayout")
   └─ MainDock (ProportionalDock, Horizontal, Id="MainDock")
      ├─ LeftTools (ProportionalDock, Id="LeftTools", Proportion 0.25)
      │  └─ LeftToolDock (ToolDock, Id="LeftToolDock", Alignment.Left)
      │     ├─ OpenBookDialogViewModel   (tool: "Select a Book" tree)
      │     └─ SearchViewModel           (tool: Search panel)
      ├─ MainSplitter (ProportionalDockSplitter, Id="MainSplitter")
      └─ MainDocumentDock (DocumentDock, Id="MainDocumentDock", Proportion 0.75)
         └─ WelcomeViewModel (ReactiveDocument, Id="WelcomeDocument") + book/PDF documents
```

- Tools and documents *are* the ViewModels (ReactiveTool / ReactiveDocument pattern) — no wrapper objects.
- **`WelcomeDocument` is a workaround, not a design preference.** It is permanent and non-closeable
  (`CanClose=false`) **as a workaround** to stop `MainDocumentDock` from becoming empty and being
  collapsed/removed by `CleanupEmptySplits` — *not* because an always-present welcome page is wanted.
  **Note the asymmetry:** `LeftToolDock` got no equivalent workaround, so when both tool panels leave it
  is removed (failure mode #4) and the View-menu can't bring the panels back. The overhaul should make
  the dock layer handle empty/missing docks correctly (recreate structure on demand) so such keep-alive
  workarounds aren't needed — at which point the welcome page could itself become closeable.
- Floating windows are `CstHostWindow`s tracked in `CstDockFactory.HostWindows`; each has its own `Layout` (an independent dock tree with its own `DocumentDock`). A floating window can hold **multiple books** — dragging one floated window's tab onto another combines them into one window (an **intended grouping feature to preserve**). **CORRECTION (#458): the combine did NOT go through any dispose+recreate.** The earlier claim here was false — the recreate branch in `BookDisplayView.OnAttachedToVisualTree` is dead code (`OnDetachedFromVisualTree` nulls `_currentWindow`, so every re-attach takes the "first time" path; zero "WINDOW CONTEXT CHANGED" log lines ever). The cross-window drag path (`MoveDockable`/`SwapDockable` 4-arg overloads, `SplitToDock` across windows) carried the **live** browser into the new window, and the crash is **deterministic**, not intermittent: it fires on the first re-attach after the browser's birth window is destroyed (e.g. the emptied source float auto-closes). **Fixed (#458):** `CstDockFactory` now overrides those cross-dock overloads and calls `DisposeAndEvictRecycledView` **before** the move for a book/PDF crossing windows — the same dispose-before-move the buttons use — so a fresh browser is built at the destination. `BookDisplayView` carries a `_browserBirthWindow` invariant check that logs an Error if a live browser ever re-attaches to a different window.

---

## 2. The ID scheme (and why it's the core problem)

Three different, **inconsistent** id conventions coexist:

| Kind | Examples | Source |
|---|---|---|
| **Fixed well-known strings** | `Root`, `WindowLayout`, `MainDock`, `LeftTools`, `LeftToolDock`, `MainDocumentDock`, `MainSplitter`, `WelcomeDocument` | Hardcoded in `CreateLayout` |
| **GUID-based** | book documents (auto GUID), search opens `Search_{file}_{guid:N}`, float/unfloat recreate with fresh GUID (`windowId: null`); window ids | Per-instance |
| **Empty** | Docks that Dock.Avalonia creates during a **drag** (split / re-dock) get `Id == ""` | Framework, uncontrolled |

The code **looks docks up by fixed id** in many places — `FindToolDock(mainDock, "LeftToolDock")`,
`FindDockByIdRecursive(root, "MainDock")`, proportion capture/restore keyed on `"LeftTools"`/`"MainDocumentDock"`,
`SetEqualProportions` skips `"MainDock"` by id, `IsEmptyDock` spares `"LeftTools"` by id. So when a drag
produces empty-id docks, or a fixed dock is removed, these lookups silently fail or misfire.

**Design implication:** dock ids need the same treatment window ids got — **stable, unique, and always
assigned** (including to dynamically-created docks), so lookups/cleanup/restore are reliable and
position-independent. **Exception — the invariant spine** (`Root → WindowLayout → MainDock →
MainDocumentDock`, + the permanent Welcome doc) legitimately keeps **fixed** well-known ids (created
once, never moved) and must be **protected from cleanup**; only the variable parts (tool docks,
drag-created splits, floating windows) need unique ids + recreate-on-demand. (Today `MainDock` isn't
*explicitly* protected; it survives single-child collapse only **incidentally**, because its parent is a
`RootDock` which the redundancy rule never scans. **But a drag can re-parent it:** dropping a tool at the
top-level edge wraps `MainDock` in a new empty-id `ProportionalDock` (confirmed live June 2026), which
*removes* that incidental safety — `MainDock` is then a scanned child and **was collapsed live** (June
2026: `Removing empty split: ProportionalDock (ID: MainDock)`) once it went single-child in that nested
state. So protect the spine **explicitly**. See §7 Q1.)

---

## 3. Component responsibilities

- **`CstDockFactory.cs` (~2826 lines)** — does almost everything: builds the layout; opens books/PDFs;
  close/remove; **button** float/unfloat (recreation); `CleanupEmptySplits`; `SplitToDock`/`SwapDockable`/
  `MoveDockable` overrides; proportion capture/restore; floating-window lifecycle; the document
  collection-changed handler; `_goToSubscribedBooks`; application-state save/restore of book windows.
  Single-responsibility violation; prime candidate for extraction.
- **`LayoutViewModel.cs`** — View-menu panel show/hide/toggle (`ShowSearchPanel`/`ShowSelectBookPanel`/
  `Hide*`/`Toggle*`), `FindTool`/`FindToolDock`/`FindParentDock`, and the empty-`LeftToolDock` removal.
- **`BookDisplayView.axaml.cs` (~2563 lines)** — the per-document View hosting the CEF `WebView`; WebView
  lifecycle, scroll/anchor cache, search-highlight navigation.
- **`SimpleTabbedWindow.cs`** — main window; **global** dock-drag detection that hides/restores all
  WebViews during a drag; window geometry save/restore.

---

## 4. CEF WebView lifecycle (the crash-critical part)

**Core rule the code follows:** never carry a *live* CEF WebView across a re-parent. Instead it
**disposes and recreates** the browser whenever the View's window context changes.

**Confirmed mechanism** (see [`CONTROL_RECYCLING_CEF_CRASH.md`](../implementation/CONTROL_RECYCLING_CEF_CRASH.md)):
on macOS CEF binds its native handle to the **creating window** and **aggressively disposes the browser
when the NSView detaches** from its window; a later re-attach then dereferences the dead child handle →
SIGSEGV in `AvnNativeControlHostTopLevelAttachment::InitializeWithChildHandle`. CEF lifecycle = View
lifecycle; a live browser **cannot** survive a cross-window move. The one *clean* fix
(`NativeWebView.BeginReparenting`) ships only in the **paid Avalonia Accelerate** — **off the table for
open-source CST** — which bounds the overhaul to the free manual *dispose-before-move + recreate*
lifecycle. ControlRecycling stays **enabled** (App.axaml) for instant same-window tab switching.

**Cost of the free (recreate) approach — not just a crash trade:** every dispose+recreate reloads the
book and **loses precise state** — scroll lands *near* (anchor-based) not exact, text selection is lost,
in-page JS state resets, plus a flicker. The buttons already pay this; spreading it to drags would
regress same-window splits/reorders (instant today). So recreate must be **selective** — paid only when a
drag genuinely **crosses a window** (the fatal case), never same-window. `BeginReparenting` (paid) would
remove this cost entirely by keeping the live browser; the free middle path cannot.

- `OnAttachedToVisualTree` (~229–303): three branches by **reference equality**: **(a)** `_currentWindow`
  is non-null and a **different** window → real window change → `DisposeWebView()` + `TryCreateWebView()`
  (fresh CEF) + reload HTML; **(b)** `_currentWindow == null` → first attach / post-detach reattach →
  just track the window, **no recreate**; **(c)** same window instance → **ControlRecycling tab switch →
  no recreate (instant switching preserved)**.
- `OnDetachedFromVisualTree` (~305–321): sets `_currentWindow = null` — a "CRITICAL FIX" to force
  window-change detection on a later reattach (fixed a float→unfloat→tab-switch→tab-back crash). **Tab
  switches do *not* recreate the WebView.** Caveat: because detach nulls `_currentWindow`, *which* drag
  paths actually hit branch (a) vs (b) is subtle and has been iteratively patched — a fragility the
  overhaul should untangle (the reliable recreate is the explicit button path below).
- `OnWebViewLifecycleOperationChanged` (~374–412): the **button** float/unfloat path drives this
  **explicitly and in sequence** — VM sets `PrepareForFloat`/`PrepareForUnfloat` → `DisposeWebView()`;
  then `RestoreAfterFloat`/`RestoreAfterUnfloat` → `TryCreateWebView()` + reload HTML.
- `SimpleTabbedWindow` drag handling (~432–583): a 50 ms timer watches `DockControl.IsDraggingDock`;
  after a 150 ms threshold it **hides all WebViews in all windows**, restoring them ≥100 ms after the
  drag ends. **This hide is itself a workaround** for the native-WebView **airspace** problem (the CEF
  surface renders on top and would otherwise occlude Dock's drop indicators during a drag). Crucially it
  only sets `IsVisible=false` — it does **not** dispose the browser, so a live CEF surface still exists
  through the drop.

**Why buttons are safe and drags crashed:** the button path tears down and rebuilds the browser in a
**controlled, sequenced** way *before* the dock move (dispose+evict, move, fresh browser at the
destination) — no live browser exists during the move. A **cross-window drag** instead let the framework
move the live View across windows; the `OnAttachedToVisualTree` window-change "recreate" branch that was
supposed to catch this is **dead code** (see the #458 correction under Floating windows above), so nothing
disposed the browser. The dividing line was **dispose-before-move (buttons) vs carry-live-browser
(drags)**. **Fixed (#458):** `CstDockFactory` now overrides the cross-dock `MoveDockable`/`SwapDockable`
overloads and cross-window `SplitToDock` to dispose-before-move for books/PDFs, so the drag paths match the
buttons. The crash was **deterministic** (fires on the first re-attach after the browser's birth window is
destroyed), not a timing race — it only looked intermittent because many different drag sequences were
being exercised.

**Pre-move hooks available** (for a dispose-*before*-move on drags): Avalonia has **no** pre-detach
event (only `DetachedFromVisualTree`, post). Dock's factory events are all post (`DockableMoved`/
`Removed`/`Undocked`). But the drag state machine exposes **`OnMoveDragBegin`/`OnMoveDrag`** (a real
drag-start signal — verify hookability), and `ValidateDockable(bExecute:false)` is a pre-execute (but
hover-noisy) phase. Simplest: the existing `IsDraggingDock` drag-start detection already fires pre-move —
switch it from *hide* to *dispose* (+recreate on drop), which would also subsume the airspace workaround.
Caveats: handle a cancelled drag (recreate if no drop) and same-window reorder.

`CanFloat = false` on book documents (set at open, ~515) blocks drag-to-float specifically; book
drag-to-split and tool-panel drags are still possible and still hit the risky path.

### Workarounds inventory (the dock subsystem is held together by these)

Each is a point-fix for a CEF ↔ Dock.Avalonia ↔ Avalonia-NativeControlHost interaction. The overhaul
should **consciously replace** them with a coherent model, not add another. The **complete ~40-item
inventory (every kludge + its reason) is in [`DOCK_WEBVIEW_WORKAROUNDS.md`](DOCK_WEBVIEW_WORKAROUNDS.md)**;
the six below are the headline ones:

1. **Ever-present, non-closeable Welcome page** — keeps `MainDocumentDock` from going empty/collapsing (§1).
2. **Dispose+recreate the WebView on every window-context change** (incl. tab switch) — avoids CEF handle
   corruption across ControlRecycling.
3. **Button-based float/unfloat** — controlled dispose-before-move; the only crash-reliable float path.
4. **`CanFloat=false` on books** — blocks the drag-to-float crash.
5. **Hide all WebViews during a drag** — airspace workaround so drop indicators are visible; does not dispose.
6. **(June 2026) Restore `CanFloat=false` on button-unfloat** — closes a drag-float crash after unfloat.

A single **dispose-before-move on drag-start** could plausibly retire #4 and #5 (and make drag-split/
unfloat/combine as safe as the buttons); #1's retirement depends on the dock layer handling empty docks.

---

## 5. Operation flows (entry points)

- **Open book:** `OpenBook` (~461) / `OpenBookInNewTab` (search, ~342) / `OpenPdf` (~588) →
  `AddDocumentToLayout` (~823). Sets `CanDrag=true`, `CanFloat=false`; subscribes events; adds to
  `_goToSubscribedBooks`; captures/restores MainDock proportions around the add.
- **Close:** `CloseDockable` (~1559) → `RemoveBookWindowState` → `base` → `vm.Dispose()` +
  `_goToSubscribedBooks.Remove` → `CleanupEmptySplits`.
- **Button float:** `FloatDockableWithoutRecycling` (~1295): capture scroll/state → `RemoveDockable(old)`
  + `old.Dispose()` → create fresh VM (new GUID) → `FloatDockable(new)`; toggles `CanFloat` true then
  back to false (~1387–1389).
- **Button unfloat:** `UnfloatDockableWithoutRecycling` (~1406): mirror; create fresh VM in main dock;
  close source floating window if empty. **(Fixed June 2026: now also restores `CanFloat=false` on the
  recreated VM — its omission let a button-unfloated book be drag-floated → CEF crash.)**
- **Drag split/move/swap:** `SplitToDock` (~910, prevents tools tab-docking into the DocumentDock, forces
  50/50), `SwapDockable`/`MoveDockable` (~1604) → `CleanupEmptySplits`.
- **Cleanup:** `CleanupEmptySplits` (~1952, ≤10 iterations) → `FindEmptySplits`/`IsEmptyDock`/
  `RemoveEmptySplit`/`CleanupSplitters`. Can collapse single-child proportional docks and remove docks.
- **Panel show/hide:** `LayoutViewModel.ShowSearchPanel`/`ShowSelectBookPanel` (find tool by id first,
  else create + add to `LeftToolDock`); `RemoveToolFromLayout` removes the tool and, if `LeftToolDock`
  becomes empty, **removes `LeftToolDock` from `MainDock`**.
- **Save/restore:** book windows + window geometry persisted to `ApplicationState`
  (`SaveAllBookWindowStatesAsync` ~611, `SaveBookWindowState` ~645). **The dock split-structure itself is
  not serialized.** Window geometry restore now validates against connected screens
  (`SimpleTabbedWindow.RestoreWindowState`).

---

## 6. Known failure modes (reproduced or revealed)

1. **CEF segfault on drag re-parent** (drag-to-split, drag-unfloat) — framework-driven recreate races the
   re-parent. Intermittent; button paths are crash-free. (Issue #39.)
2. **`CanFloat` not restored on button-unfloat** → book becomes drag-floatable → crash. **Fixed.**
3. **Empty-id docks** from drags → fixed-id lookups (proportions, cleanup, panel restore) misfire.
   **Reproduced live (June 2026):** a single tool-panel drag-split (`SplitToDock`) spawned an empty-id
   `ProportionalDock` wrapping `MainDock` **and** an empty-id `ToolDock` for the dragged panel — leaving
   two tool docks and an unnamed wrapper above the spine (spine itself held). **Each subsequent split-drag
   spawns another empty-id `ProportionalDock`** (via `base.SplitToDock` → un-id'd `CreateProportionalDock`),
   so they **nest/proliferate**; the second such drag also collapsed `MainDock` (see §2).
4. **`LeftToolDock` removed when emptied** (`RemoveToolFromLayout`) → `ShowSearchPanel`/`ShowSelectBookPanel`
   find no `LeftToolDock` → "Layout may be corrupted" no-op → **panels unrecoverable without restart**.
   **Reproduced live (June 2026):** floating the tool dock out then closing its window removed
   `LeftToolDock` *and* `LeftTools`; the **spine held** (`MainDock`/`MainDocumentDock`/Welcome survived —
   `MainDock` did *not* collapse), but the tool panels were left with **no container** → View-menu
   restore no-ops (exact log: `ERR [Layout] LeftToolDock not found - cannot add Search panel. Layout may
   be corrupted.`) — recoverable only by restart. Confirms the fix shape (Q1+Q2): protect the spine, and
   recreate the tool container under the always-present `MainDock`.
5. **Drag-unfloat doesn't update `IsFloating`** → float/unfloat button state desyncs (only the button
   methods set `IsFloating`).
6. **Floated book's tab name reverts to Devanagari** — recreated VM's title not re-applied in current script.
7. **Fixed-id fragility generally:** `MainDock`/`LeftTools`/`MainDocumentDock` lookups assume the structure
   and ids are intact; degraded structures break proportions and restore.
8. *(Adjacent, separate)* double-reindex on XML update (#40); window-off-screen restore (fixed).

---

## 7. Open design questions for the overhaul

**Release goals / hard constraints (Frank, June 2026):**
- **Zero known crashes by Beta 4.** The intermittent CEF drag crash (#39) is **release-blocking** — the
  selective controlled-recreation fix (Q3) must land. No known crash may ship.
- **Minimum window re-creations — only where needed.** Recreation carries the §4 UX cost (lost exact
  scroll/selection, flicker), so recreate **only** on a genuine cross-window move; never for same-window
  splits/reorders/tab-switches.
- **Document every kludge and its reason** (see the Workarounds inventory in §4 — to be kept complete as
  more are found; this is an explicit goal, not incidental).
- **Large-file support is non-negotiable for any embedding alternative** (see Q7).

1. **Dock identity + protected spine:** give every dock a stable, unique id (assign to drag-created docks
   too). Keep the well-known anchors, but don't let lookups depend on a dock staying in a fixed
   *position*. **Decision (Frank): the invariant spine `Root → WindowLayout → MainDock → MainDocumentDock`
   (+ Welcome doc) must be protected from cleanup.** Centralize an `IsProtectedSpine(dock)` predicate (the
   well-known spine ids) honored by `IsEmptyDock`/`FindEmptySplits`/`RemoveEmptySplit`, replacing the
   scattered per-id skips (`LeftTools`, `MainDock`). `MainDock` survives today only incidentally (RootDock
   parent) — make it robust. The spine keeps **fixed** well-known ids; only **variable** parts (tool
   docks, drag splits, floating windows) get unique ids + recreate-on-demand. Because the spine is
   guaranteed, tool-panel restore (Q2) always has a known anchor (`MainDock`) to rebuild `LeftTools` +
   `LeftToolDock` under.
   **Root cause of empty-id docks (confirmed):** `SplitToDock` override delegates to `base.SplitToDock`
   ([945](src/CST.Avalonia/Services/CstDockFactory.cs#L945)), and the framework builds the split
   container/tool host via `CreateProportionalDock()` / `CreateToolDock()` — which the factory **does not
   override**, and the one override it has (`CreateDocumentDock()` [1550](src/CST.Avalonia/Services/CstDockFactory.cs#L1550))
   **omits the id too**. So every framework-created dock is born id-less; only the hand-built `CreateLayout`
   docks have ids. **Fix (small, surgical):** override `CreateProportionalDock()` + `CreateToolDock()` and
   add an id in `CreateDocumentDock()` to stamp a stable unique id (GUID) at creation — then no dock is
   ever anonymous. **Framing (Frank):** the framework *creating* a `ProportionalDock` per split (and
   collapsing single-child redundancy) is **normal, expected** Dock behavior — **not** a problem in
   itself; the defect is purely the **missing id**. So id-stamping is sufficient for #3. The
   panels-can't-return bug (#4) is **separate**: once a container is legitimately removed there's no tool
   dock to add to *regardless of ids*, so it needs **recreate-on-demand** (Q2). And since `MainDock` can
   legitimately collapse, anchor that recreate off the truly-robust **`MainDocumentDock`** — which may make
   explicitly protecting `MainDock` unnecessary.
2. **Tool-panel restore:** key off the **tool identity** (the singleton VMs), not a positional container
   id. Recreate a tool dock (default "home", left) if none exists; never no-op. Decide **default-home (A)**
   vs **last-position (B)** — leaning A, reinforced by the layout-restore pitfalls (don't replay
   environment-dependent geometry blindly). Prefer **recreate-on-demand** (rebuild the tool dock/home
   when a panel is shown and none exists) rather than a persistent keep-alive — the ever-present
   `WelcomeDocument` is a *workaround* we'd rather retire, not a pattern to copy. A robust overhaul lets
   empty docks come and go cleanly.
3. **Re-parent safety (the central fix):** the clean `BeginReparenting` API is paywalled (§4), so the
   free path is to make *every* drag do **dispose-before-move + recreate**, like the buttons. The native
   drags that currently move a live browser: **drag-to-split** (main), **drag-unfloat** (floated→main),
   and **floated→floated combine** (grouping — an **intended feature to keep**, so blunt `CanDrag=false`
   is out). **Most promising unifying fix:** turn the existing drag-start detection (which today *hides*
   WebViews) into **dispose-at-drag-start + recreate-on-drop** — one change that also subsumes the
   airspace hide workaround; trigger via `OnMoveDragBegin`/`IsDraggingDock`, and handle cancelled drags
   and same-window reorder. **Targeted alternative:** a custom `IDockManager.ValidateDockable` (confirmed
   available; `DockControl.DockManager` is settable) can reject a specific drag before it executes — e.g.
   block drag-unfloat into the main window while still allowing the combine — or **redirect** it through
   `UnfloatDockableWithoutRecycling` so the drag still does what the user expects via the safe path.
4. **Decompose `CstDockFactory`** into focused services (layout construction, document lifecycle,
   floating-window management, cleanup/proportions, persistence).
5. **State desyncs:** derive `IsFloating` (and similar) from the actual dock location rather than setting
   it only in the button paths; re-apply book script/title on any VM recreation.
6. **Restore robustness:** continue validating any restored geometry/layout against the current
   environment; prefer reconstructing sane defaults over faithful replay.
7. **Embedding-level decision (to *remove* the recreate UX cost, not just the crash) — a strategic fork
   separate from dock stabilization:** all *free* embeddings hit the same macOS CEF reparent wall
   (`WebView2.Avalonia` → CefGlue on macOS; CefSharp Windows-only — see
   [`BROWSER_EMBEDDING_OPTIONS.md`](../research/BROWSER_EMBEDDING_OPTIONS.md)). Options that keep a **live**
   browser across windows (exact scroll/selection, no flicker): **(a) Avalonia Accelerate native WebView
   + `BeginReparenting` — paid**; **(b) CEF offscreen/windowless rendering — free but heavy** (no native
   handle to reparent; render-pipeline + input-forwarding work, flagged "months / not feasible" in 2025 —
   worth re-checking against current CefGlue OSR support). The manual dispose-recreate lifecycle is the
   free middle path and carries the §4 cost. Dock stabilization (1–6) is worth doing regardless; this
   fork decides whether the recreate cost is ever truly eliminated. **Hard gate:** *some embedded-browser
   options cannot render the largest CST books* — a major reason CefGlue/CEF was chosen — so **any
   alternative must be validated against the largest books first**. This may rule out the native-engine
   options (WKWebView/WebView2) regardless of cost, which would leave **CEF OSR** as the only path that
   removes the cost while keeping large-file support.
   **Prior research** ([`BROWSER_EMBEDDING_OPTIONS.md`](../research/BROWSER_EMBEDDING_OPTIONS.md),
   [`ALTERNATIVE_RENDERING_ENGINES.md`](../research/ALTERNATIVE_RENDERING_ENGINES.md), `RENDERING_REQUIREMENTS.md`)
   scored 9 alternatives against 18 requirements. The large-file gate is concretely **~3.6 MB HTML** (the
   largest book) — it eliminates `Avalonia.HtmlRenderer` and `RichTextBox`, and makes `TextBlock+Inlines`
   doubtful (perf + *no text selection*: `SelectableTextBlock` can't host `Inlines`). Two cautions from
   that research: (i) even the **paid** Accelerate WebView's reparent/ControlRecycling compatibility is
   **unverified** — it needs an Avalonia-team answer / POC against the 3.6 MB book before it can be
   assumed to fix anything; (ii) the cost-free *long-term* escape they favor is a **custom native Avalonia
   rendering engine** (3–6 mo; perfect recycling, ~20 MB; text selection across the 14 scripts is the hard
   part) — a third option alongside CEF-OSR and the paid native WebView.
