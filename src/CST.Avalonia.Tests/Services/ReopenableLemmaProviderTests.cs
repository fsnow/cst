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

    [Fact]
    public void Dispose_releases_every_instance_it_has_opened()
    {
        var path = Path.Combine(_dir, "dpd-cst-subset.db");
        BuildAssetDb(path);
        var provider = new ReopenableLemmaProvider(path);
        provider.Reopen();
        provider.Reopen();
        Assert.True(provider.IsAvailable);

        provider.Dispose();
        SqliteConnection.ClearAllPools();

        // A retired instance still holding the file would make this throw on Windows, which is precisely the
        // platform #563 is held open to verify.
        File.Delete(path);
        Assert.False(File.Exists(path));
    }

    // A minimal but VALID dpd-cst-subset asset: the tables SqliteLemmaProvider requires, plus one form→lemma
    // row so a resolve can prove the wrapper is really querying the new file.
    private static void BuildAssetDb(string path, string lemma = "dhamma")
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
        SqliteConnection.ClearAllPools();
    }

    public void Dispose()
    {
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
