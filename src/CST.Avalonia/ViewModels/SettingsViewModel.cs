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
    public class SettingsViewModel : ViewModelBase, IDisposable
    {
        private readonly ISettingsService _settingsService;
        private readonly Services.Dictionaries.DictionarySourcePreferenceService _sourcePrefs;
        private readonly ILogger _logger;
        private SettingsCategoryViewModel? _selectedCategory;
        private bool _hasUnsavedChanges;

        // Held only so it can be disposed with the window; it also lives in Categories.
        private readonly AppearanceSettingsViewModel? _appearanceSettings;

        /// <summary>Releases child subscriptions. Called from the window's Closed handler.</summary>
        public void Dispose() => _appearanceSettings?.Dispose();

        public SettingsViewModel(ISettingsService settingsService, Services.Dictionaries.DictionarySourcePreferenceService sourcePrefs)
        {
            _settingsService = settingsService;
            _sourcePrefs = sourcePrefs;
            _logger = Log.ForContext<SettingsViewModel>();


            // Initialize categories. Nav names describe the actual settings in each (#100), instead of
            // generic groupings (General/Appearance/Advanced/Developer).
            var directoriesSettings = new GeneralSettingsViewModel(_settingsService) { Parent = this };
            var fontSettings = new AppearanceSettingsViewModel(_settingsService);
            _appearanceSettings = fontSettings;
            var configurationSettings = new ConfigurationSettingsViewModel(_settingsService);
            var xmlUpdateSettings = new XmlUpdateSettingsViewModel(_settingsService);
            var dpdUpdateSettings = new DpdUpdateSettingsViewModel(_settingsService);
            // One "Dictionary" category (#479) folds the source enable/order preference together with the
            // existing update settings — two groups under a single nav entry, not two "Dictionary…" entries.
            var dictionarySettings = new DictionaryCategoryViewModel(
                new DictionarySourceSettingsViewModel(_sourcePrefs), dpdUpdateSettings);
            // #585: the assistant half of the AI panel needs the credential store (keys never touch
            // settings.json), the model registry (the fidelity advisory) and the resolver (so Settings
            // reports readiness using the SAME code the assistant will run, rather than a second opinion
            // that can drift from it). Resolved rather than injected because this VM is constructed here.
            var aiSettings = new AiSettingsViewModel(
                _settingsService,
                App.TryGetService<Services.Ai.IAiCredentialStore>(),
                App.TryGetService<Services.Ai.IChatProviderResolver>());
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

    public class AppearanceSettingsViewModel : ViewModelBase, IDisposable
    {
        private readonly ISettingsService _settingsService;
        private readonly IFontService _fontService;
        // #42/#572: kept so the per-script size line can follow zoom changes made in a book window while
        // Settings is open, and so the subscription can be released when this panel goes away.
        private IBookZoomService? _bookZoomService;
        private EventHandler<BookZoomChangedEventArgs>? _bookZoomChangedHandler;

        /// <summary>
        /// Releases the zoom subscription. Without this the panel would be rooted by the BookZoomService
        /// singleton and leak one instance per Settings open.
        /// </summary>
        public void Dispose()
        {
            if (_bookZoomService != null && _bookZoomChangedHandler != null)
                _bookZoomService.ZoomChanged -= _bookZoomChangedHandler;
            _bookZoomChangedHandler = null;
            _bookZoomService = null;
        }

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
            
            // Alphabetical. The dictionary's insertion order put Latin first and the rest in a sequence
            // nobody could predict, which was tolerable in a 4-row box and is not in a list showing all
            // fourteen — with more names visible, a findable order matters more than a privileged one.
            foreach (var kvp in fontSettings.ScriptFonts.OrderBy(k => k.Key, StringComparer.CurrentCulture))
            {
                var vm = new ScriptFontSettingViewModel
                {
                    ScriptName = kvp.Key,
                    FontFamily = kvp.Value.FontFamily,
                    FontSize = kvp.Value.FontSize,
                    Parent = this
                };
                // #42: seed the book face WITHOUT going through the property, which would treat it as a
                // user pick and write it straight back — turning "riding the shipped default" into an
                // explicit choice for every script just by opening Settings.
                vm.SeedBookFontFamily(kvp.Value.BookFontFamily);
                // Initialize the preview text and font display name after setting all properties
                vm.UpdatePreviewText();
                vm.UpdateFontDisplayName();
                vm.UpdateEffectiveFontFamilyObject(); // Initialize the FontFamily object
                ScriptFontSettings.Add(vm);
            }
            
            // #42/#572: the size line shows the live zoom for the selected script, and zoom is changed in a
            // BOOK window while Settings can be open beside it. Without this the only place in the app that
            // reveals zoom is per-script would sit there showing a stale number. (fable review)
            if (App.ServiceProvider?.GetService(typeof(IBookZoomService)) is IBookZoomService zoomSvc)
            {
                _bookZoomService = zoomSvc;
                _bookZoomChangedHandler = (_, _) => Dispatcher.UIThread.Post(() =>
                {
                    foreach (var vm in ScriptFontSettings) vm.RaiseBookSizeDescriptionChanged();
                });
                zoomSvc.ZoomChanged += _bookZoomChangedHandler;
            }

            // Open on the script the app is CURRENTLY displaying, not a hardcoded Latin. Someone reading in
            // Devanagari who opens Settings is almost certainly there to adjust Devanagari; making them
            // find it in a fourteen-item list first is friction with no upside. Falls back to Latin, then
            // to whatever exists, so an unknown or missing script cannot leave nothing selected.
            var currentScript = (App.ServiceProvider?.GetService(typeof(IScriptService)) as IScriptService)?.CurrentScript;
            var currentScriptName = currentScript.HasValue ? ScriptKeys.Of(currentScript.Value) : null;

            SelectedScript = (currentScriptName != null
                                 ? ScriptFontSettings.FirstOrDefault(s => s.ScriptName == currentScriptName)
                                 : null)
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
        
        /// <summary>
        /// Persists the BOOK font face for a script and makes open books pick it up. (#42)
        ///
        /// Distinct from <see cref="UpdateScriptFont"/> above, which sets the app CHROME font for the same
        /// script — the two are separate systems and this one reaches book content only, by being injected
        /// into the stylesheet at transform time.
        /// </summary>
        public void UpdateScriptBookFont(string scriptName, string? bookFontFamily)
        {
            if (!_settingsService.Settings.FontSettings.ScriptFonts.TryGetValue(scriptName, out var setting))
                return;

            // Empty means "ride the shipped default" — see BookFontResolver. Storing a copy of the default
            // instead would freeze this script against whatever the default happened to be today.
            setting.BookFontFamily = bookFontFamily ?? string.Empty;

            // Re-render open books: the face is baked in at transform time, so unlike a chrome font change
            // nothing updates until the HTML is regenerated.
            var fontService = App.ServiceProvider?.GetService(typeof(IFontService)) as IFontService;
            fontService?.UpdateFontSettings(_settingsService.Settings.FontSettings);
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

        /// <summary>
        /// The book-list equivalent of <see cref="ResolveFontSelection"/>.
        ///
        /// <para>
        /// A ComboBox only displays a SelectedItem that is equal to an item in its ItemsSource, so the
        /// saved name has to be resolved to the actual list entry — matching case-insensitively and
        /// trimmed, exactly as the chrome path does. Assigning the raw saved string instead leaves the
        /// control blank whenever it differs by so much as case, and always when the font is no longer
        /// installed. (fable review, then reproduced)
        /// </para>
        ///
        /// <para>
        /// Falls back to the "Default" LABEL for display only. The saved value is untouched, so a font
        /// that is temporarily missing comes back by itself once reinstalled — the #67 rule.
        /// </para>
        /// </summary>
        internal static string ResolveBookFontSelection(IReadOnlyList<string> bookFonts, string? savedFont)
        {
            if (string.IsNullOrWhiteSpace(savedFont))
                return ScriptFontSettingViewModel.BookFontDefaultLabel;

            var match = bookFonts.FirstOrDefault(f =>
                string.Equals(f?.Trim(), savedFont.Trim(), StringComparison.OrdinalIgnoreCase));
            return match ?? ScriptFontSettingViewModel.BookFontDefaultLabel;
        }

        /// <summary>
        /// The saved face, when it is not among the installed ones — the entry the book list has to carry so
        /// the picker can show what is actually configured. Null when there is no saved face, or when it is
        /// installed and therefore already in the list. (#614)
        ///
        /// <para>
        /// The face is listed under its own name, with no "(missing)" decoration. Two reasons: the entry has
        /// to round-trip through <see cref="ScriptFontSettingViewModel.BookFontFamily"/>, where a decorated
        /// label would be stored verbatim as the face name; and telling the user a chosen font is not
        /// installed is #573's job, which can say it once, in one place, for chrome and book alike.
        /// </para>
        /// </summary>
        internal static string? SavedFaceMissingFrom(IReadOnlyList<string> bookFonts, string? savedFont)
        {
            if (string.IsNullOrWhiteSpace(savedFont))
                return null;

            var saved = savedFont.Trim();
            var installed = bookFonts.Any(f =>
                string.Equals(f?.Trim(), saved, StringComparison.OrdinalIgnoreCase));

            return installed ? null : saved;
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
        private string? _bookFontFamily = null;                   // #42: the BOOK face, empty = shipped default
        private string _selectedBookFontFamily = "Default";       // its ComboBox display, decoupled the same way
        private ObservableCollection<string> _availableBookFonts = new();
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
        
        /// <summary>
        /// The BOOK font face for this script, or null to use the shipped default. (#42)
        ///
        /// Not to be confused with <see cref="FontFamily"/> below, which is the app CHROME font. This one
        /// affects only rendered book content.
        /// </summary>
        public string? BookFontFamily
        {
            get => _bookFontFamily;
            set
            {
                var valueToSet = (value == BookFontDefaultLabel) ? null : value;
                if (_bookFontFamily == valueToSet) return;

                this.RaiseAndSetIfChanged(ref _bookFontFamily, valueToSet);
                Parent?.UpdateScriptBookFont(ScriptName, valueToSet);
                _ = Parent?.SaveSettingsAsync();
            }
        }

        /// <summary>Re-reads <see cref="BookSizeDescription"/>, which is computed rather than stored.</summary>
        internal void RaiseBookSizeDescriptionChanged() =>
            this.RaisePropertyChanged(nameof(BookSizeDescription));

        /// <summary>The label standing for "no choice made, use what the app ships".</summary>
        public const string BookFontDefaultLabel = "Default";

        /// <summary>
        /// Loads the saved book face without treating it as a user pick — no persist, no re-render.
        /// Assigning the property would write the value straight back, converting every script from
        /// "no choice made" into an explicit choice the first time Settings opened. (#42)
        /// </summary>
        internal void SeedBookFontFamily(string? saved)
        {
            _bookFontFamily = string.IsNullOrWhiteSpace(saved) ? null : saved;
            _selectedBookFontFamily = _bookFontFamily ?? BookFontDefaultLabel;
        }

        /// <summary>
        /// The book-font ComboBox's selection. Decoupled from <see cref="BookFontFamily"/> for the same
        /// reason as the chrome one (#67): swapping the ItemsSource pushes a null selection, and a load
        /// showing the default for a temporarily-uninstalled font must not erase the saved choice.
        /// </summary>
        public string SelectedBookFontFamily
        {
            get => _selectedBookFontFamily;
            set
            {
                if (_applyingLoadResult || value is null || value == _selectedBookFontFamily) return;
                this.RaiseAndSetIfChanged(ref _selectedBookFontFamily, value);
                BookFontFamily = value;
            }
        }

        /// <summary>
        /// The same installed faces the chrome picker offers, but headed by "Default" rather than "System
        /// Default" — for books, not choosing means the face the APP ships for this script, which is a
        /// specific Pāli font stack rather than whatever the OS would pick. (#42)
        /// </summary>
        public ObservableCollection<string> AvailableBookFonts
        {
            get => _availableBookFonts;
            set => this.RaiseAndSetIfChanged(ref _availableBookFonts, value);
        }

        /// <summary>
        /// Explains where book text size comes from, and shows this script's current zoom. (#42/#572)
        ///
        /// There is deliberately no size control here: #574 gave every script the same stylesheet ladder,
        /// so zoom is the only per-script size, and it is adjusted against live text rather than typed into
        /// a dialog. Without this line nothing in Settings would reveal that zoom is per script, and the
        /// panel would read as though the size control had been forgotten.
        /// </summary>
        public string BookSizeDescription
        {
            get
            {
                var zoomService = App.ServiceProvider?.GetService(typeof(IBookZoomService)) as IBookZoomService;
                if (zoomService == null || !ScriptKeys.TryParse(ScriptName, out var script))
                    return "Text size is set by zoom in the book window, per script.";

                var mod = OperatingSystem.IsMacOS() ? "\u2318" : "Ctrl";
                return $"Text size is set by zoom, currently {zoomService.FormatZoom(script)} for {ScriptName}. " +
                       $"Use {mod}+ and {mod}- in a book to change it, or {mod}0 to return to 100%.";
            }
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

                // "System Default" is always present and is not a font, so the real question is whether
                // anything else made it through the coverage filter. (#29)
                NoFontSupportsScript = fonts.All(f => f == "System Default");

                // The book list is the same faces under a different first entry: "Default" here means the
                // app's shipped stack for this script, not the OS default.
                var bookFonts = new List<string> { BookFontDefaultLabel };
                bookFonts.AddRange(fonts.Where(f => f != "System Default"));

                // A SAVED FACE THAT IS NOT INSTALLED JOINS THE LIST. Without this the picker fell back to
                // showing "Default" while the renderer went on using the missing name, so the two disagreed
                // about what was configured - and the obvious correction, choosing "Default", did nothing at
                // all: the ComboBox raises no change for the entry already displayed, and the setter's
                // equality guard would have swallowed it regardless. The only way out was the undiscoverable
                // pick-another-font-then-Default. Listing the saved face makes the picker tell the truth and
                // makes clearing it a real selection change. #67's rule is untouched - the stored value is
                // still never erased by a load, so reinstalling the font restores it as an ordinary match.
                // Saying that it is missing is #573's job, not the list's. (#614)
                if (AppearanceSettingsViewModel.SavedFaceMissingFrom(bookFonts, _bookFontFamily) is { } missing)
                    bookFonts.Insert(1, missing);

                AvailableBookFonts = new ObservableCollection<string>(bookFonts);

                // Resolve against the list rather than assigning the saved string: a SelectedItem that is
                // not an item of the ItemsSource simply renders blank.
                _selectedBookFontFamily =
                    AppearanceSettingsViewModel.ResolveBookFontSelection(bookFonts, _bookFontFamily);

                _selectedFontFamily = selected;
                this.RaisePropertyChanged(nameof(SelectedFontFamily));   // force re-select after the ItemsSource swap
                this.RaisePropertyChanged(nameof(SelectedBookFontFamily));
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

        private bool _noFontSupportsScript;

        /// <summary>
        /// True when NOT ONE installed font can render this script. (#29)
        ///
        /// <para>Worth saying out loud in the window rather than only in the log. The picker still offers
        /// "System Default" - that entry is the app's "do not override" token, not a font - so without this the
        /// user sees a dropdown with a single unremarkable entry and no reason to suspect their system is
        /// missing something. The old behaviour was worse still: every installed font was listed, so they would
        /// pick one, get tofu, and reasonably conclude the application was broken.</para>
        /// </summary>
        public bool NoFontSupportsScript
        {
            get => _noFontSupportsScript;
            private set => this.RaiseAndSetIfChanged(ref _noFontSupportsScript, value);
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
        private readonly IApplicationStateService? _stateService;
        private readonly Services.RecentBooksService? _recentBooks;
        private bool _useHardwareAcceleration;
        private int _maxRecentBooks;

        public ConfigurationSettingsViewModel(ISettingsService settingsService)
        {
            _logger = Log.ForContext<ConfigurationSettingsViewModel>();
            _settingsService = settingsService;
            _useHardwareAcceleration = settingsService.Settings.UseHardwareAcceleration;
            SettingsFilePath = settingsService.GetSettingsFilePath();
            OpenSettingsFileCommand = ReactiveCommand.Create(OpenSettingsFile);

            // Recent-books (MRU) size + clear (#44). MaxRecentBooks lives in ApplicationState.Preferences, not
            // the settings file, so resolve those services from the container (this VM is constructed directly).
            _stateService = App.ServiceProvider?.GetService<IApplicationStateService>();
            _recentBooks = App.ServiceProvider?.GetService<Services.RecentBooksService>();
            _maxRecentBooks = _stateService?.Current.Preferences.MaxRecentBooks ?? 10;
            ClearRecentBooksCommand = ReactiveCommand.Create(() => _recentBooks?.Clear());
        }

        /// <summary>How many recently-opened books the File → Open Recent menu remembers (#44). 0 disables the
        /// list. Persists to ApplicationState.Preferences and trims the current list immediately.</summary>
        public int MaxRecentBooks
        {
            get => _maxRecentBooks;
            set
            {
                var clamped = Math.Max(0, value);
                if (clamped == _maxRecentBooks)
                    return;   // no spurious dirty-mark on an unchanged value (Fable NIT-8)
                this.RaiseAndSetIfChanged(ref _maxRecentBooks, clamped);
                if (_stateService != null)
                {
                    _stateService.Current.Preferences.MaxRecentBooks = clamped;
                    _stateService.MarkDirty();
                    // The new cap is enforced as books are opened (and by the state validator on load). We do
                    // NOT trim the stored list here: nudging the number down then back up while experimenting
                    // must not irreversibly delete history. (Fable MEDIUM-1)
                }
            }
        }

        public ReactiveCommand<Unit, Unit> ClearRecentBooksCommand { get; }

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
    /// <summary>
/// A provider the build knows how to talk to, with the name a user would recognise. (#585)
///
/// <para>
/// Two, not a long list, because the second one is not really "OpenAI" — it is a SHAPE. The same adapter
/// reaches DeepSeek, OpenRouter, Ollama and LM Studio, and what selects between them is the base URL, not
/// this box. A list of brand names would go stale within a month and would imply the others are unsupported.
/// </para>
/// </summary>
public sealed record AiProviderChoice(Services.Ai.ChatProviderKind Kind, string Display, string Stored);

public class AiSettingsViewModel : ViewModelBase
    {
        private readonly ISettingsService _settingsService;
        private bool _aiEnabled;
        private bool _localApiEnabled;
        private bool _mcpEnabled;
        private bool _allowRemoteControl;

        public AiSettingsViewModel(ISettingsService settingsService)
            : this(settingsService, null, null)
        {
        }

        public AiSettingsViewModel(
            ISettingsService settingsService,
            Services.Ai.IAiCredentialStore? credentials,
            Services.Ai.IChatProviderResolver? providerResolver)
        {
            _settingsService = settingsService;
            _credentials = credentials;
            _providerResolver = providerResolver;

            var ai = _settingsService.Settings.Ai;
            _aiEnabled = ai.Enabled;
            _localApiEnabled = ai.LocalApi.Enabled;
            _mcpEnabled = ai.LocalApi.EnableMcpServer;
            _allowRemoteControl = ai.LocalApi.AllowRemoteControl;

            var chat = ai.Chat;
            _chatEnabled = chat.Enabled;
            // The existing single-provider fields now edit the ACTIVE connection (#689). Kept working, and
            // deliberately not deleted, so the app stays configurable until the connections UI (#691) lands.
            var active = ActiveConnection();
            _providerChoice = Providers.FirstOrDefault(
                c => Services.Ai.ChatProviderResolver.TryParseKind(active.Kind, out var k)
                     && k == c.Kind) ?? Providers[0];
            _baseUrl = active.BaseUrl;
            _model = _settingsService.Settings.Ai.Chat.ActiveModelId ?? "";
            _answerLanguage = string.IsNullOrWhiteSpace(chat.AnswerLanguage) ? "English" : chat.AnswerLanguage;

            SaveApiKeyCommand = ReactiveCommand.Create(SaveApiKey);
            RemoveApiKeyCommand = ReactiveCommand.Create(RemoveApiKey);

            RefreshKeyStatus();
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
                this.RaisePropertyChanged(nameof(AssistantFieldsEnabled));
                // The assistant hangs off the master switch too, so turning AI off takes its panel with it.
                ApplyAssistantVisibility();
                // Readiness is derived from the master too. Without this the line kept reporting "AI features
                // are turned off." after the user had just turned them on, until some other field happened to
                // be edited -- the one line on the screen whose entire job is not to be out of date.
                RefreshReadiness();
                // Apply live - no restart (#529). The server's surfaces are fixed at construction, so a change
                // here rebuilds the host; discovery is the handshake file, not a fixed port, so clients pick up
                // the new one on their next spawn (#278). Fire-and-forget with a logged failure: a checkbox must
                // not block the UI thread on Kestrel binding a port.
                _ = ApplyAiServerStateAsync();

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
                // Apply live - no restart (#529). The server's surfaces are fixed at construction, so a change
                // here rebuilds the host; discovery is the handshake file, not a fixed port, so clients pick up
                // the new one on their next spawn (#278). Fire-and-forget with a logged failure: a checkbox must
                // not block the UI thread on Kestrel binding a port.
                _ = ApplyAiServerStateAsync();

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
                // Apply live - no restart (#529). The server's surfaces are fixed at construction, so a change
                // here rebuilds the host; discovery is the handshake file, not a fixed port, so clients pick up
                // the new one on their next spawn (#278). Fire-and-forget with a logged failure: a checkbox must
                // not block the UI thread on Kestrel binding a port.
                _ = ApplyAiServerStateAsync();

            }
        }

        /// <summary>
        /// Applies the AI server settings live, logging rather than throwing. (#529)
        ///
        /// <para>Deliberately fire-and-forget from the setters: starting Kestrel and binding a loopback port
        /// takes long enough to be visible, and a checkbox that freezes while it happens is a worse experience
        /// than one that takes a moment to take effect. Failures are recorded on the lifecycle
        /// (<c>App.LocalApiStartFailed</c>) for the Settings indicator, so a silent swallow here still surfaces.</para>
        /// </summary>
        private static async Task ApplyAiServerStateAsync()
        {
            try
            {
                await App.ApplyAiSettingsAsync();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to apply AI server settings live (#529)");
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

        #region The assistant — surface B (#585)

        private readonly Services.Ai.IAiCredentialStore? _credentials;
        private readonly Services.Ai.IChatProviderResolver? _providerResolver;

        private bool _chatEnabled;
        private AiProviderChoice _providerChoice = null!;
        private string _baseUrl = "";
        private string _model = "";
        private string _answerLanguage = "English";
        private string _apiKeyEntry = "";
        private string _keyStatus = "";

        // Order is the dropdown's order, and the first entry is also the fallback when a stored value cannot
        // be parsed — so it must agree with ChatSettings.Provider's default, or an unreadable setting would
        // resolve to a different provider than a fresh install does.
        private static readonly AiProviderChoice[] Providers =
        {
            new(Services.Ai.ChatProviderKind.OpenAiCompatible, "OpenAI-compatible endpoint", "openai-compatible"),
            new(Services.Ai.ChatProviderKind.Anthropic, "Claude (Anthropic)", "anthropic"),
        };

        /// <summary>
        /// Answer language suggestions. Editable rather than a closed list: the model decides what it can
        /// write, not this app, and a reader whose language is missing would otherwise be told the feature is
        /// not for them.
        /// </summary>
        private static readonly string[] AnswerLanguages =
        {
            "English", "Italian", "German", "French", "Spanish", "Portuguese",
            "Hindi", "Burmese", "Sinhala", "Thai", "Vietnamese", "Chinese", "Japanese", "Russian",
        };

        /// <summary>Bindable views of the two lists above — an instance binding cannot reach a static member.</summary>
        public AiProviderChoice[] ProviderChoices => Providers;
        public string[] AnswerLanguageSuggestions => AnswerLanguages;

        /// <summary>Turns the in-app assistant on. Effective only under the AI master switch, like every other surface.</summary>
        public bool ChatEnabled
        {
            get => _chatEnabled;
            set
            {
                this.RaiseAndSetIfChanged(ref _chatEnabled, value);
                _settingsService.Settings.Ai.Chat.Enabled = value;
                _settingsService.RequestSave();
                this.RaisePropertyChanged(nameof(AssistantFieldsEnabled));
                RefreshReadiness();
                // Ticking the box IS the gesture: the panel appears now rather than at the next launch, and
                // unticking takes it away rather than leaving four buttons that decline every request. (#667)
                ApplyAssistantVisibility();
            }
        }

        public AiProviderChoice SelectedProvider
        {
            get => _providerChoice;
            set
            {
                this.RaiseAndSetIfChanged(ref _providerChoice, value);
                ActiveConnection().Kind = value?.Stored ?? "anthropic";
                _settingsService.RequestSave();
                this.RaisePropertyChanged(nameof(IsOpenAiCompatible));
                this.RaisePropertyChanged(nameof(BaseUrlDescription));
                this.RaisePropertyChanged(nameof(ApiKeyDescription));
                RefreshKeyStatus();
                RefreshReadiness();
            }
        }

        /// <summary>
        /// The connection the single-provider fields edit, creating it on first use. (#689)
        ///
        /// <para>Surface B shipped with one provider, one base URL and one model; the model is now plural.
        /// Rather than delete the working UI before its replacement (#691) exists, these fields edit the
        /// <i>active</i> connection — so the app remains configurable, and whatever a reader sets here is a
        /// real connection record that the new UI will show rather than state that has to be migrated.</para>
        /// </summary>
        private CST.Avalonia.Models.AiConnectionRecord ActiveConnection()
        {
            var chat = _settingsService.Settings.Ai.Chat;

            var existing = chat.Connections.FirstOrDefault(
                c => string.Equals(c.Id, chat.ActiveConnectionId, System.StringComparison.Ordinal));
            if (existing is not null) return existing;

            existing = chat.Connections.FirstOrDefault();
            if (existing is not null)
            {
                chat.ActiveConnectionId = existing.Id;
                return existing;
            }

            var created = new CST.Avalonia.Models.AiConnectionRecord
            {
                Id = "default",
                DisplayName = "My provider",
                Kind = "openai-compatible",
                BaseUrl = "",
            };
            chat.Connections.Add(created);
            chat.ActiveConnectionId = created.Id;
            return created;
        }

        /// <summary>Sets the active model, keeping the connection's own list in step so the model a reader
        /// typed here appears in the per-turn picker (#693) rather than only in this box.</summary>
        private void SetActiveModel(string? value)
        {
            var chat = _settingsService.Settings.Ai.Chat;
            var id = string.IsNullOrWhiteSpace(value) ? null : value.Trim();
            chat.ActiveModelId = id;

            if (id is null) return;

            var connection = ActiveConnection();
            if (!connection.Models.Any(m => string.Equals(m.Id, id, System.StringComparison.Ordinal)))
                connection.Models.Add(new CST.Avalonia.Models.AiModelRecord { Id = id, DisplayName = id });
        }

        /// <summary>Whether the endpoint field is the load-bearing one — it is what selects the provider.</summary>
        public bool IsOpenAiCompatible =>
            SelectedProvider?.Kind == Services.Ai.ChatProviderKind.OpenAiCompatible;

        public string BaseUrlDescription => IsOpenAiCompatible
            ? "Required. The endpoint's base URL — this is what points the app at DeepSeek, OpenRouter, "
              + "Ollama, LM Studio or any other OpenAI-compatible server, e.g. http://localhost:11434/v1"
            : "Optional. Leave empty unless you reach Claude through a proxy or gateway.";

        public string BaseUrl
        {
            get => _baseUrl;
            set
            {
                this.RaiseAndSetIfChanged(ref _baseUrl, value);
                ActiveConnection().BaseUrl = value?.Trim() ?? "";
                _settingsService.RequestSave();
                RefreshReadiness();
            }
        }

        /// <summary>
        /// The model id, verbatim. Never validated against a list and never a dropdown: the OpenAI-compatible
        /// shape serves arbitrary endpoints, and any list shipped here would be wrong within a month — it
        /// would reject a model that works and imply the app had been abandoned.
        /// </summary>
        public string Model
        {
            get => _model;
            set
            {
                this.RaiseAndSetIfChanged(ref _model, value);
                SetActiveModel(value);
                _settingsService.RequestSave();
                RefreshReadiness();
            }
        }

        /// <summary>
        /// The language the answer is written in — a different axis from the script quoted Pāli appears in.
        /// The two were previously conflated; see <see cref="PaliScriptNote"/>.
        /// </summary>
        public string AnswerLanguage
        {
            get => _answerLanguage;
            set
            {
                this.RaiseAndSetIfChanged(ref _answerLanguage, value);
                _settingsService.Settings.Ai.Chat.AnswerLanguage =
                    string.IsNullOrWhiteSpace(value) ? "English" : value.Trim();
                _settingsService.RequestSave();
            }
        }

        /// <summary>
        /// The second axis, stated rather than offered. The system prompt already asks the model to mark
        /// quoted Pāli, but this version renders those quotes in Latin and converts nothing — so a script
        /// picker here would be a control that does nothing, which is worse than a sentence that is true.
        /// </summary>
        public string PaliScriptNote =>
            "Pāli quoted in answers is shown in Latin script in this version, whatever script you read books "
            + "in. The answer language above is a separate setting and takes effect now.";

        /// <summary>
        /// What the user is typing into the key box. Deliberately NOT persisted anywhere — it is handed to the
        /// OS credential store on Save and cleared. Bound to a masked box.
        /// </summary>
        public string ApiKeyEntry
        {
            get => _apiKeyEntry;
            set => this.RaiseAndSetIfChanged(ref _apiKeyEntry, value);
        }

        /// <summary>Whether a key is stored for the selected provider, or why one cannot be. Never the key.</summary>
        public string KeyStatus
        {
            get => _keyStatus;
            private set => this.RaiseAndSetIfChanged(ref _keyStatus, value);
        }

        /// <summary>
        /// Whether the assistant's own fields are editable: the master switch AND the assistant's switch.
        /// Keying them to the master alone left provider, endpoint, model and language fully editable with
        /// "Enable the assistant" unticked, and readiness still reporting on a feature that was off.
        /// </summary>
        public bool AssistantFieldsEnabled => AiEnabled && ChatEnabled;

        public bool CanStoreKeys => _credentials?.IsAvailable == true && AssistantFieldsEnabled;

        public string ApiKeyDescription => IsOpenAiCompatible
            ? "Optional. A local runner on your own machine usually needs none; a hosted endpoint will."
            : "Required for Claude.";

        public ReactiveCommand<Unit, Unit> SaveApiKeyCommand { get; }
        public ReactiveCommand<Unit, Unit> RemoveApiKeyCommand { get; }

        /// <summary>
        /// What can honestly be said about a model's Pāli, which is not a rating. (#670)
        ///
        /// <para>This replaced a curated per-model fidelity tier. Pāli ability is emergent from pre-training —
        /// there is no Pāli-specific training — so it is not predicted by published benchmarks, is not
        /// monotonic with general capability or size, and can move between point releases of one model. A tier
        /// could not be kept true by sampling; it would have to be re-measured for every release of every
        /// model. So the app states the general fact, which is permanently true, instead of a verdict that
        /// would be stale on arrival.</para>
        /// </summary>
        public string PaliAbilityNote =>
            "How well a model reads Pāli varies widely and is not predicted by its general benchmarks or its "
            + "size — it is an ability that emerges from pre-training rather than one anybody trains for. This "
            + "app cannot certify it for you. Check answers against the text in front of you, and treat a "
            + "fluent translation as a claim to verify rather than a result.";

        /// <summary>
        /// Whether the assistant would actually run, asked of the SAME resolver the assistant uses. A second
        /// implementation of "is this configured" would drift from the first, and the version that lies is
        /// always the one in Settings.
        /// </summary>
        public string ReadinessText
        {
            get
            {
                if (_providerResolver == null) return "";
                var resolution = _providerResolver.Resolve(out var problem);
                return resolution != null ? "Ready." : problem ?? "Not configured.";
            }
        }

        public bool IsReady => _providerResolver?.Resolve(out _) != null;

        /// <summary>
        /// What leaves the machine, in plain language (AI_SURFACE_B.md §10). Stated here rather than buried in
        /// documentation because this is the screen where the user decides.
        /// </summary>
        public string PrivacyNote =>
            "When you ask the assistant something, these are sent to the provider configured above: the "
            + "passage text from the book you are reading, your question, and the app's instructions to the "
            + "model. Nothing else is sent, and nothing is sent until you ask. If you point this at a model "
            + "running on your own machine, nothing leaves it at all.";

        private void SaveApiKey()
        {
            if (_credentials == null || string.IsNullOrWhiteSpace(ApiKeyEntry)) return;

            _credentials.SetApiKey(SelectedProvider.Kind, ApiKeyEntry);
            // Cleared immediately: the box exists to hand the key over, not to hold it.
            ApiKeyEntry = "";
            RefreshKeyStatus();
            RefreshReadiness();
        }

        private void RemoveApiKey()
        {
            _credentials?.DeleteApiKey(SelectedProvider.Kind);
            ApiKeyEntry = "";
            RefreshKeyStatus();
            RefreshReadiness();
        }

        private void RefreshKeyStatus()
        {
            if (_credentials == null)
            {
                KeyStatus = "";
            }
            else if (!_credentials.IsAvailable)
            {
                // The honest message from the store itself, which knows WHY — a Windows build without DPAPI
                // and a Linux build without libsecret are different sentences, and "add a key in Settings"
                // is the wrong advice for both.
                KeyStatus = _credentials.Unavailable ?? "Keys cannot be stored on this system.";
            }
            else
            {
                KeyStatus = _credentials.GetApiKey(SelectedProvider.Kind) is null
                    ? "No key stored for this provider."
                    : "A key is stored for this provider.";
            }

            this.RaisePropertyChanged(nameof(CanStoreKeys));
        }

        /// <summary>
        /// Show or hide the assistant panel to match the two switches. Settings is a separate window, so the
        /// panel has no way to learn about a change on its own.
        /// </summary>
        private void ApplyAssistantVisibility()
        {
            try
            {
                if ((App.MainWindow?.DataContext as LayoutViewModel) is not { } layout) return;

                var wanted = _settingsService.Settings.Ai.Enabled && _settingsService.Settings.Ai.Chat.Enabled;
                if (wanted) layout.ShowAssistantPanel();
                else layout.HideAssistantPanel();
            }
            catch (Exception ex)
            {
                Serilog.Log.Debug(ex, "Could not apply the assistant panel's visibility");
            }
        }

        private void RefreshReadiness()
        {
            this.RaisePropertyChanged(nameof(ReadinessText));
            this.RaisePropertyChanged(nameof(IsReady));
        }

        #endregion
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
