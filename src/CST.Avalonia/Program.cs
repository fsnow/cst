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

            // Headless MCP bridge: relay this process's STDIO to the running app's /mcp (#278). Handled FIRST,
            // before any UI/logging init. STDOUT is the JSON-RPC stream, so logs go to STDERR only; no GUI.
            if (args.Length > 0 && args[0] == "--mcp-bridge")
            {
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Warning()
                    .WriteTo.Console(standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose)
                    .CreateLogger();

                var handshakeDir = System.IO.Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    CST.Avalonia.Constants.AppConstants.AppDataDirectoryName);

                CST.Avalonia.Services.LocalApi.Mcp.McpBridge
                    .RunFromStdioAsync(handshakeDir, System.Threading.CancellationToken.None)
                    .GetAwaiter().GetResult();
                return;
            }

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

            // Single-instance guard (#289): only one GUI per data directory. The --mcp-bridge relay and the CLI
            // utility flags above have already returned, so this gates only a real GUI launch. Two instances would
            // clobber the shared settings / app-state / index and race on local-api.json.
            var appDataDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                CST.Avalonia.Constants.AppConstants.AppDataDirectoryName);
            if (!SingleInstanceGuard.TryAcquire(appDataDir))
            {
                _logger.Information("Another CST Reader instance already owns {Dir}; activating it and exiting.", appDataDir);
                SingleInstanceGuard.ActivateRunningInstance();
                return;
            }

            // Build the Avalonia app without starting it yet
            var app = BuildAvaloniaApp();

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