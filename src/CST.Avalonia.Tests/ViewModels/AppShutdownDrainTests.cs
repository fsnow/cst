using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services.Ai;
using Xunit;

using static CST.Avalonia.Tests.ViewModels.AiAssistantSessionWiringTests;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// The order of the first two steps of Quit: settle the Assistant, then save application state. (#1018)
///
/// <para>A conversation's first turn names its session in <c>ActiveAssistantSessionId</c> when it ends. Ended after
/// the state save, the file is on disk and the next launch does not know to reopen it. <c>App</c> cannot be built
/// here, so the step is a static method with both halves handed in — the #1016 precedent,
/// <see cref="AppStateLoadSettleTests"/>.</para>
/// </summary>
public class AppShutdownDrainTests
{
    [Fact]
    public async Task The_drain_runs_before_the_state_save()
    {
        var order = new List<string>();

        await CST.Avalonia.App.DrainAssistantThenSaveStateAsync(
            async () => { await Task.Yield(); order.Add("drain"); },
            () => { order.Add("save"); return Task.CompletedTask; });

        Assert.Equal(new[] { "drain", "save" }, order);
    }

    /// <summary>A drain that throws costs the Assistant, never the state save behind it.</summary>
    [Fact]
    public async Task A_drain_that_throws_does_not_stop_the_state_save()
    {
        var saved = false;

        await CST.Avalonia.App.DrainAssistantThenSaveStateAsync(
            () => throw new InvalidOperationException("the panel could not be drained"),
            () => { saved = true; return Task.CompletedTask; });

        Assert.True(saved);
    }

    /// <summary>
    /// The smaller gap in #1018, end to end through the real panel: a conversation's first turn still streaming at
    /// Quit. By the time the state save runs, the session has been written and application state names it — what
    /// the save would serialize is the id of a file that exists.
    /// </summary>
    [Fact]
    public async Task A_first_turn_cut_off_by_quit_is_named_in_state_before_the_state_save()
    {
        var orchestrator = new AiAssistantShutdownDrainTests.StreamingOrchestrator();
        var (vm, store, state, _) = Panel(orchestrator);

        var pending = vm.AskAsync(AiTask.Explain);
        await orchestrator.Streaming.Task;
        Assert.Null(state.ActiveAssistantSessionId);

        string? idAtSave = null;
        var onDiskAtSave = false;
        await CST.Avalonia.App.DrainAssistantThenSaveStateAsync(
            () => vm.DrainAsync(TimeSpan.FromSeconds(10)),
            () =>
            {
                idAtSave = state.ActiveAssistantSessionId;
                onDiskAtSave = idAtSave is not null && store.Disk.ContainsKey(idAtSave);
                return Task.CompletedTask;
            });

        Assert.NotNull(idAtSave);
        Assert.True(onDiskAtSave);
        Assert.Equal(store.LastSaved!.Id, idAtSave);
        await pending;
    }
}
