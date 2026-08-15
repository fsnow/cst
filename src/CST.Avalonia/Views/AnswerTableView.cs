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
/// wrap and stop lining up at exactly the width where the table was supposed to help. Auto-sized columns
/// wrap per cell instead, which is what a reader needs.</para>
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

        for (var c = 0; c < columns; c++)
            grid.ColumnDefinitions.Add(new ColumnDefinition(GridLength.Auto));
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
