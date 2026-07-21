using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Recycling.Model;
using Avalonia.Threading;
using Dock.Model.Core;
using Dock.Model.Controls;
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

        // Typed references to two spine docks, set in CreateLayout, used to recreate the tool container
        // on demand (see EnsureLeftToolDock). Reference-stable: MainDock is protected and never removed.
        internal RootDock? _rootDock;
        internal ProportionalDock? _mainDock;

        // Track which books have Go To event subscriptions to prevent duplicates
        private readonly HashSet<string> _goToSubscribedBooks = new HashSet<string>();

        public CstDockFactory()
        {
            _logger = Log.ForContext<CstDockFactory>();
        }

        public override RootDock CreateLayout()
        {
            _logger.Debug("Creating dock layout");
            
            // Get ViewModels from the service provider
            // ViewModels ARE the tools now (ReactiveTool pattern) - no wrapper needed
            var openBookTool = App.ServiceProvider!.GetRequiredService<OpenBookDialogViewModel>();
            var searchTool = App.ServiceProvider!.GetRequiredService<SearchViewModel>();
            var dictionaryTool = App.ServiceProvider!.GetRequiredService<DictionaryViewModel>();

            _logger.Debug("Created tools - OpenBook: {OpenBookType}, Search: {SearchType}",
                openBookTool.GetType().Name, searchTool.GetType().Name);
            _logger.Debug("OpenBookViewModel BookTree has {BookCount} items", openBookTool.BookTree.Count);

            // Create the book selection tool dock (left side)
            var leftToolDock = new ToolDock
            {
                Id = "LeftToolDock",
                Title = "Tools",
                ActiveDockable = openBookTool,
                VisibleDockables = CreateList<IDockable>(openBookTool, searchTool, dictionaryTool),
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

            // Bring the Welcome tab forward only while real work runs (a full re-index or an XML download
            // reports progress), so it is visible instead of hidden behind a restored book tab. Routine
            // startup messages do NOT raise StartupWorkReported, so a fast startup never flashes the Welcome
            // tab over a restored book. App returns focus to the restored tab on StartupCompleted.
            // Self-limited (no-ops after CompleteStartup); defensive so a focus change can never break
            // startup. (#56)
            welcomeDocument.StartupWorkReported += () =>
                Dispatcher.UIThread.Post(() =>
                {
                    try { SetActiveDockable(welcomeDocument); }
                    catch (Exception ex) { Log.Debug(ex, "Could not focus Welcome tab on status update"); }
                });

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
                                Log.Debug("*** Document removed from UI - cleaning up application state: {DocumentId} ***", removedBookViewModel.Id);
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
                                                Log.Debug("*** Floated Tool/ToolDock instead of tab docking ***");
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
                        Log.Debug("*** SYNCHRONOUS cleanup triggered by document removal ***");
                        try
                        {
                            // Run cleanup synchronously FIRST to prevent any visibility of empty areas
                            CleanupEmptySplits();
                            Log.Debug("*** SYNCHRONOUS cleanup completed successfully ***");
                        }
                        catch (Exception ex)
                        {
                            Log.Error(ex, "*** ERROR in synchronous cleanup - scheduling async backup ***");
                        }
                        
                        // Also schedule async cleanup as backup in case synchronous cleanup missed something
                        Dispatcher.UIThread.Post(() =>
                        {
                            Log.Debug("*** ASYNC backup cleanup after document removal ***");
                            CleanupEmptySplits();
                        }, DispatcherPriority.Render);
                    }

                    // Only clean up empty splits in the main dock
                    // NOTE: We do NOT check for empty floating windows here because:
                    // - Main window tab operations (drag, close) shouldn't affect floating windows
                    // - Floating windows monitor their own collection changes (line ~1700)
                    // - CheckForEmptyFloatingWindows() during main dock changes causes floating windows
                    //   to disappear when their document references are temporarily null during cleanup
                    Log.Debug("*** Main dock collection changed - cleaning up empty splits ***");
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
                        Log.Debug("*** ACTIVE TAB CHANGED - Saving all book window states ***");
                        _ = SaveAllBookWindowStatesAsync();
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

            // Register the invariant spine by REFERENCE (see IsProtectedSpine). Only these exact
            // instances are protected from cleanup; framework-cloned docks that copy a spine id are not.
            _spineDocks.Clear();
            _spineDocks.AddRange(new IDockable[] { rootDock, windowLayout, mainDock, documentDock });
            _rootDock = rootDock;
            _mainDock = mainDock;

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
        
        // Returns TRUE when the book was actually opened, FALSE when the duplicate-suppression window
        // swallowed it — so a non-UI caller (the #187 present-tool) can't report a false success.
        public bool OpenBookInNewTab(CST.Book book, List<string> searchTerms, List<TermPosition> positions)
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
                    return false;
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
                windowId,     // Pass unique ID for search results
                this,         // Pass factory for float/unfloat operations
                initialBookScript: scriptService?.CurrentScript  // seed so the set below is a no-op (BOOK-3)
            );

            // Set the correct script after construction (a no-op given the seed; kept as a safety net)
            if (scriptService != null)
            {
                bookDisplayViewModel.BookScript = scriptService.CurrentScript;
                _logger.Debug("Set book script to: {Script} for search result", scriptService.CurrentScript);
            }

            // Add search icon to title for search results
            bookDisplayViewModel.Title = $"🔍 {bookDisplayViewModel.DisplayTitle}";

            // Prevent drag-to-float, same as the regular open path (OpenBook): dragging a tab with a
            // live CEF WebView across windows SIGSEGVs on macOS; floating goes through the button
            // paths instead. The VM constructor defaults CanFloat to true, so without this a
            // search-opened book was drag-floatable. (DOCK-1)
            bookDisplayViewModel.CanDrag = true;   // Allow tab reordering
            bookDisplayViewModel.CanFloat = false; // Prevent drag-to-float (use the float button instead)

            // Search terms are already passed to BookDisplayViewModel constructor for highlighting

            // Wire ALL per-book handlers (Go To / Attha-Tika / View Source / title sync / script state)
            // through the one shared wiring point. The old inline copy here wired only a subset and
            // pre-added the id to _goToSubscribedBooks, permanently blocking the repair mechanism from
            // completing the wiring — View Source silently no-oped and titles went stale on script
            // change for search-opened books. (DOCK-4)
            EnsureBookEventSubscription(bookDisplayViewModel);

            _logger.Debug("Creating search document with ID: {DocumentId}", bookDisplayViewModel.Id);

            // BookDisplayViewModel is now a ReactiveDocument - add it directly to the dock
            var documentDock = FindDocumentDock();
            if (documentDock != null)
            {
                Log.Debug("*** ADDING SEARCH DOCUMENT TO LAYOUT: {DocumentId} ***", bookDisplayViewModel.Id);

                // Capture current proportions before adding document (preserves user adjustments)
                CaptureMainDockProportions();

                // A null collection would make the ?.Add a silent no-op and leave us reporting success with
                // no tab; treat it as the broken-layout failure it is. (fable)
                if (documentDock.VisibleDockables is not { } searchDockables)
                {
                    Log.Error("*** Document dock has no VisibleDockables collection - cannot add search document ***");
                    return false;
                }
                searchDockables.Add(bookDisplayViewModel);
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
                return false;   // nothing was opened — don't let the caller report success (fable)
            }
            
            _logger.Debug("Search book opening completed");
            return true;
        }
        
        private static readonly object _regularOpenLock = new object();
        private static DateTime _lastRegularOpenTime = DateTime.MinValue;
        private static string? _lastRegularOpenedBook = null;
        
        // Returns TRUE when the book was actually opened, FALSE when the duplicate-suppression window
        // swallowed it — so a non-UI caller (the #187 present-tool) can't report a false success.
        public bool OpenBook(CST.Book book, string? anchor, Script? bookScript, string? windowId,
            List<string>? searchTerms = null, int? docId = null, List<TermPosition>? searchPositions = null,
            int? initialCurrentHitIndex = null, bool showFootnotes = true, bool showSearchTerms = true,
            ReadingPositionToken? initialPositionToken = null)
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
                        return false;
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

            // Use the provided script or fall back to the current application setting. Computed BEFORE
            // construction so we can seed it into the VM and avoid the double load. (BOOK-3)
            Script targetScript = bookScript ?? scriptService?.CurrentScript ?? Script.Devanagari;

            // Pass search data if available (for state restoration with highlighting)
            var bookDisplayViewModel = new BookDisplayViewModel(book, searchTerms, anchor, chapterListsService, settingsService, fontService, docId, searchPositions, null, this, initialCurrentHitIndex, targetScript, initialPositionToken);

            // No-op given the seed above (avoids the second full pipeline run); kept as a safety net.
            bookDisplayViewModel.BookScript = targetScript;
            _logger.Debug("Set book script to: {ActualScript} (requested: {RequestedScript})", targetScript, bookScript?.ToString() ?? "null");

            // #224: apply restored per-book View toggles (default on for fresh opens). Set before the View
            // attaches, so ExecutePendingRestoration applies them on the first paint (no shown-then-hidden flash).
            bookDisplayViewModel.ShowFootnotes = showFootnotes;
            bookDisplayViewModel.ShowSearchTerms = showSearchTerms;

            // Phase 1: Prevent drag-to-float for documents with CEF WebView
            // CanDrag = true allows tab reordering within same window
            // CanFloat = false prevents drag-to-float across windows (CEF crash mitigation)
            // Related: docs/research/BUTTON_BASED_FLOAT_APPROACH.md
            bookDisplayViewModel.CanDrag = true;   // Allow tab reordering
            bookDisplayViewModel.CanFloat = false; // Prevent drag-to-float (will use buttons instead)
            _logger.Debug("Book docking capabilities set: CanDrag={CanDrag}, CanFloat={CanFloat}",
                bookDisplayViewModel.CanDrag, bookDisplayViewModel.CanFloat);

            // Wire ALL per-book handlers (Go To / Attha-Tika / View Source / title sync / script state)
            // through the one shared wiring point, replacing this path's inline copy that had drifted
            // from the search path's. (DOCK-4)
            EnsureBookEventSubscription(bookDisplayViewModel);

            // Add to the document dock
            var added = AddDocumentToLayout(bookDisplayViewModel);

            // Don't save state immediately - let tab changes trigger state saving

            _logger.Debug("Book document created: {DocumentId} with title: {Title}", bookDisplayViewModel.Id, bookDisplayViewModel.Title);
            return added;   // no dock => nothing opened; don't report success (fable)
        }

        /// <summary>
        /// Opens a PDF source document for viewing.
        /// Downloads the PDF from SharePoint and displays it in a dockable tab.
        /// </summary>
        public void OpenPdf(string bookFilename, Sources.SourceType sourceType, int targetPage)
        {
            _logger.Information("Opening PDF for {BookFilename}, {SourceType}, page {Page}",
                bookFilename, sourceType, targetPage);

            // Get SharePointService from DI
            var sharePointService = App.ServiceProvider?.GetService<ISharePointService>();

            // Create PdfDisplayViewModel
            var pdfViewModel = new PdfDisplayViewModel(
                bookFilename,
                sourceType,
                targetPage,
                sharePointService,
                this
            );

            // Add to the document dock
            AddDocumentToLayout(pdfViewModel);

            _logger.Debug("PDF document created: {DocumentId} with title: {Title}", pdfViewModel.Id, pdfViewModel.Title);
        }

        public async Task SaveAllBookWindowStatesAsync()
        {
            try
            {
                var documentDock = FindDocumentDock();
                if (documentDock?.VisibleDockables == null) return;

                var activeDocument = documentDock.ActiveDockable;
                Log.Debug("*** Saving all book states - Active document: {ActiveId} ***", activeDocument?.Id ?? "none");

                // Snapshot before iterating: there is an await inside the loop, and a concurrent dock
                // drag/cleanup can modify VisibleDockables during it, throwing "Collection was modified".
                foreach (var dockable in documentDock.VisibleDockables.ToList())
                {
                    if (dockable is BookDisplayViewModel bookDisplayViewModel &&
                        bookDisplayViewModel.Book != null)
                    {
                        // The snapshot can contain books closed since we started; don't touch their
                        // (possibly disposed) WebView state. (DOCK-3)
                        if (!documentDock.VisibleDockables.Contains(dockable))
                        {
                            Log.Debug("*** Skipping state save for {BookFileName} - closed before capture ***",
                                bookDisplayViewModel.Book.FileName);
                            continue;
                        }

                        // Capture final position before saving (ensures very latest scroll position is saved)
                        await bookDisplayViewModel.CaptureCurrentPositionAsync();

                        // The await above can span a tab close: RemoveBookWindowStateByWindowId has
                        // already run for that book, and saving now would re-ADD the removed state
                        // (UpdateBookWindowState is add-if-missing) — a ghost tab on next launch.
                        // Re-check against the LIVE collection, not the snapshot. (DOCK-3)
                        if (!documentDock.VisibleDockables.Contains(dockable))
                        {
                            Log.Debug("*** Skipping state save for {BookFileName} - closed during save loop ***",
                                bookDisplayViewModel.Book.FileName);
                            continue;
                        }

                        // Only the active document gets IsSelected = true
                        var isSelected = dockable == activeDocument;
                        SaveBookWindowState(bookDisplayViewModel.Book, bookDisplayViewModel, isSelected);
                        Log.Debug("*** Saved state for {BookFileName} - IsSelected: {IsSelected} ***",
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

                // Get the cached scroll position anchor for restoration on next startup
                // The anchor is updated every 200ms by the scroll timer and persists in the ViewModel
                string? currentAnchor = bookDisplayViewModel.LastCapturedAnchor;
                Log.Information("Using cached scroll position anchor for {BookFileName}: {Anchor}",
                    book.FileName, currentAnchor ?? "null");

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
                    CurrentAnchor = currentAnchor, // Save scroll position for restoration (coarse fallback)
                    ReadingPosition = bookDisplayViewModel.LastPositionToken, // #434 exact reading position (preferred)
                    CurrentHitIndex = bookDisplayViewModel.CurrentHitIndex, // Save which search hit was active
                    TotalHits = bookDisplayViewModel.TotalHits,
                    TabIndex = 0, // TODO: Get actual tab index from dock
                    IsSelected = isSelectedValue,
                    ShowFootnotes = bookDisplayViewModel.ShowFootnotes,
                    ShowSearchTerms = bookDisplayViewModel.ShowSearchTerms
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

        // #224: persist the per-book Footnotes / search-highlight toggles when they change.
        private void UpdateBookViewFlagsInState(string windowId, bool showFootnotes, bool showSearchTerms)
        {
            try
            {
                var stateService = App.ServiceProvider?.GetRequiredService<IApplicationStateService>();
                if (stateService != null)
                {
                    stateService.UpdateBookWindowViewFlags(windowId, showFootnotes, showSearchTerms);
                    Log.Information("Updated book view flags in state for window {WindowId}: Footnotes={Foot} SearchTerms={Search}", windowId, showFootnotes, showSearchTerms);
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Failed to update book view flags in state for window {WindowId}", windowId);
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

        // Returns TRUE when the document was actually added; FALSE when there is no document dock (e.g. the
        // layout spine is still being built), so callers can report an honest failure. (fable / #187)
        private bool AddDocumentToLayout(IDockable dockable)
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

                // Same guard: a null collection must not be reported as a successful add. (fable)
                if (documentDock.VisibleDockables is not { } dockables)
                {
                    Log.Error("*** Document dock has no VisibleDockables collection - cannot add document ***");
                    return false;
                }
                dockables.Add(dockable);
                documentDock.ActiveDockable = dockable;
                SetFactory(dockable); // Ensure the document has the factory reference
                Log.Information("*** DOCUMENT ADDED SUCCESSFULLY. Total documents: {DocumentCount} ***", documentDock.VisibleDockables?.Count ?? 0);
                _logger.Debug("Added document to layout: {DocumentId}", dockable.Id);

                // Restore user's proportions (framework may have recalculated them)
                RestoreMainDockProportions();
                return true;
            }

            Log.Warning("*** WARNING: Could not find document dock to add book ***");
            _logger.Warning("Could not find document dock to add book");
            return false;
        }
        
        private void RemoveDocumentFromLayout(IDockable dockable)
        {
            var documentDock = FindDocumentDock();
            if (documentDock != null && documentDock.VisibleDockables != null)
            {
                Log.Debug("*** REMOVING DOCUMENT FROM LAYOUT: {DocumentId} ***", dockable.Id);
                var countBefore = documentDock.VisibleDockables.Count;
                documentDock.VisibleDockables.Remove(dockable);
                var countAfter = documentDock.VisibleDockables.Count;
                Log.Debug("*** DOCUMENT REMOVAL: Before={CountBefore}, After={CountAfter} ***", countBefore, countAfter);

                // If this was the active document, activate another one
                if (documentDock.ActiveDockable == dockable)
                {
                    // Find the Welcome document first, then fall back to last document
                    var welcomeDoc = documentDock.VisibleDockables.FirstOrDefault(d => d.Id == "WelcomeDocument");
                    documentDock.ActiveDockable = welcomeDoc ?? documentDock.VisibleDockables.LastOrDefault();
                    Log.Debug("*** ACTIVATED NEW DOCUMENT: {NewActiveDocument} ***", documentDock.ActiveDockable?.Id ?? "null");
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
                    Log.Debug("*** Allowing Tool/ToolDock split operation: {Operation} ***", operationStr);
                }
            }
            
            Log.Debug("*** Using framework default split behavior ***");
            
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
                Log.Debug("*** Setting equal proportions after {Operation} split ***", operationString);
                SetEqualProportions(dock);
            }
            
            // IMMEDIATE cleanup after split operations - run at multiple priorities to ensure fast execution
            Dispatcher.UIThread.Post(() =>
            {
                Log.Debug("*** IMMEDIATE Post-split cleanup (priority Render) ***");
                CleanupEmptySplits();
            }, DispatcherPriority.Render);

            Dispatcher.UIThread.Post(() =>
            {
                Log.Debug("*** SECONDARY Post-split cleanup (priority Background) ***");
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
                Log.Debug("*** ===== COMPLETE DOCK HIERARCHY DEBUG (Main + Floating Windows) ===== ***");
                
                // Log main window hierarchy
                Log.Debug("*** MAIN WINDOW HIERARCHY: ***");
                if (_context is IDock rootDock)
                {
                    LogDockHierarchyRecursive(rootDock, 0);
                }
                else
                {
                    Log.Debug("*** No main window context available ***");
                }
                
                // Log all floating windows
                Log.Debug("*** FLOATING WINDOWS HIERARCHY (Total: {HostWindowCount}): ***", HostWindows.Count);
                for (int i = 0; i < HostWindows.Count; i++)
                {
                    var hostWindow = HostWindows[i];
                    
                    if (hostWindow is CstHostWindow cstHostWindow)
                    {
                        Log.Debug("*** FLOATING WINDOW {Index}: {WindowId} ***", i, cstHostWindow.Id);
                        
                        if (cstHostWindow.Layout is IDock floatingDock)
                        {
                            LogDockHierarchyRecursive(floatingDock, 1);
                        }
                        else
                        {
                            Log.Debug("***   No dock layout ***");
                        }
                    }
                    else
                    {
                        Log.Debug("*** FLOATING WINDOW {Index}: Not CstHostWindow (Type: {WindowType}) ***", i, hostWindow.GetType().Name);
                    }
                }
                
                Log.Debug("*** ===== END DOCK HIERARCHY DEBUG ===== ***");
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
                
                Log.Debug($"*** {indent}{dockInfo} ***");
                
                // Log documents in DocumentDocks
                if (dock is DocumentDock docDockForDetails && docDockForDetails.VisibleDockables != null)
                {
                    foreach (var dockable in docDockForDetails.VisibleDockables)
                    {
                        if (dockable is Document doc)
                        {
                            Log.Debug($"*** {indent}  - Document: {doc.Title} (ID: {doc.Id}) ***");
                        }
                        else if (dockable is ReactiveDocument reactiveDoc)
                        {
                            Log.Debug($"*** {indent}  - ReactiveDocument: {reactiveDoc.Title} (ID: {reactiveDoc.Id}) ***");
                        }
                        else
                        {
                            Log.Debug($"*** {indent}  - Other: {dockable.GetType().Name} (ID: {dockable.Id ?? "null"}) ***");
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
                Log.Debug("*** SetEqualProportions called for dock: {DockType} (ID: {DockId}) ***", 
                    dock?.GetType().Name, dock?.Id);
                
                // Find the parent that contains this dock
                var parent = dock != null ? FindParentDock(dock) : null;
                if (parent is ProportionalDock proportionalParent && proportionalParent.VisibleDockables != null)
                {
                    Log.Debug("*** Found ProportionalDock parent: {ParentId} with {ChildCount} dockables ***",
                        proportionalParent.Id, proportionalParent.VisibleDockables.Count);

                    // IMPORTANT: Don't adjust MainDock proportions - it should maintain 25% LeftTools / 75% DocumentDock
                    if (proportionalParent.Id == "MainDock")
                    {
                        Log.Debug("*** Skipping SetEqualProportions for MainDock - preserving LeftTools (25%) / DocumentDock (75%) split ***");
                        return;
                    }

                    var nonSplitters = proportionalParent.VisibleDockables
                        .Where(d => !(d is ProportionalDockSplitter))
                        .ToList();
                        
                    if (nonSplitters.Count == 2)
                    {
                        Log.Debug("*** Setting 50/50 proportions for 2 child docks ***");
                        nonSplitters[0].Proportion = 0.5;
                        nonSplitters[1].Proportion = 0.5;
                        
                        Log.Debug("*** Proportions set - First: {FirstProp}, Second: {SecondProp} ***", 
                            nonSplitters[0].Proportion, nonSplitters[1].Proportion);
                    }
                    else
                    {
                        Log.Debug("*** Parent has {ChildCount} non-splitter children - not setting proportions ***", 
                            nonSplitters.Count);
                    }
                }
                else
                {
                    Log.Debug("*** No ProportionalDock parent found or parent has no children ***");
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
            Log.Debug("*** FloatDockable called for: {DockableType} (ID: {DockableId}) ***", dockable?.GetType().Name, dockable?.Id);
            Log.Debug("*** Dockable CanFloat: {CanFloat} ***", dockable?.CanFloat ?? false);

            // Check if dockable can float - if not, don't proceed
            if (dockable != null && !dockable.CanFloat)
            {
                Log.Warning("*** FLOATING REJECTED - Dockable CanFloat is false ***");
                return;
            }
            
            try 
            {
                Log.Debug("*** Calling base FloatDockable ***");
                if (dockable != null)
                {
                    base.FloatDockable(dockable);
                }
                else
                {
                    Log.Warning("*** FloatDockable called with null dockable ***");
                    return;
                }
                Log.Debug("*** Base FloatDockable completed successfully ***");
                
                // Clean up any empty splits left behind after floating
                Log.Debug("*** Post-float cleanup - checking for empty splits ***");
                CleanupEmptySplits();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** FLOATING FAILED - Exception during FloatDockable operation ***");
                Log.Error(ex, "*** This might be why tabs disappear - floating failed but document was already removed from original dock ***");

                // The dockable might have been removed from the original dock but the floating window
                // creation failed - we need to add it back to prevent data loss. Restore by TYPE:
                // AddDocumentToLayout puts things into MainDocumentDock, and a Tool restored there is
                // the exact forbidden state the collection-changed guard fights (it would remove the
                // tool and call FloatDockable again - a 50ms retry loop ending with the tool nowhere).
                // Tools go back to the left tool dock instead. (DOCK-7)
                if (dockable != null)
                {
                    Log.Debug("*** Attempting to restore dockable to original dock after failed float ***");
                    try
                    {
                        if (dockable is ITool)
                        {
                            var toolDock = EnsureLeftToolDock();
                            if (toolDock != null)
                            {
                                AddDockable(toolDock, dockable);
                                SetActiveDockable(dockable);
                                Log.Debug("*** Tool restored to left tool dock ***");
                            }
                            else
                            {
                                Log.Error("*** CRITICAL: No tool dock available to restore tool after failed float ***");
                            }
                        }
                        else
                        {
                            AddDocumentToLayout(dockable);
                            Log.Debug("*** Document restored to original dock ***");
                        }
                    }
                    catch (Exception restoreEx)
                    {
                        Log.Error(restoreEx, "*** CRITICAL: Failed to restore dockable after failed float - document may be lost ***");
                    }
                }
                
                // Don't rethrow - we want to handle this gracefully
            }
            
            Log.Debug("*** FloatDockable completed ***");
        }

        /// <summary>
        /// Float a book window by creating a brand new ViewModel instance
        /// This forces ControlRecycling to create a fresh View with no CEF baggage
        /// Related: docs/research/BUTTON_BASED_FLOAT_APPROACH.md
        /// </summary>
        public async void FloatDockableWithoutRecycling(BookDisplayViewModel oldVm)
        {
            Log.Information("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Log.Information("FloatDockableWithoutRecycling called for: {BookFile}, OldInstance: {OldInstanceId}",
                oldVm.Book.FileName, oldVm.Id);

            try
            {
                // Step 1: Query WebView for actual current scroll position
                string? currentAnchor = null;
                ReadingPositionToken? positionToken = null;   // #434: exact reading position across the re-parent
                if (oldVm.BookDisplayControl != null)
                {
                    Log.Information("Querying WebView for current scroll position...");
                    currentAnchor = await oldVm.BookDisplayControl.GetCurrentParagraphAnchorAsync();
                    positionToken = await oldVm.BookDisplayControl.GetCurrentPositionTokenAsync();
                    Log.Information("Current scroll position anchor: {Anchor}", currentAnchor ?? "none");
                }
                else
                {
                    // Fallback to last known VRI anchor / rolling token if the WebView isn't available.
                    currentAnchor = oldVm.CurrentVriAnchor;
                    positionToken = oldVm.LastPositionToken;
                    Log.Warning("BookDisplayControl not available, using fallback VRI anchor: {Anchor}", currentAnchor ?? "none");
                }

                // Step 2: Capture all other state from old ViewModel
                var book = oldVm.Book;
                var searchTerms = oldVm.SearchTerms?.ToList();
                var searchPositions = oldVm.SearchPositions?.ToList();
                var bookScript = oldVm.BookScript;
                var docId = oldVm.DocId;
                var currentHitIndex = oldVm.CurrentHitIndex;
                var totalHits = oldVm.TotalHits;
                var showFootnotes = oldVm.ShowFootnotes;       // #224: carry View toggles across float/unfloat
                var showSearchTerms = oldVm.ShowSearchTerms;
                var title = oldVm.Title;  // preserve the exact tab title (current script + any prefix) across recreation

                Log.Information("Captured state: SearchTerms={Count}, Script={Script}, HitIndex={Hit}/{Total}, Anchor={Anchor}",
                    searchTerms?.Count ?? 0, bookScript, currentHitIndex, totalHits, currentAnchor ?? "none");

                // Step 2: Remove old ViewModel state from ApplicationState BEFORE removing from dock
                RemoveBookWindowState(oldVm.Id);
                Log.Information("Removed old ViewModel state from ApplicationState");

                // Step 3: Remove old ViewModel from dock (auto-disposes View and WebView)
                if (oldVm.Owner is IDock currentDock)
                {
                    RemoveDockable(oldVm, false);
                    Log.Information("Removed old ViewModel from dock");
                }

                // The old ViewModel is permanently replaced by newVm below; its state was already
                // captured above, so release its subscriptions and GoTo id now (avoids the leak) —
                // and shut down + evict its recycled View: "dispose-before-move" is the documented
                // discipline, but nothing implemented it (the PrepareForFloat/PrepareForUnfloat
                // handler is dead code for books), so the detached View's live CEF browser survived
                // in the app-lifetime ControlRecycling cache on every float/unfloat. (#193)
                _goToSubscribedBooks.Remove(oldVm.Id);
                DisposeAndEvictRecycledView(oldVm);
                oldVm.Dispose();

                // Step 4: Create brand new ViewModel with fresh GUID (NO windowId parameter!)
                var chapterListsService = App.ServiceProvider?.GetService<ChapterListsService>();
                var settingsService = App.ServiceProvider?.GetService<ISettingsService>();
                var fontService = App.ServiceProvider?.GetService<IFontService>();

                var newVm = new BookDisplayViewModel(
                    book: book,
                    searchTerms: searchTerms,
                    initialAnchor: currentAnchor,  // Restore scroll position
                    chapterListsService: chapterListsService,
                    settingsService: settingsService,
                    fontService: fontService,
                    docId: docId,
                    searchPositions: searchPositions,
                    windowId: null,  // CRITICAL: null = generates fresh GUID
                    dockFactory: this,
                    initialBookScript: bookScript,  // seed so the set below is a no-op (BOOK-3)
                    initialPositionToken: positionToken  // #434: restore the exact reading position across float/unfloat
                );

                // Restore additional state (BookScript set is a no-op given the seed; kept as a safety net)
                newVm.BookScript = bookScript;
                newVm.CurrentHitIndex = currentHitIndex;
                newVm.TotalHits = totalHits;
                newVm.ShowFootnotes = showFootnotes;
                newVm.ShowSearchTerms = showSearchTerms;
                newVm.IsFloating = true;
                // Setting BookScript doesn't refresh the dockable Title (the tab binds to Title, not
                // DisplayTitle), so carry the old tab title over — else it reverts to Devanagari (#6).
                newVm.Title = title;

                Log.Information("Created new ViewModel with fresh GUID: {NewInstanceId}", newVm.Id);

                // Subscribe to events for the new instance
                EnsureBookEventSubscription(newVm);

                // Step 5: Add new ViewModel to main dock first (FloatDockable requires it to have an Owner)
                var mainDocDock = FindDocumentDock();
                if (mainDocDock == null)
                {
                    Log.Error("Cannot float - main document dock not found");
                    return;
                }

                AddDockable(mainDocDock, newVm);
                Log.Information("Added new ViewModel to main dock");

                // Step 6: Float the new ViewModel (creates fresh View in floating window)
                newVm.CanFloat = true;
                base.FloatDockable(newVm);
                newVm.CanFloat = false;  // Restore to prevent drag-to-float

                // A programmatic (button) float has no pointer position, so Dock lands the host window at the
                // monitor boundary — which can be a different, possibly powered-off monitor (it landed at the
                // left edge of the secondary screen). Reposition it onto the main window's screen. (#float-disappear)
                PlaceAndRevealFloatingWindow(newVm);

                Log.Information("Float operation completed - new instance in floating window");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CRITICAL: Float operation failed for book: {BookFile}", oldVm.Book.FileName);
            }

            Log.Information("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        }

        // Place a just-button-floated host window near the main window, CLAMPED to the main window's screen, so
        // it never lands on another (possibly powered-off) monitor. A programmatic (button) float gives Dock no
        // pointer position, so it defaults to a monitor boundary — on a multi-monitor setup that can be a second
        // screen. This only repositions the already-shown window (no WebView is moved → the re-parent hard rule
        // is unaffected) and brings it to front. The drag-to-float path is unaffected (Dock places it at the
        // drop point). (#float-disappear)
        private void PlaceAndRevealFloatingWindow(BookDisplayViewModel floatedVm)
        {
            try
            {
                var floatDock = floatedVm.Owner as IDock;
                if (floatDock == null) return; // no owning dock (shouldn't happen post-float) → don't risk a null==null host match (fable)
                CstHostWindow? host = null;
                int hostCount = 0;
                foreach (var hw in HostWindows)
                {
                    if (hw is CstHostWindow chw)
                    {
                        hostCount++;
                        if (host == null && FindDocumentDockInLayout(chw.Layout) == floatDock) host = chw;
                    }
                }
                if (host == null) return;

                var main = CST.Avalonia.App.MainWindow;
                if (main == null) { host.Activate(); return; }

                // The screen the MAIN window is on (proven #430 pattern: WorkingArea contains the main position).
                global::Avalonia.Platform.Screen? screen = null;
                var all = host.Screens?.All;
                if (all != null)
                    foreach (var s in all)
                        if (s.WorkingArea.Contains(main.Position)) { screen = s; break; }
                screen ??= host.Screens?.Primary;

                // Cascade near the main window; clamp inside that screen's work area.
                int cascade = 30 * Math.Max(0, hostCount - 1);
                int x = main.Position.X + 40 + cascade;
                int y = main.Position.Y + 40 + cascade;
                if (screen != null)
                {
                    var wa = screen.WorkingArea;                 // device px
                    double scale = screen.Scaling <= 0 ? 1.0 : screen.Scaling;
                    int w = (int)(host.Width * scale);           // Width/Height are logical
                    int h = (int)(host.Height * scale);
                    x = Math.Max(wa.X, Math.Min(x, wa.X + wa.Width - w));
                    y = Math.Max(wa.Y, Math.Min(y, wa.Y + wa.Height - h));
                }
                host.Position = new PixelPoint(x, y);
                host.Activate();
                Log.Information("Placed floating window on the main window's screen at {Pos}", host.Position);
            }
            catch (Exception ex) { Log.Warning(ex, "Failed to place/reveal new floating window"); }
        }

        /// <summary>
        /// Unfloat a book window by creating a brand new ViewModel instance
        /// This forces ControlRecycling to create a fresh View with no CEF baggage
        /// Related: docs/research/BUTTON_BASED_FLOAT_APPROACH.md
        /// </summary>
        public async void UnfloatDockableWithoutRecycling(BookDisplayViewModel oldVm)
        {
            Log.Information("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
            Log.Information("UnfloatDockableWithoutRecycling called for: {BookFile}, OldInstance: {OldInstanceId}",
                oldVm.Book.FileName, oldVm.Id);

            try
            {
                // Find main document dock
                var mainDocDock = FindDocumentDock();
                if (mainDocDock == null)
                {
                    Log.Error("Cannot unfloat - main document dock not found");
                    return;
                }

                // Step 1: Query WebView for actual current scroll position
                string? currentAnchor = null;
                ReadingPositionToken? positionToken = null;   // #434: exact reading position across the re-parent
                if (oldVm.BookDisplayControl != null)
                {
                    Log.Information("Querying WebView for current scroll position...");
                    currentAnchor = await oldVm.BookDisplayControl.GetCurrentParagraphAnchorAsync();
                    positionToken = await oldVm.BookDisplayControl.GetCurrentPositionTokenAsync();
                    Log.Information("Current scroll position anchor: {Anchor}", currentAnchor ?? "none");
                }
                else
                {
                    // Fallback to last known VRI anchor / rolling token if the WebView isn't available.
                    currentAnchor = oldVm.CurrentVriAnchor;
                    positionToken = oldVm.LastPositionToken;
                    Log.Warning("BookDisplayControl not available, using fallback VRI anchor: {Anchor}", currentAnchor ?? "none");
                }

                // Step 2: Capture all other state from old ViewModel
                var book = oldVm.Book;
                var searchTerms = oldVm.SearchTerms?.ToList();
                var searchPositions = oldVm.SearchPositions?.ToList();
                var bookScript = oldVm.BookScript;
                var docId = oldVm.DocId;
                var currentHitIndex = oldVm.CurrentHitIndex;
                var totalHits = oldVm.TotalHits;
                var showFootnotes = oldVm.ShowFootnotes;       // #224: carry View toggles across float/unfloat
                var showSearchTerms = oldVm.ShowSearchTerms;
                var title = oldVm.Title;  // preserve the exact tab title (current script + any prefix) across recreation

                Log.Information("Captured state: SearchTerms={Count}, Script={Script}, HitIndex={Hit}/{Total}, Anchor={Anchor}",
                    searchTerms?.Count ?? 0, bookScript, currentHitIndex, totalHits, currentAnchor ?? "none");

                // Step 2: Remove old ViewModel state from ApplicationState BEFORE removing from dock
                RemoveBookWindowState(oldVm.Id);
                Log.Information("Removed old ViewModel state from ApplicationState");

                // Step 3: Remove old ViewModel from floating dock (auto-disposes View and WebView)
                // IMPORTANT: Track which host window this book is in BEFORE removing it
                CstHostWindow? sourceHostWindow = null;
                if (oldVm.Owner is IDock currentDock)
                {
                    // Find the host window that contains this dock
                    sourceHostWindow = HostWindows.OfType<CstHostWindow>()
                        .FirstOrDefault(hw => FindDocumentDockInLayout(hw.Layout) == currentDock);

                    Log.Information("Unfloating from host window: {WindowId}", sourceHostWindow?.Id ?? "unknown");

                    RemoveDockable(oldVm, false);
                    Log.Information("Removed old ViewModel from floating dock");
                }

                // The old ViewModel is permanently replaced by newVm below; its state was already
                // captured above, so release its subscriptions and GoTo id now (avoids the leak) —
                // and shut down + evict its recycled View: "dispose-before-move" is the documented
                // discipline, but nothing implemented it (the PrepareForFloat/PrepareForUnfloat
                // handler is dead code for books), so the detached View's live CEF browser survived
                // in the app-lifetime ControlRecycling cache on every float/unfloat. (#193)
                _goToSubscribedBooks.Remove(oldVm.Id);
                DisposeAndEvictRecycledView(oldVm);
                oldVm.Dispose();

                // Step 4: Create brand new ViewModel with fresh GUID (NO windowId parameter!)
                var chapterListsService = App.ServiceProvider?.GetService<ChapterListsService>();
                var settingsService = App.ServiceProvider?.GetService<ISettingsService>();
                var fontService = App.ServiceProvider?.GetService<IFontService>();

                var newVm = new BookDisplayViewModel(
                    book: book,
                    searchTerms: searchTerms,
                    initialAnchor: currentAnchor,  // Restore scroll position
                    chapterListsService: chapterListsService,
                    settingsService: settingsService,
                    fontService: fontService,
                    docId: docId,
                    searchPositions: searchPositions,
                    windowId: null,  // CRITICAL: null = generates fresh GUID
                    dockFactory: this,
                    initialBookScript: bookScript,  // seed so the set below is a no-op (BOOK-3)
                    initialPositionToken: positionToken  // #434: restore the exact reading position across float/unfloat
                );

                // Restore additional state (BookScript set is a no-op given the seed; kept as a safety net)
                newVm.BookScript = bookScript;
                newVm.CurrentHitIndex = currentHitIndex;
                newVm.TotalHits = totalHits;
                newVm.ShowFootnotes = showFootnotes;
                newVm.ShowSearchTerms = showSearchTerms;
                newVm.IsFloating = false;
                newVm.CanFloat = false; // Restore drag-to-float block (CEF crash mitigation); matches the open + float paths
                // Carry the exact tab title across recreation (BookScript change doesn't refresh Title) — #6.
                newVm.Title = title;

                Log.Information("Created new ViewModel with fresh GUID: {NewInstanceId}", newVm.Id);

                // Subscribe to events for the new instance
                EnsureBookEventSubscription(newVm);

                // Step 5: Add to main dock and set active (creates fresh View automatically)
                AddDockable(mainDocDock, newVm);
                SetActiveDockable(newVm);
                SetFocusedDockable(mainDocDock, newVm);

                // Clean up ONLY the specific floating window that this book came from
                // DO NOT check all floating windows - this was causing other floating windows to disappear
                if (sourceHostWindow != null)
                {
                    var documentDock = FindDocumentDockInLayout(sourceHostWindow.Layout);
                    if (documentDock != null)
                    {
                        // Check for ReactiveDocument (BookDisplayViewModel) not just Document
                        var hasDocuments = documentDock.VisibleDockables?.OfType<ReactiveDocument>().Any() ?? false;
                        if (!hasDocuments)
                        {
                            Log.Information("Source floating window {WindowId} is now empty - closing it", sourceHostWindow.Id);
                            CloseEmptyHostWindow(sourceHostWindow);
                        }
                        else
                        {
                            Log.Information("Source floating window {WindowId} still has {Count} documents - keeping it open",
                                sourceHostWindow.Id, documentDock.VisibleDockables?.OfType<ReactiveDocument>().Count() ?? 0);
                        }
                    }
                }

                Log.Information("Unfloat operation completed - new instance in main window");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "CRITICAL: Unfloat operation failed for book: {BookFile}", oldVm.Book.FileName);
            }

            Log.Information("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        }

        /// <summary>
        /// Clean up empty floating windows after unfloat operations
        /// </summary>
        private void CleanupEmptyFloatingWindows()
        {
            // Use the existing method that properly handles empty window detection and cleanup
            CheckForEmptyFloatingWindows();
        }

        // Override CreateDocumentDock to enable visual drop adorners
        public override DocumentDock CreateDocumentDock()
        {
            var documentDock = new DocumentDock();
            // Stamp a stable unique id so framework-created docks are never anonymous.
            // (The spine's MainDocumentDock is built directly in CreateLayout with its
            //  well-known id, so it is unaffected by this.)
            if (string.IsNullOrEmpty(documentDock.Id))
                documentDock.Id = $"DocDock_{Guid.NewGuid():N}";
            documentDock.CanCreateDocument = false;
            documentDock.IsCollapsable = false;
            return documentDock;
        }

        // Framework-created split containers were previously born with empty ids, which broke
        // every fixed-id lookup and let degraded/anonymous structures accumulate during drags
        // (see docs/architecture/DOCK_SUBSYSTEM.md). Stamp a stable unique id at creation so no
        // dock is ever anonymous. The hand-built spine docks in CreateLayout set their own
        // well-known ids and never go through these factory methods.
        public override ProportionalDock CreateProportionalDock()
        {
            // base returns the concrete Mvvm ProportionalDock (typed as the interface) with its
            // VisibleDockables list initialized — keep that, just stamp an id.
            var dock = (ProportionalDock)base.CreateProportionalDock();
            if (string.IsNullOrEmpty(dock.Id))
                dock.Id = $"PDock_{Guid.NewGuid():N}";
            return dock;
        }

        public override ToolDock CreateToolDock()
        {
            var dock = (ToolDock)base.CreateToolDock();
            if (string.IsNullOrEmpty(dock.Id))
                dock.Id = $"ToolDock_{Guid.NewGuid():N}";
            return dock;
        }

        public override RootDock CreateRootDock()
        {
            // The framework creates a fresh RootDock per floating window; stamp it too so no dock is
            // anonymous. CreateLayout overwrites the two main roots with their well-known ids afterward.
            var dock = (RootDock)base.CreateRootDock();
            if (string.IsNullOrEmpty(dock.Id))
                dock.Id = $"RootDock_{Guid.NewGuid():N}";
            return dock;
        }

        /// <summary>
        /// Returns the LeftToolDock that hosts the tool panels, recreating it (and its LeftTools wrapper)
        /// under the protected MainDock if it has been removed — e.g. both panels were floated out and
        /// their windows closed (failure mode #4). This is what lets View → Show Search / Select-a-Book
        /// always bring panels back instead of no-op'ing on a corrupted layout.
        /// </summary>
        internal ToolDock? EnsureLeftToolDock()
        {
            // Reuse an existing LeftToolDock anywhere in the main layout (it may be nested after drags).
            if (_rootDock != null && FindDockByIdRecursive(_rootDock, "LeftToolDock") is ToolDock existing)
            {
                return existing;
            }

            if (_mainDock?.VisibleDockables == null)
            {
                Log.Error("*** EnsureLeftToolDock: MainDock unavailable - cannot recreate tool container ***");
                return null;
            }

            Log.Information("*** EnsureLeftToolDock: LeftToolDock missing - recreating tool container under MainDock ***");

            var leftToolDock = new ToolDock
            {
                Id = "LeftToolDock",
                Title = "Tools",
                Alignment = Alignment.Left,
                GripMode = GripMode.Visible,
                CanDrag = true,
                CanDrop = true,
                VisibleDockables = CreateList<IDockable>(),
                Factory = this
            };
            var leftTools = new ProportionalDock
            {
                Id = "LeftTools",
                Proportion = 0.25,
                Orientation = Orientation.Vertical,
                IsCollapsable = false,
                CanDrop = true,
                VisibleDockables = CreateList<IDockable>(leftToolDock),
                Factory = this
            };

            // Insert at the left of MainDock (mirrors CreateLayout: [leftTools, splitter, documents]).
            var splitter = new ProportionalDockSplitter { Id = "MainSplitter", Title = "MainSplitter" };
            _mainDock.VisibleDockables.Insert(0, splitter);
            _mainDock.VisibleDockables.Insert(0, leftTools);

            // Wire the new docks (Owner, Factory, init events) the way CreateLayout/InitLayout does.
            // Without a proper Owner, base.FloatDockable can't detach them, so floating these recreated
            // panels would silently no-op.
            InitDockable(leftTools, _mainDock);
            InitDockable(leftToolDock, leftTools);

            return leftToolDock;
        }
        
        // Override to prevent accidental closes during drag operations
        // The app-wide ControlRecycling instance (App.axaml resource, shared by every DockControl).
        private IControlRecycling? GetControlRecycling()
        {
            try
            {
                if (Application.Current?.TryGetResource("ControlRecyclingKey", null, out var res) == true
                    && res is IControlRecycling recycling)
                    return recycling;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Could not resolve the ControlRecycling resource");
            }
            return null;
        }

        // On a real tab close, dispose the closed book's recycled View (its CEF WebView) and evict it
        // from the app-wide recycling cache. Dock 11.3.6.5 exposes no per-key removal (only Clear(),
        // which would nuke every other tab's cached View and orphan their live browsers), so we remove
        // this single entry from the private _cache by value — guarded, so a Dock upgrade that renames
        // the field degrades to "WebView disposed (CEF freed), empty View shell retained". (BOOK-1)
        //
        // Releasing the CEF browser is the load-bearing half; do it via the VM's own live View
        // reference too, not only via the cache lookup. The cache hit relies on recycling keying by
        // VM instance (TryToUseIdAsKey stays false in App.axaml) and on the View having been built
        // through the recycling template — flip either and the cache lookup would silently miss and
        // re-leak the browser. vm.BookDisplayControl is a direct, always-correct fallback. (F1)
        // Handles both BookDisplayView and PdfDisplayView — both host a live CEF WebView that outlives
        // detach and must be torn down explicitly on close, or the closed tab strands a browser +
        // renderer for the session. (BookDisplayViewModel also exposes a live-view backref as a
        // cache-miss fallback; PdfDisplayViewModel has none, so it relies on the cache lookup.)
        private void DisposeAndEvictRecycledView(IDockable dockable)
        {
            var recycling = GetControlRecycling();

            try
            {
                object? cachedControl = recycling != null && recycling.TryGetValue(dockable, out var control) ? control : null;

                // Shut down whichever View we can reach — the cached one, or (for books) the VM's live
                // reference if the cache missed (resource-lookup failure, non-recycled build, key-mode
                // change). Shutdown is idempotent, so hitting the same View twice is harmless.
                object? viewToShutdown = cachedControl
                    ?? (dockable is BookDisplayViewModel bookVm ? bookVm.BookDisplayControl : null);
                switch (viewToShutdown)
                {
                    case BookDisplayView bookView: bookView.Shutdown(); break;  // release the CEF WebView — the actual leak
                    case PdfDisplayView pdfView: pdfView.Shutdown(); break;
                }

                if (recycling == null)
                    return;

                var cacheField = recycling.GetType().GetField("_cache", BindingFlags.NonPublic | BindingFlags.Instance);
                if (cachedControl != null && cacheField?.GetValue(recycling) is IDictionary cache)
                {
                    object? keyToRemove = null;
                    foreach (DictionaryEntry entry in cache)
                    {
                        if (ReferenceEquals(entry.Value, cachedControl))
                        {
                            keyToRemove = entry.Key;
                            break;
                        }
                    }
                    if (keyToRemove != null)
                    {
                        cache.Remove(keyToRemove);
                        Log.Debug("Evicted recycled View for closed dockable {Id} from ControlRecycling", dockable.Id);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "Failed to dispose/evict recycled View for closed dockable {Id}", dockable.Id);
            }
        }

        public override void CloseDockable(IDockable dockable)
        {
            Log.Debug("*** CloseDockable called for: {DockableType} (ID: {DockableId}) ***", dockable?.GetType().Name, dockable?.Id);

            // Allow closing but add logging to help debug accidental closes
            if (dockable is Document document)
            {
                Log.Debug("*** Closing Document: {DocumentTitle} ***", document.Title);
            }
            else if (dockable is ReactiveDocument reactiveDocument)
            {
                Log.Debug("*** Closing ReactiveDocument: {DocumentTitle} ***", reactiveDocument.Title);
            }

            if (dockable != null)
            {
                // Remove from application state if it's a BookDisplayViewModel
                if (dockable is BookDisplayViewModel)
                {
                    RemoveBookWindowState(dockable.Id);
                    Log.Debug("*** Removed book window state for {DockableId} ***", dockable.Id);
                }

                base.CloseDockable(dockable);

                // Tab permanently closed: release the VM's subscriptions and drop its GoTo id so
                // neither leaks for the rest of the session. (Safe here — CloseDockable is the real
                // close path, not the recycled detach/reorder path.)
                if (dockable is BookDisplayViewModel closedBookVm)
                {
                    _goToSubscribedBooks.Remove(closedBookVm.Id);
                    // Release the recycled View's CEF WebView and drop the cache entry, or the closed
                    // tab leaks a live browser + its rendered DOM + HtmlContent for the session. (BOOK-1)
                    DisposeAndEvictRecycledView(closedBookVm);
                    closedBookVm.Dispose();
                }
                else if (dockable is PdfDisplayViewModel closedPdfVm)
                {
                    // PDFs render in a CEF WebView (PDFium) too, and the close path never disposed it —
                    // so every opened-then-closed PDF stranded a live browser + renderer for the session.
                    // Same treatment as books. (PDF close leak; missed by the BOOK-1 review, which only
                    // covered book views.)
                    DisposeAndEvictRecycledView(closedPdfVm);
                    (closedPdfVm as IDisposable)?.Dispose();
                }
            }
            else
            {
                Log.Warning("*** CloseDockable called with null dockable ***");
            }

            // Clean up empty splits after closing a dockable
            Log.Debug("*** Post-close cleanup - checking for empty splits ***");
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
            Log.Debug("*** Post-swap cleanup - checking for empty splits ***");
            CleanupEmptySplits();
        }
        
        // Override MoveDockable to trigger cleanup after tab moves
        public override void MoveDockable(IDock dock, IDockable sourceDockable, IDockable targetDockable)
        {
            Log.Debug("*** MoveDockable called - Dock: {DockId}, Source: {SourceId}, Target: {TargetId} ***", dock?.Id, sourceDockable?.Id, targetDockable?.Id);
            if (dock != null && sourceDockable != null && targetDockable != null)
            {
                base.MoveDockable(dock, sourceDockable, targetDockable);
            }
            else
            {
                Log.Warning("*** MoveDockable called with null parameters ***");
                return;
            }
            Log.Debug("*** MoveDockable completed ***");
            
            // Trigger cleanup after move operations as they can leave empty structures
            Log.Debug("*** Post-move cleanup - checking for empty splits ***");
            CleanupEmptySplits();
        }
        
        // Override AddDockable to trigger cleanup 
        public override void AddDockable(IDock dock, IDockable dockable)
        {
            Log.Debug("*** AddDockable called - Dock: {DockId}, Dockable: {DockableId} ***", dock?.Id, dockable?.Id);
            if (dock != null && dockable != null)
            {
                base.AddDockable(dock, dockable);
            }
            else
            {
                Log.Warning("*** AddDockable called with null parameters ***");
                return;
            }
            Log.Debug("*** AddDockable completed ***");
            
            // Trigger cleanup after add operations 
            Log.Debug("*** Post-add cleanup - checking for empty splits ***");
            CleanupEmptySplits();
        }
        
        // Override RemoveDockable to trigger cleanup
        public override void RemoveDockable(IDockable dockable, bool collapse)
        {
            Log.Debug("*** RemoveDockable called - Dockable: {DockableId}, Collapse: {Collapse} ***", dockable?.Id, collapse);
            if (dockable != null)
            {
                // Remove from application state if it's a BookDisplayViewModel
                if (dockable is BookDisplayViewModel)
                {
                    RemoveBookWindowState(dockable.Id);
                    Log.Debug("*** Removed book window state for {DockableId} ***", dockable.Id);
                }

                base.RemoveDockable(dockable, collapse);
            }
            else
            {
                Log.Warning("*** RemoveDockable called with null dockable ***");
                return;
            }
            Log.Debug("*** RemoveDockable completed ***");

            // Trigger cleanup after remove operations
            Log.Debug("*** Post-remove cleanup - checking for empty splits ***");
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
        
        // CreateWindowFrom(IDockWindow?) deleted (DOCK-8): it was not an override and had no
        // callers — floating windows are created via DefaultHostWindowLocator = CreateCstHostWindow —
        // so its title/position/size customization never ran, and its catch-fallback would have
        // leaked the first, already-tracked window as an untracked zombie while returning a second.

        // Create host window for floating documents - fallback approach
        private IHostWindow CreateCstHostWindow()
        {
            Log.Debug("*** CreateCstHostWindow() called - creating floating window via fallback ***");
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
                        Log.Debug("*** View menu setup completed for floating window ***");
                    }
                }

                Log.Debug("*** Host window created successfully ***");
                Log.Debug("*** Host window properties: Id={Id}, Factory={HasFactory} ***",
                    hostWindow.Id, hostWindow.Factory != null);
                Log.Debug("*** Total host windows: {HostWindowCount} ***", HostWindows.Count);

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
                    Log.Debug("*** Setting up collection monitoring for floating window document dock: {DockId} ***", documentDock.Id);
                    
                    observableCollection.CollectionChanged += (sender, e) =>
                    {
                        Log.Debug("*** FLOATING WINDOW COLLECTION CHANGED: Action={Action}, NewItems={NewCount}, RemovedItems={RemoveCount} ***", 
                            e.Action, e.NewItems?.Count ?? 0, e.OldItems?.Count ?? 0);
                        
                        if (e.OldItems != null)
                        {
                            foreach (var item in e.OldItems)
                            {
                                Log.Debug("*** FLOATING WINDOW REMOVED ITEM: {ItemType} {ItemId} ***", item?.GetType().Name, (item as IDockable)?.Id);

                                // Clean up application state when documents are removed from floating windows
                                if (item is BookDisplayViewModel removedBookViewModel)
                                {
                                    Log.Debug("*** Document removed from floating window - cleaning up application state: {DocumentId} ***", removedBookViewModel.Id);
                                    RemoveBookWindowState(removedBookViewModel.Id);
                                }
                            }
                        }
                        if (e.NewItems != null)
                        {
                            foreach (var item in e.NewItems)
                            {
                                Log.Debug("*** FLOATING WINDOW ADDED ITEM: {ItemType} {ItemId} ***", item?.GetType().Name, (item as IDockable)?.Id);
                            }
                        }
                        
                        // Check for empty floating windows after any collection change
                        Log.Debug("*** Floating window collection changed - calling CheckForEmptyFloatingWindows ***");
                        CheckForEmptyFloatingWindows();
                        
                        // Clean up empty splits in floating windows after any collection change
                        Log.Debug("*** Floating window collection changed - cleaning up empty splits ***");
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

        // Fallback title for a floating window that momentarily has no content (empty windows are
        // auto-closed, so this is transient).
        private const string DefaultFloatingWindowTitle = "CST Reader";

        // #322 (#284 follow-up): the title-tracking event subscriptions per floating window, kept as
        // detach delegates so they can be removed when the window closes (no leaked handlers / rooted
        // window graph) and re-wired idempotently on a repeat SetLayout (no double-wiring).
        internal readonly Dictionary<IHostWindowTitleTarget, List<Action>> _titleSubscriptions = new();

        // #284: keep a floating window's title in sync with its content. Naming scheme:
        //   single item  -> that item's title (book name or tool name),
        //   multiple items-> active tab's title + "  +N"  (N = the other tabs),
        // updated live as tabs are added/removed, as the active tab changes, and as a leaf's own title
        // changes (e.g. a global script switch retitles a book). (The main window keeps its static
        // "CST Reader" title.) Wired from CstHostWindow.SetLayout, once the layout exists.
        public void SetupHostWindowTitleTracking(CstHostWindow window)
        {
            // Hook the window's close exactly once to detach + forget its subscriptions, then (re)wire.
            if (!_titleSubscriptions.ContainsKey(window))
            {
                window.Closed += (_, _) =>
                {
                    if (_titleSubscriptions.TryGetValue(window, out var subs))
                    {
                        DetachTitleSubscriptions(subs);
                        _titleSubscriptions.Remove(window);
                    }
                };
            }
            WireHostWindowTitle(window);
        }

        // Testable core (#425): (re)wire title tracking for a host and refresh its title. Idempotent —
        // detaches any prior subscriptions first, so it's safe to call repeatedly (a SetLayout re-run, or the
        // tab-set change handler). Has no dependency on a real Avalonia Window, so unit tests drive it with a
        // fake IHostWindowTitleTarget.
        internal void WireHostWindowTitle(IHostWindowTitleTarget host)
        {
            try
            {
                // Idempotent: drop any prior subscriptions before re-wiring.
                if (_titleSubscriptions.TryGetValue(host, out var existing))
                {
                    DetachTitleSubscriptions(existing);
                }

                // Store the list BEFORE wiring so that if WireTitleUpdates throws mid-walk, the handlers it
                // already attached are still reachable (via close / the next re-wire) and don't leak.
                // (Fable review of #357.)
                var detaches = new List<Action>();
                _titleSubscriptions[host] = detaches;
                WireTitleUpdates(host, host.Layout, 0, detaches);

                UpdateHostWindowTitle(host);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "*** Failed to set up host window title tracking ***");
            }
        }

        private static void DetachTitleSubscriptions(List<Action> detaches)
        {
            foreach (var detach in detaches)
            {
                try { detach(); } catch { /* best-effort unsubscribe */ }
            }
            detaches.Clear();
        }

        // Subscribe the things that change a floating window's title: each dock's tab set
        // (VisibleDockables collection) and active tab (ActiveDockable/FocusedDockable), plus each LEAF
        // dockable's own Title (a book retitles on a global script switch — A8-3). Every subscription is
        // paired with a detach delegate collected into <paramref name="detaches"/> so it can be undone on
        // window close. Docks/leaves present at wire-up are tracked; a later tab add/remove re-runs this
        // wiring via the collection-change handler (#357), so dragged-in leaves get subscribed too.
        private void WireTitleUpdates(IHostWindowTitleTarget host, IDockable? node, int depth, List<Action> detaches)
        {
            if (node == null || depth > 32) return;

            if (node is IDock dock)
            {
                if (dock is INotifyPropertyChanged inpc)
                {
                    PropertyChangedEventHandler h = (_, e) =>
                    {
                        if (e.PropertyName == nameof(IDock.ActiveDockable) ||
                            e.PropertyName == nameof(IDock.FocusedDockable))
                        {
                            UpdateHostWindowTitle(host);
                        }
                    };
                    inpc.PropertyChanged += h;
                    detaches.Add(() => inpc.PropertyChanged -= h);
                }

                if (dock.VisibleDockables is INotifyCollectionChanged incc)
                {
                    // #357: on any tab add/remove, re-run the whole wiring (detach + re-subscribe) so a leaf
                    // dragged INTO an existing float also gets its own Title subscription. Without this, once
                    // the originally-wired tab is gone a later in-place retitle (global script switch) leaves
                    // this window's title bar + Window-menu entry stale. Re-wiring also refreshes the title.
                    NotifyCollectionChangedEventHandler h = (_, _) => WireHostWindowTitle(host);
                    incc.CollectionChanged += h;
                    detaches.Add(() => incc.CollectionChanged -= h);
                }

                if (dock.VisibleDockables != null)
                {
                    foreach (var child in dock.VisibleDockables)
                    {
                        WireTitleUpdates(host, child, depth + 1, detaches);
                    }
                }
            }
            else if (node is INotifyPropertyChanged leaf)
            {
                // Leaf content VM (book/tool): its Title can change in place (script switch → new
                // DisplayTitle → Title). A single-tab float has no tab-switch to otherwise refresh it.
                PropertyChangedEventHandler h = (_, e) =>
                {
                    if (e.PropertyName == nameof(IDockable.Title) || e.PropertyName == "DisplayTitle")
                    {
                        UpdateHostWindowTitle(host);
                    }
                };
                leaf.PropertyChanged += h;
                detaches.Add(() => leaf.PropertyChanged -= h);
            }
        }

        // Compute and apply the title for a floating window from its current content. Short-circuits when
        // the computed title is unchanged, so ordinary focus flapping doesn't churn SetTitle + the native
        // Window-menu rebuild (A8-5).
        internal void UpdateHostWindowTitle(IHostWindowTitleTarget host)
        {
            try
            {
                var newTitle = ComputeFloatingWindowTitle(host.Layout);
                if (string.Equals(host.Title, newTitle, StringComparison.Ordinal)) return;

                host.SetTitle(newTitle);
                // #284: the Window menu shows this title, so refresh the lists when it actually changes.
                (Application.Current as App)?.RebuildWindowMenus();
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "*** Failed to update host window title ***");
            }
        }

        internal string ComputeFloatingWindowTitle(IDock? layout)
        {
            var leaves = new List<IDockable>();
            CollectLeafDockables(layout, leaves, 0);
            if (leaves.Count == 0) return DefaultFloatingWindowTitle;

            var active = FindActiveLeafDockable(layout) ?? leaves[0];
            var baseTitle = string.IsNullOrWhiteSpace(active.Title) ? DefaultFloatingWindowTitle : active.Title;

            var others = leaves.Count - 1;
            return others > 0 ? $"{baseTitle}  +{others}" : baseTitle;
        }

        // Leaves = the actual content view-models (books/tools), i.e. dockables that are not themselves docks.
        private static void CollectLeafDockables(IDockable? node, List<IDockable> leaves, int depth)
        {
            if (node == null || depth > 32) return;
            if (node is IDock dock)
            {
                if (dock.VisibleDockables != null)
                {
                    foreach (var child in dock.VisibleDockables)
                    {
                        CollectLeafDockables(child, leaves, depth + 1);
                    }
                }
            }
            else
            {
                leaves.Add(node);
            }
        }

        // Follow the ActiveDockable chain down to the focused leaf (the visible tab), or null if it
        // doesn't resolve to a leaf (caller then falls back to the first leaf).
        private static IDockable? FindActiveLeafDockable(IDockable? node)
        {
            var guard = 0;
            while (node is IDock dock && dock.ActiveDockable != null && guard++ < 32)
            {
                node = dock.ActiveDockable;
            }
            return node is IDock ? null : node;
        }

        // Check for empty floating windows and close them
        private void CheckForEmptyFloatingWindows()
        {
            try
            {
                Log.Debug("*** CheckForEmptyFloatingWindows called - Total host windows: {HostWindowCount} ***", HostWindows.Count);
                
                var emptyWindows = new List<CstHostWindow>();
                
                foreach (var hostWindow in HostWindows.OfType<CstHostWindow>().ToList())
                {
                    Log.Debug("*** Checking host window: {WindowId} ***", hostWindow.Id);
                    
                    // Find the DocumentDock within the host window's layout hierarchy
                    var documentDock = FindDocumentDockInLayout(hostWindow.Layout);
                    
                    if (documentDock != null)
                    {
                        var totalDockables = documentDock.VisibleDockables?.Count ?? 0;
                        // Check for ReactiveDocument (BookDisplayViewModel) not just Document
                        var hasDocuments = documentDock.VisibleDockables?.OfType<ReactiveDocument>().Any() ?? false;
                        var docCount = documentDock.VisibleDockables?.OfType<ReactiveDocument>().Count() ?? 0;

                        Log.Debug("*** Host window {WindowId} - Layout: {LayoutType}, DocumentDock found - Total dockables: {TotalCount}, ReactiveDocuments: {DocumentCount}, HasDocuments: {HasDocuments} ***",
                            hostWindow.Id, hostWindow.Layout?.GetType().Name, totalDockables, docCount, hasDocuments);

                        if (!hasDocuments)
                        {
                            Log.Debug("*** EMPTY FLOATING WINDOW DETECTED: {WindowId} - scheduling for closure ***", hostWindow.Id);
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
                    Log.Debug("*** AUTO-CLOSING EMPTY FLOATING WINDOW: {WindowId} ***", emptyWindow.Id);
                    CloseEmptyHostWindow(emptyWindow);
                }
                
                Log.Debug("*** CheckForEmptyFloatingWindows completed - Closed {EmptyWindowCount} empty windows ***", emptyWindows.Count);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** ERROR while checking for empty floating windows ***");
            }
        }
        
        /// <summary>
        /// Recursively traverse all dock layouts (main + floating windows) and clean up empty splits after drag operations.
        /// Internal so LayoutViewModel.RemoveToolFromLayout can collapse the empty dock left by hiding a panel. (DOCK-5)
        /// </summary>
        internal void CleanupEmptySplits()
        {
            try
            {
                Log.Debug("*** CleanupEmptySplits called - starting iterative cleanup (Main + {FloatingCount} floating windows) ***", HostWindows.Count);
                int totalRemoved = 0;
                int iteration = 0;
                
                // Keep running cleanup until no more empty splits are found
                // This ensures that removing one empty split doesn't leave parent splits empty
                while (true)
                {
                    iteration++;
                    Log.Debug("*** CleanupEmptySplits iteration {Iteration} ***", iteration);
                    
                    var emptySplits = new List<IDock>();
                    
                    // Check main window
                    if (_context is IDock rootDock && rootDock.VisibleDockables != null)
                    {
                        Log.Debug("*** Checking main window for empty splits ***");
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
                            Log.Debug("*** Checking floating window {Index} ({WindowId}) for empty splits ***", i, cstHostWindow.Id);
                            
                            if (cstHostWindow.Layout is IDock floatingDock)
                            {
                                FindEmptySplits(floatingDock, emptySplits);
                            }
                            else
                            {
                                Log.Debug("***   Floating window {Index} has no dock layout ***", i);
                            }
                        }
                        else
                        {
                            Log.Debug("*** Floating window {Index}: Not CstHostWindow (Type: {WindowType}) ***", i, hostWindow.GetType().Name);
                        }
                    }
                    
                    Log.Debug("*** Iteration {Iteration}: Found {EmptySplitCount} empty splits to clean up ***", 
                        iteration, emptySplits.Count);
                    
                    // If no empty splits found, we're done
                    if (emptySplits.Count == 0)
                    {
                        Log.Debug("*** No more empty splits found - cleanup complete ***");
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
                    Log.Debug("*** Iteration {Iteration}: Removed {RemovedCount} empty splits ***", 
                        iteration, emptySplits.Count);
                }
                
                Log.Debug("*** CleanupEmptySplits completed after {Iterations} iterations - Total removed: {TotalRemoved} empty splits ***", 
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
        internal void FindEmptySplits(IDock dock, List<IDock> emptySplits)
        {
            try
            {
                if (dock?.VisibleDockables == null)
                {
                    return;
                }
                
                Log.Debug("*** Examining dock: {DockType} (ID: {DockId}) with {DockableCount} dockables ***", 
                    dock.GetType().Name, dock.Id ?? "null", dock.VisibleDockables.Count);
                
                // Check if this is a ProportionalDock (split container)
                if (dock is ProportionalDock proportionalDock)
                {
                    // Count non-splitter dockables
                    var nonSplitterDockables = proportionalDock.VisibleDockables?
                        .Where(d => !(d is ProportionalDockSplitter))
                        .ToList() ?? new List<IDockable>();
                    
                    Log.Debug("*** ProportionalDock {DockId} has {NonSplitterCount} non-splitter dockables ***", 
                        proportionalDock.Id ?? "null", nonSplitterDockables.Count);
                    
                    // List each dockable for debugging
                    for (int i = 0; i < nonSplitterDockables.Count; i++)
                    {
                        var dockable = nonSplitterDockables[i];
                        Log.Debug("***   Child {Index}: {DockableType} (ID: {DockableId}) ***", 
                            i, dockable.GetType().Name, (dockable as IDockable)?.Id ?? "null");
                    }
                    
                    // Self-redundancy: collapse a single-child ProportionalDock regardless of its parent
                    // type. The child-scan below only runs for ProportionalDock parents, so a redundant
                    // wrapper sitting directly under a RootDock (e.g. around the protected MainDock) would
                    // otherwise never be flattened, leaving the tree one level short of the spine.
                    // (Protected spine and the intentional LeftTools wrapper are excluded.)
                    if (nonSplitterDockables.Count == 1 && nonSplitterDockables[0] is IDock
                        && !IsProtectedSpine(proportionalDock) && proportionalDock.Id != "LeftTools")
                    {
                        Log.Debug("*** REDUNDANT SINGLE-CHILD DOCK: {DockId} - collapsing ***", proportionalDock.Id ?? "null");
                        if (!emptySplits.Contains(proportionalDock))
                            emptySplits.Add(proportionalDock);
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
                                Log.Debug("***     Child {ChildType} (ID: {ChildId}) is empty: {IsEmpty} ***", 
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
                                Log.Debug("***     Child {ChildType} is not a dock - not empty ***", 
                                    child.GetType().Name);
                                allChildrenEmpty = false;
                            }
                        }
                        
                        // Strategy 1: If ALL children are empty, mark the whole ProportionalDock for removal.
                        // BUT never mark a protected spine dock — instead fall through to Strategy 2 so its
                        // empty/redundant CHILDREN are removed (e.g. a redundant wrapper under MainDock is
                        // collapsed, promoting MainDocumentDock back up), letting the tree heal toward the
                        // spine without ever trying to delete the spine itself.
                        if (allChildrenEmpty && nonSplitterDockables.Count > 0 && !IsProtectedSpine(proportionalDock))
                        {
                            Log.Debug("*** EMPTY SPLIT DETECTED: ProportionalDock {DockId} - all {ChildCount} children are empty ***",
                                proportionalDock.Id ?? "null", nonSplitterDockables.Count);
                            emptySplits.Add(proportionalDock);
                        }
                        // Strategy 2: If some children are empty but not all, mark individual empty children
                        // (also handles the protected-parent case above: parent stays, empty children go)
                        else if (emptyChildren.Count > 0)
                        {
                            Log.Debug("*** PARTIAL EMPTY SPLIT DETECTED: ProportionalDock {DockId} - {EmptyCount} of {TotalCount} children are empty ***", 
                                proportionalDock.Id ?? "null", emptyChildren.Count, nonSplitterDockables.Count);
                            
                            // Add individual empty children to cleanup list
                            foreach (var emptyChild in emptyChildren)
                            {
                                Log.Debug("*** ADDING INDIVIDUAL EMPTY CHILD FOR CLEANUP: {ChildType} (ID: {ChildId}) ***", 
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
        // The invariant spine — the ACTUAL dock instances created in CreateLayout (Root, WindowLayout,
        // MainDock, MainDocumentDock). Matched by REFERENCE, not by id: the framework can clone a dock and
        // copy its id (document-area splits produce several "MainDocumentDock"-id'd clones), and those
        // clones must NOT be protected — only the originals. See docs/architecture/DOCK_SUBSYSTEM.md.
        internal readonly List<IDockable> _spineDocks = new();

        internal bool IsProtectedSpine(IDockable? dock) =>
            dock != null && _spineDocks.Any(d => ReferenceEquals(d, dock));

        internal bool IsEmptyDock(IDock dock)
        {
            try
            {
                // Never treat a spine dock as empty/redundant — it stays put even when single-child.
                if (IsProtectedSpine(dock))
                {
                    return false;
                }

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
                        Log.Debug("***   DocumentDock {DockId} has {Count} dockables:",
                            documentDock.Id ?? "null", documentDock.VisibleDockables.Count);
                        foreach (var dockable in documentDock.VisibleDockables)
                        {
                            Log.Debug("***     - {DockableType} (ID: {DockableId})",
                                dockable.GetType().Name, dockable.Id ?? "null");
                        }
                        Log.Debug("***   But {DocumentCount} are Document/ReactiveDocument", documents.Count);
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
                            Log.Debug("*** ProportionalDock {DockId} is redundant - has only one child dock (unnecessary nesting) ***",
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
                // Defense-in-depth: never remove a protected spine dock, even if something flagged it.
                if (IsProtectedSpine(emptySplit))
                {
                    Log.Warning("*** Refusing to remove protected spine dock: {DockId} ***", emptySplit.Id ?? "null");
                    return;
                }

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
                        Log.Debug("*** ProportionalDock has no special handling - removing completely ***");
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
                        Log.Information("*** Removing unnecessary splitter from ProportionalDock {DockId} ***", 
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
                Log.Debug("*** FindParentDock searching for parent of: {TargetDockType} (ID: {TargetDockId}) ***", 
                    targetDock?.GetType().Name, targetDock?.Id ?? "null");
                
                // Search in main window first
                if (_context is IDock rootDock && rootDock.VisibleDockables != null)
                {
                    Log.Debug("*** Searching in main window hierarchy ***");
                    var parent = targetDock != null ? FindParentDockRecursive(rootDock, targetDock) : null;
                    if (parent != null)
                    {
                        Log.Debug("*** Found parent in main window: {ParentType} (ID: {ParentId}) ***", 
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
                        Log.Debug("*** Searching in floating window {Index} ({WindowId}) ***", i, cstHostWindow.Id);
                        var parent = targetDock != null ? FindParentDockRecursive(floatingDock, targetDock) : null;
                        if (parent != null)
                        {
                            Log.Debug("*** Found parent in floating window {Index}: {ParentType} (ID: {ParentId}) ***", 
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
                        Log.Debug("*** CaptureMainDockProportions: MainDock not found ***");
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

                        Log.Debug("*** CaptureMainDockProportions: Captured Left={Left:F3} (ID: {LeftId}), Right={Right:F3} (was Left={OldLeft:F3}, Right={OldRight:F3}) ***",
                            _mainDockLeftProportion, leftDock.Id, _mainDockRightProportion, oldLeft, oldRight);
                    }
                    else
                    {
                        Log.Warning("*** CaptureMainDockProportions: Could not find left dock or MainDocumentDock (leftDock={LeftFound}, docDock={DocFound}) ***",
                            leftDock != null, documentDock != null);

                        // Debug: Log all visible dockables in MainDock to diagnose
                        Log.Debug("*** CaptureMainDockProportions: MainDock has {Count} visible dockables ***",
                            mainDock.VisibleDockables.Count);
                        foreach (var dockable in mainDock.VisibleDockables)
                        {
                            Log.Debug("***   - {Type} (ID: {Id}, Proportion: {Prop:F3}) ***",
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

                            Log.Debug("*** RestoreMainDockProportions: Restored proportions from Left={CurrentLeft:F3} (ID: {LeftId}), Right={CurrentRight:F3} to Left={TargetLeft:F3}, Right={TargetRight:F3} ***",
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
                        Log.Debug("*** RestoreMainDockProportions: MainDock has {Count} visible dockables ***",
                            mainDock.VisibleDockables.Count);
                        foreach (var dockable in mainDock.VisibleDockables)
                        {
                            Log.Debug("***   - {Type} (ID: {Id}, Proportion: {Prop:F3}) ***",
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
                Log.Debug("*** Closing empty host window: {WindowId} ***", hostWindow.Id);

                // Remove from our tracking first
                HostWindows.Remove(hostWindow);

                // Close the actual window
                hostWindow.Close();

                Log.Debug("*** Empty host window closed successfully ***");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** ERROR closing empty host window: {WindowId} ***", hostWindow.Id);
            }
        }
        
        // Handle closing of host windows (manually closed or with remaining documents)
        public void CloseHostWindow(CstHostWindow hostWindow)
        {
            Log.Debug("*** CloseHostWindow called for: {WindowId} ***", hostWindow.Id);

            // Untrack FIRST: the rescue below empties this window's dock, and the collection-changed
            // monitor reacts to that by closing any empty window it finds in HostWindows — running
            // that against a window already inside its own Closing event would re-enter Close().
            // (Idempotent: CloseEmptyHostWindow untracks before Close(), so this is often a no-op.)
            HostWindows.Remove(hostWindow);
            Log.Debug("*** Host window removed from tracking. Remaining host windows: {Count} ***", HostWindows.Count);

            // The old rescue was doubly dead code: hostWindow.Layout is a RootDock (never a
            // DocumentDock), and books are ReactiveDocument, which OfType<Document>() never matched —
            // so closing a floating window silently dropped its books (leaked VMs, ghost tabs on next
            // launch). Rescue them for real now. (DOCK-2)
            RescueBooksFromClosingWindow(hostWindow);

            // Update panel visibility state after removing window
            // This ensures menu checkmarks update correctly when panels were in the closed window
            if (App.MainWindow?.DataContext is LayoutViewModel layoutViewModel)
            {
                layoutViewModel.UpdatePanelVisibility();
                Log.Debug("*** Panel visibility updated after window close ***");
            }
        }

        // Move any books left in a closing floating window back into the main document dock. (DOCK-2)
        private void RescueBooksFromClosingWindow(CstHostWindow hostWindow)
        {
            // During app shutdown every floating window closes as a matter of course: the books'
            // states are already saved (they restore next launch) and ServiceProvider may already be
            // disposed, so recreating tabs — which spins up fresh CEF browsers mid-teardown — would
            // be wasted work at best and a crash at worst.
            if (App.IsShuttingDown)
            {
                Log.Information("*** Skipping book rescue for {WindowId} - application is shutting down ***", hostWindow.Id);
                return;
            }

            try
            {
                var floatingDock = FindDocumentDockInLayout(hostWindow.Layout);
                var mainDocDock = FindDocumentDock();
                var booksToRescue = floatingDock?.VisibleDockables?.OfType<BookDisplayViewModel>().ToList();

                if (booksToRescue == null || booksToRescue.Count == 0)
                {
                    Log.Debug("*** No books to rescue - window was already empty ***");
                    return;
                }
                if (mainDocDock == null)
                {
                    Log.Error("*** Cannot rescue {Count} book(s) from {WindowId} - main document dock not found ***",
                        booksToRescue.Count, hostWindow.Id);
                    return;
                }

                Log.Information("*** Rescuing {Count} book(s) from closing floating window {WindowId} ***",
                    booksToRescue.Count, hostWindow.Id);

                foreach (var oldVm in booksToRescue)
                {
                    try
                    {
                        RescueBookToMainDock(oldVm, mainDocDock);
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "*** ERROR rescuing book {BookFile} from closing window ***", oldVm.Book.FileName);
                    }
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** ERROR rescuing books from closing window {WindowId} ***", hostWindow.Id);
            }
        }

        // Recreate one book from a closing floating window inside the main dock. Same discipline as
        // UnfloatDockableWithoutRecycling: the old View's live CEF WebView must never be re-parented —
        // capture state, shut down + evict the old View, dispose the old VM, create a fresh VM
        // (fresh GUID => fresh View + browser). NOTE the WebView does NOT die with the closing window:
        // WebViewControl auto-disposes only on detach with the window's PlatformImpl already null, and
        // this runs inside Window.Closing while it is still live — the explicit
        // DisposeAndEvictRecycledView below is what actually releases the browser (#177 reopen).
        // The window is mid-Close, so no async WebView query is possible; CurrentVriAnchor (from the
        // last scroll status update) is the best available scroll position. (DOCK-2)
        private void RescueBookToMainDock(BookDisplayViewModel oldVm, DocumentDock mainDocDock)
        {
            var book = oldVm.Book;
            var searchTerms = oldVm.SearchTerms?.ToList();
            var searchPositions = oldVm.SearchPositions?.ToList();
            var bookScript = oldVm.BookScript;
            var docId = oldVm.DocId;
            var currentHitIndex = oldVm.CurrentHitIndex;
            var totalHits = oldVm.TotalHits;
            var showFootnotes = oldVm.ShowFootnotes;       // #224: carry View toggles across float/unfloat
            var showSearchTerms = oldVm.ShowSearchTerms;
            var title = oldVm.Title;  // preserve the exact tab title (current script + any prefix) across recreation
            var anchor = oldVm.CurrentVriAnchor;

            // Old id's persisted state and subscriptions go away with the old VM (the collection-changed
            // monitor also removes the state on RemoveDockable; this keeps the ordering explicit).
            RemoveBookWindowState(oldVm.Id);
            RemoveDockable(oldVm, false);
            _goToSubscribedBooks.Remove(oldVm.Id);
            DisposeAndEvictRecycledView(oldVm);
            oldVm.Dispose();

            var chapterListsService = App.ServiceProvider?.GetService<ChapterListsService>();
            var settingsService = App.ServiceProvider?.GetService<ISettingsService>();
            var fontService = App.ServiceProvider?.GetService<IFontService>();

            var newVm = new BookDisplayViewModel(
                book: book,
                searchTerms: searchTerms,
                initialAnchor: anchor,
                chapterListsService: chapterListsService,
                settingsService: settingsService,
                fontService: fontService,
                docId: docId,
                searchPositions: searchPositions,
                windowId: null,  // CRITICAL: null = generates fresh GUID
                dockFactory: this,
                initialBookScript: bookScript  // seed so the set below is a no-op (BOOK-3)
            );

            // Restore additional state (BookScript set is a no-op given the seed; kept as a safety net)
            newVm.BookScript = bookScript;
            newVm.CurrentHitIndex = currentHitIndex;
            newVm.TotalHits = totalHits;
            newVm.ShowFootnotes = showFootnotes;
            newVm.ShowSearchTerms = showSearchTerms;
            newVm.IsFloating = false;
            newVm.CanFloat = false; // Restore drag-to-float block (CEF crash mitigation); matches the open + float paths
            newVm.Title = title;

            // Wire Go To / Attha-Tika / View Source for the fresh instance
            EnsureBookEventSubscription(newVm);

            AddDockable(mainDocDock, newVm);
            SetActiveDockable(newVm);
            SetFocusedDockable(mainDocDock, newVm);

            Log.Information("*** Rescued book {BookFile} into main window as {NewId} ***", book.FileName, newVm.Id);
        }

        // Find which window contains a specific book instance
        private Window? FindWindowContainingBook(BookDisplayViewModel bookViewModel)
        {
            _logger.Debug("Searching for window containing book: {BookFile}", bookViewModel.Book.FileName);

            // Check main window first
            if (App.MainWindow != null)
            {
                var mainDocDock = FindDocumentDock();
                if (mainDocDock?.VisibleDockables?.Contains(bookViewModel) == true)
                {
                    _logger.Information("Book found in main window");
                    return App.MainWindow;
                }
            }

            // Check floating windows
            foreach (var hostWindow in HostWindows)
            {
                if (hostWindow is CstHostWindow cstHostWindow && cstHostWindow.Layout != null)
                {
                    var docDock = FindDocumentDockInLayout(cstHostWindow.Layout) as DocumentDock;
                    if (docDock?.VisibleDockables?.Contains(bookViewModel) == true)
                    {
                        _logger.Information("Book found in floating window: {WindowTitle}", cstHostWindow.Title);
                        return cstHostWindow;
                    }
                }
            }

            _logger.Warning("Book not found in any window");
            return null;
        }

        // Ensure all existing books have event subscriptions
        public void EnsureBookEventSubscriptions()
        {
            _logger.Debug("Ensuring event subscriptions for all books");
            int subscriptionCount = 0;

            // Subscribe to books in main window
            var mainDocDock = FindDocumentDock();
            if (mainDocDock?.VisibleDockables != null)
            {
                foreach (var dockable in mainDocDock.VisibleDockables)
                {
                    if (dockable is BookDisplayViewModel bookViewModel)
                    {
                        EnsureBookEventSubscription(bookViewModel);
                        subscriptionCount++;
                    }
                }
            }

            // Subscribe to books in floating windows
            foreach (var hostWindow in HostWindows.OfType<CstHostWindow>())
            {
                if (hostWindow.Layout != null)
                {
                    var docDock = FindDocumentDockInLayout(hostWindow.Layout) as DocumentDock;
                    if (docDock?.VisibleDockables != null)
                    {
                        foreach (var dockable in docDock.VisibleDockables)
                        {
                            if (dockable is BookDisplayViewModel bookViewModel)
                            {
                                EnsureBookEventSubscription(bookViewModel);
                                subscriptionCount++;
                            }
                        }
                    }
                }
            }

            _logger.Information("Event subscriptions ensured for {Count} books", subscriptionCount);
        }

        // Prefix carried by search-result tab titles; must survive title refreshes on script change. (DOCK-4)
        internal const string SearchTitlePrefix = "🔍 ";

        // Ensure a single book has ALL its per-book wiring (idempotent - won't duplicate subscriptions).
        // This is the ONE wiring point shared by the regular open path, the search open path, and
        // float/unfloat/rescue recreation — the two open paths previously wired slightly different
        // subsets inline and had drifted (search-opened books had no View Source, no title sync, and
        // no script-state handler; worse, they pre-added their id to _goToSubscribedBooks, which made
        // this method skip them forever). (DOCK-4)
        private void EnsureBookEventSubscription(BookDisplayViewModel bookViewModel)
        {
            // Check if already subscribed using the book's unique ID
            if (!_goToSubscribedBooks.Contains(bookViewModel.Id))
            {
                _logger.Debug("Adding book-action event subscriptions (Go To / Attha-Tika / View Source / title+script sync) for book: {BookFile} (ID: {BookId})",
                    bookViewModel.Book.FileName, bookViewModel.Id);

                bookViewModel.OpenGoToDialogRequested += () =>
                {
                    _logger.Information("OpenGoToDialogRequested event fired for book: {BookFile}", bookViewModel.Book.FileName);
                    Dispatcher.UIThread.Post(async () =>
                    {
                        try
                        {
                            await ShowGoToDialog(bookViewModel);
                        }
                        catch (Exception ex)
                        {
                            _logger.Error(ex, "Error showing Go To dialog");
                        }
                    });
                };

                // Attha/Tika linked-book buttons. Must be re-wired here (not only in the open-book create
                // path) so float/unfloat-created ViewModels keep working — they get a fresh GUID and route
                // through this method, so without this the buttons silently no-op after any float/unfloat.
                bookViewModel.OpenBookRequested += (linkedBook, anchorForLinked) =>
                {
                    _logger.Debug("Opening linked book: {BookFile} with anchor: {Anchor}", linkedBook.FileName, anchorForLinked ?? "null");
                    OpenBook(linkedBook, anchorForLinked);
                };

                // View Source PDF buttons (1957 / 2010) — same reasoning as OpenBookRequested above.
                bookViewModel.OpenPdfRequested += (bookFilename, sourceType, targetPage) =>
                {
                    _logger.Information("OpenPdfRequested event fired for book: {BookFile}, source: {SourceType}, page: {Page}",
                        bookFilename, sourceType, targetPage);
                    OpenPdf(bookFilename, sourceType, targetPage);
                };

                // Keep the tab title in sync when a script change regenerates DisplayTitle, preserving
                // the search-result prefix; and persist per-tab script changes into application state.
                bookViewModel.PropertyChanged += (sender, e) =>
                {
                    if (e.PropertyName == nameof(BookDisplayViewModel.DisplayTitle))
                    {
                        var prefix = bookViewModel.Title?.StartsWith(SearchTitlePrefix) == true
                            ? SearchTitlePrefix : string.Empty;
                        _logger.Information("DisplayTitle changed for document {DocumentId}: {OldTitle} -> {NewTitle}",
                            bookViewModel.Id, bookViewModel.Title, prefix + bookViewModel.DisplayTitle);
                        bookViewModel.Title = prefix + bookViewModel.DisplayTitle;
                    }
                    else if (e.PropertyName == nameof(BookDisplayViewModel.BookScript))
                    {
                        UpdateBookScriptInState(bookViewModel.Id, bookViewModel.BookScript);
                    }
                    // #224: persist the Footnotes / search-highlight toggles as the user flips them.
                    else if (e.PropertyName == nameof(BookDisplayViewModel.ShowFootnotes) ||
                             e.PropertyName == nameof(BookDisplayViewModel.ShowSearchTerms))
                    {
                        UpdateBookViewFlagsInState(bookViewModel.Id, bookViewModel.ShowFootnotes, bookViewModel.ShowSearchTerms);
                    }
                };

                _goToSubscribedBooks.Add(bookViewModel.Id);
            }
        }

        // Show Go To dialog for a book
        private async System.Threading.Tasks.Task ShowGoToDialog(BookDisplayViewModel bookViewModel)
        {
            try
            {
                _logger.Information("Showing Go To dialog for book: {BookFile}", bookViewModel.Book.FileName);

                // Create dialog ViewModel
                var dialogViewModel = new GoToDialogViewModel(bookViewModel);

                // Create and show dialog
                var dialog = new GoToDialog(dialogViewModel);

                // Find the owner window - check if book is in a floating window or main window
                Window? ownerWindow = FindWindowContainingBook(bookViewModel);
                if (ownerWindow == null)
                {
                    _logger.Warning("Cannot find window containing book - falling back to main window");
                    ownerWindow = App.MainWindow;
                }

                if (ownerWindow == null)
                {
                    _logger.Warning("Cannot show Go To dialog - no window found");
                    return;
                }

                _logger.Information("Showing dialog with owner: {OwnerType}", ownerWindow.GetType().Name);

                // Show dialog modally
                var result = await dialog.ShowDialog<bool>(ownerWindow);

                if (result && !string.IsNullOrEmpty(dialogViewModel.ConstructedAnchor))
                {
                    _logger.Information("Go To navigation requested: {Anchor}", dialogViewModel.ConstructedAnchor);

                    // Trigger navigation via internal invoke method
                    bookViewModel.InvokeNavigateToChapter(dialogViewModel.ConstructedAnchor);
                }
                else
                {
                    _logger.Debug("Go To dialog cancelled or no anchor constructed");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error showing Go To dialog");
            }
        }

    }
}