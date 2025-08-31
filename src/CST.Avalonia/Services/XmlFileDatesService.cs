using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using CST;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services
{
    public class XmlFileDatesService : IXmlFileDatesService
    {
        private readonly ILogger<XmlFileDatesService> _logger;
        private readonly ISettingsService _settingsService;
        private Dictionary<string, DateTime> _fileDates = new();
        private FileDatesWithCommits? _fileDatesData;
        private string _fileDatesPath = string.Empty;

        public XmlFileDatesService(ILogger<XmlFileDatesService> logger, ISettingsService settingsService)
        {
            _logger = logger;
            _settingsService = settingsService;
        }

        public async Task InitializeAsync()
        {
            _logger.LogInformation("XmlFileDatesService.InitializeAsync() started");
            
            // Get the application data directory
            var appDataDir = GetAppDataDirectory();
            _fileDatesPath = Path.Combine(appDataDir, "file-dates.json");
            _logger.LogInformation("File dates cache path: {FileDatesPath}", _fileDatesPath);

            // Load existing file dates if they exist
            if (File.Exists(_fileDatesPath))
            {
                try
                {
                    _logger.LogInformation("Loading existing file dates cache...");
                    var json = await File.ReadAllTextAsync(_fileDatesPath);
                    
                    // Try to deserialize as new format first
                    try
                    {
                        _fileDatesData = JsonSerializer.Deserialize<FileDatesWithCommits>(json);
                        if (_fileDatesData?.Files != null)
                        {
                            // Convert to legacy format for compatibility
                            _fileDates = _fileDatesData.Files.ToDictionary(
                                kvp => kvp.Key, 
                                kvp => kvp.Value.LastIndexedTimestamp
                            );
                            _logger.LogInformation("Loaded {Count} file dates from enhanced cache format", _fileDates.Count);
                        }
                        else
                        {
                            throw new JsonException("Invalid enhanced format");
                        }
                    }
                    catch
                    {
                        // Fall back to legacy format
                        _fileDates = JsonSerializer.Deserialize<Dictionary<string, DateTime>>(json) ?? new();
                        _logger.LogInformation("Loaded {Count} file dates from legacy cache format", _fileDates.Count);
                        
                        // Convert legacy format to new format
                        _fileDatesData = new FileDatesWithCommits
                        {
                            Files = _fileDates.ToDictionary(
                                kvp => kvp.Key,
                                kvp => new FileCommitInfo
                                {
                                    LastIndexedTimestamp = kvp.Value,
                                    CommitHash = "" // No commit hash available from legacy format
                                }
                            )
                        };
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to load file dates cache");
                    _fileDates = new();
                    _fileDatesData = new FileDatesWithCommits();
                }
            }
            else
            {
                _logger.LogInformation("No existing file dates cache found - will create new one");
                _fileDates = new();
                _fileDatesData = new FileDatesWithCommits();
            }
            
            _logger.LogInformation("XmlFileDatesService.InitializeAsync() completed");
        }

        public virtual async Task<List<int>> GetChangedBooksAsync()
        {
            _logger.LogInformation("GetChangedBooksAsync() started");
            var changedBooks = new List<int>();
            var xmlDirectory = _settingsService.Settings.XmlBooksDirectory;
            _logger.LogInformation("XML books directory from settings: {XmlDirectory}", xmlDirectory);

            if (string.IsNullOrEmpty(xmlDirectory) || !Directory.Exists(xmlDirectory))
            {
                _logger.LogWarning("XML directory not found or not configured: {XmlDirectory}", xmlDirectory);
                return changedBooks;
            }

            var books = Books.Inst;
            _logger.LogInformation("Total books to check: {BookCount}", books.Count);
            
            for (int i = 0; i < books.Count; i++)
            {
                var book = books[i];
                var xmlPath = Path.Combine(xmlDirectory, book.FileName);
                
                if (File.Exists(xmlPath))
                {
                    var lastWriteTime = File.GetLastWriteTimeUtc(xmlPath);
                    
                    // Check if file has changed
                    if (_fileDates.TryGetValue(book.FileName, out var cachedTime))
                    {
                        if (lastWriteTime > cachedTime)
                        {
                            changedBooks.Add(i);
                            _logger.LogInformation("Book {BookIndex} ({FileName}) has been modified", i, book.FileName);
                        }
                    }
                    else
                    {
                        // File not in cache, needs indexing
                        changedBooks.Add(i);
                        _logger.LogInformation("Book {BookIndex} ({FileName}) not in cache, needs indexing", i, book.FileName);
                    }
                    
                    // Update the cache with current time
                    _fileDates[book.FileName] = lastWriteTime;
                }
                else
                {
                    _logger.LogWarning("XML file not found: {XmlPath}", xmlPath);
                }
            }

            _logger.LogInformation("GetChangedBooksAsync() completed - found {Count} changed books", changedBooks.Count);
            return changedBooks;
        }

        public async Task SaveFileDatesAsync()
        {
            try
            {
                var appDataDir = GetAppDataDirectory();
                Directory.CreateDirectory(appDataDir); // Ensure directory exists

                // Update the enhanced format with current file dates
                if (_fileDatesData != null)
                {
                    foreach (var kvp in _fileDates)
                    {
                        if (_fileDatesData.Files.TryGetValue(kvp.Key, out var fileInfo))
                        {
                            // Update existing entry's timestamp
                            fileInfo.LastIndexedTimestamp = kvp.Value;
                        }
                        else
                        {
                            // Add new entry with empty commit hash
                            _fileDatesData.Files[kvp.Key] = new FileCommitInfo
                            {
                                LastIndexedTimestamp = kvp.Value,
                                CommitHash = ""
                            };
                        }
                    }
                }
                else
                {
                    // Create new enhanced format
                    _fileDatesData = new FileDatesWithCommits
                    {
                        Files = _fileDates.ToDictionary(
                            kvp => kvp.Key,
                            kvp => new FileCommitInfo
                            {
                                LastIndexedTimestamp = kvp.Value,
                                CommitHash = ""
                            }
                        )
                    };
                }

                var json = JsonSerializer.Serialize(_fileDatesData, new JsonSerializerOptions 
                { 
                    WriteIndented = true 
                });
                
                await File.WriteAllTextAsync(_fileDatesPath, json);
                _logger.LogInformation($"Saved {_fileDates.Count} file dates to enhanced cache format");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save file dates cache");
            }
        }

        public void UpdateFileDate(int bookIndex, DateTime lastWriteTime)
        {
            var books = Books.Inst;
            if (bookIndex >= 0 && bookIndex < books.Count)
            {
                var book = books[bookIndex];
                _fileDates[book.FileName] = lastWriteTime;
            }
        }
        
        // Enhanced format methods for XmlUpdateService
        public async Task<FileDatesWithCommits?> GetFileDatesDataAsync()
        {
            return _fileDatesData;
        }
        
        public async Task SaveFileDatesDataAsync(Dictionary<string, FileCommitInfo> files, string? repositoryCommitHash)
        {
            try
            {
                var appDataDir = GetAppDataDirectory();
                Directory.CreateDirectory(appDataDir);
                
                _fileDatesData = new FileDatesWithCommits
                {
                    LastKnownRepositoryCommitHash = repositoryCommitHash,
                    Files = files
                };
                
                // Update legacy format for compatibility
                _fileDates = files.ToDictionary(
                    kvp => kvp.Key,
                    kvp => kvp.Value.LastIndexedTimestamp
                );
                
                var json = JsonSerializer.Serialize(_fileDatesData, new JsonSerializerOptions
                {
                    WriteIndented = true
                });
                
                await File.WriteAllTextAsync(_fileDatesPath, json);
                _logger.LogInformation("Saved {Count} file dates with commit hashes", files.Count);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to save file dates data");
            }
        }

        protected virtual string GetAppDataDirectory()
        {
            string appDataPath;
            
            if (OperatingSystem.IsWindows())
            {
                appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CST.Avalonia");
            }
            else if (OperatingSystem.IsMacOS())
            {
                appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "CST.Avalonia");
            }
            else // Linux and others
            {
                appDataPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config", "CST.Avalonia");
            }

            return appDataPath;
        }
    }
    
    // Data models for enhanced file-dates.json format
    public class FileDatesWithCommits
    {
        public string? LastKnownRepositoryCommitHash { get; set; }
        public Dictionary<string, FileCommitInfo> Files { get; set; } = new();
    }

    public class FileCommitInfo
    {
        public DateTime LastIndexedTimestamp { get; set; }
        public string CommitHash { get; set; } = string.Empty;
    }
}