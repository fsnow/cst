using System;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using CST.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

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
                    Log.Debug("[SearchPanel] Duplicate open prevented for {FileName} (last opened {Milliseconds:F0}ms ago)", 
                        occurrence.Book.FileName, timeSinceLastOpen.TotalMilliseconds);
                    return;
                }
                
                _lastOpenTime = now;
                _lastOpenedBook = occurrence.Book.FileName;
            }
            
            try
            {
                // Open book with search terms for highlighting
                // Use App.MainWindow to handle both docked and floating search panel scenarios
                var layoutViewModel = App.MainWindow?.DataContext as LayoutViewModel;

                if (layoutViewModel != null && DataContext is SearchViewModel searchViewModel)
                {
                    // Collect search terms from selected terms for highlighting
                    var searchTerms = searchViewModel.SelectedTerms.Select(t => t.Term).ToList();
                    
                    Log.Information("[SearchPanel] Opening book {FileName} with {TermCount} search terms for highlighting", 
                        occurrence.Book.FileName, searchTerms.Count);
                    
                    // Use the search-specific method to open book with highlighting
                    layoutViewModel.Factory.OpenBookInNewTab(occurrence.Book, searchTerms, occurrence.Positions);
                    
                    Log.Debug("[SearchPanel] OpenBookInNewTab call completed");
                }
                else
                {
                    Log.Warning("[SearchPanel] Cannot open book - layout or search view model not found");
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[SearchPanel] Error opening book from search results");
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
            Log.Debug("[SearchPanel] Terms list selection changed: {Count} items selected", 
                listBox.SelectedItems?.Count ?? 0);
            SyncSelection(listBox);
        }
    }
    
    private void OnTermsListTapped(object? sender, TappedEventArgs e)
    {
        if (sender is ListBox listBox)
        {
            Log.Debug("[SearchPanel] Terms list tapped: {Count} items selected", 
                listBox.SelectedItems?.Count ?? 0);
            
            // Force a selection update after tap
            Dispatcher.UIThread.Post(() => 
            {
                Log.Debug("[SearchPanel] Post-tap dispatch: {Count} items selected", 
                    listBox.SelectedItems?.Count ?? 0);
                SyncSelection(listBox);
            }, DispatcherPriority.Background);
        }
    }
    
    private void SyncSelection(ListBox listBox)
    {
        if (DataContext is SearchViewModel viewModel)
        {
            Log.Debug("[SearchPanel] ViewModel state - SelectedTerms: {SelectedCount}, Occurrences: {OccurrenceCount}", 
                viewModel.SelectedTerms.Count, viewModel.Occurrences.Count);
            
            // Manually sync the selection since binding might not be working
            Log.Debug("[SearchPanel] Before sync - SelectedTerms count: {Count}", viewModel.SelectedTerms.Count);
            viewModel.SelectedTerms.Clear();
            Log.Debug("[SearchPanel] After clear - SelectedTerms count: {Count}", viewModel.SelectedTerms.Count);
            
            if (listBox.SelectedItems != null)
            {
                Log.Debug("[SearchPanel] ListBox has {Count} selected items", listBox.SelectedItems.Count);
                foreach (var item in listBox.SelectedItems.OfType<MatchingTermViewModel>())
                {
                    Log.Debug("[SearchPanel] Adding selected term: {Term} with {Count} occurrences", 
                        item.DisplayTerm, item.Occurrences?.Count ?? 0);
                    viewModel.SelectedTerms.Add(item);
                }
            }
            else
            {
                Log.Debug("[SearchPanel] ListBox.SelectedItems is null");
            }
            Log.Debug("[SearchPanel] After manual sync - SelectedTerms count: {Count}", viewModel.SelectedTerms.Count);
            
            // Explicitly trigger UpdateOccurrences since manual sync might not fire CollectionChanged
            Log.Debug("[SearchPanel] Explicitly calling UpdateOccurrences");
            try 
            {
                var updateOccurrencesMethod = typeof(SearchViewModel).GetMethod("UpdateOccurrences", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                updateOccurrencesMethod?.Invoke(viewModel, null);
                Log.Debug("[SearchPanel] UpdateOccurrences called successfully");
                
                // Also explicitly call UpdateStatistics
                var updateStatisticsMethod = typeof(SearchViewModel).GetMethod("UpdateStatistics", 
                    System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
                updateStatisticsMethod?.Invoke(viewModel, null);
                Log.Debug("[SearchPanel] UpdateStatistics called successfully");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[SearchPanel] Error calling update methods");
            }
        }
    }
    
    // OnOpenBookRequested method removed - we now handle book opening directly in OnOccurrenceDoubleClick
    
    // No longer need OnDataContextChanged since we handle book opening directly
}