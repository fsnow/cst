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