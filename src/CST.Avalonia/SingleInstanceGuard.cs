using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using Serilog;

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

        // flock() sets errno EWOULDBLOCK (== EAGAIN) for a genuine "another holder has it" — 35 on macOS, 11 on
        // Linux. Any OTHER errno (ENOTSUP/ENOLCK on NFS, ENOMEM, ...) is NOT contention. (#315 A6-1)
        private const int EWOULDBLOCK_MacOS = 35;
        private const int EAGAIN_Linux = 11;

        /// <summary>Outcome of trying to take the lock: we got it, another instance holds it, or the attempt failed
        /// for a reason that is NOT contention (unwritable/networked data dir, flock unsupported, ...).</summary>
        internal enum LockResult { Acquired, Contended, Failed }

        /// <summary>True if this process became THE instance for <paramref name="dataDirectory"/> (or the guard is
        /// bypassed via <c>CST_ALLOW_MULTIPLE=1</c>); false ONLY if another instance already holds the lock. A
        /// non-contention failure (can't create/lock the file) is logged and treated as "proceed unguarded"
        /// (returns true, no activation) — never misdiagnosed as another instance, which would relaunch/loop.</summary>
        public static bool TryAcquire(string dataDirectory)
        {
            if (Environment.GetEnvironmentVariable("CST_ALLOW_MULTIPLE") == "1")
                return true;

            var (result, held) = TryOpenLock(dataDirectory);
            switch (result)
            {
                case LockResult.Acquired:
                    _held = held;   // keep the handle (and thus the lock) alive for the process lifetime
                    return true;
                case LockResult.Contended:
                    return false;   // another instance owns it → caller activates that one and exits
                default:
                    return true;    // couldn't lock for a non-contention reason → run unguarded (already logged)
            }
        }

        /// <summary>
        /// Try to open the per-data-dir lock file exclusively. Uses <c>FileShare.None</c> (authoritative on Windows)
        /// plus an explicit advisory <c>flock</c> on Unix, where .NET's FileShare enforcement is unreliable for
        /// cross-process locking. Distinguishes genuine contention (<see cref="LockResult.Contended"/>) from any
        /// other failure (<see cref="LockResult.Failed"/>), inspecting errno / HResult rather than assuming every
        /// error is another instance. Exposed for tests.
        /// </summary>
        internal static (LockResult result, FileStream? stream) TryOpenLock(string dataDirectory)
        {
            FileStream fs;
            try
            {
                Directory.CreateDirectory(dataDirectory);
                var path = Path.Combine(dataDirectory, LockFileName);
                fs = new FileStream(path, FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            }
            catch (IOException ex) when (IsSharingViolation(ex))
            {
                return (LockResult.Contended, null);   // another holder where FileShare.None is enforced (Windows)
            }
            catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
            {
                // NOT contention: unwritable/networked data dir, a read-only or directory `instance.lock`, disk
                // full, AV-held handle, ... Proceed unguarded rather than misdiagnose as another instance and
                // relaunch/loop or die silently. (#315 A6-1/A6-2)
                Log.Warning(ex, "SingleInstanceGuard: could not open the lock file in {Dir}; running WITHOUT the single-instance guard.", dataDirectory);
                return (LockResult.Failed, null);
            }

            if (!OperatingSystem.IsWindows())
            {
                int fd = (int)fs.SafeFileHandle.DangerousGetHandle();
                if (flock(fd, LOCK_EX | LOCK_NB) != 0)
                {
                    int err = Marshal.GetLastPInvokeError();
                    fs.Dispose();
                    if (err == EWOULDBLOCK_MacOS || err == EAGAIN_Linux)
                        return (LockResult.Contended, null);   // genuine "another instance holds the advisory lock"
                    // ENOTSUP/ENOLCK (NFS/AFS home), ENOMEM, ... — advisory locking unavailable, not contention.
                    Log.Warning("SingleInstanceGuard: flock failed (errno {Errno}) in {Dir}; running WITHOUT the single-instance guard.", err, dataDirectory);
                    return (LockResult.Failed, null);
                }
            }
            return (LockResult.Acquired, fs);
        }

        // FileShare.None contention surfaces as an IOException whose HResult low bits are the OS error code:
        // Windows ERROR_SHARING_VIOLATION (32) / ERROR_LOCK_VIOLATION (33); on Unix .NET encodes the raw errno,
        // and the FileShare.None conflict is EWOULDBLOCK/EAGAIN (35 macOS / 11 Linux). Any OTHER code is a real
        // error (permission, is-a-directory, disk full), NOT contention. (#315 A6-1)
        private static bool IsSharingViolation(IOException ex)
        {
            int code = ex.HResult & 0xFFFF;
            return OperatingSystem.IsWindows()
                ? code == 32 || code == 33
                : code == EWOULDBLOCK_MacOS || code == EAGAIN_Linux;
        }

        /// <summary>Best-effort: bring the already-running instance to the foreground (macOS <c>open</c> the
        /// enclosing <c>.app</c> bundle, which LaunchServices activates rather than relaunching). No-op in dev /
        /// outside a bundle, so it can never spawn a second GUI.</summary>
        public static void ActivateRunningInstance()
        {
            if (!OperatingSystem.IsMacOS())
            {
                // No foreground-activation path on Windows/Linux yet — say so (to the now-real logger, #316 A6-3)
                // instead of a silent no-op that looks broken. (#317 A6-6)
                Log.Information("SingleInstanceGuard: another instance is running; activation is macOS-only, so just exiting.");
                return;
            }
            try
            {
                var dir = Path.GetDirectoryName(Environment.ProcessPath);
                // Require a REAL bundle: the name ends .app AND it has Contents/Info.plist — not merely any dir
                // whose name happens to end .app. (#317 A6-7)
                while (!string.IsNullOrEmpty(dir) &&
                       !(dir.EndsWith(".app", StringComparison.OrdinalIgnoreCase) &&
                         File.Exists(Path.Combine(dir, "Contents", "Info.plist"))))
                    dir = Path.GetDirectoryName(dir);
                if (string.IsNullOrEmpty(dir)) return;

                // `open` asks LaunchServices to activate the registered instance. WaitForExit so LS has acted
                // before Main exits — shrinks the flash/relaunch race against a dev (unregistered) holder. (#317 A6-5)
                var psi = new ProcessStartInfo("open") { UseShellExecute = false };
                psi.ArgumentList.Add(dir);
                using var p = Process.Start(psi);
                p?.WaitForExit(5000);
            }
            catch { /* best effort */ }
        }
    }
}
