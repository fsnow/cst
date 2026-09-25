using System;
using System.Threading.Tasks;
using Avalonia.Threading;
using CST.Avalonia.Services;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace CST.Avalonia.ViewModels;

/// <summary>
/// When the Assistant panel reopens its last conversation and fills its session list: once application state has
/// loaded AND the assistant is on — whichever of the two becomes true second. (#849; review findings 5 and 2)
///
/// <para><b>Two events, one decision.</b> The panel's view model is built during the layout build, before
/// application state has loaded, so the restore cannot run from its constructor (the #87/#479 sequencing). The
/// launch path therefore restores when the state load finishes — but only if the assistant is on at that moment.
/// A reader who switches it on in Settings mid-session gets the panel from
/// <c>LayoutViewModel.ShowAssistantPanel</c>, and that panel was never restored: no conversation, and no session list
/// until its first turn ended. So both events come here: <see cref="OnStateLoaded"/> from the app,
/// <see cref="OnPanelShown"/> from the show path.</para>
///
/// <para><b>One lock, so no ordering falls between them.</b> The state load finishes on a background thread; the
/// panel is shown on the UI thread. A panel shown just before the load finishes sees "not loaded" and leaves it to the
/// load, and the load — taking the same lock after it — reads the assistant as on, because the Settings toggle wrote
/// the setting before it showed the panel. The first cut checked "enabled" early in <c>InitializeFromLoadedState</c>
/// and set "loaded" later, and a toggle between the two was restored by neither. (review, finding 5)</para>
///
/// <para><b>Once.</b> Both events can fire at launch — startup's own reconcile shows the panel too — and
/// <see cref="AiAssistantViewModel.RestoreOnceAsync"/> makes the second call do nothing.</para>
/// </summary>
internal sealed class AssistantRestoreTrigger
{
    /// <summary>The app's instance: the assistant's enabled state read from settings each time, the singleton panel
    /// resolved on the UI thread (resolving it creates it, which must not happen while the assistant is off), and
    /// the work posted and failure-isolated — a transcript must not be able to fail a launch or a panel show.</summary>
    internal static AssistantRestoreTrigger Shared { get; } = new(
        CstDockFactory.AssistantEnabled,
        () => App.ServiceProvider?.GetService<AiAssistantViewModel>(),
        work => Dispatcher.UIThread.Post(async void () =>
        {
            try
            {
                await work();
            }
            catch (Exception ex)
            {
                // An async void continuation: an escape here reaches the unhandled handler and kills a reading app
                // over a transcript.
                Log.Error(ex, "Could not restore the assistant conversation");
            }
        }));

    private readonly Func<bool> _assistantEnabled;
    private readonly Func<AiAssistantViewModel?> _resolvePanel;
    private readonly Action<Func<Task>> _post;
    private readonly object _gate = new();
    private bool _stateLoaded;

    /// <summary>Test seam: what "enabled" reads, how the panel is found, and how the work is scheduled.</summary>
    internal AssistantRestoreTrigger(
        Func<bool> assistantEnabled, Func<AiAssistantViewModel?> resolvePanel, Action<Func<Task>> post)
    {
        _assistantEnabled = assistantEnabled;
        _resolvePanel = resolvePanel;
        _post = post;
    }

    /// <summary>
    /// Application state has loaded — or failed to, leaving the defaults, which still has conversations to list.
    /// Restores now if the assistant is on; otherwise the panel restores when it is shown.
    /// </summary>
    internal void OnStateLoaded()
    {
        lock (_gate)
        {
            _stateLoaded = true;
            if (!_assistantEnabled()) return;
        }

        Restore();
    }

    /// <summary>
    /// The Assistant panel has been shown. Restores if state has loaded; before that, <see cref="OnStateLoaded"/>
    /// will — a restore now would read the default empty state and use up the once.
    /// </summary>
    internal void OnPanelShown()
    {
        lock (_gate)
        {
            if (!_stateLoaded) return;
        }

        Restore();
    }

    private void Restore() => _post(async () =>
    {
        var panel = _resolvePanel();
        if (panel != null) await panel.RestoreOnceAsync();
    });
}
