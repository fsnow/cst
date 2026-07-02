using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Disposables.Fluent;
using System.Reactive.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CST.Avalonia.Models;
using CST.Avalonia.Search;
using CST.Avalonia.Services;
using CST.Avalonia.ViewModels.Dock;
using CST.Conversion;
using CST;
using DynamicData;
using Microsoft.Extensions.Logging;
using ReactiveUI;

namespace CST.Avalonia.ViewModels;

public class SearchViewModel : ReactiveTool, IActivatableViewModel, IDisposable
{
    private readonly ISearchService _searchService;
    private readonly IScriptService _scriptService;
    private readonly IFontService _fontService;
    private readonly IApplicationStateService _applicationStateService;
    // True while ApplyState() is restoring saved values. Used to (a) stop the save-on-change handlers from
    // echoing restored values back, and (b) stop the filter/mode/proximity changes from each kicking off a
    // live re-search during restore - the single SearchText change drives one search instead. (#87)
    private bool _isRestoringState;
    // Saved selected-term keys waiting to be re-applied once the restored query's search repopulates the
    // term list (the list is empty at restore time). Applied once, then cleared. (#87)
    private List<string>? _pendingSelectedTerms;
    private readonly ILogger<SearchViewModel> _logger;
    private CancellationTokenSource? _searchCancellation;
    private Action<Script>? _scriptChangedHandler;
    private EventHandler? _fontChangedHandler;

    public SearchViewModel() : this(
        App.ServiceProvider?.GetService(typeof(ISearchService)) as ISearchService ?? throw new InvalidOperationException("SearchService not available"),
        App.ServiceProvider?.GetService(typeof(IScriptService)) as IScriptService ?? throw new InvalidOperationException("ScriptService not available"),
        App.ServiceProvider?.GetService(typeof(IFontService)) as IFontService ?? throw new InvalidOperationException("FontService not available"),
        App.ServiceProvider?.GetService(typeof(IApplicationStateService)) as IApplicationStateService ?? throw new InvalidOperationException("ApplicationStateService not available"),
        App.ServiceProvider?.GetService(typeof(ILogger<SearchViewModel>)) as ILogger<SearchViewModel> ?? throw new InvalidOperationException("Logger not available"))
    {
    }

    public SearchViewModel(
        ISearchService searchService,
        IScriptService scriptService,
        IFontService fontService,
        IApplicationStateService applicationStateService,
        ILogger<SearchViewModel> logger)
    {
        _searchService = searchService;
        _scriptService = scriptService;
        _fontService = fontService;
        _applicationStateService = applicationStateService;
        _logger = logger;

        // Configure Dock properties
        Id = "SearchTool";
        Title = "Search";
        CanPin = false;     // Prevent pinning (vertical text issues)
        CanClose = false;   // Keep search panel always available
        CanFloat = true;    // Allow floating to separate window
        CanDrag = true;     // Allow dragging

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
        
        // Live search-as-you-type (debounced 300ms). Ctor-level, NOT in WhenActivated, because SearchPanel
        // is a plain UserControl with no activation wiring (no Avalonia.ReactiveUI integration), so the VM's
        // WhenActivated block never runs — same reason the #52 filter re-search lives at ctor level.
        // DistinctUntilChanged skips no-op edits; ExecuteSearchAsync cancels any in-flight search, so fast
        // typing collapses cleanly. (#57)
        this.WhenAnyValue(x => x.SearchText)
            .Throttle(TimeSpan.FromMilliseconds(300))
            .DistinctUntilChanged()
            .Where(text => !string.IsNullOrWhiteSpace(text))
            .SelectMany(async _ =>
            {
                await ExecuteSearchAsync();
                return Unit.Default;
            })
            .Subscribe();

        // Start persisting input changes. The actual restore (ApplyState) is pushed from App once the
        // saved state has finished loading - the ctor can run before the async load completes, so
        // restoring here would read an empty default. (#87)
        SetupStatePersistence();

        // These are ctor-level, NOT in a WhenActivated block: SearchPanel is a plain UserControl with no
        // activation wiring, so WhenActivated never runs. They used to live in a dead WhenActivated block,
        // and the view compensated by invoking UpdateOccurrences/UpdateStatistics through reflection. (SRCH-10)
        SelectedTerms.CollectionChanged += (_, e) =>
        {
            _logger.LogInformation("*** SelectedTerms CollectionChanged: Action={Action}, NewItems={NewCount}, OldItems={OldCount} ***",
                e.Action, e.NewItems?.Count ?? 0, e.OldItems?.Count ?? 0);
            UpdateOccurrences();
        };

        // Recompute the summary line whenever the terms/selection/occurrences change.
        this.WhenAnyValue(x => x.Terms, x => x.SelectedTerms, x => x.Occurrences)
            .Subscribe(_ => UpdateStatistics());
    }

    // --- Search-pane state persistence (#87) -------------------------------------------------------

    private void SetupStatePersistence()
    {
        // Persist whenever a saved field changes. UpdateSearchDialogState marks the state dirty (the 60s
        // timer + the shutdown save handle the disk write) and its StateChanged listeners are cheap, so a
        // per-change call is fine - and keeps the in-memory state current for the shutdown save.
        this.PropertyChanged += (_, e) =>
        {
            if (!_isRestoringState && IsPersistableProperty(e.PropertyName))
                _applicationStateService.UpdateSearchDialogState(CaptureState());
        };

        // The selected matching terms drive the occurrences list; persist them too. The collection is
        // cleared/repopulated by each search, but the net settled state is what gets saved. (#87)
        SelectedTerms.CollectionChanged += (_, _) =>
        {
            if (!_isRestoringState)
                _applicationStateService.UpdateSearchDialogState(CaptureState());
        };
    }

    private static bool IsPersistableProperty(string? name) =>
        name is nameof(SearchText) or nameof(SelectedSearchMode) or nameof(ProximityDistance)
            or nameof(IncludeVinaya) or nameof(IncludeSutta) or nameof(IncludeAbhidhamma)
            or nameof(IncludeMula) or nameof(IncludeAttha) or nameof(IncludeTika) or nameof(IncludeOther)
            or nameof(IsTextTypesExpanded);

    /// <summary>Snapshot the persistable search inputs into a <see cref="SearchDialogState"/>. (#87)</summary>
    internal SearchDialogState CaptureState() => new()
    {
        SearchText = SearchText ?? string.Empty,
        SearchMode = SelectedSearchMode?.Value ?? SearchMode.Wildcard,
        ProximityDistance = ProximityDistance,
        IncludeVinaya = IncludeVinaya,
        IncludeSutta = IncludeSutta,
        IncludeAbhidhamma = IncludeAbhidhamma,
        IncludeMula = IncludeMula,
        IncludeAttha = IncludeAttha,
        IncludeTika = IncludeTika,
        IncludeOther = IncludeOther,
        IsTextTypesExpanded = IsTextTypesExpanded,
        SelectedTerms = SelectedTerms.Select(t => t.Term).ToList(),
    };

    /// <summary>Apply saved search inputs to the view model (no-op if null). (#87)</summary>
    internal void ApplyState(SearchDialogState? state)
    {
        if (state == null)
            return;

        _isRestoringState = true;
        try
        {
            IncludeVinaya = state.IncludeVinaya;
            IncludeSutta = state.IncludeSutta;
            IncludeAbhidhamma = state.IncludeAbhidhamma;
            IncludeMula = state.IncludeMula;
            IncludeAttha = state.IncludeAttha;
            IncludeTika = state.IncludeTika;
            IncludeOther = state.IncludeOther;
            IsTextTypesExpanded = state.IsTextTypesExpanded;
            ProximityDistance = state.ProximityDistance;

            var mode = SearchModes.FirstOrDefault(m => m.Value == state.SearchMode);
            if (mode != null)
                SelectedSearchMode = mode;

            // The term list is empty until the search re-runs, so defer re-selecting the saved terms;
            // ExecuteSearchAsync applies them once the results come back.
            _pendingSelectedTerms = state.SelectedTerms is { Count: > 0 } ? new List<string>(state.SelectedTerms) : null;

            SearchText = state.SearchText; // set last - triggers the live search-as-you-type
        }
        finally
        {
            _isRestoringState = false;
        }
    }

    /// <summary>Raised on the UI thread after a search finishes populating the term list, so the view can
    /// restore a saved term selection into the list box. (#87)</summary>
    public event Action? SearchResultsReady;

    /// <summary>Saved term keys awaiting selection in the list box after a restore (null if none). The
    /// view reads this on <see cref="SearchResultsReady"/> and then calls <see cref="ClearPendingSelectedTerms"/>. (#87)</summary>
    public IReadOnlyList<string>? PendingSelectedTerms => _pendingSelectedTerms;

    /// <summary>Called by the view once it has applied the pending term selection. (#87)</summary>
    public void ClearPendingSelectedTerms() => _pendingSelectedTerms = null;

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

    // Expanded/collapsed state of the "Include Text Types" box (persisted). (#87)
    private bool _isTextTypesExpanded;
    public bool IsTextTypesExpanded
    {
        get => _isTextTypesExpanded;
        set => this.RaiseAndSetIfChanged(ref _isTextTypesExpanded, value);
    }

    // Proximity distance
    private int _proximityDistance = 10;
    public int ProximityDistance
    {
        get => _proximityDistance;
        set => this.RaiseAndSetIfChanged(ref _proximityDistance, value);
    }

    // Phrase and proximity search UI properties.
    // The query parses into units (word or quoted phrase). Within a phrase => adjacency; between
    // units => the proximity window. So with multi-phrase support these are no longer mutually
    // exclusive: e.g. `"evam me" sutam` is BOTH a phrase and a proximity search.
    private List<SearchUnit> ParsedUnits =>
        MultiWordSearch.ParseUnits(MultiWordSearch.StripJoiners(SearchText ?? string.Empty).Replace("\u201C", "\"").Replace("\u201D", "\""));

    // True when any quoted multi-word group is present (=> "exact word order" applies to it).
    public bool IsPhraseSearch => ParsedUnits.Any(u => u.IsPhrase);

    // True when the query has more than one word in total (a phrase counts each of its words).
    public bool IsMultiWord => ParsedUnits.Sum(u => u.Words.Count) > 1;

    // The proximity window applies only BETWEEN units, i.e. when there are 2+ units.
    public bool IsProximitySearchEnabled => ParsedUnits.Count > 1;

    // Search state
    private bool _isSearching;
    public bool IsSearching
    {
        get => _isSearching;
        set => this.RaiseAndSetIfChanged(ref _isSearching, value);
    }

    // Set when the result set was capped (wildcard expansion or page-size limit); shown in the UI.
    private bool _isResultsTruncated;
    public bool IsResultsTruncated
    {
        get => _isResultsTruncated;
        set => this.RaiseAndSetIfChanged(ref _isResultsTruncated, value);
    }

    private string? _truncationMessage;
    public string? TruncationMessage
    {
        get => _truncationMessage;
        set => this.RaiseAndSetIfChanged(ref _truncationMessage, value);
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

    // Unintrusive inline hint shown under the search box only when non-empty (e.g. invalid regex). (#59)
    private string _validationMessage = string.Empty;
    public string ValidationMessage
    {
        get => _validationMessage;
        set => this.RaiseAndSetIfChanged(ref _validationMessage, value);
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
        BitArray? bookBits = null;
        BitArray? clBits = null;
        BitArray? pitBits = null;
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
            bookBits = clBits!.And(pitBits!);
        else if (clSelected)
            bookBits = clBits!;
        else if (pitSelected)
            bookBits = pitBits!;
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
        // Supersede any in-flight search and capture THIS search's token, so a stale completion can't
        // clobber the newer search's results or its IsSearching flag. (SRCH-7)
        _searchCancellation?.Cancel();
        var cts = new CancellationTokenSource();
        _searchCancellation = cts;
        var token = cts.Token;

        try
        {
            // The pipeline runs on a thread-pool thread (Throttle) and SearchService runs its Lucene work
            // synchronously (no Task.Run), so keep the search off the UI thread and marshal every UI-bound
            // write to the dispatcher instead of ObserveOn-ing the whole pipeline onto the UI thread (which
            // would freeze it for the search's duration). (SRCH-8)
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                IsSearching = true;
                StatusText = "Searching...";
            });

            // Build search query
            var searchText = SearchText ?? string.Empty;

            // Determine actual search mode:
            // If the user selected Wildcard mode but used no wildcard chars, treat it as exact — a
            // wildcard pattern with no */? is anchored (^pat$) and equivalent to an exact match.
            // Regex mode is NOT downgraded: a plain string like "kassa" is a valid (unanchored) regex
            // that matches every term containing it as a substring, so downgrading to exact would
            // silently drop most matches (the user explicitly chose Regex). (#58)
            var searchMode = SelectedSearchMode.Value;
            if (searchMode == SearchMode.Wildcard && !ContainsWildcardChars(searchText))
            {
                searchMode = SearchMode.Exact;
                _logger.LogInformation("No wildcard characters detected, using exact match");
            }

            // Regex pre-validation (unintrusive): with live search-as-you-type a partially typed regex is
            // routinely in an invalid state. Show a quiet hint and KEEP the current results, rather than
            // clearing them or surfacing a raw RegexParseException. (Wildcard/Exact can't be invalid.) (#59)
            if (searchMode == SearchMode.Regex && !IsValidRegexPattern(searchText))
            {
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    ValidationMessage = "Invalid regex pattern";
                    IsSearching = false;
                });
                return;
            }

            // Clear previous results (and any prior validation hint).
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                ValidationMessage = string.Empty;
                Terms.Clear();
                Occurrences.Clear();
                SelectedTerms.Clear();
                IsResultsTruncated = false;
                TruncationMessage = null;
            });

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
            var result = await _searchService.SearchAsync(query, token);
            
            _logger.LogInformation("Search returned {TermCount} terms, {OccurrenceCount} occurrences", 
                result.Terms.Count, result.TotalOccurrenceCount);

            // Update UI with results
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                // A newer search superseded this one while it ran — don't clobber its results. (SRCH-7)
                if (token.IsCancellationRequested)
                    return;

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

                // Auto-select the single term (common case), unless a saved selection is pending restore:
                // in that case the view selects the saved terms in the list box once notified (below), so
                // the selection goes through the normal SelectionChanged -> SyncSelection path (visual
                // highlight + occurrences). (#87)
                if (_pendingSelectedTerms == null && Terms.Count == 1)
                {
                    SelectedTerms.Add(Terms[0]);
                    _logger.LogInformation("Auto-selected single search term: {Term}", Terms[0].DisplayTerm);
                }

                // Notify the view (on the UI thread) that the term list is ready, so it can restore a
                // saved term selection into the list box via the normal selection path. (#87)
                SearchResultsReady?.Invoke();

                IsResultsTruncated = result.ResultsTruncated;
                TruncationMessage = result.TruncationMessage;

                StatusText = $"Search completed in {result.SearchDuration.TotalMilliseconds:F0}ms - Found {result.TotalTermCount} terms, {result.TotalOccurrenceCount} occurrences";
                
                stopwatch.Stop();
                _logger.LogInformation("UI update completed in {Elapsed}ms for {TermCount} terms", 
                    stopwatch.ElapsedMilliseconds, Terms.Count);
                
                // Explicitly update statistics after terms are populated and selected
                UpdateStatistics();

                IsSearching = false;
            });
        }
        catch (OperationCanceledException)
        {
            // Superseded/cancelled — leave IsSearching and StatusText to the newer search. (SRCH-7)
            _logger.LogInformation("Search cancelled");
        }
        catch (System.Text.RegularExpressions.RegexParseException)
        {
            // Backstop for any invalid regex the up-front check didn't catch (e.g. a multi-unit query).
            // Quiet hint, not an error. (#59)
            _logger.LogDebug("Invalid regex pattern for query: {Query}", SearchText);
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;
                ValidationMessage = "Invalid regex pattern";
                IsSearching = false;
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search failed");
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                if (token.IsCancellationRequested) return;
                StatusText = $"Search failed: {ex.Message}";
                IsSearching = false;
            });
        }
    }

    private void ExecuteClear()
    {
        SearchText = string.Empty;
        Terms.Clear();
        Occurrences.Clear();
        SelectedTerms.Clear();
        StatusText = "Ready to search";
        ValidationMessage = string.Empty;
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
    
    // Pumped whenever a book-type filter toggles; throttled in WhenActivated to re-run the search live.
    private readonly System.Reactive.Subjects.Subject<Unit> _filterChanged = new();

    private void SetupFilterChangeNotifications()
    {
        // The 7-property WhenAnyValue tuple overload used here previously did not fire on toggle, so
        // the summary labels never refreshed and results didn't re-filter until the next manual search
        // (#52). Drive off the raw PropertyChanged event instead — it is guaranteed to fire for every
        // Include* change. Labels update immediately here; the debounced live re-search is in WhenActivated.
        this.PropertyChanged += (_, e) =>
        {
            if (IsFilterProperty(e.PropertyName))
            {
                this.RaisePropertyChanged(nameof(FilterSummary));
                this.RaisePropertyChanged(nameof(BookCountLabel));
                // Don't re-search per-property while restoring saved state; the restored SearchText drives
                // a single search. Otherwise the filter trigger and the SearchText search-as-you-type race
                // and the term list ends up doubled. (#87)
                if (!_isRestoringState) _filterChanged.OnNext(Unit.Default);
            }
            else if (e.PropertyName is nameof(SelectedSearchMode) or nameof(ProximityDistance))
            {
                // Mode (Wildcard/Regex) and proximity change the result set too, so re-run the live
                // search when one of them changes (same debounced path as the filters) - except during
                // restore, for the reason above.
                if (!_isRestoringState) _filterChanged.OnNext(Unit.Default);
            }
        };

        // Re-run the search live when a book-type filter changes. This lives here (ctor-level), NOT in
        // WhenActivated, because SearchPanel is a plain UserControl with no activation wiring, so the VM's
        // WhenActivated block never runs (the SearchText live auto-search there is dead for the same reason).
        // Debounced so quick multi-toggles and Select All/None collapse into one search; only re-runs when
        // there is an active query. Held alive by the _filterChanged subject field. (#52)
        _filterChanged
            .Throttle(TimeSpan.FromMilliseconds(300))
            .Where(_ => !string.IsNullOrWhiteSpace(SearchText))
            .SelectMany(async _ =>
            {
                await ExecuteSearchAsync();
                return Unit.Default;
            })
            .Subscribe();
    }

    private static bool IsFilterProperty(string? name) => name is
        nameof(IncludeVinaya) or nameof(IncludeSutta) or nameof(IncludeAbhidhamma) or
        nameof(IncludeMula) or nameof(IncludeAttha) or nameof(IncludeTika) or nameof(IncludeOther);

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
                UpdateStatistics();   // keep the summary line in sync (SRCH-10)
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
            UpdateStatistics();   // occurrences changed -> refresh the summary line (SRCH-10)
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

    // Validate a Regex query the same way the search compiles it: the text is IPE-converted, then the
    // matcher does new Regex(pattern). Compile a throwaway here so an invalid pattern can be reported
    // quietly without running (or error-logging) a failed search. Empty/whitespace is treated as valid
    // (handled downstream as a no-op). (#59)
    private static bool IsValidRegexPattern(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return true;
        try
        {
            _ = new System.Text.RegularExpressions.Regex(Any2Ipe.Convert(text));
            return true;
        }
        catch (System.Text.RegularExpressions.RegexParseException)
        {
            return false;
        }
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
            // Multi-word/phrase results store Term as the matched words joined by '~'. Convert each
            // word independently and rejoin with spaces so the label reads naturally (a single-word
            // term has no '~' and is converted as-is).
            term.DisplayTerm = string.Join(" ",
                term.Term.Split('~').Select(w => ScriptConverter.Convert(w, Script.Ipe, newScript)));
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