using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Conversion;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace DemoStateSystem;

class Program
{
    static async Task Main(string[] args)
    {
        Console.WriteLine("CST Avalonia State System Demo");
        Console.WriteLine("===============================");

        // Set up services
        var services = new ServiceCollection();
        services.AddLogging(builder => builder.AddConsole());
        services.AddSingleton<IApplicationStateService, ApplicationStateService>();
        
        var serviceProvider = services.BuildServiceProvider();
        var stateService = serviceProvider.GetRequiredService<IApplicationStateService>();

        // Demo: Create sample application state
        Console.WriteLine("\n1. Creating sample application state...");
        
        var sampleState = new ApplicationState
        {
            MainWindow = new MainWindowState
            {
                Width = 1200,
                Height = 800,
                X = 100,
                Y = 50,
                IsMaximized = false
            },
            OpenBookDialog = new OpenBookDialogState
            {
                IsVisible = true,
                Width = 900,
                Height = 700,
                TreeExpansionStates = new List<bool> { true, false, true, true, false },
                TreeVersion = 12345,
                TotalNodeCount = 5,
                SelectedBookPath = "तिपिटक (मूल)/सुत्त पिटक/दीघ निकाय"
            },
            SearchDialog = new SearchDialogState
            {
                IsVisible = false,
                SearchTerms = "dhamma nirvana",
                SearchSutta = true,
                SearchMula = true
            },
            BookWindows = new List<BookWindowState>
            {
                new BookWindowState
                {
                    BookIndex = 1,
                    BookFileName = "s0101m.mul.xml",
                    BookScript = Script.Devanagari,
                    ShowFootnotes = true,
                    SearchTerms = new List<string> { "dhamma", "buddha" },
                    TabIndex = 0,
                    IsSelected = true
                },
                new BookWindowState
                {
                    BookIndex = 5,
                    BookFileName = "s0102m.mul.xml", 
                    BookScript = Script.Latin,
                    ShowFootnotes = false,
                    TabIndex = 1,
                    IsSelected = false
                }
            },
            Preferences = new ApplicationPreferences
            {
                CurrentScript = Script.Devanagari,
                InterfaceLanguage = "en",
                RecentBooks = new List<RecentBookItem>
                {
                    new RecentBookItem
                    {
                        BookIndex = 1,
                        BookFileName = "s0101m.mul.xml",
                        DisplayName = "Brahmajāla Sutta",
                        LastOpened = DateTime.UtcNow.AddHours(-2)
                    }
                }
            }
        };

        // Update state service
        stateService.UpdateMainWindowState(sampleState.MainWindow);
        stateService.UpdateOpenBookDialogState(sampleState.OpenBookDialog);
        stateService.UpdateSearchDialogState(sampleState.SearchDialog);
        stateService.UpdatePreferences(sampleState.Preferences);

        foreach (var bookWindow in sampleState.BookWindows)
        {
            stateService.UpdateBookWindowState(bookWindow);
        }

        // Demo: Save state to JSON
        Console.WriteLine("2. Saving application state to JSON...");
        var saved = await stateService.SaveStateAsync();
        Console.WriteLine($"   Save result: {saved}");

        // Demo: Show JSON content
        var appDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "CST.Avalonia",
            "application-state.json"
        );

        if (File.Exists(appDataPath))
        {
            Console.WriteLine("\n3. Generated JSON file content:");
            Console.WriteLine("   File: " + appDataPath);
            Console.WriteLine("   " + new string('=', 60));
            
            var jsonContent = await File.ReadAllTextAsync(appDataPath);
            Console.WriteLine(jsonContent);
        }

        // Demo: Validate state
        Console.WriteLine("\n4. Validating saved state...");
        var validation = await stateService.ValidateStateAsync();
        Console.WriteLine($"   Is Valid: {validation.IsValid}");
        Console.WriteLine($"   Can Recover: {validation.CanRecover}");
        
        if (validation.Errors.Length > 0)
        {
            Console.WriteLine("   Errors:");
            foreach (var error in validation.Errors)
                Console.WriteLine($"     - {error}");
        }
        
        if (validation.Warnings.Length > 0)
        {
            Console.WriteLine("   Warnings:");
            foreach (var warning in validation.Warnings)
                Console.WriteLine($"     - {warning}");
        }

        // Demo: Clear and reload
        Console.WriteLine("\n5. Testing reload functionality...");
        await stateService.ClearStateAsync();
        Console.WriteLine("   State cleared");
        
        var reloaded = await stateService.LoadStateAsync();
        Console.WriteLine($"   Reload result: {reloaded}");
        Console.WriteLine($"   Current script: {stateService.Current.Preferences.CurrentScript}");
        Console.WriteLine($"   Book windows: {stateService.Current.BookWindows.Count}");

        // Demo: Show backup files
        Console.WriteLine("\n6. Backup files available:");
        var backups = stateService.GetBackupFilePaths();
        if (backups.Length > 0)
        {
            foreach (var backup in backups)
            {
                var info = new FileInfo(backup);
                Console.WriteLine($"   - {Path.GetFileName(backup)} ({info.LastWriteTime:yyyy-MM-dd HH:mm:ss})");
            }
        }
        else
        {
            Console.WriteLine("   No backup files found");
        }

        Console.WriteLine("\nDemo completed! Key benefits of JSON state system:");
        Console.WriteLine("- Human readable and debuggable");
        Console.WriteLine("- Automatic backups for recovery");
        Console.WriteLine("- Validation and error recovery");
        Console.WriteLine("- Version tracking for compatibility");
        Console.WriteLine("- No more mysterious binary file corruption issues!");
    }
}