using Avalonia.Controls;
using Avalonia.Input;
using CST.Avalonia.Services;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// Which Avalonia focus landings count as interactions (#621, #635).
///
/// <para>
/// The rule was first written against the TYPE of element focus landed on, and that was the wrong key.
/// Activating a window makes Avalonia restore focus to whichever element held it last; the type it lands on
/// depends only on what the user happened to touch before leaving. A restore onto a tab item — a perfectly
/// ordinary configuration — walked straight past a type-based filter and was recorded as a fresh
/// interaction, reproducing the bug the filter existed to kill.
/// </para>
///
/// <para>
/// The distinction is causal, and Avalonia carries it: the restore in <c>FocusManager.SetFocusScope</c>
/// focuses with no navigation method (<see cref="NavigationMethod.Unspecified"/>), while a click focuses
/// with <see cref="NavigationMethod.Pointer"/>.
/// </para>
/// </summary>
public class DocumentFocusReporterTests
{
    private sealed class TestDocument : Document
    {
    }

    // ---- The cause ------------------------------------------------------------------------------

    [Theory]
    [InlineData(NavigationMethod.Pointer)]
    [InlineData(NavigationMethod.Tab)]
    [InlineData(NavigationMethod.Directional)]
    public void Focus_the_user_moved_is_an_interaction(NavigationMethod method)
    {
        Assert.True(DocumentFocusReporter.ShouldReport(method));
    }

    [Fact]
    public void Focus_moved_programmatically_is_not()
    {
        // The window-activation restore, and any Focus() the app performs itself. Ignoring the latter is
        // also right: opening a document raises the dock model's activation, and that feed speaks for it.
        Assert.False(DocumentFocusReporter.ShouldReport(NavigationMethod.Unspecified));
    }

    [Fact]
    public void The_rule_does_not_depend_on_what_the_focus_landed_on()
    {
        // #635 in one assertion. A restore is ignored and a click is recorded WHATEVER element is involved,
        // so no landing type — tab item, document view, toolbar button — can smuggle an echo through.
        Assert.False(DocumentFocusReporter.ShouldReport(NavigationMethod.Unspecified));
        Assert.True(DocumentFocusReporter.ShouldReport(NavigationMethod.Pointer));
    }

    // ---- The walk -------------------------------------------------------------------------------
    //
    // Untested before #635, and it is the half that decides WHICH dockable the rule is applied to.

    [Fact]
    public void A_tab_item_carries_its_dockable_directly()
    {
        // The shape that defeated the type-based rule: clicking a tab focuses the tab item, whose
        // DataContext IS the dockable, so the walk stops immediately and never reaches a document view.
        var doc = new TestDocument { Id = "a", Title = "A" };
        var tabItem = new ContentControl { DataContext = doc };

        Assert.Same(doc, DocumentFocusReporter.ResolveDockable(tabItem));
    }

    [Fact]
    public void A_control_inside_a_document_resolves_to_that_document()
    {
        // A toolbar button: no DataContext of its own worth stopping at, so the walk climbs to the view
        // that carries the dockable.
        var doc = new TestDocument { Id = "b", Title = "B" };
        var view = new Panel { DataContext = doc };
        var button = new Button();
        view.Children.Add(button);

        Assert.Same(doc, DocumentFocusReporter.ResolveDockable(button));
    }

    [Fact]
    public void The_nearest_dockable_wins_over_an_outer_one()
    {
        // Documents nest inside docks, which are dockables too. The walk must stop at the first one or a
        // click inside a book would be attributed to the dock that holds it.
        var inner = new TestDocument { Id = "inner", Title = "Inner" };
        var outer = new TestDocument { Id = "outer", Title = "Outer" };
        var outerPanel = new Panel { DataContext = outer };
        var innerPanel = new Panel { DataContext = inner };
        var button = new Button();
        innerPanel.Children.Add(button);
        outerPanel.Children.Add(innerPanel);

        Assert.Same(inner, DocumentFocusReporter.ResolveDockable(button));
    }

    [Fact]
    public void Focus_on_something_that_belongs_to_no_dockable_resolves_to_nothing()
    {
        Assert.Null(DocumentFocusReporter.ResolveDockable(new Button()));
        Assert.Null(DocumentFocusReporter.ResolveDockable(null));
        Assert.Null(DocumentFocusReporter.ResolveDockable("not a visual"));
    }

    [Fact]
    public void A_non_dockable_DataContext_is_walked_past_rather_than_stopped_at()
    {
        // Plenty of controls carry a DataContext that is not a dockable — an item template's item, a
        // sub-ViewModel. Stopping there would lose the document.
        var doc = new TestDocument { Id = "c", Title = "C" };
        var view = new Panel { DataContext = doc };
        var itemWithOwnContext = new Panel { DataContext = "some item" };
        var button = new Button();
        itemWithOwnContext.Children.Add(button);
        view.Children.Add(itemWithOwnContext);

        Assert.Same(doc, DocumentFocusReporter.ResolveDockable(button));
    }
}
