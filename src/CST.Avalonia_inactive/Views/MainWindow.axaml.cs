using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using CST.Avalonia.ViewModels;
using CST;

namespace CST.Avalonia.Views;

public partial class MainWindow : Window
{
    private readonly ObservableCollection<BookTabInfo> _openBooks = new();
    private BookTabInfo? _selectedBook;

    public MainWindow()
    {
        AvaloniaXamlLoader.Load(this);
        
        // Set up the tabs control
        var bookTabsControl = this.FindControl<ItemsControl>("BookTabsControl");
        if (bookTabsControl != null)
        {
            bookTabsControl.ItemsSource = _openBooks;
        }

        // Handle tab events
        SetupEventHandlers();
        
        // Initially show welcome panel
        UpdateDisplayState();
    }

    public void SetOpenBookContent(Control content)
    {
        var contentHost = this.FindControl<ContentControl>("OpenBookContentHost");
        if (contentHost != null)
        {
            contentHost.Content = content;
        }
    }

    public void OpenBook(Book book, List<string>? searchTerms = null)
    {
        // Check if book is already open
        var existingTab = _openBooks.FirstOrDefault(t => t.Book.FileName == book.FileName);
        if (existingTab != null)
        {
            SelectBook(existingTab);
            return;
        }

        // Create new book display
        var bookDisplayViewModel = new BookDisplayViewModel(book, searchTerms ?? new List<string>());
        var bookDisplayView = new BookDisplayView
        {
            DataContext = bookDisplayViewModel
        };

        var bookTab = new BookTabInfo(book, bookDisplayViewModel, bookDisplayView);
        _openBooks.Add(bookTab);
        
        SelectBook(bookTab);
        UpdateDisplayState();
    }

    private void SelectBook(BookTabInfo bookTab)
    {
        _selectedBook = bookTab;
        
        var selectedBookHost = this.FindControl<ContentControl>("SelectedBookContentHost");
        if (selectedBookHost != null)
        {
            selectedBookHost.Content = bookTab.BookView;
        }
        
        UpdateDisplayState();
    }

    private void CloseBook(BookTabInfo bookTab)
    {
        var index = _openBooks.IndexOf(bookTab);
        _openBooks.Remove(bookTab);

        // Select adjacent tab if current tab was closed
        if (bookTab == _selectedBook && _openBooks.Any())
        {
            var newIndex = Math.Min(index, _openBooks.Count - 1);
            SelectBook(_openBooks[newIndex]);
        }
        else if (!_openBooks.Any())
        {
            _selectedBook = null;
        }

        UpdateDisplayState();
    }

    private void CloseAllBooks()
    {
        _openBooks.Clear();
        _selectedBook = null;
        UpdateDisplayState();
    }

    private void UpdateDisplayState()
    {
        var welcomePanel = this.FindControl<Border>("WelcomePanel");
        var selectedBookHost = this.FindControl<ContentControl>("SelectedBookContentHost");
        var closeAllButton = this.FindControl<Button>("CloseAllTabsButton");

        if (welcomePanel != null)
        {
            welcomePanel.IsVisible = !_openBooks.Any();
        }

        if (selectedBookHost != null)
        {
            selectedBookHost.IsVisible = _openBooks.Any();
        }

        if (closeAllButton != null)
        {
            closeAllButton.IsVisible = _openBooks.Any();
        }
    }

    private void SetupEventHandlers()
    {
        // Handle close all button
        var closeAllButton = this.FindControl<Button>("CloseAllTabsButton");
        if (closeAllButton != null)
        {
            closeAllButton.Click += (s, e) => CloseAllBooks();
        }

        // Handle individual tab close buttons - this will be set up through data template events
        // For now, we'll handle it in the code-behind approach
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        
        // Set up tab selection and close handlers after the control is loaded
        var bookTabsControl = this.FindControl<ItemsControl>("BookTabsControl");
        if (bookTabsControl != null)
        {
            // We'll need to handle click events on the generated items
            // This is a simplified approach - in a full MVVM setup, we'd use commands
        }
    }
}

public class BookTabInfo
{
    public BookTabInfo(Book book, BookDisplayViewModel viewModel, BookDisplayView view)
    {
        Book = book;
        BookDisplayViewModel = viewModel;
        BookView = view;
        DisplayTitle = book.LongNavPath?.Split('/').LastOrDefault() ?? book.FileName;
    }

    public Book Book { get; }
    public BookDisplayViewModel BookDisplayViewModel { get; }
    public BookDisplayView BookView { get; }
    public string DisplayTitle { get; }
}