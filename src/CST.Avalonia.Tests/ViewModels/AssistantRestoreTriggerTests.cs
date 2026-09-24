using System.Collections.Generic;
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
/// When the Assistant panel restores: <see cref="AssistantRestoreTrigger"/>, and the show path that feeds it
/// (<see cref="LayoutViewModel.ShowAssistant"/>). (review findings 5 and 2)
///
/// <para>The panel restores once application state has loaded AND the assistant is on, whichever comes second — and
/// exactly once. A panel created by the Settings toggle mid-session was never restored before this.</para>
/// </summary>
public class AssistantRestoreTriggerTests
{
    private sealed class Silent : IAiChatOrchestrator
    {
        public async IAsyncEnumerable<AiTurnEvent> RunAsync(
            AiTurnRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            await Task.CompletedTask;
            yield break;
        }

        public void Stop()
        {
        }
    }

    private sealed class Rig
    {
        internal bool Enabled;
        internal int Resolved;
        internal readonly List<Task> Posted = new();
        internal readonly FakeStore Store = new();
        internal readonly AiAssistantViewModel Vm;
        internal readonly AssistantRestoreTrigger Trigger;

        internal Rig()
        {
            Store.Seed(new AiSession { Id = "last", Name = "Last" });
            (Vm, _, _, _) = Panel(new Silent(), store: Store,
                state: new ApplicationState { ActiveAssistantSessionId = "last" });
            Trigger = new AssistantRestoreTrigger(
                () => Enabled,
                () => { Resolved++; return Vm; },
                work => Posted.Add(work()));
        }

        internal Task Drained() => Task.WhenAll(Posted);
    }

    /// <summary>The Settings toggle mid-session: state loaded with the assistant off, so nothing was restored and
    /// the panel was not even created; switching it on shows the panel, and the show restores it. Fails if the show
    /// path does not tell the trigger.</summary>
    [Fact]
    public async Task A_panel_shown_after_state_loaded_with_the_assistant_off_is_restored()
    {
        var rig = new Rig();
        rig.Trigger.OnStateLoaded();
        await rig.Drained();
        Assert.Equal(0, rig.Resolved);   // off: the panel is not created

        rig.Enabled = true;
        var shown = false;
        LayoutViewModel.ShowAssistant(() => shown = true, rig.Trigger);
        await rig.Drained();

        Assert.True(shown);
        Assert.Equal(1, rig.Store.LoadCalls);
        Assert.Equal("Last", rig.Vm.ActiveSessionName);
        Assert.Single(rig.Vm.Sessions);
    }

    /// <summary>A panel shown before state has loaded is left to the load (a restore then would read the default
    /// empty state), and the load restores it — reading "enabled" when it finishes, not earlier. The first cut read
    /// it early, so a toggle between that read and the load finishing was restored by neither path.</summary>
    [Fact]
    public async Task A_panel_shown_before_state_loaded_is_restored_when_the_load_finishes()
    {
        var rig = new Rig();
        rig.Enabled = true;
        LayoutViewModel.ShowAssistant(() => { }, rig.Trigger);
        await rig.Drained();
        Assert.Equal(0, rig.Store.LoadCalls);

        rig.Trigger.OnStateLoaded();
        await rig.Drained();

        Assert.Equal(1, rig.Store.LoadCalls);
        Assert.Equal("Last", rig.Vm.ActiveSessionName);
    }

    /// <summary>At launch both fire — the state load and startup's own reconcile showing the panel — and the
    /// conversation is read once.</summary>
    [Fact]
    public async Task Launch_with_both_events_restores_once()
    {
        var rig = new Rig();
        rig.Enabled = true;
        rig.Trigger.OnStateLoaded();
        LayoutViewModel.ShowAssistant(() => { }, rig.Trigger);
        LayoutViewModel.ShowAssistant(() => { }, rig.Trigger);
        await rig.Drained();

        Assert.Equal(1, rig.Store.LoadCalls);
        Assert.Equal(1, rig.Store.ListCalls);
    }
}
