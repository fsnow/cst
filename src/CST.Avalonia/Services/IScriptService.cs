using System;
using System.Collections.Generic;
using CST.Conversion;

namespace CST.Avalonia.Services;

/// <summary>
/// Service for managing script conversion and selection
/// Replaces AppState.Inst.CurrentScript functionality from CST4
/// </summary>
public interface IScriptService
{
    /// <summary>
    /// Current script for displaying Pali text
    /// </summary>
    Script CurrentScript { get; set; }
    
    /// <summary>
    /// Event fired when script changes
    /// </summary>
    event Action<Script> ScriptChanged;
    
    /// <summary>
    /// Get all available scripts
    /// </summary>
    IReadOnlyList<Script> AvailableScripts { get; }
    
    /// <summary>
    /// Convert text from Devanagari to current script
    /// Equivalent to FormSelectBook.GetNodeText()
    /// </summary>
    string ConvertToCurrentScript(string devanagariText);
    
    /// <summary>
    /// Convert text between any two scripts
    /// </summary>
    string ConvertText(string text, Script fromScript, Script toScript);
    
    /// <summary>
    /// Get display name for script (for UI)
    /// </summary>
    string GetScriptDisplayName(Script script);
}