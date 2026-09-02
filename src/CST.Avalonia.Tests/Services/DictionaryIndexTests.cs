using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// Matching-semantics tests for <see cref="DictionaryIndex"/> — a faithful port of CST4's
/// <c>FormDictionary.Search</c>. The algorithm is purely string/codepoint based, so these use plain
/// ASCII "headwords" as stand-ins for IPE; the real IPE round-trip is covered in
/// <see cref="DictionaryServiceTests"/>.
/// </summary>
public class DictionaryIndexTests
{
    private static DictionaryIndex Index(params string[] words)
        => new(words.Select(w => new DictionaryWord(w, "def:" + w)));

    private static string[] Words(IReadOnlyList<DictionaryWord> r) => r.Select(w => w.Word).ToArray();

    [Fact]
    public void EmptyIndex_ReturnsEmpty()
        => Assert.Empty(Index().Lookup("anything"));

    [Fact]
    public void EmptyQuery_ReturnsEmpty()
        => Assert.Empty(Index("apple", "banana").Lookup(""));

    [Fact]
    public void ExactMatch_NoPrefixFollowers_ReturnsOnlyExact()
    {
        // "apply" does not start with "apple", so only the exact entry comes back.
        var r = Index("apple", "apply", "banana").Lookup("apple");
        Assert.Equal(new[] { "apple" }, Words(r));
    }

    [Fact]
    public void ExactMatch_WithPrefixRun_ReturnsExactThenStartsWithRun()
    {
        var r = Index("car", "card", "care", "cat").Lookup("car");
        Assert.Equal(new[] { "car", "card", "care" }, Words(r));
    }

    [Fact]
    public void Miss_PrefixOfLongerWords_ReturnsAheadRun()
    {
        // "appl" matches nothing but is a prefix of apple/apply (4 common chars each).
        var r = Index("apple", "apply", "banana").Lookup("appl");
        Assert.Equal(new[] { "apple", "apply" }, Words(r));
    }

    [Fact]
    public void Miss_AheadRunStopsAtLesserCommonPrefix()
    {
        var r = Index("apply", "banana", "band", "bandana", "cat").Lookup("ban");
        Assert.Equal(new[] { "banana", "band", "bandana" }, Words(r));
    }

    [Fact]
    public void Miss_BehindOnly_ReturnsBehindRunInAscendingOrder()
    {
        // "bandz" sits after "bandana"; the tied-max side is behind (4 common chars with band/bandana).
        var r = Index("apply", "banana", "band", "bandana", "cat").Lookup("bandz");
        Assert.Equal(new[] { "band", "bandana" }, Words(r));
    }

    [Fact]
    public void Miss_TiedBothSides_ReturnsBehindThenAhead()
    {
        // "abn" shares 2 leading chars with both neighbors "abm" (behind) and "abp" (ahead).
        var r = Index("abm", "abp").Lookup("abn");
        Assert.Equal(new[] { "abm", "abp" }, Words(r));
    }

    [Fact]
    public void Miss_NoCommonPrefix_ReturnsEmpty()
    {
        Assert.Empty(Index("abm", "abp").Lookup("zzz"));
    }

    // ---- ḷ/l folding on a miss (#933) ----
    //
    // These use the real IPE codepoints rather than ASCII stand-ins, because the fold is defined on them:
    // Latn2Ipe maps l -> U+00E5 and ḷ -> U+00E9. Written as escapes, per CLAUDE.md.
    private const string IpeL = "\u00E5";    // l
    private const string IpeLDot = "\u00E9"; // ḷ

    /// <summary>The reported case. DPPN carries Nālandā with a plain l; the corpus spells the same name
    /// with ḷ four times in five, so most clicks missed the entry entirely (#933).</summary>
    [Fact]
    public void A_query_spelled_with_retroflex_l_reaches_a_plain_l_headword()
    {
        // The decoy is load-bearing. It shares THREE leading characters with the query where the real
        // target shares two, so the old near-neighbour guess prefers it — without folding this returns the
        // decoy, which is the shape of the reported miss. A two-entry index would pass either way.
        var r = Index("na" + IpeL + "anda", "na" + IpeLDot + "bbb", "zzz")
            .Lookup("na" + IpeLDot + "anda");

        Assert.Equal(new[] { "na" + IpeL + "anda" }, Words(r));
    }

    /// <summary>And the reverse — the corpus contains both spellings, so the fold has to work in both
    /// directions rather than privileging one tradition.</summary>
    [Fact]
    public void A_query_spelled_with_plain_l_reaches_a_retroflex_l_headword()
    {
        var r = Index("na" + IpeLDot + "anda", "na" + IpeL + "bbb", "zzz")
            .Lookup("na" + IpeL + "anda");

        Assert.Equal(new[] { "na" + IpeLDot + "anda" }, Words(r));
    }

    /// <summary>The homograph run still follows, so a folded hit behaves like an exact one rather than
    /// returning a lone entry — Nālandā 1, Nālandā 2, Nālandāsutta 1 … all reachable.</summary>
    [Fact]
    public void A_folded_hit_still_returns_the_prefix_run()
    {
        var r = Index("na" + IpeL + "anda", "na" + IpeL + "andasutta", "na" + IpeLDot + "bbb", "zzz")
            .Lookup("na" + IpeLDot + "anda");

        Assert.Equal(new[] { "na" + IpeL + "anda", "na" + IpeL + "andasutta" }, Words(r));
    }

    /// <summary>An exact spelling always wins. Both spellings exist as separate entries here, and the query
    /// must reach its OWN — folding must never divert a lookup that already resolves, and must never merge
    /// two entries that Pāli keeps apart.</summary>
    [Fact]
    public void An_exact_spelling_is_never_diverted_by_folding()
    {
        var index = Index("na" + IpeL + "anda", "na" + IpeLDot + "anda");

        Assert.Equal(new[] { "na" + IpeL + "anda" }, Words(index.Lookup("na" + IpeL + "anda")));
        Assert.Equal(new[] { "na" + IpeLDot + "anda" }, Words(index.Lookup("na" + IpeLDot + "anda")));
    }

    /// <summary>The reported case, in the shape the real dictionary has it. (#933)
    ///
    /// <para>This is the test the first attempt at this fix did not have, and it is why that attempt did not
    /// work. The query is the INFLECTED <c>nāḷandaṃ</c>, which has no entry at all — so the fix has to reach
    /// it through the near-neighbour guess, not through an exact match. DPPN really does hold
    /// <c>Nāḷika</c>, <c>Nāḷikera</c>, <c>Nāḷikīra</c> and <c>Nāḷisobbha</c>, and those share <c>nāḷ</c>
    /// with the query, so a fix that folds only the exact arm leaves the reader on them — which is what a
    /// lookup already did.</para></summary>
    [Fact]
    public void An_inflected_form_reaches_the_headword_across_the_spelling_difference()
    {
        // n ā ḷ a n d a ṃ  — no entry; the headword is nālandā, and nāḷika… is the wrong near neighbour.
        var index = Index(
            "n\u0101" + IpeLDot + "ika",        // nāḷika    — shares "nāḷ" (3) unfolded
            "n\u0101" + IpeLDot + "ikera",      // nāḷikera
            "n\u0101" + IpeL + "and\u0101",    // nālandā   — shares "nāland" (6) once folded
            "n\u0101" + IpeL + "and\u0101sutta");

        var r = index.Lookup("n\u0101" + IpeLDot + "anda\u1E43");

        Assert.Equal(
            new[] { "n\u0101" + IpeL + "and\u0101", "n\u0101" + IpeL + "and\u0101sutta" },
            Words(r));
    }

    /// <summary>The other side of the same index: a word genuinely spelled with ḷ must still find itself,
    /// not be dragged to a plain-l neighbour by folding.</summary>
    [Fact]
    public void A_word_that_really_is_spelled_with_retroflex_l_still_finds_itself()
    {
        var index = Index(
            "n\u0101" + IpeLDot + "ika",
            "n\u0101" + IpeL + "and\u0101");

        Assert.Equal(new[] { "n\u0101" + IpeLDot + "ika" },
            Words(index.Lookup("n\u0101" + IpeLDot + "ika")));
    }

    /// <summary>Folding is tried before the near-neighbour guess but must not replace it: a query with no
    /// folded counterpart still gets the old best-guess run.</summary>
    [Fact]
    public void A_word_with_no_folded_counterpart_still_gets_the_near_neighbour_guess()
    {
        var r = Index("apple", "apply", "banana").Lookup("appl");
        Assert.Equal(new[] { "apple", "apply" }, Words(r));
    }

    [Fact]
    public void CountReflectsEntries()
    {
        Assert.Equal(3, Index("a", "b", "c").Count);
    }
}
