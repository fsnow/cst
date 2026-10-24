# #827 — Reading the assistant's selection without the 4096-character title cap

**Status:** plan, revised after two adversarial plan reviews and the maintainer's decisions (2026-10-24). A
probe runs before the feature is written (§7). Branch `plan-827-selection-bridge`.
**Issue:** #827 (bug, Beta 9). Related: #829 (the Beta 6 mitigation), #833 (translating a whole sutta —
needs this, is not delivered by it), #672 (context budget).

Provenance, per CLAUDE.md. **[fsnow]** marks the maintainer's own words or the option label he picked.
The comments on #827 and the body of #833 were written by an agent on his account; where they report a
maintainer correction they paraphrase it, so they are cited as the issue's record, not as his words.
**[observed]** is a fact from code or a measurement, with its source. **[suggestion]** is reasoning — this
plan's or a reviewer's.

[fsnow, 2026-10-24] "In the meantime I would like to implement 827. I will defer to you on the way to organize
this task, but we will want plenty of adversarial review, maybe even in the planning stage."

---

## 1. Problem

[observed, #827, #829] The assistant reads the reader's selection by running a script in the book's WebView
that writes `CST_AI_SEL:…` into `document.title`; `BookDisplayView.OnTitleChanged` parses it. Chromium caps
`document.title` at 4096 characters. After #829 the ceiling is about **1,100 characters in a non-Latin script
and about 2,900 in Latin**. A longer selection is refused and the turn answers over the surrounding passage,
with the notice "Your selection could not be read, so the answer covers the whole passage." (`PromptBuilder`).

Scope, from the issue's record: only `CST_AI_SEL` carries payload that is unbounded in practice.
`CST_LOOKUP_SEL` (Cmd+D, Cmd+Shift+F) is a word-or-phrase gesture (measured maximum 791 characters over 44
samples) and stays on the title channel; the status and control messages are bounded (≤ 223 characters) and
stay. The ceiling binds in Latin too: a sutta is 8–50× the Latin ceiling (#833).

**Goal.** `GetWebViewSelectionWithParagraphAsync` reads selections of any realistic size (a whole sutta is up
to ~130,000 characters, #833), with the same paragraph resolution as today.

**Non-goals.** The lookup path, the status channel, #833's chunked translation, the context budget (#672).

## 2. The current path, end to end [observed]

| Step | Where |
|---|---|
| Caller | `ReaderState.ReadSelectionAsync` — the only caller, on the UI thread via `Dispatcher.UIThread.InvokeAsync`. Reached from `ReaderState.GetCurrentAsync` (assistant turns, and `LocalApiServer` for agents). |
| Request | `BookDisplayView.GetWebViewSelectionWithParagraphAsync`: `(null, null)` if `_webView == null || !_isBrowserInitialized`; else a fresh TCS in the single field `_aiSelectionTcs`, `ExecuteScript`, and a wait of `AiSelectionTimeout` = 700 ms. |
| Script | `window.getSelection()`; PARA from the selection's start node by **document order** against `a[name^=para]` (strips `_bookcode` and a `-range`); base64 UTF-8; writes `CST_AI_SEL:|TAB:…|SEQ:…|PARA=…|LEN=…|B64:…`. |
| Response | `OnTitleChanged`: TAB must equal `_tabId`; a payload shorter than LEN is refused and logged; decode; `TrySetResult`. |
| Result | `ReadSelectionAsync`: `null` = unavailable (→ the notice), `""` = nothing selected, otherwise `SelectionPipeline.Normalize(raw, script)`. |
| Downstream | `AiContextBundler.BuildSelection` passes the normalized selection **whole** into `{{selection}}`; `TeiPassageReader` locates it with `selectionCap = MaxSelectionChars` (20,000) and reports `SelectionTruncated`, which drives the "was cut to fit" notice. |

Contracts to keep: **null vs empty** (#581); **the paragraph from the selection's own position** (#649).

## 3. Mechanisms considered

### A. `WebView.EvaluateScript<T>` — request/response from .NET — **chosen**

[observed, decompiled WebViewControl.Avalonia 3.120.9 and CefGlue.Common.ARM64 120.6099.207, the versions
`project.assets.json` resolves]
- `EvaluateScript<T>(script, frameName, timeout)` returns `Task<T>`. The script is queued on the frame's
  `JavascriptExecutor` — **the same per-frame queue `ExecuteScript` uses, drained by one flush thread** — and
  evaluated in the renderer; the result returns in a CEF process message (no title, no 4096 cap, no encoding
  multiplier).
- CefGlue evaluates the script as a **function body**: `cefglue.evaluateScript(function() { <script> })`
  (`JavascriptHelper.WrapScriptForEvaluation`). **The script must `return` its value**; a script without a
  top-level `return` yields `undefined`, which arrives as null.
- **`timeout` does not bound the caller's wait.** In `JavascriptExecutor.EvaluateScript` it is used two ways:
  the outer `Task.WhenAny` throws `JavascriptException("Timeout")` only while `IsFlushTaskInitializing`;
  otherwise the method goes on awaiting `evaluationTask`, whose inner CefGlue timer starts only when the flush
  thread dequeues the script. So a call queued behind slow scripts can wait indefinitely. (The 60 s
  `DefaultTimeout` applies to `BulkExecuteScripts`, the fire-and-forget path — not to evaluation.)
- If the executor is disposed or its flush cancelled, the task completes with `default(T)` instead of
  throwing; if the executor was disposed **before** the script was flushed, `QueueScript` returns null and
  the task completes with `default(T)` at once.
- Renderer side: if the frame's V8 context cannot be entered (`EnterOrFail`), the evaluation produces **no
  reply**, so the caller's task depends on its own bound.

**History, corrected.** The codebase's belief that "EvaluateScript returns null in this build" appears in
`3ce8e63` (#25), but git shows no version of `GetWebViewSelectionAsync` that called `EvaluateScript` — that
commit already used the title channel. [inferred] The belief most likely came from
`docs/research/RENDERING_REQUIREMENTS.md:488`, which names CefSharp's `EvaluateScriptAsync` with an IIFE
expression and no `return`. Under CefGlue that shape returns null for the reason above. [observed] The
wrapping is confirmed from code; the probe confirms it in the running app.

Why A:
- **No lifecycle.** Nothing to register, re-register after the float/move funnel, or outlive the browser.
- **Routing by construction.** The request goes to this view's browser; the answer completes this call's
  task. No shared channel to filter, no single-slot TCS, no `SEQ`.
- It removes code (the CST_AI_SEL machinery) rather than adding a second inbound channel.

### B. `WebView.RegisterJavascriptObject` — rejected

[observed] Registration may precede the browser and survives navigation (objects are rebound in every new
main-frame context); JS gets a Promise; arguments and results travel as JSON. The bound method runs on CEF's
browser-process UI thread — the Avalonia UI thread on macOS (`MultiThreadedMessageLoop = false`, external
pump), **another thread on Windows/Linux** (`MultiThreadedMessageLoop = true`).

Rejected because: [suggestion, reviewer B] on Windows/Linux a bound call in flight at `WebView.Dispose` makes
the library defer the dispose to `Task.Run` (`InnerDispose`, `JavascriptPendingCalls`) — **off the UI thread,
racing `SplitToWindow`'s dispose-and-recreate**, the subsystem where a mistake costs a SIGSEGV. A never puts a
call in that state. B also needs a register-per-instance lifecycle and still pairs a JS push with a pending
.NET request — the pairing that made the title path fragile.

### C. Rejected without a probe

Chunking over the title channel (writes coalesce and identical writes fire no event — CODE_REVIEW_2026-07-01
BOOK-4); the clipboard (overwrites the reader's clipboard, no paragraph); a custom CEF process message (needs a
renderer handler; CefGlue's renderer process is prebuilt).

## 4. Decisions [fsnow, 2026-10-24]

1. **No title fallback for the assistant.** [fsnow, picked "Remove it (Recommended)"] The assistant reads the
   selection by `EvaluateScript` only. On failure it reports "couldn't read" with a logged cause, and the
   answer covers the passage as today. The title path stays for Cmd+D and Cmd+Shift+F
   (`GetWebViewSelectionAsync`), unchanged. Once the probe confirms `EvaluateScript`, the CST_AI_SEL machinery
   goes: `_aiSelectionTcs`, its `OnTitleChanged` branch, `AiSelectionTimeout`.
2. **Over-cap selections.** [fsnow, picked "Cut to 20,000, notice true (Recommended)"] `{{selection}}` is cut
   at the same `TeiPassageReader.MaxSelectionChars` (20,000), so the bundler's notice and the reader's "cut to
   fit" note are both true. [observed] Today `BuildSelection` passes the selection whole while the passage
   reader caps at 20,000 — a 130,000-character selection would reach the prompt uncut.
3. **Probe machines.** [fsnow, picked "Kestrel and Merlin (Recommended)"] macOS arm64 and Windows.

## 5. Design

`GetWebViewSelectionWithParagraphAsync` keeps its signature and contract.

1. **The script** is a named constant, `AiSelectionScript`: today's selection and document-order PARA logic,
   ending in a top-level `return JSON.stringify({ t: text, p: para });`. No base64, no title.
2. **The bound.** One value, `AiSelectionEvalTimeout` (set from the probe's measurements), is passed **both**
   as `EvaluateScript`'s `timeout` (so the library frees the flush thread) **and** as our own
   `Task.WhenAny(call, Task.Delay(bound))`. **Invariant: never call `EvaluateScript` without a timeout.** An
   abandoned call is observed (a continuation logs its eventual outcome and swallows its exception) so it
   cannot surface as an unobserved task exception.
3. **Staleness.** Before calling, capture the WebView instance, `_isShutDown`, and a **load generation** that is
   incremented when `LoadHtmlContent` **starts** (the existing `_navGeneration` increments only on
   `Navigated`, too late to catch a read that crosses a reload, re-script or tab rebind). After the await,
   if any changed, return null — never `""` — and log the cause.
4. **Parse** in a pure function: JSON → `(text, para)`; empty selection → `""`; missing or non-numeric para →
   null paragraph; malformed or null → read failure.
5. **Threading.** No `ConfigureAwait(false)` in `GetWebViewSelectionWithParagraphAsync` — it reads UI-thread
   state after the await. Every `TaskCompletionSource` in the path uses `RunContinuationsAsynchronously`.
6. **Logging.** Every null names its cause — `disposed`, `navigated`, `shutdown`, `timeout`, `script-null`,
   `parse`, `exception` — with elapsed ms and the load generation before and after. A successful read logs
   length, paragraph and elapsed ms at Information.
7. **Cancellation.** Pass `GetCurrentAsync`'s `ct` through to `ReadSelectionAsync` (`ReaderState.cs` ~180).
8. **Cap** (decision 2): cut `{{selection}}` to `TeiPassageReader.MaxSelectionChars` in the bundler, where the
   notice is written, so the two always agree.

**Test seam.** The transport sits behind a delegate, e.g. `Func<string script, TimeSpan timeout,
Task<string?>>`, defaulting to `_webView.EvaluateScript<string>`. The decision logic is then testable without
CEF.

## 6. Risks [observed unless marked]

- The renderer's `EnterOrFail` path and the disposed-before-flush executor path both leave the caller
  dependent on its own bound — covered by §5.2.
- `EvaluateScript`'s result mapping into `T` (JSON vs raw string) is not verified — returning a JSON string
  and parsing it ourselves avoids most of it; the probe checks the rest.
- Queue latency is **not** a distinguishing risk: the title path's `ExecuteScript` sits in the same per-frame
  queue. [suggestion, reviewer A]
- [inferred] Chromium's IPC message limit is far above any selection; the probe measures 150,000 characters.
- Nothing here can be tested headlessly against CEF; the project has no CEF harness (#829 records the gap).

## 7. Probe (before the feature)

A diagnostic, env-var-gated duplicate read (`CST_PROBE_827=1`, optional `CST_PROBE_827_BUSY_MS`): each
assistant selection read also runs the planned `EvaluateScript` read after the existing title read and logs
both. Steps are given separately for Kestrel (macOS) and Merlin (Windows). It is removed when the feature
lands.

## 8. Tests

- **Unit, against a fake transport:** never completes (→ timeout null), returns null (→ script-null), throws
  `JavascriptException("Timeout")`, throws something else, returns late after the bound, returns across a
  load-generation bump (→ navigated null, never `""`), returns `{"t":"","p":""}` (→ `""`), returns
  well-formed text and paragraph.
- **The script constant has a top-level `return`.**
- **Headless, through `AiContextBundler` → `PromptBuilder`,** with 25,000- and 130,000-character
  selections: `{{selection}}` is cut to 20,000 and the "cut to fit" notice is present; and
  `TeiPassageReader.LocateSelection`'s cost with a 130,000-character needle (measure; fix if pathological).
- **Manual test list** for the maintainer: a paragraph and a whole sutta in Latin, Devanāgarī and Myanmar;
  nothing selected; float or re-dock during a read; two books in two windows; Cmd+D and Cmd+Shift+F still
  work; the browser-not-ready notice; and an agent-path read (`LocalApiServer`) with a large selection.

Noted, out of scope: `/ai/context-preview` passes `reader.Paragraph` rather than `SelectionParagraph`
(`LocalApiServer.cs` ~739).

## 9. Doc and comment sweep (when the feature lands)

`BookDisplayView.axaml.cs` ~395–400, ~409, ~4114 (the print path's "EvaluateScript returns null" claim);
`ReaderState.cs` ~173, ~308–318; the `AiContextBundler.BuildSelection` comment; `AI_SURFACE_B.md` ~157 and
~571; `CODE_REVIEW_2026-08_FINDINGS.md` R8-1; `RENDERING_REQUIREMENTS.md:488`.

**Release note:** retire #827's Known Issues entry and write the resolution note.
