using System;
using System.IO;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.Common.Events;
using CST.Avalonia.ViewModels;
using CST.Avalonia.Services;

namespace CST.Avalonia.Views;

public partial class BookDisplayView : UserControl
{
    private BookDisplayViewModel? _viewModel;
    private AvaloniaCefBrowser? _cefBrowser;
    private ScrollViewer? _fallbackBrowser;
    private Decorator? _browserWrapper;
    private int _lastScrollPosition = 0;
    private bool _isBrowserInitialized = false;

    public BookDisplayView()
    {
        InitializeComponent();
        
        _browserWrapper = this.FindControl<Decorator>("browserWrapper");
        _fallbackBrowser = this.FindControl<ScrollViewer>("fallbackBrowser");
        
        // Try to create CefGlue browser
        TryCreateCefBrowser();
    }

    private void TryCreateCefBrowser()
    {
        try
        {
            if (_browserWrapper != null)
            {
                _cefBrowser = new AvaloniaCefBrowser();
                
                // Set up event handlers for debugging
                _cefBrowser.BrowserInitialized += () => OnBrowserInitialized(null, EventArgs.Empty);
                _cefBrowser.LoadEnd += OnLoadEnd;
                _cefBrowser.LoadError += OnLoadError;
                _cefBrowser.TitleChanged += OnTitleChanged;
                
                _browserWrapper.Child = _cefBrowser;
                
                Console.WriteLine("CefGlue browser created successfully");
            }
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Failed to create CefGlue browser: {ex.Message}");
            _cefBrowser = null;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        
        _viewModel = DataContext as BookDisplayViewModel;
        if (_viewModel != null)
        {
            // Set the reference to this control in the ViewModel for scroll position management
            _viewModel.BookDisplayControl = this;
            
            // Subscribe to HTML content changes
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            
            // Set up communication between ViewModel and View for navigation
            _viewModel.NavigateToHighlightRequested += NavigateToHighlight;
            _viewModel.NavigateToChapterRequested += NavigateToAnchor;
            
            // Subscribe to page reference updates from JavaScript
            CstSchemeHandlerFactory.PageReferencesUpdated += OnPageReferencesUpdated;
        }
    }


    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BookDisplayViewModel.HtmlContent))
        {
            Dispatcher.UIThread.Post(() => LoadHtmlContent());
        }
    }

    private void OnPageReferencesUpdated(PageReferences pageReferences)
    {
        // Ensure we're on the UI thread
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => OnPageReferencesUpdated(pageReferences));
            return;
        }

        // Update the ViewModel with the page references from JavaScript
        if (_viewModel != null)
        {
            Console.WriteLine($"Updating page references: VRI={pageReferences.Vri}, Myanmar={pageReferences.Myanmar}, PTS={pageReferences.Pts}, Thai={pageReferences.Thai}, Other={pageReferences.Other}");
            _viewModel.UpdatePageReferences(
                pageReferences.Vri, 
                pageReferences.Myanmar, 
                pageReferences.Pts, 
                pageReferences.Thai, 
                pageReferences.Other
            );
        }
    }

    private void LoadHtmlContent()
    {
        Console.WriteLine($"LoadHtmlContent called - ViewModel: {_viewModel != null}, HtmlContent empty: {string.IsNullOrEmpty(_viewModel?.HtmlContent)}");
        
        if (_viewModel == null || string.IsNullOrEmpty(_viewModel.HtmlContent)) 
        {
            Console.WriteLine("Exiting LoadHtmlContent - no viewmodel or content");
            return;
        }

        // Ensure we're on the UI thread for CefGlue operations
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Console.WriteLine("LoadHtmlContent - dispatching to UI thread");
            Dispatcher.UIThread.Post(LoadHtmlContent);
            return;
        }

        try
        {
            Console.WriteLine($"LoadHtmlContent - CefGlue available: {_viewModel.IsCefGlueAvailable}, Browser: {_cefBrowser != null}");
            
            if (_viewModel.IsCefGlueAvailable && _cefBrowser != null)
            {
                try
                {
                    // Check content size and use appropriate loading method
                    Console.WriteLine($"Loading HTML content (content length: {_viewModel.HtmlContent.Length})");
                    Console.WriteLine($"HTML content preview: {_viewModel.HtmlContent.Substring(0, Math.Min(200, _viewModel.HtmlContent.Length))}...");
                    
                    // Write HTML content to temporary file and load it
                    // This completely bypasses data URI size limitations
                    var tempFileName = $"cst_book_{_viewModel.Book.FileName.Replace('.', '_')}.html";
                    var tempFilePath = Path.Combine(Path.GetTempPath(), tempFileName);
                    
                    Console.WriteLine($"Writing HTML content to temp file: {tempFilePath}");
                    File.WriteAllText(tempFilePath, _viewModel.HtmlContent, System.Text.Encoding.UTF8);
                    
                    var fileUrl = $"file://{tempFilePath}";
                    Console.WriteLine($"Loading content from file URL: {fileUrl}");
                    
                    _cefBrowser.Address = fileUrl;
                    _viewModel.PageStatusText = "Loading content from file...";
                    Console.WriteLine("HTML content loaded from temporary file");
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Failed to load HTML content: {ex.Message}");
                    _viewModel.SetCefGlueAvailability(false, "Failed to load content - using fallback");
                }
            }
            else if (_cefBrowser == null)
            {
                // Browser creation failed, disable CefGlue
                Console.WriteLine("Browser is null - setting CefGlue unavailable");
                _viewModel.SetCefGlueAvailability(false, "CefGlue browser unavailable - using fallback text display");
            }
            else
            {
                Console.WriteLine("CefGlue not available - using fallback");
            }
            // Fallback is already handled by data binding in XAML
        }
        catch (Exception ex)
        {
            // If CefGlue fails, mark it as unavailable and fall back to text display
            Console.WriteLine($"Exception in LoadHtmlContent: {ex.Message}");
            _viewModel?.SetCefGlueAvailability(false, $"CefGlue error, using fallback: {ex.Message}");
        }
    }

    private void OnBrowserInitialized(object? sender, EventArgs e)
    {
        _isBrowserInitialized = true;
        
        if (_viewModel != null)
        {
            Dispatcher.UIThread.Post(() => 
            {
                // Browser is now ready - no need to change availability since we started optimistically
                Console.WriteLine("Browser initialized successfully");
                _viewModel.PageStatusText = "Browser ready";
                
                // Load content if it's ready
                if (!string.IsNullOrEmpty(_viewModel.HtmlContent))
                {
                    Console.WriteLine($"Loading content immediately - HTML length: {_viewModel.HtmlContent.Length}");
                    LoadHtmlContent();
                }
                else
                {
                    Console.WriteLine("No HTML content ready yet - will load when content is generated");
                }
            });
        }
    }

    private void OnLoadEnd(object? sender, LoadEndEventArgs e)
    {
        if (_viewModel != null && e.Frame.IsMain)
        {
            Dispatcher.UIThread.Post(() => 
            {
                Console.WriteLine($"OnLoadEnd called - Main frame loaded successfully. URL: {e.Frame.Url}");
                _viewModel.PageStatusText = "Document loaded successfully";
                
                // Set up JavaScript bridge after content loads
                SetupJavaScriptBridge();
                
                // Navigate to current highlight if we have search results
                if (_viewModel.HasSearchHighlights && _viewModel.CurrentHitIndex > 0)
                {
                    NavigateToHighlight(_viewModel.CurrentHitIndex);
                }
            });
        }
    }

    private void OnLoadError(object? sender, LoadErrorEventArgs e)
    {
        if (_viewModel != null)
        {
            Dispatcher.UIThread.Post(() => 
            {
                Console.WriteLine($"OnLoadError called - Error: {e.ErrorText}, Code: {e.ErrorCode}, URL: {e.FailedUrl}");
                _viewModel.PageStatusText = $"Load error: {e.ErrorText}";
            });
        }
    }

    private void OnTitleChanged(object? sender, string title)
    {
        Console.WriteLine($"Page title changed: {title}");
        
        // Check for scroll position data in title
        if (title != null && title.StartsWith("CST_SCROLL_POS:"))
        {
            var scrollPosStr = title.Substring("CST_SCROLL_POS:".Length);
            if (int.TryParse(scrollPosStr, out var scrollPos))
            {
                _lastScrollPosition = scrollPos;
                Console.WriteLine($"Updated scroll position: {scrollPos}");
            }
        }
        // Check for page reference data in title
        else if (title != null && title.StartsWith("CST_PAGE_REFS:"))
        {
            Console.WriteLine("Detected page reference data in title!");
            
            try
            {
                // Parse: CST_PAGE_REFS:VRI=1|MYANMAR=1|PTS=1|THAI=1|OTHER=*
                var data = title.Substring("CST_PAGE_REFS:".Length);
                var parts = data.Split('|');
                
                string vriPage = "*", myanmarPage = "*", ptsPage = "*", thaiPage = "*", otherPage = "*";
                
                foreach (var part in parts)
                {
                    if (part.StartsWith("VRI=")) vriPage = part.Substring(4);
                    else if (part.StartsWith("MYANMAR=")) myanmarPage = part.Substring(8);
                    else if (part.StartsWith("PTS=")) ptsPage = part.Substring(4);
                    else if (part.StartsWith("THAI=")) thaiPage = part.Substring(5);
                    else if (part.StartsWith("OTHER=")) otherPage = part.Substring(6);
                }
                
                Console.WriteLine($"Parsed page references: VRI={vriPage}, Myanmar={myanmarPage}, PTS={ptsPage}, Thai={thaiPage}, Other={otherPage}");
                
                // Update the ViewModel with the page references
                if (_viewModel != null)
                {
                    // Ensure we're on the UI thread
                    if (!Dispatcher.UIThread.CheckAccess())
                    {
                        Dispatcher.UIThread.Post(() => _viewModel.UpdatePageReferences(vriPage, myanmarPage, ptsPage, thaiPage, otherPage));
                    }
                    else
                    {
                        _viewModel.UpdatePageReferences(vriPage, myanmarPage, ptsPage, thaiPage, otherPage);
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing page references from title: {ex.Message}");
            }
        }
        // Check for current chapter data in title
        else if (title != null && title.StartsWith("CST_CURRENT_CHAPTER:"))
        {
            try
            {
                var chapterId = title.Substring("CST_CURRENT_CHAPTER:".Length);
                Console.WriteLine($"Detected current chapter: {chapterId}");
                
                if (_viewModel != null)
                {
                    _viewModel.UpdateCurrentChapter(chapterId);
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error parsing current chapter: {ex.Message}");
            }
        }
        // Check for debug messages
        else if (title != null && title.StartsWith("CST_DEBUG:"))
        {
            var debugMessage = title.Substring("CST_DEBUG:".Length);
            Console.WriteLine($"JavaScript Debug: {debugMessage}");
        }
    }


    private void SetupJavaScriptBridge()
    {
        if (_cefBrowser != null)
        {
            try
            {
                // Add JavaScript functions for search navigation
                var script = @"
                    console.log('=== CST JavaScript Bridge Starting ===');
                    window.cstSearchHighlights = {
                        hits: [],
                        currentIndex: 0,
                        
                        init: function() {
                            console.log('cstSearchHighlights.init() called');
                            console.log('Document readyState:', document.readyState);
                            
                            // Look for <span class='hit'> elements generated by XSLT transformation
                            console.log('Searching for highlight elements...');
                            this.hits = Array.from(document.querySelectorAll('span.hit'));
                            
                            // Try alternative selectors if the first one doesn't work
                            if (this.hits.length === 0) {
                                console.log('Trying alternative selector: span[class=hit]');
                                this.hits = Array.from(document.querySelectorAll('span[class=""hit""]'));
                            }
                            
                            if (this.hits.length === 0) {
                                console.log('Trying to find any elements with hit class');
                                this.hits = Array.from(document.querySelectorAll('.hit'));
                            }
                            
                            if (this.hits.length === 0) {
                                console.log('Trying to find all span elements to debug');
                                var allSpans = Array.from(document.querySelectorAll('span'));
                                console.log('Found ' + allSpans.length + ' span elements total');
                                var hitSpans = allSpans.filter(function(el) {
                                    return el.className && el.className.includes('hit');
                                });
                                console.log('Found ' + hitSpans.length + ' spans with hit class');
                                if (hitSpans.length > 0) {
                                    console.log('First hit span:', hitSpans[0].outerHTML);
                                }
                                this.hits = hitSpans;
                            }
                            
                            console.log('Found ' + this.hits.length + ' search highlights');
                            if (this.hits.length > 0) {
                                console.log('First hit element:', this.hits[0]);
                                console.log('First hit HTML:', this.hits[0].outerHTML);
                            }
                            
                            this.updateHighlightStyles();
                        },
                        
                        navigateToHit: function(index) {
                            console.log('navigateToHit called with index:', index);
                            console.log('Total hits available:', this.hits.length);
                            
                            if (index < 1 || index > this.hits.length) {
                                console.log('Index out of range, returning');
                                return;
                            }
                            
                            this.currentIndex = index - 1;
                            var hit = this.hits[this.currentIndex];
                            console.log('Selected hit element:', hit);
                            
                            if (hit) {
                                console.log('Scrolling to hit element');
                                hit.scrollIntoView({ behavior: 'smooth', block: 'center' });
                                this.updateHighlightStyles();
                            } else {
                                console.log('Hit element is null or undefined');
                            }
                        },
                        
                        updateHighlightStyles: function() {
                            this.hits.forEach((hit, i) => {
                                if (i === this.currentIndex) {
                                    hit.style.backgroundColor = 'red';
                                    hit.style.color = 'white';
                                } else {
                                    hit.style.backgroundColor = 'blue';  // Use original CST4 blue color
                                    hit.style.color = 'white';
                                }
                            });
                        },
                        
                        showHits: function(visible) {
                            this.hits.forEach(hit => {
                                hit.style.display = visible ? 'inline' : 'none';
                            });
                        },
                        
                        showFootnotes: function(visible) {
                            var footnotes = document.querySelectorAll('.footnote');
                            footnotes.forEach(fn => {
                                fn.style.display = visible ? 'block' : 'none';
                            });
                        }
                    };
                    
                    // Page references functionality (ported from CST4 FormBookDisplay.cs)
                    window.cstPageReferences = {
                        vPages: [],  // VRI pages
                        mPages: [],  // Myanmar pages
                        pPages: [],  // PTS pages
                        tPages: [],  // Thai pages
                        oPages: [],  // Other pages
                        lastScrollTop: -1,
                        scrollTimer: null,
                        
                        init: function() {
                            console.log('cstPageReferences.init() called');
                            this.collectPageAnchors();
                            this.setupScrollListener();
                            
                            // Calculate initial page status (like CST4's GetInitialPageStatus)
                            setTimeout(() => {
                                console.log('Calculating initial page status...');
                                this.calculatePageStatus();
                            }, 200);
                        },
                        
                        collectPageAnchors: function() {
                            // Collect all anchor elements and categorize by prefix (port of CST4 line 1048-1094)
                            var anchors = Array.from(document.querySelectorAll('a[name], a[id]'));
                            console.log('Found ' + anchors.length + ' total anchors');
                            
                            this.vPages = [];
                            this.mPages = [];
                            this.pPages = [];
                            this.tPages = [];
                            this.oPages = [];
                            
                            anchors.forEach(anchor => {
                                var anchorName = anchor.name || anchor.id;
                                if (anchorName) {
                                    if (anchorName.startsWith('V')) {
                                        this.vPages.push(anchor);
                                    } else if (anchorName.startsWith('M')) {
                                        this.mPages.push(anchor);
                                    } else if (anchorName.startsWith('P')) {
                                        this.pPages.push(anchor);
                                    } else if (anchorName.startsWith('T')) {
                                        this.tPages.push(anchor);
                                    } else if (anchorName.startsWith('O')) {
                                        this.oPages.push(anchor);
                                    }
                                }
                            });
                            
                            console.log('Page anchors found:', {
                                VRI: this.vPages.length,
                                Myanmar: this.mPages.length, 
                                PTS: this.pPages.length,
                                Thai: this.tPages.length,
                                Other: this.oPages.length
                            });
                        },
                        
                        setupScrollListener: function() {
                            // Set up scroll event listener with proper throttling for responsive updates
                            var self = this;
                            var isThrottled = false;
                            
                            function handleScroll() {
                                if (!isThrottled) {
                                    // Update immediately for responsive feel
                                    self.calculatePageStatus();
                                    isThrottled = true;
                                    
                                    // Allow next update after 50ms (instead of waiting for scroll to stop)
                                    setTimeout(function() {
                                        isThrottled = false;
                                    }, 50);
                                }
                            }
                            
                            window.addEventListener('scroll', handleScroll);
                            document.addEventListener('scroll', handleScroll);
                        },
                        
                        calculatePageStatus: function() {
                            console.log('calculatePageStatus called');
                            console.log('Page arrays:', {
                                vPages: this.vPages.length,
                                mPages: this.mPages.length,
                                pPages: this.pPages.length,
                                tPages: this.tPages.length,
                                oPages: this.oPages.length
                            });
                            
                            // Don't recalculate if no pages found (prevents overwriting good initial values)
                            if (this.vPages.length === 0 && this.mPages.length === 0 && this.pPages.length === 0 && this.tPages.length === 0 && this.oPages.length === 0) {
                                console.log('No page arrays found, returning');
                                return;
                            }
                            
                            var scrollTop = window.pageYOffset || document.documentElement.scrollTop;
                            console.log('Current scrollTop:', scrollTop, 'Last scrollTop:', this.lastScrollTop);
                            
                            if (scrollTop === this.lastScrollTop) {
                                return; // No change in scroll position
                            }
                            this.lastScrollTop = scrollTop;
                            
                            // Send scroll position update to C#
                            document.title = 'CST_SCROLL_POS:' + scrollTop;
                            
                            // Find the appropriate page for each type based on scroll position
                            var vriPage = this.vPages.length > 0 ? this.findScrollTopPageName(this.vPages, scrollTop) : '*';
                            var myanmarPage = this.mPages.length > 0 ? this.findScrollTopPageName(this.mPages, scrollTop) : '*';
                            var ptsPage = this.pPages.length > 0 ? this.findScrollTopPageName(this.pPages, scrollTop) : '*';
                            var thaiPage = this.tPages.length > 0 ? this.findScrollTopPageName(this.tPages, scrollTop) : '*';
                            var otherPage = this.oPages.length > 0 ? this.findScrollTopPageName(this.oPages, scrollTop) : '*';
                            
                            console.log('Calculated page references:', {
                                vri: vriPage,
                                myanmar: myanmarPage,
                                pts: ptsPage,
                                thai: thaiPage,
                                other: otherPage
                            });
                            
                            // Send update to C# code via document title
                            this.updatePageReferences(vriPage, myanmarPage, ptsPage, thaiPage, otherPage);
                        },
                        
                        findScrollTopPageName: function(pageArray, scrollTop) {
                            // Port of CST4's FindScrollTopElementName method (line 449-512)
                            console.log('findScrollTopPageName called with:', pageArray.length, 'pages, scrollTop:', scrollTop);
                            
                            if (pageArray.length === 0) {
                                console.log('No pages in array, returning empty string');
                                return '';
                            }
                            
                            var closestPage = '';
                            var closestDistance = Infinity;
                            
                            for (var i = 0; i < pageArray.length; i++) {
                                var anchor = pageArray[i];
                                var anchorTop = anchor.offsetTop;
                                var anchorName = anchor.name || anchor.id;
                                
                                console.log('  Checking anchor', i + ':', anchorName, 'at offsetTop:', anchorTop);
                                
                                if (anchorTop <= scrollTop) {
                                    var distance = scrollTop - anchorTop;
                                    console.log('    Above scroll position, distance:', distance);
                                    if (distance < closestDistance) {
                                        closestDistance = distance;
                                        closestPage = anchorName;
                                        console.log('    New closest page:', closestPage, 'distance:', distance);
                                    }
                                } else {
                                    console.log('    Below scroll position, skipping');
                                }
                            }
                            
                            console.log('findScrollTopPageName returning:', closestPage);
                            return closestPage;
                        },
                        
                        parsePage: function(anchorName) {
                            // Handle actual format: 'V1.0023' -> '1.23' or 'V0.0001' -> '1'
                            if (!anchorName || anchorName.length === 0) {
                                return '*';
                            }
                            
                            try {
                                if (anchorName.length > 1) {
                                    var pageNumber = anchorName.substring(1); // Remove prefix (V, M, P, T, O)
                                    
                                    // Handle format like ""1.0023"" -> ""1.23"" or ""0.0001"" -> ""1""
                                    if (pageNumber.includes('.')) {
                                        var parts = pageNumber.split('.');
                                        var wholePart = parts[0];
                                        var decimalPart = parts[1];
                                        
                                        // If leading digit is zero, strip ""0."" and any leading zeroes from decimal part
                                        if (wholePart === '0') {
                                            return parseInt(decimalPart, 10).toString();
                                        } else {
                                            // Strip leading zeroes from decimal part
                                            var trimmedDecimal = parseInt(decimalPart, 10).toString();
                                            return wholePart + '.' + trimmedDecimal;
                                        }
                                    }
                                    
                                    return pageNumber;
                                }
                                return anchorName;
                            } catch (e) {
                                return '*';
                            }
                        },
                        
                        updatePageReferences: function(vriPage, myanmarPage, ptsPage, thaiPage, otherPage) {
                            // Store page references in window object for debugging
                            window.currentPageReferences = {
                                vri: vriPage,
                                myanmar: myanmarPage,
                                pts: ptsPage,
                                thai: thaiPage,
                                other: otherPage,
                                lastUpdated: Date.now()
                            };
                            
                            // Send page references to C# via document title
                            try {
                                document.title = 'CST_PAGE_REFS:VRI=' + vriPage + '|MYANMAR=' + myanmarPage + '|PTS=' + ptsPage + '|THAI=' + thaiPage + '|OTHER=' + otherPage;
                            } catch (error) {
                                document.title = 'CST_PAGE_REFS:ERROR:' + error.message;
                            }
                        }
                    };
                    
                    // Initialize when DOM is ready - with a small delay to ensure content is fully processed
                    function initializeHighlights() {
                        console.log('Attempting to initialize highlights...');
                        window.cstSearchHighlights.init();
                        
                        // If no hits found, try again after a short delay (in case content is still loading)
                        if (window.cstSearchHighlights.hits.length === 0) {
                            console.log('No hits found, retrying in 500ms...');
                            setTimeout(function() {
                                console.log('Retry attempt...');
                                window.cstSearchHighlights.init();
                            }, 500);
                        }
                    }
                    
                    function initializePageReferences() {
                        try {
                            if (window.cstPageReferences) {
                                // Set up scroll monitoring for dynamic updates (but don't force calculation yet)
                                window.cstPageReferences.collectPageAnchors();
                                window.cstPageReferences.setupScrollListener();
                                
                                // Don't force calculation - let the immediate execution handle initial values
                            } else {
                                document.title = 'CST_PAGE_REFS:ERROR:cstPageReferences object not found';
                            }
                        } catch (error) {
                            document.title = 'CST_PAGE_REFS:ERROR:' + error.message;
                        }
                    }
                    
                    // Chapter tracking system
                    console.log('Creating cstChapterTracking object...');
                    window.cstChapterTracking = {
                        chapterElements: [],
                        currentChapter: null,
                        
                        collectChapterAnchors: function() {
                            // Collect all anchor elements that could be chapters (with names matching chapter pattern)
                            var anchors = document.querySelectorAll('a[name]');
                            this.chapterElements = [];
                            
                            console.log('Total anchors with names found:', anchors.length);
                            
                            anchors.forEach(function(anchor) {
                                var anchorName = anchor.name;
                                // Look for anchors with names like 'dn1', 'dn1_1', 'dn1_2', etc.
                                // But exclude paragraph-level anchors like 'para1', 'para10', etc.
                                if (anchorName && (anchorName.match(/^[a-z]+\d+(_\d+)*$/)) && !anchorName.startsWith('para')) {
                                    this.chapterElements.push({
                                        id: anchorName,
                                        element: anchor,
                                        offsetTop: anchor.offsetTop
                                    });
                                    console.log('Added chapter element:', anchorName, 'at offset:', anchor.offsetTop);
                                }
                            }.bind(this));
                            
                            // Sort by offset position
                            this.chapterElements.sort(function(a, b) {
                                return a.offsetTop - b.offsetTop;
                            });
                            
                            console.log('Found', this.chapterElements.length, 'chapter elements total');
                            this.chapterElements.forEach(function(ch) {
                                console.log('Chapter:', ch.id, 'at', ch.offsetTop);
                            });
                        },
                        
                        findCurrentChapter: function() {
                            var scrollTop = window.pageYOffset || document.documentElement.scrollTop;
                            var currentChapter = null;
                            
                            // Find the topmost chapter that is still above the scroll position
                            for (var i = 0; i < this.chapterElements.length; i++) {
                                var chapter = this.chapterElements[i];
                                if (chapter.offsetTop <= scrollTop + 50) { // 50px buffer
                                    currentChapter = chapter.id;
                                } else {
                                    break;
                                }
                            }
                            
                            return currentChapter;
                        },
                        
                        updateCurrentChapter: function() {
                            var scrollTop = window.pageYOffset || document.documentElement.scrollTop;
                            console.log('updateCurrentChapter called, scrollTop:', scrollTop);
                            var newChapter = this.findCurrentChapter();
                            console.log('findCurrentChapter returned:', newChapter);
                            if (newChapter !== this.currentChapter) {
                                console.log('Chapter changed from', this.currentChapter, 'to', newChapter);
                                this.currentChapter = newChapter;
                                if (newChapter) {
                                    document.title = 'CST_CURRENT_CHAPTER:' + newChapter;
                                    console.log('Set title to CST_CURRENT_CHAPTER:' + newChapter);
                                }
                            }
                        },
                        
                        setupScrollListener: function() {
                            var self = this;
                            var isThrottled = false;
                            
                            console.log('Setting up scroll listener');
                            
                            function handleScroll() {
                                if (!isThrottled) {
                                    // Update immediately for responsive feel
                                    self.updateCurrentChapter();
                                    isThrottled = true;
                                    
                                    // Allow next update after 50ms (instead of waiting for scroll to stop)
                                    setTimeout(function() {
                                        isThrottled = false;
                                    }, 50);
                                }
                            }
                            
                            window.addEventListener('scroll', handleScroll);
                        }
                    };
                    
                    // Test our custom scheme handler
                    function testSchemeHandler() {
                        console.log('Testing custom scheme handler...');
                        try {
                            fetch('http://cst-local/page-references', {
                                method: 'POST',
                                headers: { 'Content-Type': 'application/json' },
                                body: JSON.stringify({ Vri: 'TEST', Myanmar: 'TEST', Pts: 'TEST', Thai: 'TEST', Other: 'TEST' })
                            }).then(response => {
                                console.log('Test POST response:', response.status);
                                return response.text();
                            }).then(text => {
                                console.log('Test POST response body:', text);
                            }).catch(error => {
                                console.error('Test POST error:', error);
                            });
                        } catch (error) {
                            console.error('Test scheme handler error:', error);
                        }
                    }

                    // Immediate execution for testing - use document title to communicate back to C#
                    try {
                        // Find page anchors and send data via document title
                        var allAnchors = document.querySelectorAll('a[name]');
                        var pageAnchors = Array.from(allAnchors).filter(function(a) {
                            var name = a.name || '';
                            return name.match(/^[VMPTO]/);
                        });
                        
                        if (pageAnchors.length > 0) {
                            // Extract first page of each type
                            var vriPage = '*', myanmarPage = '*', ptsPage = '*', thaiPage = '*', otherPage = '*';
                            
                            for (var i = 0; i < pageAnchors.length; i++) {
                                var name = pageAnchors[i].name;
                                if (name.startsWith('V') && vriPage === '*') vriPage = name;
                                else if (name.startsWith('M') && myanmarPage === '*') myanmarPage = name;
                                else if (name.startsWith('P') && ptsPage === '*') ptsPage = name;
                                else if (name.startsWith('T') && thaiPage === '*') thaiPage = name;
                                else if (name.startsWith('O') && otherPage === '*') otherPage = name;
                            }
                            
                            // Use document title to send page references back to C#
                            // Format: CST_PAGE_REFS:VRI=1|MYANMAR=1|PTS=1|THAI=1|OTHER=*
                            document.title = 'CST_PAGE_REFS:VRI=' + vriPage + '|MYANMAR=' + myanmarPage + '|PTS=' + ptsPage + '|THAI=' + thaiPage + '|OTHER=' + otherPage;
                        } else {
                            document.title = 'CST_PAGE_REFS:NO_ANCHORS_FOUND';
                        }
                    } catch (error) {
                        document.title = 'CST_PAGE_REFS:ERROR:' + error.message;
                    }

                    function initializeChapterTracking() {
                        console.log('initializeChapterTracking called');
                        try {
                            if (window.cstChapterTracking) {
                                console.log('cstChapterTracking object exists, initializing...');
                                window.cstChapterTracking.collectChapterAnchors();
                                window.cstChapterTracking.setupScrollListener();
                                // Get initial chapter
                                window.cstChapterTracking.updateCurrentChapter();
                                console.log('Chapter tracking initialization complete');
                            } else {
                                console.log('ERROR: cstChapterTracking object not found!');
                            }
                        } catch (error) {
                            console.log('Error initializing chapter tracking:', error.message);
                        }
                    }

                    if (document.readyState === 'complete') {
                        setTimeout(initializeHighlights, 100);
                        setTimeout(initializePageReferences, 150);
                        setTimeout(initializeChapterTracking, 200);
                        setTimeout(testSchemeHandler, 300);
                    } else {
                        document.addEventListener('DOMContentLoaded', function() {
                            setTimeout(initializeHighlights, 100);
                            setTimeout(initializePageReferences, 150);
                            setTimeout(initializeChapterTracking, 200);
                            setTimeout(testSchemeHandler, 300);
                        });
                    }
                ";
                
                _cefBrowser.ExecuteJavaScript(script);
            }
            catch (Exception ex)
            {
                // JavaScript setup failed, but don't crash the app
                Console.WriteLine($"Failed to setup JavaScript bridge: {ex.Message}");
            }
        }
    }

    private void NavigateToHighlight(int hitIndex)
    {
        if (_cefBrowser != null)
        {
            // Ensure CefGlue operations are on UI thread
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => NavigateToHighlight(hitIndex));
                return;
            }

            try
            {
                Console.WriteLine($"NavigateToHighlight called with hitIndex: {hitIndex}");
                
                // First check if our JavaScript bridge exists
                var checkScript = "console.log('Bridge exists:', !!window.cstSearchHighlights); if (window.cstSearchHighlights) { console.log('Hits length:', window.cstSearchHighlights.hits.length); }";
                _cefBrowser.ExecuteJavaScript(checkScript);
                
                var script = $"window.cstSearchHighlights?.navigateToHit({hitIndex});";
                _cefBrowser.ExecuteJavaScript(script);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to navigate to highlight: {ex.Message}");
            }
        }
        else
        {
            Console.WriteLine("NavigateToHighlight called but _cefBrowser is null");
        }
    }

    // Public method to navigate to a specific anchor
    public void NavigateToAnchor(string anchor)
    {
        if (_cefBrowser != null && !string.IsNullOrEmpty(anchor))
        {
            // Ensure CefGlue operations are on UI thread
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => NavigateToAnchor(anchor));
                return;
            }

            try
            {
                var script = $"document.location.hash = '#{anchor}';";
                _cefBrowser.ExecuteJavaScript(script);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to navigate to anchor: {ex.Message}");
            }
        }
    }

    // Public method to toggle search highlighting visibility
    public void SetHighlightVisibility(bool visible)
    {
        if (_cefBrowser != null)
        {
            // Ensure CefGlue operations are on UI thread
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => SetHighlightVisibility(visible));
                return;
            }

            try
            {
                var script = $"window.cstSearchHighlights?.showHits({visible.ToString().ToLower()});";
                _cefBrowser.ExecuteJavaScript(script);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to set highlight visibility: {ex.Message}");
            }
        }
    }

    // Public method to get current scroll position
    public int GetScrollPosition()
    {
        // Return 0 if browser is not ready
        if (_cefBrowser == null || !_isBrowserInitialized)
            return 0;
            
        // Return the last known scroll position
        return _lastScrollPosition;
    }
    
    // Public method to restore scroll position
    public void SetScrollPosition(int position)
    {
        if (_cefBrowser != null && _isBrowserInitialized && position > 0)
        {
            // Ensure CefGlue operations are on UI thread
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => SetScrollPosition(position));
                return;
            }

            try
            {
                var script = $"window.scrollTo(0, {position});";
                _cefBrowser.ExecuteJavaScript(script);
                _lastScrollPosition = position;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to set scroll position: {ex.Message}");
            }
        }
    }
    
    // Public method to get current page anchor for position preservation
    public string GetCurrentPageAnchor()
    {
        // Return the current VRI anchor if available, otherwise empty
        if (_viewModel != null && !string.IsNullOrEmpty(_viewModel.CurrentVriAnchor) && _viewModel.CurrentVriAnchor != "*")
        {
            // Return the raw anchor name (e.g., "V1.0123")
            return _viewModel.CurrentVriAnchor;
        }
        return "";
    }
    
    // Public method to scroll to a page anchor
    public void ScrollToPageAnchor(string anchorName)
    {
        if (_cefBrowser != null && _isBrowserInitialized && !string.IsNullOrEmpty(anchorName))
        {
            // Ensure CefGlue operations are on UI thread
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => ScrollToPageAnchor(anchorName));
                return;
            }

            try
            {
                // Scroll to the anchor element
                var script = $@"
                    var anchor = document.querySelector('a[name=""{anchorName}""]');
                    if (anchor) {{
                        anchor.scrollIntoView({{ behavior: 'instant', block: 'start' }});
                        console.log('Scrolled to anchor: {anchorName}');
                    }} else {{
                        console.log('Anchor not found: {anchorName}');
                    }}
                ";
                _cefBrowser.ExecuteJavaScript(script);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to scroll to anchor: {ex.Message}");
            }
        }
    }
    
    // Public method to toggle footnote visibility
    public void SetFootnoteVisibility(bool visible)
    {
        if (_cefBrowser != null)
        {
            // Ensure CefGlue operations are on UI thread
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(() => SetFootnoteVisibility(visible));
                return;
            }

            try
            {
                var script = $"window.cstSearchHighlights?.showFootnotes({visible.ToString().ToLower()});";
                _cefBrowser.ExecuteJavaScript(script);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to set footnote visibility: {ex.Message}");
            }
        }
    }


    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        
        // Unsubscribe from page reference events
        CstSchemeHandlerFactory.PageReferencesUpdated -= OnPageReferencesUpdated;
        
        if (_viewModel != null)
        {
            _viewModel.BookDisplayControl = null; // Clear the reference
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.NavigateToHighlightRequested -= NavigateToHighlight;
            _viewModel.NavigateToChapterRequested -= NavigateToAnchor;
        }
        
        if (_cefBrowser != null)
        {
            _cefBrowser.BrowserInitialized -= () => OnBrowserInitialized(null, EventArgs.Empty);
            _cefBrowser.LoadEnd -= OnLoadEnd;
            _cefBrowser.LoadError -= OnLoadError;
            _cefBrowser.TitleChanged -= OnTitleChanged;
        }
    }

    // Public method to update page references from JavaScript polling
    public void UpdatePageReferencesFromJavaScript()
    {
        if (_cefBrowser != null && _viewModel != null)
        {
            // Ensure CefGlue operations are on UI thread
            if (!Dispatcher.UIThread.CheckAccess())
            {
                Dispatcher.UIThread.Post(UpdatePageReferencesFromJavaScript);
                return;
            }

            try
            {
                // For now, just trigger the calculation with placeholder scroll position
                // In a full implementation, we'd poll the JavaScript for the actual page references
                _viewModel.CalculatePageStatus(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to update page references: {ex.Message}");
            }
        }
    }
}