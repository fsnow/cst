using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
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
            // Held outside the try so the catch can attempt a tolerant read of the same bytes rather than
            // going back to disk, where a file being rewritten underneath us would give a different answer
            // than the one that failed. (#803)
            string? json = null;
            try
            {
                if (File.Exists(_settingsFilePath))
                {
                    json = await File.ReadAllTextAsync(_settingsFilePath);
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

                        // Persist the upgraded/repaired settings so the on-disk file is brought up to date —
                        // unless this file came from a NEWER build, in which case writing it back is how the
                        // reader loses whatever that build added.
                        //
                        // The "reading as-is" note counted toward "something changed", so merely launching an
                        // older build rewrote the file in the older shape before the reader touched anything.
                        // The note said as-is; the next line wrote it back reduced. Extension data (#883) now
                        // carries unknown top-level properties through a round-trip, so a save the reader
                        // actually asks for is no longer destructive — but a save nobody asked for is still
                        // not something to do to a file we have just said we do not fully understand. (#883)
                        if ((notes.Count > 0 || fixes.Count > 0)
                            && !SettingsValidator.IsNewerThanSupported(_settings))
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
                    // No primary file is USUALLY a true first run — but not always, and the exception is the
                    // recovery path's own worst moment. PreserveUnreadable moves the file aside, and if the
                    // app is force-quit or crashes before the restore is written back, the next launch arrives
                    // here with backups sitting right there and would have called it a first run: the restore
                    // evaporates silently and the reader loses everything a second time. (#785, fable)
                    if (GetBackupFilePaths().Length > 0 && await TryLoadFromBackupAsync().ConfigureAwait(false))
                        return;

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
                // Keep what parses, BEFORE falling back to an older copy. (#803)
                //
                // The two answers are not equivalent and the order matters: a backup is a previous save, so
                // recovering from one loses everything the reader changed since, while this loses only the
                // part we genuinely cannot read. The incident that produced all of this — one property whose
                // type had changed — should have cost a header, not a session's work.
                //
                // Only reached once a strict read has already failed. An ordinary file never comes through
                // here, which is deliberate: tolerance on every load would hide a shape change from everyone
                // until it had hidden it for a year.
                if (await TryLoadTolerantlyAsync(json).ConfigureAwait(false))
                    return;

                PreserveUnreadable();

                // Then try to RECOVER, which is the half that means the reader never learns any of this
                // happened. ApplicationStateService has done this since it was written; settings never did,
                // and settings is the file that holds everything a reader configured by hand. (#785)
                if (await TryLoadFromBackupAsync().ConfigureAwait(false))
                    return;

                // Nothing to recover from. Defaults, and the unreadable file is already kept beside them.
                //
                // This save deliberately does NOT leave a backup. A persisted-type change breaks every backup
                // at once — the expected case — so defaults would become the NEWEST backup, and the walk stops
                // at the first readable one. A later recovery would then restore defaults and report that
                // nothing was lost, with a genuinely configured backup sitting one file below, never reached.
                // (#785, fable)
                _settings = new Settings();
                ApplyFirstRunDefaults();
                _backupNextSave = false;
                RequestSave();
            }
        }

        /// <summary>
        /// Whether the NEXT save should leave a backup. False for exactly one save: the defaults written when
        /// no backup could be read, which must not become the newest backup. (#785)
        /// </summary>
        private bool _backupNextSave = true;

        /// <summary>
        /// Rebuild the settings from a file a strict read refused, keeping every part that parses. (#803)
        /// </summary>
        private async Task<bool> TryLoadTolerantlyAsync(string? json)
        {
            if (string.IsNullOrWhiteSpace(json)) return false;

            try
            {
                var salvaged = TolerantSettingsReader.Read<Settings>(json!, _jsonOptions, out var dropped);
                if (salvaged is null) return false;

                // Nothing dropped means the strict read failed for a reason this cannot see. Do not claim a
                // salvage that did not happen — fall through to the backups, which is the stronger answer.
                if (dropped.Count == 0) return false;

                foreach (var note in SettingsValidator.Migrate(salvaged))
                    _logger.Information("Settings migration (partial read): {Note}", note);
                foreach (var fix in SettingsValidator.Sanitize(salvaged))
                    _logger.Warning("Settings sanitized (partial read): {Fix}", fix);

                _settings = salvaged;
                try { ApplyFirstRunDefaults(); } catch { /* see the load path */ }

                // At Error, listing every path, because this is the one message that says what the reader
                // lost. A partial load reported as a success is the defect one level down.
                _logger.Error(
                    "Settings could not be read in full. Kept everything that parsed; these were reset to "
                    + "their defaults: {Dropped}", string.Join(", ", dropped));

                // A COPY of the original kept beside it, before the salvage is written over it.
                //
                // The salvage path is only reached when at least one node was dropped — so part of the file
                // was NOT read, and saving replaces its only copy. The dropped paths are named in the log;
                // their CONTENT would survive only in a #785 backup, and only if the app itself had once
                // saved it. A connection or model list the reader added since the last save would be
                // destroyed outright, where the behaviour this replaces at least kept the whole file. A copy
                // costs nothing and keeps that guarantee. (fable)
                PreserveUnreadable(copy: true);

                // AWAITED, not fired and forgotten — the same defect #785's review caught in the backup
                // restore, which I reproduced here in a new place. Until the primary file is readable again
                // a crash or force-quit sends the next launch down a path that knows nothing about this
                // salvage, and the reader loses it having been silently rescued in between.
                await SaveSettingsAsync().ConfigureAwait(false);
                return true;
            }
            catch (Exception ex)
            {
                _logger.Warning(ex, "Could not read the settings file even partially");
                return false;
            }
        }

        /// <summary>The directory holding timestamped copies of previously-saved settings. (#785)</summary>
        private string BackupDirectory => Path.Combine(_settingsDirectory, "settings-backups");

        /// <summary>Backups, newest first. Empty when there are none or the directory cannot be read.</summary>
        internal string[] GetBackupFilePaths()
        {
            try
            {
                // UTC: a local clock moved backwards, or a DST fall-back, would otherwise make a newer backup
                // sort as older — and the walk restores the first one it can read. (fable)
                return Directory.GetFiles(BackupDirectory, "settings-*.json")
                    .OrderByDescending(File.GetLastWriteTimeUtc)
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
        private async Task<bool> TryLoadFromBackupAsync()
        {
            foreach (var path in GetBackupFilePaths())
            {
                try
                {
                    var loaded = JsonSerializer.Deserialize<Settings>(
                        await File.ReadAllTextAsync(path).ConfigureAwait(false), _jsonOptions);
                    if (loaded == null) continue;

                    // The same repairs the primary file gets, and reported the same way. A restore that
                    // silently dropped connections or models — Sanitize does remove id-less ones — would look
                    // identical to one that lost nothing. (fable)
                    foreach (var note in SettingsValidator.Migrate(loaded))
                        _logger.Information("Settings migration (from backup): {Note}", note);
                    foreach (var fix in SettingsValidator.Sanitize(loaded))
                        _logger.Warning("Settings sanitized (from backup): {Fix}", fix);

                    _settings = loaded;
                    try { ApplyFirstRunDefaults(); } catch { /* see the load path */ }

                    // The timestamp, and no claim beyond it. The newest READABLE backup can be older than the
                    // file that failed — a shape change breaks the recent ones first — so "nothing was lost"
                    // would be a guess stated as a fact. (fable)
                    _logger.Information(
                        "Settings were restored from the backup taken at {Taken}.",
                        File.GetLastWriteTime(path));

                    // Written back NOW, not on the debounce timer. Until the primary file exists again the
                    // recovery is only in memory, and a crash or force-quit in that window sends the next
                    // launch down the no-file path — which is why that path now checks the backups too, but
                    // the fix belongs here first: a restore that has to survive a race to count is not a
                    // restore. (fable)
                    await SaveSettingsAsync().ConfigureAwait(false);
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
        /// <param name="copy">
        /// Keep the original in place as well. True on the salvage path, where the file is still the primary
        /// and is about to be rewritten; false when the file is being abandoned entirely.
        /// </param>
        private void PreserveUnreadable(bool copy = false)
        {
            try
            {
                if (!File.Exists(_settingsFilePath)) return;

                var kept = Path.Combine(
                    _settingsDirectory,
                    // Milliseconds, matching the backup filenames: two failed loads in the same second would
                    // otherwise collide, File.Move(overwrite: false) would throw, and the copy holding the
                    // reader's configuration would be left in place to be overwritten by the next save. (fable)
                    $"settings.unreadable-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");

                if (copy) File.Copy(_settingsFilePath, kept, overwrite: false);
                else File.Move(_settingsFilePath, kept, overwrite: false);

                // At Error, beside the failure itself: a reader who has just lost their configuration is
                // reading this line, and it is the one that tells them it is recoverable.
                _logger.Error(
                    "The previous settings file could not be read in full and has been kept at {Path}. "
                    + "Nothing from it was deleted.", kept);
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

        /// <summary>
        /// One save at a time. (#878)
        ///
        /// <para>Distinct from <c>_saveLock</c> below, which guards the debounce FLAG and nothing else — the
        /// write itself was unserialized. Concurrent callers are ordinary, not hypothetical: the 750ms
        /// debounce flushes on a pool thread while <c>XmlUpdateService</c> and <c>IndexingService</c> call
        /// <see cref="SaveSettingsAsync"/> directly from background flows. Both writers used the same
        /// <c>settings.json.tmp</c>, so A could finish writing the temp file, B truncate and start rewriting
        /// it, and A's <c>File.Replace</c> promote B's half-written temp over the real file — or, on Windows,
        /// one writer take an IOException and lose that save silently.</para>
        ///
        /// <para>The tolerant reader and the backup walk would recover from that, but they are the last
        /// resort, not the design. <c>ApplicationStateService</c> has serialized its writes since STATE-2.
        /// </para>
        /// </summary>
        private readonly SemaphoreSlim _writeLock = new(1, 1);

        // What this does NOT serialize, deliberately: MUTATION of _settings while a background save
        // serializes it. A save from the timer, XmlUpdateService or IndexingService can enumerate an AI model
        // list or ScriptFonts while the UI thread edits it, which throws InvalidOperationException — the save
        // is lost and logged, the file stays intact, and the next save carries the change. Pre-existing and
        // not worsened here (UI-initiated saves already serialize on the UI thread); named so it is not
        // mistaken for something this lock covers. (fable review)

        public async Task SaveSettingsAsync()
        {
            await _writeLock.WaitAsync().ConfigureAwait(false);
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
                if (_backupNextSave) await WriteBackupAsync(json).ConfigureAwait(false);
                _backupNextSave = true;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save settings to {Path}", _settingsFilePath);
                throw;
            }
            finally
            {
                _writeLock.Release();
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
            {
                // Nothing of OURS to write — but a save started by the debounce timer or by a background
                // flow can still be in flight, and this method is what shutdown awaits before letting the
                // process exit. Returning here let it exit mid-write. Draining costs nothing when idle.
                //
                // Not airtight, and the gap is worth stating rather than implying away: a flush that has
                // already cleared _savePending but not yet acquired the write lock is invisible here, so a
                // concurrent shutdown drains a free semaphore and exits with that save between flag and
                // lock. Bounded to that one save — the temp-file-plus-replace write means the file itself
                // is never torn. (#878, fable review)
                await _writeLock.WaitAsync().ConfigureAwait(false);
                _writeLock.Release();
                return;
            }
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