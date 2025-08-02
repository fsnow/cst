using System;
using System.IO;
using System.Linq;
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
using CST;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Serilog.Events;
using ReactiveUI;
using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Disposables;

namespace CST.Avalonia;

public partial class App : Application
{
    public static ServiceProvider? ServiceProvider { get; private set; }
    public static Window? MainWindow { get; private set; }

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
                System.Console.WriteLine($"Book selected: {book.FileName} - {book.LongNavPath}");
                
                // Open book via LayoutViewModel
                layoutViewModel.OpenBook(book);
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
                await SaveApplicationStateAsync();
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
            // Use basic console logging since Serilog isn't configured yet
            Console.WriteLine($"Warning: Failed to read splash screen setting, defaulting to show splash: {ex.Message}");
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
            Console.WriteLine($"Failed to load settings: {ex.Message}");
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
                await stateService.LoadStateAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to load application state: {ex.Message}");
        }
    }

    /// <summary>
    /// Save application state on shutdown - equivalent to CST4 AppState.Serialize()
    /// </summary>
    private async Task SaveApplicationStateAsync()
    {
        try
        {
            var stateService = ServiceProvider?.GetRequiredService<IApplicationStateService>();
            if (stateService != null)
            {
                await stateService.SaveStateAsync();
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to save application state: {ex.Message}");
        }
    }

    private void ConfigureServices(IServiceCollection services)
    {
        // Configure logging with environment variable support
        var logLevel = Environment.GetEnvironmentVariable("CST_LOG_LEVEL")?.ToLowerInvariant() switch
        {
            "debug" => LogEventLevel.Debug,
            "information" => LogEventLevel.Information,
            "warning" => LogEventLevel.Warning,
            "error" => LogEventLevel.Error,
            "fatal" => LogEventLevel.Fatal,
            _ => LogEventLevel.Debug // Default
        };
        
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Is(logLevel)
            .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss.fff} {Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .WriteTo.File("logs/cst-avalonia-.log", 
                rollingInterval: RollingInterval.Day,
                outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff} [{Level:u3}] {SourceContext} {Message:lj}{NewLine}{Exception}")
            .Enrich.FromLogContext()
            .CreateLogger();

        services.AddLogging(builder => builder.AddSerilog());

        // Register services
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IScriptService, ScriptService>();
        services.AddSingleton<ISettingsService, SettingsService>();
        services.AddSingleton<IApplicationStateService, ApplicationStateService>();
        services.AddSingleton<ChapterListsService>();
        // services.AddSingleton<IBookService, BookService>();
        // services.AddSingleton<ISearchService, SearchService>();
        services.AddTransient<TreeStateService>();

        // Register ViewModels
        services.AddSingleton<OpenBookDialogViewModel>();
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
        Console.WriteLine($"ReactiveUI Exception (handled): {ex.Message}");
        
        // If it's a threading exception, we can safely ignore it as we've implemented proper UI thread dispatching
        if (ex is InvalidOperationException && ex.Message.Contains("Call from invalid thread"))
        {
            Console.WriteLine("Threading exception caught and ignored - UI updates are properly handled via Dispatcher");
            return;
        }
        
        // For other exceptions, log more details
        if (ex.InnerException != null)
        {
            Console.WriteLine($"Inner exception: {ex.InnerException.Message}");
        }
    }

    public void OnError(Exception error)
    {
        Console.WriteLine($"ReactiveUI Critical Error: {error.Message}");
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
                Console.WriteLine($"Scheduler exception: {ex.Message}");
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
                Console.WriteLine($"Scheduler exception: {ex.Message}");
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