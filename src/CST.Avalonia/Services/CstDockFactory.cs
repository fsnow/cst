using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Collections.Specialized;
using System.ComponentModel;
using System.Linq;
using Avalonia.Threading;
using Dock.Model.Core;
using Dock.Model.Mvvm;
using Dock.Model.Mvvm.Controls;
using CST.Avalonia.ViewModels;
using CST.Avalonia.Views;
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

        public CstDockFactory()
        {
            _logger = Log.ForContext<CstDockFactory>();
        }

        public override RootDock CreateLayout()
        {
            System.Console.WriteLine("CreateLayout called - starting dock layout creation");
            
            // Get OpenBookDialogViewModel from the service provider
            var openBookViewModel = App.ServiceProvider?.GetRequiredService<OpenBookDialogViewModel>();
            System.Console.WriteLine($"OpenBookViewModel: {openBookViewModel?.GetType().Name ?? "null"}");
            
            // Create the book selection tool
            var openBookTool = new Tool
            {
                Id = "OpenBookTool",
                Title = "Select a Book",
                Context = openBookViewModel,
                CanPin = false,    // Prevent pinning to avoid vertical text issues
                CanClose = false   // Prevent accidental closing
            };
            
            System.Console.WriteLine($"Created OpenBookTool with Context: {openBookTool.Context?.GetType().Name ?? "null"}");
            if (openBookViewModel != null)
            {
                System.Console.WriteLine($"OpenBookViewModel BookTree has {openBookViewModel.BookTree.Count} items");
            }

            // Create the book selection tool dock (left side)
            var leftToolDock = new ToolDock
            {
                Id = "LeftToolDock",
                Title = "Select a Book",
                Proportion = 0.25, // 25% of width
                ActiveDockable = openBookTool,
                VisibleDockables = CreateList<IDockable>(openBookTool),
                CanFloat = false, // Prevent floating
                CanPin = false, // Prevent pinning
                CanClose = false // Prevent closing
            };

            // Create a permanent welcome document that can't be closed
            var welcomeDocument = new Document
            {
                Id = "WelcomeDocument",
                Title = "Welcome",
                Context = new WelcomeViewModel(),
                CanClose = false,  // This prevents the tab from being closed
                CanFloat = false,  // Prevent floating this document
                CanPin = false     // Prevent pinning
            };

            // Create document dock for book content (right side)
            var documentDock = new DocumentDock
            {
                Id = "MainDocumentDock",
                Title = "Documents",
                Proportion = 0.75, // 75% of width
                ActiveDockable = welcomeDocument,
                VisibleDockables = CreateList<IDockable>(welcomeDocument),
                CanCreateDocument = true, // Allow creating documents through drag operations
                CanFloat = true, // Allow floating for multi-window support
                CanPin = false, // Prevent pinning
                CanClose = false, // Prevent closing the entire dock
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
                                        if (observableCollection.Contains(item as IDockable))
                                        {
                                            observableCollection.Remove(item as IDockable);
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
                    
                    // Check if this is the main document dock or a floating window dock
                    Log.Information("*** Main dock collection changed - checking for empty floating windows ***");
                    CheckForEmptyFloatingWindows();
                    
                    Log.Information("*** Main dock collection changed - cleaning up empty splits ***");
                    CleanupEmptySplits();
                };
            }

            // Create main proportional dock (horizontal split)
            var mainDock = new ProportionalDock
            {
                Id = "MainDock",
                Title = "Main",
                Orientation = Orientation.Horizontal,
                VisibleDockables = CreateList<IDockable>(leftToolDock, documentDock)
            };

            // Create root dock
            var rootDock = new RootDock
            {
                Id = "Root",
                Title = "Root",
                ActiveDockable = mainDock,
                DefaultDockable = mainDock,
                VisibleDockables = CreateList<IDockable>(mainDock)
            };

            // Set the factory on all dockables
            SetFactory(rootDock);

            System.Console.WriteLine($"Layout created successfully:");
            System.Console.WriteLine($"  RootDock: {rootDock.Id} with {rootDock.VisibleDockables?.Count} dockables");
            System.Console.WriteLine($"  MainDock: {mainDock.Id} with {mainDock.VisibleDockables?.Count} dockables");
            System.Console.WriteLine($"  LeftToolDock: {leftToolDock.Id} with {leftToolDock.VisibleDockables?.Count} dockables");
            System.Console.WriteLine($"  DocumentDock: {documentDock.Id} with {documentDock.VisibleDockables?.Count} dockables");

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
                // For tools and documents, we might need to set factory too
                if (otherDockable is Tool tool)
                {
                    tool.Factory = this;
                }
                else if (otherDockable is Document document)
                {
                    document.Factory = this;
                }
            }
        }

        // Required abstract members implementation - simplified for now
        public override IDictionary<IDockable, IDockableControl> VisibleDockableControls { get; } = new Dictionary<IDockable, IDockableControl>();
        public override IDictionary<IDockable, IDockableControl> TabDockableControls { get; } = new Dictionary<IDockable, IDockableControl>();
        public override IDictionary<IDockable, IDockableControl> PinnedDockableControls { get; } = new Dictionary<IDockable, IDockableControl>();
        public override IList<IDockControl> DockControls { get; } = new List<IDockControl>();
        public override ObservableCollection<IHostWindow> HostWindows { get; } = new ObservableCollection<IHostWindow>();


        private void EnableDragAndDropForDock(IDock dock)
        {
            // Enable drag and drop for this dock
            if (dock is DocumentDock documentDock)
            {
                System.Console.WriteLine($"Enabling drag/drop for DocumentDock: {documentDock.Id}");
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
            System.Console.WriteLine($"Opening book: {book.FileName} - {book.LongNavPath}");
            
            // Allow multiple copies of the same book to be opened
            // This is useful for comparing the same text in different scripts
            
            // Create BookDisplayViewModel for the book content with proper script service
            var scriptService = App.ServiceProvider?.GetRequiredService<IScriptService>();
            var chapterListsService = App.ServiceProvider?.GetRequiredService<ChapterListsService>();
            var bookDisplayViewModel = new BookDisplayViewModel(book, null, null, chapterListsService);
            
            // Set the correct script after construction
            if (scriptService != null && bookDisplayViewModel != null)
            {
                // Set the script to match the current application setting
                bookDisplayViewModel.BookScript = scriptService.CurrentScript;
                System.Console.WriteLine($"Set book script to: {scriptService.CurrentScript}");
            }
            
            // Use the same DisplayTitle logic as BookDisplayViewModel to ensure consistency
            string documentTitle = bookDisplayViewModel.DisplayTitle;

            // Create a new document for the book with a unique ID
            // Using a GUID suffix to allow multiple copies of the same book
            var document = new Document
            {
                Id = $"Book_{book.FileName}_{Guid.NewGuid():N}",
                Title = documentTitle,
                Context = bookDisplayViewModel,
                CanFloat = true,   // Allow floating for multi-window support
                CanPin = false,    // Prevent pinning
                CanClose = true    // Allow closing
            };
            
            // Subscribe to DisplayTitle changes to update the Document.Title for tab updates
            if (bookDisplayViewModel is INotifyPropertyChanged propertyChanged)
            {
                propertyChanged.PropertyChanged += (sender, e) =>
                {
                    if (e.PropertyName == nameof(BookDisplayViewModel.DisplayTitle))
                    {
                        _logger.Information("DisplayTitle changed for document {DocumentId}: {OldTitle} -> {NewTitle}", 
                            document.Id, document.Title, bookDisplayViewModel.DisplayTitle);
                        document.Title = bookDisplayViewModel.DisplayTitle;
                    }
                };
            }
            
            // Add to the document dock
            AddDocumentToLayout(document);
            
            System.Console.WriteLine($"Created document: {document.Id} with title: {document.Title}");
        }

        public void CloseBook(string bookId)
        {
            // Prevent closing the Welcome document
            if (bookId == "WelcomeDocument")
            {
                System.Console.WriteLine("Cannot close Welcome document - it's permanent");
                return;
            }
            
            var document = FindDocument(bookId);
            if (document != null)
            {
                RemoveDocumentFromLayout(document);
                System.Console.WriteLine($"Closed book: {bookId}");
            }
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
        
        private void ActivateDocument(Document document)
        {
            // Find the document dock containing this document and make it active
            var documentDock = FindDocumentDock();
            if (documentDock != null)
            {
                documentDock.ActiveDockable = document;
                System.Console.WriteLine($"Activated existing document: {document.Id}");
            }
        }
        
        private void AddDocumentToLayout(Document document)
        {
            var documentDock = FindDocumentDock();
            if (documentDock != null)
            {
                Log.Information("*** ADDING DOCUMENT TO LAYOUT: {DocumentId} ***", document.Id);
                documentDock.VisibleDockables?.Add(document);
                documentDock.ActiveDockable = document;
                SetFactory(document); // Ensure the document has the factory reference
                Log.Information("*** DOCUMENT ADDED SUCCESSFULLY. Total documents: {DocumentCount} ***", documentDock.VisibleDockables?.Count ?? 0);
                System.Console.WriteLine($"Added document to layout: {document.Id}");
            }
            else
            {
                Log.Warning("*** WARNING: Could not find document dock to add book ***");
                System.Console.WriteLine("Warning: Could not find document dock to add book");
            }
        }
        
        private void RemoveDocumentFromLayout(Document document)
        {
            var documentDock = FindDocumentDock();
            if (documentDock != null && documentDock.VisibleDockables != null)
            {
                Log.Information("*** REMOVING DOCUMENT FROM LAYOUT: {DocumentId} ***", document.Id);
                var countBefore = documentDock.VisibleDockables.Count;
                documentDock.VisibleDockables.Remove(document);
                var countAfter = documentDock.VisibleDockables.Count;
                Log.Information("*** DOCUMENT REMOVAL: Before={CountBefore}, After={CountAfter} ***", countBefore, countAfter);
                
                // If this was the active document, activate another one
                if (documentDock.ActiveDockable == document)
                {
                    // Find the Welcome document first, then fall back to last document
                    var welcomeDoc = documentDock.VisibleDockables.OfType<Document>().FirstOrDefault(d => d.Id == "WelcomeDocument");
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
        
        // Override split operations to handle document splits and create new DocumentDocks
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
            
            // Handle Document/DocumentDock split operations to create new DocumentDocks
            if (dock is DocumentDock targetDock && (dockable is Document || dockable is DocumentDock))
            {
                var operationStr = operation.ToString();
                Log.Information("*** Document/DocumentDock operation detected: {Operation} on DocumentDock ***", operationStr);
                Log.Information("*** Dockable type: {DockableType}, Target dock: {DockId} ***", dockable.GetType().Name, targetDock.Id);
                
                if (operationStr == "Left" || operationStr == "Right" || operationStr == "Top" || operationStr == "Bottom")
                {
                    Log.Information("*** Split operation detected: {Operation} - attempting custom split ***", operationStr);
                    
                    // For Document, use existing logic
                    if (dockable is Document document)
                    {
                        if (CreateDocumentSplit(targetDock, document, operation))
                        {
                            Log.Information("*** Document split completed successfully - skipping default behavior ***");
                            return; // Skip default behavior since we handled it
                        }
                        else
                        {
                            Log.Warning("*** Document split failed, falling back to default behavior ***");
                        }
                    }
                    // For DocumentDock (e.g., floating window being docked back), let default behavior handle it
                    // but log the proportions to understand what's happening
                    else if (dockable is DocumentDock sourceDock)
                    {
                        Log.Information("*** DocumentDock split - using default behavior but monitoring proportions ***");
                        Log.Information("*** Source dock proportion: {SourceProp}, Target dock proportion: {TargetProp} ***", 
                            sourceDock.Proportion, targetDock.Proportion);
                    }
                }
                else
                {
                    Log.Information("*** Tab operation (Fill) detected - using default behavior ***");
                }
            }
            else if (dockable is Document)
            {
                Log.Information("*** Document operation on non-DocumentDock: DockType={DockType}, Operation={Operation} ***", dock?.GetType().Name, operation);
            }
            
            base.SplitToDock(dock, dockable, operation);
            
            // Schedule proportion fix after split operations complete
            ScheduleProportionFix();
            
            // Schedule empty split cleanup after split operations complete
            Dispatcher.UIThread.Post(() =>
            {
                Log.Information("*** Post-split cleanup - checking for empty splits ***");
                CleanupEmptySplits();
            }, DispatcherPriority.Background);
            
            Log.Information("*** SplitToDock completed ***");
        }
        
        /// <summary>
        /// Create a split layout when a document is dragged to create a new DocumentDock
        /// </summary>
        private bool CreateDocumentSplit(DocumentDock originalDock, Document documentToSplit, DockOperation operation)
        {
            try
            {
                Log.Information("*** CreateDocumentSplit starting - Operation: {Operation} ***", operation);
                
                // Find the parent of the original DocumentDock
                var parentDock = FindParentDock(originalDock);
                if (parentDock == null)
                {
                    Log.Warning("*** Cannot find parent dock for split operation ***");
                    return false;
                }
                
                Log.Information("*** Found parent dock: {ParentType} (ID: {ParentId}) ***", parentDock.GetType().Name, parentDock.Id);
                
                // Remove the document from the original dock first
                if (originalDock.VisibleDockables?.Contains(documentToSplit) == true)
                {
                    originalDock.VisibleDockables.Remove(documentToSplit);
                    Log.Information("*** Removed document from original dock ***");
                }
                
                // Store original dock's proportion before modifying
                var originalProportion = originalDock.Proportion;
                Log.Information("*** Original dock proportion: {OriginalProportion} ***", originalProportion);
                
                // Create a new DocumentDock for the split document
                var newDocumentDock = CreateDocumentDock();
                newDocumentDock.Id = $"SplitDocumentDock_{Guid.NewGuid():N}";
                newDocumentDock.Title = "Documents";
                newDocumentDock.VisibleDockables = CreateList<IDockable>(documentToSplit);
                newDocumentDock.ActiveDockable = documentToSplit;
                newDocumentDock.Factory = this;
                
                Log.Information("*** Created new DocumentDock: {NewDockId} ***", newDocumentDock.Id);
                
                // Set up collection monitoring for the new dock
                SetupDocumentDockMonitoring(newDocumentDock);
                
                // Create ProportionalDock based on operation
                var proportionalDock = new ProportionalDock();
                proportionalDock.Id = $"SplitContainer_{Guid.NewGuid():N}";
                proportionalDock.Title = "Split Container";
                proportionalDock.Factory = this;
                
                // Configure orientation and dockable order based on operation
                switch (operation)
                {
                    case DockOperation.Left:
                        proportionalDock.Orientation = Orientation.Horizontal;
                        proportionalDock.VisibleDockables = CreateList<IDockable>(newDocumentDock, originalDock);
                        newDocumentDock.Proportion = 1.0; // Equal weight for 50/50 split
                        originalDock.Proportion = 1.0;   // Equal weight for 50/50 split
                        break;
                    case DockOperation.Right:
                        proportionalDock.Orientation = Orientation.Horizontal;
                        proportionalDock.VisibleDockables = CreateList<IDockable>(originalDock, newDocumentDock);
                        originalDock.Proportion = 1.0;   // Equal weight for 50/50 split
                        newDocumentDock.Proportion = 1.0; // Equal weight for 50/50 split
                        break;
                    case DockOperation.Top:
                        proportionalDock.Orientation = Orientation.Vertical;
                        proportionalDock.VisibleDockables = CreateList<IDockable>(newDocumentDock, originalDock);
                        newDocumentDock.Proportion = 1.0; // Equal weight for 50/50 split
                        originalDock.Proportion = 1.0;   // Equal weight for 50/50 split
                        break;
                    case DockOperation.Bottom:
                        proportionalDock.Orientation = Orientation.Vertical;
                        proportionalDock.VisibleDockables = CreateList<IDockable>(originalDock, newDocumentDock);
                        originalDock.Proportion = 1.0;   // Equal weight for 50/50 split
                        newDocumentDock.Proportion = 1.0; // Equal weight for 50/50 split
                        break;
                    default:
                        Log.Warning("*** Unsupported split operation: {Operation} ***", operation);
                        return false;
                }
                
                // Force equal proportions after switch statement
                if (proportionalDock.VisibleDockables != null && proportionalDock.VisibleDockables.Count == 2)
                {
                    var firstDock = proportionalDock.VisibleDockables[0];
                    var secondDock = proportionalDock.VisibleDockables[1];
                    
                    // Reset to ensure no inherited proportions
                    firstDock.Proportion = double.NaN;
                    secondDock.Proportion = double.NaN;
                    
                    // Set equal proportions
                    firstDock.Proportion = 0.5;
                    secondDock.Proportion = 0.5;
                    
                    Log.Information("*** Final proportions set - First: {FirstProp}, Second: {SecondProp} ***", 
                        firstDock.Proportion, secondDock.Proportion);
                }
                
                Log.Information("*** Created ProportionalDock: {ProportionalDockId}, Orientation: {Orientation} ***", 
                    proportionalDock.Id, proportionalDock.Orientation);
                
                // Replace the original DocumentDock with the ProportionalDock in the parent
                if (ReplaceInParent(parentDock, originalDock, proportionalDock))
                {
                    Log.Information("*** Successfully replaced DocumentDock with ProportionalDock in parent ***");
                    return true;
                }
                else
                {
                    Log.Warning("*** Failed to replace DocumentDock in parent ***");
                    // Restore document to original dock if split failed
                    originalDock.VisibleDockables?.Add(documentToSplit);
                    return false;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** Error in CreateDocumentSplit ***");
                return false;
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
        
        /// <summary>
        /// Set up collection monitoring for a DocumentDock (similar to existing monitoring)
        /// </summary>
        private void SetupDocumentDockMonitoring(DocumentDock documentDock)
        {
            if (documentDock.VisibleDockables is ObservableCollection<IDockable> observableCollection)
            {
                observableCollection.CollectionChanged += (sender, e) =>
                {
                    Log.Information("*** SPLIT DOCK COLLECTION CHANGED: Action={Action}, NewItems={NewCount}, RemovedItems={RemoveCount} ***", 
                        e.Action, e.NewItems?.Count ?? 0, e.OldItems?.Count ?? 0);
                    
                    if (e.OldItems != null)
                    {
                        foreach (var item in e.OldItems)
                        {
                            Log.Information("*** SPLIT DOCK REMOVED ITEM: {ItemType} {ItemId} ***", item?.GetType().Name, (item as IDockable)?.Id);
                        }
                    }
                    if (e.NewItems != null)
                    {
                        foreach (var item in e.NewItems)
                        {
                            Log.Information("*** SPLIT DOCK ADDED ITEM: {ItemType} {ItemId} ***", item?.GetType().Name, (item as IDockable)?.Id);
                            
                            // Prevent Tools and ToolDocks from being added to split DocumentDocks too
                            if (item is Tool || item is ToolDock)
                            {
                                Log.Warning("*** Tool/ToolDock being added to split DocumentDock - preventing tab docking ***");
                                System.Threading.Tasks.Task.Run(async () =>
                                {
                                    await System.Threading.Tasks.Task.Delay(50);
                                    await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                                    {
                                        if (observableCollection.Contains(item as IDockable))
                                        {
                                            observableCollection.Remove(item as IDockable);
                                            if (item is IDockable dockable)
                                            {
                                                FloatDockable(dockable);
                                                Log.Information("*** Floated Tool/ToolDock instead of tab docking in split ***");
                                            }
                                        }
                                    });
                                });
                            }
                        }
                    }
                    
                    // Check for empty split docks and potentially merge them back
                    CheckForEmptySplitDocks();
                };
            }
        }
        
        /// <summary>
        /// Check for empty split DocumentDocks and clean up the layout
        /// </summary>
        private void CheckForEmptySplitDocks()
        {
            try
            {
                Log.Information("*** CheckForEmptySplitDocks called ***");
                
                if (_context is not RootDock rootDock)
                    return;
                    
                // Find empty DocumentDocks in ProportionalDocks and clean them up
                CheckForEmptySplitDocksRecursive(rootDock);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** Error checking for empty split docks ***");
            }
        }
        
        private void CheckForEmptySplitDocksRecursive(IDock dock)
        {
            if (dock.VisibleDockables == null)
                return;
                
            // Process children first (depth-first)
            var children = dock.VisibleDockables.OfType<IDock>().ToList();
            foreach (var child in children)
            {
                CheckForEmptySplitDocksRecursive(child);
            }
            
            // Now check if this dock is a ProportionalDock with empty DocumentDocks
            if (dock is ProportionalDock proportionalDock)
            {
                var documentDocks = proportionalDock.VisibleDockables?.OfType<DocumentDock>().ToList() ?? new List<DocumentDock>();
                var emptyDocks = documentDocks.Where(d => d.VisibleDockables?.OfType<Document>().Any() != true).ToList();
                
                if (emptyDocks.Any())
                {
                    Log.Information("*** Found {EmptyCount} empty DocumentDocks in ProportionalDock {DockId} ***", emptyDocks.Count, proportionalDock.Id);
                    
                    // If only one DocumentDock remains, replace the ProportionalDock with it
                    var remainingDocks = documentDocks.Except(emptyDocks).ToList();
                    if (remainingDocks.Count == 1)
                    {
                        var remainingDock = remainingDocks[0];
                        var parent = FindParentDock(proportionalDock);
                        if (parent != null)
                        {
                            Log.Information("*** Replacing ProportionalDock with single remaining DocumentDock ***");
                            ReplaceInParent(parent, proportionalDock, remainingDock);
                        }
                    }
                    else
                    {
                        // Remove empty docks from the ProportionalDock
                        foreach (var emptyDock in emptyDocks)
                        {
                            proportionalDock.VisibleDockables?.Remove(emptyDock);
                            Log.Information("*** Removed empty DocumentDock {DockId} ***", emptyDock.Id);
                        }
                    }
                }
            }
        }
        
        // Override to handle floating operations
        public override void FloatDockable(IDockable dockable)
        {
            Log.Information("*** FloatDockable called for: {DockableType} (ID: {DockableId}) ***", dockable?.GetType().Name, dockable?.Id);
            Log.Information("*** Dockable CanFloat: {CanFloat} ***", (dockable as Document)?.CanFloat ?? false);
            
            // Check if dockable can float - if not, don't proceed
            if (dockable is Document document && !document.CanFloat)
            {
                Log.Warning("*** FLOATING REJECTED - Document CanFloat is false ***");
                return;
            }
            
            try 
            {
                Log.Information("*** Calling base FloatDockable ***");
                base.FloatDockable(dockable);
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
                if (dockable is Document failedDocument)
                {
                    Log.Information("*** Attempting to restore document to original dock after failed float ***");
                    try
                    {
                        AddDocumentToLayout(failedDocument);
                        Log.Information("*** Document restored to original dock ***");
                    }
                    catch (Exception restoreEx)
                    {
                        Log.Error(restoreEx, "*** CRITICAL: Failed to restore document after failed float - document may be lost ***");
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
            documentDock.CanCreateDocument = true;
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
                Log.Information("*** Closing document: {DocumentTitle} ***", document.Title);
            }
            
            base.CloseDockable(dockable);
            
            // Clean up empty splits after closing a dockable
            Log.Information("*** Post-close cleanup - checking for empty splits ***");
            CleanupEmptySplits();
        }
        
        // Override SwapDockable to debug tab operations
        public override void SwapDockable(IDock dock, IDockable sourceDockable, IDockable targetDockable)
        {
            Log.Information("*** SwapDockable called - Dock: {DockId}, Source: {SourceId}, Target: {TargetId} ***", dock?.Id, sourceDockable?.Id, targetDockable?.Id);
            base.SwapDockable(dock, sourceDockable, targetDockable);
            Log.Information("*** SwapDockable completed ***");
        }
        
        // Schedule proportion fixes after split operations complete
        private void ScheduleProportionFix()
        {
            System.Threading.Tasks.Task.Run(async () =>
            {
                await System.Threading.Tasks.Task.Delay(100); // Small delay to let split layout complete
                await global::Avalonia.Threading.Dispatcher.UIThread.InvokeAsync(() =>
                {
                    FixSplitProportions();
                });
            });
        }
        
        /// <summary>
        /// Fix proportions in ProportionalDocks to ensure 50/50 splits
        /// </summary>
        private void FixSplitProportions()
        {
            try
            {
                Log.Information("*** FixSplitProportions called ***");
                
                if (_context is not RootDock rootDock)
                    return;
                    
                FixSplitProportionsRecursive(rootDock);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "*** Error in FixSplitProportions ***");
            }
        }
        
        private void FixSplitProportionsRecursive(IDock dock)
        {
            if (dock.VisibleDockables == null)
            {
                Log.Information("*** FixSplitProportionsRecursive - dock has no visible dockables: {DockType} {DockId} ***", dock.GetType().Name, dock.Id);
                return;
            }
                
            Log.Information("*** FixSplitProportionsRecursive - processing dock: {DockType} {DockId} with {DockableCount} dockables ***", 
                dock.GetType().Name, dock.Id, dock.VisibleDockables.Count);
                
            // Process children first
            foreach (var child in dock.VisibleDockables.OfType<IDock>())
            {
                Log.Information("*** Processing child dock: {ChildType} {ChildId} ***", child.GetType().Name, child.Id);
                FixSplitProportionsRecursive(child);
            }
            
            // If this is a ProportionalDock, analyze its structure and fix proportions
            if (dock is ProportionalDock proportionalDock)
            {
                Log.Information("*** Found ProportionalDock {DockId} with {DockableCount} dockables ***", proportionalDock.Id, proportionalDock.VisibleDockables?.Count ?? 0);
                
                // Log all dockables to understand the structure
                if (proportionalDock.VisibleDockables != null)
                {
                    for (int i = 0; i < proportionalDock.VisibleDockables.Count; i++)
                    {
                        var dockable = proportionalDock.VisibleDockables[i];
                        Log.Information("*** Dockable {Index}: {DockableType} (ID: {DockableId}, Proportion: {Proportion}) ***", 
                            i, dockable.GetType().Name, dockable.Id, dockable.Proportion);
                    }
                }
                
                var documentDocks = proportionalDock.VisibleDockables?.OfType<DocumentDock>().ToList() ?? new List<DocumentDock>();
                Log.Information("*** ProportionalDock has {DocumentDockCount} DocumentDocks ***", documentDocks.Count);
                
                // Handle both 2 and 3 dockable cases (in case there are splitters or other elements)
                if (documentDocks.Count == 2)
                {
                    var first = documentDocks[0];
                    var second = documentDocks[1];
                    
                    Log.Information("*** Current DocumentDock proportions - First: {FirstProp}, Second: {SecondProp} ***", 
                        first.Proportion, second.Proportion);
                    
                    // Only fix if proportions are unequal
                    if (Math.Abs(first.Proportion - 0.5) > 0.01 || Math.Abs(second.Proportion - 0.5) > 0.01)
                    {
                        Log.Information("*** Fixing unequal DocumentDock proportions - First: {FirstProp}, Second: {SecondProp} ***", 
                            first.Proportion, second.Proportion);
                        
                        first.Proportion = 0.5;
                        second.Proportion = 0.5;
                        
                        Log.Information("*** DocumentDock proportions SET to 50/50 ***");
                        
                        // Verify the proportions were actually set
                        Log.Information("*** VERIFICATION - First proportion after set: {FirstProp}, Second after set: {SecondProp} ***", 
                            first.Proportion, second.Proportion);
                        
                        // Note: We've set the proportions, but the visual layout may need time to update
                    }
                    else
                    {
                        Log.Information("*** DocumentDock proportions already equal - no fix needed ***");
                    }
                }
                else if (proportionalDock.VisibleDockables?.Count == 2)
                {
                    // Handle case where we have exactly 2 dockables that might not be DocumentDocks
                    var first = proportionalDock.VisibleDockables[0];
                    var second = proportionalDock.VisibleDockables[1];
                    
                    Log.Information("*** Current general proportions - First: {FirstProp}, Second: {SecondProp} ***", 
                        first.Proportion, second.Proportion);
                    
                    if (Math.Abs(first.Proportion - 0.5) > 0.01 || Math.Abs(second.Proportion - 0.5) > 0.01)
                    {
                        Log.Information("*** Fixing unequal general proportions - First: {FirstProp}, Second: {SecondProp} ***", 
                            first.Proportion, second.Proportion);
                        
                        first.Proportion = 0.5;
                        second.Proportion = 0.5;
                        
                        Log.Information("*** General proportions SET to 50/50 ***");
                        
                        // Verify the proportions were actually set
                        Log.Information("*** VERIFICATION - First proportion after set: {FirstProp}, Second after set: {SecondProp} ***", 
                            first.Proportion, second.Proportion);
                        
                        // Note: We've set the proportions, but the visual layout may need time to update
                    }
                    else
                    {
                        Log.Information("*** General proportions already equal - no fix needed ***");
                    }
                }
            }
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
            System.Console.WriteLine($"InitLayout called. Layout type: {layout?.GetType().Name}");
            if (layout is RootDock rootDock)
            {
                System.Console.WriteLine($"RootDock has {rootDock.VisibleDockables?.Count ?? 0} visible dockables");
            }
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
            System.Console.WriteLine("Host windows initialized for multi-window support");
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
        /// Recursively traverse the dock layout and clean up empty splits after drag operations
        /// </summary>
        private void CleanupEmptySplits()
        {
            try
            {
                Log.Information("*** CleanupEmptySplits called - starting iterative cleanup ***");
                int totalRemoved = 0;
                int iteration = 0;
                
                // Keep running cleanup until no more empty splits are found
                // This ensures that removing one empty split doesn't leave parent splits empty
                while (true)
                {
                    iteration++;
                    Log.Information("*** CleanupEmptySplits iteration {Iteration} ***", iteration);
                    
                    var emptySplits = new List<IDock>();
                    
                    if (_context is IDock rootDock && rootDock.VisibleDockables != null)
                    {
                        foreach (var dockable in rootDock.VisibleDockables)
                        {
                            if (dockable is IDock dock)
                            {
                                FindEmptySplits(dock, emptySplits);
                            }
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
                    var nonSplitterDockables = proportionalDock.VisibleDockables
                        .Where(d => !(d is ProportionalDockSplitter))
                        .ToList();
                    
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
                    Log.Information("*** Dock {DockId} is empty - no visible dockables ***", dock?.Id ?? "null");
                    return true;
                }
                
                // For DocumentDock, check if it has any actual documents
                if (dock is DocumentDock documentDock)
                {
                    var documents = documentDock.VisibleDockables?.OfType<Document>().ToList() ?? new List<Document>();
                    bool isEmpty = documents.Count == 0;
                    
                    Log.Information("*** DocumentDock {DockId} has {DocumentCount} documents - Empty: {IsEmpty} ***", 
                        documentDock.Id ?? "null", documents.Count, isEmpty);
                    
                    return isEmpty;
                }
                
                // For ToolDock, check if it has any actual tools
                if (dock is ToolDock toolDock)
                {
                    var tools = toolDock.VisibleDockables?.OfType<Tool>().ToList() ?? new List<Tool>();
                    bool isEmpty = tools.Count == 0;
                    
                    Log.Debug("*** ToolDock {DockId} has {ToolCount} tools - Empty: {IsEmpty} ***", 
                        toolDock.Id ?? "null", tools.Count, isEmpty);
                    
                    return isEmpty;
                }
                
                // For ProportionalDock, recursively check if all children are empty
                if (dock is ProportionalDock proportionalDock)
                {
                    var nonSplitterChildren = proportionalDock.VisibleDockables
                        .Where(d => !(d is ProportionalDockSplitter))
                        .ToList();
                    
                    if (nonSplitterChildren.Count == 0)
                    {
                        Log.Debug("*** ProportionalDock {DockId} is empty - no non-splitter children ***", 
                            proportionalDock.Id ?? "null");
                        return true;
                    }
                    
                    bool allChildrenEmpty = true;
                    foreach (var child in nonSplitterChildren)
                    {
                        if (child is IDock childDock)
                        {
                            if (!IsEmptyDock(childDock))
                            {
                                allChildrenEmpty = false;
                                break;
                            }
                        }
                        else
                        {
                            // Non-dock dockable - not empty
                            allChildrenEmpty = false;
                            break;
                        }
                    }
                    
                    Log.Debug("*** ProportionalDock {DockId} with {ChildCount} children - All empty: {AllEmpty} ***", 
                        proportionalDock.Id ?? "null", nonSplitterChildren.Count, allChildrenEmpty);
                    
                    return allChildrenEmpty;
                }
                
                // Default: if dock has any visible dockables, it's not empty
                Log.Debug("*** Dock {DockId} ({DockType}) has {DockableCount} dockables - not empty ***", 
                    dock.Id ?? "null", dock.GetType().Name, dock.VisibleDockables.Count);
                
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
                    Log.Information("*** Found parent dock: {ParentType} (ID: {ParentId}) - removing empty child ***", 
                        parent.GetType().Name, parent.Id ?? "null");
                    
                    parent.VisibleDockables.Remove(emptySplit);
                    
                    // If parent is also a ProportionalDock, clean up splitters
                    if (parent is ProportionalDock parentProportional)
                    {
                        CleanupSplitters(parentProportional);
                    }
                    
                    Log.Information("*** Successfully removed empty split from parent ***");
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
        /// Find the parent dock of a given dock
        /// </summary>
        private IDock? FindParentDock(IDock targetDock)
        {
            try
            {
                if (!(_context is IDock rootDock) || rootDock.VisibleDockables == null)
                    return null;
                
                return FindParentDockRecursive(rootDock, targetDock);
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
        }
        
    }
}