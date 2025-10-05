using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Threading;
using CST;
using CST.Avalonia.Models;
using CST.Avalonia.Constants;
using CST.Avalonia.Views;
using CST.Lucene;
using Microsoft.Extensions.Logging;
using Octokit;

namespace CST.Avalonia.Services
{
    public class XmlUpdateService : IXmlUpdateService
    {
        private readonly ILogger<XmlUpdateService> _logger;
        private readonly ISettingsService _settingsService;
        private readonly IIndexingService _indexingService;
        private readonly IXmlFileDatesService _xmlFileDatesService;
        
        private bool _isCheckingForUpdates;
        private bool _isDownloading;
        private GitHubClient? _gitHubClient;
        private HttpClient? _httpClient;

        public event Action<string>? UpdateStatusChanged;
        public event Action<int, int>? DownloadProgressChanged;

        public bool IsCheckingForUpdates => _isCheckingForUpdates;
        public bool IsDownloading => _isDownloading;

        public XmlUpdateService(
            ILogger<XmlUpdateService> logger,
            ISettingsService settingsService,
            IIndexingService indexingService,
            IXmlFileDatesService xmlFileDatesService)
        {
            _logger = logger;
            _settingsService = settingsService;
            _indexingService = indexingService;
            _xmlFileDatesService = xmlFileDatesService;
            
            // Initialize GitHub client
            _gitHubClient = new GitHubClient(new ProductHeaderValue(AppConstants.UserAgent));

            // Initialize HTTP client for direct downloads with timeout
            _httpClient = new HttpClient();
            _httpClient.Timeout = TimeSpan.FromSeconds(30); // 30 second timeout for network requests
            _httpClient.DefaultRequestHeaders.Add("User-Agent", AppConstants.UserAgent);
        }

        public async Task CheckForUpdatesAsync()
        {
            if (_isCheckingForUpdates || _isDownloading)
            {
                _logger.LogWarning("Update check already in progress");
                return;
            }

            try
            {
                _isCheckingForUpdates = true;
                
                var settings = _settingsService.Settings;
                var xmlDir = settings.XmlBooksDirectory;
                
                // Step 1: Check if XML data exists locally
                bool hasLocalData = CheckForLocalXmlData(xmlDir);
                
                if (!hasLocalData)
                {
                    _logger.LogInformation("No local XML data found - starting initial download");
                    UpdateStatusChanged?.Invoke("No XML data found - downloading Tipitaka files...");
                    SplashScreen.SetStatus("Downloading Tipitaka XML files for first time setup...");

                    // Automatically download the XML files on first run
                    await PerformInitialDownloadAsync();
                    return;
                }
                
                // Step 2: Check if automatic updates are enabled
                if (!settings.XmlUpdateSettings.EnableAutomaticUpdates)
                {
                    _logger.LogInformation("Automatic updates are disabled");
                    return;
                }
                
                // Step 3: File dates are managed by XmlFileDatesService
                // No need to load separately
                
                // Step 4: Check for updates from GitHub
                await CheckGitHubForUpdatesAsync();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking for XML updates");
                UpdateStatusChanged?.Invoke($"Error checking for updates: {ex.Message}");
            }
            finally
            {
                _isCheckingForUpdates = false;
            }
        }

        private bool CheckForLocalXmlData(string xmlDir)
        {
            if (string.IsNullOrEmpty(xmlDir) || !Directory.Exists(xmlDir))
                return false;
            
            // Check for the first book file in the Books collection
            if (Books.Inst.Count() > 0)
            {
                var firstBook = Books.Inst.First();
                var firstBookPath = Path.Combine(xmlDir, firstBook.FileName);
                return File.Exists(firstBookPath);
            }
            
            return false;
        }

        private async Task<bool> PromptForInitialDataAsync()
        {
            // Check if main window is available (not during startup)
            if (App.MainWindow == null)
            {
                _logger.LogInformation("Skipping initial download prompt - main window not ready");
                return false;
            }

            // This will be called on the UI thread
            var result = false;

            await Dispatcher.UIThread.InvokeAsync(async () =>
            {
                var window = App.MainWindow;
                if (window == null) return;
                
                var dialog = new Window
                {
                    Title = "Download Tipitaka Data",
                    Width = 500,
                    Height = 200,
                    WindowStartupLocation = WindowStartupLocation.CenterOwner
                };
                
                var panel = new StackPanel
                {
                    Margin = new Thickness(20)
                };
                
                panel.Children.Add(new TextBlock
                {
                    Text = "No Tipitaka XML data found. Would you like to download it now?",
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 0, 0, 10)
                });
                
                panel.Children.Add(new TextBlock
                {
                    Text = $"This will download {Books.Inst.Count()} Tipitaka book files from GitHub.",
                    FontSize = 12,
                    Foreground = Brushes.Gray,
                    Margin = new Thickness(0, 0, 0, 20)
                });
                
                var buttonPanel = new StackPanel
                {
                    Orientation = Orientation.Horizontal,
                    HorizontalAlignment = HorizontalAlignment.Center
                };
                
                var downloadButton = new Button
                {
                    Content = "Download Data",
                    Margin = new Thickness(0, 0, 10, 0),
                    IsDefault = true
                };
                
                var browseButton = new Button
                {
                    Content = "Browse for Existing Data",
                    Margin = new Thickness(0, 0, 10, 0)
                };
                
                var cancelButton = new Button
                {
                    Content = "Cancel",
                    IsCancel = true
                };
                
                downloadButton.Click += (s, e) =>
                {
                    result = true;
                    dialog.Close();
                };
                
                browseButton.Click += async (s, e) =>
                {
                    var folderDialog = await window.StorageProvider.OpenFolderPickerAsync(new global::Avalonia.Platform.Storage.FolderPickerOpenOptions
                    {
                        Title = "Select XML Data Directory",
                        AllowMultiple = false
                    });
                    
                    if (folderDialog != null && folderDialog.Any())
                    {
                        var selectedPath = folderDialog[0].Path.LocalPath;
                        _settingsService.Settings.XmlBooksDirectory = selectedPath;
                        await _settingsService.SaveSettingsAsync();
                        result = false; // Don't download, user provided existing data
                        dialog.Close();
                    }
                };
                
                cancelButton.Click += (s, e) =>
                {
                    result = false;
                    dialog.Close();
                };
                
                buttonPanel.Children.Add(downloadButton);
                buttonPanel.Children.Add(browseButton);
                buttonPanel.Children.Add(cancelButton);
                
                panel.Children.Add(buttonPanel);
                dialog.Content = panel;
                
                await dialog.ShowDialog(window);
            });
            
            return result;
        }

        private async Task PerformInitialDownloadAsync()
        {
            try
            {
                _isDownloading = true;
                UpdateStatusChanged?.Invoke("Starting initial download of Tipitaka XML data...");
                
                var settings = _settingsService.Settings.XmlUpdateSettings;
                var owner = settings.XmlRepositoryOwner;
                var repo = settings.XmlRepositoryName;
                var targetPath = settings.XmlRepositoryPath;
                var branch = string.IsNullOrEmpty(settings.XmlRepositoryBranch) ? "main" : settings.XmlRepositoryBranch;
                
                _logger.LogInformation("Using hybrid approach for initial download");
                
                // STEP 1: Get latest commit SHA (1 API call) with timeout
                UpdateStatusChanged?.Invoke("Getting repository information...");

                var branchTask = _gitHubClient!.Repository.Branch.Get(owner, repo, branch);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15));
                var completedTask = await Task.WhenAny(branchTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _logger.LogWarning("GitHub API call timed out after 15 seconds");
                    UpdateStatusChanged?.Invoke("Network request timed out - skipping updates");
                    return;
                }

                var branchInfo = await branchTask;
                var commitSha = branchInfo.Commit.Sha;
                _logger.LogInformation("Latest commit SHA: {CommitSha}", commitSha.Substring(0, 7));
                
                // STEP 2: Get complete repository tree (1 API call) with timeout
                UpdateStatusChanged?.Invoke("Fetching repository tree...");

                var treeTask = _gitHubClient!.Git.Tree.GetRecursive(owner, repo, commitSha);
                var treeTimeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
                var treeCompletedTask = await Task.WhenAny(treeTask, treeTimeoutTask);

                if (treeCompletedTask == treeTimeoutTask)
                {
                    _logger.LogWarning("GitHub tree API call timed out after 30 seconds");
                    UpdateStatusChanged?.Invoke("Network request timed out - skipping updates");
                    return;
                }

                var tree = await treeTask;
                _logger.LogInformation("Tree retrieved with {TotalItems} total items", tree.Tree.Count);
                
                // Filter to target path XML files
                var targetFiles = tree.Tree
                    .Where(item => item.Type == TreeType.Blob && 
                                   item.Path.StartsWith(targetPath + "/") &&
                                   item.Path.EndsWith(".xml"))
                    .ToList();
                
                _logger.LogInformation("Found {XmlFileCount} XML files in '{TargetPath}'", targetFiles.Count, targetPath);
                
                // Create a dictionary for quick lookup of repository files by name
                var repoFilesByName = targetFiles.ToDictionary(f => Path.GetFileName(f.Path), f => f);
                
                // Get only the book files we need from the Books collection
                var bookFiles = new List<TreeItem>();
                foreach (var book in Books.Inst)
                {
                    if (repoFilesByName.TryGetValue(book.FileName, out var repoFile))
                    {
                        bookFiles.Add(repoFile);
                    }
                    else
                    {
                        _logger.LogWarning("Book file {FileName} not found in repository", book.FileName);
                    }
                }
                
                _logger.LogInformation("Found {Count} XML files to download", bookFiles.Count);
                UpdateStatusChanged?.Invoke($"Found {bookFiles.Count} XML files to download");
                
                // Create XML directory if it doesn't exist
                var xmlDir = _settingsService.Settings.XmlBooksDirectory;
                if (string.IsNullOrEmpty(xmlDir))
                {
                    // Use default location
                    xmlDir = Path.Combine(GetAppDataDirectory(), "xml-data");
                    _settingsService.Settings.XmlBooksDirectory = xmlDir;
                    await _settingsService.SaveSettingsAsync();
                }
                
                Directory.CreateDirectory(xmlDir);
                
                // STEP 3: Download each file via direct HTTPS (no API calls)
                var downloadedFiles = new Dictionary<string, FileCommitInfo>();
                var tempDir = Path.Combine(Path.GetTempPath(), $"cst-download-{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDir);
                
                try
                {
                    for (int i = 0; i < bookFiles.Count; i++)
                    {
                        var file = bookFiles[i];
                        var fileName = Path.GetFileName(file.Path);
                        UpdateStatusChanged?.Invoke($"Downloading {fileName} ({i + 1}/{bookFiles.Count})...");
                        DownloadProgressChanged?.Invoke(i + 1, bookFiles.Count);
                        
                        // Construct direct download URL (no API, no rate limit)
                        var downloadUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{Uri.EscapeDataString(file.Path)}";
                        _logger.LogDebug("Downloading {FileName} from {Url}", fileName, downloadUrl);
                        
                        var response = await _httpClient!.GetAsync(downloadUrl);
                        if (response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsByteArrayAsync();
                            var tempPath = Path.Combine(tempDir, fileName);
                            await File.WriteAllBytesAsync(tempPath, content);
                            
                            downloadedFiles[fileName] = new FileCommitInfo
                            {
                                LastIndexedTimestamp = null,  // null = needs indexing after download
                                CommitHash = file.Sha
                            };
                            
                            _logger.LogDebug("Downloaded {FileName} successfully ({Size:N0} bytes, SHA: {Sha})", 
                                fileName, content.Length, file.Sha.Substring(0, 7));
                        }
                        else
                        {
                            _logger.LogError("Failed to download {FileName}: {StatusCode}", fileName, response.StatusCode);
                            throw new HttpRequestException($"Failed to download {fileName}: {response.StatusCode}");
                        }
                    }
                    
                    // Move all files to final location
                    UpdateStatusChanged?.Invoke("Moving files to final location...");
                    foreach (var file in Directory.GetFiles(tempDir))
                    {
                        var fileName = Path.GetFileName(file);
                        var destPath = Path.Combine(xmlDir, fileName);
                        File.Move(file, destPath, overwrite: true);
                    }
                    
                    // Save file dates with commit hashes
                    await _xmlFileDatesService.SaveFileDatesDataAsync(downloadedFiles, commitSha);
                    
                    // Trigger initial indexing
                    UpdateStatusChanged?.Invoke("Indexing downloaded files...");
                    var progress = new Progress<IndexingProgress>();
                    await _indexingService.BuildIndexAsync(progress);
                    
                    UpdateStatusChanged?.Invoke($"Successfully downloaded and indexed {bookFiles.Count} files");
                    _logger.LogInformation("Initial download completed successfully. API calls used: 2 total. Direct downloads: {Count}", bookFiles.Count);
                }
                finally
                {
                    // Cleanup temp directory
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during initial download");
                UpdateStatusChanged?.Invoke($"Error downloading data: {ex.Message}");
                throw;
            }
            finally
            {
                _isDownloading = false;
            }
        }

        private async Task CheckGitHubForUpdatesAsync()
        {
            try
            {
                var settings = _settingsService.Settings.XmlUpdateSettings;
                var owner = settings.XmlRepositoryOwner;
                var repo = settings.XmlRepositoryName;
                var targetPath = settings.XmlRepositoryPath;
                var branch = string.IsNullOrEmpty(settings.XmlRepositoryBranch) ? "main" : settings.XmlRepositoryBranch;
                
                _logger.LogInformation("Using hybrid approach: minimal API calls + direct downloads");
                
                // STEP 1: Get latest commit SHA (1 API call) with timeout
                UpdateStatusChanged?.Invoke("Getting repository information...");

                var branchTask = _gitHubClient!.Repository.Branch.Get(owner, repo, branch);
                var timeoutTask = Task.Delay(TimeSpan.FromSeconds(15));
                var completedTask = await Task.WhenAny(branchTask, timeoutTask);

                if (completedTask == timeoutTask)
                {
                    _logger.LogWarning("GitHub API call timed out after 15 seconds");
                    UpdateStatusChanged?.Invoke("Network request timed out - skipping updates");
                    return;
                }

                var branchInfo = await branchTask;
                var commitSha = branchInfo.Commit.Sha;
                _logger.LogInformation("Latest commit SHA: {CommitSha}", commitSha.Substring(0, 7));
                
                // Get current file dates data
                var fileDatesData = await _xmlFileDatesService.GetFileDatesDataAsync();
                
                // Check if we've already synced this commit
                if (fileDatesData?.LastKnownRepositoryCommitHash == commitSha)
                {
                    _logger.LogInformation("Already up to date with commit {Hash}", commitSha.Substring(0, 7));
                    UpdateStatusChanged?.Invoke("XML data is up to date");
                    return;
                }
                
                // STEP 2: Get complete repository tree (1 API call) with timeout
                UpdateStatusChanged?.Invoke("Fetching repository tree...");

                var treeTask = _gitHubClient!.Git.Tree.GetRecursive(owner, repo, commitSha);
                var treeTimeoutTask = Task.Delay(TimeSpan.FromSeconds(30));
                var treeCompletedTask = await Task.WhenAny(treeTask, treeTimeoutTask);

                if (treeCompletedTask == treeTimeoutTask)
                {
                    _logger.LogWarning("GitHub tree API call timed out after 30 seconds");
                    UpdateStatusChanged?.Invoke("Network request timed out - skipping updates");
                    return;
                }

                var tree = await treeTask;
                _logger.LogInformation("Tree retrieved with {TotalItems} total items", tree.Tree.Count);
                
                // Filter to target path XML files
                var targetFiles = tree.Tree
                    .Where(item => item.Type == TreeType.Blob && 
                                   item.Path.StartsWith(targetPath + "/") &&
                                   item.Path.EndsWith(".xml"))
                    .ToList();
                
                _logger.LogInformation("Found {XmlFileCount} XML files in '{TargetPath}'", targetFiles.Count, targetPath);
                
                // Create a dictionary for quick lookup of repository files by name
                var repoFilesByName = targetFiles.ToDictionary(f => Path.GetFileName(f.Path), f => f);
                
                // Check only the book files we need from the Books collection
                var filesToUpdate = new List<TreeItem>();
                var xmlDir = _settingsService.Settings.XmlBooksDirectory;
                _logger.LogInformation("XML directory for comparison: {XmlDir}", xmlDir);
                var xmlFileCount = Directory.Exists(xmlDir) ? Directory.GetFiles(xmlDir, "*.xml").Length : 0;
                _logger.LogInformation("Found {Count} XML files in directory", xmlFileCount);
                
                // Check if we need to do hash comparison for missing commit hash tracking data
                // IndexingService may have created file-dates.json with timestamps but no commit hashes
                bool needsHashComparison = fileDatesData?.Files == null || 
                    fileDatesData.Files.Count == 0 ||
                    fileDatesData.Files.Values.Any(f => string.IsNullOrEmpty(f.CommitHash));
                    
                if (needsHashComparison)
                {
                    _logger.LogInformation("No commit hash tracking data found. Will hash-compare existing files to avoid unnecessary downloads.");
                    UpdateStatusChanged?.Invoke("Checking existing files...");
                }
                
                // Track files that are already up-to-date (for saving to file-dates.json)
                var upToDateFiles = new Dictionary<string, FileCommitInfo>();
                int filesChecked = 0, filesFound = 0, shaMatches = 0;
                
                _logger.LogInformation("Processing {Count} books from Books.Inst", Books.Inst.Count());
                
                int booksProcessed = 0, booksNotFound = 0;
                foreach (var book in Books.Inst)
                {
                    booksProcessed++;
                    if (!repoFilesByName.TryGetValue(book.FileName, out var repoFile))
                    {
                        booksNotFound++;
                        if (booksNotFound <= 5) // Only log first 5 to avoid spam
                        {
                            _logger.LogWarning("Book file {FileName} not found in repository", book.FileName);
                        }
                        continue;
                    }
                    
                    bool needsUpdate = false;
                    
                    if (!needsHashComparison && fileDatesData?.Files != null && fileDatesData.Files.TryGetValue(book.FileName, out var localFile))
                    {
                        // We have tracking data with valid commit hashes - use stored SHA
                        needsUpdate = localFile.CommitHash != repoFile.Sha;
                        if (needsUpdate)
                        {
                            _logger.LogDebug("File {Name} needs update (tracked SHA: {LocalSha} vs remote: {RemoteSha})", 
                                book.FileName, localFile.CommitHash, repoFile.Sha.Substring(0, 7));
                        }
                        else
                        {
                            // File is up-to-date, keep it in tracking with existing timestamp
                            upToDateFiles[book.FileName] = localFile;
                        }
                    }
                    else
                    {
                        // No tracking data - check if local file exists
                        var localPath = Path.Combine(xmlDir, book.FileName);
                        filesChecked++;
                        if (File.Exists(localPath))
                        {
                            filesFound++;
                            // Hash-compare existing file against remote SHA
                            _logger.LogDebug("Hash-comparing existing file: {FileName}", book.FileName);
                            var localSha = CalculateGitBlobSha(localPath);
                            needsUpdate = localSha != repoFile.Sha;
                            if (!needsUpdate) shaMatches++;
                            
                            _logger.LogDebug("SHA comparison for {FileName}: Local={LocalSha}, Remote={RemoteSha}, NeedsUpdate={NeedsUpdate}", 
                                book.FileName, localSha.Substring(0, 7), repoFile.Sha.Substring(0, 7), needsUpdate);
                            
                            if (needsUpdate)
                            {
                                _logger.LogDebug("File {Name} needs update (local SHA: {LocalSha} vs remote: {RemoteSha})", 
                                    book.FileName, localSha.Substring(0, 7), repoFile.Sha.Substring(0, 7));
                            }
                            else
                            {
                                _logger.LogDebug("File {Name} is up to date (SHA: {Sha})", 
                                    book.FileName, localSha.Substring(0, 7));
                                // File matches - save it as up-to-date
                                // Set CommitHash, leave LastIndexedTimestamp as null for indexer to manage
                                upToDateFiles[book.FileName] = new FileCommitInfo
                                {
                                    LastIndexedTimestamp = null,  // null = not indexed yet, indexer will set when it runs
                                    CommitHash = repoFile.Sha
                                };
                            }
                        }
                        else
                        {
                            // File doesn't exist locally - needs download
                            needsUpdate = true;
                            _logger.LogWarning("File {Name} not found at {Path}, needs download", book.FileName, localPath);
                        }
                    }
                    
                    if (needsUpdate)
                    {
                        filesToUpdate.Add(repoFile);
                    }
                }
                
                if (filesToUpdate.Count == 0)
                {
                    _logger.LogInformation("No files need updating");
                    UpdateStatusChanged?.Invoke("No files need updating");
                    
                    // If we did hash comparison (no tracking data), rebuild file-dates.json with current SHAs
                    if (needsHashComparison)
                    {
                        _logger.LogInformation("Rebuilding file tracking data from hash comparison results");
                        UpdateStatusChanged?.Invoke("Updating file tracking data...");
                        
                        var rebuiltFiles = new Dictionary<string, FileCommitInfo>();
                        foreach (var book in Books.Inst)
                        {
                            if (repoFilesByName.TryGetValue(book.FileName, out var repoFile))
                            {
                                rebuiltFiles[book.FileName] = new FileCommitInfo
                                {
                                    LastIndexedTimestamp = null,  // Let indexer determine if these need indexing
                                    CommitHash = repoFile.Sha
                                };
                            }
                        }
                        await _xmlFileDatesService.SaveFileDatesDataAsync(rebuiltFiles, commitSha);
                        _logger.LogInformation("Rebuilt tracking data for {Count} files", rebuiltFiles.Count);
                    }
                    else if (fileDatesData != null)
                    {
                        // Update the repository commit hash even if no files changed
                        await _xmlFileDatesService.SaveFileDatesDataAsync(fileDatesData.Files, commitSha);
                    }
                    return;
                }
                
                _logger.LogInformation("Book processing: Processed={Processed}, NotFoundInRepo={NotFound}", booksProcessed, booksNotFound);
                _logger.LogInformation("Hash comparison results: Checked={Checked}, Found={Found}, Matches={Matches}", filesChecked, filesFound, shaMatches);
                _logger.LogInformation("Files needing update: {UpdateCount}/{TotalBooks}", filesToUpdate.Count, Books.Inst.Count());
                _logger.LogInformation("Files already up-to-date: {UpToDateCount}/{TotalBooks}", upToDateFiles.Count, Books.Inst.Count());
                
                // STEP 3: Download updated files via direct HTTPS (no API calls)
                await DownloadUpdatedFilesDirectAsync(filesToUpdate, upToDateFiles, commitSha, owner, repo, branch);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking GitHub for updates");
                UpdateStatusChanged?.Invoke($"Error checking for updates: {ex.Message}");
            }
        }

        private async Task DownloadUpdatedFilesDirectAsync(List<TreeItem> filesToUpdate, Dictionary<string, FileCommitInfo> upToDateFiles, string latestCommitHash, string owner, string repo, string branch)
        {
            try
            {
                _isDownloading = true;
                var xmlDir = _settingsService.Settings.XmlBooksDirectory;
                
                _logger.LogInformation("Downloading {Count} updated files via direct HTTPS", filesToUpdate.Count);
                UpdateStatusChanged?.Invoke($"Downloading {filesToUpdate.Count} updated files...");
                
                var tempDir = Path.Combine(Path.GetTempPath(), $"cst-update-{Guid.NewGuid()}");
                Directory.CreateDirectory(tempDir);
                
                try
                {
                    // Start with the files that are already up-to-date
                    var allFiles = new Dictionary<string, FileCommitInfo>(upToDateFiles);
                    
                    for (int i = 0; i < filesToUpdate.Count; i++)
                    {
                        var file = filesToUpdate[i];
                        var fileName = Path.GetFileName(file.Path);
                        UpdateStatusChanged?.Invoke($"Downloading {fileName} ({i + 1}/{filesToUpdate.Count})...");
                        DownloadProgressChanged?.Invoke(i + 1, filesToUpdate.Count);
                        
                        // Construct direct download URL (no API, no rate limit)
                        var downloadUrl = $"https://raw.githubusercontent.com/{owner}/{repo}/{branch}/{Uri.EscapeDataString(file.Path)}";
                        _logger.LogDebug("Downloading {FileName} from {Url}", fileName, downloadUrl);
                        
                        var response = await _httpClient!.GetAsync(downloadUrl);
                        if (response.IsSuccessStatusCode)
                        {
                            var content = await response.Content.ReadAsByteArrayAsync();
                            var tempPath = Path.Combine(tempDir, fileName);
                            await File.WriteAllBytesAsync(tempPath, content);
                            
                            // Add/update this file in our tracking
                            // Set LastIndexedTimestamp to null since file was updated and needs re-indexing
                            allFiles[fileName] = new FileCommitInfo
                            {
                                LastIndexedTimestamp = null,  // null = needs indexing after update
                                CommitHash = file.Sha
                            };
                            
                            _logger.LogDebug("Downloaded {FileName} successfully ({Size:N0} bytes, SHA: {Sha})", 
                                fileName, content.Length, file.Sha.Substring(0, 7));
                        }
                        else
                        {
                            _logger.LogError("Failed to download {FileName}: {StatusCode}", fileName, response.StatusCode);
                            throw new HttpRequestException($"Failed to download {fileName}: {response.StatusCode}");
                        }
                    }
                    
                    // Move all files to final location
                    UpdateStatusChanged?.Invoke("Applying updates...");
                    foreach (var file in Directory.GetFiles(tempDir))
                    {
                        var fileName = Path.GetFileName(file);
                        var destPath = Path.Combine(xmlDir, fileName);
                        File.Move(file, destPath, overwrite: true);
                    }
                    
                    // Save all file dates with commit hashes (both up-to-date and newly downloaded)
                    await _xmlFileDatesService.SaveFileDatesDataAsync(allFiles, latestCommitHash);
                    
                    // Trigger incremental indexing
                    UpdateStatusChanged?.Invoke("Re-indexing updated files...");
                    var progress = new Progress<IndexingProgress>();
                    await _indexingService.BuildIndexAsync(progress);
                    
                    UpdateStatusChanged?.Invoke($"Successfully updated {filesToUpdate.Count} files");
                    _logger.LogInformation("Update completed successfully. API calls used: 2 total. Direct downloads: {Count}", filesToUpdate.Count);
                }
                finally
                {
                    // Cleanup temp directory
                    if (Directory.Exists(tempDir))
                        Directory.Delete(tempDir, recursive: true);
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error downloading updated files");
                UpdateStatusChanged?.Invoke($"Error downloading updates: {ex.Message}");
                throw;
            }
            finally
            {
                _isDownloading = false;
            }
        }


        private string GetAppDataDirectory()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appDataPath, AppConstants.AppDataDirectoryName);
        }

        /// <summary>
        /// Calculates the Git blob SHA for a file, which matches the SHA returned by GitHub's Tree API.
        /// Git blob SHA = SHA1("blob " + filesize + "\0" + content)
        /// </summary>
        private static string CalculateGitBlobSha(string filePath)
        {
            var content = File.ReadAllBytes(filePath);
            var header = Encoding.UTF8.GetBytes($"blob {content.Length}\0");
            var combined = new byte[header.Length + content.Length];
            
            Array.Copy(header, 0, combined, 0, header.Length);
            Array.Copy(content, 0, combined, header.Length, content.Length);
            
            using var sha1 = SHA1.Create();
            var hash = sha1.ComputeHash(combined);
            return Convert.ToHexString(hash).ToLowerInvariant();
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

    }
}