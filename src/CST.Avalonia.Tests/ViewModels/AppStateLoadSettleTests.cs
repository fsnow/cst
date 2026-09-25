using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CST.Avalonia.ViewModels;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// The order of what runs once application state has loaded. (#849; review L1)
///
/// <para>The Assistant's restore trigger must be told state has loaded BEFORE the open-book work, because that
/// work can throw and nothing after a throw runs: the trigger would believe state never loaded, and the Assistant
/// would have no restore and no session list for the whole session. <c>App</c> cannot be built here, so the
/// settle step is a static method with both steps handed in; these pin its order.</para>
/// </summary>
public class AppStateLoadSettleTests
{
    /// <summary>A trigger that records when it is told state loaded. Its "enabled" delegate is read inside
    /// <c>OnStateLoaded</c>, and returning false keeps it from posting a restore there is no panel for.</summary>
    private static AssistantRestoreTrigger Recording(List<string> order) =>
        new(() => { order.Add("trigger"); return false; }, () => null, _ => { });

    [Fact]
    public async Task The_trigger_is_told_before_the_open_book_work()
    {
        var order = new List<string>();

        await CST.Avalonia.App.SettleStateLoadAsync(
            Recording(order), () => { order.Add("open-book"); return Task.CompletedTask; });

        Assert.Equal(new[] { "trigger", "open-book" }, order);
    }

    /// <summary>The case the order exists for: the open-book work throws, the failure still propagates as it
    /// always did, and the trigger has already heard.</summary>
    [Fact]
    public async Task An_open_book_failure_does_not_stop_the_trigger_hearing()
    {
        var order = new List<string>();

        await Assert.ThrowsAsync<InvalidOperationException>(() => CST.Avalonia.App.SettleStateLoadAsync(
            Recording(order), () => throw new InvalidOperationException("the open-book panel could not be built")));

        Assert.Equal(new[] { "trigger" }, order);
    }
}
