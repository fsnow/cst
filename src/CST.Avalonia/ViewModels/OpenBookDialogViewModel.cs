using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CST.Avalonia.Commands;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Conversion;
using CstBook = CST.Book;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace CST.Avalonia.ViewModels;

/// <summary>
/// ViewModel for the Open Book dialog - replacement for FormSelectBook
/// Provides hierarchical tree navigation of Buddhist texts with state persistence
/// </summary>
public class OpenBookDialogViewModel : ViewModelBase, IDisposable
{
    private readonly ILocalizationService _localizationService;
    private readonly IScriptService _scriptService;
    private readonly IApplicationStateService _stateService;
    private readonly TreeStateService _treeStateService;
    private readonly ILogger<OpenBookDialogViewModel> _logger;
    private readonly Dictionary<string, BookTreeNode> _nodeCache = new();

    public OpenBookDialogViewModel(
        ILocalizationService localizationService,
        IScriptService scriptService,
        IApplicationStateService stateService,
        TreeStateService treeStateService,
        ILogger<OpenBookDialogViewModel> logger)
    {
        _localizationService = localizationService;
        _scriptService = scriptService;
        _stateService = stateService;
        _treeStateService = treeStateService;
        _logger = logger;

        BookTree = new ObservableCollection<BookTreeNode>();
        SelectedNodes = new ObservableCollection<BookTreeNode>();

        // Initialize commands
        OpenBookCommand = new SimpleCommand<BookTreeNode>(OpenBook, CanOpenBook);
        RefreshCommand = new SimpleCommand(async () => await RefreshTreeAsync());
        ExpandAllCommand = new SimpleCommand(ExpandAll);
        CollapseAllCommand = new SimpleCommand(CollapseAll);
        CloseCommand = new SimpleCommand(Close);

        // Subscribe to script changes
        _scriptService.ScriptChanged += OnScriptChanged;

        // Delay tree initialization to allow state to load first
        _ = Task.Run(async () =>
        {
            // Give the application state time to load
            await Task.Delay(100);
            await Dispatcher.UIThread.InvokeAsync(async () => await InitializeAsync());
        });
    }

    // Properties
    public ObservableCollection<BookTreeNode> BookTree { get; }
    public ObservableCollection<BookTreeNode> SelectedNodes { get; }

    private BookTreeNode? _selectedNode;
    public BookTreeNode? SelectedNode
    {
        get => _selectedNode;
        set
        {
            _selectedNode = value;
            this.RaisePropertyChanged();
            this.RaisePropertyChanged(nameof(SelectedBookInfo));
            ((SimpleCommand<BookTreeNode>)OpenBookCommand).RaiseCanExecuteChanged();
        }
    }

    private string _statusText = "Loading books...";
    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            this.RaisePropertyChanged();
        }
    }

    private int _totalBooks = 0;
    public int TotalBooks
    {
        get => _totalBooks;
        set
        {
            _totalBooks = value;
            this.RaisePropertyChanged();
        }
    }

    private bool _isLoading = true;
    public bool IsLoading
    {
        get => _isLoading;
        set
        {
            _isLoading = value;
            this.RaisePropertyChanged();
        }
    }

    public string SelectedBookInfo
    {
        get
        {
            if (SelectedNode?.CstBook != null)
            {
                var cstBook = SelectedNode.CstBook;
                return $"Selected: {SelectedNode.DisplayName}\n" +
                       $"File: {cstBook.FileName}\n" +
                       $"Collection: {cstBook.Pitaka}\n" +
                       $"Type: {cstBook.Matn}";
            }
            else if (SelectedNode != null)
            {
                return $"Category: {SelectedNode.DisplayName}\n" +
                       $"Contains: {SelectedNode.Children.Count} items";
            }
            return "No selection";
        }
    }

    // Commands
    public ICommand OpenBookCommand { get; }
    public ICommand RefreshCommand { get; }
    public ICommand ExpandAllCommand { get; }
    public ICommand CollapseAllCommand { get; }
    public ICommand CloseCommand { get; }

    // Events
    public event Action<CstBook>? BookOpenRequested;
    public event Action? CloseRequested;

    // Script properties
    public Script CurrentScript 
    { 
        get => _scriptService.CurrentScript;
        set => _scriptService.CurrentScript = value;
    }
    public IReadOnlyList<Script> AvailableScripts => _scriptService.AvailableScripts;

    // Methods
    private async Task InitializeAsync()
    {
        try
        {
            IsLoading = true;
            StatusText = "Loading book collection...";
            _logger.LogInformation("InitializeAsync started. Current script from ScriptService: {Script}", _scriptService.CurrentScript);

            // Books are loaded automatically via CST.Books.Inst
            await BuildBookTreeAsync();

            StatusText = $"Ready - {TotalBooks} books available";
            _logger.LogInformation("Open Book dialog initialized with {BookCount} books using script {Script}", TotalBooks, _scriptService.CurrentScript);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize Open Book dialog");
            StatusText = "Failed to load books";
        }
        finally
        {
            IsLoading = false;
        }
    }

    private async Task BuildBookTreeAsync()
    {
        var cstBooks = CST.Books.Inst;
        
        var rootNodes = await Task.Run(() =>
        {
            var nodes = new Dictionary<string, BookTreeNode>();

            // Build tree exactly like FormSelectBook does
            foreach (var book in cstBooks)
            {
                string[] parts = book.LongNavPath.Split('/');
                
                // Add root node if it doesn't exist
                BookTreeNode? node = null;
                if (nodes.TryGetValue(parts[0], out node) == false)
                {
                    node = new BookTreeNode
                    {
                        OriginalDevanagariText = parts[0],
                        DisplayName = GetNodeText(parts[0]),
                        FullPath = parts[0],
                        Level = 0,
                        NodeType = BookTreeNodeType.Category
                    };
                    nodes[parts[0]] = node;
                }

                // Add everything under the root
                for (int i = 1; i < parts.Length; i++)
                {
                    BookTreeNode? node2 = node.Children.FirstOrDefault(n => n.OriginalDevanagariText == parts[i]);
                    if (node2 == null)
                    {
                        node2 = new BookTreeNode
                        {
                            OriginalDevanagariText = parts[i],
                            DisplayName = GetNodeText(parts[i]),
                            FullPath = string.Join("/", parts.Take(i + 1)),
                            Level = i,
                            Parent = node,
                            NodeType = (i == parts.Length - 1) ? BookTreeNodeType.Book : BookTreeNodeType.Category
                        };

                        // If this is a leaf node, attach the book
                        if (i == parts.Length - 1)
                        {
                            node2.CstBook = book;
                        }

                        node.Children.Add(node2);
                    }
                    
                    node = node2;
                }
            }
            
            return nodes;
        });

        await Dispatcher.UIThread.InvokeAsync(() =>
        {
            BookTree.Clear();
            
            // Maintain insertion order like CST4 - don't sort alphabetically
            var orderedRootNodes = new List<BookTreeNode>();
            var seenRootNames = new HashSet<string>();
            
            // Iterate through books in order to preserve root node insertion order
            foreach (var book in cstBooks)
            {
                string rootName = book.LongNavPath.Split('/')[0];
                if (!seenRootNames.Contains(rootName))
                {
                    seenRootNames.Add(rootName);
                    orderedRootNodes.Add(rootNodes[rootName]);
                }
            }
            
            foreach (var rootNode in orderedRootNodes)
            {
                BookTree.Add(rootNode);
            }

            TotalBooks = cstBooks.Count();
            CalculateChildCounts();
            
            // Debug logging
            _logger.LogInformation("BookTree populated with {Count} root nodes", BookTree.Count);
            
            // Trigger property change to ensure UI updates
            this.RaisePropertyChanged(nameof(BookTree));
            
            // Restore tree expansion state after tree is built
            _ = RestoreTreeExpansionState();
        });
    }


    private void CalculateChildCounts()
    {
        foreach (var rootNode in BookTree)
        {
            CalculateChildCountRecursive(rootNode);
        }
    }

    private int CalculateChildCountRecursive(BookTreeNode node)
    {
        if (node.NodeType == BookTreeNodeType.Book)
        {
            node.BookCount = 1;
            return 1;
        }

        var totalBooks = 0;
        foreach (var child in node.Children)
        {
            totalBooks += CalculateChildCountRecursive(child);
        }

        node.BookCount = totalBooks;
        return totalBooks;
    }

    private async Task RefreshTreeAsync()
    {
        _logger.LogInformation("Refreshing book tree");
        await BuildBookTreeAsync();
    }

    private void ExpandAll()
    {
        foreach (var rootNode in BookTree)
        {
            ExpandNodeRecursive(rootNode);
        }
        _logger.LogInformation("Expanded all tree nodes");
    }

    private void CollapseAll()
    {
        foreach (var rootNode in BookTree)
        {
            CollapseNodeRecursive(rootNode);
        }
        _logger.LogInformation("Collapsed all tree nodes");
    }

    private void ExpandNodeRecursive(BookTreeNode node)
    {
        node.IsExpanded = true;
        foreach (var child in node.Children)
        {
            ExpandNodeRecursive(child);
        }
    }

    private void CollapseNodeRecursive(BookTreeNode node)
    {
        node.IsExpanded = false;
        foreach (var child in node.Children)
        {
            CollapseNodeRecursive(child);
        }
    }

    private bool CanOpenBook(BookTreeNode? node)
    {
        return node?.NodeType == BookTreeNodeType.Book && node.CstBook != null;
    }

    private void OpenBook(BookTreeNode? node)
    {
        var timestamp = DateTime.UtcNow;
        System.Console.WriteLine($"*** [OPEN BOOK COMMAND] OpenBook method called at {timestamp:HH:mm:ss.fff} for: {node?.CstBook?.FileName ?? "null"} ***");
        
        if (!CanOpenBook(node) || node?.CstBook == null)
        {
            System.Console.WriteLine($"*** [OPEN BOOK COMMAND] Cannot open book - CanOpenBook: {CanOpenBook(node)}, CstBook: {node?.CstBook != null} ***");
            return;
        }

        _logger.LogInformation("Opening book: {BookFileName}", node.CstBook.FileName);
        System.Console.WriteLine($"*** [OPEN BOOK COMMAND] Invoking BookOpenRequested event for: {node.CstBook.FileName} at {DateTime.UtcNow:HH:mm:ss.fff} ***");
        BookOpenRequested?.Invoke(node.CstBook);
        System.Console.WriteLine($"*** [OPEN BOOK COMMAND] BookOpenRequested event invocation completed at {DateTime.UtcNow:HH:mm:ss.fff} ***");
    }

    private void Close()
    {
        _logger.LogInformation("Closing Open Book dialog");
        
        // Save current tree state before closing - equivalent to CST4 FormBookOpen_FormClosing
        SaveTreeExpansionState();
        
        CloseRequested?.Invoke();
    }

    /// <summary>
    /// Save current tree expansion state - equivalent to CST4 GetNodeStates()
    /// </summary>
    private void SaveTreeExpansionState()
    {
        try
        {
            if (BookTree.Count == 0)
                return;

            var expansionStates = _treeStateService.CollectExpansionStates(BookTree);
            var treeVersion = _treeStateService.GenerateTreeVersion(BookTree);
            var totalNodeCount = _treeStateService.CountAllNodes(BookTree);

            _stateService.SetTreeExpansionStates(expansionStates, treeVersion, totalNodeCount);
            
            // Also save selected book path for restoration
            if (SelectedNode?.CstBook != null)
            {
                var dialogState = _stateService.Current.OpenBookDialog;
                dialogState.SelectedBookPath = SelectedNode.CstBook.LongNavPath;
                _stateService.UpdateOpenBookDialogState(dialogState);
            }

            _logger.LogDebug("Saved tree expansion state with {NodeCount} nodes", totalNodeCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to save tree expansion state");
        }
    }

    /// <summary>
    /// Restore tree expansion state - equivalent to CST4 SetNodeStates()
    /// </summary>
    private async Task RestoreTreeExpansionState()
    {
        try
        {
            if (BookTree.Count == 0)
                return;

            var dialogState = _stateService.Current.OpenBookDialog;
            var savedStates = dialogState.TreeExpansionStates.ToArray();
            
            if (savedStates.Length == 0)
            {
                _logger.LogDebug("No saved tree expansion states found");
                return;
            }

            // Validate tree structure hasn't changed
            if (!_treeStateService.ValidateTreeStructure(BookTree, dialogState.TreeVersion, dialogState.TotalNodeCount))
            {
                _logger.LogInformation("Tree structure changed, skipping expansion state restoration");
                return;
            }

            // Apply saved expansion states
            var success = _treeStateService.ApplyExpansionStates(BookTree, savedStates);
            if (success)
            {
                _logger.LogDebug("Restored tree expansion state for {NodeCount} nodes", savedStates.Length);
                
                // Try to restore selected book
                await RestoreSelectedBook(dialogState.SelectedBookPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore tree expansion state");
        }
    }

    /// <summary>
    /// Restore selected book by navigation path
    /// </summary>
    private async Task RestoreSelectedBook(string? selectedBookPath)
    {
        if (string.IsNullOrEmpty(selectedBookPath))
            return;

        try
        {
            await Task.Run(() =>
            {
                var bookNode = FindBookNodeByPath(BookTree, selectedBookPath);
                if (bookNode != null)
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        SelectedNode = bookNode;
                        _logger.LogDebug("Restored book selection: {BookPath}", selectedBookPath);
                    });
                }
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to restore selected book: {BookPath}", selectedBookPath);
        }
    }

    /// <summary>
    /// Find a book node by its navigation path
    /// </summary>
    private BookTreeNode? FindBookNodeByPath(IEnumerable<BookTreeNode> nodes, string path)
    {
        foreach (var node in nodes)
        {
            if (node.CstBook?.LongNavPath == path)
                return node;
                
            if (node.Children.Count > 0)
            {
                var found = FindBookNodeByPath(node.Children, path);
                if (found != null)
                    return found;
            }
        }
        return null;
    }

    /// <summary>
    /// Convert Devanagari text to current script - equivalent to FormSelectBook.GetNodeText()
    /// </summary>
    private string GetNodeText(string devanagariText)
    {
        var result = _scriptService.ConvertToCurrentScript(devanagariText);
        if (!_nodeCache.ContainsKey(devanagariText))
        {
            _logger.LogDebug("GetNodeText converting '{Text}' using script {Script}, result: '{Result}'", 
                devanagariText, _scriptService.CurrentScript, result);
        }
        return result;
    }
    
    /// <summary>
    /// Update the tree display to use the specified script
    /// </summary>
    public void UpdateTreeScript(Script newScript)
    {
        // Update all nodes in the tree to display the new script
        UpdateNodeScripts(BookTree, newScript);
    }
    
    private void UpdateNodeScripts(IEnumerable<BookTreeNode> nodes, Script script)
    {
        foreach (var node in nodes)
        {
            // If node has original Devanagari text, convert it to the selected script
            if (!string.IsNullOrEmpty(node.OriginalDevanagariText))
            {
                if (script == Script.Devanagari)
                {
                    node.DisplayName = node.OriginalDevanagariText;
                }
                else
                {
                    // Convert from Devanagari to the target script
                    node.DisplayName = ScriptConverter.Convert(node.OriginalDevanagariText, Script.Devanagari, script);
                }
            }
            
            // Recursively update children
            if (node.Children.Any())
            {
                UpdateNodeScripts(node.Children, script);
            }
        }
    }

    /// <summary>
    /// Handle script changes - equivalent to FormSelectBook.ChangeScript()
    /// </summary>
    private void OnScriptChanged(Script newScript)
    {
        _logger.LogInformation("Script changed to {Script}, updating tree display", newScript);
        
        // Update all node display names
        Dispatcher.UIThread.Post(() =>
        {
            UpdateNodeDisplayNamesRecursive(BookTree);
            this.RaisePropertyChanged(nameof(CurrentScript));
        });
    }

    /// <summary>
    /// Update display names for all nodes recursively - equivalent to FormSelectBook.ChangeScript(TreeNodeCollection)
    /// </summary>
    private void UpdateNodeDisplayNamesRecursive(IEnumerable<BookTreeNode> nodes)
    {
        foreach (var node in nodes)
        {
            // Convert the original Devanagari text to current script
            node.DisplayName = GetNodeText(node.OriginalDevanagariText);
            
            // Recursively update children
            if (node.Children.Count > 0)
            {
                UpdateNodeDisplayNamesRecursive(node.Children);
            }
        }
    }

    public void Dispose()
    {
        _scriptService.ScriptChanged -= OnScriptChanged;
    }
}

/// <summary>
/// Tree node for hierarchical book navigation
/// </summary>
public class BookTreeNode : INotifyPropertyChanged
{
    private bool _isExpanded = false;
    private bool _isSelected = false;
    private string _displayName = string.Empty;

    public string DisplayName 
    { 
        get => _displayName;
        set
        {
            _displayName = value;
            OnPropertyChanged();
        }
    }
    
    public string OriginalDevanagariText { get; set; } = string.Empty;
    public string FullPath { get; set; } = string.Empty;
    public int Level { get; set; }
    public BookTreeNodeType NodeType { get; set; }
    public int BookCount { get; set; }

    // Data references  
    public CstBook? CstBook { get; set; }

    // Tree structure
    public BookTreeNode? Parent { get; set; }
    public ObservableCollection<BookTreeNode> Children { get; } = new();

    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    // Computed properties
    public bool HasChildren => Children.Count > 0;
    public bool IsCategory => NodeType == BookTreeNodeType.Category;
    public bool IsBook => NodeType == BookTreeNodeType.Book;
    public string IconKey => IsBook ? "BookIcon" : (IsExpanded ? "FolderOpenIcon" : "FolderClosedIcon");
    public string NodeInfo => IsBook ? $"Book: {DisplayName}" : $"Category: {DisplayName} ({BookCount} books)";

    // INotifyPropertyChanged implementation
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Type of tree node
/// </summary>
public enum BookTreeNodeType
{
    Category,   // Folder/container node
    Book        // Leaf node representing an actual book
}