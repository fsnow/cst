using Avalonia;
using System;
using System.IO;
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
            
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
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
        bool showSplash = !OperatingSystem.IsMacOS();
        
        if (showSplash)
        {
            SplashScreen.SetStatus("Initializing application...");
            SplashScreen.SetReferencePoint();

            SplashScreen.SetStatus("Configuring UI framework...");
            SplashScreen.SetReferencePoint();
        }
        
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .AfterSetup(_ => 
            {
                if (showSplash)
                {
                    SplashScreen.SetStatus("Loading main application...");
                    SplashScreen.SetReferencePoint();
                }
                
                _logger.Information("WebView-based CST application starting");
            });
    }
}