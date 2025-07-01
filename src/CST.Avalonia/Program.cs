using Avalonia;
using System;
using System.IO;
using Xilium.CefGlue;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Shared;
using CST.Avalonia.Services;

namespace CST.Avalonia;

sealed class Program
{
    // Initialization code. Don't use any Avalonia, third-party APIs or any
    // SynchronizationContext-reliant code before AppMain is called: things aren't initialized
    // yet and stuff might break.
    [STAThread]
    public static void Main(string[] args)
    {
        try
        {
            BuildAvaloniaApp().StartWithClassicDesktopLifetime(args);
        }
        finally
        {
            // Cleanup CefGlue resources
            try
            {
                CefRuntime.Shutdown();
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during CefGlue shutdown: {ex.Message}");
            }
        }
    }

    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        // Generate unique cache path for CefGlue
        var cachePath = Path.Combine(Path.GetTempPath(), "CST_CefGlue_" + Guid.NewGuid().ToString().Replace("-", ""));
        
        // Set up process exit cleanup
        AppDomain.CurrentDomain.ProcessExit += delegate { Cleanup(cachePath); };
        
        return AppBuilder.Configure<App>()
            .UsePlatformDetect()
            .WithInterFont()
            .LogToTrace()
            .AfterSetup(_ => 
            {
                try
                {
                    // Initialize CefGlue with proper settings
                    var settings = new CefSettings() 
                    {
                        RootCachePath = cachePath,
                        WindowlessRenderingEnabled = false,
                        LogSeverity = CefLogSeverity.Warning,
                        LogFile = Path.Combine(cachePath, "cef.log")
                    };
                    
                    CefRuntimeLoader.Initialize(settings);
                    
                    // Register the custom scheme handler factory after initialization
                    // This allows us to serve large HTML content without URL length limitations
                    CefRuntime.RegisterSchemeHandlerFactory(
                        CstSchemeHandlerFactory.SchemeName,
                        CstSchemeHandlerFactory.DomainName,
                        new CstSchemeHandlerFactory()
                    );
                    
                    Console.WriteLine($"CefGlue initialized successfully with cache path: {cachePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to initialize CefGlue: {ex.Message}");
                    // Continue without CefGlue - fallback text display will be used
                }
            });
    }
    
    private static void Cleanup(string cachePath)
    {
        try
        {
            CefRuntime.Shutdown();
            
            // Clean up cache directory
            if (Directory.Exists(cachePath))
            {
                Directory.Delete(cachePath, true);
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during cleanup: {ex.Message}");
        }
    }
}
