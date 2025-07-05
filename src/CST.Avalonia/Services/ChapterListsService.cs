using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Xml;
using CST.Avalonia.Models;
using CST;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services;

/// <summary>
/// Service for managing chapter lists data.
/// Port of CST4's ChapterLists class with JSON storage and async support.
/// </summary>
public class ChapterListsService
{
    private readonly ILogger<ChapterListsService> _logger;
    private readonly string _dataFilePath;
    private Dictionary<int, List<DivTag>> _chapterLists;

    public ChapterListsService(ILogger<ChapterListsService> logger)
    {
        _logger = logger;
        _dataFilePath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), 
                                    "CST", "chapter_lists.json");
        _chapterLists = new Dictionary<int, List<DivTag>>();
        
        LoadFromFile();
    }

    /// <summary>
    /// Gets the chapter list for a specific book index
    /// </summary>
    public List<DivTag>? GetChapterList(int bookIndex)
    {
        // Debug: Check what the book's ChapterListTypes is set to
        try
        {
            var book = Books.Inst[bookIndex];
            if (book != null)
            {
                _logger.LogInformation("Book {Index} ({FileName}): ChapterListTypes = '{ChapterListTypes}'", 
                                     bookIndex, book.FileName, book.ChapterListTypes ?? "(null)");
                
                // If the book has ChapterListTypes but no generated chapters, try generating them
                if (!string.IsNullOrEmpty(book.ChapterListTypes) && !_chapterLists.ContainsKey(bookIndex))
                {
                    _logger.LogInformation("Book has ChapterListTypes but no generated chapters. Generating now...");
                    var xmlDirectory = "/Users/fsnow/Cloud-Drive/Projects/CST_UnitTestData/Xml";
                    Generate(new List<int> { bookIndex }, xmlDirectory);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error checking book {Index} for chapter data", bookIndex);
        }
        
        return _chapterLists.TryGetValue(bookIndex, out var chapters) ? chapters : null;
    }

    /// <summary>
    /// Generates chapter lists for books that have changed.
    /// Port of CST4's ChapterLists.Generate() method.
    /// </summary>
    public void Generate(List<int> changedBookIndices, string xmlDirectory)
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
                    _chapterLists[bookIndex] = chapterList;
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
            string heading = headElement.InnerText?.Trim() ?? "";
            
            // Strip footnote tags like CST4 does
            heading = Regex.Replace(heading, "<note>(.+?)</note>", "", RegexOptions.IgnoreCase);
            
            return heading;
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
                var data = JsonSerializer.Deserialize<Dictionary<string, List<DivTag>>>(jsonContent);
                
                if (data != null)
                {
                    // Convert string keys back to int keys
                    _chapterLists = data.ToDictionary(
                        kvp => int.Parse(kvp.Key), 
                        kvp => kvp.Value
                    );
                    
                    _logger.LogInformation("Loaded chapter lists for {Count} books", _chapterLists.Count);
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
            _chapterLists = new Dictionary<int, List<DivTag>>();
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
            var data = _chapterLists.ToDictionary(
                kvp => kvp.Key.ToString(), 
                kvp => kvp.Value
            );
            
            var options = new JsonSerializerOptions
            {
                WriteIndented = true
            };
            
            string jsonContent = JsonSerializer.Serialize(data, options);
            File.WriteAllText(_dataFilePath, jsonContent);
            
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
        _chapterLists.Clear();
        SaveToFile();
    }
}