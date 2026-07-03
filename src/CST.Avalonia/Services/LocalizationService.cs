using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reactive.Subjects;
using System.Resources;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services;

public class LocalizationService : ILocalizationService, IDisposable
{
    private readonly ILogger<LocalizationService> _logger;
    private readonly BehaviorSubject<CultureInfo> _currentCulture;
    private readonly Dictionary<string, ResourceManager> _resourceManagers;

    public IObservable<CultureInfo> CurrentCulture => _currentCulture;

    public LocalizationService(ILogger<LocalizationService> logger)
    {
        _logger = logger;
        _currentCulture = new BehaviorSubject<CultureInfo>(CultureInfo.CurrentUICulture);
        _resourceManagers = new Dictionary<string, ResourceManager>();
        
        // Initialize resource managers for existing RESX files
        InitializeResourceManagers();
    }

    private void InitializeResourceManagers()
    {
        // This will be populated with the actual RESX resource managers
        // For now, create a placeholder structure
        _logger.LogInformation("Initializing resource managers for localization");
    }

    public CultureInfo GetCurrentCulture()
    {
        return _currentCulture.Value;
    }

    public async Task ChangeCultureAsync(CultureInfo culture)
    {
        try
        {
            _logger.LogInformation("Changing culture to: {Culture}", culture.Name);
            
            // Set the process-wide default so threads created AFTER this call also pick up the culture
            // (setting only Thread.CurrentThread would leave the UI thread and any pooled threads stale).
            Thread.CurrentThread.CurrentUICulture = culture;
            CultureInfo.CurrentUICulture = culture;
            CultureInfo.DefaultThreadCurrentUICulture = culture;

            _currentCulture.OnNext(culture);
            
            _logger.LogInformation("Culture changed successfully to: {Culture}", culture.Name);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to change culture to: {Culture}", culture.Name);
            throw;
        }
    }

    public string GetString(string key)
    {
        try
        {
            // This would use the actual resource managers to get localized strings.
            // TODO(SCRIPT-8): implement real lookup with a key→parent-culture→invariant fallback
            // chain once RESX satellite resources exist. For now, return the key as placeholder.
            return key;
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to get localized string for key: {Key}", key);
            return key; // Fallback to key name
        }
    }

    public CultureInfoDisplayItem[] GetAvailableLanguages()
    {
        // This mirrors the existing LanguageCollector functionality
        // For now, return a basic set - will be expanded with actual language discovery
        var languages = new List<CultureInfoDisplayItem>
        {
            new(new CultureInfo("en"), "English"),
            new(new CultureInfo("hi"), "हिन्दी"),
            new(new CultureInfo("ta"), "தமிழ்"),
            new(new CultureInfo("de"), "Deutsch"),
            new(new CultureInfo("es"), "Español"),
            new(new CultureInfo("fr"), "Français"),
            // Use the modern BCP-47 names; the legacy zh-CHS/zh-CHT synthetic cultures won't match
            // zh-Hans/zh-Hant satellite resources when localization is eventually wired up.
            new(new CultureInfo("zh-Hans"), "简体中文"),
            new(new CultureInfo("zh-Hant"), "繁體中文")
        };

        return languages.ToArray();
    }

    public void Dispose()
    {
        _currentCulture?.Dispose();
    }
}