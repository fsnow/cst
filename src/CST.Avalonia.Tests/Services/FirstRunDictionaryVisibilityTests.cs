using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Dictionaries;
using CST.Lemma;
using CST.Tools;
using Microsoft.Data.Sqlite;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// The first-run sequence a reader actually reported (#563): launch with no dictionaries installed, let DPD and
/// DPPN download in the background, and expect both in the picker WITHOUT visiting Settings and WITHOUT a
/// restart. Fixed by #536/PR #539 two days after the beta 5 tag, and held open to verify on beta 6.
///
/// <para>These tests assemble the real chain the app wires in <c>App.axaml.cs</c> — the registry built by
/// <see cref="DictionarySourceFactory"/> over a <see cref="ReopenableLemmaProvider"/>, and the picker list
/// computed by <see cref="DictionarySourcePreferenceService"/> — and drive it with the same two moves the
/// <c>AssetInstalled</c> handlers make: reopen the lemma provider, re-query the preference. Every piece was
/// covered on its own; the JOIN was not, which is why the defect could only be found by hand.</para>
///
/// <para>What this cannot cover is the one Windows-only branch: an install that cannot replace a locked
/// database stages itself instead and deliberately stays quiet, since it is not live until the next launch.
/// A clean first run has nothing to lock — see
/// <c>DpdUpdateServiceTests.InstallFromGzip_into_an_empty_directory_reports_the_asset_live_not_staged</c>.</para>
/// </summary>
public sealed class FirstRunDictionaryVisibilityTests : IDisposable
{
    private readonly string _dir;
    private readonly string _dpdPath;
    private readonly string _dppnPath;

    public FirstRunDictionaryVisibilityTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"first-run-dict-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
        _dpdPath = Path.Combine(_dir, "dpd-cst-subset", "dpd-cst-subset.db");
        _dppnPath = Path.Combine(_dir, "dppn", "dppn.db");
        Directory.CreateDirectory(Path.GetDirectoryName(_dpdPath)!);
        Directory.CreateDirectory(Path.GetDirectoryName(_dppnPath)!);
    }

    // The reported bug, as one test: bundled dictionaries only at launch, then both downloads land.
    [Fact]
    public void Both_downloaded_dictionaries_join_the_picker_with_no_settings_visit_and_no_restart()
    {
        var (lemma, prefs) = BuildApp();

        // At launch: the bundled VRI dictionaries and nothing else — Antonio's first screenshot.
        Assert.Equal(new[] { "en", "hi" }, PickerIds(prefs));

        // The background download finishes for both assets.
        BuildDpdAsset(_dpdPath);
        BuildDppnLexicon(_dppnPath);

        // What the AssetInstalled handlers do. Only DPD needs the reopen — the lexicon source re-stamps the
        // file itself — but the picker has to be told to re-query in both cases.
        lemma.Reopen();

        Assert.Equal(new[] { "en", "hi", "dpd", "dppn" }, PickerIds(prefs));
    }

    // The asymmetry in the report — "Settings ▸ Dictionary → DPPN becomes visible immediately", but DPD needed
    // a restart — was the tell that the two sources fail differently. This pins each half separately so a
    // regression names which one broke.
    [Fact]
    public void The_lexicon_source_goes_live_on_the_file_alone()
    {
        var (_, prefs) = BuildApp();
        Assert.DoesNotContain("dppn", PickerIds(prefs));

        BuildDppnLexicon(_dppnPath);

        // No reopen, no restart: SqliteDictionarySource re-reads its meta when the file changes.
        Assert.Contains("dppn", PickerIds(prefs));
    }

    [Fact]
    public void The_dpd_source_stays_dark_until_the_lemma_provider_is_reopened()
    {
        var (lemma, prefs) = BuildApp();
        BuildDpdAsset(_dpdPath);

        // The file is there and the app is still showing the bundled set. This is the whole of #536: the
        // provider bound IsAvailable in its constructor, so the asset on disk changes nothing by itself.
        Assert.DoesNotContain("dpd", PickerIds(prefs));

        lemma.Reopen();

        Assert.Contains("dpd", PickerIds(prefs));
    }

    // An asset arriving must not move the reader somewhere they did not ask to be. The picker gains entries at
    // the end; the first enabled source — the default — is unchanged.
    [Fact]
    public void An_arriving_asset_is_appended_and_does_not_change_the_default_source()
    {
        var (lemma, prefs) = BuildApp();
        var defaultBefore = PickerIds(prefs).First();

        BuildDpdAsset(_dpdPath);
        BuildDppnLexicon(_dppnPath);
        lemma.Reopen();

        var after = PickerIds(prefs);
        Assert.Equal(defaultBefore, after.First());
        // Appended, not interleaved — but without pinning the factory's own registration order between the
        // two new sources, which is not what this test is about.
        Assert.Equal(new[] { "en", "hi" }, after.Take(2).ToArray());
        Assert.Equal(new[] { "dpd", "dppn" }, after.Skip(2).OrderBy(id => id).ToArray());
    }

    // A reader who disabled a source before it was ever installed must not have that undone by the install.
    [Fact]
    public void A_source_the_reader_disabled_stays_out_of_the_picker_when_its_asset_lands()
    {
        var (lemma, prefs, state) = BuildAppWithState();
        BuildDpdAsset(_dpdPath);
        BuildDppnLexicon(_dppnPath);
        lemma.Reopen();
        Assert.Contains("dppn", PickerIds(prefs));

        prefs.SetEnabled("dppn", false);

        Assert.DoesNotContain("dppn", PickerIds(prefs));
        Assert.Contains("dpd", PickerIds(prefs));
        // And the choice is recorded, so it survives the restart that used to be the workaround.
        Assert.Contains(state.DictionaryDialog.SourceOrder, p =>
            string.Equals(p.Id, "dppn", StringComparison.OrdinalIgnoreCase) && !p.Enabled);
    }

    // ---- the notification layer: what tells the app an asset landed ----

    // The sharpest regression fable found: App.BindLemmaReopen pattern-matches the registered provider, so a
    // DI registration changed back to a bare SqliteLemmaProvider would make the reopen SILENTLY do nothing —
    // no error, no failing test, and #536 returns. The binding now reports that, and this pins it.
    [Fact]
    public void Binding_the_reopen_to_a_provider_that_cannot_reopen_is_reported_not_ignored()
    {
        var updates = new FakeUpdates();
        using var raw = new SqliteLemmaProvider(_dpdPath);

        Assert.False(App.BindLemmaReopen(updates, raw));
        Assert.Equal(0, updates.SubscriberCount);
    }

    [Fact]
    public void Binding_the_reopen_survives_a_missing_provider()
    {
        var updates = new FakeUpdates();
        Assert.False(App.BindLemmaReopen(updates, null));
        Assert.Equal(0, updates.SubscriberCount);
    }

    // The bound handler is what actually reopens on a first run.
    [Fact]
    public void The_bound_handler_reopens_the_provider_when_the_dpd_asset_lands()
    {
        var (lemma, prefs) = BuildApp();
        var updates = new FakeUpdates();
        Assert.True(App.BindLemmaReopen(updates, lemma));

        BuildDpdAsset(_dpdPath);
        Assert.DoesNotContain("dpd", PickerIds(prefs));

        updates.RaiseAssetInstalled(App.DpdAssetId);

        Assert.Contains("dpd", PickerIds(prefs));
    }

    // A lexicon install must not reopen the lemma provider — it is a different asset, and reopening on every
    // event would retire a live inner provider for nothing.
    [Fact]
    public void The_bound_handler_ignores_an_asset_that_is_not_dpd()
    {
        var (lemma, _) = BuildApp();
        var updates = new FakeUpdates();
        Assert.True(App.BindLemmaReopen(updates, lemma));

        BuildDpdAsset(_dpdPath);
        updates.RaiseAssetInstalled("dppn");

        Assert.False(lemma.IsAvailable);   // not reopened
    }

    private sealed class FakeUpdates : IDpdUpdateService
    {
        public event Action<string>? StatusChanged;
        public event Action<string>? AssetInstalled;
        public event Action<long, long>? DownloadProgressChanged;
        public event Action<string>? AssetFailed;
        public bool IsBusy => false;
        public IReadOnlyCollection<string> FailedAssetIds => System.Array.Empty<string>();
        public Task CheckAndUpdateAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RetryMissingAsync(CancellationToken ct = default) => Task.CompletedTask;

        public int SubscriberCount => AssetInstalled?.GetInvocationList().Length ?? 0;
        public void RaiseAssetInstalled(string id) => AssetInstalled?.Invoke(id);

        // Silences the unused-event warnings without changing behaviour.
        internal void Unused() { StatusChanged?.Invoke(""); DownloadProgressChanged?.Invoke(0, 0); AssetFailed?.Invoke(""); }
    }

    // ---- the app's own wiring, assembled ----

    private (ReopenableLemmaProvider lemma, DictionarySourcePreferenceService prefs) BuildApp()
    {
        var (lemma, prefs, _) = BuildAppWithState();
        return (lemma, prefs);
    }

    private (ReopenableLemmaProvider lemma, DictionarySourcePreferenceService prefs, ApplicationState state) BuildAppWithState()
    {
        // Two bundled flat-file dictionaries, as a clean install ships.
        var dictionary = new Mock<IDictionaryService>();
        dictionary.SetupGet(d => d.AvailableLanguages).Returns(new[] { "en", "hi" });

        var lemma = new ReopenableLemmaProvider(_dpdPath);
        var registry = DictionarySourceFactory.Build(dictionary.Object, lemma, _dppnPath);

        var state = new ApplicationState();
        var stateService = new Mock<IApplicationStateService>();
        stateService.SetupGet(s => s.Current).Returns(state);

        _providers.Add(lemma);
        return (lemma, new DictionarySourcePreferenceService(registry, stateService.Object), state);
    }

    /// <summary>The picker, exactly as <c>DictionaryViewModel.RebuildSources</c> computes it.</summary>
    private static string[] PickerIds(DictionarySourcePreferenceService prefs) =>
        prefs.GetEffectiveSources().Select(s => s.Id).ToArray();

    // ---- fixtures: minimal but VALID assets, the same shape the real downloads install ----

    private static void BuildDpdAsset(string path)
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
                INSERT INTO lemma (id, lemma, pos, gloss) VALUES (1, 'dhamma', 'nt', 'a test gloss');
                INSERT INTO form_lemma (form, lemma_id) VALUES ('dhammaṃ', 1);";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
    }

    private static void BuildDppnLexicon(string path)
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
                    ('definition_language','en'),('source_version','2025-06'),('converter_version','1');
                INSERT INTO entry (headword, body_html) VALUES ('Nāgita','<p>a monk</p>');";
            cmd.ExecuteNonQuery();
        }
        SqliteConnection.ClearAllPools();
    }

    private readonly List<IDisposable> _providers = new();

    public void Dispose()
    {
        foreach (var p in _providers) { try { p.Dispose(); } catch { /* best effort */ } }
        SqliteConnection.ClearAllPools();
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }
}
