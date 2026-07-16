using System;
using System.Collections.Generic;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Search;
using CST.Conversion;
using CST.Lemma;

namespace CST.Avalonia.Services;

/// <summary>
/// Assembles the structured lemma report (dossier) and caches it. The cached report is script-neutral (all
/// Pāli in IPE), so the script picker is a pure re-render (in the renderer), never a recompute. Assembly does
/// several corpus searches (the focus paradigm + each family sibling's total + the family:true union), which
/// is why the cache matters. Invalidate on a corpus re-index or asset swap.
/// </summary>
public interface ILemmaReportService
{
    bool IsAvailable { get; }
    Task<LemmaReport?> BuildAsync(long lemmaId, CancellationToken ct = default);
    void InvalidateCache();
}

public sealed class LemmaReportService : ILemmaReportService
{
    private const int CacheCapacity = 32;

    private readonly ILemmaProvider _lemma;
    private readonly ILemmaSearchService _search;
    private readonly BoundedCache<long, LemmaReport> _cache = new(CacheCapacity);
    private readonly object _lock = new();

    public LemmaReportService(ILemmaProvider lemma, ILemmaSearchService search)
    {
        _lemma = lemma;
        _search = search;
    }

    public bool IsAvailable => _lemma.IsAvailable;

    public void InvalidateCache() { lock (_lock) _cache.Clear(); }

    public async Task<LemmaReport?> BuildAsync(long lemmaId, CancellationToken ct = default)
    {
        if (!IsAvailable) return null;
        lock (_lock) { if (_cache.TryGet(lemmaId, out var hit) && hit is not null) return hit; }

        var report = await AssembleAsync(lemmaId, ct).ConfigureAwait(false);
        if (report is not null) lock (_lock) _cache.Set(lemmaId, report);
        return report;
    }

    private async Task<LemmaReport?> AssembleAsync(long lemmaId, CancellationToken ct)
    {
        var d = _lemma.GetDetail(lemmaId);
        if (d is null) return null;

        // The focus lemma's word-family (its derived_from cluster). Used to tell a REAL homograph (a form
        // shared with a DIFFERENT word) from a mere in-family overlap — e.g. a form the finite verb shares
        // with its own present participle (pajānantīti = pajānanti+iti OR pajānantī+iti), which is not a
        // meaningful ambiguity.
        var exp = _lemma.ExpandLemma(lemmaId, includeFamily: true);
        var familyIds = new HashSet<long> { lemmaId };
        if (exp?.Family is { } famMembers) foreach (var m in famMembers) familyIds.Add(m.LemmaId);

        // Focus paradigm — forms with corpus counts (in IPE). A form is a homograph only if it also belongs
        // to a lemma OUTSIDE this word-family.
        var focus = await _search.ExpandAndSearchAsync(lemmaId, includeFamily: false, filter: null, outputScript: Script.Ipe, ct: ct)
            .ConfigureAwait(false);
        var forms = new List<ReportForm>();
        var homographs = new List<ReportHomograph>();
        if (focus is not null)
        {
            var focusHeadword = StripHomonym(d.Lemma);
            foreach (var f in focus.AttestedForms)
            {
                var res = _lemma.ResolveForm(ScriptConverter.Convert(f.Ipe, Script.Ipe, Script.Latin));
                bool homo = res is not null && res.Candidates.Any(c => !familyIds.Contains(c.LemmaId));
                string? grammar = GrammarFor(res?.Grammar, focusHeadword);
                forms.Add(new ReportForm(f.Ipe, f.Count, f.BookCount, homo, grammar));
                if (homo)
                {
                    var senses = res!.Candidates
                        .Select(c => new ReportHomographSense(c.LemmaId, ToIpe(c.Lemma), c.Pos, c.Gloss, ToIpeOrNull(c.DerivedFrom)))
                        .ToList();
                    homographs.Add(new ReportHomograph(f.Ipe, f.Count, senses));
                }
            }
        }
        homographs = homographs.OrderByDescending(h => h.Count).ToList();

        // Word family — derived_from siblings, each with its own corpus total, grouped by pos category.
        var family = new List<ReportFamilyMember>();
        if (exp?.Family is { } siblings)
        {
            foreach (var m in siblings)
            {
                if (m.LemmaId == lemmaId) continue;
                var mr = await _search.ExpandAndSearchAsync(m.LemmaId, false, null, Script.Ipe, ct).ConfigureAwait(false);
                family.Add(new ReportFamilyMember(m.LemmaId, ToIpe(m.Lemma), m.Pos, m.Gloss, mr?.TotalOccurrences ?? 0, PosGroup(m.Pos)));
            }
        }
        family = family.OrderByDescending(m => m.TotalOccurrences).ToList();

        // family:true de-duplicated union totals (the whole word-family, counted once).
        var famAll = await _search.ExpandAndSearchAsync(lemmaId, includeFamily: true, null, Script.Ipe, ct).ConfigureAwait(false);

        var meta = _lemma.Meta;
        return new LemmaReport(
            d.LemmaId, ToIpe(d.Lemma), d.Pos, d.Gloss, d.MeaningLit, ToIpeOrNull(d.DerivedFrom),
            ToIpeOrNull(d.Construction), d.Sanskrit, d.Pattern, MapRoot(d.Root), d.EbtCount,
            forms, focus?.TotalOccurrences ?? 0, focus?.AttestedFormCount ?? 0, focus?.CandidateFormCount ?? 0, focus?.ExpansionCapped ?? false,
            ToIpeMarkupOrNull(d.Example), d.ExampleSource, d.ExampleSutta,
            family, famAll?.TotalOccurrences ?? 0, famAll?.AttestedFormCount ?? 0,
            homographs,
            meta?.DpdVersion ?? string.Empty, meta?.Attribution ?? string.Empty);
    }

    // Sanskrit cognates carry characters outside the Pāli IPE inventory (ṛ ṝ ś ṣ …) that Any2Ipe can't
    // round-trip, so Sanskrit stays verbatim IAST (rendered as-is, not script-converted).
    private static ReportRoot? MapRoot(RootDetail? r) => r is null ? null
        : new ReportRoot(ToIpe(r.RootKey), r.RootMeaning, r.RootGroup, r.SanskritRoot, ToIpeOrNull(r.DhatupathaPali), r.DhatupathaEnglish);

    private static string ToIpe(string iast) => Any2Ipe.Convert(iast);
    private static string? ToIpeOrNull(string? iast) => string.IsNullOrEmpty(iast) ? iast : Any2Ipe.Convert(iast!);

    // Like ToIpeOrNull but tag-aware: the DPD example carries <b>…</b> markup. Convert only the text runs
    // to IPE; re-emit the tags literally so they survive the later IPE→script render un-mangled.
    private static string? ToIpeMarkupOrNull(string? iast)
    {
        if (string.IsNullOrEmpty(iast)) return iast;
        var sb = new System.Text.StringBuilder(iast!.Length);
        foreach (var part in System.Text.RegularExpressions.Regex.Split(iast!, "(</?b>)"))
        {
            if (part is "<b>" or "</b>") sb.Append(part);
            else if (part.Length > 0) sb.Append(Any2Ipe.Convert(part));
        }
        return sb.ToString();
    }

    // The form's grammatical analysis restricted to THIS lemma. forms.grammar is a JSON array of
    // [headword, pos, grammar] triples — a single surface form can analyse to several headwords (its own
    // lemma, homographs, participles) — so we keep only the analyses whose headword is the focus lemma's,
    // expand the abbreviations, and join distinct results (a form may be, e.g., both present and imperative).
    private static string? GrammarFor(string? grammarJson, string focusHeadword)
    {
        if (string.IsNullOrEmpty(grammarJson)) return null;
        List<string>? outp = null;
        try
        {
            using var doc = JsonDocument.Parse(grammarJson!);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var triple in doc.RootElement.EnumerateArray())
            {
                if (triple.ValueKind != JsonValueKind.Array || triple.GetArrayLength() < 3) continue;
                if (triple[0].GetString() != focusHeadword) continue;
                var gram = triple[2].GetString();
                if (string.IsNullOrWhiteSpace(gram)) continue;
                var expanded = ExpandGrammar(gram!);
                outp ??= new List<string>();
                if (!outp.Contains(expanded)) outp.Add(expanded);
            }
        }
        catch (JsonException) { return null; }
        return outp is { Count: > 0 } ? string.Join(" / ", outp) : null;
    }

    private static string ExpandGrammar(string gram) =>
        string.Join(' ', gram.Split(' ', StringSplitOptions.RemoveEmptyEntries)
                             .Select(t => GrammarTokens.TryGetValue(t, out var v) ? v : t));

    // DPD grammar abbreviations → full words. Unknown tokens (1st/2nd/3rd, rare tags) pass through as-is.
    private static readonly Dictionary<string, string> GrammarTokens = new()
    {
        ["pr"] = "present", ["fut"] = "future", ["aor"] = "aorist", ["imp"] = "imperative",
        ["opt"] = "optative", ["cond"] = "conditional", ["imperf"] = "imperfect", ["perf"] = "perfect",
        ["reflx"] = "reflexive", ["caus"] = "causative", ["pass"] = "passive", ["denom"] = "denominative",
        ["desid"] = "desiderative", ["intens"] = "intensive",
        ["sg"] = "singular", ["pl"] = "plural", ["dual"] = "dual",
        ["nom"] = "nominative", ["acc"] = "accusative", ["instr"] = "instrumental", ["dat"] = "dative",
        ["abl"] = "ablative", ["gen"] = "genitive", ["loc"] = "locative", ["voc"] = "vocative",
        ["masc"] = "masculine", ["fem"] = "feminine", ["nt"] = "neuter",
    };

    // "paññāya 1" → "paññāya" (strips a trailing homonym number, mirroring the provider).
    private static string StripHomonym(string lemma)
    {
        int sp = lemma.LastIndexOf(' ');
        return sp > 0 && int.TryParse(lemma.AsSpan(sp + 1), out _) ? lemma[..sp] : lemma;
    }

    // Broad pos → family grouping (pos alone can't separate causative/passive from finite verbs — DPD tags
    // both as 'pr' — so this is a coarse-but-defensible three-way split).
    private static string PosGroup(string? pos) => pos switch
    {
        "pr" or "aor" or "fut" or "opt" or "imp" or "cond" or "prp" or "app" or "abs" or "ger" or "inf"
            or "pp" or "ptp" or "fpp" or "caus" or "pass" or "denom" => "Verbs & participles",
        "masc" or "fem" or "nt" => "Nouns",
        _ => "Adjectives & other",
    };
}
