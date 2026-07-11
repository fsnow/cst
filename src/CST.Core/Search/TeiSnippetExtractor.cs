using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace CST.Search
{
    /// <summary>
    /// Extracts a "search result in context" snippet around a hit, given the decoded book XML and the hit's
    /// character offsets (the same coordinate the search offsets use). Prose is bounded to the sentence(s)
    /// around the hit (danda / double-danda), measured in rendered text length so XML tags don't count;
    /// verse (<c>rend="gatha..."</c>) includes the hit's line plus one line each side. Footnotes
    /// (<c>&lt;note&gt;</c>) and the paragraph-number marker are stripped by default; text is romanized to the
    /// requested script and the matched term's range within the snippet is recomputed after conversion.
    /// Low-level rendering/cleaning lives in <see cref="TeiText"/>.
    /// </summary>
    public static class TeiSnippetExtractor
    {
        /// <summary>Single-term convenience overload — the degenerate one-mark case.</summary>
        public static SnippetResult Extract(string xml, int hitStart, int hitLength, BookMarkers markers, SnippetOptions opts)
            => Extract(xml, new[] { new SnippetMark(hitStart, hitStart + hitLength, true) }, markers, opts);

        /// <summary>
        /// Extract a snippet marking one or more spans (a single term, or the several co-occurring words of a
        /// proximity/phrase hit). The window spans all marks; each mark's rendered range is reported in
        /// snippet-local coordinates via <see cref="SnippetResult.Highlights"/>, exactly one flagged as the anchor.
        /// </summary>
        public static SnippetResult Extract(string xml, IReadOnlyList<SnippetMark> marks, BookMarkers markers, SnippetOptions opts)
        {
            // Clamp + sort marks; drop any that clamp to empty. Fall back to a zero-width mark at 0 if none remain.
            var ms = marks
                .Select(m =>
                {
                    int s = Math.Clamp(m.Start, 0, xml.Length);
                    int e = Math.Clamp(m.End, s, xml.Length);
                    return new SnippetMark(s, e, m.IsAnchor);
                })
                .OrderBy(m => m.Start).ThenBy(m => m.End)
                .ToList();
            if (ms.Count == 0) ms.Add(new SnippetMark(0, 0, true));

            int spanStart = ms[0].Start;
            int spanEnd = ms[ms.Count - 1].End;
            var anchor = ms.FirstOrDefault(m => m.IsAnchor) ?? ms[0];

            var (openIdx, pStart, pEnd, rend) = FindEnclosingP(xml, spanStart);
            if (pStart < 0) { openIdx = -1; pStart = 0; pEnd = xml.Length; rend = ""; }

            int winStart, winEnd;
            bool ellipsisStart = false, ellipsisEnd = false;

            if (rend.StartsWith("gatha", StringComparison.Ordinal))
                (winStart, winEnd) = VerseWindow(xml, openIdx, pStart, pEnd);
            else
                (winStart, winEnd, ellipsisStart, ellipsisEnd) =
                    ProseWindow(xml, pStart, pEnd, spanStart, spanEnd, opts);

            // A window bound can land INSIDE a tag (e.g. SnapToSpace stopping at a space within `<hi rend="dot">`),
            // which would leak the tag's tail (`rend="dot">`) as text since Clean can't see the `<` before it.
            // Nudge the start past the tag and the end before it. Never cross the marks. (Code+API friction D-2)
            winStart = Math.Min(NudgePastTag(xml, winStart), ms[0].Start);
            winEnd = Math.Max(NudgeBeforeTag(xml, winEnd), ms[ms.Count - 1].End);

            // Render as segments: prefix . before . [mark . gap]* . mark . after . suffix, tracking each mark's
            // snippet-local start as the cumulative rendered length before it. Same length rule as the single-mark
            // markStart, accumulated over the marks.
            var sb = new StringBuilder();
            var highlights = new List<SnippetHighlight>(ms.Count);

            sb.Append(ellipsisStart ? "... " : "");
            sb.Append(TeiText.Collapse(TeiText.Convert(
                TeiText.Clean(xml, winStart, ms[0].Start, opts.IncludeFootnotes), opts.OutputScript)).TrimStart());

            for (int i = 0; i < ms.Count; i++)
            {
                string markText = TeiText.Convert(
                    TeiText.Clean(xml, ms[i].Start, ms[i].End, opts.IncludeFootnotes), opts.OutputScript).Trim();
                highlights.Add(new SnippetHighlight(sb.Length, markText.Length, ms[i].IsAnchor));
                sb.Append(markText);

                if (i < ms.Count - 1)
                {
                    string gap = TeiText.Collapse(TeiText.Convert(
                        TeiText.Clean(xml, ms[i].End, ms[i + 1].Start, opts.IncludeFootnotes), opts.OutputScript));
                    sb.Append(gap.Length == 0 ? " " : gap); // never fuse two distinct marked words
                }
            }

            sb.Append(TeiText.Collapse(TeiText.Convert(
                TeiText.Clean(xml, ms[ms.Count - 1].End, winEnd, opts.IncludeFootnotes), opts.OutputScript)).TrimEnd());
            sb.Append(ellipsisEnd ? " ..." : "");

            var (num, code, pages) = markers.RefsAt(anchor.Start);
            var anchorHl = highlights.FirstOrDefault(h => h.IsAnchor) ?? highlights[0];
            return new SnippetResult(
                sb.ToString(), anchorHl.Start, anchorHl.Length, num, code, pages, opts.IncludeFootnotes, highlights);
        }

        /// <summary>
        /// Group hits that fall in the SAME enclosing sentence into one mark-set (so co-located hits render as a
        /// single snippet with multiple highlights, not N byte-identical snippets). Each group keeps exactly one
        /// anchor (the first hit's); the rest become plain highlights. Ordered by anchor position. The caller
        /// pages over the returned groups and calls <see cref="Extract(string,IReadOnlyList{SnippetMark},BookMarkers,SnippetOptions)"/>
        /// on each. (Desktop MCP friction report — the snippet de-dup / token win.)
        /// </summary>
        public static List<IReadOnlyList<SnippetMark>> GroupCoLocated(
            string xml, IReadOnlyList<IReadOnlyList<SnippetMark>> hits)
        {
            var ordered = hits
                .Where(h => h.Count > 0)
                .Select(h => (marks: h, anchor: (h.FirstOrDefault(m => m.IsAnchor) ?? h[0]).Start))
                .OrderBy(x => x.anchor)
                .ToList();

            var groups = new List<IReadOnlyList<SnippetMark>>();
            List<SnippetMark>? current = null;
            int currentSentence = int.MinValue;
            foreach (var (marks, anchor) in ordered)
            {
                int sentence = EnclosingSentenceStart(xml, anchor);
                if (current == null || sentence != currentSentence)
                {
                    current = new List<SnippetMark>();
                    groups.Add(current);
                    currentSentence = sentence;
                }
                current.AddRange(marks);
            }
            // Collapse each group to a single anchor (the earliest), demoting the rest to plain highlights, so the
            // "exactly one anchor" invariant holds for the merged snippet.
            return groups.Select(WithSingleAnchor).ToList();
        }

        private static IReadOnlyList<SnippetMark> WithSingleAnchor(IReadOnlyList<SnippetMark> marks)
        {
            bool anchored = false;
            var result = new List<SnippetMark>(marks.Count);
            foreach (var m in marks)
            {
                if (m.IsAnchor && !anchored) { anchored = true; result.Add(m); }
                else if (m.IsAnchor) result.Add(new SnippetMark(m.Start, m.End, false));
                else result.Add(m);
            }
            return result;
        }

        private static int EnclosingSentenceStart(string xml, int pos)
        {
            var (_, pStart, pEnd, _) = FindEnclosingP(xml, pos);
            if (pStart < 0) { pStart = 0; pEnd = xml.Length; }
            var notes = TeiText.NoteRegions(xml, pStart, pEnd);
            return SentenceStart(xml, pStart, pos, notes);
        }

        // True if pos sits inside a start/end tag (an unclosed '<' precedes it without a matching '>').
        private static bool InsideTag(string xml, int pos)
        {
            if (pos <= 0 || pos > xml.Length) return false;
            return xml.LastIndexOf('<', pos - 1) > xml.LastIndexOf('>', pos - 1);
        }

        private static int NudgePastTag(string xml, int pos)
        {
            if (InsideTag(xml, pos)) { int gt = xml.IndexOf('>', pos); if (gt >= 0) return gt + 1; }
            return pos;
        }

        private static int NudgeBeforeTag(string xml, int pos)
        {
            if (InsideTag(xml, pos)) { int lt = xml.LastIndexOf('<', Math.Max(0, pos - 1)); if (lt >= 0) return lt; }
            return pos;
        }

        /// <summary>
        /// Group hits that fall in the SAME enclosing sentence into one mark-set (so co-located hits render as a
        /// single snippet with multiple highlights, not N byte-identical snippets). Each group keeps exactly one
        /// anchor (the first hit's); the rest become plain highlights. Ordered by anchor position. The caller
        /// pages over the returned groups and calls <see cref="Extract(string,IReadOnlyList{SnippetMark},BookMarkers,SnippetOptions)"/>
        /// on each. (Desktop MCP friction report — the snippet de-dup / token win.)
        /// </summary>
        public static List<IReadOnlyList<SnippetMark>> GroupCoLocated(
            string xml, IReadOnlyList<IReadOnlyList<SnippetMark>> hits)
        {
            var ordered = hits
                .Where(h => h.Count > 0)
                .Select(h => (marks: h, anchor: (h.FirstOrDefault(m => m.IsAnchor) ?? h[0]).Start))
                .OrderBy(x => x.anchor)
                .ToList();

            var groups = new List<IReadOnlyList<SnippetMark>>();
            List<SnippetMark>? current = null;
            int currentSentence = int.MinValue;
            foreach (var (marks, anchor) in ordered)
            {
                int sentence = EnclosingSentenceStart(xml, anchor);
                if (current == null || sentence != currentSentence)
                {
                    current = new List<SnippetMark>();
                    groups.Add(current);
                    currentSentence = sentence;
                }
                current.AddRange(marks);
            }
            // Collapse each group to a single anchor (the earliest), demoting the rest to plain highlights, so the
            // "exactly one anchor" invariant holds for the merged snippet.
            return groups.Select(WithSingleAnchor).ToList();
        }

        private static IReadOnlyList<SnippetMark> WithSingleAnchor(IReadOnlyList<SnippetMark> marks)
        {
            bool anchored = false;
            var result = new List<SnippetMark>(marks.Count);
            foreach (var m in marks)
            {
                if (m.IsAnchor && !anchored) { anchored = true; result.Add(m); }
                else if (m.IsAnchor) result.Add(new SnippetMark(m.Start, m.End, false));
                else result.Add(m);
            }
            return result;
        }

        private static int EnclosingSentenceStart(string xml, int pos)
        {
            var (_, pStart, pEnd, _) = FindEnclosingP(xml, pos);
            if (pStart < 0) { pStart = 0; pEnd = xml.Length; }
            var notes = TeiText.NoteRegions(xml, pStart, pEnd);
            return SentenceStart(xml, pStart, pos, notes);
        }

        // True if pos sits inside a start/end tag (an unclosed '<' precedes it without a matching '>').
        private static bool InsideTag(string xml, int pos)
        {
            if (pos <= 0 || pos > xml.Length) return false;
            return xml.LastIndexOf('<', pos - 1) > xml.LastIndexOf('>', pos - 1);
        }

        private static int NudgePastTag(string xml, int pos)
        {
            if (InsideTag(xml, pos)) { int gt = xml.IndexOf('>', pos); if (gt >= 0) return gt + 1; }
            return pos;
        }

        private static int NudgeBeforeTag(string xml, int pos)
        {
            if (InsideTag(xml, pos)) { int lt = xml.LastIndexOf('<', Math.Max(0, pos - 1)); if (lt >= 0) return lt; }
            return pos;
        }

        private static (int start, int end, bool ellStart, bool ellEnd) ProseWindow(
            string xml, int pStart, int pEnd, int hitStart, int hitEnd, SnippetOptions opts)
        {
            var notes = TeiText.NoteRegions(xml, pStart, pEnd);

            int start = SentenceStart(xml, pStart, hitStart, notes);
            int end = SentenceEnd(xml, hitEnd, pEnd, notes);

            while (TeiText.VisibleLen(xml, start, end) < opts.MinChars)
            {
                int ns = start > pStart ? SentenceStart(xml, pStart, start - 1, notes) : start;
                int ne = end < pEnd ? SentenceEnd(xml, end, pEnd, notes) : end;
                if (ns == start && ne == end) break;
                start = ns; end = ne;
            }

            bool ellStart = false, ellEnd = false;
            if (TeiText.VisibleLen(xml, start, end) > opts.MaxChars)
            {
                int half = opts.MaxChars / 2;
                int ns = SnapToSpace(xml, Math.Max(pStart, hitStart - half), +1, hitStart);
                int ne = SnapToSpace(xml, Math.Min(pEnd, hitEnd + half), -1, hitEnd);
                if (ns > start) { start = ns; ellStart = true; }
                if (ne < end) { end = ne; ellEnd = true; }
            }
            return (start, end, ellStart, ellEnd);
        }

        private static (int start, int end) VerseWindow(string xml, int openIdx, int pStart, int pEnd)
        {
            int start = pStart, end = pEnd;
            if (openIdx >= 0)
            {
                int prevOpen = FindLastPOpen(xml, openIdx);
                if (prevOpen >= 0) { int gt = xml.IndexOf('>', prevOpen); if (gt >= 0) start = gt + 1; }
            }
            int nextOpen = FindNextPOpen(xml, pEnd);
            if (nextOpen >= 0)
            {
                int nextEnd = xml.IndexOf("</p>", nextOpen, StringComparison.Ordinal);
                if (nextEnd >= 0) end = nextEnd;
            }
            return (start, end);
        }

        private static int SentenceStart(string xml, int lo, int from, List<(int s, int e)> notes)
        {
            for (int i = Math.Min(from, xml.Length) - 1; i >= lo; i--)
                if (TeiText.IsBoundary(xml[i]) && !TeiText.InNote(i, notes)) return i + 1;
            return lo;
        }

        private static int SentenceEnd(string xml, int from, int hi, List<(int s, int e)> notes)
        {
            for (int i = from; i < hi; i++)
                if (TeiText.IsBoundary(xml[i]) && !TeiText.InNote(i, notes)) return i + 1;
            return hi;
        }

        private static (int openIdx, int contentStart, int pEnd, string rend) FindEnclosingP(string xml, int hitStart)
        {
            int lastClose = xml.LastIndexOf("</p>", Math.Min(hitStart, xml.Length), StringComparison.Ordinal);
            int open = FindLastPOpen(xml, hitStart);
            if (open < 0 || open < lastClose) return (-1, -1, -1, "");
            int gt = xml.IndexOf('>', open);
            if (gt < 0) return (-1, -1, -1, "");
            int pEnd = xml.IndexOf("</p>", gt + 1, StringComparison.Ordinal);
            if (pEnd < 0) pEnd = xml.Length;
            return (open, gt + 1, pEnd, TeiText.Attr(xml.Substring(open, gt - open + 1), "rend"));
        }

        private static int FindLastPOpen(string xml, int before)
        {
            int idx = Math.Min(before, xml.Length);
            while ((idx = xml.LastIndexOf("<p", idx - 1, StringComparison.Ordinal)) >= 0)
            {
                char c = idx + 2 < xml.Length ? xml[idx + 2] : '\0';
                if (c == ' ' || c == '>' || c == '\t' || c == '\n' || c == '\r') return idx;
            }
            return -1;
        }

        private static int FindNextPOpen(string xml, int from)
        {
            int idx = Math.Max(from, 0);
            while ((idx = xml.IndexOf("<p", idx, StringComparison.Ordinal)) >= 0)
            {
                char c = idx + 2 < xml.Length ? xml[idx + 2] : '\0';
                if (c == ' ' || c == '>' || c == '\t' || c == '\n' || c == '\r') return idx;
                idx += 2;
            }
            return -1;
        }

        private static int SnapToSpace(string xml, int pos, int dir, int stopAt)
        {
            for (int i = pos; dir > 0 ? i < stopAt : i > stopAt; i += dir)
                if (xml[i] == ' ' || xml[i] == '\n') return i + (dir > 0 ? 1 : 0);
            return pos;
        }
    }
}
