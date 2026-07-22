using System;
using System.Collections.Generic;
using System.IO;
using Microsoft.Data.Sqlite;

namespace CST.Lexicon
{
    /// <summary>
    /// Writes a canonical lexicon SQLite from a source's meta + raw entries. Build-time only (our local
    /// converters, and later the in-app import). Derives each entry's IPE key, splits its homonym number, and
    /// strips HTML from the headword — through <see cref="LexiconKey"/>, the same logic the reader's query path
    /// uses — so a lexicon and a query can't disagree on keys.
    ///
    /// <para><b>Body HTML is stored verbatim.</b> The builder does NOT sanitize: sanitization is the converter's
    /// job (done once at build time, over a source's known markup) so the app never ships an HTML sanitizer for
    /// its own trusted downloaded assets. The in-app import path (untrusted HTML) sanitizes before calling this.</para>
    /// </summary>
    public static class LexiconBuilder
    {
        /// <summary>
        /// Build a lexicon at <paramref name="dbPath"/> (overwriting any existing file) from
        /// <paramref name="meta"/> and <paramref name="entries"/>. An entry whose headword is blank after HTML
        /// stripping is skipped (counted in the return).
        /// </summary>
        /// <returns>The number of entries written.</returns>
        public static int Build(string dbPath, LexiconMeta meta, IEnumerable<RawEntry> entries)
        {
            if (dbPath is null) throw new ArgumentNullException(nameof(dbPath));
            if (meta is null) throw new ArgumentNullException(nameof(meta));
            if (entries is null) throw new ArgumentNullException(nameof(entries));

            // Build to a temp file and atomically rename into place, so a crash mid-build (journal_mode=OFF)
            // can never leave a corrupt file at the real path, and an existing good lexicon is only replaced by
            // a complete new one. (fable HIGH-1)
            string tmp = dbPath + ".tmp";
            if (File.Exists(tmp)) File.Delete(tmp);

            // Pooling=false: these are open-once/close-once connections. With pooling on, a rebuild-at-same-path
            // gets back a pooled handle pointing at the unlinked old inode ("table already exists" / "unable to
            // open"), and a pooled handle also blocks File.Move/Delete on Windows. Same footgun SqliteLemmaProvider
            // guards with ClearPool. (fable HIGH-1)
            var csb = new SqliteConnectionStringBuilder
            {
                DataSource = tmp,
                Mode = SqliteOpenMode.ReadWriteCreate,
                Pooling = false
            };

            int written = 0;
            using (var conn = new SqliteConnection(csb.ToString()))
            {
                conn.Open();

                using (var pragma = conn.CreateCommand())
                {
                    pragma.CommandText = "PRAGMA journal_mode=OFF; PRAGMA synchronous=OFF;";
                    pragma.ExecuteNonQuery();
                }
                using (var create = conn.CreateCommand())
                {
                    create.CommandText = LexiconSchema.CreateSql;
                    create.ExecuteNonQuery();
                }

                using var tx = conn.BeginTransaction();

                WriteMeta(conn, meta);

                using (var insert = conn.CreateCommand())
                {
                    insert.CommandText =
                        "INSERT INTO entry(ipe_key, headword, homonym, body_html) VALUES ($k, $h, $n, $b)";
                    var pk = insert.CreateParameter(); pk.ParameterName = "$k"; insert.Parameters.Add(pk);
                    var ph = insert.CreateParameter(); ph.ParameterName = "$h"; insert.Parameters.Add(ph);
                    var pn = insert.CreateParameter(); pn.ParameterName = "$n"; insert.Parameters.Add(pn);
                    var pb = insert.CreateParameter(); pb.ParameterName = "$b"; insert.Parameters.Add(pb);

                    foreach (var raw in entries)
                    {
                        string stripped = LexiconKey.StripHtml(raw.Headword ?? string.Empty);
                        if (string.IsNullOrWhiteSpace(stripped)) continue;   // nothing to key on

                        var (headBase, homonym) = LexiconKey.SplitHomonym(stripped);
                        string key = LexiconKey.DeriveKey(headBase);
                        if (string.IsNullOrEmpty(key)) continue;

                        pk.Value = key;
                        ph.Value = stripped;                 // verbatim published form (homonym number kept)
                        pn.Value = homonym;
                        pb.Value = raw.BodyHtml ?? string.Empty;
                        insert.ExecuteNonQuery();
                        written++;
                    }
                }

                tx.Commit();
            }   // connection disposed (and, with Pooling=false, its file handle released) before the rename

            File.Move(tmp, dbPath, overwrite: true);
            return written;
        }

        private static void WriteMeta(SqliteConnection conn, LexiconMeta m)
        {
            using var cmd = conn.CreateCommand();
            cmd.CommandText = "INSERT OR REPLACE INTO meta(key, value) VALUES ($k, $v)";
            var pk = cmd.CreateParameter(); pk.ParameterName = "$k"; cmd.Parameters.Add(pk);
            var pv = cmd.CreateParameter(); pv.ParameterName = "$v"; cmd.Parameters.Add(pv);

            void Put(string key, string? value)
            {
                if (value is null) return;
                pk.Value = key; pv.Value = value; cmd.ExecuteNonQuery();
            }

            Put(LexiconSchema.MetaSchemaVersion, LexiconSchema.SchemaVersion.ToString());
            Put(LexiconSchema.MetaSourceId, m.SourceId);
            Put(LexiconSchema.MetaDisplayName, m.DisplayName);
            Put(LexiconSchema.MetaDefinitionLanguage, m.DefinitionLanguage);
            Put(LexiconSchema.MetaKind, m.Kind == LexiconKind.ProperNames ? "proper-names" : "general");
            Put(LexiconSchema.MetaTitle, m.Title);
            Put(LexiconSchema.MetaAuthor, m.Author);
            Put(LexiconSchema.MetaReviser, m.Reviser);
            Put(LexiconSchema.MetaYear, m.Year);
            Put(LexiconSchema.MetaPublisher, m.Publisher);
            Put(LexiconSchema.MetaLicense, m.License);
            Put(LexiconSchema.MetaUrl, m.Url);
            Put(LexiconSchema.MetaSourceVersion, m.SourceVersion);
            Put(LexiconSchema.MetaConverterVersion, m.ConverterVersion.ToString());
        }
    }
}
