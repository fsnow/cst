using System.Globalization;
using System.Text;
using System.Text.RegularExpressions;
using CST.Conversion;

namespace CST.Lexicon
{
    /// <summary>
    /// Turns a published headword into its canonical IPE lookup key, and splits off a homonym marker — the
    /// SINGLE place that logic lives, so a lexicon built by the converter and a query typed at runtime can
    /// never disagree on how text becomes a key. The runtime query path (DictionaryService) does
    /// <c>StripJoiners → ToLowerInvariant → NFC → Any2Ipe</c>; a headword does the same, after first removing
    /// the things a query never carries (HTML tags, a trailing homonym number).
    /// </summary>
    public static class LexiconKey
    {
        // Zero-width joiner / non-joiner: Any2Ipe classifies them Unknown and passes them through, so a key or
        // query that kept them would match nothing. Same set DictionaryService/MultiWordSearch strip. (SRCH-3)
        private static string StripJoiners(string s) => s.Replace("\u200C", string.Empty).Replace("\u200D", string.Empty);

        private static readonly Regex TagRx = new("<[^>]+>", RegexOptions.Compiled);

        /// <summary>
        /// Reduce a headword to its plain text: strip HTML tags (some upstream sources wrap the name in markup),
        /// then decode HTML entities. Decoding AFTER the tag strip is deliberate — an entity-escaped headword
        /// like <c>N&amp;#257;ga</c> must become <c>Nāga</c> (else its key contains literal <c>&amp;#257;</c> and
        /// the entry is permanently unfindable), while genuine escaped text (<c>a &amp;lt; b</c>) is left as the
        /// literal it denotes rather than being re-interpreted as a tag. (fable MED-1)
        /// </summary>
        public static string StripHtml(string headword) =>
            string.IsNullOrEmpty(headword)
                ? headword
                : System.Net.WebUtility.HtmlDecode(TagRx.Replace(headword, string.Empty)).Trim();

        /// <summary>
        /// Split a trailing homonym number off a headword: <c>"Nāgita 1"</c> → (<c>"Nāgita"</c>, 1);
        /// <c>"Sāvatthī"</c> → (<c>"Sāvatthī"</c>, 0). A homonym marker is a final space-separated token of
        /// digits ONLY. Not split: an interior number, a token with non-digits, or a DOTTED sub-homonym like
        /// DPD's <c>"dhamma 1.01"</c> — this deliberately differs from <c>SqliteLemmaProvider.StripHomonym</c>
        /// (which allows dots) because the proper-name/import sources this serves publish plain integer markers;
        /// a dotted DPD-shaped source would keep its whole headword (harmless — still findable by prefix).
        /// </summary>
        public static (string Base, int Homonym) SplitHomonym(string headword)
        {
            if (string.IsNullOrEmpty(headword)) return (headword, 0);
            int sp = headword.LastIndexOf(' ');
            if (sp <= 0 || sp + 1 >= headword.Length) return (headword, 0);
            for (int i = sp + 1; i < headword.Length; i++)
                if (!char.IsDigit(headword[i])) return (headword, 0);
            // TryParse, not Parse: an absurdly long all-digit run overflows int and falls back to (whole, 0).
            var tail = headword[(sp + 1)..];
            return int.TryParse(tail, NumberStyles.None, CultureInfo.InvariantCulture, out int n)
                ? (headword[..sp].TrimEnd(), n)
                : (headword, 0);
        }

        /// <summary>
        /// The IPE lookup key for a headword base (homonym + HTML already removed). Mirrors the runtime query
        /// normalization exactly so keys and queries collate identically.
        /// </summary>
        public static string DeriveKey(string headwordBase) =>
            Any2Ipe.Convert(StripJoiners(headwordBase).ToLowerInvariant().Normalize(NormalizationForm.FormC));

        /// <summary>
        /// Normalize a runtime query the same way DictionaryService does, to compare against stored keys. No
        /// HTML/homonym stripping — a query never carries those.
        /// </summary>
        public static string DeriveQueryKey(string query) =>
            string.IsNullOrEmpty(query)
                ? string.Empty
                : Any2Ipe.Convert(StripJoiners(query).ToLowerInvariant().Normalize(NormalizationForm.FormC));
    }
}
