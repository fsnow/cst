using System;
using System.Collections.Generic;
using CST.Conversion;

namespace CST.Search
{
    /// <summary>
    /// Reads a bounded, paged "reading window" from a book — the level-2 zoom above a snippet. From a start
    /// position it returns up to <c>maxChars</c> of rendered text (tags transparent; footnotes/paranum
    /// stripped by default), snapped at BOTH ends to sentence boundaries so it never cuts mid-sentence,
    /// romanized to the requested script — plus prev/next cursors (character positions) to page through the
    /// surrounding text, and the citation refs at the start. A wall-of-text paragraph just becomes page 1 of N.
    /// When <paramref name="snapStartToSentence"/> is set (a cursor pointing AT a hit, which lands mid-sentence),
    /// the window START is pulled back to the enclosing sentence's start so the hit is read with its governing
    /// clause, not from the hit itself.
    /// </summary>
    public static class TeiPassageReader
    {
        public static PassageWindow ReadWindow(
            string xml, int startPos, int maxChars, bool includeVariants, Script outputScript, BookMarkers markers,
            bool snapStartToSentence = false)
        {
            startPos = Math.Clamp(startPos, 0, xml.Length);
            if (maxChars < 1) maxChars = 1;

            // A cursor from `occurrences` points at the hit (mid-sentence); pull the start back to the enclosing
            // sentence start so the reader gets the governing clause, not a headless predicate. Bounded by the
            // enclosing paragraph so it can't bleed into the previous paragraph, and only applied if the
            // sentence-aligned window still reaches past the cursor — otherwise a hard-capped over-long-sentence
            // paging cursor would snap back onto itself and loop. (Desktop MCP friction report, P1)
            int readStart = startPos;
            if (snapStartToSentence)
            {
                int floor = EnclosingParagraphStart(startPos, markers);
                int candidate = SnapBackToSentenceStart(xml, startPos, floor);
                if (candidate < startPos
                    && WalkForward(xml, candidate, maxChars, includeVariants, xml.Length) > startPos)
                    readStart = candidate;
            }

            int end = WalkForward(xml, readStart, maxChars, includeVariants, xml.Length);
            string text = TeiText.Collapse(
                TeiText.Convert(TeiText.Clean(xml, readStart, end, includeVariants), outputScript)).Trim();

            int? next = end < xml.Length ? end : (int?)null;
            int prevStart = WalkBackward(xml, readStart, maxChars, 0);
            int? prev = prevStart < readStart ? prevStart : (int?)null;

            var (num, code, pages) = markers.RefsAt(readStart);
            return new PassageWindow(text, prev, next, num, code, pages);
        }

        // The nearest sentence start at or after <paramref name="minStart"/> and at/before <paramref name="startPos"/>
        // — i.e. just past the closest preceding sentence boundary, without crossing minStart. Tags are skipped.
        private static int SnapBackToSentenceStart(string xml, int startPos, int minStart)
        {
            if (minStart < 0) minStart = 0;
            int i = startPos - 1;
            while (i >= minStart)
            {
                char c = xml[i];
                if (c == '>')
                {
                    int lt = xml.LastIndexOf('<', i);
                    i = lt >= minStart ? lt - 1 : minStart - 1;
                    continue;
                }
                if (TeiText.IsBoundary(c)) return i + 1;   // begin just past the sentence-ending danda
                i--;
            }
            return minStart;
        }

        // Start position of the paragraph enclosing <paramref name="startPos"/> (the backward-snap floor), or 0.
        private static int EnclosingParagraphStart(int startPos, BookMarkers markers)
        {
            var (num, code, _) = markers.RefsAt(startPos);
            if (num is int n)
            {
                int p = markers.PositionOfParagraph(n, code);
                if (p >= 0 && p <= startPos) return p;
            }
            return 0;
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
