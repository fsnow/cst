using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Services.Dictionaries;
using CST.Conversion;
using CST.Lemma;
using CST.Lexicon;
using CST.Tools;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// The dictionary source registry (#466) that replaced CompositeDictionaryTool: DPD is a source composed from
/// the dpd-cst-subset asset, a downloaded lexicon (DPPN) is a source, and the flat-file languages are sources —
/// all enumerated + routed through the one registry, with the RegistryDictionaryTool exposing them over /v1.
/// </summary>
public sealed class DictionaryRegistryTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dpdPath;

    public DictionaryRegistryTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cst-dictreg-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dpdPath = Path.Combine(_dir, "dpd.db");
        BuildDpdFixture(_dpdPath);
    }

    public void Dispose()
    {
        try { SqliteConnection.ClearAllPools(); } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }

    // ---- DpdDictionarySource (the composed DPD path, formerly CompositeDictionaryTool) ----

    private DpdDictionarySource Dpd(bool available = true) =>
        new(new SqliteLemmaProvider(available ? _dpdPath : Path.Combine(_dir, "absent.db")));

    [Fact]
    public async Task Dpd_composes_an_entry_with_lemmaId_and_source_from_meta()
    {
        var res = await Dpd().LookupAsync(new DictionaryRequest("dpd", "paññā", Script.Latin));
        var e = Assert.Single(res);
        Assert.Equal("paññā 1", e.Headword);
        Assert.Equal(100, e.LemmaId);                       // chains to /v1/lemma-report/100
        Assert.Equal("Digital Pāḷi Dictionary (DPD)", e.Source);
        Assert.Contains("wisdom", e.MeaningHtml);
        Assert.Contains("<i>fem</i>", e.MeaningHtml);
        Assert.Contains("pa+√ñā", e.MeaningHtml);
        Assert.Contains("lit. knowing", e.MeaningHtml);
    }

    [Fact]
    public async Task Dpd_homograph_returns_multiple_entries_and_respects_max_and_script()
    {
        Assert.Equal(2, (await Dpd().LookupAsync(new DictionaryRequest("dpd", "paññāya", Script.Latin))).Count);
        Assert.Single(await Dpd().LookupAsync(new DictionaryRequest("dpd", "paññāya", Script.Latin, MaxEntries: 1)));
        // A Devanagari query normalizes to the same IAST key as the Latin one.
        string deva = ScriptConverter.Convert("paññā", Script.Latin, Script.Devanagari);
        Assert.Single(await Dpd().LookupAsync(new DictionaryRequest("dpd", deva, Script.Latin)));
    }

    [Fact]
    public async Task Dpd_is_unavailable_and_empty_when_the_asset_is_absent()
    {
        Assert.False(Dpd(available: false).IsAvailable);
        Assert.Empty(await Dpd(available: false).LookupAsync(new DictionaryRequest("dpd", "paññā", Script.Latin)));
    }

    // ---- SqliteDictionarySource (a downloaded lexicon) ----

    private string BuildLexicon(string sourceId = "dppn", string? license = null)
    {
        var meta = new LexiconMeta(sourceId, "DPPN", "en", LexiconKind.ProperNames,
            Title: "Dictionary of Pāli Proper Names", Author: "G. P. Malalasekera",
            Reviser: "Ānandajoti Bhikkhu", Year: "2025", License: license, SourceVersion: "2025-06");
        var path = Path.Combine(_dir, sourceId + ".db");
        LexiconBuilder.Build(path, meta, new[]
        {
            new RawEntry("Sāvatthī", "<p>Capital of Kosala.</p>"),
            new RawEntry("Jetavana 1", "<p>A grove.</p>"),
            new RawEntry("Jetavana 2", "<p>Another.</p>"),
        });
        return path;
    }

    [Fact]
    public async Task A_lexicon_source_looks_up_by_headword_with_homonyms_and_meta_identity()
    {
        var src = new SqliteDictionarySource(BuildLexicon(), "dppn");
        Assert.True(src.IsAvailable);
        Assert.Equal("DPPN", src.DisplayName);
        Assert.Equal(DictionarySourceKind.ProperNames, src.Kind);

        var jeta = await src.LookupAsync(new DictionaryRequest("dppn", "jetavana", Script.Latin));
        Assert.Equal(new[] { "Jetavana 1", "Jetavana 2" }, jeta.Select(e => e.Headword));
        Assert.Contains("grove", jeta[0].MeaningHtml);

        var sav = await src.LookupAsync(new DictionaryRequest("dppn", "sāvatthī", Script.Latin));
        Assert.Equal("Dictionary of Pāli Proper Names", Assert.Single(sav).Source);
    }

    [Fact]
    public void A_lexicon_source_with_no_license_reports_a_null_license()
    {
        var src = new SqliteDictionarySource(BuildLexicon(license: null), "dppn");
        Assert.NotNull(src.Attribution);                    // still attributed (title/author)
        Assert.Null(src.Attribution!.License);              // but no license asserted
    }

    [Fact]
    public async Task A_missing_lexicon_file_is_unavailable_and_empty_not_a_crash()
    {
        var src = new SqliteDictionarySource(Path.Combine(_dir, "not-installed.db"), "dppn");
        Assert.False(src.IsAvailable);
        Assert.Empty(await src.LookupAsync(new DictionaryRequest("dppn", "sāvatthī", Script.Latin)));
    }

    [Fact]
    public async Task A_non_positive_maxEntries_returns_zero_not_the_unbounded_prefix_run()
    {
        // fable MED-1: the lexicon reader treats max<=0 as UNBOUNDED; the tool contract (#305) is that a
        // non-positive ask returns zero. The source must guard, or a maxEntries:0 lookup dumps the whole run.
        var src = new SqliteDictionarySource(BuildLexicon(), "dppn");
        Assert.Empty(await src.LookupAsync(new DictionaryRequest("dppn", "j", Script.Latin, MaxEntries: 0)));
        Assert.Empty(await src.LookupAsync(new DictionaryRequest("dppn", "j", Script.Latin, MaxEntries: -5)));
        Assert.NotEmpty(await src.LookupAsync(new DictionaryRequest("dppn", "jetavana", Script.Latin, MaxEntries: 5)));
    }

    [Fact]
    public async Task A_just_installed_lexicon_flips_available_it_is_not_a_frozen_failure()
    {
        // fable MED-2: a source pointed at a not-yet-present file must become available once the file appears,
        // rather than caching the initial "absent" forever.
        string path = Path.Combine(_dir, "late.db");
        var src = new SqliteDictionarySource(path, "dppn");
        Assert.False(src.IsAvailable);                       // not installed yet

        File.Copy(BuildLexicon(sourceId: "late-src"), path); // "download" completes
        Assert.True(src.IsAvailable);                        // flips available without a restart
        Assert.NotEmpty(await src.LookupAsync(new DictionaryRequest("dppn", "sāvatthī", Script.Latin)));
    }

    // ---- the registry + RegistryDictionaryTool ----

    [Fact]
    public void The_registry_lists_only_available_sources_and_de_dups_by_id_first_wins()
    {
        var first = new FakeSource("dpd", available: true, display: "First");
        var second = new FakeSource("dpd", available: true, display: "Second");   // same id → deduped
        var absent = new FakeSource("hi", available: false);                       // not installed
        var en = new FakeSource("en", available: true);

        var registry = new DictionarySourceRegistry(new IDictionarySource[] { en, first, second, absent });

        Assert.Equal(new[] { "en", "dpd" }, registry.Available.Select(s => s.Id));   // hi absent; dup deduped
        Assert.Same(first, registry.ById("DPD"));                                    // first registration wins, case-insensitive
        Assert.Null(registry.ById("hi"));                                            // unavailable → not routable
    }

    [Fact]
    public void The_factory_gives_reserved_ids_to_their_sources_not_a_flat_dir_of_the_same_name()
    {
        // fable HIGH-1: the real composition path. A flat-file dir literally named "dpd" must NOT shadow the
        // real DPD source; the factory owns that ordering, and the DI wiring routes through it.
        var flat = new FakeDictionaryService("dpd", "en");   // a stray "dpd" flat dir alongside "en"
        var registry = DictionarySourceFactory.Build(flat, new SqliteLemmaProvider(_dpdPath), Path.Combine(_dir, "no-dppn.db"));

        var dpd = registry.ById("dpd");
        Assert.IsType<DpdDictionarySource>(dpd);             // the real composed DPD, not the flat dir
        Assert.Contains("en", registry.Available.Select(s => s.Id));   // the genuine flat language survives
    }

    [Fact]
    public async Task The_tool_advertises_available_sources_and_routes_by_id()
    {
        var registry = new DictionarySourceRegistry(new IDictionarySource[]
        {
            new FakeSource("en", available: true, display: "Childers"),
            Dpd(),
        });
        var tool = new RegistryDictionaryTool(registry);

        var langs = tool.Languages.Select(l => l.Language).ToList();
        Assert.Contains("en", langs);
        Assert.Contains("dpd", langs);

        // Routes "dpd" to the DPD source (composed entry).
        var dpdRes = await tool.LookupAsync(new DictionaryRequest("dpd", "paññā", Script.Latin));
        Assert.Equal(100, Assert.Single(dpdRes).LemmaId);
        // Routes "en" to the fake flat source.
        var enRes = await tool.LookupAsync(new DictionaryRequest("en", "x", Script.Latin));
        Assert.Equal("FAKE:en:x", Assert.Single(enRes).MeaningHtml);
        // An unknown/unavailable id is an empty result, not a throw.
        Assert.Empty(await tool.LookupAsync(new DictionaryRequest("zz", "x", Script.Latin)));
    }

    // ---- fixtures ----

    private sealed class FakeDictionaryService : CST.Avalonia.Services.IDictionaryService
    {
        private readonly string[] _langs;
        public FakeDictionaryService(params string[] langs) => _langs = langs;
        public IReadOnlyList<string> AvailableLanguages => _langs;
        public DictionarySourceInfo? SourceFor(string language) => null;
        public Task<IReadOnlyList<CST.Avalonia.Models.DictionaryWord>> LookupAsync(string language, string query, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<CST.Avalonia.Models.DictionaryWord>>(Array.Empty<CST.Avalonia.Models.DictionaryWord>());
    }

    private sealed class FakeSource : IDictionarySource
    {
        public FakeSource(string id, bool available, string? display = null)
        { Id = id; IsAvailable = available; DisplayName = display ?? id; }

        public string Id { get; }
        public string DisplayName { get; }
        public string DefinitionLanguage => "en";
        public DictionarySourceKind Kind => DictionarySourceKind.General;
        public bool IsAvailable { get; }
        public DictionarySourceInfo? Attribution => new(DisplayName, null, null, null, null, null, null);

        public Task<IReadOnlyList<DictionaryEntry>> LookupAsync(DictionaryRequest request, CancellationToken ct = default)
            => Task.FromResult<IReadOnlyList<DictionaryEntry>>(
                new[] { new DictionaryEntry(request.Query, $"FAKE:{Id}:{request.Query}", DisplayName) });
    }

    private static void BuildDpdFixture(string path)
    {
        using var c = new SqliteConnection($"Data Source={path}");
        c.Open();
        void Exec(string sql) { using var cmd = c.CreateCommand(); cmd.CommandText = sql; cmd.ExecuteNonQuery(); }
        Exec(@"
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
            INSERT INTO form_lemma VALUES ('paññā',100),('paññāya',100),('paññāya',101);
            INSERT INTO meta VALUES ('scope','mid'),('source','Digital Pāḷi Dictionary (DPD)'),
                ('author','Bodhirāsa'),('homepage','https://www.dpdict.net/'),
                ('license','CC BY-NC-SA 4.0'),('dpd_version','v0.4.20260531');");
    }
}
