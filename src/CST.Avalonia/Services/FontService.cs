using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia.Media;
using CST.Avalonia.Models;
#if MACOS
using CST.Avalonia.Services.Platform.Mac;
#endif
using CST.Avalonia.Services.Platform;
using CST.Conversion;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services
{
    public class FontService : IFontService
    {
        private readonly ISettingsService _settingsService;
        private readonly ILogger<FontService> _logger;
#if MACOS
        private readonly MacFontService? _macFontService;
#endif

#if MACOS
        public FontService(ISettingsService settingsService, ILogger<FontService> logger, MacFontService? macFontService = null)
#else
        public FontService(ISettingsService settingsService, ILogger<FontService> logger)
#endif
        {
            _settingsService = settingsService;
            _logger = logger;
#if MACOS
            _macFontService = macFontService;
#endif
        }

        // Helper property to always get current font settings
        private FontSettings CurrentFontSettings => _settingsService.Settings.FontSettings;

        public string? GetScriptFontFamily(Script script)
        {
            var scriptName = script.ToString();
            _logger.LogDebug("[FONT SERVICE] GetScriptFontFamily called for script: {Script}", scriptName);
            if (CurrentFontSettings.TryGetFont(script, out var setting) && setting != null)
            {
                var fontFamily = setting.FontFamily;
                _logger.LogDebug("[FONT SERVICE] Font family for {Script}: '{FontFamily}' (null/empty=system default)", scriptName, fontFamily ?? "null");
                // Return null for system default, or the specific font family
                var result = string.IsNullOrWhiteSpace(fontFamily) ? null : fontFamily;
                _logger.LogDebug("[FONT SERVICE] Returning font family: '{Result}' for {Script}", result ?? "null", scriptName);
                return result;
            }
            
            _logger.LogWarning("No font settings found for script: {Script}", scriptName);
            return null;
        }

        public int GetScriptFontSize(Script script)
        {
            var scriptName = script.ToString();
            if (CurrentFontSettings.TryGetFont(script, out var setting) && setting != null)
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
        
        // ConcurrentDictionary: PreloadFontsForAllScriptsAsync writes this from many parallel task
        // continuations at once; a plain Dictionary can corrupt under concurrent writes. (SCRIPT-1)
        private readonly ConcurrentDictionary<Script, List<string>> _cachedFonts = new();

        // Created lazily so that constructing a FontService - which several tests and the settings layer do -
        // never has to touch Avalonia's FontManager. (#29)
        private ScriptFontService? _scriptFonts;
        private ScriptFontService ScriptFonts => _scriptFonts ??= new ScriptFontService(_logger);
        
        public async Task PreloadFontsForAllScriptsAsync()
        {
            _logger.LogInformation("Pre-loading fonts for all scripts...");
            
            var allScripts = Enum.GetValues<Script>();
            var tasks = allScripts.Select(async script =>
            {
                try
                {
                    var fonts = await GetAvailableFontsForScriptAsync(script);
                    _cachedFonts[script] = fonts;
                    _logger.LogDebug("Pre-loaded {Count} fonts for {Script}", fonts.Count, script);
                }
                catch (Exception ex)
                {
                    _logger.LogError(ex, "Failed to pre-load fonts for {Script}", script);
                    _cachedFonts[script] = new List<string>();
                }
            }).ToArray();
            
            await Task.WhenAll(tasks);
            _logger.LogInformation("Font pre-loading completed for {ScriptCount} scripts", allScripts.Length);
        }
        
        public async Task<List<string>> GetAvailableFontsForScriptAsync(Script script)
        {
            _logger.LogDebug("Getting available fonts for script: {Script}", script);
            
            // Return cached fonts if available
            if (_cachedFonts.TryGetValue(script, out var cachedFonts))
            {
                _logger.LogDebug("Returning {Count} cached fonts for {Script}", cachedFonts.Count, script);
                return cachedFonts;
            }
            
            // Load fonts on-demand if not cached yet
            _logger.LogDebug("Loading fonts on-demand for {Script}", script);
#if MACOS
            if (_macFontService != null)
            {
                _logger.LogDebug("Using MacFontService to get fonts.");
                var fonts = await _macFontService.GetAvailableFontsForScriptAsync(script);
                _cachedFonts[script] = fonts; // Cache the result
                return fonts;
            }
#endif
        
            // Everywhere else, filter by actual glyph coverage rather than handing back every installed
            // font. (#29) Before this, Windows offered the whole system list identically for all 14 scripts,
            // so the fonts that work were indistinguishable from the ones that render tofu.
            var detected = ScriptFonts.GetAvailableFontsForScript(script);
            _cachedFonts[script] = detected; // Cache the result
            return await Task.FromResult(detected);
        }
        
        public async Task<string?> GetSystemDefaultFontForScriptAsync(Script script)
        {
            _logger.LogDebug("Getting system default font for script: {Script}", script);
            
#if MACOS
            if (_macFontService != null)
            {
                _logger.LogDebug("Using MacFontService to get system default font.");
                return await _macFontService.GetSystemDefaultFontForScriptAsync(script);
            }
#endif
            
            // Ask the platform which font IT would use for this script, rather than reporting "no opinion"
            // and leaving the picker with nothing pre-selected. (#29)
            return await Task.FromResult(ScriptFonts.GetSystemDefaultFontForScript(script));
        }
    }
}
