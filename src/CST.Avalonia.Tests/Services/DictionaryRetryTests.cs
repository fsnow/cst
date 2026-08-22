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

    // The real install paths, sampled around EVERY test in this class.
    //
    // A per-test assertion would only have covered the test that wrote it, and the incident this guards
    // against was a shared fixture helper — the one place a per-test check does not look. Sampling in the
    // constructor and verifying in Dispose means any test here that reaches the real user data directory, by
    // any route, fails the class. (#773, fable)
    private static readonly string[] RealPaths =
        { DpdUpdateService.DpdSubsetPath, DpdUpdateService.DppnLexiconPath };

    private readonly string[] _realBefore;

    public DictionaryRetryTests()
    {
        _root = Path.Combine(Path.GetTempPath(), $"dict-retry-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_root);
        _realBefore = RealPaths.Select(Snapshot).ToArray();
    }

    // Existence, size and last-write time: enough to catch a file created, replaced or truncated.
    private static string Snapshot(string path) =>
        File.Exists(path)
            ? $"{path}|{File.GetLastWriteTimeUtc(path):O}|{new FileInfo(path).Length}"
            : $"{path}|absent";

    private DpdUpdateService Service(bool automaticUpdates = true, string? repositoryOwner = null)
    {
        var settings = new Mock<ISettingsService>();
        settings.SetupGet(s => s.Settings).Returns(new Settings
        {
            DpdUpdateSettings = new DpdUpdateSettings
            {
                EnableAutomaticUpdates = automaticUpdates,
                RepositoryOwner = repositoryOwner ?? "no-such-owner",
                RepositoryName = "no-such-repository",
            }
        });
        // Pointed at a closed local port, so no test here reaches the network.
        //
        // The previous version used a repository owner of "invalid.invalid" and a comment claiming that could
        // not resolve. It is a PATH SEGMENT, not a host: Octokit still called api.github.com and took a 404,
        // six times per suite run, against a shared unauthenticated rate limit. The tests were green and the
        // isolation claim was false — which is worse than being obviously online, because nobody looks again.
        // (fable)
        return new DpdUpdateService(
            NullLogger<DpdUpdateService>.Instance, settings.Object, _root,
            gitHubBaseAddress: new Uri("http://127.0.0.1:1/"));
    }

    // ---- the retry ----

    [Fact]
    public async Task With_both_assets_present_the_retry_returns_without_running_a_check()
    {
        BuildDpd();
        BuildDppn();
        var svc = Service();
        var status = new System.Collections.Concurrent.ConcurrentBag<string>();
        svc.StatusChanged += m => status.Add(m);

        await svc.RetryMissingAsync();

        // StatusChanged is the discriminator, and it has to be: with both files present a run that DID happen
        // would reconcile and record nothing, so the failure set cannot tell the two apart, and IsBusy is
        // false after the await either way. The first thing a run does is announce "Checking for dictionary
        // data updates..." — so silence is proof the early return fired. The previous version asserted those
        // two proxies and passed under exactly the mutation it existed to catch. (fable)
        Assert.Empty(status);
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

        // Fails the test that just ran if anything here touched a real dictionary — created, replaced or
        // truncated. After the temp cleanup, so a failure does not leak the temp dir too.
        Assert.Equal(_realBefore, RealPaths.Select(Snapshot).ToArray());
    }
}
