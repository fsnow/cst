using System;
using System.IO;
using System.Text.Json;

namespace CST.Avalonia.Services
{
    /// <summary>
    /// Which version of a source PDF is on disk: its <see cref="QuickXorHash"/>, remembered in a small hidden
    /// file beside it (<c>.&lt;name&gt;.version.json</c>) so that a 300 MB scan is hashed once, not on every
    /// open. The remembered hash holds only while the PDF's size and modification time are the ones it was
    /// taken at; any other file there is hashed again. (#1136)
    /// </summary>
    public static class LocalPdfVersion
    {
        private sealed record Stamp(long Size, long ModifiedUtcTicks, string QuickXorHash);

        internal static string StampPath(string pdfPath) =>
            Path.Combine(Path.GetDirectoryName(pdfPath) ?? "", "." + Path.GetFileName(pdfPath) + ".version.json");

        /// <summary>The PDF's hash, from its stamp if the stamp still describes it, otherwise computed and stamped.</summary>
        public static string Get(string pdfPath)
        {
            var info = new FileInfo(pdfPath);
            var stamp = Read(pdfPath);
            if (stamp != null && stamp.Size == info.Length && stamp.ModifiedUtcTicks == info.LastWriteTimeUtc.Ticks)
                return stamp.QuickXorHash;
            var hash = QuickXorHash.ComputeFile(pdfPath);
            Write(pdfPath, info, hash);
            return hash;
        }

        /// <summary>Stamp a PDF whose hash is already known (just downloaded and checked against the server's).</summary>
        public static void Remember(string pdfPath, string quickXorHash) =>
            Write(pdfPath, new FileInfo(pdfPath), quickXorHash);

        private static Stamp? Read(string pdfPath)
        {
            try
            {
                var path = StampPath(pdfPath);
                return File.Exists(path) ? JsonSerializer.Deserialize<Stamp>(File.ReadAllText(path)) : null;
            }
            catch
            {
                return null;   // an unreadable stamp is only a lost shortcut: the PDF is hashed again
            }
        }

        private static void Write(string pdfPath, FileInfo info, string hash)
        {
            try
            {
                var path = StampPath(pdfPath);
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(new Stamp(info.Length, info.LastWriteTimeUtc.Ticks, hash)));
                File.Move(temp, path, overwrite: true);
            }
            catch
            {
                // Best effort: without a stamp the next open hashes the PDF again.
            }
        }
    }
}
