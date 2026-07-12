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
    // A PROPERTY, not a field captured at static-init: at type-load Log.Logger is still Serilog's silent default,
    // so a captured contextual logger would swallow every Program-level message forever (the guard's "another
    // instance owns {dir}", the "shutting down" line). Re-resolving each use picks up the bootstrap logger set in
    // Main and, later, App DI's full logger. (#316 A6-3)
    private static ILogger Logger => Log.ForContext<Program>();
    
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
                // Information (not Warning): the bridge's attach/launch/watcher-exit breadcrumbs are the exact
                // trace needed to debug launch-or-attach, and stderr-only keeps the JSON-RPC stdout clean. (#307 A2-7)
                Log.Logger = new LoggerConfiguration()
                    .MinimumLevel.Information()
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

            // Bootstrap logger for the GUI path: until App DI installs the full logger, Log.Logger is still the
            // silent default, so the guard / blocked-launch / early-startup lines would vanish. Install a minimal
            // one here (App DI replaces it). Goes to stderr — this path never carries the bridge's JSON-RPC
            // stdout (that returned above). (#316 A6-3)
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console(standardErrorFromLevel: Serilog.Events.LogEventLevel.Verbose)
                .CreateLogger();

            // Single-instance guard (#289): only one GUI per data directory. The --mcp-bridge relay and the CLI
            // utility flags above have already returned, so this gates only a real GUI launch. Two instances would
            // clobber the shared settings / app-state / index and race on local-api.json.
            var appDataDir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                CST.Avalonia.Constants.AppConstants.AppDataDirectoryName);
            if (!SingleInstanceGuard.TryAcquire(appDataDir))
            {
                Logger.Information("Another CST Reader instance already owns {Dir}; activating it and exiting.", appDataDir);
                SingleInstanceGuard.ActivateRunningInstance();
                return;
            }

            // Build the Avalonia app without starting it yet
            var app = BuildAvaloniaApp();

            app.StartWithClassicDesktopLifetime(args);
        }
        catch (Exception ex)
        {
            // Last-resort: an early-startup failure (e.g. the single-instance guard on an unwritable data dir)
            // would otherwise be an unhandled crash with no window and — if it happened before logging init — no
            // log. Make it visible on stderr, and log if Serilog is up. (#315 A6-2)
            Console.Error.WriteLine($"CST Reader failed to start: {ex}");
            try { Serilog.Log.Fatal(ex, "Fatal startup error"); } catch { /* logging may not be configured yet */ }
            throw;
        }
        finally
        {
            // No cleanup needed for WebView
            Logger.Information("Application shutting down");
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
                Logger.Information("WebView-based CST application starting");
            });
    }
}