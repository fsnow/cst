using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.Conversion;

/// <summary>
/// The whole corpus, Devanagari -> script -> Devanagari, for the scripts whose converters move or combine
/// letters: Thai (pre-vowels, the au frame, vocalic r after a consonant), Myanmar (medials, kinzi, tall aa,
/// au as o + asat), Tibetan (stacking, tsheg) and Khmer (coeng). (#1049 review)
///
/// <para><see cref="ConverterEquivalenceTests"/> only checks that the fast and reference paths agree, so a
/// rule that misreads real text in BOTH paths would pass it. This one would not. Joiners are compared
/// stripped (the converters drop and re-insert them by their own rules), and the Devanagari abbreviation
/// sign U+0970 by the "." these scripts write for it. No-ops when the corpus is absent.</para>
/// </summary>
public class CorpusRoundTripTests
{
    private static string XmlDir =>
        Environment.GetEnvironmentVariable("CST_XML_DIR")
        ?? Path.Combine(Environment.GetEnvironmentVariable("HOME") ?? "", "Library/Application Support/CSTReader/xml");

    public static IEnumerable<object[]> Pairs => new[]
    {
        new object[] { "Thai" }, new object[] { "Myanmar" }, new object[] { "Tibetan" }, new object[] { "Khmer" },
    };

    private static (Func<string, string> To, Func<string, string> Back) For(string name) => name switch
    {
        "Thai" => (Deva2Thai.Convert, Thai2Deva.Convert),
        "Myanmar" => (Deva2Mymr.Convert, Mymr2Deva.Convert),
        "Tibetan" => (Deva2Tibt.Convert, Tibt2Deva.Convert),
        "Khmer" => (Deva2Khmr.Convert, Khmr2Deva.Convert),
        _ => throw new ArgumentException(name),
    };

    private static string Norm(string s) =>
        s.Replace("\u200C", "").Replace("\u200D", "").Replace("\u0970", ".");

    [Theory]
    [MemberData(nameof(Pairs))]
    public void EveryBook_RoundTrips(string script)
    {
        if (!Directory.Exists(XmlDir)) return;
        var files = Directory.GetFiles(XmlDir, "*.xml");
        if (files.Length == 0) return;

        var (to, back) = For(script);
        var misses = new List<string>();
        foreach (var f in files.OrderBy(x => x, StringComparer.Ordinal))
        {
            var lines = File.ReadAllText(f).Split('\n');
            foreach (var line in lines)
            {
                var expected = Norm(line);
                var got = Norm(back(to(line)));
                if (got != expected && misses.Count < 20)
                    misses.Add($"{Path.GetFileName(f)}: {Snip(expected, got)}");
            }
        }
        Assert.True(misses.Count == 0, $"{script}: lines that did not round-trip:\n" + string.Join("\n", misses));
    }

    // Sequences the Thai rules must NOT treat as au or as the vocalic r sign.
    [Theory]
    [InlineData("\u0915\u0947\u0906")]             // ke + independent aa (written e-frame + a + aa)
    [InlineData("\u0915\u0947\u0915\u093E")]       // ke + kaa
    [InlineData("\u0915 \u090B")]                  // independent vocalic r after a space
    [InlineData("\u0915\u094D \u090B")]           // ... after a visible halant and a space
    [InlineData("\u0915\u093E\u090B")]             // ... after a vowel sign
    [InlineData("\u0915\u094B\u0906")]             // ko + independent aa
    public void Thai_RulesDoNotFireOnPaliSequences(string deva)
    {
        Assert.Equal(deva, Thai2Deva.Convert(Deva2Thai.Convert(deva)));
        Assert.Equal(deva, Thai2Deva.ConvertReference(Deva2Thai.ConvertReference(deva)));
    }

    private static string Snip(string a, string b)
    {
        int i = 0;
        while (i < a.Length && i < b.Length && a[i] == b[i]) i++;
        int s = Math.Max(0, i - 10);
        string Hex(string x) => string.Join(" ", x.Skip(s).Take(24).Select(c => ((int)c).ToString("X4")));
        return $"at {i}: expected [{Hex(a)}] got [{Hex(b)}]";
    }
}
