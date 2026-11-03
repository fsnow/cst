# #827 — Reading the assistant's selection without the 4096-character title cap

**Status:** implemented (Beta 9), on branch `plan-827-selection-bridge`. The plan had two adversarial plan
reviews and a probe on macOS and Windows before any of this was written.
**Issue:** #827. Related: #829 (the Beta 6 mitigation it replaces), #833 (translating a whole sutta — needs
this, is not delivered by it), #672 (context budget).

Provenance, per CLAUDE.md. **[fsnow]** marks the maintainer's own words or an option label he picked.
**[observed]** is a fact from code or a measurement, with its source. **[suggestion]** is an agent's
reasoning.

[fsnow, 2026-10-24] "In the meantime I would like to implement 827. I will defer to you on the way to organize
this task, but we will want plenty of adversarial review, maybe even in the planning stage."

---

## 1. The problem it solved

[observed, #827, #829] The assistant used to read the reader's selection by having a script write
`CST_AI_SEL:…` into `document.title`, which `BookDisplayView.OnTitleChanged` parsed. Chromium caps
`document.title` at 4096 characters, so after #829 the ceiling was about 1,100 characters in a non-Latin
script and about 2,900 in Latin. A longer selection was refused, and the answer covered the surrounding
passage instead. A sutta is 8–50× the Latin ceiling (#833).

Out of scope: `CST_LOOKUP_SEL` (Cmd+D, Cmd+Shift+F) is a word-or-phrase gesture (largest measured: 791
characters over 44 samples) and stays on the title channel. The status and control messages are bounded
(≤ 223 characters) and stay too.

## 2. Decisions [fsnow, 2026-10-24]

1. [fsnow, picked "Remove it (Recommended)"] **No title fallback for the assistant.** It reads the selection
   by `EvaluateScript` only. A failed read gives "couldn't read", logs its cause, and the answer covers the
   passage.
2. [fsnow, picked "Cut to 20,000, notice true (Recommended)"] `{{selection}}` is cut at
   `TeiPassageReader.MaxSelectionChars` (20,000), so the notice about it is true.
3. [fsnow, picked "Kestrel and Merlin (Recommended)"] The probe ran on macOS arm64 and on Windows.
4. [fsnow, 2026-11-02: "Let's not quantify the text limit in the message. Something like 'Your selection
   was very long ...'"] The notice names no number. [observed, #827 review] The limit counts the Latin sent
   to the model, which is about 11% longer than the same Devanāgarī and 13% longer than Myanmar, so a figure
   would be untrue for a reader counting what they selected.

## 3. How it works now

| Step | Where |
|---|---|
| Caller | `ReaderState.ReadSelectionAsync(document, ct)`, on the UI thread, reached from `GetCurrentAsync` (assistant turns, and `LocalApiServer` for agents). `ct` is honoured before and after the read. |
| Read | `BookDisplayView.GetWebViewSelectionWithParagraphAsync` → `WebViewSelectionReader.ReadAsync`, with `webView.EvaluateScript<string>` as the transport. |
| Script | `WebViewSelectionReader.Script`: `window.getSelection()`, and the paragraph from the selection's start node by document order against `a[name^=para]` (#649). It ends `return JSON.stringify({ t: text, p: para });`. |
| Bound | `WebViewSelectionReader.Timeout` (2 s) is passed to the library **and** used for our own `Task.WhenAny`. An abandoned call gets a continuation that logs its outcome when it eventually finishes. The continuation runs on the UI thread, so the 2 s is not a wall-clock guarantee while that thread is busy. |
| Staleness | `_loadGeneration` is incremented at the start of `LoadHtmlContent`. After the answer, a changed generation, a different WebView instance, or `_isShutDown` makes the read a failure. |
| Result | `null` = could not read, with the cause logged (`Timeout`, `LibraryTimeout`, `ScriptNull`, `Parse`, `Exception`, `Stale`, or browser not ready). `""` = nothing selected (#581). |
| Cap | `AiContextBundler.CapSelection` normalizes the selection and cuts it once, before the passage fetch, so `{{selection}}` and the window's search use the same text. The cut keeps the start and falls on a space within 200 characters of 20,000 Latin characters, or at 20,000 if there is none. |
| Window cap | [observed, #827 review] `TeiPassageReader.ReadWindowAroundSelection` keeps its own 20,000 cap, counted in the book's script with notes included. It doesn't usually fire on a selection already cut, since the Latin is longer. On text heavy with notes it can; the window then ends before the end of the cut selection, and the partial-passage badge says so. |
| Notice | A selection that was cut gets one notice, "Your selection was very long, so only its beginning was used." The passage's own "cut to fit" notice is suppressed then, so the reader isn't told about the same cut twice. The partial-passage badge still follows `PassageTrimmed`. |

[observed, decompiled WebViewControl.Avalonia 3.120.9 / CefGlue.Common.ARM64 120.6099.207] These facts about
the library shape the design:
- CefGlue evaluates the script as a **function body** (`cefglue.evaluateScript(function() { … })`), so the
  script must `return` its value. Without a `return`, it yields `undefined`, which arrives as null. That is
  the source of the codebase's old belief that "EvaluateScript returns null". The belief most likely began
  with a CefSharp-style IIFE in `RENDERING_REQUIREMENTS.md`; no version of the code ever called
  `EvaluateScript`.
- **The library's `timeout` does not bound the caller's wait.** It throws only while the flush task is
  initializing. Otherwise the inner timer starts when the per-frame queue dequeues the script. So we keep a
  bound of our own.
- A disposed executor completes the task with `default(T)`, which is null. A renderer that cannot enter the
  frame's V8 context sends no reply at all. In both cases the caller depends on its own bound.

`RegisterJavascriptObject` was rejected. On Windows and Linux a bound call that is still in flight at
`WebView.Dispose` makes the library defer the dispose to `Task.Run`. That puts the dispose off the UI thread,
racing `SplitToWindow`'s dispose-and-recreate funnel.

## 4. Measurements [observed]

| What | Result |
|---|---|
| Probe, macOS arm64 (Kestrel) | eval matched the title read wherever both worked; Myanmar selections of 1,775 and 5,238 characters in 1 ms; the whole book (661,884 characters) in 22 ms; float, re-dock or close mid-read → "browser changed" after 1.6–2.9 s, no crash. The probe's bound was 10 s, not the shipped 2 s |
| Probe, Windows arm64 (Merlin) | 403,195 characters in 39 ms; every case passed |
| Busy renderer | during a 5 s busy hold, the Welcome page (same renderer process) didn't repaint until clicked — the reason the bound is short |
| Reads per turn | about 4 reader-state reads per assistant turn |
| `TeiPassageReader.LocateSelection`, repetitive text, needle matching up to its last word | 130,000-character needle: 3,110 ms; 20,000-character needle: 858 ms — the reason the cut happens before the passage fetch |

**The timeout, 2 s** [suggestion, recorded in `WebViewSelectionReader.Timeout`'s comment]: about 50× the
slowest probe read. It is also how long an abandoned read can hold the book's script queue, so it stays
short.

## 5. Tests

- `WebViewSelectionReaderTests` runs against fake transports. It covers:
  - a selection and its paragraph;
  - empty → `""`;
  - a non-numeric paragraph;
  - never answers → `Timeout`;
  - a late answer is not used;
  - null → `ScriptNull`;
  - `TimeoutException` → `LibraryTimeout`;
  - other exceptions, thrown synchronously or not;
  - a parse failure, including a lone surrogate in the reply;
  - a stale read across a reload → `Stale`, null;
  - the library is given the same bound;
  - the script ends in a top-level `return`;
  - the bound is short.
- `AiContextBundlerTests.A_selection_over_the_limit_is_cut_and_said_once`, at 25,000 and 130,000 characters.
  It runs bundler → `PromptBuilder` and checks four things:
  - the selection is cut to within 200 characters of 20,000 and marked `TrimmedForBudget`;
  - the prompt carries the cut text;
  - there is exactly one "very long" notice, with no number in it, and no passage "cut to fit" duplicate;
  - the prompt is bounded.
- `CapSelection` directly: a selection at the limit is not cut; the cut falls on a space; with no space near
  the limit the cut is at the limit.
- There is no CEF harness (#829), so the browser side is checked by hand. That includes `BookDisplayView`'s
  part of the staleness check (`_loadGeneration` and the WebView instance); the tests drive it through a fake.

Out of scope [observed]: `/ai/context-preview` passes `reader.Paragraph` rather than `SelectionParagraph`
(`LocalApiServer`).
