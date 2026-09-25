using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.ViewModels;
using Moq;
using Xunit;

// The stubs and context helpers the live-turn suite next door already owns. Shared rather than copied: a
// second StubOrchestrator would drift from the first, and most of what is asserted here is that a restored turn
// equals a live one — which is only worth asserting if both were built the same way.
using static CST.Avalonia.Tests.ViewModels.AiAssistantViewModelTests;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// The Assistant panel's conversation, written down and read back. (#849 P2 wiring)
///
/// <para><b>[fsnow]</b>: <i>"Restore the last session silently"</i> — so the behaviour under test is a panel
/// that reopens where it was left, with no model call and nothing asked of the reader. Two properties carry
/// most of the weight: <b>what a record holds</b> (everything the panel shows, plus the structured originals
/// the display strings were made from), and <b>that a restored turn is indistinguishable from a live one</b> —
/// including to <c>HistoryFor</c>, because a follow-up after a restart has to continue the conversation rather
/// than start one.</para>
///
/// <para>The store is faked rather than pointed at a temp directory: <c>AiSessionStoreTests</c> already covers
/// the file layer against a real one, and what is left to pin here is the panel's side of the contract — when
/// it saves, what it puts in the record, and what it does when a save fails.</para>
/// </summary>
public class AiAssistantSessionWiringTests
{
    /// <summary>
    /// A store that records rather than writes. <b>Answers, never throws</b> — the same contract the real one
    /// keeps, which is what makes "a failed save is a sentence" testable at all.
    ///
    /// <para><b>It also keeps a disk</b> (#997): every successful save is kept as its serialized JSON, through the
    /// real store's own <see cref="AiSessionStore.JsonOptions"/>, and loading, listing and deleting work from
    /// that. A copy rather than the object the panel handed in, because the panel mutates its session after
    /// saving it — a list or a switch reading the live object would see changes no file holds. Shared with
    /// <c>AiAssistantSessionListTests</c>, which is what it was widened for.</para>
    /// </summary>
    internal sealed class FakeStore : IAiSessionStore
    {
        internal AiSession? ToLoad { get; set; }

        /// <summary>Fired from <see cref="LoadAsync"/> when set, where the real store fires it.</summary>
        internal AiSessionUnreadable? ReportOnLoad { get; set; }

        internal string? AskedFor { get; private set; }
        internal bool SaveSucceeds { get; set; } = true;

        /// <summary>Every session handed to <see cref="SaveAsync"/>, by reference — the panel mutates one
        /// object, so how many turns it held AT each save is recorded separately.</summary>
        internal List<AiSession> Saves { get; } = new();

        internal List<int> TurnCountAtSave { get; } = new();

        internal AiSession? LastSaved => Saves.Count == 0 ? null : Saves[^1];

        /// <summary>Taken by the NEXT <see cref="LoadAsync"/>, which reads the disk first and then waits on it
        /// before answering — so a test can act on the panel while a restore or switch is in flight (the
        /// check-then-await window the guards close), and a later load is not held with it.</summary>
        internal TaskCompletionSource? Gate { get; set; }

        /// <summary>Raised from a thread-pool thread, where the real store raises it: from the catch in its own
        /// read, after an <c>await … ConfigureAwait(false)</c>. <see cref="ReportOnLoad"/> fires on the caller's
        /// thread instead, which is why it could not see the defect this exists for.</summary>
        internal AiSessionUnreadable? ReportFromThreadPool { get; set; }

        /// <summary>Sessions as they were last written, by id, as JSON.</summary>
        internal Dictionary<string, string> Disk { get; } = new(StringComparer.Ordinal);

        /// <summary>Ids whose file will not parse: loading one moves it off the disk and reports it, as the real
        /// store does.</summary>
        internal HashSet<string> UnreadableIds { get; } = new(StringComparer.Ordinal);

        /// <summary>Ids whose file is there but cannot be OPENED just now — a permission, a sharing violation. A load
        /// or a listing reports it as transient and leaves it on the disk, as the real store now does. (review,
        /// finding 1)</summary>
        internal HashSet<string> TransientIds { get; } = new(StringComparer.Ordinal);

        /// <summary>Like <see cref="Gate"/>, one per load in order, for tests that must hold two loads at once —
        /// a rename's and then a switch's.</summary>
        internal Queue<TaskCompletionSource> Gates { get; } = new();

        internal bool DeleteFails { get; set; }

        /// <summary>
        /// While set, every listing snapshots the disk and then waits to be released by its index in
        /// <see cref="HeldListings"/> — so a test can hold listings open and let a newer one finish first, the
        /// overlap the real store's file reads make possible.
        /// </summary>
        internal bool HoldListings { get; set; }

        internal List<TaskCompletionSource> HeldListings { get; } = new();

        /// <summary>Raised from a thread-pool thread by every listing, as the real store does when it trips over a
        /// broken file while listing.</summary>
        internal AiSessionUnreadable? ListReportsUnreadable { get; set; }
        internal int LoadCalls { get; private set; }
        internal int ListCalls { get; private set; }
        internal List<string> Deleted { get; } = new();

        /// <summary>Put a session on the disk without the panel having written it — another launch's work.</summary>
        internal void Seed(AiSession session) =>
            Disk[session.Id] = System.Text.Json.JsonSerializer.Serialize(session, AiSessionStore.JsonOptions);

        /// <summary>A session as it stands on the disk now: a fresh copy, never the object that was saved.</summary>
        internal AiSession? OnDisk(string id) =>
            Disk.TryGetValue(id, out var json)
                ? System.Text.Json.JsonSerializer.Deserialize<AiSession>(json, AiSessionStore.JsonOptions)
                : null;

        public async Task<AiSession?> LoadAsync(string id, CancellationToken cancellationToken = default)
        {
            AskedFor = id;
            LoadCalls++;
            if (ReportOnLoad is { } report) Unreadable?.Invoke(report);
            if (ReportFromThreadPool is { } offThread)
                await Task.Run(() => Unreadable?.Invoke(offThread)).ConfigureAwait(false);
            if (UnreadableIds.Contains(id))
            {
                Disk.Remove(id);
                Unreadable?.Invoke(new AiSessionUnreadable(id, $"/kept/{id}.unreadable-1.json", "truncated"));
                return null;
            }

            if (TransientIds.Contains(id) && Disk.ContainsKey(id))
            {
                Unreadable?.Invoke(new AiSessionUnreadable(id, $"/sessions/{id}.json", "denied", Transient: true));
                return null;
            }

            // Read BEFORE waiting, as a real read would have: what a held load answers is what the file said when
            // it was read, whatever has been written since.
            var read = ToLoad ?? OnDisk(id);

            if (Gate is { } gate)
            {
                Gate = null;
                await gate.Task;
            }
            else if (Gates.Count > 0)
            {
                await Gates.Dequeue().Task;
            }

            return read;
        }

        /// <summary>Taken by the NEXT save: it snapshots the session at once, then waits, and succeeds or fails with
        /// the value it is released with. Saves called after it land after it, as the real store's write lock
        /// orders them — so a test can hold a rename's write while a turn's save queues behind it.</summary>
        internal TaskCompletionSource<bool>? HoldNextSave { get; set; }

        private Task _writes = Task.CompletedTask;

        public async Task<bool> SaveAsync(AiSession session, CancellationToken cancellationToken = default)
        {
            Saves.Add(session);
            TurnCountAtSave.Add(session.Turns.Count);

            // Serialized now, as the real store does before it waits for its lock.
            var json = System.Text.Json.JsonSerializer.Serialize(session, AiSessionStore.JsonOptions);
            var id = session.Id;

            var previous = _writes;
            var mine = new TaskCompletionSource();
            _writes = mine.Task;

            bool ok;
            if (HoldNextSave is { } hold)
            {
                HoldNextSave = null;
                ok = await hold.Task;
            }
            else
            {
                ok = SaveSucceeds;
            }

            await previous;
            if (ok) Disk[id] = json;
            mine.SetResult();
            return ok;
        }

        /// <summary>Newest-active first, id breaking ties — the real store's order, which the panel must keep
        /// rather than impose its own.</summary>
        public async Task<IReadOnlyList<AiSessionSummary>> ListAsync(CancellationToken cancellationToken = default)
        {
            ListCalls++;
            var snapshot = Snapshot();

            if (ListReportsUnreadable is { } report)
                await Task.Run(() => Unreadable?.Invoke(report)).ConfigureAwait(false);

            if (HoldListings)
            {
                var held = new TaskCompletionSource();
                HeldListings.Add(held);
                await held.Task;
            }

            return snapshot;
        }

        private IReadOnlyList<AiSessionSummary> Snapshot()
        {
            var summaries = Disk.Keys
                .Where(id => !TransientIds.Contains(id))
                .Select(id => OnDisk(id)!)
                .Select(s => new AiSessionSummary(
                    s.Id, s.Name, s.Created, s.LastActive, s.Turns.Count,
                    s.Turns.Select(t => t.Citation?.BookId).Where(b => !string.IsNullOrEmpty(b))
                        .Distinct().ToList()!))
                .OrderByDescending(s => s.LastActive)
                .ThenBy(s => s.Id, StringComparer.Ordinal)
                .ToList();
            return summaries;
        }

        public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
        {
            if (DeleteFails) return Task.FromResult(false);
            Deleted.Add(id);
            return Task.FromResult(Disk.Remove(id));
        }

        public event Action<AiSessionUnreadable>? Unreadable;
    }

    /// <summary>Streams a little and is then cancelled — the reader's own Stop, which the stub next door cannot
    /// express because it ends by running out of events rather than by being cancelled.</summary>
    private sealed class StoppedOrchestrator : IAiChatOrchestrator
    {
        public async IAsyncEnumerable<AiTurnEvent> RunAsync(
            AiTurnRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return AiTurnEvent.ForStarted(Context());
            yield return AiTurnEvent.ForText("Half an answer", "Half an answer");
            await Task.Yield();
            throw new OperationCanceledException();
        }

        public void Stop()
        {
        }
    }

    /// <summary>Holds a turn open at a known point, so "while a turn is running" is a state a test can be in
    /// rather than a race it has to win.</summary>
    internal sealed class BlockingOrchestrator : IAiChatOrchestrator
    {
        internal TaskCompletionSource Gate { get; } = new();

        public async IAsyncEnumerable<AiTurnEvent> RunAsync(
            AiTurnRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return AiTurnEvent.ForStarted(Context());
            await Gate.Task;
            yield return AiTurnEvent.ForCompleted(new PaliMarkerReport(0, 0));
        }

        public void Stop()
        {
        }
    }

    internal static (AiAssistantViewModel Vm, FakeStore Store, ApplicationState State,
        Mock<IApplicationStateService> StateService) Panel(
            IAiChatOrchestrator orchestrator, StubReaderState? reader = null, FakeStore? store = null,
            ApplicationState? state = null)
    {
        store ??= new FakeStore();
        state ??= new ApplicationState();

        var stateService = new Mock<IApplicationStateService>();
        stateService.SetupGet(x => x.Current).Returns(state);

        var vm = new AiAssistantViewModel(
            orchestrator, reader ?? new StubReaderState(), null, null, null, null, store, stateService.Object);

        return (vm, store, state, stateService);
    }

    /// <summary>What #665 records about a request. Non-null here because the replayed-turn ids live inside it —
    /// a turn that sent nothing has nothing to say about what it replayed either.</summary>
    private static SentContext Sent() =>
        new(new[] { new SentField("Provider", "openrouter"), new SentField("Model", "some/model") },
            "The system prompt.", "The user message.");

    /// <summary>A context that also names the connection that answered — what #849 added to
    /// <c>AiTurnContext</c> so a stored answer is attributable.</summary>
    internal static AiTurnContext ContextFrom(string providerId = "openrouter", string modelId = "some/model") =>
        new(AiTask.Explain, "English", Citation(),
            new BookContext("s0101m.mul.xml", "book", CST.Pitaka.Sutta, CST.CommentaryLevel.Mula),
            Array.Empty<string>(), false, Sent(), providerId, modelId);

    internal static StubOrchestrator AnsweringFrom(AiTurnContext context, params AiTurnEvent[] middle)
    {
        var orchestrator = new StubOrchestrator();
        orchestrator.Events.Add(AiTurnEvent.ForStarted(context));
        orchestrator.Events.AddRange(middle);
        orchestrator.Events.Add(AiTurnEvent.ForCompleted(new PaliMarkerReport(0, 0)));
        return orchestrator;
    }

    // ---- Saving ----------------------------------------------------------------------------------

    /// <summary>
    /// The first turn to END creates the conversation, names it from itself, and tells application state which
    /// file to reopen. <b>[fsnow]</b> chose <i>"Auto from the first turn, renamable"</i>.
    /// </summary>
    [Fact]
    public async Task The_first_turn_creates_a_named_session_and_records_which_one_is_active()
    {
        var (vm, store, state, stateService) = Panel(Answering(Said("Heedfulness is the path.")));

        await vm.AskAsync(AiTask.Explain);

        var saved = Assert.IsType<AiSession>(store.LastSaved);

        // Named from the turn: the preset that was pressed and the passage it was about. Not by a model —
        // nothing about a name costs a call.
        Assert.Equal(
            $"Explain · {AiAssistantViewModel.Describe(Citation())}",
            saved.Name);
        Assert.Single(saved.Turns);
        Assert.NotEqual(default, saved.Created);

        // One string in application state, so the next launch knows which transcript to read.
        Assert.Equal(saved.Id, state.ActiveAssistantSessionId);
        stateService.Verify(x => x.MarkDirty(), Times.AtLeastOnce);
    }

    /// <summary>
    /// A typed question names its own conversation. The preset label and the citation would be identical on
    /// every question asked about one passage, and the words are what the reader recognises in a list.
    /// </summary>
    [Fact]
    public async Task A_question_names_its_conversation_by_its_own_first_words()
    {
        var (vm, store, _, _) = Panel(Answering(Said("It means vigilance.")));

        vm.Question = new string('a', 40) + " " + new string('b', 40);
        await vm.AskAsync(AiTask.Ask);

        var name = store.LastSaved!.Name;

        // 60 characters and an ellipsis, cut at the end rather than elided in the middle: a name is read from
        // its start, and a list of names sharing a prefix is what the reader is scanning.
        Assert.StartsWith(new string('a', 40), name);
        Assert.EndsWith("…", name);
        Assert.Equal(61, name.Length);
    }

    /// <summary>
    /// Every turn rewrites the WHOLE session, which is why a crash costs the turn in flight and nothing else.
    /// </summary>
    [Fact]
    public async Task Each_turn_that_ends_rewrites_the_whole_conversation()
    {
        var (vm, store, _, _) = Panel(Answering(Said("An answer.")));

        await vm.AskAsync(AiTask.Explain);
        await vm.AskAsync(AiTask.Translate);
        await vm.AskAsync(AiTask.Grammar);

        Assert.Equal(new[] { 1, 2, 3 }, store.TurnCountAtSave);

        // One conversation, added to — not three files.
        Assert.Single(store.Saves.Select(s => s.Id).Distinct());
        Assert.Equal(3, store.LastSaved!.Turns.Count);
    }

    /// <summary>
    /// A stopped turn is stored with whatever had arrived. It is on screen and the reader is reading it, so it
    /// belongs in the record — and the reader's own cancellation must not be what breaks the save that keeps it.
    /// </summary>
    [Fact]
    public async Task A_stopped_turn_is_saved_with_the_text_that_had_arrived()
    {
        var (vm, store, _, _) = Panel(new StoppedOrchestrator());

        await vm.AskAsync(AiTask.Explain);

        var record = Assert.Single(store.LastSaved!.Turns);
        Assert.Equal("Half an answer", record.Answer);
        Assert.Equal("Half an answer", record.MarkedAnswer);
        Assert.Equal("Stopped.", record.Status);
        Assert.False(record.Failed);
    }

    /// <summary>A failed turn is stored too, marked as one — a transcript that dropped its failures would reopen
    /// claiming a conversation went better than it did.</summary>
    [Fact]
    public async Task A_failed_turn_is_saved_and_marked_failed()
    {
        var orchestrator = new StubOrchestrator();
        orchestrator.Events.Add(AiTurnEvent.ForStarted(Context()));
        orchestrator.Events.Add(Said("Part of an answer"));
        orchestrator.Events.Add(AiTurnEvent.ForError(new AiError(AiErrorKind.Network, "The connection dropped.")));
        var (vm, store, _, _) = Panel(orchestrator);

        await vm.AskAsync(AiTask.Explain);

        var record = Assert.Single(store.LastSaved!.Turns);
        Assert.Equal("Part of an answer", record.Answer);
        Assert.True(record.Failed);
        Assert.Equal("The connection dropped.", record.Status);
    }

    /// <summary>
    /// The record carries what the view model only DISPLAYED — the structured citation, the connection that
    /// answered, the token counts as numbers, the duration as a duration, and both halves of the answer.
    ///
    /// <para>Every one of these was dropped before the wiring: the citation arrived once on <c>Started</c> and
    /// was replaced by two rendered lines, the usage report was formatted and discarded, elapsed existed only
    /// as <c>"4.2s"</c>, and the provider and model ids were on nothing at all.</para>
    /// </summary>
    [Fact]
    public async Task The_record_carries_the_structured_facts_the_panel_only_rendered()
    {
        var orchestrator = AnsweringFrom(
            ContextFrom(),
            AiTurnEvent.ForText("The term appamada matters.", "The term [[appamada]] matters."),
            AiTurnEvent.ForReasoning("Thinking about the compound."),
            AiTurnEvent.ForUsage(new AiUsageReport(1024, 256)));
        var (vm, store, _, _) = Panel(orchestrator);

        await vm.AskAsync(AiTask.Explain);

        var record = Assert.Single(store.LastSaved!.Turns);

        // The citation as data, not as the line drawn from it: this is what a later "take me back" (P5) opens.
        Assert.Equal("s0101m.mul.xml", record.Citation!.BookId);
        Assert.Equal("para 12", record.Citation.NormalizedReference);

        Assert.Equal("openrouter", record.ProviderId);
        Assert.Equal("some/model", record.ModelId);

        // Numbers, not the line printed from them: that line has been through :N0 and reads differently on a
        // machine with different digit grouping, and nothing can be added up from it afterwards.
        Assert.Equal(1024, record.InputTokens);
        Assert.Equal(256, record.OutputTokens);

        Assert.NotNull(record.ElapsedMs);
        Assert.True(record.ElapsedMs >= 0);

        // Both halves: the stripped form is what the panel renders, the marked form is what a later turn
        // replays to the model. (#991)
        Assert.Equal("The term appamada matters.", record.Answer);
        Assert.Equal("The term [[appamada]] matters.", record.MarkedAnswer);
        Assert.Equal("Thinking about the compound.", record.Reasoning);

        // The question side as the model was shown it, stored rather than left to be re-rendered later.
        Assert.Equal(AiAssistantViewModel.DescribeAsked(vm.LastTurn!), record.AskedLine);

        // And what was sent, in full — [fsnow]: "Yes, in full".
        Assert.Equal("The system prompt.", record.Sent!.SystemPrompt);
        Assert.Equal("The user message.", record.Sent.UserContent);
        Assert.Equal(2, record.Sent.Fields.Count);

        Assert.NotEqual(default, record.When);
    }

    /// <summary>
    /// The reading position is taken at turn START. <b>[fsnow]</b>: <i>"The Assistant's memory should carry the
    /// same scroll context saved elsewhere: two anchors and a fraction between them."</i>
    ///
    /// <para>At the start and not the end, because by the time an answer arrives the reader may have scrolled
    /// on — and the position a stored turn asks to be taken back to is the one it was asked from.</para>
    /// </summary>
    [Fact]
    public async Task The_reading_position_at_turn_start_is_stored_with_the_turn()
    {
        var reader = new StubReaderState
        {
            Result = ReaderStateResult.Ok(new ReaderState(
                "s0101m.mul.xml", 12, null,
                ReadingPosition: new ReadingPositionToken
                {
                    Above = "para12", Below = "para13", Fraction = 0.25,
                })),
        };
        var (vm, store, _, _) = Panel(Answering(Said("An answer.")), reader);

        await vm.AskAsync(AiTask.Explain);

        var position = Assert.IsType<ReadingPositionToken>(store.LastSaved!.Turns[0].ReadingPosition);
        Assert.Equal("para12", position.Above);
        Assert.Equal("para13", position.Below);
        Assert.Equal(0.25, position.Fraction);
    }

    /// <summary>A turn asked before the anchor cache had built keeps everything else. A reading position is
    /// worth having and no turn is worth refusing for the want of one.</summary>
    [Fact]
    public async Task A_turn_with_no_reading_position_is_still_stored()
    {
        var (vm, store, _, _) = Panel(Answering(Said("An answer.")));

        await vm.AskAsync(AiTask.Explain);

        Assert.Null(store.LastSaved!.Turns[0].ReadingPosition);
        Assert.Equal("An answer.", store.LastSaved.Turns[0].Answer);
    }

    /// <summary>
    /// The record names the turns that were ACTUALLY replayed, by id — not by copying their answers into it, and
    /// not by recomputing later which turns would be replayed now.
    /// </summary>
    [Fact]
    public async Task The_record_names_the_turns_that_were_replayed_ahead_of_it()
    {
        var orchestrator = AnsweringFrom(ContextFrom(), Said("An answer."));
        var (vm, store, _, _) = Panel(orchestrator);

        await vm.AskAsync(AiTask.Explain);
        await vm.AskAsync(AiTask.Translate);
        await vm.AskAsync(AiTask.Grammar);

        var turns = store.LastSaved!.Turns;

        // A first turn replays nothing.
        Assert.Empty(turns[0].Sent!.ReplayedTurnIds);

        // Each later one names its predecessors, in the order they were sent...
        Assert.Equal(new[] { turns[0].Id }, turns[1].Sent!.ReplayedTurnIds);
        Assert.Equal(new[] { turns[0].Id, turns[1].Id }, turns[2].Sent!.ReplayedTurnIds);

        // ...which is exactly the history the orchestrator was handed, one exchange per named turn.
        Assert.Equal(2, orchestrator.Requests[2].History!.Count);
    }

    /// <summary>
    /// A save that fails says so in one sentence and breaks nothing. The answer is on screen; losing it to a
    /// problem with a file would be the larger harm.
    /// </summary>
    [Fact]
    public async Task A_failed_save_is_a_sentence_and_not_an_exception()
    {
        var store = new FakeStore { SaveSucceeds = false };
        var (vm, _, _, _) = Panel(Answering(Said("An answer.")), store: store);

        await vm.AskAsync(AiTask.Explain);

        Assert.Contains("could not be saved", vm.Status);

        // The turn itself is untouched — it ran, it answered, and it is not marked failed.
        Assert.Equal("An answer.", vm.LastTurn!.Answer);
        Assert.False(vm.LastTurn.Failed);
        Assert.False(vm.IsBusy);
    }

    /// <summary>A panel with no store is a panel that forgets, not one that fails — which is the whole reason
    /// both new dependencies are optional. (Every test in the suite next door constructs one.)</summary>
    [Fact]
    public async Task A_panel_with_no_store_still_runs_a_turn()
    {
        var vm = new AiAssistantViewModel(Answering(Said("An answer.")), new StubReaderState(), null, null);

        await vm.AskAsync(AiTask.Explain);

        Assert.Equal("An answer.", vm.LastTurn!.Answer);
        Assert.Equal("", vm.Status);
    }

    // ---- Restoring -------------------------------------------------------------------------------

    /// <summary>
    /// A restored turn renders as the live one that produced it. Compared field by field against a live turn
    /// built from the same events, because "renders identically" is not a property any single assertion has.
    /// </summary>
    [Fact]
    public async Task A_restored_turn_renders_as_the_live_one_that_produced_it()
    {
        var orchestrator = AnsweringFrom(
            ContextFrom(),
            AiTurnEvent.ForText("The term **appamada** matters.", "The term **[[appamada]]** matters."),
            AiTurnEvent.ForReasoning("Thinking about the compound."),
            AiTurnEvent.ForUsage(new AiUsageReport(1024, 256)));
        var (vm, store, _, _) = Panel(orchestrator);

        vm.Question = "what does the second word mean?";
        await vm.AskAsync(AiTask.Ask);

        var live = vm.LastTurn!;
        var restored = AiTurnViewModel.FromRecord(store.LastSaved!.Turns[0]);

        Assert.Equal(live.Task, restored.Task);
        Assert.Equal(live.Question, restored.Question);
        Assert.Equal(live.HasQuestion, restored.HasQuestion);
        Assert.Equal(live.PresetLabel, restored.PresetLabel);
        Assert.Equal(live.Answer, restored.Answer);
        Assert.Equal(live.HasAnswer, restored.HasAnswer);
        Assert.Equal(live.CopyText, restored.CopyText);
        Assert.Equal(live.Reasoning, restored.Reasoning);
        Assert.Equal(live.HasReasoning, restored.HasReasoning);
        Assert.Equal(live.ReasoningHeader, restored.ReasoningHeader);
        Assert.Equal(live.Citation, restored.Citation);
        Assert.Equal(live.CitationDetail, restored.CitationDetail);
        Assert.Equal(live.Subject, restored.Subject);
        Assert.Equal(live.HasSubject, restored.HasSubject);
        Assert.Equal(live.Notices, restored.Notices);
        Assert.Equal(live.HasNotices, restored.HasNotices);
        Assert.Equal(live.NoticesHeader, restored.NoticesHeader);
        Assert.Equal(live.IsPartialPassage, restored.IsPartialPassage);
        Assert.Equal(live.Usage, restored.Usage);
        Assert.Equal(live.Elapsed, restored.Elapsed);
        Assert.Equal(live.Footer, restored.Footer);
        Assert.Equal(live.HasFooter, restored.HasFooter);
        Assert.Equal(live.Status, restored.Status);
        Assert.Equal(live.HasStatus, restored.HasStatus);
        Assert.Equal(live.Failed, restored.Failed);
        Assert.Equal(live.HasSent, restored.HasSent);
        Assert.Equal(live.SentHeader, restored.SentHeader);
        Assert.Equal(live.Sent!.SystemPrompt, restored.Sent!.SystemPrompt);
        Assert.Equal(live.Sent.UserContent, restored.Sent.UserContent);
        Assert.Equal(live.Sent.Fields, restored.Sent.Fields);
        Assert.Equal(live.Sent.HasHistory, restored.Sent.HasHistory);

        // The blocks exist only after PublishAnswer, which is the one step a restore can silently skip while
        // every string assertion above still passes. The markup is there so the count is not trivially 1.
        Assert.Equal(live.Blocks.Count, restored.Blocks.Count);
        Assert.True(live.Blocks.Count > 0);

        // A turn read off disk is never running, whatever the live one was doing.
        Assert.False(restored.IsRunning);
        Assert.False(live.IsRunning);
    }

    /// <summary>
    /// At launch the panel reloads the conversation it was in. <b>[fsnow]</b>: <i>"Restore the last session
    /// silently"</i> — so no model call, and nothing asked of the reader.
    /// </summary>
    [Fact]
    public async Task The_last_conversation_is_restored_silently_at_launch()
    {
        var store = new FakeStore();
        var state = new ApplicationState();

        // Written by one launch...
        var (first, _, _, _) = Panel(Answering(Said("An answer.")), store: store, state: state);
        await first.AskAsync(AiTask.Explain);
        await first.AskAsync(AiTask.Translate);
        var written = store.LastSaved!;

        // ...and read by the next, which finds it through the id application state kept.
        store.ToLoad = written;
        var model = new StubOrchestrator();
        var (second, _, _, _) = Panel(model, store: store, state: state);

        await second.RestoreAsync();

        Assert.Equal(written.Id, store.AskedFor);
        Assert.Equal(2, second.Turns.Count);
        Assert.True(second.HasTurns);
        Assert.Equal("An answer.", second.Turns[0].Answer);
        Assert.Equal("", second.Status);

        // Silently: restoring is not asking.
        Assert.Empty(model.Requests);
    }

    /// <summary>
    /// Construction alone restores nothing. The panel is built during the dock layout build, BEFORE
    /// <c>LoadStateAsync</c> finishes, so a constructor that read the active id would read the default empty
    /// state every launch — the sequencing that <c>SearchViewModel.ApplyState</c> (#87) and
    /// <c>DictionaryViewModel.ApplyState</c> (#479) exist for.
    /// </summary>
    [Fact]
    public void Construction_restores_nothing_because_state_may_not_be_loaded_yet()
    {
        var store = new FakeStore { ToLoad = new AiSession { Id = "kept", Turns = { new AiTurnRecord() } } };
        var (vm, _, _, _) = Panel(new StubOrchestrator(), store: store,
            state: new ApplicationState { ActiveAssistantSessionId = "kept" });

        Assert.Empty(vm.Turns);
        Assert.Null(store.AskedFor);
    }

    /// <summary>
    /// A follow-up asked after a restart continues the conversation. The restored turns replay their MARKED
    /// answers — the form the model wrote, markers and all (#991) — and the question lines they were stored
    /// with rather than lines re-composed from today's wording.
    /// </summary>
    [Fact]
    public async Task A_follow_up_after_a_restart_replays_the_restored_conversation()
    {
        var store = new FakeStore();
        var state = new ApplicationState();

        var (first, _, _, _) = Panel(
            AnsweringFrom(
                ContextFrom(),
                AiTurnEvent.ForText("The term appamada matters.", "The term [[appamada]] matters.")),
            store: store, state: state);
        await first.AskAsync(AiTask.Explain);

        var written = store.LastSaved!;
        var askedLine = written.Turns[0].AskedLine;
        store.ToLoad = written;

        var next = AnsweringFrom(ContextFrom(), Said("It means vigilance."));
        var (second, _, _, _) = Panel(next, store: store, state: state);
        await second.RestoreAsync();

        second.Question = "what does the second word mean?";
        await second.AskAsync(AiTask.Ask);

        var replayed = Assert.Single(next.Requests[0].History!);

        // The marked form, not the text on screen: the system prompt asks for those markers on every Pāli span,
        // and replaying the stripped answer shows the model a transcript of itself disobeying.
        Assert.Equal("The term [[appamada]] matters.", replayed.Answer);

        // And the question line as it was STORED. Re-rendering it would mean that changing the wording of
        // DescribeAsked silently altered what every reopened conversation claims it was asked.
        Assert.Equal(askedLine, replayed.Question);

        // The new turn's own record points back at the restored one rather than copying it.
        Assert.Equal(new[] { written.Turns[0].Id }, store.LastSaved!.Turns[1].Sent!.ReplayedTurnIds);
    }

    /// <summary>
    /// A restored turn's Sent block shows the conversation it was sent with, rebuilt from the ids it stored —
    /// which is what makes storing the history by reference rather than by value cost nothing on screen.
    /// </summary>
    [Fact]
    public async Task A_restored_turn_shows_the_conversation_it_was_sent_with()
    {
        var store = new FakeStore();
        var state = new ApplicationState();

        var (first, _, _, _) = Panel(AnsweringFrom(ContextFrom(), Said("An answer.")),
            store: store, state: state);
        await first.AskAsync(AiTask.Explain);
        await first.AskAsync(AiTask.Translate);

        var written = store.LastSaved!;
        store.ToLoad = written;
        var (second, _, _, _) = Panel(new StubOrchestrator(), store: store, state: state);
        await second.RestoreAsync();

        // The first turn sent no conversation; the second sent the first.
        Assert.False(second.Turns[0].Sent!.HasHistory);

        var history = second.Turns[1].Sent!.History!;
        Assert.Equal(2, history.Count);
        Assert.Equal(ChatRole.User, history[0].Role);
        Assert.Equal(written.Turns[0].AskedLine, history[0].Content);
        Assert.Equal(ChatRole.Assistant, history[1].Role);
        Assert.Equal("An answer.", history[1].Content);
    }

    /// <summary>An id pointing at a turn that is no longer in the session is skipped, not refused. A turn
    /// deleted from a file by hand leaves one, and losing the conversation over it would be the worse
    /// failure.</summary>
    [Fact]
    public void A_dangling_replayed_turn_id_is_skipped()
    {
        var session = new AiSession
        {
            Id = "kept",
            Turns =
            {
                new AiTurnRecord
                {
                    Id = "second",
                    Answer = "An answer.",
                    Sent = new AiSentRecord
                    {
                        SystemPrompt = "sys",
                        UserContent = "user",
                        ReplayedTurnIds = { "a-turn-that-was-deleted" },
                    },
                },
            },
        };

        var byId = session.Turns.ToDictionary(t => t.Id, StringComparer.Ordinal);
        var restored = AiTurnViewModel.FromRecord(session.Turns[0], byId);

        Assert.False(restored.Sent!.HasHistory);
        Assert.Equal("An answer.", restored.Answer);
    }

    /// <summary>
    /// A session file that could not be read leaves an empty panel and one sentence saying where it was kept.
    /// Never a crash, and never a quiet deletion — the reader's transcript is still on disk.
    /// </summary>
    [Fact]
    public async Task An_unreadable_conversation_leaves_an_empty_panel_and_says_where_it_went()
    {
        var store = new FakeStore
        {
            ToLoad = null,
            ReportOnLoad = new AiSessionUnreadable("kept", "/kept/kept.unreadable-1.json", "unexpected token"),
        };
        var (vm, _, _, _) = Panel(new StubOrchestrator(), store: store,
            state: new ApplicationState { ActiveAssistantSessionId = "kept" });

        await vm.RestoreAsync();

        Assert.Empty(vm.Turns);
        Assert.Contains("could not be read", vm.Status);
        Assert.Contains("/kept/kept.unreadable-1.json", vm.Status);
    }

    /// <summary>An id naming a session that is no longer there is not an error — a hand-deleted file leaves one,
    /// and the panel simply starts empty.</summary>
    [Fact]
    public async Task A_missing_conversation_is_not_an_error()
    {
        var (vm, _, _, _) = Panel(new StubOrchestrator(),
            store: new FakeStore { ToLoad = null },
            state: new ApplicationState { ActiveAssistantSessionId = "gone" });

        await vm.RestoreAsync();

        Assert.Empty(vm.Turns);
        Assert.Equal("", vm.Status);
    }

    /// <summary>Nothing to restore, nothing read: a first-ever launch must not ask the store for anything.</summary>
    [Fact]
    public async Task With_no_active_conversation_nothing_is_read()
    {
        var (vm, store, _, _) = Panel(new StubOrchestrator());

        await vm.RestoreAsync();

        Assert.Null(store.AskedFor);
        Assert.Empty(vm.Turns);
    }

    /// <summary>A restored conversation keeps being added to rather than replaced — the next turn appends to the
    /// same file.</summary>
    [Fact]
    public async Task A_turn_after_a_restore_is_appended_to_the_conversation_it_reopened()
    {
        var store = new FakeStore();
        var state = new ApplicationState();

        var (first, _, _, _) = Panel(Answering(Said("An answer.")), store: store, state: state);
        await first.AskAsync(AiTask.Explain);
        var written = store.LastSaved!;

        store.ToLoad = written;
        var (second, _, _, _) = Panel(Answering(Said("Another answer.")), store: store, state: state);
        await second.RestoreAsync();
        await second.AskAsync(AiTask.Translate);

        Assert.Equal(written.Id, store.LastSaved!.Id);
        Assert.Equal(2, store.LastSaved.Turns.Count);
        Assert.Equal(2, second.Turns.Count);
    }

    // ---- New conversation ------------------------------------------------------------------------

    /// <summary>
    /// <b>[fsnow]</b>: <i>"'Clear' was introduced by Claude at some point and is not relevant. I would like to
    /// create new conversations like in Claude Code, maybe also with a plus button, while saving the current
    /// one."</i>
    ///
    /// <para>Nothing is saved here because nothing is left to save — the conversation was written at its last
    /// turn. What this does is let go: the transcript, the session, and the id that would otherwise reopen
    /// it.</para>
    /// </summary>
    [Fact]
    public async Task A_new_conversation_lets_go_of_the_one_that_was_already_saved()
    {
        var (vm, store, state, _) = Panel(Answering(Said("An answer.")));

        await vm.AskAsync(AiTask.Explain);
        var savesBefore = store.Saves.Count;
        var firstId = store.LastSaved!.Id;

        vm.NewConversationCommand.Execute().Subscribe();

        Assert.Empty(vm.Turns);
        Assert.False(vm.HasTurns);
        Assert.Null(vm.LastTurn);
        Assert.Equal("", vm.Status);

        // Nothing written, and nothing lost: the transcript on disk is untouched.
        Assert.Equal(savesBefore, store.Saves.Count);
        Assert.Null(state.ActiveAssistantSessionId);

        // The next turn starts a different conversation, in its own file, and nothing of the old one is replayed
        // to the model.
        await vm.AskAsync(AiTask.Translate);
        Assert.NotEqual(firstId, store.LastSaved!.Id);
        Assert.Single(store.LastSaved.Turns);
        Assert.Equal(store.LastSaved.Id, state.ActiveAssistantSessionId);
    }

    /// <summary>
    /// Blocked while a turn runs, like every other command. Letting go of a session mid-stream would leave that
    /// turn's save writing into a conversation the panel no longer shows.
    /// </summary>
    [Fact]
    public async Task A_new_conversation_is_refused_while_a_turn_is_running()
    {
        var orchestrator = new BlockingOrchestrator();
        var (vm, store, state, _) = Panel(orchestrator);

        var pending = vm.AskAsync(AiTask.Explain);
        Assert.True(vm.IsBusy);

        vm.NewConversationCommand.Execute().Subscribe();

        // Refused outright: the turn is still on screen and still running.
        Assert.Single(vm.Turns);
        Assert.True(vm.Turns[0].IsRunning);

        orchestrator.Gate.SetResult();
        await pending;

        // And it was saved into a conversation that still exists.
        Assert.Single(store.LastSaved!.Turns);
        Assert.Equal(store.LastSaved.Id, state.ActiveAssistantSessionId);
    }

    // ---- What the review named (fable, 2026-09-13) ----------------------------------------------

    /// <summary>
    /// The store raises <c>Unreadable</c> from a thread-pool continuation — inside the catch in its own read,
    /// after an <c>await … ConfigureAwait(false)</c>. Setting a bound property there throws in the real app, and
    /// the exception unwinds through the store's catch, so the file is kept aside and the reader is told nothing.
    ///
    /// <para><b>What this does and does not pin.</b> It asserts that a report raised off the caller's thread
    /// still reaches <see cref="AiAssistantViewModel.Status"/> and that nothing escapes the load. It cannot
    /// assert the marshalling: with no <c>Application</c> bound, <c>Dispatcher.UIThread.CheckAccess()</c> is
    /// true on every thread (measured), so this host takes the direct path. The posted path needs a running
    /// dispatcher, which no suite here has.</para>
    /// </summary>
    [Fact]
    public async Task An_unreadable_conversation_reported_off_the_ui_thread_still_reaches_the_reader()
    {
        var (vm, store, state, _) = Panel(Answering(Said("An answer.")));
        state.ActiveAssistantSessionId = "gone";
        store.ToLoad = null;

        var report = new AiSessionUnreadable("gone", "/kept/gone.unreadable-1.json", "truncated");
        store.ReportFromThreadPool = report;

        await vm.RestoreAsync();

        Assert.Empty(vm.Turns);
        Assert.Contains("/kept/gone.unreadable-1.json", vm.Status);
    }

    /// <summary>
    /// Restore reads the file asynchronously, and the reader can ask a question in that window. The live turn
    /// wins — it is on screen — and the restored transcript is dropped rather than interleaved with it.
    /// </summary>
    [Fact]
    public async Task A_question_asked_while_the_last_conversation_loads_wins()
    {
        var (vm, store, state, _) = Panel(Answering(Said("A live answer.")));
        state.ActiveAssistantSessionId = "earlier";
        store.ToLoad = new AiSession
        {
            Id = "earlier",
            Name = "Earlier",
            Turns = { new AiTurnRecord { Id = "t1", Task = AiTask.Explain, Answer = "An older answer." } },
        };
        var gate = new TaskCompletionSource();
        store.Gate = gate;

        var restore = vm.RestoreAsync();
        await vm.AskAsync(AiTask.Explain);

        gate.SetResult();
        await restore;

        // One turn, the live one, and the file it was saved into is not the one that was loading.
        Assert.Single(vm.Turns);
        Assert.Equal("A live answer.", vm.Turns[0].Answer);
        Assert.NotEqual("earlier", store.LastSaved!.Id);
    }

    /// <summary>
    /// A turn whose answer is only whitespace is dropped by <c>AiChatOrchestrator.Replay</c>, so recording it as
    /// replayed would have the stored record claim the model saw a turn it never did.
    /// </summary>
    [Fact]
    public async Task A_whitespace_answer_is_neither_replayed_nor_recorded_as_replayed()
    {
        // The stub replays one script, so both turns answer with whitespace; what matters is that the SECOND
        // turn found nothing to replay in the first.
        var (vm, store, _, _) = Panel(Answering(Said("   ")));

        await vm.AskAsync(AiTask.Explain);
        await vm.AskAsync(AiTask.Grammar);

        Assert.Empty(vm.Turns[1].ReplayedTurnIds);
        Assert.Empty(store.LastSaved!.Turns[1].Sent?.ReplayedTurnIds ?? new List<string>());
    }

    /// <summary>
    /// The auto-name is cut on a text element, not a UTF-16 index: cutting mid-pair makes the JSON writer
    /// substitute U+FFFD, so the conversation is named with a replacement character.
    /// </summary>
    [Fact]
    public async Task An_auto_name_is_never_cut_through_a_character()
    {
        var (vm, store, _, _) = Panel(Answering(Said("An answer.")));

        // The 60th text element is an emoji, so a naive cut lands between its two halves.
        vm.Question = new string('a', 59) + "\U0001F64F" + " and more besides";
        await vm.AskAsync(AiTask.Ask);

        var name = store.LastSaved!.Name;
        Assert.DoesNotContain('\uFFFD', name);
        Assert.False(char.IsHighSurrogate(name[^2]), "the name ends on a whole character");
    }
}
