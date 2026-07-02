using System;
using System.Collections.Generic;
using System.Net;
using System.Text.RegularExpressions;
using CST.Avalonia.Services;

namespace CST.Avalonia.ViewModels;

/// <summary>
/// Turns a dictionary definition fragment into renderable <see cref="MeaningSegment"/>s. The data is
/// plain text plus two markers: <c>&lt;see&gt;word&lt;/see&gt;</c> cross-references, and the
/// <see cref="DictionaryService.MeaningSeparator"/> that joins the definitions of a merged
/// (duplicate) headword. Pure and UI-free so the parsing (including the separator handling — DICT-1) is
/// directly unit-testable; the view renders the segments as native inlines.
/// </summary>
public static class MeaningParser
{
    private static readonly Regex SeeTag = new(@"<see>(.*?)</see>", RegexOptions.Compiled | RegexOptions.Singleline);

    /// <summary>
    /// Parse <paramref name="html"/> into segments. Each merged definition is emitted in order, with a
    /// <see cref="MeaningSegment.Separator"/> between them; <c>&lt;see&gt;</c> targets are mapped to
    /// display text via <paramref name="linkDisplay"/>.
    /// </summary>
    public static IReadOnlyList<MeaningSegment> Parse(string? html, Func<string, string> linkDisplay)
    {
        var segments = new List<MeaningSegment>();
        if (string.IsNullOrEmpty(html))
            return segments;

        var definitions = html.Split(DictionaryService.MeaningSeparator, StringSplitOptions.None);
        for (int d = 0; d < definitions.Length; d++)
        {
            if (d > 0)
                segments.Add(MeaningSegment.Separator);
            // Trim each definition's outer whitespace so a merged break (or a source's leading space,
            // e.g. the "a" entry) doesn't render as a stray indent.
            ParseDefinition(definitions[d].Trim(), linkDisplay, segments);
        }
        return segments;
    }

    private static void ParseDefinition(string html, Func<string, string> linkDisplay, List<MeaningSegment> segments)
    {
        int pos = 0;
        foreach (Match m in SeeTag.Matches(html))
        {
            if (m.Index > pos)
                segments.Add(new MeaningSegment(WebUtility.HtmlDecode(html.Substring(pos, m.Index - pos)), false, null));

            var target = m.Groups[1].Value.Trim();
            segments.Add(new MeaningSegment(linkDisplay(target), true, target));
            pos = m.Index + m.Length;
        }
        if (pos < html.Length)
            segments.Add(new MeaningSegment(WebUtility.HtmlDecode(html.Substring(pos)), false, null));
    }
}
