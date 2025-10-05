using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using Microsoft.Win32;
using Serilog;
using CST.Avalonia.Constants;

namespace CST.Avalonia.Views;

/// <summary>
/// Splash screen window that shows during application startup
/// Port of CST4's SplashScreen.cs with Avalonia implementation
/// </summary>
public partial class SplashScreen : Window
{
    private static SplashScreen? _instance;
    private static readonly object _lock = new object();
    private static readonly Serilog.ILogger _logger = Log.ForContext<SplashScreen>();
    private static readonly Queue<string> _pendingStatusMessages = new Queue<string>();
    private static string? _lastStatus = null;

    // Simple debug logging that works before Serilog is initialized
    private static void DebugLog(string message)
    {
        try
        {
            var debugLogPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "CSTReader",
                "debug-splash.log"
            );
            var directory = Path.GetDirectoryName(debugLogPath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            var timestamp = DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss.fff");
            File.AppendAllText(debugLogPath, $"{timestamp} {message}\n");
        }
        catch
        {
            // Silently fail if we can't write to the debug log
        }
    }
    
    private readonly List<DateTime> _referencePoints = new();
    private readonly List<double> _storedIncrements = new();
    private DateTime _startTime;
    private bool _isClosing = false;
    private double _averageTickTime = 50.0; // milliseconds per progress increment
    
    // Registry key for self-calibration data (similar to CST4)
    private static readonly string RegistryKeyPath = AppConstants.RegistryBasePath + "\\SplashScreen";
    
    public SplashScreen()
    {
        DebugLog("SplashScreen constructor - Entry");
        try
        {
            _logger.Information("SplashScreen constructor started");
            DebugLog("SplashScreen constructor - About to call InitializeComponent");
            InitializeComponent();
            DebugLog("SplashScreen constructor - InitializeComponent completed");
            _logger.Information("SplashScreen InitializeComponent completed");
        }
        catch (Exception ex)
        {
            DebugLog($"SplashScreen constructor - Exception during InitializeComponent: {ex.GetType().Name} - {ex.Message}");
            DebugLog($"SplashScreen constructor - Stack trace: {ex.StackTrace}");
            throw;
        }

        // Start with full opacity - no fade-in animation needed
        Opacity = 1.0;
        DebugLog("SplashScreen constructor - Opacity set to 1.0");

        // Load background image
        LoadBackgroundImage();

        // Initialize calibration data
        _startTime = DateTime.Now;
        ReadStoredIncrements();

        DebugLog("SplashScreen constructor - Initialization complete");
    }
    
    /// <summary>
    /// Shows the splash screen with platform-safe implementation
    /// </summary>
    public static void ShowSplashScreen()
    {
        DebugLog("ShowSplashScreen() - Entry point");
        try
        {
            if (_logger != null)
            {
                _logger.Information("ShowSplashScreen() called");
                DebugLog("ShowSplashScreen() - Logger available, logged to Serilog");
            }
            else
            {
                DebugLog("ShowSplashScreen() called - logger is null");
                Console.WriteLine("ShowSplashScreen() called - logger is null");
            }
        }
        catch (Exception logEx)
        {
            DebugLog($"ShowSplashScreen() - Logging failed: {logEx.Message}");
            Console.WriteLine($"ShowSplashScreen() - Logging failed: {logEx.Message}");
        }

        DebugLog("ShowSplashScreen() - About to acquire lock");
        lock (_lock)
        {
            DebugLog("ShowSplashScreen() - Lock acquired");
            try
            {
                if (_logger != null)
                    _logger.Information("ShowSplashScreen() acquired lock");
                else
                    Console.WriteLine("ShowSplashScreen() acquired lock - logger is null");
            }
            catch (Exception logEx)
            {
                DebugLog($"ShowSplashScreen() - Lock logging failed: {logEx.Message}");
                Console.WriteLine($"ShowSplashScreen() - Lock logging failed: {logEx.Message}");
            }

            if (_instance != null)
            {
                try
                {
                    DebugLog("ShowSplashScreen() - _instance is not null, returning early");
                    _logger?.Warning("ShowSplashScreen() - _instance is not null, returning early");
                }
                catch { }
                return;
            }

            try
            {
                if (_logger != null)
                    _logger.Information("ShowSplashScreen() - _instance is null, proceeding");
                else
                    Console.WriteLine("ShowSplashScreen() - _instance is null, proceeding");
            }
            catch { }

            try
            {
                DebugLog("ShowSplashScreen() - About to create splash screen");
                if (_logger != null)
                    _logger.Debug("Creating splash screen instance");
                else
                    Console.WriteLine("Creating splash screen instance");

                // Ensure we're on the UI thread
                DebugLog("ShowSplashScreen() - Checking UI thread");
                bool onUiThread = Dispatcher.UIThread.CheckAccess();
                DebugLog($"ShowSplashScreen() - On UI thread: {onUiThread}");

                if (_logger != null)
                    _logger.Information("ShowSplashScreen() - On UI thread: {OnUiThread}", onUiThread);
                else
                    Console.WriteLine($"ShowSplashScreen() - On UI thread: {onUiThread}");

                if (onUiThread)
                {
                    DebugLog("ShowSplashScreen() - Calling CreateAndShowSplashScreen directly");
                    if (_logger != null)
                        _logger.Information("ShowSplashScreen() - Calling CreateAndShowSplashScreen directly");
                    else
                        Console.WriteLine("ShowSplashScreen() - Calling CreateAndShowSplashScreen directly");
                    CreateAndShowSplashScreen();
                    DebugLog("ShowSplashScreen() - CreateAndShowSplashScreen returned");
                }
                else
                {
                    DebugLog("ShowSplashScreen() - Posting CreateAndShowSplashScreen to UI thread");
                    if (_logger != null)
                        _logger.Information("ShowSplashScreen() - Posting CreateAndShowSplashScreen to UI thread");
                    else
                        Console.WriteLine("ShowSplashScreen() - Posting CreateAndShowSplashScreen to UI thread");
                    // Post to UI thread if we're not already on it
                    Dispatcher.UIThread.Post(CreateAndShowSplashScreen, DispatcherPriority.Send);
                    DebugLog("ShowSplashScreen() - Posted to UI thread");
                }

                DebugLog("ShowSplashScreen() - Completed without exceptions");
                if (_logger != null)
                    _logger.Information("ShowSplashScreen() - Completed without exceptions");
                else
                    Console.WriteLine("ShowSplashScreen() - Completed without exceptions");
            }
            catch (Exception ex)
            {
                DebugLog($"ShowSplashScreen() - Exception: {ex.GetType().Name} - {ex.Message}");
                DebugLog($"ShowSplashScreen() - Stack trace: {ex.StackTrace}");
                try
                {
                    _logger?.Error(ex, "Failed to create splash screen instance - Exception details: {Message}", ex.Message);
                }
                catch { }
                Console.WriteLine($"Failed to create splash screen instance: {ex.GetType().Name} - {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
        }

        try
        {
            if (_logger != null)
                _logger.Information("ShowSplashScreen() - Exiting method");
            else
                Console.WriteLine("ShowSplashScreen() - Exiting method");
        }
        catch { }
    }
    
    private static void CreateAndShowSplashScreen()
    {
        DebugLog("CreateAndShowSplashScreen() - Entry");
        try
        {
            DebugLog("CreateAndShowSplashScreen: About to create SplashScreen instance");
            _logger.Information("CreateAndShowSplashScreen: Creating new SplashScreen instance");
            _instance = new SplashScreen();
            DebugLog("CreateAndShowSplashScreen: SplashScreen instance created successfully");
            _logger.Information("CreateAndShowSplashScreen: SplashScreen instance created successfully");

            // On macOS, we need to ensure the window is properly configured
            if (OperatingSystem.IsMacOS())
            {
                DebugLog("CreateAndShowSplashScreen: Configuring for macOS");
                _logger.Information("CreateAndShowSplashScreen: Configuring for macOS");

                // Make it stay above the main app window, but allow other apps to come forward
                _instance.Topmost = true;
                DebugLog("CreateAndShowSplashScreen: Topmost set to true for app-level priority");

                // Show in taskbar so it's a normal window
                _instance.ShowInTaskbar = true;
                _instance.WindowState = WindowState.Normal;

                // Ensure the window is activated
                DebugLog("CreateAndShowSplashScreen: About to call Show()");
                _instance.Show();
                DebugLog("CreateAndShowSplashScreen: macOS Show() called");
                _logger.Information("CreateAndShowSplashScreen: macOS Show() called");

                DebugLog("CreateAndShowSplashScreen: About to call Activate()");
                _instance.Activate();
                DebugLog("CreateAndShowSplashScreen: macOS Activate() called");
                _logger.Information("CreateAndShowSplashScreen: macOS Activate() called");

                // Don't force focus - let user switch windows if needed
                // _instance.Focus();
                // _instance.BringIntoView();
                DebugLog("CreateAndShowSplashScreen: Window shown without forcing topmost");
            }
            else
            {
                DebugLog("CreateAndShowSplashScreen: Showing for non-macOS platform");
                _logger.Information("CreateAndShowSplashScreen: Showing for non-macOS platform");
                _instance.Show();
            }

            DebugLog("CreateAndShowSplashScreen: Splash screen displayed successfully");
            _logger.Information("Splash screen displayed successfully");

            // Process any queued status messages
            ProcessQueuedStatusMessages();
        }
        catch (Exception ex)
        {
            DebugLog($"CreateAndShowSplashScreen: Exception: {ex.GetType().Name} - {ex.Message}");
            DebugLog($"CreateAndShowSplashScreen: Stack trace: {ex.StackTrace}");
            _logger.Error(ex, "Failed in CreateAndShowSplashScreen");
        }
        DebugLog("CreateAndShowSplashScreen() - Exit");
    }

    private static void ProcessQueuedStatusMessages()
    {
        try
        {
            lock (_lock)
            {
                // Process any messages that were queued before the splash screen was ready
                if (_pendingStatusMessages.Count > 0)
                {
                    DebugLog($"Processing {_pendingStatusMessages.Count} queued status messages");
                    while (_pendingStatusMessages.Count > 0)
                    {
                        var status = _pendingStatusMessages.Dequeue();
                        DebugLog($"Processing queued status: '{status}'");
                    }
                }

                // Set the last status message
                if (_lastStatus != null && _instance?.StatusLabel != null)
                {
                    _instance.StatusLabel.Text = _lastStatus;
                    DebugLog($"Set status to last queued message: '{_lastStatus}'");
                    _logger.Debug("Applied last queued status: {Status}", _lastStatus);

                    // Force UI update
                    _instance.StatusLabel.InvalidateVisual();
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog($"ProcessQueuedStatusMessages: Exception - {ex.Message}");
            _logger.Error(ex, "Failed to process queued status messages");
        }
    }
    
    /// <summary>
    /// Force closes the splash screen - must be called from UI thread
    /// </summary>
    public static void ForceClose()
    {
        DebugLog("ForceClose() called");
        try
        {
            lock (_lock)
            {
                if (_instance != null)
                {
                    DebugLog("ForceClose: Closing window on UI thread");

                    // Must be on UI thread to close window
                    if (Dispatcher.UIThread.CheckAccess())
                    {
                        try
                        {
                            _instance.Close();
                            DebugLog("ForceClose: Window closed successfully");
                        }
                        catch (Exception closeEx)
                        {
                            DebugLog($"ForceClose: Close() exception - {closeEx.Message}");
                        }
                    }
                    else
                    {
                        DebugLog("ForceClose: Not on UI thread - posting to UI thread");
                        Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                _instance?.Close();
                                DebugLog("ForceClose: Window closed via UI thread post");
                            }
                            catch (Exception ex)
                            {
                                DebugLog($"ForceClose: UI thread close exception - {ex.Message}");
                            }
                        });
                    }

                    _instance = null;
                    DebugLog("ForceClose: Instance cleared");
                }
                else
                {
                    DebugLog("ForceClose: No instance to close");
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog($"ForceClose: Exception - {ex.Message}");
            _logger.Error(ex, "Failed to force close splash screen");
        }
    }

    public static void CloseForm()
    {
        DebugLog("CloseForm() called");
        _logger.Debug("Closing splash screen requested");

        try
        {
            lock (_lock)
            {
                if (_instance != null && !_instance._isClosing)
                {
                    DebugLog("CloseForm: Starting close sequence");
                    _instance._isClosing = true;
                    _instance.StoreIncrements(); // Save calibration data

                    // Close immediately - no fade animation
                    // Must be on UI thread
                    if (Dispatcher.UIThread.CheckAccess())
                    {
                        DebugLog("CloseForm: On UI thread, closing directly");
                        try
                        {
                            _instance.Close();
                            DebugLog("CloseForm: Window closed successfully");
                        }
                        catch (Exception closeEx)
                        {
                            DebugLog($"CloseForm: Close() exception - {closeEx.Message}");
                        }
                        _instance = null;
                    }
                    else
                    {
                        DebugLog("CloseForm: Not on UI thread - posting close");
                        Dispatcher.UIThread.Post(() =>
                        {
                            try
                            {
                                if (_instance != null)
                                {
                                    _instance.Close();
                                    DebugLog("CloseForm: Window closed via UI thread post");
                                    _instance = null;
                                }
                            }
                            catch (Exception ex)
                            {
                                DebugLog($"CloseForm: UI thread close exception - {ex.Message}");
                            }
                        });
                    }
                }
                else
                {
                    DebugLog($"CloseForm: Already closing or no instance - _isClosing={_instance?._isClosing}");
                }
            }
        }
        catch (Exception ex)
        {
            DebugLog($"CloseForm: Exception - {ex.Message}");
            _logger.Error(ex, "Failed to close splash screen");
        }
    }
    
    /// <summary>
    /// Updates the status text on the splash screen
    /// </summary>
    public static void SetStatus(string status)
    {
        try
        {
            lock (_lock)
            {
                _lastStatus = status;

                // Only update if we have an instance and Avalonia is initialized
                if (_instance != null && Application.Current != null)
                {
                    // Update synchronously if on UI thread, otherwise use InvokeAsync with wait
                    if (Dispatcher.UIThread.CheckAccess())
                    {
                        // On UI thread - update directly
                        if (_instance.StatusLabel != null)
                        {
                            _instance.StatusLabel.Text = status;
                            DebugLog($"SetStatus: Updated directly to '{status}'");
                            _logger.Debug("Status updated: {Status}", status);
                        }
                    }
                    else
                    {
                        // Not on UI thread - use InvokeAsync and WAIT for it to complete
                        // This ensures the update actually happens in packaged apps
                        try
                        {
                            var updateTask = Dispatcher.UIThread.InvokeAsync(() =>
                            {
                                if (_instance?.StatusLabel != null)
                                {
                                    _instance.StatusLabel.Text = status;
                                    DebugLog($"SetStatus: Updated via InvokeAsync to '{status}'");
                                    _logger.Debug("Status updated: {Status}", status);
                                }
                            }, DispatcherPriority.Send);

                            // Wait for the update to complete (with timeout)
                            updateTask.Wait(TimeSpan.FromMilliseconds(100));
                        }
                        catch (Exception ex)
                        {
                            DebugLog($"SetStatus: InvokeAsync failed - {ex.Message}");
                        }
                    }
                }
                else
                {
                    // Queue the message for later
                    _pendingStatusMessages.Enqueue(status);
                    _logger.Debug("Status queued (no UI instance yet): {Status}", status);
                    DebugLog($"SetStatus: Queued '{status}' (instance not ready)");
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to set splash screen status to {Status}", status);
            DebugLog($"SetStatus: Exception - {ex.Message}");
        }
    }
    
    /// <summary>
    /// Sets a reference point for self-calibrating progress bar (like CST4)
    /// </summary>
    public static void SetReferencePoint()
    {
        try
        {
            if (_instance != null && Application.Current != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    _instance?.SetReferenceInternal();
                });
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to set reference point for progress calibration");
        }
    }
    
    private void SetReferenceInternal()
    {
        _referencePoints.Add(DateTime.Now);
        _logger.Debug("Progress reference point {PointNumber} set", _referencePoints.Count);
    }
    
    private void LoadBackgroundImage()
    {
        try
        {
            DebugLog("LoadBackgroundImage: Starting");
            // Load the CST4 Dhamma wheel splash screen image
            var imagePath = "avares://CST.Avalonia/Assets/cst-splash.png";
            _logger.Information("Loading splash screen background image: {ImagePath}", imagePath);
            DebugLog($"LoadBackgroundImage: Attempting to load {imagePath}");

            var bitmap = new Bitmap(AssetLoader.Open(new Uri(imagePath)));
            _logger.Information("Background image bitmap created successfully - Size: {Width}x{Height}",
                bitmap.PixelSize.Width, bitmap.PixelSize.Height);
            DebugLog($"LoadBackgroundImage: Bitmap created - Size: {bitmap.PixelSize.Width}x{bitmap.PixelSize.Height}");

            if (BackgroundImage != null)
            {
                BackgroundImage.Source = bitmap;
                _logger.Information("Background image assigned to BackgroundImage control");
                DebugLog("LoadBackgroundImage: Image assigned to control");
            }
            else
            {
                _logger.Warning("BackgroundImage control is null - cannot assign bitmap");
                DebugLog("LoadBackgroundImage: WARNING - BackgroundImage control is null");
            }

            _logger.Information("Background image loaded successfully");
            DebugLog("LoadBackgroundImage: Completed successfully");
        }
        catch (Exception ex)
        {
            DebugLog($"LoadBackgroundImage: Exception - {ex.GetType().Name}: {ex.Message}");
            _logger.Error(ex, "Failed to load background image from avares://CST.Avalonia/Assets/cst-splash.png, using fallback color");

            // Fallback to solid background - make it visible!
            DebugLog("LoadBackgroundImage: Setting fallback solid color background");
            var fallbackBrush = new SolidColorBrush(Color.FromRgb(100, 150, 200)); // Blue color for visibility
            Background = fallbackBrush;

            // Also make the window opaque so we can see it
            if (this.TransparencyLevelHint != null && this.TransparencyLevelHint.Any(t => t == WindowTransparencyLevel.Transparent))
            {
                DebugLog("LoadBackgroundImage: Disabling transparency for visibility");
                this.TransparencyLevelHint = new[] { WindowTransparencyLevel.None };
            }
        }
    }
    
    private void ReadStoredIncrements()
    {
        try
        {
            // Read calibration data from registry (Windows) or equivalent storage
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.OpenSubKey(RegistryKeyPath);
                if (key != null)
                {
                    var incrementsData = key.GetValue("Increments") as string;
                    var tickTimeData = key.GetValue("AverageTickTime") as string;
                    
                    if (!string.IsNullOrEmpty(incrementsData))
                    {
                        var parts = incrementsData.Split(',');
                        foreach (var part in parts)
                        {
                            if (double.TryParse(part, out var value))
                            {
                                _storedIncrements.Add(value);
                            }
                        }
                    }
                    
                    if (!string.IsNullOrEmpty(tickTimeData) && double.TryParse(tickTimeData, out var tickTime))
                    {
                        _averageTickTime = tickTime;
                    }
                    
                    _logger.Debug("Loaded {Count} stored progress increments for calibration", _storedIncrements.Count);
                }
            }
            else
            {
                // On non-Windows platforms, use default increments
                // Could implement file-based storage in the future
                _logger.Debug("Non-Windows platform detected, using default progress increments");
                
                // Add some default increments for smoother progress
                _storedIncrements.AddRange(new[] { 0.1, 0.2, 0.3, 0.5, 0.7, 0.85, 0.95, 1.0 });
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to read stored progress increments, using defaults");
        }
    }
    
    private void StoreIncrements()
    {
        try
        {
            if (_referencePoints.Count < 2) return;
            
            var totalTime = (_referencePoints[_referencePoints.Count - 1] - _startTime).TotalMilliseconds;
            var increments = new List<double>();
            
            // Calculate cumulative percentages for each reference point
            for (int i = 0; i < _referencePoints.Count; i++)
            {
                var timeAtPoint = (_referencePoints[i] - _startTime).TotalMilliseconds;
                var percentage = timeAtPoint / totalTime;
                increments.Add(percentage);
            }
            
            // Store in registry (Windows) or equivalent
            if (OperatingSystem.IsWindows())
            {
                using var key = Registry.CurrentUser.CreateSubKey(RegistryKeyPath);
                var incrementsData = string.Join(",", increments);
                key.SetValue("Increments", incrementsData);
                key.SetValue("AverageTickTime", _averageTickTime.ToString());
                
                _logger.Debug("Stored {Count} progress increments for next launch", increments.Count);
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to store progress increments for next launch");
        }
    }
    
    protected override void OnClosed(EventArgs e)
    {
        base.OnClosed(e);

        lock (_lock)
        {
            _instance = null;
        }

        _logger.Debug("Splash screen closed and disposed");
        DebugLog("Splash screen OnClosed - instance cleared");
    }
}