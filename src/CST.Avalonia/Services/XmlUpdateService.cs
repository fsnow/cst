using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CST;
using CST.Avalonia.Models;
using CST.Avalonia.Constants;
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

                    // Automatically download the XML files on first run
                    await PerformInitialDownloadAsync();
                    return;
                }
                
                // Step 2: Check if automatic updates are enabled. With them off, the books on disk are left exactly
                // as they are: this is how local edits are kept. With them on, any difference from upstream is
                // repaired. (#1093)
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

        /// <summary>
        /// Verify a downloaded file's bytes against the Git blob SHA the tree reported. Throws
        /// <see cref="InvalidDataException"/> on a mismatch - a corrupt or partial download must not be written
        /// to the XML directory. (#65)
        /// </summary>
        private void VerifyDownloadIntegrity(string fileName, byte[] content, string expectedSha)
        {
            if (GitBlobHash.Matches(content, expectedSha))
                return;

            _logger.LogError(
                "Integrity check failed for {FileName}: expected blob SHA {Expected}, got {Actual} ({Size:N0} bytes) - corrupt or partial download",
                fileName, expectedSha, GitBlobHash.Compute(content), content.Length);
            throw new InvalidDataException(
                $"Downloaded '{fileName}' failed its integrity check (Git blob SHA mismatch).");
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
                var tempDir = CreateStagingDirectory(xmlDir);
                
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
                            VerifyDownloadIntegrity(fileName, content, file.Sha);

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
                        await MoveIntoPlaceAsync(file, destPath);
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

        /// <summary>
        /// Brings the local books into line with upstream. (#1093)
        ///
        /// <para>[fsnow, 2026-11-02] "as long as "Enable automatic updates" is checked, any deviation from upstream
        /// should be detected and repaired." So every run hashes the books on disk rather than trusting the record
        /// of what it last downloaded. A local edit, a truncated or damaged file, or a missing one is replaced by
        /// upstream's copy, whether or not upstream has changed that book. Keeping local edits is what turning
        /// automatic updates off is for (see <see cref="CheckForUpdatesAsync"/>).</para>
        /// </summary>
        private async Task CheckGitHubForUpdatesAsync()
        {
            var xmlDir = _settingsService.Settings.XmlBooksDirectory;

            // STEP 0: the books on disk against the copies last downloaded. No network needed, so it is reported
            // even offline. Hashing all 217 books takes about a third of a second.
            var bookFiles = Books.Inst.Select(b => b.FileName).ToList();
            var localShas = ComputeLocalShas(xmlDir, bookFiles);
            var fileDatesData = await _xmlFileDatesService.GetFileDatesDataAsync();
            var deviations = FindLocalDeviations(bookFiles, localShas, fileDatesData?.Files);
            if (deviations.Count > 0)
            {
                _logger.LogInformation("Local check: {Count} book(s) differ from the copies last downloaded: {Files}",
                    deviations.Count, Summarize(deviations));
            }

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
                    WarnUnrepaired(deviations);
                    UpdateStatusChanged?.Invoke("Network request timed out - skipping updates");
                    return;
                }

                var branchInfo = await branchTask;
                var commitSha = branchInfo.Commit.Sha;
                _logger.LogInformation("Latest commit SHA: {CommitSha}", commitSha.Substring(0, 7));

                // Nothing to do only when upstream has not moved AND every book on disk is the copy we recorded.
                if (fileDatesData?.LastKnownRepositoryCommitHash == commitSha && deviations.Count == 0)
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
                    WarnUnrepaired(deviations);
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

                var repoFilesByName = targetFiles.ToDictionary(f => Path.GetFileName(f.Path), f => f);

                var missingFromRepo = bookFiles.Where(f => !repoFilesByName.ContainsKey(f)).ToList();
                foreach (var name in missingFromRepo.Take(5)) // Only log first 5 to avoid spam
                    _logger.LogWarning("Book file {FileName} not found in repository", name);

                // STEP 3: the books on disk against the tree. Every run compares the actual files.
                var plan = PlanSync(bookFiles, localShas, fileDatesData?.Files,
                    repoFilesByName.ToDictionary(kv => kv.Key, kv => kv.Value.Sha));

                if (plan.ToDownload.Count == 0)
                {
                    _logger.LogInformation("No files need updating");
                    UpdateStatusChanged?.Invoke("No files need updating");
                    // Saves the new repository commit, and a rebuilt tracking entry for any book that lacked one.
                    await _xmlFileDatesService.SaveFileDatesDataAsync(plan.UpToDate, commitSha);
                    return;
                }

                _logger.LogInformation(
                    "Files needing update: {UpdateCount}/{TotalBooks}, of which {RepairCount} repair(s) of local copies upstream has not changed: {RepairFiles}",
                    plan.ToDownload.Count, bookFiles.Count, plan.Repairs.Count, Summarize(plan.Repairs));
                _logger.LogInformation("Files already up-to-date: {UpToDateCount}/{TotalBooks}", plan.UpToDate.Count, bookFiles.Count);

                // STEP 4: Download updated files via direct HTTPS (no API calls)
                var filesToUpdate = plan.ToDownload.Select(f => repoFilesByName[f]).ToList();
                await DownloadUpdatedFilesDirectAsync(filesToUpdate, plan.UpToDate, commitSha, owner, repo, branch);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error checking GitHub for updates");
                WarnUnrepaired(deviations);
                UpdateStatusChanged?.Invoke($"Error checking for updates: {ex.Message}");
            }
        }

        private void WarnUnrepaired(List<string> deviations)
        {
            if (deviations.Count > 0)
                _logger.LogWarning(
                    "{Count} book(s) differ from upstream and were not repaired now; they will be at the next start with a connection: {Files}",
                    deviations.Count, Summarize(deviations));
        }

        private static string Summarize(IReadOnlyCollection<string> names, int max = 10) =>
            names.Count <= max ? string.Join(", ", names) : string.Join(", ", names.Take(max)) + $" and {names.Count - max} more";

        /// <summary>The Git blob SHA of each book on disk; null for a book with no local file. (#1093)</summary>
        internal static Dictionary<string, string?> ComputeLocalShas(string xmlDir, IEnumerable<string> bookFiles)
        {
            var shas = new Dictionary<string, string?>();
            foreach (var name in bookFiles)
            {
                var path = Path.Combine(xmlDir, name);
                shas[name] = File.Exists(path) ? GitBlobHash.Compute(File.ReadAllBytes(path)) : null;
            }
            return shas;
        }

        /// <summary>
        /// Books whose file on disk is not the copy last downloaded: edited, damaged, missing, or never recorded.
        /// (#1093)
        /// </summary>
        internal static List<string> FindLocalDeviations(IEnumerable<string> bookFiles,
            IReadOnlyDictionary<string, string?> localShas, IReadOnlyDictionary<string, FileCommitInfo>? tracked)
        {
            var deviations = new List<string>();
            foreach (var name in bookFiles)
            {
                localShas.TryGetValue(name, out var local);
                FileCommitInfo? info = null;
                tracked?.TryGetValue(name, out info);
                if (local == null || string.IsNullOrEmpty(info?.CommitHash) || local != info.CommitHash)
                    deviations.Add(name);
            }
            return deviations;
        }

        internal sealed record SyncPlan(List<string> ToDownload, List<string> Repairs,
            Dictionary<string, FileCommitInfo> UpToDate);

        /// <summary>
        /// Which books to download: every book whose file on disk is not upstream's, whatever the tracking data
        /// says. A download is a <i>repair</i> when upstream's copy is the one we recorded: upstream has not changed
        /// the book, the local file has. A book that matches keeps its tracking entry, so the indexer does not
        /// re-index it; one that matches but was recorded under another hash, or not at all, gets upstream's hash
        /// and a null index timestamp, leaving the indexer to decide. Books not in the repository are left out.
        /// (#1093)
        /// </summary>
        internal static SyncPlan PlanSync(IEnumerable<string> bookFiles, IReadOnlyDictionary<string, string?> localShas,
            IReadOnlyDictionary<string, FileCommitInfo>? tracked, IReadOnlyDictionary<string, string> repoShas)
        {
            var toDownload = new List<string>();
            var repairs = new List<string>();
            var upToDate = new Dictionary<string, FileCommitInfo>();
            foreach (var name in bookFiles)
            {
                if (!repoShas.TryGetValue(name, out var repoSha)) continue;
                localShas.TryGetValue(name, out var local);
                FileCommitInfo? info = null;
                tracked?.TryGetValue(name, out info);

                if (local == repoSha)
                {
                    upToDate[name] = info != null && info.CommitHash == repoSha
                        ? info
                        : new FileCommitInfo { LastIndexedTimestamp = null, CommitHash = repoSha };
                    continue;
                }

                toDownload.Add(name);
                if (info?.CommitHash == repoSha) repairs.Add(name);
            }
            return new SyncPlan(toDownload, repairs, upToDate);
        }

        private async Task DownloadUpdatedFilesDirectAsync(List<TreeItem> filesToUpdate, Dictionary<string, FileCommitInfo> upToDateFiles, string latestCommitHash, string owner, string repo, string branch)
        {
            try
            {
                _isDownloading = true;
                var xmlDir = _settingsService.Settings.XmlBooksDirectory;
                
                _logger.LogInformation("Downloading {Count} updated files via direct HTTPS", filesToUpdate.Count);
                UpdateStatusChanged?.Invoke($"Downloading {filesToUpdate.Count} updated files...");
                
                var tempDir = CreateStagingDirectory(xmlDir);
                
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
                            VerifyDownloadIntegrity(fileName, content, file.Sha);

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
                        await MoveIntoPlaceAsync(file, destPath);
                    }
                    
                    // Trigger incremental indexing BEFORE persisting file dates. The
                    // dates cache must still show these files as changed when BuildIndexAsync
                    // runs its change detection; persisting dates first would make it see
                    // "0 changed" and skip indexing entirely.
                    UpdateStatusChanged?.Invoke("Re-indexing updated files...");
                    var progress = new Progress<IndexingProgress>();
                    await _indexingService.BuildIndexAsync(progress);

                    // Save all file dates with commit hashes (both up-to-date and newly
                    // downloaded) only AFTER indexing succeeds, so a failed index leaves the
                    // cache stale and the next launch retries instead of silently skipping.
                    await _xmlFileDatesService.SaveFileDatesDataAsync(allFiles, latestCommitHash);
                    
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


        // Create a staging directory as a SIBLING of the XML directory, so it's on the same volume and the
        // final File.Move into place is an atomic same-volume rename. Path.GetTempPath() is often a different
        // filesystem, where Move degrades to a non-atomic copy+delete that can leave a truncated XML file on
        // a crash. (NET-4)
        internal static string CreateStagingDirectory(string xmlDir)
        {
            var parent = Path.GetDirectoryName(Path.TrimEndingDirectorySeparator(xmlDir));
            if (string.IsNullOrEmpty(parent))
                parent = xmlDir; // fall back to inside the XML dir if it has no parent
            var staging = Path.Combine(parent, $".cst-staging-{Guid.NewGuid():N}");
            Directory.CreateDirectory(staging);
            return staging;
        }

        // Move a staged file into place, retrying briefly. On Windows File.Move over a file an open book tab
        // is reading throws a sharing violation; the read is short, so a few retries let it finish rather than
        // aborting the apply and leaving a mixed-commit corpus. (NET-4)
        internal static async Task MoveIntoPlaceAsync(string source, string dest)
        {
            const int maxAttempts = 5;
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.Move(source, dest, overwrite: true);
                    return;
                }
                catch (IOException) when (attempt < maxAttempts)
                {
                    await Task.Delay(100 * attempt);
                }
            }
        }

        private string GetAppDataDirectory()
        {
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            return Path.Combine(appDataPath, AppConstants.AppDataDirectoryName);
        }

        public void Dispose()
        {
            _httpClient?.Dispose();
        }

    }
}