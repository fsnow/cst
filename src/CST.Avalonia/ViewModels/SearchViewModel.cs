using System;
using System.Collections;
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

public class SearchViewModel : ViewModelBase, IActivatableViewModel, IDisposable
{
    private readonly ISearchService _searchService;
    private readonly IScriptService _scriptService;
    private readonly IFontService _fontService;
    private readonly ILogger<SearchViewModel> _logger;
    private CancellationTokenSource? _searchCancellation;
    private Action<Script>? _scriptChangedHandler;
    private EventHandler? _fontChangedHandler;

    public SearchViewModel() : this(
        App.ServiceProvider?.GetService(typeof(ISearchService)) as ISearchService ?? throw new InvalidOperationException("SearchService not available"),
        App.ServiceProvider?.GetService(typeof(IScriptService)) as IScriptService ?? throw new InvalidOperationException("ScriptService not available"),
        App.ServiceProvider?.GetService(typeof(IFontService)) as IFontService ?? throw new InvalidOperationException("FontService not available"),
        App.ServiceProvider?.GetService(typeof(ILogger<SearchViewModel>)) as ILogger<SearchViewModel> ?? throw new InvalidOperationException("Logger not available"))
    {
    }

    public SearchViewModel(
        ISearchService searchService,
        IScriptService scriptService,
        IFontService fontService,
        ILogger<SearchViewModel> logger)
    {
        _searchService = searchService;
        _scriptService = scriptService;
        _fontService = fontService;
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
        
        // Filter commands
        SelectAllFiltersCommand = ReactiveCommand.Create(ExecuteSelectAllFilters);
        SelectNoneFiltersCommand = ReactiveCommand.Create(ExecuteSelectNoneFilters);

        // Setup font and script change handlers (outside of WhenActivated so they always work)
        SetupFontAndScriptHandlers();
        
        // Setup filter change notifications
        SetupFilterChangeNotifications();
        
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

    private void SetupFontAndScriptHandlers()
    {
        // Listen for script changes to update font properties and regenerate display text
        _scriptChangedHandler = (newScript) =>
        {
            _logger.LogInformation("*** [SEARCH SCRIPT] Script changed to {Script}, updating font properties and search result display text ***", newScript);
            Dispatcher.UIThread.Post(() =>
            {
                // Update font properties
                this.RaisePropertyChanged(nameof(CurrentScriptFontFamily));
                this.RaisePropertyChanged(nameof(CurrentScriptFontSize));
                
                // Update search result display text to new script
                UpdateSearchResultDisplayText(newScript);
                
                _logger.LogInformation("*** [SEARCH SCRIPT] Font properties and display text updated for script {Script} - Family: {FontFamily}, Size: {FontSize} ***", 
                    newScript, CurrentScriptFontFamily, CurrentScriptFontSize);
            });
        };
        
        _scriptService.ScriptChanged += _scriptChangedHandler;
        
        // Listen for font setting changes
        _fontChangedHandler = (sender, e) =>
        {
            _logger.LogInformation("*** [SEARCH FONT] Font settings changed event received, updating font properties ***");
            Dispatcher.UIThread.Post(() =>
            {
                this.RaisePropertyChanged(nameof(CurrentScriptFontFamily));
                this.RaisePropertyChanged(nameof(CurrentScriptFontSize));
                _logger.LogInformation("*** [SEARCH FONT] Font properties updated - Family: {FontFamily}, Size: {FontSize} ***", 
                    CurrentScriptFontFamily, CurrentScriptFontSize);
            });
        };
        
        _fontService.FontSettingsChanged += _fontChangedHandler;
    }

    public ViewModelActivator Activator { get; }

    // Search input
    private string? _searchText;
    public string? SearchText
    {
        get => _searchText;
        set
        {
            this.RaiseAndSetIfChanged(ref _searchText, value);
            // Update quote and multi-word detection properties
            this.RaisePropertyChanged(nameof(IsPhraseSearch));
            this.RaisePropertyChanged(nameof(IsMultiWord));
            this.RaisePropertyChanged(nameof(IsProximitySearchEnabled));
        }
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

    // Proximity distance
    private int _proximityDistance = 10;
    public int ProximityDistance
    {
        get => _proximityDistance;
        set => this.RaiseAndSetIfChanged(ref _proximityDistance, value);
    }

    // Phrase and proximity search UI properties
    public bool IsPhraseSearch => SearchText?.Contains("\"") ?? false;
    public bool IsMultiWord => (SearchText?.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).Length ?? 0) > 1;
    public bool IsProximitySearchEnabled => !IsPhraseSearch && IsMultiWord;

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
    public ReactiveCommand<Unit, Unit> SelectAllFiltersCommand { get; }
    public ReactiveCommand<Unit, Unit> SelectNoneFiltersCommand { get; }
    
    // Filter summary properties
    public string FilterSummary
    {
        get
        {
            var activeFilters = GetActiveFilterCount();
            if (activeFilters == 7)
                return "All text types";
            else if (activeFilters == 0)
                return "No text types selected";
            else
            {
                var filters = new List<string>();
                if (IncludeVinaya) filters.Add("Vinaya");
                if (IncludeSutta) filters.Add("Sutta");
                if (IncludeAbhidhamma) filters.Add("Abhidhamma");
                if (IncludeMula) filters.Add("Mūla");
                if (IncludeAttha) filters.Add("Aṭṭhakathā");
                if (IncludeTika) filters.Add("Ṭīkā");
                if (IncludeOther) filters.Add("Other");
                
                if (filters.Count <= 3)
                    return string.Join(", ", filters);
                else
                    return $"{activeFilters} of 7 types";
            }
        }
    }
    
    public string BookCountLabel => $"{GetIncludedBookCount()} of 217 books";
    
    private int GetActiveFilterCount()
    {
        var count = 0;
        if (IncludeVinaya) count++;
        if (IncludeSutta) count++;
        if (IncludeAbhidhamma) count++;
        if (IncludeMula) count++;
        if (IncludeAttha) count++;
        if (IncludeTika) count++;
        if (IncludeOther) count++;
        return count;
    }
    
    private int GetIncludedBookCount()
    {
        var bookBits = CalculateBookBits();
        
        // Count the number of true bits in the BitArray
        int count = 0;
        for (int i = 0; i < bookBits.Count; i++)
        {
            if (bookBits[i])
                count++;
        }
        
        return count;
    }
    
    // This method determines which books to search based on the filter checkboxes
    // Returns a BitArray where true = include book in search, false = exclude
    public BitArray CalculateBookBits()
    {
        var books = CST.Books.Inst;
        int bookCount = books.Count;
        BitArray bookBits = null;
        BitArray clBits = null;
        BitArray pitBits = null;
        bool clSelected = false;
        bool pitSelected = false;
        
        // Handle Commentary Level filters (OR logic within group)
        if (IncludeMula || IncludeAttha || IncludeTika)
        {
            clBits = new BitArray(bookCount);
            
            if (IncludeMula)
                clBits = clBits.Or(books.MulaBits);
            if (IncludeAttha)
                clBits = clBits.Or(books.AtthaBits);
            if (IncludeTika)
                clBits = clBits.Or(books.TikaBits);
            
            clSelected = true;
        }
        
        // Handle Pitaka filters (OR logic within group)
        if (IncludeVinaya || IncludeSutta || IncludeAbhidhamma)
        {
            pitBits = new BitArray(bookCount);
            
            if (IncludeVinaya)
                pitBits = pitBits.Or(books.VinayaBits);
            if (IncludeSutta)
                pitBits = pitBits.Or(books.SuttaBits);
            if (IncludeAbhidhamma)
                pitBits = pitBits.Or(books.AbhiBits);
            
            pitSelected = true;
        }
        
        // Combine Commentary and Pitaka filters (AND logic between groups)
        if (clSelected && pitSelected)
            bookBits = clBits.And(pitBits);
        else if (clSelected)
            bookBits = clBits;
        else if (pitSelected)
            bookBits = pitBits;
        else
            bookBits = new BitArray(bookCount);
        
        // Add Other texts (OR logic - these are always included if checked)
        if (IncludeOther)
            bookBits = bookBits.Or(books.OtherBits);
        
        return bookBits;
    }
    
    // Font properties for search results
    public string CurrentScriptFontFamily => _fontService.GetScriptFontFamily(_scriptService.CurrentScript) ?? "Helvetica";
    public int CurrentScriptFontSize => _fontService.GetScriptFontSize(_scriptService.CurrentScript);

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
                PageSize = 500,  // Reduced for better Devanagari performance
                ProximityDistance = ProximityDistance,
                IsPhrase = IsPhraseSearch,
                IsMultiWord = IsMultiWord
            };

            _logger.LogInformation("Executing search: {Query}", query.QueryText);

            // Execute search
            var result = await _searchService.SearchAsync(query, _searchCancellation.Token);
            
            _logger.LogInformation("Search returned {TermCount} terms, {OccurrenceCount} occurrences", 
                result.Terms.Count, result.TotalOccurrenceCount);

            // Update UI with results
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                var stopwatch = System.Diagnostics.Stopwatch.StartNew();
                _logger.LogInformation("Starting UI update with script: {Script}", _scriptService.CurrentScript);
                
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

                // Auto-select if there's only one term (common case for single-term searches)
                // For multi-term searches, let users manually select which terms they want
                if (Terms.Count == 1)
                {
                    SelectedTerms.Add(Terms[0]);
                    _logger.LogInformation("Auto-selected single search term: {Term}", Terms[0].DisplayTerm);
                }

                StatusText = $"Search completed in {result.SearchDuration.TotalMilliseconds:F0}ms - Found {result.TotalTermCount} terms, {result.TotalOccurrenceCount} occurrences";
                
                stopwatch.Stop();
                _logger.LogInformation("UI update completed in {Elapsed}ms for {TermCount} terms", 
                    stopwatch.ElapsedMilliseconds, Terms.Count);
                
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

        // Debug: Log position details
        _logger.LogInformation("Passing {PositionCount} positions to book display", occurrence.Positions.Count);
        foreach (var pos in occurrence.Positions.Take(5))
        {
            _logger.LogInformation("  Position: {Pos}, StartOffset: {Start}, EndOffset: {End}, IsFirstTerm: {IsFirst}, Word: {Word}",
                pos.Position, pos.StartOffset, pos.EndOffset, pos.IsFirstTerm, pos.Word ?? "null");
        }

        // Raise event to open the book
        OpenBookRequested?.Invoke(this, new OpenBookWithSearchEventArgs
        {
            Book = occurrence.Book,
            SearchTerms = searchTerms,
            Positions = occurrence.Positions
        });
    }
    
    private void ExecuteSelectAllFilters()
    {
        IncludeVinaya = true;
        IncludeSutta = true;
        IncludeAbhidhamma = true;
        IncludeMula = true;
        IncludeAttha = true;
        IncludeTika = true;
        IncludeOther = true;
    }
    
    private void ExecuteSelectNoneFilters()
    {
        IncludeVinaya = false;
        IncludeSutta = false;
        IncludeAbhidhamma = false;
        IncludeMula = false;
        IncludeAttha = false;
        IncludeTika = false;
        IncludeOther = false;
    }
    
    private void SetupFilterChangeNotifications()
    {
        // When any filter changes, raise property changed for summary properties
        this.WhenAnyValue(
            x => x.IncludeVinaya,
            x => x.IncludeSutta,
            x => x.IncludeAbhidhamma,
            x => x.IncludeMula,
            x => x.IncludeAttha,
            x => x.IncludeTika,
            x => x.IncludeOther)
            .Subscribe(_ =>
            {
                this.RaisePropertyChanged(nameof(FilterSummary));
                this.RaisePropertyChanged(nameof(BookCountLabel));
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
    
    /// <summary>
    /// Update display text for all search results when script changes
    /// </summary>
    private void UpdateSearchResultDisplayText(Script newScript)
    {
        _logger.LogInformation("*** [SEARCH SCRIPT] Updating display text for {TermCount} terms and {BookCount} books to script {Script} ***", 
            Terms.Count, Occurrences.Count, newScript);
        
        // Update terms display text (convert from IPE to new script)
        foreach (var term in Terms)
        {
            var oldDisplayTerm = term.DisplayTerm;
            term.DisplayTerm = ScriptConverter.Convert(term.Term, Script.Ipe, newScript);
            _logger.LogInformation("*** [SEARCH SCRIPT] Updated term: '{OldDisplayTerm}' -> '{NewDisplayTerm}' ***", 
                oldDisplayTerm, term.DisplayTerm);
        }
        
        // Update book occurrence display names (convert from Devanagari to new script)  
        foreach (var occurrence in Occurrences)
        {
            var originalName = occurrence.Book.ShortNavPath ?? occurrence.Book.FileName;
            var oldDisplayName = occurrence.DisplayName;
            occurrence.DisplayName = newScript == Script.Devanagari 
                ? originalName 
                : ScriptConverter.Convert(originalName, Script.Devanagari, newScript, true);
            _logger.LogInformation("*** [SEARCH SCRIPT] Updated book: '{OldDisplayName}' -> '{NewDisplayName}' ***", 
                oldDisplayName, occurrence.DisplayName);
        }
        
        _logger.LogInformation("*** [SEARCH SCRIPT] Display text update completed for script {Script} ***", newScript);
    }

    public void Dispose()
    {
        // Unsubscribe from events to prevent memory leaks
        if (_scriptChangedHandler != null)
        {
            _scriptService.ScriptChanged -= _scriptChangedHandler;
        }
        
        if (_fontChangedHandler != null)
        {
            _fontService.FontSettingsChanged -= _fontChangedHandler;
        }
        
        // Cancel any ongoing search
        _searchCancellation?.Cancel();
        _searchCancellation?.Dispose();
    }
}

// View model for a matching term
public class MatchingTermViewModel : ViewModelBase
{
    public string Term { get; set; } = string.Empty;  // IPE encoded
    
    private string _displayTerm = string.Empty;
    public string DisplayTerm
    {
        get => _displayTerm;
        set => this.RaiseAndSetIfChanged(ref _displayTerm, value);
    }
    
    public int TotalCount { get; set; }
    public List<BookOccurrence> Occurrences { get; set; } = new();
}

// View model for book occurrence
public class BookOccurrenceViewModel : ViewModelBase
{
    public Book Book { get; set; } = null!;
    public int Count { get; set; }
    public List<TermPosition> Positions { get; set; } = new();
    
    private string _displayName = string.Empty;
    public string DisplayName
    {
        get => _displayName;
        set => this.RaiseAndSetIfChanged(ref _displayName, value);
    }
}

// Event args for opening a book with search
public class OpenBookWithSearchEventArgs : EventArgs
{
    public Book Book { get; set; } = null!;
    public List<string> SearchTerms { get; set; } = new();
    public List<TermPosition> Positions { get; set; } = new();
}