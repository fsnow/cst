using System;
using System.Collections.Generic;
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

    /// <summary>Fixed-pitch: inline code, and the fallback for table-shaped lines.</summary>
    Monospace = 8,
}

/// <summary>One run of answer text and how to draw it.</summary>
public sealed record AnswerSpan(string Text, AnswerStyle Style);

/// <summary>
/// The light Markdown the models actually emit, turned into styled spans. (#586)
///
/// <para><b>Why this exists.</b> The answer was rendered as plain text, so a model writing
/// <c>**appamāda**</c> — which they all do — put literal asterisks on screen. We are not innocent bystanders
/// here either: the prompts are themselves Markdown, headings and bold and bullet lists, and
/// <see cref="PromptBuilder"/> hands the model a word-analysis TABLE. A model shown a table answers in
/// tables.</para>
///
/// <para><b>Why hand-rolled rather than a Markdown library.</b> Two reasons, and the second is the deciding
/// one. A library renders into a tree of separate controls, and a reader cannot drag-select across separate
/// controls — copying the translation out is the thing this panel exists for. And v1.1 has to render
/// <see cref="PaliQuoteMarkers"/> spans in the reader's own script, which means styled runs inside one
/// selectable control anyway; no Markdown library can host that. This is the same streaming transform
/// <see cref="PaliQuoteFilter"/> already performs, extended to carry style.</para>
///
/// <para><b>The ambition is capped on purpose:</b> bold, italic, inline code, headings, bullet lists, and a
/// fixed-pitch fallback for table-shaped lines. Everything else is left as written. Blockquotes, links and
/// nested emphasis are deliberately absent — the moment they are wanted, a library is the better answer.
/// Anything unparsed renders as itself, which is exactly what a reader would have seen before.</para>
/// </summary>
public static class AnswerMarkup
{
    /// <summary>
    /// Parse an answer into styled spans. Line breaks are carried as <c>\n</c> inside span text, so the whole
    /// answer can be one selectable control and a copy comes out as clean prose.
    /// </summary>
    public static IReadOnlyList<AnswerSpan> Parse(string? text)
    {
        var spans = new List<AnswerSpan>();
        if (string.IsNullOrEmpty(text)) return spans;

        var lines = text.Replace("\r\n", "\n").Split('\n');
        for (var i = 0; i < lines.Length; i++)
        {
            var line = lines[i];
            var newline = i < lines.Length - 1 ? "\n" : string.Empty;

            var trimmed = line.TrimStart();
            var indent = line[..(line.Length - trimmed.Length)];

            // Table-shaped: leave the pipes alone and set them in fixed pitch so the columns at least line
            // up. Parsing tables into a grid would break the single-control selection this panel needs.
            if (IsTableRow(trimmed))
            {
                Add(spans, line + newline, AnswerStyle.Monospace);
                continue;
            }

            if (HeadingBody(trimmed) is { } heading)
            {
                AddInline(spans, heading, AnswerStyle.Heading | AnswerStyle.Bold);
                if (newline.Length > 0) Add(spans, newline, AnswerStyle.None);
                continue;
            }

            if (BulletBody(trimmed) is { } bullet)
            {
                // A literal bullet glyph rather than a list control, so a copied answer reads as a list
                // instead of as run-together sentences.
                Add(spans, indent + "• ", AnswerStyle.None);
                AddInline(spans, bullet, AnswerStyle.None);
                if (newline.Length > 0) Add(spans, newline, AnswerStyle.None);
                continue;
            }

            AddInline(spans, line, AnswerStyle.None);
            if (newline.Length > 0) Add(spans, newline, AnswerStyle.None);
        }

        return Merge(spans);
    }

    /// <summary>The plain text of a parsed answer — what a copy should produce. Also the test seam that
    /// proves parsing never loses or invents a character of the reader's text.</summary>
    public static string PlainText(IReadOnlyList<AnswerSpan> spans)
    {
        var text = new StringBuilder();
        foreach (var span in spans) text.Append(span.Text);
        return text.ToString();
    }

    private static bool IsTableRow(string trimmed) =>
        trimmed.StartsWith('|') && trimmed.TrimEnd().EndsWith('|') && trimmed.Length > 1;

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
