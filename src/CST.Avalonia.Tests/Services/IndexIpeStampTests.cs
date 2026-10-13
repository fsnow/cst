using System;
using System.IO;
using CST.Avalonia.Services;
using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// The search index records which IPE version built it, so an index from an older IPE is rebuilt instead of
/// silently missing (#1049).
/// </summary>
public class IndexIpeStampTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cst-ipe-stamp-" + Guid.NewGuid().ToString("N"));

    public void Dispose()
    {
        if (Directory.Exists(_dir)) Directory.Delete(_dir, recursive: true);
    }

    [Fact]
    public void AnIndexFromBeforeTheStamp_IsNotCurrent()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, "segments_1"), ""); // an index with no stamp
        Assert.False(IndexIpeStamp.IsCurrent(_dir));
    }

    [Fact]
    public void AnOlderVersion_IsNotCurrent()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, IndexIpeStamp.FileName), (IpeVersion.Current - 1).ToString());
        Assert.False(IndexIpeStamp.IsCurrent(_dir));
    }

    [Fact]
    public void Garbage_IsNotCurrent()
    {
        Directory.CreateDirectory(_dir);
        File.WriteAllText(Path.Combine(_dir, IndexIpeStamp.FileName), "x");
        Assert.False(IndexIpeStamp.IsCurrent(_dir));
    }

    [Fact]
    public void Write_ThenIsCurrent()
    {
        IndexIpeStamp.Write(_dir); // creates the directory if needed
        Assert.True(IndexIpeStamp.IsCurrent(_dir));
    }

    [Fact]
    public void MissingDirectory_IsNotCurrent() => Assert.False(IndexIpeStamp.IsCurrent(_dir));
}
