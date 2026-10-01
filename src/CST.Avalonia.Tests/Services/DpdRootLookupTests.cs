using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Dictionaries;
using CST.Conversion;
using CST.Lemma;
using CST.Tools;
using Microsoft.Data.Sqlite;
using CST.Avalonia.Tests.TestSupport;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// DPD roots in the dictionary (#1002): a word entry shows its root as a link, and a query starting with the
/// root sign looks the <c>root</c> table up, one entry per root with the words built on it. The fixture rows
/// copy the real asset's <c>√var 1</c> / <c>√var 2</c>; <see cref="DpdRootRealAssetTests"/> checks the same
/// lookups against the installed asset.
/// </summary>
[Collection("Sqlite")]   // #960: ClearAllPools is process-wide
public sealed class DpdRootLookupTests : IDisposable
{
    // U+221A SQUARE ROOT — DPD's root sign. Escaped so no non-ASCII symbol is pasted into source.
    private const string R = "\u221A";

    private readonly string _dir;
    private readonly string _dbPath;

    public DpdRootLookupTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cst-dpdroot-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _dbPath = Path.Combine(_dir, "dpd.db");
        BuildFixture(_dbPath);
    }

    public void Dispose()
    {
        try { SqliteConnection.ClearAllPools(); } catch { }
        try { Directory.Delete(_dir, true); } catch { }
    }

    private static void BuildFixture(string path)
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
            INSERT INTO meta VALUES ('scope','full'),('source','Digital Pāḷi Dictionary (DPD)'),
                ('dpd_version','v0.4.20260531');");
        Exec($@"
            INSERT INTO root VALUES
                ('{R}var 1','a','cover, dress, restrain',1,'{R}vṛ','cover','varaṇa-sambhattisu','covering and joining'),
                ('{R}var 2','e, aya','wish, choose',8,'{R}vṛ','choose','āvaraṇ’-<b>icchāsu</b>','obstructing and <b>wishing</b>'),
                ('{R}vas 1','a','live, dwell',1,'{R}vas','dwell','nivāse','dwelling'),
                ('{R}vas 11','a','a <i>made-up</i> root & more',1,NULL,NULL,NULL,NULL),
                ('{R}kar','o','do, make',7,'{R}kṛ','do','-','-');
            INSERT INTO lemma (id,lemma,pos,gloss,derived_from,root_key,construction) VALUES
                (10,'vāreti 1','pr','prevents',NULL,'{R}var 1','{R}var + e + ti'),
                (11,'āvaraṇa 1','nt','obstruction',NULL,'{R}var 1',NULL),
                (12,'āvaraṇa 2','nt','barrier',NULL,'{R}var 1',NULL),
                (13,'vara 1','adj','excellent',NULL,'{R}var 2',NULL),
                (14,'paññā 1','fem','wisdom',NULL,NULL,'pa + {R}ñā'),
                (15,'vasati 1','pr','lives',NULL,'{R}vas 1',NULL),
                (16,'ghost','pr','root row missing',NULL,'{R}gho',NULL);
            INSERT INTO form_lemma VALUES ('vāreti',10),('āvaraṇa',11),('āvaraṇa',12),('vara',13),('paññā',14),
                ('vasati',15),('ghost',16);");
    }

    private DpdDictionarySource Dpd(int maxRootWords = DpdDictionarySource.MaxRootWords) =>
        new(new SqliteLemmaProvider(_dbPath), maxRootWords);

    private static Task<System.Collections.Generic.IReadOnlyList<DictionaryEntry>> Look(
        DpdDictionarySource s, string q, Script script = Script.Latin, int max = 25) =>
        s.LookupAsync(new DictionaryRequest("dpd", q, script, max));

    // ---- provider ----

    [Fact]
    public void FindRoots_by_prefix_returns_each_matching_root_with_its_fields_and_words()
    {
        using var p = new SqliteLemmaProvider(_dbPath);
        var roots = p.FindRoots($"{R}var", prefix: true, maxRoots: 10, maxLemmasPerRoot: 100)!;

        Assert.Equal(new[] { $"{R}var 1", $"{R}var 2" }, roots.Select(r => r.Root.RootKey));
        var v1 = roots[0];
        Assert.Equal("a", v1.Root.RootSign);
        Assert.Equal("cover, dress, restrain", v1.Root.RootMeaning);
        Assert.Equal(1, v1.Root.RootGroup);
        Assert.Equal($"{R}vṛ", v1.Root.SanskritRoot);
        Assert.Equal("covering and joining", v1.Root.DhatupathaEnglish);
        Assert.Equal(new[] { "vāreti 1", "āvaraṇa 1", "āvaraṇa 2" }, v1.Lemmas.Select(l => l.Lemma));
        Assert.Equal(3, v1.LemmaCount);
        Assert.Equal(new[] { "vara 1" }, roots[1].Lemmas.Select(l => l.Lemma));
    }

    [Fact]
    public void FindRoots_exact_matches_only_the_named_root()
    {
        using var p = new SqliteLemmaProvider(_dbPath);
        var roots = p.FindRoots($"{R}var 1", prefix: false, maxRoots: 10, maxLemmasPerRoot: 100)!;
        Assert.Equal($"{R}var 1", Assert.Single(roots).Root.RootKey);
        Assert.Empty(p.FindRoots($"{R}var", prefix: false, maxRoots: 10, maxLemmasPerRoot: 100)!);
    }

    [Fact]
    public void FindRoots_caps_the_words_per_root_but_reports_the_full_count()
    {
        using var p = new SqliteLemmaProvider(_dbPath);
        var v1 = Assert.Single(p.FindRoots($"{R}var 1", prefix: false, maxRoots: 10, maxLemmasPerRoot: 2)!);
        Assert.Equal(new[] { "vāreti 1", "āvaraṇa 1" }, v1.Lemmas.Select(l => l.Lemma));
        Assert.Equal(3, v1.LemmaCount);
    }

    [Fact]
    public void FindRoots_caps_the_roots()
    {
        using var p = new SqliteLemmaProvider(_dbPath);
        Assert.Single(p.FindRoots($"{R}va", prefix: true, maxRoots: 1, maxLemmasPerRoot: 10)!);
        Assert.Equal(4, p.FindRoots($"{R}va", prefix: true, maxRoots: 10, maxLemmasPerRoot: 10)!.Count);
        Assert.Empty(p.FindRoots($"{R}va", prefix: true, maxRoots: 0, maxLemmasPerRoot: 10)!);
        Assert.Empty(p.FindRoots($"{R}va", prefix: true, maxRoots: -1, maxLemmasPerRoot: 10)!);   // SQLite: LIMIT -1 = all
    }

    [Fact]
    public void FindRoots_treats_glob_and_like_metacharacters_literally()
    {
        using var p = new SqliteLemmaProvider(_dbPath);
        Assert.Empty(p.FindRoots($"{R}v*", prefix: true, maxRoots: 10, maxLemmasPerRoot: 10)!);
        Assert.Empty(p.FindRoots($"{R}v%", prefix: true, maxRoots: 10, maxLemmasPerRoot: 10)!);
        Assert.Empty(p.FindRoots($"{R}v_r", prefix: true, maxRoots: 10, maxLemmasPerRoot: 10)!);
    }

    [Fact]
    public void FindRoots_is_null_on_an_asset_without_a_root_table_and_when_unavailable()
    {
        var lean = Path.Combine(_dir, "lean.db");
        using (var c = new SqliteConnection($"Data Source={lean}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"CREATE TABLE lemma (id INTEGER PRIMARY KEY, lemma TEXT NOT NULL, pos TEXT, gloss TEXT,
                derived_from TEXT); CREATE TABLE form_lemma (form TEXT, lemma_id INTEGER);
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);";
            cmd.ExecuteNonQuery();
        }
        using var leanP = new SqliteLemmaProvider(lean);
        Assert.True(leanP.IsAvailable);
        Assert.Null(leanP.FindRoots($"{R}var", true, 10, 10));

        using var absent = new SqliteLemmaProvider(Path.Combine(_dir, "absent.db"));
        Assert.Null(absent.FindRoots($"{R}var", true, 10, 10));
    }

    [Fact]
    public void An_asset_whose_root_table_has_no_root_sign_still_finds_roots()
    {
        var old = Path.Combine(_dir, "nosign.db");
        using (var c = new SqliteConnection($"Data Source={old}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = $@"CREATE TABLE lemma (id INTEGER PRIMARY KEY, lemma TEXT NOT NULL, pos TEXT, gloss TEXT,
                derived_from TEXT, root_key TEXT, construction TEXT, sanskrit TEXT, meaning_lit TEXT, pattern TEXT,
                ebt_count INTEGER, example_source TEXT, example_sutta TEXT, example TEXT, synonym TEXT, antonym TEXT);
                CREATE TABLE form_lemma (form TEXT, lemma_id INTEGER);
                CREATE TABLE root (root_key TEXT PRIMARY KEY, root_meaning TEXT, root_group INTEGER,
                    sanskrit_root TEXT, sanskrit_root_meaning TEXT, dhatupatha_pali TEXT, dhatupatha_english TEXT);
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);
                INSERT INTO root VALUES ('{R}var 1','cover',1,NULL,NULL,NULL,NULL);
                INSERT INTO lemma (id,lemma,root_key) VALUES (1,'vāreti','{R}var 1');
                INSERT INTO form_lemma VALUES ('vāreti',1);";
            cmd.ExecuteNonQuery();
        }
        using var p = new SqliteLemmaProvider(old);
        var v = Assert.Single(p.FindRoots($"{R}var", true, 10, 10)!);
        Assert.Null(v.Root.RootSign);
        Assert.Equal(1, v.LemmaCount);
        Assert.Equal($"{R}var 1", p.GetDetail(1)!.Root!.RootKey);
    }

    [Fact]
    public void GetDetail_carries_the_root_sign()
    {
        using var p = new SqliteLemmaProvider(_dbPath);
        Assert.Equal("a", p.GetDetail(10)!.Root!.RootSign);
    }

    [Fact]
    public void The_reopenable_wrapper_passes_FindRoots_through()
    {
        using var p = new ReopenableLemmaProvider(_dbPath);
        Assert.Equal(2, p.FindRoots($"{R}var", true, 10, 10)!.Count);
    }

    // ---- root queries through the dictionary source ----

    [Fact]
    public async Task A_root_query_returns_one_entry_per_root_with_its_fields()
    {
        var res = await Look(Dpd(), $"{R}var");
        Assert.Equal(new[] { $"{R}var 1", $"{R}var 2" }, res.Select(e => e.Headword));
        Assert.All(res, e => Assert.Null(e.LemmaId));
        Assert.All(res, e => Assert.Equal("Digital Pāḷi Dictionary (DPD)", e.Source));

        var v1 = res[0].MeaningHtml;
        Assert.Contains("cover, dress, restrain", v1);
        Assert.Contains("group 1", v1);
        Assert.Contains("sign a", v1);
        Assert.Contains($"Sanskrit {R}vṛ (cover)", v1);
        Assert.Contains("varaṇa-sambhattisu (covering and joining)", v1);

        var v2 = res[1].MeaningHtml;
        Assert.Contains("sign e, aya", v2);
        // DPD marks the matching gloss with <b>; that tag survives, anything else is escaped.
        Assert.Contains("āvaraṇ’-<b>icchāsu</b> (obstructing and <b>wishing</b>)", v2);
    }

    [Fact]
    public async Task A_root_entry_lists_its_words_as_links_one_per_distinct_word()
    {
        var v1 = (await Look(Dpd(), $"{R}var 1")).Single().MeaningHtml;
        Assert.Contains("3 headwords", v1);
        Assert.Contains("<see>vāreti</see>", v1);
        Assert.Contains("<see>āvaraṇa</see>", v1);
        // āvaraṇa 1 and āvaraṇa 2 share a lookup target: one link, not two identical ones.
        Assert.Equal(1, CountOf(v1, "<see>āvaraṇa</see>"));
        Assert.DoesNotContain("<see>vara</see>", v1);         // root var 2's word, not root var 1's
    }

    [Fact]
    public async Task A_root_query_with_a_homonym_number_finds_only_that_root()
    {
        var res = await Look(Dpd(), $"{R}var 2");
        Assert.Equal($"{R}var 2", Assert.Single(res).Headword);
        // Exact, not prefix: "√vas 1" must not also find "√vas 11".
        Assert.Equal($"{R}vas 1", Assert.Single(await Look(Dpd(), $"{R}vas 1")).Headword);
        Assert.Equal($"{R}vas 1", Assert.Single(await Look(Dpd(), $"{R}vas   1 ")).Headword);
    }

    [Fact]
    public async Task A_root_query_folds_native_digits_in_the_homonym_number()
    {
        // A root link followed in Devanagari puts "√वर् १" in the search box; the converters leave the
        // Devanagari digit alone, so without folding it the lookup misses.
        var deva = R + "\u0935\u0930\u094D \u0967";
        var res = await Look(Dpd(), deva, Script.Devanagari);
        Assert.Equal(R + "\u0935\u0930\u094D \u0967", Assert.Single(res).Headword);
    }

    [Fact]
    public async Task A_root_query_without_the_number_finds_the_prefix_run()
    {
        var res = await Look(Dpd(), $"{R}va");
        Assert.Equal(new[] { $"{R}var 1", $"{R}var 2", $"{R}vas 1", $"{R}vas 11" }, res.Select(e => e.Headword));
    }

    [Fact]
    public async Task Markup_in_root_fields_other_than_bold_is_escaped()
    {
        var html = (await Look(Dpd(), $"{R}vas 11")).Single().MeaningHtml;
        Assert.Contains("a &lt;i&gt;made-up&lt;/i&gt; root &amp; more", html);
        Assert.DoesNotContain("Sanskrit", html);   // NULL Sanskrit root: no line
    }

    [Fact]
    public async Task A_nonexistent_root_and_a_lone_root_sign_return_nothing()
    {
        Assert.Empty(await Look(Dpd(), $"{R}xyz"));
        Assert.Empty(await Look(Dpd(), R));
        Assert.Empty(await Look(Dpd(), R + "  "));
    }

    [Fact]
    public async Task A_bare_root_word_without_the_sign_does_not_find_roots()
    {
        // [fsnow] no input affordance: "var" is a word query, never a root query.
        var res = await Look(Dpd(), "var");
        Assert.DoesNotContain(res, e => e.Headword.StartsWith(R, StringComparison.Ordinal));
    }

    [Fact]
    public async Task A_root_query_respects_maxEntries()
    {
        Assert.Single(await Look(Dpd(), $"{R}va", max: 1));
        Assert.Empty(await Look(Dpd(), $"{R}va", max: 0));
    }

    [Fact]
    public async Task A_root_entry_states_when_its_word_list_is_capped()
    {
        var capped = (await Look(Dpd(maxRootWords: 2), $"{R}var 1")).Single().MeaningHtml;
        Assert.Contains("first 2 of 3 headwords", capped);
        Assert.Contains("<see>vāreti</see>", capped);
        Assert.DoesNotContain("\">3 headwords:", capped);

        var full = (await Look(Dpd(maxRootWords: 3), $"{R}var 1")).Single().MeaningHtml;
        Assert.DoesNotContain("first", full);
        Assert.Contains("\">3 headwords: ", full);
    }

    [Fact]
    public async Task A_root_without_a_dhatupatha_omits_the_line()
    {
        var kar = (await Look(Dpd(), $"{R}kar")).Single().MeaningHtml;
        Assert.DoesNotContain("Dhātupāṭha", kar);
        Assert.DoesNotContain("(-)", kar);
        Assert.Contains("no headwords", kar);
    }

    [Fact]
    public async Task A_root_entry_follows_the_display_script_for_Pali_but_leaves_Sanskrit_in_Latin()
    {
        var deva = (await Look(Dpd(), $"{R}var 1", Script.Devanagari)).Single();
        Assert.Equal(R + "\u0935\u0930\u094D \u0967", deva.Headword);
        Assert.Contains("sign \u0905", deva.MeaningHtml);   // a -> Devanagari letter a
        Assert.Contains(ScriptConverter.Convert("varaṇa-sambhattisu", Script.Latin, Script.Devanagari), deva.MeaningHtml);
        Assert.Contains($"Sanskrit {R}vṛ", deva.MeaningHtml);
        Assert.Contains("<see>vāreti</see>", deva.MeaningHtml);                             // targets stay Latin

        var sinh = (await Look(Dpd(), $"{R}var 2", Script.Sinhala)).Single();
        Assert.Equal(ScriptConverter.Convert($"{R}var 2", Script.Latin, Script.Sinhala), sinh.Headword);
        Assert.StartsWith(R + "\u0DC0\u0DBB\u0DCA", sinh.Headword);
        // The <b> tag is preserved around converted text, not converted itself.
        Assert.Contains("<b>" + ScriptConverter.Convert("icchāsu", Script.Latin, Script.Sinhala) + "</b>", sinh.MeaningHtml);
    }

    // ---- the root line on word entries ----

    [Fact]
    public async Task A_word_with_a_root_shows_the_root_line_with_a_link()
    {
        var e = (await Look(Dpd(), "vāreti")).Single();
        Assert.Equal(10, e.LemmaId);
        Assert.Contains($"<div class=\"dpd-root\">root <see>{R}var 1</see> · cover, dress, restrain</div>", e.MeaningHtml);
    }

    [Fact]
    public async Task A_word_without_a_root_shows_nothing_extra()
    {
        var e = (await Look(Dpd(), "paññā")).Single();
        Assert.DoesNotContain("dpd-root", e.MeaningHtml);
        Assert.DoesNotContain("<see>", e.MeaningHtml);
    }

    [Fact]
    public async Task A_word_whose_root_row_is_missing_shows_nothing_extra()
    {
        var e = (await Look(Dpd(), "ghost")).Single();
        Assert.DoesNotContain("dpd-root", e.MeaningHtml);
    }

    [Fact]
    public async Task Links_resolve_in_both_directions()
    {
        var dpd = Dpd();

        // word → root: the link target, looked up as-is, is that root's entry.
        var word = (await Look(dpd, "vāreti")).Single();
        var rootTarget = SeeTargets(word.MeaningHtml).Single();
        var root = (await Look(dpd, rootTarget)).Single();
        Assert.Equal($"{R}var 1", root.Headword);

        // root → word: every word link resolves to an entry whose root is this root.
        foreach (var target in SeeTargets(root.MeaningHtml))
        {
            var back = await Look(dpd, target);
            Assert.NotEmpty(back);
            Assert.All(back, b => Assert.Contains($"<see>{R}var 1</see>", b.MeaningHtml));
        }
    }

    [Fact]
    public async Task A_root_link_followed_in_a_non_Latin_script_resolves()
    {
        // What the panel does on click: NavigateToWord puts PaliToDisplay(target) in the search box.
        var dpd = Dpd();
        var target = SeeTargets((await Look(dpd, "vāreti")).Single().MeaningHtml).Single();
        foreach (var script in new[] { Script.Devanagari, Script.Sinhala, Script.Thai, Script.Myanmar })
        {
            var typed = ScriptConverter.Convert(Any2Ipe.Convert(target), Script.Ipe, script);
            var res = await Look(dpd, typed, script);
            Assert.Equal(ScriptConverter.Convert(target, Script.Latin, script), Assert.Single(res).Headword);
        }
    }

    [Fact]
    public async Task The_panel_renders_root_and_word_links_in_the_display_script()
    {
        var dpd = Dpd();
        string Display(string latin) => ScriptConverter.Convert(Any2Ipe.Convert(latin), Script.Ipe, Script.Devanagari);

        var word = (await Look(dpd, "vāreti", Script.Devanagari)).Single();
        var html = DictionaryHtmlRenderer.Render(word.MeaningHtml, Display, "font", 12);
        Assert.Contains($"href=\"cst-see:{R}var 1\">{R}\u0935\u0930\u094D \u0967</a>", html);

        var root = (await Look(dpd, $"{R}var 1", Script.Devanagari)).Single();
        var rhtml = DictionaryHtmlRenderer.Render(root.MeaningHtml, Display, "font", 12);
        Assert.Contains($"href=\"cst-see:vāreti\">{Display("vāreti")}</a>", rhtml);
    }

    private static string[] SeeTargets(string html) =>
        System.Text.RegularExpressions.Regex.Matches(html, "<see>(.*?)</see>").Select(m => m.Groups[1].Value).ToArray();

    private static int CountOf(string s, string sub)
    {
        int n = 0;
        for (int i = s.IndexOf(sub, StringComparison.Ordinal); i >= 0; i = s.IndexOf(sub, i + sub.Length, StringComparison.Ordinal)) n++;
        return n;
    }
}

/// <summary>
/// The same root lookups against the installed dpd-cst-subset asset. No-op when it is absent (e.g. CI),
/// mirroring <see cref="DictionaryOracleTests"/>.
/// </summary>
[Collection("Sqlite")]
public sealed class DpdRootRealAssetTests
{
    private const string R = "\u221A";

    private static string AssetPath => Path.Combine(
        Environment.GetEnvironmentVariable("HOME") ?? "/Users/fsnow",
        "Library/Application Support/CSTReader/dictionaries/dpd-cst-subset/dpd-cst-subset.db");

    [Fact]
    public async Task Var_finds_both_roots_with_their_fields()
    {
        if (!File.Exists(AssetPath)) return;
        using var p = new SqliteLemmaProvider(AssetPath);
        if (!p.IsAvailable) return;

        var res = await new DpdDictionarySource(p).LookupAsync(new DictionaryRequest("dpd", R + "var", Script.Latin));
        Assert.Equal(new[] { R + "var 1", R + "var 2" }, res.Select(e => e.Headword));
        Assert.Contains("cover, dress, restrain", res[0].MeaningHtml);
        Assert.Contains("wish, choose", res[1].MeaningHtml);
        Assert.Contains("<see>", res[0].MeaningHtml);

        var roots = p.FindRoots(R + "var", true, 10, int.MaxValue)!;
        Assert.Equal("a", roots[0].Root.RootSign);
        Assert.Equal(roots[0].LemmaCount, roots[0].Lemmas.Count);
        Assert.True(roots[0].LemmaCount > 100);
    }

    [Fact]
    public void No_root_in_the_asset_is_truncated_at_the_shipped_cap()
    {
        if (!File.Exists(AssetPath)) return;
        using var p = new SqliteLemmaProvider(AssetPath);
        if (!p.IsAvailable) return;
        var all = p.FindRoots(R, true, 10_000, DpdDictionarySource.MaxRootWords)!;
        Assert.True(all.Count > 700);
        Assert.All(all, r => Assert.Equal(r.LemmaCount, r.Lemmas.Count));
    }
}
