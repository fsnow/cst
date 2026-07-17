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
                string? grammar = GrammarFor(res?.Grammar, focusHeadword, d.Pos);
                // No direct grammar? An enclitic form (e.g. pajānātīti = pajānāti + iti) decomposes to a base
                // form whose grammar IS known — resolve it and append the enclitic. (#247 Phase 2)
                if (grammar is null && res?.Deconstructor is { } decon)
                    grammar = EncliticGrammar(decon, focusHeadword, d.Pos);
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

        // Word family — the derived_from cluster, COLLAPSED so each distinct WORD (headword) is ONE row, not one
        // per DPD homonym. A headword's homonyms share their surface forms, so their union is counted ONCE (not
        // summed, which would double-count); the row is grouped by the homonyms' dominant part-of-speech, so a
        // word like paññāṇa (adj + nt homonyms) is a single row, not split across buckets. (#247 family)
        var family = new List<ReportFamilyMember>();
        if (exp?.Family is { } siblings)
        {
            foreach (var g in siblings.Where(m => m.LemmaId != lemmaId)
                                      .GroupBy(m => StripHomonym(m.Lemma), StringComparer.Ordinal))
            {
                var members = g.ToList();
                // dominant pos = the most common among the homonyms; the representative sense is the first
                // homonym of that pos.
                var repPos = members.GroupBy(m => m.Pos)
                    .OrderByDescending(x => x.Count()).ThenBy(x => x.Key, StringComparer.Ordinal).First().Key;
                var rep = members.FirstOrDefault(m => m.Pos == repPos) ?? members[0];
                var mr = await _search.ExpandAndSearchSetAsync(members.Select(m => m.LemmaId).ToList(), Script.Ipe, ct)
                    .ConfigureAwait(false);
                family.Add(new ReportFamilyMember(
                    rep.LemmaId, ToIpe(g.Key), rep.Pos, rep.Gloss, mr?.TotalOccurrences ?? 0, PosGroup(repPos)));
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
    private static string? GrammarFor(string? grammarJson, string focusHeadword, string? focusPos)
    {
        if (string.IsNullOrEmpty(grammarJson)) return null;
        var focusBucket = PosBucket(focusPos);
        List<string>? outp = null;
        try
        {
            using var doc = JsonDocument.Parse(grammarJson!);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var triple in doc.RootElement.EnumerateArray())
            {
                if (triple.ValueKind != JsonValueKind.Array || triple.GetArrayLength() < 3) continue;
                // Guard against a future DPD shape where an element isn't a string (GetString would throw
                // InvalidOperationException, not JsonException) — degrade to a blank cell, never crash.
                if (triple[0].ValueKind != JsonValueKind.String || triple[2].ValueKind != JsonValueKind.String) continue;
                if (triple[0].GetString() != focusHeadword) continue;
                // A surface form can analyse under one headword as different parts of speech (noun vs adj);
                // keep only the analyses matching the focus lemma's broad category, so a masculine noun's
                // paradigm doesn't show the adjective's "feminine nominative singular".
                if (focusBucket is not null && PosBucket(triple[1].GetString()) is { } tb && tb != focusBucket) continue;
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

    // Coarse part-of-speech bucket shared by lemma pos ('masc'/'pr'/…) and DPD grammar-triple pos
    // ('noun'/'adj'/'verb'/…). Returns null for anything we shouldn't discriminate on (participles,
    // cardinals, pronouns…), so those analyses are never wrongly filtered out.
    private static string? PosBucket(string? pos) => pos switch
    {
        "masc" or "fem" or "nt" or "noun" => "noun",
        "adj" => "adj",
        "pr" or "aor" or "fut" or "opt" or "imp" or "cond" or "imperf" or "perf" or "verb" => "verb",
        _ => null,
    };

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

    // An enclitic form (pajānātīti) carries no direct grammar; its deconstructor gives "base + enclitic"
    // (e.g. "pajānāti + iti"). Resolve the BASE form's grammar (filtered to THIS lemma) and append the enclitic,
    // yielding e.g. "present 3rd singular, + iti". Multiple base analyses join with " / ". (#247 Phase 2)
    private string? EncliticGrammar(string deconJson, string focusHeadword, string? focusPos)
    {
        List<string>? results = null;
        try
        {
            using var doc = JsonDocument.Parse(deconJson);
            if (doc.RootElement.ValueKind != JsonValueKind.Array) return null;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != JsonValueKind.String) continue;
                var (baseForm, enclitic) = SplitEnclitic(el.GetString()!);
                if (baseForm is null) continue;
                var baseGrammar = GrammarFor(_lemma.ResolveForm(baseForm)?.Grammar, focusHeadword, focusPos);
                if (baseGrammar is null) continue;
                var combined = $"{baseGrammar}, + {enclitic}";
                results ??= new List<string>();
                if (!results.Contains(combined)) results.Add(combined);
            }
        }
        catch (JsonException) { return null; }
        return results is { Count: > 0 } ? string.Join(" / ", results) : null;
    }

    // Common Pāli enclitic particles that attach to a fully-inflected base word.
    private static readonly HashSet<string> Enclitics = new(StringComparer.Ordinal)
        { "iti", "ti", "ca", "pi", "api", "eva", "ceva", "va", "hi", "kho", "su", "nu", "no", "ve" };

    // "pajānāti + iti" → ("pajānāti", "iti"), but ONLY for a 2-part split whose tail is a known enclitic
    // (so a multi-word sandhi compound is never mistaken for an enclitic). Else (null, null).
    private static (string? Base, string? Enclitic) SplitEnclitic(string decon)
    {
        var parts = decon.Split(" + ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && Enclitics.Contains(parts[1]) && parts[0].Length > 0
            ? (parts[0], parts[1]) : (null, null);
    }

    // "paññāya 1" → "paññāya"; also DPD's DOTTED sub-numbering "dhamma 1.01" → "dhamma" (mirrors the
    // provider). A trailing token of only digits and dots is a homonym marker; anything else is kept.
    private static string StripHomonym(string lemma)
    {
        int sp = lemma.LastIndexOf(' ');
        if (sp <= 0 || sp + 1 >= lemma.Length) return lemma;
        for (int i = sp + 1; i < lemma.Length; i++)
            if (!char.IsDigit(lemma[i]) && lemma[i] != '.') return lemma;
        return lemma[..sp];
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
