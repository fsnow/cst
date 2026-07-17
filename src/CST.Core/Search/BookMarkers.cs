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

        // Ascending-by-position lists.
        private readonly List<(int Pos, int Number, string? BookCode)> _paras = new();
        private readonly Dictionary<PageEdition, List<(int Pos, int Volume, int Number)>> _pages = new();

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
                            list.Add((tag.Index, vol, num));
                        }
                        break;

                    case "p":
                        if (!closing && attrs.TryGetValue("n", out var nStr) && int.TryParse(nStr, out int pnum))
                            m._paras.Add((tag.Index, pnum, CurrentBook(divStack)));
                        break;
                }
            }
            return m;
        }

        /// <summary>The character position of a numbered paragraph (optionally within a Multi sub-book), or -1.</summary>
        public int PositionOfParagraph(int number, string? bookCode = null)
        {
            foreach (var p in _paras)
                if (p.Number == number && (bookCode == null || p.BookCode == bookCode))
                    return p.Pos;
            return -1;
        }

        /// <summary>The paragraph number (and Multi-book sub-code) and per-edition pages in effect at <paramref name="pos"/>.</summary>
        public (int? Number, string? BookCode, IReadOnlyList<SnippetPageRef> Pages) RefsAt(int pos)
        {
            int? number = null; string? code = null;
            var pi = UpperBound(_paras.Count, i => _paras[i].Pos <= pos) - 1;
            if (pi >= 0) { number = _paras[pi].Number; code = _paras[pi].BookCode; }

            var pages = new List<SnippetPageRef>();
            foreach (var (edition, list) in _pages)
            {
                var idx = UpperBound(list.Count, i => list[i].Pos <= pos) - 1;
                if (idx >= 0) pages.Add(new SnippetPageRef(edition, list[idx].Volume, list[idx].Number));
            }
            pages.Sort((a, b) => a.Edition.CompareTo(b.Edition));
            return (number, code, pages);
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
