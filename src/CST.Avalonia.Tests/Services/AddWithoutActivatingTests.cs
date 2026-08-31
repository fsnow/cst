using System.Collections.ObjectModel;
using CST.Avalonia.Services;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// Adding a tool to a dock must not make it the active tab. (#919)
///
/// <para><b>The defect this pins.</b> All four tools share one rail since #906. The layout is always built
/// before settings load — deterministically, not as a race — so a reader with the assistant switched on gets
/// a rail without it, and the reconcile adds it once the settings are real. That add used to activate the
/// panel, and the dock monitor records every activation as the reader's saved tab, unable to tell the app's
/// own from a click. The write landed after the state file was read and before #91 captured the saved id, so
/// #91 restored the assistant faithfully — from a value the app had just written over the reader's. Before
/// #906 the assistant went into a dock of its own, where activating it could disturb nothing.</para>
///
/// <para><b>Why this test and not the call site.</b> <c>LayoutViewModel</c>'s constructor builds a real
/// layout and initialises host windows, so the call-site half cannot be tested until #655 lands headless
/// support — the same limit <c>MainDockRowTests</c> records. What is testable is the assumption the fix
/// rests on: that adding a dockable is not itself an activation. If a future Dock.Avalonia made
/// <c>AddDockable</c> activate what it adds, the fix would be undone silently and every other test would
/// still pass.</para>
/// </summary>
public class AddWithoutActivatingTests
{
    private static ToolDock Rail(params IDockable[] tools)
    {
        var dock = new ToolDock
        {
            Id = "LeftToolDock",
            VisibleDockables = new ObservableCollection<IDockable>(tools),
        };
        if (tools.Length > 0) dock.ActiveDockable = tools[0];
        return dock;
    }

    private static Tool Tool(string id) => new() { Id = id, Title = id };

    [Fact]
    public void Adding_a_tool_leaves_the_active_tab_alone()
    {
        var factory = new CstDockFactory();
        var restored = Tool("SearchTool");
        var rail = Rail(Tool("OpenBookTool"), restored);
        rail.ActiveDockable = restored;          // as #91's restore leaves it
        rail.Factory = factory;

        var assistant = Tool("AiAssistantTool");
        assistant.Factory = factory;
        factory.AddDockable(rail, assistant);

        Assert.Same(restored, rail.ActiveDockable);
        Assert.Contains(assistant, rail.VisibleDockables!);
    }

    /// <summary>
    /// And the opposite half, so the test cannot pass by the framework simply never activating anything:
    /// an explicit activation still works. A reader choosing View → AI Assistant must land on it.
    /// </summary>
    [Fact]
    public void An_explicit_activation_still_takes_the_tab()
    {
        var factory = new CstDockFactory();
        var rail = Rail(Tool("OpenBookTool"), Tool("SearchTool"));
        rail.Factory = factory;

        var assistant = Tool("AiAssistantTool");
        assistant.Factory = factory;
        factory.AddDockable(rail, assistant);
        factory.SetActiveDockable(assistant);

        Assert.Same(assistant, rail.ActiveDockable);
    }
}
