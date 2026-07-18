using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Services.Tools;
using CST.Conversion;
using CST.Lemma;
using CST.Tools;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CST.Avalonia.Tests.Services;

// CompositeDictionaryTool (#109): DPD ("dpd") unioned with the flat-file dictionaries. Uses a real
// report-scope SqliteLemmaProvider over a tiny fixture + a fake flat IDictionaryTool for the delegation path.
public sealed class CompositeDictionaryToolTests : IDisposable
{
    private readonly string _dbPath;

    public CompositeDictionaryToolTests()
    {
        _dbPath = Path.Combine(Path.GetTempPath(), $"dpd-lemma-dict-{Guid.NewGuid():N}.db");
        using var c = new SqliteConnection($"Data Source={_dbPath}");
        c.Open();
        Exec(c, @"
            CREATE TABLE lemma (id INTEGER PRIMARY KEY, lemma TEXT NOT NULL, pos TEXT, gloss TEXT, derived_from TEXT,
                root_key TEXT, construction TEXT, sanskrit TEXT, meaning_lit TEXT, pattern TEXT, ebt_count INTEGER,
                example_source TEXT, example_sutta TEXT, example TEXT, synonym TEXT, antonym TEXT);
            CREATE TABLE form_lemma (form TEXT NOT NULL, lemma_id INTEGER NOT NULL);
            CREATE TABLE root (root_key TEXT PRIMARY KEY, root_sign TEXT, root_meaning TEXT, root_group INTEGER,
                sanskrit_root TEXT, sanskrit_root_meaning TEXT, dhatupatha_pali TEXT, dhatupatha_english TEXT);
            CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);
            INSERT INTO lemma (id,lemma,pos,gloss,derived_from,construction,meaning_lit) VALUES
                (100,'paññā 1','fem','wisdom',NULL,'pa+√ñā','knowing'),
                (101,'paññāya 2','fem','by wisdom',NULL,NULL,NULL);
            INSERT INTO form_lemma VALUES
                ('paññā',100),
                ('paññāya',100),('paññāya',101);
            INSERT INTO meta VALUES
                ('scope','mid'),
                ('source','Digital Pāḷi Dictionary (DPD)'),
                ('author','Bodhirāsa'),
                ('homepage','https://www.dpdict.net/'),
                ('license','CC BY-NC-SA 4.0'),
                ('dpd_version','v0.4.20260531');");
    }

    private static void Exec(SqliteConnection c, string sql)
    {
        using var cmd = c.CreateCommand();
        cmd.CommandText = sql;
        cmd.ExecuteNonQuery();
    }

    private CompositeDictionaryTool NewTool(bool assetAvailable = true)
        => new(new FakeFlat(), new SqliteLemmaProvider(assetAvailable ? _dbPath : "nope.db"));

    [Fact]
    public void Languages_unions_flat_and_dpd_with_source_from_meta()
    {
        var langs = NewTool().Languages;
        Assert.Contains(langs, l => l.Language == "en");                 // flat pass-through
        var dpd = langs.SingleOrDefault(l => l.Language == "dpd");
        Assert.NotNull(dpd);
        var s = dpd!.Source;
        Assert.NotNull(s);
        Assert.Equal("Digital Pāḷi Dictionary (DPD)", s!.Title);
        Assert.Equal("Bodhirāsa", s.Compiler);
        Assert.Equal("v0.4.20260531", s.Edition);
        Assert.Equal("2026", s.Year);                                    // parsed from the YYYYMMDD in the version
        Assert.Equal("CC BY-NC-SA 4.0", s.License);
        Assert.Equal("https://www.dpdict.net/", s.Url);
    }

    [Fact]
    public void Languages_omits_dpd_when_asset_absent_but_keeps_flat()
    {
        var langs = NewTool(assetAvailable: false).Languages;
        Assert.Contains(langs, l => l.Language == "en");
        Assert.DoesNotContain(langs, l => l.Language == "dpd");
    }

    [Fact]
    public async Task Dpd_lookup_composes_entry_with_lemmaId_and_source()
    {
        var res = await NewTool().LookupAsync(new DictionaryRequest("dpd", "paññā", Script.Latin));
        var e = Assert.Single(res);
        Assert.Equal("paññā 1", e.Headword);
        Assert.Equal(100, e.LemmaId);                                    // chains to /v1/lemma-report/100
        Assert.Equal("Digital Pāḷi Dictionary (DPD)", e.Source);
        Assert.Contains("wisdom", e.MeaningHtml);
        Assert.Contains("<i>fem</i>", e.MeaningHtml);                    // pos
        Assert.Contains("pa+√ñā", e.MeaningHtml);                        // construction
        Assert.Contains("lit. knowing", e.MeaningHtml);                  // meaning_lit
    }

    [Fact]
    public async Task Dpd_lookup_of_a_homograph_returns_multiple_entries()
    {
        // paññāya resolves to two lemmas (100, 101) → two entries.
        var res = await NewTool().LookupAsync(new DictionaryRequest("dpd", "paññāya", Script.Latin));
        Assert.Equal(2, res.Count);
        Assert.Equal(new long?[] { 100, 101 }, res.Select(e => e.LemmaId).ToArray());
    }

    [Fact]
    public async Task Dpd_lookup_is_case_insensitive_on_language_and_normalizes_input_script()
    {
        // "DPD" routes to DPD; a Devanagari-script query normalizes to the same IAST key as the Latin one.
        string deva = ScriptConverter.Convert("paññā", Script.Latin, Script.Devanagari);
        var res = await NewTool().LookupAsync(new DictionaryRequest("DPD", deva, Script.Latin));
        var e = Assert.Single(res);
        Assert.Equal(100, e.LemmaId);
    }

    [Fact]
    public async Task Dpd_lookup_respects_maxEntries_clamp()
    {
        var res = await NewTool().LookupAsync(new DictionaryRequest("dpd", "paññāya", Script.Latin, MaxEntries: 1));
        Assert.Single(res);
    }

    [Fact]
    public async Task Dpd_lookup_miss_is_empty()
    {
        var res = await NewTool().LookupAsync(new DictionaryRequest("dpd", "nonesuch", Script.Latin));
        Assert.Empty(res);
    }

    [Fact]
    public async Task Dpd_lookup_when_asset_absent_is_empty()
    {
        var res = await NewTool(assetAvailable: false).LookupAsync(new DictionaryRequest("dpd", "paññā", Script.Latin));
        Assert.Empty(res);
    }

    [Fact]
    public void Dpd_is_a_reserved_code_not_double_listed_or_shadowed_by_a_flat_dpd_dir()
    {
        // A flat tool that (hypothetically) also exposes "dpd" must not double-list it; DPD wins.
        var tool = new CompositeDictionaryTool(new FakeFlat(alsoExposesDpd: true), new SqliteLemmaProvider(_dbPath));
        var dpds = tool.Languages.Where(l => l.Language == "dpd").ToList();
        var only = Assert.Single(dpds);
        Assert.Equal("Digital Pāḷi Dictionary (DPD)", only.Source!.Title);   // the DPD source, not the flat one
    }

    [Fact]
    public async Task Non_dpd_language_delegates_to_flat_tool()
    {
        var res = await NewTool().LookupAsync(new DictionaryRequest("en", "dhamma", Script.Latin));
        var e = Assert.Single(res);
        Assert.Equal("FLAT:en:dhamma", e.MeaningHtml);                   // came from the fake flat tool
        Assert.Null(e.LemmaId);
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { if (File.Exists(_dbPath)) File.Delete(_dbPath); } catch { /* best effort */ }
    }

    // Fake flat-file tool: one language ("en"), echoes the request so the delegation path is observable.
    private sealed class FakeFlat : IDictionaryTool
    {
        private readonly bool _alsoExposesDpd;
        public FakeFlat(bool alsoExposesDpd = false) => _alsoExposesDpd = alsoExposesDpd;

        public IReadOnlyList<DictionaryLanguageInfo> Languages
        {
            get
            {
                var list = new List<DictionaryLanguageInfo>
                    { new("en", new DictionarySourceInfo("Childers", null, null, "1875", null, "Public domain", null)) };
                if (_alsoExposesDpd)
                    list.Add(new DictionaryLanguageInfo("dpd", new DictionarySourceInfo("Flat DPD", null, null, null, null, null, null)));
                return list;
            }
        }

        public Task<IReadOnlyList<DictionaryEntry>> LookupAsync(DictionaryRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DictionaryEntry>>(
                new[] { new DictionaryEntry(request.Query, $"FLAT:{request.Language}:{request.Query}", "Childers") });
    }
}
