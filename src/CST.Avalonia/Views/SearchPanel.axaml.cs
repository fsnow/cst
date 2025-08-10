using System;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using CST.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;

namespace CST.Avalonia.Views;

public partial class SearchPanel : UserControl
{
    public SearchPanel()
    {
        InitializeComponent();
        
        // Set up the view model
        DataContext = App.ServiceProvider?.GetService<SearchViewModel>() ?? 
                     new SearchViewModel();
        
        SetupEventHandlers();
    }
    
    private void SetupEventHandlers()
    {
        // Handle double-click on occurrences list to open book
        var occurrencesList = this.FindControl<ListBox>("OccurrencesList");
        if (occurrencesList != null)
        {
            occurrencesList.DoubleTapped += OnOccurrenceDoubleClick;
        }
        
        // Handle Enter key in search box
        var searchInput = this.FindControl<TextBox>("SearchInput");
        if (searchInput != null)
        {
            searchInput.KeyDown += OnSearchInputKeyDown;
        }
        
        // Handle search mode combo selection
        var searchModeCombo = this.FindControl<ComboBox>("SearchModeCombo");
        if (searchModeCombo != null)
        {
            searchModeCombo.SelectionChanged += OnSearchModeChanged;
        }
        
        // Set up ViewModel event handlers
        if (DataContext is SearchViewModel viewModel)
        {
            viewModel.OpenBookRequested += OnOpenBookRequested;
        }
    }
    
    private void OnOccurrenceDoubleClick(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox listBox && 
            listBox.SelectedItem is BookOccurrenceViewModel occurrence &&
            DataContext is SearchViewModel viewModel)
        {
            // Execute the OpenBook command
            viewModel.OpenBookCommand.Execute(occurrence).Subscribe();
        }
    }
    
    private void OnSearchInputKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter && DataContext is SearchViewModel viewModel)
        {
            // Execute search on Enter
            viewModel.SearchCommand.Execute().Subscribe();
        }
        else if (e.Key == Key.Escape && DataContext is SearchViewModel vm)
        {
            // Clear search on Escape
            vm.ClearCommand.Execute().Subscribe();
        }
    }
    
    private void OnSearchModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ComboBox comboBox && 
            comboBox.SelectedItem is ComboBoxItem item &&
            item.Tag is Models.SearchMode mode &&
            DataContext is SearchViewModel viewModel)
        {
            viewModel.SelectedSearchMode = mode;
        }
    }
    
    private void OnOpenBookRequested(object? sender, OpenBookWithSearchEventArgs e)
    {
        // This event will be handled by the main application to open the book
        // For now, we can bubble it up through the visual tree or use a message bus
        // The actual implementation will depend on how the dock system handles this
        
        // TODO: Implement book opening through the dock system
        // This might involve:
        // 1. Getting reference to the main dock factory
        // 2. Creating a new BookDisplayViewModel with search terms
        // 3. Adding it as a new dockable to the document area
        
        Console.WriteLine($"Search panel requesting to open book: {e.Book.FileName} with {e.SearchTerms.Count} search terms");
    }
    
    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);
        
        // Re-setup event handlers when DataContext changes
        if (DataContext is SearchViewModel viewModel)
        {
            viewModel.OpenBookRequested += OnOpenBookRequested;
        }
    }
}