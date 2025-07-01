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
            // Subscribe to HTML content changes
            _viewModel.PropertyChanged += OnViewModelPropertyChanged;
            
            // Set up communication between ViewModel and View for navigation
            _viewModel.NavigateToHighlightRequested += NavigateToHighlight;
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        
        if (_viewModel != null)
        {
            _viewModel.PropertyChanged -= OnViewModelPropertyChanged;
            _viewModel.NavigateToHighlightRequested -= NavigateToHighlight;
        }
        
        if (_cefBrowser != null)
        {
            _cefBrowser.BrowserInitialized -= () => OnBrowserInitialized(null, EventArgs.Empty);
            _cefBrowser.LoadEnd -= OnLoadEnd;
            _cefBrowser.LoadError -= OnLoadError;
            _cefBrowser.TitleChanged -= OnTitleChanged;
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
        if (_viewModel != null)
        {
            Dispatcher.UIThread.Post(() => 
            {
                // Re-enable CefGlue for debugging white screen issue
                Console.WriteLine("Browser initialized - enabling CefGlue for debugging");
                _viewModel.SetCefGlueAvailability(true, "Browser initialized successfully");
                
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
        // Handle title changes if needed
        Console.WriteLine($"Page title changed: {title}");
    }


    private void SetupJavaScriptBridge()
    {
        if (_cefBrowser != null)
        {
            try
            {
                // Add JavaScript functions for search navigation
                var script = @"
                    window.cstSearchHighlights = {
                        hits: [],
                        currentIndex: 0,
                        
                        init: function() {
                            // Look for <hi rend='hit'> elements generated by our highlighting
                            this.hits = Array.from(document.querySelectorAll('hi[rend=""hit""]'));
                            this.updateHighlightStyles();
                            console.log('Found ' + this.hits.length + ' search highlights');
                        },
                        
                        navigateToHit: function(index) {
                            if (index < 1 || index > this.hits.length) return;
                            
                            this.currentIndex = index - 1;
                            var hit = this.hits[this.currentIndex];
                            if (hit) {
                                hit.scrollIntoView({ behavior: 'smooth', block: 'center' });
                                this.updateHighlightStyles();
                            }
                        },
                        
                        updateHighlightStyles: function() {
                            this.hits.forEach((hit, i) => {
                                if (i === this.currentIndex) {
                                    hit.style.backgroundColor = 'red';
                                    hit.style.color = 'white';
                                } else {
                                    hit.style.backgroundColor = 'yellow';
                                    hit.style.color = 'black';
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
                    
                    // Initialize when DOM is ready
                    if (document.readyState === 'complete') {
                        window.cstSearchHighlights.init();
                    } else {
                        document.addEventListener('DOMContentLoaded', function() {
                            window.cstSearchHighlights.init();
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
                var script = $"window.cstSearchHighlights?.navigateToHit({hitIndex});";
                _cefBrowser.ExecuteJavaScript(script);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Failed to navigate to highlight: {ex.Message}");
            }
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
}