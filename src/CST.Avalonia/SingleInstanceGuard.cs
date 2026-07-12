using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;

namespace CST.Avalonia
{
    /// <summary>
    /// #289: ensure only ONE CST Reader GUI runs against a given data directory. Two instances would clobber the
    /// shared settings / app-state / Lucene index and race on <c>local-api.json</c>. The lock is a file in the
    /// DATA DIRECTORY (not the bundle), so it also blocks two copies installed at different paths — the
    /// granularity is one instance per data directory (a future configurable data dir would coexist, correctly).
    /// The OS releases the lock when the process dies — crash and <c>kill -9</c> included — so there is no
    /// stale-lock failure mode. The <c>--mcp-bridge</c> relay and the CLI utility flags never reach this (they
    /// return earlier in <see cref="Program.Main"/>). Bypass for development with <c>CST_ALLOW_MULTIPLE=1</c>.
    /// </summary>
    internal static class SingleInstanceGuard
    {
        internal const string LockFileName = "instance.lock";

        // Held for the process lifetime; closing it (or the process dying) releases the lock.
        private static FileStream? _held;

        [DllImport("libc", SetLastError = true)]
        private static extern int flock(int fd, int operation);
        private const int LOCK_EX = 2, LOCK_NB = 4;

        /// <summary>True if this process became THE instance for <paramref name="dataDirectory"/> (or the guard is
        /// bypassed via <c>CST_ALLOW_MULTIPLE=1</c>); false if another instance already holds the lock.</summary>
        public static bool TryAcquire(string dataDirectory)
        {
            if (Environment.GetEnvironmentVariable("CST_ALLOW_MULTIPLE") == "1")
                return true;

            var held = TryOpenLock(dataDirectory);
            if (held is null) return false;
            _held = held;   // keep the handle (and thus the lock) alive for the process lifetime
            return true;
        }

        /// <summary>
        /// Open the per-data-dir lock file exclusively; null if another holder has it. Uses <c>FileShare.None</c>
        /// (authoritative on Windows) plus an explicit advisory <c>flock</c> on Unix, where .NET's FileShare
        /// enforcement is unreliable for cross-process locking. Exposed for tests.
        /// </summary>
        internal static FileStream? TryOpenLock(string dataDirectory)
        {
            FileStream fs;
            try
            {
                Directory.CreateDirectory(dataDirectory);
                var path = Path.Combine(dataDirectory, LockFileName);
                fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException)
            {
                return null;   // already held where FileShare.None is enforced (e.g. Windows)
            }

            if (!OperatingSystem.IsWindows())
            {
                int fd = (int)fs.SafeFileHandle.DangerousGetHandle();
                if (flock(fd, LOCK_EX | LOCK_NB) != 0)
                {
                    fs.Dispose();
                    return null;   // another instance holds the advisory lock
                }
            }
            return fs;
        }

        /// <summary>Best-effort: bring the already-running instance to the foreground (macOS <c>open</c> the
        /// enclosing <c>.app</c> bundle, which LaunchServices activates rather than relaunching). No-op in dev /
        /// outside a bundle, so it can never spawn a second GUI.</summary>
        public static void ActivateRunningInstance()
        {
            if (!OperatingSystem.IsMacOS()) return;
            try
            {
                var dir = Path.GetDirectoryName(Environment.ProcessPath);
                while (!string.IsNullOrEmpty(dir) && !dir.EndsWith(".app", StringComparison.OrdinalIgnoreCase))
                    dir = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(dir)) return;

                var psi = new ProcessStartInfo("open") { UseShellExecute = false };
                psi.ArgumentList.Add(dir);
                using var _ = Process.Start(psi);
            }
            catch { /* best effort */ }
        }
    }
}
