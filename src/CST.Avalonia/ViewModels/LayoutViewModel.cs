using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using ReactiveUI;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using CST.Avalonia.Services;
using CST.Avalonia.Models;
using CST.Conversion;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace CST.Avalonia.ViewModels
{
    public class LayoutViewModel : ReactiveObject
    {
        private RootDock? _layout;
        private readonly CstDockFactory _factory;
        private bool _isBookPanelVisible = true; // Start with panel visible
        private bool _isSelectBookPanelVisible = true; // Track Select a Book panel visibility
        private bool _isSearchPanelVisible = true; // Track Search panel visibility

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
            Log.Debug("[Layout] LayoutViewModel created. Layout has {Count} visible dockables", 
                Layout?.VisibleDockables?.Count ?? 0);
            Log.Debug("[Layout] Factory initialized with CreateHostWindow support");
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

        // Properties for individual panel visibility tracking
        public bool IsSelectBookPanelVisible
        {
            get => _isSelectBookPanelVisible;
            private set => this.RaiseAndSetIfChanged(ref _isSelectBookPanelVisible, value);
        }

        public bool IsSearchPanelVisible
        {
            get => _isSearchPanelVisible;
            private set => this.RaiseAndSetIfChanged(ref _isSearchPanelVisible, value);
        }

        // Event for notifying when panel visibility changes (for menu updates)
        public event EventHandler? PanelVisibilityChanged;

        public void OpenBook(CST.Book book, List<string>? searchTerms = null, Script? bookScript = null, string? windowId = null,
            int? docId = null, List<TermPosition>? searchPositions = null)
        {
            _factory.OpenBook(book, null, bookScript, windowId, searchTerms, docId, searchPositions);
        }

        public void CloseBook(string bookId)
        {
            _factory.CloseBook(bookId);
        }

        public WelcomeViewModel? GetWelcomeViewModel()
        {
            return _factory.GetWelcomeViewModel();
        }

        public void ShowWelcomeScreen()
        {
            _factory.ShowWelcomeScreen();
        }

        public void HideWelcomeScreen()
        {
            _factory.HideWelcomeScreen();
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
                        Log.Debug("[Layout] Book panel hidden");
                    }
                    else
                    {
                        // Panel is hidden, but we need to recreate it or find it
                        Log.Debug("[Layout] Book panel show requested - panel was already removed");
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
                Log.Debug("[Layout] Book panel restored");
            }
        }

        private void ResetLayout()
        {
            // Recreate the entire layout
            Layout = _factory.CreateLayout();
            _factory.InitLayout(Layout);
            IsBookPanelVisible = true;
            Log.Information("[Layout] Layout reset to default");
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
            Log.Debug("[Layout] About dialog requested");
        }

        public void ToggleSelectBookPanel()
        {
            if (IsSelectBookPanelVisible)
            {
                HideSelectBookPanel();
            }
            else
            {
                ShowSelectBookPanel();
            }
        }

        public void ShowSelectBookPanel()
        {
            Log.Information("[Layout] ShowSelectBookPanel requested");

            // Check if panel already exists in the layout
            if (FindTool("OpenBookTool") != null)
            {
                Log.Information("[Layout] Select a Book panel already exists");
                IsSelectBookPanelVisible = true;
                PanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Panel doesn't exist, create and add it
            var openBookViewModel = App.ServiceProvider?.GetRequiredService<OpenBookDialogViewModel>();
            if (openBookViewModel == null)
            {
                Log.Error("[Layout] Cannot restore Select a Book panel - OpenBookDialogViewModel not available");
                return;
            }

            var openBookTool = new Tool
            {
                Id = "OpenBookTool",
                Title = "Select a Book",
                Context = openBookViewModel,
                CanPin = false,
                CanClose = false,
                CanFloat = true,   // Explicitly allow floating
                Factory = _factory
            };

            // Find the left tool dock and add the tool
            var mainDock = Layout?.VisibleDockables?.FirstOrDefault(d => d.Id == "MainDock") as ProportionalDock;
            var leftToolDock = FindToolDock(mainDock, "LeftToolDock");

            if (leftToolDock != null)
            {
                // Add to existing tool dock using Factory method for proper initialization
                _factory.AddDockable(leftToolDock, openBookTool);
                _factory.SetActiveDockable(openBookTool);
                _factory.SetFocusedDockable(leftToolDock, openBookTool);
                Log.Information("[Layout] Select a Book panel added to existing LeftToolDock");
            }
            else
            {
                // No left tool dock exists, recreate it with just the requested panel
                // Don't try to add other panels - they might be in floating windows
                var toolsToAdd = new System.Collections.Generic.List<IDockable> { openBookTool };

                var newLeftToolDock = new ToolDock
                {
                    Id = "LeftToolDock",
                    Title = "Tools",
                    ActiveDockable = openBookTool,
                    VisibleDockables = _factory.CreateList<IDockable>(toolsToAdd.ToArray()),
                    Alignment = Alignment.Left,
                    GripMode = GripMode.Visible,
                    Factory = _factory
                };

                // Set factory on all tools in the dock
                foreach (var tool in toolsToAdd)
                {
                    if (tool is Tool t)
                    {
                        t.Factory = _factory;
                    }
                }

                // Wrap ToolDock in ProportionalDock to enable docking indicators (like Notepad sample)
                var newLeftTools = new ProportionalDock
                {
                    Id = "LeftTools",
                    Proportion = 0.25,
                    Orientation = Orientation.Vertical,
                    VisibleDockables = _factory.CreateList<IDockable>(newLeftToolDock),
                    Factory = _factory
                };

                // Add wrapper to main dock using Factory method for proper initialization
                if (mainDock != null)
                {
                    // Use Factory.AddDockable for proper initialization
                    _factory.AddDockable(mainDock, newLeftTools);

                    // Move it to the beginning (left side) and add splitter
                    if (mainDock.VisibleDockables != null && mainDock.VisibleDockables.Contains(newLeftTools))
                    {
                        mainDock.VisibleDockables.Remove(newLeftTools);
                        mainDock.VisibleDockables.Insert(0, newLeftTools);

                        // Add splitter after wrapper if not already present
                        if (mainDock.VisibleDockables.Count > 1 &&
                            mainDock.VisibleDockables[1] is not ProportionalDockSplitter)
                        {
                            var splitter = new ProportionalDockSplitter
                            {
                                Id = "MainSplitter",
                                Title = "MainSplitter"
                            };
                            mainDock.VisibleDockables.Insert(1, splitter);
                            Log.Information("[Layout] Added splitter after LeftTools wrapper");
                        }
                    }

                    // Enable drag and drop for the new ToolDock
                    _factory.EnableDragAndDropForDock(newLeftToolDock);

                    // Set the newly added tool as active
                    _factory.SetActiveDockable(openBookTool);
                    _factory.SetFocusedDockable(newLeftToolDock, openBookTool);
                }

                Log.Information("[Layout] Created new LeftTools wrapper with Select a Book panel");
            }

            IsSelectBookPanelVisible = true;
            PanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        public void ToggleSearchPanel()
        {
            if (IsSearchPanelVisible)
            {
                HideSearchPanel();
            }
            else
            {
                ShowSearchPanel();
            }
        }

        public void ShowSearchPanel()
        {
            Log.Information("[Layout] ShowSearchPanel requested");

            // Check if panel already exists in the layout
            if (FindTool("SearchTool") != null)
            {
                Log.Information("[Layout] Search panel already exists");
                IsSearchPanelVisible = true;
                PanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Panel doesn't exist, create and add it
            var searchViewModel = App.ServiceProvider?.GetRequiredService<SearchViewModel>();
            if (searchViewModel == null)
            {
                Log.Error("[Layout] Cannot restore Search panel - SearchViewModel not available");
                return;
            }

            var searchTool = new Tool
            {
                Id = "SearchTool",
                Title = "Search",
                Context = searchViewModel,
                CanPin = false,
                CanClose = false,
                CanFloat = true,   // Explicitly allow floating
                Factory = _factory
            };

            // Find the left tool dock and add the tool
            var mainDock = Layout?.VisibleDockables?.FirstOrDefault(d => d.Id == "MainDock") as ProportionalDock;
            var leftToolDock = FindToolDock(mainDock, "LeftToolDock");

            if (leftToolDock != null)
            {
                // Add to existing tool dock using Factory method for proper initialization
                _factory.AddDockable(leftToolDock, searchTool);
                _factory.SetActiveDockable(searchTool);
                _factory.SetFocusedDockable(leftToolDock, searchTool);
                Log.Information("[Layout] Search panel added to existing LeftToolDock");
            }
            else
            {
                // No left tool dock exists, recreate it with just the requested panel
                // Don't try to add other panels - they might be in floating windows
                var toolsToAdd = new System.Collections.Generic.List<IDockable> { searchTool };

                var newLeftToolDock = new ToolDock
                {
                    Id = "LeftToolDock",
                    Title = "Tools",
                    ActiveDockable = searchTool,
                    VisibleDockables = _factory.CreateList<IDockable>(toolsToAdd.ToArray()),
                    Alignment = Alignment.Left,
                    GripMode = GripMode.Visible,
                    Factory = _factory
                };

                // Set factory on all tools in the dock
                foreach (var tool in toolsToAdd)
                {
                    if (tool is Tool t)
                    {
                        t.Factory = _factory;
                    }
                }

                // Wrap ToolDock in ProportionalDock to enable docking indicators (like Notepad sample)
                var newLeftTools = new ProportionalDock
                {
                    Id = "LeftTools",
                    Proportion = 0.25,
                    Orientation = Orientation.Vertical,
                    VisibleDockables = _factory.CreateList<IDockable>(newLeftToolDock),
                    Factory = _factory
                };

                // Add wrapper to main dock using Factory method for proper initialization
                if (mainDock != null)
                {
                    // Use Factory.AddDockable for proper initialization
                    _factory.AddDockable(mainDock, newLeftTools);

                    // Move it to the beginning (left side) and add splitter
                    if (mainDock.VisibleDockables != null && mainDock.VisibleDockables.Contains(newLeftTools))
                    {
                        mainDock.VisibleDockables.Remove(newLeftTools);
                        mainDock.VisibleDockables.Insert(0, newLeftTools);

                        // Add splitter after wrapper if not already present
                        if (mainDock.VisibleDockables.Count > 1 &&
                            mainDock.VisibleDockables[1] is not ProportionalDockSplitter)
                        {
                            var splitter = new ProportionalDockSplitter
                            {
                                Id = "MainSplitter",
                                Title = "MainSplitter"
                            };
                            mainDock.VisibleDockables.Insert(1, splitter);
                            Log.Information("[Layout] Added splitter after LeftTools wrapper");
                        }
                    }

                    // Enable drag and drop for the new ToolDock
                    _factory.EnableDragAndDropForDock(newLeftToolDock);

                    // Set the newly added tool as active
                    _factory.SetActiveDockable(searchTool);
                    _factory.SetFocusedDockable(newLeftToolDock, searchTool);
                }

                Log.Information("[Layout] Created new LeftTools wrapper with Search panel");
            }

            IsSearchPanelVisible = true;
            PanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        public void HideSelectBookPanel()
        {
            Log.Information("[Layout] HideSelectBookPanel requested");

            var openBookTool = FindTool("OpenBookTool");
            if (openBookTool == null)
            {
                Log.Information("[Layout] Select a Book panel already hidden");
                return;
            }

            // Remove the tool from its parent dock
            RemoveToolFromLayout(openBookTool);

            IsSelectBookPanelVisible = false;
            PanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
            Log.Information("[Layout] Select a Book panel hidden");
        }

        public void HideSearchPanel()
        {
            Log.Information("[Layout] HideSearchPanel requested");

            var searchTool = FindTool("SearchTool");
            if (searchTool == null)
            {
                Log.Information("[Layout] Search panel already hidden");
                return;
            }

            // Remove the tool from its parent dock
            RemoveToolFromLayout(searchTool);

            IsSearchPanelVisible = false;
            PanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
            Log.Information("[Layout] Search panel hidden");
        }

        private void RemoveToolFromLayout(Tool tool)
        {
            // Try to find in main layout first
            var parentDock = FindParentDock(Layout, tool);

            // If not in main layout, search floating windows
            if (parentDock == null && _factory is CstDockFactory factory)
            {
                foreach (var hostWindow in factory.HostWindows)
                {
                    if (hostWindow is CstHostWindow cstHostWindow && cstHostWindow.Layout != null)
                    {
                        parentDock = FindParentDock(cstHostWindow.Layout, tool);
                        if (parentDock != null)
                        {
                            Log.Debug("[Layout] Found tool {ToolId} in floating window {WindowId}",
                                tool.Id, cstHostWindow.Id);
                            break;
                        }
                    }
                }
            }

            if (parentDock?.VisibleDockables != null)
            {
                parentDock.VisibleDockables.Remove(tool);
                Log.Information("[Layout] Removed tool {ToolId} from parent dock {ParentId}",
                    tool.Id, parentDock.Id);

                // If the parent dock is now empty
                if (parentDock.VisibleDockables.Count == 0)
                {
                    // Check if it's in a floating window - if so, close the window
                    if (_factory is CstDockFactory cstFactory)
                    {
                        foreach (var hostWindow in cstFactory.HostWindows.ToList())
                        {
                            if (hostWindow is CstHostWindow cstHostWindow && cstHostWindow.Layout != null)
                            {
                                // Check if parentDock is contained within this window's layout
                                if (IsLayoutEmpty(cstHostWindow.Layout))
                                {
                                    Log.Information("[Layout] Closing empty floating window {WindowId}", cstHostWindow.Id);
                                    cstHostWindow.Close();
                                    return; // Window closed, we're done
                                }
                            }
                        }
                    }

                    // If we get here, it's in the main window - remove the empty ToolDock from main layout
                    if (parentDock.Id == "LeftToolDock")
                    {
                        var mainDock = Layout?.VisibleDockables?.FirstOrDefault(d => d.Id == "MainDock") as ProportionalDock;
                        if (mainDock?.VisibleDockables != null)
                        {
                            mainDock.VisibleDockables.Remove(parentDock);
                            Log.Information("[Layout] Removed empty LeftToolDock from main layout");
                        }
                    }
                }
            }
            else
            {
                Log.Warning("[Layout] Could not find parent dock for tool {ToolId}", tool.Id);
            }
        }

        private IDock? FindParentDock(IDockable? dockable, Tool targetTool)
        {
            if (dockable is IDock dock && dock.VisibleDockables != null)
            {
                // Check if this dock contains the target tool
                if (dock.VisibleDockables.Contains(targetTool))
                {
                    return dock;
                }

                // Recursively search child docks
                foreach (var child in dock.VisibleDockables)
                {
                    var found = FindParentDock(child, targetTool);
                    if (found != null) return found;
                }
            }

            return null;
        }

        public void UpdatePanelVisibility()
        {
            // Check actual visibility state in layout
            IsSelectBookPanelVisible = FindTool("OpenBookTool") != null;
            IsSearchPanelVisible = FindTool("SearchTool") != null;

            Log.Debug("[Layout] Panel visibility updated - SelectBook: {SelectBook}, Search: {Search}",
                IsSelectBookPanelVisible, IsSearchPanelVisible);

            PanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        private Tool? FindTool(string toolId)
        {
            // Search main layout first
            var tool = FindToolRecursive(Layout, toolId);
            if (tool != null) return tool;

            // Search floating windows
            if (_factory is CstDockFactory factory)
            {
                foreach (var hostWindow in factory.HostWindows)
                {
                    if (hostWindow is CstHostWindow cstHostWindow && cstHostWindow.Layout != null)
                    {
                        tool = FindToolRecursive(cstHostWindow.Layout, toolId);
                        if (tool != null)
                        {
                            Log.Debug("[Layout] Found tool {ToolId} in floating window {WindowId}",
                                toolId, cstHostWindow.Id);
                            return tool;
                        }
                    }
                }
            }

            return null;
        }

        private Tool? FindToolRecursive(IDockable? dockable, string toolId)
        {
            if (dockable == null) return null;

            // Check if this is the tool we're looking for
            if (dockable is Tool tool && tool.Id == toolId)
            {
                return tool;
            }

            // Check if this dockable has child dockables
            if (dockable is IDock dock && dock.VisibleDockables != null)
            {
                foreach (var child in dock.VisibleDockables)
                {
                    var found = FindToolRecursive(child, toolId);
                    if (found != null) return found;
                }
            }

            return null;
        }

        private ToolDock? FindToolDock(IDockable? dockable, string dockId)
        {
            if (dockable == null) return null;

            // Check if this is the tool dock we're looking for
            if (dockable is ToolDock toolDock && toolDock.Id == dockId)
            {
                return toolDock;
            }

            // Check if this dockable has child dockables
            if (dockable is IDock dock && dock.VisibleDockables != null)
            {
                foreach (var child in dock.VisibleDockables)
                {
                    var found = FindToolDock(child, dockId);
                    if (found != null) return found;
                }
            }

            return null;
        }

        private bool IsLayoutEmpty(IDockable? dockable)
        {
            if (dockable == null) return true;

            // If this is a dock, check if it has any visible dockables
            if (dockable is IDock dock)
            {
                if (dock.VisibleDockables == null || dock.VisibleDockables.Count == 0)
                {
                    return true;
                }

                // Recursively check all children
                foreach (var child in dock.VisibleDockables)
                {
                    if (!IsLayoutEmpty(child))
                    {
                        return false; // Found non-empty content
                    }
                }

                return true; // All children are empty
            }

            // If this is a leaf dockable (Tool, Document, etc.), it's not empty
            return false;
        }
    }
}