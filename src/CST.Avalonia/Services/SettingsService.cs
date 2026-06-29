using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CST.Avalonia.Constants;
using CST.Avalonia.Models;
using Serilog;

namespace CST.Avalonia.Services
{
    public class SettingsService : ISettingsService
    {
        private readonly ILogger _logger;
        private Settings _settings;
        private readonly string _settingsDirectory;
        private readonly string _settingsFilePath;
        private readonly JsonSerializerOptions _jsonOptions;

        public Settings Settings => _settings;

        public SettingsService()
        {
            _logger = Log.ForContext<SettingsService>();
            _settings = new Settings();

            // Determine settings directory based on platform
            _settingsDirectory = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppConstants.AppDataDirectoryName
            );

            _settingsFilePath = Path.Combine(_settingsDirectory, "settings.json");

            _jsonOptions = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNameCaseInsensitive = true
            };

            _logger.Information("Settings file path: {SettingsPath}", _settingsFilePath);
        }

        public async Task LoadSettingsAsync()
        {
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    var json = await File.ReadAllTextAsync(_settingsFilePath);
                    var loadedSettings = JsonSerializer.Deserialize<Settings>(json, _jsonOptions);
                    
                    if (loadedSettings != null)
                    {
                        // Migrate older/missing-version files, then repair any invalid values in place (#78).
                        var notes = SettingsValidator.Migrate(loadedSettings);
                        foreach (var note in notes)
                            _logger.Information("Settings migration: {Note}", note);
                        var fixes = SettingsValidator.Sanitize(loadedSettings);
                        foreach (var fix in fixes)
                            _logger.Warning("Settings sanitized: {Fix}", fix);

                        _settings = loadedSettings;
                        _logger.Information("Settings loaded successfully from {Path}", _settingsFilePath);

                        // Persist the upgraded/repaired settings so the on-disk file is brought up to date.
                        if (notes.Count > 0 || fixes.Count > 0)
                            RequestSave();
                    }
                    else
                    {
                        _logger.Warning("Settings file was empty or invalid, using defaults");
                    }
                }
                else
                {
                    _logger.Information("No settings file found, using defaults");
                    // Set default XML directory if not exists
                    if (string.IsNullOrEmpty(_settings.XmlBooksDirectory))
                    {
                        // Set the default XML directory in the app data folder
                        var xmlPath = Path.Combine(_settingsDirectory, "xml");

                        // Create the directory if it doesn't exist
                        if (!Directory.Exists(xmlPath))
                        {
                            Directory.CreateDirectory(xmlPath);
                            _logger.Information("Created default XML directory: {Path}", xmlPath);
                        }

                        _settings.XmlBooksDirectory = xmlPath;
                        _logger.Information("Set default XML directory: {Path}", xmlPath);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to load settings from {Path}", _settingsFilePath);
            }
        }

        public async Task SaveSettingsAsync()
        {
            try
            {
                // Ensure directory exists
                if (!Directory.Exists(_settingsDirectory))
                {
                    Directory.CreateDirectory(_settingsDirectory);
                    _logger.Information("Created settings directory: {Path}", _settingsDirectory);
                }

                var json = JsonSerializer.Serialize(_settings, _jsonOptions);
                await File.WriteAllTextAsync(_settingsFilePath, json);
                
                _logger.Information("Settings saved successfully to {Path}", _settingsFilePath);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save settings to {Path}", _settingsFilePath);
                throw;
            }
        }

        // --- Debounced save (#67) -----------------------------------------------------------------
        // UI setting changes call RequestSave() instead of fire-and-forget SaveSettingsAsync(); rapid
        // changes (e.g. dragging a font-size slider) coalesce into one write ~750ms after the last change.
        private readonly object _saveLock = new();
        private System.Timers.Timer? _saveTimer;
        private bool _savePending;

        public void RequestSave()
        {
            lock (_saveLock)
            {
                _savePending = true;
                if (_saveTimer == null)
                {
                    _saveTimer = new System.Timers.Timer(750) { AutoReset = false };
                    _saveTimer.Elapsed += (_, _) => _ = FlushPendingSaveAsync();
                }
                _saveTimer.Stop();   // restart the debounce window on each request
                _saveTimer.Start();
            }
        }

        public async Task FlushPendingSaveAsync()
        {
            bool shouldSave;
            lock (_saveLock)
            {
                _saveTimer?.Stop();
                shouldSave = _savePending;
                _savePending = false;
            }
            if (!shouldSave)
                return;
            try
            {
                await SaveSettingsAsync();
            }
            catch (Exception ex)
            {
                // Debounced saves are not awaited by callers, so swallow+log rather than crash the timer
                // thread (SaveSettingsAsync rethrows on failure). (#67)
                _logger.Error(ex, "Debounced settings save failed");
            }
        }

        public void UpdateSetting<T>(string propertyName, T value)
        {
            var property = typeof(Settings).GetProperty(propertyName);
            if (property == null || !property.CanWrite)
            {
                // Fail fast. The property name is effectively a compile-time constant (callers pass
                // nameof(Settings.X)), so a missing or read-only property is a programming error - silently
                // warning let typos slip through with the setting never applied. (#63)
                throw new ArgumentException(
                    $"Settings has no writable property named '{propertyName}'.", nameof(propertyName));
            }

            property.SetValue(_settings, value);
            _logger.Debug("Updated setting {Property} to {Value}", propertyName, value);
        }

        public string GetSettingsFilePath()
        {
            return _settingsFilePath;
        }
    }
}