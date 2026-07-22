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
        // A bare integer immediately after that first </b> is the homonym marker ("…</b> 01.").
        private static readonly Regex HomonymAfterBold = new("</b>\\s*0*(\\d+)", RegexOptions.Compiled);
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

            // Look for a homonym number in the tail AFTER the first </b> only (not inside a later bold run).
            string tail = nameHtml[(m.Index + m.Length)..];
            var h = HomonymAfterBold.Match("</b>" + tail);   // re-anchor so the pattern's </b> matches
            return h.Success && int.TryParse(h.Groups[1].Value, out int n) && n > 0
                ? $"{lemma} {n}"
                : lemma;
        }

        /// <summary>The tags a definition body may keep — DPPN uses only these; everything else is dropped
        /// (its text content preserved). No attributes survive, so nothing external, scripted, or styled can.</summary>
        private static readonly HashSet<string> AllowedTags = new(StringComparer.OrdinalIgnoreCase)
        { "p", "br", "i", "b", "em", "strong", "ul", "ol", "li", "hr", "abbr" };

        private static readonly Regex TagToken = new("<(/?)\\s*([a-zA-Z0-9]+)[^>]*?(/?)>", RegexOptions.Compiled);

        /// <summary>
        /// Reduce a definition body to the closed allowlist: keep an allowed tag as its bare form (no
        /// attributes — so no href/style/on*/class/title survives), drop any other tag while keeping the text
        /// between tags. Malformed input is handled token-by-token (each tag stands alone). Trusted build-time
        /// input, so this is a normalize-to-our-tag-set filter, not an adversarial sanitizer.
        /// </summary>
        public static string CleanBody(string html)
        {
            if (string.IsNullOrEmpty(html)) return string.Empty;
            var sb = new StringBuilder(html.Length);
            int pos = 0;
            foreach (Match t in TagToken.Matches(html))
            {
                sb.Append(html, pos, t.Index - pos);          // text before this tag, verbatim
                pos = t.Index + t.Length;
                string name = t.Groups[2].Value;
                if (!AllowedTags.Contains(name)) continue;    // drop the tag, keep surrounding text
                bool closing = t.Groups[1].Value == "/";
                bool selfClose = t.Groups[3].Value == "/" || name.Equals("br", StringComparison.OrdinalIgnoreCase)
                                 || name.Equals("hr", StringComparison.OrdinalIgnoreCase);
                sb.Append(closing ? $"</{name.ToLowerInvariant()}>"
                        : selfClose ? $"<{name.ToLowerInvariant()}/>"
                        : $"<{name.ToLowerInvariant()}>");
            }
            sb.Append(html, pos, html.Length - pos);          // trailing text
            return sb.ToString().Trim();
        }

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
                // A body that is empty once tags are stripped is a header stub, not a definition.
                if (AnyTag.Replace(body, string.Empty).Trim().Length == 0) continue;

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
