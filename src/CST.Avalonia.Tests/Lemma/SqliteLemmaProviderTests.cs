using System;
using System.IO;
using System.Linq;
using CST.Lemma;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CST.Avalonia.Tests.Lemma;

// Exercises SqliteLemmaProvider against a tiny hand-built fixture (rows mirror real DPD ground truth:
// pajānāti=39702, paññā 1=39994, paññāya 1(ger)=40070, paññāya 2(fem)=40071, nappajānāti=35708).
public sealed class SqliteLemmaProviderTests : IDisposable
{
    private readonly string _dbPath;

    public SqliteLemmaProviderTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"dpd-lemma-fixture-{Guid.NewGuid():N}.db");
        BuildFixture(_dbPath);
    }

    private static void BuildFixture(string path)
    {
        using var c = new SqliteConnection($"Data Source={path}");
        c.Open();
        Exec(c, @"
            CREATE TABLE lemma (id INTEGER PRIMARY KEY, lemma TEXT NOT NULL, pos TEXT, gloss TEXT, derived_from TEXT);
            CREATE TABLE forms (form TEXT PRIMARY KEY, grammar TEXT);
            CREATE TABLE form_lemma (form TEXT NOT NULL, lemma_id INTEGER NOT NULL);
            CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);");

        Exec(c, @"INSERT INTO lemma(id,lemma,pos,gloss,derived_from) VALUES
            (39702,'pajānāti','pr','knows; understands',NULL),
            (39994,'paññā 1','fem','wisdom; understanding','pajānāti'),
            (40070,'paññāya 1','ger','knowing; understanding','pajānāti'),
            (40071,'paññāya 2','fem','by wisdom','paññā'),
            (35708,'nappajānāti','pr','does not know','pajānāti');");

        Exec(c, @"INSERT INTO form_lemma(form,lemma_id) VALUES
            ('pajānāti',39702),('pajānanti',39702),
            ('paññā',39994),('paññaṃ',39994),('paññāya',39994),
            ('paññāya',40070),('paññāya',40071),
            ('nappajānāti',35708);");

        Exec(c, @"INSERT INTO forms(form,grammar) VALUES
            ('pajānāti','[[""pajānāti"",""verb"",""pr 3rd sg""]]'),
            ('paññāya','[[""paññā"",""noun"",""fem instr sg""],[""paññāya"",""verb"",""ger""]]');");

        Exec(c, @"INSERT INTO meta(key,value) VALUES
            ('scope','mid'),('dpd_version','v0.4.20260531'),('converter_version','1'),
            ('schema_version','1'),('license','CC BY-NC-SA 4.0'),('attribution','test fixture');");
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    [Fact]
    public void Missing_asset_is_gracefully_unavailable()
    {
        using var p = new SqliteLemmaProvider(Path.Combine(Path.GetTempPath(), "does-not-exist.db"));
        Assert.False(p.IsAvailable);
        Assert.Null(p.Meta);
        Assert.Null(p.ResolveForm("pajānāti"));
        Assert.Null(p.ExpandLemma(39702));
    }

    [Fact]
    public void Available_asset_exposes_meta()
    {
        using var p = new SqliteLemmaProvider(_dbPath);
        Assert.True(p.IsAvailable);
        Assert.NotNull(p.Meta);
        Assert.Equal("mid", p.Meta!.Scope);
        Assert.Equal("v0.4.20260531", p.Meta.DpdVersion);
        Assert.Equal("CC BY-NC-SA 4.0", p.Meta.License);
    }

    [Fact]
    public void ResolveForm_simple_returns_single_candidate_and_grammar()
    {
        using var p = new SqliteLemmaProvider(_dbPath);
        var r = p.ResolveForm("pajānāti");
        Assert.NotNull(r);
        var c = Assert.Single(r!.Candidates);
        Assert.Equal(39702, c.LemmaId);
        Assert.Equal("pr", c.Pos);
        Assert.NotNull(r.Grammar);
    }

    [Fact]
    public void ResolveForm_homograph_returns_candidates_split_by_derivation()
    {
        using var p = new SqliteLemmaProvider(_dbPath);
        var r = p.ResolveForm("paññāya");
        Assert.NotNull(r);
        Assert.True(r!.Candidates.Count > 1, "paññāya is a homograph");
        Assert.Contains(r.Candidates, x => x.Pos == "ger" && x.DerivedFrom == "pajānāti"); // the verb gerund
        Assert.Contains(r.Candidates, x => x.LemmaId == 39994);                             // the noun paññā
    }

    [Fact]
    public void ResolveForm_unknown_is_null()
    {
        using var p = new SqliteLemmaProvider(_dbPath);
        Assert.Null(p.ResolveForm("zzznotaword"));
    }

    [Fact]
    public void ExpandLemma_returns_the_paradigm()
    {
        using var p = new SqliteLemmaProvider(_dbPath);
        var e = p.ExpandLemma(39994);
        Assert.NotNull(e);
        Assert.Equal("paññā 1", e!.Lemma);
        Assert.Equal(new[] { "paññaṃ", "paññā", "paññāya" }, e.Forms.OrderBy(f => f, StringComparer.Ordinal).ToArray());
        Assert.Null(e.Family); // not requested
    }

    [Fact]
    public void ExpandLemma_with_family_is_the_bidirectional_cluster()
    {
        using var p = new SqliteLemmaProvider(_dbPath);
        // Focus 'paññā 1' (39994) is a deverbal noun derived_from 'pajānāti'. Its family must reach BOTH
        // directions: UP to its parent verb 39702 and across to the verb's other derivations (35708, 40070),
        // and DOWN to its own child 40071 (derived_from 'paññā'). Before the fix the family was computed
        // downward-only, so the parent verb 39702 was missing and its shared forms were mis-flagged as
        // homographs of the noun.
        var e = p.ExpandLemma(39994, includeFamily: true);
        Assert.NotNull(e);
        Assert.NotNull(e!.Family);
        Assert.Contains(e.Family!, x => x.LemmaId == 39994);   // self
        Assert.Contains(e.Family!, x => x.LemmaId == 39702);   // parent verb (the fix)
        Assert.Contains(e.Family!, x => x.LemmaId == 40070);   // sibling under pajānāti (paññāya 1, ger)
        Assert.Contains(e.Family!, x => x.LemmaId == 35708);   // sibling under pajānāti (nappajānāti)
        Assert.Contains(e.Family!, x => x.LemmaId == 40071);   // own child (paññāya 2, derived_from paññā)
    }

    [Fact]
    public void ExpandLemma_from_a_bottom_member_reaches_a_numbered_parent()
    {
        using var p = new SqliteLemmaProvider(_dbPath);
        // Focus 'paññāya 2' (40071) is derived_from 'paññā', whose headword is the NUMBERED homonym
        // 'paññā 1' (39994). The parent lookup must climb via the homonym GLOB, not exact match, so the
        // parent is reached and 'paññāya' (shared with the parent) is not mis-flagged as a homograph.
        var e = p.ExpandLemma(40071, includeFamily: true);
        Assert.NotNull(e!.Family);
        Assert.Contains(e.Family!, x => x.LemmaId == 40071);   // self
        Assert.Contains(e.Family!, x => x.LemmaId == 39994);   // numbered parent 'paññā 1'
    }

    [Fact]
    public void ExpandLemma_unknown_id_is_null()
    {
        using var p = new SqliteLemmaProvider(_dbPath);
        Assert.Null(p.ExpandLemma(999999));
    }

    [Fact]
    public void GetLemma_returns_metadata()
    {
        using var p = new SqliteLemmaProvider(_dbPath);
        var l = p.GetLemma(40070);
        Assert.NotNull(l);
        Assert.Equal("paññāya 1", l!.Lemma);
        Assert.Equal("pajānāti", l.DerivedFrom);
    }

    [Theory]
    [InlineData("paññāya 1", "paññāya")]
    [InlineData("pajānāti", "pajānāti")]
    [InlineData("paññā 12", "paññā")]
    [InlineData("dhamma 1.01", "dhamma")]        // DPD dotted sub-numbering
    [InlineData("dhamma 2.1", "dhamma")]
    [InlineData("a b", "a b")]                    // non-numeric suffix is not a homonym marker
    public void StripHomonym_removes_trailing_number(string input, string expected)
        => Assert.Equal(expected, SqliteLemmaProvider.StripHomonym(input));

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }
}
