using System;
using System.IO;
using CST.Avalonia.Services;
using CST.Lemma;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// The wrapper that made a mid-session DPD install go live without a restart (#536), kept under test because
/// #563 is held open to VERIFY that behaviour on beta 6 and the class had no coverage at all.
///
/// <para>What is actually being pinned here is the singleton contract: <c>LemmaSearchService</c>,
/// <c>LemmaReportService</c> and <c>DpdDictionarySource</c> each capture the <see cref="ILemmaProvider"/>
/// reference at construction. If <see cref="ReopenableLemmaProvider.Reopen"/> ever handed back a NEW object
/// instead of swapping the inner one, every one of those holders would keep querying the dead instance and
/// the first-run bug would come straight back — silently, and only on a machine where the asset arrives
/// after startup.</para>
/// </summary>
public sealed class ReopenableLemmaProviderTests : IDisposable
{
    private readonly string _dir;

    public ReopenableLemmaProviderTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"reopen-lemma-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    [Fact]
    public void Is_unavailable_while_the_asset_is_absent()
    {
        using var provider = new ReopenableLemmaProvider(Path.Combine(_dir, "absent.db"));
        Assert.False(provider.IsAvailable);
        Assert.Null(provider.Meta);
        Assert.Null(provider.ResolveForm("dhammaṃ"));
    }

    // The first-run case exactly: the app starts with no asset, the download finishes mid-session, Reopen runs.
    [Fact]
    public void Goes_available_when_the_asset_lands_and_Reopen_runs()
    {
        var path = Path.Combine(_dir, "dpd-cst-subset.db");
        using var provider = new ReopenableLemmaProvider(path);
        Assert.False(provider.IsAvailable);

        BuildAssetDb(path);
        // Still false: SqliteLemmaProvider binds availability in its constructor, which is the whole reason
        // this wrapper exists. Nothing but Reopen can change it.
        Assert.False(provider.IsAvailable);

        provider.Reopen();
        Assert.True(provider.IsAvailable);
    }

    // The reason Reopen swaps IN PLACE rather than returning a replacement.
    [Fact]
    public void A_reference_captured_before_the_asset_landed_sees_it_afterwards()
    {
        var path = Path.Combine(_dir, "dpd-cst-subset.db");
        using var provider = new ReopenableLemmaProvider(path);

        // What DpdDictionarySource and LemmaSearchService hold: the interface, captured at startup.
        ILemmaProvider captured = provider;
        Assert.False(captured.IsAvailable);

        BuildAssetDb(path);
        provider.Reopen();

        Assert.True(captured.IsAvailable);
        Assert.NotNull(captured.ResolveForm("dhammaṃ"));
    }

    // The AssetInstalled handler in App.axaml.cs calls Reopen unconditionally for the dpd asset; a download
    // that reported success but left nothing on disk must not take the app down.
    [Fact]
    public void Reopen_with_the_asset_still_absent_is_safe_and_stays_unavailable()
    {
        using var provider = new ReopenableLemmaProvider(Path.Combine(_dir, "absent.db"));
        provider.Reopen();
        provider.Reopen();
        Assert.False(provider.IsAvailable);
    }

    // ---- an asset REPLACED mid-session, not merely installed (#869) ------------------------------------

    /// <summary>
    /// After an update replaces the asset in place, the provider serves the new file.
    ///
    /// <para>#536's case was a first install, where there is no pool yet and nothing can go wrong. An
    /// <b>update</b> is the hard one: <c>File.Move(overwrite: true)</c> on macOS and Linux is a rename that
    /// succeeds over open handles, so the replacement provider used to build the same connection string, be
    /// handed a pooled handle still bound to the replaced file's old inode, and go on answering from the
    /// superseded asset — while the log said the new one was active. Until the next launch.</para>
    ///
    /// <para>Note what this test does NOT do: clear the pools. The fixture used to, unconditionally, which is
    /// why this class had four passing tests and the bug shipped anyway.</para>
    /// </summary>
    [UnixFact("File.Move over an open SQLite handle throws on Windows — which is exactly why the "
        + "installer stages a .pending file there and does not raise AssetInstalled at all")]
    public void An_asset_replaced_mid_session_is_served_from_the_new_file()
    {
        var path = Path.Combine(_dir, "dpd-cst-subset.db");
        BuildAssetDb(path, "dhamma");

        using var provider = new ReopenableLemmaProvider(path);
        Assert.NotNull(provider.ResolveForm("dhammaṃ"));      // pools a handle on the file as it stands now

        // What DpdUpdateService.InstallFromGzip does: build beside it, then rename over the live file.
        var replacement = path + ".new";
        BuildAssetDb(replacement, "citta", clearPools: false);
        File.Move(replacement, path, overwrite: true);

        provider.Reopen();

        Assert.NotNull(provider.ResolveForm("cittaṃ"));
        Assert.Null(provider.ResolveForm("dhammaṃ"));
    }

    /// <summary>
    /// A live connection on the same path cannot pin the replaced file either.
    ///
    /// <para>The pool was one of two things holding the old inode. <c>Cache=Shared</c> was the other, and it
    /// works independently: a shared cache is keyed by file PATH, so while any connection holds one open, a
    /// brand-new connection on that path joins it and reads through the surviving pager — the replaced
    /// file's. Clearing the pool does not touch that. The connection here stands in for the ordinary case of
    /// a GUI lookup in flight while the install completes.</para>
    /// </summary>
    [UnixFact("File.Move over an open SQLite handle throws on Windows — which is exactly why the "
        + "installer stages a .pending file there and does not raise AssetInstalled at all")]
    public void A_shared_cache_connection_held_open_across_the_replacement_does_not_pin_it()
    {
        var path = Path.Combine(_dir, "dpd-cst-subset.db");
        BuildAssetDb(path, "dhamma");

        using var provider = new ReopenableLemmaProvider(path);
        Assert.NotNull(provider.ResolveForm("dhammaṃ"));

        using var pinned = new SqliteConnection(new SqliteConnectionStringBuilder
        {
            DataSource = path, Mode = SqliteOpenMode.ReadOnly, Cache = SqliteCacheMode.Shared,
        }.ToString());
        pinned.Open();

        var replacement = path + ".new";
        BuildAssetDb(replacement, "citta", clearPools: false);
        File.Move(replacement, path, overwrite: true);

        provider.Reopen();

        Assert.NotNull(provider.ResolveForm("cittaṃ"));
        Assert.Null(provider.ResolveForm("dhammaṃ"));
    }

    // A minimal but VALID dpd-cst-subset asset: the tables SqliteLemmaProvider requires, plus one form→lemma
    // row so a resolve can prove the wrapper is really querying the new file.
    /// <param name="clearPools">Left true for setup. A test reproducing #869 must pass false: clearing every
    /// pool is exactly what masked the bug here for a release — the pooled handle on the replaced file is the
    /// whole mechanism, and a fixture that empties the pool tests a state the app is never in.</param>
    private static void BuildAssetDb(string path, string lemma = "dhamma", bool clearPools = true)
    {
        using (var c = new SqliteConnection($"Data Source={path}"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = @"
                CREATE TABLE lemma (id INTEGER PRIMARY KEY, lemma TEXT NOT NULL, pos TEXT, gloss TEXT, derived_from TEXT);
                CREATE TABLE form_lemma (form TEXT NOT NULL, lemma_id INTEGER NOT NULL);
                CREATE TABLE meta (key TEXT PRIMARY KEY, value TEXT);
                INSERT INTO meta VALUES ('dpd_version','v0.4.20260531'),('converter_version','3');
                INSERT INTO lemma (id, lemma, pos, gloss) VALUES (1, $lemma, 'nt', 'a test gloss');
                INSERT INTO form_lemma (form, lemma_id) VALUES ($form, 1);";
            cmd.Parameters.AddWithValue("$lemma", lemma);
            cmd.Parameters.AddWithValue("$form", lemma + "ṃ");   // -ṃ, an accusative singular
            cmd.ExecuteNonQuery();
        }
        if (clearPools) SqliteConnection.ClearAllPools();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
