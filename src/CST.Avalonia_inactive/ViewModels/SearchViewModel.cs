using System;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Threading;
using CST.Avalonia.Commands;
using CST.Avalonia.Services;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.ViewModels;

public class SearchViewModel : INotifyPropertyChanged
{
    private readonly IBookService _bookService;
    private readonly ISearchService _searchService;
    private readonly ILogger<SearchViewModel> _logger;

    public SearchViewModel(
        IBookService bookService,
        ISearchService searchService,
        ILogger<SearchViewModel> logger)
    {
        _bookService = bookService;
        _searchService = searchService;
        _logger = logger;

        SearchResults = new ObservableCollection<BookHit>();
        AvailableBooks = new ObservableCollection<Book>();
        SelectedBooks = new ObservableCollection<Book>();

        // Initialize commands
        SearchCommand = new SimpleCommand(async () => await PerformSearchAsync(), () => CanSearch);
        ClearCommand = new SimpleCommand(ClearSearch);
        SelectAllBooksCommand = new SimpleCommand(SelectAllBooks);
        ClearBookSelectionCommand = new SimpleCommand(ClearBookSelection);
        OpenResultCommand = new SimpleCommand<BookHit>(OpenSearchResult);

        // Load available books
        LoadAvailableBooks();
    }

    // Events
    public event Action<Book, string>? BookOpenRequested;

    // Properties
    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set
        {
            _searchText = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSearch));
            ((SimpleCommand)SearchCommand).RaiseCanExecuteChanged();
        }
    }

    private bool _isSearching = false;
    public bool IsSearching
    {
        get => _isSearching;
        set
        {
            _isSearching = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(CanSearch));
            ((SimpleCommand)SearchCommand).RaiseCanExecuteChanged();
        }
    }

    private string _statusText = "Ready to search";
    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    private int _resultCount = 0;
    public int ResultCount
    {
        get => _resultCount;
        set
        {
            _resultCount = value;
            OnPropertyChanged();
        }
    }

    private BookHit? _selectedResult;
    public BookHit? SelectedResult
    {
        get => _selectedResult;
        set
        {
            _selectedResult = value;
            OnPropertyChanged();
        }
    }

    public bool CanSearch => !string.IsNullOrWhiteSpace(SearchText) && !IsSearching && SelectedBooks.Any();

    // Collections
    public ObservableCollection<BookHit> SearchResults { get; }
    public ObservableCollection<Book> AvailableBooks { get; }
    public ObservableCollection<Book> SelectedBooks { get; }

    // Commands
    public ICommand SearchCommand { get; }
    public ICommand ClearCommand { get; }
    public ICommand SelectAllBooksCommand { get; }
    public ICommand ClearBookSelectionCommand { get; }
    public ICommand OpenResultCommand { get; }

    // Methods
    private async void LoadAvailableBooks()
    {
        try
        {
            await _bookService.LoadBooksAsync();
            
            Dispatcher.UIThread.Post(() =>
            {
                AvailableBooks.Clear();
                foreach (var book in _bookService.Books)
                {
                    AvailableBooks.Add(book);
                }
                
                // Select all books by default
                SelectAllBooks();
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load available books");
            StatusText = "Failed to load books";
        }
    }

    private async Task PerformSearchAsync()
    {
        if (!CanSearch) return;

        try
        {
            IsSearching = true;
            StatusText = "Searching...";
            SearchResults.Clear();
            ResultCount = 0;

            _logger.LogInformation("Starting search for: {SearchText}", SearchText);

            var booksToSearch = SelectedBooks.ToArray();
            var searchObservable = _searchService.SearchAsync(SearchText, booksToSearch);

            searchObservable.Subscribe(
                results =>
                {
                    Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var hit in results)
                        {
                            SearchResults.Add(hit);
                        }
                        
                        ResultCount = SearchResults.Count;
                        StatusText = $"Found {ResultCount} results";
                        IsSearching = false;
                    });
                },
                error =>
                {
                    _logger.LogError(error, "Search failed");
                    Dispatcher.UIThread.Post(() =>
                    {
                        StatusText = "Search failed";
                        IsSearching = false;
                    });
                }
            );
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Search error");
            StatusText = "Search error occurred";
            IsSearching = false;
        }
    }

    private void ClearSearch()
    {
        SearchText = string.Empty;
        SearchResults.Clear();
        ResultCount = 0;
        StatusText = "Ready to search";
        SelectedResult = null;
    }

    private void SelectAllBooks()
    {
        SelectedBooks.Clear();
        foreach (var book in AvailableBooks)
        {
            SelectedBooks.Add(book);
        }
        OnPropertyChanged(nameof(CanSearch));
        ((SimpleCommand)SearchCommand).RaiseCanExecuteChanged();
    }

    private void ClearBookSelection()
    {
        SelectedBooks.Clear();
        OnPropertyChanged(nameof(CanSearch));
        ((SimpleCommand)SearchCommand).RaiseCanExecuteChanged();
    }

    private void OpenSearchResult(BookHit? hit)
    {
        if (hit == null) return;
        
        _logger.LogInformation("Opening search result for book: {BookName}", hit.Book.Name);
        
        // Trigger event to open book with search term for highlighting
        BookOpenRequested?.Invoke(hit.Book, SearchText);
    }

    // INotifyPropertyChanged implementation
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

