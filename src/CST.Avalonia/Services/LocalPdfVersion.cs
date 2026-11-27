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
            // Size and time are read BEFORE hashing, and checked again after it. If another pane replaces the file
            // while this one is hashing it, the stamp would otherwise pair the new file's size and time with the old
            // file's hash, and that wrong hash would then stand for the new file. (#1136 review, reproduced with a
            // 300 MB file.)
            var before = new FileInfo(pdfPath);
            long size = before.Length, ticks = before.LastWriteTimeUtc.Ticks;
            var stamp = Read(pdfPath);
            if (stamp != null && stamp.Size == size && stamp.ModifiedUtcTicks == ticks)
                return stamp.QuickXorHash;
            var hash = QuickXorHash.ComputeFile(pdfPath);
            var after = new FileInfo(pdfPath);
            if (after.Length == size && after.LastWriteTimeUtc.Ticks == ticks)
                Write(pdfPath, size, ticks, hash);
            return hash;
        }

        /// <summary>Stamp a PDF whose hash is already known (just downloaded and checked against the server's).</summary>
        public static void Remember(string pdfPath, string quickXorHash)
        {
            var info = new FileInfo(pdfPath);
            Write(pdfPath, info.Length, info.LastWriteTimeUtc.Ticks, quickXorHash);
        }

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

        private static void Write(string pdfPath, long size, long modifiedUtcTicks, string hash)
        {
            try
            {
                var path = StampPath(pdfPath);
                var temp = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
                File.WriteAllText(temp, JsonSerializer.Serialize(new Stamp(size, modifiedUtcTicks, hash)));
                File.Move(temp, path, overwrite: true);
            }
            catch
            {
                // Best effort: without a stamp the next open hashes the PDF again.
            }
        }
    }
}
