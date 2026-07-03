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
        
        // Enter/Escape on the search box are wired once via the TextBox.KeyBindings in the axaml
        // (SearchCommand / ClearCommand) — no code-behind KeyDown handler (SRCH-13).

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

        // Restore a saved term selection (#87) into the list box once results are ready. Setting the
        // list box selection drives the normal SelectionChanged -> SyncSelection path (visual highlight
        // + occurrences), which a VM-only collection change cannot do.
        if (DataContext is SearchViewModel vm)
        {
            vm.SearchResultsReady += OnSearchResultsReady;
            // The restore search may have already completed before this view subscribed; apply now if so
            // (no-op while results aren't in yet - the event will fire when they are).
            OnSearchResultsReady();
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
    
    private void OnSearchModeChanged(object? sender, SelectionChangedEventArgs e)
    {
        // Selection changes are handled automatically through binding
        // This method can be removed or used for additional logic if needed
    }
    
    private void OnSearchResultsReady()
    {
        if (DataContext is not SearchViewModel vm)
            return;
        if (vm.Terms.Count == 0)
            return; // results not in yet - the event fires again once the term list is populated

        var termsList = this.FindControl<ListBox>("TermsList");
        if (termsList?.SelectedItems == null)
            return;

        // The ListBox has no SelectedItems binding, so push the visual selection here to match the VM:
        // the saved restore set (#87) when a restore is pending, otherwise whatever the VM auto-selected
        // for a fresh search (e.g. the single-term match). Without the fresh-search case the auto-selected
        // term filled the Books pane but rendered UNselected, and a stray tap on the list then cleared it. (SRCH-10)
        var pending = vm.PendingSelectedTerms;
        var toSelect = pending is { Count: > 0 }
            ? vm.Terms.Where(t => pending.Contains(t.Term)).ToList()
            : vm.SelectedTerms.ToList();

        termsList.SelectedItems.Clear();
        foreach (var term in toSelect)
            termsList.SelectedItems.Add(term);

        if (pending is { Count: > 0 })
            vm.ClearPendingSelectedTerms();

        Log.Information("[SearchPanel] Selected {Count} term(s) in the list box", termsList.SelectedItems.Count);

        // Selecting items auto-scrolls to the last selected one; bring the list back to the top so the
        // term list opens at the top rather than scrolled down. (#87)
        if (termsList.ItemCount > 0)
            Dispatcher.UIThread.Post(() => termsList.ScrollIntoView(0), DispatcherPriority.Background);
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
            // UpdateOccurrences/UpdateStatistics run automatically off the VM's ctor-level
            // SelectedTerms.CollectionChanged + statistics subscriptions — no reflection needed. (SRCH-10)
        }
    }
    
    // OnOpenBookRequested method removed - we now handle book opening directly in OnOccurrenceDoubleClick
    
    // No longer need OnDataContextChanged since we handle book opening directly
}