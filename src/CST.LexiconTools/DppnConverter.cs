using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using CST.Lexicon;

namespace CST.LexiconTools
{
    /// <summary>
    /// Converts the Dictionary of Pāli Proper Names (DPPN, Malalasekera / rev. Ānandajoti 2025) source
    /// <c>DPPN.json</c> into a canonical <see cref="CST.Lexicon"/> asset.
    ///
    /// <para>The source is a JSON array of <c>{ "name", "entry" }</c> where <c>name</c> is a formatted heading
    /// (the lemma in the first <c>&lt;b&gt;</c>, an optional bare homonym number after it, citations in
    /// <c>&lt;abbr&gt;</c>, all in malformed markup) and <c>entry</c> is the definition HTML. Trusted, fixed,
    /// public-domain input, so the body is reduced to a closed allowlist by a build-time filter rather than a
    /// full XSS sanitizer.</para>
    /// </summary>
    public static class DppnConverter
    {
        // The lemma is the FIRST <b>…</b> run; verified across all 13,642 records to be the headword.
        private static readonly Regex FirstBold = new("<b>(.*?)</b>", RegexOptions.Singleline | RegexOptions.Compiled);
        // A bare integer after a </b> in the heading is the homonym marker ("…</b> 01."), optionally a RANGE
        // ("…</b> 05-06." — one entry covering homonyms 5–6). NOTE this matches a number after ANY </b>, not
        // only the first: the sole multi-bold+number headings in DPPN are alternative-title homonyms
        // ("<b>Uttiyasutta</b> or <b>Uttikasutta</b> 02."), where the trailing number is the real homonym and
        // matching the later </b> is correct. (fable L1)
        private static readonly Regex HomonymAfterBold =
            new("</b>\\s*0*(\\d+)(?:-0*(\\d+))?", RegexOptions.Compiled);
        private static readonly Regex AnyTag = new("<[^>]*>", RegexOptions.Compiled);

        /// <summary>
        /// Extract the published headword and homonym number from a DPPN <c>name</c> heading. The homonym (if
        /// any) is folded back into the returned headword as " N" so the lexicon builder splits it the same way
        /// it does for every other source. Returns an empty headword when there is no bold lemma.
        /// </summary>
        public static string ParseHeadword(string nameHtml)
        {
            if (string.IsNullOrEmpty(nameHtml)) return string.Empty;

            var m = FirstBold.Match(nameHtml);
            if (!m.Success) return string.Empty;

            // The bold content itself can carry inner markup/entities — reduce to plain text.
            string lemma = WebUtility.HtmlDecode(AnyTag.Replace(m.Value, string.Empty)).Trim();
            if (lemma.Length == 0) return string.Empty;

            // A homonym number (or range) after the lemma's </b>. Search from the first </b> onward.
            string tail = nameHtml[(m.Index + m.Length)..];
            var h = HomonymAfterBold.Match("</b>" + tail);
            if (!h.Success || !int.TryParse(h.Groups[1].Value, out int n) || n <= 0)
                return lemma;
            // A range ("05-06") keeps both numbers in the display; the lexicon builder's homonym split takes the
            // first as the sort number and derives the key from the lemma alone. (fable L2)
            return h.Groups[2].Success && int.TryParse(h.Groups[2].Value, out int m2)
                ? $"{lemma} {n}-{m2}"
                : $"{lemma} {n}";
        }

        /// <summary>The tags a definition body may keep — DPPN uses only these; everything else is dropped
        /// (its text content preserved). No attributes survive, so nothing external, scripted, or styled can.</summary>
        private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
        { "p", "br", "i", "b", "em", "strong", "ul", "ol", "li", "hr", "abbr" };

        // No "\s*" after "<": per the HTML spec a "<" followed by whitespace is TEXT, not a tag-open, so
        // "a < b" must not be read as a "<b>" tag. (fable M1)
        private static readonly Regex TagToken = new("<(/?)([a-zA-Z0-9]+)[^>]*?(/?)>", RegexOptions.Compiled);

        /// <summary>
        /// Reduce a definition body to the closed allowlist, SAFE BY CONSTRUCTION: an allowed tag is emitted as
        /// its bare form (no attributes — so no href/style/on*/class/title survives), any other tag is dropped,
        /// and every character of the text BETWEEN tags has its stray <c>&lt;</c>/<c>&gt;</c> escaped. That last
        /// step is what makes the guarantee a property of the code rather than of the corpus: after cleaning,
        /// the ONLY <c>&lt;</c>/<c>&gt;</c> in the output are the bare allowed tags this method wrote, so no
        /// dropped-tag remnant, HTML comment, dangling partial tag, or split-tag reassembly can form live markup
        /// in the WebView. Trusted build-time input, so this is a normalize-to-our-tag-set filter, not an
        /// adversarial sanitizer — but it is now demonstrably closed. (fable M2)
        /// </summary>
        public static string CleanBody(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var sb = new StringBuilder(html.Length);
            int pos = 0;
            foreach (Match t in TagToken.Matches(html))
            {
                AppendEscaped(sb, html, pos, t.Index);        // text before this tag, with stray <> escaped
                pos = t.Index + t.Length;
                string name = t.Groups[2].Value;
                if (!AllowedTags.Contains(name)) continue;    // drop the tag, keep surrounding (escaped) text
                bool closing = t.Groups[1].Value == "/";
                bool selfClose = t.Groups[3].Value == "/" || name.Equals("br", StringComparison.OrdinalIgnoreCase)
                                 || name.Equals("hr", StringComparison.OrdinalIgnoreCase);
                sb.Append(closing ? $"</{name.ToLowerInvariant()}>"
                        : selfClose ? $"<{name.ToLowerInvariant()}/>"
                        : $"<{name.ToLowerInvariant()}>");
            }
            AppendEscaped(sb, html, pos, html.Length);        // trailing text
            return sb.ToString().Trim();
        }

        // Append html[start..end], turning a stray '<' or '>' into an entity so text can never form or complete
        // a tag. Existing entities (&lt; etc.) contain no '<'/'>' char, so they pass through untouched.
        private static void AppendEscaped(StringBuilder sb, string html, int start, int end)
        {
            for (int i = start; i < end; i++)
            {
                char ch = html[i];
                sb.Append(ch == '<' ? "&lt;" : ch == '>' ? "&gt;" : ch.ToString());
            }
        }

        /// <summary>
        /// Build-time post-condition: a cleaned body must contain no markup outside the bare allowlist. Stripping
        /// the allowed bare tags must leave zero <c>&lt;</c>/<c>&gt;</c>. Throws otherwise, so a future CleanBody
        /// regression fails the build rather than shipping unsafe HTML. (fable M2)
        /// </summary>
        public static bool IsClosedAllowlist(string cleanedBody)
        {
            string stripped = AllowedBareTag.Replace(cleanedBody, string.Empty);
            return !stripped.Contains('<') && !stripped.Contains('>');
        }

        private static readonly Regex AllowedBareTag =
            new("</?(?:p|br|i|b|em|strong|ul|ol|li|hr|abbr)/?>", RegexOptions.Compiled);

        /// <summary>
        /// Map the DPPN source JSON to canonical raw entries. Skips records with no bold lemma or an effectively
        /// empty body (DPPN's section headers, e.g. "A-An" whose entry is just "&lt;/p&gt;").
        /// </summary>
        public static IEnumerable<RawEntry> ToEntries(string dppnJson)
        {
            using var doc = JsonDocument.Parse(dppnJson);
            foreach (var rec in doc.RootElement.EnumerateArray())
            {
                string name = rec.TryGetProperty("name", out var n) ? n.GetString() ?? string.Empty : string.Empty;
                string entry = rec.TryGetProperty("entry", out var e) ? e.GetString() ?? string.Empty : string.Empty;

                string headword = ParseHeadword(name);
                if (headword.Length == 0) continue;

                string body = CleanBody(entry);
                // A body that is empty once tags AND entities are removed is a header stub, not a definition
                // (decode so "&nbsp;</p>" also counts as empty). (fable L3)
                if (WebUtility.HtmlDecode(AnyTag.Replace(body, string.Empty)).Trim().Length == 0) continue;

                // Safety post-condition: never emit a body with markup outside the closed allowlist. (fable M2)
                if (!IsClosedAllowlist(body))
                    throw new InvalidOperationException(
                        $"CleanBody produced non-allowlisted markup for '{headword}': {body}");

                yield return new RawEntry(headword, body);
            }
        }

        /// <summary>The DPPN source metadata (attribution + version stamps) for the built lexicon.</summary>
        public static LexiconMeta Meta(string sourceVersion) => new(
            SourceId: "dppn",
            DisplayName: "DPPN",
            DefinitionLanguage: "en",
            Kind: LexiconKind.ProperNames,
            Title: "Dictionary of Pāli Proper Names",
            Author: "G. P. Malalasekera",
            Reviser: "Ānandajoti Bhikkhu",
            Year: "2025",
            Publisher: null,
            License: "public-domain",
            Url: "https://ancient-buddhist-texts.net/Textual-Studies/DPPN/index.htm",
            SourceVersion: sourceVersion,
            ConverterVersion: 1);

        /// <summary>Read <paramref name="jsonPath"/>, build a lexicon at <paramref name="dbPath"/>, return the
        /// entry count.</summary>
        public static int BuildLexicon(string jsonPath, string dbPath, string sourceVersion)
        {
            string json = System.IO.File.ReadAllText(jsonPath);
            var entries = ToEntries(json).ToList();
            return LexiconBuilder.Build(dbPath, Meta(sourceVersion), entries);
        }
    }
}
