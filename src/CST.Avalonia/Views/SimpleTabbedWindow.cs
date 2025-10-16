using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CST.Avalonia.ViewModels;
using CST.Avalonia.Services;
using CST.Avalonia.Models;
using CST;
using CST.Conversion;
using Serilog;
using Microsoft.Extensions.DependencyInjection;

namespace CST.Avalonia.Views;

public partial class SimpleTabbedWindow : Window
{
    private Script _defaultScript = Script.Latin;
    private ComboBox? _paliScriptCombo;
    private readonly ILogger _logger;
    private bool _isInitialized = false;
    private DateTime _lastSaveTime = DateTime.MinValue;

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
        
        // Add drag and drop event logging
        AddHandler(DragDrop.DragEnterEvent, (s, e) => {
            _logger.Debug("DRAG: DragEnter on SimpleTabbedWindow. Source: {Source}", e.Source?.GetType().Name);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        
        AddHandler(DragDrop.DragOverEvent, (s, e) => {
            _logger.Debug("DRAG: DragOver on SimpleTabbedWindow. Source: {Source}", e.Source?.GetType().Name);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
        
        AddHandler(DragDrop.DropEvent, (s, e) => {
            _logger.Debug("DRAG: Drop on SimpleTabbedWindow. Source: {Source}", e.Source?.GetType().Name);
        }, RoutingStrategies.Tunnel, handledEventsToo: true);
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

    public void OpenBook(Book book, List<string>? searchTerms = null, Script? bookScript = null, string? windowId = null)
    {
        // Delegate to LayoutViewModel if available
        if (DataContext is LayoutViewModel layoutViewModel)
        {
            layoutViewModel.OpenBook(book, bookScript, windowId);
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
}