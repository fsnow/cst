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
                        _settings = loadedSettings;
                        _logger.Information("Settings loaded successfully from {Path}", _settingsFilePath);
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

        public void UpdateSetting<T>(string propertyName, T value)
        {
            var property = typeof(Settings).GetProperty(propertyName);
            if (property != null && property.CanWrite)
            {
                property.SetValue(_settings, value);
                _logger.Debug("Updated setting {Property} to {Value}", propertyName, value);
            }
            else
            {
                _logger.Warning("Property {Property} not found or not writable", propertyName);
            }
        }

        public string GetSettingsFilePath()
        {
            return _settingsFilePath;
        }
    }
}