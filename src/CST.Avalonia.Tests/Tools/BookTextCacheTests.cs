using System;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Services.Tools;
using Xunit;

namespace CST.Avalonia.Tests.Tools
{
    /// <summary>
    /// #308 A3-6: the shared book-text cache returns the same decoded XML on a hit (no re-read while paging one
    /// book) and re-reads when the file's mtime changes (a re-downloaded/updated book invalidates the entry).
    /// </summary>
    public class BookTextCacheTests
    {
        [Fact]
        public async Task GetAsync_caches_by_path_and_invalidates_on_mtime_change()
        {
            var path = Path.Combine(Path.GetTempPath(), "cst-btc-" + Guid.NewGuid().ToString("N") + ".xml");
            await File.WriteAllTextAsync(path, "<book><p n=\"1\">one</p></book>", Encoding.Unicode);
            try
            {
                var a = await BookTextCache.GetAsync(path, CancellationToken.None);
                var b = await BookTextCache.GetAsync(path, CancellationToken.None);
                Assert.Same(a.Xml, b.Xml);              // cache hit hands back the SAME decoded string (no re-read)

                // A changed mtime (re-downloaded book) must invalidate: fresh read, new content.
                await File.WriteAllTextAsync(path, "<book><p n=\"2\">two</p></book>", Encoding.Unicode);
                File.SetLastWriteTimeUtc(path, DateTime.UtcNow.AddSeconds(5));

                var c = await BookTextCache.GetAsync(path, CancellationToken.None);
                Assert.NotSame(a.Xml, c.Xml);
                Assert.Contains("two", c.Xml);
            }
            finally { try { File.Delete(path); } catch { } }
        }
    }
}
