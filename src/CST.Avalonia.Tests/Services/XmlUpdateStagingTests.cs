using System;
using System.IO;
using System.Threading.Tasks;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// NET-4: XML-update downloads must stage on the same volume as the destination so the final move is an
/// atomic rename (Path.GetTempPath() is often a different filesystem, degrading Move to a non-atomic
/// copy+delete), and the move must tolerate a briefly-open file rather than aborting the apply.
/// </summary>
public class XmlUpdateStagingTests : IDisposable
{
    private readonly string _root;

    public XmlUpdateStagingTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cst-net4-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); } catch { }
    }

    [Fact]
    public void CreateStagingDirectory_IsSiblingOfXmlDir_OnSameVolume()
    {
        var xmlDir = Path.Combine(_root, "xml");
        Directory.CreateDirectory(xmlDir);

        var staging = XmlUpdateService.CreateStagingDirectory(xmlDir);

        Assert.True(Directory.Exists(staging));
        Assert.NotEqual(xmlDir, staging);
        // Same parent as the XML dir => same volume => the final File.Move is an atomic rename.
        Assert.Equal(Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(xmlDir)),
                     Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(staging)));
        Assert.Equal(Path.GetPathRoot(xmlDir), Path.GetPathRoot(staging));
    }

    [Fact]
    public async Task MoveIntoPlaceAsync_MovesFile_AndOverwritesDestination()
    {
        var src = Path.Combine(_root, "src.xml");
        var dest = Path.Combine(_root, "dest.xml");
        await File.WriteAllTextAsync(dest, "OLD");
        await File.WriteAllTextAsync(src, "NEW");

        await XmlUpdateService.MoveIntoPlaceAsync(src, dest);

        Assert.False(File.Exists(src));
        Assert.Equal("NEW", await File.ReadAllTextAsync(dest));
    }
}
