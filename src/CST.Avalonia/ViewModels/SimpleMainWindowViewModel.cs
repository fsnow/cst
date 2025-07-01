using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using System.Windows.Input;
using Avalonia.Controls;
using Avalonia.Threading;
using CST.Avalonia.Commands;
using CST.Avalonia.Services;
using CstBook = CST.Book;
using CST.Avalonia.Views;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.ViewModels;

public class SimpleMainWindowViewModel : INotifyPropertyChanged
{
    private readonly IBookService _bookService;
    private readonly ISearchService _searchService;
    private readonly ILocalizationService _localizationService;
    private readonly ILogger<SimpleMainWindowViewModel> _logger;

    public SimpleMainWindowViewModel(
        IBookService bookService,
        ISearchService searchService,
        ILocalizationService localizationService,
        ILogger<SimpleMainWindowViewModel> logger)
    {
        _bookService = bookService;
        _searchService = searchService;
        _localizationService = localizationService;
        _logger = logger;

        // Initialize basic data
        Languages = new ObservableCollection<CultureInfoDisplayItem>(_localizationService.GetAvailableLanguages());
        OpenBooks = new ObservableCollection<BookDisplayViewModel>();
        StatusText = "Ready";
        BookCount = 0;

        // Initialize commands
        OpenBookCommand = new SimpleCommand(OpenBook);
        ExitCommand = new SimpleCommand(Exit);
        FindCommand = new SimpleCommand(Find);
        ShowSearchCommand = new SimpleCommand(ShowSearch);
        ShowAboutCommand = new SimpleCommand(ShowAbout);
        ShowSelectBookCommand = new SimpleCommand(ShowSelectBook);

        // Load data
        _ = InitializeAsync();
    }

    // Properties
    public ObservableCollection<CultureInfoDisplayItem> Languages { get; }
    public ObservableCollection<BookDisplayViewModel> OpenBooks { get; }

    private string _statusText = "Ready";
    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    private int _bookCount = 0;
    public int BookCount
    {
        get => _bookCount;
        set
        {
            _bookCount = value;
            OnPropertyChanged();
        }
    }

    private CultureInfoDisplayItem? _selectedLanguage;
    public CultureInfoDisplayItem? SelectedLanguage
    {
        get => _selectedLanguage;
        set
        {
            _selectedLanguage = value;
            OnPropertyChanged();
        }
    }

    private BookDisplayViewModel? _selectedBook;
    public BookDisplayViewModel? SelectedBook
    {
        get => _selectedBook;
        set
        {
            _selectedBook = value;
            OnPropertyChanged();
        }
    }

    // Commands
    public ICommand OpenBookCommand { get; }
    public ICommand ExitCommand { get; }
    public ICommand FindCommand { get; }
    public ICommand ShowSearchCommand { get; }
    public ICommand ShowAboutCommand { get; }
    public ICommand ShowSelectBookCommand { get; }

    // Command implementations
    private void OpenBook()
    {
        _logger.LogInformation("OpenBook command executed - showing Open Book dialog");
        
        try
        {
            var openBookDialogViewModel = App.ServiceProvider?.GetRequiredService<OpenBookDialogViewModel>();
            if (openBookDialogViewModel != null)
            {
                // Subscribe to book open requests
                openBookDialogViewModel.BookOpenRequested += OnBookOpenRequestedFromDialog;
                openBookDialogViewModel.CloseRequested += OnOpenBookDialogCloseRequested;
                
                var openBookDialog = new Views.OpenBookDialog
                {
                    DataContext = openBookDialogViewModel
                };
                
                // Show as dialog
                _ = openBookDialog.ShowDialog(App.MainWindow);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open book dialog");
        }
    }

    private async void OnBookOpenRequestedFromDialog(CstBook book)
    {
        _logger.LogInformation("Opening book from dialog: {BookFileName}", book.FileName);
        
        try
        {
            // Convert CST.Book to Services.Book for compatibility
            var serviceBook = new Services.Book
            {
                Id = book.Index.ToString(),
                Name = book.ShortNavPath,
                Path = book.FileName
            };
            await OpenBookInTabAsync(serviceBook);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open book from dialog: {BookFileName}", book.FileName);
        }
    }

    private void OnOpenBookDialogCloseRequested()
    {
        _logger.LogInformation("Open Book dialog close requested");
        // The dialog will close itself, we just log the event
    }

    public async Task OpenBookInTabAsync(Book book)
    {
        try
        {
            // Check if book is already open
            var existingBookTab = OpenBooks.FirstOrDefault(b => b.Book?.Id == book.Id);
            if (existingBookTab != null)
            {
                SelectedBook = existingBookTab;
                return;
            }

            // Create new book display view model
            var bookDisplayViewModel = App.ServiceProvider?.GetRequiredService<BookDisplayViewModel>();
            if (bookDisplayViewModel != null)
            {
                // Set up close event handler
                bookDisplayViewModel.CloseRequested += () => CloseBookTab(bookDisplayViewModel);
                
                // Load the book content
                await bookDisplayViewModel.LoadBookAsync(book);
                
                // Add to tabs and select
                OpenBooks.Add(bookDisplayViewModel);
                SelectedBook = bookDisplayViewModel;
                
                _logger.LogInformation("Book opened in new tab: {BookName}", book.Name);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open book in tab: {BookName}", book.Name);
        }
    }

    private void CloseBookTab(BookDisplayViewModel bookViewModel)
    {
        if (OpenBooks.Contains(bookViewModel))
        {
            var wasSelected = SelectedBook == bookViewModel;
            OpenBooks.Remove(bookViewModel);
            
            // Select another tab if we closed the selected one
            if (wasSelected && OpenBooks.Count > 0)
            {
                SelectedBook = OpenBooks[OpenBooks.Count - 1];
            }
            else if (OpenBooks.Count == 0)
            {
                SelectedBook = null;
            }
            
            _logger.LogInformation("Book tab closed: {BookTitle}", bookViewModel.Title);
        }
    }

    private void Exit()
    {
        _logger.LogInformation("Exit command executed");
    }

    private void Find()
    {
        _logger.LogInformation("Find command executed");
    }

    private void ShowSearch()
    {
        _logger.LogInformation("ShowSearch command executed");
        
        try
        {
            var searchViewModel = App.ServiceProvider?.GetRequiredService<SearchViewModel>();
            if (searchViewModel != null)
            {
                // Subscribe to book open requests
                searchViewModel.BookOpenRequested += OnBookOpenRequested;
                
                var searchWindow = new SearchView
                {
                    DataContext = searchViewModel
                };
                
                // Show as dialog
                _ = searchWindow.ShowDialog(App.MainWindow);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open search window");
        }
    }

    private async void OnBookOpenRequested(Book book, string searchTerm)
    {
        _logger.LogInformation("Opening book from search: {BookName} with search term: {SearchTerm}", book.Name, searchTerm);
        
        try
        {
            // Open the book in a tab
            await OpenBookInTabAsync(book);
            
            // If we successfully opened the book, apply highlights
            var bookTab = OpenBooks.FirstOrDefault(b => b.Book?.Id == book.Id);
            if (bookTab != null && !string.IsNullOrEmpty(searchTerm))
            {
                // Create highlight positions for the search term
                var highlights = CreateHighlightsForSearchTerm(bookTab.Content, searchTerm);
                bookTab.SetHighlights(highlights);
                
                _logger.LogInformation("Applied {HighlightCount} highlights for term: {SearchTerm}", highlights.Count, searchTerm);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open book from search: {BookName}", book.Name);
        }
    }

    private List<CST.Avalonia.ViewModels.HighlightPosition> CreateHighlightsForSearchTerm(string content, string searchTerm)
    {
        var highlights = new List<CST.Avalonia.ViewModels.HighlightPosition>();
        
        if (string.IsNullOrEmpty(content) || string.IsNullOrEmpty(searchTerm))
            return highlights;
            
        var index = 0;
        while ((index = content.IndexOf(searchTerm, index, StringComparison.OrdinalIgnoreCase)) != -1)
        {
            highlights.Add(new CST.Avalonia.ViewModels.HighlightPosition
            {
                Start = index,
                Length = searchTerm.Length,
                Term = searchTerm
            });
            index += searchTerm.Length;
        }
        
        return highlights;
    }

    private void ShowAbout()
    {
        _logger.LogInformation("ShowAbout command executed");
    }
    
    private void ShowSelectBook()
    {
        _logger.LogInformation("ShowSelectBook command executed");
        
        try
        {
            var selectBookViewModel = App.ServiceProvider?.GetRequiredService<SelectBookViewModel>();
            if (selectBookViewModel != null)
            {
                // Subscribe to book selection events
                selectBookViewModel.BooksSelected += OnBooksSelected;
                selectBookViewModel.SelectionCancelled += OnSelectionCancelled;
                
                var selectBookWindow = new SelectBookView
                {
                    DataContext = selectBookViewModel
                };
                
                // Show as dialog
                _ = selectBookWindow.ShowDialog(App.MainWindow);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open select book window");
        }
    }

    private async void OnBooksSelected(Book[] books)
    {
        _logger.LogInformation("Books selected: {BookCount}", books.Length);
        
        try
        {
            // Open each selected book in a new tab
            foreach (var book in books)
            {
                await OpenBookInTabAsync(book);
            }
            
            _logger.LogInformation("Opened {BookCount} books in tabs", books.Length);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to open selected books");
        }
    }

    private void OnSelectionCancelled()
    {
        _logger.LogInformation("Book selection cancelled");
    }

    private async Task InitializeAsync()
    {
        try
        {
            _logger.LogInformation("Initializing application...");
            await _bookService.LoadBooksAsync();

            Dispatcher.UIThread.Post(() =>
            {
                BookCount = _bookService.Books.Count;
                StatusText = $"Ready - {BookCount} books loaded";
            });

            _logger.LogInformation("Application initialized successfully");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to initialize application");
            Dispatcher.UIThread.Post(() => StatusText = "Failed to initialize");
        }
    }

    // INotifyPropertyChanged implementation
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

