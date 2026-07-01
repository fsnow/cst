using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Constants;
using CST.Avalonia.Models;
using CST.Conversion;
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
    // Separator inserted between the definitions of a repeated headword within a file. (CST4's English
    // loader used a malformed "</p><hr/<p>"; this emits valid markup.)
    private const string MeaningSeparator = "<hr/>";

    private readonly ILogger<DictionaryService> _logger;
    private readonly string _dictionariesDirectory;

    private readonly Dictionary<string, DictionaryIndex> _cache = new();
    private readonly SemaphoreSlim _loadLock = new(1, 1);

    /// <param name="logger">Injected logger.</param>
    /// <param name="dictionariesDirectory">Override for the dictionaries root; defaults to
    /// <c>&lt;ApplicationData&gt;/CSTReader/dictionaries</c>. Tests pass a temp directory.</param>
    public DictionaryService(ILogger<DictionaryService> logger, string? dictionariesDirectory = null)
    {
        _logger = logger;
        _dictionariesDirectory = dictionariesDirectory ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            AppConstants.AppDataDirectoryName,
            "dictionaries");
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

    public async Task<IReadOnlyList<DictionaryWord>> LookupAsync(string language, string query)
    {
        if (string.IsNullOrEmpty(language) || string.IsNullOrEmpty(query))
            return Array.Empty<DictionaryWord>();

        var index = await GetOrLoadIndexAsync(language).ConfigureAwait(false);
        if (index == null)
            return Array.Empty<DictionaryWord>();

        // Normalize the query the same way headwords were normalized: lower-case (CST4 lower-cased the
        // input box) then convert any script to IPE.
        var ipeQuery = Any2Ipe.Convert(query.ToLowerInvariant());
        return index.Lookup(ipeQuery);
    }

    private async Task<DictionaryIndex?> GetOrLoadIndexAsync(string language)
    {
        lock (_cache)
        {
            if (_cache.TryGetValue(language, out var cached))
                return cached;
        }

        await _loadLock.WaitAsync().ConfigureAwait(false);
        try
        {
            lock (_cache)
            {
                if (_cache.TryGetValue(language, out var cached))
                    return cached;
            }

            var index = await LoadLanguageAsync(language).ConfigureAwait(false);
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

    private async Task<DictionaryIndex?> LoadLanguageAsync(string language)
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
                var lines = await File.ReadAllLinesAsync(file).ConfigureAwait(false);

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
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading '{Language}' dictionary from {Path}", language, languageDir);
            return null;
        }
    }
}
