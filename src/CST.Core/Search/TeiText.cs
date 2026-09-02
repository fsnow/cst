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

        internal static string Convert(string deva, Script output)
        {
            if (deva.Length == 0) return "";

            var converted = ScriptConverter.Convert(deva, Script.Devanagari, output);

            // Latin output must not carry Devanagari sentence punctuation. (#668)
            //
            // There are two Latin paths and they disagreed. The READER renders through
            // ScriptConverter.ConvertBook, which runs Deva2Latn.ConvertDandas and turns U+0964 into a period;
            // everything here goes through ScriptConverter.Convert, which does not, because the danda rules
            // are written against MARKUP (they distinguish gatha from prose, and strip the dandas around
            // namo tassa) and this text has had its tags removed by the time it arrives.
            //
            // So a reader selected text containing "." and the passage sent to the model contained "।" — the
            // same characters to a human, different ones to a comparison. That mismatch is why a selection
            // could not be located in its own passage, and why dandas showed up in the context a reader was
            // shown. #641 folded punctuation out of ONE comparison to work around it; this removes the
            // divergence instead.
            //
            // The markup-dependent rules (gatha, namo tassa) are applied by Clean, which still has the tags
            // — see LatinStop. This is the fallback for the prose case and for any text that did not come
            // through Clean, so the two never disagree the way the two Latin paths once did. (#680)
            if (output == Script.Latin)
                converted = ScriptConverter.LatinizeDandas(converted);

            return converted;
        }

        internal static string Collapse(string s) => Whitespace.Replace(s, " ");

        internal static int VisibleLen(string xml, int start, int end) => Clean(xml, start, end, false).Length;

        /// <summary>
        /// Render a raw XML range to text: drop tags (paranum/dot hi and, unless requested, notes are stripped
        /// whole). With <paramref name="output"/> = Latin the Devanagari stops are rendered as the reader
        /// renders them, which needs the markup this method still has: in a gatha a single danda ends a pada
        /// and becomes ";" while a double ends the verse and becomes ".", and the double dandas around
        /// *namo tassa* are dropped. Any other output script keeps the dandas for the converter to handle.
        /// (#680)
        /// </summary>
        internal static string Clean(string xml, int start, int end, bool includeNotes,
            Script output = Script.Devanagari)
        {
            if (start >= end) return "";
            var sb = new StringBuilder(end - start);
            // A window can begin mid-paragraph — a snippet usually does — so the opening <p> may be behind
            // `start` and never seen by the walk below. Look it up rather than defaulting to prose, or every
            // snippet that starts inside a verse would be punctuated as prose.
            bool latin = output == Script.Latin;
            string rend = latin ? ParagraphRendAt(xml, start) : "";
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
                    if (name == "note" && !tag.EndsWith("/>", System.StringComparison.Ordinal))
                    {
                        // TagName("</note>") is "note" too, so this branch fires for BOTH open and close. A close
                        // tag means the range began INSIDE a note — it must be zero-width, never a subtree: calling
                        // SkipSubtree on a lone </note> enters depth 1 and consumes to the NEXT </note> (or the whole
                        // rest of the window), silently dropping the following base text. (#310 A4-2)
                        bool isClose = tag.StartsWith("</", System.StringComparison.Ordinal);
                        if (!includeNotes)
                            i = isClose ? gt + 1 : SkipSubtree(xml, gt + 1, "note", end);
                        else
                        {
                            // Delimit an included note with curly braces (which never occur in the corpus - 0
                            // across all 217 files) so a consumer can tell injected footnote/variant apparatus
                            // from the base text; the parentheses the notes carry are otherwise indistinguishable
                            // from the text's own. Open tag => '{', close tag => '}'; inside stays `reading (sigla)`.
                            if (!isClose && sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                            sb.Append(isClose ? '}' : '{');
                            i = gt + 1;
                        }
                    }
                    else if (name == "hi" && IsStructuralHi(tag) && !tag.EndsWith("/>", System.StringComparison.Ordinal))
                        i = SkipSubtree(xml, gt + 1, "hi", end);
                    else if (name == "hi")
                        // A non-structural <hi>/</hi> (e.g. rend="bold") is intra-word to the tokenizer — treat it
                        // zero-width (no injected space), else a hit on a bold-containing word renders "sa mmā" and
                        // the highlight text stops matching the term. (#313 A4-10)
                        i = gt + 1;
                    else
                    {
                        if (latin && name == "p")
                            rend = tag.StartsWith("</", System.StringComparison.Ordinal) ? "" : Attr(tag, "rend");
                        if (sb.Length > 0 && sb[sb.Length - 1] != ' ') sb.Append(' ');
                        i = gt + 1;
                    }
                }
                else
                {
                    // A stripped inline tag (page break, dot-hi, a not-included note) — or a stray space in the
                    // source — can leave a space in front of clause/sentence punctuation (e.g. "…upaneti ,").
                    // That punctuation should hug the preceding word, so drop the dangling space. (#292)
                    if (IsClosePunctuation(c) && sb.Length > 0 && sb[sb.Length - 1] == ' ')
                        sb.Length--;

                    if (latin && IsBoundary(c))
                    {
                        // Dropping the mark rather than appending one is why the space is removed above and
                        // not here: "assa U+0965 Bhagava" closes up to "assa Bhagava" on the following space,
                        // instead of leaving two.
                        if (LatinStop(c, rend) is { } stop) sb.Append(stop);
                    }
                    else sb.Append(c);

                    i++;
                }
            }
            return sb.ToString();
        }

        /// <summary>
        /// How a Devanagari stop reads in Latin, given the <c>rend</c> of the paragraph it sits in. Mirrors
        /// Deva2Latn.ConvertDandas, which the reader renders through; null means the mark is dropped. In verse
        /// the distinction is metrical — a single danda ends a pada, a double ends the verse — so flattening
        /// both to "." would show a model every line of a gatha ending identically. (#680)
        /// </summary>
        internal static char? LatinStop(char c, string rend)
        {
            if (rend.StartsWith("gatha", StringComparison.Ordinal))
                return c == Danda ? ';' : '.';

            // *Namo tassa* carries no stop in the reader. Single dandas are untouched there, so they are here.
            if (rend == "centre" && c == DoubleDanda)
                return null;

            return '.';
        }

        /// <summary>
        /// The <c>rend</c> of the <c>&lt;p&gt;</c> enclosing <paramref name="pos"/>, or "" outside one. Scans
        /// back only as far as the previous <c>&lt;/p&gt;</c>, so it is bounded by one paragraph however large
        /// the book is. (#680)
        /// </summary>
        internal static string ParagraphRendAt(string xml, int pos)
        {
            if (pos <= 0 || xml.Length == 0) return "";

            int scan = Math.Min(pos, xml.Length) - 1;
            int closed = xml.LastIndexOf("</p>", scan, StringComparison.Ordinal);

            for (int i = scan; i > closed; i--)
            {
                // "<p" alone is not enough: <paranum> and <pb> start the same way. Same whitespace set as
                // TeiSnippetExtractor.FindLastPOpen, so the two scanners cannot disagree about what a
                // paragraph tag is.
                if (xml[i] != '<' || i + 2 >= xml.Length || xml[i + 1] != 'p') continue;
                char after = xml[i + 2];
                if (after != ' ' && after != '>' && after != '\t' && after != '\n' && after != '\r') continue;

                int gt = xml.IndexOf('>', i);
                return gt < 0 ? "" : Attr(xml.Substring(i, gt - i + 1), "rend");
            }

            return "";
        }

        // Punctuation that should sit directly after the preceding word (no space before it).
        internal static bool IsClosePunctuation(char c) =>
            c == ',' || c == ';' || c == '.' || c == '!' || c == '?' || c == Danda || c == DoubleDanda;

        /// <summary>
        /// Split the <c>{reading (sigla)}</c> apparatus spans out of already-rendered text into structured notes.
        /// Returns the brace-FREE text, the notes anchored by offset into it, and — for each input position in
        /// <paramref name="positions"/> (e.g. snippet highlight starts) — its remapped offset into the brace-free
        /// text. Braces never occur in the corpus, so they are unambiguous delimiters; a note's flanking space is
        /// collapsed (and dropped entirely before close punctuation, so the base text stays quotable), and a
        /// leading/trailing space left by an edge note is trimmed with all offsets shifted to match. (#267, #267 f/u)
        /// </summary>
        internal static (string Text, List<ApparatusNote> Notes, int[] Positions) SplitApparatus(
            string braced, IReadOnlyList<int>? positions = null)
        {
            int pn = positions?.Count ?? 0;
            var mapped = new int[pn];
            var pdone = new bool[pn];

            if (braced.IndexOf('{') < 0)
            {
                for (int k = 0; k < pn; k++) mapped[k] = positions![k];
                return (braced, new List<ApparatusNote>(), mapped);
            }

            var sb = new StringBuilder(braced.Length);
            var raw = new List<(int Offset, string Inner)>();
            int i = 0;
            while (i < braced.Length)
            {
                for (int k = 0; k < pn; k++)
                    if (!pdone[k] && i >= positions![k]) { mapped[k] = sb.Length; pdone[k] = true; }

                if (braced[i] == '{')
                {
                    int close = braced.IndexOf('}', i + 1);
                    if (close < 0) { sb.Append(braced, i, braced.Length - i); break; }   // malformed: keep verbatim
                    string inner = braced.Substring(i + 1, close - i - 1).Trim();

                    // If a tracked position (a snippet highlight) falls INSIDE this note, the hit itself is a
                    // variant reading (note text is indexed). Keep the note's text INLINE so the hit stays
                    // visible and its offset remaps char-by-char — never excise the hit into notes[] and leave
                    // an out-of-range highlight. The note is still listed. (#267 f/u review)
                    bool holdsHit = false;
                    for (int k = 0; k < pn; k++)
                        if (!pdone[k] && positions![k] > i && positions![k] < close) { holdsHit = true; break; }

                    if (holdsHit)
                    {
                        int noteOffset = sb.Length;
                        i++;                                  // past '{'
                        while (i < close)
                        {
                            for (int k = 0; k < pn; k++)
                                if (!pdone[k] && i >= positions![k]) { mapped[k] = sb.Length; pdone[k] = true; }
                            sb.Append(braced[i]);
                            i++;
                        }
                        i = close + 1;                        // past '}'
                        raw.Add((noteOffset, inner));
                    }
                    else
                    {
                        i = close + 1;
                        if (sb.Length > 0 && sb[sb.Length - 1] == ' ')
                        {
                            if (i < braced.Length && IsClosePunctuation(braced[i])) sb.Length--;   // hug the punctuation
                            else if (i < braced.Length && braced[i] == ' ') i++;                    // avoid a double space
                        }
                        raw.Add((sb.Length, inner));
                    }
                }
                else { sb.Append(braced[i]); i++; }
            }
            for (int k = 0; k < pn; k++) if (!pdone[k]) { mapped[k] = sb.Length; pdone[k] = true; }

            // Trim a leading/trailing space an edge note can leave, and shift every offset to match.
            string text = sb.ToString();
            int lead = 0; while (lead < text.Length && char.IsWhiteSpace(text[lead])) lead++;
            int tail = text.Length; while (tail > lead && char.IsWhiteSpace(text[tail - 1])) tail--;
            text = text.Substring(lead, tail - lead);

            var notes = new List<ApparatusNote>(raw.Count);
            foreach (var (offset, inner) in raw)
            {
                var (reading, sigla) = ParseApparatusNote(inner);
                notes.Add(new ApparatusNote(System.Math.Clamp(offset - lead, 0, text.Length), inner, reading, sigla));
            }
            for (int k = 0; k < pn; k++) mapped[k] = System.Math.Clamp(mapped[k] - lead, 0, text.Length);
            return (text, notes, mapped);
        }

        // Split "reading (sigla)" only when there is exactly one trailing parenthetical and nothing else in
        // parens — the notes are digitized print footnotes, not a clean apparatus, so anything more complex
        // (multiple readings, no sigla, freeform) is left as raw text with null reading/sigla. (#267)
        internal static (string? Reading, string? Sigla) ParseApparatusNote(string t)
        {
            if (!t.EndsWith(")", System.StringComparison.Ordinal)) return (null, null);
            int open = t.IndexOf('(');
            if (open <= 0 || t.IndexOf('(', open + 1) >= 0) return (null, null);   // zero or >1 '('
            string reading = t.Substring(0, open).Trim();
            string sigla = t.Substring(open + 1, t.Length - open - 2).Trim();
            return reading.Length > 0 && sigla.Length > 0 ? (reading, sigla) : (null, null);
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

        /// <summary>Count notes that INTERSECT the window <c>[lo, hi)</c>, including one that opened before
        /// <paramref name="lo"/> and extends into it. <see cref="NoteRegions"/> only finds notes that START in a
        /// range, so scan from <paramref name="scanFrom"/> (&lt;= lo — e.g. the enclosing paragraph start) and keep
        /// those overlapping the window. (#310 A4-15)</summary>
        internal static int CountNotesIntersecting(string xml, int scanFrom, int lo, int hi)
        {
            int count = 0;
            foreach (var (s, e) in NoteRegions(xml, Math.Min(scanFrom, lo), hi))
                if (s < hi && e > lo) count++;
            return count;
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
