using System;
using System.Collections.Generic;
using CST.Conversion;

namespace CST.Search
{
    /// <summary>
    /// Reads a bounded, paged "reading window" from a book — the level-2 zoom above a snippet. From a start
    /// position it returns up to <c>maxChars</c> of rendered text (tags transparent; footnotes/paranum
    /// stripped by default), extended to the next sentence boundary so it never cuts mid-sentence, romanized
    /// to the requested script — plus prev/next cursors (character positions) to page through the surrounding
    /// text, and the citation refs at the start. A wall-of-text paragraph just becomes page 1 of N.
    /// </summary>
    public static class TeiPassageReader
    {
        public static PassageWindow ReadWindow(
            string xml, int startPos, int maxChars, bool includeVariants, Script outputScript, BookMarkers markers)
        {
            startPos = Math.Clamp(startPos, 0, xml.Length);
            if (maxChars < 1) maxChars = 1;

            int end = WalkForward(xml, startPos, maxChars, includeVariants, xml.Length);
            string text = TeiText.Collapse(
                TeiText.Convert(TeiText.Clean(xml, startPos, end, includeVariants), outputScript)).Trim();

            int? next = end < xml.Length ? end : (int?)null;
            int prevStart = WalkBackward(xml, startPos, maxChars, 0);
            int? prev = prevStart < startPos ? prevStart : (int?)null;

            var (num, code, pages) = markers.RefsAt(startPos);
            return new PassageWindow(text, prev, next, num, code, pages);
        }

        // Raw end position after accumulating ~maxChars rendered chars, then extending to the next sentence
        // boundary (capped) so we never cut mid-sentence. Tags and stripped subtrees cost zero budget.
        private static int WalkForward(string xml, int start, int maxChars, bool includeNotes, int limit)
        {
            int i = start, rendered = 0, hardCap = maxChars + maxChars / 2;
            bool budgetReached = false;
            while (i < limit)
            {
                char c = xml[i];
                if (c == '<')
                {
                    int gt = xml.IndexOf('>', i);
                    if (gt < 0) break;
                    string tag = xml.Substring(i, gt - i + 1);
                    string name = TeiText.TagName(tag);
                    if (name == "note" && !includeNotes && !tag.EndsWith("/>", StringComparison.Ordinal))
                        i = TeiText.SkipSubtree(xml, gt + 1, "note", limit);
                    else if (name == "hi" && TeiText.IsStructuralHi(tag) && !tag.EndsWith("/>", StringComparison.Ordinal))
                        i = TeiText.SkipSubtree(xml, gt + 1, "hi", limit);
                    else i = gt + 1;
                }
                else
                {
                    if (budgetReached && TeiText.IsBoundary(c)) return i + 1;   // stop just past a sentence end
                    rendered++;
                    i++;
                    if (rendered >= maxChars) budgetReached = true;
                    if (rendered >= hardCap) return i;                          // no boundary found: hard cap
                }
            }
            return i;
        }

        // Start position ~maxChars rendered chars before <paramref name="start"/>, snapped forward to a
        // sentence start. Approximate (tags treated as zero-width backward); good enough for a page cursor.
        private static int WalkBackward(string xml, int start, int maxChars, int limit)
        {
            int i = start - 1, rendered = 0;
            while (i >= limit && rendered < maxChars)
            {
                if (xml[i] == '>')
                {
                    int lt = xml.LastIndexOf('<', i);
                    i = lt >= limit ? lt - 1 : limit - 1;
                }
                else { rendered++; i--; }
            }
            for (int j = Math.Max(i, limit); j < start; j++)
                if (TeiText.IsBoundary(xml[j])) return j + 1;                   // begin at a sentence start
            return Math.Max(i + 1, limit);
        }
    }

    /// <summary>A reading window: the text plus page cursors and the citation refs at its start.</summary>
    public sealed record PassageWindow(
        string Text,
        int? PrevCursor,
        int? NextCursor,
        int? ParagraphNumber,
        string? ParagraphBookCode,
        IReadOnlyList<SnippetPageRef> Pages);
}
