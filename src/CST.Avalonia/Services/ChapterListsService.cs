using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Xml;
using CST.Avalonia.Models;
using CST.Avalonia.Constants;
using CST;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services;

/// <summary>
/// Service for managing chapter lists data.
/// Port of CST4's ChapterLists class with JSON storage and async support.
///
/// <para>Each cached list records what it was built from: the book's <c>ChapterListTypes</c> and its XML
/// file's write time and length. A list whose source has changed since is rebuilt the next time it is asked
/// for. Before this, a list was built once and kept for good, so a corrected book reached readers who had
/// already opened it with its old chapter list - every markup change, and every <c>Books.cs</c> change to a
/// book's types, would have been invisible to them. (#1075)</para>
/// </summary>
public class ChapterListsService
{
    /// <summary>One cached list and the source it was built from.</summary>
    internal sealed class Entry
    {
        public string Types { get; set; } = "";
        public long FileTicks { get; set; }
        public long FileLength { get; set; }
        public List<DivTag> Chapters { get; set; } = new();
    }

    /// <summary>The file's format. A file without it (the format before #1075) carries no source record, so
    /// its lists cannot be checked and are rebuilt as each book is opened.</summary>
    private const int FileFormat = 2;

    private readonly ILogger<ChapterListsService> _logger;
    private readonly ISettingsService? _settingsService;
    private readonly string _dataFilePath;
    private readonly string? _xmlDirectoryOverride;
    private Dictionary<int, Entry> _chapterLists;

    // Guards _chapterLists and the JSON file. GetChapterList/Generate run inside Task.Run per book
    // (BookDisplayViewModel.LoadChaptersAsync), so a session restore of several chaptered books hit
    // this concurrently: unsynchronized Dictionary writes corrupt/throw, and overlapping SaveToFile
    // calls raced on the same path. Monitor is reentrant, so GetChapterList -> Generate -> SaveToFile
    // nesting is fine. (XCUT-2) Held across GenerateChapterListForBook (an XML parse) so concurrent
    // first-run generation is serialized — correct over the previous racing parallelism; the result
    // is cached, so steady state does no generation at all.
    private readonly object _lock = new object();

    public ChapterListsService(ILogger<ChapterListsService> logger, ISettingsService? settingsService = null)
        : this(logger, settingsService,
               Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                            AppConstants.AppDataDirectoryName, "chapter-lists.json"),
               xmlDirectory: null)
    {
    }

    /// <summary>For tests: an explicit cache file, and an XML directory in place of the settings'.</summary>
    internal ChapterListsService(ILogger<ChapterListsService> logger, ISettingsService? settingsService,
        string dataFilePath, string? xmlDirectory)
    {
        _logger = logger;
        _settingsService = settingsService;
        _dataFilePath = dataFilePath;
        _xmlDirectoryOverride = xmlDirectory;
        _chapterLists = new Dictionary<int, Entry>();

        lock (_lock)
        {
            LoadFromFile();
        }
    }

    /// <summary>
    /// Gets the chapter list for a specific book index
    /// </summary>
    public List<DivTag>? GetChapterList(int bookIndex)
    {
      lock (_lock)
      {
        // Debug: Check what the book's ChapterListTypes is set to
        try
        {
            var book = Books.Inst[bookIndex];
            if (book != null)
            {
                _logger.LogInformation("Book {Index} ({FileName}): ChapterListTypes = '{ChapterListTypes}'", 
                                     bookIndex, book.FileName, book.ChapterListTypes ?? "(null)");
                
                // Build the list if there is none, or if what it was built from has changed. (#1075)
                if (!string.IsNullOrEmpty(book.ChapterListTypes))
                {
                    var xmlDirectory = GetXmlDirectory();
                    if (xmlDirectory == null)
                    {
                        // Nothing to check against: serve whatever is cached rather than nothing.
                        if (!_chapterLists.ContainsKey(bookIndex))
                            _logger.LogWarning("Cannot generate chapters - no valid XML directory configured");
                    }
                    else if (!_chapterLists.TryGetValue(bookIndex, out var cached) || !IsCurrent(cached, book, xmlDirectory))
                    {
                        _logger.LogInformation("Chapter list for {FileName} is missing or out of date. Generating now...",
                            book.FileName);
                        Generate(new List<int> { bookIndex }, xmlDirectory);
                    }
                }
                else if (_chapterLists.Remove(bookIndex))
                {
                    SaveToFile();
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking book {Index} for chapter data", bookIndex);
        }

        return _chapterLists.TryGetValue(bookIndex, out var entry) ? entry.Chapters : null;
      }
    }

    /// <summary>Whether a cached list was built from the book's current types and XML file.</summary>
    private static bool IsCurrent(Entry entry, CST.Book book, string xmlDirectory)
    {
        if (entry.Types != book.ChapterListTypes) return false;
        var file = new FileInfo(Path.Combine(xmlDirectory, book.FileName));
        return file.Exists && entry.FileTicks == file.LastWriteTimeUtc.Ticks && entry.FileLength == file.Length;
    }

    /// <summary>
    /// Generates chapter lists for books that have changed.
    /// Port of CST4's ChapterLists.Generate() method.
    /// </summary>
    public void Generate(List<int> changedBookIndices, string xmlDirectory)
    {
      lock (_lock)
      {
        _logger.LogInformation("Generating chapter lists for {Count} changed books", changedBookIndices.Count);

        foreach (int bookIndex in changedBookIndices)
        {
            try
            {
                var book = Books.Inst[bookIndex];
                if (book == null)
                {
                    _logger.LogWarning("Book at index {Index} not found", bookIndex);
                    continue;
                }

                // Check if book has chapter list types defined
                if (string.IsNullOrEmpty(book.ChapterListTypes))
                {
                    _logger.LogDebug("Book {FileName} has no ChapterListTypes, skipping", book.FileName);
                    // Remove existing chapter list if book no longer has chapter types
                    _chapterLists.Remove(bookIndex);
                    continue;
                }

                _logger.LogDebug("Processing book {FileName} with chapter types: {Types}", 
                               book.FileName, book.ChapterListTypes);

                // Generate chapter list for this book
                var chapterList = GenerateChapterListForBook(book, xmlDirectory);
                
                if (chapterList.Count > 0)
                {
                    var file = new FileInfo(Path.Combine(xmlDirectory, book.FileName));
                    _chapterLists[bookIndex] = new Entry
                    {
                        Types = book.ChapterListTypes,
                        FileTicks = file.LastWriteTimeUtc.Ticks,
                        FileLength = file.Length,
                        Chapters = chapterList
                    };
                    _logger.LogInformation("Generated {Count} chapters for book {FileName}", 
                                         chapterList.Count, book.FileName);
                }
                else
                {
                    _chapterLists.Remove(bookIndex);
                    _logger.LogDebug("No chapters found for book {FileName}", book.FileName);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error generating chapter list for book index {Index}", bookIndex);
            }
        }

        // Save the updated chapter lists
        SaveToFile();
      }
    }

    /// <summary>
    /// Generates chapter list for a single book
    /// </summary>
    private List<DivTag> GenerateChapterListForBook(CST.Book book, string xmlDirectory)
    {
        var divTags = new List<DivTag>();
        
        try
        {
            // Parse chapter list types (comma-separated)
            var chapterTypes = book.ChapterListTypes
                .Split(',')
                .Select(t => t.Trim().ToLowerInvariant())
                .ToHashSet();
            
            _logger.LogInformation("Book {FileName} ChapterListTypes: '{Types}' -> Parsed: [{ParsedTypes}]", 
                                 book.FileName, book.ChapterListTypes, string.Join(", ", chapterTypes));

            // Load and parse the XML file
            string xmlFilePath = Path.Combine(xmlDirectory, book.FileName);
            if (!File.Exists(xmlFilePath))
            {
                _logger.LogWarning("XML file not found: {FilePath}", xmlFilePath);
                return divTags;
            }

            var xml = new XmlDocument();
            xml.Load(xmlFilePath);

            // Find all div elements
            var divElements = xml.GetElementsByTagName("div");
            
            foreach (XmlNode divNode in divElements)
            {
                if (divNode.Attributes?["type"]?.Value is string divType && 
                    divNode.Attributes?["id"]?.Value is string divId)
                {
                    // Check if this div type should be included
                    if (chapterTypes.Contains(divType.ToLowerInvariant()))
                    {
                        // Find the heading text
                        string heading = ExtractHeadingFromDiv(divNode);
                        if (!string.IsNullOrEmpty(heading))
                        {
                            // Calculate indentation level from ID (count underscores)
                            int indentLevel = CountUnderscores(divId);
                            
                            // Add indentation spaces (3 spaces per level like CST4)
                            string indentedHeading = new string(' ', indentLevel * 3) + heading;
                            
                            _logger.LogInformation("Adding chapter: ID='{Id}', Type='{Type}', Heading='{Heading}'", 
                                                 divId, divType, heading);
                            divTags.Add(new DivTag(divId, indentedHeading, indentLevel));
                        }
                    }
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error parsing XML for book {FileName}", book.FileName);
        }

        return divTags;
    }

    /// <summary>
    /// Extracts heading text from a div element
    /// </summary>
    private string ExtractHeadingFromDiv(XmlNode divNode)
    {
        // Look for head element within the div
        var headElement = divNode.SelectSingleNode(".//head");
        if (headElement != null)
        {
            // Drop any <note> (footnote) descendants BEFORE reading the text. InnerText already strips
            // markup, so the old "read InnerText, then regex-strip <note>...</note>" was a no-op — the
            // tags were gone and the footnote's *text* stayed merged into the heading. Clone first so we
            // don't mutate the shared source tree. (BOOK-6)
            var headClone = headElement.CloneNode(true);
            var notes = headClone.SelectNodes(".//note");
            if (notes != null)
            {
                foreach (var note in notes.Cast<XmlNode>().ToList())
                    note.ParentNode?.RemoveChild(note);
            }

            return headClone.InnerText?.Trim() ?? "";
        }

        return "";
    }

    /// <summary>
    /// Counts underscores in an ID to determine indentation level
    /// </summary>
    private int CountUnderscores(string id)
    {
        return id.Count(c => c == '_');
    }

    /// <summary>
    /// Loads chapter lists from JSON file
    /// </summary>
    private void LoadFromFile()
    {
        try
        {
            if (File.Exists(_dataFilePath))
            {
                string jsonContent = File.ReadAllText(_dataFilePath);
                var data = JsonSerializer.Deserialize<CacheFile>(jsonContent);

                if (data?.Format == FileFormat && data.Books != null)
                {
                    // Convert string keys back to int keys
                    _chapterLists = data.Books.ToDictionary(kvp => int.Parse(kvp.Key), kvp => kvp.Value);
                    _logger.LogInformation("Loaded chapter lists for {Count} books", _chapterLists.Count);
                }
                else
                {
                    // The format before #1075: no record of what each list was built from, so none can be
                    // trusted. Each book's list is rebuilt the first time it is opened.
                    _logger.LogInformation("Chapter lists file has no source records - lists will be rebuilt as books open");
                }
            }
            else
            {
                _logger.LogInformation("No chapter lists file found, starting with empty data");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error loading chapter lists from file");
            _chapterLists = new Dictionary<int, Entry>();
        }
    }

    /// <summary>
    /// Saves chapter lists to JSON file
    /// </summary>
    private void SaveToFile()
    {
        try
        {
            // Ensure directory exists
            Directory.CreateDirectory(Path.GetDirectoryName(_dataFilePath)!);
            
            // Convert int keys to string keys for JSON serialization
            var data = new CacheFile
            {
                Format = FileFormat,
                Books = _chapterLists.ToDictionary(kvp => kvp.Key.ToString(), kvp => kvp.Value)
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            
            string jsonContent = JsonSerializer.Serialize(data, options);

            // Atomic write: temp file + replace, so a crash mid-write can't leave a torn
            // chapter-lists.json (which would then fail to load and lose all cached chapters).
            // Same pattern as ApplicationStateService / SettingsService. (XCUT-4)
            var tempPath = _dataFilePath + ".tmp";
            File.WriteAllText(tempPath, jsonContent);
            if (File.Exists(_dataFilePath))
                File.Replace(tempPath, _dataFilePath, null);
            else
                File.Move(tempPath, _dataFilePath);

            _logger.LogInformation("Saved chapter lists to {FilePath}", _dataFilePath);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error saving chapter lists to file");
        }
    }

    /// <summary>
    /// Clears all chapter lists (useful for testing)
    /// </summary>
    public void ClearAll()
    {
        lock (_lock)
        {
            _chapterLists.Clear();
            SaveToFile();
        }
    }
    
    /// <summary>
    /// Gets the XML directory from settings or returns null if not set
    /// </summary>
    private string? GetXmlDirectory()
    {
        if (_xmlDirectoryOverride != null)
            return Directory.Exists(_xmlDirectoryOverride) ? _xmlDirectoryOverride : null;

        // Use the XML directory from settings if available
        if (_settingsService?.Settings?.XmlBooksDirectory is string xmlDir && !string.IsNullOrEmpty(xmlDir))
        {
            // Validate that the directory exists and contains at least one book file
            if (Directory.Exists(xmlDir))
            {
                // Check for the first book file (s0101m.mul.xml)
                var testFile = Path.Combine(xmlDir, "s0101m.mul.xml");
                if (File.Exists(testFile))
                {
                    return xmlDir;
                }
                else
                {
                    _logger.LogWarning("XML directory '{Directory}' does not contain expected book file s0101m.mul.xml", xmlDir);
                }
            }
            else
            {
                _logger.LogWarning("XML directory '{Directory}' does not exist", xmlDir);
            }
        }
        
        // No valid XML directory configured
        _logger.LogWarning("No valid XML directory configured in settings");
        return null;
    }

    /// <summary>The cache file. <see cref="Format"/> is absent (0) in the format before #1075.</summary>
    private sealed class CacheFile
    {
        public int Format { get; set; }
        public Dictionary<string, Entry>? Books { get; set; }
    }
}
