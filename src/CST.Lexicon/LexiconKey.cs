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

        /// <summary>Remove HTML tags from a headword (some upstream sources wrap the name in markup), matching
        /// the upstream exporters' <c>&lt;[^&gt;]+&gt;</c> strip. Leaves the text content.</summary>
        public static string StripHtml(string headword) =>
            string.IsNullOrEmpty(headword) ? headword : TagRx.Replace(headword, string.Empty).Trim();

        /// <summary>
        /// Split a trailing homonym number off a headword: <c>"Nāgita 1"</c> → (<c>"Nāgita"</c>, 1);
        /// <c>"Sāvatthī"</c> → (<c>"Sāvatthī"</c>, 0). A homonym marker is a final space-separated token of
        /// digits only (the form the proper-name and DPD sources publish). Not stripped: an interior number, or
        /// a token with non-digits.
        /// </summary>
        public static (string Base, int Homonym) SplitHomonym(string headword)
        {
            if (string.IsNullOrEmpty(headword)) return (headword, 0);
            int sp = headword.LastIndexOf(' ');
            if (sp <= 0 || sp + 1 >= headword.Length) return (headword, 0);
            for (int i = sp + 1; i < headword.Length; i++)
                if (!char.IsDigit(headword[i])) return (headword, 0);
            // int.Parse is safe: the tail is all digits. Clamp absurdly long runs to avoid overflow.
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
