using System;
using System.Collections.Generic;
using CST.Conversion;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services;

/// <summary>
/// Service for managing script conversion and selection
/// Replaces AppState.Inst.CurrentScript functionality from CST4
/// </summary>
public class ScriptService : IScriptService
{
    private readonly ILogger<ScriptService> _logger;
    private readonly IApplicationStateService? _stateService;
    private Script _currentScript = Script.Devanagari;

    public ScriptService(ILogger<ScriptService> logger, IApplicationStateService? stateService = null)
    {
        _logger = logger;
        _stateService = stateService;

        // The current script is initialized exactly once, deterministically, via InitializeFromState() after
        // the application state has finished loading (App.InitializeFromLoadedState). This service is the source
        // of truth for the script thereafter - it writes changes back into state - so it deliberately does NOT
        // subscribe to StateChanged (the old OnStateChanged path was redundant and could never fire at load
        // time, since LoadStateAsync does not raise StateChanged). (#81)
    }

    // Parameterless constructor for when dependencies are not available
    public ScriptService()
    {
        _logger = null!;
        _stateService = null;
    }

    public Script CurrentScript
    {
        get => _currentScript;
        set
        {
            if (_currentScript != value)
            {
                var oldScript = _currentScript;
                _currentScript = value;
                _logger?.LogInformation("Script changed from {OldScript} to {NewScript}", oldScript, value);
                
                // Save to application state if available
                if (_stateService != null)
                {
                    _stateService.Current.Preferences.CurrentScript = value;
                    _logger?.LogDebug("Updated current script in application state: {Script}", value);
                    
                    // Trigger state save to persist the change
                    _ = _stateService.SaveStateAsync();
                }
                
                ScriptChanged?.Invoke(value);
            }
        }
    }

    public event Action<Script>? ScriptChanged;

    /// <summary>
    /// The single, deterministic initialization path: set the current script from the loaded application
    /// state. Called once after state load. Idempotent - a second call is a no-op when already in sync, and
    /// it fires <see cref="ScriptChanged"/> only when the restored script actually differs from the current
    /// (default Devanagari) value. (#81)
    /// </summary>
    public void InitializeFromState()
    {
        if (_stateService == null)
            return;

        var stateScript = _stateService.Current.Preferences.CurrentScript;
        if (_currentScript != stateScript)
        {
            _currentScript = stateScript;
            _logger?.LogInformation("Initialized current script from application state: {Script}", _currentScript);
            ScriptChanged?.Invoke(_currentScript);
        }
    }

    public IReadOnlyList<Script> AvailableScripts { get; } = new List<Script>
    {
        Script.Devanagari,
        Script.Latin,
        Script.Bengali,
        Script.Cyrillic,
        Script.Gujarati,
        Script.Gurmukhi,
        Script.Kannada,
        Script.Khmer,
        Script.Malayalam,
        Script.Myanmar,
        Script.Sinhala,
        Script.Telugu,
        Script.Thai,
        Script.Tibetan
    };

    public string ConvertToCurrentScript(string devanagariText)
    {
        return ConvertText(devanagariText, Script.Devanagari, CurrentScript);
    }

    public string ConvertText(string text, Script fromScript, Script toScript)
    {
        try
        {
            return ScriptConverter.Convert(text, fromScript, toScript, true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to convert text from {FromScript} to {ToScript}", fromScript, toScript);
            return text; // Return original text on conversion failure
        }
    }

    public string GetScriptDisplayName(Script script)
    {
        // Use the same names as CST4 FormMain resx resources (tscbPaliScript.Items)
        // TODO: Implement proper localization service lookup for these resource keys
        return script switch
        {
            Script.Bengali => "Bengali",
            Script.Cyrillic => "Cyrillic",
            Script.Devanagari => "Devanagari",
            Script.Gujarati => "Gujarati", 
            Script.Gurmukhi => "Gurmukhi",
            Script.Kannada => "Kannada",
            Script.Khmer => "Khmer",
            Script.Malayalam => "Malayalam",
            Script.Myanmar => "Myanmar",
            Script.Latin => "Roman", // CST4 uses "Roman" instead of "Latin"
            Script.Sinhala => "Sinhala",
            Script.Telugu => "Telugu",
            Script.Thai => "Thai",
            Script.Tibetan => "Tibetan",
            _ => script.ToString()
        };
    }
}