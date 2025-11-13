using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CST.Avalonia.ViewModels;
using CST.Avalonia.Services;
using CST.Avalonia.Models;
using CST;
using CST.Conversion;
using Serilog;
using Microsoft.Extensions.DependencyInjection;
using WebViewControl;

namespace CST.Avalonia.Views;

public partial class SimpleTabbedWindow : Window
{
    private Script _defaultScript = Script.Latin;
    private ComboBox? _paliScriptCombo;
    private readonly ILogger _logger;
    private bool _isInitialized = false;
    private DateTime _lastSaveTime = DateTime.MinValue;

    // Drag monitoring fields
    private System.Timers.Timer? _dragMonitoringTimer;
    private bool _isPointerPressed = false;
    private bool _isDragInProgress = false;
    private DateTime _lastPointerPressedTime = DateTime.MinValue;
    private DateTime _webViewHiddenTime = DateTime.MinValue;
    private DateTime _dockDragDetectedTime = DateTime.MinValue;  // Track when IsDraggingDock first became true
    private const int DRAG_TIMER_INTERVAL = 50; // Check every 50ms
    private const int MIN_WEBVIEW_HIDE_DURATION = 100; // Minimum 100ms hide duration
    private const int DRAG_DETECTION_THRESHOLD = 150; // Wait 150ms to distinguish tab clicks from real drags

    public SimpleTabbedWindow()
    {
        InitializeComponent();
        _logger = Log.ForContext<SimpleTabbedWindow>();
        
        // Initialize Pali Script ComboBox
        InitializePaliScriptCombo();
        
        // Initialize window state management
        InitializeWindowStateManagement();
        
        // Add diagnostic logging for focus and keyboard events
        GotFocus += (s, e) => _logger.Debug("FOCUS: SimpleTabbedWindow GotFocus. Source: {Source}", e.Source?.GetType().Name);
        LostFocus += (s, e) => _logger.Debug("FOCUS: SimpleTabbedWindow LostFocus. Source: {Source}", e.Source?.GetType().Name);
        AddHandler(KeyDownEvent, (s, e) => {
            _logger.Debug("KEYBOARD: SimpleTabbedWindow KeyDown. Key: {Key}, Modifiers: {Modifiers}, Source: {Source}", e.Key, e.KeyModifiers, e.Source?.GetType().Name);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        
        // Add drag and drop event logging and cross-window drag detection
        AddHandler(DragDrop.DragEnterEvent, (s, e) => {
            _logger.Information("DRAG: DragEnter on SimpleTabbedWindow. Source: {Source}", e.Source?.GetType().Name);
            // Hide WebViews when a drag enters from another window
            if (!_isDragInProgress)
            {
                _logger.Information("*** DRAG ENTER DETECTED - HIDING WebViews for cross-window drag ***");
                _isDragInProgress = true;
                HideWebViewForDrag();
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        AddHandler(DragDrop.DragOverEvent, (s, e) => {
            _logger.Debug("DRAG: DragOver on SimpleTabbedWindow. Source: {Source}", e.Source?.GetType().Name);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        AddHandler(DragDrop.DropEvent, (s, e) => {
            _logger.Information("DRAG: Drop on SimpleTabbedWindow. Source: {Source}", e.Source?.GetType().Name);
            // Restore WebViews after drop
            if (_isDragInProgress)
            {
                _logger.Information("*** DROP DETECTED - RESTORING WebViews ***");
                _isDragInProgress = false;
                _isPointerPressed = false;
                RestoreWebViewAfterDrag();
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        AddHandler(DragDrop.DragLeaveEvent, (s, e) => {
            _logger.Information("DRAG: DragLeave on SimpleTabbedWindow. Source: {Source}", e.Source?.GetType().Name);
            // Restore WebViews if drag leaves without drop
            if (_isDragInProgress)
            {
                _logger.Information("*** DRAG LEAVE DETECTED - RESTORING WebViews ***");
                _isDragInProgress = false;
                _isPointerPressed = false;
                RestoreWebViewAfterDrag();
            }
        }, RoutingStrategies.Tunnel, handledEventsToo: true);

        // Set up global drag monitoring to handle WebView interference
        SetupDragMonitoring();

        // Clean up on window closing
        Closing += OnWindowClosing;
    }

    private void OnWindowClosing(object? sender, System.ComponentModel.CancelEventArgs e)
    {
        // Clean up drag monitoring timer
        if (_dragMonitoringTimer != null)
        {
            _dragMonitoringTimer.Stop();
            _dragMonitoringTimer.Dispose();
            _dragMonitoringTimer = null;
            _logger.Debug("Disposed drag monitoring timer");
        }
    }

    private void InitializeComponent()
    {
        AvaloniaXamlLoader.Load(this);
        _paliScriptCombo = this.FindControl<ComboBox>("PaliScriptCombo");
    }

    private void InitializePaliScriptCombo()
    {
        _logger.Information("SCRIPT_DROPDOWN: InitializePaliScriptCombo called, _paliScriptCombo is {Status}", _paliScriptCombo == null ? "NULL" : "FOUND");

        if (_paliScriptCombo == null)
        {
            _logger.Error("SCRIPT_DROPDOWN: PaliScriptCombo control not found!");
            return;
        }

        // Add available scripts (excluding Unknown and IPE)
        var availableScripts = Enum.GetValues<Script>().Where(s => s != Script.Unknown && s != Script.Ipe);
        foreach (var script in availableScripts)
        {
            _paliScriptCombo.Items.Add(script);
        }
        _logger.Information("SCRIPT_DROPDOWN: Added {Count} scripts to ComboBox", _paliScriptCombo.Items.Count);

        // Set initial script from ScriptService, falling back to default
        try
        {
            var scriptService = App.ServiceProvider?.GetRequiredService<IScriptService>();
            if (scriptService != null)
            {
                _defaultScript = scriptService.CurrentScript;
                _logger.Information("Initialized script from ScriptService: {Script}", _defaultScript);

                // Listen for script changes from ScriptService (e.g., when state is loaded)
                scriptService.ScriptChanged += OnScriptServiceScriptChanged;
            }
            else
            {
                _logger.Warning("ScriptService not available - using default script: {Script}", _defaultScript);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to get current script from ScriptService - using default: {Script}", _defaultScript);
        }

        _paliScriptCombo.SelectedItem = _defaultScript;
        _logger.Information("SCRIPT_DROPDOWN: Set initial SelectedItem to {Script}", _defaultScript);

        _paliScriptCombo.SelectionChanged += OnDefaultScriptChanged;
        _logger.Information("SCRIPT_DROPDOWN: Attached SelectionChanged event handler");
    }

    public Script DefaultScript => _defaultScript;
    
    private void OnDefaultScriptChanged(object? sender, SelectionChangedEventArgs e)
    {
        _logger.Information("SCRIPT_DROPDOWN: OnDefaultScriptChanged called! Sender: {Sender}, SelectedItem: {Item}, SelectedItem Type: {Type}",
            sender?.GetType().Name,
            _paliScriptCombo?.SelectedItem,
            _paliScriptCombo?.SelectedItem?.GetType().Name);

        if (_paliScriptCombo?.SelectedItem is Script selectedScript)
        {
            _defaultScript = selectedScript;
            _logger.Information("Default script changed to: {Script}", selectedScript);
            
            // Update the ScriptService to propagate the change to all ViewModels
            try
            {
                var scriptService = App.ServiceProvider?.GetRequiredService<IScriptService>();
                if (scriptService != null)
                {
                    scriptService.CurrentScript = selectedScript;
                    _logger.Information("Updated ScriptService current script to: {Script}", selectedScript);
                }
                else
                {
                    _logger.Warning("ScriptService not available - cannot update script");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to update ScriptService with new script: {Script}", selectedScript);
            }
        }
    }

    public void OpenBook(Book book, List<string>? searchTerms = null, Script? bookScript = null, string? windowId = null,
        int? docId = null, List<TermPosition>? searchPositions = null, string? initialAnchor = null)
    {
        // Delegate to LayoutViewModel if available
        if (DataContext is LayoutViewModel layoutViewModel)
        {
            layoutViewModel.OpenBook(book, searchTerms, bookScript, windowId, docId, searchPositions, initialAnchor);
        }
        else
        {
            _logger.Warning("Cannot open book - LayoutViewModel not available");
        }
    }
    
    private void OnScriptServiceScriptChanged(Script newScript)
    {
        // Update the combo box when the ScriptService changes the script
        // This happens when application state is loaded on startup
        // Must run on UI thread since we're updating UI controls
        Dispatcher.UIThread.Post(() =>
        {
            if (_paliScriptCombo != null && _paliScriptCombo.SelectedItem is Script currentSelection && currentSelection != newScript)
            {
                _logger.Information("ScriptService changed script to {Script}, updating UI", newScript);
                _defaultScript = newScript;

                // Temporarily disable the selection changed handler to avoid feedback loop
                _paliScriptCombo.SelectionChanged -= OnDefaultScriptChanged;
                _paliScriptCombo.SelectedItem = newScript;
                _paliScriptCombo.SelectionChanged += OnDefaultScriptChanged;
            }
        });
    }

    private void InitializeWindowStateManagement()
    {
        // Subscribe to window events to save state when window changes
        PropertyChanged += OnWindowPropertyChanged;
        Opened += OnWindowOpened;
        
        // Don't restore window state here - it will be done after application state is loaded
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        _isInitialized = true;
        _logger.Information("Window opened and initialized");
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        // Only save state after window is fully initialized to avoid saving during startup
        if (!_isInitialized) return;

        // Save state when relevant properties change
        if (e.Property == WidthProperty || 
            e.Property == HeightProperty || 
            e.Property == WindowStateProperty)
        {
            SaveWindowState();
        }
    }

    public void RestoreWindowState()
    {
        try
        {
            var stateService = App.ServiceProvider?.GetRequiredService<IApplicationStateService>();
            if (stateService?.Current?.MainWindow != null)
            {
                var mainWindowState = stateService.Current.MainWindow;
                
                // Restore window dimensions
                if (mainWindowState.Width > 0 && mainWindowState.Height > 0)
                {
                    Width = mainWindowState.Width;
                    Height = mainWindowState.Height;
                    _logger.Information("Restored window size: {Width}x{Height}", Width, Height);
                }

                // Restore window position if saved
                if (mainWindowState.X.HasValue && mainWindowState.Y.HasValue)
                {
                    Position = new PixelPoint((int)mainWindowState.X.Value, (int)mainWindowState.Y.Value);
                    _logger.Information("Restored window position: {X},{Y}", mainWindowState.X.Value, mainWindowState.Y.Value);
                }

                // Restore window state (Normal, Maximized, Minimized)
                WindowState = (global::Avalonia.Controls.WindowState)mainWindowState.WindowState;
                _logger.Information("Restored window state: {WindowState}", WindowState);
            }
            else
            {
                _logger.Information("No saved window state found, using defaults");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to restore window state");
        }
    }

    private void SaveWindowState()
    {
        try
        {
            // Debounce saves to prevent excessive file I/O during window resizing
            var now = DateTime.Now;
            if ((now - _lastSaveTime).TotalMilliseconds < 500) // Only save every 500ms
            {
                return;
            }
            _lastSaveTime = now;

            var stateService = App.ServiceProvider?.GetRequiredService<IApplicationStateService>();
            if (stateService != null)
            {
                var mainWindowState = new MainWindowState
                {
                    Width = Width,
                    Height = Height,
                    X = Position.X,
                    Y = Position.Y,
                    WindowState = (CST.Avalonia.Models.WindowState)WindowState,
                    IsMaximized = WindowState == global::Avalonia.Controls.WindowState.Maximized
                };

                stateService.UpdateMainWindowState(mainWindowState);
                _logger.Debug("Saved window state: {Width}x{Height} at {X},{Y}, State: {WindowState}",
                    Width, Height, Position.X, Position.Y, WindowState);
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to save window state");
        }
    }

    // Drag monitoring methods to handle WebView interference with dock drop indicators
    private void SetupDragMonitoring()
    {
        _logger.Information("Setting up global drag monitoring to handle WebView interference");

        // Create timer for monitoring drag operations
        _dragMonitoringTimer = new System.Timers.Timer(DRAG_TIMER_INTERVAL);
        _dragMonitoringTimer.Elapsed += OnDragMonitoringTimer;
        _dragMonitoringTimer.AutoReset = true;

        // Monitor pointer events to detect potential drag operations
        AddHandler(InputElement.PointerPressedEvent, OnWindowPointerPressed, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerReleasedEvent, OnWindowPointerReleased, RoutingStrategies.Tunnel);
        AddHandler(InputElement.PointerMovedEvent, OnWindowPointerMoved, RoutingStrategies.Tunnel);
        _logger.Information("Global drag monitoring setup complete");

        // Start the monitoring timer
        _dragMonitoringTimer.Start();
        _logger.Information("Drag monitoring timer started");
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        if (e.GetCurrentPoint(this).Properties.IsLeftButtonPressed)
        {
            _isPointerPressed = true;
            _lastPointerPressedTime = DateTime.Now;
            _logger.Debug("Pointer pressed at {Time}", _lastPointerPressedTime);
        }
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isPointerPressed = false;
        _logger.Debug("Pointer released");
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        // Pointer movement is tracked by the timer
        if (_isPointerPressed && !_isDragInProgress)
        {
            var timeSincePress = DateTime.Now - _lastPointerPressedTime;
            if (timeSincePress.TotalMilliseconds > 200)
            {
                _logger.Information("*** POINTER MOVEMENT DETECTED DRAG - HIDING WebViews ***");
                _isDragInProgress = true;
                HideWebViewForDrag();
            }
        }
    }

    private void OnDragMonitoringTimer(object? sender, System.Timers.ElapsedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            // Check if ANY DockControl has an active drag operation
            bool anyWindowDragging = false;

            if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (var window in desktop.Windows)
                {
                    // Find any DockControl in the window's visual tree (not by name)
                    var dockControls = window.GetVisualDescendants().OfType<Dock.Avalonia.Controls.DockControl>();
                    foreach (var windowDockControl in dockControls)
                    {
                        if (windowDockControl.IsDraggingDock)
                        {
                            anyWindowDragging = true;
                            _logger.Debug("Detected drag on window: {WindowTitle}", window.Title);
                            break;
                        }
                    }
                    if (anyWindowDragging) break;
                }
            }

            // Track when IsDraggingDock first becomes true
            if (anyWindowDragging && _dockDragDetectedTime == DateTime.MinValue)
            {
                _dockDragDetectedTime = DateTime.Now;
                _logger.Debug("IsDraggingDock became true at {Time}", _dockDragDetectedTime);
            }

            // Reset tracking if IsDraggingDock becomes false
            if (!anyWindowDragging && _dockDragDetectedTime != DateTime.MinValue)
            {
                var dragDuration = DateTime.Now - _dockDragDetectedTime;
                _logger.Debug("IsDraggingDock became false after {Duration}ms (threshold: {Threshold}ms)",
                    dragDuration.TotalMilliseconds, DRAG_DETECTION_THRESHOLD);
                _dockDragDetectedTime = DateTime.MinValue;
            }

            // Only consider it a real drag if IsDraggingDock has been true for longer than threshold
            bool isDockDragging = anyWindowDragging &&
                                  _dockDragDetectedTime != DateTime.MinValue &&
                                  (DateTime.Now - _dockDragDetectedTime).TotalMilliseconds >= DRAG_DETECTION_THRESHOLD;

            // Hide WebViews when dock drag starts (after threshold)
            if (isDockDragging && !_isDragInProgress)
            {
                _logger.Information("*** DOCK DRAG DETECTED - HIDING WebViews (after {Duration}ms threshold) ***",
                    (DateTime.Now - _dockDragDetectedTime).TotalMilliseconds);
                _isDragInProgress = true;
                HideWebViewForDrag();
            }

            // Restore WebViews when dock drag ends
            if (!anyWindowDragging && _isDragInProgress)
            {
                var timeSinceHidden = DateTime.Now - _webViewHiddenTime;
                if (timeSinceHidden.TotalMilliseconds >= MIN_WEBVIEW_HIDE_DURATION)
                {
                    _logger.Information("*** DOCK DRAG ENDED - RESTORING WebViews (hidden for {HideDuration}ms) ***", timeSinceHidden.TotalMilliseconds);
                    _isDragInProgress = false;
                    _isPointerPressed = false;
                    _dockDragDetectedTime = DateTime.MinValue;
                    RestoreWebViewAfterDrag();
                }
            }

            // Fallback: If WebViews have been hidden for too long (>10 seconds), restore them
            if (_isDragInProgress)
            {
                var timeSinceHidden = DateTime.Now - _webViewHiddenTime;
                if (timeSinceHidden.TotalMilliseconds > 10000) // 10 second timeout
                {
                    _logger.Information("*** FALLBACK TIMEOUT - RESTORING WebViews after 10 seconds ***");
                    _isDragInProgress = false;
                    _isPointerPressed = false;
                    _dockDragDetectedTime = DateTime.MinValue;
                    RestoreWebViewAfterDrag();
                }
            }
        });
    }

    private void HideWebViewForDrag()
    {
        _logger.Information("Hiding all WebViews across all windows to allow dock drop indicators");
        _webViewHiddenTime = DateTime.Now;

        // Hide WebViews in all application windows for cross-window drag support
        HideAllWebViewsInAllWindows();
    }

    private void HideAllWebViewsInAllWindows()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                HideAllWebViewsInWindow(window);
            }
        }
    }

    private void HideAllWebViewsInWindow(Window window)
    {
        var webViews = window.GetVisualDescendants().OfType<WebViewControl.WebView>();
        foreach (var webView in webViews)
        {
            if (webView.IsVisible)
            {
                webView.IsVisible = false;
                webView.IsHitTestVisible = false;
                _logger.Information("Hidden WebView in window: {WindowTitle}", window.Title);
            }
        }
    }

    private void RestoreWebViewAfterDrag()
    {
        _logger.Information("Restoring all WebViews across all windows after drag operation");

        // Restore WebViews in all application windows for cross-window drag support
        RestoreAllWebViewsInAllWindows();
    }

    private void RestoreAllWebViewsInAllWindows()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                RestoreAllWebViewsInWindow(window);
            }
        }
    }

    private void RestoreAllWebViewsInWindow(Window window)
    {
        var webViews = window.GetVisualDescendants().OfType<WebViewControl.WebView>();
        foreach (var webView in webViews)
        {
            if (!webView.IsVisible)
            {
                webView.IsVisible = true;
                webView.IsHitTestVisible = true;
                _logger.Information("Restored WebView in window: {WindowTitle}", window.Title);
            }
        }
    }
}