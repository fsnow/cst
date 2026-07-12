using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CST.Search;

namespace CST.Avalonia.Services.Tools
{
    /// <summary>
    /// A tiny bounded cache of a book's decoded XML + its <see cref="BookMarkers"/>, keyed by file path and
    /// last-write time, shared by the occurrences and passage adapters. Paging one book would otherwise re-read
    /// the whole UTF-16 file and rebuild <see cref="BookMarkers"/> on every call. Bounded LRU so it can't grow
    /// with the 217-file corpus; a changed mtime (a re-downloaded / updated book) invalidates the entry. Both
    /// values are read-only after construction (the XML is an immutable string; markers are only queried), so a
    /// shared entry is safe to hand to concurrent callers. (#308 A3-6)
    /// </summary>
    internal static class BookTextCache
    {
        internal readonly record struct Entry(string Xml, BookMarkers Markers);

        private const int Capacity = 6;
        private static readonly object Gate = new();
        private static readonly Dictionary<string, (long Mtime, Entry Entry)> Map = new(StringComparer.Ordinal);
        private static readonly LinkedList<string> Lru = new();   // most-recent at the front

        /// <summary>Get the (XML, markers) for <paramref name="path"/>, reading + parsing on a miss or a changed
        /// mtime. The read/parse happens OUTSIDE the lock (I/O + parse are slow; don't serialize all books behind
        /// one lock) — two concurrent misses for the same book just do the work twice, last write wins.</summary>
        public static async Task<Entry> GetAsync(string path, CancellationToken ct)
        {
            long mtime = File.GetLastWriteTimeUtc(path).Ticks;
            lock (Gate)
            {
                if (Map.TryGetValue(path, out var hit) && hit.Mtime == mtime)
                {
                    Touch(path);
                    return hit.Entry;
                }
            }

            // Char offsets index the decoded (BOM-stripped) UTF-16 text — read it the same way both adapters do.
            string xml = await File.ReadAllTextAsync(path, Encoding.Unicode, ct).ConfigureAwait(false);
            var entry = new Entry(xml, BookMarkers.Build(xml));

            lock (Gate)
            {
                Map[path] = (mtime, entry);
                Touch(path);
                while (Lru.Count > Capacity)
                {
                    var oldest = Lru.Last!.Value;
                    Lru.RemoveLast();
                    Map.Remove(oldest);
                }
            }
            return entry;
        }

        // Move (or add) path to the most-recent end. O(n) over the value, but n <= Capacity.
        private static void Touch(string path)
        {
            Lru.Remove(path);
            Lru.AddFirst(path);
        }
    }
}
