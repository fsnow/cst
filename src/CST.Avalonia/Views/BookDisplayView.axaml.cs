using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reactive.Linq;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WebViewControl;
using CST.Avalonia.ViewModels;
using CST.Avalonia.Services;
using CST.Avalonia.Models;
using Serilog;

namespace CST.Avalonia.Views;

public partial class BookDisplayView : UserControl
{
    // Shared lock to serialize JavaScript execution across all instances
    private static readonly SemaphoreSlim _jsExecutionLock = new SemaphoreSlim(1, 1);

    // Logger instance with tab context
    private readonly ILogger _logger;

    private BookDisplayViewModel? _viewModel;
    private WebView? _webView;
    private bool _isShutDown;   // set once the tab is really closed; prevents WebView resurrection (BOOK-1)
    // Completes when OnTitleChanged receives the CST_LOOKUP_SEL selection pushed for a Cmd+D lookup. (#25)
    private TaskCompletionSource<string?>? _lookupSelectionTcs;
    private ScrollViewer? _fallbackBrowser;
    private IDisposable? _lifecycleSubscription; // Subscription to WebViewLifecycleOperation changes
    private int _lastScrollPosition = 0;
    private bool _isBrowserInitialized = false;
    private TaskCompletionSource<string?>? _paraAnchorTcs = null;
    // Completes when OnTitleChanged receives the CST_POSTOKEN raw bracket payload for a reading-position
    // capture (#434). Carries the raw "above,abovePos,below,belowPos,scrollTop" string; the fraction math is
    // computed C#-side by ReadingPositionMath so it stays unit-tested.
    private TaskCompletionSource<string?>? _posTokenTcs = null;
    private int _posTokenReq = 0; // monotonic capture request id; a late title with a stale id is ignored (#434)
    private readonly string _tabId = $"tab_{DateTime.Now.Ticks}_{Guid.NewGuid().ToString("N")[..8]}";
    private string? _tempHtmlFilePath;   // the temp HTML file this View last loaded from; deleted on dispose (BOOK-8)

    static BookDisplayView()
    {
        // One-time sweep of stale per-tab book HTML left in the temp dir by previous sessions/crashes
        // (each View wrote cst_book_*_<tabId>.html and nothing deleted them). Runs before the first
        // View — and thus before this session writes any — so it only removes leftovers. Best-effort. (BOOK-8)
        try
        {
            foreach (var stale in Directory.EnumerateFiles(Path.GetTempPath(), "cst_book_*.html"))
            {
                try { File.Delete(stale); } catch { /* in use / perms — skip */ }
            }
        }
        catch { /* temp dir unavailable — ignore */ }
    }

    // C# scroll tracking for reliable status bar updates
    private int _lastKnownScrollY = 0;
    private DateTime _lastScrollTime = DateTime.MinValue;
    private System.Timers.Timer? _scrollTimer;
    private string _lastKnownVri = "*";
    private string _lastKnownMyanmar = "*";
    private string _lastKnownPts = "*";
    private string _lastKnownThai = "*";
    private string _lastKnownOther = "*";
    private string _lastKnownPara = "*";

    // Cache the last successfully captured anchor for shutdown save

    // Timer-based drag monitoring fields
    private System.Timers.Timer? _dragMonitoringTimer;
    private DateTime _lastPointerPressedTime = DateTime.MinValue;
    private DateTime _webViewHiddenTime = DateTime.MinValue;
    private Point _lastPointerPressedPosition;
    private bool _isDragInProgress = false;
    private bool _isPointerPressed = false;
    private const double DRAG_THRESHOLD = 5.0; // pixels
    private const int DRAG_TIMER_INTERVAL = 50; // milliseconds
    private const int MIN_WEBVIEW_HIDE_DURATION = 500; // milliseconds - minimum time WebView stays hidden
    private const int DRAG_TIME_THRESHOLD = 150; // milliseconds - wait before treating pointer movement as drag (filters out tab clicks)

    // Window context tracking for CEF handle invalidation detection
    private Window? _currentWindow = null;

    public BookDisplayView()
    {
        InitializeComponent();

        // Get logger with tab context
        _logger = Log.ForContext<BookDisplayView>()
            .ForContext("TabId", _tabId);

        _fallbackBrowser = this.FindControl<ScrollViewer>("fallbackBrowser");

        // Make this UserControl focusable to receive keyboard events
        this.Focusable = true;

        // Add focus and keyboard event handlers at UserControl level
        this.GotFocus += (s, e) => _logger.Debug("FOCUS: BookDisplayView GotFocus. Source: {Source}", e.Source?.GetType().Name);
        this.LostFocus += (s, e) => _logger.Debug("FOCUS: BookDisplayView LostFocus. Source: {Source}", e.Source?.GetType().Name);
        
        // Add keyboard event handler with highest priority to intercept before WebView
        this.AddHandler(KeyDownEvent, OnKeyDown, RoutingStrategies.Tunnel, handledEventsToo: true);

        // Monitor visual tree attachment to detect window context changes (float/unfloat)
        this.AttachedToVisualTree += OnAttachedToVisualTree;
        this.DetachedFromVisualTree += OnDetachedFromVisualTree;

        // Try to create WebView browser
        TryCreateWebView();
    }

    private void OnKeyDown(object? sender, KeyEventArgs e)
    {
        _logger.Debug("KEYBOARD: BookDisplayView KeyDown. Key: {Key}, Modifiers: {Modifiers}, Source: {Source}", e.Key, e.KeyModifiers, e.Source?.GetType().Name);
        
        // Check for Cmd+G or Ctrl+G (Go To)
        if (e.Key == Key.G && (e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            _logger.Debug("*** GO TO SHORTCUT DETECTED IN BookDisplayView ***");
            e.Handled = true; // Prevent further processing
            _viewModel?.InvokeOpenGoToDialog();
            return;
        }

        // Check for Cmd+C or Ctrl+C
        if (e.Key == Key.C && (e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            _logger.Debug("*** COPY SHORTCUT DETECTED IN BookDisplayView ***");
            e.Handled = true; // Prevent further processing
            ExecuteCopy();
            return;
        }

        // Check for Cmd+A or Ctrl+A (Select All)
        if (e.Key == Key.A && (e.KeyModifiers.HasFlag(KeyModifiers.Meta) || e.KeyModifiers.HasFlag(KeyModifiers.Control)))
        {
            _logger.Debug("*** SELECT ALL SHORTCUT DETECTED IN BookDisplayView ***");
            e.Handled = true; // Prevent further processing
            if (_webView != null)
            {
                try
                {
                    _webView.EditCommands.SelectAll();
                    _logger.Debug("WebView SelectAll executed successfully");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error executing SelectAll");
                }
            }
            return;
        }

        // Check for Option+1 or Alt+1 (View Source - Burmese 1957)
        if (e.Key == Key.D1 && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            _logger.Debug("*** VIEW SOURCE 1957 SHORTCUT DETECTED IN BookDisplayView ***");
            e.Handled = true; // Prevent further processing
            _viewModel?.RequestShowSource(source2010: false);
            return;
        }

        // Check for Option+2 or Alt+2 (View Source - Burmese 2010)
        if (e.Key == Key.D2 && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            _logger.Debug("*** VIEW SOURCE 2010 SHORTCUT DETECTED IN BookDisplayView ***");
            e.Handled = true; // Prevent further processing
            _viewModel?.RequestShowSource(source2010: true);
            return;
        }
    }

    private void TryCreateWebView()
    {
        if (_isShutDown)
        {
            _logger.Debug("TryCreateWebView skipped: View has been shut down (closed tab)");
            return;
        }
        try
        {
            _webView = this.FindControl<WebView>("webView");
            if (_webView != null)
            {
                // Set up event handlers
                _webView.Navigated += OnNavigationCompleted;
                _webView.TitleChanged += OnTitleChanged;

                // Add diagnostic logging for focus on the WebView itself
                _webView.GotFocus += (s, e) => _logger.Debug("FOCUS: WebView GotFocus. Source: {Source}", e.Source?.GetType().Name);
                _webView.LostFocus += (s, e) => _logger.Debug("FOCUS: WebView LostFocus. Source: {Source}", e.Source?.GetType().Name);

                _logger.Debug("WebView control found and events attached successfully");
            }
            else
            {
                _logger.Error("Failed to find WebView control in the view");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize WebView");
            _webView = null;
        }
    }

    /// <summary>
    /// Returns the text the user has selected inside the book WebView (or null/empty if none). Used by
    /// the "Look Up in Dictionary" command (Cmd+D). Routes the selection back through the document.title
    /// channel (EvaluateScript returns null in this WebView build) and awaits the round-trip, so it never
    /// blocks the UI thread on CEF.
    /// </summary>
    public async Task<string?> GetWebViewSelectionAsync()
    {
        if (_webView == null || !_isBrowserInitialized)
            return null;
        try
        {
            // EvaluateScript returns null in this CEF binding, so push the selection out through the
            // document.title channel (same mechanism as CST_STATUS_UPDATE) and await the round-trip.
            var tcs = new TaskCompletionSource<string?>();
            _lookupSelectionTcs = tcs;

            // |SEQ makes a repeated identical response a *distinct* title so TitleChanged fires again —
            // without it, a second Cmd+D on the SAME selection wrote a byte-identical title, no event
            // fired, and the lookup silently ate its 700ms timeout. SEQ goes AFTER TAB (parsers read
            // TAB positionally/by scan and ignore trailing parts). (BOOK-4 / #156)
            var script = @"
                try {
                    var sel = window.getSelection ? window.getSelection().toString() : '';
                    document.title = 'CST_LOOKUP_SEL:' + encodeURIComponent(sel) + '|TAB:__TAB_ID_PLACEHOLDER__' + '|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                } catch (e) {
                    document.title = 'CST_LOOKUP_SEL:|TAB:__TAB_ID_PLACEHOLDER__' + '|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                }";
            script = script.Replace("__TAB_ID_PLACEHOLDER__", _tabId);
            _webView.ExecuteScript(script);

            var done = await Task.WhenAny(tcs.Task, Task.Delay(700));
            _lookupSelectionTcs = null;
            return done == tcs.Task ? await tcs.Task : null;
        }
        catch (Exception ex)
        {
            _logger.Information(ex, "GetSelectionForLookup failed");
            return null;
        }
    }

    private void DisposeWebView()
    {
        if (_webView != null)
        {
            try
            {
                _logger.Information("Disposing WebView to release CEF native handle");

                // Unsubscribe from events
                _webView.Navigated -= OnNavigationCompleted;
                _webView.TitleChanged -= OnTitleChanged;

                // Dispose the WebView to release native resources
                _webView.Dispose();

                _webView = null;
                _isBrowserInitialized = false;

                _logger.Information("WebView disposed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error while disposing WebView");
                _webView = null;
            }
        }

        // Delete this View's temp HTML file (a re-load re-creates it; on close it's gone for good). (BOOK-8)
        if (_tempHtmlFilePath != null)
        {
            try { File.Delete(_tempHtmlFilePath); } catch { /* already gone / locked — ignore */ }
            _tempHtmlFilePath = null;
        }
    }

    /// <summary>
    /// Permanently release this View's CEF WebView. The dock factory calls this only when the book tab
    /// is really closed (CloseDockable) — NOT on the recycled tab-switch/float detach paths — because a
    /// closed tab's View + its live browser would otherwise sit in the app-wide ControlRecycling cache
    /// (keyed per-open, never reused, never evicted) for the rest of the session, leaking a CEF browser
    /// and the multi-MB rendered DOM per open/close cycle. (BOOK-1)
    /// </summary>
    public void Shutdown()
    {
        _isShutDown = true;
        DisposeWebView();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        this.PropertyChanged += OnIsVisibleChanged;
        _logger.Information("BookDisplayView OnLoaded called");
        // Now attached and styled, so the counter's font is resolved — size its reserve. Covers a
        // recycled view whose ViewModel already has TotalHits set (no fresh PropertyChanged). (#196)
        UpdateHitCounterWidth();
        SetupCSharpScrollTracking();

        // Monitor drag operations to temporarily hide WebView for drop indicators
        _logger.Information("Calling SetupDragMonitoring from OnLoaded");
        SetupDragMonitoring();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // PHASE 2 LOGGING: Track lifecycle events to determine if tab reordering triggers detachment
        _logger.Information("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _logger.Information("▶▶▶ ATTACHED to visual tree - Book: {BookFile}, Instance: {InstanceId}",
            _viewModel?.Book?.FileName ?? "null", _viewModel?.Id ?? "null");

        // Get the new window this view is attached to
        var newWindow = this.GetVisualRoot() as Window;

        if (newWindow != null)
        {
            // Compare by reference equality, not by title
            // This ensures we detect actual window instance changes (float/unfloat)
            if (_currentWindow != null && !ReferenceEquals(_currentWindow, newWindow))
            {
                // Window changed! This happens during float/unfloat operations
                // CEF native handles are window-specific and become invalid
                _logger.Warning("*** ⚠️ WINDOW CONTEXT CHANGED - Disposing and recreating WebView ***");
                _logger.Warning("    Old window: {OldTitle} (Hash: {OldHash}), New window: {NewTitle} (Hash: {NewHash})",
                    _currentWindow.Title ?? "null", _currentWindow.GetHashCode(),
                    newWindow.Title ?? "null", newWindow.GetHashCode());
                _logger.Warning("    Book: {BookFile}, ViewModel: {ViewModelId}",
                    _viewModel?.Book?.FileName ?? "null", _viewModel?.Id ?? "null");

                // Dispose old WebView to release invalid CEF native handle
                DisposeWebView();

                // Update window reference
                _currentWindow = newWindow;

                // Recreate WebView with fresh native handle for new window
                TryCreateWebView();

                // Reload content if ViewModel has HTML
                if (_viewModel != null && !string.IsNullOrEmpty(_viewModel.HtmlContent))
                {
                    _logger.Information("Reloading HTML content after WebView recreation");
                    Dispatcher.UIThread.Post(() => LoadHtmlContent());
                }
            }
            else if (_currentWindow == null)
            {
                // First attachment - just track the window
                _currentWindow = newWindow;
                _logger.Information("*** 🆕 BookDisplayView attached to window for FIRST TIME ***");
                _logger.Information("    Window: {WindowTitle} (Hash: {Hash})",
                    newWindow.Title ?? "null", newWindow.GetHashCode());
                _logger.Information("    Book: {BookFile}, ViewModel: {ViewModelId}",
                    _viewModel?.Book?.FileName ?? "null", _viewModel?.Id ?? "null");

                // Execute queued restoration if the browser is already live (recycled tab —
                // no new navigation will fire); a fresh/recreated browser is handled by
                // OnNavigationCompleted instead. (BOOK-7)
                ExecutePendingRestoration();
                // A recycled tab fires no navigation, so nothing else would (re)build the anchor cache —
                // this is the restore path where a book sat at "*" until a mouse move. Guarded/idempotent:
                // if the live browser's JS cache is intact this no-ops; if a navigation DOES fire here,
                // OnNavigationCompleted's unconditional rebuild supersedes this. (#423)
                EnsureAnchorCacheBuilt();
            }
            else
            {
                // Same window instance - normal ControlRecycling show/hide (tab switching)
                _logger.Information("*** ✅ SAME WINDOW - ControlRecycling reattachment (tab switching) ***");
                _logger.Information("    Window: {WindowTitle} (Hash: {Hash})",
                    newWindow.Title ?? "null", newWindow.GetHashCode());
                _logger.Information("    Book: {BookFile}, ViewModel: {ViewModelId}",
                    _viewModel?.Book?.FileName ?? "null", _viewModel?.Id ?? "null");

                // ControlRecycling reattachment - page numbers remain in ViewModel properties
                // No need to trigger updates here, the View bindings will automatically
                // pick up the ViewModel's existing page number values
                _logger.Information("    Tab reattached - ViewModel page numbers: VRI={Vri}, Para={Para}",
                    _viewModel?.VriPage ?? "*", _viewModel?.CurrentParagraph ?? "*");

                // Execute queued restoration if the browser is already live (recycled tab —
                // no new navigation will fire); a fresh/recreated browser is handled by
                // OnNavigationCompleted instead. (BOOK-7)
                ExecutePendingRestoration();
                // A recycled tab fires no navigation, so nothing else would (re)build the anchor cache —
                // this is the restore path where a book sat at "*" until a mouse move. Guarded/idempotent:
                // if the live browser's JS cache is intact this no-ops; if a navigation DOES fire here,
                // OnNavigationCompleted's unconditional rebuild supersedes this. (#423)
                EnsureAnchorCacheBuilt();
            }
        }
        _logger.Information("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
    }

    private void OnDetachedFromVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // PHASE 2 LOGGING: Track detachment events to determine if tab reordering triggers detachment
        _logger.Information("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _logger.Information("◀◀◀ DETACHED from visual tree - Book: {BookFile}, Instance: {InstanceId}",
            _viewModel?.Book?.FileName ?? "null", _viewModel?.Id ?? "null");
        _logger.Information("    Window: {WindowTitle} (Hash: {Hash})",
            _currentWindow?.Title ?? "null", _currentWindow?.GetHashCode() ?? 0);

        // CRITICAL FIX: Clear _currentWindow so that when ControlRecycling reattaches this View,
        // OnAttachedToVisualTree will detect window context change and recreate WebView
        // This fixes the crash when: float → unfloat → switch tab → tab back
        _logger.Information("    Clearing _currentWindow to force window change detection on next attach");
        _currentWindow = null;

        _logger.Information("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
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
            _lifecycleSubscription?.Dispose();
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

            // Phase 4: Subscribe to WebViewLifecycleOperation changes for float/unfloat operations
            // Related: docs/research/BUTTON_BASED_FLOAT_APPROACH.md
            // IMPORTANT: Capture ViewModel in local variable so dispose has stable reference
            var vm = _viewModel;
            _lifecycleSubscription = System.Reactive.Linq.Observable
                .FromEventPattern<System.ComponentModel.PropertyChangedEventHandler, System.ComponentModel.PropertyChangedEventArgs>(
                    h => vm.PropertyChanged += h,
                    h => vm.PropertyChanged -= h)
                .Where(pattern => pattern.EventArgs.PropertyName == nameof(BookDisplayViewModel.WebViewLifecycleOperation))
                .Subscribe(_ => OnWebViewLifecycleOperationChanged());

            // If the ViewModel already has HTML content, load it immediately
            // This handles the case where the view is recreated but the ViewModel persists
            if (!string.IsNullOrEmpty(_viewModel.HtmlContent))
            {
                _logger.Debug("ViewModel already has HTML content ({Length} chars), loading immediately", _viewModel.HtmlContent.Length);
                Dispatcher.UIThread.Post(() => LoadHtmlContent());
            }
        }
    }

    /// <summary>
    /// Handle WebViewLifecycleOperation changes for float/unfloat operations
    /// Phase 4: Manual WebView disposal and recreation to prevent CEF crash
    /// Related: docs/research/BUTTON_BASED_FLOAT_APPROACH.md
    /// </summary>
    private void OnWebViewLifecycleOperationChanged()
    {
        if (_viewModel == null) return;

        var operation = _viewModel.WebViewLifecycleOperation;
        _logger.Information("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _logger.Information("WebViewLifecycleOperation changed: {Operation}", operation);

        switch (operation)
        {
            case WebViewLifecycleOperation.PrepareForFloat:
            case WebViewLifecycleOperation.PrepareForUnfloat:
                _logger.Warning("*** DISPOSING WebView before window operation ***");
                DisposeWebView();
                _logger.Information("WebView disposed, ready for window operation");
                break;

            case WebViewLifecycleOperation.RestoreAfterFloat:
            case WebViewLifecycleOperation.RestoreAfterUnfloat:
                _logger.Warning("*** RECREATING WebView after window operation ***");
                TryCreateWebView();

                // Reload HTML content if available
                if (!string.IsNullOrEmpty(_viewModel.HtmlContent))
                {
                    _logger.Information("Reloading HTML content ({Length} chars) after WebView recreation",
                        _viewModel.HtmlContent.Length);
                    Dispatcher.UIThread.Post(() => LoadHtmlContent());
                }
                _logger.Information("WebView recreated and content reloaded");
                break;

            case WebViewLifecycleOperation.None:
            default:
                // No action needed
                break;
        }
        _logger.Information("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
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
                // Becoming visible wakes an occluded renderer — dispatch the build if it hasn't happened
                // yet (a restored/background tab whose navigation fired while hidden). Guarded/idempotent:
                // no-ops when the cache is already built or a build is in flight. (#423)
                EnsureAnchorCacheBuilt();
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
        else if (e.PropertyName == nameof(BookDisplayViewModel.TotalHits) ||
                 e.PropertyName == nameof(BookDisplayViewModel.HasSearchHighlights))
        {
            UpdateHitCounterWidth();
        }
        // #224: user toggled a per-book View control — apply it to the live WebView.
        else if (e.PropertyName == nameof(BookDisplayViewModel.ShowFootnotes))
        {
            if (sender is BookDisplayViewModel vm) ApplyFootnotesVisibility(vm.ShowFootnotes);
        }
        else if (e.PropertyName == nameof(BookDisplayViewModel.ShowSearchTerms))
        {
            if (sender is BookDisplayViewModel vm) ApplySearchTermsVisibility(vm.ShowSearchTerms);
        }
    }

    // Reserve a fixed width for the search hit counter equal to the widest string this search can
    // produce ("{total} of {total}"), MEASURED in the counter's own font — so its width never changes
    // as the current index gains digits during navigation. A changing width reflows the toolbar
    // WrapPanel to a second row, which shrinks the WebView viewport and can scroll the just-navigated
    // hit out of view. Measuring (not estimating px) is what makes this robust at a tuned window
    // width where a few stray pixels would tip it into a wrap. (#196)
    private void UpdateHitCounterWidth()
    {
        var counter = HitCounterText;
        if (counter == null) return;

        var total = _viewModel?.TotalHits ?? 0;
        if (total <= 0)
        {
            counter.MinWidth = 0;
            return;
        }

        var fontSize = double.IsNaN(counter.FontSize) || counter.FontSize <= 0 ? 14.0 : counter.FontSize;
        var typeface = new global::Avalonia.Media.Typeface(counter.FontFamily, counter.FontStyle, counter.FontWeight);

        double Measure(string s) => new global::Avalonia.Media.FormattedText(
            s, System.Globalization.CultureInfo.CurrentCulture,
            global::Avalonia.Media.FlowDirection.LeftToRight, typeface, fontSize,
            global::Avalonia.Media.Brushes.Black).Width;

        // Widest string is "{total} of {total}"; pad by one digit's width so a different-digit current
        // index of the same length (e.g. "19 of 20" vs "20 of 20") can't render a hair wider than the
        // reserve. Round up so we never under-reserve by a sub-pixel.
        counter.MinWidth = Math.Ceiling(Measure($"{total} of {total}") + Measure("0"));
    }

    private void LoadHtmlContent()
    {
        _logger.Debug("Method called - ViewModel: {HasViewModel}, HtmlContent empty: {IsHtmlEmpty}", _viewModel != null, string.IsNullOrEmpty(_viewModel?.HtmlContent));

        if (_viewModel == null || string.IsNullOrEmpty(_viewModel.HtmlContent))
        {
            _logger.Debug("Exiting - no viewmodel or content");
            return;
        }

        // Ensure we're on the UI thread for WebView operations
        if (!Dispatcher.UIThread.CheckAccess())
        {
            _logger.Debug("Dispatching to UI thread");
            Dispatcher.UIThread.Post(LoadHtmlContent);
            return;
        }

        try
        {
            _logger.Debug("WebView status - available: {IsWebViewAvailable}, Browser: {HasBrowser}", _viewModel.IsWebViewAvailable, _webView != null);

            if (_viewModel.IsWebViewAvailable && _webView != null)
            {
                try
                {
                    // Check content size and use appropriate loading method
                    _logger.Debug("Loading HTML content - length: {Length}", _viewModel.HtmlContent.Length);
                    //_logger.Debug("LoadHtmlContent", "HTML content preview", _viewModel.HtmlContent.Substring(0, Math.Min(200, _viewModel.HtmlContent.Length)) + "...");

                    // CRITICAL FIX: Invalidate anchor cache when loading new content
                    // This prevents scroll timer from querying stale cache and overwriting
                    // ViewModel's page numbers with "*" values during tab switches
                    _anchorCacheBuilt = false;
                    _anchorCacheBuildInFlight = false;   // new content ⇒ allow a fresh build to be dispatched (#423)
                    _logger.Debug("Invalidated anchor cache - will rebuild after navigation completes");

                    // Write HTML content to temporary file and load it
                    // This completely bypasses data URI size limitations
                    var tempFileName = $"cst_book_{_viewModel.Book.FileName.Replace('.', '_')}_{_tabId}.html";
                    var tempFilePath = Path.Combine(Path.GetTempPath(), tempFileName);

                    _logger.Debug("Writing to temp file | {Details}", tempFilePath);
                    File.WriteAllText(tempFilePath, _viewModel.HtmlContent, System.Text.Encoding.UTF8);
                    _tempHtmlFilePath = tempFilePath;   // remember it so DisposeWebView can delete it (BOOK-8)

                    // Uri.AbsoluteUri, not string concat: Windows backslashes and spaces in the temp
                    // path would otherwise malform the URL (same defect as NET-5 / #162).
                    var fileUrl = new Uri(tempFilePath).AbsoluteUri;
                    _logger.Debug("Loading from file URL | {Details}", fileUrl);

                    _webView.LoadUrl(fileUrl);
                    _viewModel.PageStatusText = "Loading content from file...";
                    _logger.Debug("HTML content loaded from temporary file");
                }
                catch (Exception ex)
                {
                    _logger.Error("Failed to load HTML content | {Details}", ex.Message);
                    _viewModel.SetWebViewAvailability(false, "Failed to load content - using fallback");
                }
            }
            else if (_webView == null)
            {
                // Browser creation failed, disable WebView
                _logger.Warning("Browser is null - setting WebView unavailable");
                _viewModel.SetWebViewAvailability(false, "WebView browser unavailable - using fallback text display");
            }
            else
            {
                _logger.Warning("WebView not available - using fallback");
            }
            // Fallback is already handled by data binding in XAML
        }
        catch (Exception ex)
        {
            // If WebView fails, mark it as unavailable and fall back to text display
            _logger.Error("Exception occurred | {Details}", ex.Message);
            _viewModel?.SetWebViewAvailability(false, $"WebView error, using fallback: {ex.Message}");
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
        if (_webView == null || !_isBrowserInitialized || _viewModel == null)
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

            if (!_anchorCacheBuilt || _webView == null)
            {
                _logger.Debug("UpdateScrollBasedStatus skipped - anchorCacheBuilt: {AnchorCacheBuilt}, browser: {HasBrowser}", _anchorCacheBuilt, _webView != null);
                return;
            }

            // Try to get scroll position and status in a single JavaScript call
            _logger.Debug("Executing JavaScript for status update");
            var script = $@"
                (function() {{
                try {{
                    var scrollY = window.pageYOffset || document.documentElement.scrollTop || 0;

                    // Gate on a POPULATED cache: if it is missing (a reload wiped the JS context) or
                    // defined-but-not-yet-built (build still deferred behind a paint), emit NOTHING —
                    // pushing an all-'*' readout in that transient state is exactly what clobbered good
                    // page numbers in the reverted #429 (#432 constraint). The C#-side _anchorCacheBuilt
                    // guard is the first line of defense; this catches the stale-flag case. The next
                    // 200ms scroll tick simply retries. (#423)
                    if (!window.cstAnchorCache || !window.cstAnchorCache.isBuilt) {{ return; }}

                    var vri = '*', myanmar = '*', pts = '*', thai = '*', other = '*', para = '*';
                    
                    // Try to get page references
                    try {{
                        if (window.cstAnchorCache && window.cstAnchorCache.getPageReferences) {{
                            var refs = window.cstAnchorCache.getPageReferences(scrollY);
                            vri = refs.vri || '*';
                            myanmar = refs.myanmar || '*';
                            pts = refs.pts || '*';
                            thai = refs.thai || '*';
                            other = refs.other || '*';
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
                            // Look for chapters within viewport (scrollY to scrollY+200px)
                            var searchStart = scrollY;
                            var searchEnd = scrollY + 200;
                            var bestChapter = null;
                            var bestDistance = Infinity;

                            // First, look for chapters within the viewport
                            for (var i = 0; i < window.cstAnchorCache.sortedChapterAnchors.length; i++) {{
                                var chapterAnchor = window.cstAnchorCache.sortedChapterAnchors[i];
                                if (chapterAnchor.position >= searchStart && chapterAnchor.position <= searchEnd) {{
                                    var distance = Math.abs(chapterAnchor.position - scrollY);
                                    if (distance < bestDistance) {{
                                        bestDistance = distance;
                                        bestChapter = chapterAnchor;
                                    }}
                                }} else if (chapterAnchor.position > searchEnd) {{
                                    break; // Past viewport, stop searching
                                }}
                            }}

                            // If no chapter within viewport, fall back to closest chapter BEFORE scroll position
                            if (!bestChapter) {{
                                for (var i = window.cstAnchorCache.sortedChapterAnchors.length - 1; i >= 0; i--) {{
                                    var chapterAnchor = window.cstAnchorCache.sortedChapterAnchors[i];
                                    if (chapterAnchor.position <= scrollY) {{
                                        bestChapter = chapterAnchor;
                                        break;
                                    }}
                                }}
                            }}

                            // If still no chapter found (e.g., at very top), use the first chapter
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

                    // Get the best anchor for scroll position restoration (paragraph, chapter, or page)
                    var bestAnchor = '*';
                    try {{
                        if (window.cstAnchorCache && window.cstAnchorCache.getCurrentAnchor) {{
                            var anchor = window.cstAnchorCache.getCurrentAnchor(scrollY);
                            if (anchor && anchor !== 'null') {{
                                bestAnchor = anchor;
                            }}
                        }}
                    }} catch(anchorError) {{
                    }}

                    // ATOMIC UPDATE: Send all status info in one message with tab ID including chapter and best anchor
                    document.title = 'CST_STATUS_UPDATE:VRI=' + vri + '|MYANMAR=' + myanmar + '|PTS=' + pts + '|THAI=' + thai + '|OTHER=' + other + '|PARA=' + para + '|CHAPTER=' + currentChapter + '|ANCHOR=' + bestAnchor + '|SCROLL=' + scrollY + '|TAB:__TAB_ID_PLACEHOLDER__';
                }} catch(e) {{
                    // Emit nothing on error — an all-'*' title would clobber a good readout (#432
                    // constraint). The next scroll tick retries. (#423)
                }}
                }})();
            ";

            // Replace tab ID placeholder with actual tab ID value
            script = script.Replace("__TAB_ID_PLACEHOLDER__", _tabId);
            
            _webView.ExecuteScript(script);
        }
        catch (Exception ex)
        {
            _logger.Error("Error updating scroll-based status | {Details}", ex.Message);
        }
    }

    private bool _anchorCacheBuilt = false;
    // True from the moment a build is dispatched until CACHE_BUILT confirms it (or a reset releases it).
    // Makes the build idempotent now that several events can trigger it (navigation, visibility, reattach)
    // so overlapping triggers don't inject the script 2-3x and race on window.cstAnchorCache / the shared
    // document.title channel. (#423)
    private bool _anchorCacheBuildInFlight = false;

    private const int AnchorCacheBuildDeadlineMs = 2000;

    // UNCONDITIONAL rebuild for navigation/reload. Any navigation means a fresh JS context —
    // window.cstAnchorCache is gone no matter what _anchorCacheBuilt says, so reset both guards and
    // dispatch. The reverted #429 attempt called the GUARDED path here; when Navigated fired without
    // routing through LoadHtmlContent (tab switch away/back), the stale flag skipped the rebuild and
    // the page readout stuck at "*" (the #432 regressions). The pre-#429 code also rebuilt on every
    // navigation — this preserves that contract, minus the fixed 2s delay. (#423, #432)
    private void RebuildAnchorCacheAfterNavigation()
    {
        _anchorCacheBuilt = false;
        _anchorCacheBuildInFlight = false;
        EnsureAnchorCacheBuilt();
    }

    // GUARDED, idempotent entry point: no-ops when the cache is already built or a build is in flight.
    // For triggers where the JS context may still hold a valid cache — the view becoming visible, a
    // recycled tab reattaching with a live browser (the restore path that previously had NOTHING
    // trigger a build, leaving the page readout at "*" until a mouse move woke the renderer). NEVER
    // use this for navigation — that must go through RebuildAnchorCacheAfterNavigation. The build
    // script defers position reads behind a paint (see BuildAnchorPositionCache), so on an occluded
    // background tab it simply parks and completes the moment the tab is next shown. (#423)
    private void EnsureAnchorCacheBuilt()
    {
        if (_webView == null || !_isBrowserInitialized) return;
        if (_anchorCacheBuilt || _anchorCacheBuildInFlight) return;

        _anchorCacheBuildInFlight = true;
        _logger.Debug("EnsureAnchorCacheBuilt: dispatching anchor cache build");
        Task.Run(BuildAnchorPositionCache);
        StartAnchorCacheBuildWatchdog();
    }

    // Safety net: _anchorCacheBuilt flips true ONLY when the JS CACHE_BUILT title round-trips, which is
    // gated on a paint. The happy path completes in tens of ms. But if the renderer never paints this
    // build (stuck occluded tab, JS fault before build() emits the title), _anchorCacheBuildInFlight
    // would stay true forever and suppress every retrigger. After a deadline with no CACHE_BUILT,
    // release the guard and retry while visible; a hidden tab just waits for its next show to
    // re-trigger via OnIsVisibleChanged. (#423)
    private void StartAnchorCacheBuildWatchdog()
    {
        Task.Run(async () =>
        {
            await Task.Delay(AnchorCacheBuildDeadlineMs);
            if (_anchorCacheBuilt || !_anchorCacheBuildInFlight) return; // completed, or already released
            _logger.Debug("Anchor cache build watchdog fired: no CACHE_BUILT within deadline; releasing guard");
            _anchorCacheBuildInFlight = false;
            // Marshal to the UI thread for the IsVisible / WebView reads.
            Dispatcher.UIThread.Post(() =>
            {
                if (this.IsVisible) EnsureAnchorCacheBuilt();
            });
        });
    }

    private async Task BuildAnchorPositionCache()
    {
        if (_webView == null) return;

        _logger.Debug("BuildAnchorPositionCache attempting to acquire JS lock");
        if (await _jsExecutionLock.WaitAsync(10))
        {
            _logger.Debug("BuildAnchorPositionCache acquired JS lock successfully");
            try
            {
                _logger.Debug("Building anchor position cache");

                // Store anchor positions directly in JavaScript  
                var script = $@"
                (function() {{
                    // Store anchor positions in the window object for C# queries
                    window.cstAnchorCache = {{
                        pageAnchors: {{}},
                        paragraphAnchors: {{}},
                        chapterAnchors: {{}},
                        // Add properties to hold the pre-sorted lists for performance
                        sortedPageAnchors: {{ V: [], M: [], P: [], T: [], O: [] }},
                        sortedParagraphAnchors: [],
                        sortedChapterAnchors: [],
                        sortedAllAnchors: [],   // merged page+para+chapter, position-sorted (#434 token bracket lookup)
                        // False until build() has actually populated the anchors. The status-update
                        // script gates on this so a defined-but-empty cache (script injected, build
                        // still deferred behind a paint) can never emit an all-'*' readout that
                        // clobbers good page numbers in the ViewModel — the #432 regression. (#423)
                        isBuilt: false,

                        build: function() {{
                            this.pageAnchors = {{}};
                            this.paragraphAnchors = {{}};
                            this.chapterAnchors = {{}};
                            this.sortedPageAnchors = {{ V: [], M: [], P: [], T: [], O: [] }};
                            this.sortedParagraphAnchors = [];
                            this.sortedChapterAnchors = [];
                            this.sortedAllAnchors = [];

                            // Force a full synchronous layout so getBoundingClientRect() returns correct
                            // absolute values. One reflow computes layout for the whole document — the old
                            // O(N) loop over every element was redundant. (#423)
                            void document.body.offsetHeight;

                            // Collect page anchors with the CORRECT position calculation.
                            ['V', 'M', 'P', 'T', 'O'].forEach(function(prefix) {{
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
                                if (anchor.name && anchor.name.match(/^[a-z]+\d+(_\d+)*$/) &&
                                    !anchor.name.startsWith('para') && 
                                    !anchor.name.match(/^[VMPTO]/)) {{
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

                            // Merged, position-sorted list of ALL anchor types (page V/M/P/T/O + paragraph +
                            // chapter) so the reading-position token (#434) can find the anchors bracketing the
                            // viewport top in one lookup. The A->B gap across all types is typically well under a
                            // screenful, so interpolating between them is a faithful proxy for the reading
                            // position even across reflow/script change. Names only need to be UNIQUE and STABLE
                            // across a script change, which they are (derived from the source XML).
                            this.sortedAllAnchors = [];
                            for (var pn in this.pageAnchors) this.sortedAllAnchors.push({{ name: pn, position: this.pageAnchors[pn] }});
                            for (var qn in this.paragraphAnchors) this.sortedAllAnchors.push({{ name: qn, position: this.paragraphAnchors[qn] }});
                            for (var cn in this.chapterAnchors) this.sortedAllAnchors.push({{ name: cn, position: this.chapterAnchors[cn] }});
                            this.sortedAllAnchors.sort(function(a, b) {{ return a.position - b.position; }});

                            // Populated — status queries may now trust this cache. Set BEFORE the title
                            // signal so the C# side can never observe CACHE_BUILT ahead of the data. (#423)
                            this.isBuilt = true;

                            // |SEQ makes a rebuild with identical counts a *distinct* title so TitleChanged
                            // fires again (the #156 identical-title-fires-no-event hazard); parsers scan
                            // parts for their own prefixes, so the extra part is ignored. (#423)
                            document.title = 'CST_STATUS_UPDATE:CACHE_BUILT=' + Object.keys(this.pageAnchors).length + ',' + Object.keys(this.paragraphAnchors).length + ',' + Object.keys(this.chapterAnchors).length + '|TAB:__TAB_ID_PLACEHOLDER__' + '|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                        }},
                        
                        getPageReferences: function(scrollY) {{
                            var result = {{ vri: '*', myanmar: '*', pts: '*', thai: '*', other: '*' }};
                            var docPos = scrollY + 20; // CST4 algorithm offset

                            // PERFORMANCE OPTIMIZATION: Use pre-sorted lists instead of expensive sorting on every call
                            // The findBestAnchor function now works on the pre-sorted lists
                            // Two cases:
                            // 1. Normal/scrolled: last anchor at or before the current position
                            // 2. Above the FIRST marker (book title / namo tassa / a chapter heading that
                            //    precedes the first page marker): resolve to the first anchor in the book.
                            //    This fallback was previously gated on scrollY < 100, but a chapter-list
                            //    jump can land the scroll above the first markers yet past 100px, which
                            //    returned '*' — the #423 case-2 defect. Anywhere above the first marker,
                            //    the first page IS the current page, so no threshold. (#423)
                            function findBestAnchor(sortedAnchors) {{
                                if (!sortedAnchors || sortedAnchors.length === 0) {{
                                    return null;
                                }}

                                // Case 1: Linear search for anchor at or before current position
                                var bestAnchor = null;
                                for (var i = 0; i < sortedAnchors.length; i++) {{
                                    if (sortedAnchors[i].position <= docPos) {{
                                        bestAnchor = sortedAnchors[i];
                                    }} else {{
                                        // Since the list is sorted, we can stop here
                                        break;
                                    }}
                                }}

                                // Case 2: No anchor at-or-before ⇒ we are above the first marker — return it
                                if (!bestAnchor) {{
                                    bestAnchor = sortedAnchors[0];
                                }}

                                return bestAnchor;
                            }}

                            // Find best anchor for each type using the pre-sorted lists
                            var vriAnchor = findBestAnchor(this.sortedPageAnchors.V);
                            var myanmarAnchor = findBestAnchor(this.sortedPageAnchors.M);
                            var ptsAnchor = findBestAnchor(this.sortedPageAnchors.P);
                            var thaiAnchor = findBestAnchor(this.sortedPageAnchors.T);
                            var otherAnchor = findBestAnchor(this.sortedPageAnchors.O);

                            result.vri = vriAnchor ? vriAnchor.name : '*';
                            result.myanmar = myanmarAnchor ? myanmarAnchor.name : '*';
                            result.pts = ptsAnchor ? ptsAnchor.name : '*';
                            result.thai = thaiAnchor ? thaiAnchor.name : '*';
                            result.other = otherAnchor ? otherAnchor.name : '*';

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

                            // Above the first paragraph marker (front matter / a heading that precedes it):
                            // resolve to the FIRST paragraph instead of '*' — same above-first-marker gap as
                            // getPageReferences, no scroll threshold. (#423)
                            if (!bestPara) {{
                                bestPara = this.sortedParagraphAnchors[0];
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
                        }},

                        getCurrentAnchor: function(scrollY) {{
                            // Find the best anchor of ANY type (paragraph, chapter, or page) within viewport
                            // Allow anchors slightly below scroll position (within top 200px of viewport)
                            var searchStart = scrollY;
                            var searchEnd = scrollY + 200; // Look within first 200px of viewport

                            var bestAnchor = null;
                            var bestDistance = Infinity;

                            // Check paragraph anchors
                            for (var i = 0; i < this.sortedParagraphAnchors.length; i++) {{
                                var anchor = this.sortedParagraphAnchors[i];
                                if (anchor.position >= searchStart && anchor.position <= searchEnd) {{
                                    var distance = Math.abs(anchor.position - scrollY);
                                    if (distance < bestDistance) {{
                                        bestDistance = distance;
                                        bestAnchor = anchor.name;
                                    }}
                                }} else if (anchor.position > searchEnd) {{
                                    break; // List is sorted, no need to continue
                                }}
                            }}

                            // Check chapter anchors
                            for (var i = 0; i < this.sortedChapterAnchors.length; i++) {{
                                var anchor = this.sortedChapterAnchors[i];
                                if (anchor.position >= searchStart && anchor.position <= searchEnd) {{
                                    var distance = Math.abs(anchor.position - scrollY);
                                    if (distance < bestDistance) {{
                                        bestDistance = distance;
                                        bestAnchor = anchor.name;
                                    }}
                                }} else if (anchor.position > searchEnd) {{
                                    break;
                                }}
                            }}

                            // If we found an anchor within the viewport, return it
                            if (bestAnchor) {{
                                return bestAnchor;
                            }}

                            // Otherwise, fall back to closest anchor BEFORE scroll position
                            // Check all sorted lists and find the closest one
                            var candidates = [];

                            // Last paragraph before scroll position
                            for (var i = this.sortedParagraphAnchors.length - 1; i >= 0; i--) {{
                                if (this.sortedParagraphAnchors[i].position <= scrollY) {{
                                    candidates.push(this.sortedParagraphAnchors[i]);
                                    break;
                                }}
                            }}

                            // Last chapter before scroll position
                            for (var i = this.sortedChapterAnchors.length - 1; i >= 0; i--) {{
                                if (this.sortedChapterAnchors[i].position <= scrollY) {{
                                    candidates.push(this.sortedChapterAnchors[i]);
                                    break;
                                }}
                            }}

                            // Find the closest candidate
                            if (candidates.length > 0) {{
                                var closest = candidates[0];
                                for (var i = 1; i < candidates.length; i++) {{
                                    if (candidates[i].position > closest.position) {{
                                        closest = candidates[i];
                                    }}
                                }}
                                return closest.name;
                            }}

                            // Last resort: return 'top' if we're near the beginning
                            if (scrollY < 100) {{
                                return 'top';
                            }}

                            return null;
                        }}
                    }};

                    // Build the cache once layout has SETTLED and the renderer has PAINTED, not after a
                    // fixed delay. Waiting on fonts.ready avoids reading positions mid-shaping (complex
                    // scripts reflow after first paint — reading rects too early is the #37 failure
                    // mode); the double requestAnimationFrame reads getBoundingClientRect only after a
                    // real paint frame — which also means that on a background/occluded tab this simply
                    // PARKS and fires the moment the tab is next shown, instead of stalling until a
                    // mouse move. build() emits the CACHE_BUILT title, the authoritative 'cache ready'
                    // signal the C# side waits on; until it runs, isBuilt stays false and status
                    // queries emit nothing. (#423)
                    var cstBuildWhenReady = function() {{
                        requestAnimationFrame(function() {{
                            requestAnimationFrame(function() {{ window.cstAnchorCache.build(); }});
                        }});
                    }};
                    if (document.fonts && document.fonts.ready && typeof document.fonts.ready.then === 'function') {{
                        document.fonts.ready.then(cstBuildWhenReady);
                    }} else {{
                        cstBuildWhenReady();
                    }}

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

                _webView.ExecuteScript(script);

                // Do NOT set _anchorCacheBuilt here or wait a fixed delay — the build is deferred in JS
                // (fonts.ready + double-rAF) and completes on the next paint. _anchorCacheBuilt is set
                // only when the script's CACHE_BUILT title arrives in OnTitleChanged — i.e. only after
                // the cache is actually populated (the #432 constraint). (#423)
                _logger.Debug("Anchor position cache build script injected; awaiting CACHE_BUILT");
            }
            catch (Exception ex)
            {
                // The script never posted → no CACHE_BUILT will arrive, so release the in-flight guard
                // to let a later trigger (visibility / reattach / navigation) retry. (#423)
                _anchorCacheBuildInFlight = false;
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

    private void OnNavigationCompleted(string url, string frameName)
    {
        if (_viewModel != null)
        {
            Dispatcher.UIThread.Post(() =>
            {
                _logger.Debug("Navigation completed successfully");
                _viewModel.PageStatusText = "Document loaded successfully";

                // Mark browser as initialized for scroll tracking
                _isBrowserInitialized = true;

                // Signal the ViewModel that initialization is complete and navigation can be enabled
                _viewModel.CompleteInitialization();

                // Make sure this UserControl can receive keyboard focus
                this.Focusable = true;
                // Focus the UserControl for keyboard shortcuts
                this.Focus();
                _logger.Debug("BookDisplayView focused for keyboard shortcuts");
                
                // Set up JavaScript bridge after content loads
                SetupJavaScriptBridge();

                // Build the anchor position cache — UNCONDITIONALLY, because this navigation just gave
                // the page a fresh JS context (any previous window.cstAnchorCache is gone, whatever
                // _anchorCacheBuilt claims). No fixed "let it settle" delay: the build itself waits for
                // fonts.ready + a paint frame before reading positions, so this fires immediately and
                // the heavy lifting is gated on real readiness, not a 2s guess. (#423, #432)
                _logger.Debug("Navigation completed - dispatching unconditional anchor cache rebuild");
                RebuildAnchorCacheAfterNavigation();

                // The document is ready — execute any queued restoration (saved anchor or saved
                // search hit) NOW, from the one signal that knows the DOM exists. (BOOK-7)
                ExecutePendingRestoration();
            });
        }
    }

    // Mark a hit as the CURRENT one (red styling) without scrolling to it. Used when a reload's
    // scroll position is owned by a saved anchor but the JS highlight state (which hit is current)
    // was reset by the reload. Mirrors NavigateToHighlight's JS-lock discipline. (BOOK-7)
    private void SyncCurrentHitStyle(int hitIndex)
    {
        if (_webView == null || !_isBrowserInitialized || hitIndex < 1)
            return;

        if (_jsExecutionLock.Wait(0))
        {
            try
            {
                // If highlights aren't collected yet (document still initializing), queue the intent
                // for init() to apply — never silently lost. (BOOK-7)
                var script = "if (window.cstSearchHighlights && window.cstSearchHighlights.hits.length > 0) { " +
                             $"window.cstSearchHighlights.currentIndex = Math.min({hitIndex}, window.cstSearchHighlights.hits.length) - 1; " +
                             "window.cstSearchHighlights.updateHighlightStyles(); " +
                             "} else { " +
                             $"window.__cstPendingHit = {{ index: {hitIndex}, scroll: false }}; " +
                             "}";
                _webView.ExecuteScript(script);
                _logger.Debug("Synced current-hit styling to hit {HitIndex} (no scroll)", hitIndex);
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error syncing current-hit styling");
            }
            finally
            {
                _jsExecutionLock.Release();
            }
        }
        else
        {
            // Lock busy (e.g. the anchor scroll is executing) - retry shortly.
            Task.Run(async () =>
            {
                await Task.Delay(100);
                await Dispatcher.UIThread.InvokeAsync(() => SyncCurrentHitStyle(hitIndex));
            });
        }
    }

    // Execute any queued restoration (saved anchor / saved search hit) now that the document is
    // actually ready. Called from OnNavigationCompleted (fresh load and reloads, e.g. script change)
    // and from the attach handler when a recycled tab reattaches with a live browser. Precedence:
    // saved hit > saved anchor > re-anchor to the current hit after a reload of a search book —
    // mirroring InitializeAsync's #36 preference for the exact hit over its paragraph anchor.
    // Replaces three racing fixed-delay attempts (1000/500/300 ms) that silently no-opped when the
    // browser wasn't ready, leaving the book at the top on slow loads. (BOOK-7)
    private void ExecutePendingRestoration()
    {
        if (_viewModel == null || _webView == null || !_isBrowserInitialized)
            return;

        // #224: a fresh (re)render shows all notes and highlights by default, so re-apply the per-book
        // toggle state on every load/reload before the hit/anchor restoration branches below.
        ApplyFootnotesVisibility(_viewModel.ShowFootnotes);
        ApplySearchTermsVisibility(_viewModel.ShowSearchTerms);

        var pendingHit = _viewModel.TakePendingHitNavigation();
        var pendingToken = _viewModel.TakePendingPositionToken();
        var pendingAnchor = _viewModel.TakePendingAnchorNavigation();

        if (pendingHit is int savedHit && savedHit >= 1)
        {
            // Inject IMMEDIATELY: cstSearchHighlights exists (the JS bridge was set up earlier in
            // this same callback) but its hits aren't collected yet, so the script queues the intent
            // and init() applies it BEFORE its first styling pass — a single correct paint. Waiting
            // (the old +300ms) let init() paint defaults first, causing a visible blue→red flash on
            // reattach. (BOOK-7)
            var total = _viewModel.TotalHits;
            var target = total > 0 ? Math.Min(savedHit, total) : savedHit;
            _logger.Information("Restoring scroll to saved search hit {Hit}", target);
            NavigateToHighlight(target);
            return;
        }

        // #434 reading-position token — preferred over the coarse string anchor (it interpolates to the exact
        // reading position). Search-hit restore still wins (Fable §6 / #36). ScrollToPositionToken is cache-free
        // (live querySelector), so it works here even before the deferred cache rebuild (Fable §2).
        if (pendingToken != null)
        {
            _logger.Information("Restoring reading position from #434 token (above={Above}, below={Below}, frac={Frac})",
                pendingToken.Above, pendingToken.Below, pendingToken.Fraction);
            ScrollToPositionToken(pendingToken);

            // As in the anchor branch: the token owns the scroll position, so re-mark the CURRENT hit (red)
            // WITHOUT scrolling to keep the highlight matching the "N of M" counter after a reload.
            if (_viewModel.HasSearchHighlights && _viewModel.CurrentHitIndex > 0)
                SyncCurrentHitStyle(_viewModel.CurrentHitIndex);
            return;
        }

        if (!string.IsNullOrEmpty(pendingAnchor))
        {
            _logger.Information("Restoring scroll to saved anchor {Anchor}", pendingAnchor);
            ScrollToPageAnchor(pendingAnchor);

            // A reload resets the JS highlight state, so re-mark the CURRENT hit (red styling)
            // WITHOUT scrolling — the anchor above owns the position; this keeps the red highlight
            // matching the "N of M" counter after a script change. Injected immediately: the JS
            // queues the intent if hits aren't collected yet and init() applies it before its first
            // styling pass (no flash). (BOOK-7)
            if (_viewModel.HasSearchHighlights && _viewModel.CurrentHitIndex > 0)
            {
                SyncCurrentHitStyle(_viewModel.CurrentHitIndex);
            }
            return;
        }

        // No queued intent: a (re)load of a search book still lands on the current hit once the
        // highlights initialize (e.g. a fresh search-result open, tab reattach) — injected
        // immediately, queued by the JS if hits aren't collected yet (no flash). (BOOK-7)
        if (_viewModel.HasSearchHighlights && _viewModel.CurrentHitIndex > 0)
        {
            _logger.Debug("Navigating to current search hit: {HitIndex}", _viewModel.CurrentHitIndex);
            NavigateToHighlight(_viewModel.CurrentHitIndex);
        }
    }


    private void OnTitleChanged()
    {
        var title = _webView?.Title ?? "";
        _logger.Debug("Page title changed | {Details}", title);

        // Cmd+D lookup: the page pushed the current selection back to us through the title. (#25)
        if (title != null && title.StartsWith("CST_LOOKUP_SEL:"))
        {
            try
            {
                var data = title.Substring("CST_LOOKUP_SEL:".Length);
                var parts = data.Split('|');
                string messageTabId = "";
                foreach (var p in parts)
                    if (p.StartsWith("TAB:")) { messageTabId = p.Substring(4); break; }
                if (messageTabId != _tabId)
                    return;   // not for this tab
                var encoded = parts.Length > 0 ? parts[0] : "";
                string sel;
                try { sel = Uri.UnescapeDataString(encoded); } catch { sel = encoded; }
                _lookupSelectionTcs?.TrySetResult(sel);
            }
            catch (Exception ex)
            {
                _logger.Debug(ex, "Failed to parse CST_LOOKUP_SEL");
                _lookupSelectionTcs?.TrySetResult(null);
            }
            return;
        }

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
                string vri = "*", myanmar = "*", pts = "*", thai = "*", other = "*", para = "*", chapter = "*", anchor = "*";
                int scrollY = 0;
                bool isCacheBuilt = false;

                foreach (var part in parts)
                {
                    if (part.StartsWith("VRI=")) vri = part.Substring(4);
                    else if (part.StartsWith("MYANMAR=")) myanmar = part.Substring(8);
                    else if (part.StartsWith("PTS=")) pts = part.Substring(4);
                    else if (part.StartsWith("THAI=")) thai = part.Substring(5);
                    else if (part.StartsWith("OTHER=")) other = part.Substring(6);
                    else if (part.StartsWith("PARA=")) para = part.Substring(5);
                    else if (part.StartsWith("CHAPTER=")) chapter = part.Substring(8);
                    else if (part.StartsWith("ANCHOR=")) anchor = part.Substring(7);
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

                // Handle cache built notification — the authoritative 'cache ready' signal, emitted by
                // build() only AFTER the anchors are populated, so the flag is trustworthy. (#423)
                if (isCacheBuilt)
                {
                    _anchorCacheBuilt = true;
                    _anchorCacheBuildInFlight = false;
                    // Surface the resolved page NOW instead of waiting for the next scroll tick
                    // (≤200ms) plus its 200ms pre-lock delay. Same lock discipline as
                    // OnScrollPositionCheck: skip (don't block) if JS work is in progress —
                    // the timer retries. (#423)
                    Dispatcher.UIThread.Post(async () =>
                    {
                        if (await _jsExecutionLock.WaitAsync(0))
                        {
                            try
                            {
                                UpdateScrollBasedStatus();
                            }
                            finally
                            {
                                _jsExecutionLock.Release();
                            }
                        }
                    });
                }
                else
                {
                    // Handle status update
                    _logger.Debug("Status values - VRI: {Vri}, Myanmar: {Myanmar}, PTS: {Pts}, Thai: {Thai}, Other: {Other}, Para: {Para}, Chapter: {Chapter}, Anchor: {Anchor}, Scroll: {ScrollY}", vri, myanmar, pts, thai, other, para, chapter, anchor, scrollY);

                    // Update scroll position
                    if (scrollY > 0) _lastKnownScrollY = scrollY;

                    // Store last known values
                    if (vri != "*") _lastKnownVri = vri;
                    if (myanmar != "*") _lastKnownMyanmar = myanmar;
                    if (pts != "*") _lastKnownPts = pts;
                    if (thai != "*") _lastKnownThai = thai;
                    if (other != "*") _lastKnownOther = other;
                    if (para != "*") _lastKnownPara = para;

                    // Cache the best anchor in ViewModel for shutdown save (persists across float/unfloat)
                    if (anchor != "*" && !string.IsNullOrEmpty(anchor) && _viewModel != null)
                    {
                        _viewModel.UpdateLastCapturedAnchor(anchor);
                        _logger.Debug("Cached best anchor in ViewModel from status update: {Anchor}", anchor);
                    }

                    // Update the ViewModel
                    if (_viewModel != null)
                    {
                        Dispatcher.UIThread.Post(() =>
                        {
                            _logger.Debug("Updating ViewModel on UI thread");
                            _viewModel.UpdatePageReferences(vri, myanmar, pts, thai, other);
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
        // (CST_CURRENT_CHAPTER handler removed with the redundant cstChapterTracking JS: the current
        // chapter now comes solely from CST_STATUS_UPDATE's CHAPTER= field, which also calls
        // UpdateCurrentChapter. Two competing signals could disagree and flicker the dropdown. (BOOK-5)
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
        // Reading-position token capture result (#434) — hand the raw bracket payload to the awaiting capture.
        else if (title != null && title.StartsWith("CST_POSTOKEN:"))
        {
            try
            {
                var parts = title.Split('|');
                var raw = parts[0].Substring("CST_POSTOKEN:".Length);
                var messageTabId = "";
                int reqId = -1;
                foreach (var p in parts)
                {
                    if (p.StartsWith("TAB:")) messageTabId = p.Substring(4);
                    else if (p.StartsWith("REQ:")) int.TryParse(p.Substring(4), out reqId);
                }
                // Only accept the title for THIS tab and THIS request — a late result from a timed-out capture
                // (stale reqId) must not complete a newer capture with the wrong payload. (Fable PR-B review §3)
                if (messageTabId == _tabId && reqId == _posTokenReq)
                    _posTokenTcs?.TrySetResult(raw);
            }
            catch (Exception ex)
            {
                _logger.Error("Error parsing reading-position token result | {Details}", ex.Message);
                _posTokenTcs?.TrySetException(ex);
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
                    _logger.Debug("Copy operation successful - {CharacterCount} characters copied", lengthStr);
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
        // Check for copy request from JavaScript
        else if (title != null && title.StartsWith("CST_COPY_REQUESTED:"))
        {
            try
            {
                var parts = title.Split('|');
                var messageTabId = parts.Length > 1 && parts[1].StartsWith("TAB:") ? parts[1].Substring(4) : "";

                if (messageTabId == _tabId)
                {
                    _logger.Debug("*** COPY REQUESTED FROM JAVASCRIPT ***");
                    // OnTitleChanged runs on the CEF thread; marshal UI/edit work to the UI thread. (BOOK-2)
                    Dispatcher.UIThread.Post(() => ExecuteCopy());
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error processing copy request from JavaScript | {Details}", ex.Message);
            }
        }
        // Check for select all request from JavaScript
        else if (title != null && title.StartsWith("CST_SELECT_ALL_REQUESTED:"))
        {
            try
            {
                var parts = title.Split('|');
                var messageTabId = parts.Length > 1 && parts[1].StartsWith("TAB:") ? parts[1].Substring(4) : "";

                if (messageTabId == _tabId)
                {
                    _logger.Debug("*** SELECT ALL REQUESTED FROM JAVASCRIPT ***");
                    // OnTitleChanged runs on the CEF thread; run EditCommands on the UI thread. (BOOK-2)
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (_webView != null)
                        {
                            try
                            {
                                _webView.EditCommands.SelectAll();
                                _logger.Debug("WebView SelectAll executed successfully from JavaScript request");
                            }
                            catch (Exception ex)
                            {
                                _logger.Error(ex, "Error executing SelectAll from JavaScript request");
                            }
                        }
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error processing select all request from JavaScript | {Details}", ex.Message);
            }
        }
        // Check for View Source 1957 request from JavaScript
        else if (title != null && title.StartsWith("CST_VIEW_SOURCE_1957:"))
        {
            try
            {
                var parts = title.Split('|');
                var messageTabId = parts.Length > 1 && parts[1].StartsWith("TAB:") ? parts[1].Substring(4) : "";

                if (messageTabId == _tabId)
                {
                    _logger.Debug("*** VIEW SOURCE 1957 REQUESTED FROM JAVASCRIPT ***");
                    // OnTitleChanged runs on the CEF thread; the command mutates the dock layout, so it
                    // must run on the UI thread. (BOOK-2)
                    Dispatcher.UIThread.Post(() => _viewModel?.RequestShowSource(source2010: false));
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error processing View Source 1957 request from JavaScript | {Details}", ex.Message);
            }
        }
        // Check for View Source 2010 request from JavaScript
        else if (title != null && title.StartsWith("CST_VIEW_SOURCE_2010:"))
        {
            try
            {
                var parts = title.Split('|');
                var messageTabId = parts.Length > 1 && parts[1].StartsWith("TAB:") ? parts[1].Substring(4) : "";

                if (messageTabId == _tabId)
                {
                    _logger.Debug("*** VIEW SOURCE 2010 REQUESTED FROM JAVASCRIPT ***");
                    // OnTitleChanged runs on the CEF thread; the command mutates the dock layout, so it
                    // must run on the UI thread. (BOOK-2)
                    Dispatcher.UIThread.Post(() => _viewModel?.RequestShowSource(source2010: true));
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error processing View Source 2010 request from JavaScript | {Details}", ex.Message);
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
        if (_webView == null) return;

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
                // Add JavaScript functions for search navigation and keyboard capture
                var script = @"
                    // Keyboard event capture system
                    window.cstKeyboardCapture = {
                        init: function() {
                            document.addEventListener('keydown', function(event) {
                                // Log all keyboard events for debugging
                                window.cstLogger.log('DEBUG', 'JS KeyDown: ' + event.key + ' + modifiers: ' + event.ctrlKey + '/' + event.metaKey + '/' + event.altKey + '/' + event.shiftKey);
                                
                                // Check for Cmd+C or Ctrl+C
                                if (event.key === 'c' && (event.metaKey || event.ctrlKey)) {
                                    window.cstLogger.log('DEBUG', 'Copy shortcut detected in JavaScript');
                                    event.preventDefault(); // Prevent default browser behavior
                                    event.stopPropagation(); // Stop event bubbling
                                    
                                    // Signal C# to handle copy operation
                                    // |SEQ makes a repeated identical command a *distinct* title so
                                    // TitleChanged fires again (two Cmd+C in one tick used to no-op). C#
                                    // parses TAB from Split('|')[1], so the trailing SEQ is ignored. (BOOK-4)
                                    document.title = 'CST_COPY_REQUESTED:|TAB:{_tabId}|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                                    return false;
                                }
                                
                                // Check for Cmd+A or Ctrl+A
                                if (event.key === 'a' && (event.metaKey || event.ctrlKey)) {
                                    window.cstLogger.log('DEBUG', 'Select All shortcut detected in JavaScript');
                                    event.preventDefault(); // Prevent default browser behavior
                                    event.stopPropagation(); // Stop event bubbling

                                    // Signal C# to handle select all operation
                                    document.title = 'CST_SELECT_ALL_REQUESTED:|TAB:{_tabId}|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                                    return false;
                                }

                                // Check for Shift+Ctrl+E or Shift+Cmd+E (View Source 2010)
                                // Must check before Cmd+E since both involve 'e' key
                                if ((event.key === 'E' || event.key === 'e') && event.shiftKey && (event.metaKey || event.ctrlKey)) {
                                    window.cstLogger.log('DEBUG', 'View Source 2010 shortcut detected in JavaScript');
                                    event.preventDefault();
                                    event.stopPropagation();
                                    document.title = 'CST_VIEW_SOURCE_2010:|TAB:{_tabId}|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                                    return false;
                                }

                                // Check for Ctrl+E or Cmd+E (View Source 1957) - without Shift
                                if (event.key === 'e' && !event.shiftKey && (event.metaKey || event.ctrlKey)) {
                                    window.cstLogger.log('DEBUG', 'View Source 1957 shortcut detected in JavaScript');
                                    event.preventDefault();
                                    event.stopPropagation();
                                    document.title = 'CST_VIEW_SOURCE_1957:|TAB:{_tabId}|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                                    return false;
                                }
                            }, true); // Use capture phase to intercept before other handlers
                            
                            window.cstLogger.log('DEBUG', 'Keyboard capture initialized');
                        }
                    };

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
                                // Diagnostic JS logs go to the browser console, NOT document.title. The
                                // title is the C#<->JS control channel; logging every JS event through it
                                // (worst: on every keydown) clobbered pending control messages such as
                                // CST_GET_PARA_RESULT before C# could read them. (BOOK-4)
                                console.log('[CST][' + level + '] ' + fullMessage);
                            } catch (e) {
                                // Failsafe, do nothing
                            }
                        }
                    };

                    window.cstSearchHighlights = {
                        hits: [],
                        currentIndex: 0,
                        highlightsVisible: true,   // #224: false clears the highlight color (keeps the words)

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

                            // Apply any navigation C# requested BEFORE highlights were ready (tab
                            // reattach / reload: the shared JS lock can delay the injected call past
                            // document readiness, or run it before init - either way the intent waits
                            // here instead of being silently lost). Consume it BEFORE the first
                            // styling pass so the correct hit is red in the initial paint - painting
                            // defaults first caused a visible blue->red flash on reattach. (BOOK-7)
                            var pendingScroll = false;
                            if (window.__cstPendingHit && this.hits.length > 0) {
                                var pending = window.__cstPendingHit;
                                window.__cstPendingHit = null;
                                this.currentIndex = Math.min(pending.index, this.hits.length) - 1;
                                pendingScroll = pending.scroll;
                            }

                            // #321 (A8-2): honor a highlight-visibility intent that C# requested BEFORE this
                            // object existed. ApplySearchTermsVisibility can run while SetupJavaScriptBridge is
                            // still deferred behind the shared JS lock, so its setHighlightsVisible() call would
                            // hit an undefined object and be lost - then this fresh object would default to
                            // visible:true and paint every hit despite the toggle/persisted state saying off.
                            // The intent is queued on window.__cstPendingHighlightsVisible; consume it before the
                            // first styling pass so the initial paint is correct (also restores the off state on
                            // reload, where the object is recreated with the true default).
                            if (typeof window.__cstPendingHighlightsVisible === 'boolean') {
                                this.highlightsVisible = window.__cstPendingHighlightsVisible;
                            }

                            this.updateHighlightStyles();

                            if (pendingScroll) {
                                var hit = this.hits[this.currentIndex];
                                if (hit) { hit.scrollIntoView({ behavior: 'instant', block: 'center' }); }
                            }
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
                                if (!this.highlightsVisible) {
                                    // #224: highlight OFF for EVERY hit (blue + the red current one). Must
                                    // OVERRIDE the CSS '.hit { background: blue }' rule, not just clear the
                                    // inline style — clearing to '' would fall back to the CSS blue and only
                                    // the red hit would visibly change. 'transparent' + 'inherit' removes the
                                    // highlight and shows the word in the normal text color (also correct in
                                    // dark mode, unlike CST4's hardcoded white/black).
                                    hit.style.backgroundColor = 'transparent';
                                    hit.style.color = 'inherit';
                                } else if (i === this.currentIndex) {
                                    hit.style.backgroundColor = 'red';
                                    hit.style.color = 'white';
                                } else {
                                    hit.style.backgroundColor = 'blue';  // Use original CST4 blue color
                                    hit.style.color = 'white';
                                }
                            });
                        },

                        // #224: toggle search-term highlighting on/off (per book). The flag persists on the
                        // object so init()'s styling pass after a reload respects it. (Replaces the old
                        // showHits, which hid the words via display:none — wrong semantics.)
                        setHighlightsVisible: function(visible) {
                            this.highlightsVisible = visible;
                            // #321 (A8-2): persist the intent so a later re-injection of this object (reload)
                            // initializes from it instead of the visible:true default.
                            window.__cstPendingHighlightsVisible = visible;
                            this.updateHighlightStyles();
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
                    if (document.readyState === 'complete') {
                        setTimeout(initializeHighlights, 100);
                        setTimeout(function() { window.cstKeyboardCapture.init(); }, 50);
                    } else {
                        document.addEventListener('DOMContentLoaded', function() {
                            setTimeout(initializeHighlights, 100);
                            setTimeout(function() { window.cstKeyboardCapture.init(); }, 50);
                        });
                    }
                ";

                // Replace tab ID placeholder with actual tab ID value
                script = script.Replace("{_tabId}", _tabId);

                _webView.ExecuteScript(script);
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
        if (_webView == null)
        {
            _logger.Warning("NavigateToHighlight called but _webView is null");
            return;
        }

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => NavigateToHighlight(hitIndex));
            return;
        }

        // Handle special signals for copy and select all
        if (hitIndex == -1)
        {
            _logger.Debug("*** COPY COMMAND TRIGGERED VIA KEYBOARD SHORTCUT ***");
            HandleCopySelectedText();
            return;
        }
        
        if (hitIndex == -2)
        {
            _logger.Debug("*** SELECT ALL COMMAND TRIGGERED VIA KEYBOARD SHORTCUT ***");
            if (_webView != null)
            {
                try
                {
                    _webView.EditCommands.SelectAll();
                    _logger.Debug("WebView SelectAll executed successfully");
                }
                catch (Exception ex)
                {
                    _logger.Error(ex, "Error executing SelectAll");
                }
            }
            return;
        }

        _logger.Debug("Method called - hitIndex: {HitIndex}", hitIndex);
        _logger.Debug("NavigateToHighlight attempting to acquire JS lock");
        if (_jsExecutionLock.Wait(0))
        {
            _logger.Debug("NavigateToHighlight acquired JS lock successfully");
            try
            {
                // If highlights aren't collected yet (reload in flight, init not run), queue the
                // intent for init() to apply — the old optional-chaining call was a silent no-op in
                // that window, losing the red current-hit styling on tab reattach. (BOOK-7)
                var script = "if (window.cstSearchHighlights && window.cstSearchHighlights.hits.length > 0) { " +
                             $"window.cstSearchHighlights.navigateToHit({hitIndex}); " +
                             "} else { " +
                             $"window.__cstPendingHit = {{ index: {hitIndex}, scroll: true }}; " +
                             "}";
                _webView.ExecuteScript(script);
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
        if (_webView == null || string.IsNullOrEmpty(anchor)) return;

        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => NavigateToAnchor(anchor));
            return;
        }

        _logger.Debug("NavigateToAnchor called | {Details}", anchor);
        _logger.Debug("NavigateToAnchor attempting to acquire JS lock | {Details}", anchor);
        if (_jsExecutionLock.Wait(0))
        {
            _logger.Debug("NavigateToAnchor acquired JS lock successfully | {Details}", anchor);
            try
            {
                // JSON-encode the anchor so it's a properly-escaped JS string literal; a raw splice broke
                // the whole injected script on any anchor containing a quote. (BOOK-11)
                var anchorJson = System.Text.Json.JsonSerializer.Serialize(anchor);
                var script = $@"
                (function() {{
                    try {{
                        var element = document.getElementById({anchorJson}) || document.querySelector('a[name=' + JSON.stringify({anchorJson}) + ']');
                        if (element) {{
                            element.scrollIntoView({{ behavior: 'smooth', block: 'start' }});
                        }}
                    }} catch (error) {{ }}
                }})();";
                _webView.ExecuteScript(script);
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

    // #224: apply the per-book "Footnotes" toggle. Notes are static <span class="note"> from the XSLT with
    // no inline-style owner, so a direct inline-display toggle is robust (vs the XSL getStyleClass
    // stylesheet-walk). Re-applied after every (re)load since a fresh render shows notes by default.
    public void ApplyFootnotesVisibility(bool visible)
    {
        if (_webView == null) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplyFootnotesVisibility(visible));
            return;
        }
        try
        {
            var display = visible ? "''" : "'none'";
            // #321 (A8-1): toggling notes reflows the document, which invalidates cstAnchorCache's absolute
            // pixel positions (chapter/para/page tracking + the persisted scroll anchor read from them).
            // Rebuild the cache after the toggle, and keep the viewport steady by holding a reference anchor
            // at its pre-toggle offset so the content doesn't jump under the reader.
            var script = $@"
                (function() {{
                    var refName = (window.cstAnchorCache && window.cstAnchorCache.getCurrentAnchor)
                        ? window.cstAnchorCache.getCurrentAnchor(window.pageYOffset) : null;
                    var refEl = refName ? document.querySelector('a[name=""' + refName + '""]') : null;
                    var refOffset = refEl ? refEl.getBoundingClientRect().top : null;

                    document.querySelectorAll('.note').forEach(function(n) {{ n.style.display = {display}; }});

                    if (window.cstAnchorCache && window.cstAnchorCache.build) {{ window.cstAnchorCache.build(); }}

                    if (refEl && refOffset !== null) {{
                        window.scrollBy(0, refEl.getBoundingClientRect().top - refOffset);
                    }}
                }})();";
            _webView.ExecuteScript(script);
        }
        catch (Exception ex)
        {
            _logger.Error("ApplyFootnotesVisibility failed | {Details}", ex.Message);
        }
    }

    // #224: apply the per-book search-term highlight toggle. Routed through cstSearchHighlights because it
    // owns the inline blue/red colors on the .hit spans (which override the CSS rule). ON re-applies the
    // blue/red styling; OFF clears it so matched words show as normal text (CST4's "remove highlight, keep
    // the words"). Re-applied after every (re)load.
    public void ApplySearchTermsVisibility(bool visible)
    {
        if (_webView == null) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => ApplySearchTermsVisibility(visible));
            return;
        }
        try
        {
            // #321 (A8-2): always queue the intent on the window so a not-yet-injected cstSearchHighlights
            // still picks it up in init() (the bridge can be deferred behind the shared JS lock); apply it
            // immediately too when the object is already present.
            var v = visible.ToString().ToLower();
            _webView.ExecuteScript(
                $"window.__cstPendingHighlightsVisible = {v}; " +
                $"if (window.cstSearchHighlights) {{ window.cstSearchHighlights.setHighlightsVisible({v}); }}");
        }
        catch (Exception ex)
        {
            _logger.Error("ApplySearchTermsVisibility failed | {Details}", ex.Message);
        }
    }

    // Public method to get current scroll position
    public int GetScrollPosition()
    {
        // Return 0 if browser is not ready
        if (_webView == null || !_isBrowserInitialized)
            return 0;

        // Return the last known scroll position
        return _lastScrollPosition;
    }

    // Public method to restore scroll position
    public void SetScrollPosition(int position)
    {
        if (_webView == null || !_isBrowserInitialized || position <= 0) return;

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
                _webView.ExecuteScript(script);
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
        if (_webView == null || !_isBrowserInitialized || string.IsNullOrEmpty(anchorName)) return;

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
                // JSON-encode the anchor so it's a properly-escaped JS string literal (injected once as
                // __a); a raw splice broke the whole injected script on any anchor with a quote. (BOOK-11)
                var anchorJson = System.Text.Json.JsonSerializer.Serialize(anchorName);
                var script = $@"
                    (function() {{
                        var __a = {anchorJson};
                        // JSON.stringify quotes the attribute value: unquoted selectors throw a
                        // SyntaxError on dotted anchors (every VRI page anchor, e.g. V1.0001),
                        // which killed the whole script before the getElementById fallback.
                        var anchor = document.querySelector('a[name=' + JSON.stringify(__a) + ']') ||
                                    document.querySelector('a[id=' + JSON.stringify(__a) + ']') ||
                                    document.getElementById(__a);

                        if (anchor) {{
                            anchor.scrollIntoView({{ behavior: ""instant"", block: ""start"" }});
                        }} else {{
                            var allAnchors = Array.from(document.querySelectorAll(""a[name]""));
                            var paraAnchors = allAnchors.filter(a => a.name && a.name.startsWith(""para""));

                            if (__a.startsWith(""para"")) {{
                                var targetText = __a.substring(4);
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
                _webView.ExecuteScript(script);
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

    /// <summary>
    /// Get current paragraph anchor asynchronously - port of CST4's GetPara() method with async/await pattern
    /// Returns the paragraph anchor at the top of the viewport (e.g., "para123")
    /// This method fixes the UI thread deadlock issue by using TaskCompletionSource
    /// </summary>
    public async Task<string?> GetCurrentParagraphAnchorAsync()
    {
        if (_webView == null || !_isBrowserInitialized)
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
                        if (window.cstAnchorCache && window.cstAnchorCache.getCurrentAnchor) {
                            result = window.cstAnchorCache.getCurrentAnchor(scrollY);
                        }
                        document.title = 'CST_GET_PARA_RESULT:' + (result || 'null') + '|TAB:{_tabId}' + '|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                    } catch (error) {
                        document.title = 'CST_GET_PARA_RESULT:error:' + error.message + '|TAB:{_tabId}' + '|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                    }
                })();";

                // Replace tab ID placeholder with actual tab ID value
                script = script.Replace("{_tabId}", _tabId);

                _webView.ExecuteScript(script);

                var timeoutTask = Task.Delay(TimeSpan.FromMilliseconds(1000));
                var completedTask = await Task.WhenAny(_paraAnchorTcs.Task, timeoutTask);

                if (completedTask == _paraAnchorTcs.Task)
                {
                    var result = await _paraAnchorTcs.Task;
                    // Cache the result in ViewModel for shutdown save (persists across float/unfloat)
                    if (!string.IsNullOrEmpty(result) && result != "null" && _viewModel != null)
                    {
                        _viewModel.UpdateLastCapturedAnchor(result);
                        _logger.Debug("Cached anchor in ViewModel: {Anchor}", result);
                    }
                    return result;
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

    /// <summary>
    /// Capture the current reading position as a #434 token — the anchors bracketing the viewport top plus the
    /// fraction between them. The JS uses the cache only to SELECT the bracketing names (sorted lookup over
    /// sortedAllAnchors), then reads LIVE getBoundingClientRect on just those two elements (Fable §1: immune to
    /// cache staleness); the fraction is computed C#-side by <see cref="ReadingPositionMath"/> so it stays
    /// unit-tested. Returns null when the cache isn't built yet (nothing meaningful to capture).
    /// </summary>
    public async Task<ReadingPositionToken?> GetCurrentPositionTokenAsync(int attempt = 0)
    {
        if (_webView == null || !_isBrowserInitialized) return null;

        if (await _jsExecutionLock.WaitAsync(10))
        {
            try
            {
                // Tag this request so a LATE title from a previous (timed-out) capture can't complete THIS one
                // with stale data — OnTitleChanged only accepts the matching REQ. (Fable PR-B review §3)
                var reqId = ++_posTokenReq;
                _posTokenTcs?.TrySetCanceled();
                _posTokenTcs = new TaskCompletionSource<string?>();

                var script = @"
                (function() {
                    try {
                        var scrollY = window.pageYOffset || document.documentElement.scrollTop || 0;
                        var out = 'null';
                        var c = window.cstAnchorCache;
                        if (c && c.isBuilt && c.sortedAllAnchors && c.sortedAllAnchors.length > 0) {
                            var list = c.sortedAllAnchors;
                            // Cache SELECTS the bracket (last <= scrollY, first > scrollY); live rects MEASURE it.
                            var aIdx = -1;
                            for (var i = 0; i < list.length; i++) { if (list[i].position <= scrollY) aIdx = i; else break; }
                            var aName = aIdx >= 0 ? list[aIdx].name : '';
                            var bName = (aIdx + 1 < list.length) ? list[aIdx + 1].name : '';
                            var livePos = function(name) {
                                if (!name) return '';
                                var el = document.querySelector('a[name=' + JSON.stringify(name) + ']') || document.getElementById(name);
                                return el ? Math.round(el.getBoundingClientRect().top + window.pageYOffset) : '';
                            };
                            out = aName + ',' + livePos(aName) + ',' + bName + ',' + livePos(bName) + ',' + Math.round(scrollY);
                        }
                        document.title = 'CST_POSTOKEN:' + out + '|TAB:{_tabId}' + '|REQ:{_reqId}' + '|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                    } catch (e) {
                        document.title = 'CST_POSTOKEN:err|TAB:{_tabId}' + '|REQ:{_reqId}' + '|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                    }
                })();";
                script = script.Replace("{_tabId}", _tabId).Replace("{_reqId}", reqId.ToString());
                _webView.ExecuteScript(script);

                var completed = await Task.WhenAny(_posTokenTcs.Task, Task.Delay(1000));
                if (completed != _posTokenTcs.Task) { _posTokenTcs.TrySetCanceled(); return null; }
                return ParsePositionToken(await _posTokenTcs.Task);
            }
            catch (Exception ex)
            {
                _logger.Error("Error capturing reading-position token | {Details}", ex.Message);
                return null;
            }
            finally { _jsExecutionLock.Release(); }
        }

        // Bounded lock-contention retry — a wedged lock must degrade to "no token" (no restore), NOT hang the
        // synchronous script-change reload that awaits this. ~1s worth of attempts, then give up. (Fable §3)
        if (attempt >= 10) { _logger.Warning("GetCurrentPositionTokenAsync gave up after {N} lock-contention retries", attempt); return null; }
        await Task.Delay(100);
        return await GetCurrentPositionTokenAsync(attempt + 1);
    }

    // Parse the raw "above,abovePos,below,belowPos,scrollTop" payload into a token via the unit-tested math.
    private static ReadingPositionToken? ParsePositionToken(string? raw)
    {
        if (string.IsNullOrEmpty(raw) || raw == "null" || raw == "err") return null;
        var f = raw.Split(',');
        if (f.Length != 5) return null;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        string? above = f[0].Length == 0 ? null : f[0];
        string? below = f[2].Length == 0 ? null : f[2];
        double.TryParse(f[1], System.Globalization.NumberStyles.Any, inv, out var aPos);
        double.TryParse(f[3], System.Globalization.NumberStyles.Any, inv, out var bPos);
        double.TryParse(f[4], System.Globalization.NumberStyles.Any, inv, out var scrollTop);
        if (above == null && below == null) return null; // no anchors → nothing to capture
        return ReadingPositionMath.Capture(above, aPos, below, bPos, scrollTop);
    }

    /// <summary>
    /// Restore a reading-position token (#434). CACHE-FREE (Fable §2): resolves the bracketing anchors by name
    /// with live querySelector and interpolates, so it works even before the (deferred) cache rebuild and slots
    /// into the existing ExecutePendingRestoration timing. Non-bracket cases are handled C#-side without JS.
    /// </summary>
    public void ScrollToPositionToken(ReadingPositionToken token)
    {
        if (_webView == null || !_isBrowserInitialized || token == null) return;
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(() => ScrollToPositionToken(token)); return; }

        // Empty / unresolvable (no anchors, or a malformed token) → leave the position untouched.
        if (token.Above == null && token.Below == null) return;

        // Document start → top, no anchor lookup needed.
        if (token.Above == null) { RunScrollScript("window.scrollTo({ top: 0, behavior: 'instant' });"); return; }

        // Past the last anchor → land on the upper anchor (reuse the existing anchor scroll + fuzzy fallback).
        if (token.Below == null) { ScrollToPageAnchor(token.Above); return; }

        // Full bracket. Restore RELATIVELY — scrollIntoView(above) then scrollBy(fraction * gap) — rather than
        // computing an ABSOLUTE window.scrollTo(target). The final scrollTop is the same as
        // ReadingPositionMath.ResolveTarget (above + fraction*(below-above)), but this is robust to an early /
        // pre-reflow rect read: if the gap is misread small (or 0), it simply lands at the above anchor's top
        // edge — i.e. no worse than ScrollToPageAnchor — instead of an absolute scrollTo(0) jump-to-top (the
        // #423 failure mode a raw getBoundingClientRect().top could trigger). (Fable PR-B review §1/§2)
        var aboveJson = System.Text.Json.JsonSerializer.Serialize(token.Above);
        var belowJson = System.Text.Json.JsonSerializer.Serialize(token.Below);
        // Clamp the fraction defensively (matches ResolveTarget's guard) for hand-built / persisted tokens.
        var f = double.IsNaN(token.Fraction) ? 0.0 : Math.Max(0.0, Math.Min(1.0, token.Fraction));
        var fraction = f.ToString(System.Globalization.CultureInfo.InvariantCulture);
        var script = $@"
            (function() {{
                var find = function(name) {{
                    return document.querySelector('a[name=' + JSON.stringify(name) + ']') || document.getElementById(name);
                }};
                var aEl = find({aboveJson}), bEl = find({belowJson});
                if (aEl) {{
                    aEl.scrollIntoView({{ behavior: 'instant', block: 'start' }});
                    if (bEl) {{
                        // With aEl now at the viewport top, bEl's rect.top IS the live gap to the next anchor.
                        var gap = bEl.getBoundingClientRect().top;
                        if (gap > 0) {{ window.scrollBy(0, {fraction} * gap); }}
                    }}
                }} else if (bEl) {{
                    bEl.scrollIntoView({{ behavior: 'instant', block: 'start' }});
                }}
                // Both anchors gone (a corpus-update rename) → leave the position; the nearest-paragraph fuzzy
                // fallback belongs to the cross-run PR.
            }})();";
        RunScrollScript(script);
    }

    // Run a scroll script under the JS lock, mirroring ScrollToPageAnchor's lock discipline + retry.
    private void RunScrollScript(string script)
    {
        if (_webView == null) return;
        if (_jsExecutionLock.Wait(0))
        {
            try { _webView.ExecuteScript(script); }
            catch (Exception ex) { _logger.Error("Reading-position scroll failed | {Details}", ex.Message); }
            finally { _jsExecutionLock.Release(); }
        }
        else
        {
            Dispatcher.UIThread.Post(async () => { await Task.Delay(100); RunScrollScript(script); }, DispatcherPriority.Background);
        }
    }



    public Task HandleCopyFromGlobalShortcut()
    {
        _logger.Debug("Global copy shortcut received - attempting to copy selected text");
        return HandleCopySelectedText();
    }

    // Alternative approach: Poll the JavaScript for selected text and provide copy functionality
    private Task HandleCopySelectedText()
    {
        if (_webView == null)
        {
            _logger.Debug("Copy failed - WebView not available");
            return Task.CompletedTask;
        }

        try
        {
            _logger.Debug("Using WebView native copy command");
            _webView.EditCommands.Copy();
            _logger.Debug("Copy command executed successfully");
        }
        catch (Exception ex)
        {
            _logger.Error("Error in HandleCopySelectedText | {Details}", ex.Message);
        }
        
        return Task.CompletedTask;
    }

    public void ExecuteCopy()
    {
        _logger.Debug("ACTION: ExecuteCopy called.");
        HandleCopySelectedText();
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);
        this.PropertyChanged -= OnIsVisibleChanged;

        // Stop and dispose the timers to prevent resource leaks
        if (_scrollTimer != null)
        {
            _scrollTimer.Stop();
            _scrollTimer.Dispose();
            _scrollTimer = null;
            _logger.Debug("Paused and disposed scroll tracking");
        }
        
        if (_dragMonitoringTimer != null)
        {
            _dragMonitoringTimer.Stop();
            _dragMonitoringTimer.Dispose();
            _dragMonitoringTimer = null;
            _logger.Debug("Disposed drag monitoring timer");
        }
    }

    private void SetupDragMonitoring()
    {
        _logger.Information("Drag monitoring disabled in BookDisplayView - SimpleTabbedWindow handles all drag detection");
        // BookDisplayView's local drag monitoring is DISABLED because:
        // 1. SimpleTabbedWindow already has comprehensive drag detection for ALL windows
        // 2. Duplicate monitoring causes CEF crashes when WebViews are hidden/shown during tab switches
        // 3. ControlRecycling + repeated WebView hide/show operations invalidate CEF native handles
        // 4. The crash: AvnNativeControlHostTopLevelAttachment::InitializeWithChildHandle null pointer dereference
    }

    private void OnDragMonitoringTimer(object? sender, System.Timers.ElapsedEventArgs e)
    {
        Dispatcher.UIThread.Post(() =>
        {
            var now = DateTime.Now;
            
            // Check if a drag operation might be in progress
            if (_isPointerPressed && !_isDragInProgress)
            {
                var timeSincePress = now - _lastPointerPressedTime;
                if (timeSincePress.TotalMilliseconds > 200) // Drag threshold time
                {
                    _logger.Information("*** TIMER DETECTED POTENTIAL DRAG - HIDING WebView ***");
                    _isDragInProgress = true;
                    HideWebViewForDrag();
                }
            }
            
            // If drag ended, restore WebView (but only after minimum hide duration)
            if (!_isPointerPressed && _isDragInProgress)
            {
                var timeSinceHidden = now - _webViewHiddenTime;
                if (timeSinceHidden.TotalMilliseconds >= MIN_WEBVIEW_HIDE_DURATION)
                {
                    _logger.Information("*** TIMER DETECTED DRAG END - RESTORING WebView (hidden for {HideDuration}ms) ***", timeSinceHidden.TotalMilliseconds);
                    _isDragInProgress = false;
                    RestoreWebViewAfterDrag();
                }
                else
                {
                    _logger.Debug("*** Drag ended but WebView hidden for only {HideDuration}ms - waiting for minimum {MinDuration}ms ***", 
                        timeSinceHidden.TotalMilliseconds, MIN_WEBVIEW_HIDE_DURATION);
                }
            }
            
            // Fallback: If WebView has been hidden for too long (>10 seconds), restore it
            if (_isDragInProgress)
            {
                var timeSinceDragStart = now - _lastPointerPressedTime;
                if (timeSinceDragStart.TotalMilliseconds > 10000) // 10 second timeout
                {
                    _logger.Information("*** FALLBACK TIMEOUT - RESTORING WebView after 10 seconds ***");
                    _isDragInProgress = false;
                    _isPointerPressed = false;
                    RestoreWebViewAfterDrag();
                }
            }
        });
    }

    private void HideWebViewForDrag()
    {
        _logger.Information("Hiding all WebViews across all windows to allow dock drop indicators");
        _webViewHiddenTime = DateTime.Now;
        
        // Hide WebViews in all application windows for cross-window drag support
        HideAllWebViewsInAllWindows();
    }
    
    private void HideAllWebViewsInWindow(Window window)
    {
        var webViews = window.GetVisualDescendants().OfType<WebViewControl.WebView>();
        foreach (var webView in webViews)
        {
            if (webView.IsVisible)
            {
                _logger.Information("Hiding WebView for drag operation");
                webView.IsVisible = false;
                webView.IsHitTestVisible = false;
            }
        }
    }

    private void RestoreWebViewAfterDrag()
    {
        _logger.Information("Restoring all WebViews across all windows after drag operation");
        
        // Restore WebViews in all application windows for cross-window drag support
        RestoreAllWebViewsInAllWindows();
    }
    
    private void HideAllWebViewsInAllWindows()
    {
        try
        {
            // Get all application windows
            var app = Application.Current;
            if (app?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (var window in desktop.Windows)
                {
                    HideAllWebViewsInWindow(window);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error hiding WebViews across all windows");
            // Fallback to hiding just current WebView
            if (_webView != null && _webView.IsVisible)
            {
                _logger.Information("Fallback: Hiding only current WebView");
                _webView.IsVisible = false;
                _webView.IsHitTestVisible = false;
            }
        }
    }
    
    private void RestoreAllWebViewsInAllWindows()
    {
        try
        {
            // Get all application windows
            var app = Application.Current;
            if (app?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                foreach (var window in desktop.Windows)
                {
                    RestoreAllWebViewsInWindow(window);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.Warning(ex, "Error restoring WebViews across all windows");
            // Fallback to restoring just current WebView
            if (_webView != null && !_webView.IsVisible && _viewModel?.IsWebViewAvailable == true)
            {
                _logger.Information("Fallback: Restoring only current WebView");
                _webView.IsVisible = true;
                _webView.IsHitTestVisible = true;
            }
        }
    }
    
    private void RestoreAllWebViewsInWindow(Window window)
    {
        var webViews = window.GetVisualDescendants().OfType<WebViewControl.WebView>();
        foreach (var webView in webViews)
        {
            if (!webView.IsVisible)
            {
                _logger.Information("Restoring WebView after drag operation");
                webView.IsVisible = true;
                webView.IsHitTestVisible = true;
            }
        }
    }

    private void OnWindowPointerPressed(object? sender, PointerPressedEventArgs e)
    {
        _isPointerPressed = true;
        _lastPointerPressedTime = DateTime.Now;
        _lastPointerPressedPosition = e.GetPosition(this);
        _logger.Debug("Pointer pressed - monitoring for potential drag");
    }

    private void OnWindowPointerReleased(object? sender, PointerReleasedEventArgs e)
    {
        _isPointerPressed = false;
        _logger.Debug("Pointer released - ending drag monitoring");
        
        // Ensure WebView is restored when pointer is released
        if (_isDragInProgress)
        {
            var timeSinceHidden = DateTime.Now - _webViewHiddenTime;
            _logger.Information("*** POINTER RELEASED - RESTORING WebView (hidden for {HideDuration}ms) ***", timeSinceHidden.TotalMilliseconds);
            _isDragInProgress = false;
            RestoreWebViewAfterDrag();
        }
    }

    private void OnWindowPointerMoved(object? sender, PointerEventArgs e)
    {
        if (_isPointerPressed && !_isDragInProgress)
        {
            // Check if pointer has been pressed long enough to be a drag (not just a tab click)
            var timeSincePress = DateTime.Now - _lastPointerPressedTime;
            if (timeSincePress.TotalMilliseconds < DRAG_TIME_THRESHOLD)
            {
                _logger.Debug("Pointer movement ignored - too soon after press ({Duration}ms < {Threshold}ms)",
                    timeSincePress.TotalMilliseconds, DRAG_TIME_THRESHOLD);
                return;
            }

            var currentPosition = e.GetPosition(this);
            var distance = Math.Sqrt(
                Math.Pow(currentPosition.X - _lastPointerPressedPosition.X, 2) +
                Math.Pow(currentPosition.Y - _lastPointerPressedPosition.Y, 2)
            );

            if (distance > DRAG_THRESHOLD)
            {
                _logger.Information("*** POINTER MOVEMENT DETECTED DRAG - HIDING WebView (after {Duration}ms) ***",
                    timeSincePress.TotalMilliseconds);
                _isDragInProgress = true;
                HideWebViewForDrag();
            }
        }
    }

}