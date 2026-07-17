using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using CST;
using CST.Avalonia.Services.Tools;

namespace CST.Avalonia.Services.LocalApi
{
    /// <summary>
    /// The distinct sub-book codes of each Multi book (e.g. <c>s0403a.att.xml</c> → <c>["an5","an6","an7"]</c>),
    /// so the <c>books</c> catalog can advertise the valid <c>bookCode</c>s for a Multi book instead of leaving
    /// an agent to guess (a wrong guess dead-ends with no recovery). (#266)
    ///
    /// The codes come from the book div structure in the TEI XML, so they need the corpus. The map is primed
    /// once, at API-server startup, off the shared <see cref="BookTextCache"/> (the same decode + parse the
    /// passage tool uses); the <c>books</c> tool itself stays static and dependency-free — it just reads this
    /// ready cache, and reports empty codes for any book if the corpus was unavailable at prime time.
    /// </summary>
    internal static class MultiBookCodes
    {
        private static readonly Dictionary<string, IReadOnlyList<string>> _codes = new(StringComparer.Ordinal);
        private static readonly object _gate = new();

        /// <summary>Parse the sub-book codes of every Multi book once. Safe to call when the XML dir is missing
        /// or a book can't be read — those books simply get no codes.</summary>
        public static async Task PrimeAsync(string? xmlDir, CancellationToken ct = default)
        {
            if (string.IsNullOrEmpty(xmlDir)) return;
            foreach (var book in Books.Inst)
            {
                if (book.BookType != BookType.Multi) continue;
                ct.ThrowIfCancellationRequested();
                var path = Path.Combine(xmlDir!, book.FileName);
                if (!File.Exists(path)) continue;
                try
                {
                    var entry = await BookTextCache.GetAsync(path, ct).ConfigureAwait(false);
                    var codes = entry.Markers.DistinctBookCodes();
                    lock (_gate) _codes[book.FileName] = codes;
                }
                catch (OperationCanceledException) { throw; }
                catch { /* unreadable/corrupt book → no codes, never fail startup */ }
            }
        }

        /// <summary>The Multi book's sub-book codes, or an empty list (non-Multi, un-primed, or unreadable).
        /// Takes primitives (not a Book) to stay clear of the CST.Book / CST.Avalonia.Services.Book name clash.</summary>
        public static IReadOnlyList<string> For(string fileName, bool isMulti)
        {
            if (!isMulti) return Array.Empty<string>();
            lock (_gate)
                return _codes.TryGetValue(fileName, out var codes) ? codes : Array.Empty<string>();
        }
    }
}
