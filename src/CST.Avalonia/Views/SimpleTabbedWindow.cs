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
        int? initialCurrentHitIndex = null)
    {
        // Delegate to LayoutViewModel if available
        if (DataContext is LayoutViewModel layoutViewModel)
        {
            layoutViewModel.OpenBook(book, searchTerms, bookScript, windowId, docId, searchPositions, initialAnchor, initialCurrentHitIndex);
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
                        w = Math.Min(w, placementScreen.WorkingArea.Width / placementScreen.Scaling);
                        h = Math.Min(h, placementScreen.WorkingArea.Height / placementScreen.Scaling);
                    }
                    Width = w;
                    Height = h;
                    _logger.Information("Restored window size: {Width}x{Height}", Width, Height);
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

            // Get the active document from this window's layout
            if (layoutViewModel.Layout is RootDock rootDock)
            {
                var documentDock = FindDocumentDockInLayout(rootDock);
                if (documentDock?.ActiveDockable is BookDisplayViewModel bookViewModel)
                {
                    _logger.Information("Triggering Go To dialog for active book: {BookFile}", bookViewModel.Book.FileName);
                    bookViewModel.InvokeOpenGoToDialog();
                    return;
                }
                else
                {
                    _logger.Warning("No active book in this window's document dock. ActiveDockable type: {Type}",
                        documentDock?.ActiveDockable?.GetType().Name ?? "null");
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
        try
        {
            var dockControl = this.FindDescendantOfType<global::Dock.Avalonia.Controls.DockControl>();
            if (dockControl?.DataContext is not LayoutViewModel layoutViewModel)
                return;

            if (App.ServiceProvider?.GetService(typeof(DictionaryViewModel)) is not DictionaryViewModel dictionary)
                return;

            // Recreate the Dictionary pane if it was closed (float+close leaves it out of the layout, so
            // SetActiveDockable below would no-op). Cmd+D must always be able to reopen it. (#175 follow-up)
            layoutViewModel.ShowDictionaryPanel();

            // If a book is active AND has a selection, look that word up; otherwise we still open the pane.
            // Cmd+D (and the menu item) must reveal the Dictionary regardless of selection or book focus. (#175)
            string? selection = null;
            if (layoutViewModel.Layout is RootDock rootDock)
            {
                var documentDock = FindDocumentDockInLayout(rootDock);
                if (documentDock?.ActiveDockable is BookDisplayViewModel bookViewModel &&
                    bookViewModel.BookDisplayControl != null)
                {
                    selection = await bookViewModel.BookDisplayControl.GetWebViewSelectionAsync();
                }
            }

            var word = ExtractLookupWord(selection);
            if (!string.IsNullOrEmpty(word))
            {
                dictionary.SearchText = word;
                _logger.Information("Looked up '{Word}' in the dictionary", word);
            }
            else
            {
                _logger.Debug("Look Up in Dictionary: no selection — just opening the Dictionary pane");
            }

            // Always bring the Dictionary tab forward, whether or not a word was found. (#175)
            layoutViewModel.Factory?.SetActiveDockable(dictionary);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Look Up in Dictionary failed");
        }
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

    private void TriggerViewSource(bool source2010)
    {
        try
        {
            // Resolve the active book in THIS window (main or floating), same as the Go To handler.
            var dockControl = this.FindDescendantOfType<global::Dock.Avalonia.Controls.DockControl>();
            if (dockControl?.DataContext is not LayoutViewModel layoutViewModel ||
                layoutViewModel.Layout is not RootDock rootDock)
                return;

            var documentDock = FindDocumentDockInLayout(rootDock);
            if (documentDock?.ActiveDockable is not BookDisplayViewModel bookViewModel)
                return;

            var command = source2010 ? bookViewModel.ShowSource2010Command : bookViewModel.ShowSource1957Command;
            _logger.Information("View Source ({Edition}) via menu/shortcut for book: {BookFile}",
                source2010 ? "2010" : "1957", bookViewModel.Book.FileName);
            // Swallow the CanExecute-false case (book has no source PDF) instead of faulting.
            command.Execute().Subscribe(_ => { }, ex => _logger.Debug(ex, "View Source command not available"));
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
        try
        {
            var dockControl = this.FindDescendantOfType<global::Dock.Avalonia.Controls.DockControl>();
            if (dockControl?.DataContext is not LayoutViewModel layoutViewModel)
                return;

            string? selection = null;
            if (layoutViewModel.Layout is RootDock rootDock)
            {
                var documentDock = FindDocumentDockInLayout(rootDock);
                if (documentDock?.ActiveDockable is BookDisplayViewModel bookViewModel &&
                    bookViewModel.BookDisplayControl != null)
                {
                    selection = await bookViewModel.BookDisplayControl.GetWebViewSelectionAsync();
                }
            }

            if (App.ServiceProvider?.GetService(typeof(SearchViewModel)) is not SearchViewModel search)
                return;

            var query = BuildSearchQuery(selection);
            if (!string.IsNullOrEmpty(query))
                search.SearchText = query;   // the Search tool's real-time throttle runs the search
            layoutViewModel.Factory?.SetActiveDockable(search);   // reveal the Search tab
            _logger.Information("Search for selection: '{Query}'", query);
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Search for Selection failed");
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

    private DocumentDock? FindDocumentDockInLayout(IDock dock)
    {
        if (dock is DocumentDock documentDock)
            return documentDock;

        if (dock.VisibleDockables != null)
        {
            foreach (var dockable in dock.VisibleDockables)
            {
                if (dockable is IDock childDock)
                {
                    var result = FindDocumentDockInLayout(childDock);
                    if (result != null)
                        return result;
                }
            }
        }

        return null;
    }
}