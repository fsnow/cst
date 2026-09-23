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

        public async IAsyncEnumerable<AiTurnEvent> RunAsync(
            AiTurnRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            Requests.Add(request);

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
