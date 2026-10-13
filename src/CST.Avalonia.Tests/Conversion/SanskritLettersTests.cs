using System;
using System.Collections.Generic;
using System.Linq;
using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.Conversion;

/// <summary>
/// The Sanskrit letters added to IPE and the converters by #1049: visarga, vocalic r, ai, au, and the palatal
/// and retroflex sibilants.
///
/// <para>[fsnow, 2026-10-13] "All the converters need to support these additional characters, IPE needs to be
/// modified to support them (in correct alphabetical order)". The order, agreed the same day: visarga after
/// niggahita; vocalic r after uu; ai after e; au after o; the two sibilants between va and sa.</para>
///
/// <para>Two decisions shape what each script shows, both fsnow's (2026-10-13). A script with no letter for one
/// of these keeps the Devanagari letter ("Keep Devanagari"), as Gurmukhi does for vocalic r and the retroflex
/// sibilant. And Latin keeps reading ai / au as the Pali vowel hiatus a + i / a + u ("Keep Pali as is"), so a
/// diphthong shown in Latin does not read back as one.</para>
/// </summary>
public class SanskritLettersTests
{
    // Written as escapes: no Devanagari glyphs in source.
    public static readonly string[] Cases =
    {
        "\u0915\u0903",                                // kah: visarga after the inherent a
        "\u0930\u093E\u091C\u0928\u094D\u092F\u0903",  // rajanyah
        "\u090B",                                      // independent vocalic r
        "\u0915\u0943\u0924\u0903",                    // krtah: the vocalic r sign
        "\u0910",                                      // independent ai
        "\u0915\u0948",                                // kai
        "\u0914",                                      // independent au
        "\u0915\u094C",                                // kau
        "\u0936\u0942\u0926\u094D\u0930\u094B",        // shudro
        "\u0935\u0948\u0936\u094D\u092F\u0903",        // vaishyah
        "\u0915\u094D\u0937",                          // ksa: conjunct ending in the retroflex sibilant
        "\u0936\u094D\u0930",                          // shra: conjunct starting with the palatal sibilant
        "\u0937\u094D\u091F",                          // sta
        "\u0935\u0943\u0915\u094D\u0937",              // vrksa: vocalic r sign mid-word (Thai: vrksa)
        "\u0915\u094D\u0937\u094C",                    // ksau: au after a conjunct
        "\u0915\u094C\u0915\u093E",                    // kauka: au then aa on the next syllable
        "\u092D\u0942\u0930\u094D\u092D\u0941\u0935\u0903 \u0938\u094D\u0935\u0903", // bhur bhuvah svah (s0202t)
    };

    // The Devanagari letters each script keeps, because it has no letter of its own for them.
    private static readonly Dictionary<Script, string> KeptDevanagari = new()
    {
        [Script.Gurmukhi] = "\u090B\u0943\u0937",
        [Script.Thai] = "\u0903",
        [Script.Myanmar] = "\u0914\u094C",
    };

    private sealed record Pair(Script Script, Func<string, string> To, Func<string, string> ToRef,
                               Func<string, string> Back, Func<string, string> BackRef);

    private static readonly Pair[] Pairs =
    {
        new(Script.Bengali,   Deva2Beng.Convert, Deva2Beng.ConvertReference, Beng2Deva.Convert, Beng2Deva.ConvertReference),
        new(Script.Gujarati,  Deva2Gujr.Convert, Deva2Gujr.ConvertReference, Gujr2Deva.Convert, Gujr2Deva.ConvertReference),
        new(Script.Gurmukhi,  Deva2Guru.Convert, Deva2Guru.ConvertReference, Guru2Deva.Convert, Guru2Deva.ConvertReference),
        new(Script.Kannada,   Deva2Knda.Convert, Deva2Knda.ConvertReference, Knda2Deva.Convert, Knda2Deva.ConvertReference),
        new(Script.Khmer,     Deva2Khmr.Convert, Deva2Khmr.ConvertReference, Khmr2Deva.Convert, Khmr2Deva.ConvertReference),
        new(Script.Malayalam, Deva2Mlym.Convert, Deva2Mlym.ConvertReference, Mlym2Deva.Convert, Mlym2Deva.ConvertReference),
        new(Script.Myanmar,   Deva2Mymr.Convert, Deva2Mymr.ConvertReference, Mymr2Deva.Convert, Mymr2Deva.ConvertReference),
        new(Script.Sinhala,   Deva2Sinh.Convert, Deva2Sinh.ConvertReference, Sinh2Deva.Convert, Sinh2Deva.ConvertReference),
        new(Script.Telugu,    Deva2Telu.Convert, Deva2Telu.ConvertReference, Telu2Deva.Convert, Telu2Deva.ConvertReference),
        new(Script.Thai,      Deva2Thai.Convert, Deva2Thai.ConvertReference, Thai2Deva.Convert, Thai2Deva.ConvertReference),
        new(Script.Tibetan,   Deva2Tibt.Convert, Deva2Tibt.ConvertReference, Tibt2Deva.Convert, Tibt2Deva.ConvertReference),
    };

    public static IEnumerable<object[]> Scripts => Pairs.Select(p => new object[] { p.Script });

    private static bool IsDevanagari(char c) => c >= '\u0900' && c <= '\u097F' && c != '\u0964' && c != '\u0965';

    [Theory]
    [MemberData(nameof(Scripts))]
    public void NewLetters_ConvertAndRoundTrip_OnBothPaths(Script script)
    {
        var p = Pairs.Single(x => x.Script == script);
        var kept = KeptDevanagari.TryGetValue(script, out var k) ? k : "";
        foreach (var deva in Cases)
        {
            var shown = p.To(deva);
            Assert.True(shown == p.ToRef(deva), $"{script} {Cp(deva)}: fast [{Cp(shown)}] != reference [{Cp(p.ToRef(deva))}]");

            var stray = shown.Where(c => IsDevanagari(c) && !kept.Contains(c)).ToArray();
            Assert.True(stray.Length == 0, $"{script} {Cp(deva)} -> [{Cp(shown)}]: unmapped Devanagari {Cp(new string(stray))}");

            var back = p.Back(shown);
            Assert.True(back == p.BackRef(shown), $"{script} {Cp(shown)}: back fast [{Cp(back)}] != reference [{Cp(p.BackRef(shown))}]");
            Assert.True(back == deva, $"{script} {Cp(deva)} -> [{Cp(shown)}] -> [{Cp(back)}]: did not round-trip");
        }
    }

    [Theory]
    [MemberData(nameof(Scripts))]
    public void NewLetters_ReadBackThroughAutoDetect(Script script)
    {
        // Search and the dictionary read text back through Any2Ipe, which picks a converter per character by
        // Unicode block. A kept Devanagari letter splits a word into runs of two scripts; Any2Ipe joins them
        // in Devanagari before IPE, so the kept sign stays with its consonant.
        var p = Pairs.Single(x => x.Script == script);
        foreach (var deva in Cases)
            Assert.True(Deva2Ipe.Convert(deva) == Any2Ipe.Convert(p.To(deva)),
                $"{script} {Cp(deva)} -> [{Cp(p.To(deva))}]: Any2Ipe read it back differently");
    }

    [Fact]
    public void NativeSpellings_ExactCodePoints()
    {
        // fsnow, 2026-10-13: "use the native spellings" for the letters these scripts write by convention.
        // Thai: the vocalic r sign is the vocalic r letter after the consonant; au is the e...aa frame.
        Assert.Equal("\u0E27\u0E24\u0E01\u0E3A\u0E29", Deva2Thai.Convert("\u0935\u0943\u0915\u094D\u0937"));
        Assert.Equal("\u0E40\u0E01\u0E32", Deva2Thai.Convert("\u0915\u094C"));
        Assert.Equal("\u0E40\u0E2D\u0E32", Deva2Thai.Convert("\u0914"));
        // Tibetan: ra + reversed gi gu; subjoined ra after a consonant.
        Assert.Equal("\u0F62\u0F80", Deva2Tibt.Convert("\u090B"));
        Assert.Equal("\u0F40\u0FB2\u0F80", Deva2Tibt.Convert("\u0915\u0943"));
        // Khmer: coeng + the vocalic r letter.
        Assert.Equal("\u1780\u17D2\u17AB", Deva2Khmr.Convert("\u0915\u0943"));
    }

    [Fact]
    public void Ipe_SortsTheNewLettersInTheAgreedOrder()
    {
        // Each letter's IPE code, in the agreed alphabetical order. Consonants are written with a virama so
        // the IPE is the bare consonant.
        string[] ordered =
        {
            "\u0902", "\u0903",                                           // niggahita, visarga
            "\u0905", "\u0906", "\u0907", "\u0908", "\u0909", "\u090A",   // a aa i ii u uu
            "\u090B", "\u090F", "\u0910", "\u0913", "\u0914",             // vocalic r, e, ai, o, au
            "\u0915\u094D", "\u0935\u094D",                          // ka ... va
            "\u0936\u094D", "\u0937\u094D", "\u0938\u094D", "\u0939\u094D", "\u0933\u094D", // sha ssa sa ha lla
        };
        var codes = ordered.Select(Deva2Ipe.Convert).ToArray();
        Assert.All(codes, c => Assert.Equal(1, c.Length));
        for (int i = 1; i < codes.Length; i++)
            Assert.True(string.CompareOrdinal(codes[i - 1], codes[i]) < 0,
                $"IPE order broken between {Cp(ordered[i - 1])} (U+{(int)codes[i - 1][0]:X4}) and {Cp(ordered[i])} (U+{(int)codes[i][0]:X4})");
    }

    [Fact]
    public void Ipe_RoundTripsTheNewLetters_OnBothPaths()
    {
        foreach (var deva in Cases)
        {
            var ipe = Deva2Ipe.Convert(deva);
            Assert.Equal(ipe, Deva2Ipe.ConvertReference(deva));
            Assert.Equal(deva, Ipe2Deva.Convert(ipe));
            Assert.Equal(deva, Ipe2Deva.ConvertReference(ipe));
            Assert.Equal(Ipe2Latn.Convert(ipe), Ipe2Latn.ConvertReference(ipe));
        }
    }

    [Fact]
    public void Latin_WritesTheNewLetters()
    {
        Assert.Equal("k\u1E5Bta\u1E25", Deva2Latn.Convert("\u0915\u0943\u0924\u0903"));
        Assert.Equal("\u015B\u016Bdro", Deva2Latn.Convert("\u0936\u0942\u0926\u094D\u0930\u094B"));
        Assert.Equal("vai\u015Bya\u1E25", Deva2Latn.Convert("\u0935\u0948\u0936\u094D\u092F\u0903"));
        Assert.Equal("k\u1E63a", Deva2Latn.Convert("\u0915\u094D\u0937"));
        // IPE -> Latin gives the same as Devanagari -> Latin
        foreach (var deva in Cases)
            Assert.Equal(Deva2Latn.Convert(deva), Ipe2Latn.Convert(Deva2Ipe.Convert(deva)));
    }

    [Fact]
    public void Latin_ReadsTheNewLettersBack_ExceptTheDiphthongs()
    {
        foreach (var conv in new Func<string, string>[] { Latn2Deva.Convert, Latn2Deva.ConvertReference })
        {
            Assert.Equal("\u0915\u0943\u0924\u0903", conv("k\u1E5Bta\u1E25"));
            Assert.Equal("\u0936\u0942\u0926\u094D\u0930\u094B", conv("\u015B\u016Bdro"));
            Assert.Equal("\u0915\u094D\u0937", conv("k\u1E63a"));
            Assert.Equal("\u090B\u0937\u093F", conv("\u1E5B\u1E63i"));
            // ai stays the Pali hiatus a + i: "kai" is ka + independent i
            Assert.Equal("\u0915\u0907", conv("kai"));
        }
        foreach (var conv in new Func<string, string>[] { Latn2Ipe.Convert, Latn2Ipe.ConvertReference })
        {
            Assert.Equal(Deva2Ipe.Convert("\u0915\u0943\u0924\u0903"), conv("k\u1E5Bta\u1E25"));
            Assert.Equal(Deva2Ipe.Convert("\u0936\u0942\u0926\u094D\u0930\u094B"), conv("\u015A\u016Bdro")); // capital S acute
            Assert.Equal(Deva2Ipe.Convert("\u0915\u0907"), conv("kai"));
        }
    }

    private static string Cp(string s) => string.Join(" ", s.Select(c => ((int)c).ToString("X4")));
}
