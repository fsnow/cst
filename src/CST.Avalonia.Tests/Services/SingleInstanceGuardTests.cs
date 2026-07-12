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
                var (r1, first) = SingleInstanceGuard.TryOpenLock(dir);
                Assert.Equal(SingleInstanceGuard.LockResult.Acquired, r1);   // this "instance" acquires
                Assert.NotNull(first);

                var (r2, second) = SingleInstanceGuard.TryOpenLock(dir);     // a second is CONTENDED while first holds it
                Assert.Equal(SingleInstanceGuard.LockResult.Contended, r2);
                Assert.Null(second);

                first!.Dispose();                                            // release (process death frees it the same way)

                var (r3, third) = SingleInstanceGuard.TryOpenLock(dir);
                Assert.Equal(SingleInstanceGuard.LockResult.Acquired, r3);   // available again once released
                third!.Dispose();
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }

        [Fact]
        public void A_non_contention_failure_is_Failed_not_Contended()
        {
            // #315 A6-1/A6-2: a failure that is NOT another instance (here `instance.lock` existing as a DIRECTORY,
            // so the file open throws) must be reported as Failed — proceed unguarded — never Contended, which
            // would wrongly activate/relaunch and could loop.
            var dir = Path.Combine(Path.GetTempPath(), "cst-lock-fail-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(Path.Combine(dir, SingleInstanceGuard.LockFileName));   // lock path is a dir
            try
            {
                var (result, stream) = SingleInstanceGuard.TryOpenLock(dir);
                Assert.Equal(SingleInstanceGuard.LockResult.Failed, result);
                Assert.Null(stream);
            }
            finally
            {
                try { Directory.Delete(dir, recursive: true); } catch { }
            }
        }
    }
}
