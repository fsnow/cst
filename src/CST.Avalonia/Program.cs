using Avalonia;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Xilium.CefGlue;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Shared;
using CST.Avalonia.Services;
using CST.Avalonia.Views;

namespace CST.Avalonia;

sealed class Program
{
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

    private static string? _currentCachePath;
    
    // Avalonia configuration, don't remove; also used by visual designer.
    public static AppBuilder BuildAvaloniaApp()
    {
        bool showSplash = !OperatingSystem.IsMacOS();
        
        if (showSplash)
        {
            SplashScreen.SetStatus("Initializing application...");
            SplashScreen.SetReferencePoint();
        }
        
        // Clean up orphaned cache directories first
        CleanupOrphanedCacheDirectories();
        
        if (showSplash)
        {
            SplashScreen.SetStatus("Setting up cache directories...");
            SplashScreen.SetReferencePoint();
        }
        
        // Create a unique cache directory for this instance with process ID
        var processId = Environment.ProcessId;
        _currentCachePath = Path.Combine(Path.GetTempPath(), $"CST_CefGlue_{processId}_{Guid.NewGuid().ToString("N")[..8]}");
        
        // Set up process exit cleanup
        AppDomain.CurrentDomain.ProcessExit += delegate { Cleanup(_currentCachePath); };
        
        if (showSplash)
        {
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
                    SplashScreen.SetStatus("Initializing browser engine...");
                    SplashScreen.SetReferencePoint();
                }
                
                try
                {
                    // Initialize CefGlue with proper settings
                    var settings = new CefSettings() 
                    {
                        RootCachePath = _currentCachePath,
                        WindowlessRenderingEnabled = false,
                        LogSeverity = CefLogSeverity.Warning,
                        LogFile = Path.Combine(_currentCachePath!, "cef.log")
                    };
                    
                    // Platform-specific settings
                    if (OperatingSystem.IsMacOS())
                    {
                        // On macOS, let CefGlue handle the message loop
                        settings.MultiThreadedMessageLoop = true;
                        settings.ExternalMessagePump = false;
                    }
                    else
                    {
                        // On Windows/Linux, use external message pump
                        settings.MultiThreadedMessageLoop = false;
                        settings.ExternalMessagePump = true;
                    }
                    
                    CefRuntimeLoader.Initialize(settings);
                    
                    if (showSplash)
                    {
                        SplashScreen.SetStatus("Registering custom handlers...");
                        SplashScreen.SetReferencePoint();
                    }
                    
                    // Register the custom scheme handler factory after initialization
                    // This allows us to serve large HTML content without URL length limitations
                    CefRuntime.RegisterSchemeHandlerFactory(
                        CstSchemeHandlerFactory.SchemeName,
                        CstSchemeHandlerFactory.DomainName,
                        new CstSchemeHandlerFactory()
                    );
                    
                    Console.WriteLine($"CefGlue initialized successfully with cache path: {_currentCachePath}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to initialize CefGlue: {ex.Message}");
                    // Continue without CefGlue - fallback text display will be used
                }
                
                if (showSplash)
                {
                    SplashScreen.SetStatus("Loading main application...");
                    SplashScreen.SetReferencePoint();
                }
            });
    }
    
    private static void CleanupOrphanedCacheDirectories()
    {
        try
        {
            var tempPath = Path.GetTempPath();
            var cacheDirectories = Directory.GetDirectories(tempPath, "CST_CefGlue_*");
            
            var orphanedDirs = new List<string>();
            var activeDirs = new List<string>();
            
            foreach (var dir in cacheDirectories)
            {
                var dirName = Path.GetFileName(dir);
                
                // Parse process ID from directory name (format: CST_CefGlue_{processId}_{guid})
                var parts = dirName.Split('_');
                if (parts.Length >= 3 && int.TryParse(parts[2], out var processId))
                {
                    // Check if the process is still running
                    try
                    {
                        var process = System.Diagnostics.Process.GetProcessById(processId);
                        if (process != null && !process.HasExited)
                        {
                            activeDirs.Add(dirName);
                            continue; // Process is still running, don't clean up
                        }
                    }
                    catch (ArgumentException)
                    {
                        // Process doesn't exist anymore
                    }
                    catch (Exception)
                    {
                        // Any other error means we can't verify the process, assume it's orphaned
                    }
                }
                
                // This directory is orphaned (process no longer exists or invalid format)
                orphanedDirs.Add(dir);
            }
            
            Console.WriteLine($"Found {cacheDirectories.Length} CefGlue cache directories: {activeDirs.Count} active, {orphanedDirs.Count} orphaned");
            
            foreach (var dir in orphanedDirs)
            {
                try
                {
                    Directory.Delete(dir, true);
                    Console.WriteLine($"Cleaned up orphaned cache directory: {Path.GetFileName(dir)}");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Could not delete orphaned cache directory {Path.GetFileName(dir)}: {ex.Message}");
                }
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during orphaned cache cleanup: {ex.Message}");
        }
    }
    
    private static void Cleanup(string cachePath)
    {
        try
        {
            CefRuntime.Shutdown();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error during CefGlue shutdown: {ex.Message}");
        }
        
        try
        {
            // Clean up cache directory (don't fail if it's in use)
            if (Directory.Exists(cachePath))
            {
                Directory.Delete(cachePath, true);
                Console.WriteLine($"Cleaned up cache directory: {Path.GetFileName(cachePath)}");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Could not delete cache directory {Path.GetFileName(cachePath)}: {ex.Message}");
            // Don't fail the application if we can't clean up the cache
        }
    }
}
