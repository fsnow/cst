using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Platform.Storage;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Conversion;
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

            // Commands
            SaveCommand = ReactiveCommand.CreateFromTask(SaveSettingsAsync);
            CancelCommand = ReactiveCommand.Create(Close);

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

        public ReactiveCommand<Unit, Unit> SaveCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

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

        private async Task SaveSettingsAsync()
        {
            try
            {
                await _settingsService.SaveSettingsAsync();
                HasUnsavedChanges = false;
                _logger.Information("Settings saved successfully");
                Close();
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to save settings");
            }
        }

        private void Close()
        {
            CloseWindow?.Invoke();
        }

        public void MarkAsChanged()
        {
            HasUnsavedChanges = true;
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
                    if (Parent is SettingsViewModel parent) parent.MarkAsChanged();
                });
                
            this.WhenAnyValue(x => x.IndexDirectory)
                .Skip(1)
                .Subscribe(value => 
                {
                    _settingsService.UpdateSetting(nameof(Settings.IndexDirectory), value);
                    if (Parent is SettingsViewModel parent) parent.MarkAsChanged();
                });

            this.WhenAnyValue(x => x.ShowWelcomeOnStartup)
                .Skip(1)
                .Subscribe(value => 
                {
                    _settingsService.UpdateSetting(nameof(Settings.ShowWelcomeOnStartup), value);
                    if (Parent is SettingsViewModel parent) parent.MarkAsChanged();
                });

            this.WhenAnyValue(x => x.XmlBooksDirectory)
                .Skip(1)
                .Subscribe(value => 
                {
                    _settingsService.UpdateSetting(nameof(Settings.XmlBooksDirectory), value);
                    if (Parent is SettingsViewModel parent) parent.MarkAsChanged();
                });

            this.WhenAnyValue(x => x.MaxRecentBooks)
                .Skip(1)
                .Subscribe(value => 
                {
                    _settingsService.UpdateSetting(nameof(Settings.MaxRecentBooks), value);
                    if (Parent is SettingsViewModel parent) parent.MarkAsChanged();
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
        private string _theme;

        public AppearanceSettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            _theme = _settingsService.Settings.Theme;

            Themes = new[] { "Light", "Dark", "Auto" };
        }

        public string Theme
        {
            get => _theme;
            set
            {
                this.RaiseAndSetIfChanged(ref _theme, value);
                _settingsService.UpdateSetting(nameof(Settings.Theme), value);
            }
        }

        public string[] Themes { get; }
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

        public AdvancedSettingsViewModel(ISettingsService settingsService)
        {
            _settingsService = settingsService;
            _logger = Log.ForContext<AdvancedSettingsViewModel>();
            
            SettingsFilePath = _settingsService.GetSettingsFilePath();
            OpenSettingsFileCommand = ReactiveCommand.Create(OpenSettingsFile);
        }

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

            // Update service when log level changes
            this.WhenAnyValue(x => x.LogLevel)
                .Skip(1)
                .Subscribe(value => 
                {
                    _settingsService.Settings.DeveloperSettings.LogLevel = value;
                    if (Parent is SettingsViewModel parent) parent.MarkAsChanged();
                    
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