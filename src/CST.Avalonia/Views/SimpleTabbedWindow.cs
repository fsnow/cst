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
using CST.Avalonia.Input;
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
    // Set once Closing has taken its final geometry capture; blocks any later capture from overwriting it
    // with a destroyed window's 0,0 Position. (#535)
    private bool _geometryCaptureFrozen = false;
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

        // #28: off macOS the menu's gestures are decorative until we bind them ourselves.
        RegisterMenuShortcutKeyBindings();
        // #28: and Settings has no entry point off macOS until we add one.
        AddSettingsMenuItemOffMacOS();
        // #746: same story for About, which macOS keeps in the application menu.
        AddHelpMenuOffMacOS();
        // #778: and Exit, which macOS keeps there as Quit.
        AddExitMenuItemOffMacOS();

        // #621 Feed C: record which document owns whatever just took focus, so a command pressed after a
        // detour through a tool still targets the pane the user was working in. Bubbling and passive — it
        // only reads the event.
        AddHandler(GotFocusEvent, (_, e) => Services.DocumentFocusReporter.NoteFocus(e.Source, e.NavigationMethod),
            RoutingStrategies.Bubble);

        // Add diagnostic logging for focus and keyboard events
        GotFocus += (s, e) => _logger.Debug("FOCUS: SimpleTabbedWindow GotFocus. Source: {Source}, Method: {Method}", e.Source?.GetType().Name, e.NavigationMethod);
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
        //
        // Skipped once shutdown is underway. On Cmd+Q the ShutdownRequested handler has ALREADY captured
        // this window's geometry while it was still alive (App.SaveApplicationStateAsync) and has since
        // DISPOSED the ServiceProvider — so capturing again here only resolves a disposed provider and
        // throws ObjectDisposedException, which the catch below swallowed after logging an Error on every
        // clean quit. On the red-button path IsShuttingDown is still false (Closing runs before
        // ShutdownRequested), so the capture below is the good one and still runs. (#535, DOCK-2)
        if (!App.IsShuttingDown)
            SaveWindowState(force: true);
        // This is the last moment the native window can report a real Position. Freeze geometry capture
        // so the shutdown-path capture that runs after the window is destroyed can't replace it with
        // 0,0. Set AFTER the capture above, which is the good one. (#535)
        _geometryCaptureFrozen = true;

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
            // Once Closing has captured the final geometry, refuse every later capture. `Position` is
            // platform-backed and reads 0,0 after the native window is destroyed, while Width/Height are
            // styled properties that keep their values — so a post-close capture silently replaced a good
            // position with the origin, and the window reopened at 0,0 with the right size.
            //
            // That is exactly what happened on the red-button path: Closing captured the true position,
            // the window was destroyed, then the lifetime raised ShutdownRequested and
            // App.SaveApplicationStateAsync's forced capture ran against the dead window and overwrote it.
            // (Cmd+Q was unaffected: there ShutdownRequested runs BEFORE Closing, while the window is
            // still alive.) Freezing here fixes both orderings at a single point, and is safe because no
            // capture after Closing can be more accurate than the one Closing just took. (#535)
            //
            // The flag is never reset, which is correct while ShutdownMode is OnMainWindowClose (closing
            // this window IS quitting, so the instance is never reused). If a future handler ever CANCELS
            // the main window's Closing — nothing does today — the window would stay alive with geometry
            // persistence silently switched off, and this would need to reset on the cancelled close.
            if (_geometryCaptureFrozen)
            {
                _logger.Debug("Skipping window-state capture: geometry frozen at close (#535)");
                return;
            }

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

    /// <summary>
    /// Binds the menu shortcuts as real window accelerators on Windows/Linux.
    ///
    /// The in-window &lt;NativeMenuBar/&gt; only ever *displayed* them: NativeMenuBarPresenter binds
    /// NativeMenuItem.Gesture to MenuItem.InputGesture, which is decoration, and never to MenuItem.HotKey,
    /// which is what registers an accelerator. So off macOS every menu shortcut was dead - the menu showed
    /// "Ctrl+D" and pressing it did nothing. Only ⌘/Ctrl+G, W, E, C and A appeared to work, because those
    /// are separately forwarded out of the book WebView by JavaScript. (#28)
    ///
    /// macOS is deliberately excluded: the system menu bar already registers these gestures, and a second
    /// binding here would invoke each command twice.
    ///
    /// This covers focus anywhere in Avalonia's own controls. When a book WebView holds focus, CEF takes
    /// the keystroke before Avalonia sees it - that case is handled by the JS capture in BookDisplayView.
    /// </summary>
    /// <summary>
    /// macOS-only: the zoom key spellings a NativeMenu cannot advertise. (#572, fable review)
    ///
    /// The View menu declares ⌘=, ⌘- and ⌘0, and on macOS those key equivalents are the only route while
    /// focus is outside a book (the window-level handlers below all return early there, because a binding
    /// plus the system menu would fire twice). But a menu item carries exactly one gesture, so ⌘⇧= — which
    /// is what most people actually press for "⌘+" — and the numpad keys did nothing anywhere except
    /// inside a book, where the JS capture accepts every spelling. Windows accepts them all everywhere.
    ///
    /// No double-fire risk: these are precisely the spellings the menu does NOT register, and with focus in
    /// the WebView CEF consumes the keystroke before Avalonia sees it.
    /// </summary>
    private void RegisterMacZoomKeyBindings()
    {
        if (!OperatingSystem.IsMacOS()) return;

        AddHandler(KeyDownEvent, (object? s, KeyEventArgs e) =>
        {
            // Skip what the View menu already claims, or ⌘= would zoom twice.
            if (ZoomKeys.IsMacMenuEquivalent(e)) return;

            var command = ZoomKeys.Match(e, PlatformGesture.CommandModifier);
            if (command == null) return;

            _logger.Debug("*** MAC ZOOM SHORTCUT: {Command} (key={Key}, physical={Physical}) ***",
                command, e.Key, e.PhysicalKey);
            e.Handled = true;
            switch (command)
            {
                case ZoomCommand.In: OnZoomInClick(this, EventArgs.Empty); break;
                case ZoomCommand.Out: OnZoomOutClick(this, EventArgs.Empty); break;
                default: OnZoomResetClick(this, EventArgs.Empty); break;
            }
        }, RoutingStrategies.Bubble);

        _logger.Information("Registered macOS zoom key spellings the View menu cannot declare (shifted + numpad)");
    }

    private void RegisterMenuShortcutKeyBindings()
    {
        RegisterMacZoomKeyBindings();
        if (OperatingSystem.IsMacOS()) return;

        // A bubbling AddHandler, NOT KeyBindings. Measured on Windows: with a ComboBox dropdown open the
        // keystroke reaches BookDisplayView's AddHandler (logged, Source=ComboBoxItem) but never fires
        // Window.KeyBindings - an open dropdown lives in its own PopupRoot, so the event routes through the
        // logical chain without reaching the Window. The original KeyBindings form of this method therefore
        // left every shortcut dead whenever a dropdown had focus. Confirmed by direct A/B: the same
        // keystroke in the same state worked in a floating window (AddHandler) and did nothing here. (#511)
        //
        // Bubble, and NOT handledEventsToo, is what keeps this single-dispatch: BookDisplayView registers a
        // TUNNEL handler, and the whole tunnel phase completes before any bubble handler runs - so it claims
        // G/C/A and sets e.Handled first, and this handler only sees keystrokes nothing has claimed. (It is
        // the tunnel/bubble ordering that guarantees this, not proximity in the tree. fable review)
        var shortcuts = new (KeyGesture Gesture, Action Invoke)[]
        {
            // Same set, and the same handlers, as the NativeMenu declarations in SimpleTabbedWindow.axaml.
            (PlatformGesture.Parse("o"),       () => OnSelectBookClick(this, EventArgs.Empty)),
            (PlatformGesture.Parse("p"),       () => OnPrintClick(this, EventArgs.Empty)),
            (PlatformGesture.Parse("shift+p"), () => OnPrintSelectionClick(this, EventArgs.Empty)),
            (PlatformGesture.Parse("w"),       () => OnCloseTabClick(this, EventArgs.Empty)),
            (PlatformGesture.Parse("g"),       () => OnGoToMenuItemClick(this, EventArgs.Empty)),
            (PlatformGesture.Parse("d"),       () => OnLookUpInDictionaryClick(this, EventArgs.Empty)),
            // #570: F is now Find in Page (browser-universal, and what CST4 used it for). Search for
            // Selection, which held F, moves to Shift+F.
            (PlatformGesture.Parse("f"),       () => OnFindInPageClick(this, EventArgs.Empty)),
            (PlatformGesture.Parse("shift+f"), () => OnSearchForSelectionClick(this, EventArgs.Empty)),
            (PlatformGesture.Parse("e"),       () => OnViewSource1957Click(this, EventArgs.Empty)),
            (PlatformGesture.Parse("shift+e"), () => OnViewSource2010Click(this, EventArgs.Empty)),
            // #572 book zoom is NOT in this list — it is matched separately below, by physical key as well
            // as by produced character, because a non-Latin input source makes the KeyGesture route
            // unreliable. See ZoomKeys.
            // #564: the Window menu's Minimize item declares this gesture, and a NativeMenuBar gesture
            // dispatches nothing off macOS - so without this entry the menu would advertise a dead shortcut.
            (PlatformGesture.Parse("m"), () => WindowState = global::Avalonia.Controls.WindowState.Minimized),
            // Settings lives in the macOS app menu, so it has no NativeMenu declaration here to mirror -
            // see AddSettingsMenuItemOffMacOS, which adds the Tools entry this shortcut matches.
            (PlatformGesture.Parse("OemComma"), () =>
            {
                // Logged like the menu handlers above: without it the log cannot answer "did the shortcut
                // fire, or did the dialog fail to open?", which is exactly the question that comes up.
                _logger.Information("Settings opened via keyboard shortcut from window: {WindowTitle}", this.Title);
                _ = App.ShowSettingsWindow();
            }),
        };

        AddHandler(KeyDownEvent, (object? s, KeyEventArgs e) =>
        {
            foreach (var (gesture, invoke) in shortcuts)
            {
                if (!gesture.Matches(e)) continue;

                _logger.Debug("*** MAIN WINDOW SHORTCUT: {Gesture} ***", gesture);
                e.Handled = true;
                invoke();
                return;
            }

            // #572 zoom, after the gesture list so it can never shadow a letter shortcut.
            var zoom = ZoomKeys.Match(e, PlatformGesture.CommandModifier);
            if (zoom == null) return;

            _logger.Debug("*** MAIN WINDOW ZOOM SHORTCUT: {Command} (key={Key}, physical={Physical}) ***",
                zoom, e.Key, e.PhysicalKey);
            e.Handled = true;
            switch (zoom)
            {
                case ZoomCommand.In: OnZoomInClick(this, EventArgs.Empty); break;
                case ZoomCommand.Out: OnZoomOutClick(this, EventArgs.Empty); break;
                default: OnZoomResetClick(this, EventArgs.Empty); break;
            }
        }, RoutingStrategies.Bubble);

        _logger.Information("Registered {Count} menu shortcuts (NativeMenuBar gestures are display-only off macOS)", shortcuts.Length);
    }

    /// <summary>
    /// Adds Tools &gt; Settings on Windows/Linux.
    ///
    /// Preferences is declared in App.axaml's *application*-level NativeMenu, which Avalonia only ever
    /// realises as the macOS application menu. Off macOS the in-window &lt;NativeMenuBar/&gt; renders the
    /// *window's* menu (File/View/Tools/Window), so that declaration is never shown and the Settings
    /// dialog was completely unreachable - no menu item, and no working shortcut either. (#28)
    ///
    /// Added here rather than in SimpleTabbedWindow.axaml because it must not appear on macOS, where
    /// Preferences belongs in the application menu per the platform convention.
    /// </summary>
    private void AddSettingsMenuItemOffMacOS()
    {
        if (OperatingSystem.IsMacOS()) return;

        try
        {
            var toolsMenu = NativeMenu.GetMenu(this)?
                .Items.OfType<NativeMenuItem>()
                .FirstOrDefault(i => i.Header?.ToString() == "Tools")?.Menu;

            if (toolsMenu == null)
            {
                _logger.Warning("Tools menu not found - Settings will be reachable only via its keyboard shortcut");
                return;
            }

            var settingsItem = new NativeMenuItem
            {
                Header = "Settings…",
                Gesture = PlatformGesture.Parse("OemComma")
            };
            settingsItem.Click += async (s, e) =>
            {
                _logger.Information("Settings opened from the Tools menu");
                await App.ShowSettingsWindow();
            };

            toolsMenu.Add(new NativeMenuItemSeparator());
            toolsMenu.Add(settingsItem);
            _logger.Information("Added Tools > Settings (macOS shows Preferences in the application menu instead)");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add the Settings menu item");
        }
    }

    /// <summary>
    /// The File menu's header, as declared in <c>SimpleTabbedWindow.axaml</c>. (#778)
    ///
    /// <para>XAML cannot reference a C# const, so the markup carries the string literally and
    /// <see cref="AddExitMenuItemOffMacOS"/> matches against this. Reword the markup and the lookup finds
    /// nothing: Exit silently stops being added, and the only trace is a warning in a log nobody reads.
    /// <c>SimpleTabbedWindowMenuTests</c> is what keeps the two in step - the same treatment
    /// <c>App.AboutMenuHeader</c> gets for the same reason.</para>
    /// </summary>
    internal const string FileMenuHeader = "File";

    /// <summary>
    /// Adds File &gt; Exit on Windows/Linux. (#778)
    ///
    /// <para>Third in the family with <see cref="AddSettingsMenuItemOffMacOS"/> and
    /// <see cref="AddHelpMenuOffMacOS"/>, for the same underlying reason: macOS keeps quitting in the
    /// APPLICATION menu (CST Reader &gt; Quit, supplied by the OS), and Avalonia only ever realises that menu
    /// on macOS. Off macOS the in-window menu bar shows the window's menu, where nothing offered a way out -
    /// the File menu ended at Close Tab, which is an easy thing to reach for by mistake when what you wanted
    /// was to leave.</para>
    ///
    /// <para>Built here rather than in SimpleTabbedWindow.axaml because an item declared there would also
    /// appear on macOS, duplicating Quit and putting it somewhere the platform does not use.</para>
    ///
    /// <para><b>Routed through <c>TryShutdown</c>, never <c>Shutdown</c> or <c>Close</c>.</b> Only
    /// TryShutdown raises ShutdownRequested, which is where the graceful sequence lives - await the state
    /// save, then dispose services. App.axaml.cs records what the shortcut costs: the old path fired the save
    /// and hard-shutdown on top of it, racing the write against process exit (XCUT-1). An Exit that skipped
    /// that would drop layout, open tabs and reading positions intermittently, and would present as "it
    /// sometimes forgets where I was" rather than as anything to do with this menu.</para>
    /// </summary>
    private void AddExitMenuItemOffMacOS()
    {
        if (OperatingSystem.IsMacOS()) return;

        try
        {
            var fileMenu = NativeMenu.GetMenu(this)?
                .Items.OfType<NativeMenuItem>()
                .FirstOrDefault(i => i.Header?.ToString() == FileMenuHeader)?.Menu;

            if (fileMenu == null)
            {
                _logger.Warning("File menu not found - Exit will be reachable only via the window close button");
                return;
            }

            var exitItem = new NativeMenuItem
            {
                Header = "Exit",
                // Display only, and true: the window manager provides Alt+F4, and this being the main window
                // means closing it shuts the application down (ShutdownMode.OnMainWindowClose). Nothing is
                // bound here - NativeMenuBar gestures are decorative off macOS anyway.
                Gesture = new KeyGesture(Key.F4, KeyModifiers.Alt),
            };
            exitItem.Click += (_, _) =>
            {
                _logger.Information("Exit chosen from the File menu");
                // Fully qualified to match the four other lifetime checks in this file, which avoid a
                // using for it.
                if (global::Avalonia.Application.Current?.ApplicationLifetime
                    is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
                    desktop.TryShutdown();
            };

            fileMenu.Add(new NativeMenuItemSeparator());
            fileMenu.Add(exitItem);
            _logger.Information("Added File > Exit (macOS quits from the application menu instead)");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add the Exit menu item");
        }
    }

    /// <summary>
    /// Adds Help &gt; About CST Reader on Windows/Linux. (#746)
    ///
    /// <para>Same shape as <see cref="AddSettingsMenuItemOffMacOS"/> and for the same reason: About is
    /// declared in App.axaml's application-level NativeMenu, which Avalonia only ever realises as the macOS
    /// application menu, so off macOS that declaration is never shown.</para>
    ///
    /// <para>A whole new top-level menu rather than another Tools entry, because Help is where Windows and
    /// Linux users look for About — and it is the menu the eventual user guide and issue-tracker links
    /// belong in too. Built here rather than in SimpleTabbedWindow.axaml because a menu declared there would
    /// also appear on macOS, which already has the item in the application menu.</para>
    /// </summary>
    private void AddHelpMenuOffMacOS()
    {
        if (OperatingSystem.IsMacOS()) return;

        try
        {
            var windowMenu = NativeMenu.GetMenu(this);
            if (windowMenu == null)
            {
                _logger.Warning("Window menu not found - About will have no entry point");
                return;
            }

            var aboutItem = new NativeMenuItem { Header = App.AboutMenuHeader };
            aboutItem.Click += async (s, e) =>
            {
                _logger.Information("About opened from the Help menu");
                await App.ShowAboutWindow();
            };

            var helpMenu = new NativeMenu();
            helpMenu.Add(aboutItem);
            windowMenu.Add(new NativeMenuItem { Header = "Help", Menu = helpMenu });

            _logger.Information("Added Help > About (macOS shows it in the application menu instead)");
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to add the Help menu");
        }
    }

    private void OnGoToMenuItemClick(object? sender, EventArgs e)
    {
        _logger.Information("Go To menu item clicked from window: {WindowTitle}", this.Title);

        // Check if THIS window (could be main or floating) has a LayoutViewModel in its dock content
        // The ACTIVE window's layout, which on macOS is not necessarily this one: the application menu
        // claims ⌘G, so a shortcut pressed in a floating window arrives at this handler. (#633)
        var (targetWindow, targetLayout) = ActiveDocumentWindow();
        if (targetLayout != null)
        {
            _logger.Information("Resolving Go To against window: {Window}", targetWindow?.Title ?? "(unknown)");

            var active = DocumentTargetResolver.ResolveActiveDocument(
                targetLayout, ResolveFocusedDockable(targetWindow), RecentDocuments());
            if (active is BookDisplayViewModel bookViewModel)
            {
                _logger.Information("Triggering Go To dialog for active book: {BookFile}", bookViewModel.Book.FileName);
                bookViewModel.InvokeOpenGoToDialog();
                return;
            }

            _logger.Warning("Go To: the active document is not a book ({Type})", active?.GetType().Name ?? "null");
        }

        _logger.Warning("Could not find active book document for Go To command");
    }

    // "Look Up in Dictionary" (Cmd+D): take the word selected in the active book's WebView, drop it into
    // the Dictionary tool's search box, and bring the Dictionary tab forward. (#25)
    // #112: print the active book. Native probe — routes to window.print() on the book's WebView.
    private void OnPrintClick(object? sender, EventArgs e)
    {
        _logger.Information("Print (Cmd+P) from window: {WindowTitle}", this.Title);
        var book = FindActiveBookInThisWindow();
        if (book?.BookDisplayControl == null)
        {
            _logger.Information("Print: no active book to print");
            return;
        }
        book.BookDisplayControl.Print();
    }

    // #572: book-text zoom. Acts on the active book only — zoom is stored per script, so the change then
    // propagates through BookZoomService.ZoomChanged to every other open book showing that same script.
    // "No active book" is a genuine state (an empty window, or focus in the tree), and doing nothing is the
    // honest answer: zoom has no meaning without a book, and the chrome deliberately never scales.
    private void OnZoomInClick(object? sender, EventArgs e) => InvokeZoom(b => b.ZoomIn(), "Zoom In");

    private void OnZoomOutClick(object? sender, EventArgs e) => InvokeZoom(b => b.ZoomOut(), "Zoom Out");

    private void OnZoomResetClick(object? sender, EventArgs e) => InvokeZoom(b => b.ResetZoom(), "Actual Size");

    // #570: open Find in Page on the active book. No active book means nothing to search, and doing
    // nothing is the honest answer — find has no meaning without a document.
    private void OnFindInPageClick(object? sender, EventArgs e)
    {
        var book = FindActiveBookInThisWindow();
        if (book?.BookDisplayControl == null)
        {
            _logger.Debug("Find in Page: no active book in window {WindowTitle}", this.Title);
            return;
        }
        _logger.Information("Find in Page (Cmd+F) from window: {WindowTitle}", this.Title);
        book.BookDisplayControl.ShowFindBar();
    }

    /// <summary>
    /// Opens Find in Page on the active book for a Cmd/Ctrl+F that came from a WebView-hosted view, where
    /// the menu accelerator never reaches us. (#846)
    ///
    /// <para>It delegates to the menu item's own handler rather than resolving a book itself. That is the
    /// whole point: the complaint was that Cmd+F behaved differently depending on where focus sat, so the
    /// fix must not introduce a second way of choosing the book. <see cref="FindActiveBookInThisWindow"/>
    /// re-resolves the active window on every call, so it does not matter that this enters through the main
    /// window — a floating window holding the focus still wins.</para>
    /// </summary>
    internal static void ShowFindInActiveBook()
    {
        if (App.MainWindow is SimpleTabbedWindow window)
            window.OnFindInPageClick(null, EventArgs.Empty);
    }

    private void InvokeZoom(Action<BookDisplayView> action, string what)
    {
        var book = FindActiveBookInThisWindow();
        if (book?.BookDisplayControl == null)
        {
            _logger.Debug("{What}: no active book in window {WindowTitle}", what, this.Title);
            return;
        }
        // Naming the resolved book is what makes a wrong-target report diagnosable from a log rather than
        // from a description: "it zoomed the other one" and "it zoomed this one" produce identical lines
        // without it. (#621)
        _logger.Information("{What} from window {WindowTitle} - target: {Book}", what, this.Title,
            book.Book?.FileName ?? "(unknown)");
        action(book.BookDisplayControl);
    }

    // #112: print the current selection in the active book (falls back to whole-book when nothing is selected).
    private void OnPrintSelectionClick(object? sender, EventArgs e)
    {
        _logger.Information("Print Selection (Shift+Cmd+P) from window: {WindowTitle}", this.Title);
        var book = FindActiveBookInThisWindow();
        if (book?.BookDisplayControl == null)
        {
            _logger.Information("Print Selection: no active book");
            return;
        }
        book.BookDisplayControl.PrintSelection();
    }

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

    /// <summary>
    /// The active book in the window the user is working in, or null if that window's active document is
    /// not a book.
    ///
    /// <para>
    /// Not necessarily THIS window. On macOS a menu key equivalent is claimed by the application menu, so a
    /// shortcut pressed while a floating window is frontmost can still arrive at the main window's handler —
    /// which then resolved against the MAIN layout and answered with whatever it held. Measured: with every
    /// book dragged out to a floating window, ⌘G resolved to the Welcome tab and did nothing at all, while
    /// a double-click into a book made it work, because that hands focus to CEF and the in-page relay takes
    /// over before Avalonia ever sees the key.
    /// </para>
    ///
    /// <para>
    /// Resolving against the ACTIVE window is the same principle as #621 one level up: act where the user is
    /// working, not where the event happened to be delivered. When this window is the active one — the
    /// normal case — nothing changes.
    /// </para>
    /// </summary>
    private BookDisplayViewModel? FindActiveBookInThisWindow()
    {
        var (window, layout) = ActiveDocumentWindow();
        if (layout == null) return null;

        return DocumentTargetResolver.ResolveActiveDocument(layout, ResolveFocusedDockable(window), RecentDocuments())
            as BookDisplayViewModel;
    }

    /// <summary>
    /// The window the user is working in and its dock layout: the active window if it has one, else this
    /// window. Falling back to this window keeps every previous behaviour intact when nothing is active —
    /// during shutdown, or when a dialog holds activation.
    /// </summary>
    internal (Window? Window, IDock? Layout) ActiveDocumentWindow()
    {
        if (global::Avalonia.Application.Current?.ApplicationLifetime
            is global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop)
        {
            foreach (var window in desktop.Windows)
            {
                if (!window.IsActive) continue;

                if (window is Services.CstHostWindow floating && floating.Layout != null)
                    return (floating, floating.Layout);

                if (window is SimpleTabbedWindow main && main.CurrentLayout() is { } mainLayout)
                    return (main, mainLayout);
            }
        }

        return (this, CurrentLayout());
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

            CloseDockableIfClosable(DocumentTargetResolver.ResolveActiveDocument(rootDock, ResolveFocusedDockable(this), RecentDocuments()));
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
    /// <summary>This window's dock layout, or null before the DockControl exists.</summary>
    private IDock? CurrentLayout() =>
        this.FindDescendantOfType<global::Dock.Avalonia.Controls.DockControl>()?.DataContext
            is LayoutViewModel { Layout: { } layout } ? layout : null;

    /// <summary>
    /// Interaction history for <see cref="DocumentTargetResolver.ResolveActiveDocument"/> (#621), most
    /// recent first. Null when the tracker is unavailable, which resolves exactly as before this existed.
    /// </summary>
    private static IEnumerable<IDockable>? RecentDocuments() =>
        App.TryGetService<ActiveDocumentTracker>()?.Recent;

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

            if (DocumentTargetResolver.ResolveActiveDocument(rootDock, ResolveFocusedDockable(this), RecentDocuments()) is not BookDisplayViewModel bookViewModel)
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