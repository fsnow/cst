using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.ViewModels;
using Xunit;
using static CST.Avalonia.Tests.ViewModels.AiAssistantSessionWiringTests;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// The session operations against each other and against a file that will not open: the findings of the
/// integrated review of the sessions subsystem (2026-09-23), each pinned by the reviewer's probe turned into a
/// test. (#997, #998, ASSISTANT_SESSIONS.md §3.3)
///
/// <para>Against the fake store's in-memory disk, which keeps what was saved as JSON, so nothing here can pass by
/// reading the object the panel is still holding.</para>
/// </summary>
public class AiAssistantSessionOverlapTests
{
    /// <summary>Answers every turn, numbered; <see cref="Hold"/> keeps the next turn open after it has started, so
    /// "during a turn" is a state rather than a race.</summary>
    private sealed class Answering : IAiChatOrchestrator
    {
        private int _n;
        internal TaskCompletionSource? Hold;

        public async IAsyncEnumerable<AiTurnEvent> RunAsync(
            AiTurnRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            yield return AiTurnEvent.ForStarted(ContextFrom());
            if (Hold is { } hold)
            {
                Hold = null;
                await hold.Task;
            }

            var n = ++_n;
            yield return AiTurnEvent.ForText($"Answer {n}", $"Answer {n} [[x]]");
            yield return AiTurnEvent.ForCompleted(new PaliMarkerReport(1, 0));
        }

        public void Stop()
        {
        }
    }

    // ---- finding 2: a failed rename ---------------------------------------------------------------------

    /// <summary>
    /// A rename whose save failed is not applied later. The first cut set the name on the object the panel holds
    /// before saving and left it there, so the reader was told the rename failed and the next turn's save wrote it
    /// anyway. (review probe A)
    /// </summary>
    [Fact]
    public async Task A_rename_whose_save_failed_is_not_written_by_the_next_turn()
    {
        var (vm, store, state, _) = Panel(new Answering());
        await vm.AskAsync(AiTask.Explain);
        await vm.RefreshSessionsAsync();
        var id = state.ActiveAssistantSessionId!;
        var before = store.OnDisk(id)!.Name;

        store.SaveSucceeds = false;
        Assert.False(await vm.RenameSessionAsync(id, "Renamed"));
        Assert.Equal("That conversation could not be renamed.", vm.Status);
        Assert.Equal(before, vm.ActiveSessionName);

        store.SaveSucceeds = true;
        await vm.AskAsync(AiTask.Explain);
        await vm.RefreshSessionsAsync();

        Assert.Equal(before, store.OnDisk(id)!.Name);
        Assert.Equal(before, vm.ActiveSessionName);
    }

    // ---- finding 3: rename against delete and switch ----------------------------------------------------

    /// <summary>
    /// A delete that arrives while a rename of the same conversation is reading it stays deleted. The rename's
    /// write used to land after the delete and put the file back. (review probe C)
    /// </summary>
    [Fact]
    public async Task A_delete_during_a_rename_is_not_undone_by_the_rename()
    {
        var (vm, store, state, _) = Panel(new Answering());
        await vm.AskAsync(AiTask.Explain);
        var x = state.ActiveAssistantSessionId!;
        vm.NewConversationCommand.Execute().Subscribe();
        await vm.RefreshSessionsAsync();

        var gate = new TaskCompletionSource();
        store.Gate = gate;
        var rename = vm.RenameSessionAsync(x, "Mine");
        var delete = vm.DeleteSessionAsync(x);

        gate.SetResult();
        await rename;
        await delete;

        Assert.Null(store.OnDisk(x));
        Assert.DoesNotContain(vm.Sessions, r => r.Id == x);
    }

    /// <summary>
    /// A switch to a conversation whose rename is between its read and its write shows the NEW name, and the next
    /// turn keeps it. Unserialised, the switch read the file before the rename wrote it, showed the old name, and
    /// the next turn's save wrote the old name back over the rename. (review, reasoned; built here with two held
    /// reads.)
    /// </summary>
    [Fact]
    public async Task A_switch_that_reads_during_a_rename_shows_and_keeps_the_new_name()
    {
        var (vm, store, state, _) = Panel(new Answering());
        await vm.AskAsync(AiTask.Explain);
        var x = state.ActiveAssistantSessionId!;
        vm.NewConversationCommand.Execute().Subscribe();
        await vm.AskAsync(AiTask.Explain);
        await vm.RefreshSessionsAsync();

        var renameRead = new TaskCompletionSource();
        var switchRead = new TaskCompletionSource();
        store.Gates.Enqueue(renameRead);
        store.Gates.Enqueue(switchRead);

        var rename = vm.RenameSessionAsync(x, "Mine");
        var @switch = vm.SwitchToSessionAsync(x);

        renameRead.SetResult();
        await rename;
        switchRead.SetResult();
        await @switch;

        Assert.Equal("Mine", vm.ActiveSessionName);

        await vm.AskAsync(AiTask.Explain);
        Assert.Equal("Mine", store.OnDisk(x)!.Name);
    }

    // ---- finding 4: a restore that failed, and the row's Delete ----------------------------------------

    /// <summary>A stored duration outside TimeSpan's range: the store reads it, and mapping it throws — the
    /// launch restore's <c>Failed</c> outcome, with the file still on disk.</summary>
    private static FakeStore StoreWithAnUnmappableSession(string id)
    {
        var store = new FakeStore();
        var session = new AiSession { Id = id, Name = "Broken" };
        session.Turns.Add(new AiTurnRecord
        {
            Id = "t1", Task = AiTask.Explain, AskedLine = "a", Answer = "b", ElapsedMs = long.MaxValue,
        });
        store.Seed(session);
        return store;
    }

    /// <summary>
    /// After a restore that could not open the conversation, its row's Delete does what it offers — also during
    /// the first turn of the next conversation. The row computed <c>CanDelete</c> from the session the panel held
    /// while the delete refused on the id in application state, so the confirm did nothing. (review probe F) The
    /// running turn stays on screen, and the next launch no longer names the deleted file.
    /// </summary>
    [Fact]
    public async Task A_row_whose_restore_failed_can_be_deleted_as_it_offers_during_a_turn()
    {
        var store = StoreWithAnUnmappableSession("broken");
        var answering = new Answering();
        var (vm, _, state, _) = Panel(answering, store: store,
            state: new ApplicationState { ActiveAssistantSessionId = "broken" });

        await vm.RestoreAsync();
        Assert.Equal("The last conversation could not be reopened. It is still on disk.", vm.Status);
        Assert.Equal("broken", state.ActiveAssistantSessionId);   // kept, so the next launch tries again

        var hold = new TaskCompletionSource();
        answering.Hold = hold;
        var turn = vm.AskAsync(AiTask.Explain);
        Assert.True(vm.IsBusy);

        var row = vm.Sessions.Single(r => r.Id == "broken");
        Assert.True(row.CanDelete);

        await vm.DeleteSessionAsync("broken");
        Assert.Null(store.OnDisk("broken"));
        Assert.Single(vm.Turns);                                   // the running turn is untouched
        Assert.NotEqual("broken", state.ActiveAssistantSessionId);

        hold.SetResult();
        await turn;
        Assert.NotNull(state.ActiveAssistantSessionId);
        Assert.NotEqual("broken", state.ActiveAssistantSessionId);
        Assert.Null(store.OnDisk("broken"));
    }

    /// <summary>The contrast: the conversation a turn is running IN still cannot be deleted, and its row says so —
    /// the same predicate answers both.</summary>
    [Fact]
    public async Task The_conversation_a_turn_runs_in_is_refused_and_its_row_says_so()
    {
        var answering = new Answering();
        var (vm, store, state, _) = Panel(answering);
        await vm.AskAsync(AiTask.Explain);
        await vm.RefreshSessionsAsync();
        var id = state.ActiveAssistantSessionId!;

        var hold = new TaskCompletionSource();
        answering.Hold = hold;
        var turn = vm.AskAsync(AiTask.Explain);

        Assert.False(vm.Sessions.Single(r => r.Id == id).CanDelete);
        await vm.DeleteSessionAsync(id);
        Assert.NotNull(store.OnDisk(id));

        hold.SetResult();
        await turn;
    }

    // ---- finding 1: a file that cannot be opened just now ----------------------------------------------

    /// <summary>
    /// A switch to a conversation whose file cannot be opened just now leaves the current one on screen, says so
    /// in words that do not claim the file was moved, and leaves the file where it is.
    /// </summary>
    [Fact]
    public async Task A_switch_to_a_file_that_cannot_be_opened_just_now_says_so_and_leaves_it()
    {
        var (vm, store, state, _) = Panel(new Answering());
        await vm.AskAsync(AiTask.Explain);
        var x = state.ActiveAssistantSessionId!;
        vm.NewConversationCommand.Execute().Subscribe();
        await vm.AskAsync(AiTask.Explain);
        var current = state.ActiveAssistantSessionId!;

        store.TransientIds.Add(x);
        await vm.SwitchToSessionAsync(x);

        Assert.Equal("That conversation could not be opened just now. It is still on disk.", vm.Status);
        Assert.Equal(current, state.ActiveAssistantSessionId);
        Assert.NotNull(store.OnDisk(x));

        store.TransientIds.Clear();
        await vm.SwitchToSessionAsync(x);
        Assert.Equal(x, state.ActiveAssistantSessionId);
    }

    /// <summary>The launch restore of such a file starts empty, says so, and keeps the id so the next launch
    /// tries again.</summary>
    [Fact]
    public async Task A_restore_of_a_file_that_cannot_be_opened_just_now_keeps_the_id()
    {
        var store = new FakeStore();
        store.Seed(new AiSession { Id = "held", Name = "Held" });
        store.TransientIds.Add("held");
        var (vm, _, state, _) = Panel(new Answering(), store: store,
            state: new ApplicationState { ActiveAssistantSessionId = "held" });

        await vm.RestoreAsync();

        Assert.Empty(vm.Turns);
        Assert.Equal("The last conversation could not be opened just now. It is still on disk.", vm.Status);
        Assert.Equal("held", state.ActiveAssistantSessionId);
        Assert.NotNull(store.OnDisk("held"));
    }

    /// <summary>A rename of such a file writes nothing and says why.</summary>
    [Fact]
    public async Task A_rename_of_a_file_that_cannot_be_opened_just_now_says_so()
    {
        var store = new FakeStore();
        store.Seed(new AiSession { Id = "held", Name = "Held" });
        var (vm, _, _, _) = Panel(new Answering(), store: store);
        store.TransientIds.Add("held");

        Assert.False(await vm.RenameSessionAsync("held", "Mine"));
        Assert.Equal("That conversation could not be renamed just now.", vm.Status);
        Assert.Equal("Held", store.OnDisk("held")!.Name);
    }

    /// <summary>A delete the store could not carry out, of a file that then cannot be opened either, is a delete
    /// that FAILED — the file is still there. It used to count as gone, because the check after a failed delete
    /// was "does it load".</summary>
    [Fact]
    public async Task A_failed_delete_of_a_file_that_cannot_be_opened_just_now_is_reported_as_failed()
    {
        var store = new FakeStore();
        store.Seed(new AiSession { Id = "held", Name = "Held" });
        var (vm, _, _, _) = Panel(new Answering(), store: store);
        store.TransientIds.Add("held");
        store.DeleteFails = true;

        await vm.DeleteSessionAsync("held");

        Assert.Equal("That conversation could not be deleted.", vm.Status);
        Assert.NotNull(store.OnDisk("held"));
    }

    // ---- finding 5: a panel that appears after launch -----------------------------------------------------

    /// <summary>
    /// The launch and the panel appearing later (the Settings toggle, #667) both call
    /// <see cref="AiAssistantViewModel.RestoreOnceAsync"/>; the second does nothing, so launch never restores twice.
    /// </summary>
    [Fact]
    public async Task Restore_once_restores_once_whichever_caller_is_first()
    {
        var store = new FakeStore();
        store.Seed(new AiSession { Id = "last", Name = "Last" });
        var (vm, _, _, _) = Panel(new Answering(), store: store,
            state: new ApplicationState { ActiveAssistantSessionId = "last" });

        await vm.RestoreOnceAsync();
        await vm.RestoreOnceAsync();

        Assert.Equal(1, store.LoadCalls);
        Assert.Equal(1, store.ListCalls);
        Assert.Equal("Last", vm.ActiveSessionName);
        Assert.Single(vm.Sessions);
    }

    /// <summary>The launch path's own <see cref="AiAssistantViewModel.RestoreAsync"/> counts as the once, too.</summary>
    [Fact]
    public async Task Restore_once_after_a_restore_does_nothing()
    {
        var store = new FakeStore();
        store.Seed(new AiSession { Id = "last", Name = "Last" });
        var (vm, _, _, _) = Panel(new Answering(), store: store,
            state: new ApplicationState { ActiveAssistantSessionId = "last" });

        await vm.RestoreAsync();
        await vm.RestoreOnceAsync();

        Assert.Equal(1, store.LoadCalls);
        Assert.Equal(1, store.ListCalls);
    }
}
