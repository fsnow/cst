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
            : this(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppConstants.AppDataDirectoryName))
        {
        }

        // Test seam: lets tests point the service at a temp directory instead of the real user
        // settings file. (InternalsVisibleTo CST.Avalonia.Tests)
        internal SettingsService(string settingsDirectory)
        {
            _logger = Log.ForContext<SettingsService>();
            _settings = new Settings();

            _settingsDirectory = settingsDirectory;
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
                        // Deserialize returned null (empty/whitespace file) — treat as first run so the
                        // XML directory default still gets applied, not left blank. (STATE-3)
                        _logger.Warning("Settings file was empty or invalid, using defaults");
                        ApplyFirstRunDefaults();
                    }
                }
                else
                {
                    _logger.Information("No settings file found, using defaults");
                    ApplyFirstRunDefaults();
                }
            }
            catch (Exception ex)
            {
                // A corrupt/torn settings.json lands here. Previously we only logged and returned, so the
                // app ran with an EMPTY XmlBooksDirectory (the default was set only in the no-file branch)
                // — changing update/indexing behavior. Fall through to first-run defaulting instead, and
                // persist it (the atomic save replaces the corrupt file). (STATE-3)
                _logger.Error(ex, "Failed to load settings from {Path} - reverting to defaults", _settingsFilePath);
                _settings = new Settings();
                ApplyFirstRunDefaults();
                RequestSave();
            }
        }

        // Set the default XML books directory (creating it) when it isn't already set. Runs on a true
        // first run (no file) and whenever the file is empty/corrupt, so the app never operates with a
        // blank XmlBooksDirectory. (STATE-3)
        private void ApplyFirstRunDefaults()
        {
            if (!string.IsNullOrEmpty(_settings.XmlBooksDirectory))
                return;

            var xmlPath = Path.Combine(_settingsDirectory, "xml");
            if (!Directory.Exists(xmlPath))
            {
                Directory.CreateDirectory(xmlPath);
                _logger.Information("Created default XML directory: {Path}", xmlPath);
            }

            _settings.XmlBooksDirectory = xmlPath;
            _logger.Information("Set default XML directory: {Path}", xmlPath);
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

                // Write to a temp file then atomically replace, so a crash/power-loss mid-write can't
                // leave a torn settings.json (which would then load as corrupt and lose first-run
                // defaults). Same pattern as ApplicationStateService. (STATE-3)
                var tempPath = _settingsFilePath + ".tmp";
                await File.WriteAllTextAsync(tempPath, json);
                if (File.Exists(_settingsFilePath))
                {
                    File.Replace(tempPath, _settingsFilePath, null);
                }
                else
                {
                    File.Move(tempPath, _settingsFilePath);
                }

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