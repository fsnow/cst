using System.Collections.ObjectModel;
using CST.Avalonia.Services;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// How the MainDock row splits its width between the tool rail and the documents. (#910)
///
/// <para>The defect this pins was not the arithmetic - that was always right - but who asked for it. The
/// rebalance hung off <c>HideAssistantPanel</c> alone, so hiding the last remaining tool reclaimed its width
/// only when that tool happened to be the assistant. The call-site half cannot be tested until #655 lands
/// headless support, because <c>LayoutViewModel</c>'s constructor builds a real layout and initialises host
/// windows. What is testable is the contract those call sites depend on: a row with no rail gives the
/// documents everything.</para>
/// </summary>
public class MainDockRowTests
{
    private const double Quarter = 0.25;

    private static ProportionalDock Row(params IDockable[] children) =>
        new()
        {
            Id = "MainDock",
            VisibleDockables = new ObservableCollection<IDockable>(children)
        };

    private static ProportionalDock Rail(string id = "LeftTools") => new() { Id = id, Proportion = 0.5 };
    private static DocumentDock Documents() => new() { Id = "MainDocumentDock", Proportion = 0.5 };

    [Fact]
    public void With_the_rail_present_the_two_columns_sum_to_one()
    {
        var rail = Rail();
        var documents = Documents();

        var documentsShare = CstDockFactory.ApplyMainDockRow(Row(rail, documents), Quarter);

        Assert.Equal(Quarter, rail.Proportion);
        Assert.Equal(1.0 - Quarter, documents.Proportion);
        Assert.Equal(1.0 - Quarter, documentsShare);
    }

    [Fact]
    public void Hiding_the_last_tool_hands_the_whole_width_to_the_documents()
    {
        // The row after the rail has been emptied and removed - which is what hiding the last tool does,
        // whichever tool it was. Leaving the documents at 0.75 here is the bug: the freed quarter belongs to
        // them, and if nobody says so the framework decides what to do with the gap.
        var documents = Documents();

        var documentsShare = CstDockFactory.ApplyMainDockRow(Row(documents), Quarter);

        Assert.Equal(1.0, documents.Proportion);
        Assert.Equal(1.0, documentsShare);
    }

    [Fact]
    public void A_rail_that_never_got_its_proportional_wrapper_is_still_found()
    {
        // EnsureLeftToolDock builds LeftTools around LeftToolDock, but a layout restored from disk can carry
        // the tool dock directly. Both spellings have to be recognised or the rail reads as absent and the
        // documents are handed width that is not free.
        var rail = Rail("LeftToolDock");
        var documents = Documents();

        CstDockFactory.ApplyMainDockRow(Row(rail, documents), Quarter);

        Assert.Equal(Quarter, rail.Proportion);
        Assert.Equal(1.0 - Quarter, documents.Proportion);
    }

    [Fact]
    public void A_row_that_does_not_exist_yet_is_not_an_error()
    {
        // Called during construction and on the recreate path, both of which can run before there is a row.
        // Null says "nothing stated" so the caller leaves its stored share alone rather than recording 1.0.
        Assert.Null(CstDockFactory.ApplyMainDockRow(null, Quarter));
        Assert.Null(CstDockFactory.ApplyMainDockRow(new ProportionalDock { Id = "MainDock" }, Quarter));
    }
}
