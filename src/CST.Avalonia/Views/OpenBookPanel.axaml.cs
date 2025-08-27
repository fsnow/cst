using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CST.Avalonia.ViewModels;
using CST.Conversion;
using Serilog;

namespace CST.Avalonia.Views;

public partial class OpenBookPanel : UserControl
{
    public OpenBookPanel()
    {
        InitializeComponent();
        
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
        
        // Get the DataContext (OpenBookDialogViewModel)
        if (DataContext is OpenBookDialogViewModel viewModel && viewModel.SelectedNode != null)
        {
            Log.Debug("[OpenBookPanel] Selected node: {NodeName} (Type: {NodeType})", 
                viewModel.SelectedNode.DisplayName, viewModel.SelectedNode.NodeType);
            
            // Only open if it's a book node (leaf node)
            if (viewModel.SelectedNode.NodeType == BookTreeNodeType.Book)
            {
                Log.Debug("[OpenBookPanel] Checking if command can execute for book: {FileName}", 
                    viewModel.SelectedNode.CstBook?.FileName ?? "null");
                
                // Execute the open book command
                if (viewModel.OpenBookCommand.CanExecute(viewModel.SelectedNode))
                {
                    Log.Information("[OpenBookPanel] Opening book from tree: {FileName}", 
                        viewModel.SelectedNode.CstBook?.FileName ?? "null");
                    viewModel.OpenBookCommand.Execute(viewModel.SelectedNode);
                    Log.Debug("[OpenBookPanel] OpenBookCommand.Execute completed");
                }
                else
                {
                    Log.Debug("[OpenBookPanel] Command cannot execute");
                }
            }
            else
            {
                Log.Debug("[OpenBookPanel] Not a book node, ignoring double-tap");
            }
        }
        else
        {
            Log.Debug("[OpenBookPanel] No valid DataContext or SelectedNode");
        }
    }
}