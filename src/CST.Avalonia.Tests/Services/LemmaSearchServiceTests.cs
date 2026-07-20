using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Conversion;
using CST.Lemma;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CST.Avalonia.Tests.Services;

// Orchestration tests for LemmaSearchService: form gathering, alternation building, result mapping.
// Uses a real SqliteLemmaProvider over a tiny fixture + a fake ISearchService that "attests" a subset of
// the alternation forms. outputScript=Ipe throughout so terms pass through without script conversion.
public sealed class LemmaSearchServiceTests : IDisposable
{
    private readonly string _dbPath;

    public LemmaSearchServiceTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"dpd-lemma-ls-{Guid.NewGuid():N}.db");
        using var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        Exec(c, @"
            CREATE TABLE lemma (id INTEGER PRIMARY KEY, lemma TEXT NOT NULL, pos TEXT, gloss TEXT, derived_from TEXT);
            CREATE TABLE form_lemma (form TEXT NOT NULL, lemma_id INTEGER NOT NULL);
            CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);
            INSERT INTO lemma VALUES
                (39702,'pajānāti','pr','knows',NULL),
                (39994,'paññā 1','fem','wisdom','pajānāti'),
                (40070,'paññāya 1','ger','knowing','pajānāti'),
                (40071,'paññāya 2','fem','by wisdom','paññā');
            INSERT INTO form_lemma VALUES
                ('pajānāti',39702),
                ('paññā',39994),('paññaṃ',39994),('paññāya',39994),
                ('paññāya',40070),('paññāya',40071),('paññāhi',40071);
            INSERT INTO meta VALUES ('scope','mid'),('dpd_version','test');");
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private LemmaSearchService NewService(FakeSearchService fake)
        => new(new SqliteLemmaProvider(_dbPath), fake);

    [Fact]
    public void Unavailable_when_asset_missing()
    {
        var svc = new LemmaSearchService(new SqliteLemmaProvider("nope.db"), new FakeSearchService());
        Assert.False(svc.IsAvailable);
        Assert.Null(svc.ResolveWord("paññāya", Script.Latin));
    }

    [Fact]
    public void ResolveWord_delegates_to_provider()
    {
        var svc = NewService(new FakeSearchService());
        var r = svc.ResolveWord("paññāya", Script.Latin);
        Assert.NotNull(r);
        Assert.True(r!.Candidates.Count > 1); // homograph
    }

    [Fact]
    public async Task ExpandAndSearch_builds_regex_alternation_and_maps_counts()
    {
        var fake = new FakeSearchService(); // attests all but the last alternation form
        var svc = NewService(fake);

        var result = await svc.ExpandAndSearchAsync(39994, includeFamily: false, outputScript: Script.Ipe);

        Assert.NotNull(result);
        // paññā's forms: paññā, paññaṃ, paññāya  => 3 candidates
        Assert.Equal(3, result!.CandidateFormCount);
        Assert.Equal(2, result.AttestedFormCount);              // fake dropped one (synthetic)
        Assert.Equal(199, result.TotalOccurrences);             // 100 + 99
        Assert.False(result.ExpansionCapped);
        Assert.Equal("paññā 1", result.Lemma.Lemma);
        // counts are sorted descending
        Assert.True(result.AttestedForms[0].Count >= result.AttestedForms[^1].Count);

        // it went through the Regex path as an anchored alternation
        Assert.NotNull(fake.LastQuery);
        Assert.Equal(SearchMode.Regex, fake.LastQuery!.Mode);
        Assert.True(fake.LastQuery.CountsOnly);
        Assert.StartsWith("^(", fake.LastQuery.QueryText);
        Assert.EndsWith(")$", fake.LastQuery.QueryText);
        Assert.Contains("paññāya", fake.LastQuery.QueryText);
    }

    [Fact]
    public async Task ExpandAndSearch_with_family_includes_related_forms()
    {
        var fake = new FakeSearchService();
        var svc = NewService(fake);

        // family of paññā (39994): baseName "paññā" -> self + 40071 (derived_from paññā), whose 'paññāhi' is added.
        var result = await svc.ExpandAndSearchAsync(39994, includeFamily: true, outputScript: Script.Ipe);

        Assert.NotNull(result);
        Assert.Contains("paññāhi", fake.LastQuery!.QueryText);   // pulled in from the family member
        Assert.True(result!.CandidateFormCount >= 4);
    }

    [Fact]
    public async Task ExpandAndSearch_reports_relatedLemmas_even_without_family_union()
    {
        var fake = new FakeSearchService();
        var svc = NewService(fake);

        // Focus = the verb pajānāti (39702). Its derived_from cluster = {39702, 39994 (paññā 1, fem),
        // 40070 (paññāya 1, ger)}. Even with family:false (forms = just the verb's own), relatedLemmas must list
        // the SIBLINGS with their pos so a client can scope a conjugation (the verbal `ger` 40070) vs a deverbal
        // noun (`fem` 39994) — WITHOUT unioning the whole family. (#247 relatedLemmas)
        var result = await svc.ExpandAndSearchAsync(39702, includeFamily: false, outputScript: Script.Ipe);

        Assert.NotNull(result);
        var related = result!.RelatedLemmas.ToDictionary(r => r.LemmaId);
        Assert.Contains(39994L, related.Keys);              // deverbal noun sibling
        Assert.Contains(40070L, related.Keys);              // a VERBAL sibling (its own lemmaId)
        Assert.DoesNotContain(39702L, related.Keys);        // the focus itself is excluded
        Assert.Equal("ger", related[40070].Pos);            // pos carried so the client can scope the conjugation
        Assert.Equal("fem", related[39994].Pos);
        // family:false → the searched alternation is just the verb's own forms, not the siblings'
        Assert.DoesNotContain("paññāhi", fake.LastQuery!.QueryText);
    }

    [Fact]
    public async Task ExpandAndSearch_includeRelated_false_skips_relatedLemmas()
    {
        // The report's focus-count path opts out — no family query, empty relatedLemmas. (#247 perf)
        var svc = NewService(new FakeSearchService());
        var result = await svc.ExpandAndSearchAsync(39702, includeFamily: false, outputScript: Script.Ipe, includeRelated: false);
        Assert.NotNull(result);
        Assert.Empty(result!.RelatedLemmas);
    }

    [Fact]
    public async Task ExpandAndSearchSet_unions_several_lemmas_forms_in_one_query()
    {
        var fake = new FakeSearchService();
        var svc = NewService(fake);

        // Homonyms 'paññāya 1' (40070) + 'paññāya 2' (40071): their forms are UNIONed into ONE search, so a
        // collapsed family row counts the shared forms once. (#247 family)
        var result = await svc.ExpandAndSearchSetAsync(new long[] { 40070, 40071 }, Script.Ipe);

        Assert.NotNull(result);
        Assert.Contains("paññāya", fake.LastQuery!.QueryText);
        Assert.Contains("paññāhi", fake.LastQuery.QueryText);   // pulled from the 2nd homonym, unioned in
    }

    [Fact]
    public async Task ExpandAndSearchSet_empty_is_null()
    {
        var svc = NewService(new FakeSearchService());
        Assert.Null(await svc.ExpandAndSearchSetAsync(System.Array.Empty<long>()));
    }

    [Fact]
    public async Task ExpandAndSearch_unknown_lemma_is_null()
    {
        var svc = NewService(new FakeSearchService());
        Assert.Null(await svc.ExpandAndSearchAsync(999999));
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }

    // A fake search service that parses an anchored alternation `^(a|b|c)$` and returns MatchingTerms for
    // all forms except the last (simulating one synthetic/unattested form), with descending counts.
    private sealed class FakeSearchService : ISearchService
    {
        public SearchQuery? LastQuery;

        public Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken cancellationToken = default)
        {
            LastQuery = query;
            var forms = ParseAlternation(query.QueryText);
            var terms = new List<MatchingTerm>();
            int total = 0;
            for (int i = 0; i < forms.Count - 1; i++) // drop the last -> "synthetic, not attested"
            {
                int count = 100 - i;
                terms.Add(new MatchingTerm { Term = forms[i], DisplayTerm = forms[i], TotalCount = count, BookCount = 1 });
                total += count;
            }
            return Task.FromResult(new SearchResult
            {
                Terms = terms,
                TotalTermCount = terms.Count,
                TotalOccurrenceCount = total,
            });
        }

        private static List<string> ParseAlternation(string q)
        {
            var s = q.Trim();
            if (s.StartsWith('^')) s = s[1..];
            if (s.EndsWith('$')) s = s[..^1];
            if (s.StartsWith('(') && s.EndsWith(')')) s = s[1..^1];
            return s.Split('|').ToList();
        }

        // Unused by LemmaSearchService.
        public Task<List<string>> GetAllTermsAsync(string prefix = "", int limit = 100, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<SearchResult> GetNextPageAsync(string continuationToken, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<List<TermPosition>> GetTermPositionsAsync(string bookFileName, string term, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
        public Task<List<List<TermPosition>>> GetMultiWordPositionsAsync(string bookFileName, string query, SearchMode mode, int proximityDistance, CancellationToken cancellationToken = default)
            => throw new NotSupportedException();
    }
}
