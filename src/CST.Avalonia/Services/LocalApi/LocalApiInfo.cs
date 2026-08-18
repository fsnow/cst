using System;
using System.IO;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using System.Threading;
using Serilog;

namespace CST.Avalonia.Services.LocalApi
{
    /// <summary>
    /// The discovery/handshake file for the local API (AI_INTEGRATION.md §6). Written to
    /// <c>…/CSTReader/local-api.json</c> when the server starts so a client (the MCP adapter, a coding agent)
    /// can find the ephemeral <see cref="Port"/> and present the per-session bearer <see cref="Token"/>. The
    /// <see cref="Pid"/> plus <see cref="StartToken"/> let a client detect a stale file after a crash: a bare
    /// pid can be recycled by the OS onto an unrelated process, so the pid is paired with a stable start token to
    /// form a durable identity (#351). Written owner-only where the OS supports it; never contains anything but
    /// this handshake (the token is a session secret, not persisted to settings).
    /// </summary>
    /// <param name="StartToken">An opaque, stable identity token for the publishing process — its start time,
    /// in whatever clock-immune form the platform provides (see <c>ProcessIdentity</c>). 0 = not recorded, in
    /// which case a reader falls back to a pid-only liveness check. Compared only for equality, never interpreted.
    /// On macOS/Windows this exceeds 2^53, so a consumer that round-trips this file through IEEE doubles (e.g. a
    /// naive JS reader) would corrupt it — the only in-process reader is the C# bridge, which keeps it a
    /// <c>long</c>, and external readers need just port/token; revisit as a string if that ever changes.</param>
    public sealed record LocalApiInfo(
        [property: JsonPropertyName("port")] int Port,
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("pid")] int Pid,
        [property: JsonPropertyName("startToken")] long StartToken = 0)
    {
        public const string FileName = "local-api.json";

        /// <summary>Where the (unauthenticated) orientation doc lives, so a client needn't guess. Relative to the base URL.</summary>
        [JsonPropertyName("docs")]
        public string Docs => "/llms.txt";

        private static readonly JsonSerializerOptions Options = new() { WriteIndented = true };

        public static string PathIn(string directory) => Path.Combine(directory, FileName);

        public void Write(string directory)
        {
            var path = PathIn(directory);
            var json = JsonSerializer.Serialize(this, Options);

            // Write to a temp file created 0600 at open(2) (no world-readable window on macOS/Linux — the token is
            // a session secret), then atomically rename it over the real path so a concurrently polling client (the
            // --mcp-bridge relay) never reads torn JSON. (#303)
            var tmp = path + "." + Environment.ProcessId + ".tmp";
            var options = new FileStreamOptions { Mode = FileMode.Create, Access = FileAccess.Write };
            if (!OperatingSystem.IsWindows())
                options.UnixCreateMode = UnixFileMode.UserRead | UnixFileMode.UserWrite;

            using (var fs = new FileStream(tmp, options))
            using (var w = new StreamWriter(fs, new UTF8Encoding(false)))
                w.Write(json);

            MoveIntoPlace(tmp, path);   // rename preserves the temp's 0600 mode
            TrySetOwnerOnly(path);      // belt-and-suspenders; also fixes a pre-existing file's mode
        }

        /// <summary>
        /// Read the handshake, or null when it is absent or unreadable.
        ///
        /// <para><b>Opened share-delete</b> rather than via <c>File.ReadAllText</c>. On Unix a rename over an
        /// open file always succeeds - the reader simply keeps the old inode - but Windows refuses to replace a
        /// file that someone holds without <c>FILE_SHARE_DELETE</c>, and <c>ReadAllText</c> does not ask for it.
        /// Since the <c>--mcp-bridge</c> relay polls this file while the server may be rewriting it, the default
        /// share mode turns a reader into a blocker and makes <see cref="Write"/> fail intermittently. (#506)</para>
        /// </summary>
        public static LocalApiInfo? Read(string directory)
        {
            try
            {
                var path = PathIn(directory);
                if (!File.Exists(path)) return null;

                using var fs = new FileStream(
                    path, FileMode.Open, FileAccess.Read,
                    FileShare.ReadWrite | FileShare.Delete);
                using var reader = new StreamReader(fs, Encoding.UTF8);
                return JsonSerializer.Deserialize<LocalApiInfo>(reader.ReadToEnd(), Options);
            }
            catch { return null; }
        }

        public static void Delete(string directory)
        {
            try { File.Delete(PathIn(directory)); } catch { /* best effort */ }
        }

        /// <summary>
        /// Put the temp file in place of the real one, tolerating a concurrent reader. (#506)
        ///
        /// <para><b>Measured on Windows 11, because the intuitive answer is wrong.</b> Replacing a file that
        /// another handle has open behaves like this:</para>
        ///
        /// <code>
        /// reader's FileShare      File.Move(overwrite: true)      File.Replace
        /// Read                    UnauthorizedAccessException     IOException
        /// ReadWrite               UnauthorizedAccessException     IOException
        /// ReadWrite | Delete      UnauthorizedAccessException     OK
        /// Delete                  UnauthorizedAccessException     OK
        /// </code>
        ///
        /// <para>So <c>File.Move</c> NEVER replaces an open destination on Windows - not even when the reader
        /// allowed delete, which is the case one would expect to work. Only <c>File.Replace</c> (Win32
        /// <c>ReplaceFile</c>) can, and only when the reader permits deletion. BOTH halves are therefore
        /// required: <see cref="Read"/> opens share-delete, and the swap goes through <c>File.Replace</c>.
        /// Doing either alone leaves the failure exactly where it was.</para>
        ///
        /// <para><b>Windows only.</b> Unix renames over open files happily, and the existing <c>File.Move</c>
        /// there carries a property worth keeping: the rename preserves the TEMP file's 0600 mode, which is how
        /// the token avoids a world-readable window (#303). <c>ReplaceFile</c> semantics preserve the
        /// DESTINATION's attributes instead, so switching Unix to it could silently inherit the permissions of
        /// a pre-existing file. There is no problem to fix on that side, and a real property to lose.</para>
        ///
        /// <para><c>File.Replace</c> requires the destination to exist, so a first write still moves.</para>
        /// </summary>
        private static void MoveIntoPlace(string tmp, string path)
        {
            const int attempts = 5;
            for (var attempt = 1; ; attempt++)
            {
                try
                {
                    if (OperatingSystem.IsWindows() && File.Exists(path))
                        File.Replace(tmp, path, destinationBackupFileName: null);
                    else
                        File.Move(tmp, path, overwrite: true);
                    return;
                }
                catch (IOException ex) when (attempt < attempts)
                {
                    // A reader we do NOT control - an antivirus scanner, a backup agent, a third-party client
                    // reading the file the obvious way - can still hold it without share-delete. The window is
                    // milliseconds, so a short backoff turns a startup failure into a brief pause.
                    //
                    // FileNotFoundException is an IOException too, and lands here deliberately: it means the
                    // destination vanished between the check and the call, and the retry simply takes the move
                    // branch instead.
                    var delay = 20 * (int)Math.Pow(2, attempt - 1);   // 20, 40, 80, 160 ms
                    Log.Debug("Handshake swap blocked (attempt {Attempt}/{Total}, {Error}); retrying in {Delay} ms.",
                        attempt, attempts, ex.GetType().Name, delay);
                    Thread.Sleep(delay);
                }
                catch
                {
                    // Give up: remove the temp so repeated failures cannot leave a litter of them beside a file
                    // whose whole job is to be found and parsed by other programs, then report. A handshake that
                    // was never written leaves the server running but undiscoverable - not something to swallow.
                    try { File.Delete(tmp); } catch { /* best effort */ }
                    throw;
                }
            }
        }

        /// <summary>
        /// Restrict the handshake to its owner. macOS/Linux get mode <c>0600</c>; Windows gets an explicit
        /// owner-only ACL with inheritance switched off. (#303, #505)
        ///
        /// <para>Windows was previously a no-op here, which was not a hole so much as an unstated assumption:
        /// <c>%APPDATA%</c> is the Roaming profile and is ACL'd per user by default, so another standard user
        /// cannot read it. But that is inheritance doing the work, and inheritance is exactly the thing a
        /// misconfigured profile, a redirected folder, or an over-broad grant higher up the tree can undo -
        /// silently, with nothing here to notice. The Unix side does not lean on the directory, and neither
        /// should this one.</para>
        ///
        /// <para><c>SetAccessRuleProtection(true, false)</c> is the important call: it detaches the file from
        /// inherited permissions and drops the inherited entries rather than copying them down, so what remains
        /// is the single rule added here. Without <c>preserveInheritance: false</c> the inherited grants would
        /// be flattened onto the file and preserved, which looks like hardening while changing nothing.</para>
        /// </summary>
        private static void TrySetOwnerOnly(string path)
        {
            try
            {
                if (OperatingSystem.IsWindows())
                    SetWindowsOwnerOnly(path);
                else
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch (Exception ex)
            {
                // Don't swallow: a token file left group/world-readable is a real exposure worth a log line. (#303)
                Log.Warning(ex, "Could not restrict permissions on {Path}; the local-API token may be readable by other local users.", path);
            }
        }

        [SupportedOSPlatform("windows")]
        private static void SetWindowsOwnerOnly(string path)
        {
            var user = WindowsIdentity.GetCurrent().User;
            if (user is null)
            {
                // No SID to grant to. Better to leave the inherited ACL - which is per-user in the ordinary
                // case - than to write a protected ACL granting nobody and lock the app out of its own file.
                Log.Warning("Could not determine the current user's SID; leaving inherited permissions on {Path}.", path);
                return;
            }

            var security = new FileSecurity();
            security.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
            security.AddAccessRule(new FileSystemAccessRule(user, FileSystemRights.FullControl, AccessControlType.Allow));
            new FileInfo(path).SetAccessControl(security);
        }
    }
}
