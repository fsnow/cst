using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using System.Threading.Tasks;
using Avalonia.VisualTree;
using CST.Avalonia.ViewModels;
using CST.Avalonia.Services;
using CST.Avalonia.Models;
using CST;
using CST.Conversion;
using Serilog;
using Microsoft.Extensions.DependencyInjection;
using WebViewControl;
using Dock.Model.Mvvm.Controls;
using Dock.Model.Core;

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

    // Completes once the window has opened, giving a deterministic "UI is ready" signal for startup
    // book-window restoration instead of fixed Task.Delay() guesses. (#70)
    private readonly TaskCompletionSource _readyTcs = new(TaskCreationOptions.RunContinuationsAsynchronously);
    public Task WhenReady => _readyTcs.Task;

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
        // Final geometry capture, bypassing the debounce: the 500ms leading-edge throttle drops the
        // trailing events of a resize/move, so without this the last ~500ms of geometry changes were
        // lost when the window was closed with the red button. (DOCK-6)
        SaveWindowState(force: true);

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
        int? docId = null, List<TermPosition>? searchPositions = null, string? initialAnchor = null,
        int? initialCurrentHitIndex = null, bool showFootnotes = true, bool showSearchTerms = true,
        ReadingPositionToken? initialPositionToken = null)
    {
        // Delegate to LayoutViewModel if available
        if (DataContext is LayoutViewModel layoutViewModel)
        {
            layoutViewModel.OpenBook(book, searchTerms, bookScript, windowId, docId, searchPositions, initialAnchor, initialCurrentHitIndex, showFootnotes, showSearchTerms, initialPositionToken);
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
        // Window MOVES don't raise any styled-property change (Position isn't a StyledProperty),
        // so without this a move-then-quit restored at the old position. (DOCK-6)
        PositionChanged += (_, _) => { if (_isInitialized) SaveWindowState(); };
        Opened += OnWindowOpened;
        
        // Don't restore window state here - it will be done after application state is loaded
    }

    private void OnWindowOpened(object? sender, EventArgs e)
    {
        _isInitialized = true;
        _readyTcs.TrySetResult(); // signal startup restoration that the UI is ready (#70)
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
                
                // Identify which currently-connected screen (if any) the saved position lands on.
                // A position saved on a monitor that's no longer attached must NOT be replayed
                // blindly, or the window restores off-screen and is unusable (CST4 lesson).
                var screens = Screens?.All;
                bool canValidate = screens != null && screens.Count > 0;
                var savedPos = (mainWindowState.X.HasValue && mainWindowState.Y.HasValue)
                    ? new PixelPoint((int)mainWindowState.X.Value, (int)mainWindowState.Y.Value)
                    : (PixelPoint?)null;
                // Probe a point near the title bar so the window stays grabbable.
                var targetScreen = (canValidate && savedPos.HasValue)
                    ? screens!.FirstOrDefault(s => s.WorkingArea.Contains(new PixelPoint(savedPos.Value.X + 40, savedPos.Value.Y + 10)))
                    : null;

                // Choose the screen to place the window on: the one under the saved top-left corner,
                // else the primary. Used for BOTH size and position so they stay consistent.
                var placementScreen = targetScreen ?? Screens?.Primary ?? (canValidate ? screens![0] : null);

                // Restore window dimensions, clamped to the placement screen's working area.
                if (mainWindowState.Width > 0 && mainWindowState.Height > 0)
                {
                    double w = mainWindowState.Width, h = mainWindowState.Height;
                    if (placementScreen != null)
                    {
                        // Clamp to the working area (logical units) so a large saved OR the 1400x900 default never
                        // opens bigger than the screen on a smaller / display-scaled laptop. (#428)
                        double scaling = placementScreen.Scaling <= 0 ? 1.0 : placementScreen.Scaling;
                        w = Math.Max(MinWidth, Math.Min(w, placementScreen.WorkingArea.Width / scaling));
                        h = Math.Max(MinHeight, Math.Min(h, placementScreen.WorkingArea.Height / scaling));
                        _logger.Information("Clamped window size to screen: working area {WW}x{WH} @ {Scale}x -> {W}x{H}",
                            placementScreen.WorkingArea.Width, placementScreen.WorkingArea.Height, scaling, w, h);
                    }
                    else
                    {
                        _logger.Warning("No placement screen available at restore; using size {W}x{H} unclamped", w, h);
                    }
                    Width = w;
                    Height = h;
                }

                // Restore position, clamped so the WHOLE window rectangle stays within the placement
                // screen's working area - not just the top-left corner. A window saved near the right
                // or bottom edge (or on a since-disconnected monitor) would otherwise pass a corner-only
                // check and restore partly off-screen, unreachable. (#105)
                if (savedPos.HasValue && placementScreen != null)
                {
                    var wa = placementScreen.WorkingArea;
                    double scaling = placementScreen.Scaling;
                    int winW = (int)(Width * scaling);
                    int winH = (int)(Height * scaling);
                    int maxX = wa.X + Math.Max(0, wa.Width - winW);
                    int maxY = wa.Y + Math.Max(0, wa.Height - winH);
                    int x = Math.Clamp(savedPos.Value.X, wa.X, maxX);
                    int y = Math.Clamp(savedPos.Value.Y, wa.Y, maxY);
                    Position = new PixelPoint(x, y);
                    if (x != savedPos.Value.X || y != savedPos.Value.Y)
                        _logger.Warning("Saved window position {SX},{SY} adjusted to {X},{Y} to keep the window on-screen",
                            savedPos.Value.X, savedPos.Value.Y, x, y);
                    else
                        _logger.Information("Restored window position: {X},{Y}", x, y);
                }
                else if (savedPos.HasValue)
                {
                    // No screen info to validate against - replay as saved.
                    Position = savedPos.Value;
                    _logger.Information("Restored window position (unvalidated): {X},{Y}", savedPos.Value.X, savedPos.Value.Y);
                }
                else if (placementScreen != null)
                {
                    // No saved position (fresh install): give the default a comfortable inset (~90% of the working
                    // area, not edge-to-edge) and center it, so it comes up as an obvious normal, resizable window
                    // with its controls reachable - never oversized/off-screen on a smaller or scaled display. (#428)
                    var wa = placementScreen.WorkingArea;
                    double scaling = placementScreen.Scaling <= 0 ? 1.0 : placementScreen.Scaling;
                    Width = Math.Max(MinWidth, Math.Min(Width, (wa.Width / scaling) * 0.9));
                    Height = Math.Max(MinHeight, Math.Min(Height, (wa.Height / scaling) * 0.9));
                    int cx = wa.X + (int)Math.Max(0, (wa.Width - Width * scaling) / 2);
                    int cy = wa.Y + (int)Math.Max(0, (wa.Height - Height * scaling) / 2);
                    Position = new PixelPoint(cx, cy);
                    _logger.Information("No saved window position; sized default to {W}x{H} and centered at {X},{Y}", Width, Height, cx, cy);
                }

                // Restore window state, but never launch minimized: a window saved while minimized
                // (e.g. quit via Cmd+Q while minimized) would otherwise reopen minimized and look like
                // the app failed to start. Coerce Minimized -> Normal; keep Maximized. (STATE-5)
                var savedState = (global::Avalonia.Controls.WindowState)mainWindowState.WindowState;
                WindowState = savedState == global::Avalonia.Controls.WindowState.Minimized
                    ? global::Avalonia.Controls.WindowState.Normal
                    : savedState;
                _logger.Information("Restored window state: {WindowState}{Note}", WindowState,
                    savedState == global::Avalonia.Controls.WindowState.Minimized ? " (coerced from Minimized)" : "");
            }
            else
            {
                // Current.MainWindow is effectively never null (ApplicationState.MainWindow defaults to a value),
                // so this is only a defensive fallback - use the XAML default size as-is.
                _logger.Information("No application state MainWindow; using the XAML default window size");
            }

            // Persist the restored/clamped/centered geometry now, forcing past the 500ms debounce (which would
            // otherwise swallow the final Height/Position until the next clean close, losing it on a crash). (#428)
            SaveWindowState(force: true);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to restore window state");
        }
    }

    // Internal + force so the shutdown path (App.SaveApplicationStateAsync) can capture the final
    // geometry bypassing the debounce — the Cmd+Q sequence writes the state file BEFORE this window's
    // Closing event fires, so the OnWindowClosing capture alone doesn't reach disk on that path. (DOCK-6)
    internal void SaveWindowState(bool force = false)
    {
        try
        {
            // Debounce saves to prevent excessive updates during window resizing; the debounce is
            // leading-edge, so the trailing events are covered by the forced captures at closing
            // and shutdown. (DOCK-6)
            var now = DateTime.Now;
            if (!force && (now - _lastSaveTime).TotalMilliseconds < 500) // Only save every 500ms
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

    private void OnGoToMenuItemClick(object? sender, EventArgs e)
    {
        _logger.Information("Go To menu item clicked from window: {WindowTitle}", this.Title);

        // Check if THIS window (could be main or floating) has a LayoutViewModel in its dock content
        // Look through the visual tree to find a DockControl with a LayoutViewModel
        var dockControl = this.FindDescendantOfType<global::Dock.Avalonia.Controls.DockControl>();
        if (dockControl?.DataContext is LayoutViewModel layoutViewModel)
        {
            _logger.Information("Found LayoutViewModel in current window's DockControl");

            // Get the document the user is working in - follows focus, so a split layout resolves to
            // the pane they are actually in rather than always the first one. (#443)
            if (layoutViewModel.Layout is RootDock rootDock)
            {
                var active = DocumentTargetResolver.ResolveActiveDocument(rootDock, ResolveFocusedDockable(this));
                if (active is BookDisplayViewModel bookViewModel)
                {
                    _logger.Information("Triggering Go To dialog for active book: {BookFile}", bookViewModel.Book.FileName);
                    bookViewModel.InvokeOpenGoToDialog();
                    return;
                }
                else
                {
                    _logger.Warning("No active book in this window. Resolved dockable type: {Type}",
                        active?.GetType().Name ?? "null");
                }
            }
        }

        _logger.Warning("Could not find active book document for Go To command");
    }

    // "Look Up in Dictionary" (Cmd+D): take the word selected in the active book's WebView, drop it into
    // the Dictionary tool's search box, and bring the Dictionary tab forward. (#25)
    private async void OnLookUpInDictionaryClick(object? sender, EventArgs e)
    {
        _logger.Information("Look Up in Dictionary (Cmd+D) from window: {WindowTitle}", this.Title);
        await LookUpInDictionaryAsync(FindActiveBookInThisWindow());
    }

    // The Dictionary and Search tools always live in the main window, so only the book differs between the
    // main-window and floating-window shortcuts — hence one shared implementation each, taking the active
    // book as its parameter. Both finish by bringing the tool's own window forward, wherever it lives, so
    // the keystroke never looks like it did nothing. (#448)
    internal static async Task LookUpInDictionaryAsync(BookDisplayViewModel? book)
    {
        try
        {
            if (App.MainWindow?.DataContext is not LayoutViewModel layoutViewModel)
                return;

            if (App.ServiceProvider?.GetService(typeof(DictionaryViewModel)) is not DictionaryViewModel dictionary)
                return;

            // Recreate the Dictionary pane if it was closed (float+close leaves it out of the layout, so
            // SetActiveDockable below would no-op). Cmd+D must always be able to reopen it. (#175 follow-up)
            layoutViewModel.ShowDictionaryPanel();

            // If a book is active AND has a selection, look that word up; otherwise we still open the pane.
            // Cmd+D (and the menu item) must reveal the Dictionary regardless of selection or book focus. (#175)
            string? selection = book?.BookDisplayControl != null
                ? await book.BookDisplayControl.GetWebViewSelectionAsync()
                : null;

            var word = ExtractLookupWord(selection);
            if (!string.IsNullOrEmpty(word))
            {
                dictionary.SearchText = word;
                Serilog.Log.Information("Looked up '{Word}' in the dictionary", word);
            }
            else
            {
                Serilog.Log.Debug("Look Up in Dictionary: no selection — just opening the Dictionary pane");
            }

            // Always bring the Dictionary tab forward, whether or not a word was found. (#175)
            layoutViewModel.Factory?.SetActiveDockable(dictionary);
            RevealWindowHosting(dictionary, layoutViewModel);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Look Up in Dictionary failed");
        }
    }

    // "Select a Book" (Cmd+O): reveal the book tree and put keyboard focus in it. Deliberately never
    // hides the panel — Cmd+O means "I want to open a book", so closing the tree on a second press
    // would be the opposite of the intent. The View menu checkbox is still how you hide it. (#111)
    private void OnSelectBookClick(object? sender, EventArgs e) => RevealSelectBookPanel();

    internal static void RevealSelectBookPanel()
    {
        try
        {
            if (App.MainWindow?.DataContext is not LayoutViewModel layoutViewModel)
                return;

            if (App.ServiceProvider?.GetService(typeof(OpenBookDialogViewModel)) is not OpenBookDialogViewModel openBook)
                return;

            // Recreates the panel if it was closed, same recreate-on-demand path as Cmd+D / Cmd+F.
            layoutViewModel.ShowSelectBookPanel();
            layoutViewModel.Factory?.SetActiveDockable(openBook);

            var host = RevealWindowHosting(openBook, layoutViewModel);

            // Focus after the layout settles: when the panel was just recreated, its view doesn't exist
            // yet at this point, so focusing now would find nothing to focus.
            Dispatcher.UIThread.Post(() =>
            {
                // A focused CEF WebView (a book, or the Welcome page) holds the platform keyboard focus,
                // and focusing an Avalonia control does not take it back — keystrokes keep going to the
                // page, so the tree looked focused but arrow keys went nowhere. Release it first.
                ReleaseWebViewKeyboardFocus(host);
                host?.FindDescendantOfType<OpenBookPanel>()?.FocusBookTree();
            }, DispatcherPriority.Loaded);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Select a Book (Cmd+O) failed");
        }
    }

    // Ask the top level to drop whatever holds focus before the tree takes it. This is enough when focus
    // sits on an Avalonia control; it is NOT enough when a CEF WebView (a book, or the Welcome page) holds
    // the platform keyboard focus, which is the known limitation on #111 — the panel still reveals, but
    // arrow keys keep going to the page until you click the tree.
    private static void ReleaseWebViewKeyboardFocus(Window? host)
    {
        try
        {
            host?.FocusManager?.ClearFocus();
        }
        catch (Exception ex)
        {
            Serilog.Log.Debug(ex, "Could not clear focus before focusing the book tree");
        }
    }

    // Bring the window that actually holds a tool to the front. The tool is usually docked in the main
    // window, but it can be floated into its own window — and then activating the main window would bury
    // the very pane the shortcut just revealed. Activating the main window when it already is the host is
    // a harmless no-op. (#448 follow-up)
    private static Window? RevealWindowHosting(IDockable tool, LayoutViewModel layoutViewModel)
    {
        var hostWindows = layoutViewModel.Factory?.HostWindows;
        if (hostWindows != null)
        {
            foreach (var host in hostWindows)
            {
                if (host is CstHostWindow hostWindow && hostWindow.Layout != null &&
                    LayoutContains(hostWindow.Layout, tool))
                {
                    hostWindow.Activate();
                    return hostWindow;
                }
            }
        }

        App.MainWindow?.Activate();
        return App.MainWindow;
    }

    private static bool LayoutContains(IDock dock, IDockable target)
    {
        if (ReferenceEquals(dock, target))
            return true;

        if (dock.VisibleDockables == null)
            return false;

        foreach (var dockable in dock.VisibleDockables)
        {
            if (ReferenceEquals(dockable, target))
                return true;
            if (dockable is IDock childDock && LayoutContains(childDock, target))
                return true;
        }

        return false;
    }

    // The active book in THIS window, or null if the active tab isn't a book (a PDF, Welcome, or nothing).
    private BookDisplayViewModel? FindActiveBookInThisWindow()
    {
        var dockControl = this.FindDescendantOfType<global::Dock.Avalonia.Controls.DockControl>();
        if (dockControl?.DataContext is not LayoutViewModel layoutViewModel ||
            layoutViewModel.Layout is not RootDock rootDock)
            return null;

        return DocumentTargetResolver.ResolveActiveDocument(rootDock, ResolveFocusedDockable(this)) as BookDisplayViewModel;
    }

    // Reduce a selection to a single lookup word: first whitespace-delimited token, minus surrounding
    // punctuation (incl. Devanagari dandas) the dictionary wouldn't match.
    private static string ExtractLookupWord(string? selection)
    {
        if (string.IsNullOrWhiteSpace(selection))
            return "";
        var s = selection.Trim();
        int i = 0;
        while (i < s.Length && !char.IsWhiteSpace(s[i]))
            i++;
        s = s.Substring(0, i);
        // \u0964 / \u0965 are the Devanagari danda / double danda (escaped per the no-literal-glyphs rule).
        return s.Trim().Trim('.', ',', ';', ':', '!', '?', '"', '\'', '(', ')', '\u0964', '\u0965');
    }

    // View Source (Cmd+E = 1957, Cmd+Shift+E = 2010) as app-level native-menu shortcuts, so they work
    // regardless of whether the book's WebView has focus. Previously only a JS keydown INSIDE the WebView
    // handled these, so they required browser focus.
    private void OnViewSource1957Click(object? sender, EventArgs e) => TriggerViewSource(source2010: false);
    private void OnViewSource2010Click(object? sender, EventArgs e) => TriggerViewSource(source2010: true);

    // #110: ⌘W closes the active document tab in THIS window (main or floating routes here for the main
    // window; floating windows use App.OnCloseTabFromFloatingWindow). Resolves the active dockable the same
    // way ⌘G/⌘E do, but for ANY document type (book or View Source PDF); a non-closable tab (Welcome,
    // CanClose=false) is skipped. A floating window with a single tab closes with its tab (framework closes
    // the emptied window).
    private void OnCloseTabClick(object? sender, EventArgs e)
    {
        try
        {
            var dockControl = this.FindDescendantOfType<global::Dock.Avalonia.Controls.DockControl>();
            if (dockControl?.DataContext is not LayoutViewModel layoutViewModel ||
                layoutViewModel.Layout is not RootDock rootDock)
                return;

            CloseDockableIfClosable(DocumentTargetResolver.ResolveActiveDocument(rootDock, ResolveFocusedDockable(this)));
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Close Tab (⌘W) failed");
        }
    }

    // Which dockable does the user actually have focus in? Dock's own RootDock.FocusedDockable is never
    // populated in this app (it reads null even immediately after clicking a tab), so ask Avalonia for the
    // focused element and walk up the visual tree to the first control bound to a dockable. Clicking a tab
    // focuses the tab item, whose DataContext IS the dockable; clicking into a book focuses the WebView,
    // whose DataContext is the book's ViewModel. Both give the right answer. (#443)
    //
    // NOTE: the window argument does NOT scope this - Avalonia's FocusManager is app-global, so a floating
    // window's handler can be handed a dockable that lives in the main window's layout. What keeps that
    // safe is the containment check in DocumentTargetResolver: a dockable outside this window's layout is
    // contained by none of its document docks, so resolution falls back instead of reaching across windows.
    // A test covers this; do not drop the containment check.
    internal static IDockable? ResolveFocusedDockable(Window? window)
    {
        var element = window?.FocusManager?.GetFocusedElement() as Visual;

        while (element != null)
        {
            if (element is StyledElement { DataContext: IDockable dockable })
                return dockable;

            element = element.GetVisualParent();
        }

        return null;
    }

    // Close a dockable via its own factory if it exists and permits closing (Welcome opts out via
    // CanClose=false). Shared by the ⌘W menu handler and the JS-forwarded close from a focused book WebView.
    internal static void CloseDockableIfClosable(IDockable? active)
    {
        if (active is not { CanClose: true }) return;

        // Belt-and-braces: CstDockFactory now stamps Factory on every added dockable, but a dockable
        // restored from an older layout could still arrive without one — fall back to the owning dock's
        // factory rather than silently doing nothing (the original #110 failure mode: books had a null
        // Factory, so ⌘W closed PDFs but never a book).
        var factory = active.Factory ?? (active.Owner as IDock)?.Factory;
        if (factory is null)
        {
            Serilog.Log.Warning("Close Tab: no factory for {Dockable} - tab left open", active.GetType().Name);
            return;
        }

        var owner = active.Owner as IDock;
        factory.CloseDockable(active);

        // Closing removes the focused tab's control, so Avalonia focus lands nowhere useful and the NEXT
        // Cmd+W would resolve through the fallback - i.e. the first split's tab, the very #443 bug, one
        // press later. Point Dock's own focus at the pane's new active tab; the resolver consults it after
        // real keyboard focus, so repeated Cmd+W keeps closing tabs in the pane the user is working in.
        if (owner?.ActiveDockable is { } nextActive)
        {
            factory.SetFocusedDockable(owner, nextActive);
        }
    }

    private void TriggerViewSource(bool source2010)
    {
        try
        {
            // Resolve the active book in THIS window (main or floating), same as the Go To handler.
            var dockControl = this.FindDescendantOfType<global::Dock.Avalonia.Controls.DockControl>();
            if (dockControl?.DataContext is not LayoutViewModel layoutViewModel ||
                layoutViewModel.Layout is not RootDock rootDock)
                return;

            if (DocumentTargetResolver.ResolveActiveDocument(rootDock, ResolveFocusedDockable(this)) is not BookDisplayViewModel bookViewModel)
                return;

            _logger.Information("View Source ({Edition}) via menu/shortcut for book: {BookFile}",
                source2010 ? "2010" : "1957", bookViewModel.Book.FileName);
            // Queue-intent: fires now if the Myanmar page is resolved, else once it resolves (so a shortcut
            // pressed mid-recalc during fast UI sequences isn't a silent no-op). (#54 follow-up)
            bookViewModel.RequestShowSource(source2010);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "View Source shortcut failed");
        }
    }

    // "Search for Selection" (Cmd+F): take the word or phrase selected in the active book and run it
    // through the Search tool, bringing the Search tab forward. Multi-word selections are quoted so they
    // search as an exact phrase. (#25 adjacent feature)
    private async void OnSearchForSelectionClick(object? sender, EventArgs e)
    {
        _logger.Information("Search for Selection (Cmd+F) from window: {WindowTitle}", this.Title);
        await SearchForSelectionAsync(FindActiveBookInThisWindow());
    }

    // Shared by both windows' Cmd+F, same as LookUpInDictionaryAsync above. (#448)
    internal static async Task SearchForSelectionAsync(BookDisplayViewModel? book)
    {
        try
        {
            if (App.MainWindow?.DataContext is not LayoutViewModel layoutViewModel)
                return;

            string? selection = book?.BookDisplayControl != null
                ? await book.BookDisplayControl.GetWebViewSelectionAsync()
                : null;

            if (App.ServiceProvider?.GetService(typeof(SearchViewModel)) is not SearchViewModel search)
                return;

            // Recreate the Search pane if it was closed, for the same reason Cmd+D reopens the Dictionary.
            layoutViewModel.ShowSearchPanel();

            var query = BuildSearchQuery(selection);
            if (!string.IsNullOrEmpty(query))
                search.SearchText = query;   // the Search tool's real-time throttle runs the search
            layoutViewModel.Factory?.SetActiveDockable(search);   // reveal the Search tab
            RevealWindowHosting(search, layoutViewModel);
            Serilog.Log.Information("Search for selection: '{Query}'", query);
        }
        catch (Exception ex)
        {
            Serilog.Log.Error(ex, "Search for Selection failed");
        }
    }

    // Turn a selection into a search query: trim + collapse internal whitespace; quote it as an exact
    // phrase when it's more than one word (single words go through bare).
    private static string BuildSearchQuery(string? selection)
    {
        if (string.IsNullOrWhiteSpace(selection))
            return "";
        var s = System.Text.RegularExpressions.Regex.Replace(selection.Trim(), @"\s+", " ").Replace("\"", "");
        return s.Contains(' ') ? $"\"{s}\"" : s;
    }

}