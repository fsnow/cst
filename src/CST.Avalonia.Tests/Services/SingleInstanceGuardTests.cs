using System;
using System.IO;
using CST.Avalonia;
using Xunit;

namespace CST.Avalonia.Tests.Services
{
    /// <summary>
    /// #289: one CST Reader per data directory. The lock lives in the data dir (not the bundle), releases on
    /// process death, and is exposed here via <see cref="SingleInstanceGuard.TryOpenLock"/> so a second holder
    /// can be exercised in-process.
    /// </summary>
    public class SingleInstanceGuardTests
    {
        [Fact]
        public void Only_one_holder_of_the_data_dir_lock_and_it_frees_on_release()
        {
            var dir = Path.Combine(Path.GetTempPath(), "cst-lock-" + Guid.NewGuid().ToString("N"));
            try
            {
                var first = SingleInstanceGuard.TryOpenLock(dir);
                Assert.NotNull(first);                                 // this "instance" acquires

                Assert.Null(SingleInstanceGuard.TryOpenLock(dir));     // a second is blocked while the first holds it

                first!.Dispose();                                      // release (process death frees it the same way)

                var third = SingleInstanceGuard.TryOpenLock(dir);
                Assert.NotNull(third);                                 // available again once released
                third!.Dispose();
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }
    }
}
