using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Platform.Storage;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Conversion;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using Serilog;

namespace CST.Avalonia.ViewModels
{
    public class SettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly ILogger _logger;
        private string _searchQuery = "";
        private SettingsCategoryViewModel? _selectedCategory;
        private bool _hasUnsavedChanges;

        public SettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            _logger = Log.ForContext<SettingsViewModel>();

#if MACOS
            // Quick test: Try font detection for different scripts
            TestFontDetectionForMultipleScripts();
#endif

            // Initialize categories
            var generalSettings = new GeneralSettingsViewModel(_settingsService) { Parent = this };
            var appearanceSettings = new AppearanceSettingsViewModel(_settingsService);
            var searchSettings = new SearchSettingsViewModel(_settingsService);
            var advancedSettings = new AdvancedSettingsViewModel(_settingsService);
            var developerSettings = new DeveloperSettingsViewModel(_settingsService) { Parent = this };
            
            Categories = new ObservableCollection<SettingsCategoryViewModel>
            {
                new SettingsCategoryViewModel("General", "General application settings", generalSettings),
                new SettingsCategoryViewModel("Appearance", "Theme and display settings", appearanceSettings),
                new SettingsCategoryViewModel("Search", "Search behavior and options", searchSettings),
                new SettingsCategoryViewModel("Advanced", "Advanced configuration options", advancedSettings),
                new SettingsCategoryViewModel("Developer", "Debugging and diagnostic tools", developerSettings)
            };

            // Select first category by default
            SelectedCategory = Categories.FirstOrDefault();


            // Watch for changes to mark as dirty
            this.WhenAnyValue(x => x.SearchQuery)
                .Throttle(TimeSpan.FromMilliseconds(300))
                .Subscribe(_ => FilterSettings());
        }

        public ObservableCollection<SettingsCategoryViewModel> Categories { get; }

        public string SearchQuery
        {
            get => _searchQuery;
            set => this.RaiseAndSetIfChanged(ref _searchQuery, value);
        }

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

        private void FilterSettings()
        {
            // TODO: Implement search filtering across all categories
            _logger.Debug("Filtering settings with query: {Query}", SearchQuery);
        }


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

#if MACOS
        private void RunTiroDevanagariTest()
        {
            try
            {
                var test = App.ServiceProvider?.GetService<Services.Platform.Mac.TiroDevanagariTest>();
                if (test != null)
                {
                    _logger.Information("Running Tiro Devanagari font test...");
                    test.TestTiroDevanagariFont();
                }
                else
                {
                    _logger.Warning("TiroDevanagariTest service not available");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to run Tiro Devanagari test");
            }
        }

        private async void TestFontDetectionForMultipleScripts()
        {
            try
            {
                _logger.Information("Testing font detection for multiple scripts...");
                
                var fontService = App.ServiceProvider?.GetService<IFontService>();
                if (fontService == null)
                {
                    _logger.Warning("Font service not available for testing");
                    return;
                }

                // Test a few different scripts
                var scriptsToTest = new[] 
                { 
                    Script.Bengali, 
                    Script.Telugu, 
                    Script.Gujarati,
                    Script.Myanmar 
                };

                foreach (var script in scriptsToTest)
                {
                    _logger.Information("Testing font detection for {Script}", script);
                    var fonts = await fontService.GetAvailableFontsForScriptAsync(script);
                    _logger.Information("Found {Count} fonts for {Script}: {Fonts}", 
                        fonts.Count, script, string.Join(", ", fonts.Take(5)));
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error during font detection test");
            }
        }
#endif
    }

    public class SettingsCategoryViewModel : ViewModelBase
    {
        public SettingsCategoryViewModel(string name, string description, ViewModelBase content)
        {
            Name = name;
            Description = description;
            Content = content;
        }

        public string Name { get; }
        public string Description { get; }
        public ViewModelBase Content { get; }
    }

    public class GeneralSettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private string _xmlBooksDirectory;
        private string _indexDirectory;
        private bool _showWelcomeOnStartup;
        private int _maxRecentBooks;

        public GeneralSettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            _xmlBooksDirectory = _settingsService.Settings.XmlBooksDirectory;
            _indexDirectory = _settingsService.Settings.IndexDirectory;
            _showWelcomeOnStartup = _settingsService.Settings.ShowWelcomeOnStartup;
            _maxRecentBooks = _settingsService.Settings.MaxRecentBooks;

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
                    _ = _settingsService.SaveSettingsAsync();
                });
                
            this.WhenAnyValue(x => x.IndexDirectory)
                .Skip(1)
                .Subscribe(value => 
                {
                    _settingsService.UpdateSetting(nameof(Settings.IndexDirectory), value);
                    _ = _settingsService.SaveSettingsAsync();
                });

            this.WhenAnyValue(x => x.ShowWelcomeOnStartup)
                .Skip(1)
                .Subscribe(value => 
                {
                    _settingsService.UpdateSetting(nameof(Settings.ShowWelcomeOnStartup), value);
                    _ = _settingsService.SaveSettingsAsync();
                });

            this.WhenAnyValue(x => x.MaxRecentBooks)
                .Skip(1)
                .Subscribe(value => 
                {
                    _settingsService.UpdateSetting(nameof(Settings.MaxRecentBooks), value);
                    _ = _settingsService.SaveSettingsAsync();
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

        public bool ShowWelcomeOnStartup
        {
            get => _showWelcomeOnStartup;
            set => this.RaiseAndSetIfChanged(ref _showWelcomeOnStartup, value);
        }

        public int MaxRecentBooks
        {
            get => _maxRecentBooks;
            set => this.RaiseAndSetIfChanged(ref _maxRecentBooks, value);
        }

        public ViewModelBase? Parent { get; set; }
        public ReactiveCommand<Unit, Unit> BrowseCommand { get; }
        public ReactiveCommand<Unit, Unit> BrowseIndexCommand { get; }
    }

    public class AppearanceSettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly IFontService _fontService;
        private string _theme = "";
        private ScriptFontSettingViewModel? _selectedScript;
        internal bool _isChangingScript = false;

        public AppearanceSettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            _fontService = App.ServiceProvider.GetRequiredService<IFontService>();
            _theme = _settingsService.Settings.Theme;

            Themes = new[] { "Light", "Dark", "Auto" };
            
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
            
            
            this.WhenAnyValue(x => x.SelectedScript)
                .Where(s => s != null)
                .Subscribe(s => LoadAvailableFontsForScript(s!));
                
            // Pre-load fonts for all scripts to prevent empty dropdown on first click
            _ = Task.Run(async () =>
            {
                await Task.Delay(100); // Small delay to let UI initialize first
                foreach (var script in ScriptFontSettings)
                {
                    LoadAvailableFontsForScript(script); // Fire and forget - runs async internally
                    await Task.Delay(10); // Small delay between scripts to avoid overwhelming the UI
                }
            });
        }

        public string Theme
        {
            get => _theme;
            set
            {
                this.RaiseAndSetIfChanged(ref _theme, value);
                _settingsService.UpdateSetting(nameof(Settings.Theme), value);
                
                // Immediate save
                _ = _settingsService.SaveSettingsAsync();
            }
        }

        public string[] Themes { get; }
        
        public ObservableCollection<ScriptFontSettingViewModel> ScriptFontSettings { get; }
        
        public ScriptFontSettingViewModel? SelectedScript
        {
            get => _selectedScript;
            set 
            {
                Log.Debug("[Settings] SelectedScript setter called: {ScriptName}", value?.ScriptName ?? "null");
                
                // Set flag to prevent font changes during script switching
                _isChangingScript = true;
                this.RaiseAndSetIfChanged(ref _selectedScript, value);
                _isChangingScript = false;
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
                _ = _settingsService.SaveSettingsAsync();
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
                _ = _settingsService.SaveSettingsAsync();
            }
        }
        
        public void UpdateScriptFont(string scriptName, string? fontFamily, int fontSize)
        {
            if (_settingsService.Settings.FontSettings.ScriptFonts.TryGetValue(scriptName, out var setting))
            {
                setting.FontFamily = fontFamily;
                setting.FontSize = fontSize;
                
                // Notify FontService about the change so other components update
                var fontService = App.ServiceProvider?.GetService(typeof(IFontService)) as IFontService;
                fontService?.UpdateFontSettings(_settingsService.Settings.FontSettings);
            }
        }
        
        public async Task SaveSettingsAsync()
        {
            await _settingsService.SaveSettingsAsync();
        }
        
        private async void LoadAvailableFontsForScript(ScriptFontSettingViewModel scriptVm)
        {
            // No loading state needed since fonts are pre-cached
            var scriptEnum = ScriptFontSettingViewModel.GetScriptFromName(scriptVm.ScriptName);
            
            Log.Debug("[Settings] Loading fonts for {ScriptName}", scriptVm.ScriptName);
            
            try
            {
                // Always use await to ensure we get the cached or fresh fonts properly
                var fonts = await _fontService.GetAvailableFontsForScriptAsync(scriptEnum);
                Log.Debug("[Settings] {ScriptName}: {FontCount} fonts found", scriptVm.ScriptName, fonts?.Count ?? 0);
                
                if (fonts == null || fonts.Count == 0)
                {
                    Log.Debug("[Settings] {ScriptName}: No fonts found - retrying...", scriptVm.ScriptName);
                    // Retry once after a small delay
                    await Task.Delay(100);
                    fonts = await _fontService.GetAvailableFontsForScriptAsync(scriptEnum);
                    Log.Debug("[Settings] {ScriptName}: Retry got {FontCount} fonts", scriptVm.ScriptName, fonts?.Count ?? 0);
                }
                
                // Create a copy to avoid modifying the cached list
                var fontsCopy = new List<string>(fonts ?? new List<string>());
                
                // Add a "System Default" option to the top of the list (safe since we have a copy)
                fontsCopy.Insert(0, "System Default");
                
                // Get the saved font from settings
                var savedFontFamily = scriptVm.FontFamily; // This comes from the saved settings
                
                // Log for debugging
                Log.Debug("[Settings] {ScriptName}: {TotalFonts} fonts total (saved: {SavedFont})", 
                    scriptVm.ScriptName, fontsCopy.Count, savedFontFamily ?? "null");
                
                // Set the fonts collection
                scriptVm.AvailableFonts = new ObservableCollection<string>(fontsCopy);
                
                // Load system default font information (fire and forget - purely for display)
                _ = Task.Run(async () =>
                {
                    try
                    {
                        await scriptVm.LoadSystemDefaultFontAsync();
                    }
                    catch (Exception ex)
                    {
                        Log.Debug("[Settings] {ScriptName}: Failed to load system default font info - {Message}", 
                            scriptVm.ScriptName, ex.Message);
                    }
                });
                
                // Force a small delay to ensure UI has time to process the collection change
                await Task.Delay(50);
                
                // Set the selected font based on what's saved in settings
                // Handle both null and empty string as "unset" (for backward compatibility)
                if (!string.IsNullOrWhiteSpace(savedFontFamily))
                {
                    // Look for the saved font in the list (case-insensitive and trimmed comparison)
                    var matchingFont = fontsCopy.FirstOrDefault(f => 
                        string.Equals(f?.Trim(), savedFontFamily.Trim(), StringComparison.OrdinalIgnoreCase));
                        
                    if (matchingFont != null)
                    {
                        // Set the selection to the matching font (use the exact string from the list)
                        Log.Debug("[Settings] {ScriptName}: Selected font {Font}", scriptVm.ScriptName, matchingFont);
                        scriptVm.SelectedFontFamily = matchingFont;
                    }
                    else
                    {
                        // Saved font not found in list, default to System Default
                        Log.Debug("[Settings] {ScriptName}: Font '{SavedFont}' not found, using System Default", 
                            scriptVm.ScriptName, savedFontFamily);
                        scriptVm.SelectedFontFamily = "System Default";
                    }
                }
                else
                {
                    // No font saved (null or empty), select "System Default"
                    Log.Debug("[Settings] {ScriptName}: No saved font, using System Default", scriptVm.ScriptName);
                    scriptVm.SelectedFontFamily = "System Default";
                }
                
                // Another small delay to ensure selection is applied
                await Task.Delay(25);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[Settings] {ScriptName}: Error loading fonts", scriptVm.ScriptName);
                // Fallback: provide at least "System Default" 
                scriptVm.AvailableFonts = new ObservableCollection<string> { "System Default" };
                scriptVm.SelectedFontFamily = "System Default";
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
        
        public string SelectedFontFamily
        {
            get {
                var result = string.IsNullOrWhiteSpace(_fontFamily) ? "System Default" : _fontFamily;
                Log.Debug("[Settings] SelectedFontFamily getter: Script={ScriptName}, Returning={Result}", ScriptName, result);
                return result;
            }
            set 
            {
                Log.Debug("[Settings] SelectedFontFamily setter: Script={ScriptName}, Input={Input}, CurrentGet={Current}", 
                    ScriptName, value, string.IsNullOrWhiteSpace(_fontFamily) ? "System Default" : _fontFamily);
                
                // Ignore font changes during script switching to prevent overwriting other scripts' fonts
                if (Parent is AppearanceSettingsViewModel parent && parent._isChangingScript)
                {
                    Log.Debug("[Settings] Ignoring font change for {ScriptName} during script switching", ScriptName);
                    return;
                }
                
                FontFamily = value;
            }
        }
        
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
                var fontService = App.ServiceProvider.GetRequiredService<IFontService>();
                SystemDefaultFontName = await fontService.GetSystemDefaultFontForScriptAsync(scriptEnum);
                UpdateFontDisplayName(); // Refresh display name with system default info
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to load system default font for script {Script}", ScriptName);
                SystemDefaultFontName = null;
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
                // If conversion fails, use original text capitalized
                PreviewText = basePaliText.ToUpper();
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

    public class SearchSettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private bool _caseSensitive;
        private bool _wholeWords;
        private bool _useRegex;
        private int _maxSearchResults;

        public SearchSettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            var settings = _settingsService.Settings.SearchSettings;
            _caseSensitive = settings.CaseSensitive;
            _wholeWords = settings.WholeWords;
            _useRegex = settings.UseRegex;
            _maxSearchResults = settings.MaxSearchResults;
        }

        public bool CaseSensitive
        {
            get => _caseSensitive;
            set
            {
                this.RaiseAndSetIfChanged(ref _caseSensitive, value);
                _settingsService.Settings.SearchSettings.CaseSensitive = value;
            }
        }

        public bool WholeWords
        {
            get => _wholeWords;
            set
            {
                this.RaiseAndSetIfChanged(ref _wholeWords, value);
                _settingsService.Settings.SearchSettings.WholeWords = value;
            }
        }

        public bool UseRegex
        {
            get => _useRegex;
            set
            {
                this.RaiseAndSetIfChanged(ref _useRegex, value);
                _settingsService.Settings.SearchSettings.UseRegex = value;
            }
        }

        public int MaxSearchResults
        {
            get => _maxSearchResults;
            set
            {
                this.RaiseAndSetIfChanged(ref _maxSearchResults, value);
                _settingsService.Settings.SearchSettings.MaxSearchResults = value;
            }
        }
    }

    public class AdvancedSettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private readonly ILogger _logger;
        private bool _enableAutomaticUpdates;
        private string _xmlRepositoryOwner;
        private string _xmlRepositoryName;
        private string _xmlRepositoryPath;
        private string _xmlRepositoryBranch;

        public AdvancedSettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            _logger = Log.ForContext<AdvancedSettingsViewModel>();
            
            SettingsFilePath = _settingsService.GetSettingsFilePath();
            OpenSettingsFileCommand = ReactiveCommand.Create(OpenSettingsFile);
            
            // Initialize XML update settings
            var xmlSettings = _settingsService.Settings.XmlUpdateSettings;
            _enableAutomaticUpdates = xmlSettings.EnableAutomaticUpdates;
            _xmlRepositoryOwner = xmlSettings.XmlRepositoryOwner;
            _xmlRepositoryName = xmlSettings.XmlRepositoryName;
            _xmlRepositoryPath = xmlSettings.XmlRepositoryPath;
            _xmlRepositoryBranch = xmlSettings.XmlRepositoryBranch;
        }

        public string SettingsFilePath { get; }
        
        public ReactiveCommand<Unit, Unit> OpenSettingsFileCommand { get; }
        
        public bool EnableAutomaticUpdates
        {
            get => _enableAutomaticUpdates;
            set
            {
                this.RaiseAndSetIfChanged(ref _enableAutomaticUpdates, value);
                _settingsService.Settings.XmlUpdateSettings.EnableAutomaticUpdates = value;
                _ = _settingsService.SaveSettingsAsync();
            }
        }
        
        public string XmlRepositoryOwner
        {
            get => _xmlRepositoryOwner;
            set
            {
                this.RaiseAndSetIfChanged(ref _xmlRepositoryOwner, value);
                _settingsService.Settings.XmlUpdateSettings.XmlRepositoryOwner = value;
                _ = _settingsService.SaveSettingsAsync();
            }
        }
        
        public string XmlRepositoryName
        {
            get => _xmlRepositoryName;
            set
            {
                this.RaiseAndSetIfChanged(ref _xmlRepositoryName, value);
                _settingsService.Settings.XmlUpdateSettings.XmlRepositoryName = value;
                _ = _settingsService.SaveSettingsAsync();
            }
        }
        
        public string XmlRepositoryPath
        {
            get => _xmlRepositoryPath;
            set
            {
                this.RaiseAndSetIfChanged(ref _xmlRepositoryPath, value);
                _settingsService.Settings.XmlUpdateSettings.XmlRepositoryPath = value;
                _ = _settingsService.SaveSettingsAsync();
            }
        }
        
        public string XmlRepositoryBranch
        {
            get => _xmlRepositoryBranch;
            set
            {
                this.RaiseAndSetIfChanged(ref _xmlRepositoryBranch, value);
                _settingsService.Settings.XmlUpdateSettings.XmlRepositoryBranch = value;
                _ = _settingsService.SaveSettingsAsync();
            }
        }

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

            // Available log levels
            LogLevels = new[] { "Debug", "Information", "Warning", "Error", "Fatal" };

            // Open logs folder command
            OpenLogsCommand = ReactiveCommand.Create(OpenLogsFolder);
            
            // Font detection POC command
            TestFontDetectionCommand = ReactiveCommand.Create(TestFontDetection);

            // Update service when log level changes
            this.WhenAnyValue(x => x.LogLevel)
                .Skip(1)
                .Subscribe(value => 
                {
                    _settingsService.Settings.DeveloperSettings.LogLevel = value;
                    _ = _settingsService.SaveSettingsAsync();
                    
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
        public ReactiveCommand<Unit, Unit> TestFontDetectionCommand { get; }

        private void OpenLogsFolder()
        {
            try
            {
                var appSupportDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    "CST.Avalonia");
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

        private void TestFontDetection()
        {
            try
            {
                _logger.Information("Starting Font Detection POC Test");
                
                // Create POC with a basic logger (we'll need DI to get proper logger)
                var pocLogger = Log.ForContext<FontDetectionPOC>();
                var poc = new FontDetectionPOC(pocLogger);
                
                // Run the tests
                poc.RunTests();
                
                _logger.Information("Font Detection POC Test Complete - check logs for results");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to run font detection POC");
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
                    "CST.Avalonia");
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
