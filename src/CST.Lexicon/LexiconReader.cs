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
            // Pooling=false: a pooled read-only handle would block the delivery layer from replacing the lexicon
            // file (File.Move/Delete) on Windows. Open-once/close-once — pooling buys nothing. (fable HIGH-1)
            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = dbPath,
                Mode = SqliteOpenMode.ReadOnly,
                Pooling = false
            };
            using var conn = new SqliteConnection(csb.ToString());
            conn.Open();

            var meta = ReadMeta(conn);

            var list = new List<LexiconEntry>();
            using (var cmd = conn.CreateCommand())
            {
                cmd.CommandText = "SELECT headword, body_html FROM entry";
                using var r = cmd.ExecuteReader();
                while (r.Read())
                {
                    string headword = r.GetString(0);
                    // Derive the lookup key + homonym HERE, from the stored headword — the single derivation
                    // path (LexiconKey) also serves typed queries, so keys and queries can't drift.
                    var (headBase, homonym) = LexiconKey.SplitHomonym(headword);
                    string key = LexiconKey.DeriveKey(headBase);
                    list.Add(new LexiconEntry(key, headword, homonym, r.GetString(1)));
                }
            }

            // Sort by the derived key with the SAME ordinal (UTF-16 code-unit) comparison the binary search uses.
            var arr = list.ToArray();
            Array.Sort(arr, (a, b) =>
            {
                int c = string.CompareOrdinal(a.IpeKey, b.IpeKey);
                if (c != 0) return c;
                return a.Homonym.CompareTo(b.Homonym);
            });
            return new LexiconReader(arr, meta);
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

            // The version stamp must be PRESENT and parseable: an unstamped file isn't a lexicon this reader
            // should guess at. Absent or NEWER than we understand → refuse, don't misread. (fable LOW-1)
            if (!int.TryParse(Get(LexiconSchema.MetaSchemaVersion), out int sv))
                throw new NotSupportedException("Not a lexicon: missing or invalid schema_version.");
            if (sv > LexiconSchema.SchemaVersion)
                throw new NotSupportedException(
                    $"Lexicon schema version {sv} is newer than this build supports ({LexiconSchema.SchemaVersion}).");

            var kind = string.Equals(Get(LexiconSchema.MetaKind), "proper-names", StringComparison.Ordinal)
                ? LexiconKind.ProperNames : LexiconKind.General;
            // Default to 1 when absent, symmetric with the record default (a lexicon the builder wrote always
            // carries it). (fable LOW-5)
            int conv = int.TryParse(Get(LexiconSchema.MetaConverterVersion), out int c) ? c : 1;

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
