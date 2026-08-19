using System.Collections.Generic;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Documents;
using Avalonia.Layout;
using Avalonia.Media;
using CST.Avalonia.Services.Ai;

namespace CST.Avalonia.Views;

/// <summary>
/// Draws a parsed <see cref="AnswerTable"/> into a <see cref="Grid"/>. (#586)
///
/// <para><b>Why a real grid and not aligned monospace.</b> The word-analysis table we hand the model is a
/// Markdown table, so a word-by-word answer comes back as one — and in a 25%-wide panel, monospace pipes
/// wrap and stop lining up at exactly the width where the table was supposed to help.</para>
///
/// <para><b>Columns share the panel's width rather than sizing to their content.</b> They were
/// <see cref="GridLength.Auto"/> inside a horizontally scrolling viewer, and that combination cannot wrap:
/// Auto asks a cell how wide it wants to be, the viewer offers infinite width, and a wrapping run's answer
/// under infinite width is its whole unwrapped line. So every table grew as wide as its longest cell and the
/// reader scrolled sideways to read a sentence — reported from use, most often on Explain, whose answers are
/// prose-heavy and produce exactly the long cells that made the table widest.</para>
///
/// <para><b>Cells are separate controls, so a drag cannot select across the whole answer.</b> That is the
/// cost of rendering tables at all, and it is why each turn carries an explicit Copy control: copying is a
/// first-class operation over the parsed blocks rather than a side effect of being able to drag across
/// them. Each cell is still individually selectable for lifting one reading out.</para>
/// </summary>
public static class AnswerTableView
{
    public static readonly AttachedProperty<AnswerTable?> TableProperty =
        AvaloniaProperty.RegisterAttached<Grid, AnswerTable?>("Table", typeof(AnswerTableView));

    public static void SetTable(Grid element, AnswerTable? value) => element.SetValue(TableProperty, value);

    public static AnswerTable? GetTable(Grid element) => element.GetValue(TableProperty);

    static AnswerTableView()
    {
        TableProperty.Changed.AddClassHandler<Grid>((grid, args) => Apply(grid, args.NewValue as AnswerTable));
    }

    /// <summary>
    /// How to divide the width between columns, from how much text each holds.
    ///
    /// <para>Equal shares would be simpler and worse: a word-analysis table has a one-word column beside a
    /// column of glosses, and giving them the same width wraps the glosses to a ribbon while the short column
    /// sits mostly empty. Weighting by the longest cell keeps the split roughly proportional to the reading.</para>
    ///
    /// <para><b>Clamped at both ends.</b> A floor of one keeps an empty column from collapsing to nothing;
    /// a ceiling stops one long sentence from taking the whole table and squeezing every other column into a
    /// character-wide sliver — past a certain length a cell is going to wrap regardless, so more width buys
    /// the reader nothing and costs its neighbours everything.</para>
    /// </summary>
    internal static IReadOnlyList<double> ColumnWeights(
        IReadOnlyList<IReadOnlyList<AnswerCell>> rows, int columns)
    {
        const double floor = 1;
        const double ceiling = 24;

        var weights = new double[columns];

        for (var c = 0; c < columns; c++)
        {
            double longest = 0;
            foreach (var row in rows)
            {
                if (c >= row.Count) continue;

                double length = 0;
                foreach (var span in row[c].Spans) length += span.Text.Length;
                if (length > longest) longest = length;
            }

            weights[c] = longest < floor ? floor : longest > ceiling ? ceiling : longest;
        }

        return weights;
    }

    private static void Apply(Grid grid, AnswerTable? table)
    {
        grid.Children.Clear();
        grid.RowDefinitions.Clear();
        grid.ColumnDefinitions.Clear();
        if (table is null) return;

        var rows = new List<IReadOnlyList<AnswerCell>>();
        if (table.Header.Count > 0) rows.Add(table.Header);
        rows.AddRange(table.Rows);
        if (rows.Count == 0) return;

        var columns = 0;
        foreach (var row in rows) columns = row.Count > columns ? row.Count : columns;

        foreach (var weight in ColumnWeights(rows, columns))
            grid.ColumnDefinitions.Add(new ColumnDefinition(new GridLength(weight, GridUnitType.Star)));
        for (var r = 0; r < rows.Count; r++)
            grid.RowDefinitions.Add(new RowDefinition(GridLength.Auto));

        var headerRows = table.Header.Count > 0 ? 1 : 0;

        for (var r = 0; r < rows.Count; r++)
        {
            for (var c = 0; c < columns; c++)
            {
                var cell = c < rows[r].Count ? rows[r][c] : new AnswerCell(new List<AnswerSpan>(), AnswerAlign.Left);

                var text = new SelectableTextBlock
                {
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(6, 3),
                    FontWeight = r < headerRows ? FontWeight.SemiBold : FontWeight.Normal,
                    HorizontalAlignment = cell.Align switch
                    {
                        AnswerAlign.Center => HorizontalAlignment.Center,
                        AnswerAlign.Right => HorizontalAlignment.Right,
                        _ => HorizontalAlignment.Left,
                    },
                };

                foreach (var span in cell.Spans)
                {
                    var run = new Run(span.Text);
                    if (span.Style.HasFlag(AnswerStyle.Bold)) run.FontWeight = FontWeight.SemiBold;
                    if (span.Style.HasFlag(AnswerStyle.Italic)) run.FontStyle = FontStyle.Italic;
                    if (span.Style.HasFlag(AnswerStyle.Monospace)) run.FontFamily = FontFamily.Parse("monospace");
                    text.Inlines?.Add(run);
                }

                // A rule under the header only. Full gridlines in a narrow panel are more ink than the data.
                var border = new Border
                {
                    Child = text,
                    BorderThickness = r == headerRows - 1 ? new Thickness(0, 0, 0, 1) : default,
                    BorderBrush = Brushes.Gray,
                };

                Grid.SetRow(border, r);
                Grid.SetColumn(border, c);
                grid.Children.Add(border);
            }
        }
    }
}
