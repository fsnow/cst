using System;
using System.IO;
using System.IO.Pipes;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Serilog;

namespace CST.Avalonia.Services
{
    /// <summary>
    /// Lets a second launch bring the ALREADY-RUNNING instance to the front instead of exiting silently. (#568)
    ///
    /// <para>macOS needs none of this: <c>open</c>-ing the bundle asks LaunchServices to activate the registered
    /// instance, and <see cref="SingleInstanceGuard.ActivateRunningInstance"/> keeps doing exactly that. Windows
    /// has no equivalent, so the running process has to be asked directly - and it is the only one that can
    /// raise its own window anyway.</para>
    ///
    /// <para><b>Why a pipe rather than finding the window.</b> The obvious alternative - enumerate top-level
    /// windows for the known pid and <c>SetForegroundWindow</c> - runs straight into the foreground lock:
    /// Windows refuses foreground changes from a process that does not already own the foreground, so it fails
    /// silently or merely flashes the taskbar button. Asking the owner to raise itself sidesteps that, and it
    /// is the conventional shape for this on Windows.</para>
    ///
    /// <para><b>The foreground handshake.</b> Even the owner cannot raise itself unaided, for the same reason.
    /// But the SECOND process can: it was just launched by the user, so it holds the foreground privilege, and
    /// <c>AllowSetForegroundWindow</c> lets it hand that privilege to a named pid. So the server sends its pid
    /// first, the client grants it, and only then asks for activation. Without that step the request arrives and
    /// the window stays stubbornly behind whatever the user was looking at.</para>
    ///
    /// <para>Deliberately not tied to Windows in its own right: the pipe half is cross-platform, and #507 needs
    /// the same "activate the running instance" capability for <c>--mcp-bridge</c>. Only the foreground grant is
    /// Windows-specific.</para>
    /// </summary>
    internal static class InstanceActivation
    {
        private const string ActivateCommand = "ACTIVATE";

        /// <summary>Enough for a local connect on a machine under load, short enough not to hang a launch.</summary>
        private static readonly TimeSpan ConnectTimeout = TimeSpan.FromSeconds(3);

        [DllImport("user32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool AllowSetForegroundWindow(int dwProcessId);

        /// <summary>
        /// One pipe per DATA DIRECTORY, matching the granularity of the lock itself - two copies pointed at
        /// different data directories are both legitimately running and must not activate each other.
        ///
        /// <para>Hashed rather than embedded: a path can exceed the pipe-name length limit and can contain
        /// characters that are not legal in one. Case-folded because the comparison has to agree with the
        /// filesystem's own view of whether two paths are the same directory.</para>
        /// </summary>
        internal static string PipeNameFor(string dataDirectory)
        {
            var normalized = dataDirectory.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
            if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
                normalized = normalized.ToLowerInvariant();

            var digest = SHA256.HashData(Encoding.UTF8.GetBytes(normalized));
            return "CSTReader-activate-" + Convert.ToHexString(digest, 0, 8).ToLowerInvariant();
        }

        /// <summary>
        /// Listen for activation requests for the lifetime of the process. Failure is non-fatal by design: the
        /// app runs perfectly well without it, and the only casualty is that a second launch goes back to
        /// exiting quietly - which is where this started.
        /// </summary>
        internal static void StartListener(string dataDirectory, Action onActivate)
        {
            var pipeName = PipeNameFor(dataDirectory);

            // Long-running background loop rather than a Task on the pool: it spends its life blocked on a
            // connection, which is precisely what the pool should not be used for.
            var thread = new Thread(() => ListenLoop(pipeName, onActivate))
            {
                IsBackground = true,   // must never keep the process alive after the window closes
                Name = "instance-activation",
            };
            thread.Start();

            Log.Debug("InstanceActivation: listening on {PipeName} for {Dir}", pipeName, dataDirectory);
        }

        private static void ListenLoop(string pipeName, Action onActivate)
        {
            while (true)
            {
                try
                {
                    // Rebuilt per connection: a NamedPipeServerStream serves one client and cannot be reused.
                    using var server = new NamedPipeServerStream(
                        pipeName, PipeDirection.InOut, 1, PipeTransmissionMode.Byte, PipeOptions.None);

                    server.WaitForConnection();

                    using var reader = new StreamReader(server, Encoding.UTF8, false, 128, leaveOpen: true);
                    using var writer = new StreamWriter(server, new UTF8Encoding(false), 128, leaveOpen: true)
                    {
                        AutoFlush = true,
                    };

                    // Our pid first, so the client can hand us the foreground privilege before asking.
                    writer.WriteLine(Environment.ProcessId);

                    if (reader.ReadLine() == ActivateCommand)
                    {
                        Log.Information("InstanceActivation: another launch asked us to come forward.");
                        onActivate();
                    }
                }
                catch (Exception ex)
                {
                    // A broken connection is ordinary - the client exits the moment it has sent the command.
                    // Keep listening; a failure here must never take down the running app.
                    Log.Debug("InstanceActivation: listener iteration ended | {Details}", ex.Message);

                    // Guard against a tight spin if the pipe itself cannot be created at all (name taken by a
                    // stale process, sandbox refusing it). Sleeping briefly keeps a broken state cheap.
                    Thread.Sleep(250);
                }
            }
        }

        /// <summary>
        /// Ask the running instance to come forward. Returns false when there was nobody to ask or the request
        /// could not be delivered - the caller then falls back to simply exiting, which is the previous
        /// behaviour rather than a new failure.
        /// </summary>
        internal static bool RequestActivation(string dataDirectory)
        {
            var pipeName = PipeNameFor(dataDirectory);

            try
            {
                using var client = new NamedPipeClientStream(".", pipeName, PipeDirection.InOut);
                client.Connect((int)ConnectTimeout.TotalMilliseconds);

                using var reader = new StreamReader(client, Encoding.UTF8, false, 128, leaveOpen: true);
                using var writer = new StreamWriter(client, new UTF8Encoding(false), 128, leaveOpen: true)
                {
                    AutoFlush = true,
                };

                var line = reader.ReadLine();
                if (!int.TryParse(line, out var serverPid))
                {
                    Log.Warning("InstanceActivation: running instance did not identify itself; not activating.");
                    return false;
                }

                // Hand over the foreground privilege we hold as the freshly-launched process. Without this the
                // other instance is allowed to ask and Windows is entitled to ignore it. Best-effort: a false
                // return still leaves the request worth sending, since the window may already be foreground-able.
                if (OperatingSystem.IsWindows() && !AllowSetForegroundWindow(serverPid))
                {
                    Log.Debug("InstanceActivation: AllowSetForegroundWindow({Pid}) declined (error {Error}); "
                              + "asking anyway.", serverPid, Marshal.GetLastWin32Error());
                }

                writer.WriteLine(ActivateCommand);

                // Let the request land before this process exits and the pipe drops.
                client.Flush();
                Log.Information("InstanceActivation: asked instance {Pid} to come forward.", serverPid);
                return true;
            }
            catch (TimeoutException)
            {
                // The lock is held but nothing is listening. Most likely an older build, or the running
                // instance is mid-startup and has not begun listening yet.
                Log.Information("InstanceActivation: the running instance is not accepting activation requests.");
                return false;
            }
            catch (Exception ex)
            {
                Log.Warning("InstanceActivation: could not ask the running instance to come forward | {Details}",
                    ex.Message);
                return false;
            }
        }
    }
}
