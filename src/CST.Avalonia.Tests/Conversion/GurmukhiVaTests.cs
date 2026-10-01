using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using CST.Conversion;
using Microsoft.Data.Sqlite;
using Xunit;

namespace CST.Avalonia.Tests.Conversion;

/// <summary>
/// #1025: Gurmukhi va is U+0A35. Builds before the fix wrote U+0AB5 (the Gujarati letter va), which
/// <see cref="Any2Ipe"/>/<see cref="Any2Deva"/> split into its own run, so a Gurmukhi word with va before a
/// vowel sign or virama did not read back (<c>vAreti</c> came back as <c>vaAreti</c>). All non-ASCII text
/// here is written as escapes.
/// </summary>
public class GurmukhiVaTests
{
    private const char GuruVa = '\u0A35';
    private const char GujrVa = '\u0AB5';
    private const char DevaVa = '\u0935';

    public static IEnumerable<object[]> Words => new[]
    {
        new object[] { "v\u0101reti" },      // vareti: va + vowel sign aa
        new object[] { "\u221Adev" },        // root dev: word-final va + virama
        new object[] { "\u221Adhov" },       // root dhov
        new object[] { "deva" },             // word-final va, inherent a
        new object[] { "devat\u0101" },      // devata
        new object[] { "avy\u0101kata" },    // va + virama + ya
        new object[] { "kvaci" },            // ka + virama + va (a ZWJ conjunct in Guru2Deva)
        new object[] { "vivattati" },        // va twice, initial and medial
    };

    private static string ToGuru(string latn) => ScriptConverter.Convert(latn, Script.Latin, Script.Gurmukhi);
    private static string ToGujr(string latn) => ScriptConverter.Convert(latn, Script.Latin, Script.Gujarati);
    private static string IpeToLatn(string ipe) => ScriptConverter.Convert(ipe, Script.Ipe, Script.Latin);

    // The display-script-agnostic read-back path used by search and the dictionary.
    private static string ViaAny2Ipe(string s) => IpeToLatn(Any2Ipe.Convert(s));
    private static string ViaAny2Deva(string s) => ScriptConverter.Convert(Any2Deva.Convert(s), Script.Devanagari, Script.Latin);

    [Theory]
    [MemberData(nameof(Words))]
    public void Gurmukhi_WritesGurmukhiVa(string latn)
    {
        var guru = ToGuru(latn);
        Assert.Contains(GuruVa, guru);
        Assert.DoesNotContain(GujrVa, guru);
    }

    [Theory]
    [MemberData(nameof(Words))]
    public void Gurmukhi_RoundTrips(string latn)
    {
        var guru = ToGuru(latn);
        Assert.Equal(latn, ViaAny2Ipe(guru));
        Assert.Equal(latn, ViaAny2Deva(guru));
        Assert.Equal(latn, ScriptConverter.Convert(guru, Script.Gurmukhi, Script.Latin));
    }

    // Gurmukhi text written by builds before #1025 carries U+0AB5 for va. It still reads back.
    [Theory]
    [MemberData(nameof(Words))]
    public void LegacyGurmukhiWithGujaratiVa_StillReadsBack(string latn)
    {
        var legacy = ToGuru(latn).Replace(GuruVa, GujrVa);
        Assert.Contains(GujrVa, legacy);
        Assert.Equal(latn, ViaAny2Ipe(legacy));
        Assert.Equal(latn, ViaAny2Deva(legacy));
        Assert.Equal(latn, ScriptConverter.Convert(legacy, Script.Gurmukhi, Script.Latin));
    }

    [Theory]
    [MemberData(nameof(Words))]
    public void Gujarati_StillWritesGujaratiVa_AndRoundTrips(string latn)
    {
        var gujr = ToGujr(latn);
        Assert.Contains(GujrVa, gujr);
        Assert.Equal(latn, ViaAny2Ipe(gujr));
        Assert.Equal(latn, ViaAny2Deva(gujr));
        Assert.Equal(latn, ScriptConverter.Convert(gujr, Script.Gujarati, Script.Latin));
    }

    // Mixed runs: a Gujarati word and a (legacy) Gurmukhi word side by side each keep their own run.
    [Fact]
    public void MixedGurmukhiAndGujarati_EachWordReadsBack()
    {
        const string w = "v\u0101reti";
        var legacyGuru = ToGuru(w).Replace(GuruVa, GujrVa);
        var gujr = ToGujr(w);
        Assert.Equal(w + " " + w, ViaAny2Ipe(legacyGuru + " " + gujr));
        Assert.Equal(w + " " + w, ViaAny2Ipe(gujr + " " + legacyGuru));
        Assert.Equal(w + " " + w, ViaAny2Ipe(ToGuru(w) + " " + gujr));
    }

    [Fact]
    public void ScriptDetector_ClassesGujaratiVaByContext()
    {
        // Before a Gurmukhi vowel sign: Gurmukhi, whatever came before.
        Assert.Equal(Script.Gurmukhi, ScriptDetector.GetScript("\u0AB5\u0A3E", 0, Script.Latin));
        // Before a Gujarati vowel sign: Gujarati, even after a Gurmukhi run.
        Assert.Equal(Script.Gujarati, ScriptDetector.GetScript("\u0AB5\u0ABE", 0, Script.Gurmukhi));
        // Word-final: joins the run before it.
        Assert.Equal(Script.Gurmukhi, ScriptDetector.GetScript("\u0AA6\u0AB5", 1, Script.Gurmukhi));
        Assert.Equal(Script.Gujarati, ScriptDetector.GetScript("\u0AA6\u0AB5", 1, Script.Gujarati));
        // A ZWJ between it and the next letter is looked past.
        Assert.Equal(Script.Gurmukhi, ScriptDetector.GetScript("\u0AB5\u200D\u0A4D", 0, Script.Latin));
        // Every other character is classed by its block alone.
        Assert.Equal(Script.Gujarati, ScriptDetector.GetScript("\u0AA6\u0A3E", 0, Script.Gurmukhi));
    }

    // The converters keep two forms: the frozen readable ConvertReference and the single-pass Convert
    // (#86). Both read the same table, so both carry the change; assert each directly.
    [Fact]
    public void Deva2Guru_BothForms_WriteGurmukhiVa()
    {
        var deva = "\u0926\u0947\u0935\u094D"; // dev + virama
        var expected = "\u0A26\u0A47\u0A35\u0A4D";
        Assert.Equal(expected, Deva2Guru.Convert(deva));
        Assert.Equal(expected, Deva2Guru.ConvertReference(deva));
    }

    [Theory]
    [InlineData(GuruVa)]
    [InlineData(GujrVa)]
    public void Guru2Deva_BothForms_ReadVa(char va)
    {
        var guru = "\u0A26\u0A47" + va + "\u0A4D";
        var expected = "\u0926\u0947\u0935\u094D";
        Assert.Equal(expected, Guru2Deva.Convert(guru));
        Assert.Equal(expected, Guru2Deva.ConvertReference(guru));
    }

    [Fact]
    public void Gujarati_BothForms_Unchanged()
    {
        Assert.Equal(GujrVa.ToString(), Deva2Gujr.Convert(DevaVa.ToString()));
        Assert.Equal(GujrVa.ToString(), Deva2Gujr.ConvertReference(DevaVa.ToString()));
        Assert.Equal(DevaVa.ToString(), Gujr2Deva.Convert(GujrVa.ToString()));
        Assert.Equal(DevaVa.ToString(), Gujr2Deva.ConvertReference(GujrVa.ToString()));
    }
}

/// <summary>
/// #1025 sweep: every DPD root key through Gurmukhi and back. The #1002 review found 28 failures here.
/// Reads the installed DPD asset and no-ops when it is absent (CI), like the corpus oracle tests.
/// </summary>
[Collection("Sqlite")]
public class GurmukhiDpdRootSweepTests
{
    private static string DbPath => Path.Combine(
        Environment.GetEnvironmentVariable("HOME") ?? "/Users/fsnow",
        "Library/Application Support/CSTReader/dictionaries/dpd-cst-subset/dpd-cst-subset.db");

    private static readonly Regex HomonymSuffix = new(@" \d+$", RegexOptions.CultureInvariant);

    [Fact]
    public void EveryDpdRoot_RoundTripsThroughGurmukhi()
    {
        if (!File.Exists(DbPath))
            return; // DPD asset absent (CI / fresh checkout): no-op

        var roots = new List<string>();
        using (var c = new SqliteConnection($"Data Source={DbPath};Mode=ReadOnly;Pooling=False"))
        {
            c.Open();
            using var cmd = c.CreateCommand();
            cmd.CommandText = "SELECT root_key FROM root";
            using var r = cmd.ExecuteReader();
            while (r.Read())
                roots.Add(HomonymSuffix.Replace(r.GetString(0), ""));
        }
        roots = roots.Distinct(StringComparer.Ordinal).ToList();
        Assert.NotEmpty(roots);

        var misses = new List<string>();
        int withVa = 0;
        foreach (var root in roots)
        {
            var guru = ScriptConverter.Convert(root, Script.Latin, Script.Gurmukhi);
            if (root.IndexOf('v') >= 0) withVa++;
            var back = ScriptConverter.Convert(Any2Ipe.Convert(guru), Script.Ipe, Script.Latin);
            if (!string.Equals(root, back, StringComparison.Ordinal))
                misses.Add($"{root} -> {back}");
        }

        Assert.True(withVa > 0, "sweep exercised no root containing va");
        Assert.True(misses.Count == 0,
            $"{misses.Count} of {roots.Count} DPD roots did not round-trip through Gurmukhi:\n" + string.Join("\n", misses.Take(40)));
    }
}
