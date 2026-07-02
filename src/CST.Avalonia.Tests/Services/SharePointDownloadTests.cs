using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// NET-1: <see cref="SharePointService.SaveStreamVerifiedAsync"/> must never leave a partial or corrupt
/// file at the destination — the downloaded source PDFs are the permanent preservation store and the
/// cache check trusts any file already present. A partial/failed download must leave the destination
/// absent (or unchanged) and clean up its ".part".
/// </summary>
public class SharePointDownloadTests : IDisposable
{
    private readonly string _dir;
    private readonly string _final;

    public SharePointDownloadTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), "cst-net1-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_dir);
        _final = Path.Combine(_dir, "sub", "doc.pdf"); // includes a not-yet-created subdirectory
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true); } catch { }
    }

    private static Stream Bytes(int n) => new MemoryStream(Encoding.ASCII.GetBytes(new string('a', n)));
    private string PartPath => _final + ".part";

    [Fact]
    public async Task CorrectSize_WritesFinalFile_AndLeavesNoPart()
    {
        var ok = await SharePointService.SaveStreamVerifiedAsync(Bytes(1000), _final, 1000, NullLogger.Instance);

        Assert.True(ok);
        Assert.True(File.Exists(_final));
        Assert.Equal(1000, new FileInfo(_final).Length);
        Assert.False(File.Exists(PartPath));
    }

    [Fact]
    public async Task NullExpectedSize_SkipsVerification_AndWritesFile()
    {
        var ok = await SharePointService.SaveStreamVerifiedAsync(Bytes(500), _final, null, NullLogger.Instance);

        Assert.True(ok);
        Assert.True(File.Exists(_final));
        Assert.Equal(500, new FileInfo(_final).Length);
    }

    [Fact]
    public async Task SizeMismatch_ReturnsFalse_NoFinalFile_NoPart()
    {
        // Server said 2000 bytes but the stream only had 1000 (truncated download).
        var ok = await SharePointService.SaveStreamVerifiedAsync(Bytes(1000), _final, 2000, NullLogger.Instance);

        Assert.False(ok);
        Assert.False(File.Exists(_final));
        Assert.False(File.Exists(PartPath));
    }

    [Fact]
    public async Task StreamThrowsMidCopy_Throws_NoFinalFile_NoPart()
    {
        await Assert.ThrowsAsync<IOException>(() =>
            SharePointService.SaveStreamVerifiedAsync(new ThrowingStream(), _final, 1000, NullLogger.Instance));

        Assert.False(File.Exists(_final));
        Assert.False(File.Exists(PartPath));
    }

    [Fact]
    public async Task DoesNotClobberExistingGoodFile_WhenNewDownloadFails()
    {
        // A previously-preserved good PDF must survive a later failed re-download.
        Directory.CreateDirectory(Path.GetDirectoryName(_final)!);
        await File.WriteAllTextAsync(_final, "GOOD-PRESERVED-PDF");

        var ok = await SharePointService.SaveStreamVerifiedAsync(Bytes(10), _final, 999, NullLogger.Instance);

        Assert.False(ok);
        Assert.Equal("GOOD-PRESERVED-PDF", await File.ReadAllTextAsync(_final));
        Assert.False(File.Exists(PartPath));
    }

    // A read-stream that always throws, to simulate a dropped connection mid-download.
    private sealed class ThrowingStream : Stream
    {
        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set { } }
        public override int Read(byte[] buffer, int offset, int count) => throw new IOException("connection dropped");
        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
            => throw new IOException("connection dropped");
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }
}
