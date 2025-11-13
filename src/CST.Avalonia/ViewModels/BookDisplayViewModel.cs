using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Xsl;
using Avalonia.Threading;
using ReactiveUI;
using CST;
using CST.Conversion;
using CST.Avalonia.Services;
using CST.Avalonia.Models;
using CST.Avalonia.Constants;
using CST.Avalonia.Views;
using CST.Avalonia.ViewModels.Dock;
using Serilog;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Util;
using Microsoft.Extensions.DependencyInjection;

namespace CST.Avalonia.ViewModels
{
    public class BookDisplayViewModel : ReactiveDocument
    {
        private readonly ScriptService _scriptService;
        private readonly ChapterListsService? _chapterListsService;
        private readonly ISettingsService? _settingsService;
        private readonly IFontService? _fontService;
        private readonly Book _book;
        private readonly List<string>? _searchTerms;
        private readonly List<TermPosition>? _searchPositions;  // NEW: Store positions with IsFirstTerm flags
        private readonly string? _initialAnchor;
        private readonly int? _docId;

        // Logger instance for BookDisplayViewModel
        private readonly ILogger _logger;
        
        // Event for requesting new book to be opened (will be handled by SimpleTabbedWindow)
        public event Action<Book, string?>? OpenBookRequested;
        
        public event Action<int>? NavigateToHighlightRequested;
        public event Action<string>? NavigateToChapterRequested;
        
        private Script _bookScript;
        private bool _isLoading;
        private string _pageStatusText = "";
        private string _bookInfoText = "";
        private string _hitStatusText = "";
        private string _pageReferencesText = "";
        
        // Page reference properties (from CST4 FormBookDisplay.cs)
        private string _vriPage = "*";
        private string _myanmarPage = "*";
        private string _ptsPage = "*";
        private string _thaiPage = "*";
        private string _otherPage = "*";
        private string _currentParagraph = "*";
        private int _currentHitIndex;
        private int _totalHits;
        private bool _hasSearchHighlights;
        private bool _hasChapters;
        private bool _hasLinkedBooks;
        private bool _hasMula;
        private bool _hasAtthakatha;
        private bool _hasTika;
        private DivTag? _selectedChapter;
        private string _htmlContent = "";
        private bool _isWebViewAvailable = true; // Start optimistically to avoid fallback flash
        private bool _updatingChapterFromScroll = false;
        private bool _isFloating = false; // True when this book is in a floating window
        private bool _isInitializing = true;
        private WebViewLifecycleOperation _webViewLifecycleOperation = WebViewLifecycleOperation.None;
        private WebViewState? _savedWebViewState = null; // Saved state during float/unfloat
        private readonly CstDockFactory? _dockFactory; // Factory for float/unfloat operations
        private string? _lastCapturedAnchor = null; // Cached anchor for scroll position restoration (persists across float/unfloat)
        private string? _pendingAnchorNavigation = null; // Anchor to navigate to when View becomes available

        public BookDisplayViewModel(Book book, List<string>? searchTerms = null, string? initialAnchor = null, ChapterListsService? chapterListsService = null, ISettingsService? settingsService = null, IFontService? fontService = null, int? docId = null, List<TermPosition>? searchPositions = null, string? windowId = null, CstDockFactory? dockFactory = null)
        {
            _logger = Log.ForContext<BookDisplayViewModel>();
            // For now, create ScriptService without logger
            _scriptService = new ScriptService();
            _chapterListsService = chapterListsService;
            _settingsService = settingsService;
            _fontService = fontService;
            _dockFactory = dockFactory;
            _book = book;
            _searchTerms = searchTerms;
            _searchPositions = searchPositions;  // NEW: Store positions for two-color highlighting
            _initialAnchor = initialAnchor;
            _docId = docId;
            _bookScript = _scriptService.CurrentScript;

            // Configure Dock properties - CRITICAL: Unique GUID per instance to prevent ControlRecycling cache conflicts
            // This ensures each book window instance gets a unique ID, preventing CEF crashes when floating/unfloating
            if (windowId != null)
            {
                Id = windowId;  // Use restored ID from saved state
            }
            else
            {
                // Generate unique GUID-based ID for each book instance (like search results do)
                // This prevents ControlRecycling from reusing cached WebViews across different window contexts
                var bookGuid = Guid.NewGuid();
                Id = $"Book_{book.Index}_{book.FileName}_{bookGuid:N}";
            }
            Title = DisplayTitle;  // Initialize with book display name
            CanClose = true;    // Books can be closed
            CanFloat = true;    // Books can float to separate windows
            CanPin = false;     // Disable pinning

            // Debug search terms and positions
            if (searchTerms != null && searchTerms.Any())
            {
                Log.Information("[BookDisplay] Created with {Count} search terms: [{Terms}]",
                    searchTerms.Count, string.Join(", ", searchTerms));
                if (searchPositions != null && searchPositions.Any())
                {
                    Log.Information("[BookDisplay] Created with {Count} search positions (with IsFirstTerm flags)",
                        searchPositions.Count);
                    foreach (var pos in searchPositions.Take(5))
                    {
                        Log.Information("[BookDisplay]   Position: {Pos}, StartOffset: {Start}, EndOffset: {End}, IsFirstTerm: {IsFirst}, Word: {Word}",
                            pos.Position, pos.StartOffset, pos.EndOffset, pos.IsFirstTerm, pos.Word ?? "null");
                    }
                }
            }
            else
            {
                Log.Debug("[BookDisplay] Created with no search terms");
            }
            
            // Initialize collections - exclude Unknown and IPE from UI dropdown
            AvailableScripts = new ObservableCollection<Script>(
                Enum.GetValues<Script>().Where(s => s != Script.Unknown && s != Script.Ipe));
            Chapters = new ObservableCollection<DivTag>();
            
            // Initialize properties
            _totalHits = searchTerms?.Count ?? 0;
            _hasSearchHighlights = _totalHits > 0;
            _bookInfoText = GetBookInfoDisplayName(book);
            
            // Initialize commands with simple setup to avoid threading issues
            FirstHitCommand = ReactiveCommand.Create(NavigateToFirstHit);
            PreviousHitCommand = ReactiveCommand.Create(NavigateToPreviousHit);
            NextHitCommand = ReactiveCommand.Create(NavigateToNextHit);
            LastHitCommand = ReactiveCommand.Create(NavigateToLastHit);
            
            OpenMulaCommand = ReactiveCommand.CreateFromTask(OpenMulaBookAsync);
            OpenAtthakathaCommand = ReactiveCommand.CreateFromTask(OpenAtthakathaBookAsync);
            OpenTikaCommand = ReactiveCommand.CreateFromTask(OpenTikaBookAsync);
            
            // WebView edit commands
            CopyCommand = ReactiveCommand.Create(ExecuteCopy);
            SelectAllCommand = ReactiveCommand.Create(ExecuteSelectAll);

            // Window management commands (Float/Unfloat)
            FloatWindowCommand = ReactiveCommand.Create(FloatWindow);
            UnfloatWindowCommand = ReactiveCommand.Create(UnfloatWindow);

            // Subscribe to script changes - reload from source like CST4 does
            this.WhenAnyValue(x => x.BookScript)
                .Skip(1) // Skip initial value
                .Subscribe(async script => 
                {
                    _logger.Debug("Script changed - reloading from source files: {Script}", script.ToString());
                    
                    // Update font properties when script changes
                    this.RaisePropertyChanged(nameof(CurrentScriptFontFamily));
                    this.RaisePropertyChanged(nameof(CurrentScriptFontSize));
                    
                    // Save current page anchor for position preservation
                    string savedPageAnchor = "";
                    if (BookDisplayControl != null)
                    {
                        savedPageAnchor = BookDisplayControl.GetCurrentPageAnchor();
                        if (!string.IsNullOrEmpty(savedPageAnchor))
                        {
                            _logger.Debug("Saved page anchor: {Anchor}", savedPageAnchor);
                        }
                        else
                        {
                            _logger.Warning("No page anchor available, won't restore position");
                        }
                    }
                    
                    // Ensure property updates happen on UI thread
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        // Update book info text with new script
                        BookInfoText = GetBookInfoDisplayName(_book);
                        // Notify that DisplayTitle has changed (for tab updates)
                        this.RaisePropertyChanged(nameof(DisplayTitle));
                        
                        // Preserve the current selected chapter
                        var currentSelectedChapter = SelectedChapter;
                        
                        // Update all chapters to use the new script
                        foreach (var chapter in Chapters)
                        {
                            chapter.BookScript = script;
                        }
                        
                        // Force the chapters dropdown to refresh its display
                        var temp = Chapters.ToList();
                        Chapters.Clear();
                        foreach (var chapter in temp)
                        {
                            Chapters.Add(chapter);
                        }
                        
                        // Restore the selected chapter
                        if (currentSelectedChapter != null)
                        {
                            var matchingChapter = Chapters.FirstOrDefault(c => c.Id == currentSelectedChapter.Id);
                            if (matchingChapter != null)
                            {
                                _updatingChapterFromScroll = true;
                                SelectedChapter = matchingChapter;
                                _updatingChapterFromScroll = false;
                            }
                        }
                    });
                    await LoadBookContentAsync();
                    
                    // Restore page position after content loads
                    if (!string.IsNullOrEmpty(savedPageAnchor) && BookDisplayControl != null)
                    {
                        // Wait a bit for content to render and page references to be calculated
                        await Task.Delay(1000);
                        await Dispatcher.UIThread.InvokeAsync(() =>
                        {
                            BookDisplayControl.ScrollToPageAnchor(savedPageAnchor);
                            _logger.Debug("Restored position to page anchor: {Anchor}", savedPageAnchor);
                        });
                    }
                });
            
            // Subscribe to font setting changes
            if (_fontService != null)
            {
                _fontService.FontSettingsChanged += (_, _) =>
                {
                    this.RaisePropertyChanged(nameof(CurrentScriptFontFamily));
                    this.RaisePropertyChanged(nameof(CurrentScriptFontSize));
                };
            }
                
            this.WhenAnyValue(x => x.SelectedChapter)
                .Where(chapter => chapter != null && !_isInitializing)
                .Subscribe(chapter => NavigateToChapter(chapter!));
                
            // Initialize data on UI thread to prevent threading issues
            _ = Dispatcher.UIThread.InvokeAsync(InitializeAsync);
        }
        
        public ObservableCollection<Script> AvailableScripts { get; }
        public ObservableCollection<DivTag> Chapters { get; }
        
        public ReactiveCommand<Unit, Unit> FirstHitCommand { get; }
        public ReactiveCommand<Unit, Unit> PreviousHitCommand { get; }
        public ReactiveCommand<Unit, Unit> NextHitCommand { get; }
        public ReactiveCommand<Unit, Unit> LastHitCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenMulaCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenAtthakathaCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenTikaCommand { get; }
        public ReactiveCommand<Unit, Unit> CopyCommand { get; }
        public ReactiveCommand<Unit, Unit> SelectAllCommand { get; }
        public ReactiveCommand<Unit, Unit> FloatWindowCommand { get; }
        public ReactiveCommand<Unit, Unit> UnfloatWindowCommand { get; }

        public Script BookScript
        {
            get => _bookScript;
            set => this.RaiseAndSetIfChanged(ref _bookScript, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => this.RaiseAndSetIfChanged(ref _isLoading, value);
        }

        public bool IsFloating
        {
            get => _isFloating;
            set => this.RaiseAndSetIfChanged(ref _isFloating, value);
        }

        public WebViewLifecycleOperation WebViewLifecycleOperation
        {
            get => _webViewLifecycleOperation;
            set => this.RaiseAndSetIfChanged(ref _webViewLifecycleOperation, value);
        }

        public string PageStatusText
        {
            get => _pageStatusText;
            set => this.RaiseAndSetIfChanged(ref _pageStatusText, value);
        }

        public string BookInfoText
        {
            get => _bookInfoText;
            set => this.RaiseAndSetIfChanged(ref _bookInfoText, value);
        }

        public string HitStatusText
        {
            get => _hitStatusText;
            set => this.RaiseAndSetIfChanged(ref _hitStatusText, value);
        }

        public string PageReferencesText
        {
            get => _pageReferencesText;
            set => this.RaiseAndSetIfChanged(ref _pageReferencesText, value);
        }

        public string VriPage
        {
            get => _vriPage;
            set => this.RaiseAndSetIfChanged(ref _vriPage, value);
        }

        public string MyanmarPage
        {
            get => _myanmarPage;
            set => this.RaiseAndSetIfChanged(ref _myanmarPage, value);
        }

        public string PtsPage
        {
            get => _ptsPage;
            set => this.RaiseAndSetIfChanged(ref _ptsPage, value);
        }

        public string ThaiPage
        {
            get => _thaiPage;
            set => this.RaiseAndSetIfChanged(ref _thaiPage, value);
        }

        public string OtherPage
        {
            get => _otherPage;
            set => this.RaiseAndSetIfChanged(ref _otherPage, value);
        }

        /// <summary>
        /// Gets the last captured anchor for scroll position restoration.
        /// Updated every 200ms by the scroll timer, persists across float/unfloat.
        /// </summary>
        public string? LastCapturedAnchor => _lastCapturedAnchor;

        /// <summary>
        /// Updates the cached anchor for scroll position restoration.
        /// Called by BookDisplayView scroll timer every 200ms.
        /// Also clears any pending anchor navigation since user has now scrolled.
        /// </summary>
        public void UpdateLastCapturedAnchor(string? anchor)
        {
            _lastCapturedAnchor = anchor;

            // Clear pending navigation - user has scrolled manually, so we shouldn't
            // jump to the old pending position when tab reattaches
            if (_pendingAnchorNavigation != null)
            {
                _logger.Debug("Clearing pending anchor navigation {PendingAnchor} - user has scrolled to {CurrentAnchor}",
                    _pendingAnchorNavigation, anchor);
                _pendingAnchorNavigation = null;
            }
        }

        /// <summary>
        /// Called by BookDisplayView when it becomes attached to visual tree.
        /// Executes any pending anchor navigation that was deferred because View wasn't ready.
        /// </summary>
        public void OnViewAttached()
        {
            if (!string.IsNullOrEmpty(_pendingAnchorNavigation) && BookDisplayControl != null)
            {
                _logger.Information("View now attached - executing pending anchor navigation: {Anchor}", _pendingAnchorNavigation);
                Dispatcher.UIThread.Post(async () =>
                {
                    // Wait for content to fully render
                    await Task.Delay(500);
                    BookDisplayControl?.ScrollToPageAnchor(_pendingAnchorNavigation);
                    _pendingAnchorNavigation = null; // Clear after navigation
                });
            }
        }

        /// <summary>
        /// Captures the current scroll position immediately before shutdown.
        /// This ensures the very latest position is saved even if the user quit
        /// before the 200ms scroll timer fired.
        /// </summary>
        public async Task CaptureCurrentPositionAsync()
        {
            if (BookDisplayControl != null)
            {
                try
                {
                    var anchor = await BookDisplayControl.GetCurrentParagraphAnchorAsync();
                    if (!string.IsNullOrEmpty(anchor) && anchor != "null")
                    {
                        _lastCapturedAnchor = anchor;
                        _logger.Information("Captured final position before shutdown: {Anchor}", anchor);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Warning(ex, "Failed to capture final position before shutdown");
                }
            }
        }

        public string CurrentParagraph
        {
            get => _currentParagraph;
            set => this.RaiseAndSetIfChanged(ref _currentParagraph, value);
        }
        
        // Store raw anchor name for position preservation
        public string CurrentVriAnchor { get; private set; } = "";

        public int CurrentHitIndex
        {
            get => _currentHitIndex;
            set => this.RaiseAndSetIfChanged(ref _currentHitIndex, value);
        }

        public int TotalHits
        {
            get => _totalHits;
            set => this.RaiseAndSetIfChanged(ref _totalHits, value);
        }

        public bool HasSearchHighlights
        {
            get => _hasSearchHighlights;
            set => this.RaiseAndSetIfChanged(ref _hasSearchHighlights, value);
        }

        public bool HasChapters
        {
            get => _hasChapters;
            set => this.RaiseAndSetIfChanged(ref _hasChapters, value);
        }

        public bool HasLinkedBooks
        {
            get => _hasLinkedBooks;
            set => this.RaiseAndSetIfChanged(ref _hasLinkedBooks, value);
        }

        public bool HasMula
        {
            get => _hasMula;
            set => this.RaiseAndSetIfChanged(ref _hasMula, value);
        }

        public bool HasAtthakatha
        {
            get => _hasAtthakatha;
            set => this.RaiseAndSetIfChanged(ref _hasAtthakatha, value);
        }

        public bool HasTika
        {
            get => _hasTika;
            set => this.RaiseAndSetIfChanged(ref _hasTika, value);
        }

        public DivTag? SelectedChapter
        {
            get => _selectedChapter;
            set => this.RaiseAndSetIfChanged(ref _selectedChapter, value);
        }

        public string HtmlContent
        {
            get => _htmlContent;
            set => this.RaiseAndSetIfChanged(ref _htmlContent, value);
        }
        
        // Reference to the BookDisplayView control for scroll position management
        public BookDisplayView? BookDisplayControl { get; set; }

        public bool IsWebViewAvailable
        {
            get => _isWebViewAvailable;
            set => this.RaiseAndSetIfChanged(ref _isWebViewAvailable, value);
        }

        public Book Book => _book;
        public string DisplayTitle => GetBookDisplayName(_book);

        // Search data properties (for state restoration)
        public List<string>? SearchTerms => _searchTerms;
        public int? DocId => _docId;
        public List<TermPosition>? SearchPositions => _searchPositions;

        // Font properties for tab title, chapter dropdown, and status bar
        public string CurrentScriptFontFamily => _fontService?.GetScriptFontFamily(BookScript) ?? "Helvetica";
        public int CurrentScriptFontSize => _fontService?.GetScriptFontSize(BookScript) ?? 12;

        private string GetBookDisplayName(Book book)
        {
            // Use the last part of the LongNavPath (which is in Devanagari) and convert to current script with capitalization
            string bookName = "";
            
            if (!string.IsNullOrEmpty(book.LongNavPath))
            {
                var parts = book.LongNavPath.Split('/');
                bookName = parts[parts.Length - 1];
                
                // Convert from Devanagari to current script with capitalization
                if (_bookScript != Script.Devanagari)
                {
                    bookName = ScriptConverter.Convert(bookName, Script.Devanagari, _bookScript, true);
                }
            }
            else if (!string.IsNullOrEmpty(book.FileName))
            {
                bookName = Path.GetFileNameWithoutExtension(book.FileName);
            }
            else
            {
                bookName = "Unknown Book";
            }
            
            return bookName;
        }
        
        private string GetBookInfoDisplayName(Book book)
        {
            // Use ShortNavPath for bottom bar display (more concise than LongNavPath)
            string bookName = "";
            
            if (!string.IsNullOrEmpty(book.ShortNavPath))
            {
                bookName = book.ShortNavPath;
                
                // Convert from Devanagari to current script with capitalization
                if (_bookScript != Script.Devanagari)
                {
                    bookName = ScriptConverter.Convert(bookName, Script.Devanagari, _bookScript, true);
                }
            }
            else if (!string.IsNullOrEmpty(book.LongNavPath))
            {
                // Fallback to LongNavPath if ShortNavPath is not available
                var parts = book.LongNavPath.Split('/');
                bookName = parts[parts.Length - 1];
                
                if (_bookScript != Script.Devanagari)
                {
                    bookName = ScriptConverter.Convert(bookName, Script.Devanagari, _bookScript, true);
                }
            }
            else if (!string.IsNullOrEmpty(book.FileName))
            {
                bookName = Path.GetFileNameWithoutExtension(book.FileName);
            }
            else
            {
                bookName = "Unknown Book";
            }
            
            return bookName;
        }

        private async Task InitializeAsync()
        {
            _logger.Debug("InitializeAsync starting: {FileName}", _book.FileName);
            
            // Ensure UI property updates happen on UI thread
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = true);
            
            try
            {
                _logger.Debug("Step 1: Checking WebView availability");
                // Check if WebView is available
                CheckWebViewAvailability();
                
                _logger.Debug("Step 2: Loading chapters");
                // Load chapters if available
                await LoadChaptersAsync();
                
                _logger.Debug("Step 3: Checking linked books");
                // Check for linked books (must run on UI thread due to ReactiveUI property updates)
                await Dispatcher.UIThread.InvokeAsync(() => CheckLinkedBooks());
                
                _logger.Debug("Step 4: Loading book content");
                // Load book content
                await LoadBookContentAsync();
                
                _logger.Debug("Step 5: Handling initial navigation");
                // Navigate to initial position if specified
                if (!string.IsNullOrEmpty(_initialAnchor))
                {
                    _logger.Debug("Navigating to initial anchor: {Anchor}", _initialAnchor);
                    // Wait a bit for content to render, then navigate to anchor
                    await Task.Delay(1000);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        if (BookDisplayControl != null)
                        {
                            BookDisplayControl.ScrollToPageAnchor(_initialAnchor);
                            _logger.Debug("Scrolled to initial anchor: {Anchor}", _initialAnchor);
                        }
                        else
                        {
                            // View not created yet (inactive tab with ControlRecycling)
                            // Store for later navigation when tab becomes active
                            _pendingAnchorNavigation = _initialAnchor;
                            _logger.Information("View not ready - stored pending anchor navigation: {Anchor}", _initialAnchor);
                        }
                    });
                }
                else if (_searchTerms?.Any() == true)
                {
                    _logger.Debug("Setting up search navigation: {TermCount} terms", _searchTerms.Count);
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        CurrentHitIndex = 1;
                        UpdateHitStatusText();
                    });
                }
                
                _logger.Debug("InitializeAsync completed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error in InitializeAsync");
                            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
            }
        }

        private void CheckWebViewAvailability()
        {
            try
            {
                // Start optimistically with WebView enabled to avoid fallback flash
                _logger.Debug("WebView availability check - starting optimistically enabled");
                Dispatcher.UIThread.Post(() =>
                {
                    IsWebViewAvailable = true; // Start enabled, will be disabled if browser fails
                    PageStatusText = "Initializing browser...";
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsWebViewAvailable = false;
                    PageStatusText = $"Using fallback text display: {ex.Message}";
                });
            }
        }
        
        public void SetWebViewAvailability(bool available, string statusMessage = "")
        {
            IsWebViewAvailable = available;
            if (!string.IsNullOrEmpty(statusMessage))
            {
                PageStatusText = statusMessage;
            }
        }

        public void CompleteInitialization()
        {
            _isInitializing = false;
            _logger.Debug("ViewModel initialization complete. Navigation enabled.");
        }

        private async Task LoadChaptersAsync()
        {
            // Load chapters from ChapterListsService
            var chapters = await Task.Run(() =>
            {
                if (_chapterListsService == null)
                    return new List<DivTag>();
                    
                var originalChapters = _chapterListsService.GetChapterList(_book.Index) ?? new List<DivTag>();
                
                // Create a deep copy of the chapter list to prevent mutating the global static cache.
                // This is the fix for the chapter list corruption bug.
                return originalChapters.Select(c => new DivTag(c.Id, c.Heading, c.IndentLevel)).ToList();
            });
            
            // Update UI properties on UI thread
            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                Chapters.Clear();
                foreach (var chapter in chapters)
                {
                    // Set the script for proper display conversion
                    chapter.BookScript = _bookScript;
                    Chapters.Add(chapter);
                }
                HasChapters = chapters.Count > 0;
                
                // Set the first chapter as selected by default
                if (chapters.Count > 0 && SelectedChapter == null)
                {
                    SelectedChapter = Chapters.First();
                    _logger.Debug("Set default selected chapter: {ChapterId} - {ChapterHeading}", SelectedChapter.Id, SelectedChapter.Heading);
                }
                
                _logger.Debug("Loaded chapters: {ChapterCount} chapters for book {FileName}", chapters.Count, _book.FileName);
            });
        }

        private void CheckLinkedBooks()
        {
            // Port of CST4 FormBookDisplay.Init() logic (L112-122)
            // Check if the current Book object has any defined links
            if (_book.MulaIndex < 0 && _book.AtthakathaIndex < 0 && _book.TikaIndex < 0)
            {
                // No linked books available
                HasMula = false;
                HasAtthakatha = false;
                HasTika = false;
                HasLinkedBooks = false;
                _logger.Debug("No linked books found: {FileName}", _book.FileName);
            }
            else
            {
                // Set each button's enabled state based on whether its corresponding index is valid (>= 0)
                HasMula = _book.MulaIndex >= 0;
                HasAtthakatha = _book.AtthakathaIndex >= 0;
                HasTika = _book.TikaIndex >= 0;
                HasLinkedBooks = HasMula || HasAtthakatha || HasTika;
                
                _logger.Debug("Linked books - Book: {FileName}, Mula={HasMula} (index={MulaIndex}), " +
                                 $"Atthakatha={HasAtthakatha} (index={_book.AtthakathaIndex}), " +
                                 $"Tika={HasTika} (index={_book.TikaIndex})");
            }
        }

        private async Task LoadBookContentAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = true);
            
            try
            {
                var htmlContent = await GenerateHtmlContentAsync();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    HtmlContent = htmlContent;
                    PageStatusText = "Content loaded";
                    // Initialize page references text
                    UpdatePageReferencesText();
                });
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
            }
        }

        private async Task<string> GenerateHtmlContentAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Get the books directory
                    var booksDir = GetBooksDirectory();
                    if (booksDir == null)
                    {
                        _logger.Error("Cannot load book - no valid XML directory configured");
                        return "<html><body><h3>Error: No XML directory configured</h3><p>Please configure a valid XML directory in Settings.</p></body></html>";
                    }
                    
                    // Load XML content
                    var xmlPath = Path.Combine(booksDir, _book.FileName);
                    _logger.Debug("Loading XML from: {XmlPath}", xmlPath);
                    
                    if (!File.Exists(xmlPath))
                    {
                        _logger.Warning("XML file not found: {XmlPath}", xmlPath);
                        return "<html><body><h1>Book file not found</h1><p>File: " + xmlPath + "</p></body></html>";
                    }

                    // Read raw XML content as string first
                    _logger.Debug("Reading raw XML content as string");
                    string xmlContent = File.ReadAllText(xmlPath, System.Text.Encoding.UTF8);
                    
                    // Apply search highlighting to raw XML string if needed
                    if (_searchTerms?.Any() == true)
                    {
                        _logger.Debug("Applying search highlighting to raw XML - {TermCount} terms", _searchTerms.Count);
                        xmlContent = ApplySearchHighlightingToRawXml(xmlContent);
                    }

                    var xmlDoc = new XmlDocument();
                    
                    // Load the (potentially highlighted) XML content
                    _logger.Debug("Parsing XML content into document");
                    xmlDoc.LoadXml(xmlContent);
                    _logger.Debug("XML loaded successfully - root element: {RootElement}", xmlDoc.DocumentElement?.Name);

                    // Apply script conversion if needed - use ConvertBook for proper XML handling
                    if (_bookScript != Script.Devanagari)
                    {
                        _logger.Debug("Converting script - Devanagari to {Script}", _bookScript);
                        var convertedXml = ScriptConverter.ConvertBook(xmlDoc.OuterXml, _bookScript);
                        xmlDoc.LoadXml(convertedXml);
                    }
                    
                    // Update chapter display script after script conversion
                    Dispatcher.UIThread.Post(() =>
                    {
                        foreach (var chapter in Chapters)
                        {
                            chapter.BookScript = _bookScript;
                        }
                    });

                    // Apply XSL transformation
                    var xslPath = GetXslPath(_bookScript);
                    _logger.Debug("Using XSL file: {XslPath}", xslPath);
                    
                    if (!File.Exists(xslPath))
                    {
                        _logger.Warning("XSL file not found: {XslPath}", xslPath);
                        return "<html><body><h1>XSL file not found</h1><p>File: " + xslPath + "</p></body></html>";
                    }

                    var xslTransform = new XslCompiledTransform();
                    xslTransform.Load(xslPath);

                    using var stringWriter = new StringWriter();
                    xslTransform.Transform(xmlDoc, null, stringWriter);
                    
                    var htmlContent = stringWriter.ToString();
                    _logger.Debug("Generated HTML content - length: {Length}", htmlContent.Length);
                    
                    return htmlContent;
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error generating HTML content");
                                        return $"<html><body><h1>Error loading book</h1><p>{ex.Message}</p><pre>{ex.StackTrace}</pre></body></html>";
                }
            });
        }

        private string ApplySearchHighlightingToRawXml(string xmlContent)
        {
            Log.Debug("[BookDisplay] ApplyHighlighting called with {Count} search terms (IPE format)", _searchTerms?.Count ?? 0);
            if (_searchTerms != null && _searchTerms.Any())
            {
                Log.Debug("[BookDisplay] IPE Search terms: [{Terms}]", string.Join(", ", _searchTerms));
                Log.Debug("[BookDisplay] DocId: {DocId}, Book: {FileName}", _docId, _book.FileName);
                if (_searchPositions != null && _searchPositions.Any())
                {
                    Log.Information("[BookDisplay] Using {Count} pre-computed positions with IsFirstTerm flags for two-color highlighting",
                        _searchPositions.Count);
                }
            }

            if (_searchTerms == null || !_searchTerms.Any() || _docId == null)
            {
                Log.Debug("[BookDisplay] Skipping highlighting - no terms or no DocId");
                return xmlContent;
            }

            try
            {
                // NEW: If we have pre-computed positions (from phrase/proximity search), use them directly
                if (_searchPositions != null && _searchPositions.Any())
                {
                    return ApplyHighlightingFromPositions(xmlContent);
                }
                // Get the IndexingService to access the Lucene index
                var indexingService = App.ServiceProvider?.GetService<IIndexingService>();
                if (indexingService == null)
                {
                    _logger.Warning("IndexingService not available for highlighting");
                    return xmlContent;
                }

                var indexReader = indexingService.GetIndexReader();
                if (indexReader == null)
                {
                    _logger.Warning("Could not get index reader for highlighting");
                    return xmlContent;
                }

                // Use the raw XML content directly (this matches what Lucene indexed)
                var xmlString = xmlContent;
                
                // Get term vectors for this document
                var termVectors = indexReader.GetTermVector(_docId.Value, "text");
                if (termVectors == null)
                {
                    _logger.Warning("No term vectors found for document {DocId}", _docId.Value);
                    return xmlContent;
                }

                // Collect all offset information for our search terms
                var offsetList = new List<(int start, int end, string term)>();
                
                Log.Debug("[BookDisplay] Processing {Count} search terms for highlighting", _searchTerms.Count);
                
                foreach (var searchTerm in _searchTerms)
                {
                    if (string.IsNullOrWhiteSpace(searchTerm)) continue;
                    
                    Log.Debug("[BookDisplay] Looking for term: '{Term}'", searchTerm);
                    
                    // Get the postings for this term
                    var termsEnum = termVectors.GetIterator(null);
                    var termBytes = new BytesRef(System.Text.Encoding.UTF8.GetBytes(searchTerm));
                    
                    if (termsEnum.SeekExact(termBytes))
                    {
                        Log.Debug("[BookDisplay] Found term '{Term}' in index", searchTerm);
                        
                        // Get positions and offsets for this term
                        var postingsEnum = termsEnum.DocsAndPositions(null, null, DocsAndPositionsFlags.OFFSETS);
                        if (postingsEnum != null)
                        {
                            while (postingsEnum.NextDoc() != DocIdSetIterator.NO_MORE_DOCS)
                            {
                                var freq = postingsEnum.Freq;
                                Log.Debug("[BookDisplay] Term '{Term}' appears {Count} times in document", searchTerm, freq);
                                
                                for (int i = 0; i < freq; i++)
                                {
                                    postingsEnum.NextPosition();
                                    var startOffset = postingsEnum.StartOffset;
                                    var endOffset = postingsEnum.EndOffset;
                                    
                                    if (startOffset >= 0 && endOffset > startOffset)
                                    {
                                        // Verify the offset points to the expected text
                                        // NOTE: Lucene offsets are inclusive-exclusive, so no +1
                                        var actualText = xmlString.Substring(startOffset, endOffset - startOffset);
                                        offsetList.Add((startOffset, endOffset, searchTerm));
                                        Log.Debug("[BookDisplay] Offset {Start}-{End} for '{Term}': actual text '{Text}'",
                                            startOffset, endOffset, searchTerm, actualText);
                                    }
                                    else
                                    {
                                        Log.Warning("[BookDisplay] Invalid offset for '{Term}': {Start}-{End}", 
                                            searchTerm, startOffset, endOffset);
                                    }
                                }
                            }
                        }
                        else
                        {
                            Log.Debug("[BookDisplay] No postings enum for term '{Term}'", searchTerm);
                        }
                    }
                    else
                    {
                        Log.Debug("[BookDisplay] Term '{Term}' not found in index", searchTerm);
                    }
                }

                if (!offsetList.Any())
                {
                    Log.Debug("[BookDisplay] No offsets found for any search terms");
                    return xmlContent;
                }

                // Sort offsets by position in REVERSE order (back to front)
                // This is critical so that inserting tags doesn't invalidate later offsets
                offsetList.Sort((a, b) => b.start.CompareTo(a.start));
                
                // Build the highlighted XML by inserting <hi> tags at the offsets
                var sb = new StringBuilder(xmlString);
                var hitCount = offsetList.Count;

                // For single term search, use the same highlight style for all instances
                // For multi-term search, each term would get different colors (not implemented yet)
                var isSingleTerm = _searchTerms.Count == 1;
                var hitIndex = hitCount - 1;
                
                Log.Debug("[BookDisplay] Applying {Count} highlights (single-term: {IsSingle})", hitCount, isSingleTerm);
                
                foreach (var (start, end, term) in offsetList)
                {
                    // Get the actual text at this offset
                    var highlightedText = xmlString.Substring(start, end - start + 1);
                    
                    // Create the highlight tags - always use unique IDs for navigation
                    var openTag = $"<hi rend=\"hit\" id=\"hit{hitIndex + 1}\">";
                    var closeTag = "</hi>";
                    
                    // Replace the text at this offset with highlighted version
                    sb.Remove(start, end - start + 1);
                    sb.Insert(start, openTag + highlightedText + closeTag);
                    
                    Log.Debug("[BookDisplay] Applied highlight #{Number} at offset {Start}-{End}: '{Text}' (term: '{Term}')", 
                        hitIndex + 1, start, end, highlightedText, term);
                    hitIndex--;
                }
                
                // Return the highlighted XML string
                var highlightedXml = sb.ToString();
                
                Log.Information("[BookDisplay] Successfully applied {Count} highlights", hitCount);
                
                // Update hit count and status
                var totalHits = hitCount;
                Dispatcher.UIThread.Post(() =>
                {
                    TotalHits = totalHits;
                    HasSearchHighlights = totalHits > 0;
                    if (totalHits > 0)
                    {
                        CurrentHitIndex = 1;
                        UpdateHitStatusText();
                    }
                });
                
                return highlightedXml;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to apply search highlighting");
                Log.Error(ex, "[BookDisplay] Error applying highlights");
                return xmlContent; // Return original content on error
            }
        }

        /// <summary>
        /// Apply highlighting using pre-computed TermPosition objects with IsFirstTerm flags (for phrase/proximity search)
        /// </summary>
        private string ApplyHighlightingFromPositions(string xmlContent)
        {
            try
            {
                Log.Information("[BookDisplay] ApplyHighlightingFromPositions: Using {Count} pre-computed positions", _searchPositions!.Count);

                // Sort positions by offset in REVERSE order (back to front) to avoid invalidating offsets
                var sortedPositions = _searchPositions.OrderByDescending(p => p.StartOffset).ToList();

                var sb = new StringBuilder(xmlContent);
                var hitIndex = sortedPositions.Count;

                foreach (var position in sortedPositions)
                {
                    // Get the actual text at this offset
                    var startOffset = position.StartOffset;
                    var endOffset = position.EndOffset;

                    if (startOffset < 0 || endOffset < startOffset || endOffset >= xmlContent.Length)
                    {
                        Log.Warning("[BookDisplay] Invalid offset range: {Start}-{End} (xml length: {Length})",
                            startOffset, endOffset, xmlContent.Length);
                        continue;
                    }

                    // NOTE: CST4 uses +1 for Lucene offsets (see Search.cs:595, 676)
                    var highlightedText = xmlContent.Substring(startOffset, endOffset - startOffset + 1);

                    // Check for existing <hi> tags in the highlighted text (tag crossing detection from CST4)
                    bool hasHiOpen = highlightedText.Contains("<hi");
                    bool hasHiClose = highlightedText.Contains("</hi");

                    // Determine highlight style based on IsFirstTerm flag
                    var rendValue = position.IsFirstTerm ? "hit" : "context";
                    var hiBoldOpen = "<hi rend=\"bold\">";
                    var hiHitOpen = position.IsFirstTerm
                        ? $"<hi rend=\"hit\" id=\"hit{hitIndex}\">"
                        : $"<hi rend=\"context\">";
                    var hiClose = "</hi>";

                    // Build the highlighted text based on tag crossing cases (from CST4 Search.cs:605-626)
                    string finalText;
                    if ((hasHiOpen && hasHiClose) || (!hasHiOpen && !hasHiClose))
                    {
                        // Normal case: no tag crossing or both tags present
                        finalText = hiHitOpen + highlightedText + hiClose;
                    }
                    else if (hasHiOpen)
                    {
                        // Word contains opening <hi> tag - close and reopen properly
                        finalText = hiHitOpen + highlightedText + hiClose + hiClose + hiBoldOpen;
                    }
                    else // hasHiClose
                    {
                        // Word contains closing </hi> tag - close first, then apply highlight
                        finalText = hiClose + hiHitOpen + hiBoldOpen + highlightedText + hiClose;
                    }

                    // Replace the text at this offset with highlighted version
                    sb.Remove(startOffset, endOffset - startOffset + 1);
                    sb.Insert(startOffset, finalText);

                    Log.Debug("[BookDisplay] Applied {RendValue} highlight #{HitNum} at offset {Start}-{End}: '{Text}' (IsFirstTerm={IsFirst}, Word={Word})",
                        rendValue, hitIndex, startOffset, endOffset, highlightedText, position.IsFirstTerm, position.Word ?? "null");

                    hitIndex--;
                }

                var highlightedXml = sb.ToString();
                Log.Information("[BookDisplay] Successfully applied {Count} two-color highlights from positions", _searchPositions.Count);

                // Update hit count and status (count only first terms for navigation)
                var totalHits = _searchPositions.Count(p => p.IsFirstTerm);
                Dispatcher.UIThread.Post(() =>
                {
                    TotalHits = totalHits;
                    HasSearchHighlights = totalHits > 0;
                    if (totalHits > 0)
                    {
                        CurrentHitIndex = 1;
                        UpdateHitStatusText();
                    }
                });

                return highlightedXml;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "[BookDisplay] Error applying highlighting from positions");
                _logger.Error(ex, "Failed to apply highlighting from positions");
                return xmlContent;
            }
        }

        private void CollectTextNodes(XmlNode? node, List<XmlNode> textNodes)
        {
            if (node == null) return;

            if (node.NodeType == XmlNodeType.Text)
            {
                // Skip text nodes that are already inside highlighting elements
                var parent = node.ParentNode;
                if (parent?.Name == "hi" && parent.Attributes?["rend"]?.Value == "hit")
                    return;

                textNodes.Add(node);
            }
            else if (node.HasChildNodes)
            {
                foreach (XmlNode child in node.ChildNodes)
                {
                    CollectTextNodes(child, textNodes);
                }
            }
        }

        private string? GetBooksDirectory()
        {
            // Use the XML directory from settings if available
            if (_settingsService?.Settings?.XmlBooksDirectory is string xmlDir && !string.IsNullOrEmpty(xmlDir))
            {
                // Validate that the directory exists and contains at least one book file
                if (Directory.Exists(xmlDir))
                {
                    // Check for the first book file (s0101m.mul.xml)
                    var testFile = Path.Combine(xmlDir, "s0101m.mul.xml");
                    if (File.Exists(testFile))
                    {
                        return xmlDir;
                    }
                    else
                    {
                        _logger.Warning("XML directory '{Directory}' does not contain expected book file s0101m.mul.xml", xmlDir);
                    }
                }
                else
                {
                    _logger.Warning("XML directory '{Directory}' does not exist", xmlDir);
                }
            }
            
            // No valid XML directory configured
            _logger.Warning("No valid XML directory configured in settings");
            return null;
        }

        private string GetXslPath(Script script)
        {
            var scriptName = script switch
            {
                Script.Latin => "latn",
                Script.Devanagari => "deva",
                Script.Thai => "thai",
                Script.Myanmar => "mymr",
                Script.Sinhala => "sinh",
                Script.Khmer => "khmr",
                Script.Bengali => "beng",
                Script.Gujarati => "gujr",
                Script.Gurmukhi => "guru",
                Script.Kannada => "knda",
                Script.Malayalam => "mlym",
                Script.Telugu => "telu",
                Script.Tibetan => "tibt",
                _ => "latn"
            };
            
            var xslFileName = $"tipitaka-{scriptName}.xsl";
            
            // Ensure XSL files are set up in user directory
            EnsureXslFilesInUserDirectory();
            
            // Use user's Application Support directory (editable location)
            var userXslPath = GetUserXslPath(xslFileName);
            if (File.Exists(userXslPath))
            {
                _logger.Debug("Using user XSL file: {Path}", userXslPath);
                return userXslPath;
            }

            // If XSL file doesn't exist, log error but return the expected path
            // The application should have already copied the XSL files during initialization
            _logger.Error("XSL file not found: {Path}", userXslPath);
            return userXslPath;
        }
        
        private string GetUserXslPath(string xslFileName)
        {
            var appSupportDir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                AppConstants.AppDataDirectoryName,
                "xsl");
            return Path.Combine(appSupportDir, xslFileName);
        }
        
        private void EnsureXslFilesInUserDirectory()
        {
            try
            {
                var userXslDir = Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                    AppConstants.AppDataDirectoryName,
                    "xsl");
                
                // Create directory if it doesn't exist
                if (!Directory.Exists(userXslDir))
                {
                    Directory.CreateDirectory(userXslDir);
                    _logger.Information("Created user XSL directory: {Path}", userXslDir);
                }
                
                // Check if we need to copy XSL files from app bundle
                var existingFiles = Directory.GetFiles(userXslDir, "*.xsl");
                if (existingFiles.Length == 0)
                {
                    // Try to copy from app bundle if running from .app
                    CopyXslFilesFromBundle(userXslDir);
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to ensure XSL files in user directory");
            }
        }
        
        private void CopyXslFilesFromBundle(string targetDir)
        {
            try
            {
                string? sourceXslDir = null;

                // First, try to find XSL files in development environment
                var assemblyLocation = System.Reflection.Assembly.GetExecutingAssembly().Location;
                var projectXslPath = Path.Combine(
                    Path.GetDirectoryName(assemblyLocation) ?? "",
                    "..", "..", "..", "Xsl");

                if (Directory.Exists(projectXslPath))
                {
                    sourceXslDir = projectXslPath;
                    _logger.Information("Found XSL files in development directory: {Path}", projectXslPath);
                }
                else
                {
                    // Try app bundle location for production (lowercase)
                    var bundleResourcesPath = Path.Combine(
                        Path.GetDirectoryName(assemblyLocation) ?? "",
                        "..", "Resources", "xsl");

                    if (Directory.Exists(bundleResourcesPath))
                    {
                        sourceXslDir = bundleResourcesPath;
                        _logger.Information("Found XSL files in app bundle: {Path}", bundleResourcesPath);
                    }
                }

                if (sourceXslDir != null)
                {
                    var xslFiles = Directory.GetFiles(sourceXslDir, "*.xsl");
                    int copiedCount = 0;
                    foreach (var xslFile in xslFiles)
                    {
                        var fileName = Path.GetFileName(xslFile);
                        var targetPath = Path.Combine(targetDir, fileName);
                        if (!File.Exists(targetPath))
                        {
                            File.Copy(xslFile, targetPath);
                            copiedCount++;
                            _logger.Debug("Copied XSL file to user directory: {FileName}", fileName);
                        }
                    }
                    if (copiedCount > 0)
                    {
                        _logger.Information("Copied {Count} XSL files to user directory", copiedCount);
                    }
                }
                else
                {
                    _logger.Warning("XSL directory not found in development or bundle locations");
                }
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Failed to copy XSL files");
            }
        }

        private void NavigateToFirstHit()
        {
            // Check if we have search highlights
            if (!HasSearchHighlights || TotalHits <= 0) return;
            
            Dispatcher.UIThread.Post(() =>
            {
                CurrentHitIndex = 1;
                UpdateHitStatusText();
                NavigateToHighlightRequested?.Invoke(CurrentHitIndex);
                PageStatusText = $"Navigated to first hit: hit_1";
            });
        }

        private void NavigateToPreviousHit()
        {
            // Check if we can navigate to previous hit
            if (CurrentHitIndex <= 1) return;
            
            Dispatcher.UIThread.Post(() =>
            {
                CurrentHitIndex--;
                UpdateHitStatusText();
                NavigateToHighlightRequested?.Invoke(CurrentHitIndex);
                PageStatusText = $"Navigated to hit: hit_{CurrentHitIndex}";
            });
        }

        private void NavigateToNextHit()
        {
            // Check if we can navigate to next hit
            if (CurrentHitIndex >= TotalHits) return;
            
            Dispatcher.UIThread.Post(() =>
            {
                CurrentHitIndex++;
                UpdateHitStatusText();
                _logger.Debug("NavigateToNextHit - index {Index}", CurrentHitIndex);
                NavigateToHighlightRequested?.Invoke(CurrentHitIndex);
                PageStatusText = $"Navigated to hit: hit_{CurrentHitIndex}";
            });
        }

        private void NavigateToLastHit()
        {
            // Check if we have search highlights
            if (!HasSearchHighlights || TotalHits <= 0) return;
            
            Dispatcher.UIThread.Post(() =>
            {
                CurrentHitIndex = TotalHits;
                UpdateHitStatusText();
                NavigateToHighlightRequested?.Invoke(CurrentHitIndex);
                PageStatusText = $"Navigated to last hit: hit_{TotalHits}";
            });
        }

        private void ExecuteCopy()
        {
            _logger.Debug("*** EXECUTE COPY COMMAND CALLED ***");
            // Signal the view to execute copy directly
            NavigateToHighlightRequested?.Invoke(-1); // Use -1 as a special signal for copy
        }

        private void ExecuteSelectAll()
        {
            _logger.Debug("*** EXECUTE SELECT ALL COMMAND CALLED ***");
            // Signal the view to execute select all directly
            NavigateToHighlightRequested?.Invoke(-2); // Use -2 as a special signal for select all
        }

        /// <summary>
        /// Float this book window - opens book in a separate floating window
        /// Creates a brand new ViewModel instance in the floating window
        /// Related: docs/research/BUTTON_BASED_FLOAT_APPROACH.md
        /// </summary>
        private void FloatWindow()
        {
            _logger.Information("FloatWindow command called for book: {BookFile}, Instance: {InstanceId}",
                Book.FileName, Id);

            // Factory creates brand new ViewModel with same book/state in floating window
            // This ViewModel instance will be disposed when removed from main dock
            if (_dockFactory != null)
            {
                _dockFactory.FloatDockableWithoutRecycling(this);
            }
            else
            {
                _logger.Error("Factory not available - cannot float window");
            }
        }

        /// <summary>
        /// Unfloat this book window - moves book back to main window
        /// Creates a brand new ViewModel instance in the main window
        /// Related: docs/research/BUTTON_BASED_FLOAT_APPROACH.md
        /// </summary>
        private void UnfloatWindow()
        {
            _logger.Information("UnfloatWindow command called for book: {BookFile}, Instance: {InstanceId}",
                Book.FileName, Id);

            // Factory creates brand new ViewModel with same book/state in main window
            // This ViewModel instance will be disposed when removed from floating dock
            if (_dockFactory != null)
            {
                _dockFactory.UnfloatDockableWithoutRecycling(this);
            }
            else
            {
                _logger.Error("Factory not available - cannot unfloat window");
            }
        }

        /// <summary>
        /// Restore WebView state after float/unfloat operation
        /// </summary>
        private void RestoreWebViewState()
        {
            if (_savedWebViewState == null)
            {
                _logger.Warning("No saved state to restore");
                return;
            }

            _logger.Information("Restoring WebView state: HtmlLength={HtmlLength}, Positions={PositionCount}, Terms={TermCount}",
                _savedWebViewState.HtmlContent?.Length ?? 0,
                _savedWebViewState.SearchPositions?.Count ?? 0,
                _savedWebViewState.SearchTerms?.Count ?? 0);

            // Restore state (Note: _searchTerms and _searchPositions are readonly,
            // so we can't reassign them - they stay as initialized)
            _htmlContent = _savedWebViewState.HtmlContent ?? "";
            CurrentHitIndex = _savedWebViewState.CurrentHitIndex;
            TotalHits = _savedWebViewState.TotalHits;

            // Update hit status text
            UpdateHitStatusText();

            // View will reload HTML and restore scroll position when it recreates WebView
        }

        private void UpdateHitStatusText()
        {
            if (TotalHits > 0)
            {
                HitStatusText = $"{CurrentHitIndex} of {TotalHits}";
            }
            else
            {
                HitStatusText = "";
            }
        }

        private void NavigateToChapter(DivTag chapter)
        {
            // Don't navigate if this change is coming from scroll position updates
            if (_updatingChapterFromScroll)
            {
                return;
            }
            
            _logger.Debug("Navigating to chapter: {ChapterId} - {ChapterHeading}", chapter.Id, chapter.Heading);
            
            // Navigate to the chapter anchor
            NavigateToChapterRequested?.Invoke(chapter.Id);
            
            PageStatusText = $"Chapter: {chapter.Heading.Trim()}";
        }

        private async Task OpenMulaBookAsync()
        {
            await OpenLinkedBookAsync(CommentaryLevel.Mula);
        }

        private async Task OpenAtthakathaBookAsync()
        {
            await OpenLinkedBookAsync(CommentaryLevel.Atthakatha);
        }

        private async Task OpenTikaBookAsync()
        {
            await OpenLinkedBookAsync(CommentaryLevel.Tika);
        }

        /// <summary>
        /// Port of CST4 FormBookDisplay.OpenLinkedBook method (L789-923)
        /// Opens a linked book (Mula/Atthakatha/Tika) and attempts to maintain reading position
        /// </summary>
        private async Task OpenLinkedBookAsync(CommentaryLevel linkedBookType)
        {
            try
            {
                _logger.Debug("Opening linked book: {BookType}", linkedBookType.ToString());
                
                // Step 1: Identify the Target Book (port of CST4 L790-800)
                Book? linkedBook = null;
                switch (linkedBookType)
                {
                    case CommentaryLevel.Mula:
                        if (_book.MulaIndex >= 0 && _book.MulaIndex < Books.Inst.Count)
                            linkedBook = Books.Inst[_book.MulaIndex];
                        break;
                    case CommentaryLevel.Atthakatha:
                        if (_book.AtthakathaIndex >= 0 && _book.AtthakathaIndex < Books.Inst.Count)
                            linkedBook = Books.Inst[_book.AtthakathaIndex];
                        break;
                    case CommentaryLevel.Tika:
                        if (_book.TikaIndex >= 0 && _book.TikaIndex < Books.Inst.Count)
                            linkedBook = Books.Inst[_book.TikaIndex];
                        break;
                }

                if (linkedBook == null)
                {
                    _logger.Warning("No linked book found for type: {BookType}", linkedBookType.ToString());
                    return;
                }

                _logger.Debug("Found linked book: {FileName}", linkedBook.FileName);

                // Step 2: Determine the Navigation Anchor (port of CST4 L801-850)
                string? anchor = await CalculateNavigationAnchorAsync(linkedBook);
                
                _logger.Debug("Calculated navigation anchor: {Anchor}", anchor ?? "null");

                // Step 3: Open the New Book (port of CST4 L920-923)
                // Trigger event for SimpleTabbedWindow to handle opening the new tab
                OpenBookRequested?.Invoke(linkedBook, anchor);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error opening linked book");
                            }
        }

        /// <summary>
        /// Calculate the appropriate navigation anchor based on current position and book types (async)
        /// Port of CST4 FormBookDisplay.OpenLinkedBook anchor calculation logic (L801-919)
        /// </summary>
        private async Task<string?> CalculateNavigationAnchorAsync(Book targetBook)
        {
            try
            {
                // Use the last known paragraph from status updates
                // This comes from the unified CST_STATUS_UPDATE messaging system
                string? currentAnchor = $"para{CurrentParagraph}";
                
                // If we don't have a current paragraph from status updates, try direct detection
                if (string.IsNullOrEmpty(CurrentParagraph) || CurrentParagraph == "*")
                {
                    _logger.Debug("No current paragraph from status updates");
                    currentAnchor = null;
                }
                
                if (!string.IsNullOrEmpty(currentAnchor))
                {
                    _logger.Debug("Using continuously tracked paragraph anchor: {Anchor}", currentAnchor);
                    _logger.Debug("Status bar should now show - Para: {Para}", ParseParagraph(currentAnchor));
                }
                else
                {
                    _logger.Debug("Still no continuously tracked paragraph available");
                    // No fallback available since GetCurrentParagraphAnchorAsync was removed
                    currentAnchor = null;
                    
                    if (!string.IsNullOrEmpty(currentAnchor))
                    {
                        _logger.Information("Fallback detection got paragraph anchor: {Anchor}", currentAnchor);
                    }
                    else
                    {
                        _logger.Warning("No paragraph anchor available - unable to calculate navigation anchor");
                        return null;
                    }
                }

                _logger.Debug("Current position anchor: {Anchor}", currentAnchor);
                _logger.Debug("Book types - Source: {SourceType}, Target: {TargetType}", _book.BookType, targetBook.BookType);

                // Handle complex book type mappings like CST4 does
                // For most cases (Whole to Whole), we can use the paragraph anchor directly
                // For complex cases (Multi, Split), we may need additional logic
                
                if (_book.BookType == BookType.Multi && targetBook.BookType == BookType.Whole)
                {
                    // Similar to CST4 L851-870: Multi book to Whole book navigation
                    // May need to extract book code from anchor if it has format like "para123_an4"
                    if (currentAnchor.Contains("_"))
                    {
                        // Extract base paragraph number without book code
                        var parts = currentAnchor.Split('_');
                        currentAnchor = parts[0]; // e.g., "para123_an4" → "para123"
                        _logger.Debug("Extracted base paragraph anchor: {Anchor}", currentAnchor);
                    }
                }
                else if (_book.BookType == BookType.Whole && targetBook.BookType == BookType.Multi)
                {
                    // Whole book to Multi book navigation
                    // May need to add book code to paragraph anchor based on target book
                    // This is more complex and may require additional logic
                    _logger.Debug("Navigation from Whole to Multi book - using direct anchor");
                }
                
                return currentAnchor;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error calculating navigation anchor");
                return null;
            }
        }

        /// <summary>
        /// Get current paragraph anchor asynchronously - implementation via JavaScript bridge
        /// Port of CST4's GetPara() method with async/await pattern - ONLY for book linking, not position restoration
        /// </summary>
        private async Task<string?> GetCurrentParagraphAnchorAsync()
        {
            if (BookDisplayControl == null)
            {
                _logger.Warning("BookDisplayControl is null in GetCurrentParagraphAnchorAsync");
                return null;
            }
                
            _logger.Debug("Calling BookDisplayControl.GetCurrentParagraphAnchorAsync()");
            // This is ONLY used for book linking navigation, not for script position restoration
            
            try
            {
                var result = await BookDisplayControl.GetCurrentParagraphAnchorAsync();
                _logger.Debug("GetCurrentParagraphAnchorAsync returned: {Result}", result ?? "null");
                return result;
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "GetCurrentParagraphAnchorAsync failed");
                return null;
            }
        }

        // REMOVED: GetParaWithBookCode() - dead code, never called
        // This method wrapped GetCurrentParagraphAnchorWithBookCode() but was not used

        /// <summary>
        /// Handle navigation from Whole books to Split books
        /// Port of CST4 FormBookDisplay.OpenLinkedBook L820-830
        /// </summary>
        private string? HandleWholesToSplitNavigation(string? currentPara, Book targetBook)
        {
            // TODO: Implement special handling for split books (Theragatha, etc.)
            // For now, return simple anchor
            return currentPara;
        }

        /// <summary>
        /// Handle navigation from Multi books to Whole books  
        /// Port of CST4 FormBookDisplay.OpenLinkedBook L851-870
        /// </summary>
        private string? HandleMultiToWholeNavigation(string? currentPara, Book targetBook)
        {
            try
            {
                if (string.IsNullOrEmpty(currentPara))
                    return null;

                // Extract book code from paragraph anchor (e.g., "para123_an4" -> "an4")
                string? bookCode = GetBookCode(currentPara);
                if (string.IsNullOrEmpty(bookCode))
                {
                    _logger.Debug("No book code found in paragraph anchor");
                    return currentPara;
                }

                _logger.Debug("Extracted book code: {BookCode}", bookCode);

                // TODO: Implement book code to target book mapping
                // This requires understanding the relationship between book codes and target books
                // For now, return the original anchor without book code
                return ParseParaAnchor(currentPara);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error handling Multi to Whole navigation");
                return currentPara;
            }
        }

        /// <summary>
        /// Extract book code from paragraph anchor with book code
        /// Port of CST4 FormBookDisplay.GetBookCode method
        /// </summary>
        private string? GetBookCode(string? paraWithBook)
        {
            if (string.IsNullOrEmpty(paraWithBook))
                return null;

            // Extract book code after underscore (e.g., "para123_an4" -> "an4")
            int underscoreIndex = paraWithBook.LastIndexOf('_');
            if (underscoreIndex >= 0 && underscoreIndex < paraWithBook.Length - 1)
            {
                return paraWithBook.Substring(underscoreIndex + 1);
            }

            return null;
        }

        /// <summary>
        /// Parse paragraph number from anchor string
        /// Port of CST4 FormBookDisplay.ParseParaAnchor method
        /// </summary>
        private string? ParseParaAnchor(string? paraWithBook)
        {
            if (string.IsNullOrEmpty(paraWithBook))
                return null;

            // Remove book code suffix (e.g., "para123_an4" -> "para123")
            int underscoreIndex = paraWithBook.LastIndexOf('_');
            if (underscoreIndex >= 0)
            {
                return paraWithBook.Substring(0, underscoreIndex);
            }

            return paraWithBook;
        }
        
        // Page references functionality (ported from CST4 FormBookDisplay.cs line 387)
        public void CalculatePageStatus(int scrollTop)
        {
            // Port of CST4's CalculatePageStatus method
            // This will be called from JavaScript when scroll position changes
            Dispatcher.UIThread.Post(() =>
            {
                // For now, set placeholder values - will be updated when JavaScript bridge provides page anchors
                VriPage = "*";
                MyanmarPage = "*";
                PtsPage = "*";
                ThaiPage = "*";
                OtherPage = "*";
                
                UpdatePageReferencesText();
            });
        }
        
        private void UpdatePageReferencesText()
        {
            _logger.Debug("UpdatePageReferencesText called: {DisplayTitle}", DisplayTitle);
            
            // Port of CST4's SetPageStatusText method
            // TODO: Use LocalizationService once it's fully implemented
            // For now, use the same format as CST4's PageNumbersStatusFormat from Resources.resx
            // string format = _localizationService.GetString("PageNumbersStatusFormat");
            // PageReferencesText = string.Format(format, VriPage, MyanmarPage, PtsPage, ThaiPage, OtherPage);
            
            // Temporary hardcoded format until localization is implemented - now includes paragraph number for debugging
            var newText = $"VRI: {VriPage}   Myanmar: {MyanmarPage}   PTS: {PtsPage}   Thai: {ThaiPage}   Other: {OtherPage}   Para: {CurrentParagraph}";
            var oldText = PageReferencesText;
            PageReferencesText = newText;
            
            _logger.Debug("PageReferencesText updated - {DisplayTitle}: '{OldText}' -> '{NewText}'", DisplayTitle, oldText, newText);
        }
        
        // Method to update page references from JavaScript bridge
        public void UpdatePageReferences(string vriPage, string myanmarPage, string ptsPage, string thaiPage, string otherPage)
        {
            _logger.Debug("UpdatePageReferences called - {DisplayTitle} - VRI: {VriPage}, Myanmar: {MyanmarPage}, PTS: {PtsPage}, Thai: {ThaiPage}, Other: {OtherPage}", DisplayTitle, vriPage, myanmarPage, ptsPage, thaiPage, otherPage);
            
            Dispatcher.UIThread.Post(() =>
            {
                _logger.Debug("UpdatePageReferences executing on UI thread: {DisplayTitle}", DisplayTitle);
                
                // Store the raw anchor name for position preservation
                CurrentVriAnchor = vriPage;
                
                var oldVri = VriPage;
                var oldMyanmar = MyanmarPage;
                var oldPts = PtsPage;
                var oldThai = ThaiPage;
                var oldOther = OtherPage;
                
                VriPage = ParsePage(vriPage);
                MyanmarPage = ParsePage(myanmarPage);
                PtsPage = ParsePage(ptsPage);
                ThaiPage = ParsePage(thaiPage);
                OtherPage = ParsePage(otherPage);
                
                _logger.Debug("Page values updated - {DisplayTitle} - VRI: {OldVri}->{VriPage}, Myanmar: {OldMyanmar}->{MyanmarPage}, PTS: {OldPts}->{PtsPage}, Thai: {OldThai}->{ThaiPage}, Other: {OldOther}->{OtherPage}", DisplayTitle, oldVri, VriPage, oldMyanmar, MyanmarPage, oldPts, PtsPage, oldThai, ThaiPage, oldOther, OtherPage);
                
                UpdatePageReferencesText();
                
                _logger.Debug("UpdatePageReferences completed: {DisplayTitle}", DisplayTitle);
            });
        }
        
        // Method to update current paragraph from JavaScript bridge
        public void UpdateCurrentParagraph(string paragraphAnchor)
        {
            _logger.Debug("UpdateCurrentParagraph called - {DisplayTitle} with: {ParagraphAnchor}", DisplayTitle, paragraphAnchor);
            
            Dispatcher.UIThread.Post(() =>
            {
                _logger.Debug("UpdateCurrentParagraph executing on UI thread: {DisplayTitle}", DisplayTitle);
                
                var oldParagraph = CurrentParagraph;
                
                // Store the raw paragraph anchor and parse it for display
                CurrentParagraph = ParseParagraph(paragraphAnchor);
                
                _logger.Debug("Paragraph updated - {DisplayTitle}: '{OldParagraph}' -> '{CurrentParagraph}'", DisplayTitle, oldParagraph, CurrentParagraph);
                
                UpdatePageReferencesText(); // Refresh the status bar
                
                _logger.Debug("UpdateCurrentParagraph completed: {DisplayTitle}", DisplayTitle);
            });
        }
        
        // Method to update current chapter from JavaScript bridge
        public void UpdateCurrentChapter(string chapterId)
        {
            Dispatcher.UIThread.Post(() =>
            {
                try
                {
                    // Use case-insensitive comparison for robustness against data source inconsistencies.
                    var chapter = Chapters.FirstOrDefault(c => c.Id.Equals(chapterId, StringComparison.OrdinalIgnoreCase));

                    if (chapter != null && chapter != SelectedChapter)
                    {
                        _updatingChapterFromScroll = true;
                        SelectedChapter = chapter;
                        _logger.Debug("Updated selected chapter: {ChapterId} - {ChapterHeading}", chapter.Id, chapter.Heading);

                        // Post a subsequent, lower-priority action to reset the flag.
                        Dispatcher.UIThread.Post(() =>
                        {
                            _updatingChapterFromScroll = false;
                        }, DispatcherPriority.Background);
                    }
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error updating current chapter");
                }
            });
        }
        
        /// <summary>
        /// Parse anchor name to display format (port of CST4's ParsePage method)
        /// Examples: 'V1.0023' -> '1.23', 'V0.0001' -> '1', 'V01234' -> '1.234'
        /// </summary>
        private string ParsePage(string anchorName)
        {
            if (string.IsNullOrEmpty(anchorName) || anchorName == "*")
                return "*";
                
            try
            {
                // Remove prefix (V, M, P, T, O)
                if (anchorName.Length > 1)
                {
                    string pageNumber = anchorName.Substring(1);
                    
                    // Handle format like "1.0023" -> "1.23" or "0.0001" -> "1"
                    if (pageNumber.Contains('.'))
                    {
                        string[] parts = pageNumber.Split('.');
                        string wholePart = parts[0];
                        string decimalPart = parts[1];
                        
                        // If leading digit is zero, strip "0." and any leading zeroes from decimal part
                        if (wholePart == "0")
                        {
                            return int.Parse(decimalPart).ToString();
                        }
                        else
                        {
                            // Strip leading zeroes from decimal part
                            int trimmedDecimal = int.Parse(decimalPart);
                            return $"{wholePart}.{trimmedDecimal}";
                        }
                    }
                    
                    return pageNumber;
                }
                
                return anchorName;
            }
            catch
            {
                return "*";
            }
        }
        
        /// <summary>
        /// Parse paragraph anchor to display format
        /// Examples: 'para123' -> '123', 'para548-9' -> '548'
        /// Note: For ranges, extracts the start number for CST4 compatibility
        /// </summary>
        private string ParseParagraph(string paragraphAnchor)
        {
            if (string.IsNullOrEmpty(paragraphAnchor) || paragraphAnchor == "*")
                return "*";
                
            try
            {
                // Remove 'para' prefix
                if (paragraphAnchor.StartsWith("para"))
                {
                    string para = paragraphAnchor.Substring(4); // Remove "para"
                    
                    // For ranges like "548-9", extract just the start number
                    // This matches CST4's ParseParaAnchor behavior
                    if (para.Contains("-"))
                    {
                        para = para.Substring(0, para.IndexOf('-'));
                    }
                    
                    return para; // Return start number: "548-9" → "548"
                }
                
                return paragraphAnchor;
            }
            catch
            {
                return "*";
            }
        }
    }

    /// <summary>
    /// Lifecycle operations for WebView management during float/unfloat
    /// Related: docs/research/BUTTON_BASED_FLOAT_APPROACH.md Phase 4
    /// </summary>
    public enum WebViewLifecycleOperation
    {
        None,
        PrepareForFloat,      // Signal View to dispose WebView before floating
        RestoreAfterFloat,    // Signal View to recreate WebView after floating
        PrepareForUnfloat,    // Signal View to dispose WebView before unfloating
        RestoreAfterUnfloat   // Signal View to recreate WebView after unfloating
    }

    /// <summary>
    /// Saved state for WebView recreation after float/unfloat operations
    /// Related: docs/research/BUTTON_BASED_FLOAT_APPROACH.md Phase 4
    /// </summary>
    public class WebViewState
    {
        public string? HtmlContent { get; set; }
        public int ScrollPosition { get; set; }
        public List<TermPosition>? SearchPositions { get; set; }
        public List<string>? SearchTerms { get; set; }
        public Script BookScript { get; set; }
        public string? CurrentAnchor { get; set; }
        public int CurrentHitIndex { get; set; }
        public int TotalHits { get; set; }
    }
}