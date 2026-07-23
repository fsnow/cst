using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Constants;
using CST.Avalonia.Models;
using CST.Lemma;
using CST.Lexicon;
using Microsoft.Extensions.Logging;
using Octokit;

namespace CST.Avalonia.Services;

/// <summary>
/// Keeps the derived dictionary assets up to date from the cst-dictionaries GitHub Releases, parallel to
/// <see cref="XmlUpdateService"/> for the corpus XML. Reads a single CATALOG manifest listing every dictionary
/// (dpd-cst-subset, dppn, …); for each, compares the release's version stamps to the installed asset and
/// downloads + verifies (SHA-256) + decompresses (gzip) + atomically installs when newer or absent. Serving a new
/// dictionary needs a manifest entry + asset AND a matching <see cref="AssetDescriptor"/> here (install path,
/// version reader, usability probe). Preservation store: an existing asset is replaced only after a new one is
/// fully verified. (#390/#468)
/// </summary>
public sealed class DpdUpdateService : IDpdUpdateService
{
    private const string ManifestAssetName = "dictionaries.manifest.json";
    // A verified install staged for next launch when the target db can't be replaced in place (Windows: the live
    // provider holds it open). Applied by ApplyPendingInstall() before the db is opened at startup. (#394)
    private const string PendingSuffix = ".pending";

    // The installed asset paths (must match the DI wiring in App.axaml.cs).
    public static string DpdSubsetPath =>
        Path.Combine(AppConstants.DataDirectory, "dpd-cst-subset", "dpd-cst-subset.db");
    public static string DppnLexiconPath =>
        Path.Combine(AppConstants.DataDirectory, "dppn", "dppn.db");

    private readonly ILogger<DpdUpdateService> _logger;
    private readonly ISettingsService _settings;
    private readonly GitHubClient _github;
    private readonly HttpClient _http;
    private int _busy;

    public event Action<string>? StatusChanged;
    public event Action<long, long>? DownloadProgressChanged;
    public bool IsBusy => Volatile.Read(ref _busy) == 1;

    public DpdUpdateService(ILogger<DpdUpdateService> logger, ISettingsService settings)
    {
        _logger = logger;
        _settings = settings;
        _github = new GitHubClient(new ProductHeaderValue(AppConstants.UserAgent));
        _http = new HttpClient();
        _http.DefaultRequestHeaders.Add("User-Agent", AppConstants.UserAgent);
    }

    // The dictionaries this build knows how to install: id (matches a manifest entry), install path, how to read
    // the installed version stamps, and how to probe that a decompressed file is a usable asset (not just valid
    // gzip). Reading the version differs per asset (DPD's lemma db vs a lexicon), which is why it's a descriptor.
    private static IReadOnlyList<AssetDescriptor> Descriptors => new[]
    {
        new AssetDescriptor("dpd", DpdSubsetPath, ReadDpdVersion, ProbeDpdUsable),
        new AssetDescriptor("dppn", DppnLexiconPath, ReadLexiconVersion, ProbeLexiconUsable),
    };

    public async Task CheckAndUpdateAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return; // already running
        try
        {
            var cfg = _settings.Settings.DpdUpdateSettings ?? new DpdUpdateSettings();
            if (!cfg.EnableAutomaticUpdates)
            {
                _logger.LogInformation("dictionary-asset automatic updates disabled; skipping update check (present files still work).");
                return;
            }
            // Hard bound on the WHOLE check+download (HttpClient.Timeout doesn't cover a streamed body). (fable)
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(10));
            await RunAsync(cfg, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("dictionary-asset update check canceled/timed out (non-fatal).");
            StatusChanged?.Invoke("Update check timed out.");
        }
        catch (Exception ex)
        {
            // Every expected failure (offline, timeout, no release, corrupt download, bad settings) is non-fatal.
            _logger.LogWarning(ex, "dictionary-asset update check failed (non-fatal; features degrade to asset-absent).");
            StatusChanged?.Invoke("Could not check for dictionary data updates.");
        }
        finally { Volatile.Write(ref _busy, 0); }
    }

    private async Task RunAsync(DpdUpdateSettings cfg, CancellationToken ct)
    {
        StatusChanged?.Invoke("Checking for dictionary data updates...");

        var release = await WithTimeout(
            _github.Repository.Release.GetLatest(cfg.RepositoryOwner, cfg.RepositoryName),
            TimeSpan.FromSeconds(15), "release lookup").ConfigureAwait(false);
        if (release is null) return;

        var manifestAsset = release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, ManifestAssetName, StringComparison.OrdinalIgnoreCase));
        if (manifestAsset is null)
        {
            _logger.LogWarning("Latest release {Tag} has no {Manifest}.", release.TagName, ManifestAssetName);
            return;
        }

        var manifestBytes = await _http.GetByteArrayAsync(manifestAsset.BrowserDownloadUrl, ct).ConfigureAwait(false);
        var catalog = ParseCatalog(manifestBytes);
        if (catalog is null)
        {
            _logger.LogWarning("Could not parse {Manifest} from release {Tag}.", ManifestAssetName, release.TagName);
            return;
        }

        bool anyInstalled = false, anyFailed = false;
        // Each dictionary is independent: a failed/absent one never blocks the others.
        foreach (var desc in Descriptors)
        {
            ct.ThrowIfCancellationRequested();
            var entry = catalog.FirstOrDefault(e => string.Equals(e.Id, desc.Id, StringComparison.OrdinalIgnoreCase));
            if (entry is null) continue;   // this build knows a dictionary the release doesn't offer (yet)

            try
            {
                if (await UpdateOneAsync(desc, entry, release, ct).ConfigureAwait(false))
                    anyInstalled = true;
            }
            // Only a REAL cancellation (our 10-min CTS or the caller's token) aborts the whole run. An
            // HttpClient inactivity timeout also surfaces as an OCE but with the token NOT signalled — treat that
            // as this asset's failure so the others still run. (fable MED-1)
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
            catch (Exception ex)
            {
                anyFailed = true;
                _logger.LogWarning(ex, "Update of dictionary '{Id}' failed (non-fatal; others continue).", desc.Id);
            }
        }

        StatusChanged?.Invoke(
            anyInstalled ? "Dictionary data installed (active after restart)."
            : anyFailed  ? "Could not update some dictionary data."
            : "Dictionary data is up to date.");
    }

    private async Task<bool> UpdateOneAsync(AssetDescriptor desc, ManifestEntry entry, Release release, CancellationToken ct)
    {
        var installed = desc.ReadInstalledVersion(desc.InstallPath);
        if (!NeedsUpdate(installed, entry))
        {
            _logger.LogInformation("{Id} up to date (source {Src}, converter {Conv}).",
                desc.Id, entry.SourceVersion, entry.ConverterVersion);
            return false;
        }

        var archiveAsset = release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, entry.File, StringComparison.OrdinalIgnoreCase));
        if (archiveAsset is null)
        {
            _logger.LogWarning("Release {Tag} manifest names '{File}' for '{Id}' but that asset is missing.",
                release.TagName, entry.File, desc.Id);
            return false;
        }

        StatusChanged?.Invoke(installed is null ? "Downloading dictionary data..." : "Downloading updated dictionary data...");
        var archiveBytes = await DownloadWithProgressAsync(archiveAsset.BrowserDownloadUrl, entry.CompressedBytes, ct).ConfigureAwait(false);

        StatusChanged?.Invoke("Installing dictionary data...");
        InstallFromGzip(archiveBytes, entry.Sha256, desc.InstallPath, desc.ProbeUsable);

        _logger.LogInformation("Installed {Id} (source {Src}, converter {Conv}, ~{Mb} MB). Active on next launch.",
            desc.Id, entry.SourceVersion, entry.ConverterVersion, entry.RawBytes / 1024 / 1024);
        return true;
    }

    // Buffer the archive in memory (tens of MB, infrequent). Streams headers-first so progress fires.
    // <paramref name="expectedBytes"/> is the manifest's compressed size; a body far exceeding it (bogus/hostile
    // Content-Length or a mis-published multi-GB asset) is rejected early rather than allocated. (fable LOW-4)
    private async Task<byte[]> DownloadWithProgressAsync(string url, long expectedBytes, CancellationToken ct)
    {
        // Cap = 2× the manifest size + 32 MB slack, floored at 512 MB when the manifest gave no size. Bounds both
        // the pre-allocation and the accepted body without tripping on normal gzip/size variance.
        long cap = expectedBytes > 0 ? expectedBytes * 2 + 32L * 1024 * 1024 : 512L * 1024 * 1024;

        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? 0;
        if (total > cap)
            throw new InvalidDataException(
                $"asset body ({total} bytes) far exceeds the manifest's {expectedBytes} — refusing to download.");
        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        int initial = total > 0 ? (int)Math.Min(total, cap) : 0;
        using var mem = new MemoryStream(initial);
        var buf = new byte[81920];
        long read = 0; int n;
        while ((n = await stream.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            read += n;
            if (read > cap)
                throw new InvalidDataException($"asset body exceeded {cap} bytes mid-stream — refusing (likely mis-published).");
            mem.Write(buf, 0, n);
            DownloadProgressChanged?.Invoke(read, total);
        }
        return mem.ToArray();
    }

    private async Task<T?> WithTimeout<T>(Task<T> task, TimeSpan timeout, string what) where T : class
    {
        var done = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
        if (done != task)
        {
            _ = task.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);  // observe if it later faults
            _logger.LogWarning("dictionary-asset {What} timed out after {Sec}s.", what, timeout.TotalSeconds);
            StatusChanged?.Invoke("Update check timed out.");
            return null;
        }
        return await task.ConfigureAwait(false);
    }

    // ---- testable core (InternalsVisibleTo CST.Avalonia.Tests) ----

    // One dictionary in the catalog manifest.
    internal sealed record ManifestEntry(string Id, string File, string SourceVersion, string ConverterVersion,
        string Sha256, long CompressedBytes, long RawBytes);

    internal sealed record InstalledVersion(string SourceVersion, string ConverterVersion);

    // A dictionary this build can install: id, where it goes, how to read its installed version, how to probe usability.
    internal sealed record AssetDescriptor(
        string Id, string InstallPath,
        Func<string, InstalledVersion?> ReadInstalledVersion,
        Func<string, bool> ProbeUsable);

    // Parse { schemaVersion, dictionaries:[ { id, file, sourceVersion, converterVersion, sha256, compressedBytes,
    // rawBytes } ] }. Skips entries missing a required field rather than failing the whole catalog. Null only for
    // malformed JSON / wrong shape.
    internal static IReadOnlyList<ManifestEntry>? ParseCatalog(byte[] json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            if (root.ValueKind != JsonValueKind.Object) return null;
            if (!root.TryGetProperty("dictionaries", out var arr) || arr.ValueKind != JsonValueKind.Array) return null;

            var list = new List<ManifestEntry>();
            foreach (var e in arr.EnumerateArray())
            {
                if (e.ValueKind != JsonValueKind.Object) continue;
                string S(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
                long L(string k) => e.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
                var id = S("id"); var file = S("file"); var sha = S("sha256");
                var src = S("sourceVersion"); var conv = S("converterVersion");
                // id, file, sha256, and both version axes are required — without them we can't safely download/compare.
                if (id.Length == 0 || file.Length == 0 || sha.Length == 0 || src.Length == 0 || conv.Length == 0) continue;
                list.Add(new ManifestEntry(id, file, src, conv, sha, L("compressedBytes"), L("rawBytes")));
            }
            return list;
        }
        catch (JsonException) { return null; }
    }

    // Update when the asset is ABSENT/unreadable, or EITHER version axis differs (source release OR our converter).
    internal static bool NeedsUpdate(InstalledVersion? installed, ManifestEntry latest)
        => installed is null
           || !string.Equals(installed.SourceVersion, latest.SourceVersion, StringComparison.Ordinal)
           || !string.Equals(installed.ConverterVersion, latest.ConverterVersion, StringComparison.Ordinal);

    // ---- per-asset version readers + usability probes ----

    // DPD's dpd-cst-subset: version stamps via the same provider the app uses (no new SQLite dep).
    internal static InstalledVersion? ReadDpdVersion(string dbPath)
    {
        if (!File.Exists(dbPath)) return null;
        try
        {
            using var p = new SqliteLemmaProvider(dbPath);
            var m = p.Meta;
            if (!p.IsAvailable || m is null || string.IsNullOrEmpty(m.DpdVersion) || string.IsNullOrEmpty(m.ConverterVersion))
                return null;
            return new InstalledVersion(m.DpdVersion, m.ConverterVersion);
        }
        catch { return null; }
    }

    internal static bool ProbeDpdUsable(string dbPath)
    {
        try { using var p = new SqliteLemmaProvider(dbPath); return p.IsAvailable; }
        catch { return false; }
    }

    // A lexicon asset (dppn): version stamps from its meta (source_version + converter_version).
    internal static InstalledVersion? ReadLexiconVersion(string dbPath)
    {
        if (!File.Exists(dbPath)) return null;
        try
        {
            var m = LexiconReader.OpenMeta(dbPath);
            if (string.IsNullOrEmpty(m.SourceVersion)) return null;
            return new InstalledVersion(m.SourceVersion!, m.ConverterVersion.ToString());
        }
        catch { return null; }
    }

    // Full Open (not OpenMeta): a usable lexicon needs the `entry` table too, not just a well-stamped `meta`.
    // Probing meta-only would let a converter bug that drops `entry` verify and then clobber a good install.
    // The entry set is small (tens of thousands) — loaded once here and discarded. (fable MED-2)
    internal static bool ProbeLexiconUsable(string dbPath)
    {
        try { LexiconReader.Open(dbPath); return true; }
        catch { return false; }
    }

    // Verify the gzip archive's SHA-256, decompress to a temp file, probe it is a USABLE asset, then atomically
    // install (or stage for next launch on Windows). The existing asset is untouched until then, so a
    // failed/corrupt/wrong-schema download never destroys a good install. (fable — preservation)
    internal static void InstallFromGzip(byte[] archive, string expectedSha256, string finalPath, Func<string, bool> probeUsable)
    {
        string actual = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"asset archive failed its SHA-256 check (expected {expectedSha256}, got {actual}) — corrupt or partial download.");

        var dir = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(dir);
        var tmp = finalPath + ".new";
        try
        {
            using (var src = new MemoryStream(archive, writable: false))
            using (var gz = new GZipStream(src, CompressionMode.Decompress))
            using (var outFs = File.Create(tmp))
                gz.CopyTo(outFs);

            // SHA-256 proves only TRANSPORT integrity. Require the decompressed file to be a USABLE asset before
            // the replace, so a truncated/wrong-schema source can't verify and then clobber a good asset. (fable)
            if (!probeUsable(tmp))
                throw new InvalidDataException(
                    "decompressed asset is not usable (missing/invalid tables) — not installing.");

            try
            {
                File.Move(tmp, finalPath, overwrite: true);
                // Supersede any earlier staged install so a stale one can't later regress the asset. (fable, #394)
                // Log a failed delete: if it lingers, ApplyPendingInstall would move the OLDER file back over this
                // fresh one on next launch (self-heals on the next update check, but worth surfacing). (fable LOW-1)
                var superseded = finalPath + PendingSuffix;
                if (File.Exists(superseded))
                {
                    try { File.Delete(superseded); }
                    catch (Exception ex)
                    {
                        Serilog.Log.Warning(ex,
                            "Could not remove a superseded staged install at {Path}; a stale swap may be re-applied next launch.",
                            superseded);
                    }
                }
            }
            catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && File.Exists(finalPath))
            {
                // Windows: the live db is open → stage beside it and swap on next launch (existing asset intact). (#394)
                File.Move(tmp, finalPath + PendingSuffix, overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best effort */ } }
        }
    }

    /// <summary>
    /// Apply a staged install for <paramref name="finalPath"/> on the next launch — call BEFORE the db is opened.
    /// No-op when nothing is staged (the common case, always on macOS/Linux). Best-effort; never blocks startup.
    /// Returns true when a staged install was applied. (#394)
    /// </summary>
    internal static bool ApplyPendingInstall(string finalPath)
    {
        var pending = finalPath + PendingSuffix;
        if (!File.Exists(pending)) return false;
        try { File.Move(pending, finalPath, overwrite: true); return true; }
        catch (Exception ex)
        {
            Serilog.Log.Warning(ex,
                "Failed to apply a staged update at {Pending}; will retry on next launch.", pending);
            return false;
        }
    }

    /// <summary>
    /// Apply any staged installs for ALL known asset paths. Call once, early at startup — before anything opens a
    /// db. No-op when nothing is staged. Returns true when at least one staged install was applied. (#394/#468)
    /// </summary>
    public static bool ApplyPendingInstall()
    {
        bool any = false;
        foreach (var desc in Descriptors)
            any |= ApplyPendingInstall(desc.InstallPath);
        return any;
    }
}
