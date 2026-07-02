using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using CST.Lucene;
using Microsoft.Extensions.Logging;
using CST.Avalonia.Constants;
using Lucene.Net.Index;
using Lucene.Net.Store;

namespace CST.Avalonia.Services
{
    public class IndexingService : IIndexingService, IDisposable
    {
        private readonly ILogger<IndexingService> _logger;
        private readonly ISettingsService _settingsService;
        private readonly IXmlFileDatesService _xmlFileDatesService;
        private BookIndexerAsync? _bookIndexer;
        private string _indexDirectory = string.Empty;
        private DirectoryReader? _indexReader;
        private FSDirectory? _directory;
        private string? _directoryPath;
        private readonly object _readerLock = new object();

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
            
            System.IO.Directory.CreateDirectory(_indexDirectory);
            _logger.LogInformation("Index directory created/verified: {IndexDirectory}", _indexDirectory);

            // Note: XmlFileDatesService is now initialized earlier in CheckForXmlUpdatesAsync()
            // to ensure it's available before XmlUpdateService tries to use it

            _logger.LogInformation("IndexingService.InitializeAsync() completed successfully");
        }

        public Task<bool> IsIndexValidAsync()
        {
            // Validity means the index is actually readable, not merely that segment files exist. A torn or
            // half-written index has files but throws on open; the old file-existence check reported it valid,
            // so indexing was skipped and every search then failed with no recovery. (SRCH-11)
            try
            {
                if (!System.IO.Directory.Exists(_indexDirectory))
                    return Task.FromResult(false);

                using var dir = FSDirectory.Open(_indexDirectory);
                if (!DirectoryReader.IndexExists(dir))
                {
                    _logger.LogInformation("No readable index in {IndexDirectory}", _indexDirectory);
                    return Task.FromResult(false);
                }

                // A cheap open confirms readability; a corrupt index throws here.
                using var reader = DirectoryReader.Open(dir);
                _logger.LogInformation("Index is present and readable ({Count} docs)", reader.NumDocs);
                return Task.FromResult(true);
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Index in {IndexDirectory} is missing or corrupt", _indexDirectory);
                return Task.FromResult(false);
            }
        }

        // True if the index directory contains any files (so an invalid index here means corrupt, not absent).
        private bool IndexDirectoryHasFiles()
            => System.IO.Directory.Exists(_indexDirectory)
               && System.IO.Directory.GetFiles(_indexDirectory).Length > 0;

        // Delete the contents of a corrupt index so it can be rebuilt from scratch. Closes any open reader
        // first so the files aren't held. (SRCH-11)
        private void DeleteCorruptIndex()
        {
            Dispose(); // release _indexReader / _directory handles
            if (!System.IO.Directory.Exists(_indexDirectory))
                return;

            foreach (var file in System.IO.Directory.GetFiles(_indexDirectory))
            {
                try { System.IO.File.Delete(file); }
                catch (Exception ex) { _logger.LogWarning(ex, "Could not delete corrupt index file {File}", file); }
            }
        }

        public async Task BuildIndexAsync(IProgress<IndexingProgress> progress)
        {
            try
            {
                _logger.LogInformation("BuildIndexAsync() started");

                // Recover from a corrupt/torn index: if index files are present but not readable, delete the
                // index and reset the file-dates cache so every book is re-indexed from scratch — rather than
                // skipping indexing (because "files exist") and leaving every search to throw. (SRCH-11)
                var indexValid = await IsIndexValidAsync();
                if (!indexValid && IndexDirectoryHasFiles())
                {
                    _logger.LogWarning("Search index is present but not readable (corrupt) - deleting and rebuilding from scratch");
                    DeleteCorruptIndex();
                    _xmlFileDatesService.ResetFileDates();
                }

                // Get changed books (all books on first run, or after a corrupt-index reset)
                _logger.LogInformation("Getting changed books from XmlFileDatesService...");
                var changedBooks = await _xmlFileDatesService.GetChangedBooksAsync();
                _logger.LogInformation("Found {Count} changed books to index", changedBooks.Count);

                if (changedBooks.Count == 0 && indexValid)
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

            if (string.IsNullOrEmpty(xmlDirectory) || !System.IO.Directory.Exists(xmlDirectory))
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

            // Indexing succeeded (IndexAllAsync did not throw) → commit the detected file timestamps for
            // these books so SaveFileDatesAsync persists them. If it had thrown, nothing is committed and
            // the books are re-detected as changed next run. (SRCH-1)
            _xmlFileDatesService.MarkBooksIndexed(changedBooks);

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
                    AppConstants.AppDataDirectoryName, "index");
            }
            else if (OperatingSystem.IsMacOS())
            {
                indexPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    "Library", "Application Support", AppConstants.AppDataDirectoryName, "index");
            }
            else // Linux and others
            {
                indexPath = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    ".config", AppConstants.AppDataDirectoryName, "index");
            }

            return indexPath;
        }

        // Returns a reference-counted DirectoryReader for highlighting; the caller MUST release it with
        // DecRef() in a finally. Refreshes when the index has changed (IsCurrent), so a mid-session
        // re-index doesn't leave highlighting reading stale term vectors against new-generation docIds.
        // Reference counting lets the refresh open a fresh reader without disposing one an in-flight
        // caller is still enumerating. Mirrors SearchService's reader (SRCH-2). (SRCH-6)
        public DirectoryReader? GetIndexReader()
        {
            lock (_readerLock)
            {
                try
                {
                    if (_indexReader == null || !_indexReader.IsCurrent())
                    {
                        if (!System.IO.Directory.Exists(_indexDirectory))
                        {
                            _logger.LogWarning("Index directory does not exist: {IndexDirectory}", _indexDirectory);
                            return null;
                        }

                        // Reuse a single FSDirectory; only (re)open it when missing or the index path
                        // changes (a new directory per call without disposing leaked native handles).
                        if (_directory == null || _directoryPath != _indexDirectory)
                        {
                            _directory?.Dispose();
                            _directory = FSDirectory.Open(_indexDirectory);
                            _directoryPath = _indexDirectory;
                        }

                        var newReader = DirectoryReader.Open(_directory);
                        _indexReader?.DecRef();   // release the owner ref; closes once in-flight callers DecRef too
                        _indexReader = newReader;
                        _logger.LogDebug("Opened fresh index reader for highlighting ({DocCount} docs)", _indexReader.NumDocs);
                    }
                    _indexReader.IncRef();   // hand out a counted reference; caller DecRefs in a finally
                    return _indexReader;
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to get index reader");
                    return null;
                }
            }
        }

        public void Dispose()
        {
            lock (_readerLock)
            {
                _indexReader?.DecRef();   // release the owner ref (closes once any in-flight callers DecRef) - SRCH-6
                _indexReader = null;
                _directory?.Dispose();
                _directory = null;
            }
        }
    }
}