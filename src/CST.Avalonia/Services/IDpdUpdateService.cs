using System;
using System.Threading;
using System.Threading.Tasks;

namespace CST.Avalonia.Services;

/// <summary>
/// Keeps the derived <c>dpd-cst-subset</c> asset (lemma / dictionary / sandhi) up to date by polling the
/// derived-asset repo's GitHub Releases — parallel to <see cref="IXmlUpdateService"/> for the corpus XML.
/// It only DOWNLOADS the asset; feature availability is driven by the file's presence (the provider opens it at
/// startup), so a freshly downloaded (or manually dropped-in) asset takes effect on the next launch. A no-op
/// when polling is disabled or the network is unreachable — the app degrades to "asset absent". (#390)
/// </summary>
public interface IDpdUpdateService
{
    /// <summary>Human-readable progress for a status banner (same UX as the XML update).</summary>
    event Action<string>? StatusChanged;

    /// <summary>Download progress: (bytesSoFar, totalBytes). totalBytes may be 0 if the length is unknown.</summary>
    event Action<long, long>? DownloadProgressChanged;

    bool IsBusy { get; }

    /// <summary>
    /// Check the latest release's manifest and, if it is newer than the installed asset (compared on DPD version
    /// + our converter version) or the asset is absent, download + verify + install it. Never throws for the
    /// expected failure modes (polling off, offline, timeout, no release) — it logs and returns. The existing
    /// asset is preserved until a new one is fully verified.
    /// </summary>
    Task CheckAndUpdateAsync(CancellationToken ct = default);
}
