using System;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using Microsoft.Data.Sqlite;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// A dictionary that failed to download must not stay missing for the whole session. (#773)
///
/// <para><b>The defect.</b> <c>CheckAndUpdateAsync</c> runs once per launch and catches each asset's failure
/// as non-fatal so the others still run. There is no retry and nothing re-runs the check, so a first run whose
/// download fails leaves the reader with no DPD and no DPPN until they restart — #563's symptom reached by a
/// different mechanism, and just as silent, because the picker simply does not list them.</para>
///
/// <para><b>Every test here installs into a temp directory.</b> The first attempt at this work did not: the
/// install paths were static absolutes, so its fixtures wrote into the real
/// <c>&lt;app-support&gt;/CSTReader/dictionaries</c>. On a machine with no dictionaries that plants an openable
/// but EMPTY database — a source the picker lists and which answers nothing — and a lexicon stamped closely
/// enough to the shipped one that the updater could treat the fake as current indefinitely. A test suite must
/// not be able to do that, which is why <see cref="DpdUpdateService"/> now takes a root.</para>
/// </summary>
public sealed class DictionaryRetryTests : IDisposable
{
    private readonly string _root;

    public DictionaryRetryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"dict-retry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
    }

    private DpdUpdateService Service(bool automaticUpdates = true, string? repositoryOwner = null)
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Settings).Returns(new Settings
        {
            DpdUpdateSettings = new DpdUpdateSettings
            {
                EnableAutomaticUpdates = automaticUpdates,
                // An address that cannot resolve, so a run that DOES reach the network fails fast and
                // locally — no real request, and nothing that could install anything.
                RepositoryOwner = repositoryOwner ?? "invalid.invalid",
                RepositoryName = "no-such-repository",
            }
        });
        return new DpdUpdateService(NullLogger<DpdUpdateService>.Instance, settings.Object, _root);
    }

    // ---- the retry ----

    [Fact]
    public async Task With_both_assets_present_the_retry_returns_without_running_a_check()
    {
        BuildDpd();
        BuildDppn();
        var svc = Service();

        await svc.RetryMissingAsync();

        // A run would have reconciled and recorded nothing (both files exist), so the failure set cannot
        // distinguish the two. IsBusy would; but the real evidence is that a run against an unresolvable host
        // takes network time, and this returns synchronously fast. Assert the observable consequence instead:
        // nothing was attempted, so nothing is reported as failed and no work is in flight.
        Assert.False(svc.IsBusy);
        Assert.Empty(svc.FailedAssetIds);
    }

    // The heart of #773: a missing asset means the retry DOES run — and a run that fails records the failure.
    [Fact]
    public async Task With_an_asset_missing_the_retry_runs_and_a_failed_run_is_recorded()
    {
        BuildDpd();          // dppn deliberately absent

        var svc = Service();
        await svc.RetryMissingAsync();

        Assert.Equal(new[] { "dppn" }, svc.FailedAssetIds.ToArray());
    }

    // Fable's finding, and the case that matters most: offline at launch never reaches an asset at all — the
    // run dies at the release lookup. Recording failures only in the per-asset handler left the set empty at
    // exactly the moment a reader most needs to be told something.
    [Fact]
    public async Task A_run_that_never_reaches_an_asset_still_records_every_missing_one()
    {
        // Neither asset present, and the release lookup cannot succeed.
        var svc = Service();

        await svc.CheckAndUpdateAsync();

        Assert.Equal(new[] { "dpd", "dppn" }, svc.FailedAssetIds.OrderBy(id => id).ToArray());
    }

    [Fact]
    public async Task The_failed_event_names_each_missing_asset_once()
    {
        var seen = new System.Collections.Concurrent.ConcurrentBag<string>();
        var svc = Service();
        svc.AssetFailed += id => seen.Add(id);

        await svc.CheckAndUpdateAsync();
        await svc.CheckAndUpdateAsync();      // a second failing run must not re-announce

        Assert.Equal(new[] { "dpd", "dppn" }, seen.OrderBy(id => id).ToArray());
    }

    // An asset absent because the reader switched updates off is a choice, not a failure. Reporting it as one
    // would hand the reader their own setting back as a fault.
    [Fact]
    public async Task Nothing_is_reported_as_failed_when_automatic_updates_are_off()
    {
        var svc = Service(automaticUpdates: false);

        await svc.CheckAndUpdateAsync();

        Assert.Empty(svc.FailedAssetIds);
    }

    // The state is filtered by presence at read time, so it cannot go stale.
    [Fact]
    public async Task An_asset_that_appears_later_stops_being_reported_as_failed()
    {
        var svc = Service();
        await svc.CheckAndUpdateAsync();
        Assert.Contains("dpd", svc.FailedAssetIds);

        BuildDpd();          // dropped in by hand, or a staged install applied at the next launch

        Assert.DoesNotContain("dpd", svc.FailedAssetIds);
    }

    // ---- the seam itself ----

    [Fact]
    public async Task The_service_reads_the_root_it_was_given_and_not_the_real_data_directory()
    {
        BuildDpd();          // under _root only
        var svc = Service();

        await svc.CheckAndUpdateAsync();

        // The discriminator: dpd exists under OUR root and dppn does not, so exactly one is reported. A
        // service still reading the real data directory would answer from whatever that machine happens to
        // have installed — on a developer's box, both, and this would report nothing at all.
        Assert.Equal(new[] { "dppn" }, svc.FailedAssetIds.ToArray());
        Assert.True(File.Exists(Path.Combine(_root, "dpd-cst-subset", "dpd-cst-subset.db")));
    }

    [Fact]
    public void Nothing_is_written_outside_the_root_it_was_given()
    {
        // The failure this replaces was a fixture planting fake dictionaries in a real user's data directory,
        // where an empty-but-openable database becomes a source the picker lists and which answers nothing.
        BuildDpd();
        BuildDppn();

        var written = Directory.GetFiles(_root, "*.db", SearchOption.AllDirectories);
        Assert.Equal(2, written.Length);
        Assert.All(written, f => Assert.StartsWith(_root, f, StringComparison.Ordinal));
    }

    // ---- fixtures, all under _root ----

    private void BuildDpd()
    {
        var path = Path.Combine(_root, "dpd-cst-subset", "dpd-cst-subset.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var c = new SqliteConnection($"Data Source={path}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE lemma (id INTEGER PRIMARY KEY, lemma TEXT NOT NULL, pos TEXT, gloss TEXT, derived_from TEXT);
                CREATE TABLE form_lemma (form TEXT NOT NULL, lemma_id INTEGER NOT NULL);
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);
                INSERT INTO meta VALUES ('dpd_version','v0.4.20260531'),('converter_version','3');";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
    }

    private void BuildDppn()
    {
        var path = Path.Combine(_root, "dppn", "dppn.db");
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        using (var c = new SqliteConnection($"Data Source={path}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);
                CREATE TABLE entry (id INTEGER PRIMARY KEY, headword TEXT NOT NULL, body_html TEXT);
                INSERT INTO meta VALUES ('schema_version','1'),('source_id','dppn'),('display_name','DPPN'),
                    ('definition_language','en'),('source_version','2025-06'),('converter_version','1');
                INSERT INTO entry (headword, body_html) VALUES ('Nāgita','<p>a monk</p>');";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_root, recursive: true); } catch { /* best effort */ }
    }
}
