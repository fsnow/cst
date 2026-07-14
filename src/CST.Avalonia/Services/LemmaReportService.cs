using System.Collections.Generic;
using System.Linq;
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

        // Focus paradigm — forms with corpus counts (in IPE). Flag each form that is a homograph.
        var focus = await _search.ExpandAndSearchAsync(lemmaId, includeFamily: false, filter: null, outputScript: Script.Ipe, ct: ct)
            .ConfigureAwait(false);
        var forms = new List<ReportForm>();
        string? homographForm = null;
        int homographCount = -1;
        var homoSenses = new List<ReportHomographSense>();
        if (focus is not null)
        {
            foreach (var f in focus.AttestedForms)
            {
                var res = _lemma.ResolveForm(ScriptConverter.Convert(f.Ipe, Script.Ipe, Script.Latin));
                bool homo = res is not null && res.Candidates.Count > 1;
                forms.Add(new ReportForm(f.Ipe, f.Count, f.BookCount, homo));
                if (homo && f.Count > homographCount)
                {
                    homographCount = f.Count;
                    homographForm = f.Ipe;
                    homoSenses = res!.Candidates
                        .Select(c => new ReportHomographSense(c.LemmaId, ToIpe(c.Lemma), c.Pos, c.Gloss, ToIpeOrNull(c.DerivedFrom)))
                        .ToList();
                }
            }
        }

        // Word family — derived_from siblings, each with its own corpus total, grouped by pos category.
        var family = new List<ReportFamilyMember>();
        var exp = _lemma.ExpandLemma(lemmaId, includeFamily: true);
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
            ToIpeOrNull(d.Construction), ToIpeOrNull(d.Sanskrit), d.Pattern, MapRoot(d.Root), d.EbtCount,
            forms, focus?.TotalOccurrences ?? 0, focus?.AttestedFormCount ?? 0, focus?.CandidateFormCount ?? 0, focus?.ExpansionCapped ?? false,
            ToIpeOrNull(d.Example), d.ExampleSource, d.ExampleSutta,
            family, famAll?.TotalOccurrences ?? 0, famAll?.AttestedFormCount ?? 0,
            homographForm, homoSenses,
            meta?.DpdVersion ?? string.Empty, meta?.Attribution ?? string.Empty);
    }

    private static ReportRoot? MapRoot(RootDetail? r) => r is null ? null
        : new ReportRoot(ToIpe(r.RootKey), r.RootMeaning, r.RootGroup, ToIpeOrNull(r.SanskritRoot), ToIpeOrNull(r.DhatupathaPali), r.DhatupathaEnglish);

    private static string ToIpe(string iast) => Any2Ipe.Convert(iast);
    private static string? ToIpeOrNull(string? iast) => string.IsNullOrEmpty(iast) ? iast : Any2Ipe.Convert(iast!);

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
