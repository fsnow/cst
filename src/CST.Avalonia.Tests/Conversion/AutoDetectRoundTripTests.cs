using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.Conversion;

/// <summary>
/// Round-trips the ScriptValidation word lists through the AUTO-DETECT read-back path in every display
/// script: Devanagari -> IPE -> script, then <see cref="Any2Ipe"/> (auto-detect) -> IPE, compared with the
/// first IPE. (#1025)
///
/// <para><c>CST.ScriptValidation</c>'s <c>QuickTest</c> round-trips the same lists, but names the script at
/// every step, so the read-back goes through the script's own converter. <see cref="Any2Ipe"/> instead picks a
/// converter per character from its Unicode block (<see cref="ScriptDetector"/>); a converter that writes a
/// character outside its own script's block splits the run there. The converter pair is symmetric, so #1025
/// (Gurmukhi va written as the Gujarati letter) passed every script-explicit test. This one catches it.</para>
///
/// <para>The word lists are read from <c>src/CST.ScriptValidation/</c>, so there is one copy:
/// <c>syllable-test-words.txt</c> has a word for every unique syllable in the corpus,
/// <c>vowel-test-words.txt</c> a word for every vowel-hiatus combination.</para>
/// </summary>
public class AutoDetectRoundTripTests
{
    // Cyrillic is excluded on purpose. Its transliteration is a known legacy limitation that does not
    // round-trip: consonant + a-macron is written "aa", which collides with consonant + inherent a + an
    // independent a (vowel hiatus). See src/CST.ScriptValidation/README.md, "Known Issues". Ipe is the
    // pivot, not a display script, and Unknown is not a script.
    private static readonly Script[] Excluded = { Script.Cyrillic, Script.Ipe, Script.Unknown };

    public static IEnumerable<object[]> Scripts =>
        Enum.GetValues<Script>().Where(s => !Excluded.Contains(s)).Select(s => new object[] { s });

    private static readonly Lazy<IReadOnlyList<string>> Words = new(LoadWords);

    [Theory]
    [MemberData(nameof(Scripts))]
    public void EveryTestWord_RoundTripsThroughAutoDetect(Script script)
    {
        var words = Words.Value;
        Assert.True(words.Count > 2000, $"expected the ScriptValidation word lists, read {words.Count} words");

        var misses = new List<string>();
        foreach (var deva in words)
        {
            // Latin writes the diphthongs ai / au the same as the Pali hiatus a + i / a + u, and reads them
            // back as the hiatus. fsnow, 2026-10-13: "Keep Pali as is". (#1049)
            if (script == Script.Latin && deva.IndexOfAny(Diphthongs) >= 0)
                continue;
            var ipe1 = ScriptConverter.Convert(deva, Script.Devanagari, Script.Ipe);
            var shown = ScriptConverter.Convert(ipe1, Script.Ipe, script);
            var ipe2 = Any2Ipe.Convert(shown);
            if (!string.Equals(ipe1, ipe2, StringComparison.Ordinal))
                misses.Add($"{Latn(ipe1)} -> {Escape(shown)} -> {Latn(ipe2)}");
        }

        Assert.True(misses.Count == 0,
            $"{script}: {misses.Count} of {words.Count} words did not round-trip through Any2Ipe:\n"
            + string.Join("\n", misses.Take(40)));
    }

    // Characters any script's output may carry outside its own block: ASCII that is not a letter (the word
    // lists hold a few compounds joined with '+' or '='), the zero-width joiners (Devanagari and Sinhala
    // write ZWJ/ZWNJ in conjuncts; ScriptDetector keeps them in the surrounding run), and the Devanagari
    // danda and double danda, which every converter passes through as sentence punctuation.
    private static bool IsShared(char ch) =>
        (ch < 0x80 && !char.IsAsciiLetter(ch))
        || ch == '\u200C' || ch == '\u200D'
        || ch == '\u0964' || ch == '\u0965';

    private static readonly char[] Diphthongs = { '\u0910', '\u0914', '\u0948', '\u094C' };

    // The Devanagari letters a script keeps because it has no letter of its own for them. fsnow, 2026-10-13,
    // for a letter a script lacks: "Keep Devanagari". Every other Sanskrit letter is mapped since #1049; listed
    // here per script so nothing else is let through. Any2Ipe joins the runs in Devanagari, so these still
    // round-trip.
    private static readonly Dictionary<Script, char[]> KnownPassThrough = new()
    {
        [Script.Gurmukhi] = new[] { '\u090B', '\u0943', '\u0937' },  // vocalic r, retroflex sa
        [Script.Khmer] = new[] { '\u0943' },                           // vocalic r sign
        [Script.Myanmar] = new[] { '\u0914', '\u094C' },               // au
        [Script.Thai] = new[] { '\u0943', '\u0903', '\u0914', '\u094C' }, // vocalic r sign, visarga, au
        [Script.Tibetan] = new[] { '\u090B', '\u0943' },               // vocalic r
    };

    /// <summary>
    /// Every character a script's display conversion writes must be one <see cref="ScriptDetector"/> assigns
    /// to that script (for Latin: one in no other script's block), or be in the explicit allow-lists above.
    /// This is the property #1025 broke - Gurmukhi output carried U+0AB5 from the Gujarati block - asserted
    /// directly. The round trip above cannot guard it on its own: Guru2Deva still reads U+0AB5 for old text,
    /// so writing it again would round-trip cleanly.
    /// </summary>
    [Theory]
    [MemberData(nameof(Scripts))]
    public void EveryTestWord_OutputStaysInItsOwnScriptBlock(Script script)
    {
        var allowed = KnownPassThrough.TryGetValue(script, out var extra) ? extra : Array.Empty<char>();
        var offenders = new SortedDictionary<char, string>();
        foreach (var deva in Words.Value)
        {
            var ipe = ScriptConverter.Convert(deva, Script.Devanagari, Script.Ipe);
            var shown = ScriptConverter.Convert(ipe, Script.Ipe, script);
            foreach (var ch in shown)
            {
                if (ScriptDetector.GetScript(ch) == script || IsShared(ch) || allowed.Contains(ch))
                    continue;
                if (!offenders.ContainsKey(ch))
                    offenders[ch] = $"{Latn(ipe)} -> {Escape(shown)}";
            }
        }

        Assert.True(offenders.Count == 0,
            $"{script}: output contains characters outside the {script} block:\n"
            + string.Join("\n", offenders.Select(o => $"U+{(int)o.Key:X4} ({ScriptDetector.GetScript(o.Key)}) e.g. {o.Value}")));
    }

    private static IReadOnlyList<string> LoadWords()
    {
        var dir = FindScriptValidationDir();
        var words = new List<string>();
        foreach (var name in new[] { "syllable-test-words.txt", "vowel-test-words.txt" })
            words.AddRange(File.ReadAllText(Path.Combine(dir, name))
                .Split(new[] { ' ', '\n', '\r', '\t' }, StringSplitOptions.RemoveEmptyEntries));
        return words.Distinct(StringComparer.Ordinal).ToList();
    }

    /// <summary>Walk up from the test binary to the repo, so the lists are read from the working tree.</summary>
    private static string FindScriptValidationDir()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir is not null)
        {
            var candidate = Path.Combine(dir.FullName, "src", "CST.ScriptValidation");
            if (File.Exists(Path.Combine(candidate, "syllable-test-words.txt"))) return candidate;
            dir = dir.Parent;
        }
        throw new FileNotFoundException("src/CST.ScriptValidation/syllable-test-words.txt not found above "
                                        + AppContext.BaseDirectory);
    }

    private static string Latn(string ipe) => ScriptConverter.Convert(ipe, Script.Ipe, Script.Latin);

    private static string Escape(string s) =>
        string.Concat(s.Select(ch => ch < 0x80 ? ch.ToString() : $"\\u{(int)ch:X4}"));
}
