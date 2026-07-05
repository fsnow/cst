using System;
using System.IO;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace CST.Avalonia.Services.LocalApi
{
    /// <summary>
    /// The discovery/handshake file for the local API (AI_INTEGRATION.md §6). Written to
    /// <c>…/CSTReader/local-api.json</c> when the server starts so a client (the MCP adapter, a coding agent)
    /// can find the ephemeral <see cref="Port"/> and present the per-session bearer <see cref="Token"/>. The
    /// <see cref="Pid"/> lets a client detect a stale file after a crash. Written owner-only where the OS
    /// supports it; never contains anything but this handshake (the token is a session secret, not persisted
    /// to settings).
    /// </summary>
    public sealed record LocalApiInfo(
        [property: JsonPropertyName("port")] int Port,
        [property: JsonPropertyName("token")] string Token,
        [property: JsonPropertyName("pid")] int Pid)
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
            File.WriteAllText(path, JsonSerializer.Serialize(this, Options));
            TrySetOwnerOnly(path);
        }

        public static LocalApiInfo? Read(string directory)
        {
            try
            {
                var path = PathIn(directory);
                if (!File.Exists(path)) return null;
                return JsonSerializer.Deserialize<LocalApiInfo>(File.ReadAllText(path), Options);
            }
            catch { return null; }
        }

        public static void Delete(string directory)
        {
            try { File.Delete(PathIn(directory)); } catch { /* best effort */ }
        }

        private static void TrySetOwnerOnly(string path)
        {
            // Owner read/write only, where the OS models Unix permissions (macOS/Linux). No-op on Windows.
            try
            {
                if (!OperatingSystem.IsWindows())
                    File.SetUnixFileMode(path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
            }
            catch { /* best effort */ }
        }
    }
}
