using Avalonia.Controls;
using Avalonia.Input;
using CST.Avalonia.ViewModels;

namespace CST.Avalonia.Views;

public partial class OpenBookDialog : Window
{
    public OpenBookDialog()
    {
        InitializeComponent();
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        // Handle Enter key to open selected book (like original FormSelectBook)
        if (e.Key == Key.Enter && DataContext is OpenBookDialogViewModel viewModel)
        {
            if (viewModel.SelectedNode?.IsBook == true)
            {
                viewModel.OpenBookCommand.Execute(viewModel.SelectedNode);
                e.Handled = true;
                return;
            }
        }

        base.OnKeyDown(e);
    }
}