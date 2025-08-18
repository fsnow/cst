using System;
using CST.Avalonia.Models;
using CST.Conversion;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services
{
    public class FontService : IFontService
    {
        private readonly ISettingsService _settingsService;
        private readonly ILogger<FontService> _logger;

        public FontService(ISettingsService settingsService, ILogger<FontService> logger)
        {
            _settingsService = settingsService;
            _logger = logger;
        }

        // Helper property to always get current font settings
        private FontSettings CurrentFontSettings => _settingsService.Settings.FontSettings;

        public string? GetScriptFontFamily(Script script)
        {
            var scriptName = script.ToString();
            _logger.LogInformation("[FONT SERVICE] GetScriptFontFamily called for script: {Script}", scriptName);
            if (CurrentFontSettings.ScriptFonts.TryGetValue(scriptName, out var setting))
            {
                var fontFamily = setting.FontFamily;
                _logger.LogInformation("[FONT SERVICE] Font family for {Script}: '{FontFamily}' (null/empty=system default)", scriptName, fontFamily ?? "null");
                // Return null for system default, or the specific font family
                var result = string.IsNullOrWhiteSpace(fontFamily) ? null : fontFamily;
                _logger.LogInformation("[FONT SERVICE] Returning font family: '{Result}' for {Script}", result ?? "null", scriptName);
                return result;
            }
            
            _logger.LogWarning("No font settings found for script: {Script}", scriptName);
            return null;
        }

        public int GetScriptFontSize(Script script)
        {
            var scriptName = script.ToString();
            if (CurrentFontSettings.ScriptFonts.TryGetValue(scriptName, out var setting))
            {
                _logger.LogDebug("Font size for {Script}: {FontSize}", scriptName, setting.FontSize);
                return setting.FontSize;
            }
            
            _logger.LogWarning("No font settings found for script: {Script}, using default size 12", scriptName);
            return 12; // Default font size
        }

        public string GetLocalizationFontFamily()
        {
            return CurrentFontSettings.LocalizationFontFamily ?? "";
        }

        public int GetLocalizationFontSize()
        {
            return CurrentFontSettings.LocalizationFontSize;
        }

        public void UpdateFontSettings(FontSettings fontSettings)
        {
            // Since we now always use CurrentFontSettings, we just need to trigger the event
            // The SettingsService should already have the updated settings
            _logger.LogInformation("Font settings updated - notifying {SubscriberCount} subscribers", 
                FontSettingsChanged?.GetInvocationList()?.Length ?? 0);
            FontSettingsChanged?.Invoke(this, EventArgs.Empty);
        }

        public event EventHandler? FontSettingsChanged;
    }
}