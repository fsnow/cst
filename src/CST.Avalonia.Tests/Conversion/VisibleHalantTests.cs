using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.Conversion;

/// <summary>
/// The visible halant: a virama with no consonant after it (#1048). Every converter must show it as a visible
/// mark and carry it back to Devanagari.
///
/// <para>Myanmar and Khmer used to write it with their stacking sign (U+1039, U+17D2), which is invisible when
/// nothing follows. Both still round-tripped, so the round-trip tests passed. That is why these tests assert
/// the exact code points produced, not only the round trip.</para>
///
/// <para>Every case runs through both the fast <c>Convert</c> and the frozen <c>ConvertReference</c>, in both
/// directions. The Tipitaka.org converters are the reference versions, so a fix has to be right there too.</para>
///
/// <para>Cyrillic is excluded. Its transliteration is a known legacy scheme that does not round-trip (see
/// <see cref="AutoDetectRoundTripTests"/>), and it drops the halant on the way back. Whether to change that is
/// open on #1048.</para>
/// </summary>
public class VisibleHalantTests
{
    private const char Virama = '\u094D';

    // Inputs with a visible halant, written as escapes (no Devanagari glyphs in source).
    public static readonly string[] HalantCases =
    {
        "\u0935\u0919\u094D",                         // vaṅ: final ṅ, end of text (Myanmar kinzi trap)
        "\u091E\u094D",                               // ñ alone (Myanmar ñ has two letter forms)
        "\u092F\u0926\u094D",                         // yad
        "\u092E\u093E\u0938\u0940\u0926\u094D",       // māsīd
        "\u092F\u0926\u094D \u0935\u093E",            // yad, then a space and a word
        "\u092F\u0926\u094D\u0964",                   // yad, then danda
        "\u092F\u0926\u094D\u2019\u2019\u0924\u093F", // yad, then closing quotes and ti
        "\u0935\u0919\u094D\u2018\u2018\u0964",       // vaṅ, then opening quotes and danda (corpus form)
        "\u0938\u0928\u094D\u0924\u094D",             // sant: conjunct, then a halant
        "\u0938\u0941\u091E\u094D \u0938\u0938\u094D\u0938", // suñ, then a word with a conjunct (corpus form)
    };

    // A halant directly before an independent vowel (corpus forms from the grammars). The Indic scripts keep it;
    // Latin is phonetic and has no mark for it, so ṅ + u reads back as ṅu (see
    // HalantBeforeIndependentVowel_IsPhoneticInLatin).
    public static readonly string[] BeforeVowelCases =
    {
        "\u092E\u093F\u092F\u0919\u094D\u0909\u0935\u0919\u094D", // miyaṅ uvaṅ
        "\u0917\u0915\u094D\u0905\u0928\u094D\u0924\u093E",         // gak antā
    };

    // Inputs whose virama is NOT visible: it forms a conjunct. Must be unchanged by the fix.
    public static readonly string[] ConjunctCases =
    {
        "\u0938\u0919\u094D\u0918",                   // saṅgha: Myanmar kinzi
        "\u092C\u0941\u0926\u094D\u0927",             // buddha: stacked
        "\u0915\u094D\u200D\u0915",                   // kka with ZWJ: still a conjunct
        "\u092F\u0926\u094D\u200C\u0927",             // ZWNJ between virama and consonant: still a conjunct
    };

    private sealed record Pair(
        Script Script, Func<string, string> To, Func<string, string> ToRef,
        Func<string, string> Back, Func<string, string> BackRef, string? VisibleSign);

    // VisibleSign: the script's visible halant. Null for Latin, which writes the bare consonant.
    private static readonly Pair[] Pairs =
    {
        new(Script.Bengali,   Deva2Beng.Convert, Deva2Beng.ConvertReference, Beng2Deva.Convert, Beng2Deva.ConvertReference, "\u09CD"),
        new(Script.Gujarati,  Deva2Gujr.Convert, Deva2Gujr.ConvertReference, Gujr2Deva.Convert, Gujr2Deva.ConvertReference, "\u0ACD"),
        new(Script.Gurmukhi,  Deva2Guru.Convert, Deva2Guru.ConvertReference, Guru2Deva.Convert, Guru2Deva.ConvertReference, "\u0A4D"),
        new(Script.Kannada,   Deva2Knda.Convert, Deva2Knda.ConvertReference, Knda2Deva.Convert, Knda2Deva.ConvertReference, "\u0CCD"),
        new(Script.Khmer,     Deva2Khmr.Convert, Deva2Khmr.ConvertReference, Khmr2Deva.Convert, Khmr2Deva.ConvertReference, "\u17D1"),
        new(Script.Latin,     Deva2Latn.Convert, Deva2Latn.ConvertReference, Latn2Deva.Convert, Latn2Deva.ConvertReference, null),
        new(Script.Malayalam, Deva2Mlym.Convert, Deva2Mlym.ConvertReference, Mlym2Deva.Convert, Mlym2Deva.ConvertReference, "\u0D4D"),
        new(Script.Myanmar,   Deva2Mymr.Convert, Deva2Mymr.ConvertReference, Mymr2Deva.Convert, Mymr2Deva.ConvertReference, "\u103A"),
        new(Script.Sinhala,   Deva2Sinh.Convert, Deva2Sinh.ConvertReference, Sinh2Deva.Convert, Sinh2Deva.ConvertReference, "\u0DCA"),
        new(Script.Telugu,    Deva2Telu.Convert, Deva2Telu.ConvertReference, Telu2Deva.Convert, Telu2Deva.ConvertReference, "\u0C4D"),
        new(Script.Thai,      Deva2Thai.Convert, Deva2Thai.ConvertReference, Thai2Deva.Convert, Thai2Deva.ConvertReference, "\u0E3A"),
        new(Script.Tibetan,   Deva2Tibt.Convert, Deva2Tibt.ConvertReference, Tibt2Deva.Convert, Tibt2Deva.ConvertReference, "\u0F84"),
    };

    public static IEnumerable<object[]> Scripts => Pairs.Select(p => new object[] { p.Script });

    private static Pair For(Script s) => Pairs.Single(p => p.Script == s);

    private static int CountVisible(string deva) => Halant.VisibleVirama.Matches(deva).Count;

    [Theory]
    [MemberData(nameof(Scripts))]
    public void VisibleHalant_ShowsTheVisibleSign_AndRoundTrips_OnBothPaths(Script script)
    {
        var p = For(script);
        var cases = script == Script.Latin ? HalantCases : HalantCases.Concat(BeforeVowelCases);
        foreach (var deva in cases)
        {
            var fast = p.To(deva);
            var reference = p.ToRef(deva);
            Assert.True(fast == reference, $"{script} {Cp(deva)}: fast [{Cp(fast)}] != reference [{Cp(reference)}]");

            if (p.VisibleSign != null)
            {
                int signs = Regex.Matches(fast, Regex.Escape(p.VisibleSign)).Count;
                Assert.True(signs >= CountVisible(deva),
                    $"{script} {Cp(deva)} -> [{Cp(fast)}]: expected the visible sign U+{(int)p.VisibleSign[0]:X4} "
                    + $"at least {CountVisible(deva)}x, found {signs}");
            }

            var back = p.Back(fast);
            var backRef = p.BackRef(fast);
            Assert.True(back == backRef, $"{script} {Cp(fast)}: back fast [{Cp(back)}] != reference [{Cp(backRef)}]");
            Assert.True(back == deva, $"{script} {Cp(deva)} -> [{Cp(fast)}] -> [{Cp(back)}]: did not round-trip");
        }
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public void Conjunct_IsNotTurnedIntoAVisibleHalant(Script script)
    {
        var p = For(script);
        foreach (var deva in ConjunctCases)
        {
            var fast = p.To(deva);
            Assert.True(fast == p.ToRef(deva), $"{script} {Cp(deva)}: fast != reference");
            if (p.VisibleSign != null && script is Script.Myanmar or Script.Khmer)
            {
                // In these two the visible sign is distinct from the stacking sign, so it must not appear.
                // (Myanmar's kinzi uses the asat before U+1039; that is checked separately below.)
                var stripped = script == Script.Myanmar ? fast.Replace("\u103A\u1039", "") : fast;
                Assert.False(stripped.Contains(p.VisibleSign),
                    $"{script} {Cp(deva)} -> [{Cp(fast)}]: conjunct written as a visible halant");
            }
        }
    }

    [Fact]
    public void Myanmar_ExactCodePoints()
    {
        // Matches the Myanmar print of e0105n (#1046): mukhamāsīd and yad end in the asat.
        Assert.Equal("\u101A\u1012\u103A", Deva2Mymr.Convert("\u092F\u0926\u094D"));
        Assert.Equal("\u1019\u102C\u101E\u102E\u1012\u103A", Deva2Mymr.Convert("\u092E\u093E\u0938\u0940\u0926\u094D"));
        // final ṅ is ṅ + asat, not a kinzi with nothing under it
        Assert.Equal("\u101D\u1004\u103A", Deva2Mymr.Convert("\u0935\u0919\u094D"));
        Assert.Equal("\u1009\u103A", Deva2Mymr.Convert("\u091E\u094D"));
        // a kinzi is unchanged: ṅ + asat + stacking virama + consonant
        Assert.Equal("\u101E\u1004\u103A\u1039\u1003", Deva2Mymr.Convert("\u0938\u0919\u094D\u0918"));
        // a stacked conjunct is unchanged
        Assert.Equal("\u1017\u102F\u1012\u1039\u1013", Deva2Mymr.Convert("\u092C\u0941\u0926\u094D\u0927"));
    }

    [Fact]
    public void Myanmar_Reverse_AsatIsAHalantExceptInAKinzi()
    {
        foreach (var conv in new Func<string, string>[] { Mymr2Deva.Convert, Mymr2Deva.ConvertReference })
        {
            Assert.Equal("\u092F\u0926\u094D", conv("\u101A\u1012\u103A"));
            Assert.Equal("\u0935\u0919\u094D", conv("\u101D\u1004\u103A"));
            Assert.Equal("\u0938\u0919\u094D\u0918", conv("\u101E\u1004\u103A\u1039\u1003"));
        }
    }

    [Fact]
    public void Khmer_ExactCodePoints()
    {
        Assert.Equal("\u1799\u1791\u17D1", Deva2Khmr.Convert("\u092F\u0926\u094D"));
        Assert.Equal("\u179F\u1793\u17D2\u178F\u17D1", Deva2Khmr.Convert("\u0938\u0928\u094D\u0924\u094D")); // conjunct keeps coeng
        foreach (var conv in new Func<string, string>[] { Khmr2Deva.Convert, Khmr2Deva.ConvertReference })
            Assert.Equal("\u092F\u0926\u094D", conv("\u1799\u1791\u17D1"));
    }

    [Fact]
    public void Latin_WritesTheBareConsonant()
    {
        Assert.Equal("yad", Deva2Latn.Convert("\u092F\u0926\u094D"));
        Assert.Equal("ya\u1E45", Deva2Latn.Convert("\u092F\u0919\u094D"));
    }

    [Fact]
    public void HalantBeforeIndependentVowel_IsPhoneticInLatin()
    {
        // gak antā is written gakantā: the same sounds, which is what Latin display needs. Reading it back
        // gives the spelling without the halant; the halant before the vowel is not recoverable from Latin.
        var deva = BeforeVowelCases[1];
        Assert.Equal("gakant\u0101", Deva2Latn.Convert(deva));
        Assert.Equal("\u0917\u0915\u0928\u094D\u0924\u093E", Latn2Deva.Convert("gakant\u0101"));
    }

    [Fact]
    public void Ipe_RoundTripsTheHalant_OnBothPaths()
    {
        foreach (var deva in HalantCases)
        {
            var ipe = Deva2Ipe.Convert(deva);
            Assert.Equal(ipe, Deva2Ipe.ConvertReference(deva));
            Assert.Equal(deva, Ipe2Deva.Convert(ipe));
            Assert.Equal(deva, Ipe2Deva.ConvertReference(ipe));
        }
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public void AutoDetect_ReadsTheHalantBack(Script script)
    {
        // The search box and the dictionary read text back through Any2Ipe, which picks a converter per
        // character by Unicode block. The visible sign must be in the script's block for this to work.
        var p = For(script);
        foreach (var deva in HalantCases)
        {
            var shown = p.To(deva);
            Assert.True(Deva2Ipe.Convert(deva) == Any2Ipe.Convert(shown),
                $"{script} {Cp(deva)} -> [{Cp(shown)}]: Any2Ipe read it back differently");
        }
    }

    /// <summary>
    /// Every visible halant in the real corpus (101 of them in 2026-10, all in the grammar texts), as the word
    /// it sits in, through every script and back. No-ops when the corpus is absent.
    /// </summary>
    [Theory]
    [MemberData(nameof(Scripts))]
    public void CorpusHalants_RoundTripInEveryScript(Script script)
    {
        var words = CorpusHalantWords.Value;
        if (words.Count == 0)
            return; // XML corpus absent (CI / fresh checkout)

        var p = For(script);
        var misses = new List<string>();
        foreach (var deva in words)
        {
            if (script == Script.Latin && HalantBeforeVowel.IsMatch(deva))
                continue; // phonetic in Latin, see HalantBeforeIndependentVowel_IsPhoneticInLatin
            var fast = p.To(deva);
            if (fast != p.ToRef(deva)) { misses.Add($"fast!=ref {Cp(deva)}"); continue; }
            if (p.VisibleSign != null && !fast.Contains(p.VisibleSign)) { misses.Add($"no sign {Cp(deva)} -> {Cp(fast)}"); continue; }
            var back = p.Back(fast);
            if (back != deva) misses.Add($"{Cp(deva)} -> {Cp(fast)} -> {Cp(back)}");
        }
        Assert.True(misses.Count == 0, $"{script}: {misses.Count} of {words.Count}:\n" + string.Join("\n", misses.Take(20)));
    }

    private static readonly Regex HalantBeforeVowel = new("\u094D[\u0904-\u0914]");

    private static readonly Lazy<IReadOnlyList<string>> CorpusHalantWords = new(() =>
    {
        var dir = Environment.GetEnvironmentVariable("CST_XML_DIR")
                  ?? Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", "Library/Application Support/CSTReader/xml");
        if (!Directory.Exists(dir))
            return Array.Empty<string>();
        // A run of Devanagari letters and signs (joiners stripped, as the converters drop them) that contains
        // a visible halant.
        var word = new Regex("[\u0900-\u097F]+");
        var set = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var f in Directory.GetFiles(dir, "*.xml"))
        {
            var text = Regex.Replace(File.ReadAllText(f), "<[^>]+>", " ").Replace("\u200C", "").Replace("\u200D", "");
            foreach (Match m in word.Matches(text))
                if (Halant.VisibleVirama.IsMatch(m.Value))
                    set.Add(m.Value);
        }
        return set.ToList();
    });

    private static string Cp(string s) => string.Join(" ", s.Select(c => ((int)c).ToString("X4")));
}
