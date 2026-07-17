using System;
using System.IO;
using System.Linq;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Services.LocalApi.Lemma;
using CST.Conversion;
using CST.Lemma;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CST.Avalonia.Tests.Services;

// Deconstruction tests for the sandhi decomposer (#383). Uses a real SqliteLemmaProvider over a tiny fixture
// with a full-scope `forms` table (deconstructor populated for non-enclitic compounds too). Covers the four
// shapes: pure sandhi (split, no headword), enclitic split, compound-stored-as-lemma (direct, no split), and
// a plain word with neither.
public sealed class LemmaDeconstructTests : IDisposable
{
    private readonly string _dbPath;

    public LemmaDeconstructTests() : this("full") { }

    private LemmaDeconstructTests(string scope)
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"dpd-lemma-dc-{Guid.NewGuid():N}.db");
        using var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        Exec(c, $@"
            CREATE TABLE lemma (id INTEGER PRIMARY KEY, lemma TEXT NOT NULL, pos TEXT, gloss TEXT, derived_from TEXT);
            CREATE TABLE form_lemma (form TEXT NOT NULL, lemma_id INTEGER NOT NULL);
            CREATE TABLE forms ( form TEXT PRIMARY KEY, grammar TEXT, deconstructor TEXT );
            CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);
            INSERT INTO lemma VALUES
                (1,'pajānāti','pr','knows',NULL),
                (2,'sammāsambuddha','masc','fully self-awakened one',NULL),
                (3,'gacchati','pr','goes',NULL);
            -- pajānāti is a headword; sammāsambuddho is a headword (compound stored as lemma); gacchati plain.
            INSERT INTO form_lemma VALUES
                ('pajānāti',1),
                ('sammāsambuddho',2),
                ('gacchati',3);
            -- forms.deconstructor: pure sandhi (no form_lemma), enclitic, and the plain word (no decon).
            INSERT INTO forms VALUES
                ('satthakosakaraṇatthāya', NULL, '[""sattha + kosa + karaṇatthāya"",""satthako + usa + karaṇatthāya""]'),
                ('pajānātīti', NULL, '[""pajānāti + iti""]'),
                ('sammāsambuddho', 'masc nom sg', NULL),
                ('gacchati', 'pr 3rd sg', NULL);
            INSERT INTO meta VALUES ('scope','{scope}'),('dpd_version','test');");
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private LemmaSearchService NewService() => new(new SqliteLemmaProvider(_dbPath), new FakeSearch());

    [Fact]
    public void PureSandhi_returns_ranked_splits_and_no_direct_lemma()
    {
        var res = NewService().Deconstruct("satthakosakaraṇatthāya", Script.Latin);

        Assert.NotNull(res);
        Assert.Empty(res!.DirectLemmas);            // no headword — this word only exists as a split
        Assert.Equal(2, res.Splits.Count);
        // array order is the rank; element 0 is DPD's best
        Assert.Equal(0, res.Splits[0].Rank);
        Assert.Equal(new[] { "sattha", "kosa", "karaṇatthāya" }, res.Splits[0].Parts.ToArray());
        Assert.Equal(1, res.Splits[1].Rank);
        Assert.Equal(new[] { "satthako", "usa", "karaṇatthāya" }, res.Splits[1].Parts.ToArray());
    }

    [Fact]
    public void Enclitic_splits_into_base_plus_particle()
    {
        var res = NewService().Deconstruct("pajānātīti", Script.Latin);

        Assert.NotNull(res);
        Assert.Single(res!.Splits);
        Assert.Equal(new[] { "pajānāti", "iti" }, res.Splits[0].Parts.ToArray());
    }

    [Fact]
    public void CompoundStoredAsLemma_falls_through_to_direct_lemma_no_split()
    {
        var res = NewService().Deconstruct("sammāsambuddho", Script.Latin);

        Assert.NotNull(res);
        Assert.Empty(res!.Splits);                  // no deconstructor — it's a headword
        Assert.Single(res.DirectLemmas);
        Assert.Equal("sammāsambuddha", res.DirectLemmas[0].Lemma);
    }

    [Fact]
    public void PlainWord_with_neither_split_nor_extra_is_still_returned_via_direct_lemma()
    {
        // gacchati has a form_lemma row but no deconstructor: returned with the direct lemma, empty splits.
        var res = NewService().Deconstruct("gacchati", Script.Latin);

        Assert.NotNull(res);
        Assert.Empty(res!.Splits);
        Assert.Single(res.DirectLemmas);
    }

    [Fact]
    public void Unknown_word_is_null()
    {
        Assert.Null(NewService().Deconstruct("nonesuchword", Script.Latin));
    }

    [Fact]
    public void Unavailable_asset_is_null()
    {
        var svc = new LemmaSearchService(new SqliteLemmaProvider("nope.db"), new FakeSearch());
        Assert.Null(svc.Deconstruct("pajānātīti", Script.Latin));
    }

    [Fact]
    public void Dto_note_flags_multi_split_ambiguity_and_chain()
    {
        var res = NewService().Deconstruct("satthakosakaraṇatthāya", Script.Latin);
        var dto = LemmaApi.ToDeconstruct("satthakosakaraṇatthāya", res!, Script.Latin);

        Assert.Equal(2, dto.Splits.Count);
        Assert.Contains("ALTERNATIVE", dto.Note);          // ambiguity warning
        Assert.Contains("/v1/lemma/", dto.Note);            // the resolve-each-part chain
        Assert.Empty(dto.DirectLemmas);
    }

    [Fact]
    public void Dto_note_for_headword_only_word_points_at_directLemmas()
    {
        var res = NewService().Deconstruct("sammāsambuddho", Script.Latin);
        var dto = LemmaApi.ToDeconstruct("sammāsambuddho", res!, Script.Latin);

        Assert.Empty(dto.Splits);
        Assert.Single(dto.DirectLemmas);
        Assert.Contains("headword", dto.Note);
    }

    [Fact]
    public void MidScope_note_hints_full_asset_when_no_split()
    {
        using var mid = new LemmaDeconstructTests("mid");
        // gacchati: no split; on a mid asset the note should hint the full-scope asset covers compounds.
        var res = mid.NewService().Deconstruct("gacchati", Script.Latin);
        var dto = LemmaApi.ToDeconstruct("gacchati", res!, Script.Latin);
        Assert.Contains("full-scope", dto.Note);
    }

    [Fact]
    public void NotFoundNote_is_scope_aware_and_points_at_lemma_fallback()
    {
        // full scope: no full-asset upsell, just the lemma-fallback pointer.
        var full = LemmaApi.DeconstructNotFoundNote("xyz", "full");
        Assert.Contains("/v1/lemma/xyz", full);
        Assert.DoesNotContain("full-scope", full);

        // mid scope: the missing-compound case DOES get the "install full-scope asset" hint.
        var mid = LemmaApi.DeconstructNotFoundNote("xyz", "mid");
        Assert.Contains("/v1/lemma/xyz", mid);
        Assert.Contains("full-scope", mid);

        // empty/unknown scope (meta row absent) must NOT print a bare "scope ''" hint.
        var empty = LemmaApi.DeconstructNotFoundNote("xyz", "");
        Assert.DoesNotContain("scope ''", empty);
        Assert.DoesNotContain("full-scope", empty);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }

    // Minimal ISearchService — deconstruct never touches the corpus, so this is never called.
    private sealed class FakeSearch : ISearchService
    {
        public System.Threading.Tasks.Task<SearchResult> SearchAsync(SearchQuery query, System.Threading.CancellationToken ct = default)
            => throw new NotSupportedException();
        public System.Threading.Tasks.Task<System.Collections.Generic.List<string>> GetAllTermsAsync(string prefix = "", int limit = 100, System.Threading.CancellationToken ct = default)
            => throw new NotSupportedException();
        public System.Threading.Tasks.Task<SearchResult> GetNextPageAsync(string continuationToken, System.Threading.CancellationToken ct = default)
            => throw new NotSupportedException();
        public System.Threading.Tasks.Task<System.Collections.Generic.List<TermPosition>> GetTermPositionsAsync(string bookFileName, string term, System.Threading.CancellationToken ct = default)
            => throw new NotSupportedException();
        public System.Threading.Tasks.Task<System.Collections.Generic.List<System.Collections.Generic.List<TermPosition>>> GetMultiWordPositionsAsync(string bookFileName, string query, SearchMode mode, int proximityDistance, System.Threading.CancellationToken ct = default)
            => throw new NotSupportedException();
    }
}
