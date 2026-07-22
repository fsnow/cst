using System;
using System.Collections.Generic;
using Microsoft.Data.Sqlite;

namespace CST.Lexicon
{
    /// <summary>
    /// Reads a canonical lexicon and answers headword lookups. Loads the (small — tens of thousands of) entries
    /// into a sorted in-memory array once at open, then serves lookups IO-free, with the exact/prefix/nearest
    /// semantics ported from the flat-file <c>DictionaryIndex</c> (CST4's <c>FormDictionary.Search</c>), adapted
    /// so HOMONYMS — several rows sharing one key — all surface together.
    /// </summary>
    public sealed class LexiconReader
    {
        private readonly LexiconEntry[] _entries;   // sorted by (ipe_key ordinal, homonym, rowid)
        public LexiconMeta Meta { get; }

        private LexiconReader(LexiconEntry[] entries, LexiconMeta meta)
        {
            _entries = entries;
            Meta = meta;
        }

        public int Count => _entries.Length;

        /// <summary>Open a lexicon file, or throw if it is missing/malformed/too new for this schema.</summary>
        public static LexiconReader Open(string dbPath)
        {
            var csb = new SqliteConnectionStringBuilder { DataSource = dbPath, Mode = SqliteOpenMode.ReadOnly };
            using var conn = new SqliteConnection(csb.ToString());
            conn.Open();

            var meta = ReadMeta(conn);

            var list = new List<LexiconEntry>();
            using (var cmd = conn.CreateCommand())
            {
                // Sort in SQL by key then homonym then rowid so homonyms surface in published order.
                cmd.CommandText =
                    "SELECT ipe_key, headword, homonym, body_html FROM entry ORDER BY ipe_key, homonym, id";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    list.Add(new LexiconEntry(r.GetString(0), r.GetString(1), r.GetInt32(2), r.GetString(3)));
            }
            return new LexiconReader(list.ToArray(), meta);
        }

        /// <summary>
        /// Look up an already-derived query key (see <see cref="LexiconKey.DeriveQueryKey"/>). Returns the exact
        /// entries plus the prefix run, or on a miss the nearest-common-prefix run. <paramref name="max"/>
        /// bounds the result (0 or negative = unbounded).
        /// </summary>
        public IReadOnlyList<LexiconEntry> LookupByKey(string queryKey, int max = 0)
        {
            var results = new List<LexiconEntry>();
            if (string.IsNullOrEmpty(queryKey) || _entries.Length == 0)
                return results;

            int lo = LowerBound(queryKey);   // first entry with key >= queryKey

            // Exact-key hit: collect the contiguous StartsWith run (homonyms sharing the key + deeper prefixes).
            if (lo < _entries.Length && string.CompareOrdinal(_entries[lo].IpeKey, queryKey) == 0)
            {
                for (int i = lo; i < _entries.Length; i++)
                {
                    if (!_entries[i].IpeKey.StartsWith(queryKey, StringComparison.Ordinal)) break;
                    results.Add(_entries[i]);
                    if (Reached(results, max)) return results;
                }
                return results;
            }

            // Miss: pick the neighbor side (behind / ahead of the insertion point) sharing the most leading
            // characters, and collect the consecutive run tied at that length. Ported from DictionaryIndex.
            int commonBehind = lo - 1 >= 0 ? CommonPrefix(queryKey, _entries[lo - 1].IpeKey) : 0;
            int commonAhead = lo < _entries.Length ? CommonPrefix(queryKey, _entries[lo].IpeKey) : 0;

            if (commonBehind >= commonAhead && commonBehind > 0)
            {
                var stack = new Stack<LexiconEntry>();
                for (int i = lo - 1; i >= 0; i--)
                {
                    if (CommonPrefix(queryKey, _entries[i].IpeKey) == commonBehind) stack.Push(_entries[i]);
                    else break;
                }
                while (stack.Count > 0)
                {
                    results.Add(stack.Pop());
                    if (Reached(results, max)) return results;
                }
            }

            if (commonAhead >= commonBehind && commonAhead > 0)
            {
                for (int i = lo; i < _entries.Length; i++)
                {
                    if (CommonPrefix(queryKey, _entries[i].IpeKey) != commonAhead) break;
                    results.Add(_entries[i]);
                    if (Reached(results, max)) return results;
                }
            }

            return results;
        }

        private static bool Reached(List<LexiconEntry> r, int max) => max > 0 && r.Count >= max;

        // First index whose key is >= queryKey (ordinal). Standard lower-bound binary search.
        private int LowerBound(string queryKey)
        {
            int lo = 0, hi = _entries.Length;
            while (lo < hi)
            {
                int mid = lo + ((hi - lo) >> 1);
                if (string.CompareOrdinal(_entries[mid].IpeKey, queryKey) < 0) lo = mid + 1;
                else hi = mid;
            }
            return lo;
        }

        private static int CommonPrefix(string a, string b)
        {
            if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b)) return 0;
            int n = Math.Min(a.Length, b.Length), i = 0;
            while (i < n && a[i] == b[i]) i++;
            return i;
        }

        private static LexiconMeta ReadMeta(SqliteConnection conn)
        {
            var m = new Dictionary<string, string>(StringComparer.Ordinal);
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT key, value FROM meta";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                    if (!r.IsDBNull(1)) m[r.GetString(0)] = r.GetString(1);
            }

            string? Get(string k) => m.TryGetValue(k, out var v) ? v : null;

            // A file whose schema is NEWER than this reader understands must be refused, not misread.
            if (int.TryParse(Get(LexiconSchema.MetaSchemaVersion), out int sv) && sv > LexiconSchema.SchemaVersion)
                throw new NotSupportedException(
                    $"Lexicon schema version {sv} is newer than this build supports ({LexiconSchema.SchemaVersion}).");

            var kind = string.Equals(Get(LexiconSchema.MetaKind), "proper-names", StringComparison.Ordinal)
                ? LexiconKind.ProperNames : LexiconKind.General;
            int.TryParse(Get(LexiconSchema.MetaConverterVersion), out int conv);

            return new LexiconMeta(
                SourceId: Get(LexiconSchema.MetaSourceId) ?? string.Empty,
                DisplayName: Get(LexiconSchema.MetaDisplayName) ?? string.Empty,
                DefinitionLanguage: Get(LexiconSchema.MetaDefinitionLanguage) ?? string.Empty,
                Kind: kind,
                Title: Get(LexiconSchema.MetaTitle),
                Author: Get(LexiconSchema.MetaAuthor),
                Reviser: Get(LexiconSchema.MetaReviser),
                Year: Get(LexiconSchema.MetaYear),
                Publisher: Get(LexiconSchema.MetaPublisher),
                License: Get(LexiconSchema.MetaLicense),
                Url: Get(LexiconSchema.MetaUrl),
                SourceVersion: Get(LexiconSchema.MetaSourceVersion),
                ConverterVersion: conv);
        }

        /// <summary>Convenience: derive the key from a raw query and look up. (The app's source adapter can call
        /// this directly.)</summary>
        public IReadOnlyList<LexiconEntry> Lookup(string query, int max = 0) =>
            LookupByKey(LexiconKey.DeriveQueryKey(query), max);
    }
}
