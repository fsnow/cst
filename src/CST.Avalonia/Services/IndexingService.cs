using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CST.Lucene;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services
{
    public class IndexingService : IIndexingService
    {
        private readonly ILogger<IndexingService> _logger;
        private readonly ISettingsService _settingsService;
        private readonly IXmlFileDatesService _xmlFileDatesService;
        private BookIndexerAsync? _bookIndexer;
        private string _indexDirectory = string.Empty;

        public string IndexDirectory => _indexDirectory;

        public IndexingService(
            ILogger<IndexingService> logger,
            ISettingsService settingsService,
            IXmlFileDatesService xmlFileDatesService)
        {
            _logger = logger;
            _settingsService = settingsService;
            _xmlFileDatesService = xmlFileDatesService;
        }

        public async Task InitializeAsync()
        {
            _logger.LogInformation("IndexingService.InitializeAsync() started");
            
            // Setup index directory from settings or use default
            var configuredIndexDir = _settingsService.Settings.IndexDirectory;
            if (!string.IsNullOrEmpty(configuredIndexDir))
            {
                _indexDirectory = configuredIndexDir;
                _logger.LogInformation("Using configured index directory: {IndexDirectory}", _indexDirectory);
            }
            else
            {
                _indexDirectory = GetDefaultIndexDirectory();
                _logger.LogInformation("Using default index directory: {IndexDirectory}", _indexDirectory);
                
                // Save the default index directory to settings so user can see/modify it
                _settingsService.Settings.IndexDirectory = _indexDirectory;
                await _settingsService.SaveSettingsAsync();
                _logger.LogInformation("Saved default index directory to settings");
            }
            
            Directory.CreateDirectory(_indexDirectory);
            _logger.LogInformation("Index directory created/verified: {IndexDirectory}", _indexDirectory);

            // Initialize the XML file dates service
            _logger.LogInformation("Initializing XmlFileDatesService...");
            await _xmlFileDatesService.InitializeAsync();
            _logger.LogInformation("XmlFileDatesService initialized");

            _logger.LogInformation("IndexingService.InitializeAsync() completed successfully");
        }

        public async Task<bool> IsIndexValidAsync()
        {
            try
            {
                _logger.LogInformation("Checking index validity in directory: {IndexDirectory}", _indexDirectory);
                
                if (!Directory.Exists(_indexDirectory))
                {
                    _logger.LogInformation("Index directory does not exist, index is invalid");
                    return false;
                }

                var indexFiles = Directory.GetFiles(_indexDirectory, "*.cfs");
                _logger.LogInformation("Found {Count} .cfs files", indexFiles.Length);
                
                if (indexFiles.Length == 0)
                {
                    indexFiles = Directory.GetFiles(_indexDirectory, "*.fdt");
                    _logger.LogInformation("Found {Count} .fdt files", indexFiles.Length);
                }

                bool isValid = indexFiles.Length > 0;
                _logger.LogInformation("Index validity check result: {IsValid}", isValid);
                return isValid;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking index validity");
                return false;
            }
        }

        public async Task BuildIndexAsync(IProgress<IndexingProgress> progress)
        {
            try
            {
                _logger.LogInformation("BuildIndexAsync() started");

                // Get changed books (all books on first run)
                _logger.LogInformation("Getting changed books from XmlFileDatesService...");
                var changedBooks = await _xmlFileDatesService.GetChangedBooksAsync();
                _logger.LogInformation("Found {Count} changed books to index", changedBooks.Count);

                if (changedBooks.Count == 0 && await IsIndexValidAsync())
                {
                    _logger.LogInformation("No changed books and index is valid - index is up to date");
                    progress?.Report(new IndexingProgress
                    {
                        StatusMessage = "Index is up to date",
                        IsComplete = true
                    });
                    return;
                }

                _logger.LogInformation("Starting indexing process for {Count} books", changedBooks.Count);
                await PerformIndexingAsync(changedBooks, progress);

                // Save file dates after successful indexing
                _logger.LogInformation("Saving file dates after successful indexing...");
                await _xmlFileDatesService.SaveFileDatesAsync();
                _logger.LogInformation("File dates saved");

                _logger.LogInformation("BuildIndexAsync() completed successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error building index");
                throw;
            }
        }

        public async Task UpdateIndexAsync(List<int> changedBooks, IProgress<IndexingProgress> progress)
        {
            try
            {
                _logger.LogInformation($"Updating index for {changedBooks.Count} changed books");

                await PerformIndexingAsync(changedBooks, progress);

                // Save file dates after successful indexing
                await _xmlFileDatesService.SaveFileDatesAsync();

                _logger.LogInformation("Index update completed");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error updating index");
                throw;
            }
        }

        public Task OptimizeIndexAsync()
        {
            // The modern Lucene.NET IndexWriter handles optimization automatically
            // during commit, so this is mostly a no-op now
            _logger.LogInformation("Index optimization requested (handled automatically by Lucene)");
            return Task.CompletedTask;
        }

        private async Task PerformIndexingAsync(List<int> changedBooks, IProgress<IndexingProgress>? progress)
        {
            var xmlDirectory = _settingsService.Settings.XmlBooksDirectory;

            if (string.IsNullOrEmpty(xmlDirectory) || !Directory.Exists(xmlDirectory))
            {
                throw new InvalidOperationException($"XML directory not found: {xmlDirectory}");
            }

            _bookIndexer = new BookIndexerAsync
            {
                XmlDirectory = xmlDirectory,
                IndexDirectory = _indexDirectory
            };

            progress?.Report(new IndexingProgress
            {
                StatusMessage = "Starting indexing...",
                CurrentBook = 0,
                TotalBooks = changedBooks.Count
            });

            await _bookIndexer.IndexAllAsync(progress, changedBooks);

            progress?.Report(new IndexingProgress
            {
                StatusMessage = "Indexing complete",
                IsComplete = true,
                CurrentBook = changedBooks.Count,
                TotalBooks = changedBooks.Count
            });
        }

        private string GetDefaultIndexDirectory()
        {
            string indexPath;

            if (OperatingSystem.IsWindows())
            {
                indexPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CST.Avalonia", "Index");
            }
            else if (OperatingSystem.IsMacOS())
            {
                indexPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", "CST.Avalonia", "Index");
            }
            else // Linux and others
            {
                indexPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config", "CST.Avalonia", "Index");
            }

            return indexPath;
        }
    }
}