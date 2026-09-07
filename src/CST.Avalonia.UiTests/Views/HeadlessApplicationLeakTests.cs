using System;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using CST.Avalonia.Input;
using CST.Avalonia.UiTests.TestSupport;
using Xunit;

namespace CST.Avalonia.UiTests.Views;

/// <summary>
/// The three facts that make this a separate test project, kept as assertions rather than as a comment
/// someone will later doubt. (#655)
///
/// <para>#655 asked for the headless harness inside <c>CST.Avalonia.Tests</c>. Putting it there took that
/// project from 2853 passed / 0 failed to 2778 passed / 88 failed, because an Avalonia application is
/// process-global and changes what unrelated code observes. If a future Avalonia scopes any of this per
/// thread or per session, one of these tests fails, and merging the two projects back together becomes a
/// decision that can be made on evidence.</para>
/// </summary>
public class HeadlessApplicationLeakTests
{
    [AvaloniaFact]
    public void An_avalonia_test_runs_inside_this_projects_headless_application()
    {
        Assert.IsType<HeadlessStyleTestApp>(Application.Current);
    }

    /// <summary>
    /// The one that cost 87 tests. <c>AiConnectionService.RaiseChanged</c> calls its subscribers directly
    /// when <c>Dispatcher.UIThread.CheckAccess()</c> is true and posts otherwise; with no Avalonia
    /// application in the process that check is true on any thread, so the view-model tests see their
    /// rebuild happen inline. Under a live session the UI thread is the session's own, the post lands in a
    /// queue only that thread can pump, and the rebuild never happens.
    /// </summary>
    [AvaloniaFact]
    public async Task The_dispatcher_belongs_to_the_session_thread_and_to_no_other()
    {
        Assert.True(Dispatcher.UIThread.CheckAccess(), "An [AvaloniaFact] body should be on the UI thread.");

        var elsewhere = await Task.Run(() => Dispatcher.UIThread.CheckAccess());

        Assert.False(elsewhere,
            "Dispatcher.UIThread is no longer bound to the session's thread. If that is now true of every " +
            "thread, the reason CST.Avalonia.UiTests exists has gone away - re-measure before merging.");
    }

    /// <summary>
    /// The 88th. <see cref="PlatformGesture.CommandModifier"/> prefers Avalonia's answer over its own OS
    /// check, and Avalonia's headless answer is <c>Control</c> whatever the machine is - so on macOS this
    /// application changes what that property returns, process-wide, for anyone who reads it.
    /// </summary>
    [AvaloniaFact]
    public void The_headless_backend_reports_Control_whatever_the_host_is()
    {
        Assert.Equal(
            KeyModifiers.Control,
            Application.Current!.PlatformSettings!.HotkeyConfiguration.CommandModifiers);

        Assert.Equal(KeyModifiers.Control, PlatformGesture.CommandModifier);

        // Stated plainly so the divergence is not something a reader has to work out: on a Mac the app's
        // real answer is Meta, and this is the stand-in disagreeing with the machine.
        if (OperatingSystem.IsMacOS())
            Assert.NotEqual(KeyModifiers.Meta, PlatformGesture.CommandModifier);
    }

    // And the application itself is visible from any thread, which is what makes the two above everyone's
    // problem rather than the UI thread's.
    [AvaloniaFact]
    public async Task The_application_is_visible_from_other_threads_too()
    {
        var seen = await Task.Run(() => Application.Current?.GetType().Name);

        Assert.Equal(nameof(HeadlessStyleTestApp), seen);
    }
}
