using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia;
using Avalonia.Threading;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using CST.Avalonia.ViewModels;
using CST.Avalonia.ViewModels.Dock;
using CST.Avalonia.Views;
using CST.Avalonia.Models;
using CST.Conversion;
using Microsoft.Extensions.DependencyInjection;
using Avalonia.Input;
using Serilog;

namespace CST.Avalonia.Services
{
    public class CstDockFactory : Factory
    {
        private object? _context;
        private readonly ILogger _logger;

        // Store the user's desired main dock proportions (LeftTools / MainDocumentDock)
        // These start at 0.25/0.75 but can be adjusted by the user dragging the splitter
        private double _mainDockLeftProportion = 0.25;
        private double _mainDockRightProportion = 0.75;

        public CstDockFactory()
        {
            _logger = Log.ForContext<CstDockFactory>();
        }

        public override RootDock CreateLayout()
        {
            _logger.Debug("Creating dock layout");
            
            // Get ViewModels from the service provider
            // ViewModels ARE the tools now (ReactiveTool pattern) - no wrapper needed
            var openBookTool = App.ServiceProvider?.GetRequiredService<OpenBookDialogViewModel>();
            var searchTool = App.ServiceProvider?.GetRequiredService<SearchViewModel>();

            _logger.Debug("Created tools - OpenBook: {OpenBookType}, Search: {SearchType}",
                openBookTool?.GetType().Name ?? "null", searchTool?.GetType().Name ?? "null");
            if (openBookTool != null)
            {
                _logger.Debug("OpenBookViewModel BookTree has {BookCount} items", openBookTool.BookTree.Count);
            }

            // Create the book selection tool dock (left side)
            var leftToolDock = new ToolDock
            {
                Id = "LeftToolDock",
                Title = "Tools",
                ActiveDockable = openBookTool,
                VisibleDockables = CreateList<IDockable>(openBookTool, searchTool),
                Alignment = Alignment.Left,
                GripMode = GripMode.Visible,
                CanDrag = true,
                CanDrop = true
            };

            // Wrap ToolDock in ProportionalDock to enable docking indicators
            var leftTools = new ProportionalDock
            {
                Id = "LeftTools",
                Proportion = 0.25, // 25% of width
                Orientation = Orientation.Vertical,
                IsCollapsable = false,  // Like Notepad sample
                CanDrop = true,
                VisibleDockables = CreateList<IDockable>(leftToolDock)
            };

            // Create a permanent welcome document that prevents tab area collapse
            // WelcomeViewModel IS the document - no wrapper needed (ReactiveDocument pattern)
            var welcomeDocument = new WelcomeViewModel();

            // Create document dock for book content (right side)
            var documentDock = new DocumentDock
            {
                Id = "MainDocumentDock",
                Title = "Documents",
                Proportion = 0.75, // 75% of width
                ActiveDockable = welcomeDocument,
                VisibleDockables = CreateList<IDockable>(welcomeDocument),
                CanCreateDocument = false, // Disable "+" button - books opened via "Select a Book" panel
                CanFloat = true, // Allow floating for multi-window support
                CanPin = false, // Prevent pinning
                CanClose = false, // Prevent closing the entire dock
                CanDrop = true,  // Allow dropping dockables here
                IsCollapsable = false // Prevent dock from collapsing
            };
            
            // Monitor collection changes
            if (documentDock.VisibleDockables is ObservableCollection<IDockable> observableCollection)
            {
                observableCollection.CollectionChanged += (sender, e) =>
                {
                    Log.Information("*** DOCUMENT COLLECTION CHANGED: Action={Action}, NewItems={NewCount}, RemovedItems={RemoveCount} ***", 
                        e.Action, e.NewItems?.Count ?? 0, e.OldItems?.Count ?? 0);
                    if (e.OldItems != null)
                    {
                        foreach (var item in e.OldItems)
                        {
                            Log.Information("*** REMOVED ITEM: {ItemType} {ItemId} ***", item?.GetType().Name, (item as IDockable)?.Id);

                            // Clean up application state when documents are removed
                            if (item is BookDisplayViewModel removedBookViewModel)
                            {
                                Log.Information("*** Document removed from UI - cleaning up application state: {DocumentId} ***", removedBookViewModel.Id);
                                RemoveBookWindowState(removedBookViewModel.Id);
                            }
                        }
                    }
                    if (e.NewItems != null)
                    {
                        foreach (var item in e.NewItems)
                        {
                            Log.Information("*** ADDED ITEM: {ItemType} {ItemId} ***", item?.GetType().Name, (item as IDockable)?.Id);
                            
                            // Prevent Tools and ToolDocks from being added to DocumentDock
                            // This handles the center-drop case that bypasses SplitToDock
                            if (item is Tool || item is ToolDock)
                            {
                                Log.Warning("*** Tool/ToolDock being added to DocumentDock - preventing tab docking ***");
                                // Remove it and float it instead
                                System.Threading.Tasks.Task.Run(async () =>
                                {
                                    await System.Threading.Tasks.Task.Delay(50); // Small delay to let UI update
                                    await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                                    {
                                        var dockableItem = item as IDockable;
                                        if (dockableItem != null && observableCollection.Contains(dockableItem))
                                        {
                                            observableCollection.Remove(dockableItem);
                                            // Float the tool instead of losing it
                                            if (item is IDockable dockable)
                                            {
                                                FloatDockable(dockable);
                                                Log.Information("*** Floated Tool/ToolDock instead of tab docking ***");
                                            }
                                        }
                                    });
                                });
                            }
                        }
                    }
                    
                    // SYNCHRONOUS cleanup when documents are removed - prevent empty areas from ever being visible
                    if (e.Action == NotifyCollectionChangedAction.Remove && e.OldItems?.Count > 0)
                    {
                        Log.Information("*** SYNCHRONOUS cleanup triggered by document removal ***");
                        try
                        {
                            // Run cleanup synchronously FIRST to prevent any visibility of empty areas
                            CleanupEmptySplits();
                            Log.Information("*** SYNCHRONOUS cleanup completed successfully ***");
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "*** ERROR in synchronous cleanup - scheduling async backup ***");
                        }
                        
                        // Also schedule async cleanup as backup in case synchronous cleanup missed something
                        Dispatcher.UIThread.Post(() =>
                        {
                            Log.Information("*** ASYNC backup cleanup after document removal ***");
                            CleanupEmptySplits();
                        }, DispatcherPriority.Render);
                    }
                    
                    // Check if this is the main document dock or a floating window dock
                    Log.Information("*** Main dock collection changed - checking for empty floating windows ***");
                    CheckForEmptyFloatingWindows();
                    
                    Log.Information("*** Main dock collection changed - cleaning up empty splits ***");
                    CleanupEmptySplits();
                };
            }
            
            // Monitor ActiveDockable changes to save state when user switches tabs
            if (documentDock is INotifyPropertyChanged propertyChanged)
            {
                propertyChanged.PropertyChanged += (sender, e) =>
                {
                    if (e.PropertyName == nameof(documentDock.ActiveDockable))
                    {
                        Log.Information("*** ACTIVE TAB CHANGED - Saving all book window states ***");
                        SaveAllBookWindowStates();
                    }
                };
            }

            // Create splitter between left tool dock and document dock to enable resizing
            var splitter = new ProportionalDockSplitter
            {
                Id = "MainSplitter",
                Title = "MainSplitter"
            };

            // Create main proportional dock (horizontal split) with splitter for resizing
            var mainDock = new ProportionalDock
            {
                Id = "MainDock",
                Title = "Main",
                Orientation = Orientation.Horizontal,
                IsCollapsable = false,  // Like Notepad sample
                CanDrop = true,  // Allow dropping dockables here
                VisibleDockables = CreateList<IDockable>(leftTools, splitter, documentDock)
            };

            // Create inner root dock (window layout) with pinned dockables for edge drop indicators
            var windowLayout = (RootDock)CreateRootDock();
            windowLayout.Id = "WindowLayout";
            windowLayout.Title = "Window";
            windowLayout.IsCollapsable = false;
            windowLayout.ActiveDockable = mainDock;
            windowLayout.VisibleDockables = CreateList<IDockable>(mainDock);

            // Create outer root dock (like Notepad sample structure)
            var rootDock = (RootDock)CreateRootDock();
            rootDock.Id = "Root";
            rootDock.Title = "Root";
            rootDock.IsCollapsable = false;
            rootDock.ActiveDockable = windowLayout;
            rootDock.DefaultDockable = windowLayout;
            rootDock.VisibleDockables = CreateList<IDockable>(windowLayout);

            // Set the factory on all dockables
            SetFactory(rootDock);

            _logger.Debug("Layout created - Root: {RootCount} dockables, Main: {MainCount}, LeftTool: {LeftCount}, Document: {DocCount}",
                rootDock.VisibleDockables?.Count ?? 0, mainDock.VisibleDockables?.Count ?? 0, 
                leftToolDock.VisibleDockables?.Count ?? 0, documentDock.VisibleDockables?.Count ?? 0);

            return rootDock;
        }

        private void SetFactory(IDockable dockable)
        {
            if (dockable is IDock dock)
            {
                dock.Factory = this;
                if (dock.VisibleDockables != null)
                {
                    foreach (var child in dock.VisibleDockables)
                    {
                        SetFactory(child);
                    }
                }
            }
            else if (dockable is IDockable otherDockable)
            {
                // For tools, documents, and splitters, we might need to set factory too
                if (otherDockable is Tool tool)
                {
                    tool.Factory = this;
                }
                else if (otherDockable is Document document)
                {
                    document.Factory = this;
                }
                else if (otherDockable is ReactiveDocument reactiveDocument)
                {
                    reactiveDocument.Factory = this;
                }
                else if (otherDockable is ReactiveTool reactiveTool)
                {
                    reactiveTool.Factory = this;
                }
                else if (otherDockable is ProportionalDockSplitter splitter)
                {
                    splitter.Factory = this;
                }
            }
        }

        // Required abstract members implementation - simplified for now
        public override IDictionary<IDockable, IDockableControl> VisibleDockableControls { get; } = new Dictionary<IDockable, IDockableControl>();
        public override IDictionary<IDockable, IDockableControl> TabDockableControls { get; } = new Dictionary<IDockable, IDockableControl>();
        public override IDictionary<IDockable, IDockableControl> PinnedDockableControls { get; } = new Dictionary<IDockable, IDockableControl>();
        public override IList<IDockControl> DockControls { get; } = new List<IDockControl>();
        public override ObservableCollection<IHostWindow> HostWindows { get; } = new ObservableCollection<IHostWindow>();


        public void EnableDragAndDropForDock(IDock dock)
        {
            // Enable drag and drop for this dock
            if (dock is DocumentDock documentDock)
            {
                _logger.Debug("Enabling drag/drop for DocumentDock: {DockId}", documentDock.Id);
            }
            
            // Recursively enable for child docks
            if (dock.VisibleDockables != null)
            {
                foreach (var dockable in dock.VisibleDockables)
                {
                    if (dockable is IDock childDock)
                    {
                        EnableDragAndDropForDock(childDock);
                    }
                }
            }
        }

        public void OpenBook(CST.Book book)
        {
            OpenBook(book, (string?)null);
        }

        public void OpenBook(CST.Book book, Script? bookScript)
        {
            OpenBook(book, null, bookScript);
        }

        public void OpenBook(CST.Book book, string? anchor)
        {
            OpenBook(book, anchor, null);
        }

        public void OpenBook(CST.Book book, string? anchor, Script? bookScript)
        {
            OpenBook(book, anchor, bookScript, null);
        }
        
        private static readonly object _searchOpenLock = new object();
        private static DateTime _lastSearchOpenTime = DateTime.MinValue;
        private static string? _lastSearchOpenedBook = null;
        
        public void OpenBookInNewTab(CST.Book book, List<string> searchTerms, List<TermPosition> positions)
        {
            _logger.Information("Opening book from search: {BookFile} with {SearchTermCount} search terms and {PositionCount} positions",
                book.FileName, searchTerms.Count, positions.Count);

            // Additional duplicate prevention for search book opening
            lock (_searchOpenLock)
            {
                var now = DateTime.UtcNow;
                var timeSinceLastSearchOpen = now - _lastSearchOpenTime;

                // Prevent duplicate search opens of the same book within 2 seconds
                if (book.FileName == _lastSearchOpenedBook && timeSinceLastSearchOpen.TotalMilliseconds < 2000)
                {
                    _logger.Debug("Duplicate search book open prevented: {BookFile} (opened {TimeAgo}ms ago)", book.FileName, timeSinceLastSearchOpen.TotalMilliseconds);
                    return;
                }

                _lastSearchOpenTime = now;
                _lastSearchOpenedBook = book.FileName;
            }

            // Get required services from DI container
            var scriptService = App.ServiceProvider?.GetRequiredService<IScriptService>();
            var chapterListsService = App.ServiceProvider?.GetRequiredService<ChapterListsService>();
            var settingsService = App.ServiceProvider?.GetRequiredService<ISettingsService>();
            var fontService = App.ServiceProvider?.GetRequiredService<IFontService>();

            // Create BookDisplayViewModel with proper services and script
            // Generate unique ID for search results to allow multiple instances
            var searchGuid = Guid.NewGuid();
            var windowId = $"Search_{book.FileName}_{searchGuid:N}";

            var bookDisplayViewModel = new BookDisplayViewModel(
                book,
                searchTerms,  // Pass search terms for highlighting (in IPE format)
                null,         // anchor
                chapterListsService,
                settingsService,
                fontService,
                book.DocId,   // Pass DocId for Lucene offset lookup
                positions,    // NEW: Pass positions with IsFirstTerm flags for two-color highlighting
                windowId      // Pass unique ID for search results
            );

            // Set the correct script after construction
            if (scriptService != null)
            {
                bookDisplayViewModel.BookScript = scriptService.CurrentScript;
                _logger.Debug("Set book script to: {Script} for search result", scriptService.CurrentScript);
            }

            // Add search icon to title for search results
            bookDisplayViewModel.Title = $"🔍 {bookDisplayViewModel.DisplayTitle}";

            // Search terms are already passed to BookDisplayViewModel constructor for highlighting

            // Subscribe to OpenBookRequested event for Attha/Tika button functionality
            bookDisplayViewModel!.OpenBookRequested += (linkedBook, anchorForLinked) =>
            {
                _logger.Debug("Opening linked book: {BookFile} with anchor: {Anchor}", linkedBook.FileName, anchorForLinked ?? "null");
                // Open the linked book with anchor navigation for positioning
                OpenBook(linkedBook, anchorForLinked);
            };

            _logger.Debug("Creating search document with ID: {DocumentId}", bookDisplayViewModel.Id);

            // BookDisplayViewModel is now a ReactiveDocument - add it directly to the dock
            var documentDock = FindDocumentDock();
            if (documentDock != null)
            {
                Log.Information("*** ADDING SEARCH DOCUMENT TO LAYOUT: {DocumentId} ***", bookDisplayViewModel.Id);

                // Capture current proportions before adding document (preserves user adjustments)
                CaptureMainDockProportions();

                documentDock.VisibleDockables?.Add(bookDisplayViewModel);
                documentDock.ActiveDockable = bookDisplayViewModel;
                SetFactory(bookDisplayViewModel);
                Log.Information("*** SEARCH DOCUMENT ADDED SUCCESSFULLY. Total documents: {DocumentCount} ***", documentDock.VisibleDockables?.Count ?? 0);
                _logger.Debug("Added search document to layout: {DocumentId}", bookDisplayViewModel.Id);

                // Restore user's proportions (framework may have recalculated them)
                RestoreMainDockProportions();
            }
            else
            {
                Log.Error("*** NO DOCUMENT DOCK FOUND - Cannot add search document ***");
                _logger.Error("No document dock found for search document");
            }
            
            _logger.Debug("Search book opening completed");
        }
        
        private static readonly object _regularOpenLock = new object();
        private static DateTime _lastRegularOpenTime = DateTime.MinValue;
        private static string? _lastRegularOpenedBook = null;
        
        public void OpenBook(CST.Book book, string? anchor, Script? bookScript, string? windowId,
            List<string>? searchTerms = null, int? docId = null, List<TermPosition>? searchPositions = null)
        {
            _logger.Information("Opening book: {BookFile} with anchor: {Anchor}, SearchTerms: {TermCount}, Positions: {PosCount}",
                book.FileName, anchor ?? "null", searchTerms?.Count ?? 0, searchPositions?.Count ?? 0);
            
            // Prevent duplicate opens from rapid event firing while still allowing intentional multiple copies
            // Only prevent if it's the exact same book with no specific windowId within a short timeframe
            if (windowId == null) // Only apply duplicate prevention to new opens, not state restoration
            {
                lock (_regularOpenLock)
                {
                    var now = DateTime.UtcNow;
                    var timeSinceLastOpen = now - _lastRegularOpenTime;
                    
                    // Prevent duplicate opens of the same book within 1 second (for rapid double-clicks/events)
                    if (book.FileName == _lastRegularOpenedBook && timeSinceLastOpen.TotalMilliseconds < 1000)
                    {
                        _logger.Debug("Duplicate book open prevented: {BookFile} (opened {TimeAgo}ms ago)", book.FileName, timeSinceLastOpen.TotalMilliseconds);
                        return;
                    }
                    
                    _lastRegularOpenTime = now;
                    _lastRegularOpenedBook = book.FileName;
                }
            }
            
            // Allow multiple copies of the same book to be opened
            // This is useful for comparing the same text in different scripts
            
            // Create BookDisplayViewModel for the book content with proper script service
            var scriptService = App.ServiceProvider?.GetRequiredService<IScriptService>();
            var chapterListsService = App.ServiceProvider?.GetRequiredService<ChapterListsService>();
            var settingsService = App.ServiceProvider?.GetRequiredService<ISettingsService>();
            var fontService = App.ServiceProvider?.GetRequiredService<IFontService>();

            // Pass search data if available (for state restoration with highlighting)
            var bookDisplayViewModel = new BookDisplayViewModel(book, searchTerms, anchor, chapterListsService, settingsService, fontService, docId, searchPositions);
            
            // Set the correct script after construction
            if (bookDisplayViewModel != null)
            {
                // Use provided script or fall back to current application setting
                Script targetScript = bookScript ?? scriptService?.CurrentScript ?? Script.Devanagari;
                bookDisplayViewModel.BookScript = targetScript;
                _logger.Debug("Set book script to: {ActualScript} (requested: {RequestedScript})", targetScript, bookScript?.ToString() ?? "null");
            }
            
            // Subscribe to OpenBookRequested event for Attha/Tika button functionality
            bookDisplayViewModel!.OpenBookRequested += (linkedBook, anchorForLinked) =>
            {
                _logger.Debug("Opening linked book: {BookFile} with anchor: {Anchor}", linkedBook.FileName, anchorForLinked ?? "null");
                // Open the linked book with anchor navigation for positioning
                OpenBook(linkedBook, anchorForLinked);
            };
            
            // BookDisplayViewModel is now a ReactiveDocument - use it directly (no Document wrapper)
            // Subscribe to DisplayTitle changes to update the Title for tab updates
            bookDisplayViewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(BookDisplayViewModel.DisplayTitle))
                {
                    _logger.Information("DisplayTitle changed for document {DocumentId}: {OldTitle} -> {NewTitle}",
                        bookDisplayViewModel.Id, bookDisplayViewModel.Title, bookDisplayViewModel.DisplayTitle);
                    bookDisplayViewModel.Title = bookDisplayViewModel.DisplayTitle;
                }
            };

            // Add to the document dock
            AddDocumentToLayout(bookDisplayViewModel);

            // Don't save state immediately - let tab changes trigger state saving

            // Subscribe to script changes to update state when script changes
            bookDisplayViewModel.PropertyChanged += (sender, e) =>
            {
                if (e.PropertyName == nameof(BookDisplayViewModel.BookScript))
                {
                    UpdateBookScriptInState(bookDisplayViewModel.Id, bookDisplayViewModel.BookScript);
                }
            };

            _logger.Debug("Book document created: {DocumentId} with title: {Title}", bookDisplayViewModel.Id, bookDisplayViewModel.Title);
        }

        private void SaveAllBookWindowStates()
        {
            try
            {
                var documentDock = FindDocumentDock();
                if (documentDock?.VisibleDockables == null) return;

                var activeDocument = documentDock.ActiveDockable;
                Log.Information("*** Saving all book states - Active document: {ActiveId} ***", activeDocument?.Id ?? "none");

                foreach (var dockable in documentDock.VisibleDockables)
                {
                    if (dockable is BookDisplayViewModel bookDisplayViewModel &&
                        bookDisplayViewModel.Book != null)
                    {
                        // Only the active document gets IsSelected = true
                        var isSelected = dockable == activeDocument;
                        SaveBookWindowState(bookDisplayViewModel.Book, bookDisplayViewModel, isSelected);
                        Log.Information("*** Saved state for {BookFileName} - IsSelected: {IsSelected} ***",
                            bookDisplayViewModel.Book.FileName, isSelected);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save all book window states");
            }
        }

        private void SaveBookWindowState(CST.Book book, BookDisplayViewModel bookDisplayViewModel, bool? isSelected = null)
        {
            try
            {
                // Get the book index
                var booksList = Books.Inst.ToList();
                var bookIndex = booksList.IndexOf(book);

                if (bookIndex == -1)
                {
                    Log.Warning("Could not find book index for {BookFileName}", book.FileName);
                    return;
                }

                // Get the application state service
                var stateService = App.ServiceProvider?.GetRequiredService<IApplicationStateService>();
                if (stateService == null)
                {
                    Log.Warning("ApplicationStateService not available");
                    return;
                }

                // Use provided isSelected value or determine it dynamically
                var isSelectedValue = isSelected ?? (bookDisplayViewModel == FindDocumentDock()?.ActiveDockable);

                // Create book window state using bookDisplayViewModel.Id as WindowId
                var bookWindowState = new BookWindowState
                {
                    WindowId = bookDisplayViewModel.Id,
                    BookIndex = bookIndex,
                    BookFileName = book.FileName,
                    BookScript = bookDisplayViewModel.BookScript,
                    SearchTerms = bookDisplayViewModel.SearchTerms ?? new List<string>(),
                    DocId = bookDisplayViewModel.DocId,
                    SearchPositions = bookDisplayViewModel.SearchPositions ?? new List<TermPosition>(),
                    TabIndex = 0, // TODO: Get actual tab index from dock
                    IsSelected = isSelectedValue,
                    ShowFootnotes = true, // Default for now
                    ShowSearchTerms = bookDisplayViewModel.HasSearchHighlights
                };

                // Update the state
                stateService.UpdateBookWindowState(bookWindowState);

                Log.Information("Saved book window state for {BookFileName} (index {BookIndex})", book.FileName, bookIndex);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to save book window state for {BookFileName}", book.FileName);
            }
        }

        private void UpdateBookScriptInState(string windowId, Script newScript)
        {
            try
            {
                var stateService = App.ServiceProvider?.GetRequiredService<IApplicationStateService>();
                if (stateService != null)
                {
                    stateService.UpdateBookWindowScript(windowId, newScript);
                    Log.Information("Updated book script in state for window {WindowId}: {Script}", windowId, newScript);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update book script in state for window {WindowId}", windowId);
            }
        }

        private void RemoveBookWindowState(string windowId)
        {
            try
            {
                var stateService = App.ServiceProvider?.GetRequiredService<IApplicationStateService>();
                if (stateService != null)
                {
                    stateService.RemoveBookWindowStateByWindowId(windowId);
                    Log.Information("Removed book window state for window {WindowId}", windowId);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to remove book window state for window {WindowId}", windowId);
            }
        }

        public void CloseBook(string bookId)
        {
            var dockable = FindDockable(bookId);
            if (dockable != null)
            {
                // Remove book window state before removing the document
                RemoveBookWindowState(dockable.Id);

                RemoveDocumentFromLayout(dockable);
                _logger.Debug("Closed book: {BookId}", bookId);
            }
        }

        public WelcomeViewModel? GetWelcomeViewModel()
        {
            // WelcomeViewModel IS the document now (ReactiveDocument pattern)
            var welcomeDoc = FindDockable("WelcomeDocument");
            return welcomeDoc as WelcomeViewModel;
        }

        public void ShowWelcomeScreen()
        {
            // Show the Welcome document tab as active
            var welcomeDoc = FindDockable("WelcomeDocument");
            if (welcomeDoc != null)
            {
                var documentDock = FindDocumentDock();
                if (documentDock != null)
                {
                    documentDock.ActiveDockable = welcomeDoc;
                    _logger.Debug("Welcome screen activated");
                }
            }
        }

        public void HideWelcomeScreen()
        {
            // Welcome document is always present but can be deactivated
            // by switching to another document tab
            _logger.Debug("HideWelcomeScreen called (no action needed - switch to book tab instead)");
        }
        
        private Document? FindDocument(string documentId)
        {
            if (_context is RootDock rootDock && rootDock.VisibleDockables != null)
            {
                foreach (var dockable in rootDock.VisibleDockables)
                {
                    var document = FindDocumentRecursive(dockable, documentId);
                    if (document != null) return document;
                }
            }
            return null;
        }
        
        private Document? FindDocumentRecursive(IDockable dockable, string documentId)
        {
            if (dockable is Document document && document.Id == documentId)
                return document;

            if (dockable is IDock dock && dock.VisibleDockables != null)
            {
                foreach (var child in dock.VisibleDockables)
                {
                    var found = FindDocumentRecursive(child, documentId);
                    if (found != null) return found;
                }
            }
            return null;
        }

        // Generic dockable finder - works for ReactiveDocument/ReactiveTool as well as Document/Tool
        private IDockable? FindDockable(string id)
        {
            if (_context is RootDock rootDock && rootDock.VisibleDockables != null)
            {
                foreach (var dockable in rootDock.VisibleDockables)
                {
                    var found = FindDockableRecursive(dockable, id);
                    if (found != null) return found;
                }
            }
            return null;
        }

        private IDockable? FindDockableRecursive(IDockable dockable, string id)
        {
            if (dockable.Id == id)
                return dockable;

            if (dockable is IDock dock && dock.VisibleDockables != null)
            {
                foreach (var child in dock.VisibleDockables)
                {
                    var found = FindDockableRecursive(child, id);
                    if (found != null) return found;
                }
            }
            return null;
        }
        
        private void ActivateDocument(IDockable dockable)
        {
            // Find the document dock containing this document and make it active
            var documentDock = FindDocumentDock();
            if (documentDock != null)
            {
                documentDock.ActiveDockable = dockable;
                _logger.Debug("Activated existing document: {DocumentId}", dockable.Id);
            }
        }

        private void AddDocumentToLayout(IDockable dockable)
        {
            var documentDock = FindDocumentDock();
            if (documentDock != null)
            {
                Log.Information("*** ADDING DOCUMENT TO LAYOUT: {DocumentId} ***", dockable.Id);

                // Capture current proportions before adding document (preserves user adjustments)
                CaptureMainDockProportions();

                // Debug: Check for duplicate IDs (this shouldn't happen but let's detect it)
                var existingWithSameId = documentDock.VisibleDockables?.Where(d => d.Id == dockable.Id).ToList();
                if (existingWithSameId?.Any() == true)
                {
                    Log.Error("*** ERROR: Document with ID {DocumentId} already exists {Count} times! ***",
                        dockable.Id, existingWithSameId.Count);
                    _logger.Error("Document with ID {DocumentId} already exists {Count} times", dockable.Id, existingWithSameId.Count);
                }

                documentDock.VisibleDockables?.Add(dockable);
                documentDock.ActiveDockable = dockable;
                SetFactory(dockable); // Ensure the document has the factory reference
                Log.Information("*** DOCUMENT ADDED SUCCESSFULLY. Total documents: {DocumentCount} ***", documentDock.VisibleDockables?.Count ?? 0);
                _logger.Debug("Added document to layout: {DocumentId}", dockable.Id);

                // Restore user's proportions (framework may have recalculated them)
                RestoreMainDockProportions();
            }
            else
            {
                Log.Warning("*** WARNING: Could not find document dock to add book ***");
                _logger.Warning("Could not find document dock to add book");
            }
        }
        
        private void RemoveDocumentFromLayout(IDockable dockable)
        {
            var documentDock = FindDocumentDock();
            if (documentDock != null && documentDock.VisibleDockables != null)
            {
                Log.Information("*** REMOVING DOCUMENT FROM LAYOUT: {DocumentId} ***", dockable.Id);
                var countBefore = documentDock.VisibleDockables.Count;
                documentDock.VisibleDockables.Remove(dockable);
                var countAfter = documentDock.VisibleDockables.Count;
                Log.Information("*** DOCUMENT REMOVAL: Before={CountBefore}, After={CountAfter} ***", countBefore, countAfter);

                // If this was the active document, activate another one
                if (documentDock.ActiveDockable == dockable)
                {
                    // Find the Welcome document first, then fall back to last document
                    var welcomeDoc = documentDock.VisibleDockables.FirstOrDefault(d => d.Id == "WelcomeDocument");
                    documentDock.ActiveDockable = welcomeDoc ?? documentDock.VisibleDockables.LastOrDefault();
                    Log.Information("*** ACTIVATED NEW DOCUMENT: {NewActiveDocument} ***", documentDock.ActiveDockable?.Id ?? "null");
                }
            }
        }
        
        private DocumentDock? FindDocumentDock()
        {
            if (_context is RootDock rootDock && rootDock.VisibleDockables != null)
            {
                foreach (var dockable in rootDock.VisibleDockables)
                {
                    var docDock = FindDocumentDockRecursive(dockable);
                    if (docDock != null) return docDock;
                }
            }
            return null;
        }
        
        private DocumentDock? FindDocumentDockRecursive(IDockable dockable)
        {
            if (dockable is DocumentDock documentDock)
                return documentDock;
                
            if (dockable is IDock dock && dock.VisibleDockables != null)
            {
                foreach (var child in dock.VisibleDockables)
                {
                    var found = FindDocumentDockRecursive(child);
                    if (found != null) return found;
                }
            }
            return null;
        }
        
        // Override split operations with simplified approach - let framework handle splits, then fix proportions
        public override void SplitToDock(IDock dock, IDockable dockable, DockOperation operation)
        {
            // Detailed logging for debugging drag operations
            Log.Information("*** SplitToDock called ***");
            Log.Information("  Dock: {DockType} (ID: {DockId})", dock?.GetType().Name, dock?.Id);
            Log.Information("  Dockable: {DockableType} (ID: {DockableId})", dockable?.GetType().Name, dockable?.Id);
            Log.Information("  Operation: {Operation}", operation);
            Log.Information("  Dockable CanFloat: {CanFloat}", (dockable as Document)?.CanFloat ?? false);
            
            // Prevent Tools and ToolDocks from being docked into DocumentDock as tabbed documents
            // But allow split operations (Left, Right, Top, Bottom)
            if ((dockable is Tool || dockable is ToolDock) && dock is DocumentDock)
            {
                // Check if this is a split operation (has direction) or tab operation
                var operationStr = operation.ToString();
                if (operationStr == "Fill" || string.IsNullOrEmpty(operationStr))
                {
                    // This is trying to dock as a tab - prevent it
                    Log.Warning("*** DOCKING REJECTED - Cannot dock Tool/ToolDock as tab in DocumentDock ***");
                    // Try to float it instead
                    if (dockable != null)
                    {
                        FloatDockable(dockable);
                    }
                    return; // Don't allow this operation
                }
                else
                {
                    // This is a split operation (Left, Right, etc) - allow it
                    Log.Information("*** Allowing Tool/ToolDock split operation: {Operation} ***", operationStr);
                }
            }
            
            Log.Information("*** Using framework default split behavior ***");
            
            // Let the framework handle the split
            if (dock != null && dockable != null)
            {
                base.SplitToDock(dock, dockable, operation);
            }
            else
            {
                Log.Warning("*** SplitToDock called with null parameters - dock: {Dock}, dockable: {Dockable} ***", dock != null, dockable != null);
                return;
            }
            
            // Immediately set 50/50 proportions for split operations
            var operationString = operation.ToString();
            if (operationString == "Left" || operationString == "Right" || operationString == "Top" || operationString == "Bottom")
            {
                Log.Information("*** Setting equal proportions after {Operation} split ***", operationString);
                SetEqualProportions(dock);
            }
            
            // IMMEDIATE cleanup after split operations - run at multiple priorities to ensure fast execution
            Dispatcher.UIThread.Post(() =>
            {
                Log.Information("*** IMMEDIATE Post-split cleanup (priority Render) ***");
                CleanupEmptySplits();
            }, DispatcherPriority.Render);

            Dispatcher.UIThread.Post(() =>
            {
                Log.Information("*** SECONDARY Post-split cleanup (priority Background) ***");
                CleanupEmptySplits();
            }, DispatcherPriority.Background);
            
            Log.Information("*** SplitToDock completed ***");
            
            // DEBUG: Log complete dock hierarchy after split to identify empty areas
            LogCompleteHierarchy();
        }
        
        /// <summary>
        /// DEBUG: Log the complete dock hierarchy including floating windows to identify structure issues
        /// </summary>
        private void LogCompleteHierarchy()
        {
            try
            {
                Log.Information("*** ===== COMPLETE DOCK HIERARCHY DEBUG (Main + Floating Windows) ===== ***");
                
                // Log main window hierarchy
                Log.Information("*** MAIN WINDOW HIERARCHY: ***");
                if (_context is IDock rootDock)
                {
                    LogDockHierarchyRecursive(rootDock, 0);
                }
                else
                {
                    Log.Information("*** No main window context available ***");
                }
                
                // Log all floating windows
                Log.Information("*** FLOATING WINDOWS HIERARCHY (Total: {HostWindowCount}): ***", HostWindows.Count);
                for (int i = 0; i < HostWindows.Count; i++)
                {
                    var hostWindow = HostWindows[i];
                    
                    if (hostWindow is CstHostWindow cstHostWindow)
                    {
                        Log.Information("*** FLOATING WINDOW {Index}: {WindowId} ***", i, cstHostWindow.Id);
                        
                        if (cstHostWindow.Layout is IDock floatingDock)
                        {
                            LogDockHierarchyRecursive(floatingDock, 1);
                        }
                        else
                        {
                            Log.Information("***   No dock layout ***");
                        }
                    }
                    else
                    {
                        Log.Information("*** FLOATING WINDOW {Index}: Not CstHostWindow (Type: {WindowType}) ***", i, hostWindow.GetType().Name);
                    }
                }
                
                Log.Information("*** ===== END DOCK HIERARCHY DEBUG ===== ***");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** ERROR logging dock hierarchy ***");
            }
        }
        
        /// <summary>
        /// Recursively log dock hierarchy with indentation
        /// </summary>
        private void LogDockHierarchyRecursive(IDock dock, int level)
        {
            try
            {
                string indent = new string(' ', level * 2);
                string dockInfo = $"{dock.GetType().Name} (ID: {dock.Id ?? "null"})";
                
                if (dock is DocumentDock docDock)
                {
                    var documents = docDock.VisibleDockables?.OfType<Document>().ToList() ?? new List<Document>();
                    dockInfo += $" - {docDock.VisibleDockables?.Count ?? 0} total, {documents.Count} documents";
                    if (documents.Count == 0)
                    {
                        dockInfo += " *** EMPTY DOCUMENT DOCK ***";
                    }
                }
                else if (dock is ProportionalDock propDock)
                {
                    var nonSplitters = propDock.VisibleDockables?.Where(d => !(d is ProportionalDockSplitter)).ToList() ?? new List<IDockable>();
                    dockInfo += $" - {propDock.VisibleDockables?.Count ?? 0} total, {nonSplitters.Count} non-splitters";
                    if (nonSplitters.Count == 0)
                    {
                        dockInfo += " *** EMPTY PROPORTIONAL DOCK ***";
                    }
                    else if (nonSplitters.Count == 1)
                    {
                        dockInfo += " *** SINGLE-CHILD PROPORTIONAL DOCK ***";
                    }
                }
                else if (dock is ToolDock toolDock)
                {
                    var tools = toolDock.VisibleDockables?.OfType<Tool>().ToList() ?? new List<Tool>();
                    dockInfo += $" - {toolDock.VisibleDockables?.Count ?? 0} total, {tools.Count} tools";
                }
                
                Log.Information($"*** {indent}{dockInfo} ***");
                
                // Log documents in DocumentDocks
                if (dock is DocumentDock docDockForDetails && docDockForDetails.VisibleDockables != null)
                {
                    foreach (var dockable in docDockForDetails.VisibleDockables)
                    {
                        if (dockable is Document doc)
                        {
                            Log.Information($"*** {indent}  - Document: {doc.Title} (ID: {doc.Id}) ***");
                        }
                        else if (dockable is ReactiveDocument reactiveDoc)
                        {
                            Log.Information($"*** {indent}  - ReactiveDocument: {reactiveDoc.Title} (ID: {reactiveDoc.Id}) ***");
                        }
                        else
                        {
                            Log.Information($"*** {indent}  - Other: {dockable.GetType().Name} (ID: {dockable.Id ?? "null"}) ***");
                        }
                    }
                }
                
                // Recursively log child docks
                if (dock.VisibleDockables != null)
                {
                    foreach (var dockable in dock.VisibleDockables)
                    {
                        if (dockable is IDock childDock)
                        {
                            LogDockHierarchyRecursive(childDock, level + 1);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** ERROR in LogDockHierarchyRecursive ***");
            }
        }
        
        /// <summary>
        /// Set equal proportions (50/50) for splits by finding the parent and adjusting child proportions
        /// </summary>
        private void SetEqualProportions(IDock dock)
        {
            try
            {
                Log.Information("*** SetEqualProportions called for dock: {DockType} (ID: {DockId}) ***", 
                    dock?.GetType().Name, dock?.Id);
                
                // Find the parent that contains this dock
                var parent = dock != null ? FindParentDock(dock) : null;
                if (parent is ProportionalDock proportionalParent && proportionalParent.VisibleDockables != null)
                {
                    Log.Information("*** Found ProportionalDock parent: {ParentId} with {ChildCount} dockables ***",
                        proportionalParent.Id, proportionalParent.VisibleDockables.Count);

                    // IMPORTANT: Don't adjust MainDock proportions - it should maintain 25% LeftTools / 75% DocumentDock
                    if (proportionalParent.Id == "MainDock")
                    {
                        Log.Information("*** Skipping SetEqualProportions for MainDock - preserving LeftTools (25%) / DocumentDock (75%) split ***");
                        return;
                    }

                    var nonSplitters = proportionalParent.VisibleDockables
                        .Where(d => !(d is ProportionalDockSplitter))
                        .ToList();
                        
                    if (nonSplitters.Count == 2)
                    {
                        Log.Information("*** Setting 50/50 proportions for 2 child docks ***");
                        nonSplitters[0].Proportion = 0.5;
                        nonSplitters[1].Proportion = 0.5;
                        
                        Log.Information("*** Proportions set - First: {FirstProp}, Second: {SecondProp} ***", 
                            nonSplitters[0].Proportion, nonSplitters[1].Proportion);
                    }
                    else
                    {
                        Log.Information("*** Parent has {ChildCount} non-splitter children - not setting proportions ***", 
                            nonSplitters.Count);
                    }
                }
                else
                {
                    Log.Information("*** No ProportionalDock parent found or parent has no children ***");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** Error in SetEqualProportions ***");
            }
        }
        
        /// <summary>
        /// Find the parent dock of a given dockable
        /// </summary>
        private IDock? FindParentDock(IDockable target)
        {
            if (_context is not RootDock rootDock)
                return null;
                
            return FindParentDockRecursive(rootDock, target);
        }
        
        private IDock? FindParentDockRecursive(IDock dock, IDockable target)
        {
            if (dock.VisibleDockables?.Contains(target) == true)
            {
                return dock;
            }
            
            if (dock.VisibleDockables != null)
            {
                foreach (var child in dock.VisibleDockables.OfType<IDock>())
                {
                    var found = FindParentDockRecursive(child, target);
                    if (found != null)
                        return found;
                }
            }
            
            return null;
        }
        
        /// <summary>
        /// Replace a child dockable in a parent dock
        /// </summary>
        private bool ReplaceInParent(IDock parent, IDockable oldChild, IDockable newChild)
        {
            try
            {
                if (parent.VisibleDockables == null)
                    return false;
                    
                var index = parent.VisibleDockables.IndexOf(oldChild);
                if (index < 0)
                    return false;
                    
                parent.VisibleDockables[index] = newChild;
                
                // Update active dockable if needed
                if (parent.ActiveDockable == oldChild)
                {
                    parent.ActiveDockable = newChild;
                }
                
                Log.Information("*** Replaced child at index {Index} in parent {ParentId} ***", index, parent.Id);
                return true;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** Error replacing child in parent ***");
                return false;
            }
        }
        
        
        // Override to handle floating operations
        public override void FloatDockable(IDockable dockable)
        {
            Log.Information("*** FloatDockable called for: {DockableType} (ID: {DockableId}) ***", dockable?.GetType().Name, dockable?.Id);
            Log.Information("*** Dockable CanFloat: {CanFloat} ***", dockable?.CanFloat ?? false);

            // Check if dockable can float - if not, don't proceed
            if (dockable != null && !dockable.CanFloat)
            {
                Log.Warning("*** FLOATING REJECTED - Dockable CanFloat is false ***");
                return;
            }
            
            try 
            {
                Log.Information("*** Calling base FloatDockable ***");
                if (dockable != null)
                {
                    base.FloatDockable(dockable);
                }
                else
                {
                    Log.Warning("*** FloatDockable called with null dockable ***");
                    return;
                }
                Log.Information("*** Base FloatDockable completed successfully ***");
                
                // Clean up any empty splits left behind after floating
                Log.Information("*** Post-float cleanup - checking for empty splits ***");
                CleanupEmptySplits();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** FLOATING FAILED - Exception during FloatDockable operation ***");
                Log.Error(ex, "*** This might be why tabs disappear - floating failed but document was already removed from original dock ***");

                // The document might have been removed from the original dock but the floating window creation failed
                // We need to add it back to prevent data loss
                if (dockable != null)
                {
                    Log.Information("*** Attempting to restore dockable to original dock after failed float ***");
                    try
                    {
                        AddDocumentToLayout(dockable);
                        Log.Information("*** Dockable restored to original dock ***");
                    }
                    catch (Exception restoreEx)
                    {
                        Log.Error(restoreEx, "*** CRITICAL: Failed to restore dockable after failed float - document may be lost ***");
                    }
                }
                
                // Don't rethrow - we want to handle this gracefully
            }
            
            Log.Information("*** FloatDockable completed ***");
        }
        
        // Override CreateDocumentDock to enable visual drop adorners
        public override DocumentDock CreateDocumentDock()
        {
            var documentDock = new DocumentDock();
            documentDock.CanCreateDocument = false;
            documentDock.IsCollapsable = false;
            return documentDock;
        }
        
        // Override to prevent accidental closes during drag operations
        public override void CloseDockable(IDockable dockable)
        {
            Log.Information("*** CloseDockable called for: {DockableType} (ID: {DockableId}) ***", dockable?.GetType().Name, dockable?.Id);

            // Allow closing but add logging to help debug accidental closes
            if (dockable is Document document)
            {
                Log.Information("*** Closing Document: {DocumentTitle} ***", document.Title);
            }
            else if (dockable is ReactiveDocument reactiveDocument)
            {
                Log.Information("*** Closing ReactiveDocument: {DocumentTitle} ***", reactiveDocument.Title);
            }

            if (dockable != null)
            {
                base.CloseDockable(dockable);
            }
            else
            {
                Log.Warning("*** CloseDockable called with null dockable ***");
            }
            
            // Clean up empty splits after closing a dockable
            Log.Information("*** Post-close cleanup - checking for empty splits ***");
            CleanupEmptySplits();
        }
        
        // Override SwapDockable to debug tab operations and trigger cleanup
        public override void SwapDockable(IDock dock, IDockable sourceDockable, IDockable targetDockable)
        {
            Log.Information("*** SwapDockable called - Dock: {DockId}, Source: {SourceId}, Target: {TargetId} ***", dock?.Id, sourceDockable?.Id, targetDockable?.Id);
            if (dock != null && sourceDockable != null && targetDockable != null)
            {
                base.SwapDockable(dock, sourceDockable, targetDockable);
            }
            else
            {
                Log.Warning("*** SwapDockable called with null parameters ***");
                return;
            }
            Log.Information("*** SwapDockable completed ***");
            
            // Trigger cleanup after swap operations as they can leave empty structures
            Log.Information("*** Post-swap cleanup - checking for empty splits ***");
            CleanupEmptySplits();
        }
        
        // Override MoveDockable to trigger cleanup after tab moves
        public override void MoveDockable(IDock dock, IDockable sourceDockable, IDockable targetDockable)
        {
            Log.Information("*** MoveDockable called - Dock: {DockId}, Source: {SourceId}, Target: {TargetId} ***", dock?.Id, sourceDockable?.Id, targetDockable?.Id);
            if (dock != null && sourceDockable != null && targetDockable != null)
            {
                base.MoveDockable(dock, sourceDockable, targetDockable);
            }
            else
            {
                Log.Warning("*** MoveDockable called with null parameters ***");
                return;
            }
            Log.Information("*** MoveDockable completed ***");
            
            // Trigger cleanup after move operations as they can leave empty structures
            Log.Information("*** Post-move cleanup - checking for empty splits ***");
            CleanupEmptySplits();
        }
        
        // Override AddDockable to trigger cleanup 
        public override void AddDockable(IDock dock, IDockable dockable)
        {
            Log.Information("*** AddDockable called - Dock: {DockId}, Dockable: {DockableId} ***", dock?.Id, dockable?.Id);
            if (dock != null && dockable != null)
            {
                base.AddDockable(dock, dockable);
            }
            else
            {
                Log.Warning("*** AddDockable called with null parameters ***");
                return;
            }
            Log.Information("*** AddDockable completed ***");
            
            // Trigger cleanup after add operations 
            Log.Information("*** Post-add cleanup - checking for empty splits ***");
            CleanupEmptySplits();
        }
        
        // Override RemoveDockable to trigger cleanup
        public override void RemoveDockable(IDockable dockable, bool collapse)
        {
            Log.Information("*** RemoveDockable called - Dockable: {DockableId}, Collapse: {Collapse} ***", dockable?.Id, collapse);
            if (dockable != null)
            {
                base.RemoveDockable(dockable, collapse);
            }
            else
            {
                Log.Warning("*** RemoveDockable called with null dockable ***");
                return;
            }
            Log.Information("*** RemoveDockable completed ***");
            
            // Trigger cleanup after remove operations
            Log.Information("*** Post-remove cleanup - checking for empty splits ***");
            CleanupEmptySplits();
        }
        
        
        // Initialize host window support for floating windows
        public override void InitLayout(IDockable layout)
        {
            _context = layout;

            // Set up the context locator for proper view resolution
            ContextLocator = new Dictionary<string, Func<object?>>
            {
                ["OpenBookTool"] = () => App.ServiceProvider?.GetRequiredService<OpenBookDialogViewModel>()
            };

            // Set up the default host window locator for floating windows
            DefaultHostWindowLocator = () => CreateCstHostWindow();

            base.InitLayout(layout);

            // Enable drag and drop operations
            if (layout is IDock dock)
            {
                EnableDragAndDropForDock(dock);
            }

            // Debug output
            _logger.Debug("InitLayout called - Layout type: {LayoutType}, RootDock dockables: {Count}",
                layout?.GetType().Name ?? "null",
                (layout as RootDock)?.VisibleDockables?.Count ?? 0);
        }
        
        // CreateWindowFrom - called by host window locator when floating windows are needed
        public IHostWindow CreateWindowFrom(IDockWindow? source)
        {
            Log.Information("*** CreateWindowFrom called for IDockWindow: {WindowId} ***", source?.Id);
            
            try
            {
                // Create our custom host window
                var hostWindow = CreateCstHostWindow();
                
                // Customize the window based on source
                if (source != null && hostWindow is CstHostWindow customWindow)
                {
                    customWindow.Title = $"CST - {source.Title ?? "Floating Window"}";
                    
                    if (source.Layout != null)
                    {
                        customWindow.SetLayout(source.Layout);
                        Log.Information("*** Layout set on floating window: {LayoutType} ***", source.Layout.GetType().Name);
                        
                        // Set up collection monitoring for floating window document docks
                        SetupFloatingWindowMonitoring(source.Layout);
                    }
                    
                    // Set position if specified
                    if (source.X != 0 || source.Y != 0)
                    {
                        customWindow.SetPosition(source.X, source.Y);
                        Log.Information("*** Window position set: {X}, {Y} ***", source.X, source.Y);
                    }
                    
                    // Set size if specified
                    if (source.Width > 0 && source.Height > 0)
                    {
                        customWindow.SetSize(source.Width, source.Height);
                        Log.Information("*** Window size set: {Width}x{Height} ***", source.Width, source.Height);
                    }
                }
                
                Log.Information("*** CreateWindowFrom completed successfully ***");
                return hostWindow;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** CreateWindowFrom failed, creating basic window ***");
                // Fallback to basic implementation
                return CreateCstHostWindow();
            }
        }

        // Create host window for floating documents - fallback approach
        private IHostWindow CreateCstHostWindow()
        {
            Log.Information("*** CreateCstHostWindow() called - creating floating window via fallback ***");
            try
            {
                var hostWindow = new CstHostWindow();
                hostWindow.Factory = this;
                HostWindows.Add(hostWindow);

                // Set up View menu for this floating window (macOS only)
                if (OperatingSystem.IsMacOS())
                {
                    if (Application.Current is App app)
                    {
                        app.SetupFloatingWindowMenu(hostWindow);
                        Log.Information("*** View menu setup completed for floating window ***");
                    }
                }

                Log.Information("*** Host window created successfully ***");
                Log.Information("*** Host window properties: Id={Id}, Factory={HasFactory} ***",
                    hostWindow.Id, hostWindow.Factory != null);
                Log.Information("*** Total host windows: {HostWindowCount} ***", HostWindows.Count);

                return hostWindow;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** CRITICAL: Failed to create host window ***");
                throw;
            }
        }
        

        // Initialize host windows collection for multi-window support
        public void InitializeHostWindows()
        {
            // The framework will create host windows as needed when floating occurs
            _logger.Debug("Host windows initialized for multi-window support");
        }
        
        
        // Set up monitoring for floating window document docks
        public void SetupFloatingWindowMonitoring(IDock layout)
        {
            try
            {
                if (layout is DocumentDock documentDock && documentDock.VisibleDockables is ObservableCollection<IDockable> observableCollection)
                {
                    Log.Information("*** Setting up collection monitoring for floating window document dock: {DockId} ***", documentDock.Id);
                    
                    observableCollection.CollectionChanged += (sender, e) =>
                    {
                        Log.Information("*** FLOATING WINDOW COLLECTION CHANGED: Action={Action}, NewItems={NewCount}, RemovedItems={RemoveCount} ***", 
                            e.Action, e.NewItems?.Count ?? 0, e.OldItems?.Count ?? 0);
                        
                        if (e.OldItems != null)
                        {
                            foreach (var item in e.OldItems)
                            {
                                Log.Information("*** FLOATING WINDOW REMOVED ITEM: {ItemType} {ItemId} ***", item?.GetType().Name, (item as IDockable)?.Id);

                                // Clean up application state when documents are removed from floating windows
                                if (item is BookDisplayViewModel removedBookViewModel)
                                {
                                    Log.Information("*** Document removed from floating window - cleaning up application state: {DocumentId} ***", removedBookViewModel.Id);
                                    RemoveBookWindowState(removedBookViewModel.Id);
                                }
                            }
                        }
                        if (e.NewItems != null)
                        {
                            foreach (var item in e.NewItems)
                            {
                                Log.Information("*** FLOATING WINDOW ADDED ITEM: {ItemType} {ItemId} ***", item?.GetType().Name, (item as IDockable)?.Id);
                            }
                        }
                        
                        // Check for empty floating windows after any collection change
                        Log.Information("*** Floating window collection changed - calling CheckForEmptyFloatingWindows ***");
                        CheckForEmptyFloatingWindows();
                        
                        // Clean up empty splits in floating windows after any collection change
                        Log.Information("*** Floating window collection changed - cleaning up empty splits ***");
                        CleanupEmptySplits();
                    };
                }
                
                // Recursively set up monitoring for child docks
                if (layout.VisibleDockables != null)
                {
                    foreach (var child in layout.VisibleDockables.OfType<IDock>())
                    {
                        SetupFloatingWindowMonitoring(child);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** ERROR setting up floating window monitoring ***");
            }
        }
        
        // Helper method to find DocumentDock in floating window layout hierarchy
        private DocumentDock? FindDocumentDockInLayout(IDock? layout)
        {
            if (layout == null) return null;
            
            // Direct DocumentDock
            if (layout is DocumentDock documentDock)
                return documentDock;
                
            // Search recursively in child docks
            if (layout.VisibleDockables != null)
            {
                foreach (var child in layout.VisibleDockables.OfType<IDock>())
                {
                    var found = FindDocumentDockInLayout(child);
                    if (found != null) return found;
                }
            }
            
            return null;
        }
        
        // Check for empty floating windows and close them
        private void CheckForEmptyFloatingWindows()
        {
            try
            {
                Log.Information("*** CheckForEmptyFloatingWindows called - Total host windows: {HostWindowCount} ***", HostWindows.Count);
                
                var emptyWindows = new List<CstHostWindow>();
                
                foreach (var hostWindow in HostWindows.OfType<CstHostWindow>().ToList())
                {
                    Log.Information("*** Checking host window: {WindowId} ***", hostWindow.Id);
                    
                    // Find the DocumentDock within the host window's layout hierarchy
                    var documentDock = FindDocumentDockInLayout(hostWindow.Layout);
                    
                    if (documentDock != null)
                    {
                        var totalDockables = documentDock.VisibleDockables?.Count ?? 0;
                        var hasDocuments = documentDock.VisibleDockables?.OfType<Document>().Any() ?? false;
                        var docCount = documentDock.VisibleDockables?.OfType<Document>().Count() ?? 0;
                        
                        Log.Information("*** Host window {WindowId} - Layout: {LayoutType}, DocumentDock found - Total dockables: {TotalCount}, Documents: {DocumentCount}, HasDocuments: {HasDocuments} ***", 
                            hostWindow.Id, hostWindow.Layout?.GetType().Name, totalDockables, docCount, hasDocuments);
                        
                        if (!hasDocuments)
                        {
                            Log.Information("*** EMPTY FLOATING WINDOW DETECTED: {WindowId} - scheduling for closure ***", hostWindow.Id);
                            emptyWindows.Add(hostWindow);
                        }
                    }
                    else
                    {
                        Log.Warning("*** Host window {WindowId} has layout {LayoutType} but no DocumentDock found ***", 
                            hostWindow.Id, hostWindow.Layout?.GetType().Name ?? "null");
                    }
                }
                
                // Close empty windows
                foreach (var emptyWindow in emptyWindows)
                {
                    Log.Information("*** AUTO-CLOSING EMPTY FLOATING WINDOW: {WindowId} ***", emptyWindow.Id);
                    CloseEmptyHostWindow(emptyWindow);
                }
                
                Log.Information("*** CheckForEmptyFloatingWindows completed - Closed {EmptyWindowCount} empty windows ***", emptyWindows.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** ERROR while checking for empty floating windows ***");
            }
        }
        
        /// <summary>
        /// Recursively traverse all dock layouts (main + floating windows) and clean up empty splits after drag operations
        /// </summary>
        private void CleanupEmptySplits()
        {
            try
            {
                Log.Information("*** CleanupEmptySplits called - starting iterative cleanup (Main + {FloatingCount} floating windows) ***", HostWindows.Count);
                int totalRemoved = 0;
                int iteration = 0;
                
                // Keep running cleanup until no more empty splits are found
                // This ensures that removing one empty split doesn't leave parent splits empty
                while (true)
                {
                    iteration++;
                    Log.Information("*** CleanupEmptySplits iteration {Iteration} ***", iteration);
                    
                    var emptySplits = new List<IDock>();
                    
                    // Check main window
                    if (_context is IDock rootDock && rootDock.VisibleDockables != null)
                    {
                        Log.Information("*** Checking main window for empty splits ***");
                        foreach (var dockable in rootDock.VisibleDockables)
                        {
                            if (dockable is IDock dock)
                            {
                                FindEmptySplits(dock, emptySplits);
                            }
                        }
                    }
                    
                    // Check all floating windows
                    for (int i = 0; i < HostWindows.Count; i++)
                    {
                        var hostWindow = HostWindows[i];
                        
                        if (hostWindow is CstHostWindow cstHostWindow)
                        {
                            Log.Information("*** Checking floating window {Index} ({WindowId}) for empty splits ***", i, cstHostWindow.Id);
                            
                            if (cstHostWindow.Layout is IDock floatingDock)
                            {
                                FindEmptySplits(floatingDock, emptySplits);
                            }
                            else
                            {
                                Log.Information("***   Floating window {Index} has no dock layout ***", i);
                            }
                        }
                        else
                        {
                            Log.Information("*** Floating window {Index}: Not CstHostWindow (Type: {WindowType}) ***", i, hostWindow.GetType().Name);
                        }
                    }
                    
                    Log.Information("*** Iteration {Iteration}: Found {EmptySplitCount} empty splits to clean up ***", 
                        iteration, emptySplits.Count);
                    
                    // If no empty splits found, we're done
                    if (emptySplits.Count == 0)
                    {
                        Log.Information("*** No more empty splits found - cleanup complete ***");
                        break;
                    }
                    
                    // Safety check: prevent infinite loops
                    if (iteration > 10)
                    {
                        Log.Warning("*** Too many cleanup iterations ({Iteration}) - stopping to prevent infinite loop ***", iteration);
                        break;
                    }
                    
                    // Clean up empty splits (do this in reverse order to avoid collection modification issues)
                    for (int i = emptySplits.Count - 1; i >= 0; i--)
                    {
                        var emptySplit = emptySplits[i];
                        RemoveEmptySplit(emptySplit);
                    }
                    
                    totalRemoved += emptySplits.Count;
                    Log.Information("*** Iteration {Iteration}: Removed {RemovedCount} empty splits ***", 
                        iteration, emptySplits.Count);
                }
                
                Log.Information("*** CleanupEmptySplits completed after {Iterations} iterations - Total removed: {TotalRemoved} empty splits ***", 
                    iteration, totalRemoved);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** ERROR during CleanupEmptySplits ***");
            }
        }
        
        /// <summary>
        /// Recursively find empty splits in the dock hierarchy
        /// </summary>
        private void FindEmptySplits(IDock dock, List<IDock> emptySplits)
        {
            try
            {
                if (dock?.VisibleDockables == null)
                {
                    return;
                }
                
                Log.Information("*** Examining dock: {DockType} (ID: {DockId}) with {DockableCount} dockables ***", 
                    dock.GetType().Name, dock.Id ?? "null", dock.VisibleDockables.Count);
                
                // Check if this is a ProportionalDock (split container)
                if (dock is ProportionalDock proportionalDock)
                {
                    // Count non-splitter dockables
                    var nonSplitterDockables = proportionalDock.VisibleDockables?
                        .Where(d => !(d is ProportionalDockSplitter))
                        .ToList() ?? new List<IDockable>();
                    
                    Log.Information("*** ProportionalDock {DockId} has {NonSplitterCount} non-splitter dockables ***", 
                        proportionalDock.Id ?? "null", nonSplitterDockables.Count);
                    
                    // List each dockable for debugging
                    for (int i = 0; i < nonSplitterDockables.Count; i++)
                    {
                        var dockable = nonSplitterDockables[i];
                        Log.Information("***   Child {Index}: {DockableType} (ID: {DockableId}) ***", 
                            i, dockable.GetType().Name, (dockable as IDockable)?.Id ?? "null");
                    }
                    
                    // Check for empty child docks
                    if (nonSplitterDockables.Count > 0)
                    {
                        bool allChildrenEmpty = true;
                        var emptyChildren = new List<IDock>();
                        
                        // Check ALL children and collect empty ones
                        foreach (var child in nonSplitterDockables)
                        {
                            if (child is IDock childDock)
                            {
                                bool isEmpty = IsEmptyDock(childDock);
                                Log.Information("***     Child {ChildType} (ID: {ChildId}) is empty: {IsEmpty} ***", 
                                    childDock.GetType().Name, childDock.Id ?? "null", isEmpty);
                                    
                                if (isEmpty)
                                {
                                    emptyChildren.Add(childDock);
                                }
                                else
                                {
                                    allChildrenEmpty = false;
                                }
                            }
                            else
                            {
                                // Non-dock dockable (like a document or tool) - not empty
                                Log.Information("***     Child {ChildType} is not a dock - not empty ***", 
                                    child.GetType().Name);
                                allChildrenEmpty = false;
                            }
                        }
                        
                        // Strategy 1: If ALL children are empty, mark the whole ProportionalDock for removal
                        if (allChildrenEmpty && nonSplitterDockables.Count > 0)
                        {
                            Log.Information("*** EMPTY SPLIT DETECTED: ProportionalDock {DockId} - all {ChildCount} children are empty ***", 
                                proportionalDock.Id ?? "null", nonSplitterDockables.Count);
                            emptySplits.Add(proportionalDock);
                        }
                        // Strategy 2: If some children are empty but not all, mark individual empty children
                        else if (emptyChildren.Count > 0)
                        {
                            Log.Information("*** PARTIAL EMPTY SPLIT DETECTED: ProportionalDock {DockId} - {EmptyCount} of {TotalCount} children are empty ***", 
                                proportionalDock.Id ?? "null", emptyChildren.Count, nonSplitterDockables.Count);
                            
                            // Add individual empty children to cleanup list
                            foreach (var emptyChild in emptyChildren)
                            {
                                Log.Information("*** ADDING INDIVIDUAL EMPTY CHILD FOR CLEANUP: {ChildType} (ID: {ChildId}) ***", 
                                    emptyChild.GetType().Name, emptyChild.Id ?? "null");
                                emptySplits.Add(emptyChild);
                            }
                        }
                    }
                }
                
                // Recursively check child docks
                foreach (var dockable in dock.VisibleDockables)
                {
                    if (dockable is IDock childDock)
                    {
                        FindEmptySplits(childDock, emptySplits);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** ERROR in FindEmptySplits for dock {DockId} ***", dock?.Id ?? "null");
            }
        }
        
        /// <summary>
        /// Check if a dock is empty (contains no meaningful content)
        /// </summary>
        private bool IsEmptyDock(IDock dock)
        {
            try
            {
                if (dock?.VisibleDockables == null || dock.VisibleDockables.Count == 0)
                {
                    return true;
                }
                
                // For DocumentDock, check if it has any actual documents (Document OR ReactiveDocument)
                if (dock is DocumentDock documentDock)
                {
                    var documents = documentDock.VisibleDockables?
                        .Where(d => d is Document || d is ReactiveDocument)
                        .ToList() ?? new List<IDockable>();

                    // Enhanced logging to debug the empty dock issue
                    if (documentDock.VisibleDockables?.Count > 0)
                    {
                        Log.Information("***   DocumentDock {DockId} has {Count} dockables:",
                            documentDock.Id ?? "null", documentDock.VisibleDockables.Count);
                        foreach (var dockable in documentDock.VisibleDockables)
                        {
                            Log.Information("***     - {DockableType} (ID: {DockableId})",
                                dockable.GetType().Name, dockable.Id ?? "null");
                        }
                        Log.Information("***   But {DocumentCount} are Document/ReactiveDocument", documents.Count);
                    }

                    return documents.Count == 0;
                }

                // For ToolDock, check if it has any actual tools (Tool OR ReactiveTool)
                if (dock is ToolDock toolDock)
                {
                    var tools = toolDock.VisibleDockables?
                        .Where(d => d is Tool || d is ReactiveTool)
                        .ToList() ?? new List<IDockable>();
                    return tools.Count == 0;
                }
                
                // For ProportionalDock, check if all children are empty OR if it's a redundant single-child container
                if (dock is ProportionalDock proportionalDock)
                {
                    var nonSplitterChildren = proportionalDock.VisibleDockables
                        ?.Where(d => !(d is ProportionalDockSplitter))
                        .ToList() ?? new List<IDockable>();
                    
                    if (nonSplitterChildren.Count == 0)
                    {
                        return true;
                    }
                    
                    // Check if this is a redundant single-child ProportionalDock (unnecessary nesting)
                    // IMPORTANT: Skip LeftTools - it's intentional for controlling main dock proportions
                    if (nonSplitterChildren.Count == 1 && nonSplitterChildren[0] is IDock)
                    {
                        if (proportionalDock.Id != "LeftTools")
                        {
                            Log.Information("*** ProportionalDock {DockId} is redundant - has only one child dock (unnecessary nesting) ***",
                                proportionalDock.Id ?? "null");
                            return true;
                        }
                        else
                        {
                            Log.Debug("*** Skipping redundancy check for LeftTools - it's intentional for proportion control ***");
                            return false; // LeftTools is NOT redundant
                        }
                    }
                    
                    // Note: Duplicate DocumentDock IDs after splits are normal behavior from the framework
                    // Only mark as empty if we have actual empty DocumentDocks, not just duplicates
                    
                    // All children must be empty for the ProportionalDock to be empty
                    foreach (var child in nonSplitterChildren)
                    {
                        if (child is IDock childDock)
                        {
                            if (!IsEmptyDock(childDock))
                            {
                                return false;
                            }
                        }
                        else
                        {
                            // Non-dock dockable - not empty
                            return false;
                        }
                    }
                    
                    return true; // All children are empty
                }
                
                // Default: if dock has any visible dockables, it's not empty
                return false;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** ERROR in IsEmptyDock for dock {DockId} ***", dock?.Id ?? "null");
                return false;
            }
        }
        
        /// <summary>
        /// Remove an empty split from the layout
        /// </summary>
        private void RemoveEmptySplit(IDock emptySplit)
        {
            try
            {
                Log.Information("*** Removing empty split: {DockType} (ID: {DockId}) ***", 
                    emptySplit.GetType().Name, emptySplit.Id ?? "null");
                
                // Find the parent dock
                var parent = FindParentDock(emptySplit);
                if (parent?.VisibleDockables != null && parent.VisibleDockables.Contains(emptySplit))
                {
                    // Special handling for ProportionalDock issues
                    if (emptySplit is ProportionalDock problemSplit)
                    {
                        var nonSplitterChildren = problemSplit.VisibleDockables
                            ?.Where(d => !(d is ProportionalDockSplitter))
                            .ToList() ?? new List<IDockable>();
                        
                        // Handle single-child ProportionalDock - replace with child
                        if (nonSplitterChildren.Count == 1 && nonSplitterChildren[0] is IDock onlyChild)
                        {
                            Log.Information("*** Replacing single-child ProportionalDock with its child: {ChildType} (ID: {ChildId}) ***", 
                                onlyChild.GetType().Name, onlyChild.Id ?? "null");
                            
                            // Replace the single-child ProportionalDock with its child
                            var index = parent.VisibleDockables.IndexOf(emptySplit);
                            if (index >= 0)
                            {
                                parent.VisibleDockables[index] = onlyChild;
                                
                                // Update active dockable if needed
                                if (parent.ActiveDockable == emptySplit)
                                {
                                    parent.ActiveDockable = onlyChild;
                                }
                                
                                Log.Information("*** Successfully replaced single-child ProportionalDock with child ***");
                            }
                            else
                            {
                                Log.Warning("*** Could not find index of single-child ProportionalDock in parent ***");
                                // Fallback to regular removal
                            }
                            return;
                        }
                        
                        // Normal ProportionalDock removal - no special duplicate handling needed
                        // The framework creates duplicate DocumentDock IDs during splits which is normal behavior
                        
                        // If we get here, it's a ProportionalDock without special handling needed
                        Log.Information("*** ProportionalDock has no special handling - removing completely ***");
                        parent.VisibleDockables.Remove(emptySplit);
                    }
                    else
                    {
                        Log.Information("*** Removing empty dock completely ***");
                        parent.VisibleDockables.Remove(emptySplit);
                    }
                    
                    // If parent is also a ProportionalDock, clean up splitters
                    if (parent is ProportionalDock parentProportional)
                    {
                        CleanupSplitters(parentProportional);
                    }
                    
                    Log.Information("*** Successfully processed empty split ***");
                }
                else
                {
                    Log.Warning("*** Could not find parent dock for empty split: {DockId} ***", emptySplit.Id ?? "null");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** ERROR removing empty split {DockId} ***", emptySplit?.Id ?? "null");
            }
        }
        
        /// <summary>
        /// Clean up unnecessary splitters in a ProportionalDock
        /// </summary>
        private void CleanupSplitters(ProportionalDock proportionalDock)
        {
            try
            {
                if (proportionalDock?.VisibleDockables == null)
                    return;
                
                var nonSplitterDockables = proportionalDock.VisibleDockables
                    .Where(d => !(d is ProportionalDockSplitter))
                    .ToList();
                
                Log.Debug("*** Cleaning up splitters in ProportionalDock {DockId} - {NonSplitterCount} non-splitter dockables ***", 
                    proportionalDock.Id ?? "null", nonSplitterDockables.Count);
                
                // If there's only one non-splitter dockable left, remove all splitters
                if (nonSplitterDockables.Count <= 1)
                {
                    var splitters = proportionalDock.VisibleDockables
                        .OfType<ProportionalDockSplitter>()
                        .ToList();
                    
                    foreach (var splitter in splitters)
                    {
                        Log.Debug("*** Removing unnecessary splitter from ProportionalDock {DockId} ***", 
                            proportionalDock.Id ?? "null");
                        proportionalDock.VisibleDockables.Remove(splitter);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** ERROR cleaning up splitters in ProportionalDock {DockId} ***", 
                    proportionalDock?.Id ?? "null");
            }
        }
        
        /// <summary>
        /// Find the parent dock of a given dock by searching all dock hierarchies (main + floating windows)
        /// </summary>
        private IDock? FindParentDock(IDock targetDock)
        {
            try
            {
                Log.Information("*** FindParentDock searching for parent of: {TargetDockType} (ID: {TargetDockId}) ***", 
                    targetDock?.GetType().Name, targetDock?.Id ?? "null");
                
                // Search in main window first
                if (_context is IDock rootDock && rootDock.VisibleDockables != null)
                {
                    Log.Information("*** Searching in main window hierarchy ***");
                    var parent = targetDock != null ? FindParentDockRecursive(rootDock, targetDock) : null;
                    if (parent != null)
                    {
                        Log.Information("*** Found parent in main window: {ParentType} (ID: {ParentId}) ***", 
                            parent.GetType().Name, parent.Id ?? "null");
                        return parent;
                    }
                }
                
                // Search in all floating windows
                for (int i = 0; i < HostWindows.Count; i++)
                {
                    var hostWindow = HostWindows[i];
                    
                    if (hostWindow is CstHostWindow cstHostWindow && cstHostWindow.Layout is IDock floatingDock)
                    {
                        Log.Information("*** Searching in floating window {Index} ({WindowId}) ***", i, cstHostWindow.Id);
                        var parent = targetDock != null ? FindParentDockRecursive(floatingDock, targetDock) : null;
                        if (parent != null)
                        {
                            Log.Information("*** Found parent in floating window {Index}: {ParentType} (ID: {ParentId}) ***", 
                                i, parent.GetType().Name, parent.Id ?? "null");
                            return parent;
                        }
                    }
                }
                
                Log.Warning("*** Parent dock not found in any hierarchy for: {TargetDockType} (ID: {TargetDockId}) ***", 
                    targetDock?.GetType().Name, targetDock?.Id ?? "null");
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** ERROR finding parent dock for {DockId} ***", targetDock?.Id ?? "null");
                return null;
            }
        }
        
        /// <summary>
        /// Recursively search for the parent dock
        /// </summary>
        private IDock? FindParentDockRecursive(IDock currentDock, IDock targetDock)
        {
            try
            {
                if (currentDock?.VisibleDockables == null)
                    return null;
                
                // Check if targetDock is a direct child of currentDock
                if (currentDock.VisibleDockables.Contains(targetDock))
                {
                    return currentDock;
                }
                
                // Recursively search in child docks
                foreach (var dockable in currentDock.VisibleDockables)
                {
                    if (dockable is IDock childDock)
                    {
                        var parent = FindParentDockRecursive(childDock, targetDock);
                        if (parent != null)
                        {
                            return parent;
                        }
                    }
                }
                
                return null;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** ERROR in FindParentDockRecursive for {CurrentDockId} ***", 
                    currentDock?.Id ?? "null");
                return null;
            }
        }

        /// <summary>
        /// Capture the current main dock proportions before adding documents
        /// This preserves any user adjustments to the splitter position
        /// </summary>
        private void CaptureMainDockProportions()
        {
            try
            {
                if (_context is RootDock rootDock)
                {
                    // Find MainDock
                    var mainDock = FindDockByIdRecursive(rootDock, "MainDock") as ProportionalDock;
                    if (mainDock?.VisibleDockables == null)
                    {
                        Log.Information("*** CaptureMainDockProportions: MainDock not found ***");
                        return;
                    }

                    // Find left dock - try both LeftTools (ProportionalDock wrapper) and LeftToolDock (direct ToolDock)
                    // The structure changes during app lifecycle
                    var leftDock = mainDock.VisibleDockables.FirstOrDefault(d => d.Id == "LeftTools")
                                   ?? mainDock.VisibleDockables.FirstOrDefault(d => d.Id == "LeftToolDock");
                    var documentDock = mainDock.VisibleDockables.FirstOrDefault(d => d.Id == "MainDocumentDock");

                    if (leftDock != null && documentDock != null)
                    {
                        var oldLeft = _mainDockLeftProportion;
                        var oldRight = _mainDockRightProportion;

                        _mainDockLeftProportion = leftDock.Proportion;
                        _mainDockRightProportion = documentDock.Proportion;

                        Log.Information("*** CaptureMainDockProportions: Captured Left={Left:F3} (ID: {LeftId}), Right={Right:F3} (was Left={OldLeft:F3}, Right={OldRight:F3}) ***",
                            _mainDockLeftProportion, leftDock.Id, _mainDockRightProportion, oldLeft, oldRight);
                    }
                    else
                    {
                        Log.Warning("*** CaptureMainDockProportions: Could not find left dock or MainDocumentDock (leftDock={LeftFound}, docDock={DocFound}) ***",
                            leftDock != null, documentDock != null);

                        // Debug: Log all visible dockables in MainDock to diagnose
                        Log.Information("*** CaptureMainDockProportions: MainDock has {Count} visible dockables ***",
                            mainDock.VisibleDockables.Count);
                        foreach (var dockable in mainDock.VisibleDockables)
                        {
                            Log.Information("***   - {Type} (ID: {Id}, Proportion: {Prop:F3}) ***",
                                dockable.GetType().Name, dockable.Id ?? "null", dockable.Proportion);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** ERROR in CaptureMainDockProportions ***");
            }
        }

        /// <summary>
        /// Restore the main dock proportions after adding documents
        /// This reverts any framework recalculation back to the user's chosen proportions
        /// Uses Dispatcher to delay restoration until after UI updates complete
        /// </summary>
        private void RestoreMainDockProportions()
        {
            // Schedule restoration with multiple priorities to ensure it happens after framework recalculation
            Dispatcher.UIThread.Post(() => RestoreMainDockProportionsImpl(), DispatcherPriority.Render);
            Dispatcher.UIThread.Post(() => RestoreMainDockProportionsImpl(), DispatcherPriority.Background);
            Dispatcher.UIThread.Post(() => RestoreMainDockProportionsImpl(), DispatcherPriority.Loaded);
        }

        /// <summary>
        /// Implementation of proportion restoration
        /// </summary>
        private void RestoreMainDockProportionsImpl()
        {
            try
            {
                if (_context is RootDock rootDock)
                {
                    // Find MainDock
                    var mainDock = FindDockByIdRecursive(rootDock, "MainDock") as ProportionalDock;
                    if (mainDock?.VisibleDockables == null)
                    {
                        Log.Debug("*** RestoreMainDockProportions: MainDock not found ***");
                        return;
                    }

                    // Find left dock - try both LeftTools (ProportionalDock wrapper) and LeftToolDock (direct ToolDock)
                    // The structure changes during app lifecycle
                    var leftDock = mainDock.VisibleDockables.FirstOrDefault(d => d.Id == "LeftTools")
                                   ?? mainDock.VisibleDockables.FirstOrDefault(d => d.Id == "LeftToolDock");
                    var documentDock = mainDock.VisibleDockables.FirstOrDefault(d => d.Id == "MainDocumentDock");

                    if (leftDock != null && documentDock != null)
                    {
                        var currentLeft = leftDock.Proportion;
                        var currentRight = documentDock.Proportion;

                        // Only restore if proportions have changed significantly (framework recalculated)
                        if (Math.Abs(currentLeft - _mainDockLeftProportion) > 0.01 ||
                            Math.Abs(currentRight - _mainDockRightProportion) > 0.01)
                        {
                            leftDock.Proportion = _mainDockLeftProportion;
                            documentDock.Proportion = _mainDockRightProportion;

                            Log.Information("*** RestoreMainDockProportions: Restored proportions from Left={CurrentLeft:F3} (ID: {LeftId}), Right={CurrentRight:F3} to Left={TargetLeft:F3}, Right={TargetRight:F3} ***",
                                currentLeft, leftDock.Id, currentRight, _mainDockLeftProportion, _mainDockRightProportion);
                        }
                        else
                        {
                            Log.Debug("*** RestoreMainDockProportions: Proportions unchanged (Left={CurrentLeft:F3}, Right={CurrentRight:F3}), no restore needed ***",
                                currentLeft, currentRight);
                        }
                    }
                    else
                    {
                        Log.Warning("*** RestoreMainDockProportions: Could not find left dock or MainDocumentDock (leftDock={LeftFound}, docDock={DocFound}) ***",
                            leftDock != null, documentDock != null);

                        // Debug: Log all visible dockables in MainDock to diagnose
                        Log.Information("*** RestoreMainDockProportions: MainDock has {Count} visible dockables ***",
                            mainDock.VisibleDockables.Count);
                        foreach (var dockable in mainDock.VisibleDockables)
                        {
                            Log.Information("***   - {Type} (ID: {Id}, Proportion: {Prop:F3}) ***",
                                dockable.GetType().Name, dockable.Id ?? "null", dockable.Proportion);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** ERROR in RestoreMainDockProportions ***");
            }
        }

        /// <summary>
        /// Find a dock by ID recursively in the dock hierarchy
        /// </summary>
        private IDockable? FindDockByIdRecursive(IDock dock, string dockId)
        {
            if (dock.Id == dockId)
                return dock;

            if (dock.VisibleDockables != null)
            {
                foreach (var dockable in dock.VisibleDockables)
                {
                    if (dockable.Id == dockId)
                        return dockable;

                    if (dockable is IDock childDock)
                    {
                        var found = FindDockByIdRecursive(childDock, dockId);
                        if (found != null)
                            return found;
                    }
                }
            }

            return null;
        }

        // Close an empty host window without moving documents (since it's already empty)
        private void CloseEmptyHostWindow(CstHostWindow hostWindow)
        {
            try
            {
                Log.Information("*** Closing empty host window: {WindowId} ***", hostWindow.Id);

                // Remove from our tracking first
                HostWindows.Remove(hostWindow);

                // Close the actual window
                hostWindow.Close();

                Log.Information("*** Empty host window closed successfully ***");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** ERROR closing empty host window: {WindowId} ***", hostWindow.Id);
            }
        }
        
        // Handle closing of host windows (manually closed or with remaining documents)
        public void CloseHostWindow(CstHostWindow hostWindow)
        {
            Log.Information("*** CloseHostWindow called for: {WindowId} ***", hostWindow.Id);
            
            // Move any remaining documents back to the main window
            if (hostWindow.Layout is DocumentDock floatingDock && floatingDock.VisibleDockables != null)
            {
                var mainDocumentDock = FindDocumentDock();
                if (mainDocumentDock != null)
                {
                    var documentsToMove = floatingDock.VisibleDockables.OfType<Document>().ToList();
                    
                    if (documentsToMove.Any())
                    {
                        Log.Information("*** Moving {DocumentCount} documents back to main window ***", documentsToMove.Count);
                        
                        foreach (var document in documentsToMove)
                        {
                            floatingDock.VisibleDockables.Remove(document);
                            mainDocumentDock.VisibleDockables?.Add(document);
                            mainDocumentDock.ActiveDockable = document;
                            Log.Information("*** Moved document {DocumentId} back to main window ***", document.Id);
                        }
                    }
                    else
                    {
                        Log.Information("*** No documents to move - window was already empty ***");
                    }
                }
            }
            
            HostWindows.Remove(hostWindow);
            Log.Information("*** Host window removed from tracking. Remaining host windows: {Count} ***", HostWindows.Count);

            // Update panel visibility state after removing window
            // This ensures menu checkmarks update correctly when panels were in the closed window
            if (App.MainWindow?.DataContext is LayoutViewModel layoutViewModel)
            {
                layoutViewModel.UpdatePanelVisibility();
                Log.Information("*** Panel visibility updated after window close ***");
            }
        }
        
    }
}