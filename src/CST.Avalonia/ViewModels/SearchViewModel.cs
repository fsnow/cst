using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Conversion;
using CST;
using DynamicData;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace CST.Avalonia.ViewModels;

public class SearchViewModel : ViewModelBase, IActivatableViewModel
{
    private readonly ISearchService _searchService;
    private readonly IScriptService _scriptService;
    private readonly ILogger<SearchViewModel> _logger;
    private CancellationTokenSource? _searchCancellation;

    public SearchViewModel() : this(
        App.ServiceProvider?.GetService(typeof(ISearchService)) as ISearchService ?? throw new InvalidOperationException("SearchService not available"),
        App.ServiceProvider?.GetService(typeof(IScriptService)) as IScriptService ?? throw new InvalidOperationException("ScriptService not available"),
        App.ServiceProvider?.GetService(typeof(ILogger<SearchViewModel>)) as ILogger<SearchViewModel> ?? throw new InvalidOperationException("Logger not available"))
    {
    }

    public SearchViewModel(
        ISearchService searchService,
        IScriptService scriptService,
        ILogger<SearchViewModel> logger)
    {
        _searchService = searchService;
        _scriptService = scriptService;
        _logger = logger;

        Activator = new ViewModelActivator();

        // Initialize collections
        Terms = new ObservableCollection<MatchingTermViewModel>();
        Occurrences = new ObservableCollection<BookOccurrenceViewModel>();
        SelectedTerms = new ObservableCollection<MatchingTermViewModel>();

        // Initialize search modes - Exact match is automatic when no special chars are present
        SearchModes = new ObservableCollection<SearchModeItem>
        {
            new(SearchMode.Wildcard, "Wildcard (*?)"),
            new(SearchMode.Regex, "Regex")
        };

        // Set default to Wildcard mode
        SelectedSearchMode = SearchModes.First();
        
        // Initialize book filters to all true
        IncludeVinaya = true;
        IncludeSutta = true;
        IncludeAbhidhamma = true;
        IncludeMula = true;
        IncludeAttha = true;
        IncludeTika = true;
        IncludeOther = true;

        // Create search command with throttling
        var canSearch = this.WhenAnyValue(
            x => x.SearchText,
            x => x.IsSearching,
            (text, searching) => !string.IsNullOrWhiteSpace(text) && !searching);

        SearchCommand = ReactiveCommand.CreateFromTask(
            ExecuteSearchAsync,
            canSearch);
        
        // Remove test data since we confirmed left panel works

        // Handle search errors
        SearchCommand.ThrownExceptions
            .Subscribe(ex =>
            {
                _logger.LogError(ex, "Search failed");
                StatusText = $"Search failed: {ex.Message}";
                IsSearching = false;
            });

        // Clear command
        ClearCommand = ReactiveCommand.Create(ExecuteClear);

        // Open book command
        OpenBookCommand = ReactiveCommand.Create<BookOccurrenceViewModel>(ExecuteOpenBook);

        // Setup live search with debouncing
        this.WhenActivated(disposables =>
        {
            // Auto-search when text changes (with debounce)
            this.WhenAnyValue(x => x.SearchText)
                .Throttle(TimeSpan.FromMilliseconds(500))
                .DistinctUntilChanged()
                .Where(text => !string.IsNullOrWhiteSpace(text))
                .SelectMany(async _ => 
                {
                    await ExecuteSearchAsync();
                    return Unit.Default;
                })
                .Subscribe()
                .DisposeWith(disposables);

            // Update occurrences when term selection changes
            SelectedTerms.CollectionChanged += (_, e) => 
            {
                _logger.LogInformation("*** SelectedTerms CollectionChanged: Action={Action}, NewItems={NewCount}, OldItems={OldCount} ***", 
                    e.Action, e.NewItems?.Count ?? 0, e.OldItems?.Count ?? 0);
                _logger.LogInformation("*** Selected terms count: {SelectedCount}, Total terms count: {TotalCount} ***", 
                    SelectedTerms.Count, Terms.Count);
                UpdateOccurrences();
            };

            // Update statistics
            this.WhenAnyValue(
                x => x.Terms,
                x => x.SelectedTerms,
                x => x.Occurrences)
                .Subscribe(_ => UpdateStatistics())
                .DisposeWith(disposables);
        });
    }

    public ViewModelActivator Activator { get; }

    // Search input
    private string? _searchText;
    public string? SearchText
    {
        get => _searchText;
        set => this.RaiseAndSetIfChanged(ref _searchText, value);
    }

    // Search modes collection
    public ObservableCollection<SearchModeItem> SearchModes { get; private set; }

    // Search mode
    private SearchModeItem _selectedSearchMode = null!;
    public SearchModeItem SelectedSearchMode
    {
        get => _selectedSearchMode;
        set => this.RaiseAndSetIfChanged(ref _selectedSearchMode, value);
    }

    // Book filters
    private bool _includeVinaya;
    public bool IncludeVinaya
    {
        get => _includeVinaya;
        set => this.RaiseAndSetIfChanged(ref _includeVinaya, value);
    }

    private bool _includeSutta;
    public bool IncludeSutta
    {
        get => _includeSutta;
        set => this.RaiseAndSetIfChanged(ref _includeSutta, value);
    }

    private bool _includeAbhidhamma;
    public bool IncludeAbhidhamma
    {
        get => _includeAbhidhamma;
        set => this.RaiseAndSetIfChanged(ref _includeAbhidhamma, value);
    }

    private bool _includeMula;
    public bool IncludeMula
    {
        get => _includeMula;
        set => this.RaiseAndSetIfChanged(ref _includeMula, value);
    }

    private bool _includeAttha;
    public bool IncludeAttha
    {
        get => _includeAttha;
        set => this.RaiseAndSetIfChanged(ref _includeAttha, value);
    }

    private bool _includeTika;
    public bool IncludeTika
    {
        get => _includeTika;
        set => this.RaiseAndSetIfChanged(ref _includeTika, value);
    }

    private bool _includeOther;
    public bool IncludeOther
    {
        get => _includeOther;
        set => this.RaiseAndSetIfChanged(ref _includeOther, value);
    }

    // Search state
    private bool _isSearching;
    public bool IsSearching
    {
        get => _isSearching;
        set => this.RaiseAndSetIfChanged(ref _isSearching, value);
    }

    // Collections
    public ObservableCollection<MatchingTermViewModel> Terms { get; }
    public ObservableCollection<BookOccurrenceViewModel> Occurrences { get; }
    public ObservableCollection<MatchingTermViewModel> SelectedTerms { get; }

    // Statistics
    private string _termStats = "Words: 0, Selected: 0";
    public string TermStats
    {
        get => _termStats;
        set => this.RaiseAndSetIfChanged(ref _termStats, value);
    }

    private string _occurrenceStats = "Occurrences: 0, Books: 0";
    public string OccurrenceStats
    {
        get => _occurrenceStats;
        set => this.RaiseAndSetIfChanged(ref _occurrenceStats, value);
    }

    private string _statusText = "Ready to search";
    public string StatusText
    {
        get => _statusText;
        set => this.RaiseAndSetIfChanged(ref _statusText, value);
    }

    // Commands
    public ReactiveCommand<Unit, Unit> SearchCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearCommand { get; }
    public ReactiveCommand<BookOccurrenceViewModel, Unit> OpenBookCommand { get; }

    // Event for opening a book with search terms
    public event EventHandler<OpenBookWithSearchEventArgs>? OpenBookRequested;

    private async Task ExecuteSearchAsync()
    {
        try
        {
            // Cancel any existing search
            _searchCancellation?.Cancel();
            _searchCancellation = new CancellationTokenSource();

            IsSearching = true;
            StatusText = "Searching...";
            
            // Clear previous results
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Terms.Clear();
                Occurrences.Clear();
                SelectedTerms.Clear();
            });

            // Build search query
            var searchText = SearchText ?? string.Empty;
            
            // Determine actual search mode:
            // If user selected Wildcard mode but didn't use wildcards, treat as exact match
            // If user selected Regex mode but didn't use regex chars, treat as exact match
            var searchMode = SelectedSearchMode.Value;
            if (searchMode == SearchMode.Wildcard && !ContainsWildcardChars(searchText))
            {
                searchMode = SearchMode.Exact;
                _logger.LogInformation("No wildcard characters detected, using exact match");
            }
            else if (searchMode == SearchMode.Regex && !ContainsRegexChars(searchText))
            {
                searchMode = SearchMode.Exact;
                _logger.LogInformation("No regex characters detected, using exact match");
            }
            
            var query = new SearchQuery
            {
                QueryText = searchText,
                Mode = searchMode,
                Filter = new BookFilter
                {
                    IncludeVinaya = IncludeVinaya,
                    IncludeSutta = IncludeSutta,
                    IncludeAbhidhamma = IncludeAbhidhamma,
                    IncludeMula = IncludeMula,
                    IncludeAttha = IncludeAttha,
                    IncludeTika = IncludeTika,
                    IncludeOther = IncludeOther
                },
                PageSize = 10000  // Limit results for UI performance
            };

            _logger.LogInformation("Executing search: {Query}", query.QueryText);

            // Execute search
            var result = await _searchService.SearchAsync(query, _searchCancellation.Token);

            // Update UI with results
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                foreach (var term in result.Terms)
                {
                    var termVm = new MatchingTermViewModel
                    {
                        Term = term.Term,
                        DisplayTerm = term.DisplayTerm,
                        TotalCount = term.TotalCount,
                        Occurrences = term.Occurrences
                    };
                    Terms.Add(termVm);
                }

                // Auto-selection removed - let users manually select terms
                // This ensures statistics show "Selected: 0" initially

                StatusText = $"Search completed in {result.SearchDuration.TotalMilliseconds:F0}ms - Found {result.TotalTermCount} terms, {result.TotalOccurrenceCount} occurrences";
                
                // Explicitly update statistics after terms are populated and selected
                UpdateStatistics();
            });

            IsSearching = false;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInformation("Search cancelled");
            StatusText = "Search cancelled";
            IsSearching = false;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed");
            StatusText = $"Search failed: {ex.Message}";
            IsSearching = false;
        }
    }

    private void ExecuteClear()
    {
        SearchText = string.Empty;
        Terms.Clear();
        Occurrences.Clear();
        SelectedTerms.Clear();
        StatusText = "Ready to search";
    }

    private void ExecuteOpenBook(BookOccurrenceViewModel? occurrence)
    {
        if (occurrence == null) return;

        _logger.LogInformation("Opening book: {BookName} with search terms", occurrence.Book.FileName);

        // Collect all selected terms for highlighting
        var searchTerms = SelectedTerms.Select(t => t.Term).ToList();
        
        // Raise event to open the book
        OpenBookRequested?.Invoke(this, new OpenBookWithSearchEventArgs
        {
            Book = occurrence.Book,
            SearchTerms = searchTerms,
            Positions = occurrence.Positions
        });
    }

    private void UpdateOccurrences()
    {
        _logger.LogInformation("*** UpdateOccurrences called - SelectedTerms count: {SelectedCount} ***", SelectedTerms.Count);
        
        // Ensure UI operations happen on the UI thread
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            Occurrences.Clear();

            if (!SelectedTerms.Any()) 
            {
                _logger.LogInformation("*** No selected terms - clearing occurrences ***");
                return;
            }

            // Merge occurrences from all selected terms
            var bookOccurrences = new Dictionary<string, BookOccurrenceViewModel>();

            foreach (var term in SelectedTerms)
            {
                _logger.LogInformation("*** Processing selected term: {Term} with {OccurrenceCount} occurrences ***", 
                    term.DisplayTerm, term.Occurrences.Count);
                
                foreach (var occurrence in term.Occurrences)
                {
                    if (!bookOccurrences.TryGetValue(occurrence.Book.FileName, out var existing))
                    {
                        // Get the display name in the current script
                        var originalName = occurrence.Book.ShortNavPath ?? occurrence.Book.FileName;
                        var displayName = _scriptService.CurrentScript == Script.Devanagari 
                            ? originalName 
                            : ScriptConverter.Convert(originalName, Script.Devanagari, _scriptService.CurrentScript, true);

                        existing = new BookOccurrenceViewModel
                        {
                            Book = occurrence.Book,
                            Count = 0,
                            Positions = new List<TermPosition>(),
                            DisplayName = displayName
                        };
                        bookOccurrences[occurrence.Book.FileName] = existing;
                        _logger.LogInformation("*** Added new book occurrence: {BookName}, DisplayName: {DisplayName}, Index: {Index} ***", 
                            occurrence.Book.FileName, 
                            occurrence.Book.ShortNavPath ?? "NULL", 
                            occurrence.Book.Index);
                    }

                    existing.Count += occurrence.Count;
                    existing.Positions.AddRange(occurrence.Positions);
                }
            }

            // Sort by book index and add to collection
            foreach (var occurrence in bookOccurrences.Values.OrderBy(o => o.Book.Index))
            {
                _logger.LogInformation("*** Adding to Occurrences collection: {DisplayName} (Count: {Count}) ***", 
                    occurrence.DisplayName, occurrence.Count);
                Occurrences.Add(occurrence);
            }
            
            _logger.LogInformation("*** UpdateOccurrences completed - Added {BookCount} books to occurrences collection ***", Occurrences.Count);
        });
    }

    private void AddTestData()
    {
        _logger.LogInformation("*** AddTestData called - adding test books to verify UI binding ***");
        
        // Add test data to see if UI works at all
        Dispatcher.UIThread.InvokeAsync(() =>
        {
            // Add test terms
            Terms.Add(new MatchingTermViewModel 
            { 
                Term = "test", 
                DisplayTerm = "test", 
                TotalCount = 123,
                Occurrences = new List<BookOccurrence>()
            });
            
            // Add test books 
            Occurrences.Add(new BookOccurrenceViewModel
            {
                Book = new Book { FileName = "test-book-1.xml", Index = 1 },
                Count = 5
            });
            
            Occurrences.Add(new BookOccurrenceViewModel  
            {
                Book = new Book { FileName = "test-book-2.xml", Index = 2 },
                Count = 10
            });
            
            _logger.LogInformation("*** AddTestData completed - added {TermCount} terms and {BookCount} books ***", Terms.Count, Occurrences.Count);
        });
    }

    private void UpdateStatistics()
    {
        var selectedCount = SelectedTerms?.Count ?? 0;
        var totalTerms = Terms?.Count ?? 0;
        TermStats = $"Words: {totalTerms}, Selected: {selectedCount}";

        var totalOccurrences = Occurrences?.Sum(o => o.Count) ?? 0;
        var totalBooks = Occurrences?.Count ?? 0;
        OccurrenceStats = $"Occurrences: {totalOccurrences}, Books: {totalBooks}";
        
        _logger.LogInformation("*** UpdateStatistics called - Terms: {TermCount}, Selected: {SelectedCount}, Occurrences: {OccurrenceCount}, Books: {BookCount} ***", 
            totalTerms, selectedCount, totalOccurrences, totalBooks);
    }
    
    private static bool ContainsWildcardChars(string text)
    {
        // Check for wildcard characters (* and ?)
        return text.Contains('*') || text.Contains('?');
    }
    
    private static bool ContainsRegexChars(string text)
    {
        // Check for common regex metacharacters
        // This is a simplified check - you might want to expand this based on your needs
        var regexChars = new[] { '.', '^', '$', '[', ']', '(', ')', '{', '}', '|', '\\', '+' };
        return regexChars.Any(text.Contains);
    }
}

// View model for a matching term
public class MatchingTermViewModel : ViewModelBase
{
    public string Term { get; set; } = string.Empty;  // IPE encoded
    public string DisplayTerm { get; set; } = string.Empty;  // Display script
    public int TotalCount { get; set; }
    public List<BookOccurrence> Occurrences { get; set; } = new();
}

// View model for book occurrence
public class BookOccurrenceViewModel : ViewModelBase
{
    public Book Book { get; set; } = null!;
    public int Count { get; set; }
    public List<TermPosition> Positions { get; set; } = new();
    public string DisplayName { get; set; } = string.Empty;
}

// Event args for opening a book with search
public class OpenBookWithSearchEventArgs : EventArgs
{
    public Book Book { get; set; } = null!;
    public List<string> SearchTerms { get; set; } = new();
    public List<TermPosition> Positions { get; set; } = new();
}