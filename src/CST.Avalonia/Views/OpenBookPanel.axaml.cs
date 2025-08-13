using System;
using Avalonia.Controls;
using Avalonia.Interactivity;
using CST.Avalonia.ViewModels;
using CST.Conversion;

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
            System.Console.WriteLine($"OpenBookPanel DataContext changed to: {vm?.GetType().Name ?? "null"}");
            if (vm != null)
            {
                System.Console.WriteLine($"ViewModel has {vm.BookTree.Count} root nodes");
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
        var timestamp = DateTime.UtcNow;
        System.Console.WriteLine($"*** [TREE DOUBLE-TAP] TreeView_DoubleTapped fired at {timestamp:HH:mm:ss.fff} ***");
        
        // Get the DataContext (OpenBookDialogViewModel)
        if (DataContext is OpenBookDialogViewModel viewModel && viewModel.SelectedNode != null)
        {
            System.Console.WriteLine($"*** [TREE DOUBLE-TAP] Selected node: {viewModel.SelectedNode.DisplayName} (Type: {viewModel.SelectedNode.NodeType}) ***");
            
            // Only open if it's a book node (leaf node)
            if (viewModel.SelectedNode.NodeType == BookTreeNodeType.Book)
            {
                System.Console.WriteLine($"*** [TREE DOUBLE-TAP] Checking if command can execute for book: {viewModel.SelectedNode.CstBook?.FileName ?? "null"} ***");
                
                // Execute the open book command
                if (viewModel.OpenBookCommand.CanExecute(viewModel.SelectedNode))
                {
                    System.Console.WriteLine($"*** [TREE DOUBLE-TAP] Executing OpenBookCommand for: {viewModel.SelectedNode.CstBook?.FileName ?? "null"} ***");
                    viewModel.OpenBookCommand.Execute(viewModel.SelectedNode);
                    System.Console.WriteLine($"*** [TREE DOUBLE-TAP] OpenBookCommand.Execute completed at {DateTime.UtcNow:HH:mm:ss.fff} ***");
                }
                else
                {
                    System.Console.WriteLine($"*** [TREE DOUBLE-TAP] Command cannot execute ***");
                }
            }
            else
            {
                System.Console.WriteLine($"*** [TREE DOUBLE-TAP] Not a book node, ignoring double-tap ***");
            }
        }
        else
        {
            System.Console.WriteLine($"*** [TREE DOUBLE-TAP] No valid DataContext or SelectedNode ***");
        }
    }
}