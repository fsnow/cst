using System;
using System.IO;
using System.Threading.Tasks;
using CST.Avalonia.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// #1136 against the real SharePoint: a cached copy that differs from the server's is replaced, and one that
/// matches is kept. Network-dependent, so it runs only with CST_SHAREPOINT_LIVE set to a cached PDF's path
/// under the CSTReader pdfs folder (it is copied into a temporary cache; the real one is never touched).
/// </summary>
public class SharePointVersionLiveTests
{
    [Fact]
    public async Task ACachedCopyIsKeptTheSameAsSharePoints()
    {
        var source = Environment.GetEnvironmentVariable("CST_SHAREPOINT_LIVE");
        if (string.IsNullOrEmpty(source) || !File.Exists(source))
            return;
        var pdfsRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            CST.Avalonia.Constants.AppConstants.AppDataDirectoryName, "pdfs");
        var relative = Path.GetRelativePath(pdfsRoot, source).Replace(Path.DirectorySeparatorChar, '/');
        // Only a PDF inside the cache: anything else would make the copy below land outside the temp directory.
        Assert.False(relative.StartsWith("..") || Path.IsPathRooted(relative), $"{source} is not under {pdfsRoot}");

        var dir = Path.Combine(Path.GetTempPath(), "cst-1136-live-" + Guid.NewGuid().ToString("N"));
        try
        {
            var cached = Path.Combine(dir, relative.Replace('/', Path.DirectorySeparatorChar));
            Directory.CreateDirectory(Path.GetDirectoryName(cached)!);
            File.Copy(source, cached);
            var before = QuickXorHash.ComputeFile(cached);

            var service = new SharePointService(NullLogger<SharePointService>.Instance, dir);
            var first = await service.GetPdfAsync(relative);
            Assert.NotNull(first);
            Assert.Equal(cached, first!.LocalPath);
            Assert.Equal(QuickXorHash.ComputeFile(cached), first.QuickXorHash);
            Console.WriteLine($"cached {before} -> now {first.QuickXorHash} ({(before == first.QuickXorHash ? "kept" : "replaced")})");

            // Now the same as the server's: a second open keeps it.
            var writtenAt = File.GetLastWriteTimeUtc(cached);
            var second = await service.GetPdfAsync(relative);
            Assert.Equal(first.QuickXorHash, second!.QuickXorHash);
            Assert.Equal(writtenAt, File.GetLastWriteTimeUtc(cached));
        }
        finally
        {
            try { Directory.Delete(dir, recursive: true); } catch { }
        }
    }
}
