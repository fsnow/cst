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

        // Set default values
        SelectedSearchMode = SearchMode.Exact;
        
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
            this.WhenAnyValue(x => x.SelectedTerms)
                .Where(terms => terms != null)
                .Subscribe(_ => UpdateOccurrences())
                .DisposeWith(disposables);

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

    // Search mode
    private SearchMode _selectedSearchMode;
    public SearchMode SelectedSearchMode
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
            var query = new SearchQuery
            {
                QueryText = SearchText ?? string.Empty,
                Mode = SelectedSearchMode,
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
                PageSize = 1000  // Limit results for UI performance
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

                // Auto-select all terms if there are 10 or fewer
                if (Terms.Count <= 10)
                {
                    foreach (var term in Terms)
                    {
                        SelectedTerms.Add(term);
                    }
                }
                else if (Terms.Count > 0)
                {
                    // Select just the first term
                    SelectedTerms.Add(Terms[0]);
                }

                StatusText = $"Search completed in {result.SearchDuration.TotalMilliseconds:F0}ms - Found {result.TotalTermCount} terms, {result.TotalOccurrenceCount} occurrences";
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
        Occurrences.Clear();

        if (!SelectedTerms.Any()) return;

        // Merge occurrences from all selected terms
        var bookOccurrences = new Dictionary<string, BookOccurrenceViewModel>();

        foreach (var term in SelectedTerms)
        {
            foreach (var occurrence in term.Occurrences)
            {
                if (!bookOccurrences.TryGetValue(occurrence.Book.FileName, out var existing))
                {
                    existing = new BookOccurrenceViewModel
                    {
                        Book = occurrence.Book,
                        Count = 0,
                        Positions = new List<TermPosition>()
                    };
                    bookOccurrences[occurrence.Book.FileName] = existing;
                }

                existing.Count += occurrence.Count;
                existing.Positions.AddRange(occurrence.Positions);
            }
        }

        // Sort by book index and add to collection
        foreach (var occurrence in bookOccurrences.Values.OrderBy(o => o.Book.Index))
        {
            Occurrences.Add(occurrence);
        }
    }

    private void UpdateStatistics()
    {
        var selectedCount = SelectedTerms?.Count ?? 0;
        var totalTerms = Terms?.Count ?? 0;
        TermStats = $"Words: {totalTerms}, Selected: {selectedCount}";

        var totalOccurrences = Occurrences?.Sum(o => o.Count) ?? 0;
        var totalBooks = Occurrences?.Count ?? 0;
        OccurrenceStats = $"Occurrences: {totalOccurrences}, Books: {totalBooks}";
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
    
    public string DisplayName => Book.ShortNavPath ?? Book.FileName;
}

// Event args for opening a book with search
public class OpenBookWithSearchEventArgs : EventArgs
{
    public Book Book { get; set; } = null!;
    public List<string> SearchTerms { get; set; } = new();
    public List<TermPosition> Positions { get; set; } = new();
}