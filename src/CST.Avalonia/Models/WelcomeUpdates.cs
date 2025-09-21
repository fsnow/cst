using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace CST.Avalonia.Models
{
    /// <summary>
    /// Root model for the welcome updates JSON structure
    /// </summary>
    public class WelcomeUpdates
    {
        [JsonPropertyName("schemaVersion")]
        public int SchemaVersion { get; set; }

        [JsonPropertyName("lastUpdated")]
        public DateTime LastUpdated { get; set; }

        [JsonPropertyName("currentVersion")]
        public CurrentVersionInfo CurrentVersion { get; set; } = new();

        [JsonPropertyName("messages")]
        public Dictionary<string, VersionMessage> Messages { get; set; } = new();

        [JsonPropertyName("announcements")]
        public List<Announcement> Announcements { get; set; } = new();

        [JsonPropertyName("criticalNotices")]
        public List<CriticalNotice> CriticalNotices { get; set; } = new();
    }

    /// <summary>
    /// Current version information for stable and beta channels
    /// </summary>
    public class CurrentVersionInfo
    {
        [JsonPropertyName("stable")]
        public string? Stable { get; set; }

        [JsonPropertyName("beta")]
        public string? Beta { get; set; }
    }

    /// <summary>
    /// Version-specific message
    /// </summary>
    public class VersionMessage
    {
        [JsonPropertyName("type")]
        public string Type { get; set; } = "info"; // info, upgrade, warning

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("downloadUrl")]
        public string? DownloadUrl { get; set; }
    }

    /// <summary>
    /// General announcement
    /// </summary>
    public class Announcement
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("date")]
        public DateTime Date { get; set; }

        [JsonPropertyName("title")]
        public string Title { get; set; } = string.Empty;

        [JsonPropertyName("content")]
        public string Content { get; set; } = string.Empty;

        [JsonPropertyName("showUntil")]
        public DateTime? ShowUntil { get; set; }

        [JsonPropertyName("targetVersions")]
        public List<string> TargetVersions { get; set; } = new();
    }

    /// <summary>
    /// Critical notice for affected versions
    /// </summary>
    public class CriticalNotice
    {
        [JsonPropertyName("id")]
        public string Id { get; set; } = string.Empty;

        [JsonPropertyName("severity")]
        public string Severity { get; set; } = "critical";

        [JsonPropertyName("affectedVersions")]
        public List<string> AffectedVersions { get; set; } = new();

        [JsonPropertyName("message")]
        public string Message { get; set; } = string.Empty;
    }

    /// <summary>
    /// Cache wrapper for storing updates with metadata
    /// </summary>
    public class CachedWelcomeUpdates
    {
        [JsonPropertyName("fetchedAt")]
        public DateTime FetchedAt { get; set; }

        [JsonPropertyName("etag")]
        public string? ETag { get; set; }

        [JsonPropertyName("data")]
        public WelcomeUpdates? Data { get; set; }
    }
}