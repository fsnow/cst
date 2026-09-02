using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CST.Avalonia.Services.Ai;

/// <summary>How a span of answer text should be drawn.</summary>
[Flags]
public enum AnswerStyle
{
    None = 0,
    Bold = 1,
    Italic = 2,

    /// <summary>A heading line. Drawn as emphasis, not as a new font size — this is a side panel.</summary>
    Heading = 4,

    /// <summary>Fixed-pitch: inline code.</summary>
    Monospace = 8,
}

/// <summary>Column alignment, as the table's delimiter row declared it.</summary>
public enum AnswerAlign
{
    Left,
    Center,
    Right,
}

/// <summary>One run of answer text and how to draw it.</summary>
public sealed record AnswerSpan(string Text, AnswerStyle Style);

/// <summary>A piece of a rendered answer: a run of prose, or a table.</summary>
public abstract record AnswerBlock;

/// <summary>Prose — possibly many lines, with headings and bullets already resolved into spans.</summary>
public sealed record AnswerParagraph(IReadOnlyList<AnswerSpan> Spans) : AnswerBlock;

/// <summary>One table cell: its styled content and the column's alignment.</summary>
public sealed record AnswerCell(IReadOnlyList<AnswerSpan> Spans, AnswerAlign Align);

/// <summary>A pipe table. <paramref name="Header"/> may be empty if the table had no header text.</summary>
public sealed record AnswerTable(
    IReadOnlyList<AnswerCell> Header,
    IReadOnlyList<IReadOnlyList<AnswerCell>> Rows) : AnswerBlock;

/// <summary>
/// The light Markdown the models actually emit, turned into blocks the panel can draw. (#586)
///
/// <para><b>Why this exists.</b> The answer was rendered as plain text, so a model writing
/// <c>**appamāda**</c> — which they all do — put literal asterisks on screen. We are not bystanders: the
/// prompts are themselves Markdown, and <see cref="PromptBuilder"/> hands the model a word-analysis table.
/// A model shown a table answers in tables.</para>
///
/// <para><b>Tables are parsed, not merely aligned.</b> The first version set table-shaped lines in fixed
/// pitch and the system prompt asked the model not to emit tables at all — on the reasoning that a real table
/// needs one control per cell and a drag-selection cannot cross separate controls. That traded a capability
/// for a copy path, and it was the wrong trade twice over: instruction-following on "no formatting" is
/// unreliable on exactly the weak models this app must support, so the tables arrived anyway; and copy is
/// better served by an explicit control than by drag-select, which no longer has to carry the whole burden.
/// See <see cref="PlainText"/> — copying is now a first-class operation over the parsed blocks.</para>
///
/// <para><b>Streaming safety.</b> A table is only recognised once its delimiter row has arrived, so a header
/// row alone renders as ordinary text and becomes a table on the next flush rather than flickering between
/// two layouts. Anything unrecognised renders as itself, which is what a reader would have seen before.</para>
/// </summary>
public static class AnswerMarkup
{
    /// <summary>Parse an answer into blocks.</summary>
    public static IReadOnlyList<AnswerBlock> Parse(string? text)
    {
        var blocks = new List<AnswerBlock>();
        if (string.IsNullOrEmpty(text)) return blocks;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        var prose = new List<AnswerSpan>();

        for (var i = 0; i < lines.Length; i++)
        {
            // A table needs its delimiter row before it is a table. Until then the header row is just a line
            // of text — which is what it looks like mid-stream, one flush before the delimiter arrives.
            if (i + 1 < lines.Length && IsPipeRow(lines[i]) && IsDelimiterRow(lines[i + 1]))
            {
                FlushProse(blocks, prose);

                var aligns = Alignments(lines[i + 1]);
                var header = Cells(lines[i], aligns);
                var rows = new List<IReadOnlyList<AnswerCell>>();

                var row = i + 2;
                while (row < lines.Length && IsPipeRow(lines[row]))
                {
                    if (rows.Count < MaxRows) rows.Add(Cells(lines[row], aligns));
                    row++;   // keep consuming, so the overflow is not re-read as prose
                }

                blocks.Add(new AnswerTable(header, rows));
                i = row - 1;
                continue;
            }

            AppendLine(prose, lines[i], last: i == lines.Length - 1);
        }

        FlushProse(blocks, prose);
        return blocks;
    }

    /// <summary>
    /// The plain text of a parsed answer — what Copy hands over, and the test seam that proves parsing never
    /// loses or invents a character of the reader's text. Tables come back as aligned pipe rows, which is
    /// what pastes usefully into a document or a message.
    /// </summary>
    public static string PlainText(IReadOnlyList<AnswerBlock> blocks)
    {
        var text = new StringBuilder();

        foreach (var block in blocks)
        {
            switch (block)
            {
                case AnswerParagraph paragraph:
                    foreach (var span in paragraph.Spans) text.Append(span.Text);
                    break;

                case AnswerTable table:
                    AppendTable(text, table);
                    break;
            }
        }

        return text.ToString();
    }

    private static void AppendTable(StringBuilder text, AnswerTable table)
    {
        var rows = new List<IReadOnlyList<AnswerCell>>();
        if (table.Header.Count > 0) rows.Add(table.Header);
        rows.AddRange(table.Rows);
        if (rows.Count == 0) return;

        var columns = rows.Max(r => r.Count);
        var widths = new int[columns];
        for (var c = 0; c < columns; c++)
            widths[c] = rows.Max(r => c < r.Count ? Text(r[c]).Length : 0);

        for (var r = 0; r < rows.Count; r++)
        {
            text.Append('|');
            for (var c = 0; c < columns; c++)
            {
                var cell = c < rows[r].Count ? Text(rows[r][c]) : string.Empty;
                text.Append(' ').Append(cell.PadRight(widths[c])).Append(" |");
            }
            text.Append('\n');

            // The separator goes back in under the header, so a pasted table is still a Markdown table.
            if (r == 0 && table.Header.Count > 0)
            {
                text.Append('|');
                for (var c = 0; c < columns; c++) text.Append(' ').Append(new string('-', widths[c])).Append(" |");
                text.Append('\n');
            }
        }
    }

    private static string Text(AnswerCell cell) =>
        string.Concat(cell.Spans.Select(s => s.Text));

    // ---- Prose ---------------------------------------------------------------------------------------

    private static void FlushProse(List<AnswerBlock> blocks, List<AnswerSpan> prose)
    {
        if (prose.Count == 0) return;

        // A paragraph ending immediately before a table would otherwise carry a dangling newline into a block
        // that is about to be followed by one anyway.
        var merged = Merge(prose);
        blocks.Add(new AnswerParagraph(merged));
        prose.Clear();
    }

    private static void AppendLine(List<AnswerSpan> spans, string line, bool last)
    {
        var newline = last ? string.Empty : "\n";

        var trimmed = line.TrimStart();
        var indent = line[..(line.Length - trimmed.Length)];

        if (HeadingBody(trimmed) is { } heading)
        {
            AddInline(spans, heading, AnswerStyle.Heading | AnswerStyle.Bold);
            Add(spans, newline, AnswerStyle.None);
            return;
        }

        if (BulletBody(trimmed) is { } bullet)
        {
            // A literal bullet glyph rather than a list control, so a copied answer reads as a list instead
            // of as run-together sentences.
            Add(spans, indent + "• ", AnswerStyle.None);
            AddInline(spans, bullet, AnswerStyle.None);
            Add(spans, newline, AnswerStyle.None);
            return;
        }

        AddInline(spans, line, AnswerStyle.None);
        Add(spans, newline, AnswerStyle.None);
    }

    /// <summary>The text of an ATX heading line, or null.</summary>
    private static string? HeadingBody(string trimmed)
    {
        var hashes = 0;
        while (hashes < trimmed.Length && trimmed[hashes] == '#') hashes++;
        if (hashes is < 1 or > 6) return null;
        if (hashes >= trimmed.Length || trimmed[hashes] != ' ') return null;
        return trimmed[(hashes + 1)..];
    }

    /// <summary>The text of a bullet line, or null. Ordered lists are left as written — their numbers are
    /// already legible, and renumbering is a promise this does not need to make.</summary>
    private static string? BulletBody(string trimmed)
    {
        if (trimmed.Length < 2) return null;
        if (trimmed[0] is not ('-' or '*' or '+')) return null;
        if (trimmed[1] != ' ') return null;
        return trimmed[2..];
    }

    // ---- Tables --------------------------------------------------------------------------------------

    private static bool IsPipeRow(string line)
    {
        var trimmed = line.Trim();
        return trimmed.Length > 1 && trimmed.StartsWith('|') && trimmed.EndsWith('|');
    }

    /// <summary>A row of nothing but dashes, colons, pipes and spaces — the thing that makes a table a table.</summary>
    private static bool IsDelimiterRow(string line)
    {
        if (!IsPipeRow(line)) return false;

        var seenDash = false;
        foreach (var c in line.Trim())
        {
            if (c == '-') seenDash = true;
            else if (c is not ('|' or ':' or ' ')) return false;
        }
        return seenDash;
    }

    private static IReadOnlyList<AnswerAlign> Alignments(string delimiter) =>
        SplitRow(delimiter)
            .Select(cell => cell.Trim())
            .Select(cell => (cell.StartsWith(':'), cell.EndsWith(':')) switch
            {
                (true, true) => AnswerAlign.Center,
                (false, true) => AnswerAlign.Right,
                _ => AnswerAlign.Left,
            })
            .ToList();

    private static IReadOnlyList<AnswerCell> Cells(string line, IReadOnlyList<AnswerAlign> aligns) =>
        SplitRow(line)
            .Select((cell, index) =>
            {
                var spans = new List<AnswerSpan>();
                AddInline(spans, cell.Trim(), AnswerStyle.None);
                return new AnswerCell(
                    Merge(spans),
                    index < aligns.Count ? aligns[index] : AnswerAlign.Left);
            })
            .ToList();

    /// <summary>
    /// The widest and tallest table that will be rendered as a table. (R6-3)
    ///
    /// <para>Both are far beyond any table a reader would want — the assistant's real tables are a handful
    /// of columns of word-by-word glosses — and low enough that a malfunctioning model cannot cost the UI
    /// thread. <c>AnswerTableView.Apply</c> builds rows×columns <c>Border</c> + <c>SelectableTextBlock</c>
    /// controls and rebuilds the WHOLE grid on every 100 ms streaming flush, so a few-hundred-column row
    /// freezes the panel and the app rather than degrading.</para>
    ///
    /// <para>Overflow is not dropped: a row too wide keeps its first <see cref="MaxColumns"/> cells, and a
    /// table too tall keeps its first <see cref="MaxRows"/>, so the answer stays readable rather than
    /// vanishing. Model output is untrusted input like any other.</para>
    /// </summary>
    internal const int MaxColumns = 64;
    internal const int MaxRows = 500;

    /// <summary>Split a pipe row into cells, dropping the leading and trailing pipes.</summary>
    private static IReadOnlyList<string> SplitRow(string line)
    {
        var trimmed = line.Trim();
        var inner = trimmed[1..^1];
        var cells = inner.Split('|');
        return cells.Length <= MaxColumns ? cells : cells[..MaxColumns];
    }

    // ---- Inline emphasis -----------------------------------------------------------------------------

    /// <summary>
    /// Emphasis within one line: <c>**bold**</c>, <c>*italic*</c>, <c>`code`</c>. Scanned longest-marker
    /// first so a bold pair is never read as two italics.
    /// </summary>
    private static void AddInline(List<AnswerSpan> spans, string line, AnswerStyle baseStyle)
    {
        var position = 0;
        while (position < line.Length)
        {
            var next = NextMarker(line, position);
            if (next is not var (start, marker, style))
            {
                Add(spans, line[position..], baseStyle);
                return;
            }

            var close = line.IndexOf(marker, start + marker.Length, StringComparison.Ordinal);
            if (close < 0)
            {
                // An opener with no partner is not emphasis, it is an asterisk. Leave it as the reader would
                // have seen it rather than styling the rest of the line — a streamed answer is unclosed at
                // every flush until it isn't, and the alternative flickers.
                Add(spans, line[position..(start + marker.Length)], baseStyle);
                position = start + marker.Length;
                continue;
            }

            if (start > position) Add(spans, line[position..start], baseStyle);

            var inner = line[(start + marker.Length)..close];
            if (inner.Length == 0)
            {
                // "**" with nothing inside is punctuation, not emphasis.
                Add(spans, line[start..(close + marker.Length)], baseStyle);
            }
            else
            {
                Add(spans, inner, baseStyle | style);
            }

            position = close + marker.Length;
        }
    }

    private static (int Start, string Marker, AnswerStyle Style)? NextMarker(string line, int from)
    {
        var best = (Start: -1, Marker: string.Empty, Style: AnswerStyle.None);

        foreach (var (marker, style) in Markers)
        {
            var at = line.IndexOf(marker, from, StringComparison.Ordinal);
            if (at < 0) continue;
            // Earliest wins; on a tie the longer marker wins, so "**" is never read as "*".
            if (best.Start < 0 || at < best.Start || (at == best.Start && marker.Length > best.Marker.Length))
                best = (at, marker, style);
        }

        return best.Start < 0 ? null : best;
    }

    private static readonly (string Marker, AnswerStyle Style)[] Markers =
    {
        ("**", AnswerStyle.Bold),
        ("__", AnswerStyle.Bold),
        ("*", AnswerStyle.Italic),
        ("`", AnswerStyle.Monospace),
    };

    private static void Add(List<AnswerSpan> spans, string text, AnswerStyle style)
    {
        if (text.Length > 0) spans.Add(new AnswerSpan(text, style));
    }

    /// <summary>Coalesce adjacent same-style spans, so a long answer is a handful of runs rather than one per
    /// line. Fewer runs is materially cheaper to lay out, and the panel re-renders on every flush.</summary>
    private static List<AnswerSpan> Merge(List<AnswerSpan> spans)
    {
        var merged = new List<AnswerSpan>(spans.Count);
        foreach (var span in spans)
        {
            if (merged.Count > 0 && merged[^1].Style == span.Style)
                merged[^1] = merged[^1] with { Text = merged[^1].Text + span.Text };
            else
                merged.Add(span);
        }
        return merged;
    }
}
