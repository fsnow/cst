using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using CST.Avalonia.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// #1136: the app tells which version of a source PDF it has by SharePoint's <c>quickXorHash</c>, keeps the
/// cached copy the same as SharePoint's, and picks the page mapping recorded for the version on disk.
/// </summary>
public class PdfVersionTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cst-1136-" + Guid.NewGuid().ToString("N"));

    public PdfVersionTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    // Deterministic bytes: (31 i + 7) mod 256.
    private static byte[] Gen(int n) => Enumerable.Range(0, n).Select(i => (byte)((i * 31 + 7) % 256)).ToArray();

    // Expected values from a byte-by-byte reference implementation of the definition (each byte XORed in at
    // bit 11 i mod 160, then the length), which reproduced the server's hash for two real source PDFs.
    [Theory]
    [InlineData(0, "AAAAAAAAAAAAAAAAAAAAAAAAAAA=")]
    [InlineData(1, "BwAAAAAAAAAAAAAAAQAAAAAAAAA=")]
    [InlineData(159, "GD+KGCbnJ+lXCrS00xVjSpWmgvI=")]
    [InlineData(160, "GD+KGCbnJ+lXCrS07BVjSpWmgvs=")]
    [InlineData(161, "fz+KGCbnJ+lXCrS07RVjSpWmgvs=")]
    [InlineData(1000, "X4X7cC7/cVgPZMrjju+fOUsa7aY=")]
    [InlineData(100003, "fw/OMSbnJ+lXCrS075NiSpWmgvs=")]
    public void QuickXorHash_MatchesTheReference(int length, string expected)
    {
        Assert.Equal(expected, QuickXorHash.Compute(Gen(length)));
    }

    [Fact]
    public void QuickXorHash_Text()
    {
        Assert.Equal("YRDDGAAAAAAAAAAAAwAAAAAAAAA=", QuickXorHash.Compute(Encoding.ASCII.GetBytes("abc")));
    }

    [Fact]
    public void QuickXorHash_DoesNotDependOnHowTheDataIsSplit()
    {
        var data = Gen(100003);
        foreach (var chunk in new[] { 1, 7, 159, 160, 161, 4096 })
        {
            var h = new QuickXorHash();
            for (int i = 0; i < data.Length; i += chunk)
                h.Append(data.AsSpan(i, Math.Min(chunk, data.Length - i)));
            Assert.Equal("fw/OMSbnJ+lXCrS075NiSpWmgvs=", h.Finish());
        }
    }

    [Fact]
    public void QuickXorHash_ComputeFile_MatchesCompute()
    {
        var path = Path.Combine(_dir, "a.pdf");
        File.WriteAllBytes(path, Gen(3_000_001));   // more than one read buffer
        Assert.Equal(QuickXorHash.Compute(Gen(3_000_001)), QuickXorHash.ComputeFile(path));
    }

    [Fact]
    public void LocalPdfVersion_StampsTheHash_AndRehashesAChangedFile()
    {
        var path = Path.Combine(_dir, "b.pdf");
        File.WriteAllBytes(path, Gen(1000));
        Assert.Equal(QuickXorHash.Compute(Gen(1000)), LocalPdfVersion.Get(path));
        Assert.True(File.Exists(LocalPdfVersion.StampPath(path)));

        // A different file at the same path (as when a new version replaces it) is hashed again.
        File.WriteAllBytes(path, Gen(1001));
        Assert.Equal(QuickXorHash.Compute(Gen(1001)), LocalPdfVersion.Get(path));
    }

    [Fact]
    public void LocalPdfVersion_UsesTheStamp_WhileTheFileIsUnchanged()
    {
        var path = Path.Combine(_dir, "c.pdf");
        File.WriteAllBytes(path, Gen(1000));
        LocalPdfVersion.Remember(path, "remembered");
        Assert.Equal("remembered", LocalPdfVersion.Get(path));
    }

    [Fact]
    public void LocalPdfVersion_AnUnreadableStamp_IsOnlyALostShortcut()
    {
        var path = Path.Combine(_dir, "d.pdf");
        File.WriteAllBytes(path, Gen(1000));
        File.WriteAllText(LocalPdfVersion.StampPath(path), "{ not json");
        Assert.Equal(QuickXorHash.Compute(Gen(1000)), LocalPdfVersion.Get(path));
    }

    [Fact]
    public async Task SaveStreamVerified_WrongHash_LeavesTheCachedCopyAsItWas()
    {
        var path = Path.Combine(_dir, "e.pdf");
        File.WriteAllBytes(path, Gen(10));
        var ok = await SharePointService.SaveStreamVerifiedAsync(new MemoryStream(Gen(1000)), path, 1000,
            NullLogger.Instance, expectedQuickXorHash: "not-the-hash");
        Assert.False(ok);
        Assert.Equal(Gen(10), File.ReadAllBytes(path));
        Assert.Empty(Directory.GetFiles(_dir, "*.part"));
    }

    [Fact]
    public async Task SaveStreamVerified_RightHash_ReplacesTheCachedCopy()
    {
        var path = Path.Combine(_dir, "f.pdf");
        File.WriteAllBytes(path, Gen(10));
        var ok = await SharePointService.SaveStreamVerifiedAsync(new MemoryStream(Gen(1000)), path, 1000,
            NullLogger.Instance, expectedQuickXorHash: QuickXorHash.Compute(Gen(1000)));
        Assert.True(ok);
        Assert.Equal(Gen(1000), File.ReadAllBytes(path));
    }
}
