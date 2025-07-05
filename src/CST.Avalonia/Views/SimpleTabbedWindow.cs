using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Styling;
using CST.Avalonia.ViewModels;
using CST.Avalonia.Services;
using CST;
using CST.Conversion;
using Microsoft.Extensions.DependencyInjection;

namespace CST.Avalonia.Views;

public class SimpleTabbedWindow : Window
{
    private readonly List<BookTabInfo> _openBooks = new();
    private BookTabInfo? _selectedBook;
    private ContentControl _openBookHost;
    private ContentControl _selectedBookHost;
    private StackPanel _tabsPanel;
    private Border _welcomePanel;
    private Script _defaultScript = Script.Latin;
    private ComboBox? _paliScriptCombo;

    public SimpleTabbedWindow()
    {
        Title = "CST - Chaṭṭha Saṅgāyana Tipiṭaka";
        WindowStartupLocation = WindowStartupLocation.CenterScreen;
        Width = 1400;
        Height = 900;
        MinWidth = 800;
        MinHeight = 600;

        BuildUI();
        UpdateDisplayState();
    }

    private void BuildUI()
    {
        // Create main content container with toolbar
        var mainContainer = new DockPanel();
        
        // Top Toolbar
        var toolbar = CreateMainToolbar();
        DockPanel.SetDock(toolbar, Dock.Top);
        mainContainer.Children.Add(toolbar);
        
        // Create main grid
        var mainGrid = new Grid();
        
        // Define columns
        var col1 = new ColumnDefinition(350, GridUnitType.Pixel);
        col1.MinWidth = 200;
        mainGrid.ColumnDefinitions.Add(col1);
        mainGrid.ColumnDefinitions.Add(new ColumnDefinition(4, GridUnitType.Pixel));
        var col3 = new ColumnDefinition(1, GridUnitType.Star);
        col3.MinWidth = 400;
        mainGrid.ColumnDefinitions.Add(col3);

        // Left Panel: Open Book Tree
        var leftPanel = new Border
        {
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(0, 0, 1, 0)
        };
        Grid.SetColumn(leftPanel, 0);

        var leftDockPanel = new DockPanel();
        
        // Panel Header
        var leftHeader = new Border
        {
            Background = Brushes.LightBlue,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        DockPanel.SetDock(leftHeader, Dock.Top);
        
        var leftHeaderText = new TextBlock
        {
            Text = "Select a Book",
            FontWeight = FontWeight.Bold,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 4)
        };
        leftHeader.Child = leftHeaderText;
        leftDockPanel.Children.Add(leftHeader);

        // Open Book Content Host
        _openBookHost = new ContentControl();
        leftDockPanel.Children.Add(_openBookHost);
        leftPanel.Child = leftDockPanel;
        mainGrid.Children.Add(leftPanel);

        // Splitter
        var splitter = new GridSplitter
        {
            Background = Brushes.Gray,
            Width = 4,
            HorizontalAlignment = HorizontalAlignment.Stretch
        };
        Grid.SetColumn(splitter, 1);
        mainGrid.Children.Add(splitter);

        // Right Panel: Book Display Area
        var rightPanel = new DockPanel();
        Grid.SetColumn(rightPanel, 2);

        // Tab Strip Container with thin scrollbar
        var tabStripContainer = new Grid();
        var tabsRowDefinition = new RowDefinition(32, GridUnitType.Pixel); // Fixed height for tabs
        var scrollbarRowDefinition = new RowDefinition(0, GridUnitType.Pixel); // Start with 0 height - will expand when needed
        tabStripContainer.RowDefinitions.Add(tabsRowDefinition);
        tabStripContainer.RowDefinitions.Add(scrollbarRowDefinition);
        DockPanel.SetDock(tabStripContainer, Dock.Top);
        
        // Tab Strip (without built-in scrollbar)
        var tabStrip = new Border
        {
            Background = Brushes.LightGray,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(0, 0, 0, 1)
        };
        Grid.SetRow(tabStrip, 0);
        
        var tabScrollViewer = new ScrollViewer
        {
            HorizontalScrollBarVisibility = ScrollBarVisibility.Hidden,
            VerticalScrollBarVisibility = ScrollBarVisibility.Disabled,
            Margin = new Thickness(4)
        };

        // Tab Headers Panel
        _tabsPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center
        };

        tabScrollViewer.Content = _tabsPanel;
        tabStrip.Child = tabScrollViewer;
        
        // Very thin scrollbar - fixed height, simple design
        var thinScrollTrack = new Border
        {
            Background = Brushes.LightGray,
            Height = 3,
            Margin = new Thickness(4, 0, 4, 0),
            IsVisible = false // Start hidden, will show when needed
        };
        Grid.SetRow(thinScrollTrack, 1);
        
        var thinScrollThumb = new Border
        {
            Background = Brushes.DarkGray,
            Height = 3,
            Width = 50, // Will be updated dynamically
            HorizontalAlignment = HorizontalAlignment.Left,
            CornerRadius = new CornerRadius(1.5),
            Cursor = new Cursor(StandardCursorType.Hand)
        };
        
        thinScrollTrack.Child = thinScrollThumb;
        
        // Add dragging functionality to the scrollbar thumb
        bool isDragging = false;
        double dragStartX = 0;
        double scrollStartOffset = 0;
        
        thinScrollThumb.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(thinScrollThumb).Properties.IsLeftButtonPressed)
            {
                isDragging = true;
                dragStartX = e.GetPosition(thinScrollTrack).X;
                scrollStartOffset = tabScrollViewer.Offset.X;
                e.Handled = true;
            }
        };
        
        thinScrollTrack.PointerMoved += (s, e) =>
        {
            if (isDragging)
            {
                var currentX = e.GetPosition(thinScrollTrack).X;
                var deltaX = currentX - dragStartX;
                var trackWidth = thinScrollTrack.Bounds.Width - 8; // Account for margins
                var thumbWidth = thinScrollThumb.Width;
                var scrollableWidth = tabScrollViewer.Extent.Width - tabScrollViewer.Viewport.Width;
                
                if (trackWidth > thumbWidth && scrollableWidth > 0)
                {
                    var scrollRatio = deltaX / (trackWidth - thumbWidth);
                    var newOffset = Math.Max(0, Math.Min(scrollableWidth, scrollStartOffset + (scrollRatio * scrollableWidth)));
                    tabScrollViewer.Offset = new Vector(newOffset, 0);
                }
                e.Handled = true;
            }
        };
        
        thinScrollTrack.PointerReleased += (s, e) =>
        {
            if (isDragging)
            {
                isDragging = false;
                e.Handled = true;
            }
        };
        
        // Also allow clicking on the track to jump to position
        thinScrollTrack.PointerPressed += (s, e) =>
        {
            if (e.GetCurrentPoint(thinScrollTrack).Properties.IsLeftButtonPressed && !isDragging)
            {
                var clickX = e.GetPosition(thinScrollTrack).X;
                var trackWidth = thinScrollTrack.Bounds.Width - 8;
                var thumbWidth = thinScrollThumb.Width;
                var scrollableWidth = tabScrollViewer.Extent.Width - tabScrollViewer.Viewport.Width;
                
                if (trackWidth > thumbWidth && scrollableWidth > 0)
                {
                    var targetThumbPosition = Math.Max(0, Math.Min(trackWidth - thumbWidth, clickX - 4 - thumbWidth / 2));
                    var scrollRatio = targetThumbPosition / (trackWidth - thumbWidth);
                    var newOffset = scrollRatio * scrollableWidth;
                    tabScrollViewer.Offset = new Vector(newOffset, 0);
                }
                e.Handled = true;
            }
        };
        
        // Update scrollbar visibility and position
        void UpdateScrollbar()
        {
            // Only show scrollbar when content is wider than viewport (scrollable)
            var isScrollable = tabScrollViewer.Extent.Width > tabScrollViewer.Viewport.Width;
            
            // Show/hide scrollbar AND adjust row height
            thinScrollTrack.IsVisible = isScrollable;
            scrollbarRowDefinition.Height = isScrollable ? new GridLength(3, GridUnitType.Pixel) : new GridLength(0, GridUnitType.Pixel);
            
            if (isScrollable && thinScrollTrack.Bounds.Width > 0)
            {
                var trackWidth = thinScrollTrack.Bounds.Width - 8; // Account for margins
                var thumbWidth = Math.Max(20, (tabScrollViewer.Viewport.Width / tabScrollViewer.Extent.Width) * trackWidth);
                var scrollableWidth = tabScrollViewer.Extent.Width - tabScrollViewer.Viewport.Width;
                var thumbPosition = scrollableWidth > 0 ? 
                    (tabScrollViewer.Offset.X / scrollableWidth) * (trackWidth - thumbWidth) : 0;
                
                thinScrollThumb.Width = thumbWidth;
                thinScrollThumb.Margin = new Thickness(thumbPosition + 4, 0, 0, 0);
            }
        }
        
        // Update on scroll changes
        tabScrollViewer.ScrollChanged += (s, e) => UpdateScrollbar();
        
        // Also update when layout changes (tabs added/removed)
        tabScrollViewer.SizeChanged += (s, e) => UpdateScrollbar();
        thinScrollTrack.SizeChanged += (s, e) => UpdateScrollbar();
        
        // Handle mouse wheel on tab area for horizontal scrolling
        tabStrip.PointerWheelChanged += (s, e) =>
        {
            var delta = e.Delta.Y * 50; // Adjust scroll speed
            var newOffset = Math.Max(0, Math.Min(tabScrollViewer.Extent.Width - tabScrollViewer.Viewport.Width, tabScrollViewer.Offset.X - delta));
            tabScrollViewer.Offset = new Vector(newOffset, 0);
        };
        
        tabStripContainer.Children.Add(tabStrip);
        tabStripContainer.Children.Add(thinScrollTrack);
        rightPanel.Children.Add(tabStripContainer);

        // Book Display Content
        var contentGrid = new Grid();

        // Welcome Message
        _welcomePanel = new Border
        {
            Background = Brushes.WhiteSmoke
        };

        var welcomeStack = new StackPanel
        {
            HorizontalAlignment = HorizontalAlignment.Center,
            VerticalAlignment = VerticalAlignment.Center,
            Spacing = 16
        };

        welcomeStack.Children.Add(new TextBlock
        {
            Text = "📖",
            FontSize = 48,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        welcomeStack.Children.Add(new TextBlock
        {
            Text = "Welcome to CST",
            FontSize = 24,
            FontWeight = FontWeight.Bold,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        welcomeStack.Children.Add(new TextBlock
        {
            Text = "Select a book to begin reading",
            FontSize = 14,
            Foreground = Brushes.Gray,
            HorizontalAlignment = HorizontalAlignment.Center
        });

        _welcomePanel.Child = welcomeStack;
        contentGrid.Children.Add(_welcomePanel);

        // Selected Book Display
        _selectedBookHost = new ContentControl();
        contentGrid.Children.Add(_selectedBookHost);

        rightPanel.Children.Add(contentGrid);
        mainGrid.Children.Add(rightPanel);
        
        // Add main grid to container and set as content
        mainContainer.Children.Add(mainGrid);
        Content = mainContainer;
    }

    private Border CreateMainToolbar()
    {
        var toolbar = new Border
        {
            Background = Brushes.LightGray,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(0, 0, 0, 1),
            Height = 40
        };

        var toolbarPanel = new StackPanel
        {
            Orientation = Orientation.Horizontal,
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(8, 4)
        };

        // Interface Language dropdown (placeholder)
        var interfaceLangLabel = new TextBlock
        {
            Text = "Interface Language:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        };
        toolbarPanel.Children.Add(interfaceLangLabel);

        var interfaceLangCombo = new ComboBox
        {
            MinWidth = 120,
            Margin = new Thickness(0, 0, 20, 0)
        };
        interfaceLangCombo.Items.Add("English");
        interfaceLangCombo.SelectedIndex = 0;
        toolbarPanel.Children.Add(interfaceLangCombo);

        // Pali Script dropdown - this controls the default script for new books and the tree
        var paliScriptLabel = new TextBlock
        {
            Text = "Pali Script:",
            VerticalAlignment = VerticalAlignment.Center,
            Margin = new Thickness(0, 0, 5, 0)
        };
        toolbarPanel.Children.Add(paliScriptLabel);

        _paliScriptCombo = new ComboBox
        {
            MinWidth = 120
        };
        ToolTip.SetTip(_paliScriptCombo, "Default script for Select a Book tree and new book windows");
        
        // Add available scripts (excluding Unknown and IPE like we did for book display)
        var availableScripts = Enum.GetValues<Script>().Where(s => s != Script.Unknown && s != Script.Ipe);
        foreach (var script in availableScripts)
        {
            _paliScriptCombo.Items.Add(script);
        }
        _paliScriptCombo.SelectedItem = _defaultScript; // Default to Latin
        _paliScriptCombo.SelectionChanged += OnDefaultScriptChanged;
        toolbarPanel.Children.Add(_paliScriptCombo);

        toolbar.Child = toolbarPanel;
        return toolbar;
    }

    public void SetOpenBookContent(Control content)
    {
        _openBookHost.Content = content;
        
        // If it's an OpenBookPanel, set the initial script
        if (content is OpenBookPanel openBookPanel)
        {
            openBookPanel.UpdateScript(_defaultScript);
        }
    }
    
    public Script DefaultScript => _defaultScript;
    
    private void OnDefaultScriptChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_paliScriptCombo?.SelectedItem is Script selectedScript)
        {
            _defaultScript = selectedScript;
            
            // Update the OpenBookPanel to use the new script
            if (_openBookHost.Content is OpenBookPanel openBookPanel)
            {
                openBookPanel.UpdateScript(_defaultScript);
            }
        }
    }

    public void OpenBook(Book book, List<string>? searchTerms = null)
    {
        Console.WriteLine($"OpenBook called for: {book.FileName}");
        
        // Check if book is already open
        var existingTab = _openBooks.FirstOrDefault(t => t.Book.FileName == book.FileName);
        if (existingTab != null)
        {
            Console.WriteLine("Book already open, selecting existing tab");
            SelectBook(existingTab);
            return;
        }

        // Create new book display with the default script
        Console.WriteLine($"Creating BookDisplayViewModel with {searchTerms?.Count ?? 0} search terms and default script {_defaultScript}");
        
        // Get ChapterListsService from dependency injection
        var chapterListsService = App.ServiceProvider?.GetRequiredService<ChapterListsService>();
        
        var bookDisplayViewModel = new BookDisplayViewModel(book, searchTerms ?? new List<string>(), null, chapterListsService);
        bookDisplayViewModel.BookScript = _defaultScript; // Set the default script
        var bookDisplayView = new BookDisplayView
        {
            DataContext = bookDisplayViewModel
        };

        var bookTab = new BookTabInfo(book, bookDisplayViewModel, bookDisplayView);
        _openBooks.Add(bookTab);
        
        // Create tab UI
        CreateTabUI(bookTab);
        
        SelectBook(bookTab);
        UpdateDisplayState();
        Console.WriteLine("Book tab created and selected");
    }

    private void CreateTabUI(BookTabInfo bookTab)
    {
        var tabBorder = new Border
        {
            Background = Brushes.White,
            BorderBrush = Brushes.Gray,
            BorderThickness = new Thickness(1, 1, 1, 0),
            Margin = new Thickness(0, 0, 2, 0),
            Padding = new Thickness(8, 4)
        };

        var tabDockPanel = new DockPanel();

        var closeButton = new Button
        {
            Content = "✕",
            Width = 16,
            Height = 16,
            Background = Brushes.Transparent,
            BorderThickness = new Thickness(0),
            FontSize = 10,
            Margin = new Thickness(4, 0, 0, 0)
        };
        closeButton.Click += (s, e) => CloseBook(bookTab);
        DockPanel.SetDock(closeButton, Dock.Right);
        tabDockPanel.Children.Add(closeButton);

        var titleText = new TextBlock
        {
            Text = bookTab.DisplayTitle,
            VerticalAlignment = VerticalAlignment.Center,
            MaxWidth = 150
        };
        tabDockPanel.Children.Add(titleText);
        
        // Store reference to text block for updates
        bookTab.TabTextBlock = titleText;
        
        // Subscribe to DisplayTitle changes
        bookTab.BookDisplayViewModel.PropertyChanged += (sender, args) =>
        {
            if (args.PropertyName == nameof(BookDisplayViewModel.DisplayTitle))
            {
                titleText.Text = bookTab.DisplayTitle;
            }
        };

        tabBorder.Child = tabDockPanel;
        
        // Handle tab selection
        tabBorder.PointerPressed += (s, e) => SelectBook(bookTab);

        // Store reference for later removal
        bookTab.TabUI = tabBorder;
        
        _tabsPanel.Children.Add(tabBorder);
    }

    private void SelectBook(BookTabInfo bookTab)
    {
        // Update selection state and UI
        foreach (var tab in _openBooks)
        {
            tab.IsSelected = false;
            if (tab.TabUI != null)
            {
                tab.TabUI.Background = Brushes.LightGray;
            }
        }
        
        bookTab.IsSelected = true;
        if (bookTab.TabUI != null)
        {
            bookTab.TabUI.Background = Brushes.White;
        }
        
        _selectedBook = bookTab;
        _selectedBookHost.Content = bookTab.BookView;
        
        UpdateDisplayState();
    }

    private void CloseBook(BookTabInfo bookTab)
    {
        var index = _openBooks.IndexOf(bookTab);
        _openBooks.Remove(bookTab);

        // Remove tab UI
        if (bookTab.TabUI != null)
        {
            _tabsPanel.Children.Remove(bookTab.TabUI);
        }

        // Select adjacent tab if current tab was closed
        if (bookTab.IsSelected && _openBooks.Any())
        {
            var newIndex = Math.Min(index, _openBooks.Count - 1);
            SelectBook(_openBooks[newIndex]);
        }
        else if (!_openBooks.Any())
        {
            _selectedBook = null;
            _selectedBookHost.Content = null;
        }

        UpdateDisplayState();
    }

    private void UpdateDisplayState()
    {
        var hasBooks = _openBooks.Any();
        _welcomePanel.IsVisible = !hasBooks;
        _selectedBookHost.IsVisible = hasBooks;
    }
}

public class BookTabInfo
{
    public BookTabInfo(Book book, BookDisplayViewModel viewModel, BookDisplayView view)
    {
        Book = book;
        BookDisplayViewModel = viewModel;
        BookView = view;
    }

    public Book Book { get; }
    public BookDisplayViewModel BookDisplayViewModel { get; }
    public BookDisplayView BookView { get; }
    public string DisplayTitle => BookDisplayViewModel.DisplayTitle;
    public bool IsSelected { get; set; }
    public Border? TabUI { get; set; }
    public TextBlock? TabTextBlock { get; set; }
}