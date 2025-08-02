using System;
using System.Collections.Generic;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CST.Avalonia.ViewModels;
using CST.Avalonia.Services;
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

    public SimpleTabbedWindow()
    {
        InitializeComponent();
        _logger = Log.ForContext<SimpleTabbedWindow>();
        
        // Initialize Pali Script ComboBox
        InitializePaliScriptCombo();
        
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
        if (_paliScriptCombo == null) return;
        
        // Add available scripts (excluding Unknown and IPE)
        var availableScripts = Enum.GetValues<Script>().Where(s => s != Script.Unknown && s != Script.Ipe);
        foreach (var script in availableScripts)
        {
            _paliScriptCombo.Items.Add(script);
        }
        
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
        _paliScriptCombo.SelectionChanged += OnDefaultScriptChanged;
    }

    public Script DefaultScript => _defaultScript;
    
    private void OnDefaultScriptChanged(object? sender, SelectionChangedEventArgs e)
    {
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

    public void OpenBook(Book book, List<string>? searchTerms = null)
    {
        // Delegate to LayoutViewModel if available
        if (DataContext is LayoutViewModel layoutViewModel)
        {
            layoutViewModel.OpenBook(book);
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
        if (_paliScriptCombo != null && _paliScriptCombo.SelectedItem is Script currentSelection && currentSelection != newScript)
        {
            _logger.Information("ScriptService changed script to {Script}, updating UI", newScript);
            _defaultScript = newScript;
            
            // Temporarily disable the selection changed handler to avoid feedback loop
            _paliScriptCombo.SelectionChanged -= OnDefaultScriptChanged;
            _paliScriptCombo.SelectedItem = newScript;
            _paliScriptCombo.SelectionChanged += OnDefaultScriptChanged;
        }
    }
}