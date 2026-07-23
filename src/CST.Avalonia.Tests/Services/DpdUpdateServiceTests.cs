using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using CST.Avalonia.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CST.Avalonia.Tests.Services;

// Unit tests for DpdUpdateService's testable core (#390/#468): CATALOG manifest parsing, the two-axis version
// comparison, the per-asset version reads (a DPD lemma db vs a lexicon), and the verify→decompress→probe→atomic
// install with preservation of a good existing asset.
public sealed class DpdUpdateServiceTests : IDisposable
{
    private readonly string _dir;

    public DpdUpdateServiceTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"dpd-upd-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    private static byte[] Gzip(byte[] data)
    {
        using var ms = new MemoryStream();
        using (var gz = new GZipStream(ms, CompressionMode.Compress)) gz.Write(data, 0, data.Length);
        return ms.ToArray();
    }

    private static string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b)).ToLowerInvariant();

    // A catalog with a single dpd entry.
    private static byte[] Catalog(string id, string file, string src, string conv, string sha) =>
        Encoding.UTF8.GetBytes(
            "{\"schemaVersion\":1,\"dictionaries\":[{\"id\":\"" + id + "\",\"file\":\"" + file +
            "\",\"sourceVersion\":\"" + src + "\",\"converterVersion\":\"" + conv +
            "\",\"sha256\":\"" + sha + "\",\"compressedBytes\":10,\"rawBytes\":20}]}");

    private static DpdUpdateService.ManifestEntry Entry(string id, string file, string src, string conv, string sha)
        => DpdUpdateService.ParseCatalog(Catalog(id, file, src, conv, sha))!.Single();

    // ---- ParseCatalog ----

    [Fact]
    public void ParseCatalog_reads_valid_entries()
    {
        var json = Encoding.UTF8.GetBytes("""
            {"schemaVersion":1,"dictionaries":[
                {"id":"dpd","file":"dpd-cst-subset.db.gz","sourceVersion":"v0.4.20260531","converterVersion":"3","sha256":"abc","compressedBytes":10,"rawBytes":20},
                {"id":"dppn","file":"dppn.db.gz","sourceVersion":"2025-08","converterVersion":"1","sha256":"def","compressedBytes":30,"rawBytes":40}
            ]}
            """);
        var c = DpdUpdateService.ParseCatalog(json);
        Assert.NotNull(c);
        Assert.Equal(2, c!.Count);
        var dpd = c.Single(e => e.Id == "dpd");
        Assert.Equal("dpd-cst-subset.db.gz", dpd.File);
        Assert.Equal("v0.4.20260531", dpd.SourceVersion);
        Assert.Equal("3", dpd.ConverterVersion);
        Assert.Equal("abc", dpd.Sha256);
        Assert.Equal(20, dpd.RawBytes);
    }

    [Fact]
    public void ParseCatalog_skips_incomplete_entries_but_keeps_good_ones()
    {
        var json = Encoding.UTF8.GetBytes("""
            {"dictionaries":[
                {"id":"dpd","sourceVersion":"v1","converterVersion":"3","sha256":"a"},
                {"id":"dppn","file":"dppn.db.gz","sourceVersion":"2025-08","converterVersion":"1","sha256":"def"}
            ]}
            """);
        var c = DpdUpdateService.ParseCatalog(json);
        Assert.NotNull(c);
        Assert.Single(c!);                         // the dpd entry (missing file) dropped
        Assert.Equal("dppn", c!.Single().Id);
    }

    [Theory]
    [InlineData("not json")]
    [InlineData("[1,2,3]")]                                              // not an object
    [InlineData("""{"schemaVersion":1}""")]                             // no dictionaries array
    [InlineData("""{"dictionaries":{}}""")]                             // dictionaries not an array
    public void ParseCatalog_rejects_malformed(string json)
        => Assert.Null(DpdUpdateService.ParseCatalog(Encoding.UTF8.GetBytes(json)));

    [Fact]
    public void ParseCatalog_empty_array_is_an_empty_list_not_null()
    {
        var c = DpdUpdateService.ParseCatalog(Encoding.UTF8.GetBytes("""{"dictionaries":[]}"""));
        Assert.NotNull(c);
        Assert.Empty(c!);
    }

    // ---- NeedsUpdate (two version axes) ----

    [Fact]
    public void NeedsUpdate_true_when_asset_absent()
        => Assert.True(DpdUpdateService.NeedsUpdate(null, Entry("dpd", "x.gz", "v1", "3", "a")));

    [Fact]
    public void NeedsUpdate_false_when_both_axes_match()
        => Assert.False(DpdUpdateService.NeedsUpdate(
            new DpdUpdateService.InstalledVersion("v1", "3"), Entry("dpd", "x.gz", "v1", "3", "a")));

    [Fact]
    public void NeedsUpdate_true_on_a_new_source_version()
        => Assert.True(DpdUpdateService.NeedsUpdate(
            new DpdUpdateService.InstalledVersion("v1", "3"), Entry("dpd", "x.gz", "v2", "3", "a")));

    [Fact]
    public void NeedsUpdate_true_on_a_converter_bump_even_with_same_source()
        // The second axis: our converter changed with the source unchanged.
        => Assert.True(DpdUpdateService.NeedsUpdate(
            new DpdUpdateService.InstalledVersion("v1", "3"), Entry("dpd", "x.gz", "v1", "4", "a")));

    // ---- InstallFromGzip: verify + decompress + probe + atomic install + preservation ----

    [Fact]
    public void InstallFromGzip_verifies_decompresses_and_installs()
    {
        // A REAL (tiny) valid asset db as the payload — InstallFromGzip probes usability before installing.
        var gz = Gzip(BuildAssetDbBytes(dpd: "v1", conv: "3"));
        var final = Path.Combine(_dir, "sub", "dpd-cst-subset.db");   // note: a not-yet-existing subdir

        DpdUpdateService.InstallFromGzip(gz, Sha(gz), final, DpdUpdateService.ProbeDpdUsable);

        Assert.True(File.Exists(final));
        Assert.Equal("v1", DpdUpdateService.ReadDpdVersion(final)!.SourceVersion);   // installed + readable
        Assert.False(File.Exists(final + ".new"));                    // temp cleaned up
    }

    [Fact]
    public void InstallFromGzip_rejects_a_sha_mismatch_and_preserves_the_existing_asset()
    {
        var final = Path.Combine(_dir, "dpd-cst-subset.db");
        File.WriteAllText(final, "GOOD-OLD-ASSET");
        var gz = Gzip(BuildAssetDbBytes("v1", "3"));

        Assert.Throws<InvalidDataException>(() =>
            DpdUpdateService.InstallFromGzip(gz, "deadbeef", final, DpdUpdateService.ProbeDpdUsable));

        Assert.Equal("GOOD-OLD-ASSET", File.ReadAllText(final));      // untouched
        Assert.False(File.Exists(final + ".new"));                    // temp cleaned up
    }

    [Fact]
    public void InstallFromGzip_rejects_a_valid_gzip_that_is_not_a_usable_asset_and_preserves_existing()
    {
        // Transport is fine (SHA matches its own bytes) but the decompressed content is NOT a usable asset — a bad
        // PUBLISH must not clobber a good install. (fable review — preservation gap)
        var final = Path.Combine(_dir, "dpd-cst-subset.db");
        File.WriteAllText(final, "GOOD-OLD-ASSET");
        var gz = Gzip(Encoding.UTF8.GetBytes("decompresses fine but is not a sqlite asset"));

        Assert.Throws<InvalidDataException>(() =>
            DpdUpdateService.InstallFromGzip(gz, Sha(gz), final, DpdUpdateService.ProbeDpdUsable));

        Assert.Equal("GOOD-OLD-ASSET", File.ReadAllText(final));      // preserved despite a matching SHA
        Assert.False(File.Exists(final + ".new"));
    }

    [Fact]
    public void InstallFromGzip_installs_a_lexicon_asset_probed_by_the_lexicon_reader()
    {
        // The dppn path: a lexicon db installs when it probes usable via the lexicon reader.
        var gz = Gzip(BuildLexiconDbBytes(src: "2025-08", conv: "1"));
        var final = Path.Combine(_dir, "dppn", "dppn.db");

        DpdUpdateService.InstallFromGzip(gz, Sha(gz), final, DpdUpdateService.ProbeLexiconUsable);

        Assert.True(File.Exists(final));
        var v = DpdUpdateService.ReadLexiconVersion(final);
        Assert.Equal("2025-08", v!.SourceVersion);
        Assert.Equal("1", v.ConverterVersion);
    }

    [Fact]
    public void InstallFromGzip_rejects_a_lexicon_with_meta_but_no_entry_table_and_preserves_existing()
    {
        // A converter bug could publish a lexicon whose `meta` is well-stamped (OpenMeta passes) but whose `entry`
        // table is missing — unusable at runtime. The probe uses full Open (not OpenMeta), so it must reject this
        // and NOT clobber the good installed asset. (fable MED-2)
        var final = Path.Combine(_dir, "dppn", "dppn.db");
        Directory.CreateDirectory(Path.GetDirectoryName(final)!);
        File.WriteAllText(final, "GOOD-OLD-DPPN");
        var gz = Gzip(BuildMetaOnlyLexiconDbBytes(src: "2025-08", conv: "1"));

        Assert.Throws<InvalidDataException>(() =>
            DpdUpdateService.InstallFromGzip(gz, Sha(gz), final, DpdUpdateService.ProbeLexiconUsable));

        Assert.Equal("GOOD-OLD-DPPN", File.ReadAllText(final));   // preserved despite a matching SHA + valid meta
        Assert.False(File.Exists(final + ".new"));
    }

    // ---- Staged-swap install on Windows (#394) ----

    [Fact]
    public void ApplyPendingInstall_swaps_a_staged_file_into_place()
    {
        var final = Path.Combine(_dir, "dpd-cst-subset.db");
        File.WriteAllText(final, "OLD");
        File.WriteAllText(final + ".pending", "NEW-STAGED");

        Assert.True(DpdUpdateService.ApplyPendingInstall(final));
        Assert.Equal("NEW-STAGED", File.ReadAllText(final));
        Assert.False(File.Exists(final + ".pending"));   // staged file consumed
    }

    [Fact]
    public void ApplyPendingInstall_is_noop_when_nothing_staged()
    {
        var final = Path.Combine(_dir, "dpd-cst-subset.db");
        File.WriteAllText(final, "OLD");

        Assert.False(DpdUpdateService.ApplyPendingInstall(final));
        Assert.Equal("OLD", File.ReadAllText(final));    // untouched
    }

    [Fact]
    public void InstallFromGzip_stages_when_the_target_is_locked_then_activates_on_apply()
    {
        // Windows-specific: File.Move over an OPEN db throws a sharing violation, so the install stages a
        // ".pending" swap for next launch instead of failing (and never clobbers the good, open asset). POSIX
        // allows the move, so this reproduces only on Windows. (#394)
        if (!OperatingSystem.IsWindows()) return;

        var final = Path.Combine(_dir, "dpd-cst-subset.db");
        File.WriteAllBytes(final, BuildAssetDbBytes(dpd: "v0", conv: "3"));   // a real, openable existing asset
        var gz = Gzip(BuildAssetDbBytes(dpd: "v1", conv: "3"));

        using (var hold = new FileStream(final, FileMode.Open, FileAccess.Read, FileShare.None))   // live provider
        {
            DpdUpdateService.InstallFromGzip(gz, Sha(gz), final, DpdUpdateService.ProbeDpdUsable);   // must NOT throw
            Assert.True(File.Exists(final + ".pending"), "new asset staged for next launch");
            Assert.False(File.Exists(final + ".new"), "temp cleaned up");
        }

        // Lock released → applying the staged swap activates the new asset.
        Assert.True(DpdUpdateService.ApplyPendingInstall(final));
        Assert.Equal("v1", DpdUpdateService.ReadDpdVersion(final)!.SourceVersion);
        Assert.False(File.Exists(final + ".pending"));
    }

    // ---- per-asset version reads ----

    [Fact]
    public void ReadDpdVersion_reads_versions_from_a_real_asset()
    {
        var db = Path.Combine(_dir, "installed.db");
        BuildMetaFixture(db, dpd: "v0.4.20260531", conv: "3");
        var m = DpdUpdateService.ReadDpdVersion(db);
        Assert.NotNull(m);
        Assert.Equal("v0.4.20260531", m!.SourceVersion);
        Assert.Equal("3", m.ConverterVersion);
    }

    [Fact]
    public void ReadDpdVersion_null_when_absent() =>
        Assert.Null(DpdUpdateService.ReadDpdVersion(Path.Combine(_dir, "nope.db")));

    [Fact]
    public void ReadLexiconVersion_reads_versions_from_a_real_lexicon() =>
        Assert.Equal("2025-08", DpdUpdateService.ReadLexiconVersion(
            BuildLexiconDb(Path.Combine(_dir, "lex.db"), src: "2025-08", conv: "1"))!.SourceVersion);

    [Fact]
    public void ReadLexiconVersion_null_when_absent() =>
        Assert.Null(DpdUpdateService.ReadLexiconVersion(Path.Combine(_dir, "nope.db")));

    // ---- fixtures ----

    private void BuildMetaFixture(string path, string dpd, string conv)
    {
        using (var c = new SqliteConnection($"Data Source={path}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE lemma (id INTEGER PRIMARY KEY, lemma TEXT NOT NULL, pos TEXT, gloss TEXT, derived_from TEXT);
                CREATE TABLE form_lemma (form TEXT NOT NULL, lemma_id INTEGER NOT NULL);
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);
                INSERT INTO meta VALUES ('dpd_version','" + dpd + @"'),('converter_version','" + conv + @"');";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();   // release the file handle so callers can read its bytes
    }

    // A minimal but VALID dpd-cst-subset asset (lemma + form_lemma + meta so SqliteLemmaProvider.IsAvailable),
    // returned as raw db bytes for use as a download payload.
    private byte[] BuildAssetDbBytes(string dpd, string conv)
    {
        var p = Path.Combine(_dir, $"payload-{Guid.NewGuid():N}.db");
        BuildMetaFixture(p, dpd, conv);
        var bytes = File.ReadAllBytes(p);
        File.Delete(p);
        return bytes;
    }

    // A minimal but VALID canonical lexicon (meta with schema/source/converter version + an entry) at `path`.
    private string BuildLexiconDb(string path, string src, string conv)
    {
        using (var c = new SqliteConnection($"Data Source={path}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);
                CREATE TABLE entry (id INTEGER PRIMARY KEY, headword TEXT NOT NULL, body_html TEXT);
                INSERT INTO meta VALUES
                    ('schema_version','1'),('source_id','dppn'),('display_name','DPPN'),
                    ('definition_language','en'),('source_version','" + src + @"'),
                    ('converter_version','" + conv + @"');
                INSERT INTO entry (headword, body_html) VALUES ('Nāgita','<p>a monk</p>');";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        return path;
    }

    private byte[] BuildLexiconDbBytes(string src, string conv)
    {
        var p = BuildLexiconDb(Path.Combine(_dir, $"lex-{Guid.NewGuid():N}.db"), src, conv);
        var bytes = File.ReadAllBytes(p);
        File.Delete(p);
        return bytes;
    }

    // A lexicon with a valid, stamped `meta` but NO `entry` table — OpenMeta succeeds, Open fails.
    private byte[] BuildMetaOnlyLexiconDbBytes(string src, string conv)
    {
        var p = Path.Combine(_dir, $"lexmeta-{Guid.NewGuid():N}.db");
        using (var c = new SqliteConnection($"Data Source={p}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);
                INSERT INTO meta VALUES
                    ('schema_version','1'),('source_id','dppn'),('display_name','DPPN'),
                    ('definition_language','en'),('source_version','" + src + @"'),
                    ('converter_version','" + conv + @"');";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
        var bytes = File.ReadAllBytes(p);
        File.Delete(p);
        return bytes;
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
