# #827 — Reading the assistant's selection without the 4096-character title cap

**Status:** plan, for adversarial review before any code. Branch `plan-827-selection-bridge`.
**Issue:** #827 (bug, Beta 9). Related: #829 (the mitigation that shipped in Beta 6), #833 (translating a
whole sutta — needs this, but is not delivered by it), #672 (context budget).

Provenance, per CLAUDE.md. **[fsnow]** marks the maintainer's own words. The comments on #827 and the body of
#833 were written by an agent on the maintainer's account; where they report a maintainer correction they
paraphrase it, so they are cited here as the issue's record, not as his words. **[observed]** is a fact from
code or a measurement, with its source. **[suggestion]** is this plan's reasoning.

[fsnow, 2026-10-24] "In the meantime I would like to implement 827. I will defer to you on the way to organize
this task, but we will want plenty of adversarial review, maybe even in the planning stage."

---

## 1. Problem

[observed, #827 and #829] The assistant reads the reader's selection by running a script in the book's
WebView that writes `CST_AI_SEL:…` into `document.title`; `BookDisplayView.OnTitleChanged` parses it.
Chromium caps `document.title` at 4096 characters. After #829 (routing before payload, base64, a declared
`LEN=` so truncation is detected) the ceiling is about **1,100 characters in a non-Latin script and about
2,900 in Latin**. A longer selection is refused and the turn falls back, with a notice, to the surrounding
passage (`PromptBuilder` — "Your selection could not be read, so the answer covers the whole passage.").

The issue's record (its own comments) settled the scope:
- Only `CST_AI_SEL` carries payload that is unbounded in practice. `CST_LOOKUP_SEL` (Cmd+D, Cmd+Shift+F)
  is a word-or-phrase gesture (measured maximum 791 characters over 44 samples) and stays on the title
  channel. `CST_STATUS_UPDATE` and the other ~15 control messages are bounded (≤ 223 characters) and stay.
- The ceiling binds in Latin too: a sutta is 8–50× the Latin ceiling (#833).

**Goal.** `GetWebViewSelectionWithParagraphAsync` returns selections of any realistic size (a whole sutta is
up to ~130,000 characters, #833) with the same paragraph resolution as today, and falls back to the current
title path automatically if the new path is unavailable, so the worst case is today's behaviour.

**Non-goals.** Not the lookup path, not the status channel, not #833's chunked translation, not the
context budget (#672). The selection arriving whole does not mean it fits the model; that is downstream.

## 2. The current path, end to end [observed]

| Step | Where |
|---|---|
| Caller | `ReaderState.ReadSelectionAsync` (`Services/Ai/ReaderState.cs`, ~322) — the only caller. Runs on the UI thread via `Dispatcher.UIThread.InvokeAsync`. Used by the assistant turn and, through `ReaderState`, by `LocalApiServer` (agents). |
| Request | `BookDisplayView.GetWebViewSelectionWithParagraphAsync` (~284): returns `(null, null)` if `_webView == null || !_isBrowserInitialized`; else stores a fresh `TaskCompletionSource` in the single field `_aiSelectionTcs`, runs the script with `_webView.ExecuteScript`, and awaits it against `AiSelectionTimeout` = 700 ms. |
| Script | Reads `window.getSelection()`; resolves PARA from the selection's start node by **document order** against `a[name^=para]` anchors (strips the `_bookcode` suffix and a `-range`); base64-encodes UTF-8 text; writes `CST_AI_SEL:|TAB:<tabId>|SEQ:<n>|PARA=…|LEN=…|B64:…`. |
| Response | `OnTitleChanged` (~2781): checks `TAB` equals `_tabId`, parses PARA and LEN, refuses a payload whose length differs from LEN (logged warning), decodes, `TrySetResult((sel, para))`. |
| Result | `ReadSelectionAsync`: `null` text means unavailable (→ `SelectionUnavailable`, the notice above); `""` means nothing selected; otherwise `SelectionPipeline.Normalize(raw, script)`. |

Two properties the replacement must keep: **null vs empty** (could-not-read vs nothing-selected, #581), and
**the paragraph from the selection's own position**, not the reading position (#649).

## 3. Mechanisms considered

Both candidate mechanisms carry data in a CEF process message, not in the title, so neither has the 4096 cap
or the encoding multiplier. [observed: CefGlue `Messages.JsEvaluationResult` / `NativeObjectCallRequest`;
Chromium's IPC message limit is far above any selection — inferred, not measured.]

### A. `WebView.EvaluateScript<T>` — request/response from .NET **[suggestion: preferred, subject to the probe in §6]**

[observed, decompiled WebViewControl.Avalonia 3.120.9 and CefGlue.Common.ARM64 120.6099.207 — the versions
`project.assets.json` resolves]
- `EvaluateScript<T>(script, frameName, timeout)` returns `Task<T>`. It queues the script on the frame's
  `JavascriptExecutor` (the same queue `ExecuteScript` uses), which evaluates it in the renderer and
  completes the task with the result.
- CefGlue wraps the script as a **function body**: `cefglue.evaluateScript(function() { <script> })`
  (`JavascriptHelper.WrapScriptForEvaluation`). **A script must `return` its value.**
- If the executor is disposed or its flush is cancelled (view disposed), the task completes with
  `default(T)` rather than throwing (`JavascriptExecutor.EvaluateScript`: `QueueScript` returns null, or
  `FlushTaskCancellationToken` is cancelled). With a `timeout`, a context that never initialises throws
  `JavascriptException("Timeout")`.

**Why the codebase believes "EvaluateScript returns null in this build".** [observed] The belief dates from
#25 (commit `3ce8e63`): the first version of `GetWebViewSelectionAsync` used `EvaluateScript`, its comment then
records "EvaluateScript returns null in this CEF binding", and the code switched to the title channel. The
cause was never recorded. [inferred, high confidence] The wrapper above explains it: a script written as an
IIFE expression without `return` (the shape in `docs/research/RENDERING_REQUIREMENTS.md`) evaluates to
`undefined` and comes back null. This is a hypothesis to prove in the probe, not a fact.

Why prefer it:
- **No lifecycle.** Nothing is registered, so nothing must be re-registered after float/move/recycle, and
  nothing outlives the browser. The request goes to *this view's* browser and the answer comes back on *this
  call's* task — **tab routing is by construction**; there is no shared channel to filter.
- **No single-slot TCS, no SEQ, no timeout guess for a shared channel.** Each call has its own task.
- It removes code (the CST_AI_SEL parse in `OnTitleChanged`) rather than adding a second inbound channel.

Risks:
- It shares the executor queue with every `ExecuteScript` (status-tick scripts, scroll restoration). A long
  queue delays the answer; the wait needs an honest timeout (the library default is 60 s).
- `T` mapping: the renderer returns the value as JSON and the browser side deserialises into `T`
  [inferred from `ResultAsJson`/`GetResult<T>`; the exact rules for a string vs an object need the probe].
  Returning `JSON.stringify({ text, para })` and deserialising ourselves avoids depending on them.

### B. `WebView.RegisterJavascriptObject` — a JS→.NET bridge (the issue's proposal)

[observed, same decompilation]
- `RegisterJavascriptObject(name, obj, interceptCall = null)` → CefGlue `NativeObjectRegistry.Register`. Can
  be called **before the browser exists**: the registration is stored and sent to the renderer when the
  browser is set (`SetBrowser`). Returns false if the name is already registered on this WebView.
- In the renderer, registered objects are **re-created in every new main-frame V8 context**
  (`JavascriptToNativeDispatcherRenderSide.HandleContextCreated`), so a registration survives navigation
  (`LoadUrl` of a book, reload on script change) within one browser.
- JS sees `window.<name>.<method>(…)` returning a **Promise**; arguments travel as JSON, the result returns as
  JSON. A method returning a `Task` is rejected ("Unexpected Task type result") unless an async interceptor
  is used.
- **Threading.** The .NET method runs on CEF's browser-process UI thread. macOS: `MultiThreadedMessageLoop =
  false` with an external pump, so that is the Avalonia UI thread. Windows/Linux: `MultiThreadedMessageLoop
  = true`, so it is **not** the Avalonia UI thread [observed `CefRuntimeLoader`; the thread the handler runs
  on is inferred from CEF's model].
- **Disposal.** `WebView.Dispose` counts in-flight bound calls; if any are pending it defers the actual
  dispose to `Task.Run` until they finish (`InnerDispose`), i.e. **off the UI thread** — in the subsystem where
  a mistake costs a SIGSEGV. After `isDisposing`, new calls return null immediately.
- Each `BookDisplayView` has its own WebView and so its own registry, which routes to the right view; a
  per-request id would still be needed to drop late answers.

Why not preferred: it adds a second inbound channel with a lifecycle (register per WebView instance, survive
the dispose+recreate funnel, never let a call be in flight at dispose), it needs cross-thread marshalling on
Windows, and it is JS-initiated, so the .NET side still has to pair a push with a pending request — the very
pairing that made the title path fragile. It is the right tool for JS that calls .NET unprompted; the
selection is a question .NET asks.

### C. Rejected

- **Chunking over the title channel.** [observed, CODE_REVIEW_2026-07-01 BOOK-4] `OnTitleChanged` reads the
  latest `Title`, so rapid writes coalesce and identical writes fire no event. Chunking needs an ack per chunk
  over a channel shared with 3,600 status messages a session — more of the mechanism that is the problem.
- **Clipboard** (`EditCommands.Copy`, which Cmd+C already uses natively). Overwrites the reader's clipboard,
  gives no paragraph, and reading the clipboard back is its own async path.
- **A custom CEF process message.** Needs a renderer-side handler; CefGlue's renderer process is prebuilt
  (`Xilium.CefGlue.BrowserProcess`), and A/B are the supported surface over exactly this mechanism.

## 4. Design (mechanism A)

[suggestion] In `BookDisplayView`:

1. `GetWebViewSelectionWithParagraphAsync` keeps its signature and contract (null = could not read, "" =
   nothing selected, paragraph from the selection's position).
2. It first tries **`EvaluateScript<string>`** with the same selection and document-order PARA logic as
   today, ending in `return JSON.stringify({ t: text, p: para });` — no base64, no title.
3. The task is awaited with a timeout (to be set from the probe's measurements; the issue's 700 ms was
   chosen for a shared title channel). On success it parses the JSON and returns `(text, para)`.
4. **Fallback:** if the call throws, times out, or returns null/unparseable, it logs which, and runs today's
   title path unchanged. The worst case is today's behaviour.
5. Guards before calling: `_webView != null`, `_isBrowserInitialized`, not shut down. A call that loses its
   browser mid-flight completes with `default(T)` (library) and falls back.
6. Logging: one Information line per read, naming the path that answered, length, paragraph and elapsed ms;
   a Warning for each fallback with its cause.

If the probe shows A unusable, the same section is rewritten for B with: register in `TryCreateWebView`
(once per WebView instance; the funnel builds a new View with a new WebView), a bound method that does
nothing but complete a per-request `TaskCompletionSource` (thread-safe, no Avalonia access, returns at once,
so no bound call is ever in flight at dispose), and a request id checked on arrival.

## 5. Concurrency and threading

- [observed] `ReadSelectionAsync` runs on the UI thread and is the only caller. Overlapping reads (two
  assistant turns, or an agent request during a turn) are possible in principle.
- A: each call owns its task, so overlapping reads cannot clobber each other — unlike `_aiSelectionTcs`,
  which is a single slot.
- The title fallback keeps today's single-slot behaviour; it is only reached when A fails.
- Cmd+D's `_lookupSelectionTcs` is untouched.

## 6. Probe before implementation [suggestion]

The plan stands on an inference about why `EvaluateScript` "returned null". Before writing the feature:

1. A temporary, logged probe in `BookDisplayView`: `EvaluateScript<string>` returning a known string, then a
   20,000-character and a 150,000-character string, and a selection in Myanmar; record value, length and
   elapsed time, and what happens with no `return`.
2. Run on macOS (arm64), and on Windows, where CEF's message loop is multi-threaded.
3. Exercise a float and a re-dock with a read in flight.

This needs the running app, so it is a short manual run for the maintainer, or a session he has approved
for GUI work. Nothing here can be measured headlessly: the project has no CEF test harness (#829 records the
same gap).

## 7. Tests

- **Unit (no CEF):** extract the response parsing into a pure function (JSON → `(text, para)`, including
  empty selection, missing paragraph, malformed JSON → null) and the "which path, and why" decision, and test
  those. The PARA logic stays in JS, as today.
- **Manual test list** for the maintainer: a one-paragraph and a whole-sutta selection in Latin, Devanāgarī
  and Myanmar; nothing selected; selection then float the book and ask; two books in two windows, select in
  one, ask in the other; Cmd+D and Cmd+Shift+F still work; the assistant's notice appears if the browser is
  not ready.

## 8. Open questions for the maintainer

1. Is a probe in the running app acceptable before implementation (§6), and on which machines?
2. Should a successful read via the new path make the title fallback a logged anomaly (Warning) or routine
   (Information)?
3. #833's whole-sutta translation will hit the context budget next. Should this change also log the
   selection's size against the budget, so the next limit is visible when it is reached?
