using System;
using System.IO;
using System.Linq;
using CST.Lexicon;
using CST.LexiconTools;
using Xunit;

namespace CST.Avalonia.Tests.Lexicon
{
    /// <summary>
    /// The DPPN converter (#467). The `name` field is a malformed formatted heading (lemma in the first
    /// &lt;b&gt;, an optional bare homonym number after it, citations in &lt;abbr&gt;), so these fixtures use the
    /// REAL shapes seen in DPPN.json, not tidied ones. The body filter must reduce definitions to the closed
    /// allowlist with no attributes surviving.
    /// </summary>
    public class DppnConverterTests : IDisposable
    {
        private readonly string _dir;
        public DppnConverterTests()
        {
            _dir = Path.Combine(Path.GetTempPath(), "cst-dppn-" + Guid.NewGuid().ToString("N"));
            Directory.CreateDirectory(_dir);
        }
        public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

        // ---- headword extraction ----

        [Fact]
        public void The_first_bold_run_is_the_headword()
        {
            Assert.Equal("A-An",
                DppnConverter.ParseHeadword("<p class=\"Heading3\"><b>A-An</b>. </span>"));
        }

        [Fact]
        public void A_citation_after_the_lemma_is_excluded_from_the_headword()
        {
            // The (Ja 90) abbr citation is heading chrome, not part of the name.
            Assert.Equal("Akataññujātaka",
                DppnConverter.ParseHeadword(
                    "<p><span class=\"Head\"><b>Akataññujātaka</b> (<abbr title=\"Added\">Ja 90</abbr>). </span>"));
        }

        [Fact]
        public void A_bare_number_after_the_lemma_is_a_homonym_normalized_and_folded_into_the_headword()
        {
            // "01." -> homonym 1, folded to "… 1" so the lexicon builder splits it uniformly.
            Assert.Equal("Akatuññatāsutta 1",
                DppnConverter.ParseHeadword("<b>Akatuññatāsutta</b> 01. </span>"));
            Assert.Equal("Akatuññatāsutta 2",
                DppnConverter.ParseHeadword("<b>Akatuññatāsutta</b> 02. </span>"));
        }

        [Fact]
        public void A_multi_word_name_is_kept_whole()
        {
            Assert.Equal("Akaniṭṭhā Devā",
                DppnConverter.ParseHeadword("<span class=\"Head\"><b>Akaniṭṭhā Devā</b>. </span>"));
        }

        [Fact]
        public void A_name_with_no_bold_lemma_yields_nothing()
        {
            Assert.Equal("", DppnConverter.ParseHeadword("<p>just prose, no lemma</p>"));
            Assert.Equal("", DppnConverter.ParseHeadword(""));
        }

        [Fact]
        public void Alternative_title_homonyms_take_the_first_bold_and_the_trailing_number()
        {
            // The only multi-bold+number headings in DPPN are "X or Y NN" alternatives; the first bold is the
            // indexed lemma and the trailing number after the LATER </b> is its real homonym. (fable L1)
            Assert.Equal("Uttiyasutta 2",
                DppnConverter.ParseHeadword("<b>Uttiyasutta</b> or <b>Uttikasutta</b> 02. </span>"));
        }

        [Fact]
        public void A_homonym_range_keeps_both_numbers_in_the_headword()
        {
            // DPPN's sole ranged heading: one entry covering homonyms 5–6. Display both; the key derives from the
            // lemma alone (homonym split takes 5 as the sort number). (fable L2)
            Assert.Equal("Piyasutta 5-6",
                DppnConverter.ParseHeadword("<b>Piyasutta</b> 05-06. </span>"));
        }

        // ---- body filtering ----

        [Fact]
        public void The_body_keeps_allowed_tags_and_drops_the_rest_with_no_attributes()
        {
            string cleaned = DppnConverter.CleanBody(
                "<p>A <b>Deva</b> in the <span class=\"ref\">highest</span> heaven. " +
                "<abbr title=\"Anguttara\">AN.ii.226</abbr>.</p>");
            // span dropped (text kept); class/title attributes gone; p/b/abbr kept as bare tags.
            Assert.Contains("<b>Deva</b>", cleaned);
            Assert.Contains("highest", cleaned);
            Assert.DoesNotContain("<span", cleaned);
            Assert.DoesNotContain("class=", cleaned);
            Assert.DoesNotContain("title=", cleaned);
            Assert.Contains("<abbr>AN.ii.226</abbr>", cleaned);
        }

        [Fact]
        public void No_disallowed_tag_survives_the_body_filter()
        {
            string cleaned = DppnConverter.CleanBody(
                "<script>x</script><style>y</style><a href=\"http://evil\">z</a><img src=\"q\"><b>ok</b>");
            foreach (var bad in new[] { "<script", "<style", "<a ", "<a>", "href=", "<img" })
                Assert.DoesNotContain(bad, cleaned);
            Assert.Contains("<b>ok</b>", cleaned);
            // The disallowed tags' text content is preserved (dropped tag, kept text).
            Assert.Contains("z", cleaned);
            Assert.True(DppnConverter.IsClosedAllowlist(cleaned));
        }

        [Fact]
        public void A_bare_angle_bracket_in_prose_is_not_read_as_a_tag()
        {
            // fable M1: "< b" (space after '<') is text, not a <b> tag, and the surrounding prose must survive.
            string cleaned = DppnConverter.CleanBody("a < b and c > d");
            Assert.Equal("a &lt; b and c &gt; d", cleaned);
            Assert.True(DppnConverter.IsClosedAllowlist(cleaned));
        }

        [Fact]
        public void Split_tags_and_comments_cannot_reassemble_into_live_markup()
        {
            // fable M2: the safety property is now intrinsic — stray angle brackets in text are escaped, so no
            // dropped-tag remnant, split tag, comment, or dangling partial tag can form live markup.
            foreach (var input in new[]
            {
                "<<script>script>alert(1)<</script>/script>",   // split-tag reassembly
                "x <!-- <script>y</script> --> z",              // HTML comment
                "trailing partial <b",                          // dangling open at end
                "an entity &lt;script&gt; stays text",          // pre-escaped payload
            })
            {
                string cleaned = DppnConverter.CleanBody(input);
                Assert.True(DppnConverter.IsClosedAllowlist(cleaned), $"unsafe: {cleaned}");
                Assert.DoesNotContain("<script", cleaned);
                Assert.DoesNotContain("<!--", cleaned);
            }
        }

        // ---- end to end ----

        [Fact]
        public void ToEntries_skips_header_stubs_and_maps_records()
        {
            // Record 1 is a section header (A-An) whose entry is effectively empty -> skipped.
            string json = "[" +
                "{\"name\":\"<p class=\\\"Heading3\\\"><b>A-An</b>. </span>\",\"entry\":\" </p>\"}," +
                "{\"name\":\"<b>Sāvatthī</b>. </span>\",\"entry\":\"<p>A city of <b>Kosala</b>.</p>\"}," +
                "{\"name\":\"<b>Jetavana</b> 01. </span>\",\"entry\":\"<p>A grove.</p>\"}" +
            "]";
            var entries = DppnConverter.ToEntries(json).ToList();
            Assert.Equal(2, entries.Count);                          // header stub skipped
            Assert.Equal("Sāvatthī", entries[0].Headword);
            Assert.Equal("Jetavana 1", entries[1].Headword);        // homonym folded
        }

        [Fact]
        public void Built_dppn_lexicon_is_looked_up_by_name_with_homonyms()
        {
            string json = "[" +
                "{\"name\":\"<b>Jetavana</b> 01. </span>\",\"entry\":\"<p>A grove near Sāvatthī.</p>\"}," +
                "{\"name\":\"<b>Jetavana</b> 02. </span>\",\"entry\":\"<p>Another Jetavana.</p>\"}," +
                "{\"name\":\"<span class=\\\"Head\\\"><b>Sāvatthī</b>. </span>\",\"entry\":\"<p>Capital of Kosala.</p>\"}" +
            "]";
            string dppnJsonFile = Path.Combine(_dir, "DPPN.json");
            File.WriteAllText(dppnJsonFile, json);
            string db = Path.Combine(_dir, "dppn.db");
            int n = DppnConverter.BuildLexicon(dppnJsonFile, db, "test");
            Assert.Equal(3, n);

            var lex = LexiconReader.Open(db);
            Assert.Equal(LexiconKind.ProperNames, lex.Meta.Kind);
            Assert.Equal("G. P. Malalasekera", lex.Meta.Author);

            var jeta = lex.Lookup("jetavana");
            Assert.Equal(new[] { "Jetavana 1", "Jetavana 2" }, jeta.Select(h => h.Headword));
            Assert.Single(lex.Lookup("sāvatthī"));
        }
    }
}
