using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
    
    private readonly DispatcherTimer _timer;
    private double _opacityIncrement = 0.05;
    private double _targetProgress = 0.0;
    private double _currentProgress = 0.0;
    private readonly List<DateTime> _referencePoints = new();
    private readonly List<double> _storedIncrements = new();
    private DateTime _startTime;
    private bool _isClosing = false;
    private double _averageTickTime = 50.0; // milliseconds per progress increment
    private DispatcherTimer? _safetyTimer; // Auto-close timer for safety
    
    // Registry key for self-calibration data (similar to CST4)
    private static readonly string RegistryKeyPath = AppConstants.RegistryBasePath + "\\SplashScreen";
    
    public SplashScreen()
    {
        InitializeComponent();
        
        // Start transparent for fade-in effect
        Opacity = 0.0;
        
        // Load background image
        LoadBackgroundImage();
        
        // Initialize timer for animations
        _timer = new DispatcherTimer
        {
            Interval = TimeSpan.FromMilliseconds(50) // 20 FPS
        };
        _timer.Tick += Timer_Tick;
        
        // Initialize calibration data
        _startTime = DateTime.Now;
        ReadStoredIncrements();
        
        // On macOS, we need to handle the timer start more carefully
        if (OperatingSystem.IsMacOS())
        {
            // Delay timer start to ensure window is fully initialized
            Dispatcher.UIThread.Post(() => 
            {
                _timer.Start();
                _logger.Debug("Splash screen timer started (macOS delayed start)");
            }, DispatcherPriority.Background);
        }
        else
        {
            // Start fade-in animation immediately on other platforms
            _timer.Start();
            _logger.Debug("Splash screen created and fade-in timer started");
        }
        
        // Safety timer removed - splash screen stays open until explicitly closed
        // This allows for full downloads and indexing operations to complete
        _safetyTimer = null;
    }
    
    /// <summary>
    /// Shows the splash screen with platform-safe implementation
    /// </summary>
    public static void ShowSplashScreen()
    {
        lock (_lock)
        {
            if (_instance != null) return;
            
            try
            {
                _logger.Debug("Creating splash screen instance");
                
                // Ensure we're on the UI thread
                if (Dispatcher.UIThread.CheckAccess())
                {
                    CreateAndShowSplashScreen();
                }
                else
                {
                    // Post to UI thread if we're not already on it
                    Dispatcher.UIThread.Post(CreateAndShowSplashScreen, DispatcherPriority.Send);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to create splash screen instance");
            }
        }
    }
    
    private static void CreateAndShowSplashScreen()
    {
        try
        {
            _instance = new SplashScreen();
            
            // On macOS, we need to ensure the window is properly configured
            if (OperatingSystem.IsMacOS())
            {
                // Set window level to ensure it appears on top
                _instance.Topmost = true;
                
                // Ensure the window is activated
                _instance.Show();
                _instance.Activate();
                _instance.Focus();
            }
            else
            {
                _instance.Show();
            }
            
            _logger.Information("Splash screen displayed");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed in CreateAndShowSplashScreen");
        }
    }
    
    /// <summary>
    /// Closes the splash screen with fade-out effect
    /// </summary>
    public static void CloseForm()
    {
        _logger.Debug("Closing splash screen requested");
        
        try
        {
            if (_instance != null && Application.Current != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_instance != null && !_instance._isClosing)
                    {
                        _instance._isClosing = true;
                        _instance._opacityIncrement = -0.05; // Start fade-out
                        _instance.StoreIncrements(); // Save calibration data
                    }
                });
            }
        }
        catch (Exception ex)
        {
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
            // Only use dispatcher if we have an instance and Avalonia is initialized
            if (_instance != null && Application.Current != null)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    if (_instance?.StatusLabel != null)
                    {
                        _instance.StatusLabel.Text = status;
                        _logger.Debug("Status updated: {Status}", status);
                    }
                });
            }
            else
            {
                _logger.Debug("Status set to {Status} but no UI instance available", status);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to set splash screen status to {Status}", status);
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
        
        // Calculate target progress based on stored increments or estimate
        if (_storedIncrements.Count > 0 && _referencePoints.Count <= _storedIncrements.Count)
        {
            _targetProgress = _storedIncrements[_referencePoints.Count - 1] * 100.0;
        }
        else
        {
            // Fallback: estimate based on reference point count
            _targetProgress = Math.Min((_referencePoints.Count * 100.0) / 8.0, 95.0);
        }
        
        _logger.Debug("Progress reference point {PointNumber} set, target: {TargetProgress:F1}%", _referencePoints.Count, _targetProgress);
    }
    
    private void Timer_Tick(object? sender, EventArgs e)
    {
        try
        {
            // Handle fade in/out
            var newOpacity = Opacity + _opacityIncrement;
            
            if (_isClosing)
            {
                // Fade out
                if (newOpacity <= 0.0)
                {
                    _timer.Stop();
                    Close();
                    return;
                }
                Opacity = newOpacity;
            }
            else
            {
                // Fade in
                if (newOpacity >= 1.0)
                {
                    Opacity = 1.0;
                }
                else
                {
                    Opacity = newOpacity;
                }
            }
            
            // Progress bar and time remaining removed - no longer needed
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error in splash screen timer tick");
        }
    }
    
    // UpdateTimeRemaining method removed - no longer needed without progress bar
    
    private void LoadBackgroundImage()
    {
        try
        {
            // Load the CST4 Dhamma wheel splash screen image
            var imagePath = "avares://CST.Avalonia/Assets/cst-splash.png";
            var bitmap = new Bitmap(AssetLoader.Open(new Uri(imagePath)));
            
            if (BackgroundImage != null)
            {
                BackgroundImage.Source = bitmap;
            }
            
            _logger.Debug("Background image loaded successfully");
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Failed to load background image, using fallback color");
            // Fallback to solid background
            var background = new SolidColorBrush(Color.FromRgb(240, 240, 240));
            Background = background;
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
        _timer?.Stop();
        _safetyTimer?.Stop();
        base.OnClosed(e);
        
        lock (_lock)
        {
            _instance = null;
        }
        
        _logger.Debug("Splash screen closed and disposed");
    }
}