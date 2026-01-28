using System;
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

            // Check if file already exists
            if (File.Exists(localPath))
            {
                _logger.LogInformation("PDF already cached locally: {Path}", localPath);
                return localPath;
            }

            try
            {
                var driveId = await GetDriveIdAsync();
                if (string.IsNullOrEmpty(driveId))
                {
                    _logger.LogError("Could not get drive ID");
                    return null;
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
                    return null;
                }

                // Download the file content
                var contentStream = await _graphClient.Drives[driveId].Items[itemPath].Content.GetAsync();

                if (contentStream == null)
                {
                    _logger.LogError("Failed to get content stream for: {Path}", fullPath);
                    return null;
                }

                // Ensure directory exists
                var localDir = Path.GetDirectoryName(localPath);
                if (!string.IsNullOrEmpty(localDir))
                {
                    Directory.CreateDirectory(localDir);
                }

                // Save to local file
                using (var fileStream = File.Create(localPath))
                {
                    await contentStream.CopyToAsync(fileStream);
                }

                var fileInfo = new FileInfo(localPath);
                _logger.LogInformation("Downloaded PDF to: {Path} ({Size:N0} bytes)", localPath, fileInfo.Length);

                return localPath;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Failed to download PDF: {Path}", sharePointPath);
                return null;
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
