using ReactiveUI;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using CST.Avalonia.Services;
using System.Reactive;
using System.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;

namespace CST.Avalonia.ViewModels
{
    public class LayoutViewModel : ReactiveObject
    {
        private RootDock? _layout;
        private readonly CstDockFactory _factory;
        private bool _isBookPanelVisible = true; // Start with panel visible

        public LayoutViewModel()
        {
            _factory = new CstDockFactory();
            Layout = _factory.CreateLayout();
            _factory.InitLayout(Layout);
            _factory.InitializeHostWindows();
            
            // Initialize commands
            ToggleBookPanelCommand = ReactiveCommand.Create(ToggleBookPanel);
            ResetLayoutCommand = ReactiveCommand.Create(ResetLayout);
            ExitCommand = ReactiveCommand.Create(ExitApplication);
            AboutCommand = ReactiveCommand.Create(ShowAbout);
            
            // Debug output
            System.Console.WriteLine($"LayoutViewModel created. Layout has {Layout?.VisibleDockables?.Count ?? 0} visible dockables");
            System.Console.WriteLine($"Factory initialized with CreateHostWindow support");
        }

        public RootDock? Layout
        {
            get => _layout;
            set => this.RaiseAndSetIfChanged(ref _layout, value);
        }

        public CstDockFactory Factory => _factory;

        // Commands for menu items
        public ReactiveCommand<Unit, Unit> ToggleBookPanelCommand { get; }
        public ReactiveCommand<Unit, Unit> ResetLayoutCommand { get; }
        public ReactiveCommand<Unit, Unit> ExitCommand { get; }
        public ReactiveCommand<Unit, Unit> AboutCommand { get; }

        // Property for menu binding
        public bool IsBookPanelVisible
        {
            get => _isBookPanelVisible;
            set => this.RaiseAndSetIfChanged(ref _isBookPanelVisible, value);
        }

        public void OpenBook(CST.Book book)
        {
            _factory.OpenBook(book);
        }

        public void CloseBook(string bookId)
        {
            _factory.CloseBook(bookId);
        }

        private void ToggleBookPanel()
        {
            // Find the LeftToolDock and toggle its visibility
            var mainDock = Layout?.VisibleDockables?.FirstOrDefault(d => d.Id == "MainDock") as ProportionalDock;
            if (mainDock != null)
            {
                var leftToolDock = mainDock.VisibleDockables?.FirstOrDefault(d => d.Id == "LeftToolDock");
                if (leftToolDock != null)
                {
                    if (IsBookPanelVisible)
                    {
                        // Hide the panel
                        mainDock.VisibleDockables?.Remove(leftToolDock);
                        IsBookPanelVisible = false;
                        System.Console.WriteLine("Book panel hidden");
                    }
                    else
                    {
                        // Panel is hidden, but we need to recreate it or find it
                        System.Console.WriteLine("Book panel show requested - panel was already removed");
                        // We'll need to recreate or restore the panel
                    }
                }
                else if (!IsBookPanelVisible)
                {
                    // Panel is hidden, need to restore it
                    RestoreBookPanel(mainDock);
                }
            }
        }

        private void RestoreBookPanel(ProportionalDock mainDock)
        {
            // Recreate the book panel
            var openBookViewModel = App.ServiceProvider?.GetRequiredService<OpenBookDialogViewModel>();
            if (openBookViewModel != null)
            {
                var openBookTool = new Tool
                {
                    Id = "OpenBookTool",
                    Title = "Select a Book",
                    Context = openBookViewModel,
                    CanPin = false,
                    CanClose = false
                };

                var leftToolDock = new ToolDock
                {
                    Id = "LeftToolDock",
                    Title = "Select a Book",
                    Proportion = 0.25,
                    ActiveDockable = openBookTool,
                    VisibleDockables = _factory.CreateList<IDockable>(openBookTool),
                    CanFloat = false,
                    CanPin = false,
                    CanClose = false
                };

                // Insert at the beginning (left side)
                mainDock.VisibleDockables?.Insert(0, leftToolDock);
                IsBookPanelVisible = true;
                System.Console.WriteLine("Book panel restored");
            }
        }

        private void ResetLayout()
        {
            // Recreate the entire layout
            Layout = _factory.CreateLayout();
            _factory.InitLayout(Layout);
            IsBookPanelVisible = true;
            System.Console.WriteLine("Layout reset to default");
        }

        private void ExitApplication()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown();
            }
        }

        private void ShowAbout()
        {
            // TODO: Implement About dialog
            System.Console.WriteLine("About CST - Buddhist text reader application");
        }
    }
}