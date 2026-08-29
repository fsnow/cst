using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Conversion;
using CST.Lemma;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// What the dossier cache is allowed to keep across an asset swap. (#869)
/// </summary>
public class LemmaReportCacheTests
{
    private static DpdLemmaMeta MetaFor(string version) =>
        new("full", version, "3", "1", "CC BY-NC-SA", "DPD", "dpd", "Bodhirāsa", "https://example.invalid");

    private static LemmaDetail Detail() =>
        new(1, "dhamma", "nt", "a test gloss", null, null, null, null, null, null,
            null, null, null, null, null, null);

    /// <summary>
    /// A report assembled from the asset that was replaced while it was being assembled is handed to the
    /// caller that asked for it, and NOT put in the cache.
    ///
    /// <para>Assembly awaits several corpus searches — seconds, which is the whole reason this cache exists —
    /// so an install landing inside that window clears the cache before the report arrives. Storing it
    /// anyway put a dossier built from the superseded asset back into an otherwise clean cache, version
    /// footer and all, where it sat until 32 other lemmas evicted it. The trigger is ordinary: the reader
    /// opens a lemma report while the background update installs.</para>
    /// </summary>
    [Fact]
    public async Task A_report_assembled_across_an_asset_swap_is_not_cached()
    {
        var meta = MetaFor("v0.4.20260531");
        var lemma = new Mock<ILemmaProvider>();
        lemma.SetupGet(l => l.IsAvailable).Returns(true);
        lemma.SetupGet(l => l.Meta).Returns(() => meta);
        lemma.Setup(l => l.GetDetail(1)).Returns(Detail());

        var search = new Mock<ILemmaSearchService>();
        search.Setup(s => s.ExpandAndSearchAsync(
                It.IsAny<long>(), It.IsAny<bool>(), It.IsAny<BookFilter?>(), It.IsAny<Script>(),
                It.IsAny<CancellationToken>(), It.IsAny<bool>()))
            // The install lands here, mid-assembly: the cache is cleared and the asset is a different one by
            // the time this report is finished.
            .ReturnsAsync(() => { meta = MetaFor("v0.4.20260731"); return null; });

        var service = new LemmaReportService(lemma.Object, search.Object);

        Assert.NotNull(await service.BuildAsync(1));       // the caller still gets what it asked for
        Assert.NotNull(await service.BuildAsync(1));       // and the second call rebuilds rather than serving it

        lemma.Verify(l => l.GetDetail(1), Times.Exactly(2));
    }

    /// <summary>The ordinary case still caches — otherwise the fix above would be a cache that never holds
    /// anything, and every report would pay for several corpus searches again.</summary>
    [Fact]
    public async Task A_report_assembled_with_the_asset_unchanged_is_cached()
    {
        var lemma = new Mock<ILemmaProvider>();
        lemma.SetupGet(l => l.IsAvailable).Returns(true);
        lemma.SetupGet(l => l.Meta).Returns(MetaFor("v0.4.20260531"));
        lemma.Setup(l => l.GetDetail(1)).Returns(Detail());

        var search = new Mock<ILemmaSearchService>();
        var service = new LemmaReportService(lemma.Object, search.Object);

        Assert.NotNull(await service.BuildAsync(1));
        Assert.NotNull(await service.BuildAsync(1));

        lemma.Verify(l => l.GetDetail(1), Times.Once);
    }
}
