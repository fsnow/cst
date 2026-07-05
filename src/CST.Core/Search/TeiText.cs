using System;
using System.Collections.Generic;
using System.Text;
using System.Text.RegularExpressions;
using CST.Conversion;

namespace CST.Search
{
    /// <summary>
    /// Shared low-level TEI text helpers used by the snippet extractor and the passage reader: turning a raw
    /// XML range into rendered Pali text (tags transparent, footnotes and the paragraph-number marker
    /// stripped), sentence-boundary detection, and Devanagari-to-script conversion.
    /// </summary>
    internal static class TeiText
    {
        internal const char Danda = '\u0964';        // Devanagari danda - sentence end
        internal const char DoubleDanda = '\u0965';  // Devanagari double danda - verse/gatha end
        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);

        internal static bool IsBoundary(char c) => c == Danda || c == DoubleDanda;

        internal static string Convert(string deva, Script output) =>
            deva.Length == 0 ? "" : ScriptConverter.Convert(deva, Script.Devanagari, output);

        internal static string Collapse(string s) => Whitespace.Replace(s, " ");

        internal static int VisibleLen(string xml, int start, int end) => Clean(xml, start, end, false).Length;

        /// <summary>Render a raw XML range to text: drop tags (paranum/dot hi and, unless requested, notes are stripped whole).</summary>
        internal static string Clean(string xml, int start, int end, bool includeNotes)
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
                    if (name == "note" && !includeNotes && !tag.EndsWith("/>", System.StringComparison.Ordinal))
                        i = SkipSubtree(xml, gt + 1, "note", end);
                    else if (name == "hi" && IsStructuralHi(tag) && !tag.EndsWith("/>", System.StringComparison.Ordinal))
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

        internal static List<(int s, int e)> NoteRegions(string xml, int lo, int hi)
        {
            var list = new List<(int, int)>();
            int i = lo;
            while ((i = xml.IndexOf("<note", i, System.StringComparison.Ordinal)) >= 0 && i < hi)
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

        internal static bool InNote(int pos, List<(int s, int e)> notes)
        {
            foreach (var (s, e) in notes) if (pos >= s && pos < e) return true;
            return false;
        }

        // Position just past the matching close tag for an already-open element.
        internal static int SkipSubtree(string xml, int from, string name, int limit)
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

        internal static string TagName(string tag)
        {
            int i = 1;
            if (i < tag.Length && tag[i] == '/') i++;
            int start = i;
            while (i < tag.Length && (char.IsLetterOrDigit(tag[i]) || tag[i] == ':')) i++;
            return tag.Substring(start, i - start);
        }

        internal static bool IsStructuralHi(string tag)
        {
            string rend = Attr(tag, "rend");
            return rend == "paranum" || rend == "dot";
        }

        internal static string Attr(string tag, string name)
        {
            var m = Regex.Match(tag, name + "\\s*=\\s*\"([^\"]*)\"");
            return m.Success ? m.Groups[1].Value : "";
        }
    }
}
