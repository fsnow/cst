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
        // Get the DataContext (OpenBookDialogViewModel)
        if (DataContext is OpenBookDialogViewModel viewModel && viewModel.SelectedNode != null)
        {
            // Only open if it's a book node (leaf node)
            if (viewModel.SelectedNode.NodeType == BookTreeNodeType.Book)
            {
                // Execute the open book command
                if (viewModel.OpenBookCommand.CanExecute(viewModel.SelectedNode))
                {
                    viewModel.OpenBookCommand.Execute(viewModel.SelectedNode);
                }
            }
        }
    }
}