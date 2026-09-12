# Assistant sessions — conversation, persistence, naming, compaction (Planned)

> Plan of record for #849 (retain and restore Assistant turns) and #850 (no Clear control), drafted
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
- **[observed] A turn already carries everything a stored record needs.** `AiTurnViewModel` holds the task,
  question, raw answer, reasoning, citation, notices, partial-passage flag, usage, elapsed, and the
  `SentContext` (#665) with both prompt halves. `CitationRef` carries `BookId` + `NormalizedReference`;
  `ReadingPositionToken` (#434) is the persisted position format. Nothing new has to be *computed* for
  persistence — only written down.

**[observed] `ClearCommand` is bound to nothing** (#850). `AiAssistantPanel.axaml` has no Clear control; the
command exists, is guarded against `IsBusy`, and is unreachable.

## 2. Claude Code parity map

What Claude Code does, and what the equivalent is in a reader whose "project" is a passage rather than a
directory. Items marked *skip* are deliberately not carried over.

| Claude Code | CST Reader equivalent | Phase |
|---|---|---|
| Every turn is appended to a transcript file as it completes; nothing is lost on a crash | One file per session under `CSTReader/assistant-sessions/`, written at the end of each turn | P2 |
| Prior turns are in the model's context; follow-ups work | History replayed as `user`/`assistant` pairs in `ChatRequest.Messages` | **P1** |
| `--continue` — pick up the most recent session | On launch, the panel reloads the last active session (no model call) | P2 |
| `/clear` — start a new session; the old one stays on disk | **New conversation** control. The transcript empties; nothing is deleted | P0 (#850) |
| Auto-title from the first prompt; `/rename` | Auto-name from the first turn (preset + citation, or the question); rename in the panel | P3 |
| `--resume` / `/resume` picker: title, when, how many messages | Session list in the panel: name, last active, turn count, books touched; switch by clicking | P3 |
| `/compact [instructions]` — summarise older turns, keep recent ones verbatim | **Compact** action + a compaction prompt template; the summary becomes the first message | P4 |
| Auto-compact near the context window | Automatic when the estimated request exceeds a fraction of the model's `ContextLength` (`AiModelRecord.ContextLength`, [observed] already stored from the listing) | P4 |
| `/context` — how full the window is | The per-turn "Estimated context" field (#665/#672) gains the history's share | P1 |
| Sessions expire after `cleanupPeriodDays` (30) | Retention **undecided** — see §6 | — |
| Resuming forks a new session id | *skip* — a reader resumes in place; forking is a developer concern | — |
| `/rewind`, checkpoints | *skip* — nothing in a reading session to roll back to | — |
| Sessions are per project directory | *One global list*, not per book — see §6 | — |

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
- `SentContext` (#665) must show the replayed messages, or "what did the model see" stops being true. Add a
  `Messages` list to `SentContext`; the panel's expander renders them under the two prompt halves.
- The token estimate (`AiTokens.Estimate`) runs over the whole message list, and the "Estimated context"
  field says how much of it is history — the input to auto-compaction in P4.
- Reasoning is **never** replayed. It was segregated from the answer for a reason (§8 of the design doc).
- Retry re-asks with the history *as it is now*, not as it was. Simpler, and what a reader pressing "Try
  again" on a 504 wants.
- **Prompt caching** (Anthropic `cache_control` on the stable prefix) is exactly what a replayed history
  benefits from. Not in this plan; noted so nobody designs the message layout in a way that defeats it —
  the system prompt and older history must stay byte-stable between turns, which the layout above does.

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
| task, question | `AiTurnViewModel` | |
| citation (`CitationRef`) | `AiTurnContext.Citation` | **required** — #849 comment 2: this is what makes "take me back" possible |
| subject (selection summary) | `Subject` | display text only; the full selection is in the sent prompt |
| answer (raw), reasoning | builders | reasoning is shown collapsed today; storing it keeps the turn faithful |
| notices, partial-passage flag | `Notices`, `IsPartialPassage` | |
| usage, elapsed, status, failed | | |
| sent context (`SentContext`) | #665 | see the decision in §6 — [suggestion] store it |
| reading position (`ReadingPositionToken`) | captured from the active book at `StartTurn` | #849 comment 3; **new capture**, the only new data |
| provider id, model id, when | | the model that answered is part of the record |

**Restore:** at launch the panel reads the active session and rebuilds `Turns` from records — a
constructor `AiTurnViewModel(AiTurnRecord)` alongside the live one, with `IsRunning = false`. No model call.
A session file that fails to parse is moved aside with a timestamp (the `application-state.unreadable-*`
pattern) and the panel starts empty with a notice, never a crash.

### 3.3 Sessions (P0, P3)

**Clear means "new conversation", and never "delete".** [suggestion] This answers #850's open question the
way Claude Code answers it: `/clear` starts a new session and the old one remains resumable. So the control
needs no confirmation — nothing is destroyed. Deletion is a separate, explicit action on an entry in the
session list, and that one confirms. **Two controls, two meanings; neither quietly means both.**

For #850 today, before sessions exist: bind the existing `ClearCommand` to a control labelled **New
conversation** rather than **Clear**. Its behaviour today (empty the transcript) is a special case of its
behaviour later (start a session), and the label survives P3 unchanged.

**Naming:** a session is auto-named from its first turn — `Explain · Mahāvaggapāḷi 1.1`, or the first 60
characters of a question — and renamable. [suggestion] No model-generated titles: they cost a call per
session for a name the reader can type, and the free-tier endpoints this app is tested against are the ones
least able to afford it.

**The list:** newest-active first; each row shows name, last-active time, turn count, and the distinct books
its citations name. Click switches the panel to it (the in-flight turn, if any, blocks the switch exactly as
`Clear()` is blocked by `IsBusy`). Rename and Delete on the row.

### 3.4 Compaction (P4)

Manual **Compact** and automatic compaction share one mechanism: the older turns are summarised by the active
model through a compaction prompt template (a sixth embedded template beside the five presets, user-editable
like them), the summary replaces them in the *history the model sees*, and the most recent *N* turns stay
verbatim. The transcript on screen keeps every turn — compaction changes what is **sent**, not what is
**shown** — with a marker row where the boundary falls (*"12 earlier turns summarised"*), which the `Sent`
expander can open.

Automatic trigger: when the estimated request would exceed a fraction of `ContextLength` for the resolved
model. Where `ContextLength` is unknown (a hand-typed model id, no listing) there is no automatic trigger,
only the manual action and a notice on the turn that says why. The fraction and *N* are numbers **[fsnow]**
has not chosen and this plan does not invent (see §6).

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
| **P0** | #850 | Bind `ClearCommand` to a **New conversation** control | ✗ (one button; XAML only) | — |
| **P1** | new | Conversation: `History` on `AiTurnRequest`, replay in the orchestrator, `SentContext.Messages`, estimate over the whole request | ✅ | — |
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
  `SentContext.Messages` matches what was sent; the estimate covers the list.
- **P2** — `IAiSessionStore` against a temp directory (the `ApplicationStateService` test seam pattern):
  round-trip of every field; a truncated file is moved aside and reported, not thrown; save is atomic (no
  `.tmp` promoted over good data); restore builds `Turns` identical to the live ones. A golden session file
  checked in, so a format change is a visible diff.
- **P3** — service-level: auto-name rules; switch blocked while busy; delete removes the file and clears the
  active id when it was the active one.
- **P4** — with the fake provider: the boundary falls at *N*; the summary is the first user message; the
  auto trigger fires at the threshold and not below it; no trigger when `ContextLength` is null.
- **P5** — manual, on Egret; nothing here is headless-testable (dock + CEF).

## 6. Decisions for the maintainer

Each with the agent's recommendation, marked. None is decided.

1. **Restore the last session on launch, or open empty with a Resume affordance?** [suggestion] Restore —
   the app restores books, layout and reading positions silently, and an answer about a passage is part of
   where the reader was (#849's own argument). Claude Code's default is a fresh session with `--continue`
   opt-in, but a CLI has no persistent window to restore into.
2. **History layout** (§3.1): citation line + question + answer per prior turn, bundle for the current turn
   only. [suggestion] Yes; the alternative re-sends the passage every turn.
3. **Store the sent prompt (`SentContext`) on disk?** It contains passage text. [suggestion] Yes — it is
   corpus text on the reader's own machine, the §10 privacy concern is about *sending*, and a restored turn
   without it cannot answer "what did the model see", which is what #665 exists for. Cap per turn if size
   is a concern.
4. **Clear = new conversation, delete lives in the list** (§3.3). [suggestion] Yes.
5. **Retention.** Claude Code deletes after 30 days by default. [suggestion] Leave it unset — no cap, no
   expiry — until a reason appears; a study transcript is not a build log. The setting is a placeholder.
6. **Auto-name locally, never by the model** (§3.3). [suggestion] Yes.
7. **Compaction numbers** — the context fraction that triggers it and the *N* turns kept verbatim. Not
   proposed; they are yours, and a reasonable first value would come from watching the estimate field over a
   real session.
8. **One global session list, not per book.** [suggestion] Global — a reader's study session crosses mūla
   and aṭṭhakathā, and the list shows which books each session touched. Per-book *filtering* of the list can
   come later without changing the store.
9. **Sequencing #850.** [suggestion] Ship the P0 button now as *New conversation* (a five-line XAML change)
   rather than holding it for P3; it fixes the bug in the milestone without a label that P3 would rename.
