using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using ReactiveUI;
using Dock.Model.Core;
using Dock.Model.Controls;
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
        private bool _isDictionaryPanelVisible = true; // Track Dictionary panel visibility

        public LayoutViewModel()
        {
            _factory = new CstDockFactory();
            Layout = _factory.CreateLayout();
            _factory.InitLayout(Layout);
            _factory.InitializeHostWindows();
            
            // Initialize commands
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

        public bool IsDictionaryPanelVisible
        {
            get => _isDictionaryPanelVisible;
            private set => this.RaiseAndSetIfChanged(ref _isDictionaryPanelVisible, value);
        }

        // Event for notifying when panel visibility changes (for menu updates)
        public event EventHandler? PanelVisibilityChanged;

        public void OpenBook(CST.Book book, List<string>? searchTerms = null, Script? bookScript = null, string? windowId = null,
            int? docId = null, List<TermPosition>? searchPositions = null, string? initialAnchor = null,
            int? initialCurrentHitIndex = null, bool showFootnotes = true, bool showSearchTerms = true,
            ReadingPositionToken? initialPositionToken = null)
        {
            _factory.OpenBook(book, initialAnchor, bookScript, windowId, searchTerms, docId, searchPositions, initialCurrentHitIndex, showFootnotes, showSearchTerms, initialPositionToken);
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

        // ResetLayout/ResetLayoutCommand deleted (DOCK-8): bound nowhere, and if ever invoked it
        // would drop all open book VMs without Dispose() (per-tab FontService subscription leak)
        // and orphan existing floating windows against the discarded layout tree. A safe layout
        // reset needs to close books through the factory paths first; build that when the feature
        // is actually wanted.

        private void ExitApplication()
        {
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                // TryShutdown (not Shutdown) so the ShutdownRequested handler runs the graceful
                // save+dispose sequence; a bare Shutdown() would exit with no state save at all. (XCUT-1)
                desktop.TryShutdown();
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

            // Check if panel already exists in the layout (might be in a floating window)
            if (FindTool("OpenBookTool") != null)
            {
                Log.Information("[Layout] Select a Book panel already exists (possibly in floating window)");
                IsSelectBookPanelVisible = true;
                PanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Panel doesn't exist, create and add it to the existing ToolDock
            var openBookViewModel = App.ServiceProvider?.GetRequiredService<OpenBookDialogViewModel>();
            if (openBookViewModel == null)
            {
                Log.Error("[Layout] Cannot add Select a Book panel - OpenBookDialogViewModel not available");
                return;
            }

            // Get the tool container, recreating it under MainDock if it was removed (failure mode #4).
            var leftToolDock = _factory.EnsureLeftToolDock();
            if (leftToolDock == null)
            {
                Log.Error("[Layout] Cannot add Select a Book panel - MainDock unavailable.");
                return;
            }

            // The VM IS a ReactiveTool (its Id/Title/flags are set in its constructor), so add it
            // directly — exactly as CreateLayout does at startup. Wrapping it in a generic Tool { Context }
            // renders an empty panel because the view locator resolves by dockable type.
            openBookViewModel.Factory = _factory;
            _factory.AddDockable(leftToolDock, openBookViewModel);
            _factory.SetActiveDockable(openBookViewModel);
            _factory.SetFocusedDockable(leftToolDock, openBookViewModel);

            Log.Information("[Layout] Select a Book panel added to LeftToolDock");

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

        public void ToggleDictionaryPanel()
        {
            if (IsDictionaryPanelVisible)
            {
                HideDictionaryPanel();
            }
            else
            {
                ShowDictionaryPanel();
            }
        }

        public void ShowSearchPanel()
        {
            Log.Information("[Layout] ShowSearchPanel requested");

            // Check if panel already exists in the layout (might be in a floating window)
            if (FindTool("SearchTool") != null)
            {
                Log.Information("[Layout] Search panel already exists (possibly in floating window)");
                IsSearchPanelVisible = true;
                PanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            // Panel doesn't exist, create and add it to the existing ToolDock
            var searchViewModel = App.ServiceProvider?.GetRequiredService<SearchViewModel>();
            if (searchViewModel == null)
            {
                Log.Error("[Layout] Cannot add Search panel - SearchViewModel not available");
                return;
            }

            // Get the tool container, recreating it under MainDock if it was removed (failure mode #4).
            var leftToolDock = _factory.EnsureLeftToolDock();
            if (leftToolDock == null)
            {
                Log.Error("[Layout] Cannot add Search panel - MainDock unavailable.");
                return;
            }

            // The VM IS a ReactiveTool (its Id/Title/flags are set in its constructor), so add it
            // directly — exactly as CreateLayout does at startup. Wrapping it in a generic Tool { Context }
            // renders an empty panel because the view locator resolves by dockable type.
            searchViewModel.Factory = _factory;
            _factory.AddDockable(leftToolDock, searchViewModel);
            _factory.SetActiveDockable(searchViewModel);
            _factory.SetFocusedDockable(leftToolDock, searchViewModel);

            Log.Information("[Layout] Search panel added to LeftToolDock");

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

        // Ensure the Dictionary tool exists in the layout and mark it visible, recreating it under the
        // LeftToolDock if it was closed — same recreate-on-demand pattern as ShowSearchPanel. Used by the
        // View menu toggle and by Cmd+D (Look Up in Dictionary), which must be able to reopen a closed pane.
        public void ShowDictionaryPanel()
        {
            Log.Information("[Layout] ShowDictionaryPanel requested");

            if (FindTool("DictionaryTool") != null)
            {
                Log.Information("[Layout] Dictionary panel already exists (possibly in floating window)");
                IsDictionaryPanelVisible = true;
                PanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
                return;
            }

            var dictionaryViewModel = App.ServiceProvider?.GetRequiredService<DictionaryViewModel>();
            if (dictionaryViewModel == null)
            {
                Log.Error("[Layout] Cannot add Dictionary panel - DictionaryViewModel not available");
                return;
            }

            var leftToolDock = _factory.EnsureLeftToolDock();
            if (leftToolDock == null)
            {
                Log.Error("[Layout] Cannot add Dictionary panel - MainDock unavailable.");
                return;
            }

            dictionaryViewModel.Factory = _factory;
            _factory.AddDockable(leftToolDock, dictionaryViewModel);
            _factory.SetActiveDockable(dictionaryViewModel);
            _factory.SetFocusedDockable(leftToolDock, dictionaryViewModel);

            Log.Information("[Layout] Dictionary panel added to LeftToolDock");

            IsDictionaryPanelVisible = true;
            PanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        public void HideDictionaryPanel()
        {
            Log.Information("[Layout] HideDictionaryPanel requested");

            var dictionaryTool = FindTool("DictionaryTool");
            if (dictionaryTool == null)
            {
                Log.Information("[Layout] Dictionary panel already hidden");
                return;
            }

            RemoveToolFromLayout(dictionaryTool);

            IsDictionaryPanelVisible = false;
            PanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
            Log.Information("[Layout] Dictionary panel hidden");
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

        private void RemoveToolFromLayout(ITool tool)
        {
            // Try to find in main layout first
            var parentDock = FindParentDock(Layout, tool);

            // If not in main layout, search floating windows — remembering WHICH window contains it
            CstHostWindow? sourceHostWindow = null;
            if (parentDock == null && _factory is CstDockFactory factory)
            {
                foreach (var hostWindow in factory.HostWindows)
                {
                    if (hostWindow is CstHostWindow cstHostWindow && cstHostWindow.Layout != null)
                    {
                        parentDock = FindParentDock(cstHostWindow.Layout, tool);
                        if (parentDock != null)
                        {
                            sourceHostWindow = cstHostWindow;
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

                // If the parent dock is now empty, collapse what the removal left behind
                if (parentDock.VisibleDockables.Count == 0 && _factory is CstDockFactory cstFactory)
                {
                    if (sourceHostWindow != null)
                    {
                        // Close the SOURCE window if IT is now empty. The old loop closed the FIRST
                        // window in HostWindows whose layout was empty, without checking it contained
                        // parentDock — hiding a panel could close an unrelated window and leave the
                        // real one behind as a ghost. (DOCK-5)
                        if (IsLayoutEmpty(sourceHostWindow.Layout))
                        {
                            Log.Information("[Layout] Closing empty floating window {WindowId}", sourceHostWindow.Id);
                            sourceHostWindow.Close();
                            return; // Window closed, nothing left to clean up
                        }
                    }

                    // Collapse the now-empty tool dock (and any empty splits/splitters around it) via
                    // the factory's standard cleanup, which covers main + floating layouts. The old
                    // code special-cased "LeftToolDock" by removing it from a "MainDock" found among
                    // Layout's direct children — but MainDock isn't a direct child of Root and
                    // LeftToolDock's parent is LeftTools, so it never fired, leaving a dead ~25%-wide
                    // strip until an unrelated dock operation ran the cleanup. EnsureLeftToolDock
                    // recreates the dock on demand when a panel is shown again. (DOCK-5)
                    cstFactory.CleanupEmptySplits();
                }
            }
            else
            {
                Log.Warning("[Layout] Could not find parent dock for tool {ToolId}", tool.Id);
            }
        }

        private IDock? FindParentDock(IDockable? dockable, ITool targetTool)
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
            IsDictionaryPanelVisible = FindTool("DictionaryTool") != null;

            Log.Debug("[Layout] Panel visibility updated - SelectBook: {SelectBook}, Search: {Search}, Dictionary: {Dictionary}",
                IsSelectBookPanelVisible, IsSearchPanelVisible, IsDictionaryPanelVisible);

            PanelVisibilityChanged?.Invoke(this, EventArgs.Empty);
        }

        private ITool? FindTool(string toolId)
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

        private ITool? FindToolRecursive(IDockable? dockable, string toolId)
        {
            if (dockable == null) return null;

            // Check if this is the tool we're looking for (match ITool, not concrete Tool — the
            // panels are ReactiveTools, which implement ITool but do not derive from Tool).
            if (dockable is ITool tool && tool.Id == toolId)
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