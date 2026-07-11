using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Input;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CST.Avalonia.ViewModels;
using CST.Avalonia.Views;
using CST.Avalonia.Services;
using CST.Avalonia.Models;
using CST.Avalonia.Constants;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using CST;
using CST.Conversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using ReactiveUI;
using ReactiveUI.Builder;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using WebViewControl;
#if MACOS
using CST.Avalonia.Services.Platform.Mac;
#endif

namespace CST.Avalonia;

public partial class App : Application
{
    public static ServiceProvider? ServiceProvider { get; private set; }
    public static Window? MainWindow { get; private set; }

    // Opt-in loopback API server for AI tools; gated on settings at launch (restart to apply). (#186)
    private CST.Avalonia.Services.LocalApi.LocalApiServer? _localApiServer;

    /// <summary>The configured local-API port when it was already in use at startup (so the API did NOT bind),
    /// else null. The Settings UI reads this to prompt the user to rotate the port. (#275)</summary>
    public int? LocalApiPortInUse { get; private set; }
    /// <summary>
    /// True once application shutdown has begun. Floating windows close as part of shutdown too;
    /// consumers (e.g. CstDockFactory.CloseHostWindow's book rescue) use this to distinguish
    /// "user closed this window" from "everything is closing" — after ShutdownRequested the saved
    /// state is already written and ServiceProvider may be disposed, so per-window rescue work
    /// must be skipped. (DOCK-2)
    /// </summary>
    public static bool IsShuttingDown { get; private set; }
    private bool _hasRestoredInitialBooks = false;

    // Menu items for updating checkmarks across all windows
    private List<NativeMenuItem> _selectBookMenuItems = new List<NativeMenuItem>();
    private List<NativeMenuItem> _searchMenuItems = new List<NativeMenuItem>();
    private List<NativeMenuItem> _dictionaryMenuItems = new List<NativeMenuItem>();

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
        
        // ReactiveUI 23 replaced the static RxApp config with a builder. Set the UI-thread scheduler
        // (so ReactiveCommands/observables marshal to the UI thread) and the global exception handler.
        // (ReactiveUI.Avalonia was dropped; we supply our own AvaloniaUIThreadScheduler.)
        RxAppBuilder.CreateReactiveUIBuilder()
            .WithMainThreadScheduler(new AvaloniaUIThreadScheduler())
            .WithExceptionHandler(new ReactiveExceptionHandler())
            .Build();

        // Wire up native menu events on macOS
        if (OperatingSystem.IsMacOS())
        {
            SetupNativeMenuEvents();
        }
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // FIX: Prevent "Chromium Safe Storage" keychain prompt on macOS
        // Use mock keychain to avoid system keychain access prompts
        if (OperatingSystem.IsMacOS())
        {
            WebView.Settings.AddCommandLineSwitch("use-mock-keychain", "");
            WebView.Settings.AddCommandLineSwitch("disable-password-generation", "");

            // CRITICAL: Set CEF subprocess path to helper bundle in packaged app
            // CEF needs to find the helper processes in the correct location
            // Use command line switch since WebViewControl doesn't expose CefSettings directly

            // Detect if we're running from an app bundle by checking executable path
            var exePath = Environment.ProcessPath;
            if (exePath != null && exePath.Contains(".app/Contents/MacOS/"))
            {
                // Extract bundle path from executable path
                // Executable: /path/to/CST Reader.app/Contents/MacOS/CST.Avalonia
                var macosIndex = exePath.IndexOf(".app/Contents/MacOS/");
                if (macosIndex > 0)
                {
                    var bundlePath = exePath.Substring(0, macosIndex + 4); // Include ".app"
                    var helperPath = Path.Combine(bundlePath,
                        "Contents/Frameworks/CST Reader Helper.app/Contents/MacOS/CST Reader Helper");

                    if (File.Exists(helperPath))
                    {
                        WebView.Settings.AddCommandLineSwitch("browser-subprocess-path", helperPath);
                        Log.Information("CEF Helper path set to: {HelperPath}", helperPath);
                    }
                    else
                    {
                        Log.Warning("CEF Helper not found at expected path: {HelperPath}", helperPath);
                    }
                }
            }
        }

        // Disable persistent cache to use in-memory storage only
        // We still need to provide a path (WebViewLoader expects it), but PersistCache=false ensures it's temporary
        WebView.Settings.PersistCache = false;
        // Leave CachePath as default (temp directory) to avoid ArgumentNullException on cleanup

        // Welcome page will show startup status instead of separate splash screen
        Log.Information("Starting application with Welcome page status display");

        // Configure services
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit.
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();

            // Get and initialize the OpenBookDialogViewModel BEFORE creating the layout
            var openBookViewModel = ServiceProvider.GetRequiredService<OpenBookDialogViewModel>();

            // Create the main window with dockable layout first so Welcome page can display status
            var layoutViewModel = new LayoutViewModel();
            MainWindow = new SimpleTabbedWindow
            {
                DataContext = layoutViewModel
            };

            desktop.MainWindow = MainWindow;

            // Close application when main window is closed
            desktop.ShutdownMode = ShutdownMode.OnMainWindowClose;

            // Set up window-level native menu events (View menu) on macOS
            if (OperatingSystem.IsMacOS())
            {
                SetupWindowMenuEvents();
                // Update panel visibility to sync initial checkmark state
                layoutViewModel.UpdatePanelVisibility();
            }

            // Get the WelcomeViewModel to update startup status
            var welcomeViewModel = layoutViewModel.GetWelcomeViewModel();

            // When startup/indexing finishes, return focus to the tab that was active at restore - the
            // Welcome tab is only pulled forward while real work runs (re-index/download). (#56)
            if (welcomeViewModel != null)
                welcomeViewModel.StartupCompleted += OnStartupCompletedReturnFocus;

            // Show the window BEFORE starting initialization
            MainWindow.Show();

            // Start background initialization tasks that depend on settings
            var initTask = Task.Run(async () =>
            {
                // Load settings first - MUST complete before indexing
                Dispatcher.UIThread.Post(() => welcomeViewModel?.SetStartupStatus("Loading settings..."));
                await LoadSettingsAsync();

                // Load application state
                Dispatcher.UIThread.Post(() => welcomeViewModel?.SetStartupStatus("Loading application state..."));
                await LoadApplicationStateAsync();

                // Initialize script service from loaded state (will be called after state loads)
                // State service will notify ScriptService via event when state is ready

                // Pre-load fonts for all scripts to avoid UI delays in settings
                Dispatcher.UIThread.Post(() => welcomeViewModel?.SetStartupStatus("Pre-loading fonts..."));
                await InitializeFontsAsync();

                // Initialize the search index directory BEFORE checking for XML updates.
                // The update flow re-indexes downloaded files via IndexingService, which
                // needs its index directory set first; otherwise BookIndexer throws on an
                // empty path. (InitializeIndexingAsync below calls InitializeAsync again,
                // which is idempotent.)
                var indexingServiceInit = ServiceProvider?.GetRequiredService<IIndexingService>();
                if (indexingServiceInit != null)
                {
                    await indexingServiceInit.InitializeAsync();
                }

                // Check for XML updates first to ensure we have latest files
                Dispatcher.UIThread.Post(() => welcomeViewModel?.SetStartupStatus("Checking for XML updates..."));
                await CheckForXmlUpdatesAsync(welcomeViewModel);

                // Then initialize indexing with the updated files
                Dispatcher.UIThread.Post(() => welcomeViewModel?.SetStartupStatus("Checking search index..."));
                await InitializeIndexingAsync(welcomeViewModel);
            });

            // Wait for initialization to complete and hide status banner
            _ = Task.Run(async () =>
            {
                try
                {
                    await initTask;
                    Log.Information("Initialization completed - hiding welcome status banner");

                    // Hide the startup status banner on UI thread
                    Dispatcher.UIThread.Post(() =>
                    {
                        Log.Information("Calling CompleteStartup() to hide banner");
                        welcomeViewModel?.CompleteStartup();
                        Log.Information("CompleteStartup() called");
                    }, DispatcherPriority.Send);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "Error during initialization");
                    // Hide status anyway on error
                    Dispatcher.UIThread.Post(() =>
                    {
                        Log.Warning("Hiding banner due to initialization error");
                        welcomeViewModel?.CompleteStartup();
                    }, DispatcherPriority.Send);
                }
            });


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
                // Route through TryShutdown so the ShutdownRequested handler runs the graceful
                // sequence (await state save -> dispose services). The old path fired the save
                // fire-and-forget and then hard-Shutdown()'d, racing the save against process exit. (XCUT-1)
                desktop.TryShutdown();
            };

            // Handle application shutdown to save state
            desktop.ShutdownRequested += async (sender, args) =>
            {
                Log.Information("SHUTDOWN: ShutdownRequested event triggered");

                // From here on, window Closing events are part of shutdown, not user actions. (DOCK-2)
                IsShuttingDown = true;

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
                await StartLocalApiIfEnabledAsync(settingsService);
            }
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "Failed to load settings");
        }
    }

    /// <summary>
    /// Start the opt-in loopback API server if AI features + the local API are enabled. Gated at launch:
    /// changes to the setting take effect on restart (live toggle is a later iteration). (#186)
    /// </summary>
    private async Task StartLocalApiIfEnabledAsync(ISettingsService settingsService)
    {
        try
        {
            if (!settingsService.Settings.Ai.LocalApiEnabled) return;

            var dir = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                CST.Avalonia.Constants.AppConstants.AppDataDirectoryName);
            System.IO.Directory.CreateDirectory(dir);

            var localApi = settingsService.Settings.Ai.LocalApi;

            // Persist a stable bearer token on first use, then reuse it across launches so a copied MCP client
            // config stays valid (#275). Source of truth is settings; it's also copied into local-api.json (0600).
            if (string.IsNullOrEmpty(localApi.Token))
            {
                localApi.Token = CST.Avalonia.Services.LocalApi.ApiToken.Generate();
                settingsService.RequestSave();
            }

            int port = localApi.Port;
            // A FIXED port can already be taken. Check first and, rather than let Kestrel throw and fail the API
            // silently, warn and skip start so the user can rotate the port in Settings (#275).
            if (port > 0 && !CST.Avalonia.Services.LocalApi.PortAvailability.IsAvailable(port))
            {
                Log.Warning("Local API port {Port} is in use; not starting. Rotate the port in Settings.", port);
                LocalApiPortInUse = port;   // UI (kestrel) reads this to show a rotate-the-port popup.
                return;
            }
            LocalApiPortInUse = null;

            var version = typeof(App).Assembly.GetName().Version?.ToString() ?? "unknown";
            // Resolve the tools through the shared factory (covered by AppCompositionTests), so a tool that is
            // registered but forgotten here can't silently 404 an endpoint again.
            _localApiServer = ServiceProvider is { } sp
                ? CST.Avalonia.Services.LocalApi.LocalApiServer.FromServiceProvider(sp, version, dir, Log.Logger, port, localApi.Token)
                : new CST.Avalonia.Services.LocalApi.LocalApiServer(version, dir, Log.Logger, port: port, token: localApi.Token);
            await _localApiServer.StartAsync();
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to start local API server");
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
    private async Task InitializeIndexingAsync(WelcomeViewModel? welcomeViewModel)
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

                // Create progress reporter for welcome page
                var progress = new Progress<CST.Lucene.IndexingProgress>(p =>
                {
                    if (p != null && !p.IsComplete)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            if (p.TotalBooks > 0)
                            {
                                welcomeViewModel?.SetStartupStatus($"Indexing book {p.CurrentBook} of {p.TotalBooks}...", isWork: true);
                            }
                            else if (!string.IsNullOrEmpty(p.StatusMessage))
                            {
                                welcomeViewModel?.SetStartupStatus(p.StatusMessage, isWork: true);
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
    private async Task CheckForXmlUpdatesAsync(WelcomeViewModel? welcomeViewModel)
    {
        try
        {
            Log.Information("CheckForXmlUpdatesAsync() started");

            // Initialize XmlFileDatesService first since XmlUpdateService depends on it
            var xmlFileDatesService = ServiceProvider?.GetRequiredService<IXmlFileDatesService>();
            if (xmlFileDatesService == null)
            {
                Log.Warning("XmlFileDatesService not available in DI container");
                return;
            }

            Log.Information("Initializing XmlFileDatesService...");
            await xmlFileDatesService.InitializeAsync();
            Log.Information("XmlFileDatesService initialized");

            var xmlUpdateService = ServiceProvider?.GetRequiredService<IXmlUpdateService>();
            if (xmlUpdateService == null)
            {
                Log.Warning("XmlUpdateService not available in DI container");
                return;
            }

            // Subscribe to status updates for logging and welcome page
            xmlUpdateService.UpdateStatusChanged += message =>
            {
                Log.Information("XML Update Status: {Message}", message);

                // Skip completion messages to avoid race condition with banner hiding
                if (message.Contains("up to date", StringComparison.OrdinalIgnoreCase) ||
                    message.Contains("complete", StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                // Update welcome page status
                Dispatcher.UIThread.Post(() =>
                {
                    welcomeViewModel?.SetStartupStatus(message, isWork: true);
                });
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
        finally
        {
            // Deterministic replacement for the Open Book panel's old ctor Task.Delay(100) guess: build its
            // tree only after the state load has settled, so restore reads the real ExpandedNodeKeys and the
            // user's first expand/collapse can't clobber the persisted expansion. In finally (not the try)
            // so a failed load still builds the panel against the default empty state rather than leaving it
            // blank. Idempotent. (SCRIPT-5)
            var openBookViewModel = ServiceProvider?.GetService<OpenBookDialogViewModel>();
            if (openBookViewModel != null)
                await Dispatcher.UIThread.InvokeAsync(() => openBookViewModel.InitializeFromState());
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

        // Restore the Search pane inputs now that state is loaded. The SearchViewModel singleton may be
        // constructed before the async load completes, so its ctor only wires up saving - the restore is
        // applied here. Marshalled to the UI thread (this may run off it) since it constructs the VM if
        // needed and updates bound properties. (#87)
        var searchState = state.SearchDialog;
        Dispatcher.UIThread.Post(() =>
        {
            var searchViewModel = ServiceProvider?.GetService<SearchViewModel>();
            searchViewModel?.ApplyState(searchState);
        });
        
        // Restore main window state (dimensions, position, etc.) - MUST be on UI thread
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
            desktop.MainWindow is SimpleTabbedWindow mainWindow)
        {
            await Dispatcher.UIThread.InvokeAsync(() => mainWindow.RestoreWindowState());
        }
        
        // Restore book windows if any exist
        if (state.BookWindows.Any() && !_hasRestoredInitialBooks)
        {
            _hasRestoredInitialBooks = true;

            // CRITICAL: Make a copy of BookWindows BEFORE clearing, since state.BookWindows
            // and stateService.Current.BookWindows reference the same list!
            var bookWindowsToRestore = state.BookWindows.ToList();
            Log.Information("Captured {Count} book windows to restore", bookWindowsToRestore.Count);

            // Clear existing bookWindows before restoration so we start clean and only restored books
            // end up in state. This is a plain list mutation (no StateChanged), so it needs no suppression;
            // RestoreBookWindowsAsync suppresses events around the actual book-opening.
            var stateService = ServiceProvider?.GetRequiredService<IApplicationStateService>();
            if (stateService != null)
            {
                Log.Information("Clearing {Count} existing book window entries before restoration", stateService.Current.BookWindows.Count);
                stateService.Current.BookWindows.Clear();
            }

            // Awaited so the event-subscription pass below runs strictly after restoration. The restore
            // waits on the window's readiness signal instead of a fixed delay. (#70)
            await RestoreBookWindowsAsync(bookWindowsToRestore);
        }

        // Ensure all books have event subscriptions. Runs after the window is ready and after any book
        // restoration has completed - on the UI thread, no fixed delay (was Task.Delay(1000)). (#70)
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop2 &&
            desktop2.MainWindow is SimpleTabbedWindow readyWindow)
        {
            await readyWindow.WhenReady;
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (readyWindow.DataContext is LayoutViewModel layoutViewModel &&
                    layoutViewModel.Factory is CstDockFactory factory)
                {
                    Log.Information("Calling EnsureBookEventSubscriptions");
                    factory.EnsureBookEventSubscriptions();
                }
                else
                {
                    Log.Warning("Could not ensure book event subscriptions - factory not found");
                }
            }, DispatcherPriority.Background);
        }
    }

    private void OnApplicationStateChanged(ApplicationState state)
    {
        // This method is called whenever application state changes during normal operation
        // Book window restoration should only happen at startup, not during state changes
        // Removing restoration logic to prevent duplicate book opening bug
        
        // Future: Could add other state change handling here if needed
    }

    private async Task RestoreBookWindowsAsync(List<BookWindowState> bookWindows)
    {
        try
        {
            Log.Information("Restoring {BookCount} book windows from saved state", bookWindows.Count);

            if (ApplicationLifetime is not IClassicDesktopStyleApplicationLifetime desktop ||
                desktop.MainWindow is not SimpleTabbedWindow mainWindow)
            {
                Log.Warning("Main window not available for book restoration");
                return;
            }

            // Deterministic readiness instead of Task.Delay(500): wait until the window's UI is ready. (#70)
            await mainWindow.WhenReady;

            var stateService = ServiceProvider?.GetRequiredService<IApplicationStateService>();
            try
            {
                // Suppress StateChanged events during restoration to prevent a feedback loop
                if (stateService != null)
                    stateService.SetStateChangedEventsSuppression(true);

                string? selectedBookWindowId = null;

                // First pass: open all books (on the UI thread) and note which one should be selected
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    var bookWindowsCopy = bookWindows.ToList();
                    foreach (var bookWindowState in bookWindowsCopy)
                    {
                        try
                        {
                            if (bookWindowState.IsSelected)
                                selectedBookWindowId = bookWindowState.WindowId;

                            if (bookWindowState.BookIndex >= 0 && bookWindowState.BookIndex < Books.Inst.Count)
                            {
                                var book = Books.Inst[bookWindowState.BookIndex];
                                if (book.FileName == bookWindowState.BookFileName)
                                {
                                    Log.Information("Restoring book: {BookFile} with WindowId: {WindowId}, SearchTerms: {TermCount}, Positions: {PosCount}, Anchor: {Anchor}",
                                        book.FileName, bookWindowState.WindowId, bookWindowState.SearchTerms.Count, bookWindowState.SearchPositions.Count,
                                        bookWindowState.CurrentAnchor ?? "null");
                                    mainWindow.OpenBook(book, bookWindowState.SearchTerms, bookWindowState.BookScript, bookWindowState.WindowId,
                                        bookWindowState.DocId, bookWindowState.SearchPositions, bookWindowState.CurrentAnchor,
                                        bookWindowState.CurrentHitIndex, bookWindowState.ShowFootnotes, bookWindowState.ShowSearchTerms);
                                    Log.Debug("Book restored: {BookFile} with script: {Script}, anchor: {Anchor}",
                                        book.FileName, bookWindowState.BookScript, bookWindowState.CurrentAnchor ?? "null");
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
                });

                // Remember the active tab so it can be re-selected after startup work (a re-index pulls the
                // Welcome tab forward to show progress). (#56)
                _restoredSelectedWindowId = selectedBookWindowId;

                // Second pass: restore the selected tab after the tabs have had a layout pass. Background
                // priority runs after pending layout - deterministic, replaces Task.Delay(100). (#70)
                if (!string.IsNullOrEmpty(selectedBookWindowId))
                    await Dispatcher.UIThread.InvokeAsync(() => RestoreSelectedBookTab(selectedBookWindowId),
                        DispatcherPriority.Background);
            }
            finally
            {
                if (stateService != null)
                    stateService.SetStateChangedEventsSuppression(false);
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to restore book windows");
        }
    }

    // WindowId of the book tab that was active when state was restored; re-selected after startup work
    // finishes (a re-index pulls the Welcome tab forward). Null/empty when no book tab was active. (#56)
    private string? _restoredSelectedWindowId;

    private void OnStartupCompletedReturnFocus()
    {
        var id = _restoredSelectedWindowId;
        if (string.IsNullOrEmpty(id))
            return; // nothing was active to return to (e.g. user closed on the Welcome tab)

        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow is SimpleTabbedWindow mainWindow &&
                    mainWindow.DataContext is LayoutViewModel layoutViewModel)
                {
                    var documentDock = FindDocumentDockInLayout(layoutViewModel.Layout);
                    // Only return focus if the Welcome tab is still active - don't yank a user who
                    // deliberately switched to a book tab while the work was running. (#56)
                    if (documentDock?.ActiveDockable is WelcomeViewModel)
                        RestoreSelectedBookTab(id);
                }
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "Could not return focus to restored tab after startup");
            }
        }, DispatcherPriority.Background);
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
                // Re-capture the latest live ViewModel state (scroll position, search hit
                // index, etc.) BEFORE serializing. Per-VM capture otherwise only happens on
                // a tab switch, so anything changed since the last switch is lost on close —
                // notably when the window is closed with the red button instead of Cmd+Q.
                if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop &&
                    desktop.MainWindow is SimpleTabbedWindow mainWindow &&
                    mainWindow.DataContext is LayoutViewModel layoutViewModel &&
                    layoutViewModel.Factory is CstDockFactory factory)
                {
                    Log.Information("SHUTDOWN: Re-capturing live book window states before save");
                    await factory.SaveAllBookWindowStatesAsync();

                    // Final main-window geometry capture (bypasses the debounce). On Cmd+Q this
                    // handler runs BEFORE the window's Closing event, so without this the state
                    // file was written with geometry up to 500ms stale — and window MOVES were
                    // previously never captured at all. (DOCK-6)
                    mainWindow.SaveWindowState(force: true);
                }

                // Force immediate save on shutdown
                var success = await stateService.ForceSaveAsync();
                Log.Information("SHUTDOWN: Final state save completed - Success: {Success}", success);
            }
            else
            {
                Log.Warning("SHUTDOWN: ApplicationStateService not available");
            }

            // Flush any pending debounced settings save so a change made within the debounce window
            // isn't lost on close. (#67)
            var settingsService = ServiceProvider?.GetService<ISettingsService>();
            if (settingsService != null)
            {
                await settingsService.FlushPendingSaveAsync();
                Log.Information("SHUTDOWN: Pending settings save flushed");
            }

            if (_localApiServer != null)
            {
                await _localApiServer.StopAsync();
                Log.Information("SHUTDOWN: Local API server stopped");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "SHUTDOWN: Failed to save application state");
        }
    }

    private static LogEventLevel ParseLogLevel(string? name) => name?.ToLowerInvariant() switch
    {
        "debug" => LogEventLevel.Debug,
        "information" => LogEventLevel.Information,
        "warning" => LogEventLevel.Warning,
        "error" => LogEventLevel.Error,
        "fatal" => LogEventLevel.Fatal,
        _ => LogEventLevel.Information
    };

    // Peek the persisted log level straight from settings.json. This runs before DI is built, and we
    // deliberately don't use SettingsService: its ctor never reads disk (so .Settings would just be
    // defaults), and LoadSettingsAsync() has side effects (migration saves, default-directory creation)
    // that must not fire from a throwaway probe. Returns null (-> default Information) on any failure. (STATE-1)
    private static string? ReadSavedLogLevel()
    {
        try
        {
            var settingsPath = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppConstants.AppDataDirectoryName,
                "settings.json");
            if (!File.Exists(settingsPath))
                return null;

            var json = File.ReadAllText(settingsPath);
            var settings = JsonSerializer.Deserialize<Settings>(json,
                new JsonSerializerOptions { PropertyNameCaseInsensitive = true });
            return settings?.DeveloperSettings?.LogLevel;
        }
        catch
        {
            return null;
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Configure logging with priority: Environment Variable > Saved Setting > Default.
        // Env var wins (debugging); otherwise use the persisted DeveloperSettings.LogLevel. (STATE-1)
        var envLogLevel = Environment.GetEnvironmentVariable("CST_LOG_LEVEL");
        var logLevel = !string.IsNullOrEmpty(envLogLevel)
            ? ParseLogLevel(envLogLevel)
            : ParseLogLevel(ReadSavedLogLevel());

        // Get the logs directory in user's Application Support
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
        services.AddSingleton<IDictionaryService, DictionaryService>();

        // Surface-C tool wrappers (exposed over the local API). (#186)
        services.AddSingleton<CST.Tools.ISearchTool, Services.Tools.SearchTool>();
        services.AddSingleton<CST.Tools.IDictionaryTool, Services.Tools.DictionaryTool>();
        services.AddSingleton<CST.Tools.IPassageTool, Services.Tools.PassageTool>();
        services.AddSingleton<CST.Tools.IScriptTool, Services.Tools.ScriptTool>();
        services.AddTransient<TreeStateService>();
        
        // Indexing services
        services.AddSingleton<IXmlFileDatesService, XmlFileDatesService>();
        services.AddSingleton<IIndexingService, IndexingService>();
        
        // XML Update service
        services.AddSingleton<IXmlUpdateService, XmlUpdateService>();

        // SharePoint service for PDF downloads
        services.AddSingleton<ISharePointService, SharePointService>();

        // Register ViewModels
        services.AddSingleton<OpenBookDialogViewModel>();
        services.AddSingleton<SearchViewModel>();
        services.AddSingleton<DictionaryViewModel>();
        // services.AddTransient<MainWindowViewModel>();
    }

    private void SetupNativeMenuEvents()
    {
        // Set up application-level menu (Preferences in app menu)
        var appMenu = NativeMenu.GetMenu(this);
        if (appMenu != null && appMenu.Count() > 0)
        {
            foreach (var item in appMenu)
            {
                if (item is NativeMenuItem menuItem && menuItem.Header?.ToString() == "Preferences...")
                {
                    menuItem.Click += async (s, e) =>
                    {
                        Log.Information("Preferences menu clicked via native menu");
                        await ShowSettingsWindow();
                    };
                }
            }
        }
    }

    private void SetupWindowMenuEvents()
    {
        // Set up window-level menu (View menu as top-level menu)
        if (MainWindow != null)
        {
            var windowMenu = NativeMenu.GetMenu(MainWindow);
            if (windowMenu != null && windowMenu.Count() > 0)
            {
                foreach (var item in windowMenu)
                {
                    // Handle View menu
                    if (item is NativeMenuItem viewMenuItem && viewMenuItem.Header?.ToString() == "View")
                    {
                        var viewMenu = viewMenuItem.Menu;
                        if (viewMenu != null)
                        {
                            foreach (var viewItem in viewMenu)
                            {
                                if (viewItem is NativeMenuItem viewSubItem)
                                {
                                    if (viewSubItem.Header?.ToString() == "Select a Book")
                                    {
                                        _selectBookMenuItems.Add(viewSubItem);
                                        viewSubItem.ToggleType = NativeMenuItemToggleType.CheckBox;
                                        viewSubItem.IsChecked = true; // Start checked since panels are visible by default
                                        viewSubItem.Click += (s, e) =>
                                        {
                                            Log.Information("Toggle Select a Book panel clicked via View menu (main window)");
                                            ToggleSelectBookPanel();
                                        };
                                    }
                                    else if (viewSubItem.Header?.ToString() == "Search")
                                    {
                                        _searchMenuItems.Add(viewSubItem);
                                        viewSubItem.ToggleType = NativeMenuItemToggleType.CheckBox;
                                        viewSubItem.IsChecked = true; // Start checked since panels are visible by default
                                        viewSubItem.Click += (s, e) =>
                                        {
                                            Log.Information("Toggle Search panel clicked via View menu (main window)");
                                            ToggleSearchPanel();
                                        };
                                    }
                                    else if (viewSubItem.Header?.ToString() == "Dictionary")
                                    {
                                        _dictionaryMenuItems.Add(viewSubItem);
                                        viewSubItem.ToggleType = NativeMenuItemToggleType.CheckBox;
                                        viewSubItem.IsChecked = true; // Start checked since panels are visible by default
                                        viewSubItem.Click += (s, e) =>
                                        {
                                            Log.Information("Toggle Dictionary panel clicked via View menu (main window)");
                                            ToggleDictionaryPanel();
                                        };
                                    }
                                }
                            }
                        }
                    }
                }
            }

            // Subscribe to panel visibility changes to update menu checkmarks
            if (MainWindow.DataContext is LayoutViewModel layoutViewModel)
            {
                layoutViewModel.PanelVisibilityChanged += OnPanelVisibilityChanged;
            }
        }
    }

    private void OnPanelVisibilityChanged(object? sender, EventArgs e)
    {
        if (sender is LayoutViewModel layoutViewModel)
        {
            // Update all menu checkmarks across all windows
            foreach (var menuItem in _selectBookMenuItems)
            {
                menuItem.IsChecked = layoutViewModel.IsSelectBookPanelVisible;
            }

            foreach (var menuItem in _searchMenuItems)
            {
                menuItem.IsChecked = layoutViewModel.IsSearchPanelVisible;
            }

            foreach (var menuItem in _dictionaryMenuItems)
            {
                menuItem.IsChecked = layoutViewModel.IsDictionaryPanelVisible;
            }

            Log.Debug("Updated menu checkmarks across {SelectBookCount} + {SearchCount} + {DictionaryCount} menu items - SelectBook: {SelectBook}, Search: {Search}, Dictionary: {Dictionary}",
                _selectBookMenuItems.Count, _searchMenuItems.Count, _dictionaryMenuItems.Count,
                layoutViewModel.IsSelectBookPanelVisible, layoutViewModel.IsSearchPanelVisible, layoutViewModel.IsDictionaryPanelVisible);
        }
    }

    // Public method to set up menu events for floating windows
    /// <summary>
    /// Builds and wires the native View/Tools menu for a floating dock window (macOS). This is the single
    /// source for the floating-window menu - it creates the items, syncs their initial state, attaches the
    /// click handlers, tracks the toggle items for cross-window state sync, and assigns the menu to the
    /// window. (Previously the structure was built in CstHostWindow's ctor and re-discovered here by header
    /// string; #79.)
    /// </summary>
    public void SetupFloatingWindowMenu(Window window)
    {
        if (!OperatingSystem.IsMacOS()) return;

        try
        {
            var layoutViewModel = MainWindow?.DataContext as LayoutViewModel;

            // View menu: Select a Book / Search (checkable, mirror the panels' visibility)
            var selectBookItem = new NativeMenuItem
            {
                Header = "Select a Book",
                ToggleType = NativeMenuItemToggleType.CheckBox,
                IsChecked = layoutViewModel?.IsSelectBookPanelVisible ?? false
            };
            selectBookItem.Click += (s, e) =>
            {
                Log.Information("Toggle Select a Book panel clicked via View menu (floating window)");
                ToggleSelectBookPanel();
            };
            _selectBookMenuItems.Add(selectBookItem);

            var searchItem = new NativeMenuItem
            {
                Header = "Search",
                ToggleType = NativeMenuItemToggleType.CheckBox,
                IsChecked = layoutViewModel?.IsSearchPanelVisible ?? false
            };
            searchItem.Click += (s, e) =>
            {
                Log.Information("Toggle Search panel clicked via View menu (floating window)");
                ToggleSearchPanel();
            };
            _searchMenuItems.Add(searchItem);

            var dictionaryItem = new NativeMenuItem
            {
                Header = "Dictionary",
                ToggleType = NativeMenuItemToggleType.CheckBox,
                IsChecked = layoutViewModel?.IsDictionaryPanelVisible ?? false
            };
            dictionaryItem.Click += (s, e) =>
            {
                Log.Information("Toggle Dictionary panel clicked via View menu (floating window)");
                ToggleDictionaryPanel();
            };
            _dictionaryMenuItems.Add(dictionaryItem);

            var viewMenu = new NativeMenu();
            viewMenu.Add(selectBookItem);
            viewMenu.Add(searchItem);
            viewMenu.Add(dictionaryItem);

            // Tools menu: Go To... + View Source (floating windows are book-centric)
            var goToItem = new NativeMenuItem { Header = "Go To...", Gesture = KeyGesture.Parse("Cmd+G") };
            goToItem.Click += (s, e) =>
            {
                Log.Information("Go To menu item clicked via Tools menu (floating window)");
                OnGoToMenuItemClickFromFloatingWindow(window);
            };

            // View Source as app-level shortcuts so they work without the WebView having focus. (Cmd+E/Shift+Cmd+E)
            var viewSource1957Item = new NativeMenuItem { Header = "View Source (1957 ed.)", Gesture = KeyGesture.Parse("Cmd+E") };
            viewSource1957Item.Click += (s, e) => OnViewSourceFromFloatingWindow(window, source2010: false);
            var viewSource2010Item = new NativeMenuItem { Header = "View Source (2010 ed.)", Gesture = KeyGesture.Parse("Cmd+Shift+E") };
            viewSource2010Item.Click += (s, e) => OnViewSourceFromFloatingWindow(window, source2010: true);

            var toolsMenu = new NativeMenu();
            toolsMenu.Add(goToItem);
            toolsMenu.Add(viewSource1957Item);
            toolsMenu.Add(viewSource2010Item);

            var nativeMenu = new NativeMenu();
            nativeMenu.Add(new NativeMenuItem { Header = "View", Menu = viewMenu });
            nativeMenu.Add(new NativeMenuItem { Header = "Tools", Menu = toolsMenu });
            NativeMenu.SetMenu(window, nativeMenu);

            Log.Information("Floating window menu built - tracked toggle items: {SelectBookCount} select-book, {SearchCount} search",
                _selectBookMenuItems.Count, _searchMenuItems.Count);
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to set up floating window menu");
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

    private void OnGoToMenuItemClickFromFloatingWindow(Window floatingWindow)
    {
        try
        {
            Log.Information("Go To menu item clicked from floating window: {WindowTitle}", floatingWindow.Title);
            Log.Information("Window type: {Type}", floatingWindow.GetType().Name);

            // Floating windows are CstHostWindow instances with Layout property
            if (floatingWindow is CstHostWindow hostWindow)
            {
                Log.Information("Found CstHostWindow");
                Log.Information("HostWindow.Layout type: {Type}", hostWindow.Layout?.GetType().Name ?? "null");

                if (hostWindow.Layout != null)
                {
                    // Find DocumentDock in the host window's layout
                    var documentDock = FindDocumentDockInLayout(hostWindow.Layout) as Dock.Model.Mvvm.Controls.DocumentDock;
                    Log.Information("DocumentDock found: {Found}", documentDock != null);

                    if (documentDock != null)
                    {
                        Log.Information("DocumentDock.ActiveDockable type: {Type}", documentDock.ActiveDockable?.GetType().Name ?? "null");

                        if (documentDock.ActiveDockable is BookDisplayViewModel bookViewModel)
                        {
                            Log.Information("Triggering Go To dialog for active book in floating window: {BookFile}", bookViewModel.Book.FileName);
                            bookViewModel.InvokeOpenGoToDialog();
                            return;
                        }
                    }
                }
            }
            else
            {
                Log.Warning("Window is not a CstHostWindow, type: {Type}", floatingWindow.GetType().Name);
            }

            Log.Warning("Could not find active book document in floating window for Go To command");
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling Go To from floating window");
        }
    }

    // View Source (Cmd+E = 1957, Shift+Cmd+E = 2010) for the active book in a floating window, so the
    // shortcut works without the book's WebView having focus (previously only a JS keydown handled it).
    private void OnViewSourceFromFloatingWindow(Window floatingWindow, bool source2010)
    {
        try
        {
            if (floatingWindow is CstHostWindow hostWindow && hostWindow.Layout != null)
            {
                var documentDock = FindDocumentDockInLayout(hostWindow.Layout) as Dock.Model.Mvvm.Controls.DocumentDock;
                if (documentDock?.ActiveDockable is BookDisplayViewModel bookViewModel)
                {
                    var command = source2010 ? bookViewModel.ShowSource2010Command : bookViewModel.ShowSource1957Command;
                    Log.Information("View Source ({Edition}) via floating-window menu for book: {BookFile}",
                        source2010 ? "2010" : "1957", bookViewModel.Book.FileName);
                    command.Execute().Subscribe(_ => { }, ex => Log.Debug(ex, "View Source command not available"));
                }
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Error handling View Source from floating window");
        }
    }

    private void ToggleSelectBookPanel()
    {
        try
        {
            if (MainWindow?.DataContext is LayoutViewModel layoutViewModel)
            {
                layoutViewModel.ToggleSelectBookPanel();
            }
            else
            {
                Log.Warning("Cannot toggle Select a Book panel - LayoutViewModel not available");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to toggle Select a Book panel");
        }
    }

    private void ToggleSearchPanel()
    {
        try
        {
            if (MainWindow?.DataContext is LayoutViewModel layoutViewModel)
            {
                layoutViewModel.ToggleSearchPanel();
            }
            else
            {
                Log.Warning("Cannot toggle Search panel - LayoutViewModel not available");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to toggle Search panel");
        }
    }

    private void ToggleDictionaryPanel()
    {
        try
        {
            if (MainWindow?.DataContext is LayoutViewModel layoutViewModel)
            {
                layoutViewModel.ToggleDictionaryPanel();
            }
            else
            {
                Log.Warning("Cannot toggle Dictionary panel - LayoutViewModel not available");
            }
        }
        catch (Exception ex)
        {
            Log.Error(ex, "Failed to toggle Dictionary panel");
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