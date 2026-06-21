# Dock / WebView / CEF Workarounds — Complete Inventory

**Status:** Companion to [`DOCK_SUBSYSTEM.md`](DOCK_SUBSYSTEM.md). This is the "**document every kludge and
its reason**" reference (an explicit Beta 4 goal). Each entry is a place where the code does something
*other than clean domain logic* to work around a CEF / WebViewControl-Avalonia / Avalonia
NativeControlHost / Dock.Avalonia limitation or a timing/race.

Compiled from a June 2026 agent sweep of `CstDockFactory.cs`, `LayoutViewModel.cs`,
`BookDisplayView.axaml.cs`, `SimpleTabbedWindow.cs`, `BookDisplayViewModel.cs`, `PdfDisplayView(.Model).cs`,
`CstHostWindow.cs`, `App.axaml(.cs)`, and `package-macos.sh`. **Line numbers are approximate (as-of
June 2026) and will drift — spot-verify before relying on one.** Treat this as a backlog: each item is a
candidate to *replace* (not re-add) in the overhaul.

> **Why so many?** ~70% of these exist for one root fact: **CEF binds its native handle to the creating
> window and cannot be re-parented across windows on macOS** (see [`CONTROL_RECYCLING_CEF_CRASH.md`](../implementation/CONTROL_RECYCLING_CEF_CRASH.md)).
> ControlRecycling (kept for instant tab switching) compounds it by reusing Views. The rest are
> Dock.Avalonia structural gaps, async/render timing, and macOS CEF packaging.

---

## A. CEF WebView lifecycle (dispose/recreate)

| What | Where | Works around / why |
|---|---|---|
| `TryCreateWebView()` / `DisposeWebView()` manual cycle | BookDisplayView ~160–215 | CEF native handle invalid after a window-context change; must dispose then recreate. |
| Window-change detection by **reference equality** (3 branches) → dispose+recreate+reload only on real window change | BookDisplayView ~229–303 | Distinguish float/unfloat (recreate) from same-window tab switch (keep, instant). |
| `OnDetachedFromVisualTree` nulls `_currentWindow` ("CRITICAL FIX") | BookDisplayView ~305–321 | Force window-change detection on a later ControlRecycling reattach; fixed float→unfloat→tab-switch→tab-back crash. |
| `WebViewLifecycleOperation` state machine (`PrepareForFloat`/`RestoreAfterFloat`/`…Unfloat`) → dispose **before** move, recreate **after** | BookDisplayView ~374–412 | Button float/unfloat = controlled dispose-before-move; the only crash-reliable float path. |
| `LoadHtmlContent` writes HTML to a **temp file** + `LoadUrl(fileUrl)` instead of a data URI | BookDisplayView ~441–516 | CEF data-URI size limits — the largest books (~3.6 MB) exceed them. |
| PDF tabs mirror the same dispose/recreate + lifecycle-op handling | PdfDisplayView ~31–177 | Same CEF reparent constraint for the PDFium WebView. |

## B. ControlRecycling-specific

| What | Where | Works around / why |
|---|---|---|
| ControlRecycling **enabled** | App.axaml ~43–56 | Preserves scroll/state on same-window tab switch (instant switching). It's the *reason* most CEF kludges exist. |
| **Unique GUID per BookDisplayViewModel** | BookDisplayViewModel ~126–138 | ControlRecycling caches Views by VM id; duplicate ids → reused View → CEF crash. |
| `FloatDockableWithoutRecycling` / `UnfloatDockableWithoutRecycling` — create a **fresh VM (new GUID)** at the destination, dispose the old | CstDockFactory ~1295–1538 | Bypass ControlRecycling so a fresh View (fresh browser) is built instead of recycling stale CEF baggage. |
| Local drag monitoring **disabled** in BookDisplayView (consolidated to SimpleTabbedWindow) | BookDisplayView ~2329–2335 | Duplicate hide/show from two monitors invalidates CEF handles (the `InitializeWithChildHandle` crash). |

## C. Drag / airspace

| What | Where | Works around / why |
|---|---|---|
| Hide **all** WebViews across all windows during a drag, restore after | SimpleTabbedWindow ~518–583 | Native CEF surface (airspace) occludes Dock's drop indicators; hide reveals them. (Hide ≠ dispose — browser stays live.) |
| Drag state machine: 50 ms poll, **150 ms** drag threshold, **100 ms** min-hide, 10 s fallback restore | SimpleTabbedWindow ~33–41, 432–516 | Filter flickering `IsDraggingDock`; prevent WebView flashing; recover if a drag hangs. |
| Cross-window `DragEnter`/`Drop`/`DragLeave` → hide/restore WebViews | SimpleTabbedWindow ~61–99 | Event-based path for explicit drag-drop (complements the timer). |
| `CanDrag=true`, `CanFloat=false` on books | CstDockFactory ~511–517 | Allow tab reorder; block drag-to-float (the live-reparent crash) → force the float button. |

## D. Dock structure / Dock.Avalonia gaps

| What | Where | Works around / why |
|---|---|---|
| **Permanent, non-closeable Welcome document** | CstDockFactory ~83–96 | Keeps `MainDocumentDock` non-empty so Dock doesn't collapse/remove it. (A workaround, not a desired feature.) |
| Tool/ToolDock dropped into DocumentDock → async remove (`Task.Delay(50)`) + float instead | CstDockFactory ~131–174 | Dock's center-drop bypasses `SplitToDock` and lets tools tab-dock into documents. |
| `SplitToDock` override blocks tool Fill-into-DocumentDock; forces 50/50 elsewhere | CstDockFactory ~910–975 | Same — prevent tools tab-docking; tame re-proportioning. |
| **Sync** cleanup on remove **+** double async (`Dispatcher.Post` Render+Background) | CstDockFactory ~158–178, 965–975 | Empty dock areas flash before cleanup; sync prevents the flash, async catches misses under load. |
| `CleanupEmptySplits` capped at 10 iterations | CstDockFactory ~1962–2021 | Empty-split removal can cascade/loop on degraded structures. |
| Fixed-id special-casing: find left dock by `"LeftTools"` then `"LeftToolDock"`; spare `"LeftTools"` in `IsEmptyDock`; skip `"MainDock"` in `SetEqualProportions` | CstDockFactory ~1132–1136, 2210, 2487–2557 | Structure/ids change over lifecycle; lookups assume fixed ids (the core ID-fragility). |
| MainDock 25/75 proportion **capture before add + triple-Dispatcher restore** (Render/Background/Loaded) | CstDockFactory ~436–446, 2471–2601 | Dock recomputes proportions on add (resets toward 50/50); restore after all layout passes. |
| `SetFactory` recurses the whole tree setting `.Factory = this` | CstDockFactory ~250–287 | Dock requires Factory on every dockable, incl. freshly created ones. |
| `.ToList()` snapshot of `VisibleDockables` before async iteration | CstDockFactory ~623 | Concurrent drag/cleanup mutates the collection mid-iterate ("Collection was modified"). |
| Save state on `ActiveDockable` change (tab switch) | CstDockFactory ~193–203 | Otherwise non-active tabs' state is lost if app closes right after a switch. |

## E. Floating-window management

| What | Where | Works around / why |
|---|---|---|
| `FindTool` / state-restore **search main + all floating windows** | LayoutViewModel ~402–427 | Tools/books can live in floating windows; main-only lookup misses them. |
| `RemoveToolFromLayout` finds owning floating window + closes it when empty; `IsLayoutEmpty` recursion | LayoutViewModel ~301–367, 475–501 | Need to find which window owns a now-empty dock and close it. (This is what removes `LeftToolDock` → panel-restore no-op bug.) |
| Unfloat cleans up **only the source** floating window, NOT all ("DO NOT check all — was causing other floating windows to disappear") | CstDockFactory ~1508–1528 | Global empty-check closed windows whose document refs were transiently null. |
| Float wrapped in try-catch; on failure re-add to main dock | CstDockFactory ~1245–1285 | Partial float (removed from source, window-create failed) would orphan the document. |
| Remove book-window **state before** removing the dockable; capture source window **before** removal | CstDockFactory ~1330–1332, 1455–1466 | Removal fires collection-changed handlers; ordering avoids double-cleanup / lost references. |
| `CreateWindowFrom` try-catch → basic `CreateCstHostWindow` fallback | CstDockFactory ~1769–1801 | Host-window customization can fail; a basic window beats a crash. |
| Deferred empty floating-window auto-close via collection-changed handler | CstDockFactory ~1848–1854 | Can't close a window synchronously during collection modification. |

## F. Async / render timing

| What | Where | Works around / why |
|---|---|---|
| Pervasive `Task.Delay` chains (e.g. 2000 ms post-nav, 500 ms cache, 300 ms hit-nav, 200 ms scroll, 100 ms) + `Dispatcher.Post` | BookDisplayView & BookDisplayViewModel (many) | CEF JS execution + DOM settle + Avalonia WebView init are async/unpredictable; delays wait for "ready." |
| Static `_jsExecutionLock` semaphore serializing JS across all tabs; non-blocking `WaitAsync(0)` / 10 ms tries | BookDisplayView ~27, 757–1070 | CEF thread contention when multiple tabs run JS concurrently. |
| 200 ms scroll-position polling timer; immediate `CaptureCurrentPositionAsync` before shutdown | BookDisplayView ~545–607; BookDisplayViewModel ~407–418 | Poll catches missed scroll updates; timer is lazy so capture latest before quit. |
| `TaskCompletionSource` bridging a JS callback (via `document.title`) to async/await, 1 s timeout | BookDisplayView ~2144–2225 | Avoids UI-thread deadlock doing synchronous JS. |
| Anchor cache invalidated on content load ("prevents scroll timer querying stale cache") | BookDisplayView ~471–474 | Timer race overwrites freshly-loaded scroll position with stale cached anchor. |

## G. JavaScript interop / keyboard / focus

| What | Where | Works around / why |
|---|---|---|
| `document.title` used as a JS→C# channel (logging, page/anchor/scroll status, keyboard relay), with `\|TAB:{id}` filtering | BookDisplayView ~623–747, 1453–1773 | No clean JS→C# event channel; title-change is the available signal; tab-id prevents cross-talk. |
| `cstKeyboardCapture` JS intercepts Cmd+C/A/E, Shift+Cmd+E and relays to C# | BookDisplayView ~99–157, 1470–1541 | CEF swallows some keyboard shortcuts before Avalonia sees them. |
| Staggered `setTimeout` init (50/100/200 ms) | BookDisplayView ~1735–1745 | Ensure DOM/content settled before highlight/chapter/keyboard init. |
| `getBoundingClientRect()` + `window.pageYOffset` absolute-positioning ("this is a workaround…") | BookDisplayView ~770–856 | rect is viewport-relative; need absolute positions for the anchor cache. |

## H. Duplicate-open guards

| What | Where | Works around / why |
|---|---|---|
| Time-based locks: 2 s (search opens), 1 s (regular), skipped for state restore (`windowId == null`) | CstDockFactory ~338–362, 457–487 | Rapid double-click / event re-fire opens the same book multiple times. |

## I. CEF init & macOS packaging

| What | Where | Works around / why |
|---|---|---|
| `use-mock-keychain` + `disable-password-generation` switches | App.axaml.cs ~92–97 | Stop Chromium Safe Storage from triggering a macOS keychain prompt at startup. |
| `browser-subprocess-path` set to helper bundle when packaged | App.axaml.cs ~99–127 | CEF must find GPU/Plugin/Renderer helper processes in the `.app` Frameworks dir (else silent hang). |
| `PersistCache = false` but a temp path still provided | App.axaml.cs ~130–132 | In-memory cache; WebViewLoader still requires a path argument. |
| 4 helper app bundles + shell-script launchers + `CefGlueBrowserProcess` kept in place | package-macos.sh ~82–157 | CEF needs 4 helper processes as proper bundles; .NET subprocess needs its own dir for deps/runtimeconfig. |
| Sign `CefGlueBrowserProcess` with hardened runtime + entitlements; JIT / unsigned-memory / disable-library-validation / network.client | package-macos.sh ~213–263 | macOS notarization + .NET runtime (JIT) + network all require specific entitlements; silent failure otherwise. |

## J. State restore / lifecycle / misc

| What | Where | Works around / why |
|---|---|---|
| Suppress `StateChanged` during restoration; defer book restore with delays | App.axaml.cs ~566–710 | Restoration re-fires state events → feedback loops / duplicate books; double-restore (dock + ApplicationState) bug. |
| Global ReactiveUI exception handler (log, don't crash) | App.axaml.cs ~1275–1281 | ReactiveUI unhandled exceptions would crash Avalonia. |
| Window-geometry restore validated against connected screens; 500 ms debounced save | SimpleTabbedWindow ~272–378 | CST4 lesson — off-screen restore on a disconnected monitor; debounce avoids resize I/O storm. |
| FontService subscription via a **named** handler (not lambda) so `Dispose` can unsubscribe | BookDisplayViewModel ~286–290 | Singleton FontService would otherwise root every (recreated) VM forever — a per-tab leak. |

---

## Cross-cutting observations

- **CEF is the dominant cause** (~70%): reparent-impossible + ControlRecycling reuse.
- **`document.title` is overloaded** as the all-purpose JS→C# channel — fragile and surprising.
- **Fixed dock ids** are assumed throughout (lookups, cleanup spares, proportion keys) — the central
  ID-fragility the overhaul must fix.
- **`Task.Delay`/multi-priority `Dispatcher.Post` everywhere** signals the WebView/JS readiness model is
  effectively "wait and hope" — replacing CEF (or adding real ready-events) would remove a whole class.
- The drag/airspace hide and the dispose/recreate lifecycle are **two halves of the same problem**; a
  dispose-at-drag-start could merge them (see `DOCK_SUBSYSTEM.md` §4 / Q3).
