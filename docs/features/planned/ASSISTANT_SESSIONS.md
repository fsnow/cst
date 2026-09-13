# Assistant sessions — conversation, persistence, naming, compaction (Planned)

> Plan of record for #849 (retain and restore Assistant turns) and #850 (the + new-conversation control), drafted
> 2026-09-11. Provenance is marked throughout: **[fsnow]** is the maintainer's decision, **[suggestion]** is
> an agent's and advisory, **[observed]** is a fact from code or a named source. Unmarked text is context.

## 0. The request, and the model for it

A tester wrote (2026-09, quoted in full on the issue thread) asking for persistence of conversation history:

> *"Automatically save the conversation history to a local file (e.g., JSON) associated with each document
> or study session. Reload the history when reopening the document, resending it to the API to continue the
> conversation. Optional: resend only the last N exchanges (or a summary) to keep token costs down."*

**[fsnow]** *"I would like to make a plan to implement all of these features. Claude Code itself is my
model and we should aim for feature parity with CC in context management, named sessions, session
restoration, etc."*

So the target is the Claude Code session model, transposed to a reading app. §2 maps it feature by feature.

## 1. Where things stand — one finding changes the shape of the work

**[observed] Every Assistant turn is one-shot. The model never sees a previous turn, even within a
session.** `AiChatOrchestrator.RunCoreAsync` builds the request as

```csharp
new ChatRequest(provider.Model, prompt.MaxOutputTokens, prompt.System,
    new[] { new ChatMessage(ChatRole.User, prompt.UserContent) }, effort);
```

— a single user message — and `AiTurnRequest` has no field that could carry history. `AI_SURFACE_B.md` §14
lists this as an open question (*"Follow-up turns — one-shot per invocation, or a short conversation over the
same bundle?"*). The panel is a transcript (#849 says so correctly) but the *model's* view is a series of
unrelated requests: "what did you mean by the third word?" cannot work today.

The tester's diagnosis was therefore right, and understated: the APIs are stateless *and the app does not
resend anything*. There is no conversation to persist yet. **Building the conversation comes before persisting
it**, and that reorders #849's plan.

Two things make the rest cheaper than it looks:

- **[observed] The wire layer is already multi-turn.** `ChatRequest.Messages` is a list, `ChatRole` has
  `User` and `Assistant`, and both adapters iterate the list (`OpenAiCompatibleProvider.cs:237`,
  `AnthropicMessagesProvider.cs:251`). Only the orchestrator's single-message construction stands between the
  app and a conversation.
- **[observed] A turn holds the display strings but not the facts they were made from.** `AiTurnViewModel` has
  the task, question, raw answer, marked answer, reasoning, notices, partial-passage flag and the `SentContext`
  (#665) — and, for the rest, only what is on screen. Four things a record needs were **not reachable from a
  turn** at the end of it:
  - the structured `CitationRef` — it arrived once on the `Started` event and `Handle` replaced it with two
    rendered lines, so nothing left named a file to reopen;
  - the `AiUsageReport` — formatted by `FormatUsage` and dropped, leaving a `:N0`-grouped string;
  - the elapsed duration — only `FormatElapsed`'s `"4.2s"`, and the panel's one stopwatch is restarted by the
    next turn;
  - the provider and model ids — on neither `AiTurnContext` nor the view model, so carrying them needed a
    contract change rather than a read from the panel.

  So the wiring is not "only written down": the turn had to start keeping the originals beside the strings
  derived from them. Nothing bound in the panel changed. (Corrected 2026-09-12, after building it — the
  original claim here was wrong, and the store's record docs were written against the corrected list.)

## 2. Claude Code parity map

What Claude Code does, and what the equivalent is in a reader whose "project" is a passage rather than a
directory. Items marked *skip* are deliberately not carried over.

| Claude Code | CST Reader equivalent | Phase |
|---|---|---|
| Every turn is appended to a transcript file as it completes; nothing is lost on a crash | One file per session under `CSTReader/assistant-sessions/`, written at the end of each turn | P2 |
| Prior turns are in the model's context; follow-ups work | History replayed as `user`/`assistant` pairs in `ChatRequest.Messages` | **P1** |
| `--continue` — pick up the most recent session | On launch, the panel reloads the last active session (no model call) | P2 |
| `/clear` — start a new session; the old one stays on disk | A **+** (new conversation) control: the current session is saved, the panel starts a fresh one; nothing is deleted | P0 (#850) |
| Auto-title from the first prompt; `/rename` | Auto-name from the first turn (preset + citation, or the question); rename in the panel | P3 |
| `--resume` / `/resume` picker: title, when, how many messages | Session list in the panel: name, last active, turn count, books touched; switch by clicking | P3 |
| `/compact [instructions]` — summarise older turns, keep recent ones verbatim | **Compact** action + a compaction prompt template; the summary becomes the first message | P4 |
| Auto-compact near the context window | Automatic when the estimated request exceeds a fraction of the model's `ContextLength` (`AiModelRecord.ContextLength`, [observed] already stored from the listing) | P4 |
| `/context` — how full the window is | The per-turn "Estimated context" field (#665/#672) gains the history's share | P1 |
| Sessions expire after `cleanupPeriodDays` (30) | *skip* — kept forever, no cap; delete is manual, per session (§6) | — |
| Resuming forks a new session id | *skip* — a reader resumes in place; forking is a developer concern | — |
| `/rewind`, checkpoints | *skip* — nothing in a reading session to roll back to | — |
| Sessions are per project directory | One global list, not per book (§6) | — |

The tester's three numbered points map to P2 (save), P2 (reload) and P4 (last-N or summary).

## 3. Design

### 3.1 The conversation (P1) — what the model sees on turn *n*

**[suggestion]** History carries *what was asked and answered*; the passage context is sent fresh, once, for
the current turn:

```
system:    (unchanged shared system prompt)
user:      [turn 1] «Explain» — Mahāvaggapāḷi 1.1            ← citation line + the question, no bundle
assistant: [turn 1's answer, raw]
user:      [turn 2] «Question» — Mahāvaggapāḷi 1.1: what does…
assistant: [turn 2's answer]
user:      [current turn — the full rendered UserContent: context blocks + preset instruction]
```

Why not replay each turn's full `UserContent`: it re-sends the passage, selection and lemma blocks on every
turn, so a ten-turn session about one paragraph sends the paragraph ten times. Why not drop the citation line:
the model needs to know *which* passage an earlier answer was about once the reader has moved; the citation
is the app's own text (never model output), so replaying it keeps the "chrome is built from bundle data" rule
intact. **The passage is sent for the current position only** — a follow-up about an earlier passage after the
reader has moved gets the citation, not the text. That is a limitation to state in the doc comment, not to
solve now.

Mechanics:

- `AiTurnRequest` gains `IReadOnlyList<AiExchange> History` (question-side text + answer text, both already
  rendered strings). The orchestrator prepends them to `Messages`. Failed turns with no answer text are not
  replayed; a turn with partial text is replayed as-is (it stands on screen, so it stands in the history).
- `SentContext` (#665) must show the replayed messages, or "what did the model see" stops being true. Shipped
  in #991 as `SentContext.History` (plus `HasHistory`), rendered by the panel's Sent expander above the
  message it sent.
- The token estimate (`AiTokens.Estimate`) runs over the whole message list, and the "Estimated context"
  field says how much of it is history — the input to auto-compaction in P4.
- Reasoning is **never** replayed. It was segregated from the answer for a reason (§8 of the design doc).
- Retry re-asks with the history *as it is now*, not as it was. Simpler, and what a reader pressing "Try
  again" on a 504 wants.
- **Prompt caching** (Anthropic `cache_control` on the stable prefix) is exactly what a replayed history
  benefits from, and **[observed] the request has no prefix that is stable by construction, and nothing asks
  for caching (2026-09-12)**. The layout above keeps the replayed messages byte-stable; the *system prompt* is
  not, because `Resources/Ai/system.md` embeds `{{scope}}` and `{{outputLanguage}}` and `PromptBuilder.Scope`
  renders the book name, the reference, a paragraphs-covered sentence and a selection-dependent sentence — it
  holds only while the reader stays on the same reference with the same selection state and answer language.
  Neither adapter sets `cache_control`. [suggestion] Whoever takes
  caching up will have to move the scope statement out of the system prompt and into the per-turn message
  first; that was not #991's work.

### 3.2 Persistence (P2)

**[suggestion] Not in `application-state.json`.** #849 raises the weight-class concern and it is right:
that file is loaded synchronously at launch, backed up on a schedule, and holds file names and window
rectangles. A transcript with 40 answers is a different object. `ApplicationState` gets **one string**,
`ActiveAssistantSessionId`; the sessions themselves live in `<DataDirectory>/assistant-sessions/<id>.json`,
one file each (`AppConstants.DataDirectory` is the single source of truth for the directory).

**Write cadence:** the whole session file is rewritten at `EndTurn` (turn complete, failed, or stopped), on
rename, and on compaction — never per streamed delta. A crash mid-turn loses only the turn in flight, which is
the Claude Code guarantee. Atomic write (temp + `File.Replace`), the pattern `ApplicationStateService` uses.

**What a stored turn holds** — everything the panel shows, so a restored turn renders identically:

| field | source | note |
|---|---|---|
| id, task, question, when | `AiTurnViewModel` | the id is minted at construction, so a later turn can point at this one while it is still on screen |
| citation (`CitationRef`) | `AiTurnContext.Citation`, kept on the turn as `StructuredCitation` | **required** — #849 comment 2: this is what makes "take me back" possible |
| subject (selection summary) | `Subject` | display text only; the full selection is in the sent prompt |
| answer (raw), marked answer, reasoning | builders | both answer halves: the stripped one renders, the marked one replays (#991) |
| notices, partial-passage flag | `Notices`, `IsPartialPassage` | |
| usage (two ints), elapsed (ms), status, failed | `UsageReport` and `ElapsedTime` on the turn | numbers, not the `:N0`/`"4.2s"` strings printed from them |
| asked line | `DescribeAsked(turn)` | derivable from *today's* wording, so stored: a record of what a model saw must not re-render itself |
| sent context (`SentContext`) | #665, mapped to `AiSentRecord` | prompt halves and fields by value; the replayed conversation by **id** — a 30-turn file would otherwise hold 435 copies of its own answers |
| reading position (`ReadingPositionToken`) | `ReaderState.ReadingPosition`, captured at `StartTurn` | #849 comment 3; **new capture** — read from `BookDisplayViewModel.LastPositionToken`, the rolling ~200 ms token the dock factory already persists, rather than by a fresh WebView round trip |
| provider id, model id | `AiTurnContext.ProviderId`/`ModelId` (**new** on that contract) | the model that answered is part of the record |

**Restore:** at launch the panel reads the active session and rebuilds `Turns` from records —
`AiTurnViewModel.FromRecord(record, byId)` alongside the live constructor, with `IsRunning = false` and the
answer published (`Blocks` exist only after `PublishAnswer`). No model call. A session file that fails to parse
is moved aside with a timestamp (the `application-state.unreadable-*` pattern) and the panel starts empty with a
notice, never a crash.

**[observed] Launch sequencing.** The panel's view model is built during `CstDockFactory.CreateLayout`, which
runs *before* `LoadStateAsync` completes — so a constructor reading `ActiveAssistantSessionId` would read the
default empty state every launch. The restore is therefore pushed in by `App.InitializeFromLoadedState`, as
`AiAssistantViewModel.RestoreAsync()`, next to `SearchViewModel.ApplyState` (#87) and
`DictionaryViewModel.ApplyState` (#479), which exist for exactly this. It is gated on
`CstDockFactory.AssistantEnabled()`, because resolving the singleton would otherwise *create* a panel for a
reader who has the feature switched off.

### 3.3 Sessions (P0, P3)

**There is no Clear.** **[fsnow]** *"'Clear' was introduced by Claude at some point and is not relevant. I
would like to create new conversations like in Claude Code, maybe also with a plus button, while saving the
current one."* So the control is **+** — new conversation: the current session is saved (it already is, at
every `EndTurn`), and the panel starts an empty one. Nothing is destroyed, so it needs no confirmation.
`ClearCommand` and `Clear()` are removed, not rebound; #850 is rewritten to describe the + control.

**[observed] The command exists as of the P2 wiring: `NewConversationCommand`** — `IsBusy`-guarded like every
other, it clears `Turns`, drops the session, and clears `ActiveAssistantSessionId`. It saves nothing, because
every turn was already written when it ended. `ClearCommand`/`Clear()` are gone. What is left for #850 is the
button.

**Deletion** is a separate action on a row in the session list, and it confirms — the one irreversible thing
here. **[fsnow]** chose *"Yes, with confirmation"*.

**Naming:** **[fsnow]** chose *"Auto from the first turn, renamable"* — `Explain · Mahāvaggapāḷi 1.1`, or the
first ~60 characters of a question; the reader can rename. No model-generated titles.

**The list:** newest-active first; each row shows name, last-active time, turn count, and the distinct books
its citations name. Click switches the panel to it (the in-flight turn, if any, blocks the switch, the way
`IsBusy` guards every other command). Rename and Delete on the row.

### 3.4 Compaction (P4)

Manual **Compact** and automatic compaction share one mechanism: the older turns are summarised by the active
model through a compaction prompt template (a sixth embedded template beside the five presets, user-editable
like them), the summary replaces them in the *history the model sees*, and the most recent *N* turns stay
verbatim. The transcript on screen keeps every turn — compaction changes what is **sent**, not what is
**shown** — with a marker row where the boundary falls (*"12 earlier turns summarised"*), which the `Sent`
expander can open.

**[fsnow]** *"Manual and auto at a fraction of context length"*. The automatic trigger fires when the
estimated request reaches **95% of `ContextLength`** for the resolved model — **[fsnow]** *"95%, but make
this a setting"* — so the fraction is a `Settings.Ai.Chat` value with 95 as its default. **The last 4
turns stay verbatim** (**[fsnow]**: *"Last 4 turns"*); everything older goes into the summary. Where
`ContextLength` is unknown (a hand-typed model id, no listing) there is no automatic trigger, only the manual
action and a notice on the turn that says why.

[suggestion] Two consequences of 95% worth building in: the summary call itself has to fit in the remaining
5%, so the summariser sends only the turns being compacted, never the whole session; and the estimate is a
chars-per-token heuristic that runs worse on diacritic-heavy Pāli (AI_SURFACE_B §14), so a `ContextTooLong`
error from the provider should itself trigger a compaction-and-retry rather than surface as a dead turn.

The "resend only the last N exchanges" half of the tester's point 3 falls out of the same *N* with the summary
step turned off — it is a degenerate compaction, not a separate feature.

### 3.5 Take me back (P5)

**[fsnow]** *"A restored turn should be able to reopen its book and restore the selection, as something the
reader chooses rather than something that happens on load."*

An action on the turn: open `Citation.BookId` (the dock's existing open-or-activate path — the same one
`NavigateService` drives for outside agents), go to `Citation.NormalizedReference`'s paragraph anchor, then
apply the stored `ReadingPositionToken` through the #434 restore path. **The selection is not re-selected**
— #849 comment 3 already concluded the position token gives "most of the value" and the exact span may not be
recoverable; the control's label should promise the position, not the selection.

## 4. Phasing

Ordered by dependency. **UI-free** phases are `dotnet test`-verifiable and suitable for a worktree subagent;
UI phases are the maintainer's (standing pattern).

| # | Issue | Work | UI-free | Depends on |
|---|---|---|---|---|
| **P0** | #850 | The **+** (new conversation) control; remove `ClearCommand`/`Clear()` | ✗ (one button) | — |
| **P1** | #991 | Conversation: `History` on `AiTurnRequest`, replay in the orchestrator, `SentContext.History`, estimate over the whole request | ✅ | — |
| **P2** | #849 | `AiSession`/`AiTurnRecord` models, `IAiSessionStore` (load/save/list/delete, atomic writes, unreadable-file handling), reading-position capture at `StartTurn`, `ActiveAssistantSessionId` in `ApplicationState`, restore at launch | ✅ except the launch wiring | P1 |
| **P3** | new | Session service: new / switch / rename / delete / auto-name; the list, rename and delete UI | service ✅, panel ✗ | P2 |
| **P4** | new | Compaction: template, summariser, marker row, manual action, auto trigger from `ContextLength` | ✅ except the action | P1, P3 |
| **P5** | #849 | Take me back: open + go-to + position restore, as a turn action | ✗ (dock + WebView) | P2 |

**P1 is the walking skeleton.** With it alone, a follow-up question works for the first time; nothing else in
the plan is visible to a reader until P1 exists. It is also the phase most worth a Fable design pass before
building, because the message layout in §3.1 is the one decision that everything after it (persistence
format, compaction boundary, prompt-cache stability) is shaped by.

## 5. Testing

- **P1** — the fake-provider test that already asserts the assembled request (`AiChatOrchestrator` suite)
  gains cases for: history prepended in order; a failed turn without text omitted; reasoning never replayed;
  `SentContext.History` matches what was sent; the estimate covers the list.
- **P2** — `IAiSessionStore` against a temp directory (the `ApplicationStateService` test seam pattern):
  round-trip of every field; a truncated file is moved aside and reported, not thrown; save is atomic (no
  `.tmp` promoted over good data); restore builds `Turns` identical to the live ones. A golden session file
  checked in, so a format change is a visible diff.
- **P3** — service-level: auto-name rules; switch blocked while busy; delete removes the file and clears the
  active id when it was the active one.
- **P4** — with the fake provider: the boundary falls at *N*; the summary is the first user message; the
  auto trigger fires at the threshold and not below it; no trigger when `ContextLength` is null.
- **P5** — manual, on Egret; nothing here is headless-testable (dock + CEF).

## 6. Decisions — record

Decided by **[fsnow]** on 2026-09-12, answering the questions this plan raised. Where he chose one of the
options offered, the option's label is quoted; where he wrote his own answer, his words are.

1. **History layout** (§3.1) — *"Citation + question → answer"*: each prior turn replayed as its citation
   line plus the question, then the answer; the passage bundle sent once, for the current turn only.
2. **New conversation, not Clear** (§3.3) — *"'Clear' was introduced by Claude at some point and is not
   relevant. I would like to create new conversations like in Claude Code, maybe also with a plus button,
   while saving the current one."*
3. **On launch** — *"Restore the last session silently"*: the panel reloads the last active session's
   turns, no model call, the way books and reading positions are restored.
4. **The sent prompt is stored on disk** — *"Yes, in full"*.
5. **Naming** — *"Auto from the first turn, renamable"*.
6. **Scope** — *"One global list"*, not per book.
7. **Retention** — *"Forever, no cap"*. Deletion is manual, per session.
8. **Compaction** (§3.4) — *"Manual and auto at a fraction of context length"*; the fraction is
   *"95%, but make this a setting"*; *"Last 4 turns"* stay verbatim.
9. **Delete** — *"Yes, with confirmation"*, as an action on a row in the session list.
10. **#850** — *"Retitle it as the + / New conversation button"*: the issue is kept and its body rewritten;
    the dead `ClearCommand` is removed rather than bound.
