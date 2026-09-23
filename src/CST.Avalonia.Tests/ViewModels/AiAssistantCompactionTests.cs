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

using static CST.Avalonia.Tests.ViewModels.AiAssistantViewModelTests;
using static CST.Avalonia.Tests.ViewModels.AiAssistantSessionWiringTests;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// Compaction on the panel: the manual action, the record of both kinds, and what the next turn replays. (#998)
///
/// <para><b>[fsnow]</b>: <i>"Manual and auto at a fraction of context length"</i>; <i>"Last 4 turns"</i> stay
/// verbatim. The reference is Claude Code's <c>/compact [instructions]</c>. The properties that carry the weight:
/// <b>what is shown does not change</b> (every turn stays in <c>Turns</c>), <b>what is sent does</b> (the summary
/// first, then the four kept turns), and <b>the record rebuilds exactly what was sent</b> after a restart.</para>
/// </summary>
public class AiAssistantCompactionTests
{
    /// <summary>
    /// Answers every turn, numbered so a test can tell them apart; summarises on <see cref="CompactAsync"/>; and
    /// can hold a turn or a summary open at a known point, so "while busy" is a state a test can be in.
    /// </summary>
    private sealed class CompactingOrchestrator : IAiChatOrchestrator
    {
        private int _answered;

        internal List<AiTurnRequest> Requests { get; } = new();
        internal List<AiCompactionRequest> CompactRequests { get; } = new();

        /// <summary>What the next <see cref="CompactAsync"/> answers. A summary by default.</summary>
        internal Func<AiCompactionRequest, AiCompactionResult> Summarise { get; set; } = request =>
            new AiCompactionResult(
                $"Summary of {request.Turns.Count} turn(s) about [[appamāda]].", AiCompaction.SummaryAskedLine,
                null, "openrouter", "some/model");

        /// <summary>Held open until released, when set — the next turn waits after Started, the next summary before
        /// answering.</summary>
        internal TaskCompletionSource? HoldTurn { get; set; }
        internal TaskCompletionSource? HoldSummary { get; set; }

        /// <summary>Emitted before Started on the next turn, as the orchestrator does for an automatic
        /// compaction.</summary>
        internal AiCompacted? CompactNextTurn { get; set; }

        /// <summary>
        /// When set, the next turn emits these instead of the usual opening (an optional Compacted, then Started) —
        /// for the orders the real orchestrator produces that the default cannot, such as a retry's
        /// <c>Started, Compacting, Compacted, Started</c>. A null entry is a pause: the turn waits on
        /// <see cref="HoldPrelude"/> there.
        /// </summary>
        internal List<AiTurnEvent?>? Prelude { get; set; }
        internal TaskCompletionSource? HoldPrelude { get; set; }

        public async IAsyncEnumerable<AiTurnEvent> RunAsync(
            AiTurnRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            Requests.Add(request);

            if (Prelude is { } prelude)
            {
                Prelude = null;
                foreach (var e in prelude)
                {
                    if (e is null)
                    {
                        if (HoldPrelude is { } pause) await pause.Task;
                        continue;
                    }
                    yield return e;
                }

                var m = ++_answered;
                yield return AiTurnEvent.ForText($"Answer {m} on appamāda.", $"Answer {m} on [[appamāda]].");
                yield return AiTurnEvent.ForCompleted(new PaliMarkerReport(1, 0));
                yield break;
            }

            if (CompactNextTurn is { } compaction)
            {
                CompactNextTurn = null;
                yield return AiTurnEvent.ForCompacted(compaction);
            }

            yield return AiTurnEvent.ForStarted(ContextFrom());

            if (HoldTurn is { } hold)
            {
                HoldTurn = null;
                await hold.Task;
            }

            var n = ++_answered;
            yield return AiTurnEvent.ForText($"Answer {n} on appamāda.", $"Answer {n} on [[appamāda]].");
            yield return AiTurnEvent.ForCompleted(new PaliMarkerReport(1, 0));
        }

        public void Stop()
        {
        }

        public async Task<AiCompactionResult> CompactAsync(AiCompactionRequest request, CancellationToken ct = default)
        {
            CompactRequests.Add(request);
            if (HoldSummary is { } hold)
            {
                HoldSummary = null;
                await hold.Task.WaitAsync(ct);
            }

            return Summarise(request);
        }
    }

    private static async Task AskTimes(AiAssistantViewModel vm, int count)
    {
        for (var i = 0; i < count; i++) await vm.AskAsync(AiTask.Explain);
    }

    // ---- The manual action ---------------------------------------------------------------------------------

    /// <summary>
    /// Nothing to compact beyond the last four: with four answered turns Compact is not offered and does nothing;
    /// with five it is. <b>[fsnow]</b>: <i>"Last 4 turns"</i>.
    /// </summary>
    [Fact]
    public async Task Compact_needs_more_than_the_last_four_answered_turns()
    {
        var orchestrator = new CompactingOrchestrator();
        var (vm, _, _, _) = Panel(orchestrator);

        await AskTimes(vm, AiCompaction.KeepVerbatim);
        Assert.False(vm.CanCompact);

        await vm.CompactAsync();
        Assert.Empty(orchestrator.CompactRequests);

        await vm.AskAsync(AiTask.Explain);
        Assert.True(vm.CanCompact);
    }

    /// <summary>
    /// The summariser is sent ONLY the turns being compacted — the question line the model was shown and the answer
    /// as it wrote it, markers intact — and the reader's instructions. The kept four are not in it.
    /// </summary>
    [Fact]
    public async Task Compact_sends_only_the_older_turns_with_their_markers_and_the_instructions()
    {
        var orchestrator = new CompactingOrchestrator();
        var (vm, _, _, _) = Panel(orchestrator);
        await AskTimes(vm, 6);

        vm.CompactInstructions = "Keep the grammar points.";
        await vm.CompactAsync();

        var request = Assert.Single(orchestrator.CompactRequests);
        Assert.Null(request.PreviousSummary);
        Assert.Equal(2, request.Turns.Count);
        Assert.Equal(AiAssistantViewModel.DescribeAsked(vm.Turns[0]), request.Turns[0].Question);
        Assert.Equal("Answer 1 on [[appamāda]].", request.Turns[0].Answer);
        Assert.Equal("Answer 2 on [[appamāda]].", request.Turns[1].Answer);
        Assert.Equal("Keep the grammar points.", request.Instructions);

        // The instructions box empties once they have been used.
        Assert.Equal("", vm.CompactInstructions);
    }

    /// <summary>A parameter on the command wins over the box, which is left alone.</summary>
    [Fact]
    public async Task Instructions_passed_to_the_command_win_over_the_box()
    {
        var orchestrator = new CompactingOrchestrator();
        var (vm, _, _, _) = Panel(orchestrator);
        await AskTimes(vm, 5);

        vm.CompactInstructions = "From the box.";
        await vm.CompactAsync("From the command.");

        Assert.Equal("From the command.", orchestrator.CompactRequests.Single().Instructions);
    }

    /// <summary>
    /// What is shown does not change: every turn stays in <c>Turns</c> with its answer. What does change is marked —
    /// the summarised turns, and the marker row on the first turn after them.
    /// </summary>
    [Fact]
    public async Task Summarised_turns_stay_on_screen_and_the_boundary_is_marked()
    {
        var orchestrator = new CompactingOrchestrator();
        var (vm, store, _, _) = Panel(orchestrator);
        await AskTimes(vm, 7);

        await vm.CompactAsync();

        Assert.Equal(7, vm.Turns.Count);
        Assert.Equal("Answer 1 on appamāda.", vm.Turns[0].Answer);

        Assert.Equal(new[] { true, true, true, false, false, false, false },
                     vm.Turns.Select(t => t.IsSummarised));
        Assert.Equal(new[] { false, false, false, true, false, false, false },
                     vm.Turns.Select(t => t.HasCompactionMarker));
        Assert.Equal("3 earlier turns summarised", vm.Turns[3].CompactionMarker);
        Assert.Equal("Summary of 3 turn(s) about [[appamāda]].", vm.Turns[3].CompactionSummary);

        // Written at once, with the turns it stands in for.
        var compaction = Assert.Single(store.LastSaved!.Compactions);
        Assert.Equal(vm.Turns.Take(3).Select(t => t.ToRecord().Id), compaction.SummarisedTurnIds);
        Assert.False(compaction.Automatic);
        Assert.Equal("some/model", compaction.ModelId);

        // And nothing is left to compact until a fifth unsummarised turn arrives.
        Assert.False(vm.CanCompact);
    }

    /// <summary>
    /// The next turn sends the summary first and the last four turns word for word — not the summarised turns — and
    /// its record names both, by id.
    /// </summary>
    [Fact]
    public async Task The_next_turn_replays_the_summary_then_the_last_four()
    {
        var orchestrator = new CompactingOrchestrator();
        var (vm, store, _, _) = Panel(orchestrator);
        await AskTimes(vm, 6);
        await vm.CompactAsync();

        await vm.AskAsync(AiTask.Translate);

        var request = orchestrator.Requests[^1];
        Assert.Equal(AiCompaction.SummaryAskedLine, request.Summary!.Question);
        Assert.Equal("Summary of 2 turn(s) about [[appamāda]].", request.Summary.Answer);
        Assert.Equal(new[] { 3, 4, 5, 6 }.Select(n => $"Answer {n} on [[appamāda]]."),
                     request.History!.Select(h => h.Answer));

        var record = store.LastSaved!.Turns[^1].Sent!;
        Assert.Equal(store.LastSaved.Compactions.Single().Id, record.SummaryId);
        Assert.Equal(vm.Turns.Skip(2).Take(4).Select(t => t.ToRecord().Id), record.ReplayedTurnIds);
    }

    /// <summary>
    /// A second compaction summarises the first: the standing summary goes to the summariser with the turns since,
    /// and the new record lists every turn it stands in for, the first record's included.
    /// </summary>
    [Fact]
    public async Task A_second_compaction_folds_in_the_first()
    {
        var orchestrator = new CompactingOrchestrator();
        var (vm, store, _, _) = Panel(orchestrator);
        await AskTimes(vm, 5);
        await vm.CompactAsync();
        await AskTimes(vm, 3);

        await vm.CompactAsync();

        var second = orchestrator.CompactRequests[1];
        Assert.Equal("Summary of 1 turn(s) about [[appamāda]].", second.PreviousSummary!.Answer);
        Assert.Equal(new[] { "Answer 2 on [[appamāda]].", "Answer 3 on [[appamāda]].", "Answer 4 on [[appamāda]]." },
                     second.Turns.Select(t => t.Answer));

        Assert.Equal(2, store.LastSaved!.Compactions.Count);
        Assert.Equal(vm.Turns.Take(4).Select(t => t.ToRecord().Id), store.LastSaved.Compactions[1].SummarisedTurnIds);
        Assert.Equal("4 earlier turns summarised", vm.Turns[4].CompactionMarker);
        Assert.False(vm.Turns[1].HasCompactionMarker);

        await vm.AskAsync(AiTask.Explain);
        Assert.Equal("Summary of 3 turn(s) about [[appamāda]].", orchestrator.Requests[^1].Summary!.Answer);
        Assert.Equal(4, orchestrator.Requests[^1].History!.Count);
    }

    /// <summary>Blocked while a turn runs, like every other command: a summary written while a turn streams would be
    /// of a conversation that is still changing.</summary>
    [Fact]
    public async Task Compact_is_blocked_while_a_turn_is_running()
    {
        var orchestrator = new CompactingOrchestrator();
        var (vm, _, _, _) = Panel(orchestrator);
        await AskTimes(vm, 5);
        Assert.True(vm.CanCompact);

        var hold = new TaskCompletionSource();
        orchestrator.HoldTurn = hold;
        var running = vm.AskAsync(AiTask.Explain);

        Assert.True(vm.IsBusy);
        Assert.False(vm.CanCompact);
        await vm.CompactAsync();
        Assert.Empty(orchestrator.CompactRequests);

        hold.SetResult();
        await running;
        Assert.True(vm.CanCompact);
    }

    /// <summary>And a compaction holds the panel busy in turn, so no question can be asked against a conversation that
    /// is being rewritten; Stop cancels it and nothing changes.</summary>
    [Fact]
    public async Task A_compaction_holds_the_panel_busy_and_Stop_cancels_it()
    {
        var orchestrator = new CompactingOrchestrator();
        var (vm, store, _, _) = Panel(orchestrator);
        await AskTimes(vm, 5);
        var savesBefore = store.Saves.Count;

        orchestrator.HoldSummary = new TaskCompletionSource();
        var compacting = vm.CompactAsync();

        Assert.True(vm.IsBusy);
        Assert.True(vm.IsCompacting);
        Assert.False(vm.CanAsk);

        vm.StopCommand.Execute().Subscribe();
        await compacting;

        Assert.False(vm.IsBusy);
        Assert.False(vm.IsCompacting);
        Assert.Contains("stopped", vm.Status);
        Assert.All(vm.Turns, t => Assert.False(t.IsSummarised));
        Assert.Equal(savesBefore, store.Saves.Count);
        Assert.True(vm.CanCompact);
    }

    /// <summary>A failed summary changes nothing and says so in one sentence.</summary>
    [Fact]
    public async Task A_failed_compaction_changes_nothing()
    {
        var orchestrator = new CompactingOrchestrator
        {
            Summarise = _ => AiCompactionResult.Failed(new AiError(AiErrorKind.RateLimited, "Too many requests.")),
        };
        var (vm, store, _, _) = Panel(orchestrator);
        await AskTimes(vm, 5);

        await vm.CompactAsync();

        Assert.Contains("could not be summarised", vm.Status);
        Assert.Contains("Too many requests.", vm.Status);
        Assert.Empty(store.LastSaved!.Compactions);

        await vm.AskAsync(AiTask.Explain);
        Assert.Null(orchestrator.Requests[^1].Summary);
        Assert.Equal(5, orchestrator.Requests[^1].History!.Count);
    }

    // ---- The automatic kind, recorded ---------------------------------------------------------------------

    /// <summary>
    /// A compaction the orchestrator made mid-turn is recorded against the turns it was handed: the first N history
    /// entries become the summarised ids, and the turn's own record names the summary and only the turns sent after
    /// it — which is what the model was actually sent.
    /// </summary>
    [Fact]
    public async Task An_automatic_compaction_is_recorded_against_the_turns_it_replaced()
    {
        var orchestrator = new CompactingOrchestrator();
        var (vm, store, _, _) = Panel(orchestrator);
        await AskTimes(vm, 6);

        orchestrator.CompactNextTurn = new AiCompacted(
            AiCompaction.SummaryAskedLine, "Automatic summary.", 2, "openrouter", "some/model");
        await vm.AskAsync(AiTask.Explain);

        Assert.Equal(7, vm.Turns.Count);

        var compaction = Assert.Single(store.LastSaved!.Compactions);
        Assert.True(compaction.Automatic);
        Assert.Equal("Automatic summary.", compaction.Summary);
        Assert.Equal(vm.Turns.Take(2).Select(t => t.ToRecord().Id), compaction.SummarisedTurnIds);

        var sent = store.LastSaved.Turns[^1].Sent!;
        Assert.Equal(compaction.Id, sent.SummaryId);
        Assert.Equal(vm.Turns.Skip(2).Take(4).Select(t => t.ToRecord().Id), sent.ReplayedTurnIds);
        Assert.Equal("2 earlier turns summarised", vm.Turns[2].CompactionMarker);

        // And the turn after replays it.
        await vm.AskAsync(AiTask.Explain);
        Assert.Equal("Automatic summary.", orchestrator.Requests[^1].Summary!.Answer);
        Assert.Equal(5, orchestrator.Requests[^1].History!.Count);
    }

    /// <summary>
    /// A retried turn — <c>Started, Compacting, Compacted, Started</c>, the order the orchestrator produces when the
    /// provider rejects a request as too long — keeps the SECOND Started's notices and Sent, and its record names the
    /// summary it was finally sent with. (review M-1; the default fake never emitted this order)
    /// </summary>
    [Fact]
    public async Task A_retried_turn_keeps_the_notices_and_record_of_the_request_that_was_answered()
    {
        var orchestrator = new CompactingOrchestrator();
        var (vm, store, _, _) = Panel(orchestrator);
        await AskTimes(vm, 6);

        var firstSent = new SentContext(Array.Empty<SentField>(), "sys", "first attempt");
        var finalSent = new SentContext(Array.Empty<SentField>(), "sys", "second attempt");
        orchestrator.Prelude = new List<AiTurnEvent?>
        {
            AiTurnEvent.ForStarted(ContextFrom() with
            {
                Notices = new[] { "Earlier turns could not be summarised (x), so the whole conversation was sent." },
                Sent = firstSent,
            }),
            AiTurnEvent.ForCompacting(),
            AiTurnEvent.ForCompacted(new AiCompacted(
                AiCompaction.SummaryAskedLine, "Retry summary.", 2, "openrouter", "some/model",
                new AiUsageReport(10, 5))),
            AiTurnEvent.ForStarted(ContextFrom() with
            {
                Notices = new[] { "…2 earlier turns were summarised and it was sent again." },
                Sent = finalSent,
            }),
        };

        await vm.AskAsync(AiTask.Explain);

        var turn = vm.Turns[^1];
        Assert.Equal(new[] { "…2 earlier turns were summarised and it was sent again." }, turn.Notices);
        Assert.Same(finalSent, turn.Sent);
        Assert.False(vm.IsCompacting);

        var record = store.LastSaved!.Turns[^1];
        Assert.Equal(new[] { "…2 earlier turns were summarised and it was sent again." }, record.Notices);
        Assert.Equal("second attempt", record.Sent!.UserContent);

        var compaction = Assert.Single(store.LastSaved.Compactions);
        Assert.Equal(compaction.Id, record.Sent.SummaryId);
        Assert.Equal(vm.Turns.Skip(2).Take(4).Select(t => t.ToRecord().Id), record.Sent.ReplayedTurnIds);
        Assert.Equal(10, compaction.InputTokens);
        Assert.Equal(5, compaction.OutputTokens);
    }

    /// <summary>
    /// While an automatic summary is written the panel says so — IsCompacting, and the turn's status reads
    /// "Summarising earlier turns…" rather than blaming a slow or queued model — and both clear when the summary ends.
    /// (review M-2)
    /// </summary>
    [Fact]
    public async Task An_automatic_summary_in_progress_is_announced_and_cleared_when_it_ends()
    {
        var orchestrator = new CompactingOrchestrator();
        var (vm, _, _, _) = Panel(orchestrator);
        await AskTimes(vm, 5);

        var hold = new TaskCompletionSource();
        orchestrator.HoldPrelude = hold;
        orchestrator.Prelude = new List<AiTurnEvent?>
        {
            AiTurnEvent.ForCompacting(),
            null,
            AiTurnEvent.ForCompacted(new AiCompacted(
                AiCompaction.SummaryAskedLine, "Automatic summary.", 1, "openrouter", "some/model")),
            AiTurnEvent.ForStarted(ContextFrom()),
        };

        var running = vm.AskAsync(AiTask.Explain);

        Assert.True(vm.IsCompacting);
        Assert.True(vm.IsBusy);
        Assert.StartsWith("Summarising earlier turns", vm.Turns[^1].Status);

        hold.SetResult();
        await running;

        Assert.False(vm.IsCompacting);
        Assert.Equal("", vm.Turns[^1].Status);
    }

    /// <summary>A summary that fails — Compacting, then straight to Started — clears the flag too.</summary>
    [Fact]
    public async Task A_failed_automatic_summary_clears_the_flag()
    {
        var orchestrator = new CompactingOrchestrator();
        var (vm, _, _, _) = Panel(orchestrator);
        await AskTimes(vm, 5);

        var hold = new TaskCompletionSource();
        orchestrator.HoldPrelude = hold;
        orchestrator.Prelude = new List<AiTurnEvent?>
        {
            AiTurnEvent.ForCompacting(),
            AiTurnEvent.ForStarted(ContextFrom()),
            null,
        };

        var running = vm.AskAsync(AiTask.Explain);
        Assert.False(vm.IsCompacting);
        Assert.DoesNotContain("Summarising", vm.Turns[^1].Status);

        hold.SetResult();
        await running;
        Assert.False(vm.IsCompacting);
    }

    /// <summary>A manual summary's cost has no turn to go in, so it is kept on its record.</summary>
    [Fact]
    public async Task A_manual_compaction_records_its_usage()
    {
        var orchestrator = new CompactingOrchestrator
        {
            Summarise = _ => new AiCompactionResult(
                "A summary.", AiCompaction.SummaryAskedLine, null, "openrouter", "some/model", null,
                new AiUsageReport(40, 8)),
        };
        var (vm, store, _, _) = Panel(orchestrator);
        await AskTimes(vm, 5);

        await vm.CompactAsync();

        var compaction = Assert.Single(store.LastSaved!.Compactions);
        Assert.Equal(40, compaction.InputTokens);
        Assert.Equal(8, compaction.OutputTokens);
    }

    // ---- A blank summary in a file (review L-3) ------------------------------------------------------------

    private static AiSession Seeded(params AiCompactionRecord[] compactions)
    {
        var session = new AiSession { Id = "seeded", Name = "Seeded" };
        for (var i = 1; i <= 6; i++)
        {
            session.Turns.Add(new AiTurnRecord
            {
                Id = $"t{i}", Task = AiTask.Explain, AskedLine = $"asked {i}",
                Answer = $"Answer {i}", MarkedAnswer = $"Answer {i} [[x]]",
            });
        }
        session.Compactions.AddRange(compactions);
        return session;
    }

    /// <summary>
    /// A compaction record with a blank summary — which nothing writes, and a hand-edit can — counts as no
    /// compaction. Honouring its turn list would send the model neither those turns nor anything in their place.
    /// </summary>
    [Fact]
    public async Task A_blank_summary_counts_as_no_compaction()
    {
        var store = new FakeStore();
        store.Seed(Seeded(new AiCompactionRecord { Id = "blank", Summary = "  ", SummarisedTurnIds = { "t1", "t2" } }));
        var orchestrator = new CompactingOrchestrator();
        var (vm, _, _, _) = Panel(orchestrator, store: store,
            state: new ApplicationState { ActiveAssistantSessionId = "seeded" });
        await vm.RestoreAsync();

        Assert.All(vm.Turns, t => Assert.False(t.IsSummarised));
        Assert.All(vm.Turns, t => Assert.False(t.HasCompactionMarker));

        await vm.AskAsync(AiTask.Explain);

        Assert.Null(orchestrator.Requests[^1].Summary);
        Assert.Equal(6, orchestrator.Requests[^1].History!.Count);
    }

    /// <summary>...and the compaction before a blank one stays in force.</summary>
    [Fact]
    public async Task Behind_a_blank_summary_the_previous_compaction_stays_in_force()
    {
        var store = new FakeStore();
        store.Seed(Seeded(
            new AiCompactionRecord { Id = "good", Summary = "Good summary.", SummarisedTurnIds = { "t1" } },
            new AiCompactionRecord { Id = "blank", Summary = "", SummarisedTurnIds = { "t1", "t2" } }));
        var orchestrator = new CompactingOrchestrator();
        var (vm, _, _, _) = Panel(orchestrator, store: store,
            state: new ApplicationState { ActiveAssistantSessionId = "seeded" });
        await vm.RestoreAsync();

        await vm.AskAsync(AiTask.Explain);

        Assert.Equal("Good summary.", orchestrator.Requests[^1].Summary!.Answer);
        Assert.Equal(5, orchestrator.Requests[^1].History!.Count);
        Assert.Equal("1 earlier turn summarised", vm.Turns[1].CompactionMarker);
    }

    // ---- Restore, switch, new conversation ----------------------------------------------------------------

    /// <summary>
    /// A restored conversation rebuilds exactly what was sent: the Sent block of a turn sent after a compaction
    /// shows the summary then the kept turns, and the next turn after the restart sends what it would have sent
    /// without one.
    /// </summary>
    [Fact]
    public async Task A_restored_conversation_rebuilds_the_history_that_was_sent()
    {
        var store = new FakeStore();
        var state = new ApplicationState();
        var live = new CompactingOrchestrator();
        var (first, _, _, _) = Panel(live, store: store, state: state);
        await AskTimes(first, 6);
        await first.CompactAsync();
        await first.AskAsync(AiTask.Explain);

        var sentLive = live.Requests[^1];

        var restored = new CompactingOrchestrator();
        var (second, _, _, _) = Panel(restored, store: store, state: state);
        await second.RestoreAsync();

        // The Sent block of the last turn, rebuilt from the ids it stored: the summary first, then the four.
        var history = second.Turns[^1].Sent!.History!;
        var expected = new[] { (ChatRole.User, sentLive.Summary!.Question), (ChatRole.Assistant, sentLive.Summary.Answer) }
            .Concat(sentLive.History!.SelectMany(h => new[] { (ChatRole.User, h.Question), (ChatRole.Assistant, h.Answer) }));
        Assert.Equal(expected, history.Select(m => (m.Role, m.Content)));

        // The markers come back with it.
        Assert.Equal("2 earlier turns summarised", second.Turns[2].CompactionMarker);

        // And the next turn replays the summary in force, not the turns it stands in for.
        await second.AskAsync(AiTask.Explain);
        var next = restored.Requests.Single();
        Assert.Equal(sentLive.Summary, next.Summary);
        Assert.Equal(5, next.History!.Count);
    }

    /// <summary>Switching to a compacted conversation applies its compaction to the next turn.</summary>
    [Fact]
    public async Task Switching_to_a_compacted_conversation_applies_its_summary()
    {
        var store = new FakeStore();
        var orchestrator = new CompactingOrchestrator();
        var (vm, _, _, _) = Panel(orchestrator, store: store);
        await AskTimes(vm, 5);
        await vm.CompactAsync();
        var compactedId = store.LastSaved!.Id;

        vm.NewConversationCommand.Execute().Subscribe();
        Assert.False(vm.CanCompact);
        await vm.AskAsync(AiTask.Explain);
        Assert.Null(orchestrator.Requests[^1].Summary);

        await vm.SwitchToSessionAsync(compactedId);
        await vm.AskAsync(AiTask.Explain);

        Assert.Equal("Summary of 1 turn(s) about [[appamāda]].", orchestrator.Requests[^1].Summary!.Answer);
        Assert.Equal(4, orchestrator.Requests[^1].History!.Count);
    }

    /// <summary>A new conversation starts with no summary: nothing of the old one is sent.</summary>
    [Fact]
    public async Task A_new_conversation_carries_no_summary()
    {
        var orchestrator = new CompactingOrchestrator();
        var (vm, _, _, _) = Panel(orchestrator);
        await AskTimes(vm, 5);
        await vm.CompactAsync();

        vm.NewConversationCommand.Execute().Subscribe();
        await vm.AskAsync(AiTask.Explain);

        Assert.Null(orchestrator.Requests[^1].Summary);
        Assert.Empty(orchestrator.Requests[^1].History!);
    }
}
