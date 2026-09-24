# Assistant sessions — conversation, persistence, naming, compaction

> **What exists:** the Assistant holds a conversation — earlier turns are replayed to the model (#991) — and
> every conversation is kept on disk, one file each, and reopened at launch (#849); the + starts a new one
> (#850); a session list switches, renames and deletes them (#997); older turns can be summarised by hand or
> automatically (#998). **Still open:** take me back (P5, §3.5, waiting on #1014 for the selection) and a
> shutdown drain for the turn in flight at Quit (§3.2).
>
> Provenance is marked throughout: **[fsnow]** is the maintainer's decision, **[suggestion]** is an agent's and
> advisory, **[observed]** is a fact from code or a named source. Unmarked text is context.

## 0. The request, and the model for it

A tester wrote (2026-09, quoted in full on the issue thread) asking for persistence of conversation history:

> *"Automatically save the conversation history to a local file (e.g., JSON) associated with each document
> or study session. Reload the history when reopening the document, resending it to the API to continue the
> conversation. Optional: resend only the last N exchanges (or a summary) to keep token costs down."*

**[fsnow]** *"I would like to make a plan to implement all of these features. Claude Code itself is my
model and we should aim for feature parity with CC in context management, named sessions, session
restoration, etc."*

So the target is the Claude Code session model, transposed to a reading app. §2 maps it feature by feature.

## 1. The finding that set the order of the work

**[observed] 2026-09-11: every Assistant turn was one-shot** — `AiChatOrchestrator.RunCoreAsync` sent a single
user message and `AiTurnRequest` had no field that could carry history, so the model never saw a previous turn,
even within a session. There was no conversation to persist, so **building the conversation (P1, #991) came
before persisting it (P2)**.

Two further facts from the same survey shaped the work:

- **[observed] The wire layer was already multi-turn.** `ChatRequest.Messages` is a list, `ChatRole` has
  `User` and `Assistant`, and both adapters (`OpenAiCompatibleProvider`, `AnthropicMessagesProvider`) iterate
  the list, so only the orchestrator's single-message construction stood between the app and a conversation.
- **[observed] A turn held the display strings but not the facts they were made from.** `AiTurnViewModel` had
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
  derived from them. Nothing bound in the panel changed. **[observed] 2026-09-12**, from building it.

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
reader has moved gets the citation, not the text. That limitation is stated on `AiTurnRequest.History`.

[observed] Mechanics, as built in #991:

- `AiTurnRequest.History` is an `IReadOnlyList<AiExchange>` (the asked line and the answer **as the model wrote
  it**, `[[…]]` markers intact — **[fsnow]**, before #991 merged, as recorded on `AiExchange`: *"I want to fix
  this before we merge."*). The
  orchestrator puts them ahead of the current turn in `ChatRequest.Messages`. The panel builds the list
  (`AiAssistantViewModel.HistoryFor`): turns with no answer text are not replayed; a turn with partial text is
  replayed as it is, since it stands on screen.
- `SentContext.History` (plus `HasHistory`) records the replayed messages, and the panel's Sent expander shows
  them above the message the turn sent, so "what did the model see" stays true.
- The token estimate runs over the whole message list, and the "Estimated context" field names the history's
  share — the input to automatic compaction (§3.4).
- Reasoning is never replayed: only the marked answer goes into an `AiExchange`.
- Retry re-asks with the history as it is now, not as it was when the retried turn was first sent.
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

**Still open: there is no shutdown drain.** [observed] 2026-09-13, re-checked 2026-09-23: the store is not
`IDisposable` and `SaveApplicationStateAsync` does not consult it, so (a) a turn still streaming when the reader
quits never reaches the `finally` that saves it — Stop keeps a partial answer, Quit does not — and (b) a
session's *first* turn ending inside the shutdown state save can be written after `ForceSaveAsync`, so the file
exists but `ActiveAssistantSessionId` never reaches disk. The same window is open for a crash within
`ApplicationStateService`'s 60-second save timer. (b) costs nothing but the automatic reopen: a session file
that is not the active one is listed like any other, and the reader reopens it from the session list — pinned by
`AiAssistantSessionListTests.A_conversation_nothing_points_at_is_listed_and_can_be_reopened`. (a) still loses
the turn in flight; the fix is a drain hook, not yet built.

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
answer published (`Blocks` exist only after `PublishAnswer`). No model call.

**[observed] A file that will not read is one of two things** (review fix, 2026-09-23). A file that was read and
**did not parse** is moved aside with a timestamp (the `application-state.unreadable-*` pattern), and a load the
reader asked for says where it was kept. A file that **could not be opened** — a permission, a sharing violation,
another process holding it — is left where it is and reported as `AiSessionUnreadable.Transient`: a listing skips
it that time, and a restore, switch, rename or delete the reader asked for says it "could not be opened just
now". Before the split, any read failure moved the file aside, and since the list is re-read after every turn,
one unlucky moment took a good conversation out of the list. Reads open with `FileShare.ReadWrite |
FileShare.Delete` so they do not block a concurrent replace or delete on Windows, and a save retries its
`File.Replace` once, after 100 ms, on an `IOException`; if that fails too with the session file gone, the complete
temp file is kept rather than cleaned up, since it is then the only copy [suggestion — all three reasoned from
the Windows file rules, not measured on Windows]. A restore that fails leaves `ActiveAssistantSessionId` naming
the file, so the next launch tries again. Whether the reader can delete it meanwhile depends on why: a file that
opened but could not be mapped (`Failed`) is still listed, so its row is there and deletable, and deleting it
clears the id; a file that could not be opened (`Unavailable`) is skipped by the listing too, so it has no row
until it opens.

**[observed] Launch sequencing.** The panel's view model is built during `CstDockFactory.CreateLayout`, which
runs *before* `LoadStateAsync` completes — so a constructor reading `ActiveAssistantSessionId` would read the
default empty state every launch. The restore therefore waits for the state load, like
`SearchViewModel.ApplyState` (#87) and `DictionaryViewModel.ApplyState` (#479), which exist for exactly this.
`AssistantRestoreTrigger` decides it: when the state load finishes (`App.LoadApplicationStateAsync`'s `finally`)
it restores if `CstDockFactory.AssistantEnabled()` is true at that moment, and `LayoutViewModel.ShowAssistantPanel`
restores a panel shown after that — the reader switching the Assistant on in Settings (#667). Both are checked
under one lock, so a toggle landing while the load finishes is caught by one or the other, and
`AiAssistantViewModel.RestoreOnceAsync` makes whichever comes second do nothing. The restore resolves the panel
only when the assistant is on, because resolving the singleton would otherwise *create* a panel for a reader who
has the feature switched off. It waits behind any rename, delete or switch in flight, and does not show a
conversation deleted while it was being read.

### 3.3 Sessions (P0, P3)

**There is no Clear.** **[fsnow]** *"'Clear' was introduced by Claude at some point and is not relevant. I
would like to create new conversations like in Claude Code, maybe also with a plus button, while saving the
current one."* So the control is **+** — new conversation: the current session is saved (it already is, at
every `EndTurn`), and the panel starts an empty one. [suggestion] Nothing is destroyed, so it asks for no
confirmation. #850 was rewritten to describe the + control.

**[observed] The + is bound to `NewConversationCommand`** — `IsBusy`-guarded like every other command, it clears
`Turns`, drops the session, and clears `ActiveAssistantSessionId`. It saves nothing, because every turn was
already written when it ended. `ClearCommand`/`Clear()` are gone.

**Deletion** is a separate action on a row in the session list, and it confirms — the one irreversible thing
here. **[fsnow]** chose *"Yes, with confirmation"*.

**Naming:** **[fsnow]** chose *"Auto from the first turn, renamable"* — `Explain · Mahāvaggapāḷi 1.1`, or the
first ~60 characters of a question; the reader can rename. No model-generated titles.

**The list:** newest-active first; each row shows name, last-active time, turn count, and the distinct books
its citations name. Click switches the panel to it (the in-flight turn, if any, blocks the switch, the way
`IsBusy` guards every other command). Rename and Delete on the row.

**[observed] The backend (#997, 2026-09-22; review fixes 2026-09-23)** — all on `AiAssistantViewModel`, tested in
`AiAssistantSessionListTests` and `AiAssistantSessionOverlapTests`. The view calls the methods directly
(`SwitchToSessionAsync`, `RenameSessionAsync`, `DeleteSessionAsync`); the `*Command` wrappers beside them are not
bound.

- `Sessions` — an `ObservableCollection<AiSessionRowViewModel>` in the store's order (`Id`, `Name`, `LastActive`,
  `TurnCount`, `BookIds`, `IsActive`, `CanDelete`), plus `HasSessions`. Refreshed after every save, rename,
  delete, switch, new conversation and restore; rows are updated in place and reordered rather than rebuilt, so
  a refresh at the end of a turn does not tear down an open rename box. Only the newest of overlapping refreshes
  is applied. The refresh after a turn is not awaited, so it runs outside the turn's busy window. `BookIds` are
  file names (`s0101m.mul.xml`) — the summary carries no book names.
- The store lists by reading every session file in full ([observed] review measurement: 361–488 ms and ~820 MB
  allocated per listing at 200 sessions × 30 turns). An index is a separate follow-up, not part of #997.
- `SwitchToSessionAsync(id)` — refused while `IsBusy`, and holds `IsBusy` itself while it reads. It
  loads and maps through the same `ReadTranscriptAsync` / `ShowSession` path the launch restore uses, so the two
  render identically and share the whole-or-nothing mapping and failure isolation. Switching to the conversation
  on screen does nothing; an unreadable, unopenable or missing target leaves the current one on screen and sets
  `Status`. Because a switch holds `IsBusy`, everything bound to it behaves as during a turn while the file is
  read: Stop shows (with nothing to stop), and the presets and + disable.
- `RenameSessionAsync(id, name)` — trimmed; empty or whitespace is refused and the old name kept. Works on the
  active session (renaming the object the next turn will save) and on any listed one. The rename writes a copy
  carrying the new name, and the held object takes the name only once that write succeeded — so a failed rename
  leaves nothing behind, and no other save (a turn ending, a compaction) can serialize a name not yet written. A
  turn's save that queued behind the rename's write, holding the old name, is followed by one more write of the
  held session.
  [suggestion] A rename does **not** move `LastActive`, so it does not reorder the list: "last active" is shown as
  when the conversation was last used, and a row that jumped to the top on rename would move out from under a
  reader working down the list. A rename of the conversation a switch is loading waits for the switch to land,
  then renames what it put on screen. Allowed while a turn runs.
- `DeleteSessionAsync(id)` — no confirmation here (the view asks). Deleting the conversation on screen leaves the
  panel as the + does, and it lets go before anything is awaited. A conversation application state names but the
  panel does not hold (its restore failed) loses that line and nothing on screen changes. Refused only for the
  conversation a turn is running in and the one a switch is loading — one predicate, `IsDeleteBlocked`, which the
  rows' `AiSessionRowViewModel.CanDelete` reads too, so a row never offers a Delete that does nothing. Whether the
  delete worked is decided by the store (its answer, then whether the file still loads; a file that cannot be
  opened just now is still there), never by the list.
- **Rename, delete, switch and the launch restore run one at a time** (`_sessionOps`, one lock for the panel
  [suggestion: per-id was not needed at these volumes]). Each reads a session file and then writes it, deletes it
  or adopts it, and two overlapping on one file undid each other: a delete landing between a rename's read and its
  write was written back by the rename, and a switch reading between them showed — and on the next turn saved — the
  old name. A turn's own save is not behind the lock.
- An unreadable file is announced in `Status` only for a load the reader asked for — the launch restore of the
  active conversation, a switch, a rename. One found while listing is handled by the store (§3.2) and logged.
- Enablement is bindable flags, not `canExecute` observables (the reason is recorded at `RetryCommand`'s
  construction): `CanSwitchSession`, `CanRenameSession`, and per-row `CanDelete`. The + button is enabled by
  `CanAsk` **and** `HasTurns` together (a `MultiBinding` with `BoolConverters.And` in the Kestrel UI) —
  **[fsnow]** chose *"Disabled when empty"* (2026-09-22, the option label he selected when asked on the Kestrel
  session's behalf).

**[observed] The view (#997 UI, 2026-09-22).** The panel's top row holds the + and, beside it, a switcher
labelled with the conversation on screen (`ActiveSessionName`, "New conversation" when none is). It opens a
flyout listing `Sessions`: each row shows the name, the last-active time, the turn count and the books, with
Rename and Delete icons. Rename opens an inline box (Enter keeps, Escape cancels). Delete asks inline, naming the
conversation, with Delete and Keep, as the Providers rows do. The flyout form, the inline confirmation and the row layout
are [suggestion]; the rows' rename and confirmation state lives on `AiSessionRowViewModel` so it survives the
list refresh after each answer.

### 3.4 Compaction (P4)

**[fsnow]** *"Manual and auto at a fraction of context length"*; the fraction *"95%, but make this a
setting"*; *"Last 4 turns"* stay verbatim. [suggestion] The reference taken is Claude Code's
`/compact [instructions]` and its auto-compact — an agent's reading of his *"Claude Code itself is my model and we
should aim for feature parity with CC in context management…"* (§0), not his wording.

**[fsnow] decisions, 2026-09-22** (the option labels he chose): compact-and-retry on a too-long rejection —
*"Keep it"*; the unknown-context notice — *"Every turn past 5"*.

**[observed] The backend (#998, 2026-09-22):**

- **One mechanism, two triggers.** The answered turns outside the summary in force, all but the last four
  (`AiCompaction.KeepVerbatim`), are summarised by the active provider and model, and the summary replaces them
  in what the next request replays. The transcript on screen keeps every turn — compaction changes what is
  **sent**, not what is **shown**.
- **The summariser** (`AiChatOrchestrator.CompactAsync`, and the same code mid-turn) sends **only the turns being
  compacted** — each one's stored asked line and its marked answer, `[[…]]` markers intact — plus the previous
  summary where there is one, as a single user message with no system prompt and no passage. Reasoning is
  dropped; a summary cut off at the output limit is refused rather than used. Every failure is an `AiError` on
  the result; nothing expected throws.
- **The template** is `Resources/Ai/compact.md` (`PromptTemplateNames.Compact`), embedded and user-overridable in
  `ai-templates/` like the presets. Its placeholders are its own (`PromptPlaceholders.AllowedFor`):
  `{{conversation}}`, `{{instructions}}`, `{{paliOpen}}`/`{{paliClose}}` required, `{{outputLanguage}}` allowed —
  so a preset edit cannot say `{{conversation}}` and render it as nothing. The prompt templates carry no version
  stamp, so neither does this one.
- **The summary's shape on the wire** [suggestion]: an ordinary replayed pair, first — the app's own line
  (`AiCompaction.SummaryAskedLine`, *«Earlier in this conversation» — summarise what we discussed before the turns
  that follow.*) as `user`, the summary as `assistant`. The model wrote the summary, so it comes back as its own
  words; the roles keep alternating and neither half is empty, which the Anthropic Messages API requires. Then
  the kept turns word for word, then the current turn. `SentContext.History` shows it exactly so, and the
  "Estimated context" field names it (*"a summary and 4 earlier turns"*).
- **A second compaction summarises the first** [suggestion]: the standing summary goes to the summariser with the
  turns since it, and the new summary replaces both — one summary per request, ever. Each `AiCompactionRecord`
  lists every turn it stands in for, the previous record's included, so the latest record alone is the
  boundary.
- **The record.** `AiSession.Compactions` holds `Id`, `AskedLine`, `Instructions`, `Automatic`, `ProviderId`,
  `ModelId` beside `When`, `Summary`, `SummarisedTurnIds`. A turn's `AiSentRecord.SummaryId` names the summary it
  was sent with, beside `ReplayedTurnIds` — by reference, like the turns — so a restored turn's Sent block rebuilds
  exactly what was sent, even after later compactions. Restore and switch hand the session's compactions to the
  panel, so the next turn replays the summary in force. Compaction is written at once (§3.2's cadence) and does
  not move `LastActive` [suggestion — the rename reasoning].
- **Manual:** `AiAssistantViewModel.CompactAsync` (instructions, or null to use `CompactInstructions`) — what the
  view's Summarise button calls — enabled by `CanCompact`: not while `IsBusy`, and only with more than four
  answered turns outside the summary in force. It holds `IsBusy` (and `IsCompacting`) while the model writes; Stop cancels
  it and nothing changes; a failure is one sentence in `Status` and nothing changes.
- **Automatic** (in the orchestrator, the only layer that knows the whole request's size): when the estimate the
  Sent block reports reaches `ChatSettings.AutoCompactPercent` of the resolved model's `ContextLength`, the turn
  compacts first — a `Compacting` event while the summary is written (the panel sets `IsCompacting` and the turn's
  status says *"Summarising earlier turns…"*), then `Compacted`, which the panel records, before `Started` —
  then sends. The summary call's tokens are folded into the turn's usage [suggestion: the reader paid for them as
  part of the turn] and also kept on the compaction record (`InputTokens`/`OutputTokens`); a manual compaction's
  are on its record only. The setting
  defaults to 95; **0 is off**; outside 0–100 `SettingsValidator` puts it back to 95 [suggestion]. It is getting
  a control in the Settings dialog (in progress on Kestrel, 2026-09-23) — **[fsnow]**, in the coordinating session,
  2026-09-22/23: *"yes, I intended that the
  setting would be in the Settings dialog"*. With
  `ContextLength` unknown there is no automatic trigger, and on every turn once there is something to compact
  (five or more answered turns) the turn carries a notice saying so (`AiChatOrchestrator.UnknownContextNotice`) —
  **[fsnow]** *"Every turn past 5"*, keeping the gate an agent had proposed. Past the threshold with nothing older
  than the last four, the turn goes as it is with a notice; a summary that fails leaves a notice and the whole conversation
  is sent.
- **Compact and retry** — proposed as a [suggestion], **[fsnow]** *"Keep it"* (2026-09-22): a `ContextTooLong`
  rejection before anything streamed compacts and sends again, once, while automatic compaction is on. The
  estimate assumes 2.0 characters per token (`AiTokens.PaliCharsPerToken`); `cl100k_base` measures 1.73, so on
  such a tokenizer the estimate comes out about 15% low in tokens — more than the 5% the default leaves — and with
  no published `ContextLength` the rejection is the only signal there is. The turn then carries a second `Started`
  describing the request that was answered, and its notices describe that request only: a threshold summary that
  failed before the retry is reported as having failed *at first*, never as "the whole conversation was sent".
- **For the view:** each `AiTurnViewModel` has `IsSummarised`; the first turn after the summarised span carries
  `CompactionMarker` (*"12 earlier turns summarised"*), `HasCompactionMarker` and `CompactionSummary`, so the
  marker row can be drawn from `Turns` alone and opened to show the summary. `CompactionSummary` is raw — the
  `[[…]]` Pāli markers are still in it, so the view strips them before display.
- **A compaction record with a blank summary** (a hand-edited file) counts as no compaction: the one before it, if
  any, stays in force.

The "resend only the last N exchanges" half of the tester's point 3 falls out of the same *N* with the summary
step turned off — a degenerate compaction, not built as a separate feature.

**[observed] The view (#998 UI, 2026-09-23).** A Compact icon in the panel's top row, between the session
switcher and the +, enabled by `CanCompact`; its flyout has an optional "what the summary should keep" box
(`CompactInstructions`, Enter runs it) and a Summarise button. "Summarising…" shows in the same row while
`IsCompacting`, for both triggers; Stop cancels. The marker row is an expander above the turn carrying
`CompactionMarker`, opening to `CompactionSummary` drawn as an answer is (markers stripped, Markdown rendered).
Summarised turns are drawn unchanged. The placement, the flyout and the marker opening to the summary itself
are [suggestion]; a turn sent after a compaction also shows the summary in its Sent block.

**The setting (2026-09-23).** **[fsnow]** *"I intended that the setting would be in the Settings dialog"*.
[observed] It is in Settings → AI, under the answer language: a checkbox *"Summarise earlier turns
automatically"*, then *"At [95] % of the model's context window"*. Unticked stores 0 (off); the percentage
(1–100) is kept for when it is ticked again. The shape and the wording are [suggestion], not his.

### 3.5 Take me back (P5)

**[fsnow]** *"A restored turn should be able to reopen its book and restore the selection, as something the
reader chooses rather than something that happens on load."*

Not built yet. [suggestion] An action on the turn: open `Citation.BookId` (the dock's existing open-or-activate
path — the same one `NavigateService` drives for outside agents), go to `Citation.NormalizedReference`'s
paragraph anchor, then apply the stored `ReadingPositionToken` through the #434 restore path.

**Re-selecting needs a primitive that does not exist yet, filed as #1014.** [observed] 2026-09-23: nothing in
the app sets a selection programmatically. The maintainer, 2026-09-23, on #1014: **[fsnow]** *"good. let's file
this for later"*. #1014's body carries his encoding for the selection; see the issue rather than a copy here.
[suggestion] P5 uses the primitive when it lands; where the selected text cannot be found, it falls back to the
paragraph plus the #434 position, and says so.

## 4. Phasing

Ordered by dependency. **UI-free** phases are `dotnet test`-verifiable and suitable for a worktree subagent;
UI phases are done by a Claude session on Kestrel, where the maintainer can preview them (standing pattern).

| # | Issue | Work | UI-free | Depends on |
|---|---|---|---|---|
| **P0** | #850 | The **+** (new conversation) control; remove `ClearCommand`/`Clear()` — **done** (§3.3) | ✗ (one button) | — |
| **P1** | #991 | Conversation: `History` on `AiTurnRequest`, replay in the orchestrator, `SentContext.History`, estimate over the whole request — **done** (§3.1) | ✅ | — |
| **P2** | #849 | `AiSession`/`AiTurnRecord` models, `IAiSessionStore` (load/save/list/delete, atomic writes, unreadable-file handling), reading-position capture at `StartTurn`, `ActiveAssistantSessionId` in `ApplicationState`, restore at launch — **done** (§3.2), except the shutdown drain | ✅ except the launch wiring | P1 |
| **P3** | #997 | Session list, switch, rename, delete (new and auto-name landed with P2) — **done** (§3.3): backend on the panel view model, UI in the panel's top row | ✅ | P2 |
| **P4** | #998 | Compaction: template, summariser, record, manual action, auto trigger from `ContextLength`, compact-and-retry — **done** (§3.4): backend, plus the Compact control and the marker row in the panel | ✅ | P1, P3 |
| **P5** | #849 | Take me back: open + go-to + position restore, as a turn action; re-selection when #1014 lands | ✗ (dock + WebView) | P2, #1014 |

## 5. Testing

- **P1** — the fake-provider tests that assert the assembled request (`AiChatOrchestrator` suite) cover:
  history prepended in order; a failed turn without text omitted; reasoning never replayed;
  `SentContext.History` matches what was sent; the estimate covers the list.
- **P2** — `AiSessionStoreTests`, against a temp directory (the `ApplicationStateService` test seam pattern):
  round-trip of every field; a truncated file is moved aside and reported, not thrown; a file that cannot be
  opened (no permission, or held exclusively) is left in place and reported as transient; save is atomic (no
  `.tmp` promoted over good data); restore builds `Turns` identical to the live ones. A golden session file is
  checked in, so a format change is a visible diff.
- **P3** — `AiAssistantSessionListTests` and `AiAssistantSessionOverlapTests`: auto-name rules; switch blocked
  while busy; delete removes the file and clears the active id; rename, delete and switch against each other; a
  failed rename not applied later; a row's Delete agreeing with the refusal; a restore once, whichever caller is
  first.
- **P4** — `AiCompactionOrchestratorTests` (a provider scripted per call) and `AiAssistantCompactionTests`: the
  boundary at four from both sides; the summary first, roles alternating, nothing empty; the summariser sent only
  the compacted turns, markers intact; the trigger at the threshold and not one token below; no trigger and a
  notice when `ContextLength` is null; a second compaction; compact-and-retry once; summarised turns kept on
  screen; the stored record rebuilding the sent history after a restore; manual blocked while busy and below five
  turns; the setting's default and repair.
- **P5** — manual, on Egret; nothing here is headless-testable (dock + CEF).

## 6. Decisions — record

Decided by **[fsnow]** on 2026-09-12, answering the questions the design raised. Where he chose one of the
options offered, the option's label is quoted; where he wrote his own answer, his words are.

1. **History layout** (§3.1) — *"Citation + question → answer"*. [suggestion, the option's description as the
   agent wrote it] Each prior turn is replayed as its citation line plus the question, then the answer; the
   passage bundle is sent once, for the current turn only.
2. **New conversation, not Clear** (§3.3) — *"'Clear' was introduced by Claude at some point and is not
   relevant. I would like to create new conversations like in Claude Code, maybe also with a plus button,
   while saving the current one."*
3. **On launch** — *"Restore the last session silently"*. [observed] As built, the panel reloads the last active
   session's turns with no model call, pushed in after application state loads like the other panels' restores
   (§3.2).
4. **The sent prompt is stored on disk** — *"Yes, in full"*.
5. **Naming** — *"Auto from the first turn, renamable"*.
6. **Scope** — *"One global list"*, not per book.
7. **Retention** — *"Forever, no cap"*. [observed] Nothing deletes a session except the reader's Delete on its
   row (decision 9).
8. **Compaction** (§3.4) — *"Manual and auto at a fraction of context length"*; the fraction is
   *"95%, but make this a setting"*; *"Last 4 turns"* stay verbatim.
9. **Delete** — *"Yes, with confirmation"*. [observed] It is an action on a row in the session list (§3.3).
10. **#850** — *"Retitle it as the + / New conversation button"*. [observed] The issue was kept and its body
    rewritten; the dead `ClearCommand` was removed rather than bound.
