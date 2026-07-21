using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using Serilog;

namespace CST.Avalonia.Services.LocalApi
{
    /// <summary>
    /// Durable process identity for the handshake liveness check. A bare pid is NOT a stable identity: after the
    /// app crashes (leaving <c>local-api.json</c> behind) the OS can recycle its pid onto an unrelated live
    /// process, and a pid-only check then passes for a dead loopback port. Pairing the pid with a stable
    /// START TOKEN closes that — a recycled pid necessarily belongs to a process that started later. (#351)
    /// </summary>
    internal static class ProcessIdentity
    {
        private enum Lookup { Ok, NotRunning, AccessDenied, Unknown }

        /// <summary>
        /// Start token of the CURRENT process, to record in the handshake. Returns 0 if it cannot be read — the
        /// file then carries no identity beyond the pid and callers fall back to the pid-only check.
        /// </summary>
        public static long CurrentStartToken()
        {
            using var self = Process.GetCurrentProcess();
            return TryGetStartToken(Environment.ProcessId, self, out long token) == Lookup.Ok ? token : 0;
        }

        /// <summary>
        /// Whether the process at <paramref name="pid"/> is the same instance that wrote <paramref name="recordedToken"/>.
        /// Comparison is EXACT: the token is a stable, clock-immune per-platform value, and a self-query and an
        /// other-process query of the same live pid yield the identical value on all three platforms (Linux uses
        /// the boot-relative jiffy start time from <c>/proc</c>; macOS/Windows an absolute creation timestamp), so
        /// no tolerance is needed and none is wanted — slack only widens the window for a false match.
        /// <para>
        /// A dead pid is a definite "no". A zero <paramref name="recordedToken"/> means the handshake carried no
        /// token, so this degrades to a pid-only liveness check (pre-#351 behaviour). A live pid whose token
        /// cannot be read because access is DENIED is treated as "not us" regardless of
        /// <paramref name="assumeOnError"/> — the app always runs as the invoking user, and a same-user process's
        /// start time is always readable, so access-denied means the pid was recycled onto another user's process.
        /// Only a genuinely indeterminate failure defers to <paramref name="assumeOnError"/>, whose safe default
        /// differs between the two call sites.
        /// </para>
        /// </summary>
        public static bool IsSameProcess(int pid, long recordedToken, bool assumeOnError)
        {
            if (pid <= 0) return false;

            Process p;
            try { p = Process.GetProcessById(pid); }
            catch (ArgumentException) { return false; }          // definitively not running
            catch (Exception ex)
            {
                Log.Warning(ex, "Liveness check for pid {Pid} failed; assuming {Assume}.", pid, assumeOnError);
                return assumeOnError;
            }

            using (p)
            {
                if (recordedToken == 0) return true;             // no identity recorded → pid-only fallback

                return TryGetStartToken(pid, p, out long live) switch
                {
                    Lookup.Ok => live == recordedToken,
                    Lookup.NotRunning => false,                  // exited during the race → gone
                    Lookup.AccessDenied => false,                // recycled onto another user's process → not us
                    _ => assumeOnError                           // genuinely indeterminate
                };
            }
        }

        // Linux Process.StartTime is recomputed each query as (wall clock) - (elapsed since boot) + (start
        // jiffies), so a clock step (NTP, VM resume) shifts the SAME process's reported start time — unusable as
        // an identity. The raw jiffy field is boot-relative and clock-immune, so read it directly instead. (#351,
        // fable) macOS/Windows expose an absolute creation timestamp, which is stable, so Process.StartTime is
        // fine there.
        private static Lookup TryGetStartToken(int pid, Process p, out long token)
        {
            token = 0;
            if (OperatingSystem.IsLinux())
            {
                string stat;
                try { stat = File.ReadAllText($"/proc/{pid}/stat"); }
                catch (Exception ex) when (ex is FileNotFoundException or DirectoryNotFoundException)
                {
                    return Lookup.NotRunning;
                }
                catch (UnauthorizedAccessException) { return Lookup.AccessDenied; }
                catch (Exception ex) { Log.Warning(ex, "Could not read /proc/{Pid}/stat.", pid); return Lookup.Unknown; }

                return TryParseLinuxStartJiffies(stat, out token) ? Lookup.Ok : Lookup.Unknown;
            }

            try { token = p.StartTime.ToUniversalTime().Ticks; return Lookup.Ok; }
            catch (InvalidOperationException) { return Lookup.NotRunning; }      // exited between calls
            catch (Win32Exception) { return Lookup.AccessDenied; }              // another user's process
            catch (Exception ex) { Log.Warning(ex, "Could not read start time of pid {Pid}.", pid); return Lookup.Unknown; }
        }

        /// <summary>
        /// Parse the start-time jiffy count (field 22) from a Linux <c>/proc/&lt;pid&gt;/stat</c> line. Field 2
        /// (comm) is wrapped in parentheses and may itself contain spaces and parentheses, so anchor on the LAST
        /// ')': everything after it is a clean space-separated list starting at field 3 (state), making start
        /// time index 19 in that tail. Internal + testable because the comm-quoting is the easy thing to get
        /// wrong.
        /// </summary>
        internal static bool TryParseLinuxStartJiffies(string stat, out long jiffies)
        {
            jiffies = 0;
            if (string.IsNullOrEmpty(stat)) return false;
            int close = stat.LastIndexOf(')');
            if (close < 0 || close + 2 >= stat.Length) return false;
            var tail = stat.Substring(close + 2).Split(' ', StringSplitOptions.RemoveEmptyEntries);
            // tail[0] = state (field 3), so field 22 = tail[19].
            return tail.Length > 19 && long.TryParse(tail[19], out jiffies);
        }
    }
}
