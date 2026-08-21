using System;
using System.IO;
using System.Linq;
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

                        // A file that PARSES can still be missing the field. STATE-3 covered the no-file and
                        // unreadable paths, so a settings.json written before XmlBooksDirectory existed — or
                        // hand-edited, or produced by a build that did not set it — loaded cleanly and left
                        // the app running with a blank books directory, which is the exact state STATE-3
                        // exists to prevent. The call is idempotent: it returns immediately when the
                        // directory is already set, so an ordinary file costs nothing. (#787)
                        //
                        // Guarded, because it CREATES a directory. On a read-only or missing parent that
                        // throws, and inside the try it would carry a perfectly good parsed file into the
                        // catch below — which discards it. A valid file must never be lost to a failure to
                        // create somewhere to put books; the app can run with a blank directory and complain,
                        // and the load path had never thrown for a parseable file before. (fable)
                        try { ApplyFirstRunDefaults(); }
                        catch (Exception ex)
                        {
                            _logger.Warning(ex,
                                "Could not create the default XML books directory; settings were still loaded.");
                        }

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
                // persist it. (STATE-3)
                _logger.Error(ex, "Failed to load settings from {Path} - reverting to defaults", _settingsFilePath);

                // Keep the file we could not read, BEFORE the save below writes defaults over it. (#785)
                //
                // That save used to replace it, deliberately — "the atomic save replaces the corrupt file".
                // The defaulting is right; the replacing is not, and the difference is everything: it turns
                // "your configuration is unavailable this session" into "your configuration is gone", where
                // even diagnosing the cause afterwards cannot bring it back. It has already cost a reader a
                // hand-built model list — the file was readable JSON that one property of ours had outgrown,
                // and it was destroyed rather than set aside.
                //
                // "Corrupt" is the rarer case anyway. The likelier one, and the observed one, is a file this
                // build does not understand YET: written by a newer version, restored from a backup, or
                // holding a shape we changed. None of those deserve deletion.
                PreserveUnreadable();

                // Then try to RECOVER, which is the half that means the reader never learns any of this
                // happened. ApplicationStateService has done this since it was written; settings never did,
                // and settings is the file that holds everything a reader configured by hand. (#785)
                if (TryLoadFromBackup())
                    return;

                _settings = new Settings();
                ApplyFirstRunDefaults();
                RequestSave();
            }
        }

        /// <summary>The directory holding timestamped copies of previously-saved settings. (#785)</summary>
        private string BackupDirectory => Path.Combine(_settingsDirectory, "settings-backups");

        /// <summary>Backups, newest first. Empty when there are none or the directory cannot be read.</summary>
        internal string[] GetBackupFilePaths()
        {
            try
            {
                return Directory.GetFiles(BackupDirectory, "settings-*.json")
                    .OrderByDescending(File.GetLastWriteTime)
                    .ToArray();
            }
            catch
            {
                return Array.Empty<string>();
            }
        }

        /// <summary>
        /// Keep a timestamped copy of a successful save, and prune old ones. Best-effort throughout: losing a
        /// backup is a smaller failure than losing the save it was taken from. (#785)
        /// </summary>
        private async Task WriteBackupAsync(string json)
        {
            try
            {
                Directory.CreateDirectory(BackupDirectory);
                var path = Path.Combine(
                    BackupDirectory, $"settings-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");
                await File.WriteAllTextAsync(path, json).ConfigureAwait(false);

                var kept = GetBackupFilePaths()
                    .Select(p => (path: p, when: File.GetLastWriteTime(p)))
                    .ToList();
                foreach (var stale in ApplicationStateService.SelectBackupsToDelete(kept))
                {
                    try { File.Delete(stale); } catch { /* best effort */ }
                }
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not write a settings backup");
            }
        }

        /// <summary>
        /// Walk the backups newest-first and load the first one that reads, migrating and sanitizing it
        /// exactly as the primary file would be. (#785)
        ///
        /// <para><b>This is the half that actually fixes #785.</b> Moving the unreadable file aside stops the
        /// destruction, but a file named <c>settings.unreadable-...json</c> in the application support
        /// directory is a rescue only someone who wrote this code would ever find — by the time a reader
        /// thought to look they would have rebuilt their configuration by hand, which is exactly what
        /// happened. Recovering automatically means they never learn the word.</para>
        ///
        /// <para>A backup is written in the same shape as the primary file, so a persisted-TYPE change breaks
        /// every one of them at once and this walk finds nothing. That is not a reason to skip it — it is why
        /// the load-what-the-previous-version-wrote fixtures (#787) are its complement rather than its
        /// alternative.</para>
        /// </summary>
        private bool TryLoadFromBackup()
        {
            foreach (var path in GetBackupFilePaths())
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<Settings>(File.ReadAllText(path), _jsonOptions);
                    if (loaded == null) continue;

                    SettingsValidator.Migrate(loaded);
                    SettingsValidator.Sanitize(loaded);
                    _settings = loaded;
                    try { ApplyFirstRunDefaults(); } catch { /* see the load path */ }

                    _logger.Information(
                        "Settings were restored from the backup taken at {Taken}; nothing was lost.",
                        File.GetLastWriteTime(path));

                    // Put it back as the primary file, so the next launch is an ordinary one.
                    RequestSave();
                    return true;
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Settings backup at {Path} could not be read either; trying the next", path);
                }
            }
            return false;
        }

        /// <summary>
        /// Moves an unreadable settings file aside so the defaults written next cannot destroy it. (#785)
        ///
        /// <para>Timestamped rather than a single <c>.bak</c>: a second failed start would otherwise overwrite
        /// the copy holding the reader's real configuration with a copy of the defaults that replaced it,
        /// which is the same loss one step removed.</para>
        ///
        /// <para>Never throws. It runs inside the load's catch, and a failure to preserve must not become a
        /// failure to start — the reader would then have neither their settings nor an application.</para>
        /// </summary>
        private void PreserveUnreadable()
        {
            try
            {
                if (!File.Exists(_settingsFilePath)) return;

                var kept = Path.Combine(
                    _settingsDirectory,
                    $"settings.unreadable-{DateTime.Now:yyyyMMdd-HHmmss}.json");

                File.Move(_settingsFilePath, kept, overwrite: false);

                // At Error, beside the failure itself: a reader who has just lost their configuration is
                // reading this line, and it is the one that tells them it is recoverable.
                _logger.Error(
                    "The previous settings file could not be read and has been kept at {Path}. "
                    + "Defaults are in use; nothing from it was deleted.", kept);
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not preserve the unreadable settings file");
            }
        }

        // Set the default XML books directory (creating it) when it isn't already set. Runs on a true first
        // run (no file), whenever the file is unreadable, and after ANY successful load — a file can parse
        // perfectly and still not carry the field. The app must never operate with a blank
        // XmlBooksDirectory. (STATE-3, #787)
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

                // A copy of what we just wrote, so a later build that cannot read it has something to fall
                // back to. Best-effort: a failure to keep a backup must never fail the save itself. (#785)
                await WriteBackupAsync(json).ConfigureAwait(false);
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