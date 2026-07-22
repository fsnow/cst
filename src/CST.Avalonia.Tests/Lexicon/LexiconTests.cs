using System;
using System.IO;
using System.Linq;
using CST.Conversion;
using CST.Lexicon;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CST.Avalonia.Tests.Lexicon
{
    /// <summary>
    /// The canonical lexicon format (#109): build → read round-trips, and the load-bearing correctness
    /// properties — keys are derived the SAME way a runtime query is (so lookups can't silently miss),
    /// homonyms surface together, HTML in a headword is stripped for the key but the published form is kept,
    /// and the exact/prefix/nearest search matches the flat-file dictionary's behaviour.
    /// </summary>
    public class LexiconTests : IDisposable
    {
        private readonly string _dir;

        public LexiconTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cst-lex-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }

        public void Dispose()
        {
            try { SqliteConnection.ClearAllPools(); } catch { }
            try { Directory.Delete(_dir, recursive: true); } catch { }
        }

        private LexiconReader Build(params RawEntry[] entries)
        {
            var meta = new LexiconMeta("dppn", "DPPN", "en", LexiconKind.ProperNames,
                Title: "Dictionary of Pāli Proper Names", Author: "G. P. Malalasekera",
                Reviser: "Ānandajoti Bhikkhu", Year: "2025", License: "public-domain",
                SourceVersion: "2025-06", ConverterVersion: 1);
            var path = Path.Combine(_dir, "dppn.db");
            LexiconBuilder.Build(path, meta, entries);
            return LexiconReader.Open(path);
        }

        // ---- key derivation ----

        [Fact]
        public void The_stored_key_equals_the_key_a_runtime_query_derives()
        {
            // This is the whole game: a headword's stored key and a user's typed query must normalize to the
            // same IPE string, or the word is unfindable. Derive both ways and assert equality.
            foreach (var head in new[] { "Sāvatthī", "Nāgita", "Jetavana", "Anāthapiṇḍika" })
            {
                var storedKey = LexiconKey.DeriveKey(head);
                var queryKey = LexiconKey.DeriveQueryKey(head.ToUpperInvariant());   // user types any case
                Assert.Equal(storedKey, queryKey);
                Assert.Equal(Any2Ipe.Convert(head.ToLowerInvariant()), storedKey);   // and it IS the IPE form
            }
        }

        [Fact]
        public void A_headword_is_found_regardless_of_query_case_or_script()
        {
            var lex = Build(new RawEntry("Sāvatthī", "<p>A city.</p>"));
            Assert.Single(lex.Lookup("sāvatthī"));
            Assert.Single(lex.Lookup("SĀVATTHĪ"));
            Assert.Single(lex.Lookup("Sāvatthī"));
            // Devanagari of the same word resolves to the same IPE key.
            var deva = ScriptConverter.Convert("sāvatthī", Script.Latin, Script.Devanagari);
            Assert.Single(lex.Lookup(deva));
        }

        // ---- homonyms ----

        [Fact]
        public void Homonyms_share_a_key_and_all_surface_on_an_exact_lookup()
        {
            var lex = Build(
                new RawEntry("Nāgita 1", "<p>An elder.</p>"),
                new RawEntry("Nāgita 2", "<p>A brahmin.</p>"));

            var hits = lex.Lookup("nāgita");
            Assert.Equal(2, hits.Count);
            Assert.Equal(new[] { "Nāgita 1", "Nāgita 2" }, hits.Select(h => h.Headword));
            Assert.Equal(new[] { 1, 2 }, hits.Select(h => h.Homonym));
            // Same key for both; the published form (with the number) is preserved separately.
            Assert.All(hits, h => Assert.Equal(LexiconKey.DeriveKey("Nāgita"), h.IpeKey));
        }

        [Fact]
        public void A_bare_integer_is_a_homonym_marker_but_an_interior_number_is_not()
        {
            Assert.Equal(("Nāgita", 1), LexiconKey.SplitHomonym("Nāgita 1"));
            Assert.Equal(("Sāvatthī", 0), LexiconKey.SplitHomonym("Sāvatthī"));
            Assert.Equal(("Migāra", 12), LexiconKey.SplitHomonym("Migāra 12"));   // base has the number stripped
            // A hyphenated range is a homonym marker too (DPPN "Piyasutta 5-6"): first number is the sort key.
            Assert.Equal(("Piyasutta", 5), LexiconKey.SplitHomonym("Piyasutta 5-6"));
            // A dotted DPD-style token is NOT split (kept whole).
            Assert.Equal(("dhamma 1.01", 0), LexiconKey.SplitHomonym("dhamma 1.01"));
            // A name that legitimately ends in a word, or has an interior number, is left whole.
            Assert.Equal(("Vessantara Jātaka", 0), LexiconKey.SplitHomonym("Vessantara Jātaka"));
        }

        // ---- HTML in the headword ----

        [Fact]
        public void Html_in_a_headword_is_stripped_for_key_and_display()
        {
            var lex = Build(new RawEntry("<b>Jeta</b>vana", "<p>A grove.</p>"));
            var hit = Assert.Single(lex.Lookup("jetavana"));
            Assert.Equal("Jetavana", hit.Headword);                 // tags gone from the published form
            Assert.Equal(LexiconKey.DeriveKey("Jetavana"), hit.IpeKey);
        }

        [Fact]
        public void The_body_html_is_stored_verbatim()
        {
            var lex = Build(new RawEntry("Sāvatthī", "<p>Capital of <i>Kosala</i>.</p>"));
            Assert.Equal("<p>Capital of <i>Kosala</i>.</p>", lex.Lookup("sāvatthī").Single().BodyHtml);
        }

        // ---- exact / prefix / nearest ----

        [Fact]
        public void An_exact_hit_is_followed_by_the_prefix_run()
        {
            var lex = Build(
                new RawEntry("Nāga", "n"),
                new RawEntry("Nāgadatta", "n"),
                new RawEntry("Nāgita", "n"),
                new RawEntry("Bimbisāra", "b"));
            var hits = lex.Lookup("nāga").Select(h => h.Headword).ToList();
            Assert.Equal(new[] { "Nāga", "Nāgadatta" }, hits);       // "Nāgita" shares only "nāg", not "nāga"
        }

        [Fact]
        public void A_miss_returns_the_nearest_common_prefix_run()
        {
            var lex = Build(
                new RawEntry("Nāgadatta", "n"),
                new RawEntry("Nāgita", "n"),
                new RawEntry("Bimbisāra", "b"));
            // No entry keyed exactly "nāg"; the two sharing that 3-char prefix come back.
            var hits = lex.Lookup("nāg").Select(h => h.Headword).ToList();
            Assert.Equal(new[] { "Nāgadatta", "Nāgita" }, hits);
        }

        [Fact]
        public void No_shared_leading_character_returns_nothing()
        {
            var lex = Build(new RawEntry("Sāvatthī", "s"));
            Assert.Empty(lex.Lookup("kosala"));
        }

        [Fact]
        public void Max_bounds_the_result()
        {
            var lex = Build(
                new RawEntry("Nāga 1", "n"), new RawEntry("Nāga 2", "n"), new RawEntry("Nāga 3", "n"));
            Assert.Equal(2, lex.Lookup("nāga", max: 2).Count);
        }

        // ---- meta ----

        [Fact]
        public void Meta_round_trips()
        {
            var lex = Build(new RawEntry("Sāvatthī", "s"));
            Assert.Equal("dppn", lex.Meta.SourceId);
            Assert.Equal("DPPN", lex.Meta.DisplayName);
            Assert.Equal("en", lex.Meta.DefinitionLanguage);
            Assert.Equal(LexiconKind.ProperNames, lex.Meta.Kind);
            Assert.Equal("G. P. Malalasekera", lex.Meta.Author);
            Assert.Equal("Ānandajoti Bhikkhu", lex.Meta.Reviser);
            Assert.Equal("public-domain", lex.Meta.License);
            Assert.Equal("2025-06", lex.Meta.SourceVersion);
        }

        [Fact]
        public void A_blank_headword_is_skipped()
        {
            var path = Path.Combine(_dir, "skip.db");
            var meta = new LexiconMeta("x", "X", "en", LexiconKind.General);
            int written = LexiconBuilder.Build(path, meta, new[]
            {
                new RawEntry("Sāvatthī", "s"),
                new RawEntry("<i></i>", "empty after strip"),   // nothing to key on
                new RawEntry("   ", "blank"),
            });
            Assert.Equal(1, written);
        }

        [Fact]
        public void Rebuilding_the_same_path_replaces_the_lexicon_without_loss()
        {
            // fable HIGH-1: with SQLite pooling on, a second Build to the same path resurrected the deleted
            // file's handle and threw, losing the prior good lexicon. Build twice, then read.
            var path = Path.Combine(_dir, "rebuild.db");
            var meta = new LexiconMeta("x", "X", "en", LexiconKind.General);
            LexiconBuilder.Build(path, meta, new[] { new RawEntry("Sāvatthī", "first") });
            LexiconBuilder.Build(path, meta, new[] { new RawEntry("Kosala", "second") });   // must not throw

            var lex = LexiconReader.Open(path);
            Assert.Equal(1, lex.Count);
            Assert.Empty(lex.Lookup("sāvatthī"));                 // old content gone
            Assert.Equal("second", lex.Lookup("kosala").Single().BodyHtml);
            Assert.False(File.Exists(path + ".tmp"));             // temp cleaned up by the rename
        }

        [Fact]
        public void An_entity_encoded_headword_is_decoded_so_its_key_is_findable()
        {
            // fable MED-1: "N&#257;ga" must key/display as "Nāga", not keep the literal entity (a dead key).
            var lex = Build(new RawEntry("N&#257;ga", "<p>A serpent-being.</p>"));
            var hit = Assert.Single(lex.Lookup("nāga"));
            Assert.Equal("Nāga", hit.Headword);
            Assert.Equal(LexiconKey.DeriveKey("Nāga"), hit.IpeKey);
            // A genuinely escaped literal is kept as the literal, not re-read as a tag.
            Assert.Equal("a < b", LexiconKey.StripHtml("a &lt; b"));
        }

        [Fact]
        public void Exact_homonyms_then_the_deeper_prefix_run_all_surface_together()
        {
            var lex = Build(
                new RawEntry("Nāga 1", "n"),
                new RawEntry("Nāga 2", "n"),
                new RawEntry("Nāgadatta", "n"));
            // Exact key "nāga" → both homonyms, then the deeper-prefix "Nāgadatta".
            Assert.Equal(new[] { "Nāga 1", "Nāga 2", "Nāgadatta" },
                lex.Lookup("nāga").Select(h => h.Headword));
        }

        [Fact]
        public void A_miss_tied_on_both_sides_collects_both_runs_in_ascending_key_order()
        {
            var lex = Build(
                new RawEntry("Nabhasa", "n"),      // shares "na"
                new RawEntry("Nandā", "n"));       // shares "na"
            // "naX" (no exact) with both neighbors sharing "na" (2) → both are returned...
            var hits = lex.Lookup("naxyz");
            Assert.Equal(2, hits.Count);
            Assert.Contains(hits, h => h.Headword == "Nabhasa");
            Assert.Contains(hits, h => h.Headword == "Nandā");
            // ...in ascending IPE-key order (Pāli collation: dental 'n' sorts before labial 'b', so this is
            // Nandā then Nabhasa — the point is the reader emits them sorted, whatever the collation says).
            var keys = hits.Select(h => h.IpeKey).ToList();
            Assert.Equal(keys.OrderBy(k => k, StringComparer.Ordinal), keys);
        }

        [Fact]
        public void A_file_without_a_schema_version_is_refused()
        {
            var path = Path.Combine(_dir, "noschema.db");
            LexiconBuilder.Build(path, new LexiconMeta("x", "X", "en", LexiconKind.General),
                new[] { new RawEntry("Sāvatthī", "s") });
            var csb = new SqliteConnectionStringBuilder { DataSource = path, Pooling = false };
            using (var c = new SqliteConnection(csb.ToString()))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "DELETE FROM meta WHERE key = 'schema_version'";
                cmd.ExecuteNonQuery();
            }
            Assert.Throws<NotSupportedException>(() => LexiconReader.Open(path));
        }

        [Fact]
        public void A_schema_newer_than_this_build_is_refused()
        {
            var path = Path.Combine(_dir, "future.db");
            LexiconBuilder.Build(path, new LexiconMeta("x", "X", "en", LexiconKind.General),
                new[] { new RawEntry("Sāvatthī", "s") });
            // Forge a newer schema_version in the file.
            var csb = new SqliteConnectionStringBuilder { DataSource = path };
            using (var c = new SqliteConnection(csb.ToString()))
            {
                c.Open();
                using var cmd = c.CreateCommand();
                cmd.CommandText = "UPDATE meta SET value = '999' WHERE key = 'schema_version'";
                cmd.ExecuteNonQuery();
            }
            SqliteConnection.ClearAllPools();
            Assert.Throws<NotSupportedException>(() => LexiconReader.Open(path));
        }
    }
}
