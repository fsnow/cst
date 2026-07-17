using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Search;
using CST.Conversion;
using CST.Lemma;
using CST.Tools;

namespace CST.Avalonia.Services.Tools
{
    /// <summary>
    /// An <see cref="IDictionaryTool"/> that unions the flat-file dictionaries with the Digital Pāḷi Dictionary
    /// (DPD) as the language <c>"dpd"</c>, present only when the DPD-lemma asset is installed. DPD ships no
    /// rendered-HTML definitions, so entries are COMPOSED on the fly from the asset's structured columns; the
    /// word→entry key reuses the same form→lemma index the lemma search uses (so an inflected word resolves).
    /// Every language except <c>"dpd"</c> delegates unchanged to the flat-file tool, so the wire contract in
    /// <see cref="IDictionaryTool"/> is untouched. Degrades to "flat-file only" when the asset is absent. (#109)
    /// </summary>
    public sealed class CompositeDictionaryTool : IDictionaryTool
    {
        /// <summary>The language code that routes to DPD rather than a flat-file dictionary.</summary>
        public const string DpdLanguage = "dpd";

        // Upper bound on hits per call — mirror the flat-file DictionaryTool clamp (#305).
        private const int MaxDictEntries = 500;

        private readonly IDictionaryTool _flat;
        private readonly ILemmaProvider _lemma;

        public CompositeDictionaryTool(IDictionaryTool flat, ILemmaProvider lemma)
        {
            _flat = flat;
            _lemma = lemma;
        }

        public IReadOnlyList<DictionaryLanguageInfo> Languages
        {
            get
            {
                // "dpd" is a RESERVED code that always routes to the DPD path, so drop any flat-file language of
                // that name from the union — else it would double-list and (since routing shadows it) be unreachable.
                var list = _flat.Languages.Where(l => !IsDpd(l.Language)).ToList();
                // Advertise DPD only when the asset is loaded; cite the shipped release from its meta (never guessed).
                if (_lemma.IsAvailable)
                    list.Add(new DictionaryLanguageInfo(DpdLanguage, DpdSource()));
                return list;
            }
        }

        public Task<IReadOnlyList<DictionaryEntry>> LookupAsync(DictionaryRequest request, CancellationToken ct = default)
            => IsDpd(request.Language)
                // The DPD path is blocking SQLite (a ResolveForm + up to `cap` GetDetail round-trips); offload it so
                // it doesn't hold the Kestrel request thread the way the genuinely-async flat path doesn't. (#279)
                ? Task.Run(() => LookupDpd(request, ct), ct)
                : _flat.LookupAsync(request, ct);

        private static bool IsDpd(string? language) => string.Equals(language, DpdLanguage, StringComparison.OrdinalIgnoreCase);

        // DPD lookup: normalize the (any-script) query to the IAST key DPD stores, resolve it to candidate lemmas
        // via the form→lemma index, and compose a compact entry per candidate from the asset's structured fields.
        private IReadOnlyList<DictionaryEntry> LookupDpd(DictionaryRequest request, CancellationToken ct)
        {
            ct.ThrowIfCancellationRequested();
            if (!_lemma.IsAvailable || string.IsNullOrWhiteSpace(request.Query))
                return Array.Empty<DictionaryEntry>();

            // Mirror DictionaryService's query normalization (strip zero-width joiners, lower-case, NFC), then any
            // script → IPE (Any2Ipe does the detection the flat loader relies on) → IAST, DPD's storage script.
            var normalized = MultiWordSearch.StripJoiners(request.Query).ToLowerInvariant().Normalize(NormalizationForm.FormC);
            string ipe = Any2Ipe.Convert(normalized);
            string iast = ScriptConverter.Convert(ipe, Script.Ipe, Script.Latin);

            var resolution = _lemma.ResolveForm(iast);
            if (resolution is null || resolution.Candidates.Count == 0)
                return Array.Empty<DictionaryEntry>();

            var source = DpdSource()?.Title;
            int cap = Math.Clamp(request.MaxEntries, 0, MaxDictEntries);
            var entries = new List<DictionaryEntry>(Math.Min(cap, resolution.Candidates.Count));
            foreach (var cand in resolution.Candidates)
            {
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

        // A COMPACT entry composed from DPD's structured fields — pos, the (meaning_2-coalesced) gloss, the
        // literal meaning, and the construction/etymology. Deliberately NOT the full dossier: an agent chains
        // LemmaId → /v1/lemma-report/{lemmaId} for paradigm/family/root/frequency. Fields are plain text →
        // HTML-encoded (only the composed markup is HTML).
        private static string ComposeMeaning(LemmaDetail d)
        {
            var sb = new StringBuilder();
            if (!string.IsNullOrEmpty(d.Pos)) sb.Append("<i>").Append(Enc(d.Pos)).Append("</i> ");
            if (!string.IsNullOrEmpty(d.Gloss)) sb.Append(Enc(d.Gloss));
            if (!string.IsNullOrEmpty(d.MeaningLit))
                sb.Append(" <span class=\"dpd-lit\">(lit. ").Append(Enc(d.MeaningLit)).Append(")</span>");
            if (!string.IsNullOrEmpty(d.Construction))
                sb.Append("<div class=\"dpd-con\">").Append(Enc(d.Construction)).Append("</div>");
            return sb.ToString();
        }

        // Escape ONLY the HTML-significant characters, leaving Pāli diacritics (ā, ñ, √, …) as literal UTF-8 —
        // WebUtility.HtmlEncode would mangle every non-ASCII letter into a numeric entity, which is unreadable
        // for a Pāli dictionary. The fields are element text, so &/</> is sufficient. (& must be replaced first.)
        private static string Enc(string s) => s
            .Replace("&", "&amp;").Replace("<", "&lt;").Replace(">", "&gt;");

        // The lemma is stored IAST; project to the requested output script. IPE/Unknown never leaks — the stored
        // form is IAST, and for those output scripts we return it as-is (Latin) rather than the internal encoding.
        private static string ToScript(string iast, Script output)
            => output is Script.Latin or Script.Ipe or Script.Unknown
                ? iast
                : ScriptConverter.Convert(iast, Script.Latin, output);

        // Build DPD's citation from the asset's meta (written by DpdLemmaBuilder) — never hard-coded, so it tracks
        // the shipped release. Null when the asset carries no attribution at all (mirrors the flat-file contract).
        private DictionarySourceInfo? DpdSource()
        {
            var m = _lemma.Meta;
            if (m is null) return null;
            var info = new DictionarySourceInfo(
                Title: NullIfBlank(m.Source),
                Compiler: NullIfBlank(m.Author),
                Edition: NullIfBlank(m.DpdVersion),
                Year: YearFromVersion(m.DpdVersion),
                Publisher: null,                       // not recorded in the asset — don't invent one
                License: NullIfBlank(m.License),
                Url: NullIfBlank(m.Homepage));
            return IsUnattributed(info) ? null : info;
        }

        private static string? NullIfBlank(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

        private static bool IsUnattributed(DictionarySourceInfo s) =>
            s.Title is null && s.Compiler is null && s.Edition is null && s.Year is null &&
            s.Publisher is null && s.License is null && s.Url is null;

        // DPD versions look like "v0.4.20260531"; take the year from the first >=8-digit (YYYYMMDD) run. Null if none.
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
