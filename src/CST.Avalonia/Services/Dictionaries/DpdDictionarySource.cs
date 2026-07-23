using System;
using System.Collections.Generic;
using System.Text;
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
    /// </summary>
    public sealed class DpdDictionarySource : IDictionarySource
    {
        public const string SourceId = "dpd";
        private const int MaxDictEntries = 500;

        private readonly ILemmaProvider _lemma;

        public DpdDictionarySource(ILemmaProvider lemma) => _lemma = lemma;

        public string Id => SourceId;
        public string DisplayName => "DPD";
        public string DefinitionLanguage => "en";
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
