using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
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
        
        // Handle terms list selection changes - try both events
        var termsList = this.FindControl<ListBox>("TermsList");
        if (termsList != null)
        {
            termsList.SelectionChanged += OnTermsListSelectionChanged;
            termsList.Tapped += OnTermsListTapped;
        }
        
        // Note: We handle book opening directly in OnOccurrenceDoubleClick
        // No need to subscribe to OpenBookRequested event anymore
    }
    
    private static bool _isOpening = false;
    
    private void OnOccurrenceDoubleClick(object? sender, TappedEventArgs e)
    {
        // Prevent multiple rapid calls
        if (_isOpening)
        {
            Console.WriteLine("*** Book opening already in progress - ignoring duplicate call ***");
            e.Handled = true;
            return;
        }
        
        if (sender is ListBox listBox && 
            listBox.SelectedItem is BookOccurrenceViewModel occurrence)
        {
            _isOpening = true;
            e.Handled = true;
            
            try
            {
                // Open book directly using the same method as Select a Book tree
                var mainWindow = TopLevel.GetTopLevel(this) as Window;
                var layoutViewModel = mainWindow?.DataContext as LayoutViewModel;
                
                if (layoutViewModel != null)
                {
                    Console.WriteLine($"*** [SEARCH PANEL] Opening book: {occurrence.Book.FileName} ***");
                    Console.WriteLine($"*** [SEARCH PANEL] Before layoutViewModel.OpenBook call ***");
                    layoutViewModel.OpenBook(occurrence.Book);
                    Console.WriteLine($"*** [SEARCH PANEL] After layoutViewModel.OpenBook call ***");
                }
                else
                {
                    Console.WriteLine("*** [SEARCH PANEL] No layout view model found ***");
                }
            }
            finally
            {
                // Reset flag after a short delay to allow for legitimate double-clicks
                Task.Delay(500).ContinueWith(_ => _isOpening = false);
            }
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
        // Selection changes are handled automatically through binding
        // This method can be removed or used for additional logic if needed
    }
    
    private void OnTermsListSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            Console.WriteLine($"*** Terms ListBox Selection Changed: {listBox.SelectedItems?.Count ?? 0} items selected ***");
            SyncSelection(listBox);
        }
    }
    
    private void OnTermsListTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            Console.WriteLine($"*** Terms ListBox Tapped: {listBox.SelectedItems?.Count ?? 0} items selected ***");
            
            // Force a selection update after tap
            Dispatcher.UIThread.Post(() => 
            {
                Console.WriteLine($"*** After tap dispatch: {listBox.SelectedItems?.Count ?? 0} items selected ***");
                SyncSelection(listBox);
            }, DispatcherPriority.Background);
        }
    }
    
    private void SyncSelection(ListBox listBox)
    {
        if (DataContext is SearchViewModel viewModel)
        {
            Console.WriteLine($"*** ViewModel SelectedTerms count: {viewModel.SelectedTerms.Count} ***");
            Console.WriteLine($"*** ViewModel Occurrences count: {viewModel.Occurrences.Count} ***");
            
            // Manually sync the selection since binding might not be working
            Console.WriteLine($"*** Before sync - SelectedTerms count: {viewModel.SelectedTerms.Count} ***");
            viewModel.SelectedTerms.Clear();
            Console.WriteLine($"*** After clear - SelectedTerms count: {viewModel.SelectedTerms.Count} ***");
            
            if (listBox.SelectedItems != null)
            {
                Console.WriteLine($"*** ListBox has {listBox.SelectedItems.Count} selected items ***");
                foreach (var item in listBox.SelectedItems.OfType<MatchingTermViewModel>())
                {
                    Console.WriteLine($"*** Adding selected term: {item.DisplayTerm} with {item.Occurrences?.Count ?? 0} occurrences ***");
                    viewModel.SelectedTerms.Add(item);
                }
            }
            else
            {
                Console.WriteLine("*** ListBox.SelectedItems is null ***");
            }
            Console.WriteLine($"*** After manual sync - SelectedTerms count: {viewModel.SelectedTerms.Count} ***");
            
            // Explicitly trigger UpdateOccurrences since manual sync might not fire CollectionChanged
            Console.WriteLine("*** Explicitly calling UpdateOccurrences ***");
            try 
            {
                var updateOccurrencesMethod = typeof(SearchViewModel).GetMethod("UpdateOccurrences", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                updateOccurrencesMethod?.Invoke(viewModel, null);
                Console.WriteLine("*** UpdateOccurrences called successfully ***");
                
                // Also explicitly call UpdateStatistics
                var updateStatisticsMethod = typeof(SearchViewModel).GetMethod("UpdateStatistics", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                updateStatisticsMethod?.Invoke(viewModel, null);
                Console.WriteLine("*** UpdateStatistics called successfully ***");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"*** Error calling update methods: {ex.Message} ***");
            }
        }
    }
    
    // OnOpenBookRequested method removed - we now handle book opening directly in OnOccurrenceDoubleClick
    
    // No longer need OnDataContextChanged since we handle book opening directly
}