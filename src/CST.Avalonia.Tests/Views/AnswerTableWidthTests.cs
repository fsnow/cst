using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.Views;
using Xunit;

namespace CST.Avalonia.Tests.Views;

/// <summary>
/// #586: how an answer table divides the panel's width between its columns.
///
/// <para>Reported from use — wide tables never wrapped and had to be scrolled sideways to read, most often on
/// Explain. The columns were Auto inside a horizontally scrolling viewer, which cannot wrap: Auto asks a cell
/// how wide it wants to be and the viewer answers "as wide as you like", so a wrapping run reports its whole
/// unwrapped line.</para>
///
/// <para>Only the width split is testable here; that the cells then wrap is a layout fact this project has no
/// harness for (#655).</para>
/// </summary>
public class AnswerTableWidthTests
{
    private static AnswerCell Cell(string text) =>
        new(new List<AnswerSpan> { new(text, AnswerStyle.None) }, AnswerAlign.Left);

    private static IReadOnlyList<IReadOnlyList<AnswerCell>> Rows(params string[][] rows) =>
        rows.Select(r => (IReadOnlyList<AnswerCell>)r.Select(Cell).ToList()).ToList();

    /// <summary>
    /// A column of glosses gets more room than a column of single words.
    ///
    /// <para>Equal shares would be simpler and worse: it wraps the glosses to a ribbon while the short column
    /// sits mostly empty, which is the layout a word-analysis table exists to avoid.</para>
    /// </summary>
    [Fact]
    public void A_column_holding_more_text_gets_more_width()
    {
        var weights = AnswerTableView.ColumnWeights(
            Rows(
                new[] { "Word", "Meaning" },
                new[] { "dhamma", "the teaching, the truth, a phenomenon" }),
            columns: 2);

        Assert.True(weights[1] > weights[0]);
    }

    /// <summary>
    /// One long sentence does not take the whole table.
    ///
    /// <para>Past a certain length a cell wraps regardless, so more width buys the reader nothing and costs
    /// its neighbours everything — an unclamped weight would squeeze the other columns to a character
    /// wide.</para>
    /// </summary>
    [Fact]
    public void One_very_long_cell_does_not_starve_its_neighbours()
    {
        var weights = AnswerTableView.ColumnWeights(
            Rows(
                new[] { "Word", "Note" },
                new[] { "dhamma", new string('x', 400) }),
            columns: 2);

        Assert.True(weights[1] / weights[0] < 10);
    }

    /// <summary>An empty column keeps a share rather than collapsing to nothing — a header with no body text
    /// under it is still a column the reader can see.</summary>
    [Fact]
    public void An_empty_column_still_gets_a_share()
    {
        var weights = AnswerTableView.ColumnWeights(
            Rows(new[] { "Word", "" }, new[] { "dhamma", "" }),
            columns: 2);

        Assert.All(weights, w => Assert.True(w > 0));
    }

    /// <summary>A ragged row — fewer cells than the widest — must not throw or skew the split.</summary>
    [Fact]
    public void A_short_row_does_not_break_the_split()
    {
        var weights = AnswerTableView.ColumnWeights(
            Rows(new[] { "A", "B", "C" }, new[] { "only one" }),
            columns: 3);

        Assert.Equal(3, weights.Count);
        Assert.All(weights, w => Assert.True(w > 0));
    }

    /// <summary>Every column is measured, so the split reflects the whole table rather than its first
    /// row.</summary>
    [Fact]
    public void The_longest_cell_in_a_column_decides_it_not_the_header()
    {
        var weights = AnswerTableView.ColumnWeights(
            Rows(
                new[] { "A", "Long header here" },
                new[] { "a much longer body cell than the header", "x" }),
            columns: 2);

        Assert.True(weights[0] > weights[1]);
    }
}
