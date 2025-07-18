using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Xilium.CefGlue.Avalonia;
using Xilium.CefGlue.Common.Events;
using CST.Avalonia.ViewModels;
using CST.Avalonia.Services;
using Serilog;

namespace CST.Avalonia.Views;

public partial class BookDisplayView : UserControl
{
    // Shared lock to serialize JavaScript execution across all instances
    private static readonly SemaphoreSlim _jsExecutionLock = new SemaphoreSlim(1, 1);

    // Logger instance with tab context
    private readonly ILogger _logger;

    private BookDisplayViewModel? _viewModel;
    private AvaloniaCefBrowser? _cefBrowser;
    private ScrollViewer? _fallbackBrowser;
    private Decorator? _browserWrapper;
    private int _lastScrollPosition = 0;
    private bool _isBrowserInitialized = false;
    private TaskCompletionSource<string?>? _paraAnchorTcs = null;
    private readonly string _tabId = $"tab_{DateTime.Now.Ticks}_{Guid.NewGuid().ToString("N")[..8]}";

    // C# scroll tracking for reliable status bar updates
    private int _lastKnownScrollY = 0;
    private DateTime _lastScrollTime = DateTime.MinValue;
    private System.Timers.Timer? _scrollTimer;
    private string _lastKnownVri = "*";
    private string _lastKnownMyanmar = "*";
    private string _lastKnownPts = "*";
    private string _lastKnownThai = "*";
    private string _lastKnownPara = "*";

    public BookDisplayView()
    {
        InitializeComponent();

        // Get logger with tab context
        _logger = Log.ForContext<BookDisplayView>()
            .ForContext("TabId", _tabId);

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
                // Create browser with unique context to try to isolate instances
                _cefBrowser = new AvaloniaCefBrowser();

                // Set up event handlers for debugging
                _cefBrowser.BrowserInitialized += () => OnBrowserInitialized(null, EventArgs.Empty);
                _cefBrowser.LoadEnd += OnLoadEnd;
                _cefBrowser.LoadError += OnLoadError;
                _cefBrowser.TitleChanged += OnTitleChanged;

                _browserWrapper.Child = _cefBrowser;

                _logger.Information("CefGlue browser created successfully");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to create CefGlue browser");
            _cefBrowser = null;
        }
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        this.PropertyChanged += OnIsVisibleChanged;
        _logger.Debug("OnLoaded called");
        SetupCSharpScrollTracking();
        SetupKeyboardHandling();
    }

    protected override void OnDataContextChanged(EventArgs e)
    {
        base.OnDataContextChanged(e);

        // First, unsubscribe from the old ViewModel if it exists
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.NavigateToHighlightRequested -= NavigateToHighlight;
            _viewModel.NavigateToChapterRequested -= NavigateToAnchor;
            _viewModel.BookDisplayControl = null;
        }

        // Then, subscribe to the new ViewModel
        _viewModel = DataContext as BookDisplayViewModel;
        _logger.Debug("DataContext changed. ViewModel is now: {BookInfo}", _viewModel?.BookInfoText ?? "null");

        if (_viewModel != null)
        {
            _viewModel.BookDisplayControl = this;
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            _viewModel.NavigateToHighlightRequested += NavigateToHighlight;
            _viewModel.NavigateToChapterRequested += NavigateToAnchor;
        }
    }

    private void OnIsVisibleChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == IsVisibleProperty && _scrollTimer != null)
        {
            var isVisible = e.GetNewValue<bool>();
            if (isVisible)
            {
                _logger.Debug("View became visible, starting scroll timer.");
                _scrollTimer.Start();
            }
            else
            {
                _logger.Debug("View was hidden, stopping scroll timer.");
                _scrollTimer.Stop();
            }
        }
    }


    private void OnViewModelPropertyChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (e.PropertyName == nameof(BookDisplayViewModel.HtmlContent))
        {
            Dispatcher.UIThread.Post(() => LoadHtmlContent());
        }
    }

    private void LoadHtmlContent()
    {
        _logger.Debug("Method called - ViewModel: {HasViewModel}, HtmlContent empty: {IsHtmlEmpty}", _viewModel != null, string.IsNullOrEmpty(_viewModel?.HtmlContent));

        if (_viewModel == null || string.IsNullOrEmpty(_viewModel.HtmlContent))
        {
            _logger.Debug("Exiting - no viewmodel or content");
            return;
        }

        // Ensure we're on the UI thread for CefGlue operations
        if (!Dispatcher.UIThread.CheckAccess())
        {
            _logger.Debug("Dispatching to UI thread");
            Dispatcher.UIThread.Post(LoadHtmlContent);
            return;
        }

        try
        {
            _logger.Debug("CefGlue status - available: {IsCefGlueAvailable}, Browser: {HasBrowser}", _viewModel.IsCefGlueAvailable, _cefBrowser != null);

            if (_viewModel.IsCefGlueAvailable && _cefBrowser != null)
            {
                try
                {
                    // Check content size and use appropriate loading method
                    _logger.Information("Loading HTML content - length: {Length}", _viewModel.HtmlContent.Length);
                    //_logger.Debug("LoadHtmlContent", "HTML content preview", _viewModel.HtmlContent.Substring(0, Math.Min(200, _viewModel.HtmlContent.Length)) + "...");

                    // Write HTML content to temporary file and load it
                    // This completely bypasses data URI size limitations
                    var tempFileName = $"cst_book_{_viewModel.Book.FileName.Replace('.', '_')}_{_tabId}.html";
                    var tempFilePath = Path.Combine(Path.GetTempPath(), tempFileName);

                    _logger.Debug("Writing to temp file | {Details}", tempFilePath);
                    File.WriteAllText(tempFilePath, _viewModel.HtmlContent, System.Text.Encoding.UTF8);

                    var fileUrl = $"file://{tempFilePath}";
                    _logger.Debug("Loading from file URL | {Details}", fileUrl);

                    _cefBrowser.Address = fileUrl;
                    _viewModel.PageStatusText = "Loading content from file...";
                    _logger.Information("HTML content loaded from temporary file");
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to load HTML content | {Details}", ex.Message);
                    _viewModel.SetCefGlueAvailability(false, "Failed to load content - using fallback");
                }
            }
            else if (_cefBrowser == null)
            {
                // Browser creation failed, disable CefGlue
                _logger.Warning("Browser is null - setting CefGlue unavailable");
                _viewModel.SetCefGlueAvailability(false, "CefGlue browser unavailable - using fallback text display");
            }
            else
            {
                _logger.Information("CefGlue not available - using fallback");
            }
            // Fallback is already handled by data binding in XAML
        }
        catch (Exception ex)
        {
            // If CefGlue fails, mark it as unavailable and fall back to text display
            _logger.Error("Exception occurred | {Details}", ex.Message);
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
                _logger.Information("Browser initialized successfully");
                _viewModel.PageStatusText = "Browser ready";

                // Load content if it's ready
                if (!string.IsNullOrEmpty(_viewModel.HtmlContent))
                {
                    _logger.Debug("Loading content immediately - HTML length: {Length}", _viewModel.HtmlContent.Length);
                    LoadHtmlContent();
                }
                else
                {
                    _logger.Debug("No HTML content ready yet - will load when content is generated");
                }

                // Set up C# scroll tracking for reliable status bar updates
                // SetupCSharpScrollTracking(); // This is now called from OnLoaded
            });
        }
    }

    private void SetupCSharpScrollTracking()
    {
        // If timer already exists, do nothing. This makes the method idempotent.
        if (_scrollTimer != null) return;

        _logger.Debug("SetupCSharpScrollTracking called");

        // Set up initial scroll position tracking
        _lastKnownScrollY = 0;
        _lastScrollTime = DateTime.Now;

        // Create the timer immediately on the UI thread.
        _logger.Debug("Creating scroll timer");
        _scrollTimer = new System.Timers.Timer(200);
        _scrollTimer.Elapsed += OnScrollPositionCheck;
        _scrollTimer.AutoReset = true;

        // If the control is already visible when this runs, start the timer.
        if (this.IsVisible)
        {
            _scrollTimer.Start();
        }
        _logger.Debug("Scroll timer created - enabled: {Enabled}", _scrollTimer.Enabled);
        _logger.Debug("C# scroll position monitoring setup completed");
    }

    private void OnScrollPositionCheck(object? sender, System.Timers.ElapsedEventArgs e)
    {
        if (_cefBrowser == null || !_isBrowserInitialized || _viewModel == null)
        {
            return;
        }

        // Post the work to the UI thread, and perform the lock check there.
        Dispatcher.UIThread.Post(async () =>
        {
            // This delay gives the browser time to process pending layout changes before we query it.
            // It happens BEFORE the lock is acquired, so it does not block other UI operations.
            await Task.Delay(200);

            _logger.Debug("OnScrollPositionCheck attempting to acquire JS lock");
            if (await _jsExecutionLock.WaitAsync(0))
            {
                _logger.Debug("OnScrollPositionCheck acquired JS lock successfully");
                try
                {
                    UpdateScrollBasedStatus();
                }
                finally
                {
                    _logger.Debug("OnScrollPositionCheck releasing JS lock");
                    _jsExecutionLock.Release();
                    _logger.Debug("OnScrollPositionCheck released JS lock");
                }
            }
            else
            {
                _logger.Debug("OnScrollPositionCheck failed to acquire JS lock - skipped status update");
            }
        });
    }

    private void UpdateScrollBasedStatus()
    {
        try
        {
            _logger.Debug("UpdateScrollBasedStatus called - anchorCacheBuilt: {AnchorCacheBuilt}", _anchorCacheBuilt);

            if (!_anchorCacheBuilt || _cefBrowser == null)
            {
                _logger.Debug("UpdateScrollBasedStatus skipped - anchorCacheBuilt: {AnchorCacheBuilt}, browser: {HasBrowser}", _anchorCacheBuilt, _cefBrowser != null);
                return;
            }

            // Try to get scroll position and status in a single JavaScript call
            _logger.Debug("Executing JavaScript for status update");
            var script = $@"
                try {{
                    var scrollY = window.pageYOffset || document.documentElement.scrollTop || 0;
                    
                    var vri = '*', myanmar = '*', pts = '*', thai = '*', para = '*';
                    
                    // Try to get page references
                    try {{
                        if (window.cstAnchorCache && window.cstAnchorCache.getPageReferences) {{
                            var refs = window.cstAnchorCache.getPageReferences(scrollY);
                            vri = refs.vri || '*';
                            myanmar = refs.myanmar || '*';
                            pts = refs.pts || '*';
                            thai = refs.thai || '*';
                        }}
                    }} catch(pageError) {{
                    }}
                    
                    // Try to get the paragraph number using the performant, pre-sorted cache
                    try {{
                        if (window.cstAnchorCache && window.cstAnchorCache.getCurrentParagraph) {{
                            para = window.cstAnchorCache.getCurrentParagraph(scrollY);
                        }}
                    }} catch(paraError) {{
                    }}
                    
                    // FALLBACK: If we're at the top (scroll=0) and don't have values, find the first anchors
                    if (scrollY < 50 && (vri === '*' || para === '*')) {{
                        try {{
                            // Find first page anchors if we're at the top
                            if (vri === '*' && window.cstAnchorCache && window.cstAnchorCache.sortedPageAnchors) {{
                                if (window.cstAnchorCache.sortedPageAnchors.V && window.cstAnchorCache.sortedPageAnchors.V.length > 0) {{
                                    vri = window.cstAnchorCache.sortedPageAnchors.V[0].name;
                                }}
                                if (window.cstAnchorCache.sortedPageAnchors.M && window.cstAnchorCache.sortedPageAnchors.M.length > 0) {{
                                    myanmar = window.cstAnchorCache.sortedPageAnchors.M[0].name;
                                }}
                                if (window.cstAnchorCache.sortedPageAnchors.P && window.cstAnchorCache.sortedPageAnchors.P.length > 0) {{
                                    pts = window.cstAnchorCache.sortedPageAnchors.P[0].name;
                                }}
                                if (window.cstAnchorCache.sortedPageAnchors.T && window.cstAnchorCache.sortedPageAnchors.T.length > 0) {{
                                    thai = window.cstAnchorCache.sortedPageAnchors.T[0].name;
                                }}
                            }}
                            
                            // Find first paragraph if we're at the top
                            if (para === '*' && window.cstAnchorCache && window.cstAnchorCache.sortedParagraphAnchors && window.cstAnchorCache.sortedParagraphAnchors.length > 0) {{
                                para = window.cstAnchorCache.sortedParagraphAnchors[0].name.replace('para', '');
                            }}
                        }} catch(fallbackError) {{
                        }}
                    }}
                    
                    // Determine current chapter based on scroll position
                    var currentChapter = '*';
                    try {{
                        if (window.cstAnchorCache && window.cstAnchorCache.sortedChapterAnchors && window.cstAnchorCache.sortedChapterAnchors.length > 0) {{
                            // Find the chapter anchor that's closest to but not above the current scroll position
                            var bestChapter = null;
                            var bestDistance = Infinity;
                            
                            for (var i = 0; i < window.cstAnchorCache.sortedChapterAnchors.length; i++) {{
                                var chapterAnchor = window.cstAnchorCache.sortedChapterAnchors[i];
                                var distance = scrollY - chapterAnchor.position;
                                
                                // If we're at or past this chapter, and it's closer than our current best
                                if (distance >= 0 && distance < bestDistance) {{
                                    bestChapter = chapterAnchor;
                                    bestDistance = distance;
                                }}
                            }}
                            
                            // If no chapter was found (e.g., we're at the very top), use the first chapter
                            if (!bestChapter && window.cstAnchorCache.sortedChapterAnchors.length > 0) {{
                                bestChapter = window.cstAnchorCache.sortedChapterAnchors[0];
                            }}
                            
                            if (bestChapter) {{
                                currentChapter = bestChapter.name;
                            }}
                        }}
                    }} catch(chapterError) {{
                        // If chapter detection fails, use '*' as fallback
                    }}
                    
                    // ATOMIC UPDATE: Send all status info in one message with tab ID including chapter
                    document.title = 'CST_STATUS_UPDATE:VRI=' + vri + '|MYANMAR=' + myanmar + '|PTS=' + pts + '|THAI=' + thai + '|PARA=' + para + '|CHAPTER=' + currentChapter + '|SCROLL=' + scrollY + '|TAB:__TAB_ID_PLACEHOLDER__';
                }} catch(e) {{
                    document.title = 'CST_STATUS_UPDATE:VRI=*|MYANMAR=*|PTS=*|THAI=*|PARA=*|CHAPTER=*|SCROLL=0|TAB:__TAB_ID_PLACEHOLDER__';
                }}
            ";

            // Replace tab ID placeholder with actual tab ID value
            script = script.Replace("__TAB_ID_PLACEHOLDER__", _tabId);
            
            _cefBrowser.ExecuteJavaScript(script);
        }
        catch (Exception ex)
        {
            _logger.Error("Error updating scroll-based status | {Details}", ex.Message);
        }
    }

    private bool _anchorCacheBuilt = false;

    private async Task BuildAnchorPositionCache()
    {
        if (_cefBrowser == null) return;

        _logger.Debug("BuildAnchorPositionCache attempting to acquire JS lock");
        if (await _jsExecutionLock.WaitAsync(10))
        {
            _logger.Debug("BuildAnchorPositionCache acquired JS lock successfully");
            try
            {
                _logger.Information("Building anchor position cache");

                // Store anchor positions directly in JavaScript  
                var script = $@"
                (function() {{
                    // Store anchor positions in the window object for C# queries
                    window.cstAnchorCache = {{
                        pageAnchors: {{}},
                        paragraphAnchors: {{}},
                        chapterAnchors: {{}},
                        // Add properties to hold the pre-sorted lists for performance
                        sortedPageAnchors: {{ V: [], M: [], P: [], T: [] }},
                        sortedParagraphAnchors: [],
                        sortedChapterAnchors: [],
                        
                        build: function() {{
                            this.pageAnchors = {{}};
                            this.paragraphAnchors = {{}};
                            this.chapterAnchors = {{}};
                            this.sortedPageAnchors = {{ V: [], M: [], P: [], T: [] }};
                            this.sortedParagraphAnchors = [];
                            this.sortedChapterAnchors = [];

                            // Force layout calculation for the entire document
                            // This is a workaround to ensure getBoundingClientRect() returns correct, absolute values
                            var allElements = document.getElementsByTagName('*');
                            for (var i = 0; i < allElements.length; i++) {{
                                // Accessing a property like this forces the browser to compute the layout
                                if (allElements[i].offsetParent === null) {{ continue; }}
                            }}

                            // Collect page anchors with the CORRECT position calculation.
                            ['V', 'M', 'P', 'T'].forEach(function(prefix) {{
                                var anchors = document.querySelectorAll('a[name^=""' + prefix + '""]');
                                anchors.forEach(function(anchor) {{
                                    var rect = anchor.getBoundingClientRect();
                                    // THE FIX: Add window.pageYOffset to get the absolute document position.
                                    var position = Math.round(rect.top + window.pageYOffset);
                                    this.pageAnchors[anchor.name] = position;
                                }}.bind(this));
                            }}.bind(this));

                            // Collect paragraph anchors with the CORRECT position calculation.
                            var paraAnchors = document.querySelectorAll('a[name^=""para""]');
                            paraAnchors.forEach(function(anchor) {{
                                if (anchor.name) {{
                                    var rect = anchor.getBoundingClientRect();
                                    // THE FIX: Add window.pageYOffset to get the absolute document position.
                                    var position = Math.round(rect.top + window.pageYOffset);
                                    this.paragraphAnchors[anchor.name] = position;
                                }}
                            }}.bind(this));

                            // Collect chapter anchors (anchor elements with names like 'dn1', 'dn1_1', etc.)
                            // Exclude paragraph anchors (which start with 'para') and page anchors (which start with V, M, P, T)
                            var chapterAnchors = document.querySelectorAll('a[name]');
                            chapterAnchors.forEach(function(anchor) {{
                                if (anchor.name && anchor.name.match(/^[a-z]+\d+(_\d+)?$/) && 
                                    !anchor.name.startsWith('para') && 
                                    !anchor.name.match(/^[VMPT]/)) {{
                                    var rect = anchor.getBoundingClientRect();
                                    // THE FIX: Add window.pageYOffset to get the absolute document position.
                                    var position = Math.round(rect.top + window.pageYOffset);
                                    this.chapterAnchors[anchor.name] = position;
                                }}
                            }}.bind(this));

                            // Pre-sort page anchors
                            for (var name in this.pageAnchors) {{
                                var prefix = name.charAt(0);
                                if (this.sortedPageAnchors[prefix]) {{
                                    this.sortedPageAnchors[prefix].push({{ name: name, position: this.pageAnchors[name] }});
                                }}
                            }}
                            Object.keys(this.sortedPageAnchors).forEach(function(type) {{
                                this.sortedPageAnchors[type].sort(function(a, b) {{ return a.position - b.position; }});
                            }}.bind(this));

                            // Pre-sort paragraph anchors
                            for (var name in this.paragraphAnchors) {{
                                this.sortedParagraphAnchors.push({{ name: name, position: this.paragraphAnchors[name] }});
                            }}
                            this.sortedParagraphAnchors.sort(function(a, b) {{ return a.position - b.position; }});

                            // Pre-sort chapter anchors
                            for (var name in this.chapterAnchors) {{
                                this.sortedChapterAnchors.push({{ name: name, position: this.chapterAnchors[name] }});
                            }}
                            this.sortedChapterAnchors.sort(function(a, b) {{ return a.position - b.position; }});

                            document.title = 'CST_STATUS_UPDATE:CACHE_BUILT=' + Object.keys(this.pageAnchors).length + ',' + Object.keys(this.paragraphAnchors).length + ',' + Object.keys(this.chapterAnchors).length + '|TAB:__TAB_ID_PLACEHOLDER__';
                        }},
                        
                        getPageReferences: function(scrollY) {{
                            var result = {{ vri: '*', myanmar: '*', pts: '*', thai: '*' }};
                            var docPos = scrollY + 20; // CST4 algorithm offset
                            
                            // PERFORMANCE OPTIMIZATION: Use pre-sorted lists instead of expensive sorting on every call
                            // The findBestAnchor function now works on the pre-sorted lists
                            function findBestAnchor(sortedAnchors) {{
                                if (!sortedAnchors || sortedAnchors.length === 0) {{
                                    return null;
                                }}
                                
                                // Linear search is vastly more performant than the previous implementation
                                // Since the list is sorted, we can stop as soon as we pass the scroll position
                                var bestAnchor = null;
                                for (var i = 0; i < sortedAnchors.length; i++) {{
                                    if (sortedAnchors[i].position <= docPos) {{
                                        bestAnchor = sortedAnchors[i];
                                    }} else {{
                                        // Since the list is sorted, we can stop here
                                        break; 
                                    }}
                                }}
                                return bestAnchor;
                            }}
                            
                            // Find best anchor for each type using the pre-sorted lists
                            var vriAnchor = findBestAnchor(this.sortedPageAnchors.V);
                            var myanmarAnchor = findBestAnchor(this.sortedPageAnchors.M);
                            var ptsAnchor = findBestAnchor(this.sortedPageAnchors.P);
                            var thaiAnchor = findBestAnchor(this.sortedPageAnchors.T);
                            
                            result.vri = vriAnchor ? vriAnchor.name : '*';
                            result.myanmar = myanmarAnchor ? myanmarAnchor.name : '*';
                            result.pts = ptsAnchor ? ptsAnchor.name : '*';
                            result.thai = thaiAnchor ? thaiAnchor.name : '*';
                            
                            return result;
                        }},
                        
                        getCurrentParagraph: function(scrollY) {{
                            // PERFORMANCE OPTIMIZATION: Use pre-sorted paragraph anchors for fast lookup
                            var docPos = scrollY + 100; // Offset to find the anchor just above the fold
                            
                            if (!this.sortedParagraphAnchors || this.sortedParagraphAnchors.length === 0) {{
                                return '*';
                            }}
                            
                            // Perform a fast linear search on the pre-sorted list - NO MORE EXPENSIVE LOOP!
                            var bestPara = null;
                            for (var i = 0; i < this.sortedParagraphAnchors.length; i++) {{
                                if (this.sortedParagraphAnchors[i].position <= docPos) {{
                                    bestPara = this.sortedParagraphAnchors[i];
                                }} else {{
                                    // The list is sorted, so we can stop searching
                                    break;
                                }}
                            }}
                            
                            if (bestPara) {{
                                // Extract paragraph number, handling both simple and range formats
                                var paraName = bestPara.name;
                                if (paraName.startsWith(""para"")) {{
                                    var paraText = paraName.substring(4); // Remove ""para"" prefix
                                    var underscoreIndex = paraText.indexOf(""_"");
                                    if (underscoreIndex !== -1) {{
                                        paraText = paraText.substring(0, underscoreIndex); // Remove book code suffix
                                    }}
                                    return paraText; // Returns ""548"" or ""548-9""
                                }}
                            }}
                            
                            return ""*"";
                        }}
                    }};
                    
                    // Build the cache immediately
                    window.cstAnchorCache.build();
                    
                    // Rebuild cache when window is resized
                    window.addEventListener('resize', function() {{
                        setTimeout(function() {{
                            window.cstAnchorCache.build();
                        }}, 100); // Small delay to let text reflow
                    }});
                }})();
            ";

                // Replace tab ID placeholder with actual tab ID value
                script = script.Replace("__TAB_ID_PLACEHOLDER__", _tabId);

                _cefBrowser.ExecuteJavaScript(script);

                // Wait for the cache to be built
                await Task.Delay(500);

                _anchorCacheBuilt = true;
                _logger.Information("Anchor position cache built");
            }
            catch (Exception ex)
            {
                _logger.Error("Error building anchor cache | {Details}", ex.Message);
            }
            finally
            {
                _logger.Debug("BuildAnchorPositionCache releasing JS lock");
                _jsExecutionLock.Release();
                _logger.Debug("BuildAnchorPositionCache released JS lock");
            }
        }
        else
        {
            // If lock is busy, retry after a delay
            _logger.Debug("BuildAnchorPositionCache failed to acquire JS lock - retrying after delay");
            await Task.Delay(100);
            await BuildAnchorPositionCache();
        }
    }

    private void OnLoadEnd(object? sender, LoadEndEventArgs e)
    {
        if (_viewModel != null && e.Frame.IsMain)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _logger.Information("Main frame loaded successfully - URL: {Url}", e.Frame.Url);
                _viewModel.PageStatusText = "Document loaded successfully";

                // Set up JavaScript bridge after content loads
                SetupJavaScriptBridge();

                // Build the anchor position cache in the background.
                _logger.Debug("Starting background task to build anchor cache");
                Task.Run(async () =>
                {
                    _logger.Debug("Background task started, waiting for content to settle");
                    await Task.Delay(2000); // Wait for content to settle

                    _logger.Debug("Building anchor cache");
                    await BuildAnchorPositionCache();
                });

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
                _logger.Error("Load error occurred - Error: {ErrorText}, Code: {ErrorCode}, URL: {FailedUrl}", e.ErrorText, e.ErrorCode, e.FailedUrl);
                _viewModel.PageStatusText = $"Load error: {e.ErrorText}";
            });
        }
    }

    private void OnTitleChanged(object? sender, string title)
    {
        _logger.Debug("Page title changed | {Details}", title);

        // Check for new atomic status update with tab ID filtering
        if (title != null && title.StartsWith("CST_STATUS_UPDATE:"))
        {
            try
            {
                var data = title.Substring("CST_STATUS_UPDATE:".Length);
                var parts = data.Split('|');

                // Extract tab ID and verify it matches this tab
                string messageTabId = "";
                foreach (var part in parts)
                {
                    if (part.StartsWith("TAB:"))
                    {
                        messageTabId = part.Substring(4);
                        break;
                    }
                }

                // CRITICAL: Only process messages intended for this specific tab
                if (messageTabId != _tabId)
                {
                    _logger.Debug("Ignoring message for tab | {Details}", messageTabId);
                    return;
                }

                _logger.Debug("Processing status update message");

                // Parse message components
                string vri = "*", myanmar = "*", pts = "*", thai = "*", para = "*", chapter = "*";
                int scrollY = 0;
                bool isCacheBuilt = false;

                foreach (var part in parts)
                {
                    if (part.StartsWith("VRI=")) vri = part.Substring(4);
                    else if (part.StartsWith("MYANMAR=")) myanmar = part.Substring(8);
                    else if (part.StartsWith("PTS=")) pts = part.Substring(4);
                    else if (part.StartsWith("THAI=")) thai = part.Substring(5);
                    else if (part.StartsWith("PARA=")) para = part.Substring(5);
                    else if (part.StartsWith("CHAPTER=")) chapter = part.Substring(8);
                    else if (part.StartsWith("SCROLL=")) int.TryParse(part.Substring(7), out scrollY);
                    else if (part.StartsWith("CACHE_BUILT="))
                    {
                        isCacheBuilt = true;
                        var counts = part.Substring(12).Split(',');
                        var pageCount = counts.Length > 0 ? counts[0] : "0";
                        var paraCount = counts.Length > 1 ? counts[1] : "0";
                        var chapterCount = counts.Length > 2 ? counts[2] : "0";
                        _logger.Debug("Anchor cache built - {PageCount} page anchors, {ParaCount} paragraph anchors, {ChapterCount} chapter anchors", pageCount, paraCount, chapterCount);
                    }
                }

                // Handle cache built notification
                if (isCacheBuilt)
                {
                    _anchorCacheBuilt = true;
                }
                else
                {
                    // Handle status update
                    _logger.Debug("Status values - VRI: {Vri}, Myanmar: {Myanmar}, PTS: {Pts}, Thai: {Thai}, Para: {Para}, Chapter: {Chapter}, Scroll: {ScrollY}", vri, myanmar, pts, thai, para, chapter, scrollY);

                    // Update scroll position
                    if (scrollY > 0) _lastKnownScrollY = scrollY;

                    // Store last known values
                    if (vri != "*") _lastKnownVri = vri;
                    if (myanmar != "*") _lastKnownMyanmar = myanmar;
                    if (pts != "*") _lastKnownPts = pts;
                    if (thai != "*") _lastKnownThai = thai;
                    if (para != "*") _lastKnownPara = para;

                    // Update the ViewModel
                    if (_viewModel != null)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            _logger.Debug("Updating ViewModel on UI thread");
                            _viewModel.UpdatePageReferences(vri, myanmar, pts, thai, "*");
                            _viewModel.UpdateCurrentParagraph($"para{para}");
                            
                            // Update current chapter if we have a valid chapter ID
                            if (chapter != "*")
                            {
                                _logger.Debug("Updating current chapter | {Details}", chapter);
                                _viewModel.UpdateCurrentChapter(chapter);
                            }
                            
                            _logger.Debug("ViewModel updated successfully");
                        });
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error processing atomic status update | {Details}", ex.Message);
            }
        }
        // Check for current chapter data in title
        else if (title != null && title.StartsWith("CST_CURRENT_CHAPTER:"))
        {
            try
            {
                var parts = title.Split('|');
                var chapterId = parts[0].Substring("CST_CURRENT_CHAPTER:".Length);
                var messageTabId = parts.Length > 1 && parts[1].StartsWith("TAB:") ? parts[1].Substring(4) : "";

                if (messageTabId == _tabId)
                {
                    _logger.Debug("Detected current chapter | {Details}", chapterId);
                    if (_viewModel != null)
                    {
                        _viewModel.UpdateCurrentChapter(chapterId);
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error parsing current chapter | {Details}", ex.Message);
            }
        }
        // Check for GetPara result
        else if (title != null && title.StartsWith("CST_GET_PARA_RESULT:"))
        {
            try
            {
                var parts = title.Split('|');
                var result = parts[0].Substring("CST_GET_PARA_RESULT:".Length);
                var messageTabId = parts.Length > 1 && parts[1].StartsWith("TAB:") ? parts[1].Substring(4) : "";

                if (messageTabId == _tabId)
                {
                    _logger.Debug("GetPara result | {Details}", result);
                    // Signal completion for async await pattern
                    _paraAnchorTcs?.TrySetResult(result == "null" ? null : result);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error parsing GetPara result | {Details}", ex.Message);
                _paraAnchorTcs?.TrySetException(ex);
            }
        }
        // Check for copy operation results
        else if (title != null && title.StartsWith("CST_COPY_SUCCESS:"))
        {
            try
            {
                var parts = title.Split('|');
                var lengthStr = parts[0].Substring("CST_COPY_SUCCESS:".Length);
                var messageTabId = parts.Length > 1 && parts[1].StartsWith("TAB:") ? parts[1].Substring(4) : "";

                if (messageTabId == _tabId)
                {
                    _logger.Information("Copy operation successful - {CharacterCount} characters copied", lengthStr);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error parsing copy success message | {Details}", ex.Message);
            }
        }
        else if (title != null && title.StartsWith("CST_COPY_FAILED:"))
        {
            try
            {
                var parts = title.Split('|');
                var reason = parts[0].Substring("CST_COPY_FAILED:".Length);
                var messageTabId = parts.Length > 1 && parts[1].StartsWith("TAB:") ? parts[1].Substring(4) : "";

                if (messageTabId == _tabId)
                {
                    _logger.Warning("Copy operation failed | {Details}", reason);
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error parsing copy failure message | {Details}", ex.Message);
            }
        }
        // Check for JS log messages
        else if (title != null && title.StartsWith("CST_LOG_MSG::"))
        {
            try
            {
                var parts = title.Split(new[] { "::" }, 3, StringSplitOptions.None);
                if (parts.Length == 3)
                {
                    var level = parts[1];
                    var messageWithTab = parts[2];
                    
                    var messageParts = messageWithTab.Split(new[] { "|TAB:" }, StringSplitOptions.None);
                    var message = messageParts[0];
                    var messageTabId = messageParts.Length > 1 ? messageParts[1] : "";

                    if (messageTabId == _tabId)
                    {
                        switch (level.ToUpper())
                        {
                            case "INFO":
                                _logger.Information("JS Log | {Details}", message);
                                break;
                            case "WARN":
                                _logger.Warning("JS Log | {Details}", message);
                                break;
                            case "ERROR":
                                _logger.Error("JS Log | {Details}", message);
                                break;
                            default:
                                _logger.Debug("JS Log | {Details}", message);
                                break;
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error parsing JS log message | {Details}", ex.Message);
            }
        }
    }


    private void SetupJavaScriptBridge()
    {
        if (_cefBrowser == null) return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(SetupJavaScriptBridge);
            return;
        }

        _logger.Debug("SetupJavaScriptBridge attempting to acquire JS lock");
        if (_jsExecutionLock.Wait(0))
        {
            _logger.Debug("SetupJavaScriptBridge acquired JS lock successfully");
            try
            {
                // Add JavaScript functions for search navigation
                var script = @"
                    window.cstLogger = {
                        log: function(level, message, ...args) {
                            try {
                                var formattedArgs = args.map(function(arg) {
                                    if (typeof arg === 'object' && arg !== null) {
                                        try { return JSON.stringify(arg); } catch (e) { return '[Circular]'; }
                                    }
                                    return String(arg);
                                }).join(' ');
                                
                                var fullMessage = message + ' ' + formattedArgs;
                                // Use a unique title format to avoid conflicts
                                document.title = 'CST_LOG_MSG::' + level + '::' + fullMessage + '|TAB:{_tabId}';
                            } catch (e) {
                                // Failsafe, do nothing
                            }
                        }
                    };

                    window.cstSearchHighlights = {
                        hits: [],
                        currentIndex: 0,
                        
                        init: function() {
                            
                            // Look for <span class='hit'> elements generated by XSLT transformation
                            this.hits = Array.from(document.querySelectorAll('span.hit'));
                            
                            // Try alternative selectors if the first one doesn't work
                            if (this.hits.length === 0) {
                                this.hits = Array.from(document.querySelectorAll('span[class=""hit""]'));
                            }
                            
                            if (this.hits.length === 0) {
                                this.hits = Array.from(document.querySelectorAll('.hit'));
                            }
                            
                            if (this.hits.length === 0) {
                                var allSpans = Array.from(document.querySelectorAll('span'));
                                var hitSpans = allSpans.filter(function(el) {
                                    return el.className && el.className.includes('hit');
                                });
                                if (hitSpans.length > 0) {
                                    window.cstLogger.log('DEBUG', 'Found hits with querySelectorAll:', hitSpans.length);
                                }
                                this.hits = hitSpans;
                            }
                            
                            if (this.hits.length > 0) {
                                window.cstLogger.log('DEBUG', 'Found hits:', this.hits.length);
                            }
                            
                            this.updateHighlightStyles();
                        },
                        
                        navigateToHit: function(index) {
                            
                            if (index < 1 || index > this.hits.length) {
                                return;
                            }
                            
                            this.currentIndex = index - 1;
                            var hit = this.hits[this.currentIndex];
                            
                            if (hit) {
                                hit.scrollIntoView({ behavior: 'smooth', block: 'center' });
                                this.updateHighlightStyles();
                            } else {
                                window.cstLogger.log('WARN', 'Hit not found for index:', index);
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
                    
                    // Initialize when DOM is ready - with a small delay to ensure content is fully processed
                    function initializeHighlights() {
                        window.cstSearchHighlights.init();
                        
                        // If no hits found, try again after a short delay (in case content is still loading)
                        if (window.cstSearchHighlights.hits.length === 0) {
                            setTimeout(function() {
                                window.cstSearchHighlights.init();
                            }, 500);
                        }
                    }
                    
                    // Chapter tracking system
                    window.cstChapterTracking = {
                        chapterElements: [],
                        currentChapter: null,
                        
                        collectChapterAnchors: function() {
                            // Collect all anchor elements that could be chapters (with names matching chapter pattern)
                            var anchors = document.querySelectorAll('a[name]');
                            this.chapterElements = [];
                            
                            anchors.forEach(function(anchor) {
                                var anchorName = anchor.name;
                                // Look for anchors with names like 'dn1', 'dn1_1', 'dn1_2', etc.
                                // But exclude paragraph-level anchors like 'para1', 'para10', etc.
                                if (anchorName && (anchorName.match(/^[a-z]+\d+(_\d+)*$/)) && !anchorName.startsWith('para')) {
                                    // THE FIX: Use the same robust position calculation as the anchor cache.
                                    var rect = anchor.getBoundingClientRect();
                                    var position = Math.round(rect.top + window.pageYOffset);

                                    this.chapterElements.push({
                                        id: anchorName,
                                        element: anchor,
                                        offsetTop: position
                                    });
                                }
                            }.bind(this));
                            
                            // Sort by offset position
                            this.chapterElements.sort(function(a, b) {
                                return a.offsetTop - b.offsetTop;
                            });
                            
                            this.chapterElements.forEach(function(ch) {
                                window.cstLogger.log('DEBUG', 'Chapter anchor:', ch.id, 'at position:', ch.offsetTop);
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
                            var newChapter = this.findCurrentChapter();
                            if (newChapter !== this.currentChapter) {
                                this.currentChapter = newChapter;
                                if (newChapter) {
                                    document.title = 'CST_CURRENT_CHAPTER:' + newChapter + '|TAB:{_tabId}';
                                }
                            }
                        },
                    };

                    function initializeChapterTracking() {
                        try {
                            if (window.cstChapterTracking) {
                                window.cstChapterTracking.collectChapterAnchors();
                                // Get initial chapter
                                window.cstChapterTracking.updateCurrentChapter();
                            } else {
                                window.cstLogger.log('WARN', 'cstChapterTracking not ready');
                            }
                        } catch (error) {
                            window.cstLogger.log('ERROR', 'Error initializing chapter tracking:', error);
                        }
                    }

                    if (document.readyState === 'complete') {
                        setTimeout(initializeHighlights, 100);
                        setTimeout(initializeChapterTracking, 200);
                    } else {
                        document.addEventListener('DOMContentLoaded', function() {
                            setTimeout(initializeHighlights, 100);
                            setTimeout(initializeChapterTracking, 200);
                        });
                    }
                ";

                // Replace tab ID placeholder with actual tab ID value
                script = script.Replace("{_tabId}", _tabId);

                _cefBrowser.ExecuteJavaScript(script);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to setup JavaScript bridge | {Details}", ex.Message);
            }
            finally
            {
                _logger.Debug("SetupJavaScriptBridge releasing JS lock");
                _jsExecutionLock.Release();
                _logger.Debug("SetupJavaScriptBridge released JS lock");
            }
        }
        else
        {
            _logger.Debug("SetupJavaScriptBridge failed to acquire JS lock - retrying after delay");
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(100);
                SetupJavaScriptBridge();
            }, DispatcherPriority.Background);
        }
    }

    private void NavigateToHighlight(int hitIndex)
    {
        if (_cefBrowser == null)
        {
            _logger.Warning("NavigateToHighlight called but _cefBrowser is null");
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => NavigateToHighlight(hitIndex));
            return;
        }

        _logger.Debug("Method called - hitIndex: {HitIndex}", hitIndex);
        _logger.Debug("NavigateToHighlight attempting to acquire JS lock");
        if (_jsExecutionLock.Wait(0))
        {
            _logger.Debug("NavigateToHighlight acquired JS lock successfully");
            try
            {
                var script = $"window.cstSearchHighlights?.navigateToHit({hitIndex});";
                _cefBrowser.ExecuteJavaScript(script);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to navigate to highlight | {Details}", ex.Message);
            }
            finally
            {
                _logger.Debug("NavigateToHighlight releasing JS lock");
                _jsExecutionLock.Release();
                _logger.Debug("NavigateToHighlight released JS lock");
            }
        }
        else
        {
            _logger.Warning("NavigateToHighlight failed to acquire JS lock - retrying after delay - hitIndex: {HitIndex}", hitIndex);
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(100);
                NavigateToHighlight(hitIndex);
            }, DispatcherPriority.Background);
        }
    }

    // Public method to navigate to a specific anchor
    public void NavigateToAnchor(string anchor)
    {
        if (_cefBrowser == null || string.IsNullOrEmpty(anchor)) return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => NavigateToAnchor(anchor));
            return;
        }

        _logger.Information("NavigateToAnchor called | {Details}", anchor);
        _logger.Debug("NavigateToAnchor attempting to acquire JS lock | {Details}", anchor);
        if (_jsExecutionLock.Wait(0))
        {
            _logger.Debug("NavigateToAnchor acquired JS lock successfully | {Details}", anchor);
            try
            {
                var script = $@"
                (function() {{
                    try {{
                        var element = document.getElementById('{anchor}') || document.querySelector('a[name=""{anchor}""]');
                        if (element) {{
                            element.scrollIntoView({{ behavior: 'smooth', block: 'start' }});
                        }}
                    }} catch (error) {{ }}
                }})();";
                _cefBrowser.ExecuteJavaScript(script);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to navigate to anchor | {Details}", ex.Message);
            }
            finally
            {
                _logger.Debug("NavigateToAnchor releasing JS lock | {Details}", anchor);
                _jsExecutionLock.Release();
                _logger.Debug("NavigateToAnchor released JS lock | {Details}", anchor);
            }
        }
        else
        {
            _logger.Warning("NavigateToAnchor failed to acquire JS lock - retrying after delay | {Details}", anchor);
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(100);
                NavigateToAnchor(anchor);
            }, DispatcherPriority.Background);
        }
    }

    // Public method to toggle search highlighting visibility
    public void SetHighlightVisibility(bool visible)
    {
        if (_cefBrowser == null) return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SetHighlightVisibility(visible));
            return;
        }

        _logger.Debug("SetHighlightVisibility attempting to acquire JS lock");
        if (_jsExecutionLock.Wait(0))
        {
            _logger.Debug("SetHighlightVisibility acquired JS lock successfully");
            try
            {
                var script = $"window.cstSearchHighlights?.showHits({visible.ToString().ToLower()});";
                _cefBrowser.ExecuteJavaScript(script);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to set highlight visibility | {Details}", ex.Message);
            }
            finally
            {
                _logger.Debug("SetHighlightVisibility releasing JS lock");
                _jsExecutionLock.Release();
                _logger.Debug("SetHighlightVisibility released JS lock");
            }
        }
        else
        {
            _logger.Debug("SetHighlightVisibility failed to acquire JS lock - retrying after delay");
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(100);
                SetHighlightVisibility(visible);
            }, DispatcherPriority.Background);
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
        if (_cefBrowser == null || !_isBrowserInitialized || position <= 0) return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SetScrollPosition(position));
            return;
        }

        _logger.Debug("SetScrollPosition attempting to acquire JS lock");
        if (_jsExecutionLock.Wait(0))
        {
            _logger.Debug("SetScrollPosition acquired JS lock successfully");
            try
            {
                var script = $"window.scrollTo(0, {position});";
                _cefBrowser.ExecuteJavaScript(script);
                _lastScrollPosition = position;
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to set scroll position | {Details}", ex.Message);
            }
            finally
            {
                _logger.Debug("SetScrollPosition releasing JS lock");
                _jsExecutionLock.Release();
                _logger.Debug("SetScrollPosition released JS lock");
            }
        }
        else
        {
            _logger.Debug("SetScrollPosition failed to acquire JS lock - retrying after delay");
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(100);
                SetScrollPosition(position);
            }, DispatcherPriority.Background);
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
        if (_cefBrowser == null || !_isBrowserInitialized || string.IsNullOrEmpty(anchorName)) return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ScrollToPageAnchor(anchorName));
            return;
        }

        _logger.Debug("ScrollToPageAnchor attempting to acquire JS lock | {Details}", anchorName);
        if (_jsExecutionLock.Wait(0))
        {
            _logger.Debug("ScrollToPageAnchor acquired JS lock successfully | {Details}", anchorName);
            try
            {
                var script = $@"
                    (function() {{
                        var anchor = document.querySelector('a[name=""{anchorName}""]') || 
                                    document.querySelector('a[id=""{anchorName}""]') ||
                                    document.getElementById('{anchorName}');
                                    
                        if (anchor) {{
                            anchor.scrollIntoView({{ behavior: ""instant"", block: ""start"" }});
                        }} else {{
                            var allAnchors = Array.from(document.querySelectorAll(""a[name]""));
                            var paraAnchors = allAnchors.filter(a => a.name && a.name.startsWith(""para""));
                            
                            if ('{anchorName}'.startsWith(""para"")) {{
                                var targetText = '{anchorName}'.substring(4);
                                if (targetText.indexOf(""-"") !== -1) {{
                                    targetText = targetText.substring(0, targetText.indexOf(""-""));
                                }}
                                var targetNum = parseInt(targetText);
                                
                                var anchorNumbers = paraAnchors.map(function(anchor) {{
                                    var paraText = anchor.name.substring(4);
                                    if (paraText.indexOf(""-"") !== -1) {{
                                        paraText = paraText.substring(0, paraText.indexOf(""-""));
                                    }}
                                    var num = parseInt(paraText);
                                    return {{ anchor: anchor, number: num }};
                                }}).filter(function(item) {{
                                    return !isNaN(item.number);
                                }}).sort(function(a, b) {{
                                    return a.number - b.number;
                                }});
                                
                                if (anchorNumbers.length > 0) {{
                                    var closest = null;
                                    var closestDiff = Infinity;
                                    
                                    anchorNumbers.forEach(function(item) {{
                                        var diff = Math.abs(item.number - targetNum);
                                        if (diff < closestDiff) {{
                                            closestDiff = diff;
                                            closest = item;
                                        }}
                                    }});
                                    
                                    var maxAllowedDiff = anchorNumbers.length < 300 ? 100 : 50;
                                    
                                    if (closest && closestDiff <= maxAllowedDiff) {{
                                        closest.anchor.scrollIntoView({{ behavior: ""instant"", block: ""start"" }});
                                        return;
                                    }}
                                }}
                            }}
                        }}
                    }})();
                ";
                _cefBrowser.ExecuteJavaScript(script);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to scroll to anchor | {Details}", ex.Message);
            }
            finally
            {
                _logger.Debug("ScrollToPageAnchor releasing JS lock | {Details}", anchorName);
                _jsExecutionLock.Release();
                _logger.Debug("ScrollToPageAnchor released JS lock | {Details}", anchorName);
            }
        }
        else
        {
            _logger.Debug("ScrollToPageAnchor failed to acquire JS lock - retrying after delay | {Details}", anchorName);
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(100);
                ScrollToPageAnchor(anchorName);
            }, DispatcherPriority.Background);
        }
    }

    // Public method to toggle footnote visibility
    public void SetFootnoteVisibility(bool visible)
    {
        if (_cefBrowser == null) return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => SetFootnoteVisibility(visible));
            return;
        }

        _logger.Debug("SetFootnoteVisibility attempting to acquire JS lock");
        if (_jsExecutionLock.Wait(0))
        {
            _logger.Debug("SetFootnoteVisibility acquired JS lock successfully");
            try
            {
                var script = $"window.cstSearchHighlights?.showFootnotes({visible.ToString().ToLower()});";
                _cefBrowser.ExecuteJavaScript(script);
            }
            catch (Exception ex)
            {
                _logger.Error("Failed to set footnote visibility | {Details}", ex.Message);
            }
            finally
            {
                _logger.Debug("SetFootnoteVisibility releasing JS lock");
                _jsExecutionLock.Release();
                _logger.Debug("SetFootnoteVisibility released JS lock");
            }
        }
        else
        {
            _logger.Debug("SetFootnoteVisibility failed to acquire JS lock - retrying after delay");
            Dispatcher.UIThread.Post(async () =>
            {
                await Task.Delay(100);
                SetFootnoteVisibility(visible);
            }, DispatcherPriority.Background);
        }
    }

    /// <summary>
    /// Get current paragraph anchor asynchronously - port of CST4's GetPara() method with async/await pattern
    /// Returns the paragraph anchor at the top of the viewport (e.g., "para123")
    /// This method fixes the UI thread deadlock issue by using TaskCompletionSource
    /// </summary>
    public async Task<string?> GetCurrentParagraphAnchorAsync()
    {
        if (_cefBrowser == null || !_isBrowserInitialized)
        {
            _logger.Warning("GetCurrentParagraphAnchorAsync: Browser not available");
            return null;
        }

        _logger.Debug("GetCurrentParagraphAnchorAsync attempting to acquire JS lock");
        if (await _jsExecutionLock.WaitAsync(10))
        {
            _logger.Debug("GetCurrentParagraphAnchorAsync acquired JS lock successfully");
            try
            {

                _paraAnchorTcs?.TrySetCanceled();
                _paraAnchorTcs = new TaskCompletionSource<string?>();

                var script = @"
                (function() {
                    try {
                        var scrollY = window.pageYOffset || document.documentElement.scrollTop || 0;
                        var result = '';
                        if (window.cstAnchorCache && window.cstAnchorCache.getCurrentParagraph) {
                            var paraNum = window.cstAnchorCache.getCurrentParagraph(scrollY);
                            if (paraNum && paraNum !== '*') {
                                result = 'para' + paraNum;
                            }
                        }
                        document.title = 'CST_GET_PARA_RESULT:' + (result || 'null') + '|TAB:{_tabId}';
                    } catch (error) {
                        document.title = 'CST_GET_PARA_RESULT:error:' + error.message + '|TAB:{_tabId}';
                    }
                })();";

                // Replace tab ID placeholder with actual tab ID value
                script = script.Replace("{_tabId}", _tabId);

                _cefBrowser.ExecuteJavaScript(script);

                var timeoutTask = Task.Delay(TimeSpan.FromMilliseconds(1000));
                var completedTask = await Task.WhenAny(_paraAnchorTcs.Task, timeoutTask);

                if (completedTask == _paraAnchorTcs.Task)
                {
                    return await _paraAnchorTcs.Task;
                }
                else
                {
                    _paraAnchorTcs.TrySetCanceled();
                    return null;
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error getting current paragraph anchor | {Details}", ex.Message);
                _paraAnchorTcs?.TrySetException(ex);
                return null;
            }
            finally
            {
                _logger.Debug("GetCurrentParagraphAnchorAsync releasing JS lock");
                _jsExecutionLock.Release();
                _logger.Debug("GetCurrentParagraphAnchorAsync released JS lock");
            }
        }
        else
        {
            _logger.Debug("GetCurrentParagraphAnchorAsync failed to acquire JS lock - retrying after delay");
            await Task.Delay(100);
            return await GetCurrentParagraphAnchorAsync();
        }
    }


    private void SetupKeyboardHandling()
    {
        _logger.Debug("Setting up keyboard handling for copy functionality");
        this.KeyDown += OnKeyDown;
    }

    private async void OnKeyDown(object? sender, KeyEventArgs e)
    {
        // Handle Cmd+C (macOS) or Ctrl+C (Windows/Linux) to copy selected text
        if ((e.KeyModifiers.HasFlag(KeyModifiers.Meta) && RuntimeInformation.IsOSPlatform(OSPlatform.OSX)) ||
            (e.KeyModifiers.HasFlag(KeyModifiers.Control) && !RuntimeInformation.IsOSPlatform(OSPlatform.OSX)))
        {
            if (e.Key == Key.C)
            {
                _logger.Debug("Copy shortcut detected - attempting to copy selected text");
                await HandleCopySelectedText();
                e.Handled = true;
            }
        }
    }

    public async Task HandleCopyFromGlobalShortcut()
    {
        _logger.Debug("Global copy shortcut received - attempting to copy selected text");
        await HandleCopySelectedText();
    }

    // Alternative approach: Poll the JavaScript for selected text and provide copy functionality
    public async Task<string?> GetSelectedTextAsync()
    {
        if (_cefBrowser == null || !_isBrowserInitialized)
        {
            return null;
        }

        try
        {
            await _jsExecutionLock.WaitAsync();
            try
            {
                var getSelectedTextScript = @"
                    try {
                        var selectedText = window.getSelection().toString();
                        document.title = 'CST_SELECTED_TEXT:' + (selectedText ? selectedText.substring(0, 500) : 'NONE') + '|TAB:' + window.cstTabId;
                    } catch (err) {
                        document.title = 'CST_SELECTED_TEXT:ERROR:' + err.message + '|TAB:' + window.cstTabId;
                    }";

                _cefBrowser.ExecuteJavaScript(getSelectedTextScript);
                
                // Wait a moment for the result
                await Task.Delay(100);
                
                // The result will be processed in OnTitleChanged
                return null; // We'll handle this differently
            }
            finally
            {
                _jsExecutionLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Error in GetSelectedTextAsync | {Details}", ex.Message);
            return null;
        }
    }

    private async Task HandleCopySelectedText()
    {
        if (_cefBrowser == null || !_isBrowserInitialized)
        {
            _logger.Debug("Copy failed - browser not available");
            return;
        }

        try
        {
            // Use JavaScript to get selected text and copy to clipboard
            await _jsExecutionLock.WaitAsync();
            try
            {
                var copyScript = @"
                    try {
                        var selectedText = window.getSelection().toString();
                        if (selectedText && selectedText.length > 0) {
                            // Try to use the Clipboard API if available
                            if (navigator.clipboard && navigator.clipboard.writeText) {
                                navigator.clipboard.writeText(selectedText).then(function() {
                                    document.title = 'CST_COPY_SUCCESS:' + selectedText.length + '|TAB:' + window.cstTabId;
                                }).catch(function(err) {
                                    document.title = 'CST_COPY_FAILED:Clipboard API failed|TAB:' + window.cstTabId;
                                });
                            } else {
                                // Fallback: try to use execCommand
                                var textArea = document.createElement('textarea');
                                textArea.value = selectedText;
                                document.body.appendChild(textArea);
                                textArea.select();
                                try {
                                    var successful = document.execCommand('copy');
                                    document.body.removeChild(textArea);
                                    if (successful) {
                                        document.title = 'CST_COPY_SUCCESS:' + selectedText.length + '|TAB:' + window.cstTabId;
                                    } else {
                                        document.title = 'CST_COPY_FAILED:execCommand failed|TAB:' + window.cstTabId;
                                    }
                                } catch (err) {
                                    document.body.removeChild(textArea);
                                    document.title = 'CST_COPY_FAILED:' + err.message + '|TAB:' + window.cstTabId;
                                }
                            }
                        } else {
                            document.title = 'CST_COPY_FAILED:No text selected|TAB:' + window.cstTabId;
                        }
                    } catch (err) {
                        document.title = 'CST_COPY_FAILED:' + err.message + '|TAB:' + window.cstTabId;
                    }";

                _logger.Debug("Executing copy script");
                _cefBrowser.ExecuteJavaScript(copyScript);
            }
            finally
            {
                _jsExecutionLock.Release();
            }
        }
        catch (Exception ex)
        {
            _logger.Error("Error in HandleCopySelectedText | {Details}", ex.Message);
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        this.PropertyChanged -= OnIsVisibleChanged;
        this.KeyDown -= OnKeyDown;

        // Stop and dispose the timer to prevent resource leaks
        if (_scrollTimer != null)
        {
            _scrollTimer.Stop();
            _scrollTimer.Dispose();
            _scrollTimer = null;
            _logger.Information("Paused and disposed scroll tracking");
        }
    }
}