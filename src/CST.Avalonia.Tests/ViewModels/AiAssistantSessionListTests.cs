using System;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.ViewModels;
using Moq;
using Xunit;

// Both neighbours' helpers, shared rather than copied: the point of several tests here is that a switched-to
// conversation looks exactly like a restored one and a live one, which is only worth asserting if all three were
// built the same way.
using static CST.Avalonia.Tests.ViewModels.AiAssistantViewModelTests;
using static CST.Avalonia.Tests.ViewModels.AiAssistantSessionWiringTests;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// The Assistant's conversations as a list: switch, rename, delete. (#997, ASSISTANT_SESSIONS.md §3.3)
///
/// <para><b>[fsnow]</b>: <i>"I would like to create new conversations like in Claude Code, maybe also with a plus
/// button, while saving the current one."</i> Naming <i>"Auto from the first turn, renamable"</i>; scope <i>"One
/// global list"</i>; retention <i>"Forever, no cap"</i>; delete <i>"Yes, with confirmation"</i> — the confirmation
/// is the view's, so what is tested here is the delete that runs after it.</para>
///
/// <para>Against the fake store's in-memory disk, which keeps what was saved as JSON so that nothing here can pass
/// by reading the object the panel is still holding.</para>
/// </summary>
public class AiAssistantSessionListTests
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    private static AiSession Stored(string id, string name, DateTimeOffset lastActive, params string[] answers)
    {
        var session = new AiSession { Id = id, Name = name, Created = lastActive, LastActive = lastActive };
        foreach (var answer in answers)
            session.Turns.Add(new AiTurnRecord
            {
                Task = AiTask.Explain,
                Answer = answer,
                MarkedAnswer = answer,
                Citation = new AiCitationRecord { BookId = "s0101m.mul.xml", BookName = "book" },
                When = lastActive,
            });
        return session;
    }

    /// <summary>A conversation written by a live panel, so a switch to it is compared with the real thing rather
    /// than with a hand-built record.</summary>
    private static async Task<AiSession> WrittenByAPanel(FakeStore store)
    {
        var (writer, _, _, _) = Panel(
            AnsweringFrom(
                ContextFrom(),
                AiTurnEvent.ForText("The term **appamada** matters.", "The term **[[appamada]]** matters."),
                AiTurnEvent.ForReasoning("Thinking about the compound."),
                AiTurnEvent.ForUsage(new AiUsageReport(1024, 256))),
            store: store);

        await writer.AskAsync(AiTask.Explain);
        writer.Question = "what does the second word mean?";
        await writer.AskAsync(AiTask.Ask);

        return store.OnDisk(store.LastSaved!.Id)!;
    }

    // ---- The list ---------------------------------------------------------------------------------

    /// <summary>Newest-active first, as the store answers, and the conversation on screen is the one row marked
    /// active.</summary>
    [Fact]
    public async Task The_list_is_newest_active_first_and_marks_the_conversation_on_screen()
    {
        var store = new FakeStore();
        store.Seed(Stored("older", "Older", Monday, "a"));
        store.Seed(Stored("newest", "Newest", Monday.AddHours(2), "b"));
        store.Seed(Stored("middle", "Middle", Monday.AddHours(1), "c", "d"));

        var (vm, _, _, _) = Panel(new StubOrchestrator(), store: store,
            state: new ApplicationState { ActiveAssistantSessionId = "middle" });

        await vm.RestoreAsync();

        Assert.Equal(new[] { "newest", "middle", "older" }, vm.Sessions.Select(s => s.Id));
        Assert.Equal(new[] { false, true, false }, vm.Sessions.Select(s => s.IsActive));
        Assert.True(vm.HasSessions);

        var middle = vm.Sessions[1];
        Assert.Equal("Middle", middle.Name);
        Assert.Equal(2, middle.TurnCount);
        Assert.Equal(Monday.AddHours(1), middle.LastActive);
        Assert.Equal(new[] { "s0101m.mul.xml" }, middle.BookIds);
    }

    /// <summary>
    /// The shutdown gap in §3.2: a first turn saved after <c>ActiveAssistantSessionId</c> last reached disk leaves
    /// a file nothing points at. The list is what makes it recoverable — the panel opens empty, and the
    /// conversation is a row like any other, one switch away.
    /// </summary>
    [Fact]
    public async Task A_conversation_nothing_points_at_is_listed_and_can_be_reopened()
    {
        var store = new FakeStore();
        store.Seed(Stored("orphan", "Orphaned at quit", Monday, "An answer that survived."));

        var (vm, _, state, _) = Panel(new StubOrchestrator(), store: store);

        await vm.RestoreAsync();

        Assert.Empty(vm.Turns);
        var row = Assert.Single(vm.Sessions);
        Assert.Equal("orphan", row.Id);
        Assert.False(row.IsActive);

        await vm.SwitchToSessionAsync("orphan");

        Assert.Equal("An answer that survived.", Assert.Single(vm.Turns).Answer);
        Assert.Equal("orphan", state.ActiveAssistantSessionId);
    }

    /// <summary>Every operation leaves the list current: the first turn adds a row, later turns update it, and a
    /// rename, a delete, a switch and a new conversation each re-read it.</summary>
    [Fact]
    public async Task The_list_is_refreshed_after_every_operation()
    {
        var store = new FakeStore();
        store.Seed(Stored("other", "Other", Monday, "x"));
        var (vm, _, _, _) = Panel(Answering(Said("An answer.")), store: store);

        await vm.RestoreAsync();
        Assert.Single(vm.Sessions);
        var calls = store.ListCalls;

        await vm.AskAsync(AiTask.Explain);
        Assert.True(store.ListCalls > calls, "after a turn is saved");
        Assert.Equal(2, vm.Sessions.Count);
        var mine = vm.Sessions[0];
        Assert.True(mine.IsActive);
        Assert.Equal(1, mine.TurnCount);

        await vm.AskAsync(AiTask.Translate);
        Assert.Equal(2, mine.TurnCount);
        Assert.Same(mine, vm.Sessions[0]);

        calls = store.ListCalls;
        await vm.RenameSessionAsync("other", "Renamed");
        Assert.True(store.ListCalls > calls, "after a rename");
        Assert.Equal("Renamed", vm.Sessions.Single(s => s.Id == "other").Name);

        calls = store.ListCalls;
        await vm.SwitchToSessionAsync("other");
        Assert.True(store.ListCalls > calls, "after a switch");
        Assert.True(vm.Sessions.Single(s => s.Id == "other").IsActive);
        Assert.False(mine.IsActive);

        calls = store.ListCalls;
        vm.NewConversationCommand.Execute().Subscribe();
        Assert.True(store.ListCalls > calls, "after a new conversation");
        Assert.All(vm.Sessions, s => Assert.False(s.IsActive));

        calls = store.ListCalls;
        await vm.DeleteSessionAsync("other");
        Assert.True(store.ListCalls > calls, "after a delete");
        Assert.Equal(new[] { mine.Id }, vm.Sessions.Select(s => s.Id));
    }

    // ---- Switch -----------------------------------------------------------------------------------

    /// <summary>
    /// A conversation switched to renders exactly as the same conversation restored at launch. Both go through one
    /// read-and-map path, and this is what would catch a second one creeping in.
    /// </summary>
    [Fact]
    public async Task A_switched_to_conversation_renders_as_the_restored_one()
    {
        var store = new FakeStore();
        var written = await WrittenByAPanel(store);

        var (restoring, _, _, _) = Panel(new StubOrchestrator(), store: store,
            state: new ApplicationState { ActiveAssistantSessionId = written.Id });
        await restoring.RestoreAsync();

        var (switching, _, state, stateService) = Panel(new StubOrchestrator(), store: store);
        await switching.RestoreAsync();
        await switching.SwitchToSessionAsync(written.Id);

        Assert.Equal(2, restoring.Turns.Count);
        Assert.Equal(restoring.Turns.Count, switching.Turns.Count);

        for (var i = 0; i < restoring.Turns.Count; i++)
        {
            var r = restoring.Turns[i];
            var s = switching.Turns[i];

            Assert.Equal(r.Id, s.Id);
            Assert.Equal(r.Task, s.Task);
            Assert.Equal(r.Question, s.Question);
            Assert.Equal(r.PresetLabel, s.PresetLabel);
            Assert.Equal(r.Answer, s.Answer);
            Assert.Equal(r.MarkedAnswer, s.MarkedAnswer);
            Assert.Equal(r.Reasoning, s.Reasoning);
            Assert.Equal(r.Citation, s.Citation);
            Assert.Equal(r.CitationDetail, s.CitationDetail);
            Assert.Equal(r.Footer, s.Footer);
            Assert.Equal(r.Status, s.Status);
            Assert.Equal(r.Failed, s.Failed);
            Assert.Equal(r.Blocks.Count, s.Blocks.Count);
            Assert.Equal(r.Sent!.HasHistory, s.Sent!.HasHistory);
            Assert.Equal(r.Sent.History?.Count, s.Sent.History?.Count);
            Assert.Equal(AiAssistantViewModel.DescribeAsked(r), AiAssistantViewModel.DescribeAsked(s));
            Assert.False(s.IsRunning);
        }

        // And it is now the conversation the panel adds to, and the one the next launch reopens.
        Assert.Equal(written.Id, state.ActiveAssistantSessionId);
        stateService.Verify(x => x.MarkDirty(), Times.AtLeastOnce);
        Assert.True(switching.Sessions.Single(r => r.Id == written.Id).IsActive);
    }

    /// <summary>Switching to the conversation already on screen does nothing — not even a read.</summary>
    [Fact]
    public async Task Switching_to_the_conversation_on_screen_does_nothing()
    {
        var (vm, store, _, _) = Panel(Answering(Said("An answer.")));
        await vm.AskAsync(AiTask.Explain);
        var turn = vm.Turns[0];
        var loads = store.LoadCalls;

        await vm.SwitchToSessionAsync(store.LastSaved!.Id);

        Assert.Equal(loads, store.LoadCalls);
        Assert.Same(turn, Assert.Single(vm.Turns));
    }

    /// <summary>
    /// Refused while a turn runs, like every other command: the turn's save writes to whichever session the panel
    /// holds when it ends, so a switch mid-stream would file the answer in the wrong conversation.
    /// </summary>
    [Fact]
    public async Task A_switch_is_refused_while_a_turn_is_running()
    {
        var store = new FakeStore();
        store.Seed(Stored("elsewhere", "Elsewhere", Monday, "x"));
        var orchestrator = new BlockingOrchestrator();
        var (vm, _, state, _) = Panel(orchestrator, store: store);
        await vm.RestoreAsync();

        var pending = vm.AskAsync(AiTask.Explain);
        Assert.False(vm.CanSwitchSession);

        await vm.SwitchToSessionAsync("elsewhere");

        Assert.Single(vm.Turns);
        Assert.True(vm.Turns[0].IsRunning);

        orchestrator.Gate.SetResult();
        await pending;

        Assert.True(vm.CanSwitchSession);
        Assert.NotEqual("elsewhere", store.LastSaved!.Id);
        Assert.Equal(store.LastSaved.Id, state.ActiveAssistantSessionId);
        Assert.Single(store.OnDisk("elsewhere")!.Turns);
    }

    /// <summary>
    /// A target that cannot be read leaves the conversation on screen where it was, says where the file was kept,
    /// and drops out of the list. The next turn still goes into the conversation the reader was in.
    /// </summary>
    [Fact]
    public async Task Switching_to_an_unreadable_conversation_keeps_the_current_one()
    {
        var store = new FakeStore();
        store.Seed(Stored("broken", "Broken", Monday, "x"));
        store.UnreadableIds.Add("broken");

        var (vm, _, state, _) = Panel(Answering(Said("An answer.")), store: store);
        await vm.AskAsync(AiTask.Explain);
        var current = state.ActiveAssistantSessionId;

        await vm.SwitchToSessionAsync("broken");

        Assert.Equal("An answer.", Assert.Single(vm.Turns).Answer);
        Assert.Contains("/kept/broken.unreadable-1.json", vm.Status);
        Assert.Equal(current, state.ActiveAssistantSessionId);
        Assert.DoesNotContain(vm.Sessions, s => s.Id == "broken");
        Assert.False(vm.IsBusy);

        await vm.AskAsync(AiTask.Translate);
        Assert.Equal(current, store.LastSaved!.Id);
        Assert.Equal(2, store.LastSaved.Turns.Count);
    }

    /// <summary>A conversation that has simply gone — another window, or a hand-deleted file — gets its own
    /// sentence rather than the unreadable one.</summary>
    [Fact]
    public async Task Switching_to_a_conversation_that_is_gone_says_so()
    {
        var (vm, _, _, _) = Panel(Answering(Said("An answer.")));
        await vm.AskAsync(AiTask.Explain);

        await vm.SwitchToSessionAsync("gone");

        Assert.Single(vm.Turns);
        Assert.Contains("no longer on disk", vm.Status);
    }

    /// <summary>
    /// A follow-up after a switch continues the conversation switched TO: the model is shown that conversation's
    /// marked answers, and none of the one left behind.
    /// </summary>
    [Fact]
    public async Task A_follow_up_after_a_switch_replays_the_switched_to_conversation()
    {
        var store = new FakeStore();
        var (writer, _, _, _) = Panel(
            AnsweringFrom(ContextFrom(),
                AiTurnEvent.ForText("The term appamada matters.", "The term [[appamada]] matters.")),
            store: store);
        await writer.AskAsync(AiTask.Explain);
        var target = store.LastSaved!.Id;

        var next = AnsweringFrom(ContextFrom(), Said("A different conversation."));
        var (vm, _, _, _) = Panel(next, store: store);
        await vm.AskAsync(AiTask.Grammar);

        await vm.SwitchToSessionAsync(target);
        vm.Question = "what does the second word mean?";
        await vm.AskAsync(AiTask.Ask);

        var replayed = Assert.Single(next.Requests[^1].History!);
        Assert.Equal("The term [[appamada]] matters.", replayed.Answer);

        Assert.Equal(target, store.LastSaved!.Id);
        Assert.Equal(2, store.OnDisk(target)!.Turns.Count);
    }

    // ---- Rename -----------------------------------------------------------------------------------

    /// <summary>
    /// Renaming the conversation on screen renames the object the panel will save next, so the next turn keeps
    /// the new name rather than writing the auto-name back over it.
    /// </summary>
    [Fact]
    public async Task Renaming_the_active_conversation_survives_its_next_turn()
    {
        var (vm, store, _, _) = Panel(Answering(Said("An answer.")));
        await vm.AskAsync(AiTask.Explain);
        var id = store.LastSaved!.Id;

        Assert.True(await vm.RenameSessionAsync(id, "  Appamāda, with notes  "));

        Assert.Equal("Appamāda, with notes", store.OnDisk(id)!.Name);
        Assert.Equal("Appamāda, with notes", vm.Sessions.Single(s => s.Id == id).Name);

        await vm.AskAsync(AiTask.Translate);
        Assert.Equal("Appamāda, with notes", store.OnDisk(id)!.Name);
    }

    /// <summary>A listed conversation that is not on screen is renamed on disk, and the panel is left alone.
    /// </summary>
    [Fact]
    public async Task Renaming_a_listed_conversation_leaves_the_panel_alone()
    {
        var store = new FakeStore();
        store.Seed(Stored("other", "Explain · somewhere", Monday, "x"));
        var (vm, _, state, _) = Panel(Answering(Said("An answer.")), store: store);
        await vm.AskAsync(AiTask.Explain);
        var mine = state.ActiveAssistantSessionId;

        Assert.True(await vm.RenameSessionAsync("other", "Somewhere else"));

        Assert.Equal("Somewhere else", store.OnDisk("other")!.Name);
        Assert.Equal(mine, state.ActiveAssistantSessionId);
        Assert.Single(vm.Turns);
    }

    /// <summary>
    /// A rename does not move a conversation in the list: the list is ordered by when a turn last ended, and
    /// tidying a name is not using the conversation.
    /// </summary>
    [Fact]
    public async Task A_rename_does_not_change_the_order()
    {
        var store = new FakeStore();
        store.Seed(Stored("newer", "Newer", Monday.AddHours(1), "x"));
        store.Seed(Stored("older", "Older", Monday, "y"));
        var (vm, _, _, _) = Panel(new StubOrchestrator(), store: store);
        await vm.RestoreAsync();

        await vm.RenameSessionAsync("older", "Older, renamed");

        // The rename happened — without this, a rename that silently did nothing would pass as "order unchanged".
        Assert.Equal("Older, renamed", store.OnDisk("older")!.Name);
        Assert.Equal("Older, renamed", vm.Sessions[1].Name);

        Assert.Equal(new[] { "newer", "older" }, vm.Sessions.Select(s => s.Id));
        Assert.Equal(Monday, store.OnDisk("older")!.LastActive);
    }

    /// <summary>An empty or whitespace name is refused and the old one kept — a nameless row is a row the reader
    /// cannot find again.</summary>
    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData(null)]
    public async Task An_empty_name_is_refused(string? name)
    {
        var store = new FakeStore();
        store.Seed(Stored("kept", "The old name", Monday, "x"));
        var (vm, _, _, _) = Panel(new StubOrchestrator(), store: store);
        await vm.RestoreAsync();
        var saves = store.Saves.Count;

        Assert.False(await vm.RenameSessionAsync("kept", name));

        Assert.Equal(saves, store.Saves.Count);
        Assert.Equal("The old name", store.OnDisk("kept")!.Name);
        Assert.Equal("The old name", vm.Sessions[0].Name);
    }

    /// <summary>The command form, as the view will call it.</summary>
    [Fact]
    public async Task The_rename_command_takes_an_id_and_a_name()
    {
        var store = new FakeStore();
        store.Seed(Stored("kept", "The old name", Monday, "x"));
        var (vm, _, _, _) = Panel(new StubOrchestrator(), store: store);

        await vm.RenameSessionCommand.Execute(new AiSessionRename("kept", "The new name"));

        Assert.Equal("The new name", store.OnDisk("kept")!.Name);
    }

    // ---- Delete -----------------------------------------------------------------------------------

    /// <summary>Deleting a conversation that is not on screen removes it and touches nothing else.</summary>
    [Fact]
    public async Task Deleting_a_listed_conversation_leaves_the_panel_alone()
    {
        var store = new FakeStore();
        store.Seed(Stored("other", "Other", Monday, "x"));
        var (vm, _, state, _) = Panel(Answering(Said("An answer.")), store: store);
        await vm.AskAsync(AiTask.Explain);
        var mine = state.ActiveAssistantSessionId;

        await vm.DeleteSessionAsync("other");

        Assert.Null(store.OnDisk("other"));
        Assert.Single(vm.Turns);
        Assert.Equal(mine, state.ActiveAssistantSessionId);
        Assert.Equal(new[] { mine }, vm.Sessions.Select(s => s.Id));
        Assert.Equal("", vm.Status);
    }

    /// <summary>
    /// Deleting the conversation on screen leaves the panel as a new conversation would: empty, with no active id,
    /// so neither the next turn nor the next launch brings it back.
    /// </summary>
    [Fact]
    public async Task Deleting_the_active_conversation_empties_the_panel_and_clears_the_id()
    {
        var (vm, store, state, _) = Panel(Answering(Said("An answer.")));
        await vm.AskAsync(AiTask.Explain);
        var id = store.LastSaved!.Id;

        await vm.DeleteSessionCommand.Execute(id);

        Assert.Empty(vm.Turns);
        Assert.False(vm.HasTurns);
        Assert.Null(state.ActiveAssistantSessionId);
        Assert.Empty(vm.Sessions);
        Assert.Null(store.OnDisk(id));

        await vm.AskAsync(AiTask.Translate);
        Assert.NotEqual(id, store.LastSaved!.Id);
        Assert.Null(store.OnDisk(id));
    }

    /// <summary>
    /// The conversation a turn is running in cannot be deleted until the turn ends — its save would write the file
    /// straight back. Every other row can.
    /// </summary>
    [Fact]
    public async Task Only_the_running_conversation_is_protected_from_delete()
    {
        var store = new FakeStore();
        store.Seed(Stored("other", "Other", Monday, "x"));
        var orchestrator = new BlockingOrchestrator();
        var (vm, _, state, _) = Panel(orchestrator, store: store);

        // One finished turn, so the conversation exists and is listed, then a second one held open.
        orchestrator.Gate.SetResult();
        await vm.AskAsync(AiTask.Explain);
        var mine = state.ActiveAssistantSessionId!;

        var blocking = new BlockingOrchestrator();
        var (running, _, runningState, _) = Panel(blocking, store: store, state: state);
        await running.SwitchToSessionAsync(mine);
        var pending = running.AskAsync(AiTask.Translate);

        Assert.False(running.Sessions.Single(s => s.Id == mine).CanDelete);
        Assert.True(running.Sessions.Single(s => s.Id == "other").CanDelete);

        await running.DeleteSessionAsync(mine);
        Assert.NotNull(store.OnDisk(mine));
        Assert.Equal(2, running.Turns.Count);

        await running.DeleteSessionAsync("other");
        Assert.Null(store.OnDisk("other"));

        blocking.Gate.SetResult();
        await pending;

        Assert.True(running.Sessions.Single(s => s.Id == mine).CanDelete);
        Assert.Equal(mine, runningState.ActiveAssistantSessionId);
        Assert.Equal(2, store.OnDisk(mine)!.Turns.Count);
    }

    /// <summary>A delete the store could not carry out leaves everything as it was and says so; the list, which
    /// still has the row, is what tells the two apart.</summary>
    [Fact]
    public async Task A_delete_that_failed_leaves_the_conversation_and_says_so()
    {
        var (vm, store, state, _) = Panel(Answering(Said("An answer.")));
        await vm.AskAsync(AiTask.Explain);
        var id = store.LastSaved!.Id;
        store.DeleteFails = true;

        await vm.DeleteSessionAsync(id);

        Assert.Single(vm.Turns);
        Assert.Equal(id, state.ActiveAssistantSessionId);
        Assert.Contains("could not be deleted", vm.Status);
        Assert.Single(vm.Sessions);
    }

    // ---- Overlaps (#997 review, 2026-09-22) -------------------------------------------------------
    //
    // Each of these pins a guard that a mutation removed without any other test noticing: the switch holding
    // IsBusy, the switch recording which id it is loading, the refresh's latest-wins check — and the three defects
    // the review's probes found (P1, P1b, P3).

    /// <summary>
    /// A switch holds <c>IsBusy</c> while it reads, so a question asked mid-load is refused rather than answered
    /// into the conversation being left and then wiped off the screen when the load lands.
    /// </summary>
    [Fact]
    public async Task A_question_is_refused_while_a_switch_is_loading()
    {
        var store = new FakeStore();
        store.Seed(Stored("b", "B", Monday, "B's answer."));
        var orchestrator = Answering(Said("An answer."));
        var (vm, _, _, _) = Panel(orchestrator, store: store);

        var gate = new TaskCompletionSource();
        store.Gate = gate;
        var switching = vm.SwitchToSessionAsync("b");

        Assert.False(vm.CanAsk);
        Assert.False(vm.CanSwitchSession);
        await vm.AskAsync(AiTask.Explain);
        Assert.Empty(orchestrator.Requests);

        gate.SetResult();
        await switching;

        Assert.Equal("B's answer.", Assert.Single(vm.Turns).Answer);
        Assert.True(vm.CanAsk);
    }

    /// <summary>The conversation a switch is loading cannot be deleted under it: the switch would show a
    /// conversation whose file had gone, and the next turn's save would write it back.</summary>
    [Fact]
    public async Task A_delete_is_refused_while_a_switch_is_loading_that_conversation()
    {
        var store = new FakeStore();
        store.Seed(Stored("b", "B", Monday, "B's answer."));
        var (vm, _, state, _) = Panel(new StubOrchestrator(), store: store);
        await vm.RestoreAsync();

        var gate = new TaskCompletionSource();
        store.Gate = gate;
        var switching = vm.SwitchToSessionAsync("b");

        Assert.False(vm.Sessions.Single(s => s.Id == "b").CanDelete);
        await vm.DeleteSessionAsync("b");
        Assert.NotNull(store.OnDisk("b"));

        gate.SetResult();
        await switching;

        Assert.Single(vm.Turns);
        Assert.Equal("b", state.ActiveAssistantSessionId);
        Assert.True(vm.Sessions.Single(s => s.Id == "b").CanDelete);
    }

    /// <summary>Only the newest listing lands. An older one finishing late would put back a row that has since
    /// gone, or — as here — take away one that has since arrived.</summary>
    [Fact]
    public async Task A_listing_overtaken_by_a_newer_one_is_dropped()
    {
        var store = new FakeStore();
        store.Seed(Stored("a", "A", Monday, "x"));
        var (vm, _, _, _) = Panel(new StubOrchestrator(), store: store);
        await vm.RestoreAsync();

        store.HoldListings = true;
        var older = vm.RefreshSessionsAsync();
        store.Seed(Stored("b", "B", Monday.AddHours(1), "y"));
        var newer = vm.RefreshSessionsAsync();

        store.HeldListings[1].SetResult();
        await newer;
        store.HeldListings[0].SetResult();
        await older;

        Assert.Equal(new[] { "b", "a" }, vm.Sessions.Select(s => s.Id));
    }

    /// <summary>
    /// A turn's list refresh runs outside its busy window. The store lists by reading every file in full; awaited,
    /// it held every command disabled (and Stop showing) after every answer for as long as that took.
    /// </summary>
    [Fact]
    public async Task A_turn_does_not_wait_for_the_list_to_refresh()
    {
        var (vm, store, _, _) = Panel(Answering(Said("An answer.")));
        store.HoldListings = true;

        var ask = vm.AskAsync(AiTask.Explain);
        var finished = await Task.WhenAny(ask, Task.Delay(TimeSpan.FromSeconds(10)));

        Assert.Same(ask, finished);
        Assert.False(vm.IsBusy);
        Assert.Single(store.HeldListings);

        store.HeldListings[0].SetResult();
    }

    /// <summary>
    /// Review probe P1: the active conversation deleted, then another, before the first delete's listing returns.
    /// The first listing is overtaken and dropped — which used to leave the first delete reading a stale list,
    /// reporting a failure, keeping the transcript on screen and letting the next turn write the file back.
    /// </summary>
    [Fact]
    public async Task Deleting_the_active_conversation_then_another_quickly_deletes_both()
    {
        var store = new FakeStore();
        store.Seed(Stored("a", "A", Monday.AddHours(1), "A's answer."));
        store.Seed(Stored("b", "B", Monday, "B's answer."));
        var (vm, _, state, _) = Panel(Answering(Said("An answer.")), store: store,
            state: new ApplicationState { ActiveAssistantSessionId = "a" });
        await vm.RestoreAsync();
        Assert.Single(vm.Turns);

        store.HoldListings = true;
        var first = vm.DeleteSessionAsync("a");

        // Let go of before anything was awaited.
        Assert.Empty(vm.Turns);
        Assert.Null(state.ActiveAssistantSessionId);

        var second = vm.DeleteSessionAsync("b");
        store.HeldListings[0].SetResult();
        await first;
        store.HeldListings[1].SetResult();
        await second;

        Assert.DoesNotContain("could not be deleted", vm.Status);
        Assert.Empty(vm.Turns);
        Assert.Empty(vm.Sessions);
        Assert.Null(store.OnDisk("a"));
        Assert.Null(store.OnDisk("b"));

        store.HoldListings = false;
        await vm.AskAsync(AiTask.Explain);
        Assert.NotEqual("a", store.LastSaved!.Id);
        Assert.Null(store.OnDisk("a"));
    }

    /// <summary>Review probe P1b: + pressed while a delete's listing is out. The delete's listing is overtaken, and
    /// that is not a failed delete.</summary>
    [Fact]
    public async Task A_new_conversation_during_a_delete_is_not_a_failed_delete()
    {
        var store = new FakeStore();
        store.Seed(Stored("a", "A", Monday.AddHours(1), "A's answer."));
        store.Seed(Stored("b", "B", Monday, "B's answer."));
        var (vm, _, _, _) = Panel(new StubOrchestrator(), store: store,
            state: new ApplicationState { ActiveAssistantSessionId = "a" });
        await vm.RestoreAsync();

        store.HoldListings = true;
        var deleting = vm.DeleteSessionAsync("b");
        vm.NewConversationCommand.Execute().Subscribe();

        store.HeldListings[0].SetResult();
        await deleting;
        store.HeldListings[1].SetResult();

        Assert.DoesNotContain("could not be deleted", vm.Status);
        Assert.Null(store.OnDisk("b"));
        Assert.Equal(new[] { "a" }, vm.Sessions.Select(s => s.Id));
    }

    /// <summary>
    /// Review probe P3: a rename of the conversation a switch is loading. The switch had already read the old name;
    /// without waiting for it, the rename reached disk, the switch showed the old name, and the next turn wrote the
    /// old name back. The rename now waits for the switch and renames what it put on screen.
    /// </summary>
    [Fact]
    public async Task A_rename_during_a_switch_to_that_conversation_is_kept()
    {
        var store = new FakeStore();
        store.Seed(Stored("b", "Old name", Monday, "B's answer."));
        var (vm, _, _, _) = Panel(Answering(Said("An answer.")), store: store);
        await vm.RestoreAsync();

        var gate = new TaskCompletionSource();
        store.Gate = gate;
        var switching = vm.SwitchToSessionAsync("b");
        var renaming = vm.RenameSessionAsync("b", "New name");

        gate.SetResult();
        await switching;
        Assert.True(await renaming);

        Assert.Equal("New name", store.OnDisk("b")!.Name);
        Assert.Equal("New name", vm.Sessions.Single(s => s.Id == "b").Name);

        await vm.AskAsync(AiTask.Translate);
        Assert.Equal("b", store.LastSaved!.Id);
        Assert.Equal("New name", store.OnDisk("b")!.Name);
    }

    /// <summary>A rename is allowed while a turn runs in that conversation, and the turn's own save keeps it.
    /// </summary>
    [Fact]
    public async Task A_rename_while_a_turn_runs_is_kept_by_the_turns_save()
    {
        var store = new FakeStore();
        var state = new ApplicationState();
        var (writer, _, _, _) = Panel(Answering(Said("An answer.")), store: store, state: state);
        await writer.AskAsync(AiTask.Explain);
        var id = state.ActiveAssistantSessionId!;

        var blocking = new BlockingOrchestrator();
        var (vm, _, _, _) = Panel(blocking, store: store, state: state);
        await vm.RestoreAsync();
        var pending = vm.AskAsync(AiTask.Translate);
        Assert.True(vm.IsBusy);
        Assert.True(vm.CanRenameSession);

        Assert.True(await vm.RenameSessionAsync(id, "Named mid-turn"));
        Assert.Equal("Named mid-turn", store.OnDisk(id)!.Name);

        blocking.Gate.SetResult();
        await pending;

        var onDisk = store.OnDisk(id)!;
        Assert.Equal("Named mid-turn", onDisk.Name);
        Assert.Equal(2, onDisk.Turns.Count);
    }

    // ---- Which unreadable files are announced -----------------------------------------------------

    /// <summary>
    /// A broken file found while LISTING is logged, not announced: the list is read at launch and after every
    /// turn, and the sentence would be about a conversation the reader never opened.
    /// </summary>
    [Fact]
    public async Task A_broken_file_found_while_listing_is_not_announced()
    {
        var (vm, store, _, _) = Panel(Answering(Said("An answer.")));
        store.ListReportsUnreadable = new AiSessionUnreadable("y", "/kept/y.unreadable-1.json", "truncated");

        await vm.RestoreAsync();
        Assert.Equal("", vm.Status);

        await vm.AskAsync(AiTask.Explain);
        Assert.Equal("", vm.Status);
    }

    /// <summary>
    /// A switch to a conversation that is simply gone says so, even when a listing running at the same time trips
    /// over some OTHER broken file: the report is matched by id, not by "something was reported meanwhile".
    /// </summary>
    [Fact]
    public async Task A_missing_switch_target_is_not_confused_with_another_files_report()
    {
        var (vm, store, _, _) = Panel(new StubOrchestrator());

        var gate = new TaskCompletionSource();
        store.Gate = gate;
        var switching = vm.SwitchToSessionAsync("x");

        store.ListReportsUnreadable = new AiSessionUnreadable("y", "/kept/y.unreadable-1.json", "truncated");
        await vm.RefreshSessionsAsync();

        gate.SetResult();
        await switching;

        Assert.Contains("no longer on disk", vm.Status);
        Assert.DoesNotContain("/kept/y", vm.Status);
    }

    /// <summary>A rename of a conversation whose file turns out unreadable keeps the sentence saying where it was
    /// kept, rather than replacing it with "no longer on disk".</summary>
    [Fact]
    public async Task Renaming_an_unreadable_conversation_says_where_it_was_kept()
    {
        var store = new FakeStore();
        store.Seed(Stored("broken", "Broken", Monday, "x"));
        store.UnreadableIds.Add("broken");
        var (vm, _, _, _) = Panel(new StubOrchestrator(), store: store);

        Assert.False(await vm.RenameSessionAsync("broken", "A new name"));

        Assert.Contains("/kept/broken.unreadable-1.json", vm.Status);
    }
}
