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

    private static bool DataPresent => Directory.Exists(Root);

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
        if (!DataPresent) return;
        var (words, meaning) = await LookupLatn("en", Samayam);
        Assert.Equal(new[] { "samayo" }, words);
        Assert.Contains("Agreement, combination", meaning);
    }

    [Fact]
    public async Task Samayam_Hindi_ResolvesToSamayaAndSamayantara()
    {
        if (!DataPresent) return;
        var (words, _) = await LookupLatn("hi", Samayam);
        Assert.Equal(new[] { "samaya", "samayantara" }, words);
    }

    [Fact]
    public async Task Abbhuto_English_MergesDuplicateHeadword()
    {
        if (!DataPresent) return;
        var (words, meaning) = await LookupLatn("en", "abbhuto");
        Assert.Equal("abbhuto", words[0]);
        // Both definitions of the repeated headword, joined by the separator.
        Assert.Contains("Mysterious", meaning);
        Assert.Contains("Marvellous", meaning);
        Assert.Contains("<hr/>", meaning);
    }
}
