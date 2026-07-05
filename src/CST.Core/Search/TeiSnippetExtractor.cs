using System;
using System.Collections.Generic;

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
        public static SnippetResult Extract(string xml, int hitStart, int hitLength, BookMarkers markers, SnippetOptions opts)
        {
            hitStart = Math.Clamp(hitStart, 0, xml.Length);
            int hitEnd = Math.Clamp(hitStart + hitLength, hitStart, xml.Length);

            var (openIdx, pStart, pEnd, rend) = FindEnclosingP(xml, hitStart);
            if (pStart < 0) { openIdx = -1; pStart = 0; pEnd = xml.Length; rend = ""; }

            int winStart, winEnd;
            bool ellipsisStart = false, ellipsisEnd = false;

            if (rend.StartsWith("gatha", StringComparison.Ordinal))
                (winStart, winEnd) = VerseWindow(xml, openIdx, pStart, pEnd);
            else
                (winStart, winEnd, ellipsisStart, ellipsisEnd) =
                    ProseWindow(xml, pStart, pEnd, hitStart, hitEnd, opts);

            string before = TeiText.Collapse(TeiText.Convert(TeiText.Clean(xml, winStart, hitStart, opts.IncludeVariantReadings), opts.OutputScript));
            string hit = TeiText.Convert(TeiText.Clean(xml, hitStart, hitEnd, opts.IncludeVariantReadings), opts.OutputScript).Trim();
            string after = TeiText.Collapse(TeiText.Convert(TeiText.Clean(xml, hitEnd, winEnd, opts.IncludeVariantReadings), opts.OutputScript));

            before = before.TrimStart();
            after = after.TrimEnd();
            string prefix = ellipsisStart ? "... " : "";
            string suffix = ellipsisEnd ? " ..." : "";

            string snippet = prefix + before + hit + after + suffix;
            int markStart = prefix.Length + before.Length;

            var (num, code, pages) = markers.RefsAt(hitStart);
            return new SnippetResult(snippet, markStart, hit.Length, num, code, pages, opts.IncludeVariantReadings);
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
