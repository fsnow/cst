using System;
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

    /// <summary>Download progress: (bytesSoFar, totalBytes). totalBytes may be 0 if the length is unknown.</summary>
    event Action<long, long>? DownloadProgressChanged;

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
