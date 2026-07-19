using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;
using CST.Avalonia.Services;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CST.Avalonia.Tests.Services;

// Unit tests for DpdUpdateService's testable core (#390): manifest parsing, the two-axis version comparison,
// the installed-meta read, and the verify→decompress→atomic-install with preservation of a good existing asset.
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

    private static byte[] Manifest(string file, string dpd, string conv, string sha) =>
        Encoding.UTF8.GetBytes(
            $$"""{"file":"{{file}}","dpdVersion":"{{dpd}}","converterVersion":"{{conv}}","schemaVersion":"2","scope":"full","sha256":"{{sha}}","compressedBytes":10,"rawBytes":20}""");

    // ---- ParseManifest ----

    [Fact]
    public void ParseManifest_reads_a_valid_manifest()
    {
        var m = DpdUpdateService.ParseManifest(Manifest("dpd-cst-subset.db.gz", "v0.4.20260531", "3", "abc123"));
        Assert.NotNull(m);
        Assert.Equal("dpd-cst-subset.db.gz", m!.File);
        Assert.Equal("v0.4.20260531", m.DpdVersion);
        Assert.Equal("3", m.ConverterVersion);
        Assert.Equal("abc123", m.Sha256);
        Assert.Equal("full", m.Scope);
    }

    [Theory]
    [InlineData("""{"dpdVersion":"v1","converterVersion":"3","sha256":"a"}""")]        // no file
    [InlineData("""{"file":"x.gz","converterVersion":"3","sha256":"a"}""")]            // no dpdVersion
    [InlineData("""{"file":"x.gz","dpdVersion":"v1","sha256":"a"}""")]                 // no converterVersion
    [InlineData("""{"file":"x.gz","dpdVersion":"v1","converterVersion":"3"}""")]        // no sha256
    [InlineData("not json")]
    [InlineData("[1,2,3]")]                                                             // not an object
    public void ParseManifest_rejects_incomplete_or_malformed(string json)
        => Assert.Null(DpdUpdateService.ParseManifest(Encoding.UTF8.GetBytes(json)));

    // ---- NeedsUpdate (two version axes) ----

    [Fact]
    public void NeedsUpdate_true_when_asset_absent()
    {
        var m = DpdUpdateService.ParseManifest(Manifest("x.gz", "v1", "3", "a"))!;
        Assert.True(DpdUpdateService.NeedsUpdate(null, m));
    }

    [Fact]
    public void NeedsUpdate_false_when_both_axes_match()
    {
        var m = DpdUpdateService.ParseManifest(Manifest("x.gz", "v1", "3", "a"))!;
        Assert.False(DpdUpdateService.NeedsUpdate(new DpdUpdateService.InstalledMeta("v1", "3"), m));
    }

    [Fact]
    public void NeedsUpdate_true_on_a_new_dpd_version()
    {
        var m = DpdUpdateService.ParseManifest(Manifest("x.gz", "v2", "3", "a"))!;
        Assert.True(DpdUpdateService.NeedsUpdate(new DpdUpdateService.InstalledMeta("v1", "3"), m));
    }

    [Fact]
    public void NeedsUpdate_true_on_a_converter_bump_even_with_same_dpd()
    {
        // The second axis: our converter changed (e.g. the meaning_2 coalesce) with DPD unchanged.
        var m = DpdUpdateService.ParseManifest(Manifest("x.gz", "v1", "4", "a"))!;
        Assert.True(DpdUpdateService.NeedsUpdate(new DpdUpdateService.InstalledMeta("v1", "3"), m));
    }

    // ---- InstallFromGzip: verify + decompress + atomic install + preservation ----

    [Fact]
    public void InstallFromGzip_verifies_decompresses_and_installs()
    {
        // A REAL (tiny) valid asset db as the payload — InstallFromGzip now probes that the decompressed file is a
        // usable asset before installing.
        var gz = Gzip(BuildAssetDbBytes(dpd: "v1", conv: "3"));
        var final = Path.Combine(_dir, "sub", "dpd-cst-subset.db");   // note: a not-yet-existing subdir

        DpdUpdateService.InstallFromGzip(gz, Sha(gz), final);

        Assert.True(File.Exists(final));
        Assert.Equal("v1", DpdUpdateService.ReadInstalledMeta(final)!.DpdVersion);  // installed + readable
        Assert.False(File.Exists(final + ".new"));                    // temp cleaned up
    }

    [Fact]
    public void InstallFromGzip_rejects_a_sha_mismatch_and_preserves_the_existing_asset()
    {
        var final = Path.Combine(_dir, "dpd-cst-subset.db");
        File.WriteAllText(final, "GOOD-OLD-ASSET");
        var gz = Gzip(BuildAssetDbBytes("v1", "3"));

        Assert.Throws<InvalidDataException>(() => DpdUpdateService.InstallFromGzip(gz, "deadbeef", final));

        Assert.Equal("GOOD-OLD-ASSET", File.ReadAllText(final));      // untouched
        Assert.False(File.Exists(final + ".new"));                    // temp cleaned up
    }

    [Fact]
    public void InstallFromGzip_rejects_a_valid_gzip_that_is_not_a_usable_asset_and_preserves_existing()
    {
        // Transport is fine (SHA matches its own bytes) but the decompressed content is NOT a usable dpd-cst-subset
        // db — a bad PUBLISH must not clobber a good install. (fable review — preservation gap)
        var final = Path.Combine(_dir, "dpd-cst-subset.db");
        File.WriteAllText(final, "GOOD-OLD-ASSET");
        var gz = Gzip(Encoding.UTF8.GetBytes("decompresses fine but is not a sqlite asset"));

        Assert.Throws<InvalidDataException>(() => DpdUpdateService.InstallFromGzip(gz, Sha(gz), final));

        Assert.Equal("GOOD-OLD-ASSET", File.ReadAllText(final));      // preserved despite a matching SHA
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
            DpdUpdateService.InstallFromGzip(gz, Sha(gz), final);            // must NOT throw
            Assert.True(File.Exists(final + ".pending"), "new asset staged for next launch");
            Assert.False(File.Exists(final + ".new"), "temp cleaned up");
        }

        // Lock released → applying the staged swap activates the new asset.
        Assert.True(DpdUpdateService.ApplyPendingInstall(final));
        Assert.Equal("v1", DpdUpdateService.ReadInstalledMeta(final)!.DpdVersion);
        Assert.False(File.Exists(final + ".pending"));
    }

    // ---- ReadInstalledMeta ----

    [Fact]
    public void ReadInstalledMeta_reads_versions_from_a_real_asset()
    {
        var db = Path.Combine(_dir, "installed.db");
        BuildMetaFixture(db, dpd: "v0.4.20260531", conv: "3");
        var m = DpdUpdateService.ReadInstalledMeta(db);
        Assert.NotNull(m);
        Assert.Equal("v0.4.20260531", m!.DpdVersion);
        Assert.Equal("3", m.ConverterVersion);
    }

    [Fact]
    public void ReadInstalledMeta_null_when_absent() =>
        Assert.Null(DpdUpdateService.ReadInstalledMeta(Path.Combine(_dir, "nope.db")));

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

    // A minimal but VALID dpd-cst-subset asset (has lemma + form_lemma + meta so SqliteLemmaProvider.IsAvailable),
    // returned as the raw db bytes for use as a download payload.
    private byte[] BuildAssetDbBytes(string dpd, string conv)
    {
        var p = Path.Combine(_dir, $"payload-{Guid.NewGuid():N}.db");
        BuildMetaFixture(p, dpd, conv);
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
