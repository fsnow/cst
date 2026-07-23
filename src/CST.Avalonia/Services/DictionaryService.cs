using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Constants;
using CST.Avalonia.Models;
using CST.Avalonia.Search;
using CST.Conversion;
using CST.Tools;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services;

/// <summary>
/// Loads the Pāli dictionaries from <c>&lt;app-support&gt;/CSTReader/dictionaries/&lt;lang&gt;/</c> and
/// answers lookups. Faithful backend port of CST4's <c>FormDictionary</c> load/search logic, minus the UI.
///
/// <para>Each language directory holds one dictionary's flat text file (UTF-8): alternating lines of
/// headword then HTML definition. Headwords are normalized to IPE via <see cref="Any2Ipe"/> so that
/// queries match regardless of the script the user typed. Data is loaded lazily per language and cached.</para>
/// </summary>
public sealed class DictionaryService : IDictionaryService
{
    /// <summary>
    /// Sentinel inserted between the definitions of a repeated headword within a file (a merged entry).
    /// The dictionary renderer splits on this and shows a visual break; it is not HTML that any WebView
    /// renders. Shared here so the loader and the renderer (<c>MeaningParser</c>) cannot drift.
    /// (CST4's English loader used a malformed <c>"&lt;/p&gt;&lt;hr/&lt;p&gt;"</c>.)
    /// </summary>
    public const string MeaningSeparator = "<hr/>";

    private readonly ILogger<DictionaryService> _logger;
    private readonly string _dictionariesDirectory;
    private readonly bool _isDefaultLocation;

    private readonly Dictionary<string, DictionaryIndex> _cache = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    /// <param name="logger">Injected logger.</param>
    /// <param name="dictionariesDirectory">Override for the dictionaries root; defaults to
    /// <c>&lt;ApplicationData&gt;/CSTReader/dictionaries</c>. Tests pass a temp directory.</param>
    public DictionaryService(ILogger<DictionaryService> logger, string? dictionariesDirectory = null)
    {
        _logger = logger;
        _isDefaultLocation = dictionariesDirectory == null;
        _dictionariesDirectory = dictionariesDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppConstants.AppDataDirectoryName,
            "dictionaries");

        // On a real install app-support starts empty; populate it once from the bundled data. Skipped
        // when a directory is injected (tests), so it never overwrites test fixtures. (#25)
        if (_isDefaultLocation)
            EnsureBundledDictionaries();
    }

    // First-run copy of the bundled dictionaries into app-support, mirroring EnsureXslFilesInUserDirectory.
    private void EnsureBundledDictionaries()
    {
        try
        {
            var source = ResolveBundledDictionariesDir();
            if (source == null)
            {
                _logger.LogWarning("No bundled dictionaries found to seed {Path}", _dictionariesDirectory);
                return;
            }
            int copied = SeedDictionaries(source, _dictionariesDirectory);
            _logger.LogInformation("Seeded/refreshed {Count} bundled dictionary file(s) into {Path}", copied, _dictionariesDirectory);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to seed bundled dictionaries into {Path}", _dictionariesDirectory);
        }
    }

    // Copy bundled dictionary files into the app-support tree. Re-checked every launch (cheap).
    //   - Dictionary DATA files are written only when MISSING — an existing install's data is never clobbered.
    //   - source.json is app-owned METADATA (display name + attribution, #268/#466): REFRESH it whenever the
    //     bundled copy differs, so a metadata change (e.g. a new displayName) reaches an existing install on its
    //     next launch, not just fresh installs. (Otherwise the write-if-missing rule pins the stale copy forever.)
    internal static int SeedDictionaries(string sourceRoot, string destRoot)
    {
        int copied = 0;
        foreach (var langDir in Directory.GetDirectories(sourceRoot))
        {
            var destLangDir = Path.Combine(destRoot, Path.GetFileName(langDir));
            Directory.CreateDirectory(destLangDir);
            foreach (var file in Directory.GetFiles(langDir))
            {
                var dest = Path.Combine(destLangDir, Path.GetFileName(file));
                bool isMeta = string.Equals(Path.GetFileName(file), "source.json", StringComparison.OrdinalIgnoreCase);
                if (!File.Exists(dest))
                {
                    File.Copy(file, dest);
                    copied++;
                }
                else if (isMeta && !FilesEqual(file, dest))
                {
                    File.Copy(file, dest, overwrite: true);
                    copied++;
                }
            }
        }
        return copied;
    }

    private static bool FilesEqual(string a, string b)
    {
        var fa = new FileInfo(a);
        var fb = new FileInfo(b);
        if (!fa.Exists || !fb.Exists || fa.Length != fb.Length) return false;
        return File.ReadAllBytes(a).AsSpan().SequenceEqual(File.ReadAllBytes(b));
    }

    // The bundled dictionaries live in the dev project dir, or under Resources/ in a packaged .app.
    private static string? ResolveBundledDictionariesDir()
    {
        var asmDir = Path.GetDirectoryName(System.Reflection.Assembly.GetExecutingAssembly().Location) ?? "";
        // Development: bin/<cfg>/<tfm>/ -> ../../../dictionaries (the project's dictionaries/ folder)
        var dev = Path.Combine(asmDir, "..", "..", "..", "dictionaries");
        if (Directory.Exists(dev))
            return dev;
        // Packaged beside the executable (Windows/Linux self-contained publish): <app>/dictionaries. (#403)
        var beside = Path.Combine(asmDir, "dictionaries");
        if (Directory.Exists(beside))
            return beside;
        // Packaged .app: Contents/MacOS/ -> ../Resources/dictionaries
        var bundle = Path.Combine(asmDir, "..", "Resources", "dictionaries");
        if (Directory.Exists(bundle))
            return bundle;
        return null;
    }

    public IReadOnlyList<string> AvailableLanguages
    {
        get
        {
            if (!Directory.Exists(_dictionariesDirectory))
                return Array.Empty<string>();

            return Directory.EnumerateDirectories(_dictionariesDirectory)
                .Where(dir => Directory.EnumerateFiles(dir).Any())
                .Select(dir => Path.GetFileName(dir))
                .OrderBy(name => name, StringComparer.Ordinal)
                .ToList();
        }
    }

    private readonly Dictionary<string, DictionarySourceInfo?> _sourceCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly System.Text.Json.JsonSerializerOptions SourceJsonOpts = new() { PropertyNameCaseInsensitive = true };

    public DictionarySourceInfo? SourceFor(string language)
    {
        if (string.IsNullOrEmpty(language)) return null;
        // Confine to an ACTUAL dictionary dir before Path.Combine (path-traversal guard, same as #352).
        var canonical = AvailableLanguages.FirstOrDefault(
            l => string.Equals(l, language, StringComparison.OrdinalIgnoreCase));
        if (canonical is null) return null;

        lock (_sourceCache)
            if (_sourceCache.TryGetValue(canonical, out var cached)) return cached;

        DictionarySourceInfo? info = null;
        try
        {
            var path = Path.Combine(_dictionariesDirectory, canonical, "source.json");
            if (File.Exists(path))
            {
                info = System.Text.Json.JsonSerializer.Deserialize<DictionarySourceInfo>(
                    File.ReadAllText(path), SourceJsonOpts);
                // A placeholder file with every field blank is NOT attribution — report null, never a guess.
                if (info is not null && IsUnattributed(info)) info = null;
            }
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to read source.json for dictionary language {Language}", canonical);
        }
        lock (_sourceCache) _sourceCache[canonical] = info;
        return info;
    }

    // A DisplayName counts as metadata worth keeping: a source.json that sets ONLY a display name (e.g. hi's
    // "VRI Pāli-Hindi Dictionary", with no citation yet) must still surface so the picker shows the real name
    // rather than the bare language code. (#466)
    private static bool IsUnattributed(DictionarySourceInfo s) =>
        string.IsNullOrWhiteSpace(s.DisplayName) &&
        string.IsNullOrWhiteSpace(s.Title) && string.IsNullOrWhiteSpace(s.Compiler) &&
        string.IsNullOrWhiteSpace(s.Edition) && string.IsNullOrWhiteSpace(s.Year) &&
        string.IsNullOrWhiteSpace(s.Publisher) && string.IsNullOrWhiteSpace(s.License) &&
        string.IsNullOrWhiteSpace(s.Url);

    public async Task<IReadOnlyList<DictionaryWord>> LookupAsync(string language, string query, CancellationToken ct = default)
    {
        if (string.IsNullOrEmpty(language) || string.IsNullOrEmpty(query))
            return Array.Empty<DictionaryWord>();

        // SECURITY (#352): confine `language` to an ACTUAL dictionary subdirectory before it ever reaches
        // Path.Combine in LoadLanguageAsync — a rooted path or "../" would otherwise escape the dictionaries root
        // and read arbitrary files. Exact catalog match (mirrors PassageTool.IsCatalogBook, #301); use the
        // canonical directory name, never the raw client value.
        var canonical = AvailableLanguages.FirstOrDefault(
            l => string.Equals(l, language, StringComparison.OrdinalIgnoreCase));
        if (canonical == null)
            return Array.Empty<DictionaryWord>();

        var index = await GetOrLoadIndexAsync(canonical, ct).ConfigureAwait(false);
        if (index == null)
            return Array.Empty<DictionaryWord>();

        // Normalize the pasted query so it matches the (NFC-clean) headwords: drop zero-width joiners
        // (same class as SRCH-3 — reuse its stripper so the joiner set stays single-sourced), lower-case
        // (CST4 lower-cased the input box), and compose to NFC so decomposed diacritics (e.g. a + U+0304
        // instead of ā) don't produce a different IPE key. Then convert any script to IPE. (DICT-4)
        var normalized = MultiWordSearch.StripJoiners(query).ToLowerInvariant().Normalize(NormalizationForm.FormC);
        var ipeQuery = Any2Ipe.Convert(normalized);
        return index.Lookup(ipeQuery);
    }

    private async Task<DictionaryIndex?> GetOrLoadIndexAsync(string language, CancellationToken ct)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(language, out var cached))
                return cached;
        }

        await _loadLock.WaitAsync(ct).ConfigureAwait(false);
        try
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(language, out var cached))
                    return cached;
            }

            var index = await LoadLanguageAsync(language, ct).ConfigureAwait(false);
            if (index != null)
            {
                lock (_cache)
                {
                    _cache[language] = index;
                }
            }
            return index;
        }
        finally
        {
            _loadLock.Release();
        }
    }

    private async Task<DictionaryIndex?> LoadLanguageAsync(string language, CancellationToken ct)
    {
        var languageDir = Path.Combine(_dictionariesDirectory, language);
        if (!Directory.Exists(languageDir))
        {
            _logger.LogWarning("No dictionary directory for language '{Language}' at {Path}", language, languageDir);
            return null;
        }

        try
        {
            // Merge into a unique-headword map so the index's binary search has unique keys. A repeated
            // headword's definitions are joined rather than double-listed.
            var merged = new Dictionary<string, string>(StringComparer.Ordinal);

            foreach (var file in Directory.GetFiles(languageDir).OrderBy(f => f, StringComparer.Ordinal))
            {
                ct.ThrowIfCancellationRequested();   // a client timeout stops the load, not just the caller's await
                var lines = await File.ReadAllLinesAsync(file, ct).ConfigureAwait(false);

                // Lines come in (headword, definition) pairs; skip a pair if either side is empty.
                for (int i = 0; i + 1 < lines.Length; i += 2)
                {
                    var word = lines[i];
                    var meaning = lines[i + 1];
                    if (string.IsNullOrEmpty(word) || string.IsNullOrEmpty(meaning))
                        continue;

                    var ipeWord = Any2Ipe.Convert(word);
                    merged[ipeWord] = merged.TryGetValue(ipeWord, out var existing)
                        ? existing + MeaningSeparator + meaning
                        : meaning;
                }
            }

            var index = new DictionaryIndex(merged.Select(kv => new DictionaryWord(kv.Key, kv.Value)));
            _logger.LogInformation("Loaded {Count} '{Language}' dictionary entries from {Path}",
                index.Count, language, languageDir);
            return index;
        }
        catch (OperationCanceledException) { throw; }   // cancellation isn't a load failure — let it propagate
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading '{Language}' dictionary from {Path}", language, languageDir);
            return null;
        }
    }
}
