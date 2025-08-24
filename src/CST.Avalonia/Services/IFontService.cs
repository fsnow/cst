using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Conversion;

namespace CST.Avalonia.Services
{
    public interface IFontService
    {
        /// <summary>
        /// Gets the font family for a specific Pali script (null for system default)
        /// </summary>
        string? GetScriptFontFamily(Script script);
        
        /// <summary>
        /// Gets the font size for a specific Pali script
        /// </summary>
        int GetScriptFontSize(Script script);
        
        /// <summary>
        /// Gets the font family for UI localization
        /// </summary>
        string GetLocalizationFontFamily();
        
        /// <summary>
        /// Gets the font size for UI localization
        /// </summary>
        int GetLocalizationFontSize();
        
        /// <summary>
        /// Updates font settings and notifies subscribers
        /// </summary>
        void UpdateFontSettings(FontSettings fontSettings);
        
        /// <summary>
        /// Event raised when font settings change
        /// </summary>
        event EventHandler? FontSettingsChanged;

        /// <summary>
        /// Gets a list of available font family names for a specific script.
        /// </summary>
        Task<List<string>> GetAvailableFontsForScriptAsync(Script script);
    }
}
