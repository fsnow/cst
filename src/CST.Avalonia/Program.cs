using Avalonia;
using Avalonia.Threading;
using System;
using System.IO;
using System.Threading.Tasks;
using CST.Avalonia.Views;
using Serilog;

namespace CST.Avalonia;

sealed class Program
{
    // Logger instance for Program class
    private static readonly ILogger _logger = Log.ForContext<Program>();
    
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    public static void Main(string[] args)
    {
        try
        {
            // Handle library conflicts on macOS by suppressing duplicate class warnings
            Environment.SetEnvironmentVariable("OBJC_DISABLE_INITIALIZE_FORK_SAFETY", "YES");

            // Check for browse-sharepoint command
            if (args.Length > 0 && args[0] == "--browse-sharepoint")
            {
                // Initialize Serilog for console output
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.Console()
                    .CreateLogger();

                SharePointBrowser.BrowseAndPrint().GetAwaiter().GetResult();
                return;
            }

            // Check for find-start-pages command
            if (args.Length > 0 && args[0] == "--find-start-pages")
            {
                // Initialize Serilog for console output
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.Console()
                    .CreateLogger();

                PdfStartPageFinder.FindAndPrintStartPages().GetAwaiter().GetResult();
                return;
            }

            // Check for download-tika-pdfs command
            if (args.Length > 0 && args[0] == "--download-tika-pdfs")
            {
                // Initialize Serilog for console output
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.Console()
                    .CreateLogger();

                TikaPdfDownloader.DownloadAndListPdfs().GetAwaiter().GetResult();
                return;
            }

            // Check for download-anya-pdfs command
            if (args.Length > 0 && args[0] == "--download-anya-pdfs")
            {
                // Initialize Serilog for console output
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
                    .WriteTo.Console()
                    .CreateLogger();

                AnyaPdfDownloader.DownloadAndListPdfs().GetAwaiter().GetResult();
                return;
            }

            // Build the Avalonia app without starting it yet
            var app = BuildAvaloniaApp();

            // Start the application with special handling for splash screen
            app.StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // No cleanup needed for WebView
            _logger.Information("Application shutting down");
        }
    }
    
    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .AfterSetup(_ => 
            {
                _logger.Information("WebView-based CST application starting");
            });
    }
}