using System;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CST.Avalonia.ViewModels;
using CST.Conversion;
using Serilog;

namespace CST.Avalonia.Views;

public partial class OpenBookPanel : UserControl
{
    public OpenBookPanel()
    {
        InitializeComponent();

        // Enter is claimed on the TUNNEL phase, and it has to be. See TreeView_KeyDown for why the bubble
        // handler this replaces could never fire on a book. Wired through the x:Name field rather than
        // FindControl so a rename is a compile error: a null-tolerant lookup here would silently detach
        // Enter, which is the failure mode #658 was in the first place. (#658)
        BookTreeView.AddHandler(InputElement.KeyDownEvent, TreeView_KeyDown, RoutingStrategies.Tunnel);

        // Debug: Check if DataContext is set
        this.DataContextChanged += (s, e) =>
        {
            var vm = DataContext as OpenBookDialogViewModel;
            Log.Debug("[OpenBookPanel] DataContext changed to: {ViewModelType}", vm?.GetType().Name ?? "null");
            if (vm != null)
            {
                Log.Debug("[OpenBookPanel] ViewModel has {Count} root nodes", vm.BookTree.Count);
            }
        };
    }
    
    // Put keyboard focus on the book tree, so Cmd+O lands the user somewhere they can immediately
    // arrow around. Selects the first root node when nothing is selected yet - a TreeView with no
    // selection swallows the first arrow key otherwise. (#111: Cmd+O)
    public void FocusBookTree()
    {
        var tree = this.FindControl<TreeView>("BookTreeView");
        if (tree == null)
            return;

        if (tree.SelectedItem == null && DataContext is OpenBookDialogViewModel { BookTree.Count: > 0 } viewModel)
            tree.SelectedItem = viewModel.BookTree[0];

        // Open the way to the selection before looking for its container. The remembered book can sit under
        // a branch that was collapsed when state was saved, and Fluent realises no container inside a
        // collapsed branch — so every retry below would miss and Cmd+O would fall back to focusing the
        // TreeView: no scroll, no highlight, nothing visibly happening at all. (#646)
        if (tree.SelectedItem is BookTreeNode selected)
            selected.ExpandAncestors();

        FocusSelectedTreeItem(tree, attemptsLeft: 5);
    }

    // Focus the selected node's TreeViewItem, NOT the TreeView itself: Avalonia routes arrow keys from the
    // focused item container, so focusing the TreeView leaves the tree unnavigable. The container may not
    // be realized yet when the panel was just created or revealed, so retry across a few dispatcher passes
    // rather than giving up on the first miss. (#111: Cmd+O)
    private static void FocusSelectedTreeItem(TreeView tree, int attemptsLeft)
    {
        var selected = tree.SelectedItem;
        var item = tree.GetVisualDescendants().OfType<TreeViewItem>()
            .FirstOrDefault(i => ReferenceEquals(i.DataContext, selected));

        if (item != null)
        {
            item.BringIntoView();
            item.Focus(NavigationMethod.Directional);
            Log.Debug("[OpenBookPanel] Focused tree item {Node}", (selected as BookTreeNode)?.DisplayName ?? "?");
            return;
        }

        if (attemptsLeft > 0)
        {
            Dispatcher.UIThread.Post(() => FocusSelectedTreeItem(tree, attemptsLeft - 1), DispatcherPriority.Background);
            return;
        }

        // Containers never appeared (empty tree, say) — at least put focus in the panel.
        tree.Focus();
        Log.Debug("[OpenBookPanel] No tree item container to focus; focused the TreeView instead");
    }

    public void UpdateScript(Script script)
    {
        if (DataContext is OpenBookDialogViewModel viewModel)
        {
            // Update the script service's current script first
            viewModel.CurrentScript = script;
            // Then update the tree display
            viewModel.UpdateTreeScript(script);
        }
    }

    private void TreeView_DoubleTapped(object? sender, RoutedEventArgs e)
    {
        Log.Debug("[OpenBookPanel] TreeView double-tapped");
        OpenSelectedBook("double-tap");
    }

    // Enter opens the selected book, as it did in CST4 — without it the tree can be reached and navigated
    // by keyboard but not acted on, which is exactly the gap Cmd+O would otherwise leave. (#111)
    //
    // TUNNEL phase, deliberately (#658). This was a bubble handler on the TreeView, and it never once ran
    // for a book: TreeViewItem's own class handler treats Return as expand/collapse, and on a leaf its
    // ExpandItem finds nothing to expand and returns FALSE — leaving the event unhandled, so it bubbled on
    // to the parent category, whose handler collapsed it and marked it handled. Pressing Enter on a book
    // closed the category it was in. Tunnelling routes root -> source, so this sees the key before any item
    // can claim it.
    //
    // Books only. Categories are left to the stock Return handling, which expands/collapses them and gives
    // Ctrl+Return subtree expansion for free. (The branch that used to toggle IsExpanded here was dead for
    // the same routing reason: on a category the item claims Return first, so it never ran either.)
    private void TreeView_KeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter)
            return;

        if (DataContext is not OpenBookDialogViewModel viewModel
            || viewModel.SelectedNode is not { NodeType: BookTreeNodeType.Book })
            return;

        OpenSelectedBook("Enter");
        e.Handled = true;
    }

    // Shared by the double-tap and Enter paths, so the two can't diverge.
    private void OpenSelectedBook(string source)
    {
        if (DataContext is not OpenBookDialogViewModel viewModel || viewModel.SelectedNode == null)
        {
            Log.Debug("[OpenBookPanel] No valid DataContext or SelectedNode");
            return;
        }

        var node = viewModel.SelectedNode;
        Log.Debug("[OpenBookPanel] Selected node: {NodeName} (Type: {NodeType})", node.DisplayName, node.NodeType);

        // Only open if it's a book node (leaf node)
        if (node.NodeType != BookTreeNodeType.Book)
        {
            Log.Debug("[OpenBookPanel] Not a book node, ignoring {Source}", source);
            return;
        }

        if (viewModel.OpenBookCommand.CanExecute(node))
        {
            Log.Information("[OpenBookPanel] Opening book from tree via {Source}: {FileName}",
                source, node.CstBook?.FileName ?? "null");
            viewModel.OpenBookCommand.Execute(node);
        }
        else
        {
            Log.Debug("[OpenBookPanel] Command cannot execute");
        }
    }
}