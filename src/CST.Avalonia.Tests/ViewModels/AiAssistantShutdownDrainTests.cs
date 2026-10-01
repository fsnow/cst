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
using Xunit;

using static CST.Avalonia.Tests.ViewModels.AiAssistantViewModelTests;
using static CST.Avalonia.Tests.ViewModels.AiAssistantSessionWiringTests;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// What the Assistant panel does when the reader quits. (#1018)
///
/// <para><b>Expected</b> (the issue): quitting while an answer is still arriving keeps what had arrived; the
/// conversation reopens with that turn in it, marked as stopped, the way Stop leaves it. <b>Contrast:</b> Stop then
/// Quit already did — <c>A_stopped_turn_is_saved_with_the_text_that_had_arrived</c> next door. So the drain is
/// Stop, followed by a bounded wait for the save Stop leads to, and these pin both halves.</para>
///
/// <para>Every success case runs with a generous bound and asserts the drain answered true, so a drain that
/// forgot to stop the turn fails by timing out rather than by hanging the suite.</para>
/// </summary>
public class AiAssistantShutdownDrainTests
{
    private static readonly TimeSpan Generous = TimeSpan.FromSeconds(10);
    private static readonly TimeSpan Short = TimeSpan.FromMilliseconds(100);

    /// <summary>Streams half an answer, says so, then waits on the caller's token — a live stream, which ends only
    /// when the turn is cancelled. The shape of a real provider mid-answer.</summary>
    internal sealed class StreamingOrchestrator : IAiChatOrchestrator
    {
        internal TaskCompletionSource Streaming { get; } =
            new(TaskCreationOptions.RunContinuationsAsynchronously);

        internal int StopCalls { get; private set; }

        public async IAsyncEnumerable<AiTurnEvent> RunAsync(
            AiTurnRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return AiTurnEvent.ForStarted(ContextFrom());
            yield return Said("Half an answer");

            // Resumed only once the panel has handled the text above and asked for more.
            Streaming.TrySetResult();
            await Task.Delay(Timeout.Infinite, ct);
            yield return AiTurnEvent.ForCompleted(new PaliMarkerReport(0, 0));
        }

        public void Stop() => StopCalls++;
    }

    /// <summary>A reader whose position takes a while to read — the WebView round trip a click starts with.</summary>
    private sealed class SlowReaderState : IReaderStateService
    {
        internal TaskCompletionSource Asked { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
        internal TaskCompletionSource Release { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);

        public async Task<ReaderStateResult> GetCurrentAsync(
            ReaderFocusSignal focus = ReaderFocusSignal.None, CancellationToken ct = default)
        {
            Asked.TrySetResult();
            await Release.Task;
            return ReaderStateResult.Ok(new ReaderState("s0101m.mul.xml", 12, null));
        }
    }

    /// <summary>The stub answers across <c>Task.Yield</c>s, so the save is reached a few continuations after the
    /// click rather than inside it.</summary>
    private static async Task Until(Func<bool> condition)
    {
        var watch = System.Diagnostics.Stopwatch.StartNew();
        while (!condition())
        {
            Assert.True(watch.Elapsed < TimeSpan.FromSeconds(10), "the condition never became true");
            await Task.Delay(5);
        }
    }

    // ---- The turn in flight ---------------------------------------------------------------------------

    /// <summary>
    /// The issue's own case: an answer half-streamed when Quit arrives is saved with what had arrived, marked
    /// "Stopped." and not failed — the record a Stop leaves — and, as this is the conversation's first turn, the
    /// session is named in application state by the time the drain returns.
    /// </summary>
    [Fact]
    public async Task A_streaming_turn_is_stopped_and_saved_with_what_had_arrived()
    {
        var orchestrator = new StreamingOrchestrator();
        var (vm, store, state, _) = Panel(orchestrator);

        var pending = vm.AskAsync(AiTask.Explain);
        await orchestrator.Streaming.Task;
        Assert.Empty(store.Saves);

        Assert.True(await vm.DrainAsync(Generous));

        var saved = Assert.IsType<AiSession>(store.LastSaved);
        var record = Assert.Single(saved.Turns);
        Assert.Equal("Half an answer", record.Answer);
        Assert.Equal("Stopped.", record.Status);
        Assert.False(record.Failed);
        Assert.True(store.Disk.ContainsKey(saved.Id));
        Assert.Equal(saved.Id, state.ActiveAssistantSessionId);

        // Through the Stop path, which stops the orchestrator's own source as well as the panel's token.
        Assert.Equal(1, orchestrator.StopCalls);

        await pending;
        Assert.False(vm.IsBusy);
    }

    /// <summary>
    /// A turn that has ended and is writing its file: the drain waits for the write, rather than answering as soon
    /// as the stream is over.
    /// </summary>
    [Fact]
    public async Task The_drain_waits_for_a_save_in_progress()
    {
        var (vm, store, _, _) = Panel(Answering(Said("A whole answer.")));
        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.HoldNextSave = hold;

        var pending = vm.AskAsync(AiTask.Explain);
        await Until(() => store.Saves.Count == 1);   // called, and held
        Assert.Empty(store.Disk);

        var drain = vm.DrainAsync(Generous);
        await Task.Delay(50);
        Assert.False(drain.IsCompleted);

        hold.SetResult(true);
        Assert.True(await drain);
        Assert.Single(store.Disk);
        await pending;
    }

    /// <summary>A write that never finishes — a disk that does not answer — does not hold Quit past the bound.</summary>
    [Fact]
    public async Task A_save_that_hangs_does_not_hold_the_drain_past_its_bound()
    {
        var (vm, store, _, _) = Panel(Answering(Said("A whole answer.")));
        store.HoldNextSave = new TaskCompletionSource<bool>();   // never released

        _ = vm.AskAsync(AiTask.Explain);
        await Until(() => store.Saves.Count == 1);

        var watch = System.Diagnostics.Stopwatch.StartNew();
        Assert.False(await vm.DrainAsync(Short));
        Assert.True(watch.Elapsed < TimeSpan.FromSeconds(5), $"took {watch.Elapsed}");
    }

    /// <summary>
    /// A provider that ignores cancellation — <c>BlockingOrchestrator</c> waits on a gate, not on the token — is the
    /// hung-provider case the bound exists for. The drain gives up; nothing was saved, as before #1018.
    /// </summary>
    [Fact]
    public async Task A_provider_that_ignores_stop_does_not_hold_the_drain_past_its_bound()
    {
        var orchestrator = new BlockingOrchestrator();
        var (vm, store, _, _) = Panel(orchestrator);

        var pending = vm.AskAsync(AiTask.Explain);
        Assert.False(await vm.DrainAsync(Short));
        Assert.Empty(store.Saves);

        orchestrator.Gate.SetResult();
        await pending;
    }

    /// <summary>Quit arriving while the click is still reading the reader's position: no turn is started, so there is
    /// nothing the drain would then have to wait for, and the question stays in the box.</summary>
    [Fact]
    public async Task A_turn_not_yet_started_when_the_drain_begins_is_not_started()
    {
        var orchestrator = new StreamingOrchestrator();
        var reader = new SlowReaderState();
        var store = new FakeStore();
        var state = new ApplicationState();
        var stateService = new Moq.Mock<IApplicationStateService>();
        stateService.SetupGet(x => x.Current).Returns(state);
        var vm = new AiAssistantViewModel(orchestrator, reader, null, null, null, null, store, stateService.Object);
        vm.Question = "What does appamāda mean here?";

        var pending = vm.AskAsync(AiTask.Ask);
        await reader.Asked.Task;

        var drain = vm.DrainAsync(TimeSpan.FromSeconds(2));
        reader.Release.SetResult();

        Assert.True(await drain);
        await pending;
        Assert.Empty(vm.Turns);
        Assert.Empty(store.Saves);
        Assert.Equal("What does appamāda mean here?", vm.Question);
    }

    /// <summary>Once Quit has begun no new turn starts: one started after the drain would be lost exactly as #1018
    /// described.</summary>
    [Fact]
    public async Task No_turn_starts_after_the_drain()
    {
        var (vm, store, _, _) = Panel(Answering(Said("An answer.")));

        Assert.True(await vm.DrainAsync(Short));
        await vm.AskAsync(AiTask.Explain);

        Assert.Empty(vm.Turns);
        Assert.Empty(store.Saves);
    }

    // ---- Other writes in flight -------------------------------------------------------------------------

    /// <summary>A rename whose write is still pending: the drain waits for the new name to reach the file.</summary>
    [Fact]
    public async Task The_drain_waits_for_a_rename_in_progress()
    {
        var (vm, store, _, _) = Panel(Answering(Said("An answer.")));
        await vm.AskAsync(AiTask.Explain);
        var id = store.LastSaved!.Id;

        var hold = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        store.HoldNextSave = hold;
        var rename = vm.RenameSessionAsync(id, "On heedfulness");

        var drain = vm.DrainAsync(Generous);
        await Task.Delay(50);
        Assert.False(drain.IsCompleted);

        hold.SetResult(true);
        Assert.True(await drain);
        Assert.Equal("On heedfulness", store.OnDisk(id)!.Name);
        Assert.True(await rename);
    }

    /// <summary>
    /// A delete that has to check whether the file really went: the drain waits for its last word on
    /// <c>ActiveAssistantSessionId</c>. Here the delete failed and the conversation is put back, so the id the state
    /// save writes must be the one restored — not the empty one the delete started with.
    /// </summary>
    [Fact]
    public async Task The_drain_waits_for_a_delete_to_settle_the_active_id()
    {
        var (vm, store, state, _) = Panel(Answering(Said("An answer.")));
        await vm.AskAsync(AiTask.Explain);
        var id = store.LastSaved!.Id;

        store.DeleteFails = true;
        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Gate = gate;
        var delete = vm.DeleteSessionAsync(id);
        Assert.Null(state.ActiveAssistantSessionId);   // let go of, pending the check

        var drain = vm.DrainAsync(Generous);
        await Task.Delay(50);
        Assert.False(drain.IsCompleted);

        gate.SetResult();
        Assert.True(await drain);
        Assert.Equal(id, state.ActiveAssistantSessionId);
        await delete;
    }

    /// <summary>A switch still reading the conversation it is moving to: the drain waits for it to land, so the
    /// state save names the conversation on screen.</summary>
    [Fact]
    public async Task The_drain_waits_for_a_switch_to_land()
    {
        var (vm, store, state, _) = Panel(Answering(Said("An answer.")));
        await vm.AskAsync(AiTask.Explain);
        var first = store.LastSaved!.Id;
        vm.NewConversationCommand.Execute().Subscribe();
        await vm.AskAsync(AiTask.Translate);
        Assert.NotEqual(first, state.ActiveAssistantSessionId);

        var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        store.Gate = gate;
        var switching = vm.SwitchToSessionAsync(first);

        var drain = vm.DrainAsync(Generous);
        await Task.Delay(50);
        Assert.False(drain.IsCompleted);

        gate.SetResult();
        Assert.True(await drain);
        Assert.Equal(first, state.ActiveAssistantSessionId);
        await switching;
    }

    /// <summary>A compaction the model is still writing is stopped the way Stop stops it — nothing changed, nothing
    /// half-recorded — and the drain returns once it has unwound.</summary>
    [Fact]
    public async Task A_compaction_in_flight_is_stopped_and_the_drain_settles()
    {
        var orchestrator = new AiAssistantCompactionTests.CompactingOrchestrator();
        var (vm, store, _, _) = Panel(orchestrator);
        for (var i = 0; i < AiCompaction.KeepVerbatim + 1; i++) await vm.AskAsync(AiTask.Explain);
        var savesBefore = store.Saves.Count;

        orchestrator.HoldSummary = new TaskCompletionSource();   // released only by the token
        var compact = vm.CompactAsync();
        Assert.True(vm.IsCompacting);

        Assert.True(await vm.DrainAsync(Generous));
        await compact;

        Assert.False(vm.IsCompacting);
        Assert.Equal("Summarising was stopped. Nothing was changed.", vm.Status);
        Assert.Equal(savesBefore, store.Saves.Count);
    }

    /// <summary>Nor does a compaction start after the drain: a summary written then would be lost.</summary>
    [Fact]
    public async Task No_compaction_starts_after_the_drain()
    {
        var orchestrator = new AiAssistantCompactionTests.CompactingOrchestrator();
        var (vm, _, _, _) = Panel(orchestrator);
        for (var i = 0; i < AiCompaction.KeepVerbatim + 1; i++) await vm.AskAsync(AiTask.Explain);
        Assert.True(vm.CanCompact);

        Assert.True(await vm.DrainAsync(Short));
        await vm.CompactAsync();

        Assert.Empty(orchestrator.CompactRequests);
    }

    // ---- Nothing to drain -------------------------------------------------------------------------------

    /// <summary>With nothing running the drain answers at once and touches nothing — it does not even reach Stop.</summary>
    [Fact]
    public async Task With_nothing_running_the_drain_is_a_no_op()
    {
        var orchestrator = new StreamingOrchestrator();
        var (vm, store, state, _) = Panel(orchestrator);

        var drain = vm.DrainAsync(Generous);

        Assert.True(drain.IsCompletedSuccessfully);
        Assert.True(await drain);
        Assert.Equal(0, orchestrator.StopCalls);
        Assert.Empty(store.Saves);
        Assert.Null(state.ActiveAssistantSessionId);
    }

    /// <summary>A panel with no store has nothing to keep, so the drain leaves its turn alone and answers at once.</summary>
    [Fact]
    public async Task With_no_store_the_drain_is_a_no_op()
    {
        var orchestrator = new StreamingOrchestrator();
        var vm = new AiAssistantViewModel(orchestrator, new StubReaderState(), null, null);

        var pending = vm.AskAsync(AiTask.Explain);
        await orchestrator.Streaming.Task;

        var drain = vm.DrainAsync(Generous);
        Assert.True(drain.IsCompletedSuccessfully);
        Assert.True(await drain);
        Assert.Equal(0, orchestrator.StopCalls);
        Assert.True(vm.Turns.Single().IsRunning);

        vm.StopCommand.Execute().Subscribe();
        await pending;
    }
}
