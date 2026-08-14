using System.Collections.Generic;
using System.Text.RegularExpressions;
using CST.Navigation;

namespace CST.Search
{
    /// <summary>
    /// A position index of a book's citation markers — page breaks (per edition) and numbered paragraphs —
    /// keyed by character position in the decoded XML (the same coordinate the search offsets use). Built
    /// once per book; answers "what page/paragraph is in effect at this hit position?" in a few binary
    /// searches. Reports current position only; it is not the navigation anchor catalog.
    /// </summary>
    public sealed class BookMarkers
    {
        private static readonly Regex TagRx = new(@"<[^>]*>", RegexOptions.Compiled);
        private static readonly Regex AttrRx = new("(\\w+)\\s*=\\s*\"([^\"]*)\"", RegexOptions.Compiled);

        // Ascending-by-position lists. A paragraph spans First..Last inclusive; the two are equal for the
        // ordinary single-numbered case, which is 97% of the corpus. (#446)
        private readonly List<(int Pos, int First, int Last, string? BookCode)> _paras = new();
        // Raw is the VERBATIM @n ("1.0023"). The HTML anchor is ed + that exact string and the reader looks it
        // up case/character-exactly, so the raw spelling — not the parsed ints — is what can be navigated to.
        private readonly Dictionary<PageEdition, List<(int Pos, int Volume, int Number, string Raw)>> _pages = new();

        private BookMarkers() { }

        /// <summary>Scan a decoded book XML string and index its page/paragraph markers.</summary>
        public static BookMarkers Build(string xml)
        {
            var m = new BookMarkers();
            var divStack = new List<(bool IsBook, string? Id)>();

            foreach (Match tag in TagRx.Matches(xml))
            {
                var (closing, name, attrs) = Parse(tag.Value);
                switch (name)
                {
                    case "div":
                        if (closing) { if (divStack.Count > 0) divStack.RemoveAt(divStack.Count - 1); }
                        else if (!tag.Value.EndsWith("/>"))
                            divStack.Add((attrs.TryGetValue("type", out var t) && t == "book",
                                          attrs.TryGetValue("id", out var id) ? id : null));
                        break;

                    case "pb":
                        if (!closing && attrs.TryGetValue("ed", out var ed) && attrs.TryGetValue("n", out var pn)
                            && Edition(ed) is PageEdition edition && SplitVolPage(pn, out int vol, out int num))
                        {
                            if (!m._pages.TryGetValue(edition, out var list))
                                m._pages[edition] = list = new();
                            list.Add((tag.Index, vol, num, pn));
                        }
                        break;

                    case "p":
                        if (!closing && attrs.TryGetValue("n", out var nStr)
                            && TryParseParagraphSpan(nStr, out int first, out int last))
                            m._paras.Add((tag.Index, first, last, CurrentBook(divStack)));
                        break;
                }
            }
            return m;
        }

        /// <summary>
        /// The character position of a numbered paragraph (optionally within a Multi sub-book), or -1.
        ///
        /// <para>Matches a number ANYWHERE in a paragraph's span, not just its label. Paragraph 20 of
        /// <c>vin02t.tik.xml</c> is inside the block labelled <c>16-26</c>; before #446 this returned -1 for
        /// every such N, so addressing into the 3,618 ranged paragraphs failed outright rather than landing
        /// slightly off.</para>
        /// </summary>
        public int PositionOfParagraph(int number, string? bookCode = null)
        {
            // AN EXACTLY-NUMBERED PARAGRAPH WINS OVER A RANGE THAT MERELY COVERS THE NUMBER, and the two do
            // collide: in 36 corpus files a ranged paragraph spans numbers that also exist in their own
            // right — abh03a.att.xml has 1,445 such overlaps, where "7-10" precedes standalone 7, 8, 9 and
            // 10. Taking the first positional match would hand every one of them to the range, which is a
            // worse answer than the -1 this used to return, because it is confidently wrong rather than
            // absent. The range is the fallback, for numbers that exist ONLY inside one. (#446)
            foreach (var p in _paras)
                if (p.First == number && p.Last == number && (bookCode == null || p.BookCode == bookCode))
                    return p.Pos;

            foreach (var p in _paras)
                if (number >= p.First && number <= p.Last && (bookCode == null || p.BookCode == bookCode))
                    return p.Pos;

            return -1;
        }

        /// <summary>The paragraph number (and Multi-book sub-code) and per-edition pages in effect at <paramref name="pos"/>.</summary>
        public (int? Number, string? BookCode, IReadOnlyList<SnippetPageRef> Pages) RefsAt(int pos)
        {
            int? number = null; string? code = null;
            var pi = UpperBound(_paras.Count, i => _paras[i].Pos <= pos) - 1;
            // The span's FIRST number is what a ranged paragraph is cited by. Reporting the range itself is
            // a change to this method's contract and belongs with the model work in #444; what #446 fixes is
            // that a ranged paragraph used to be skipped entirely, so this answered with the last paragraph
            // BEFORE it — a different paragraph, reported with no hint of approximation.
            if (pi >= 0) { number = _paras[pi].First; code = _paras[pi].BookCode; }

            var pages = new List<SnippetPageRef>();
            foreach (var (edition, list) in _pages)
            {
                var idx = UpperBound(list.Count, i => list[i].Pos <= pos) - 1;
                if (idx >= 0) pages.Add(new SnippetPageRef(edition, list[idx].Volume, list[idx].Number));
            }
            pages.Sort((a, b) => a.Edition.CompareTo(b.Edition));
            return (number, code, pages);
        }

        /// <summary>
        /// Look up a page break by its parsed coordinates and return the VERBATIM <c>@n</c> that the document
        /// actually carries (e.g. "1.0023"). Callers must navigate with this spelling rather than one they
        /// composed: the HTML anchor is <c>ed + @n</c> matched exactly, so "V1.23" and "V1.0023" address the
        /// same page but only one of them resolves. (#187)
        /// </summary>
        public bool TryGetPageAnchor(PageEdition edition, int volume, int number, out string rawN)
        {
            if (_pages.TryGetValue(edition, out var list))
                foreach (var p in list)
                    if (p.Volume == volume && p.Number == number) { rawN = p.Raw; return true; }
            rawN = string.Empty;
            return false;
        }

        /// <summary>
        /// The page editions this book carries, in a stable order. Not every text was printed in every edition —
        /// the VRI set is 140 volumes and many texts were never published outside Myanmar — so this reports what
        /// is actually present rather than what a caller hoped for. (#187)
        /// </summary>
        public IReadOnlyList<PageEdition> Editions()
        {
            var eds = new List<PageEdition>(_pages.Keys);
            eds.Sort();
            return eds;
        }

        /// <summary>The distinct sub-book codes in this book, in first-appearance order (empty for a non-Multi
        /// book, whose paragraphs carry no book-div code).</summary>
        public IReadOnlyList<string> DistinctBookCodes()
        {
            var codes = new List<string>();
            foreach (var p in _paras)
                if (p.BookCode is { } code && !codes.Contains(code)) codes.Add(code);
            return codes;
        }

        /// <summary>
        /// The inclusive paragraph span a <c>&lt;p&gt;</c>'s <c>@n</c> denotes. (#446)
        ///
        /// <para><b>Why this is not <c>int.TryParse</c>.</b> 3,618 paragraphs across 87 of the 217 corpus
        /// files carry a non-integer <c>@n</c>. They used to fail the parse and be dropped from the index
        /// silently, which did not produce a missing citation — it produced a WRONG one, because the lookup
        /// then answered with the last paragraph it had indexed, the one before the block.</para>
        ///
        /// <para><b>One rule covers every form found in the corpus.</b> Split on the hyphen, expand each part
        /// against the one before it, then take first-to-last as the span:</para>
        ///
        /// <list type="table">
        /// <item><term><c>21</c></term><description>21..21 — the ordinary case, 97% of paragraphs</description></item>
        /// <item><term><c>16-26</c></term><description>16..26 — a full range (2,094 of them)</description></item>
        /// <item><term><c>196-7</c></term><description>196..197 — the tail is ABBREVIATED, sharing the leading
        /// digits of its predecessor, so this is not 196..7 (1,518 of them)</description></item>
        /// <item><term><c>266-7-8</c></term><description>266..268 — the same abbreviation applied twice; the
        /// next numbered paragraph in vin11t is 269, which is what confirms the reading</description></item>
        /// <item><term><c>292-3-6</c></term><description>292..296 — likewise, followed by 297 in s0105t</description></item>
        /// </list>
        ///
        /// <para><b>The corpus also holds two genuine typos</b> — <c>179-</c> in e0812n and a bare <c>-</c> in
        /// e1207n. The trailing-hyphen form keeps its leading number, since 179 is a real paragraph and
        /// discarding it would reintroduce exactly the bug this fixes. A value with no digits at all has
        /// nothing to report and is skipped, as before.</para>
        /// </summary>
        internal static bool TryParseParagraphSpan(string? n, out int first, out int last)
        {
            first = last = 0;
            if (string.IsNullOrWhiteSpace(n)) return false;

            string? previous = null;
            bool any = false;

            foreach (var raw in n.Split('-'))
            {
                var part = raw.Trim();
                if (part.Length == 0) continue;              // "179-" and the bare "-"

                // Non-numeric anywhere means this is not a paragraph number we understand. Stop rather than
                // guess: a half-read value is indistinguishable from a correct one downstream.
                bool digits = true;
                foreach (var c in part) if (c < '0' || c > '9') { digits = false; break; }
                if (!digits) break;

                var expanded = Expand(previous, part);
                if (!int.TryParse(expanded, out var value)) break;

                if (!any) { first = value; any = true; }
                last = value;                                // the span runs from the FIRST written number
                previous = expanded;                         // to the LAST, not min to max
            }

            // A span that runs BACKWARDS is a typo, not a range. abh05t.nrf.xml has n="706-608" sitting
            // between 703-705 and 709-710, so the intent is plainly 706-708 and the 6 is a slip. Swapping
            // the ends would manufacture a 99-paragraph block out of a digit error, and that block would
            // then shadow every real paragraph from 608 to 705. Keeping only the opening number cites the
            // paragraph correctly and claims nothing about an extent we cannot know. (#446)
            if (any && last < first) last = first;
            return any;
        }

        /// <summary>
        /// Expand an abbreviated continuation against the part before it: <c>292</c> then <c>3</c> is 293,
        /// not 3. A part at least as long as its predecessor is already complete and stands as written, which
        /// is what keeps <c>16-26</c> a range from 16 to 26 rather than 16 to 16.
        /// </summary>
        private static string Expand(string? previous, string part) =>
            previous is null || part.Length >= previous.Length
                ? part
                : previous.Substring(0, previous.Length - part.Length) + part;

        // Largest index+1 whose predicate holds over an ascending-sorted list (i.e. count of leading trues).
        private static int UpperBound(int count, System.Func<int, bool> leadingTrue)
        {
            int lo = 0, hi = count;
            while (lo < hi) { int mid = (lo + hi) / 2; if (leadingTrue(mid)) lo = mid + 1; else hi = mid; }
            return lo;
        }

        private static string? CurrentBook(List<(bool IsBook, string? Id)> stack)
        {
            foreach (var d in stack) if (d.IsBook && d.Id != null) return d.Id;  // outermost book div
            return null;
        }

        private static (bool Closing, string Name, Dictionary<string, string> Attrs) Parse(string tag)
        {
            int i = 1;
            bool closing = i < tag.Length && tag[i] == '/';
            if (closing) i++;
            int start = i;
            while (i < tag.Length && (char.IsLetterOrDigit(tag[i]) || tag[i] == ':')) i++;
            string name = tag.Substring(start, i - start);
            var attrs = new Dictionary<string, string>();
            if (!closing)
                foreach (Match a in AttrRx.Matches(tag)) attrs[a.Groups[1].Value] = a.Groups[2].Value;
            return (closing, name, attrs);
        }

        private static PageEdition? Edition(string ed) => ed switch
        {
            "V" => PageEdition.Vri,
            "M" => PageEdition.Myanmar,
            "P" => PageEdition.Pts,
            "T" => PageEdition.Thai,
            "O" => PageEdition.Other,
            _ => null
        };

        private static bool SplitVolPage(string n, out int volume, out int number)
        {
            volume = 0; number = 0;
            int dot = n.IndexOf('.');
            if (dot >= 0)
                return int.TryParse(n.Substring(0, dot), out volume) && int.TryParse(n.Substring(dot + 1), out number);
            return int.TryParse(n, out number);
        }
    }
}
