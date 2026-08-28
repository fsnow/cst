using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Search;
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

    /// <summary>
    /// The IAST key to look a word up by. DPD stores forms lower-case NFC, and the lookup is a byte-exact
    /// SQL comparison under BINARY collation — so anything this misses is reported as "no lemma resolves this
    /// form", which is indistinguishable from a word DPD genuinely does not cover. (#868)
    ///
    /// <para>The Latin branch used to hand the word straight through, on the reasoning that Latin input is
    /// already IAST. It is not already <b>normalized</b> IAST: <c>Dhammā</c> — sentence-initial Pāli, or any
    /// caller echoing a capitalised form — resolved nothing where <c>dhammā</c> resolved seventeen ways, and
    /// NFD-decomposed input missed the same way. Latin is the REST and MCP default, so that was the common
    /// path, not an exotic one.</para>
    ///
    /// <para>Normalizing BOTH branches through one method is the point: the two used to differ, and a
    /// per-branch fix would have left them free to differ again.</para>
    ///
    /// <para><b>Order matters, and not in the obvious way.</b> Case-folding must happen AFTER conversion,
    /// never before. <see cref="Script.Ipe"/> is this interface's declared default, and IPE encodes Pāli
    /// letters in the Latin-1 <i>uppercase</i> block from U+00C0 (<c>Ipe2Latn</c>) — so
    /// <c>ToLowerInvariant</c> on an IPE string shifts every letter 0x20 onto a <i>different IPE letter</i>
    /// or onto nothing: IPE "dhammā" case-folded before conversion reads out as "übhmmm". An earlier draft
    /// of this method folded first, mirroring <c>DpdDictionarySource</c>; that sibling folds <i>user text</i>
    /// on its way INTO IPE and never folds IPE itself, so the resemblance was misleading. (fable review)</para>
    ///
    /// <para>The Latin branch round-trips through IPE rather than being handed straight to the lookup, so it
    /// picks up the foldings every other Latin-input path already gets — notably <c>ṁ</c> (U+1E41) and
    /// <c>ṃ</c> (U+1E43), which <c>Latn2Ipe</c> folds together as niggahita. Without it, <c>dhammaṁ</c>
    /// resolves in the dictionary panel and reports "no lemma resolves this form" here.</para>
    /// </summary>
    private static string ToLookupKey(string word, Script sourceScript)
    {
        // Joiners and surrounding space first. Every converter already drops ZWJ/ZWNJ, so stripping them
        // here only changes the Latin branch — but doing it once keeps the two branches identical.
        var stripped = MultiWordSearch.StripJoiners(word).Trim();

        var latin = sourceScript == Script.Latin
            ? stripped
            : ScriptConverter.Convert(stripped, sourceScript, Script.Latin);

        // Now it is Latin, so folding is safe. Fold before Any2Ipe: Latn2Ipe maps lower-case forms.
        latin = latin.ToLowerInvariant();

        return ScriptConverter.Convert(Any2Ipe.Convert(latin), Script.Ipe, Script.Latin)
            .Normalize(NormalizationForm.FormC);
    }

    public FormResolution? ResolveWord(string word, Script sourceScript = Script.Ipe)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(word)) return null;
        return _lemma.ResolveForm(ToLookupKey(word, sourceScript));
    }

    public WordDeconstruction? Deconstruct(string word, Script sourceScript = Script.Ipe)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(word)) return null;
        string iast = ToLookupKey(word, sourceScript);
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
        CancellationToken ct = default,
        bool includeRelated = true)
    {
        if (!IsAvailable) return null;

        // Fetch the family iff we need it — either to UNION its forms (family:true) or to REPORT its members as
        // `relatedLemmas` metadata (includeRelated, the default: a verb's participles/absolutive are their OWN
        // lemmaIds, so this is how a client discovers them and scopes "the conjugation" without over-shooting to
        // the whole family). When neither is needed (e.g. the report's focus-count call), skip the family query. (#247)
        bool needFamily = includeFamily || includeRelated;
        var expansion = _lemma.ExpandLemma(lemmaId, includeFamily: needFamily);
        if (expansion is null) return null;

        var lemma = new LemmaCandidate(expansion.LemmaId, expansion.Lemma, expansion.Pos, expansion.Gloss, expansion.DerivedFrom);
        var siblings = expansion.Family?.Where(m => m.LemmaId != lemmaId).ToList() ?? new List<LemmaCandidate>();

        // Gather forms: the lemma's own paradigm, plus — only when family:true — each family member's forms UNIONed.
        var forms = new SortedSet<string>(expansion.Forms, StringComparer.Ordinal);
        if (includeFamily)
        {
            foreach (var member in siblings)
            {
                var fe = _lemma.ExpandLemma(member.LemmaId, includeFamily: false);
                if (fe is not null)
                    foreach (var f in fe.Forms) forms.Add(f);
            }
        }

        var related = includeRelated ? (IReadOnlyList<LemmaCandidate>)siblings : Array.Empty<LemmaCandidate>();
        return await SearchFormsAsync(lemma, forms, related, filter, outputScript, ct).ConfigureAwait(false);
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
            : await SearchFormsAsync(lemma, forms, Array.Empty<LemmaCandidate>(), null, outputScript, ct).ConfigureAwait(false);
    }

    // Search a de-duplicated form set as one anchored IAST alternation (converted to IPE by the search path)
    // and project the attested terms with corpus counts. `relatedLemmas` is carried through as metadata.
    private async Task<LemmaSearchResult> SearchFormsAsync(
        LemmaCandidate lemma, SortedSet<string> forms, IReadOnlyList<LemmaCandidate> relatedLemmas,
        BookFilter? filter, Script outputScript, CancellationToken ct)
    {
        if (forms.Count == 0)
            return new LemmaSearchResult(lemma, Array.Empty<LemmaSearchForm>(), 0, 0, 0, false, relatedLemmas);

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
            ExpansionCapped: capped || sr.ExpansionCapped,
            RelatedLemmas: relatedLemmas);
    }
}
