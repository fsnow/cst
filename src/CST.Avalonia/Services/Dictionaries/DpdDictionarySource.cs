using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Search;
using CST.Conversion;
using CST.Lemma;
using CST.Tools;

namespace CST.Avalonia.Services.Dictionaries
{
    /// <summary>
    /// The Digital Pāḷi Dictionary as a source, present only when the dpd-cst-subset asset is installed. DPD is
    /// NOT headword-shaped: an inflected query resolves through the form→lemma index, and each candidate lemma's
    /// entry is COMPOSED from structured fields (pos + gloss + literal + construction) with a <c>lemmaId</c> that
    /// chains to the lemma report. Moved here verbatim from the former CompositeDictionaryTool. (#109/#466)
    /// <para>Roots (#1002): a word entry whose lemma has a root shows it as a link (<c>root √var 1 · cover, …</c>),
    /// and a query that starts with the root sign <c>√</c> looks up DPD's <c>root</c> table instead of the form
    /// index — one entry per root (headword <c>√var 1</c>, no <c>lemmaId</c>) with its meaning, group and sign,
    /// Sanskrit root and Dhātupāṭha, and a link to each headword built on it (number included, e.g. <c>accita 2.1</c>).
    /// A numbered word query returns just that headword. A query without the sign is a word query.</para>
    /// </summary>
    public sealed class DpdDictionarySource : IDictionarySource
    {
        public const string SourceId = "dpd";
        private const int MaxDictEntries = 500;

        /// <summary>Most headwords a root entry lists (#1002). Above the largest root in the shipped asset
        /// (√kar, 817 headwords in v0.4.20260531), so no root is cut short today; it bounds the entry if DPD grows.
        /// A capped list says so ("first N of M headwords").</summary>
        public const int MaxRootWords = 1000;

        /// <summary>DPD's root sign, U+221A SQUARE ROOT, as stored at the start of every <c>root_key</c>.</summary>
        public const char RootSign = '\u221A';

        private readonly ILemmaProvider _lemma;
        private readonly int _maxRootWords;

        public DpdDictionarySource(ILemmaProvider lemma, int maxRootWords = MaxRootWords)
        {
            _lemma = lemma;
            _maxRootWords = Math.Max(0, maxRootWords);
        }

        public string Id => SourceId;
        public string DisplayName => "Digital Pāḷi Dictionary";
        public DictionarySourceKind Kind => DictionarySourceKind.General;
        public bool IsAvailable => _lemma.IsAvailable;
        public DictionarySourceInfo? Attribution => DpdSource();

        public Task<IReadOnlyList<DictionaryEntry>> LookupAsync(DictionaryRequest request, CancellationToken ct = default)
            // Blocking SQLite (a ResolveForm + up to `cap` GetDetail round-trips): offload so it doesn't hold the
            // Kestrel request thread. (#279)
            => Task.Run(() => LookupDpd(request, ct), ct);

        private IReadOnlyList<DictionaryEntry> LookupDpd(DictionaryRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!_lemma.IsAvailable || string.IsNullOrWhiteSpace(request.Query))
                return Array.Empty<DictionaryEntry>();

            // Query normalization mirrors DictionaryService: strip zero-width joiners, lower-case, NFC; any script
            // → IPE → IAST (DPD's storage script).
            var normalized = MultiWordSearch.StripJoiners(request.Query).ToLowerInvariant().Normalize(NormalizationForm.FormC);
            string ipe = Any2Ipe.Convert(normalized);
            string iast = ScriptConverter.Convert(ipe, Script.Ipe, Script.Latin);

            // The converters pass the root sign through untouched (it belongs to no script), so it survives to here.
            if (iast.Length > 0 && iast[0] == RootSign)
                return LookupRoots(iast, request, ct);

            // A numbered headword ("accita 2.1", as a root entry links it) names one lemma: resolve the word without
            // the number, then keep only the candidate with that exact headword. No word form carries a number,
            // so this never shadows a form lookup. (#1002)
            string? headword = null;
            var folded = Whitespace.Replace(PaliDigits.ToAscii(iast), " ").Trim();
            if (HomonymNumber.IsMatch(folded))
            {
                headword = folded;
                iast = LemmaHeadword.StripHomonym(folded);
            }

            var resolution = _lemma.ResolveForm(iast);
            if (resolution is null || resolution.Candidates.Count == 0)
                return Array.Empty<DictionaryEntry>();

            var source = DpdSource()?.Title;
            int cap = Math.Clamp(request.MaxEntries, 0, MaxDictEntries);
            var entries = new List<DictionaryEntry>(Math.Min(cap, resolution.Candidates.Count));
            foreach (var cand in resolution.Candidates)
            {
                if (headword != null && !string.Equals(cand.Lemma, headword, StringComparison.Ordinal)) continue;
                if (entries.Count >= cap) break;
                ct.ThrowIfCancellationRequested();
                var detail = _lemma.GetDetail(cand.LemmaId);
                if (detail is null) continue;
                entries.Add(new DictionaryEntry(
                    Headword: ToScript(detail.Lemma, request.OutputScript),
                    MeaningHtml: ComposeMeaning(detail),
                    Source: source,
                    LemmaId: detail.LemmaId));
            }
            return entries;
        }

        // A root query (#1002), widest last:
        //   "√var 1" (a trailing homonym number) → that root only;
        //   "√var"   → the root with that key or its numbered homonyms (√var 1, √var 2) - so "√man", which is what
        //              a word entry links to, finds √man and not √mant;
        //   "√va"    → no such root, so every root whose key starts with it;
        //   "√"      → nothing, as an empty query is nothing.
        // Native digits are folded first: following a root link in Devanagari puts the key with a Devanagari "1"
        // in the search box, and the converters leave that digit as it is. Results are in Pāli order (by IPE,
        // whose code points sort in alphabet order), not code-point order, which put √bhā after √bhus.
        private IReadOnlyList<DictionaryEntry> LookupRoots(string iast, DictionaryRequest request, CancellationToken ct)
        {
            var rest = Whitespace.Replace(PaliDigits.ToAscii(iast.Substring(1)), " ").Trim();
            int cap = Math.Clamp(request.MaxEntries, 0, MaxDictEntries);
            if (rest.Length == 0) return Array.Empty<DictionaryEntry>();

            var key = RootSign + rest;
            IReadOnlyList<RootEntry>? roots;
            if (HomonymNumber.IsMatch(rest))
                roots = _lemma.FindRoots(key, RootMatch.Exact, cap, _maxRootWords, PaliOrder);
            else
            {
                roots = _lemma.FindRoots(key, RootMatch.Homonyms, cap, _maxRootWords, PaliOrder);
                if (roots is { Count: 0 })
                    roots = _lemma.FindRoots(key, RootMatch.Prefix, cap, _maxRootWords, PaliOrder);
            }
            if (roots is null || roots.Count == 0) return Array.Empty<DictionaryEntry>();

            ct.ThrowIfCancellationRequested();
            var source = DpdSource()?.Title;
            var entries = new List<DictionaryEntry>(roots.Count);
            foreach (var root in roots)
                entries.Add(new DictionaryEntry(
                    Headword: ToScript(root.Root.RootKey, request.OutputScript),
                    MeaningHtml: ComposeRootMeaning(root, request.OutputScript),
                    Source: source,
                    LemmaId: null));
            return entries;
        }

        private static readonly Regex Whitespace = new(@"\s+", RegexOptions.Compiled);
        private static readonly Regex HomonymNumber = new(@" [0-9][0-9.]*$", RegexOptions.Compiled);
        private static readonly Regex BoldTag = new(@"(</?b>)", RegexOptions.Compiled);

        private static string PaliOrder(string rootKey) => Any2Ipe.Convert(rootKey);

        // The root entry. Pāli (sign, Dhātupāṭha) follows the requested script; the Sanskrit root stays in IAST
        // (it is Sanskrit, and the Pāli converters have no ṛ); English is English. Root and word links are <see>
        // tags carrying the Latin lookup target, which the panel's renderer displays in the reader's script.
        private static string ComposeRootMeaning(RootEntry e, Script script)
        {
            var r = e.Root;
            var sb = new StringBuilder();
            sb.Append("<i>root</i>");
            if (!IsBlank(r.RootMeaning)) sb.Append(' ').Append(EncEnglish(r.RootMeaning!));

            var info = new List<string>();
            if (r.RootGroup is long g) info.Add("group " + g.ToString(CultureInfo.InvariantCulture));
            if (!IsBlank(r.RootSign)) info.Add("sign " + EncPali(r.RootSign!, script));
            if (info.Count > 0) sb.Append("<div class=\"dpd-root-info\">").Append(string.Join(" · ", info)).Append("</div>");

            if (!IsBlank(r.SanskritRoot))
            {
                sb.Append("<div class=\"dpd-root-sk\">Sanskrit ").Append(Enc(r.SanskritRoot!));
                if (!IsBlank(r.SanskritRootMeaning)) sb.Append(" (").Append(EncEnglish(r.SanskritRootMeaning!)).Append(')');
                sb.Append("</div>");
            }
            if (!IsBlank(r.DhatupathaPali))
            {
                sb.Append("<div class=\"dpd-root-dp\">Dhātupāṭha: ").Append(EncPali(r.DhatupathaPali!, script));
                if (!IsBlank(r.DhatupathaEnglish)) sb.Append(" (").Append(EncEnglish(r.DhatupathaEnglish!)).Append(')');
                sb.Append("</div>");
            }

            sb.Append("<div class=\"dpd-root-words\">");
            if (e.LemmaCount == 0) sb.Append("no headwords");
            else
            {
                sb.Append(e.Lemmas.Count < e.LemmaCount
                    ? $"first {e.Lemmas.Count} of {e.LemmaCount} headwords: "
                    : $"{e.LemmaCount} headwords: ");
                // One link per headword, number included ("accita 2.1"): a homonym is a different word, often
                // with a different root, and the number is what lets the lookup land on this one rather than
                // whichever homonym comes first.
                for (int i = 0; i < e.Lemmas.Count; i++)
                {
                    if (i > 0) sb.Append(", ");
                    sb.Append("<see>").Append(Enc(e.Lemmas[i].Lemma)).Append("</see>");
                }
            }
            sb.Append("</div>");
            return sb.ToString();
        }

        private static bool IsBlank(string? s) => string.IsNullOrWhiteSpace(s) || s.Trim() == "-";   // "-" is DPD's "none"

        // DPD's root fields carry <b> emphasis (22 roots in v0.4.20260531) and no other markup; keep that one tag,
        // escape the rest.
        private static string EncEnglish(string s) =>
            Enc(s).Replace("&lt;b&gt;", "<b>").Replace("&lt;/b&gt;", "</b>");

        // Convert Pāli to the display script around the <b> tags (converting a tag would turn "b" into a letter),
        // escaping each piece.
        private static string EncPali(string s, Script script)
        {
            var sb = new StringBuilder();
            foreach (var part in BoldTag.Split(s))
                sb.Append(part is "<b>" or "</b>" ? part : Enc(ToScript(part, script)));
            return sb.ToString();
        }

        private static string ComposeMeaning(LemmaDetail d)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(d.Pos)) sb.Append("<i>").Append(Enc(d.Pos)).Append("</i> ");
            if (!string.IsNullOrEmpty(d.Gloss)) sb.Append(Enc(d.Gloss));
            if (!string.IsNullOrEmpty(d.MeaningLit))
                sb.Append(" <span class=\"dpd-lit\">(lit. ").Append(Enc(d.MeaningLit)).Append(")</span>");
            // The root, linked to its root entry (#1002). The <see> target is the Latin key; the panel's renderer
            // shows it in the reader's script.
            if (d.Root is { } root)
            {
                sb.Append("<div class=\"dpd-root\">root <see>").Append(Enc(root.RootKey)).Append("</see>");
                if (!IsBlank(root.RootMeaning)) sb.Append(" · ").Append(EncEnglish(root.RootMeaning!));
                sb.Append("</div>");
            }
            if (!string.IsNullOrEmpty(d.Construction))
                sb.Append("<div class=\"dpd-con\">").Append(Enc(d.Construction)).Append("</div>");
            return sb.ToString();
        }

        // Escape only HTML-significant chars, leaving Pāli diacritics literal (WebUtility.HtmlEncode would mangle
        // every non-ASCII letter). '&' first.
        private static string Enc(string s) => s.Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        private static string ToScript(string iast, Script output)
            => output is Script.Latin or Script.Ipe or Script.Unknown
                ? iast
                : ScriptConverter.Convert(iast, Script.Latin, output);

        // DPD's citation from the asset's meta (never hard-coded), or null when unattributed.
        private DictionarySourceInfo? DpdSource()
        {
            var m = _lemma.Meta;
            if (m is null) return null;
            var info = new DictionarySourceInfo(
                Title: NullIfBlank(m.Source),
                Compiler: NullIfBlank(m.Author),
                Edition: NullIfBlank(m.DpdVersion),
                Year: YearFromVersion(m.DpdVersion),
                Publisher: null,
                License: NullIfBlank(m.License),
                Url: NullIfBlank(m.Homepage));
            return IsUnattributed(info) ? null : info;
        }

        private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

        private static bool IsUnattributed(DictionarySourceInfo s) =>
            s.Title is null && s.Compiler is null && s.Edition is null && s.Year is null &&
            s.Publisher is null && s.License is null && s.Url is null;

        private static string? YearFromVersion(string? v)
        {
            if (string.IsNullOrEmpty(v)) return null;
            for (int i = 0; i < v.Length;)
            {
                if (!char.IsDigit(v[i])) { i++; continue; }
                int j = i;
                while (j < v.Length && char.IsDigit(v[j])) j++;
                if (j - i >= 8) return v.Substring(i, 4);
                i = j;
            }
            return null;
        }
    }
}
