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
    
    private static readonly object _openLock = new object();
    private static DateTime _lastOpenTime = DateTime.MinValue;
    private static string? _lastOpenedBook = null;
    
    private void OnOccurrenceDoubleClick(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox listBox && 
            listBox.SelectedItem is BookOccurrenceViewModel occurrence)
        {
            e.Handled = true;
            
            lock (_openLock)
            {
                var now = DateTime.UtcNow;
                var timeSinceLastOpen = now - _lastOpenTime;
                
                // Prevent duplicate opens of the same book within 1 second
                if (occurrence.Book.FileName == _lastOpenedBook && timeSinceLastOpen.TotalMilliseconds < 1000)
                {
                    Console.WriteLine($"*** [SEARCH PANEL] DUPLICATE PREVENTED: {occurrence.Book.FileName} (last opened {timeSinceLastOpen.TotalMilliseconds:F0}ms ago) ***");
                    return;
                }
                
                _lastOpenTime = now;
                _lastOpenedBook = occurrence.Book.FileName;
            }
            
            try
            {
                // Open book with search terms for highlighting
                var mainWindow = TopLevel.GetTopLevel(this) as Window;
                var layoutViewModel = mainWindow?.DataContext as LayoutViewModel;
                
                if (layoutViewModel != null && DataContext is SearchViewModel searchViewModel)
                {
                    // Collect search terms from selected terms for highlighting
                    var searchTerms = searchViewModel.SelectedTerms.Select(t => t.Term).ToList();
                    
                    Console.WriteLine($"*** [SEARCH PANEL] Opening book with search highlighting: {occurrence.Book.FileName} with {searchTerms.Count} search terms ***");
                    
                    // Use the search-specific method to open book with highlighting
                    layoutViewModel.Factory.OpenBookInNewTab(occurrence.Book, searchTerms, occurrence.Positions);
                    
                    Console.WriteLine($"*** [SEARCH PANEL] OpenBookInNewTab call completed at {DateTime.UtcNow:HH:mm:ss.fff} ***");
                }
                else
                {
                    Console.WriteLine("*** [SEARCH PANEL] No layout view model or search view model found ***");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"*** [SEARCH PANEL] Error opening book: {ex.Message} ***");
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