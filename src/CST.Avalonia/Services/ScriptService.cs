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
    private Script _currentScript = Script.Devanagari;

    public ScriptService(ILogger<ScriptService> logger)
    {
        _logger = logger;
    }

    // Parameterless constructor for when logger is not available
    public ScriptService()
    {
        _logger = null!; // Will be null, but methods will handle this
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
                ScriptChanged?.Invoke(value);
            }
        }
    }

    public event Action<Script>? ScriptChanged;

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