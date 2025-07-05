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

namespace CST.Avalonia.Views;

/// <summary>
/// Splash screen window that shows during application startup
/// Port of CST4's SplashScreen.cs with Avalonia implementation
/// </summary>
public partial class SplashScreen : Window
{
    private static SplashScreen? _instance;
    private static readonly object _lock = new object();
    
    private readonly DispatcherTimer _timer;
    private double _opacityIncrement = 0.05;
    private double _targetProgress = 0.0;
    private double _currentProgress = 0.0;
    private readonly List<DateTime> _referencePoints = new();
    private readonly List<double> _storedIncrements = new();
    private DateTime _startTime;
    private bool _isClosing = false;
    private double _averageTickTime = 50.0; // milliseconds per progress increment
    
    // Registry key for self-calibration data (similar to CST4)
    private const string RegistryKeyPath = @"SOFTWARE\CST\Avalonia\SplashScreen";
    
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
        
        // Start fade-in animation
        _timer.Start();
        
        Console.WriteLine("SplashScreen: Created and timer started");
    }
    
    /// <summary>
    /// Shows the splash screen (simplified for Avalonia)
    /// </summary>
    public static void ShowSplashScreen()
    {
        lock (_lock)
        {
            if (_instance != null) return;
            
            try
            {
                Console.WriteLine("SplashScreen: Creating splash screen");
                _instance = new SplashScreen();
                _instance.Show();
                Console.WriteLine("SplashScreen: Instance created and shown");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"SplashScreen: Error creating instance: {ex.Message}");
            }
        }
    }
    
    /// <summary>
    /// Closes the splash screen with fade-out effect
    /// </summary>
    public static void CloseForm()
    {
        Console.WriteLine("SplashScreen: CloseForm called");
        
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
            Console.WriteLine($"SplashScreen: Error closing form: {ex.Message}");
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
                        Console.WriteLine($"SplashScreen: Status = {status}");
                    }
                });
            }
            else
            {
                Console.WriteLine($"SplashScreen: Status = {status} (no UI update)");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SplashScreen: Error setting status: {ex.Message}");
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
            Console.WriteLine($"SplashScreen: Error setting reference point: {ex.Message}");
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
        
        Console.WriteLine($"SplashScreen: Reference point {_referencePoints.Count}, target progress = {_targetProgress:F1}%");
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
            
            // Smooth progress bar animation
            if (_currentProgress < _targetProgress)
            {
                var increment = Math.Min((_targetProgress - _currentProgress) * 0.1, 2.0);
                _currentProgress += increment;
                ProgressBar.Value = _currentProgress;
            }
            
            // Update time remaining estimate
            UpdateTimeRemaining();
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SplashScreen: Error in Timer_Tick: {ex.Message}");
        }
    }
    
    private void UpdateTimeRemaining()
    {
        if (_currentProgress < 5.0 || _isClosing) return;
        
        try
        {
            var elapsed = DateTime.Now - _startTime;
            var estimatedTotal = elapsed.TotalMilliseconds / (_currentProgress / 100.0);
            var remaining = estimatedTotal - elapsed.TotalMilliseconds;
            
            if (remaining > 0 && remaining < 60000) // Less than 1 minute
            {
                var seconds = (int)(remaining / 1000);
                TimeRemainingLabel.Text = $"Time remaining: {seconds} seconds";
            }
            else
            {
                TimeRemainingLabel.Text = "";
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SplashScreen: Error updating time remaining: {ex.Message}");
        }
    }
    
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
            
            Console.WriteLine("SplashScreen: CST4 Dhamma wheel image loaded");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SplashScreen: Error loading background image: {ex.Message}");
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
                    
                    Console.WriteLine($"SplashScreen: Loaded {_storedIncrements.Count} stored increments");
                }
            }
            else
            {
                // On non-Windows platforms, use default increments
                // Could implement file-based storage in the future
                Console.WriteLine("SplashScreen: Non-Windows platform, using default increments");
                
                // Add some default increments for smoother progress
                _storedIncrements.AddRange(new[] { 0.1, 0.2, 0.3, 0.5, 0.7, 0.85, 0.95, 1.0 });
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SplashScreen: Error reading stored increments: {ex.Message}");
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
                
                Console.WriteLine($"SplashScreen: Stored {increments.Count} increments for next launch");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"SplashScreen: Error storing increments: {ex.Message}");
        }
    }
    
    protected override void OnClosed(EventArgs e)
    {
        _timer?.Stop();
        base.OnClosed(e);
        
        lock (_lock)
        {
            _instance = null;
        }
        
        Console.WriteLine("SplashScreen: Closed");
    }
}