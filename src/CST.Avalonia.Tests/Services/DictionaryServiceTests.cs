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
/// Loader/lookup tests for <see cref="DictionaryService"/> over a temporary dictionaries tree. Headwords
/// are written in Latin and normalized to IPE by the service; the script-independence test uses the
/// app's own <see cref="ScriptConverter"/> to produce a non-Latin query rather than embedding non-Latin
/// literals in this source file (per repo convention).
/// </summary>
public class DictionaryServiceTests : IDisposable
{
    private readonly string _root;

    public DictionaryServiceTests()
    {
        _root = Path.Combine(Path.GetTempPath(), "cst-dict-tests-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(_root);
    }

    public void Dispose()
    {
        try { if (Directory.Exists(_root)) Directory.Delete(_root, recursive: true); }
        catch { /* best-effort temp cleanup */ }
    }

    private DictionaryService Service() => new(NullLogger<DictionaryService>.Instance, _root);

    private void WriteLang(string lang, string fileName, params string[] lines)
    {
        var dir = Path.Combine(_root, lang);
        Directory.CreateDirectory(dir);
        File.WriteAllLines(Path.Combine(dir, fileName), lines);
    }

    [Fact]
    public void AvailableLanguages_ListsOnlyLanguageDirsWithFiles()
    {
        WriteLang("en", "vri-pali-english-dictionary.txt", "buddha", "<p>awakened</p>");
        WriteLang("hi", "vri-pali-hindi-dictionary.txt", "buddha", "<p>bauddha</p>");
        Directory.CreateDirectory(Path.Combine(_root, "zz")); // empty -> excluded

        Assert.Equal(new[] { "en", "hi" }, Service().AvailableLanguages);
    }

    [Fact]
    public void AvailableLanguages_MissingRoot_IsEmpty()
    {
        var svc = new DictionaryService(NullLogger<DictionaryService>.Instance,
            Path.Combine(_root, "does-not-exist"));
        Assert.Empty(svc.AvailableLanguages);
    }

    [Fact]
    public async Task Lookup_ExactMatch_ReturnsEntryWithMeaning()
    {
        WriteLang("en", "d.txt", "buddha", "<p>awakened one</p>", "dhamma", "<p>the teaching</p>");

        var r = await Service().LookupAsync("en", "buddha");

        Assert.Single(r);
        Assert.Contains("awakened one", r[0].Meaning);
    }

    [Fact]
    public async Task Lookup_LatinQuery_IsCaseInsensitive()
    {
        WriteLang("en", "d.txt", "buddha", "<p>awakened one</p>");

        var r = await Service().LookupAsync("en", "Buddha");

        Assert.Single(r);
        Assert.Contains("awakened one", r[0].Meaning);
    }

    [Fact]
    public async Task Lookup_IsScriptIndependent()
    {
        // Store a Latin headword, then query with its Devanagari form produced by the app's converter.
        // Both normalize to the same IPE, so the lookup must find the entry.
        WriteLang("en", "d.txt", "buddha", "<p>awakened one</p>");
        var devanagariQuery = ScriptConverter.Convert("buddha", Script.Latin, Script.Devanagari);

        var r = await Service().LookupAsync("en", devanagariQuery);

        Assert.Single(r);
        Assert.Contains("awakened one", r[0].Meaning);
    }

    [Fact]
    public async Task Lookup_RepeatedHeadword_MergesDefinitions()
    {
        WriteLang("en", "d.txt", "eva", "<p>indeed</p>", "eva", "<p>emphatic particle</p>");

        var r = await Service().LookupAsync("en", "eva");

        Assert.Single(r);
        Assert.Contains("indeed", r[0].Meaning);
        Assert.Contains("emphatic particle", r[0].Meaning);
        Assert.Contains("<hr/>", r[0].Meaning);
    }

    [Fact]
    public async Task Lookup_UnknownLanguage_ReturnsEmpty()
    {
        WriteLang("en", "d.txt", "buddha", "<p>awakened one</p>");
        Assert.Empty(await Service().LookupAsync("xx", "buddha"));
    }

    [Fact]
    public async Task Lookup_EmptyQuery_ReturnsEmpty()
    {
        WriteLang("en", "d.txt", "buddha", "<p>awakened one</p>");
        Assert.Empty(await Service().LookupAsync("en", ""));
    }
}
