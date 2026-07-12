using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Azure.Identity;
using Microsoft.Extensions.Logging;
using Microsoft.Graph;
using Microsoft.Graph.Models;
using CST.Avalonia.Constants;

namespace CST.Avalonia.Services
{
    /// <summary>
    /// Service for downloading PDF files from SharePoint/OneDrive using Microsoft Graph API.
    ///
    /// POC Implementation Notes:
    /// - Uses Client Credentials flow (app-only authentication)
    /// - The app must have Files.Read.All or Sites.Read.All application permission in Azure AD
    /// - For personal OneDrive (vipassanatrust-my.sharepoint.com), uses /users/{userId}/drive
    /// </summary>
    public class SharePointService : ISharePointService, IDisposable
    {
        private readonly ILogger<SharePointService> _logger;
        private readonly GraphServiceClient? _graphClient;
        private readonly string _localPdfDirectory;
        private string? _cachedDriveId;

        // ===========================================
        // Obfuscated credentials - deobfuscated at runtime
        // ===========================================
        // These are XOR-obfuscated to prevent casual extraction from the binary.
        // Use SecretObfuscator.Obfuscate() to generate new values if credentials change.

        private const string ObfuscatedTenantId = "NTokMxYSBQsTRkJHRiRPAwcuOjcgChILAwYcU11Y";
        private const string ObfuscatedClientId = "cWZsMVxRBlxfVlVXBH1VWV8hfjVrBARJVhMHBABRaQIIDSdq";
        private const string ObfuscatedClientSecret = "FSUqajQfLgIqRh51DDcEHyYHPT4gIxEwSxsKAkFQEjk1PTEgYDARNA==";
        private const string ObfuscatedUserPrincipal = "KzY4IiUVDRUbRlFZVH4OHg4=";

        // Deobfuscated values (computed once at runtime)
        private static string TenantId => SecretObfuscator.Deobfuscate(ObfuscatedTenantId);
        private static string ClientId => SecretObfuscator.Deobfuscate(ObfuscatedClientId);
        private static string ClientSecret => SecretObfuscator.Deobfuscate(ObfuscatedClientSecret);
        private static string OneDriveUserPrincipalName => SecretObfuscator.Deobfuscate(ObfuscatedUserPrincipal);

        // The root folder path within OneDrive where PDFs are stored
        private const string SourceRootFolder = "_Source";

        public SharePointService(ILogger<SharePointService> logger)
        {
            _logger = logger;

            // Set up local PDF storage directory
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _localPdfDirectory = Path.Combine(appDataPath, AppConstants.AppDataDirectoryName, "pdfs");
            Directory.CreateDirectory(_localPdfDirectory);

            _logger.LogInformation("SharePoint PDF cache directory: {Path}", _localPdfDirectory);

            // Initialize Graph client with obfuscated credentials
            try
            {
                var credential = new ClientSecretCredential(TenantId, ClientId, ClientSecret);
                _graphClient = new GraphServiceClient(credential, new[] { "https://graph.microsoft.com/.default" });
                _logger.LogInformation("Graph client initialized successfully");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to initialize Graph client");
            }
        }

        /// <summary>
        /// Gets and caches the drive ID for the configured user.
        /// </summary>
        private async Task<string?> GetDriveIdAsync()
        {
            if (_cachedDriveId != null)
                return _cachedDriveId;

            if (_graphClient == null)
                return null;

            try
            {
                var drive = await _graphClient.Users[OneDriveUserPrincipalName].Drive.GetAsync();
                _cachedDriveId = drive?.Id;
                return _cachedDriveId;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to get drive ID");
                return null;
            }
        }

        public async Task<(bool Success, string Message)> TestConnectionAsync()
        {
            if (_graphClient == null)
            {
                return (false, "Graph client not initialized. Please configure credentials in SharePointService.cs:\n" +
                    "- TenantId\n- ClientId\n- ClientSecret\n\n" +
                    "These come from your Azure AD App Registration.");
            }

            try
            {
                _logger.LogInformation("Testing connection to SharePoint...");

                // Try to get the user's drive to verify permissions
                var drive = await _graphClient.Users[OneDriveUserPrincipalName].Drive.GetAsync();

                if (drive != null)
                {
                    _cachedDriveId = drive.Id;
                    _logger.LogInformation("Successfully connected to OneDrive. Drive ID: {DriveId}, Owner: {Owner}",
                        drive.Id, drive.Owner?.User?.DisplayName ?? "Unknown");

                    return (true, $"Connected successfully!\n\nDrive ID: {drive.Id}\nOwner: {drive.Owner?.User?.DisplayName ?? "Unknown"}\nQuota Used: {drive.Quota?.Used:N0} bytes");
                }

                return (false, "Drive returned null - check user principal name");
            }
            catch (Microsoft.Graph.Models.ODataErrors.ODataError odataError)
            {
                _logger.LogError("Graph API error: {Code} - {Message}", odataError.Error?.Code, odataError.Error?.Message);
                return (false, $"Graph API Error:\n\nCode: {odataError.Error?.Code}\nMessage: {odataError.Error?.Message}\n\n" +
                    "Common issues:\n" +
                    "- 401: Invalid credentials or expired secret\n" +
                    "- 403: Missing permissions (need Files.Read.All)\n" +
                    "- 404: User not found (check OneDriveUserPrincipalName)");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to test SharePoint connection");
                return (false, $"Connection failed:\n\n{ex.GetType().Name}: {ex.Message}");
            }
        }

        public async Task<string[]> ListFilesAsync(string folderPath)
        {
            if (_graphClient == null)
            {
                _logger.LogWarning("Graph client not initialized");
                return Array.Empty<string>();
            }

            try
            {
                var driveId = await GetDriveIdAsync();
                if (string.IsNullOrEmpty(driveId))
                {
                    return new[] { "Error: Could not get drive ID" };
                }

                var fullPath = string.IsNullOrEmpty(folderPath) ? SourceRootFolder : $"{SourceRootFolder}/{folderPath}";
                _logger.LogInformation("Listing files in: {Path}", fullPath);

                // Use path-based access with the special colon syntax: root:/path:
                var itemPath = $"root:/{fullPath}:";
                var children = await _graphClient.Drives[driveId].Items[itemPath].Children.GetAsync();

                if (children?.Value == null)
                {
                    _logger.LogWarning("No children returned for path: {Path}", fullPath);
                    return Array.Empty<string>();
                }

                var files = new System.Collections.Generic.List<string>();
                foreach (var item in children.Value)
                {
                    var itemType = item.Folder != null ? "[DIR]" : "[FILE]";
                    var size = item.Size.HasValue ? $" ({item.Size:N0} bytes)" : "";
                    files.Add($"{itemType} {item.Name}{size}");
                }

                _logger.LogInformation("Found {Count} items in {Path}", files.Count, fullPath);
                return files.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to list files in {Path}", folderPath);
                return new[] { $"Error: {ex.Message}" };
            }
        }

        public async Task<string?> DownloadPdfAsync(string sharePointPath)
        {
            if (_graphClient == null)
            {
                _logger.LogWarning("Graph client not initialized");
                return null;
            }

            var localPath = GetLocalPdfPath(sharePointPath);

            // Use the cached copy only if it's an intact PDF. A pre-NET-1 truncated download left at localPath
            // would otherwise be trusted forever (these files are the permanent preservation store, never
            // evicted, so it never heals). A corrupt cached file falls through to a fresh, size-verified
            // re-download below, which atomically REPLACES the bad file - re-downloading a corrupt file does not
            // conflict with the preservation rule. (#194, follow-up to NET-1)
            // A corrupt cached PDF still on disk is a fallback if the re-download fails: it's the permanent
            // preservation store and tolerant viewers show its readable pages, so an offline user should get it
            // rather than nothing. (#324 A9-2)
            bool haveUnvalidatedCache = false;
            if (File.Exists(localPath))
            {
                if (IsIntactPdf(localPath))
                {
                    _logger.LogInformation("PDF already cached locally: {Path}", localPath);
                    return localPath;
                }
                haveUnvalidatedCache = true;
                if (WarnOnce(localPath))
                    _logger.LogWarning("Cached PDF failed integrity check (truncated/corrupt); re-downloading: {Path}", localPath);
            }

            // On any re-download failure, fall back to the (corrupt-but-present) cached copy rather than null, so
            // an offline user keeps the readable pages; never deletes it, and a later call retries. (#324 A9-2)
            string? Fallback() => haveUnvalidatedCache && File.Exists(localPath) ? localPath : null;

            try
            {
                var driveId = await GetDriveIdAsync();
                if (string.IsNullOrEmpty(driveId))
                {
                    _logger.LogError("Could not get drive ID");
                    return Fallback();
                }

                var fullPath = $"{SourceRootFolder}/{sharePointPath}";
                _logger.LogInformation("Downloading PDF from SharePoint: {Path}", fullPath);

                // Use path-based access with the special colon syntax: root:/path:
                var itemPath = $"root:/{fullPath}:";

                // Get the file info first
                var driveItem = await _graphClient.Drives[driveId].Items[itemPath].GetAsync();

                if (driveItem == null)
                {
                    _logger.LogError("File not found in SharePoint: {Path}", fullPath);
                    return Fallback();
                }

                // Download the file content
                var contentStream = await _graphClient.Drives[driveId].Items[itemPath].Content.GetAsync();

                if (contentStream == null)
                {
                    _logger.LogError("Failed to get content stream for: {Path}", fullPath);
                    return Fallback();
                }

                // Save atomically with size verification. A partial download must never land at localPath:
                // the cache check above trusts any file already there, and these PDFs are the permanent
                // preservation store (not an evictable cache). (NET-1)
                if (!await SaveStreamVerifiedAsync(contentStream, localPath, driveItem.Size, _logger))
                {
                    if (haveUnvalidatedCache)
                        _logger.LogWarning("Re-download failed verification; serving the unvalidated cached copy, will retry: {Path}", localPath);
                    return Fallback();
                }

                _logger.LogInformation("Downloaded PDF to: {Path} ({Size:N0} bytes)", localPath, new FileInfo(localPath).Length);
                return localPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download PDF: {Path}", sharePointPath);
                if (haveUnvalidatedCache)
                    _logger.LogWarning("Re-download failed; serving the unvalidated cached copy, will retry: {Path}", localPath);
                return Fallback();
            }
        }

        // Log the "corrupt cached PDF" warning at most once per path per session — a legitimately-unconventional
        // server PDF would otherwise re-warn on every open. (#324 A9-3)
        private static readonly HashSet<string> _warnedCorruptPaths = new();
        private static bool WarnOnce(string path)
        {
            lock (_warnedCorruptPaths) return _warnedCorruptPaths.Add(path);
        }

        /// <summary>
        /// Write a download stream to <paramref name="finalPath"/> without ever leaving a partial file
        /// there: stream into a sibling ".part", verify the byte count against the server-reported size
        /// (when known), then atomically move it into place. On a size mismatch or any exception the
        /// ".part" is removed and <paramref name="finalPath"/> is left untouched. (NET-1)
        /// </summary>
        internal static async Task<bool> SaveStreamVerifiedAsync(Stream content, string finalPath, long? expectedSize, ILogger logger)
        {
            var localDir = Path.GetDirectoryName(finalPath);
            if (!string.IsNullOrEmpty(localDir))
                Directory.CreateDirectory(localDir);

            // Unique temp name so two concurrent re-downloads of the same PDF don't collide on one ".part" and
            // both fail this round. (#324 A9-4)
            var tempPath = finalPath + "." + Guid.NewGuid().ToString("N") + ".part";
            try
            {
                using (var fileStream = File.Create(tempPath))
                {
                    await content.CopyToAsync(fileStream);
                }

                var downloadedLength = new FileInfo(tempPath).Length;
                if (expectedSize.HasValue && downloadedLength != expectedSize.Value)
                {
                    logger.LogError("Downloaded size mismatch for {Path}: got {Actual:N0} bytes, expected {Expected:N0}; discarding partial download",
                        finalPath, downloadedLength, expectedSize.Value);
                    TryDelete(tempPath);
                    return false;
                }

                // Same-volume rename -> atomic promote into place.
                File.Move(tempPath, finalPath, overwrite: true);
                return true;
            }
            catch
            {
                TryDelete(tempPath);
                throw;
            }
        }

        private static void TryDelete(string path)
        {
            try { if (File.Exists(path)) File.Delete(path); }
            catch { /* best-effort cleanup of the partial file */ }
        }

        /// <summary>
        /// A cheap, offline structural check that a file is a complete PDF: it must begin with the
        /// <c>%PDF-</c> header and carry the <c>%%EOF</c> end-of-file marker near its tail. A download
        /// truncated mid-transfer keeps a valid header but loses the trailing <c>%%EOF</c>, so this catches the
        /// #194 pre-fix truncation without a network round-trip (the authoritative byte-size check happens on
        /// (re)download via <see cref="SaveStreamVerifiedAsync"/>). Anything unreadable is treated as not intact
        /// so it gets re-fetched. (#194)
        /// </summary>
        internal static bool IsIntactPdf(string path)
        {
            ReadOnlySpan<byte> header = "%PDF-"u8;
            ReadOnlySpan<byte> eof = "%%EOF"u8;
            try
            {
                using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read);
                long length = fs.Length;
                if (length < header.Length + eof.Length) return false;

                // Scan the first 1KB for %PDF- rather than requiring it at byte 0 — mirrors Acrobat, which
                // tolerates junk before the header; a truncation still keeps the header, so this doesn't weaken
                // the EOF check below. (#324 A9-3)
                int headLen = (int)Math.Min(1024, length);
                Span<byte> headBuf = stackalloc byte[1024];
                headBuf = headBuf.Slice(0, headLen);
                fs.ReadExactly(headBuf);
                if (headBuf.IndexOf(header) < 0) return false;

                // Scan the final chunk for %%EOF (the spec allows trailing whitespace/newlines after it).
                int tailLength = (int)Math.Min(1024, length);
                Span<byte> tailBuf = stackalloc byte[1024];
                tailBuf = tailBuf.Slice(0, tailLength);
                fs.Seek(-tailLength, SeekOrigin.End);
                fs.ReadExactly(tailBuf);
                return tailBuf.IndexOf(eof) >= 0;
            }
            catch
            {
                return false; // unreadable/locked => treat as not intact and re-fetch rather than trust it
            }
        }

        public string GetLocalPdfPath(string sharePointPath)
        {
            // Sanitize the path for local file system
            var sanitizedPath = sharePointPath
                .Replace('/', Path.DirectorySeparatorChar)
                .Replace('\\', Path.DirectorySeparatorChar);

            return Path.Combine(_localPdfDirectory, sanitizedPath);
        }

        public bool PdfExistsLocally(string sharePointPath)
        {
            return File.Exists(GetLocalPdfPath(sharePointPath));
        }

        public void Dispose()
        {
            // GraphServiceClient doesn't need explicit disposal in v5
        }
    }
}
