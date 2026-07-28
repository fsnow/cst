using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CST.Avalonia.Services;
using CST.Conversion;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// Oracle regression tests that pin <see cref="DictionaryService"/> output against real CST4 behavior on
/// the real VRI dictionaries. Result lists were captured from CST4 itself (screenshots in
/// <c>docs/features/planned/</c>). These read the installed data under app-support and no-op when it is
/// absent (e.g. CI), mirroring the corpus-based converter oracle tests.
/// </summary>
public class DictionaryOracleTests
{
    private static string Root => Path.Combine(
        Environment.GetEnvironmentVariable("HOME") ?? "/Users/fsnow",
        "Library/Application Support/CSTReader/dictionaries");

    // Source ids are the directory names under dictionaries/ (#522 renamed en -> vri-childers, hi -> vri-hindi).
    private const string Childers = "vri-childers";
    private const string Hindi = "vri-hindi";

    // Guard on the SPECIFIC dictionary a test needs, not just the root. A root-only check kept returning
    // true after #522 renamed the language directories, so these tests ran against ids that no longer
    // existed and failed instead of no-op'ing the way the class contract says they should.
    private static bool DataPresent(string sourceId) =>
        Directory.Exists(Path.Combine(Root, sourceId));

    private static async Task<(string[] words, string firstMeaning)> LookupLatn(string lang, string query)
    {
        var svc = new DictionaryService(NullLogger<DictionaryService>.Instance, Root);
        var r = await svc.LookupAsync(lang, query);
        var words = r.Select(w => ScriptConverter.Convert(w.Word, Script.Ipe, Script.Latin)).ToArray();
        return (words, r.Count > 0 ? r[0].Meaning : "");
    }

    // "samaya\u1E43" — niggahita ṃ = U+1E43, written as an escape so no non-Latin literal appears in source.
    private const string Samayam = "samaya\u1E43";

    [Fact]
    public async Task Samayam_English_ResolvesToSamayo()
    {
        if (!DataPresent(Childers)) return;
        var (words, meaning) = await LookupLatn(Childers, Samayam);
        Assert.Equal(new[] { "samayo" }, words);
        Assert.Contains("Agreement, combination", meaning);
    }

    [Fact]
    public async Task Samayam_Hindi_ResolvesToSamayaAndSamayantara()
    {
        if (!DataPresent(Hindi)) return;
        var (words, _) = await LookupLatn(Hindi, Samayam);
        Assert.Equal(new[] { "samaya", "samayantara" }, words);
    }

    [Fact]
    public async Task Abbhuto_English_MergesDuplicateHeadword()
    {
        if (!DataPresent(Childers)) return;
        var (words, meaning) = await LookupLatn(Childers, "abbhuto");
        Assert.Equal("abbhuto", words[0]);
        // Both definitions of the repeated headword, joined by the separator sentinel (the renderer
        // turns that sentinel into a visual break — see MeaningParserTests, DICT-1).
        Assert.Contains("Mysterious", meaning);
        Assert.Contains("Marvellous", meaning);
        Assert.Contains(DictionaryService.MeaningSeparator, meaning);
    }
}
