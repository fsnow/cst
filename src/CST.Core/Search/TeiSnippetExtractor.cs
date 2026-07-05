using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using CST.Conversion;

namespace CST.Search
{
    /// <summary>
    /// Extracts a "search result in context" snippet around a hit, given the decoded book XML and the hit's
    /// character offsets (the same coordinate the search offsets use). Prose is bounded to the sentence(s)
    /// around the hit (danda / double-danda), measured in rendered text length so XML tags don't count;
    /// verse (<c>rend="gatha..."</c>) includes the hit's line plus one line each side. Footnotes
    /// (<c>&lt;note&gt;</c>) and the paragraph-number marker are stripped by default; text is romanized to the
    /// requested script and the matched term's range within the snippet is recomputed after conversion.
    /// </summary>
    public static class TeiSnippetExtractor
    {
        private const char Danda = '\u0964';        // Devanagari danda - sentence end
        private const char DoubleDanda = '\u0965';  // Devanagari double danda - verse/gatha end
        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

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

            string before = Collapse(Convert(Clean(xml, winStart, hitStart, opts.IncludeVariantReadings), opts.OutputScript));
            string hit = Convert(Clean(xml, hitStart, hitEnd, opts.IncludeVariantReadings), opts.OutputScript).Trim();
            string after = Collapse(Convert(Clean(xml, hitEnd, winEnd, opts.IncludeVariantReadings), opts.OutputScript));

            // Keep exactly one space between context and the hit; drop stray edge whitespace.
            before = before.TrimStart();
            after = after.TrimEnd();
            string prefix = ellipsisStart ? "... " : "";
            string suffix = ellipsisEnd ? " ..." : "";

            string snippet = prefix + before + hit + after + suffix;
            int markStart = prefix.Length + before.Length;

            var (num, code, pages) = markers.RefsAt(hitStart);
            return new SnippetResult(snippet, markStart, hit.Length, num, code, pages, opts.IncludeVariantReadings);
        }

        // --- window selection -----------------------------------------------

        private static (int start, int end, bool ellStart, bool ellEnd) ProseWindow(
            string xml, int pStart, int pEnd, int hitStart, int hitEnd, SnippetOptions opts)
        {
            var notes = NoteRegions(xml, pStart, pEnd);

            int start = SentenceStart(xml, pStart, hitStart, notes);
            int end = SentenceEnd(xml, hitEnd, pEnd, notes);

            // Floor: widen to neighboring sentences until enough rendered text.
            while (VisibleLen(xml, start, end) < opts.MinChars)
            {
                int ns = start > pStart ? SentenceStart(xml, pStart, start - 1, notes) : start;
                int ne = end < pEnd ? SentenceEnd(xml, end, pEnd, notes) : end;
                if (ns == start && ne == end) break;
                start = ns; end = ne;
            }

            // Ceiling: a single long sentence gets trimmed toward the hit, with ellipses.
            bool ellStart = false, ellEnd = false;
            if (VisibleLen(xml, start, end) > opts.MaxChars)
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

        // --- sentence boundaries (raw, note-aware) --------------------------

        private static int SentenceStart(string xml, int lo, int from, List<(int s, int e)> notes)
        {
            for (int i = Math.Min(from, xml.Length) - 1; i >= lo; i--)
                if (IsBoundary(xml[i]) && !InNote(i, notes)) return i + 1;
            return lo;
        }

        private static int SentenceEnd(string xml, int from, int hi, List<(int s, int e)> notes)
        {
            for (int i = from; i < hi; i++)
                if (IsBoundary(xml[i]) && !InNote(i, notes)) return i + 1;
            return hi;
        }

        private static bool IsBoundary(char c) => c == Danda || c == DoubleDanda;

        // --- cleaning: raw range -> rendered text ---------------------------

        private static string Clean(string xml, int start, int end, bool includeNotes)
        {
            if (start >= end) return "";
            var sb = new StringBuilder(end - start);
            int i = start;
            while (i < end)
            {
                char c = xml[i];
                if (c == '<')
                {
                    int gt = xml.IndexOf('>', i);
                    if (gt < 0 || gt >= end) break;
                    string tag = xml.Substring(i, gt - i + 1);
                    string name = TagName(tag);
                    if (name == "note" && !includeNotes && !tag.EndsWith("/>", StringComparison.Ordinal))
                        i = SkipSubtree(xml, gt + 1, "note", end);
                    else if (name == "hi" && IsStructuralHi(tag) && !tag.EndsWith("/>", StringComparison.Ordinal))
                        i = SkipSubtree(xml, gt + 1, "hi", end);
                    else
                    {
                        if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                        i = gt + 1;
                    }
                }
                else { sb.Append(c); i++; }
            }
            return sb.ToString();
        }

        private static int VisibleLen(string xml, int start, int end) => Clean(xml, start, end, false).Length;

        // --- helpers --------------------------------------------------------

        private static string Convert(string deva, Script output) =>
            deva.Length == 0 ? "" : ScriptConverter.Convert(deva, Script.Devanagari, output);

        private static string Collapse(string s) => Whitespace.Replace(s, " ");

        private static (int openIdx, int contentStart, int pEnd, string rend) FindEnclosingP(string xml, int hitStart)
        {
            int lastClose = xml.LastIndexOf("</p>", Math.Min(hitStart, xml.Length), StringComparison.Ordinal);
            int open = FindLastPOpen(xml, hitStart);
            if (open < 0 || open < lastClose) return (-1, -1, -1, "");
            int gt = xml.IndexOf('>', open);
            if (gt < 0) return (-1, -1, -1, "");
            int pEnd = xml.IndexOf("</p>", gt + 1, StringComparison.Ordinal);
            if (pEnd < 0) pEnd = xml.Length;
            return (open, gt + 1, pEnd, Attr(xml.Substring(open, gt - open + 1), "rend"));
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

        private static List<(int s, int e)> NoteRegions(string xml, int lo, int hi)
        {
            var list = new List<(int, int)>();
            int i = lo;
            while ((i = xml.IndexOf("<note", i, StringComparison.Ordinal)) >= 0 && i < hi)
            {
                int gt = xml.IndexOf('>', i);
                if (gt < 0) break;
                if (xml[gt - 1] == '/') { i = gt + 1; continue; }        // self-closing
                int e = SkipSubtree(xml, gt + 1, "note", xml.Length);
                list.Add((i, e));
                i = e;
            }
            return list;
        }

        private static bool InNote(int pos, List<(int s, int e)> notes)
        {
            foreach (var (s, e) in notes) if (pos >= s && pos < e) return true;
            return false;
        }

        // Position just past the matching close tag for an already-open element.
        private static int SkipSubtree(string xml, int from, string name, int limit)
        {
            int depth = 1, i = from;
            string open = "<" + name, close = "</" + name;
            while (i < limit && depth > 0)
            {
                int lt = xml.IndexOf('<', i);
                if (lt < 0 || lt >= limit) return limit;
                if (xml.AsSpan(lt).StartsWith(close)) { depth--; i = xml.IndexOf('>', lt); i = i < 0 ? limit : i + 1; }
                else if (xml.AsSpan(lt).StartsWith(open) && (lt + open.Length >= xml.Length ||
                         xml[lt + open.Length] is ' ' or '>' or '\t' or '\n' or '\r'))
                { int gt = xml.IndexOf('>', lt); if (gt >= 0 && xml[gt - 1] != '/') depth++; i = gt < 0 ? limit : gt + 1; }
                else i = lt + 1;
            }
            return i;
        }

        private static int SnapToSpace(string xml, int pos, int dir, int stopAt)
        {
            for (int i = pos; dir > 0 ? i < stopAt : i > stopAt; i += dir)
                if (xml[i] == ' ' || xml[i] == '\n') return i + (dir > 0 ? 1 : 0);
            return pos;
        }

        private static string TagName(string tag)
        {
            int i = 1;
            if (i < tag.Length && tag[i] == '/') i++;
            int start = i;
            while (i < tag.Length && (char.IsLetterOrDigit(tag[i]) || tag[i] == ':')) i++;
            return tag.Substring(start, i - start);
        }

        private static bool IsStructuralHi(string tag)
        {
            string rend = Attr(tag, "rend");
            return rend == "paranum" || rend == "dot";
        }

        private static string Attr(string tag, string name)
        {
            var m = Regex.Match(tag, name + "\\s*=\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }
    }
}
