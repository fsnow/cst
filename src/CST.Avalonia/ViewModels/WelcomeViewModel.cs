using System;
using System.IO;
using System.Reflection;
using System.Threading.Tasks;
using ReactiveUI;
using Serilog;

namespace CST.Avalonia.ViewModels
{
    public class WelcomeViewModel : ViewModelBase
    {
        private string _htmlContent = "";

        public string HtmlContent
        {
            get => _htmlContent;
            set => this.RaiseAndSetIfChanged(ref _htmlContent, value);
        }

        public WelcomeViewModel()
        {
            // Load HTML content on initialization
            _ = LoadWelcomeContentAsync();
        }

        private async Task LoadWelcomeContentAsync()
        {
            try
            {
                Log.Information("WelcomeViewModel: Starting to load welcome content");

                // First try to load from file system (development scenario)
                var devFilePath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Resources", "welcome-content.html");
                Log.Debug("WelcomeViewModel: Checking for file at {Path}", devFilePath);

                if (File.Exists(devFilePath))
                {
                    HtmlContent = await File.ReadAllTextAsync(devFilePath);
                    Log.Information("WelcomeViewModel: HTML loaded from file: {FilePath}, length: {Length}",
                        devFilePath, HtmlContent.Length);
                    return;
                }

                // Then try to load from embedded resource (installed application scenario)
                var assembly = Assembly.GetExecutingAssembly();
                var resourceName = "CST.Avalonia.Resources.welcome-content.html";

                using var stream = assembly.GetManifestResourceStream(resourceName);
                if (stream != null)
                {
                    using var reader = new StreamReader(stream);
                    HtmlContent = await reader.ReadToEndAsync();
                    Log.Information("Welcome page HTML loaded from embedded resource");
                    return;
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
                        HtmlContent = await File.ReadAllTextAsync(path);
                        Log.Information("Welcome page HTML loaded from alternative path: {FilePath}", path);
                        return;
                    }
                }

                // Fallback to simple HTML if nothing found
                Log.Warning("Welcome page resource not found in any location, using fallback HTML");
                HtmlContent = GetFallbackHtml();

                // TODO: In future, try to fetch updated content from GitHub
                // and cache it locally for offline use
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load welcome page content");
                HtmlContent = GetFallbackHtml();
            }
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
    }
}