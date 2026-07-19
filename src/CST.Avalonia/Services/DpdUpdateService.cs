using System;
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
using Microsoft.Extensions.Logging;
using Octokit;

namespace CST.Avalonia.Services;

/// <summary>
/// Downloads / updates the derived <c>dpd-cst-subset</c> asset from the derived-asset repo's GitHub Releases,
/// parallel to <see cref="XmlUpdateService"/>. Reads <c>releases/latest</c> (Octokit — the same client the XML
/// path uses), compares the release manifest's version stamps to the installed asset, and downloads + verifies
/// (SHA-256) + decompresses (gzip, built-in) + atomically installs when newer or absent. Preservation store:
/// the existing asset is replaced only after a new one is fully verified. (#390)
/// </summary>
public sealed class DpdUpdateService : IDpdUpdateService
{
    private const string ManifestAssetName = "dpd-cst-subset.manifest.json";
    private const string AssetDirName = "dpd-cst-subset";
    private const string AssetFileName = "dpd-cst-subset.db";
    // A verified install staged for next launch when the target db can't be replaced in place (Windows: the live
    // provider holds it open). Applied by ApplyPendingInstall() before the db is opened at startup. (#394)
    private const string PendingSuffix = ".pending";

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

    // The installed asset path: <app-support>/CSTReader/dpd-cst-subset/dpd-cst-subset.db (matches App.axaml.cs).
    private static string AssetPath =>
        Path.Combine(AppConstants.DataDirectory, AssetDirName, AssetFileName);

    public async Task CheckAndUpdateAsync(CancellationToken ct = default)
    {
        if (Interlocked.CompareExchange(ref _busy, 1, 0) != 0) return; // already running
        try
        {
            // Read config INSIDE the try so a null/garbage DpdUpdateSettings (e.g. an edited settings.json) is
            // swallowed like any other failure, not thrown out of the fire-and-forget task. (fable review)
            var cfg = _settings.Settings.DpdUpdateSettings ?? new DpdUpdateSettings();
            if (!cfg.EnablePolling)
            {
                _logger.LogInformation("dpd-cst-subset polling disabled; skipping update check (a present file still works).");
                return;
            }
            // Hard bound on the WHOLE check+download: HttpClient.Timeout doesn't cover a streamed body read, and the
            // startup caller passes no token — without this a stalled connection hangs the background task forever
            // and pins _busy for the session. (fable review)
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromMinutes(10));
            await RunAsync(cfg, cts.Token).ConfigureAwait(false);
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("dpd-cst-subset update check canceled/timed out (non-fatal).");
            StatusChanged?.Invoke("Update check timed out.");
        }
        catch (Exception ex)
        {
            // Every expected failure (offline, timeout, no release, corrupt download, bad settings) is non-fatal —
            // the app just keeps whatever asset it has (or none). Never let this crash startup.
            _logger.LogWarning(ex, "dpd-cst-subset update check failed (non-fatal; feature degrades to asset-absent).");
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
            _logger.LogWarning("Latest dpd-cst-subset release {Tag} has no {Manifest}.", release.TagName, ManifestAssetName);
            return;
        }

        var manifestBytes = await _http.GetByteArrayAsync(manifestAsset.BrowserDownloadUrl, ct).ConfigureAwait(false);
        var manifest = ParseManifest(manifestBytes);
        if (manifest is null)
        {
            _logger.LogWarning("Could not parse {Manifest} from release {Tag}.", ManifestAssetName, release.TagName);
            return;
        }

        var installed = ReadInstalledMeta(AssetPath);
        if (!NeedsUpdate(installed, manifest))
        {
            _logger.LogInformation("dpd-cst-subset up to date (DPD {Dpd}, converter {Conv}).",
                manifest.DpdVersion, manifest.ConverterVersion);
            StatusChanged?.Invoke("Dictionary data is up to date.");
            return;
        }

        var archiveAsset = release.Assets.FirstOrDefault(a =>
            string.Equals(a.Name, manifest.File, StringComparison.OrdinalIgnoreCase));
        if (archiveAsset is null)
        {
            _logger.LogWarning("Release {Tag} manifest names '{File}' but that asset is missing.", release.TagName, manifest.File);
            return;
        }

        StatusChanged?.Invoke(installed is null ? "Downloading dictionary data..." : "Downloading updated dictionary data...");
        var archiveBytes = await DownloadWithProgressAsync(archiveAsset.BrowserDownloadUrl, ct).ConfigureAwait(false);

        StatusChanged?.Invoke("Installing dictionary data...");
        InstallFromGzip(archiveBytes, manifest.Sha256, AssetPath);

        _logger.LogInformation("Installed dpd-cst-subset (DPD {Dpd}, converter {Conv}, ~{Mb} MB). Active on next launch.",
            manifest.DpdVersion, manifest.ConverterVersion, manifest.RawBytes / 1024 / 1024);
        StatusChanged?.Invoke("Dictionary data installed (active after restart).");
    }

    // Buffer the archive in memory (a modest tens-of-MB, infrequent). Streams headers-first so progress fires.
    private async Task<byte[]> DownloadWithProgressAsync(string url, CancellationToken ct)
    {
        using var resp = await _http.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false);
        resp.EnsureSuccessStatusCode();
        long total = resp.Content.Headers.ContentLength ?? 0;
        using var stream = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var mem = new MemoryStream(total > 0 ? (int)Math.Min(total, int.MaxValue) : 0);
        var buf = new byte[81920];
        long read = 0; int n;
        while ((n = await stream.ReadAsync(buf.AsMemory(), ct).ConfigureAwait(false)) > 0)
        {
            mem.Write(buf, 0, n);
            read += n;
            DownloadProgressChanged?.Invoke(read, total);
        }
        return mem.ToArray();
    }

    private async Task<T?> WithTimeout<T>(Task<T> task, TimeSpan timeout, string what) where T : class
    {
        var done = await Task.WhenAny(task, Task.Delay(timeout)).ConfigureAwait(false);
        if (done != task)
        {
            // Observe the abandoned task if it later faults (e.g. NotFoundException when the repo has no release
            // yet), so it doesn't surface as an UnobservedTaskException. (fable review)
            _ = task.ContinueWith(t => { _ = t.Exception; }, TaskScheduler.Default);
            _logger.LogWarning("dpd-cst-subset {What} timed out after {Sec}s.", what, timeout.TotalSeconds);
            StatusChanged?.Invoke("Update check timed out.");
            return null;
        }
        return await task.ConfigureAwait(false);
    }

    // ---- testable core (InternalsVisibleTo CST.Avalonia.Tests) ----

    internal sealed record DpdManifest(string File, string DpdVersion, string ConverterVersion,
        string? SchemaVersion, string? Scope, string Sha256, long CompressedBytes, long RawBytes);

    internal sealed record InstalledMeta(string DpdVersion, string ConverterVersion);

    internal static DpdManifest? ParseManifest(byte[] json)
    {
        try
        {
            using var doc = JsonDocument.Parse(json);
            var r = doc.RootElement;
            if (r.ValueKind != JsonValueKind.Object) return null;
            string S(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString()! : "";
            long L(string k) => r.TryGetProperty(k, out var v) && v.ValueKind == JsonValueKind.Number && v.TryGetInt64(out var n) ? n : 0;
            var file = S("file"); var sha = S("sha256");
            var dpd = S("dpdVersion"); var conv = S("converterVersion");
            // file, sha256, and both version axes are required — without them we can't safely download/compare.
            if (file.Length == 0 || sha.Length == 0 || dpd.Length == 0 || conv.Length == 0) return null;
            return new DpdManifest(file, dpd, conv, S("schemaVersion"), S("scope"), sha, L("compressedBytes"), L("rawBytes"));
        }
        catch (JsonException) { return null; }
    }

    // Update when the asset is ABSENT/unreadable, or EITHER version axis differs (DPD release OR our converter).
    internal static bool NeedsUpdate(InstalledMeta? installed, DpdManifest latest)
        => installed is null
           || !string.Equals(installed.DpdVersion, latest.DpdVersion, StringComparison.Ordinal)
           || !string.Equals(installed.ConverterVersion, latest.ConverterVersion, StringComparison.Ordinal);

    // Read the installed asset's version stamps via the same provider the app uses (no new SQLite dep). Null when
    // absent/unreadable → treated as "needs update".
    internal static InstalledMeta? ReadInstalledMeta(string dbPath)
    {
        if (!File.Exists(dbPath)) return null;
        try
        {
            using var p = new SqliteLemmaProvider(dbPath);
            var m = p.Meta;
            if (!p.IsAvailable || m is null || string.IsNullOrEmpty(m.DpdVersion) || string.IsNullOrEmpty(m.ConverterVersion))
                return null;
            return new InstalledMeta(m.DpdVersion, m.ConverterVersion);
        }
        catch { return null; }
    }

    // Verify the gzip archive's SHA-256, decompress to a temp file, then atomically move it into place. The
    // existing asset is untouched until the move, so a failed/corrupt download never destroys a good install.
    internal static void InstallFromGzip(byte[] archive, string expectedSha256, string finalPath)
    {
        string actual = Convert.ToHexString(SHA256.HashData(archive)).ToLowerInvariant();
        if (!string.Equals(actual, expectedSha256, StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                $"dpd-cst-subset archive failed its SHA-256 check (expected {expectedSha256}, got {actual}) — corrupt or partial download.");

        var dir = Path.GetDirectoryName(finalPath)!;
        Directory.CreateDirectory(dir);
        var tmp = finalPath + ".new";
        try
        {
            using (var src = new MemoryStream(archive, writable: false))
            using (var gz = new GZipStream(src, CompressionMode.Decompress))
            using (var outFs = File.Create(tmp))
                gz.CopyTo(outFs);

            // SHA-256 only proves TRANSPORT integrity (the archive matches the manifest). It does NOT prove the
            // publisher gzipped a USABLE db — a truncated/wrong-schema source would still verify and then clobber a
            // good existing asset. So open the decompressed file and require it to be a usable asset BEFORE the
            // replace; if not, throw and leave the existing install untouched. (fable review — preservation gap)
            using (var probe = new SqliteLemmaProvider(tmp))
                if (!probe.IsAvailable)
                    throw new InvalidDataException(
                        "decompressed dpd-cst-subset archive is not a usable asset (missing core tables) — not installing.");

            // Replace the installed asset with the freshly verified file. The old file is preserved until here.
            //  - macOS/Linux: File.Move succeeds even while the live provider holds the old db open (POSIX unlinks
            //    the old inode); the running provider keeps reading the old one until restart-to-activate.
            //  - Windows: File.Move over an OPEN db throws a sharing violation. Rather than fail the install, stage
            //    the verified file beside the target and swap it in on the next launch — before the provider opens
            //    it — via ApplyPendingInstall(). Same user-visible outcome either way: active after restart. (#394)
            try
            {
                File.Move(tmp, finalPath, overwrite: true);
                // This direct install supersedes any earlier staged one — drop a leftover .pending so a stale,
                // older staged file can't later regress the asset on a future launch. (fable review — #394)
                var superseded = finalPath + PendingSuffix;
                if (File.Exists(superseded)) { try { File.Delete(superseded); } catch { /* best effort */ } }
            }
            catch (Exception ex) when ((ex is IOException || ex is UnauthorizedAccessException) && File.Exists(finalPath))
            {
                // The existing asset is still in place (untouched), so preservation holds — stage for next launch.
                File.Move(tmp, finalPath + PendingSuffix, overwrite: true);
            }
        }
        finally
        {
            if (File.Exists(tmp)) { try { File.Delete(tmp); } catch { /* best effort */ } }
        }
    }

    /// <summary>
    /// Applies a staged install (see <see cref="InstallFromGzip"/>) on the next launch — call this BEFORE the
    /// asset db is opened. No-op when nothing is staged (the common case, and always on macOS/Linux). Best-effort:
    /// any failure leaves the staged file for a later attempt and must never block startup. Returns true when a
    /// staged install was applied. (#394)
    /// </summary>
    internal static bool ApplyPendingInstall(string finalPath)
    {
        var pending = finalPath + PendingSuffix;
        if (!File.Exists(pending)) return false;
        try
        {
            File.Move(pending, finalPath, overwrite: true);
            return true;
        }
        catch (Exception ex)
        {
            // Leave the staged file for the next launch rather than crash startup — but log it: a persistent
            // failure here means the update never activates and CheckAndUpdate keeps re-downloading it. (#394)
            Serilog.Log.Warning(ex,
                "Failed to apply a staged dpd-cst-subset update at {Pending}; will retry on next launch.", pending);
            return false;
        }
    }

    /// <summary>
    /// Applies a staged install for the standard asset path (<see cref="AssetPath"/>). Call this once, early at
    /// startup — before anything opens the db — so a Windows staged swap activates regardless of whether/when the
    /// lemma provider gets resolved. No-op when nothing is staged. (#394)
    /// </summary>
    internal static bool ApplyPendingInstall() => ApplyPendingInstall(AssetPath);
}
