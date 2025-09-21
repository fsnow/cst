using System;
using System.IO;
using System.Net.Http;
using System.Text.Json;
using System.Threading.Tasks;
using CST.Avalonia.Constants;
using CST.Avalonia.Models;
using Serilog;

namespace CST.Avalonia.Services
{
    /// <summary>
    /// Service for fetching and caching welcome page updates from GitHub
    /// </summary>
    public class WelcomeUpdateService
    {
        private readonly HttpClient _httpClient;
        private readonly ILogger _logger;
        private readonly string _cacheDirectory;
        private readonly string _cacheFilePath;

        // Configuration - Using main branch for simplicity
        // The JSON file lives in the root of the repository for easy access and maintenance
        private const string UpdatesUrl = "https://raw.githubusercontent.com/fsnow/cst/main/welcome-updates.json";

        private const string CacheFileName = "welcome-updates-cache.json";
        private static readonly TimeSpan CacheTtl = TimeSpan.FromHours(24);
        private static readonly TimeSpan ForcedRefreshAge = TimeSpan.FromDays(7);
        private static readonly TimeSpan MaxCacheAge = TimeSpan.FromDays(30);
        private static readonly TimeSpan HttpTimeout = TimeSpan.FromSeconds(10);

        // Current app version - will be injected from App.axaml.cs
        public string CurrentAppVersion { get; set; } = "5.0.0-beta.1";

        public WelcomeUpdateService(HttpClient? httpClient = null)
        {
            _httpClient = httpClient ?? new HttpClient { Timeout = HttpTimeout };
            _logger = Log.ForContext<WelcomeUpdateService>();

            // Determine cache directory based on platform
            var appDataPath = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
            _cacheDirectory = Path.Combine(appDataPath, AppConstants.AppDataDirectoryName, "cache");
            _cacheFilePath = Path.Combine(_cacheDirectory, CacheFileName);

            // Ensure cache directory exists
            try
            {
                Directory.CreateDirectory(_cacheDirectory);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to create cache directory: {Directory}", _cacheDirectory);
            }
        }

        /// <summary>
        /// Get welcome updates, either from cache or by fetching fresh data
        /// </summary>
        public async Task<WelcomeUpdates?> GetUpdatesAsync()
        {
            try
            {
                // Try to load from cache first
                var cachedData = await LoadCacheAsync();

                if (cachedData != null)
                {
                    var age = DateTime.UtcNow - cachedData.FetchedAt;

                    // Use cache if it's fresh enough
                    if (age < CacheTtl)
                    {
                        _logger.Debug("Using cached welcome updates (age: {Age:hh\\:mm\\:ss})", age);
                        return cachedData.Data;
                    }

                    // Force refresh if cache is too old
                    if (age > ForcedRefreshAge)
                    {
                        _logger.Information("Cache is stale (age: {Age:dd\\.hh\\:mm\\:ss}), forcing refresh", age);
                    }
                }

                // Try to fetch fresh data
                var freshData = await FetchUpdatesAsync();
                if (freshData != null)
                {
                    await SaveCacheAsync(freshData);
                    return freshData;
                }

                // Fall back to cache if fetch failed and cache exists (even if stale)
                if (cachedData?.Data != null && (DateTime.UtcNow - cachedData.FetchedAt) < MaxCacheAge)
                {
                    _logger.Warning("Failed to fetch updates, using stale cache (age: {Age:dd\\.hh\\:mm\\:ss})",
                        DateTime.UtcNow - cachedData.FetchedAt);
                    return cachedData.Data;
                }

                _logger.Warning("No updates available (offline and no valid cache)");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error getting welcome updates");
                return null;
            }
        }

        /// <summary>
        /// Fetch fresh updates from GitHub
        /// </summary>
        private async Task<WelcomeUpdates?> FetchUpdatesAsync()
        {
            try
            {
                _logger.Debug("Fetching welcome updates from: {Url}", UpdatesUrl);

                var response = await _httpClient.GetAsync(UpdatesUrl);

                if (!response.IsSuccessStatusCode)
                {
                    _logger.Warning("Failed to fetch updates: HTTP {StatusCode}", response.StatusCode);
                    return null;
                }

                var json = await response.Content.ReadAsStringAsync();
                var updates = JsonSerializer.Deserialize<WelcomeUpdates>(json, GetJsonOptions());

                if (updates == null)
                {
                    _logger.Warning("Failed to deserialize updates JSON");
                    return null;
                }

                // Validate schema version
                if (updates.SchemaVersion != 1)
                {
                    _logger.Warning("Unsupported schema version: {Version}", updates.SchemaVersion);
                    return null;
                }

                _logger.Information("Successfully fetched welcome updates (last updated: {LastUpdated:yyyy-MM-dd})",
                    updates.LastUpdated);

                return updates;
            }
            catch (HttpRequestException ex)
            {
                _logger.Debug(ex, "Network error fetching updates (likely offline)");
                return null;
            }
            catch (TaskCanceledException)
            {
                _logger.Debug("Update fetch timed out");
                return null;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Unexpected error fetching updates");
                return null;
            }
        }

        /// <summary>
        /// Load cached updates from disk
        /// </summary>
        private async Task<CachedWelcomeUpdates?> LoadCacheAsync()
        {
            try
            {
                if (!File.Exists(_cacheFilePath))
                {
                    _logger.Debug("No cache file found at: {Path}", _cacheFilePath);
                    return null;
                }

                var json = await File.ReadAllTextAsync(_cacheFilePath);
                var cached = JsonSerializer.Deserialize<CachedWelcomeUpdates>(json, GetJsonOptions());

                if (cached?.Data == null)
                {
                    _logger.Warning("Invalid cache file format");
                    return null;
                }

                return cached;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to load cache from: {Path}", _cacheFilePath);
                return null;
            }
        }

        /// <summary>
        /// Save updates to cache
        /// </summary>
        private async Task SaveCacheAsync(WelcomeUpdates updates)
        {
            try
            {
                var cached = new CachedWelcomeUpdates
                {
                    FetchedAt = DateTime.UtcNow,
                    Data = updates
                };

                var json = JsonSerializer.Serialize(cached, GetJsonOptions());
                await File.WriteAllTextAsync(_cacheFilePath, json);

                _logger.Debug("Saved updates to cache: {Path}", _cacheFilePath);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to save cache to: {Path}", _cacheFilePath);
            }
        }

        /// <summary>
        /// Clear the cache (useful for testing or forced refresh)
        /// </summary>
        public void ClearCache()
        {
            try
            {
                if (File.Exists(_cacheFilePath))
                {
                    File.Delete(_cacheFilePath);
                    _logger.Information("Cleared welcome updates cache");
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Failed to clear cache");
            }
        }

        /// <summary>
        /// Check if current app version needs updating
        /// </summary>
        public async Task<VersionCheckResult> CheckForUpdatesAsync()
        {
            var updates = await GetUpdatesAsync();
            if (updates == null)
            {
                return new VersionCheckResult
                {
                    CheckSuccessful = false,
                    IsUpdateAvailable = false
                };
            }

            _logger.Information("Version check - Current app version: '{CurrentVersion}'", CurrentAppVersion);

            // Determine which channel to check based on current version
            var currentVersion = VersionComparer.ParseVersion(CurrentAppVersion);
            var latestVersion = currentVersion?.IsPreRelease == true
                ? updates.CurrentVersion.Beta
                : updates.CurrentVersion.Stable;

            _logger.Information("Version check - Latest version for channel: '{LatestVersion}' (using {Channel} channel)",
                latestVersion, currentVersion?.IsPreRelease == true ? "beta" : "stable");

            var comparison = VersionComparer.Compare(CurrentAppVersion, latestVersion);

            _logger.Information("Version check - Comparison result: {Comparison}", comparison);

            // Get version-specific message if available
            VersionMessage? message = null;

            // First try exact match with current version
            if (updates.Messages.ContainsKey(CurrentAppVersion))
            {
                message = updates.Messages[CurrentAppVersion];
                _logger.Information("Version check - Found version-specific message for '{Version}'", CurrentAppVersion);
            }
            // If no exact match, try without build metadata (strip everything after '+')
            else
            {
                var versionWithoutBuildMetadata = CurrentAppVersion.Split('+')[0];
                if (versionWithoutBuildMetadata != CurrentAppVersion && updates.Messages.ContainsKey(versionWithoutBuildMetadata))
                {
                    message = updates.Messages[versionWithoutBuildMetadata];
                    _logger.Information("Version check - Found version-specific message for '{Version}' (stripped build metadata from '{OriginalVersion}')", versionWithoutBuildMetadata, CurrentAppVersion);
                }
            }
            if (message == null && comparison != VersionComparison.Current && updates.Messages.ContainsKey("default"))
            {
                message = updates.Messages["default"];
                _logger.Information("Version check - Using default message");
            }

            var isUpdateAvailable = comparison != VersionComparison.Current && comparison != VersionComparison.NewerThanLatest;

            _logger.Information("Version check - Update available: {IsUpdateAvailable} (comparison: {Comparison})",
                isUpdateAvailable, comparison);

            return new VersionCheckResult
            {
                CheckSuccessful = true,
                IsUpdateAvailable = isUpdateAvailable,
                CurrentVersion = CurrentAppVersion,
                LatestVersion = latestVersion,
                Comparison = comparison,
                Message = message,
                Updates = updates
            };
        }

        /// <summary>
        /// Get JSON serialization options
        /// </summary>
        private static JsonSerializerOptions GetJsonOptions()
        {
            return new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                WriteIndented = true
            };
        }
    }

    /// <summary>
    /// Result of version check
    /// </summary>
    public class VersionCheckResult
    {
        public bool CheckSuccessful { get; set; }
        public bool IsUpdateAvailable { get; set; }
        public string? CurrentVersion { get; set; }
        public string? LatestVersion { get; set; }
        public VersionComparison Comparison { get; set; }
        public VersionMessage? Message { get; set; }
        public WelcomeUpdates? Updates { get; set; }
    }
}