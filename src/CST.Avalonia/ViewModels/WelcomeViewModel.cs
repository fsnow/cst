using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using ReactiveUI;
using Serilog;

namespace CST.Avalonia.ViewModels
{
    public class WelcomeViewModel : ViewModelBase
    {
        private readonly WelcomeUpdateService _updateService;
        private string _htmlContent = "";
        private bool _isStartupInProgress = false;
        private string _startupStatusMessage = "";

        public string HtmlContent
        {
            get => _htmlContent;
            set => this.RaiseAndSetIfChanged(ref _htmlContent, value);
        }

        public bool IsStartupInProgress
        {
            get => _isStartupInProgress;
            set => this.RaiseAndSetIfChanged(ref _isStartupInProgress, value);
        }

        public string StartupStatusMessage
        {
            get => _startupStatusMessage;
            set => this.RaiseAndSetIfChanged(ref _startupStatusMessage, value);
        }

        public WelcomeViewModel() : this(new WelcomeUpdateService())
        {
        }

        public WelcomeViewModel(WelcomeUpdateService updateService)
        {
            _updateService = updateService;

            // Set current app version from assembly
            var assembly = Assembly.GetExecutingAssembly();
            var rawVersion = assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
                          ?? assembly.GetName().Version?.ToString()
                          ?? "5.0.0-beta.2";

            // Strip the git hash (everything after '+') for display
            var version = rawVersion.Contains('+') ? rawVersion.Substring(0, rawVersion.IndexOf('+')) : rawVersion;
            _updateService.CurrentAppVersion = version;

            // DON'T load HTML content in constructor - it will be loaded when the view is attached
            // This prevents "Call from invalid thread" errors in packaged apps
        }

        public void Initialize()
        {
            // Load HTML content when explicitly initialized from UI thread
            _ = LoadWelcomeContentAsync();
        }

        private async Task LoadWelcomeContentAsync()
        {
            try
            {
                Log.Information("WelcomeViewModel: Starting to load welcome content");

                // Load base HTML content
                var baseHtml = await LoadBaseHtmlAsync();

                // Inject Buddha image as base64
                baseHtml = await InjectBuddhaImageAsync(baseHtml);

                // Check for updates and inject dynamic content
                var versionCheck = await _updateService.CheckForUpdatesAsync();

                // Merge base content with dynamic updates
                var finalHtml = MergeHtmlWithUpdates(baseHtml, versionCheck);

                HtmlContent = finalHtml;
                Log.Information("Welcome page loaded with dynamic content (update check: {Success})",
                    versionCheck.CheckSuccessful);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load welcome page content");
                HtmlContent = GetFallbackHtml();
            }
        }

        private async Task<string> LoadBaseHtmlAsync()
        {
            // First try to load from file system (development scenario)
            var devFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "welcome-content.html");
            Log.Debug("WelcomeViewModel: Checking for file at {Path}", devFilePath);

            if (File.Exists(devFilePath))
            {
                var content = await File.ReadAllTextAsync(devFilePath);
                Log.Information("WelcomeViewModel: HTML loaded from file: {FilePath}, length: {Length}",
                    devFilePath, content.Length);
                return content;
            }

            // Then try to load from embedded resource (installed application scenario)
            var assembly = Assembly.GetExecutingAssembly();
            var resourceName = "CST.Avalonia.Resources.welcome-content.html";

            using var stream = assembly.GetManifestResourceStream(resourceName);
            if (stream != null)
            {
                using var reader = new StreamReader(stream);
                var content = await reader.ReadToEndAsync();
                Log.Information("Welcome page HTML loaded from embedded resource");
                return content;
            }

            // Try alternative paths for different deployment scenarios
            var alternativePaths = new[]
            {
                Path.Combine(Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location) ?? "", "Resources", "welcome-content.html"),
                Path.Combine(Environment.CurrentDirectory, "Resources", "welcome-content.html"),
                Path.Combine(AppContext.BaseDirectory, "Resources", "welcome-content.html")
            };

            foreach (var path in alternativePaths)
            {
                if (File.Exists(path))
                {
                    var content = await File.ReadAllTextAsync(path);
                    Log.Information("Welcome page HTML loaded from alternative path: {FilePath}", path);
                    return content;
                }
            }

            // Fallback to simple HTML if nothing found
            Log.Warning("Welcome page resource not found in any location, using fallback HTML");
            return GetFallbackHtml();
        }

        private async Task<string> InjectBuddhaImageAsync(string html)
        {
            try
            {
                // Try to load the Buddha image and convert to base64
                var imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "buddha-transparent.png");

                if (!File.Exists(imagePath))
                {
                    // Try Assets directory
                    imagePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Assets", "buddha-transparent.png");
                }

                if (File.Exists(imagePath))
                {
                    var imageBytes = await File.ReadAllBytesAsync(imagePath);
                    var base64 = Convert.ToBase64String(imageBytes);
                    var dataUri = $"data:image/png;base64,{base64}";

                    // Replace the placeholder src with the data URI
                    html = html.Replace("buddha-transparent.png", dataUri);
                    Log.Debug("Buddha image injected as base64 data URI");
                }
                else
                {
                    Log.Warning("Buddha image not found at {Path}", imagePath);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to inject Buddha image");
            }

            return html;
        }

        private string MergeHtmlWithUpdates(string baseHtml, VersionCheckResult versionCheck)
        {
            // If no updates were fetched, return base HTML as-is
            if (!versionCheck.CheckSuccessful || versionCheck.Updates == null)
            {
                // Optionally inject a small offline indicator
                return InjectOfflineIndicator(baseHtml);
            }

            var html = baseHtml;

            // Inject version status banner
            html = InjectVersionBanner(html, versionCheck);

            // Inject announcements
            if (versionCheck.Updates.Announcements?.Any() == true)
            {
                html = InjectAnnouncements(html, versionCheck.Updates.Announcements);
            }

            // Inject critical notices
            if (versionCheck.Updates.CriticalNotices?.Any() == true)
            {
                html = InjectCriticalNotices(html, versionCheck.Updates.CriticalNotices);
            }

            return html;
        }

        private string InjectOfflineIndicator(string html)
        {
            // Find </body> tag and inject before it
            var bodyCloseIndex = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyCloseIndex > 0)
            {
                var indicator = @"
<div style='position: fixed; bottom: 10px; right: 10px; padding: 5px 10px;
            background: rgba(255,255,255,0.9); border: 1px solid #ddd;
            border-radius: 3px; font-size: 11px; color: #666;'>
    ⚠️ Unable to check for updates (offline)
</div>";
                html = html.Insert(bodyCloseIndex, indicator);
            }
            return html;
        }

        private string InjectVersionBanner(string html, VersionCheckResult versionCheck)
        {
            string banner;

            if (!versionCheck.IsUpdateAvailable)
            {
                // Current version badge
                banner = $@"
<div class='version-badge success' style='background: #d4edda; border: 1px solid #c3e6cb;
     color: #155724; padding: 10px; margin: 10px 0; border-radius: 5px;'>
    ✓ You're running the latest version ({versionCheck.CurrentVersion})
</div>";
            }
            else
            {
                // Update available banner
                var message = versionCheck.Message ?? new VersionMessage
                {
                    Type = "info",
                    Title = "New Version Available",
                    Content = VersionComparer.GetComparisonDescription(versionCheck.Comparison, versionCheck.LatestVersion)
                };

                var urgency = message.Type switch
                {
                    "warning" => "background: #fff3cd; border-color: #ffeeba; color: #856404;",
                    "critical" => "background: #f8d7da; border-color: #f5c6cb; color: #721c24;",
                    _ => "background: #d1ecf1; border-color: #bee5eb; color: #0c5460;"
                };

                banner = $@"
<div class='version-banner {message.Type}' style='{urgency} padding: 15px;
     margin: 10px 0; border: 1px solid; border-radius: 5px;'>
    <h3 style='margin-top: 0;'>{message.Title}</h3>
    <p>{message.Content}</p>";

                if (!string.IsNullOrEmpty(message.DownloadUrl))
                {
                    banner += $@"
    <a href='{message.DownloadUrl}' style='display: inline-block; padding: 8px 16px;
       background: #007bff; color: white; text-decoration: none; border-radius: 3px;
       margin-right: 10px;'>Download Update</a>";
                }

                banner += @"
</div>";
            }

            // Find a good injection point (after <body> or first <div>)
            var bodyIndex = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
            if (bodyIndex > 0)
            {
                var bodyCloseIndex = html.IndexOf(">", bodyIndex);
                if (bodyCloseIndex > 0)
                {
                    html = html.Insert(bodyCloseIndex + 1, banner);
                }
            }

            return html;
        }

        private string InjectAnnouncements(string html, List<Announcement> announcements)
        {
            // Filter announcements that should be shown
            var now = DateTime.UtcNow;
            var relevantAnnouncements = announcements.Where(a =>
                (!a.ShowUntil.HasValue || a.ShowUntil.Value > now) &&
                (a.TargetVersions.Count == 0 || a.TargetVersions.Any(v =>
                    VersionComparer.MatchesPattern(_updateService.CurrentAppVersion, v)))
            ).ToList();

            if (!relevantAnnouncements.Any())
                return html;

            var announcementsHtml = new StringBuilder();
            announcementsHtml.AppendLine(@"<div class='announcements' style='margin: 20px 0;'>");
            announcementsHtml.AppendLine("<h2>Recent Announcements</h2>");

            foreach (var announcement in relevantAnnouncements.OrderByDescending(a => a.Date))
            {
                announcementsHtml.AppendLine($@"
<div class='announcement' style='background: #f8f9fa; padding: 10px; margin: 10px 0;
     border-left: 3px solid #007bff;'>
    <h4>{announcement.Title} <small style='color: #6c757d;'>({announcement.Date:MMM dd, yyyy})</small></h4>
    <p>{announcement.Content}</p>
</div>");
            }

            announcementsHtml.AppendLine("</div>");

            // Find a good place to inject (before closing </body> or after main content)
            var bodyCloseIndex = html.LastIndexOf("</body>", StringComparison.OrdinalIgnoreCase);
            if (bodyCloseIndex > 0)
            {
                html = html.Insert(bodyCloseIndex, announcementsHtml.ToString());
            }

            return html;
        }

        private string InjectCriticalNotices(string html, List<CriticalNotice> notices)
        {
            // Check if any critical notices apply to current version
            var applicableNotices = notices.Where(n =>
                n.AffectedVersions.Any(v =>
                    VersionComparer.MatchesPattern(_updateService.CurrentAppVersion, v))
            ).ToList();

            if (!applicableNotices.Any())
                return html;

            var noticesHtml = new StringBuilder();
            foreach (var notice in applicableNotices)
            {
                noticesHtml.AppendLine($@"
<div class='alert critical' style='background: #f8d7da; border: 1px solid #f5c6cb;
     color: #721c24; padding: 15px; margin: 10px 0; border-radius: 5px;'>
    <strong>⚠️ Critical Notice</strong>
    <p>{notice.Message}</p>
</div>");
            }

            // Inject at the very top, right after <body>
            var bodyIndex = html.IndexOf("<body", StringComparison.OrdinalIgnoreCase);
            if (bodyIndex > 0)
            {
                var bodyCloseIndex = html.IndexOf(">", bodyIndex);
                if (bodyCloseIndex > 0)
                {
                    html = html.Insert(bodyCloseIndex + 1, noticesHtml.ToString());
                }
            }

            return html;
        }

        private string GetFallbackHtml()
        {
            return @"<!DOCTYPE html>
<html>
<head>
    <style>
        body {
            font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', sans-serif;
            padding: 40px;
            text-align: center;
            background: #f8f9fa;
        }
        h1 { color: #2e3440; }
        p { color: #4c566a; margin: 20px 0; }
    </style>
</head>
<body>
    <h1>🏛️ Chaṭṭha Saṅgāyana Tipiṭaka</h1>
    <p>Welcome to CST Reader Beta</p>
    <p>Select a book from the tree on the left to begin reading.</p>
</body>
</html>";
        }

        /// <summary>
        /// Updates the startup status message
        /// </summary>
        public void SetStartupStatus(string message)
        {
            StartupStatusMessage = message;
            IsStartupInProgress = !string.IsNullOrEmpty(message);
            Log.Debug("Welcome page startup status: {Status}", message);
        }

        /// <summary>
        /// Clears the startup status and hides the banner
        /// </summary>
        public void CompleteStartup()
        {
            IsStartupInProgress = false;
            StartupStatusMessage = "";
            Log.Information("Welcome page startup completed");
        }
    }
}