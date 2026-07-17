using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Conversion;
using CST.Lemma;

namespace CST.Avalonia.Services;

/// <summary>
/// Orchestrates lemma search: DPD-lemma provider (form↔lemma) + the existing Regex search path.
/// Forward expansion builds an IAST anchored alternation (<c>^(f1|f2|…)$</c>) and submits it as a Regex
/// query; <see cref="SearchService"/> converts it to IPE via <c>Any2Ipe</c> (which preserves the regex
/// metacharacters), matches the index, and returns the attested forms with their counts. DPD supplies the
/// candidate forms; the corpus decides which are real (count &gt; 0).
/// </summary>
public sealed class LemmaSearchService : ILemmaSearchService
{
    // Bound the alternation we hand the search engine (a full derived_from family can be large). The search
    // path has its own expansion cap too; this just keeps the regex sane.
    private const int MaxAlternationForms = 2000;

    private readonly ILemmaProvider _lemma;
    private readonly ISearchService _search;

    public LemmaSearchService(ILemmaProvider lemma, ISearchService search)
    {
        _lemma = lemma;
        _search = search;
    }

    public bool IsAvailable => _lemma.IsAvailable;

    public DpdLemmaMeta? Meta => _lemma.Meta;

    public FormResolution? ResolveWord(string word, Script sourceScript = Script.Ipe)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(word)) return null;
        // DPD keys are IAST (Latin); normalize the input for the lookup.
        string iast = sourceScript == Script.Latin ? word : ScriptConverter.Convert(word, sourceScript, Script.Latin);
        return _lemma.ResolveForm(iast);
    }

    public WordDeconstruction? Deconstruct(string word, Script sourceScript = Script.Ipe)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(word)) return null;
        string iast = sourceScript == Script.Latin ? word : ScriptConverter.Convert(word, sourceScript, Script.Latin);
        var fd = _lemma.Deconstruct(iast);
        if (fd is null) return null;
        return new WordDeconstruction(iast, fd.DirectLemmas, ParseSplits(fd.Deconstructor), _lemma.Meta?.Scope);
    }

    // The DPD deconstructor is a JSON array of ranked alternative splits, each a " + "-joined string
    // (e.g. ["sattha + kosa + karaṇatthāya", "satthako + usa + karaṇatthāya"]). Project to ranked WordSplits
    // (array order IS the rank; element 0 is DPD's best). Malformed JSON degrades to what parsed so far.
    private static IReadOnlyList<WordSplit> ParseSplits(string? deconJson)
    {
        if (string.IsNullOrWhiteSpace(deconJson)) return Array.Empty<WordSplit>();
        var splits = new List<WordSplit>();
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(deconJson);
            if (doc.RootElement.ValueKind != System.Text.Json.JsonValueKind.Array) return splits;
            int rank = 0;
            foreach (var el in doc.RootElement.EnumerateArray())
            {
                if (el.ValueKind != System.Text.Json.JsonValueKind.String) continue;
                var parts = el.GetString()!
                    .Split(" + ", StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
                if (parts.Length > 0) splits.Add(new WordSplit(rank++, parts));
            }
        }
        catch (System.Text.Json.JsonException) { /* return what parsed */ }
        return splits;
    }

    public async Task<LemmaSearchResult?> ExpandAndSearchAsync(
        long lemmaId,
        bool includeFamily = false,
        BookFilter? filter = null,
        Script outputScript = Script.Latin,
        CancellationToken ct = default)
    {
        if (!IsAvailable) return null;

        var expansion = _lemma.ExpandLemma(lemmaId, includeFamily);
        if (expansion is null) return null;

        var lemma = new LemmaCandidate(expansion.LemmaId, expansion.Lemma, expansion.Pos, expansion.Gloss, expansion.DerivedFrom);

        // Gather forms: the lemma's own paradigm, plus (when asked) each derived_from family member's forms.
        var forms = new SortedSet<string>(expansion.Forms, StringComparer.Ordinal);
        if (includeFamily && expansion.Family is { } family)
        {
            foreach (var member in family.Where(m => m.LemmaId != lemmaId))
            {
                var fe = _lemma.ExpandLemma(member.LemmaId, includeFamily: false);
                if (fe is not null)
                    foreach (var f in fe.Forms) forms.Add(f);
            }
        }

        return await SearchFormsAsync(lemma, forms, filter, outputScript, ct).ConfigureAwait(false);
    }

    public async Task<LemmaSearchResult?> ExpandAndSearchSetAsync(
        IReadOnlyList<long> lemmaIds, Script outputScript = Script.Latin, CancellationToken ct = default)
    {
        if (!IsAvailable || lemmaIds.Count == 0) return null;
        // Union the forms of several lemmas (typically the homonyms of ONE headword) and search them ONCE — so a
        // collapsed family row counts the shared surface forms a single time, not once per homonym. (#247 family)
        LemmaCandidate? lemma = null;
        var forms = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var id in lemmaIds)
        {
            var e = _lemma.ExpandLemma(id, includeFamily: false);
            if (e is null) continue;
            lemma ??= new LemmaCandidate(e.LemmaId, e.Lemma, e.Pos, e.Gloss, e.DerivedFrom);
            foreach (var f in e.Forms) forms.Add(f);
        }
        return lemma is null ? null
            : await SearchFormsAsync(lemma, forms, null, outputScript, ct).ConfigureAwait(false);
    }

    // Search a de-duplicated form set as one anchored IAST alternation (converted to IPE by the search path)
    // and project the attested terms with corpus counts.
    private async Task<LemmaSearchResult> SearchFormsAsync(
        LemmaCandidate lemma, SortedSet<string> forms, BookFilter? filter, Script outputScript, CancellationToken ct)
    {
        if (forms.Count == 0)
            return new LemmaSearchResult(lemma, Array.Empty<LemmaSearchForm>(), 0, 0, 0, false);

        bool capped = forms.Count > MaxAlternationForms;
        var searchForms = capped ? forms.Take(MaxAlternationForms).ToList() : forms.ToList();

        string alternation = "^(" + string.Join("|", searchForms) + ")$";
        var query = new SearchQuery
        {
            QueryText = alternation,
            Mode = SearchMode.Regex,
            CountsOnly = true,
            Filter = filter ?? new BookFilter(),
            PageSize = Math.Max(searchForms.Count, 1),
            Skip = 0,
        };

        var sr = await _search.SearchAsync(query, ct).ConfigureAwait(false);

        var attested = sr.Terms
            .Select(t => new LemmaSearchForm(
                t.Term,
                outputScript == Script.Ipe ? t.Term : ScriptConverter.Convert(t.Term, Script.Ipe, outputScript),
                t.TotalCount,
                t.BookCount))
            .OrderByDescending(f => f.Count)
            .ToList();

        return new LemmaSearchResult(
            lemma,
            attested,
            sr.TotalOccurrenceCount,
            CandidateFormCount: searchForms.Count,
            AttestedFormCount: attested.Count,
            ExpansionCapped: capped || sr.ExpansionCapped);
    }
}
