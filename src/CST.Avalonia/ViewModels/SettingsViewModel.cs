using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Constants;
using CST.Conversion;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using Serilog;

namespace CST.Avalonia.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly Services.Dictionaries.DictionarySourcePreferenceService _sourcePrefs;
        private readonly ILogger _logger;
        private SettingsCategoryViewModel? _selectedCategory;
        private bool _hasUnsavedChanges;

        public SettingsViewModel(ISettingsService settingsService, Services.Dictionaries.DictionarySourcePreferenceService sourcePrefs)
        {
            _settingsService = settingsService;
            _sourcePrefs = sourcePrefs;
            _logger = Log.ForContext<SettingsViewModel>();


            // Initialize categories. Nav names describe the actual settings in each (#100), instead of
            // generic groupings (General/Appearance/Advanced/Developer).
            var directoriesSettings = new GeneralSettingsViewModel(_settingsService) { Parent = this };
            var fontSettings = new AppearanceSettingsViewModel(_settingsService);
            var configurationSettings = new ConfigurationSettingsViewModel(_settingsService);
            var xmlUpdateSettings = new XmlUpdateSettingsViewModel(_settingsService);
            var dpdUpdateSettings = new DpdUpdateSettingsViewModel(_settingsService);
            // One "Dictionary" category (#479) folds the source enable/order preference together with the
            // existing update settings — two groups under a single nav entry, not two "Dictionary…" entries.
            var dictionarySettings = new DictionaryCategoryViewModel(
                new DictionarySourceSettingsViewModel(_sourcePrefs), dpdUpdateSettings);
            var aiSettings = new AiSettingsViewModel(_settingsService);
            var loggingSettings = new DeveloperSettingsViewModel(_settingsService) { Parent = this };

            // Order: most-adjusted settings first, informational ones last (#100).
            Categories = new ObservableCollection<SettingsCategoryViewModel>
            {
                new SettingsCategoryViewModel("Pali Script Fonts", fontSettings),
                new SettingsCategoryViewModel("Logging", loggingSettings),
                new SettingsCategoryViewModel("Tipitaka Updates", xmlUpdateSettings),
                new SettingsCategoryViewModel("Dictionary", dictionarySettings),
                new SettingsCategoryViewModel("AI", aiSettings),
                new SettingsCategoryViewModel("Directories", directoriesSettings),
                new SettingsCategoryViewModel("Configuration", configurationSettings)
            };

            // Select first category by default
            SelectedCategory = Categories.FirstOrDefault();
        }

        public ObservableCollection<SettingsCategoryViewModel> Categories { get; }

        public SettingsCategoryViewModel? SelectedCategory
        {
            get => _selectedCategory;
            set => this.RaiseAndSetIfChanged(ref _selectedCategory, value);
        }

        public bool HasUnsavedChanges
        {
            get => _hasUnsavedChanges;
            set => this.RaiseAndSetIfChanged(ref _hasUnsavedChanges, value);
        }


        // Property to close the window
        public Action? CloseWindow { get; set; }
        
        // Actions for folder browsing
        public Action? BrowseForXmlDirectory { get; set; }
        public Action? BrowseForIndexDirectory { get; set; }

        private void Close()
        {
            CloseWindow?.Invoke();
        }



        public void RequestBrowseForXmlDirectory()
        {
            _logger.Debug("Browse for XML directory requested from GeneralSettings");
            BrowseForXmlDirectory?.Invoke();
        }
        
        public void RequestBrowseForIndexDirectory()
        {
            _logger.Debug("Browse for Index directory requested from GeneralSettings");
            BrowseForIndexDirectory?.Invoke();
        }


    }

    public class SettingsCategoryViewModel : ViewModelBase
    {
        public SettingsCategoryViewModel(string name, ViewModelBase content)
        {
            Name = name;
            Content = content;
        }

        public string Name { get; }
        public ViewModelBase Content { get; }
    }

    public class GeneralSettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private string _xmlBooksDirectory;
        private string _indexDirectory;

        public GeneralSettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            _xmlBooksDirectory = _settingsService.Settings.XmlBooksDirectory;
            _indexDirectory = _settingsService.Settings.IndexDirectory;

            // Create browse commands
            BrowseCommand = ReactiveCommand.Create(() => 
            {
                // Request browse from parent
                if (Parent is SettingsViewModel settingsVm)
                {
                    settingsVm.RequestBrowseForXmlDirectory();
                }
            });
            
            BrowseIndexCommand = ReactiveCommand.Create(() => 
            {
                // Request browse from parent
                if (Parent is SettingsViewModel settingsVm)
                {
                    settingsVm.RequestBrowseForIndexDirectory();
                }
            });

            // Update service when properties change
            this.WhenAnyValue(x => x.XmlBooksDirectory)
                .Skip(1)
                .Subscribe(value => 
                {
                    _settingsService.UpdateSetting(nameof(Settings.XmlBooksDirectory), value);
                    _settingsService.RequestSave();
                });
                
            this.WhenAnyValue(x => x.IndexDirectory)
                .Skip(1)
                .Subscribe(value => 
                {
                    _settingsService.UpdateSetting(nameof(Settings.IndexDirectory), value);
                    _settingsService.RequestSave();
                });

        }

        public string XmlBooksDirectory
        {
            get => _xmlBooksDirectory;
            set => this.RaiseAndSetIfChanged(ref _xmlBooksDirectory, value);
        }

        public string IndexDirectory
        {
            get => _indexDirectory;
            set => this.RaiseAndSetIfChanged(ref _indexDirectory, value);
        }


        public ViewModelBase? Parent { get; set; }
        public ReactiveCommand<Unit, Unit> BrowseCommand { get; }
        public ReactiveCommand<Unit, Unit> BrowseIndexCommand { get; }
    }

    public class AppearanceSettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IFontService _fontService;
        private readonly Func<Action, Task> _uiInvoke;   // dispatcher hop, injectable for tests
        private ScriptFontSettingViewModel? _selectedScript;
        private CancellationTokenSource? _fontLoadCts;    // cancels the previous script's in-flight load (#67)

        // fontService/uiInvoke are injectable for unit tests; production resolves the service and uses the
        // real UI-thread dispatcher hop.
        public AppearanceSettingsViewModel(ISettingsService settingsService,
            IFontService? fontService = null, Func<Action, Task>? uiInvoke = null)
        {
            _settingsService = settingsService;
            _fontService = fontService ?? App.ServiceProvider!.GetRequiredService<IFontService>();
            _uiInvoke = uiInvoke ?? (async a => await Dispatcher.UIThread.InvokeAsync(a));
            
            // Initialize script font settings
            ScriptFontSettings = new ObservableCollection<ScriptFontSettingViewModel>();
            var fontSettings = _settingsService.Settings.FontSettings;
            
            foreach (var kvp in fontSettings.ScriptFonts)
            {
                var vm = new ScriptFontSettingViewModel
                {
                    ScriptName = kvp.Key,
                    FontFamily = kvp.Value.FontFamily,
                    FontSize = kvp.Value.FontSize,
                    Parent = this
                };
                // Initialize the preview text and font display name after setting all properties
                vm.UpdatePreviewText();
                vm.UpdateFontDisplayName();
                vm.UpdateEffectiveFontFamilyObject(); // Initialize the FontFamily object
                ScriptFontSettings.Add(vm);
            }
            
            // Select Latin (Roman) by default as it's the most commonly used script
            SelectedScript = ScriptFontSettings.FirstOrDefault(s => s.ScriptName == "Roman") 
                           ?? ScriptFontSettings.FirstOrDefault(s => s.ScriptName == "Latin")
                           ?? ScriptFontSettings.FirstOrDefault();
                           
            // Initialize localization font settings
            LocalizationFontFamily = fontSettings.LocalizationFontFamily;
            LocalizationFontSize = fontSettings.LocalizationFontSize;
            
            
            // Load fonts for the selected script; a new selection cancels the previous in-flight load so a
            // stale result can't overwrite newer state. WhenAnyValue emits the current SelectedScript on
            // subscribe, so the initial (default) script loads here too. No preload loop: FontService already
            // warms every script's cache at app startup (App.InitializeFontsAsync), and other scripts load
            // lazily on first selection — which also removes the off-UI-thread load that seeded #67's races. (#67)
            this.WhenAnyValue(x => x.SelectedScript)
                .Where(s => s != null)
                .Subscribe(s =>
                {
                    _fontLoadCts?.Cancel();
                    _fontLoadCts = new CancellationTokenSource();
                    _ = LoadAvailableFontsForScript(s!, _fontLoadCts.Token);
                });
        }

        public ObservableCollection<ScriptFontSettingViewModel> ScriptFontSettings { get; }
        
        public ScriptFontSettingViewModel? SelectedScript
        {
            get => _selectedScript;
            set
            {
                Log.Debug("[Settings] SelectedScript setter called: {ScriptName}", value?.ScriptName ?? "null");
                this.RaiseAndSetIfChanged(ref _selectedScript, value);
            }
        }
        
        private string _localizationFontFamily = "";
        public string LocalizationFontFamily
        {
            get => _localizationFontFamily;
            set 
            {
                this.RaiseAndSetIfChanged(ref _localizationFontFamily, value);
                _settingsService.Settings.FontSettings.LocalizationFontFamily = value;
                // Notify FontService about the change
                var fontService = App.ServiceProvider?.GetService(typeof(IFontService)) as IFontService;
                fontService?.UpdateFontSettings(_settingsService.Settings.FontSettings);
                
                // Immediate save
                _settingsService.RequestSave();
            }
        }
        
        private int _localizationFontSize = 12;
        public int LocalizationFontSize
        {
            get => _localizationFontSize;
            set 
            {
                this.RaiseAndSetIfChanged(ref _localizationFontSize, value);
                _settingsService.Settings.FontSettings.LocalizationFontSize = value;
                // Notify FontService about the change
                var fontService = App.ServiceProvider?.GetService(typeof(IFontService)) as IFontService;
                fontService?.UpdateFontSettings(_settingsService.Settings.FontSettings);
                
                // Immediate save
                _settingsService.RequestSave();
            }
        }
        
        public void UpdateScriptFont(string scriptName, string? fontFamily, int fontSize)
        {
            if (_settingsService.Settings.FontSettings.ScriptFonts.TryGetValue(scriptName, out var setting))
            {
                setting.FontFamily = fontFamily ?? string.Empty;
                setting.FontSize = fontSize;
                
                // Notify FontService about the change so other components update
                var fontService = App.ServiceProvider?.GetService(typeof(IFontService)) as IFontService;
                fontService?.UpdateFontSettings(_settingsService.Settings.FontSettings);
            }
        }
        
        // Debounced save (#67); kept Task-returning for the existing fire-and-forget callers.
        public Task SaveSettingsAsync()
        {
            _settingsService.RequestSave();
            return Task.CompletedTask;
        }
        
        private async Task LoadAvailableFontsForScript(ScriptFontSettingViewModel scriptVm, CancellationToken ct)
        {
            var scriptEnum = ScriptFontSettingViewModel.GetScriptFromName(scriptVm.ScriptName);
            int version = scriptVm.BeginLoad();   // latest-wins: a stale load can't apply over a newer one
            Log.Debug("[Settings] Loading fonts for {ScriptName}", scriptVm.ScriptName);

            try
            {
                var fonts = await _fontService.GetAvailableFontsForScriptAsync(scriptEnum).ConfigureAwait(false);
                if (ct.IsCancellationRequested) return;

                // "System Default" first, then the enumerated fonts. (The empty-list retry was removed: the
                // font service caches empty results too, so retrying could only re-read the same list. (#67))
                var fontsCopy = new List<string>(fonts ?? new List<string>());
                fontsCopy.Insert(0, "System Default");
                Log.Debug("[Settings] {ScriptName}: {FontCount} fonts", scriptVm.ScriptName, fontsCopy.Count);

                await _uiInvoke(() =>
                {
                    // Apply only if this is still the newest load for the script and it wasn't cancelled, and
                    // compute the selection HERE against the CURRENT saved font — so a stale load can't revert a
                    // font the user changed while it was in flight. (#67 Bug A)
                    if (ct.IsCancellationRequested || !scriptVm.IsCurrentLoad(version)) return;
                    scriptVm.ApplyLoadedFonts(fontsCopy, ResolveFontSelection(fontsCopy, scriptVm.FontFamily));
                }).ConfigureAwait(false);

                if (ct.IsCancellationRequested) return;
                _ = LoadSystemDefaultSafe(scriptVm);   // system default font NAME (display only)
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Settings] {ScriptName}: Error loading fonts", scriptVm.ScriptName);
                if (ct.IsCancellationRequested) return;
                await _uiInvoke(() =>
                {
                    if (ct.IsCancellationRequested || !scriptVm.IsCurrentLoad(version)) return;
                    scriptVm.ApplyLoadedFonts(new List<string> { "System Default" }, "System Default");
                }).ConfigureAwait(false);
            }
        }

        // The selection the ComboBox should show for a saved font: the matching enumerated font, or
        // "System Default" when unset or the saved font isn't currently installed. Pure — unit-tested. (#67)
        internal static string ResolveFontSelection(IReadOnlyList<string> fonts, string? savedFont)
        {
            if (string.IsNullOrWhiteSpace(savedFont)) return "System Default";
            var match = fonts.FirstOrDefault(f =>
                string.Equals(f?.Trim(), savedFont.Trim(), StringComparison.OrdinalIgnoreCase));
            return match ?? "System Default";
        }

        private async Task LoadSystemDefaultSafe(ScriptFontSettingViewModel scriptVm)
        {
            try { await scriptVm.LoadSystemDefaultFontAsync(); }
            catch (Exception ex)
            {
                Log.Debug("[Settings] {ScriptName}: Failed to load system default font info - {Message}",
                    scriptVm.ScriptName, ex.Message);
            }
        }
    }
    
    public class ScriptFontSettingViewModel : ViewModelBase
    {
        private string _scriptName = "";
        private string? _fontFamily = null;
        private int _fontSize = 12;
        private string _previewText = "";
        private string _effectiveFontFamily = "";
        private string _fontDisplayName = "";
        private bool _isLoadingFonts;
        private ObservableCollection<string> _availableFonts = new();
        private string? _systemDefaultFontName;
        private string _selectedFontFamily = "System Default";   // ComboBox display, decoupled from _fontFamily (#67)
        private bool _applyingLoadResult;                         // true while a load applies its result (#67 Bug B)
        private int _loadVersion;                                 // latest-wins guard for concurrent loads (#67)
        
        public string ScriptName
        {
            get => _scriptName;
            set 
            {
                this.RaiseAndSetIfChanged(ref _scriptName, value);
                UpdatePreviewText();
                UpdateFontDisplayName();
            }
        }
        
        public string PreviewText
        {
            get => _previewText;
            private set => this.RaiseAndSetIfChanged(ref _previewText, value);
        }
        
        public string EffectiveFontFamily
        {
            get {
                Log.Debug("[Settings] EffectiveFontFamily getter: Script={ScriptName}, Returning={FontFamily}", 
                    ScriptName, _effectiveFontFamily);
                return _effectiveFontFamily;
            }
            private set {
                Log.Debug("[Settings] EffectiveFontFamily setter: Script={ScriptName}, OldValue={OldValue}, NewValue={NewValue}", 
                    ScriptName, _effectiveFontFamily, value);
                this.RaiseAndSetIfChanged(ref _effectiveFontFamily, value);
                // Update the FontFamily object when the string changes
                UpdateEffectiveFontFamilyObject();
            }
        }
        
        private global::Avalonia.Media.FontFamily? _effectiveFontFamilyObject;
        public global::Avalonia.Media.FontFamily EffectiveFontFamilyObject
        {
            get => _effectiveFontFamilyObject ?? global::Avalonia.Media.FontFamily.Default;
            private set => this.RaiseAndSetIfChanged(ref _effectiveFontFamilyObject, value);
        }
        
        public void UpdateEffectiveFontFamilyObject()
        {
            try
            {
                if (!string.IsNullOrWhiteSpace(_effectiveFontFamily))
                {
                    EffectiveFontFamilyObject = new global::Avalonia.Media.FontFamily(_effectiveFontFamily);
                    Log.Debug("[Settings] Created FontFamily object for: {FontFamily}", _effectiveFontFamily);
                }
                else
                {
                    EffectiveFontFamilyObject = global::Avalonia.Media.FontFamily.Default;
                    Log.Debug("[Settings] Using default FontFamily object");
                }
            }
            catch (Exception ex)
            {
                Log.Debug("[Settings] Error creating FontFamily object: {Message}", ex.Message);
                EffectiveFontFamilyObject = global::Avalonia.Media.FontFamily.Default;
            }
        }
        
        public string FontDisplayName
        {
            get => _fontDisplayName;
            private set => this.RaiseAndSetIfChanged(ref _fontDisplayName, value);
        }
        
        public string? FontFamily
        {
            get => _fontFamily;
            set
            {
                var valueToSet = (value == "System Default") ? null : value;
                Log.Debug("[Settings] FontFamily setter: Script={ScriptName}, Input={Input}, Storing={Storing}, Old={Old}", 
                    ScriptName, value, valueToSet ?? "(null)", _fontFamily ?? "(null)");
                if (_fontFamily != valueToSet)
                {
                    this.RaiseAndSetIfChanged(ref _fontFamily, valueToSet);
                    UpdateFontDisplayName();
                    Parent?.UpdateScriptFont(ScriptName, valueToSet, FontSize);
                    
                    // Immediate save
                    _ = Parent?.SaveSettingsAsync();
                }
            }
        }
        
        // The ComboBox's two-way-bound selection (SelectedItem). Its display value is decoupled from the saved
        // FontFamily so a load can show "System Default" for a temporarily-missing font WITHOUT erasing the
        // saved value. Only a genuine user pick persists. (#67)
        public string SelectedFontFamily
        {
            get => _selectedFontFamily;
            set
            {
                // Ignore write-backs that aren't a genuine user pick: the ItemsSource-reset null push (Avalonia
                // clears SelectedItem when AvailableFonts is swapped) and anything during a programmatic apply.
                // "System Default" is the only legitimate unset token; null is always an artifact. (#67 Bug B)
                if (_applyingLoadResult || value is null || value == _selectedFontFamily) return;
                Log.Debug("[Settings] SelectedFontFamily user pick: Script={ScriptName}, Value={Value}", ScriptName, value);
                this.RaiseAndSetIfChanged(ref _selectedFontFamily, value);
                FontFamily = value;   // persist the user's choice
            }
        }

        // Apply a completed font load in one dispatcher frame: swap the list, then set the ComboBox selection
        // for DISPLAY only. The apply flag makes the ItemsSource-reset write-back and this selection set NOT
        // persist, so a load can never wipe a still-saved font (e.g. one temporarily uninstalled). (#67 Bug B)
        internal void ApplyLoadedFonts(IReadOnlyList<string> fonts, string selected)
        {
            _applyingLoadResult = true;
            try
            {
                AvailableFonts = new ObservableCollection<string>(fonts);
                _selectedFontFamily = selected;
                this.RaisePropertyChanged(nameof(SelectedFontFamily));   // force re-select after the ItemsSource swap
            }
            finally { _applyingLoadResult = false; }
        }

        // Latest-wins guard so a slower/stale load can't apply over a newer one for the same script. (#67 Bug A)
        internal int BeginLoad() => Interlocked.Increment(ref _loadVersion);
        internal bool IsCurrentLoad(int version) => Volatile.Read(ref _loadVersion) == version;
        
        public int FontSize
        {
            get => _fontSize;
            set
            {
                this.RaiseAndSetIfChanged(ref _fontSize, value);
                Parent?.UpdateScriptFont(ScriptName, _fontFamily, value);
                
                // Immediate save
                _ = Parent?.SaveSettingsAsync();
            }
        }
        
        public bool IsLoadingFonts
        {
            get => _isLoadingFonts;
            set => this.RaiseAndSetIfChanged(ref _isLoadingFonts, value);
        }

        public ObservableCollection<string> AvailableFonts
        {
            get => _availableFonts;
            set => this.RaiseAndSetIfChanged(ref _availableFonts, value);
        }
        
        public string? SystemDefaultFontName
        {
            get => _systemDefaultFontName;
            private set => this.RaiseAndSetIfChanged(ref _systemDefaultFontName, value);
        }
        
        public AppearanceSettingsViewModel? Parent { get; set; }
        
        public async Task LoadSystemDefaultFontAsync()
        {
            try
            {
                var scriptEnum = GetScriptFromName(ScriptName);
                var fontService = App.ServiceProvider!.GetRequiredService<IFontService>();
                var sysFont = await fontService.GetSystemDefaultFontForScriptAsync(scriptEnum);
                // This runs on a thread-pool thread (fire-and-forget Task.Run); marshal the UI-bound
                // writes (SystemDefaultFontName + the display-name refresh) to the UI thread. (XCUT-3)
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    SystemDefaultFontName = sysFont;
                    UpdateFontDisplayName();
                });
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load system default font for script {Script}", ScriptName);
                await Dispatcher.UIThread.InvokeAsync(() => SystemDefaultFontName = null);
            }
        }
        
        public void UpdatePreviewText()
        {
            const string basePaliText = "sabbe satta bhavantu sukhitatta"; // Pali text in Latin script
            
            try
            {
                // Convert script name to Script enum
                var fromScript = Script.Latin;
                var toScript = GetScriptFromName(ScriptName);
                
                // Convert the text to the appropriate script and capitalize
                PreviewText = ScriptConverter.Convert(basePaliText, fromScript, toScript, true);
            }
            catch (Exception)
            {
                // If conversion fails, use original text capitalized. Invariant casing: this is Pāli
                // romanization, and a Turkish/Azerbaijani locale would map 'i' -> 'İ'. (CORE-4)
                PreviewText = basePaliText.ToUpperInvariant();
            }
        }
        
        public static Script GetScriptFromName(string scriptName)
        {
            return scriptName switch
            {
                "Bengali" => Script.Bengali,
                "Cyrillic" => Script.Cyrillic,
                "Devanagari" => Script.Devanagari,
                "Gujarati" => Script.Gujarati,
                "Gurmukhi" => Script.Gurmukhi,
                "Kannada" => Script.Kannada,
                "Khmer" => Script.Khmer,
                "Malayalam" => Script.Malayalam,
                "Myanmar" => Script.Myanmar,
                "Roman" => Script.Latin, // CST4 uses "Roman" instead of "Latin"
                "Latin" => Script.Latin,
                "Sinhala" => Script.Sinhala,
                "Telugu" => Script.Telugu,
                "Thai" => Script.Thai,
                "Tibetan" => Script.Tibetan,
                _ => Script.Devanagari // Default fallback
            };
        }
        
        public void UpdateFontDisplayName()
        {
            try
            {
                if (string.IsNullOrWhiteSpace(FontFamily))
                {
                    // Use cached system default font name if available
                    if (!string.IsNullOrEmpty(SystemDefaultFontName))
                    {
                        FontDisplayName = $"System Default ({SystemDefaultFontName})";
                        EffectiveFontFamily = SystemDefaultFontName;
                        Log.Debug("[Settings] Script={ScriptName}, Setting EffectiveFontFamily to cached system default: {SystemDefault}", 
                            ScriptName, SystemDefaultFontName);
                    }
                    else
                    {
                        // No cached system default font available yet, use generic fallback
                        FontDisplayName = "System Default";
                        EffectiveFontFamily = "Helvetica"; // Use a specific fallback font for preview
                        Log.Debug("[Settings] Script={ScriptName}, Setting EffectiveFontFamily to fallback: Helvetica", ScriptName);
                    }
                }
                else
                {
                    FontDisplayName = FontFamily;
                    EffectiveFontFamily = FontFamily;
                    Log.Debug("[Settings] Script={ScriptName}, Setting EffectiveFontFamily to: {FontFamily}", ScriptName, FontFamily);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Settings] Exception in UpdateFontDisplayName");
                FontDisplayName = string.IsNullOrWhiteSpace(FontFamily) ? "System Default" : FontFamily;
                EffectiveFontFamily = string.IsNullOrWhiteSpace(FontFamily) ? "Helvetica" : FontFamily;
            }
            
            // Force property change notification for EffectiveFontFamily to update the preview
            Log.Debug("[Settings] Forcing property change for EffectiveFontFamily: {FontFamily}", EffectiveFontFamily);
            this.RaisePropertyChanged(nameof(EffectiveFontFamily));
        }
        
    }

    // Configuration category (#100): settings file location + open-folder.
    public class ConfigurationSettingsViewModel : ViewModelBase
    {
        private readonly ILogger _logger;
        private readonly ISettingsService _settingsService;
        private bool _useHardwareAcceleration;

        public ConfigurationSettingsViewModel(ISettingsService settingsService)
        {
            _logger = Log.ForContext<ConfigurationSettingsViewModel>();
            _settingsService = settingsService;
            _useHardwareAcceleration = settingsService.Settings.UseHardwareAcceleration;
            SettingsFilePath = settingsService.GetSettingsFilePath();
            OpenSettingsFileCommand = ReactiveCommand.Create(OpenSettingsFile);
        }

        // Hardware acceleration for the embedded WebView. OFF forces software compositing, avoiding the CEF
        // off-screen-rendering "black view" stall seen under some GPUs / virtualized drivers on Windows. Applied
        // on the next launch (the CEF switch is set before the browser initializes). (#401)
        public bool UseHardwareAcceleration
        {
            get => _useHardwareAcceleration;
            set
            {
                this.RaiseAndSetIfChanged(ref _useHardwareAcceleration, value);
                _settingsService.Settings.UseHardwareAcceleration = value;
                _settingsService.RequestSave();
            }
        }

        // The mitigation only takes effect on Windows (the black-view stall is Windows / virtual-GPU specific;
        // macOS/Linux keep the GPU), so the Graphics group is hidden off-Windows rather than shown as a no-op. (#401)
        public bool IsWindows => OperatingSystem.IsWindows();

        public string SettingsFilePath { get; }

        public ReactiveCommand<Unit, Unit> OpenSettingsFileCommand { get; }

        private void OpenSettingsFile()
        {
            try
            {
                var directory = Path.GetDirectoryName(SettingsFilePath);
                if (directory != null && Directory.Exists(directory))
                {
                    // Open file explorer at the settings directory
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = directory,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to open settings directory");
            }
        }
    }

    // XML Data Updates category (#100): GitHub source for the Tipitaka XML.
    public class XmlUpdateSettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private bool _enableAutomaticUpdates;
        private string _xmlRepositoryOwner;
        private string _xmlRepositoryName;
        private string _xmlRepositoryPath;
        private string _xmlRepositoryBranch;

        public XmlUpdateSettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;

            var xmlSettings = _settingsService.Settings.XmlUpdateSettings;
            _enableAutomaticUpdates = xmlSettings.EnableAutomaticUpdates;
            _xmlRepositoryOwner = xmlSettings.XmlRepositoryOwner;
            _xmlRepositoryName = xmlSettings.XmlRepositoryName;
            _xmlRepositoryPath = xmlSettings.XmlRepositoryPath;
            _xmlRepositoryBranch = xmlSettings.XmlRepositoryBranch;

            RestoreDefaultsCommand = ReactiveCommand.Create(RestoreDefaults);
        }

        // Reset the four repository fields to the known-good defaults so a user who accidentally
        // edited one doesn't have to know the correct value (or delete settings.json). Leaves the
        // "Enable automatic updates" checkbox alone (it's a preference, not part of the source). (#100)
        public ReactiveCommand<Unit, Unit> RestoreDefaultsCommand { get; }

        private void RestoreDefaults()
        {
            var defaults = new XmlUpdateSettings();
            XmlRepositoryOwner = defaults.XmlRepositoryOwner;
            XmlRepositoryName = defaults.XmlRepositoryName;
            XmlRepositoryPath = defaults.XmlRepositoryPath;
            XmlRepositoryBranch = defaults.XmlRepositoryBranch;
        }

        public bool EnableAutomaticUpdates
        {
            get => _enableAutomaticUpdates;
            set
            {
                this.RaiseAndSetIfChanged(ref _enableAutomaticUpdates, value);
                _settingsService.Settings.XmlUpdateSettings.EnableAutomaticUpdates = value;
                _settingsService.RequestSave();
            }
        }
        
        public string XmlRepositoryOwner
        {
            get => _xmlRepositoryOwner;
            set
            {
                this.RaiseAndSetIfChanged(ref _xmlRepositoryOwner, value);
                _settingsService.Settings.XmlUpdateSettings.XmlRepositoryOwner = value;
                _settingsService.RequestSave();
            }
        }
        
        public string XmlRepositoryName
        {
            get => _xmlRepositoryName;
            set
            {
                this.RaiseAndSetIfChanged(ref _xmlRepositoryName, value);
                _settingsService.Settings.XmlUpdateSettings.XmlRepositoryName = value;
                _settingsService.RequestSave();
            }
        }
        
        public string XmlRepositoryPath
        {
            get => _xmlRepositoryPath;
            set
            {
                this.RaiseAndSetIfChanged(ref _xmlRepositoryPath, value);
                _settingsService.Settings.XmlUpdateSettings.XmlRepositoryPath = value;
                _settingsService.RequestSave();
            }
        }
        
        public string XmlRepositoryBranch
        {
            get => _xmlRepositoryBranch;
            set
            {
                this.RaiseAndSetIfChanged(ref _xmlRepositoryBranch, value);
                _settingsService.Settings.XmlUpdateSettings.XmlRepositoryBranch = value;
                _settingsService.RequestSave();
            }
        }
    }

    // Dictionary Data Updates category — parallels XmlUpdateSettingsViewModel, for the derived dictionary assets
    // (dpd-cst-subset, dppn, …) delivered from the cst-dictionaries repo's releases. (#468)
    public class DpdUpdateSettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private bool _enableAutomaticUpdates;
        private string _repositoryOwner;
        private string _repositoryName;

        public DpdUpdateSettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            var s = _settingsService.Settings.DpdUpdateSettings;
            _enableAutomaticUpdates = s.EnableAutomaticUpdates;
            _repositoryOwner = s.RepositoryOwner;
            _repositoryName = s.RepositoryName;

            RestoreDefaultsCommand = ReactiveCommand.Create(RestoreDefaults);
        }

        // Reset the repository fields to the known-good defaults; leaves the "Enable automatic updates"
        // checkbox alone (it's a preference, not part of the source). Mirrors the XML category. (#468)
        public ReactiveCommand<Unit, Unit> RestoreDefaultsCommand { get; }

        private void RestoreDefaults()
        {
            var defaults = new DpdUpdateSettings();
            RepositoryOwner = defaults.RepositoryOwner;
            RepositoryName = defaults.RepositoryName;
        }

        public bool EnableAutomaticUpdates
        {
            get => _enableAutomaticUpdates;
            set
            {
                this.RaiseAndSetIfChanged(ref _enableAutomaticUpdates, value);
                _settingsService.Settings.DpdUpdateSettings.EnableAutomaticUpdates = value;
                _settingsService.RequestSave();
            }
        }

        public string RepositoryOwner
        {
            get => _repositoryOwner;
            set
            {
                this.RaiseAndSetIfChanged(ref _repositoryOwner, value);
                _settingsService.Settings.DpdUpdateSettings.RepositoryOwner = value;
                _settingsService.RequestSave();
            }
        }

        public string RepositoryName
        {
            get => _repositoryName;
            set
            {
                this.RaiseAndSetIfChanged(ref _repositoryName, value);
                _settingsService.Settings.DpdUpdateSettings.RepositoryName = value;
                _settingsService.RequestSave();
            }
        }
    }

    /// <summary>The "Dictionary" settings category (#479): two groups under one nav entry — the source
    /// enable/order preference (<see cref="Sources"/>) and the existing update settings (<see cref="Updates"/>).</summary>
    public class DictionaryCategoryViewModel : ViewModelBase
    {
        public DictionarySourceSettingsViewModel Sources { get; }
        public DpdUpdateSettingsViewModel Updates { get; }

        public DictionaryCategoryViewModel(DictionarySourceSettingsViewModel sources, DpdUpdateSettingsViewModel updates)
        {
            Sources = sources;
            Updates = updates;
        }
    }

    /// <summary>Editor for the dictionary source enable/order preference (#479): a row per installed source
    /// with an enable checkbox and up/down reorder. Edits go straight to the shared preference service, which
    /// the live dictionary panel observes to rebuild its picker.</summary>
    public class DictionarySourceSettingsViewModel : ViewModelBase
    {
        private readonly Services.Dictionaries.DictionarySourcePreferenceService _prefs;
        private bool _rebuilding;

        public ObservableCollection<DictionarySourceRowViewModel> Rows { get; } = new();

        public DictionarySourceSettingsViewModel(Services.Dictionaries.DictionarySourcePreferenceService prefs)
        {
            _prefs = prefs;
            RebuildRows();
        }

        private void RebuildRows()
        {
            _rebuilding = true;
            Rows.Clear();
            var rows = _prefs.GetRows();
            var enabledCount = rows.Count(r => r.Enabled);
            for (var i = 0; i < rows.Count; i++)
            {
                var r = rows[i];
                Rows.Add(new DictionarySourceRowViewModel(this, r.Source.Id, r.Source.DisplayName, r.Enabled)
                {
                    // The last remaining enabled source can't be unchecked — the picker must never be empty.
                    CanDisable = !(r.Enabled && enabledCount <= 1),
                    CanMoveUp = i > 0,
                    CanMoveDown = i < rows.Count - 1,
                });
            }
            _rebuilding = false;
        }

        internal void OnRowEnabledChanged(DictionarySourceRowViewModel row, bool enabled)
        {
            if (_rebuilding) return;
            _prefs.SetEnabled(row.Id, enabled);
            // Defer the row rebuild off the checkbox's own binding-write callback — mutating the bound
            // ItemsSource from inside the originating write is the kind of re-entrancy Avalonia tolerates
            // unreliably. The next dispatcher turn refreshes the last-enabled guard on every row. (Fable LOW-6)
            Dispatcher.UIThread.Post(RebuildRows);
        }

        internal void MoveRow(DictionarySourceRowViewModel row, int delta)
        {
            _prefs.Move(row.Id, delta);
            RebuildRows();
        }
    }

    /// <summary>One source row in the Dictionary → Sources editor (#479).</summary>
    public class DictionarySourceRowViewModel : ViewModelBase
    {
        private readonly DictionarySourceSettingsViewModel _parent;
        private bool _enabled;

        public string Id { get; }
        public string DisplayName { get; }
        public bool CanDisable { get; init; } = true;
        public bool CanMoveUp { get; init; }
        public bool CanMoveDown { get; init; }

        public ReactiveCommand<Unit, Unit> MoveUpCommand { get; }
        public ReactiveCommand<Unit, Unit> MoveDownCommand { get; }

        public DictionarySourceRowViewModel(DictionarySourceSettingsViewModel parent, string id, string displayName, bool enabled)
        {
            _parent = parent;
            Id = id;
            DisplayName = displayName;
            _enabled = enabled;   // set the field directly so rebuilding rows never re-enters SetEnabled
            MoveUpCommand = ReactiveCommand.Create(() => _parent.MoveRow(this, -1));
            MoveDownCommand = ReactiveCommand.Create(() => _parent.MoveRow(this, +1));
        }

        public bool Enabled
        {
            get => _enabled;
            set
            {
                if (_enabled == value) return;
                this.RaiseAndSetIfChanged(ref _enabled, value);
                _parent.OnRowEnabledChanged(this, value);
            }
        }
    }

    // AI category (#186): the opt-in "Enable AI Features" master switch and the local-API sub-permissions.
    public class AiSettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private bool _aiEnabled;
        private bool _localApiEnabled;
        private bool _mcpEnabled;
        private bool _allowRemoteControl;

        public AiSettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;

            var ai = _settingsService.Settings.Ai;
            _aiEnabled = ai.Enabled;
            _localApiEnabled = ai.LocalApi.Enabled;
            _mcpEnabled = ai.LocalApi.EnableMcpServer;
            _allowRemoteControl = ai.LocalApi.AllowRemoteControl;
        }

        /// <summary>Master switch — "Enable AI Features". Everything AI-related is gated behind this (default OFF).</summary>
        public bool AiEnabled
        {
            get => _aiEnabled;
            set
            {
                this.RaiseAndSetIfChanged(ref _aiEnabled, value);
                _settingsService.Settings.Ai.Enabled = value;
                _settingsService.RequestSave();
                // The two enable-gates below both depend on the master.
                this.RaisePropertyChanged(nameof(SubPermissionsEnabled));
                this.RaisePropertyChanged(nameof(RemoteControlEnabled));
            }
        }

        /// <summary>Expose the /v1 REST surface (corpus data for code agents). Effective only while the master
        /// is also on. Independent of <see cref="McpEnabled"/> — the two surfaces run separately. (#280)</summary>
        public bool LocalApiEnabled
        {
            get => _localApiEnabled;
            set
            {
                this.RaiseAndSetIfChanged(ref _localApiEnabled, value);
                _settingsService.Settings.Ai.LocalApi.Enabled = value;
                _settingsService.RequestSave();
                // Remote control follows "a server surface is running", not the REST flag specifically. (#440)
                this.RaisePropertyChanged(nameof(RemoteControlEnabled));
            }
        }

        /// <summary>Expose the /mcp surface (for chat clients like Claude Desktop, via the app's --mcp-bridge
        /// relay). Effective only while the master is also on. Independent of <see cref="LocalApiEnabled"/>. The
        /// #318 workaround that forced this to track the REST flag is gone now that it has its own toggle. (#280)</summary>
        public bool McpEnabled
        {
            get => _mcpEnabled;
            set
            {
                this.RaiseAndSetIfChanged(ref _mcpEnabled, value);
                _settingsService.Settings.Ai.LocalApi.EnableMcpServer = value;
                _settingsService.RequestSave();
                // navigate is offered over BOTH surfaces, so remote control is reachable whenever EITHER runs. (#440)
                this.RaisePropertyChanged(nameof(RemoteControlEnabled));
            }
        }

        /// <summary>Let agents drive the reader (navigate/highlight) vs. read-only.</summary>
        public bool AllowRemoteControl
        {
            get => _allowRemoteControl;
            set
            {
                this.RaiseAndSetIfChanged(ref _allowRemoteControl, value);
                _settingsService.Settings.Ai.LocalApi.AllowRemoteControl = value;
                _settingsService.RequestSave();
            }
        }

        /// <summary>Pre-populated Claude Desktop MCP config for the "Copy MCP configuration" button. Emits the
        /// #278 bridge config (spawn this app with --mcp-bridge); carries no port/token. (#280 reworks the UI.)</summary>
        public string McpClientConfigJson => CST.Avalonia.Services.LocalApi.McpClientConfig.ClaudeDesktop(
            System.Environment.ProcessPath ?? "CST Reader");

        /// <summary>The local-API sub-permissions are editable only when the master switch is on.</summary>
        public bool SubPermissionsEnabled => AiEnabled;

        /// <summary>"Allow remote control" is editable whenever the master is on and a server surface (REST OR
        /// MCP) is running — because navigate is offered over both. Keying it to the REST flag alone would grey
        /// it out for an MCP-only user whose navigate works fine, telling them to enable a box already ticked. (#440)</summary>
        public bool RemoteControlEnabled => AiEnabled && (LocalApiEnabled || McpEnabled);
    }

    public class DeveloperSettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly ILogger _logger;
        private string _logLevel;

        public DeveloperSettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            _logger = Log.ForContext<DeveloperSettingsViewModel>();
            _logLevel = _settingsService.Settings.DeveloperSettings.LogLevel;

            // Available log levels — the single canonical set the validator accepts and the parsers
            // understand, so a chosen level (e.g. "Fatal") can't be sanitized away on restart. (STATE-4)
            LogLevels = SettingsValidator.LogLevels;

            // Open logs folder command
            OpenLogsCommand = ReactiveCommand.Create(OpenLogsFolder);
            

            // Update service when log level changes
            this.WhenAnyValue(x => x.LogLevel)
                .Skip(1)
                .Subscribe(value => 
                {
                    _settingsService.Settings.DeveloperSettings.LogLevel = value;
                    _settingsService.RequestSave();
                    
                    // Reconfigure logger immediately
                    ReconfigureLogger(value);
                    _logger.Information("Log level changed to: {LogLevel}", value);
                });
        }

        public string LogLevel
        {
            get => _logLevel;
            set => this.RaiseAndSetIfChanged(ref _logLevel, value);
        }

        public string[] LogLevels { get; }
        public ViewModelBase? Parent { get; set; }
        public ReactiveCommand<Unit, Unit> OpenLogsCommand { get; }

        private void OpenLogsFolder()
        {
            try
            {
                var appSupportDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    AppConstants.AppDataDirectoryName);
                var logsDir = Path.Combine(appSupportDir, "logs");
                
                if (Directory.Exists(logsDir))
                {
                    System.Diagnostics.Process.Start(new System.Diagnostics.ProcessStartInfo
                    {
                        FileName = logsDir,
                        UseShellExecute = true,
                        Verb = "open"
                    });
                }
                else
                {
                    _logger.Warning("Logs directory does not exist: {LogsDir}", logsDir);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to open logs directory");
            }
        }

        
        private void ReconfigureLogger(string logLevel)
        {
            try
            {
                // Convert string to Serilog LogEventLevel
                var serilogLevel = logLevel switch
                {
                    "Debug" => Serilog.Events.LogEventLevel.Debug,
                    "Information" => Serilog.Events.LogEventLevel.Information,
                    "Warning" => Serilog.Events.LogEventLevel.Warning,
                    "Error" => Serilog.Events.LogEventLevel.Error,
                    "Fatal" => Serilog.Events.LogEventLevel.Fatal,
                    _ => Serilog.Events.LogEventLevel.Information
                };

                // Get the logs directory
                var appSupportDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    AppConstants.AppDataDirectoryName);
                var logsDir = Path.Combine(appSupportDir, "logs");
                
                // Ensure logs directory exists
                if (!Directory.Exists(logsDir))
                {
                    Directory.CreateDirectory(logsDir);
                }
                
                var logPath = Path.Combine(logsDir, "cst-avalonia-.log");

                // Reconfigure the global logger
                Log.Logger = new Serilog.LoggerConfiguration()
                    .MinimumLevel.Is(serilogLevel)
                    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                    .WriteTo.File(logPath, 
                        rollingInterval: Serilog.RollingInterval.Day,
                        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
                    .Enrich.FromLogContext()
                    .CreateLogger();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to reconfigure logger");
            }
        }
    }
}
