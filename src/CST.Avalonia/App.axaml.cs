using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CST.Avalonia.ViewModels;
using CST.Avalonia.Views;
using CST.Avalonia.Services;
using CST.Avalonia.Models;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using CST;
using CST.Conversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using ReactiveUI;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
#if MACOS
using CST.Avalonia.Services.Platform.Mac;
#endif

namespace CST.Avalonia;

public partial class App : Application
{
    public static ServiceProvider? ServiceProvider { get; private set; }
    public static Window? MainWindow { get; private set; }
    private bool _hasRestoredInitialBooks = false;

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        
        // Configure ReactiveUI to use the UI thread scheduler to prevent threading issues
        RxApp.MainThreadScheduler = new AvaloniaUIThreadScheduler();
        
        // Wire up native menu events on macOS
        if (OperatingSystem.IsMacOS())
        {
            SetupNativeMenuEvents();
        }
        
        // Set up global exception handling for unhandled exceptions
        RxApp.DefaultExceptionHandler = new ReactiveExceptionHandler();
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // Note: Splash screen has been disabled on macOS due to threading issues
        // The splash screen must be shown after Avalonia is initialized, which defeats its purpose
        bool showSplash = !OperatingSystem.IsMacOS();
        
        // Check the "Show Welcome screen on startup" setting if platform supports splash screen
        if (showSplash)
        {
            showSplash = ShouldShowSplashScreen();
        }
        
        if (showSplash)
        {
            // Show splash screen as early as possible in the Avalonia lifecycle
            SplashScreen.ShowSplashScreen();
            SplashScreen.SetStatus("Starting CST Avalonia...");
            SplashScreen.SetReferencePoint();
        }

        // WebView initialization is handled automatically by OutSystems WebView package

        if (showSplash)
        {
            SplashScreen.SetStatus("Configuring services...");
            SplashScreen.SetReferencePoint();
        }

        // Configure services
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            
            if (showSplash)
            {
                SplashScreen.SetStatus("Loading settings...");
                SplashScreen.SetReferencePoint();
            }

            // Load settings before application state
            _ = LoadSettingsAsync();
            
            if (showSplash)
            {
                SplashScreen.SetStatus("Loading application state...");
                SplashScreen.SetReferencePoint();
            }
            
            // Load application state before creating UI
            _ = LoadApplicationStateAsync();
            
            // Initialize script service from loaded state (will be called after state loads)
            // State service will notify ScriptService via event when state is ready
            
            if (showSplash)
            {
                SplashScreen.SetStatus("Pre-loading fonts...");
                SplashScreen.SetReferencePoint();
            }
            
            // Pre-load fonts for all scripts to avoid UI delays in settings
            _ = Task.Run(async () =>
            {
                // Give settings time to load
                await Task.Delay(500);
                await InitializeFontsAsync();
            });
            
            if (showSplash)
            {
                SplashScreen.SetStatus("Checking search index...");
                SplashScreen.SetReferencePoint();
            }
            
            // Initialize indexing service and build index if needed (with delay to ensure settings are loaded)
            _ = Task.Run(async () =>
            {
                // Give settings time to load
                await Task.Delay(1000);
                
                // Check for XML updates first to ensure we have latest files
                await CheckForXmlUpdatesAsync();
                
                // Then initialize indexing with the updated files
                await InitializeIndexingAsync(showSplash);
            });
            
            if (showSplash)
            {
                SplashScreen.SetStatus("Loading books...");
                SplashScreen.SetReferencePoint();
            }
            
            // Get and initialize the OpenBookDialogViewModel BEFORE creating the layout
            var openBookViewModel = ServiceProvider.GetRequiredService<OpenBookDialogViewModel>();
            
            if (showSplash)
            {
                SplashScreen.SetStatus("Creating main window...");
                SplashScreen.SetReferencePoint();
            }
            
            // Create the main window with dockable layout (this will now get the initialized ViewModel)
            var layoutViewModel = new LayoutViewModel();
            MainWindow = new SimpleTabbedWindow
            {
                DataContext = layoutViewModel
            };
            
            desktop.MainWindow = MainWindow;
            
            if (showSplash)
            {
                SplashScreen.SetStatus("Ready");
                SplashScreen.SetReferencePoint();
                
                // Close splash screen after initialization is complete
                SplashScreen.CloseForm();
            }


            // Handle book open requests - now they open as documents in the dockable layout
            openBookViewModel.BookOpenRequested += book =>
            {
                Log.Information("BookOpenRequested event received for: {BookFile} - {NavPath}", book.FileName, book.LongNavPath);
                
                // Open book via LayoutViewModel
                Log.Debug("Opening book via LayoutViewModel: {BookFile}", book.FileName);
                layoutViewModel.OpenBook(book);
                Log.Debug("LayoutViewModel.OpenBook completed for: {BookFile}", book.FileName);
            };

            openBookViewModel.CloseRequested += () =>
            {
                // For now, just close the application when Open Book is closed
                // Later this could hide the dialog and show a menu instead
                _ = SaveApplicationStateAsync();
                desktop.Shutdown();
            };

            // Handle application shutdown to save state
            desktop.ShutdownRequested += async (sender, args) =>
            {
                Log.Information("SHUTDOWN: ShutdownRequested event triggered");
                
                // Cancel shutdown temporarily to allow async save to complete
                args.Cancel = true;
                
                try
                {
                    await SaveApplicationStateAsync();
                    
                    // Dispose ServiceProvider to trigger disposal of all singleton services
                    ServiceProvider?.Dispose();
                    
                    Log.Information("SHUTDOWN: All cleanup completed, proceeding with shutdown");
                    
                    // Now allow shutdown to proceed
                    desktop.Shutdown(0);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "SHUTDOWN: Error during shutdown sequence");
                    // Force shutdown even if save failed
                    desktop.Shutdown(1);
                }
            };
        }

        base.OnFrameworkInitializationCompleted();
    }

    /// <summary>
    /// Check if splash screen should be shown based on user settings
    /// This reads only the specific setting we need without loading the full settings system
    /// </summary>
    private bool ShouldShowSplashScreen()
    {
        try
        {
            var settingsPath = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "CST.Avalonia", "settings.json");
            
            if (!File.Exists(settingsPath))
            {
                // If settings file doesn't exist, default to showing splash screen
                return true;
            }

            var settingsJson = File.ReadAllText(settingsPath);
            
            // Parse just the ShowWelcomeOnStartup setting
            if (settingsJson.Contains("\"ShowWelcomeOnStartup\""))
            {
                // Simple JSON parsing to avoid loading full settings system
                var startIndex = settingsJson.IndexOf("\"ShowWelcomeOnStartup\":");
                if (startIndex >= 0)
                {
                    var valueStart = settingsJson.IndexOf(':', startIndex) + 1;
                    var valueEnd = settingsJson.IndexOfAny(new[] { ',', '}' }, valueStart);
                    if (valueEnd > valueStart)
                    {
                        var valueStr = settingsJson.Substring(valueStart, valueEnd - valueStart).Trim();
                        if (bool.TryParse(valueStr, out bool showSplash))
                        {
                            return showSplash;
                        }
                    }
                }
            }
            
            // Default to showing splash screen if parsing fails
            return true;
        }
        catch (Exception ex)
        {
            // Log.Warning not available yet since Serilog isn't configured, but that's OK
            // This happens very early in startup, before DI is configured
            // Default to showing splash screen on error
            return true;
        }
    }

    /// <summary>
    /// Load settings on startup
    /// </summary>
    private async Task LoadSettingsAsync()
    {
        try
        {
            var settingsService = ServiceProvider?.GetRequiredService<ISettingsService>();
            if (settingsService != null)
            {
                await settingsService.LoadSettingsAsync();
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load settings");
        }
    }

    /// <summary>
    /// Pre-load fonts for all scripts to avoid UI delays in settings
    /// </summary>
    private async Task InitializeFontsAsync()
    {
        try
        {
            Log.Information("InitializeFontsAsync() started");
            
            var fontService = ServiceProvider?.GetRequiredService<IFontService>();
            if (fontService != null)
            {
                Log.Information("Pre-loading fonts for all scripts...");
                await fontService.PreloadFontsForAllScriptsAsync();
                Log.Information("Font pre-loading completed successfully");
            }
            else
            {
                Log.Error("Could not get FontService from DI container");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error pre-loading fonts");
        }
    }

    /// <summary>
    /// Initialize the indexing service and build index if needed
    /// </summary>
    private async Task InitializeIndexingAsync(bool showProgress)
    {
        try
        {
            Log.Information("InitializeIndexingAsync() started");
            
            // Debug: Check if settings are available
            var settingsService = ServiceProvider?.GetRequiredService<ISettingsService>();
            if (settingsService != null)
            {
                Log.Information("Settings check - XmlBooksDirectory: '{XmlDirectory}'", settingsService.Settings.XmlBooksDirectory);
                Log.Information("Settings check - IndexDirectory: '{IndexDirectory}'", settingsService.Settings.IndexDirectory);
            }
            else
            {
                Log.Error("Could not get SettingsService from DI container");
            }
            
            var indexingService = ServiceProvider?.GetRequiredService<IIndexingService>();
            if (indexingService != null)
            {
                Log.Information("IndexingService obtained from DI container");
                
                Log.Information("Calling indexingService.InitializeAsync()...");
                await indexingService.InitializeAsync();
                Log.Information("indexingService.InitializeAsync() completed");
                
                // Always call BuildIndexAsync - it will handle both initial indexing and incremental updates
                Log.Information("Calling BuildIndexAsync to check for updates or build if needed...");
                
                if (showProgress)
                {
                    SplashScreen.SetStatus("Checking for index updates...");
                }
                
                // Create progress reporter for splash screen
                var progress = new Progress<CST.Lucene.IndexingProgress>(p =>
                {
                    if (showProgress && p != null)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (p.TotalBooks > 0)
                            {
                                SplashScreen.SetStatus($"Indexing book {p.CurrentBook} of {p.TotalBooks}...");
                                // Update progress bar (0-100)
                                var progressValue = p.ProgressPercentage;
                                if (progressValue > 0)
                                {
                                    // Set target progress for smooth animation
                                    SplashScreen.SetReferencePoint();
                                }
                            }
                            else
                            {
                                SplashScreen.SetStatus(p.StatusMessage);
                            }
                        });
                    }
                });
                
                Log.Information("Calling indexingService.BuildIndexAsync()...");
                await indexingService.BuildIndexAsync(progress);
                Log.Information("indexingService.BuildIndexAsync() completed");
                
                Log.Information("Indexing service initialized successfully");
            }
            else
            {
                Log.Error("Could not get IndexingService from DI container");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to initialize indexing: {ErrorMessage}", ex.Message);
            // Don't fail the app startup if indexing fails
        }
    }
    
    /// <summary>
    /// Check for XML data updates from GitHub repository
    /// </summary>
    private async Task CheckForXmlUpdatesAsync()
    {
        try
        {
            Log.Information("CheckForXmlUpdatesAsync() started");
            
            var xmlUpdateService = ServiceProvider?.GetRequiredService<IXmlUpdateService>();
            if (xmlUpdateService == null)
            {
                Log.Warning("XmlUpdateService not available in DI container");
                return;
            }
            
            // Subscribe to status updates for logging
            xmlUpdateService.UpdateStatusChanged += message =>
            {
                Log.Information("XML Update Status: {Message}", message);
            };
            
            // Check for updates (this will handle first-time download if needed)
            await xmlUpdateService.CheckForUpdatesAsync();
            
            Log.Information("CheckForXmlUpdatesAsync() completed");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error checking for XML updates");
            // Don't propagate the error - allow the app to continue even if update check fails
        }
    }

    /// <summary>
    /// Load application state on startup - equivalent to CST4 AppState.Deserialize()
    /// </summary>
    private async Task LoadApplicationStateAsync()
    {
        try
        {
            var stateService = ServiceProvider?.GetRequiredService<IApplicationStateService>();
            if (stateService != null)
            {
                // Subscribe to state changes for future modifications (not for initial load)
                stateService.StateChanged += OnApplicationStateChanged;
                
                await stateService.LoadStateAsync();
                
                // Manually handle initial state restoration without StateChanged events
                await InitializeFromLoadedState(stateService.Current);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load application state");
        }
    }
    
    private async Task InitializeFromLoadedState(ApplicationState state)
    {
        // Initialize script service with loaded script preference
        var scriptService = ServiceProvider?.GetRequiredService<IScriptService>();
        if (scriptService != null)
        {
            scriptService.InitializeFromState();
        }
        
        // Restore main window state (dimensions, position, etc.)
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && 
            desktop.MainWindow is SimpleTabbedWindow mainWindow)
        {
            mainWindow.RestoreWindowState();
        }
        
        // Restore book windows if any exist
        if (state.BookWindows.Any() && !_hasRestoredInitialBooks)
        {
            _hasRestoredInitialBooks = true;
            
            // Suppress StateChanged events during restoration to prevent loops
            var stateService = ServiceProvider?.GetRequiredService<IApplicationStateService>();
            if (stateService != null)
            {
                stateService.SetStateChangedEventsSuppression(true);
            }
            
            try
            {
                RestoreBookWindows(state.BookWindows);
            }
            finally
            {
                // Re-enable StateChanged events after restoration
                if (stateService != null)
                {
                    stateService.SetStateChangedEventsSuppression(false);
                }
            }
        }
    }

    private void OnApplicationStateChanged(ApplicationState state)
    {
        // This method is called whenever application state changes during normal operation
        // Book window restoration should only happen at startup, not during state changes
        // Removing restoration logic to prevent duplicate book opening bug
        
        // Future: Could add other state change handling here if needed
    }

    private void RestoreBookWindows(List<BookWindowState> bookWindows)
    {
        try
        {
            Log.Information("Restoring {BookCount} book windows from saved state", bookWindows.Count);
            
            // We need to delay restoration until the UI is fully loaded
            // Schedule the restoration on the UI thread after a delay
            Dispatcher.UIThread.Post(async () =>
            {
                var stateService = ServiceProvider?.GetRequiredService<IApplicationStateService>();
                
                try
                {
                    // Wait for UI to be fully initialized
                    await Task.Delay(500);
                    
                    // Suppress StateChanged events to prevent feedback loop
                    if (stateService != null)
                    {
                        stateService.SetStateChangedEventsSuppression(true);
                    }
                    
                    // Get the main window and its dock factory
                    if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && 
                        desktop.MainWindow is SimpleTabbedWindow mainWindow)
                    {
                        // Use the SimpleTabbedWindow's OpenBook method directly
                        // Create a copy to avoid collection modification issues
                        var bookWindowsCopy = bookWindows.ToList();
                        string? selectedBookWindowId = null;
                        
                        // First pass: Open all books and identify which one should be selected
                        foreach (var bookWindowState in bookWindowsCopy)
                        {
                            try
                            {
                                // Remember which book was selected
                                if (bookWindowState.IsSelected)
                                {
                                    selectedBookWindowId = bookWindowState.WindowId;
                                }
                                
                                // Get the book from Books.Inst by index
                                if (bookWindowState.BookIndex >= 0 && bookWindowState.BookIndex < Books.Inst.Count)
                                {
                                    var book = Books.Inst[bookWindowState.BookIndex];
                                    
                                    // Validate the book filename matches (for extra safety)
                                    if (book.FileName == bookWindowState.BookFileName)
                                    {
                                        Log.Information("Restoring book: {BookFile} with WindowId: {WindowId}", book.FileName, bookWindowState.WindowId);
                                        // Open the book through SimpleTabbedWindow with saved script and WindowId
                                        mainWindow.OpenBook(book, bookWindowState.SearchTerms, bookWindowState.BookScript, bookWindowState.WindowId);
                                        Log.Debug("Book restored: {BookFile} with script: {Script}", book.FileName, bookWindowState.BookScript);
                                    }
                                    else
                                    {
                                        Log.Warning("Book filename mismatch: expected {ExpectedFile}, got {ActualFile}", bookWindowState.BookFileName, book.FileName);
                                    }
                                }
                                else
                                {
                                    Log.Warning("Invalid book index: {BookIndex}", bookWindowState.BookIndex);
                                }
                            }
                            catch (Exception ex)
                            {
                                Log.Error(ex, "Failed to restore book {BookFile}", bookWindowState.BookFileName);
                            }
                        }
                        
                        // Second pass: Restore the selected tab if we found one
                        if (!string.IsNullOrEmpty(selectedBookWindowId))
                        {
                            // Add a small delay to ensure all tabs are fully loaded before setting selection
                            await Task.Delay(100);
                            RestoreSelectedBookTab(selectedBookWindowId);
                        }
                    }
                    else
                    {
                        Log.Warning("Main window not available for book restoration");
                    }
                }
                finally
                {
                    // Re-enable StateChanged events
                    if (stateService != null)
                    {
                        stateService.SetStateChangedEventsSuppression(false);
                    }
                }
            });
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restore book windows");
        }
    }
    
    private void RestoreSelectedBookTab(string selectedWindowId)
    {
        try
        {
            Log.Debug("Attempting to restore selection to book with WindowId: {WindowId}", selectedWindowId);
            
            // Get the layout to access the document dock
            if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop && 
                desktop.MainWindow is SimpleTabbedWindow mainWindow &&
                mainWindow.DataContext is LayoutViewModel layoutViewModel)
            {
                // Find the document dock by traversing the layout hierarchy
                var documentDock = FindDocumentDockInLayout(layoutViewModel.Layout);
                
                if (documentDock != null)
                {
                    Log.Debug("Found {DockableCount} visible dockables in document dock", documentDock.VisibleDockables?.Count ?? 0);
                    
                    // Log all document IDs for debugging
                    if (documentDock.VisibleDockables != null)
                    {
                        foreach (var doc in documentDock.VisibleDockables)
                        {
                            Log.Debug("  Document ID: {DocumentId}", doc.Id);
                        }
                    }
                    
                    // Find the document with the matching WindowId (exact match)
                    var targetDocument = documentDock.VisibleDockables?
                        .FirstOrDefault(d => d.Id == selectedWindowId);
                    
                    if (targetDocument != null)
                    {
                        documentDock.ActiveDockable = targetDocument;
                        Log.Information("Successfully restored selection to book with WindowId: {WindowId}", selectedWindowId);
                    }
                    else
                    {
                        Log.Warning("Could not find document with WindowId: {WindowId}", selectedWindowId);
                        // Fallback: select the last opened tab if no specific selection found
                        if (documentDock.VisibleDockables?.Count > 0)
                        {
                            documentDock.ActiveDockable = documentDock.VisibleDockables.Last();
                            Log.Debug("Fallback: Selected last opened tab");
                        }
                    }
                }
                else
                {
                    Log.Warning("Could not find document dock for tab selection restoration");
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restore selected tab");
        }
    }
    
    private IDock? FindDocumentDockInLayout(IDock? dock)
    {
        if (dock == null) return null;
        
        // Check if this dock is the MainDocumentDock or any DocumentDock
        if (dock.Id == "MainDocumentDock" || dock is DocumentDock)
        {
            return dock;
        }
        
        // Recursively search in visible dockables
        if (dock.VisibleDockables != null)
        {
            foreach (var child in dock.VisibleDockables)
            {
                if (child is IDock childDock)
                {
                    var result = FindDocumentDockInLayout(childDock);
                    if (result != null) return result;
                }
            }
        }
        
        return null;
    }

    /// <summary>
    /// Save application state on shutdown - equivalent to CST4 AppState.Serialize()
    /// </summary>
    private async Task SaveApplicationStateAsync()
    {
        try
        {
            Log.Information("SHUTDOWN: Starting final application state save");
            var stateService = ServiceProvider?.GetRequiredService<IApplicationStateService>();
            if (stateService != null)
            {
                // Force immediate save on shutdown
                var success = await stateService.ForceSaveAsync();
                Log.Information("SHUTDOWN: Final state save completed - Success: {Success}", success);
            }
            else
            {
                Log.Warning("SHUTDOWN: ApplicationStateService not available");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SHUTDOWN: Failed to save application state");
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Configure logging with priority: Environment Variable > Saved Setting > Default
        LogEventLevel logLevel;
        
        // First check environment variable (highest priority for debugging)
        var envLogLevel = Environment.GetEnvironmentVariable("CST_LOG_LEVEL")?.ToLowerInvariant();
        if (!string.IsNullOrEmpty(envLogLevel))
        {
            logLevel = envLogLevel switch
            {
                "debug" => LogEventLevel.Debug,
                "information" => LogEventLevel.Information,
                "warning" => LogEventLevel.Warning,
                "error" => LogEventLevel.Error,
                "fatal" => LogEventLevel.Fatal,
                _ => LogEventLevel.Information
            };
        }
        else
        {
            // Check saved setting (will load defaults if no saved setting exists)
            try
            {
                // Create a temporary settings service to load saved log level
                var tempSettingsService = new SettingsService();
                var savedLogLevel = tempSettingsService.Settings.DeveloperSettings.LogLevel;
                logLevel = savedLogLevel.ToLowerInvariant() switch
                {
                    "debug" => LogEventLevel.Debug,
                    "information" => LogEventLevel.Information,
                    "warning" => LogEventLevel.Warning,
                    "error" => LogEventLevel.Error,
                    "fatal" => LogEventLevel.Fatal,
                    _ => LogEventLevel.Information // Default fallback
                };
            }
            catch
            {
                // Fallback to default if settings loading fails
                logLevel = LogEventLevel.Information;
            }
        }
        
        // Get the logs directory in user's Application Support
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
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .WriteTo.File(logPath, 
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .Enrich.FromLogContext()
            .CreateLogger();

        services.AddLogging(builder => builder.AddSerilog());

#if MACOS
        // Conditionally register the Mac-specific font service
        if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
        {
            services.AddSingleton<MacFontService>();
            services.AddTransient<Services.Platform.Mac.TiroDevanagariTest>();
        }
#endif

        // Register services
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IScriptService, ScriptService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IFontService, FontService>();
        services.AddSingleton<IApplicationStateService, ApplicationStateService>();
        services.AddSingleton<ChapterListsService>();
        // services.AddSingleton<IBookService, BookService>();
        services.AddSingleton<ISearchService, SearchService>();
        services.AddTransient<TreeStateService>();
        
        // Indexing services
        services.AddSingleton<IXmlFileDatesService, XmlFileDatesService>();
        services.AddSingleton<IIndexingService, IndexingService>();
        
        // XML Update service
        services.AddSingleton<IXmlUpdateService, XmlUpdateService>();

        // Register ViewModels
        services.AddSingleton<OpenBookDialogViewModel>();
        services.AddSingleton<SearchViewModel>();
        // services.AddTransient<MainWindowViewModel>();
    }

    private void SetupNativeMenuEvents()
    {
        var menu = NativeMenu.GetMenu(this);
        if (menu != null && menu.Count() > 0)
        {
            // Find the Settings menu item
            foreach (var item in menu)
            {
                if (item is NativeMenuItem menuItem && menuItem.Header?.ToString() == "Settings...")
                {
                    menuItem.Click += async (s, e) =>
                    {
                        Log.Information("Settings menu clicked via native menu");
                        await ShowSettingsWindow();
                    };
                    break;
                }
            }
        }
    }
    
    private async Task ShowSettingsWindow()
    {
        try
        {
            var settingsService = ServiceProvider?.GetRequiredService<ISettingsService>();
            if (settingsService != null && MainWindow != null)
            {
                var settingsViewModel = new SettingsViewModel(settingsService);
                var settingsWindow = new SettingsWindow
                {
                    DataContext = settingsViewModel
                };
                
                await settingsWindow.ShowDialog(MainWindow);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to open settings window from native menu");
        }
    }

    private void DisableAvaloniaDataAnnotationValidation()
    {
        // Get an array of plugins to remove
        var dataValidationPluginsToRemove =
            BindingPlugins.DataValidators.OfType<DataAnnotationsValidationPlugin>().ToArray();

        // remove each entry found
        foreach (var plugin in dataValidationPluginsToRemove)
        {
            BindingPlugins.DataValidators.Remove(plugin);
        }
    }
}

/// <summary>
/// Global exception handler for ReactiveUI to prevent unhandled exceptions from crashing the app
/// </summary>
public class ReactiveExceptionHandler : IObserver<Exception>
{
    public void OnNext(Exception ex)
    {
        // Log the exception but don't crash the app
        Log.Warning("ReactiveUI Exception (handled): {Message}", ex.Message);
        
        // If it's a threading exception, we can safely ignore it as we've implemented proper UI thread dispatching
        if (ex is InvalidOperationException && ex.Message.Contains("Call from invalid thread"))
        {
            Log.Debug("Threading exception caught and ignored - UI updates are properly handled via Dispatcher");
            return;
        }
        
        // For other exceptions, log more details
        if (ex.InnerException != null)
        {
            Log.Warning("Inner exception: {InnerMessage}", ex.InnerException.Message);
        }
    }

    public void OnError(Exception error)
    {
        Log.Error("ReactiveUI Critical Error: {Message}", error.Message);
    }

    public void OnCompleted()
    {
        // No action needed
    }
}

/// <summary>
/// Custom scheduler implementation for ReactiveUI that ensures operations happen on the UI thread
/// </summary>
public class AvaloniaUIThreadScheduler : IScheduler
{
    public DateTimeOffset Now => DateTimeOffset.Now;

    public IDisposable Schedule<TState>(TState state, Func<IScheduler, TState, IDisposable> action)
    {
        var disposable = new System.Reactive.Disposables.SerialDisposable();
        
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                disposable.Disposable = action(this, state);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ReactiveUI scheduler exception");
            }
        });
        
        return disposable;
    }

    public IDisposable Schedule<TState>(TState state, TimeSpan dueTime, Func<IScheduler, TState, IDisposable> action)
    {
        var disposable = new System.Reactive.Disposables.SerialDisposable();
        
        var timer = new DispatcherTimer { Interval = dueTime };
        timer.Tick += (_, _) =>
        {
            timer.Stop();
            try
            {
                disposable.Disposable = action(this, state);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "ReactiveUI scheduler exception");
            }
        };
        timer.Start();
        
        return disposable;
    }

    public IDisposable Schedule<TState>(TState state, DateTimeOffset dueTime, Func<IScheduler, TState, IDisposable> action)
    {
        var delay = dueTime - Now;
        return Schedule(state, delay, action);
    }
}