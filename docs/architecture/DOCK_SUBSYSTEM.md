# Dock Subsystem — Current Architecture

**Status:** Current-behaviour map, and the foundation for the planned dock-stabilization overhaul.

**Last reconciled against the code: 2026-08-29** (every assertion below was re-checked; line numbers are
from that date and drift — treat them as hints, not addresses). Superseded material is deleted rather than
annotated: git is the history.

**Why this exists:** the dock subsystem (Dock.Avalonia + `CstDockFactory` + `LayoutViewModel` + the WebView
views) is the most fragile part of the app, because it is where a native CEF surface meets a docking
framework that re-parents controls. The docking UI itself is a non-negotiable must-have.

---

## 1. Layout object model

Built once in `CstDockFactory.CreateLayout()` ([87](../../src/CST.Avalonia/Services/CstDockFactory.cs#L87)):

```
Root (RootDock, Id="Root")
└─ WindowLayout (RootDock, Id="WindowLayout")
   └─ MainDock (ProportionalDock, Horizontal, Id="MainDock")
      ├─ LeftTools (ProportionalDock, Id="LeftTools", Proportion 0.25)
      │  └─ LeftToolDock (ToolDock, Id="LeftToolDock", Alignment.Left)
      │     ├─ OpenBookDialogViewModel   (tool: "Open a Book" tree)
      │     ├─ SearchViewModel           (tool: Search panel)
      │     └─ DictionaryViewModel       (tool: Dictionary — hosts a CEF WebView, #466)
      ├─ MainSplitter (ProportionalDockSplitter)
      ├─ MainDocumentDock (DocumentDock, Proportion 1.0 − 0.25 − 0.18)
      │  └─ WelcomeViewModel (ReactiveDocument, Id="WelcomeDocument") + book/PDF documents
      ├─ RightSplitter (ProportionalDockSplitter)   ┐ present only when the
      └─ RightTools (ProportionalDock, Prop 0.18)   │ Assistant is enabled
         └─ RightToolDock (ToolDock, Alignment.Right)
            └─ AiAssistantViewModel
```

- Tools and documents **are** the ViewModels (ReactiveTool / ReactiveDocument) — no wrapper objects.
- **The right column is conditional.** When the Assistant is off, `MainDock` is built with three children
  (`leftTools, splitter, documentDock`) and neither `RightSplitter` nor `RightTools` exists
  ([335](../../src/CST.Avalonia/Services/CstDockFactory.cs#L335)). Anything walking `MainDock`'s children
  must tolerate both shapes.
- **The Assistant is on the right deliberately** (#586): a generated answer is prose to be read *alongside*
  the passage, and as a fourth tab in the left rail it both got the narrowest column and cost the reader
  whichever of Open Book, Search or Dictionary they were using.
- **`WelcomeDocument` is a workaround, not a design preference.** Permanent and non-closeable
  (`CanClose = false`, `WelcomeViewModel.cs:93`) to stop `MainDocumentDock` from going empty and being
  collapsed by cleanup — not because an always-present welcome page is wanted. See §7 Q3: now that the spine
  is explicitly protected, this keep-alive may be retirable.
- Floating windows are `CstHostWindow`s tracked in `CstDockFactory.HostWindows`
  ([401](../../src/CST.Avalonia/Services/CstDockFactory.cs#L401)); each has its own `Layout` — an
  independent dock tree with its own `DocumentDock`. A floating window can hold **multiple books**, and
  dragging one floated window's tab onto another combines them: an **intended grouping feature to preserve**.

---

## 2. Dock identity

Three id conventions coexist, and all three are now deliberate:

| Kind | Examples | Source |
|---|---|---|
| **Fixed well-known** | `Root`, `WindowLayout`, `MainDock`, `LeftTools`, `LeftToolDock`, `MainDocumentDock`, `MainSplitter`, `RightTools`, `RightToolDock`, `RightSplitter`, `WelcomeDocument` | Hardcoded in `CreateLayout` |
| **GUID-based** | book documents (auto GUID), search opens `Search_{file}_{guid:N}`, window ids | Per-instance |
| **Stamped GUID** | `PDock_{guid}`, `ToolDock_{guid}`, `DocDock_{guid}`, `RootDock_{guid}` | Framework-created docks, stamped by the factory's `Create*` overrides |

**Framework-created docks used to be born with an empty id**, which broke every fixed-id lookup and let
anonymous structures accumulate and nest during drags. That is fixed: `CreateProportionalDock`
([1581](../../src/CST.Avalonia/Services/CstDockFactory.cs#L1581)), `CreateToolDock`
([1591](../../src/CST.Avalonia/Services/CstDockFactory.cs#L1591)), `CreateDocumentDock`
([1563](../../src/CST.Avalonia/Services/CstDockFactory.cs#L1563)) and `CreateRootDock`
([1599](../../src/CST.Avalonia/Services/CstDockFactory.cs#L1599)) each stamp a unique id when the framework
leaves one empty. **No dock is anonymous any more.** Keep it that way: a new `Create*` override that forgets
to stamp reintroduces the whole class.

Note that the framework *creating* a `ProportionalDock` per split, and collapsing single-child redundancy,
is normal Dock behaviour and not a defect in itself. The defect was only ever the missing id.

### The protected spine

The invariant spine is `Root → WindowLayout → MainDock → MainDocumentDock`, registered in `CreateLayout`
([363](../../src/CST.Avalonia/Services/CstDockFactory.cs#L363)) and honoured by `IsProtectedSpine`
([2834](../../src/CST.Avalonia/Services/CstDockFactory.cs#L2834)), which `IsEmptyDock`, `FindEmptySplits`
and `RemoveEmptySplit` all consult. Spine docks are never treated as empty or redundant, so they survive
cleanup even when single-child.

**Matched by reference, not by id** — deliberately. The framework can clone a dock and copy its id
(document-area splits produce several docks carrying `MainDocumentDock`'s id), and those clones must *not*
be protected. Only the four original instances are.

**The tool columns are not spine, and that is the design.** Both `LeftTools`/`LeftToolDock` and
`RightTools`/`RightToolDock` are variable parts: they may legitimately be emptied, floated out, or
collapsed, and they come back through recreate-on-demand (`EnsureLeftToolDock`
[1654](../../src/CST.Avalonia/Services/CstDockFactory.cs#L1654), `EnsureRightToolDock`
[1736](../../src/CST.Avalonia/Services/CstDockFactory.cs#L1736)) rather than by being pinned in place. The
Assistant column was added under this rule and required no change to the spine.

---

## 3. Component responsibilities

- **`CstDockFactory.cs` (~3760 lines)** — does almost everything: builds the layout; opens books/PDFs;
  close/remove; the dispose-before-move overrides (`SplitToWindow`, `SplitToDock`, `SwapDockable`,
  `MoveDockable`); `CleanupEmptySplits`; proportion capture/restore; floating-window lifecycle; the
  document collection-changed handler; `_goToSubscribedBooks`; application-state save/restore of book
  windows. A single-responsibility violation and the prime candidate for extraction (§7 Q1) — it has grown
  by about a third since that was first written.
- **`LayoutViewModel.cs`** — View-menu panel show/hide/toggle, `FindTool`/`FindToolDock`/`FindParentDock`,
  and tool removal (which now defers to the factory's standard cleanup rather than special-casing
  `LeftToolDock`).
- **`BookDisplayView.axaml.cs` (~4670 lines)** — the per-document View hosting the CEF `WebView`: WebView
  lifecycle, scroll/anchor cache, find bar, search-highlight navigation.
- **`SimpleTabbedWindow.cs`** — main window; global dock-drag detection that hides all WebViews during a
  drag; window geometry save/restore; the window-scoped commands (zoom, find, go-to).

---

## 4. CEF WebView lifecycle (the crash-critical part)

**The rule the whole subsystem is built around: never carry a *live* CEF WebView across a re-parent.**

**Mechanism** (see [`CONTROL_RECYCLING_CEF_CRASH.md`](../implementation/CONTROL_RECYCLING_CEF_CRASH.md)):
on macOS CEF binds its native handle to the **creating window** and aggressively disposes the browser when
the NSView detaches from that window; a later re-attach dereferences the dead child handle → SIGSEGV in
`AvnNativeControlHostTopLevelAttachment::InitializeWithChildHandle`. CEF lifecycle *is* View lifecycle. The
one clean fix (`NativeWebView.BeginReparenting`) ships only in the paid **Avalonia Accelerate** — off the
table for open-source CST — which bounds us to the free manual **dispose-before-move + recreate**
lifecycle. ControlRecycling stays **enabled** (`App.axaml:78`) for instant same-window tab switching.

**The cost of recreate, which is why it must stay selective:** every dispose+recreate reloads the book and
loses precise state — scroll lands *near* (anchor-based) rather than exact, text selection is lost, in-page
JS state resets, plus a flicker. So it is paid only when a move genuinely **crosses a window**, never for
same-window splits, reorders or tab switches.

### The dispose-before-move funnel

Every path that can move a CEF-hosting dockable into a different window disposes and evicts its View first,
letting the framework build a fresh browser at the destination:

- **`SplitToWindow`** ([2045](../../src/CST.Avalonia/Services/CstDockFactory.cs#L2045)) — every
  float-creation trigger passes through here before the View detaches: release over empty space, a drop on
  an invalid target, the "float" drop-indicator, tab double-click, and the tab context menu's Float. The
  view model is kept (no fresh-GUID recreate); reading position rides the queued #434 token.
- **The cross-dock `MoveDockable` / `SwapDockable` 4-arg overloads and cross-window `SplitToDock`**
  ([1964](../../src/CST.Avalonia/Services/CstDockFactory.cs#L1964),
  [1984](../../src/CST.Avalonia/Services/CstDockFactory.cs#L1984)) — same treatment for a drag between
  existing windows. Same-window tab moves are skipped, so instant tab switching survives.
- **`FloatAllDockables` is suppressed outright** — it would re-parent several live browsers at once and
  sweep in the non-floatable Welcome tab. Nothing in the app needs it.

This applies to `BookDisplayViewModel`, `PdfDisplayViewModel` **and `DictionaryViewModel`** — since #466 the
dictionary is a CEF-hosting *tool*, so the hazard is no longer confined to documents.

`BookDisplayView` carries a `_browserBirthWindow` invariant check
([567](../../src/CST.Avalonia/Views/BookDisplayView.axaml.cs#L567)) that logs an Error if a live browser
ever re-attaches to a different window — the early warning that some new re-parent path is missing the
guard, which is better than a SIGSEGV.

### View-side lifecycle

- `OnAttachedToVisualTree` ([550](../../src/CST.Avalonia/Views/BookDisplayView.axaml.cs#L550)) — branches by
  **reference equality**: a different non-null `_currentWindow` → dispose + recreate + reload; `null` →
  first attach or post-detach reattach, just track the window; same instance → ControlRecycling tab switch,
  no recreate.
- `OnDetachedFromVisualTree` ([655](../../src/CST.Avalonia/Views/BookDisplayView.axaml.cs#L655)) — nulls
  `_currentWindow`. Because detach nulls it, *which* path takes which branch is subtle and has been
  iteratively patched; the funnel above is what actually guarantees safety, not this branching.
- `WebViewLifecycleOperation` ([`BookDisplayViewModel.cs:2276`](../../src/CST.Avalonia/ViewModels/BookDisplayViewModel.cs#L2276))
  — **dormant.** Nothing sets its four float states; it is scaffolding retained for #419. See #896.
- **Drag-time airspace hide** (`SimpleTabbedWindow`, `DRAG_DETECTION_THRESHOLD = 150` ms): a timer watches
  `DockControl.IsDraggingDock` and, past the threshold, sets `IsVisible = false` on every WebView in every
  window, restoring shortly after the drag ends. This is a workaround for the native-WebView **airspace**
  problem — the CEF surface renders on top and would otherwise occlude Dock's drop indicators. It only
  hides; it does **not** dispose, so a live browser still exists through the drop.

### Workarounds inventory

Each is a point-fix for a CEF ↔ Dock.Avalonia ↔ Avalonia-NativeControlHost interaction. The overhaul should
**consciously replace** them, not add another. The complete inventory is in
[`DOCK_WEBVIEW_WORKAROUNDS.md`](DOCK_WEBVIEW_WORKAROUNDS.md); the headline ones:

1. **Ever-present, non-closeable Welcome page** — keeps `MainDocumentDock` from going empty (§1, §7 Q3).
2. **Dispose + recreate the WebView on window-context change** — avoids CEF handle corruption across
   ControlRecycling.
3. **Dispose-before-move funnel on every cross-window path** — the mechanism that makes dragging and
   floating safe.
4. **`FloatAllDockables` suppressed** — blocks a multi-browser re-parent rather than trying to survive it.
5. **Hide all WebViews during a drag** — the airspace workaround; does not dispose.

---

## 5. Operation flows (entry points)

- **Open book:** `OpenBook` ([425](../../src/CST.Avalonia/Services/CstDockFactory.cs#L425)) /
  `OpenBookInNewTab` ([466](../../src/CST.Avalonia/Services/CstDockFactory.cs#L466)) / `OpenPdf`
  ([667](../../src/CST.Avalonia/Services/CstDockFactory.cs#L667)) → `AddDocumentToLayout`
  ([1065](../../src/CST.Avalonia/Services/CstDockFactory.cs#L1065)). Sets `CanDrag = true` and leaves
  `CanFloat = true`; subscribes events; adds to `_goToSubscribedBooks`; captures/restores MainDock
  proportions around the add.
- **Float (drag):** `SplitToWindow` ([2045](../../src/CST.Avalonia/Services/CstDockFactory.cs#L2045)) —
  see §4.
- **Close:** `CloseDockable` ([1881](../../src/CST.Avalonia/Services/CstDockFactory.cs#L1881)) →
  `RemoveBookWindowState` → base → `vm.Dispose()` + `_goToSubscribedBooks.Remove` → `CleanupEmptySplits`.
- **Drag split/move/swap:** `SplitToDock` ([1172](../../src/CST.Avalonia/Services/CstDockFactory.cs#L1172),
  which also prevents tools tab-docking into the DocumentDock),
  `SwapDockable`/`MoveDockable` ([1964](../../src/CST.Avalonia/Services/CstDockFactory.cs#L1964)) →
  `CleanupEmptySplits`.
- **Cleanup:** `CleanupEmptySplits` ([2609](../../src/CST.Avalonia/Services/CstDockFactory.cs#L2609)) →
  `FindEmptySplits` / `IsEmptyDock` / `RemoveEmptySplit` / `CleanupSplitters`, all of which spare the
  protected spine.
- **Panel show/hide:** `LayoutViewModel.ShowSearchPanel` / `ShowSelectBookPanel` find the tool by id, else
  create it; the container itself is rebuilt on demand by `EnsureLeftToolDock` / `EnsureRightToolDock`, so a
  removed tool column is always recoverable without a restart.
- **Save/restore:** book windows + window geometry persisted to `ApplicationState`
  (`SaveAllBookWindowStatesAsync` [710](../../src/CST.Avalonia/Services/CstDockFactory.cs#L710),
  `SaveBookWindowState` [840](../../src/CST.Avalonia/Services/CstDockFactory.cs#L840)). **The dock
  split-structure itself is not serialized** — only which books are open and where their windows sit.
  Window geometry restore validates against connected screens.

---

## 6. Known failure modes

1. **`RightTools` is not excluded from the redundant-single-child collapse, but `LeftTools` is.** The two
   wrappers are structurally identical — a `ProportionalDock` holding exactly one `ToolDock`, existing to
   own a proportion — yet the collapse checks skip only `LeftTools`, by id
   ([2741](../../src/CST.Avalonia/Services/CstDockFactory.cs#L2741),
   [2900](../../src/CST.Avalonia/Services/CstDockFactory.cs#L2900)). The proportion code reads either shape
   (`"RightTools" ?? "RightToolDock"`, [3200](../../src/CST.Avalonia/Services/CstDockFactory.cs#L3200)), so
   a collapsed right column still gets its width — which is why this is not visibly broken. Whether the
   asymmetry is intended has not been established.
2. **A floated book's tab title reverting to Devanāgarī** was caused by a recreated view model's title not
   being re-applied in the current script. Float now keeps the same view model, so the stated cause is
   gone; whether the symptom is gone has not been re-tested.
3. **Fixed-id fragility, residually:** id-stamping and the protected spine removed most of it, but lookups
   like `FindDockByIdRecursive(root, "MainDock")` still assume a well-known dock is present and in a sane
   place. A degraded structure will still misfire proportion capture/restore.

---

## 7. Open design questions for the overhaul

**Standing constraints:**
- **No known crash may ship.**
- **Minimum re-creations — only where needed.** Recreation carries the §4 UX cost, so it is paid only on a
  genuine cross-window move, never for same-window splits, reorders or tab switches.
- **Document every kludge and its reason** — an explicit goal, not incidental.
- **Large-file support is non-negotiable for any embedding alternative** (Q4).

1. **Decompose `CstDockFactory`** into focused services: layout construction, document lifecycle,
   floating-window management, cleanup/proportions, persistence. At ~3760 lines it is the largest single
   obstacle to reasoning about any of the above.
2. **Restore robustness:** keep validating restored geometry and layout against the current environment;
   prefer reconstructing sane defaults over faithful replay.
3. **Can the Welcome keep-alive be retired?** It exists solely to stop `MainDocumentDock` collapsing, and
   `MainDocumentDock` is now explicitly protected from cleanup by reference. If the protection is
   sufficient, `WelcomeDocument` could become closeable like any other tab — which is what was always
   wanted. Needs verifying against an emptied document dock, not assumed.
4. **Embedding-level decision — a strategic fork, separate from dock stabilization.** This is about
   *removing* the recreate UX cost, not the crash. All *free* embeddings hit the same macOS CEF reparent
   wall (`WebView2.Avalonia` → CefGlue on macOS; CefSharp is Windows-only — see
   [`BROWSER_EMBEDDING_OPTIONS.md`](../research/BROWSER_EMBEDDING_OPTIONS.md)). Options that keep a **live**
   browser across windows (exact scroll and selection, no flicker): **(a) Avalonia Accelerate native WebView
   + `BeginReparenting` — paid**; **(b) CEF offscreen/windowless rendering — free but heavy** (no native
   handle to reparent; render-pipeline and input-forwarding work, flagged "months / not feasible" in 2025 —
   worth re-checking against current CefGlue OSR support).

   **Hard gate:** *some embedded-browser options cannot render the largest CST books* — a major reason CEF
   was chosen — so **any alternative must be validated against the largest books first**. The gate is
   concretely **~3.6 MB of HTML**. It eliminates `Avalonia.HtmlRenderer` and `RichTextBox`, and makes
   `TextBlock + Inlines` doubtful (performance, and *no text selection*: `SelectableTextBlock` cannot host
   `Inlines`). Two cautions from that research: even the **paid** Accelerate WebView's reparent and
   ControlRecycling compatibility is **unverified** and needs a POC against the 3.6 MB book before it can be
   assumed to fix anything; and the cost-free long-term escape favoured there is a **custom native Avalonia
   rendering engine** (3–6 months; perfect recycling, ~20 MB; text selection across the 14 scripts is the
   hard part).
