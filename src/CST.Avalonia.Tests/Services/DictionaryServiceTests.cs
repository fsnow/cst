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
    public void SourceFor_reads_authoritative_metadata_and_treats_a_blank_placeholder_as_null()
    {
        WriteLang("en", "vri-pali-english-dictionary.txt", "buddha", "<p>awakened</p>");
        WriteLang("hi", "vri-pali-hindi-dictionary.txt", "buddha", "<p>bauddha</p>");
        // en: real attribution
        File.WriteAllText(Path.Combine(_root, "en", "source.json"),
            "{ \"title\": \"Test PED\", \"publisher\": \"VRI\", \"year\": \"2020\" }");
        // hi: the blank placeholder we ship -> NOT attribution (must be null, never a guess)
        File.WriteAllText(Path.Combine(_root, "hi", "source.json"),
            "{ \"title\": \"\", \"compiler\": \"\", \"edition\": \"\", \"year\": \"\", \"publisher\": \"\", \"license\": \"\", \"url\": \"\" }");

        var svc = Service();
        var en = svc.SourceFor("en");
        Assert.NotNull(en);
        Assert.Equal("Test PED", en!.Title);
        Assert.Equal("VRI", en.Publisher);
        Assert.Null(svc.SourceFor("hi"));       // blank placeholder collapses to null
        Assert.Null(svc.SourceFor("zzz"));       // unknown language -> null (and no path traversal)
    }

    [Fact]
    public void SourceFor_surfaces_a_displayName_only_source()
    {
        // A source.json with ONLY a display name (hi's real case: "VRI Pāli-Hindi Dictionary" with no citation
        // yet) must NOT collapse to null — else the picker falls back to the bare language code. (#466)
        WriteLang("hi", "vri-pali-hindi-dictionary.txt", "buddha", "<p>bauddha</p>");
        File.WriteAllText(Path.Combine(_root, "hi", "source.json"),
            "{ \"displayName\": \"VRI Pāli-Hindi Dictionary\", \"title\": \"\", \"url\": \"\" }");

        var hi = Service().SourceFor("hi");
        Assert.NotNull(hi);
        Assert.Equal("VRI Pāli-Hindi Dictionary", hi!.DisplayName);
        Assert.True(string.IsNullOrEmpty(hi.Title));   // no citation title yet
    }

    [Fact]
    public void SourceFor_is_null_when_no_source_file()
    {
        WriteLang("en", "vri-pali-english-dictionary.txt", "buddha", "<p>awakened</p>");
        Assert.Null(Service().SourceFor("en"));   // no source.json -> null, not a filename-derived guess
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
    public async Task Lookup_ConfinesLanguage_RejectsTraversalAndReadsNothingOutsideRoot()
    {
        // #352: a client-supplied `language` must be confined to a real dictionary subdirectory. An absolute or
        // "../" path that would otherwise make LoadLanguageAsync read arbitrary files must return empty and load
        // nothing — while a genuine language still resolves (case-insensitively).
        WriteLang("en", "d.txt", "buddha", "<p>awakened</p>");

        var secretDir = Path.Combine(Path.GetTempPath(), "cst-dict-secret-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(secretDir);
        File.WriteAllLines(Path.Combine(secretDir, "s.txt"), new[] { "topsecret", "<p>classified</p>" });
        try
        {
            var svc = Service();

            // Absolute path — Path.Combine would discard the dictionaries root and read secretDir.
            Assert.Empty(await svc.LookupAsync(secretDir, "topsecret"));
            // Relative traversal to the same place (both are siblings under the temp dir).
            Assert.Empty(await svc.LookupAsync(Path.Combine("..", Path.GetFileName(secretDir)), "topsecret"));

            // A real language still works, case-insensitively.
            Assert.Single(await svc.LookupAsync("EN", "buddha"));
        }
        finally { try { Directory.Delete(secretDir, recursive: true); } catch { } }
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
    public async Task Lookup_DecomposedDiacritics_MatchPrecomposedHeadword()
    {
        // DICT-4: headword stored precomposed (NFC) as "g\u0101th\u0101" (ā = U+0101); query pasted with the
        // macron decomposed ("a" + U+0304). Without NFC normalization these produce different IPE keys
        // and the exact match is silently lost.
        WriteLang("en", "d.txt", "g\u0101th\u0101", "<p>a verse</p>");

        var r = await Service().LookupAsync("en", "ga\u0304tha\u0304");

        Assert.Single(r);
        Assert.Contains("a verse", r[0].Meaning);
    }

    [Fact]
    public async Task Lookup_PastedZeroWidthJoiners_StillMatches()
    {
        // DICT-4 (same class as SRCH-3): ZWNJ/ZWJ pasted into a Latin query would survive into the IPE
        // key and match nothing unless stripped.
        WriteLang("en", "d.txt", "dhamma", "<p>the teaching</p>");

        var r = await Service().LookupAsync("en", "dha\u200Cm\u200Dma");

        Assert.Single(r);
        Assert.Contains("the teaching", r[0].Meaning);
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
