using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace CST.Avalonia.Services;

/// <summary>
/// Keeps the derived dictionary assets (<c>dpd-cst-subset</c>, <c>dppn</c>, …) up to date by polling the
/// cst-dictionaries repo's GitHub Releases — parallel to <see cref="IXmlUpdateService"/> for the corpus XML. A
/// single CATALOG manifest lists every dictionary; each is downloaded/verified/installed independently. It only
/// DOWNLOADS the assets; feature availability is driven by each file's presence (the provider/reader opens it at
/// startup), so a freshly downloaded (or manually dropped-in) asset takes effect on the next launch. A no-op
/// when polling is disabled or the network is unreachable — the app degrades to "asset absent". (#390/#468)
/// </summary>
public interface IDpdUpdateService
{
    /// <summary>Human-readable progress for a status banner (same UX as the XML update).</summary>
    event Action<string>? StatusChanged;

    /// <summary>Raised with the asset id ("dpd", "dppn") when an asset has been installed and is USABLE NOW.
    /// A staged install (swapped on next launch, #394) does not raise it. Consumers use this to go live
    /// without a restart — reopening the lemma provider and rebuilding the dictionary picker. (#536)</summary>
    event Action<string>? AssetInstalled;

    /// <summary>Download progress: (bytesSoFar, totalBytes). totalBytes may be 0 if the length is unknown.</summary>
    event Action<long, long>? DownloadProgressChanged;

    /// <summary>
    /// Raised with the asset id when a dictionary could not be installed this session — a bad checksum, a
    /// failed usability probe, a dropped connection, an unreachable release. (#773)
    ///
    /// <para>A counterpart to <see cref="AssetInstalled"/>, and it exists for the same reason: without it, a
    /// failed download is indistinguishable from a build that never offered the dictionary at all. The picker
    /// simply does not list it, which reads as "this app has no DPD" rather than "we could not fetch it".</para>
    /// </summary>
    event Action<string>? AssetFailed;

    /// <summary>
    /// Asset ids that failed this session and are still not installed. A STATE rather than a log line, so a
    /// caller can say which dictionary is missing and why, and can offer to try again. (#773)
    /// </summary>
    IReadOnlyCollection<string> FailedAssetIds { get; }

    /// <summary>
    /// Re-run the check for dictionaries that are absent or failed, ignoring the ones already installed and
    /// current. Cheap when nothing is missing — it returns without touching the network. (#773)
    ///
    /// <para>Demand-driven rather than timed: <see cref="CheckAndUpdateAsync"/> runs once per launch, so a
    /// first run that fails leaves the reader without dictionaries until they restart. The moment a reader
    /// opens the dictionary panel is the moment they have said they want one, and it is a better trigger than
    /// any interval we could pick.</para>
    /// </summary>
    Task RetryMissingAsync(CancellationToken ct = default);

    bool IsBusy { get; }

    /// <summary>
    /// Check the latest release's catalog manifest and, for each dictionary that is newer than its installed
    /// asset (compared on source version + our converter version) or absent, download + verify + install it. Each
    /// dictionary is independent — a failed/absent one never blocks the others. Never throws for the expected
    /// failure modes (polling off, offline, timeout, no release) — it logs and returns. Every existing asset is
    /// preserved until its replacement is fully verified.
    /// </summary>
    Task CheckAndUpdateAsync(CancellationToken ct = default);
}
