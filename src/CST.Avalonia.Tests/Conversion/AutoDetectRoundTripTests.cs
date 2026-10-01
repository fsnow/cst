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
