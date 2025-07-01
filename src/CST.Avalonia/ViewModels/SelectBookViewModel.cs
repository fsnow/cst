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

public class SelectBookViewModel : INotifyPropertyChanged
{
    private readonly IBookService _bookService;
    private readonly ILogger<SelectBookViewModel> _logger;

    public SelectBookViewModel(
        IBookService bookService,
        ILogger<SelectBookViewModel> logger)
    {
        _bookService = bookService;
        _logger = logger;

        BookCollections = new ObservableCollection<BookCollection>();
        SelectedBooks = new ObservableCollection<Book>();

        // Initialize commands
        OpenBookCommand = new SimpleCommand<Book>(OpenBook);
        SelectAllCommand = new SimpleCommand(SelectAllBooks);
        ClearSelectionCommand = new SimpleCommand(ClearSelection);
        OkCommand = new SimpleCommand(ConfirmSelection, () => SelectedBooks.Any());
        CancelCommand = new SimpleCommand(CancelSelection);

        // Load books and organize into collections
        LoadBookCollections();
    }

    // Properties
    public ObservableCollection<BookCollection> BookCollections { get; }
    public ObservableCollection<Book> SelectedBooks { get; }

    private string _statusText = "Loading books...";
    public string StatusText
    {
        get => _statusText;
        set
        {
            _statusText = value;
            OnPropertyChanged();
        }
    }

    private int _totalBooks = 0;
    public int TotalBooks
    {
        get => _totalBooks;
        set
        {
            _totalBooks = value;
            OnPropertyChanged();
        }
    }

    private Book? _selectedBook;
    public Book? SelectedBook
    {
        get => _selectedBook;
        set
        {
            _selectedBook = value;
            OnPropertyChanged();
        }
    }

    public bool HasSelection => SelectedBooks.Any();

    // Commands
    public ICommand OpenBookCommand { get; }
    public ICommand SelectAllCommand { get; }
    public ICommand ClearSelectionCommand { get; }
    public ICommand OkCommand { get; }
    public ICommand CancelCommand { get; }

    // Events
    public event Action<Book[]>? BooksSelected;
    public event Action? SelectionCancelled;

    // Methods
    private async void LoadBookCollections()
    {
        try
        {
            StatusText = "Loading book collections...";
            
            await _bookService.LoadBooksAsync();
            
            Dispatcher.UIThread.Post(() =>
            {
                BookCollections.Clear();
                
                // Organize books into collections based on CST structure
                var collections = new[]
                {
                    new BookCollection("Sutta Piṭaka", "Collection of discourses"),
                    new BookCollection("Vinaya Piṭaka", "Monastic rules and regulations"),
                    new BookCollection("Abhidhamma Piṭaka", "Philosophical and psychological analysis")
                };

                // Organize books by their actual CST classification
                foreach (var book in _bookService.Books)
                {
                    var cstBook = ((BookService)_bookService).GetCstBook(book);
                    if (cstBook != null)
                    {
                        var targetCollection = cstBook.Pitaka switch
                        {
                            CST.Avalonia.Models.Pitaka.Sutta => collections[0],
                            CST.Avalonia.Models.Pitaka.Vinaya => collections[1],
                            CST.Avalonia.Models.Pitaka.Abhidhamma => collections[2],
                            _ => collections[0] // Default to Sutta for unknown
                        };
                        targetCollection.Books.Add(book);
                    }
                    else
                    {
                        // Fallback for books without CST classification
                        collections[0].Books.Add(book);
                    }
                }

                foreach (var collection in collections)
                {
                    BookCollections.Add(collection);
                }

                TotalBooks = _bookService.Books.Count;
                StatusText = $"{TotalBooks} books available";
            });
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load book collections");
            StatusText = "Failed to load books";
        }
    }

    private void OpenBook(Book? book)
    {
        if (book == null) return;
        
        _logger.LogInformation("Opening book: {BookName}", book.Name);
        SelectedBook = book;
        
        // TODO: Integrate with BookDisplayViewModel to show book content
        // For now, just add to selection
        if (!SelectedBooks.Contains(book))
        {
            SelectedBooks.Add(book);
            OnPropertyChanged(nameof(HasSelection));
            ((SimpleCommand)OkCommand).RaiseCanExecuteChanged();
        }
    }

    private void SelectAllBooks()
    {
        SelectedBooks.Clear();
        
        foreach (var collection in BookCollections)
        {
            foreach (var book in collection.Books)
            {
                SelectedBooks.Add(book);
            }
        }
        
        OnPropertyChanged(nameof(HasSelection));
        ((SimpleCommand)OkCommand).RaiseCanExecuteChanged();
        StatusText = $"Selected {SelectedBooks.Count} books";
    }

    private void ClearSelection()
    {
        SelectedBooks.Clear();
        OnPropertyChanged(nameof(HasSelection));
        ((SimpleCommand)OkCommand).RaiseCanExecuteChanged();
        StatusText = $"{TotalBooks} books available";
    }

    private void ConfirmSelection()
    {
        _logger.LogInformation("Confirming selection of {Count} books", SelectedBooks.Count);
        BooksSelected?.Invoke(SelectedBooks.ToArray());
    }

    private void CancelSelection()
    {
        _logger.LogInformation("Selection cancelled");
        SelectionCancelled?.Invoke();
    }

    // INotifyPropertyChanged implementation
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

// Book collection model for organizing books
public class BookCollection : INotifyPropertyChanged
{
    public BookCollection(string name, string description)
    {
        Name = name;
        Description = description;
        Books = new ObservableCollection<Book>();
        IsExpanded = true; // Start expanded for better UX
    }

    public string Name { get; }
    public string Description { get; }
    public ObservableCollection<Book> Books { get; }

    private bool _isExpanded = true;
    public bool IsExpanded
    {
        get => _isExpanded;
        set
        {
            _isExpanded = value;
            OnPropertyChanged();
        }
    }

    private bool _isSelected = false;
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            _isSelected = value;
            OnPropertyChanged();
        }
    }

    public int BookCount => Books.Count;

    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}