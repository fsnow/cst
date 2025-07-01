using Avalonia.Controls;
using Avalonia.Interactivity;
using CST.Avalonia.ViewModels;

namespace CST.Avalonia.Views;

public partial class OpenBookPanel : UserControl
{
    public OpenBookPanel()
    {
        InitializeComponent();
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