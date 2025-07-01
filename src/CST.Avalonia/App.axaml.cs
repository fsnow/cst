using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Data.Core;
using Avalonia.Data.Core.Plugins;
using Avalonia.Markup.Xaml;
using CST.Avalonia.ViewModels;
using CST.Avalonia.Views;
using CST.Avalonia.Services;
using CST;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Serilog;
using Xilium.CefGlue.Avalonia;

namespace CST.Avalonia;

public partial class App : Application
{
    public static ServiceProvider? ServiceProvider { get; private set; }
    public static Window? MainWindow { get; private set; }

    public override void Initialize()
    {
        AvaloniaXamlLoader.Load(this);
    }

    public override void OnFrameworkInitializationCompleted()
    {
        // CefGlue initialization is handled in Program.cs using CefRuntimeLoader.Initialize()
        // This ensures proper initialization before the app starts

        // Configure services
        var services = new ServiceCollection();
        ConfigureServices(services);
        ServiceProvider = services.BuildServiceProvider();

        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            // Avoid duplicate validations from both Avalonia and the CommunityToolkit. 
            // More info: https://docs.avaloniaui.net/docs/guides/development-guides/data-validation#manage-validationplugins
            DisableAvaloniaDataAnnotationValidation();
            
            // Load application state before creating UI
            _ = LoadApplicationStateAsync();
            
            // Create the main window with IDE-style layout
            MainWindow = new SimpleTabbedWindow();
            
            // Create the Open Book panel and set it in the main window
            var openBookViewModel = ServiceProvider.GetRequiredService<OpenBookDialogViewModel>();
            var openBookPanel = new OpenBookPanel { DataContext = openBookViewModel };
            
            // Set the panel in the left pane of the main window
            ((SimpleTabbedWindow)MainWindow).SetOpenBookContent(openBookPanel);
            
            desktop.MainWindow = MainWindow;

            // Handle book open requests - now they open as tabs in the main window
            openBookViewModel.BookOpenRequested += book =>
            {
                System.Console.WriteLine($"Book selected: {book.FileName} - {book.LongNavPath}");
                
                // Create BookDisplayViewModel with sample search terms
                var searchTerms = new System.Collections.Generic.List<string> { "buddha", "dhamma" };
                
                // Open book as tab in main window
                ((SimpleTabbedWindow)MainWindow).OpenBook(book, searchTerms);
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
        // Configure logging
        Log.Logger = new LoggerConfiguration()
            .MinimumLevel.Information()
            .WriteTo.File("logs/cst-avalonia-.log", rollingInterval: RollingInterval.Day)
            .CreateLogger();

        services.AddLogging(builder => builder.AddSerilog());

        // Register services
        services.AddSingleton<ILocalizationService, LocalizationService>();
        services.AddSingleton<IScriptService, ScriptService>();
        services.AddSingleton<IApplicationStateService, ApplicationStateService>();
        // services.AddSingleton<IBookService, BookService>();
        // services.AddSingleton<ISearchService, SearchService>();
        services.AddTransient<TreeStateService>();

        // Register ViewModels
        services.AddTransient<OpenBookDialogViewModel>();
        // services.AddTransient<MainWindowViewModel>();
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