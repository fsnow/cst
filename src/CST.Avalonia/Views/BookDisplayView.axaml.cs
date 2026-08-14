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
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using WebViewControl;
using CST.Avalonia.ViewModels;
using CST.Avalonia.Services;
using CST.Avalonia.Models;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace CST.Avalonia.Views;

public partial class BookDisplayView : UserControl, Services.IBrowserDocumentView
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
    private ReadingPositionToken? _lastPositionToken = null; // #434 rolling-captured reading position (from the status tick); restored on tab reattach (#31)
    // #434 resize consumer: a reflow moves content under the native scrollTop, so the reading position drifts.
    // Resize events fire AFTER layout changed, so we snapshot the still-pre-reflow rolling token on the FIRST
    // event of a gesture and restore it once the gesture settles.
    private ReadingPositionToken? _resizeRestoreToken = null;
    private System.Timers.Timer? _resizeSettleTimer = null;
    private bool _resizeInProgress = false;
    private double _lastKnownWidth = 0, _lastKnownHeight = 0;
    internal const int ResizeSettleMs = 250;   // debounce for a resize/zoom gesture, before restoring
    /// <summary>
    /// Trailing-edge debounce for the in-page anchor-cache rebuild, injected into the JS.
    ///
    /// MUST stay greater than <see cref="ResizeSettleMs"/>. The zoom restore waits for the CACHE_BUILT this
    /// rebuild emits, and the ordering guarantee — that the signal cannot arrive before the restore is
    /// waiting for it — rests entirely on this being the longer of the two. (fable review)
    /// </summary>
    internal const int AnchorRebuildDebounceMs = 300;
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

    // C# scroll tracking for reliable status bar updates. The timer drives the status tick; the scroll
    // position itself is NOT tracked here — the status values are computed JS-side from the live scrollY
    // and arrive as strings, and the reading position is carried by the #434 token. (#552)
    private System.Timers.Timer? _scrollTimer;
    private string _lastKnownVri = "*";
    private string _lastKnownMyanmar = "*";
    private string _lastKnownPts = "*";
    private string _lastKnownThai = "*";
    private string _lastKnownOther = "*";
    private string _lastKnownPara = "*";

    // Cache the last successfully captured anchor for shutdown save


    // Window context tracking for CEF handle invalidation detection
    private Window? _currentWindow = null;
    // The window this View's CEF browser was created in. Unlike _currentWindow (nulled on every detach so
    // window-change is re-evaluated), this persists across detach/reattach so we can assert the invariant a
    // live browser must never re-attach to a different window — that is the #458 SIGSEGV. Purely diagnostic;
    // the fix is dispose-before-move in CstDockFactory. If this ever logs, a re-parent path is carrying a
    // live browser and needs the same guard.
    private Window? _browserBirthWindow = null;

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

        // Zoom is stored per script and applies to book text only, so every book view listens. (#572)
        SubscribeToZoomChanges();

        // #570: wire the find bar's controls. The bar itself stays hidden until Cmd/Ctrl+F.
        SetupFindBar();

        // #618: wire the toolbar's zoom control (step buttons and the percentage box).
        SetupZoomControl();

        // #628: wire the book-information flyout's copy button.
        SetupBookInfoPanel();

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
            _viewModel?.RequestShowSource(secondary: false);
            return;
        }

        // Check for Option+2 or Alt+2 (View Source - Burmese 2010)
        if (e.Key == Key.D2 && e.KeyModifiers.HasFlag(KeyModifiers.Alt))
        {
            _logger.Debug("*** VIEW SOURCE 2010 SHORTCUT DETECTED IN BookDisplayView ***");
            e.Handled = true; // Prevent further processing
            _viewModel?.RequestShowSource(secondary: true);
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

                // #621: a click into the book's TEXT is invisible to Avalonia — CEF owns that surface — so
                // without this the next window-level command would target whichever pane happens to be
                // first in tree order. Subscribed here rather than once in the constructor because
                // TryCreateWebView runs again after every float/unfloat dispose-and-recreate, so the
                // subscription follows the browser's lifecycle for free. Reads _viewModel at event time:
                // ControlRecycling can rebind this View to a different book.
                if (_webView is Controls.CstWebView focusReporter)
                    focusReporter.BrowserGotFocus += () =>
                        App.ServiceProvider?.GetService<ActiveDocumentTracker>()?.Note(_viewModel, "browser-focus:book");

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
        // Released here rather than on detach: the zoom subscription is on a singleton, so leaving it
        // attached would root this View (and its rendered DOM) for the session — the same leak shape BOOK-1
        // describes for the browser itself. Detach is the recycled tab-switch/float path and must NOT
        // unsubscribe, or a re-attached tab would stop following zoom changes. (#572)
        UnsubscribeFromZoomChanges();
        // #570: drop the find handler's reference back into this view before the browser goes away.
        _findHandler = null;
        _findHandlerAttached = false;
        DisposeWebView();
    }

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);
        this.PropertyChanged += OnIsVisibleChanged;
        // Seed the resize baseline from the CURRENT bounds: we subscribe after the first layout pass, so the
        // "0 -> first real size" Bounds event has usually already fired unobserved. Without this the first real
        // resize is swallowed as "first measure" (a single-event maximize/snap would never restore), and on a
        // reattach the baseline would keep the OLD pane's size and fire a phantom gesture. (#434, fable §1)
        if (this.Bounds.Width > 0 && this.Bounds.Height > 0)
        {
            _lastKnownWidth = this.Bounds.Width;
            _lastKnownHeight = this.Bounds.Height;
        }
        _logger.Information("BookDisplayView OnLoaded called");
        // Now attached and styled, so the counter's font is resolved — size its reserve. Covers a
        // recycled view whose ViewModel already has TotalHits set (no fresh PropertyChanged). (#196)
        UpdateHitCounterWidth();
        SetupCSharpScrollTracking();
    }

    private void OnAttachedToVisualTree(object? sender, VisualTreeAttachmentEventArgs e)
    {
        // PHASE 2 LOGGING: Track lifecycle events to determine if tab reordering triggers detachment
        _logger.Information("━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━━");
        _logger.Information("▶▶▶ ATTACHED to visual tree - Book: {BookFile}, Instance: {InstanceId}",
            _viewModel?.Book?.FileName ?? "null", _viewModel?.Id ?? "null");

        // Get the new window this view is attached to
        var newWindow = this.GetVisualRoot() as Window;

        // #458 invariant check: a LIVE browser must never re-attach to a window other than the one it was
        // born in — that is the crash. With dispose-before-move in place this can't happen; if it ever does,
        // this Error is the early warning that a re-parent path is missing the guard (better a log than a
        // SIGSEGV). _webView != null means the browser is still live (not disposed for a rebuild).
        if (newWindow != null && _webView != null && _browserBirthWindow != null &&
            !ReferenceEquals(_browserBirthWindow, newWindow))
        {
            _logger.Error("*** #458 VIOLATION: live WebView re-attaching to a different window — Book: {BookFile}, born in {OldHash}, now {NewHash}. A re-parent path is carrying a live browser (crash risk). ***",
                _viewModel?.Book?.FileName ?? "null", _browserBirthWindow.GetHashCode(), newWindow.GetHashCode());
        }

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
                _browserBirthWindow = newWindow;  // fresh browser born here (#458)

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
                // The browser (built in the ctor's TryCreateWebView) binds to the window it first renders in —
                // here. Recording it lets the invariant check above catch any later live cross-window attach. (#458)
                _browserBirthWindow ??= newWindow;
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
        else if (e.Property == BoundsProperty)
        {
            OnViewResized();
        }
    }

    /// <summary>
    /// #434 resize consumer. A window/pane resize reflows the text, but the browser keeps the same native
    /// scrollTop PIXEL — so the content moves under it and the reading position drifts. Resize events fire
    /// AFTER layout has already changed, so there is no pre-reflow moment to capture; instead snapshot the
    /// rolling token on the FIRST event of the gesture (it still holds the pre-resize position, because the
    /// 200ms status tick hasn't re-captured the drifted one yet), debounce until the gesture settles, then
    /// restore that snapshot. ScrollToPositionToken is cache-free and re-interpolates against the anchors'
    /// NEW post-reflow positions, so it lands on the same TEXT rather than the same pixel.
    /// </summary>
    private void OnViewResized()
    {
        var b = this.Bounds;
        if (b.Width <= 0 || b.Height <= 0) return;

        bool firstMeasure = _lastKnownWidth <= 0 || _lastKnownHeight <= 0;
        double dw = Math.Abs(b.Width - _lastKnownWidth);
        _lastKnownHeight = b.Height;   // tracked, but height never gates a gesture (it doesn't re-wrap text)
        if (firstMeasure)
        {
            _lastKnownWidth = b.Width;
            return;
        }

        // Only a WIDTH change re-wraps the text. A height-only drag doesn't reflow, so native scrollTop is
        // already lossless there and restoring could only lose (it would re-apply a slightly stale token).
        // This also ignores position-only (X/Y) Bounds changes. (fable §5)
        //
        // Deliberately do NOT commit the width baseline below the threshold: sub-pixel deltas must ACCUMULATE,
        // or a very slow drag (<1 DIP per event) would re-baseline every step and never start a gesture at all.
        if (dw < 1) return;
        _lastKnownWidth = b.Width;

        // Nothing meaningful to preserve until the cache has produced a position, and never fight a hidden tab.
        if (!_anchorCacheBuilt || !this.IsVisible) return;

        if (!_resizeInProgress)
        {
            _resizeInProgress = true;
            _resizeRestoreToken = _lastPositionToken;   // still the PRE-reflow position at this instant
            _logger.Debug("Resize started - snapshotted reading position (above={Above}, below={Below}, frac={Frac})",
                _resizeRestoreToken?.Above, _resizeRestoreToken?.Below, _resizeRestoreToken?.Fraction);
        }

        // (Re)start the settle debounce — restore only once the gesture stops.
        if (_resizeSettleTimer == null)
        {
            _resizeSettleTimer = new System.Timers.Timer(ResizeSettleMs) { AutoReset = false };
            _resizeSettleTimer.Elapsed += (_, __) => Dispatcher.UIThread.Post(RestoreAfterResize);
        }
        _resizeSettleTimer.Stop();
        _resizeSettleTimer.Start();
    }

    private void RestoreAfterResize()
    {
        // A newer resize event re-armed the timer between Elapsed and this UI-thread post — or the user simply
        // paused >250ms mid-drag. The gesture is still running, so KEEP the original pre-resize snapshot and let
        // the real settle do the restore. Without this the gesture splits: the next segment would snapshot the
        // already-drifted token (the 200ms tick has re-captured by then) and commit the drift. (fable §3)
        if (_resizeSettleTimer?.Enabled == true) return;

        _resizeInProgress = false;
        var token = _resizeRestoreToken;
        _resizeRestoreToken = null;
        if (token == null || !this.IsVisible) return;

        _logger.Debug("Resize settled - restoring reading position (above={Above}, below={Below}, frac={Frac})",
            token.Above, token.Below, token.Fraction);
        ScrollToPositionToken(token);
    }

    #region Book text zoom (#572)

    // Zoom is stored per script by BookZoomService and pushed into Chromium's own browser-level zoom, which
    // scales every stylesheet class proportionally — including the heading ladder — for free. That is why
    // this uses CefBrowserHost.SetZoomLevel rather than injecting CSS: #574 left the stylesheets with
    // absolute pt sizes on ~15 classes, and CSS would have to override every one of them.
    private IBookZoomService? _bookZoomService;
    private EventHandler<BookZoomChangedEventArgs>? _zoomChangedHandler;
    private System.Timers.Timer? _zoomSettleTimer = null;
    private ReadingPositionToken? _zoomRestoreToken = null;
    // Marks a zoom BURST in progress, exactly as _resizeInProgress does for a drag. Holding Cmd+ forwards
    // auto-repeat, and the ~200ms status tick keeps re-capturing _lastPositionToken throughout — so by the
    // second step the rolling token already describes the DRIFTED position, and re-snapshotting per step
    // would commit that drift. Snapshot once, on the first step, while the token is still pre-zoom.
    // (fable review)
    private bool _zoomInProgress = false;
    // Bumped on every completed navigation. A deferred scroll restoration records the generation that armed
    // it, so one belonging to a superseded document can be recognised and dropped. -1 means none pending.
    private int _navGeneration = 0;
    private int _deferredScrollGeneration = -1;
    // 1 while a finished zoom burst is waiting for the reflow to land before restoring the position.
    private int _zoomAwaitingCacheBuilt = 0;
    // Identifies the current await. DispatcherTimer.RunOnce cannot be cancelled, so a backstop from a
    // superseded burst stays in flight; stamping it lets the stale one recognise itself and do nothing.
    private int _zoomAwaitGeneration = 0;
    // The navigation the current await belongs to, so a restore cannot be applied to a replaced document.
    private int _zoomAwaitNavGeneration = -1;
    // Backstop only. The real trigger is CACHE_BUILT; this bounds the wait if the page never reports one
    // (the same failure the anchor-cache watchdog exists for). Generous, because firing early is the very
    // bug this replaced — a late restore is merely visible, an early one is wrong.
    private const int ZoomReflowBackstopMs = 1500;

    /// <summary>
    /// Subscribes this view to zoom changes for its script. Called once from the constructor.
    ///
    /// The subscription is on a singleton service, so it roots this View until released — the same leak
    /// shape the FontService subscription had. <see cref="UnsubscribeFromZoomChanges"/> runs from the
    /// existing shutdown path.
    /// </summary>
    private void SubscribeToZoomChanges()
    {
        _bookZoomService = App.ServiceProvider?.GetService(typeof(IBookZoomService)) as IBookZoomService;
        if (_bookZoomService == null)
        {
            _logger.Debug("Book zoom service unavailable - zoom will not apply to this view");
            return;
        }

        _zoomChangedHandler = (_, args) =>
        {
            // Zoom is per script, so a change fires for every open book; only those showing that script care.
            if (_viewModel == null || _viewModel.BookScript != args.Script) return;
            Dispatcher.UIThread.Post(() => ApplyZoom(args.Zoom, preservePosition: true));
        };
        _bookZoomService.ZoomChanged += _zoomChangedHandler;
    }

    private void UnsubscribeFromZoomChanges()
    {
        if (_bookZoomService != null && _zoomChangedHandler != null)
            _bookZoomService.ZoomChanged -= _zoomChangedHandler;
        _zoomChangedHandler = null;

        _zoomSettleTimer?.Stop();
        _zoomSettleTimer?.Dispose();
        _zoomSettleTimer = null;

        // Clear the await so a CACHE_BUILT or backstop arriving after shutdown cannot try to scroll a
        // disposed browser. RestoreZoomTokenNow also checks _isShutDown; this makes it two locks on one door.
        CancelPendingZoomAwait();
        _zoomRestoreToken = null;
    }

    /// <summary>
    /// Pushes this script's stored zoom into the browser without touching the reading position.
    ///
    /// Called from <c>OnNavigationCompleted</c>, which covers every route that produces a fresh browser:
    /// first load, a script change (which reloads), and the float/unfloat cycle that disposes and recreates
    /// the WebView. Chromium also keeps its own per-origin zoom inside the request context, and all books
    /// share an origin — so without setting it explicitly on every load, a zoom set while reading one script
    /// would leak into the next book regardless of its script. Setting it unconditionally here overwrites
    /// whatever Chromium remembered, which is why this runs even when the value is 1.0.
    /// </summary>
    /// <returns>
    /// True when a zoom other than 100% was applied, i.e. the document is about to reflow. The caller uses
    /// this to defer resolving any scroll target until the new layout exists.
    /// </returns>
    private bool ApplyStoredZoomOnLoad()
    {
        if (_bookZoomService == null || _viewModel == null) return false;

        var zoom = _bookZoomService.GetZoom(_viewModel.BookScript);
        ApplyZoom(zoom, preservePosition: false);
        return Math.Abs(zoom - 1.0) > 0.001;
    }

    /// <summary>
    /// Sets the browser's zoom level.
    ///
    /// <paramref name="preservePosition"/> is false on a fresh load — there is no position to keep yet, and
    /// the saved-anchor restoration in <c>ExecutePendingRestoration</c> owns where the page lands. It is true
    /// for a live zoom change, where the reflow would otherwise move the text out from under the reader.
    /// </summary>
    private void ApplyZoom(double zoom, bool preservePosition)
    {
        if (_isShutDown || _webView == null || !_isBrowserInitialized) return;

        var host = CefBrowserAccess.TryGetBrowserHost(_webView, _logger);
        if (host == null)
        {
            // Either the reflection chain broke on a package upgrade (CefBrowserAccess.Probe says so at
            // startup) or the browser is mid-teardown. Neither is worth an exception on a keystroke.
            _logger.Debug("Zoom skipped - no browser host available");
            return;
        }

        if (preservePosition)
        {
            // A press arriving while the PREVIOUS burst is still waiting for its reflow signal is a
            // continuation of that burst, not a new one — the user simply hesitated. Cancelling the pending
            // await and resuming keeps the original pre-zoom token. Without this the await would be consumed
            // mid-burst (restoring a half-zoomed position and nulling the token), and the real final restore
            // would then find nothing to restore. (fable review)
            if (CancelPendingZoomAwait())
            {
                _zoomInProgress = true;
                _logger.Debug("Zoom resumed before the previous burst's reflow landed - keeping the original snapshot");
            }
        }

        // Snapshot BEFORE the reflow, and only on the FIRST step of a burst. The rolling token is refreshed
        // by the ~200ms status tick, so it is at most one tick stale but still pre-reflow — the best
        // available, because a zoom's resize event only arrives after layout has already moved. Re-taking it
        // on later steps would capture the drift instead. (Same two-part discipline as the resize path.)
        if (preservePosition && !_zoomInProgress && _anchorCacheBuilt && this.IsVisible)
        {
            _zoomInProgress = true;
            _zoomRestoreToken = _lastPositionToken;
            _logger.Debug("Zoom started - snapshotted reading position (above={Above}, below={Below}, frac={Frac})",
                _zoomRestoreToken?.Above, _zoomRestoreToken?.Below, _zoomRestoreToken?.Fraction);
        }

        try
        {
            var level = BookZoomService.ToCefZoomLevel(zoom);
            host.SetZoomLevel(level);
            _logger.Information("Applied book zoom {Zoom:P0} (CEF level {Level:0.###}) to {Script}",
                zoom, level, _viewModel?.BookScript);
        }
        catch (Exception ex)
        {
            _logger.Error("Failed to set zoom level | {Details}", ex.Message);
            _zoomRestoreToken = null;
            _zoomInProgress = false;   // or the burst flag would latch and suppress the next real snapshot
            return;
        }

        if (preservePosition) StartZoomSettle();
    }

    /// <summary>
    /// Restores the reading position once the zoom reflow has settled.
    ///
    /// This detects only that the KEYPRESSES have stopped; it does not mean the reflow has landed, which is
    /// why <see cref="RestoreAfterZoom"/> then waits for CACHE_BUILT rather than restoring here. A repeated
    /// press re-arms the timer, so holding Cmd+ restores once at the end rather than fighting each
    /// intermediate step.
    /// </summary>
    private void StartZoomSettle()
    {
        if (_zoomSettleTimer == null)
        {
            _zoomSettleTimer = new System.Timers.Timer(ResizeSettleMs) { AutoReset = false };
            _zoomSettleTimer.Elapsed += (_, __) => Dispatcher.UIThread.Post(RestoreAfterZoom);
        }
        _zoomSettleTimer.Stop();
        _zoomSettleTimer.Start();
    }

    private void RestoreAfterZoom()
    {
        // Another zoom step landed while this was queued (or the user simply paused mid-burst) — the burst
        // is still running, so keep the original pre-zoom snapshot and let the real settle restore it.
        // Returning WITHOUT clearing _zoomInProgress is the point: it is what stops the next step
        // re-snapshotting the drifted token. (Mirrors RestoreAfterResize.)
        if (_zoomSettleTimer?.Enabled == true) return;

        _zoomInProgress = false;
        if (_isShutDown) return;

        if (_zoomRestoreToken == null || !this.IsVisible)
        {
            _zoomRestoreToken = null;
            return;
        }

        // The burst has ended, but that does NOT mean the reflow has landed. SetZoomLevel is a
        // browser→renderer IPC round trip, so the keypresses stopping tells us nothing about whether the
        // renderer has finished laying out at the new zoom. Restoring on a fixed delay computes the scroll
        // target against whatever layout happens to exist at that moment — which is the old one whenever
        // the renderer is slow.
        //
        // "Slow" is not hypothetical: with a second book open in a floating window, BOTH browsers reflow on
        // every step, and a fast zoom in and out reliably lost the position on the focused book while the
        // single-book case looked fine. Same open-loop weakness the load path had; this is the same fix.
        //
        // The zoom's own resize event drives the (debounced) anchor-cache rebuild in the page, and build()
        // emits CACHE_BUILT only after reading real positions — so that signal means layout has genuinely
        // settled at the new zoom. Wait for it, with a backstop in case it never comes.
        //
        // Ordering is correct BY CONSTRUCTION rather than by luck: the in-page debounce
        // (AnchorRebuildDebounceMs) is deliberately longer than this settle (ResizeSettleMs), and its timer
        // starts from the renderer's last resize — which necessarily postdates the last SetZoomLevel
        // arriving. So the CACHE_BUILT that proves the reflow landed can never precede the await being
        // armed here. An earlier revision instead compared a build counter to detect a signal that had
        // already arrived; that was unsound, because a build could START before the zoom and have its title
        // reach C# after, satisfying the counter while reflecting the OLD layout. Making the two delays
        // ordered removes the hole rather than testing for it. (fable review)
        var awaitGeneration = Interlocked.Increment(ref _zoomAwaitGeneration);
        Volatile.Write(ref _zoomAwaitNavGeneration, Volatile.Read(ref _navGeneration));
        Interlocked.Exchange(ref _zoomAwaitingCacheBuilt, 1);

        _logger.Debug("Zoom burst ended - awaiting CACHE_BUILT before restoring (above={Above}, below={Below}, frac={Frac})",
            _zoomRestoreToken.Above, _zoomRestoreToken.Below, _zoomRestoreToken.Fraction);

        // Stamped with the await it belongs to: an uncancellable RunOnce from a PREVIOUS burst would
        // otherwise still be in flight and could consume a later burst's await early — restoring against an
        // unsettled layout, which is the very bug this waiting exists to prevent. (fable review)
        DispatcherTimer.RunOnce(() => RestoreZoomTokenNow(awaitGeneration),
            TimeSpan.FromMilliseconds(ZoomReflowBackstopMs));
    }

    /// <summary>
    /// Cancels a pending post-zoom await, returning true if one was actually pending. Bumping the
    /// generation is what neutralises the backstop timer already in flight for it.
    /// </summary>
    private bool CancelPendingZoomAwait()
    {
        if (Interlocked.Exchange(ref _zoomAwaitingCacheBuilt, 0) == 0) return false;
        Interlocked.Increment(ref _zoomAwaitGeneration);
        return true;
    }

    /// <summary>
    /// Performs the post-zoom position restore exactly once, for the await identified by
    /// <paramref name="awaitGeneration"/> — whichever of the CACHE_BUILT signal or the backstop reaches it
    /// first. A stale caller (a backstop from a superseded burst, or a signal belonging to a document that
    /// has since been replaced) is dropped.
    /// </summary>
    private void RestoreZoomTokenNow(int awaitGeneration)
    {
        if (Volatile.Read(ref _zoomAwaitGeneration) != awaitGeneration) return;

        // A navigation since the await was armed means this token describes a document that no longer
        // exists. Anchor names are stable across scripts, so the scroll would SUCCEED rather than no-op —
        // landing the new document at the old position and fighting ExecutePendingRestoration. (fable review)
        if (Volatile.Read(ref _navGeneration) != Volatile.Read(ref _zoomAwaitNavGeneration))
        {
            _logger.Debug("Dropping post-zoom restore - the document was replaced while waiting");
            Interlocked.Exchange(ref _zoomAwaitingCacheBuilt, 0);
            _zoomRestoreToken = null;
            return;
        }

        // Interlocked: the signal and the backstop can both arrive, and only the first may act.
        if (Interlocked.Exchange(ref _zoomAwaitingCacheBuilt, 0) == 0) return;

        var token = _zoomRestoreToken;
        _zoomRestoreToken = null;
        if (token == null || _isShutDown || !this.IsVisible) return;

        _logger.Debug("Zoom reflow settled - restoring reading position (above={Above}, below={Below}, frac={Frac})",
            token.Above, token.Below, token.Fraction);
        ScrollToPositionToken(token);
    }

    /// <summary>Zoom in one step. Public so the menu items and keyboard shortcuts can drive it.</summary>
    public void ZoomIn() => StepZoom(s => s.ZoomIn(_viewModel!.BookScript), "in");

    /// <summary>Zoom out one step.</summary>
    public void ZoomOut() => StepZoom(s => s.ZoomOut(_viewModel!.BookScript), "out");

    /// <summary>Back to 100% — the shipped stylesheet sizes exactly.</summary>
    public void ResetZoom() => StepZoom(s => s.ResetZoom(_viewModel!.BookScript), "reset");

    /// <summary>
    /// Set an arbitrary zoom. Public so the toolbar percentage box can drive it. (#618)
    ///
    /// Goes through <see cref="StepZoom"/> like the other three rather than calling CEF: the service raises
    /// ZoomChanged, every view showing this script applies it, and that is what keeps a second tab from
    /// disagreeing with this one about what it is displaying.
    /// </summary>
    public void SetZoom(double zoom) => StepZoom(s => s.SetZoom(_viewModel!.BookScript, zoom), "set");

    // The toolbar percentage box. The step buttons need no state — they call the same ZoomIn/ZoomOut the
    // keyboard does — but the box has to be put back to the stored value after every commit, so it is held.
    private TextBox? _zoomEntryBox;

    private void SetupZoomControl()
    {
        _zoomEntryBox = this.FindControl<TextBox>("zoomEntryBox");
        if (_zoomEntryBox != null)
        {
            _zoomEntryBox.KeyDown += OnZoomEntryKeyDown;
            // Committing on focus loss costs nothing: SetZoom no-ops when the value is unchanged, which is
            // what leaving the box untouched produces.
            _zoomEntryBox.LostFocus += (_, _) => CommitZoomEntry();
            // Select on focus so typing replaces the percentage instead of appending to it. Posted, not
            // immediate: on a pointer-initiated focus the TextBox positions the caret from the click AFTER
            // raising GotFocus, which would undo a selection made here and leave typing inserting into
            // "100%" — the very thing this exists to prevent. (fable review)
            _zoomEntryBox.GotFocus += (_, _) => Dispatcher.UIThread.Post(() => _zoomEntryBox?.SelectAll());
        }

        var zoomOut = this.FindControl<Button>("zoomOutButton");
        var zoomIn = this.FindControl<Button>("zoomInButton");
        if (zoomOut != null) zoomOut.Click += (_, _) => ZoomOut();
        if (zoomIn != null) zoomIn.Click += (_, _) => ZoomIn();
    }

    private void OnZoomEntryKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CommitZoomEntry();
                e.Handled = true;
                break;
            case Key.Escape:
                // Abandon the edit. Handled so it stays in the box rather than reaching the find bar's
                // close-on-Escape.
                RevertZoomEntryText();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Applies what is in the box, then puts the box back to what is actually stored.
    ///
    /// The second half is not redundant. A rejected entry must not sit there looking applied, and an
    /// accepted one has been clamped and rounded on the way through the service — so the box showing "500"
    /// while the book renders at 300% would be the control lying about the state it exists to report.
    /// </summary>
    private void CommitZoomEntry()
    {
        if (_isShutDown || _zoomEntryBox == null) return;

        if (BookZoomReadout.TryParseEntry(_zoomEntryBox.Text, out var zoom))
            SetZoom(zoom);

        RevertZoomEntryText();
    }

    private void RevertZoomEntryText()
    {
        // StepZoom is synchronous on the UI thread, so by now the service holds the new value and the VM
        // reads it straight through; the binding's own update arrives later and sets the same text.
        // SetCurrentValue rather than the CLR setter: the box's Text carries a binding to ZoomDisplay, and
        // this must not displace it — the binding is how a change made in another tab reaches this box.
        if (_zoomEntryBox != null && _viewModel != null)
            _zoomEntryBox.SetCurrentValue(TextBox.TextProperty, _viewModel.ZoomDisplay);
    }

    private void StepZoom(Func<IBookZoomService, double> step, string what)
    {
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(() => StepZoom(step, what));
            return;
        }
        if (_bookZoomService == null || _viewModel == null || _isShutDown) return;

        // The service persists and raises ZoomChanged; this view applies it through that event like any
        // other, so a second tab showing the same script updates by exactly the same route.
        var zoom = step(_bookZoomService);
        _logger.Debug("Zoom {What} requested for {Script} - now {Zoom:P0}", what, _viewModel.BookScript, zoom);
    }

    #endregion

    #region Book information (#628)

    private void SetupBookInfoPanel()
    {
        var copy = this.FindControl<Button>("copyXmlFileNameButton");
        if (copy != null) copy.Click += (_, _) => CopyXmlFileName();
    }

    /// <summary>
    /// Puts the source file name on the clipboard — the field that gets pasted into a correction report
    /// every time, so it is worth a click rather than a select-and-copy.
    /// </summary>
    private async void CopyXmlFileName()
    {
        try
        {
            var fileName = _viewModel?.XmlFileName;
            if (string.IsNullOrEmpty(fileName)) return;

            // GetTopLevel rather than a cached window: this View is recycled across float/unfloat, so the
            // window it belongs to is not the one it was created in.
            var clipboard = TopLevel.GetTopLevel(this)?.Clipboard;
            if (clipboard == null)
            {
                _logger.Warning("Copy file name: no clipboard available");
                return;
            }

            await clipboard.SetTextAsync(fileName);
            _logger.Information("Copied XML file name to clipboard: {FileName}", fileName);
        }
        catch (Exception ex)
        {
            // async void: an escaping exception here would be an unhandled crash, not a failed copy.
            _logger.Error(ex, "Failed to copy the XML file name to the clipboard");
        }
    }

    #endregion

    #region Find in Page (#570)

    // Chromium's own find, reached through CefBrowserAccess. Highlighting of every match with the active
    // one distinct, auto-scroll, wrap-around and match counts all come free.
    //
    // Matching is a literal substring, case-insensitive, with no folding and no script conversion. That is
    // deliberate: the corpus capitalises programmatically at sentence starts rather than semantically, so
    // case carries nothing searchable; and a reader working across scripts copies text out of the book
    // rather than typing it, so the query already arrives in the right form. Converting it would risk
    // corrupting a query that was correct.
    private Border? _findBar;
    private TextBox? _findQueryBox;
    private TextBlock? _findCountText;
    private CstFindHandler? _findHandler;
    private bool _findHandlerAttached;
    // Folding Chromium's multi-reply result stream into one counter. Extracted so it can be unit tested:
    // it is the part of this feature that has already been wrong once. (fable review)
    private readonly FindResultAccumulator _findResults = new();
    // Identifies a ShowFindBar call across its await, so an orphaned one cannot clobber a newer one.
    private int _findOpenGeneration;

    private void SetupFindBar()
    {
        _findBar = this.FindControl<Border>("findBar");
        _findQueryBox = this.FindControl<TextBox>("findQueryBox");
        _findCountText = this.FindControl<TextBlock>("findCountText");

        if (_findQueryBox != null)
        {
            // Incremental: each keystroke restarts the search (findNext: false), which is how Chromium
            // narrows as you type and keeps the count live.
            _findQueryBox.TextChanged += (_, _) => RunFind(forward: true, findNext: false);
            _findQueryBox.KeyDown += OnFindQueryKeyDown;
        }

        var prev = this.FindControl<Button>("findPrevButton");
        var next = this.FindControl<Button>("findNextButton");
        var close = this.FindControl<Button>("findCloseButton");
        if (prev != null) prev.Click += (_, _) => RunFind(forward: false, findNext: true);
        if (next != null) next.Click += (_, _) => RunFind(forward: true, findNext: true);
        if (close != null) close.Click += (_, _) => HideFindBar();
    }

    private void OnFindQueryKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                // Shift+Enter reverses — the standard find-bar idiom. findNext: true advances within the
                // existing search rather than restarting it.
                RunFind(forward: !e.KeyModifiers.HasFlag(KeyModifiers.Shift), findNext: true);
                e.Handled = true;
                break;
            case Key.Escape:
                HideFindBar();
                e.Handled = true;
                break;
        }
    }

    /// <summary>
    /// Opens the find bar and focuses it. Prefills from the book's current selection, which makes ⌘F and
    /// ⌘⇧F symmetrical — the same selection, searched here or across the corpus.
    /// </summary>
    public async void ShowFindBar()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(ShowFindBar); return; }
        if (_isShutDown || _findBar == null || _findQueryBox == null) return;

        if (!CefBrowserAccess.IsAvailable)
        {
            // The reflection chain broke on a package upgrade. Opening a bar that cannot search would be
            // worse than not opening one; Probe has already said so in the startup log.
            _logger.Warning("Find in Page unavailable - CEF browser access did not resolve");
            return;
        }

        AttachFindHandler();

        // The selection round-trip can take up to 700ms, and _lookupSelectionTcs is a SHARED field (⌘D
        // uses it too). A second ⌘F — or a ⌘D — replaces it, so this call's TCS never completes and it
        // sleeps out the full timeout before resuming. By then the user may have typed a query, which the
        // tail below would then select-all and the next keystroke would wipe. Stamp the call and let the
        // orphan recognise itself. (fable review)
        var generation = ++_findOpenGeneration;

        var selection = await GetWebViewSelectionAsync();

        // Re-check everything that could have changed across the await: a newer open superseded this one,
        // or the tab was closed while it was outstanding.
        if (generation != _findOpenGeneration || _isShutDown || _findBar == null || _findQueryBox == null)
            return;

        if (!string.IsNullOrWhiteSpace(selection))
        {
            // First line only: a multi-line selection as a query matches nothing and reads as broken.
            var firstLine = selection.Split('\n')[0].Trim();
            if (firstLine.Length > 0) _findQueryBox.Text = firstLine;
        }

        _findBar.IsVisible = true;
        _findQueryBox.Focus();
        _findQueryBox.SelectAll();
        // Re-run whatever is in the box: reopening on a remembered query should light its matches again,
        // rather than showing an empty count beside text that looks searched.
        RunFind(forward: true, findNext: false);
    }

    /// <summary>Closes the bar, clears Chromium's highlighting, and returns focus to the book.</summary>
    public void HideFindBar()
    {
        if (!Dispatcher.UIThread.CheckAccess()) { Dispatcher.UIThread.Post(HideFindBar); return; }
        if (_findBar == null || !_findBar.IsVisible) return;

        _findBar.IsVisible = false;
        if (_findCountText != null) _findCountText.Text = "";

        // clearSelection: FALSE — keep the match selected as the bar closes. Chromium's own find bar does
        // the same, and it matters more here than in a browser: the motivating request for this feature was
        // finding your way back to a place in the text, so discarding the selection at the moment the user
        // closes the bar would throw away the answer they just asked for. (fable review)
        StopFinding(clearSelection: false);

        // The query is deliberately left in the box: reopening find in this tab continues where it left
        // off, per tab, which is what every shipped find bar does.
        _webView?.Focus();
    }

    /// <summary>
    /// Clears the active search and its highlighting. Safe when nothing is searching.
    ///
    /// <para>
    /// <paramref name="clearSelection"/> false keeps the found text selected. Closing the bar passes false,
    /// so the match stays visible as your place in the text; clearing the query passes true, because the
    /// selection then belongs to a search the user has explicitly abandoned.
    /// </para>
    /// </summary>
    private void StopFinding(bool clearSelection)
    {
        var host = CefBrowserAccess.TryGetBrowserHost(_webView, _logger);
        if (host == null) return;
        try { host.StopFinding(clearSelection); }
        catch (Exception ex) { _logger.Error("StopFinding failed | {Details}", ex.Message); }
    }

    private void RunFind(bool forward, bool findNext)
    {
        if (_isShutDown || _findBar == null || !_findBar.IsVisible) return;

        var query = _findQueryBox?.Text ?? "";
        if (string.IsNullOrEmpty(query))
        {
            // Emptying the box must clear the highlighting too, or the previous search's matches stay lit
            // with no query on screen to explain them.
            _findResults.Reset();
            if (_findCountText != null) _findCountText.Text = "";
            // true here: the query was deliberately cleared, so the old match is not the user's place any
            // more — leaving it selected would be leftover state from a search they abandoned.
            StopFinding(clearSelection: true);
            return;
        }

        var host = CefBrowserAccess.TryGetBrowserHost(_webView, _logger);
        if (host == null) return;

        // findNext false means a NEW search rather than a step within the current one, so any accumulated
        // position is stale.
        if (!findNext) _findResults.Reset();

        try
        {
            // matchCase: false — case-insensitive, with a known and accepted cost.
            //
            // This flag is not really about case. Chromium's find is ICU string search and it selects the
            // collation STRENGTH: false is PRIMARY, which folds case AND combining marks together; true is
            // TERTIARY, which respects both. There is no case-insensitive-but-diacritic-sensitive setting —
            // it is one knob, so the two properties cannot be chosen independently.
            //
            // Choosing false therefore accepts diacritic folding: "ekaṃ" also matches "ekam" in Latin, and
            // in Myanmar the niggahita is ignored so the search widens to "eka" (294 hits in DN3 rather
            // than 94). Choosing true instead would miss sentence-initial capitals — measured at ~8% of
            // "ekaṃ" occurrences across the 217 books, since the corpus capitalises programmatically at
            // sentence starts rather than semantically.
            //
            // Case-insensitive was judged the better trade for shipping: the missed capitals are invisible
            // to the user, whereas an over-wide match is at least visible and still contains what was
            // sought. Getting BOTH requires matching in JavaScript over the DOM (CSS Custom Highlight API,
            // no DOM mutation) — understood, deliberately deferred, and worth revisiting if the folding
            // proves to matter in use.
            host.Find(query, forward, matchCase: false, findNext: findNext);
        }
        catch (Exception ex)
        {
            _logger.Error("Find failed | {Details}", ex.Message);
        }
    }

    /// <summary>
    /// Attaches the result handler to this browser. Cleared on navigation and re-attached on demand:
    /// float/unfloat disposes and rebuilds the WebView, so a one-shot attach would silently stop reporting
    /// counts after the first float.
    /// </summary>
    private void AttachFindHandler()
    {
        if (_findHandlerAttached || _isShutDown) return;

        var browser = CefBrowserAccess.TryGetChromiumBrowser(_webView, _logger);
        if (browser == null) return;

        _findHandler ??= new CstFindHandler(OnFindResult);
        browser.FindHandler = _findHandler;   // public setter on a public type - no reflection here
        _findHandlerAttached = true;
        _logger.Debug("Find handler attached");
    }

    private void OnFindResult(FindResultEventArgs e)
    {
        if (_isShutDown || _findBar == null || !_findBar.IsVisible) return;

        // A reply for a query that no longer exists must not paint a count. StopFinding self-posts to the
        // CEF UI thread, so the previous search's final reply can still arrive AFTER the box was emptied
        // and the highlighting cleared — leaving "1/94" beside an empty box with nothing highlighted, and
        // nothing further to correct it. (fable review)
        if (string.IsNullOrEmpty(_findQueryBox?.Text)) return;

        // Accept returns false only for a reply belonging to a superseded search. Whether there is yet
        // anything worth showing is Format's business — it returns "" until an authoritative total exists.
        if (!_findResults.Accept(e.Identifier, e.Count, e.ActiveMatchOrdinal, e.FinalUpdate)) return;

        var text = _findResults.Format();
        if (_findCountText != null && text.Length > 0) _findCountText.Text = text;
    }

    #endregion


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

            // No lock-lifecycle logging here, unlike the other JS callers: this runs on the ~200ms status
            // tick, per book view, so six Debug lines per tick was 89% of an entire Debug-level log — about
            // 60 lines a second with two books open, which buried everything else. The failure branch below
            // is kept, because a lock that cannot be acquired is the only outcome worth knowing about.
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
            if (!_anchorCacheBuilt || _webView == null)
            {
                _logger.Debug("UpdateScrollBasedStatus skipped - anchorCacheBuilt: {AnchorCacheBuilt}, browser: {HasBrowser}", _anchorCacheBuilt, _webView != null);
                return;
            }

            // Try to get scroll position and status in a single JavaScript call
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

                    // #434 rolling reading-position bracket, piggybacked on this same tick (no extra round-trip)
                    // so the View always has a fresh token to restore on tab reattach (#31). Isolated in its own
                    // try so a bracket glitch can never disturb the status readout above.
                    var ptA = '', ptAP = '', ptB = '', ptBP = '';
                    try {{
                        var ptc = window.cstAnchorCache;
                        if (ptc && ptc.isBuilt && ptc.sortedAllAnchors && ptc.sortedAllAnchors.length > 0) {{
                            var ptl = ptc.sortedAllAnchors;
                            var ptIdx = -1;
                            for (var pi = 0; pi < ptl.length; pi++) {{ if (ptl[pi].position <= scrollY) ptIdx = pi; else break; }}
                            ptA = ptIdx >= 0 ? ptl[ptIdx].name : '';
                            ptB = (ptIdx + 1 < ptl.length) ? ptl[ptIdx + 1].name : '';
                            var ptLive = function(nm) {{
                                if (!nm) return '';
                                var el = document.querySelector('a[name=' + JSON.stringify(nm) + ']') || document.getElementById(nm);
                                return el ? Math.round(el.getBoundingClientRect().top + window.pageYOffset) : '';
                            }};
                            ptAP = ptLive(ptA); ptBP = ptLive(ptB);
                        }}
                    }} catch(ptErr) {{ }}

                    // ATOMIC UPDATE: Send all status info in one message with tab ID including chapter and best anchor
                    // SCROLL is ROUNDED. On a Retina display scroll offsets quantize to half CSS pixels, so
                    // scrollIntoView (chapter jump / anchor restore) rests at the element's fractional layout
                    // position and scrollY comes out like 76563.5. The C# side parsed that as an int, which
                    // silently failed and left 0 — poisoning the reading-position token. Matches what
                    // GetCurrentPositionTokenAsync has always emitted. (#551)
                    document.title = 'CST_STATUS_UPDATE:VRI=' + vri + '|MYANMAR=' + myanmar + '|PTS=' + pts + '|THAI=' + thai + '|OTHER=' + other + '|PARA=' + para + '|CHAPTER=' + currentChapter + '|ANCHOR=' + bestAnchor + '|SCROLL=' + Math.round(scrollY) + '|PTA=' + ptA + '|PTAP=' + ptAP + '|PTB=' + ptB + '|PTBP=' + ptBP + '|TAB:__TAB_ID_PLACEHOLDER__';
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
                        headingPages: [],
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

                            // #542: resolve each heading region's pages now that every anchor list is built
                            // and sorted. Runs BEFORE isBuilt so a query can never see a populated cache with an
                            // empty heading table; a failure degrades to the unchanged last-marker rule.
                            try {{ this.buildHeadingPages(); }} catch (e) {{ this.headingPages = []; }}

                            // Populated — status queries may now trust this cache. Set BEFORE the title
                            // signal so the C# side can never observe CACHE_BUILT ahead of the data. (#423)
                            this.isBuilt = true;

                            // |SEQ makes a rebuild with identical counts a *distinct* title so TitleChanged
                            // fires again (the #156 identical-title-fires-no-event hazard); parsers scan
                            // parts for their own prefixes, so the extra part is ignored. (#423)
                            document.title = 'CST_STATUS_UPDATE:CACHE_BUILT=' + Object.keys(this.pageAnchors).length + ',' + Object.keys(this.paragraphAnchors).length + ',' + Object.keys(this.chapterAnchors).length + '|TAB:__TAB_ID_PLACEHOLDER__' + '|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                        }},
                        
                        // #542: precompute the page references that apply to each HEADING region.
                        //
                        // A section's page marker sits AFTER the first word of the first body paragraph following the
                        // heading -- the markers derive from page-number footnotes, and a heading is never left dangling
                        // at the foot of a page, so it travels with its text onto the new page. So for a position inside
                        // a heading, the governing marker may be the NEXT one rather than the last.
                        //
                        // Decided PER EDITION, because the editions break pages in different places. At the heading
                        // '(7) 2. Sukhavaggavannana' the next V marker (2.0052) sits after the first word of the very next
                        // paragraph and DOES apply, while the next M marker (2.0054) is nine paragraphs later and does NOT
                        // -- M stays 2.0053. Only the geometry distinguishes them.
                        //
                        // Resolved once here rather than on every scroll tick: the status path stays a lookup, the result
                        // is inspectable, and two queries inside one heading cannot disagree.

                        buildHeadingPages: function() {{
                            var BODY = {{ bodytext:1, indent:1, unindented:1, hangnum:1,
                                         gatha1:1, gatha2:1, gatha3:1, gathalast:1 }};
                            var TOL = 12;   // same-line tolerance: the marker follows the first word of the paragraph

                            function markerFor(list, regionStart, paraStart) {{
                                if (!list || list.length === 0) return null;
                                var last = null, next = null;
                                for (var i = 0; i < list.length; i++) {{
                                    if (list[i].position <= regionStart) {{ last = list[i]; }}
                                    else {{ next = list[i]; break; }}
                                }}
                                // The next marker governs the heading only when it sits at the START of the first body
                                // paragraph (same line as the paragraph number). A marker further into that paragraph
                                // means the page broke mid-paragraph, so the heading is still on the previous page.
                                if (next && paraStart >= 0 && Math.abs(next.position - paraStart) <= TOL) return next;
                                return last;
                            }}

                            var blocks = document.querySelectorAll('p[class]');
                            var regions = [], runStart = -1, prevBodyTop = -1;
                            for (var b = 0; b < blocks.length; b++) {{
                                var el = blocks[b];
                                var top = Math.round(el.getBoundingClientRect().top + window.pageYOffset);
                                if (BODY[el.className || '']) {{
                                    if (runStart >= 0) {{
                                        regions.push({{ start: runStart, paraStart: top, prevBody: prevBodyTop }});
                                        runStart = -1;
                                    }}
                                    prevBodyTop = top;
                                }} else if (runStart < 0) {{
                                    runStart = top;
                                }}
                            }}

                            // A run split by a div boundary: only the part ADJACENT to the body paragraph may look ahead.
                            // At a book boundary the previous book's closing attribution and the new book's salutation are
                            // both class 'centered' and adjacent, but the div opens between them, and the closings belong to
                            // the OLD page. So anything before the last div anchor in the run keeps the previous marker.
                            var divs = this.sortedChapterAnchors || [];
                            this.headingPages = [];
                            for (var r = 0; r < regions.length; r++) {{
                                var reg = regions[r], lastDiv = -1;
                                for (var d = 0; d < divs.length; d++) {{
                                    var dp = divs[d].position;
                                    // Chapter-list navigation scrolls to the div ANCHOR, which sits just above the
                                    // heading it introduces. Without this, the landing position falls in the gap
                                    // ABOVE the region and resolves by the old rule -- the page read one short
                                    // until you scrolled a few pixels. Pull the region start up to the anchor.
                                    if (dp < reg.start && dp > reg.prevBody) reg.start = dp;
                                    // Split ONLY at a BOOK-level anchor (id with no underscore). That is where the
                                    // previous book's closing attribution meets the new book's salutation -- both
                                    // class 'centered', with the div between them -- and the closings belong to the
                                    // OLD page. A NESTED div (pannasaka, vagga) lies entirely past the page turn,
                                    // so splitting there wrongly denied the look-ahead to the heading above it:
                                    // '3. Tatiyapannasakam' read 2.56 instead of 2.57 because the nested an2_3_1
                                    // anchor cut the heading run in two.
                                    if (dp > reg.start && dp <= reg.paraStart && divs[d].name.indexOf('_') === -1) lastDiv = dp;
                                }}
                                if (lastDiv > reg.start) {{
                                    this.headingPages.push(this.resolveRegion(reg.start, lastDiv, -1, markerFor));
                                    this.headingPages.push(this.resolveRegion(lastDiv, reg.paraStart, reg.paraStart, markerFor));
                                }} else {{
                                    this.headingPages.push(this.resolveRegion(reg.start, reg.paraStart, reg.paraStart, markerFor));
                                }}
                            }}
                            this.headingPages.sort(function(a, b) {{ return a.start - b.start; }});
                        }},

                        resolveRegion: function(start, end, paraStart, markerFor) {{
                            var sp = this.sortedPageAnchors;
                            function nm(a) {{ return a ? a.name : '*'; }}
                            return {{ start: start, end: end, pages: {{
                                vri:     nm(markerFor(sp.V, start, paraStart)),
                                myanmar: nm(markerFor(sp.M, start, paraStart)),
                                pts:     nm(markerFor(sp.P, start, paraStart)),
                                thai:    nm(markerFor(sp.T, start, paraStart)),
                                other:   nm(markerFor(sp.O, start, paraStart))
                            }} }};
                        }},

                        headingPageAt: function(docPos) {{
                            var hp = this.headingPages;
                            if (!hp || hp.length === 0) return null;
                            for (var i = 0; i < hp.length; i++) {{
                                if (hp[i].start > docPos) break;
                                if (docPos >= hp[i].start && docPos < hp[i].end) return hp[i].pages;
                            }}
                            return null;
                        }},

                        getPageReferences: function(scrollY) {{
                            var result = {{ vri: '*', myanmar: '*', pts: '*', thai: '*', other: '*' }};
                            var docPos = scrollY + 20; // CST4 algorithm offset

                            // #542: inside a heading the governing marker may be the next one rather than the
                            // last, and which it is differs per edition. Resolved at cache-build time; a lookup
                            // here. Outside a heading, fall through to the unchanged rule below.
                            var headingPages = this.headingPageAt(docPos);
                            if (headingPages) {{
                                return headingPages;
                            }}

                            // PERFORMANCE OPTIMIZATION: Use pre-sorted lists instead of expensive sorting on every call
                            // The findBestAnchor function now works on the pre-sorted lists
                            //
                            // Which marker governs a position (#542):
                            // 1. Normally the last marker at or before it — you are inside the region it governs.
                            // 2. But a section's page marker sits AFTER its heading block(s): the markers derive
                            //    from page-number footnotes in the CST texts, and for a sutta the marker lands
                            //    after the first word of the first body paragraph. So anywhere between a section's
                            //    start and its first marker, 'last at or before' returns the PREVIOUS section's
                            //    last page — one page early, or at a sub-book boundary an entire sub-book early.
                            //    There the governing marker is the next one DOWN, not the last one up.
                            // 3. Case 2 of the old code (above the very first marker in the document) is the same
                            //    phenomenon at the top of the book, and is subsumed by the rule below.
                            //
                            // The <div> is the signal, not the heading styling: div boundaries were placed by hand
                            // and coincide with the printed page turn, whereas rend classes do not — at the
                            // Tikanipāta boundary in s0402a.att.xml, rend='centre' is used BOTH for the previous
                            // book's closing attribution (old page) and for the salutation that opens the new one
                            // (new page). The div opens exactly at the salutation, so it separates them correctly.
                            //
                            // Books with no div markup have no chapter anchors, so sectionStart stays -1 and the
                            // behaviour is exactly as before — no regression where the markup is absent.
                            var sectionStart = -1;
                            var chapterList = this.sortedChapterAnchors;
                            if (chapterList && chapterList.length > 0) {{
                                for (var ci = 0; ci < chapterList.length; ci++) {{
                                    if (chapterList[ci].position <= docPos) {{
                                        sectionStart = chapterList[ci].position;
                                    }} else {{
                                        break;
                                    }}
                                }}
                            }}

                            function findBestAnchor(sortedAnchors) {{
                                if (!sortedAnchors || sortedAnchors.length === 0) {{
                                    return null;
                                }}

                                // Last marker at or before the position, and the first one after it.
                                var bestAnchor = null;
                                var firstAfter = null;
                                for (var i = 0; i < sortedAnchors.length; i++) {{
                                    if (sortedAnchors[i].position <= docPos) {{
                                        bestAnchor = sortedAnchors[i];
                                    }} else {{
                                        // Since the list is sorted, the first one past docPos is the next marker.
                                        firstAfter = sortedAnchors[i];
                                        break;
                                    }}
                                }}

                                // No marker since this section began ⇒ we are ahead of the marker that governs
                                // it ⇒ look DOWN. A marker exactly AT the section start does govern it, hence <.
                                if (sectionStart >= 0 && firstAfter &&
                                    (!bestAnchor || bestAnchor.position < sectionStart)) {{
                                    return firstAfter;
                                }}

                                // Above the first marker with nothing following (or no section info): the first
                                // marker in the book IS the current page. (former Case 2, #423)
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

                    // Rebuild the cache once the resizing STOPS. (#572)
                    //
                    // This used to schedule an unconditional setTimeout(build, 100) on every resize event,
                    // with no clearTimeout — so a burst of resizes queued one full rebuild each. A zoom
                    // burst of ten steps meant ten complete rebuilds of a 1000+ anchor cache, and a window
                    // drag was worse. Measured in the log as CACHE_BUILT firing six-plus times per burst,
                    // ~150ms apart, still arriving well after the burst had ended.
                    //
                    // That made CACHE_BUILT useless as a 'layout has settled' signal — it only ever meant
                    // 'a build queued 100ms ago just finished', which could easily predate the last resize.
                    // The zoom restore waits on that signal, so it was restoring against a layout that was
                    // still reflowing, and the position drifted. Debouncing makes the signal mean what its
                    // name says, and removes the redundant rebuilds. (#434, #321)
                    // The delay is deliberately LONGER than the C# zoom settle (ResizeSettleMs, 250ms), and
                    // this timer starts from the renderer's last resize — which necessarily postdates the
                    // last SetZoomLevel arriving. That ordering is what lets the zoom restore simply wait
                    // for CACHE_BUILT: the signal can never arrive before something is waiting for it, so
                    // no counter or already-arrived check is needed. Keep the two in step if either moves.
                    window.addEventListener('resize', function() {{
                        clearTimeout(window.__cstResizeRebuildTimer);
                        window.__cstResizeRebuildTimer = setTimeout(function() {{
                            window.cstAnchorCache.build();   // emits CACHE_BUILT when the anchors are populated
                        }}, {AnchorRebuildDebounceMs});
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

                // #572: a new document supersedes any deferred scroll restoration armed by the previous one.
                Interlocked.Increment(ref _navGeneration);

                // #570: this is a different document, so any search in flight is meaningless and the
                // handler must be re-attached (a script change reloads; float/unfloat rebuilds the browser
                // entirely). Hiding rather than re-running is deliberate — after a script change the query
                // is in the previous script and would silently match nothing.
                _findHandlerAttached = false;
                HideFindBar();

                // Signal the ViewModel that initialization is complete and navigation can be enabled
                _viewModel.CompleteInitialization();

                // Make sure this UserControl can receive keyboard focus
                this.Focusable = true;
                // Focus the UserControl for keyboard shortcuts
                this.Focus();
                _logger.Debug("BookDisplayView focused for keyboard shortcuts");
                
                // Set up JavaScript bridge after content loads
                SetupJavaScriptBridge();

                // BEFORE the anchor cache is built: zoom reflows the text, so a cache built at the old zoom
                // would hold pixel positions that are wrong the moment this applies. The build itself waits
                // for fonts.ready plus a paint frame, which also covers the relayout this triggers. (#572)
                var zoomWillReflow = ApplyStoredZoomOnLoad();

                // Build the anchor position cache — UNCONDITIONALLY, because this navigation just gave
                // the page a fresh JS context (any previous window.cstAnchorCache is gone, whatever
                // _anchorCacheBuilt claims). No fixed "let it settle" delay: the build itself waits for
                // fonts.ready + a paint frame before reading positions, so this fires immediately and
                // the heavy lifting is gated on real readiness, not a 2s guess. (#423, #432)
                _logger.Debug("Navigation completed - dispatching unconditional anchor cache rebuild");
                RebuildAnchorCacheAfterNavigation();

                // The document is ready — execute any queued restoration (saved anchor or saved
                // search hit) NOW, from the one signal that knows the DOM exists. (BOOK-7)
                // When a non-default zoom was just applied the scroll half waits for the reflow, or the
                // target is computed against the pre-zoom layout. (#572, fable review)
                ExecutePendingRestoration(zoomWillReflow ? ResizeSettleMs : 0);
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
    /// <summary>
    /// <paramref name="delayScrollMs"/> defers only the SCROLL half. (#572)
    ///
    /// A load-time zoom other than 100% reflows the document asynchronously — Chromium applies
    /// <c>SetZoomLevel</c> over browser→renderer IPC, while the restoration scripts go out over a different
    /// channel — so resolving a scroll target immediately can compute it against the pre-zoom layout and
    /// land the reader somewhere else. Unlike a live zoom there is no rolling token yet to restore from, so
    /// nothing would correct it. Every relaunch of a book in a zoomed script hits this. (fable review)
    ///
    /// The visibility half is deliberately NOT deferred: a fresh render shows all notes, so delaying
    /// <see cref="ApplyFootnotesVisibility"/> would flash them for the length of the delay in exactly the
    /// case we are trying to improve.
    /// </summary>
    private void ExecutePendingRestoration(int delayScrollMs = 0)
    {
        if (_viewModel == null || _webView == null || !_isBrowserInitialized)
            return;

        // #224: a fresh (re)render shows all notes and highlights by default, so re-apply the per-book
        // toggle state on every load/reload before the hit/anchor restoration branches below.
        ApplyFootnotesVisibility(_viewModel.ShowFootnotes);
        ApplySearchTermsVisibility(_viewModel.ShowSearchTerms);

        if (delayScrollMs > 0)
        {
            // The pending intents are deliberately NOT taken yet — TakePending* consumes them, so reading
            // them here and acting later would lose them if anything ran in between.
            //
            // Tagged with the current navigation. The primary trigger is the CACHE_BUILT signal (see
            // OnTitleChanged); this timer is only a backstop for the case where the build never reports —
            // the same failure the anchor-cache watchdog exists for. Whichever fires first wins, because
            // RunDeferredScrollRestoration clears the tag atomically.
            var generation = Volatile.Read(ref _navGeneration);
            Volatile.Write(ref _deferredScrollGeneration, generation);
            _logger.Debug("Deferring scroll restoration for the load-time zoom reflow (nav {Generation})", generation);

            DispatcherTimer.RunOnce(RunDeferredScrollRestoration, TimeSpan.FromMilliseconds(delayScrollMs * 4));
            return;
        }

        ExecutePendingScrollRestoration();
    }

    /// <summary>
    /// Runs a deferred scroll restoration exactly once, and only for the navigation that armed it. (#572)
    ///
    /// <para>
    /// The generation check is what stops a stale deferral from firing into a newer document. Without it, a
    /// script change landing inside the deferral window would let the old timer consume the intents the new
    /// navigation had just queued — the new load would then find them already taken and fall back to a
    /// coarser position, which is worse than the race the deferral was added to fix. (fable review)
    /// </para>
    /// </summary>
    private void RunDeferredScrollRestoration()
    {
        var generation = Volatile.Read(ref _navGeneration);
        // Interlocked, not a plain compare-then-write: the CACHE_BUILT signal and the backstop timer can
        // both arrive, and only the first may act.
        if (Interlocked.CompareExchange(ref _deferredScrollGeneration, -1, generation) != generation)
            return;

        if (_isShutDown) return;
        _logger.Debug("Zoom reflow settled - running deferred scroll restoration (nav {Generation})", generation);
        ExecutePendingScrollRestoration();
    }

    private void ExecutePendingScrollRestoration()
    {
        if (_viewModel == null || _webView == null || !_isBrowserInitialized)
            return;

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
        // #31: a NON-search book reattaching a recycled tab has no hit/anchor/token intent, but CEF can reset
        // the live browser's scroll on reattach — so restore the rolling-captured reading position. Lowest
        // precedence (search-hit wins, Fable §6); cache-free, so it's safe before the deferred cache rebuild.
        else if (_lastPositionToken != null)
        {
            _logger.Debug("Restoring rolling reading-position token on reattach (#31): above={Above}, below={Below}, frac={Frac}",
                _lastPositionToken.Above, _lastPositionToken.Below, _lastPositionToken.Fraction);
            ScrollToPositionToken(_lastPositionToken);
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
                string ptA = "", ptAP = "", ptB = "", ptBP = "";   // #434 rolling reading-position bracket
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
                    else if (part.StartsWith("PTAP=")) ptAP = part.Substring(5);
                    else if (part.StartsWith("PTA=")) ptA = part.Substring(4);
                    else if (part.StartsWith("PTBP=")) ptBP = part.Substring(5);
                    else if (part.StartsWith("PTB=")) ptB = part.Substring(4);
                    // Parse as a DOUBLE with InvariantCulture, then round. `int.TryParse` here silently failed
                    // on a fractional value (Retina half-pixel scroll offsets, e.g. "76563.5") and left the
                    // out-param at 0 — which made the reading-position capture compute fraction 0 and pin the
                    // restore to the anchor ABOVE the reader. The JS now rounds too; this is the second line of
                    // defence so an unrounded value degrades to a half-pixel error instead of a silent zero.
                    // InvariantCulture is required: JS always emits '.' as the decimal separator, which a
                    // comma-decimal locale would otherwise reject the same way. (#551)
                    else if (part.StartsWith("SCROLL="))
                    {
                        if (double.TryParse(part.Substring(7), System.Globalization.NumberStyles.Any,
                                System.Globalization.CultureInfo.InvariantCulture, out var scrollParsed))
                            scrollY = (int)Math.Round(scrollParsed);
                    }
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

                    // #572: a load-time zoom deferred its scroll restoration until the reflow landed. This
                    // is that signal — build() runs after fonts.ready plus a paint frame, so a CACHE_BUILT
                    // for the CURRENT navigation means layout has settled at the new zoom. Signal-driven
                    // rather than a fixed delay, because the reflow's duration is a renderer-side IPC round
                    // trip that nothing here bounds. (fable review)
                    if (Volatile.Read(ref _deferredScrollGeneration) == Volatile.Read(ref _navGeneration))
                        Dispatcher.UIThread.Post(RunDeferredScrollRestoration);

                    // #572: same signal, for a LIVE zoom. A finished zoom burst waits here rather than on a
                    // fixed delay, because the burst ending says nothing about whether the renderer has
                    // finished reflowing — and restoring against a not-yet-reflowed layout is what lost the
                    // position when a second book was open.
                    if (Volatile.Read(ref _zoomAwaitingCacheBuilt) == 1)
                    {
                        var awaitGen = Volatile.Read(ref _zoomAwaitGeneration);
                        Dispatcher.UIThread.Post(() => RestoreZoomTokenNow(awaitGen));
                    }
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

                    // #434 rolling capture: keep the freshest reading-position token so a tab reattach can
                    // restore the exact position (#31). Computed via the unit-tested ReadingPositionMath. Only
                    // overwrite when the bracket is present (cache built + a real position), so a transient
                    // empty tick can't wipe a good token.
                    {
                        string? above = ptA.Length == 0 ? null : ptA;
                        string? below = ptB.Length == 0 ? null : ptB;
                        if (above != null || below != null)
                        {
                            var inv = System.Globalization.CultureInfo.InvariantCulture;
                            double.TryParse(ptAP, System.Globalization.NumberStyles.Any, inv, out var aP);
                            double.TryParse(ptBP, System.Globalization.NumberStyles.Any, inv, out var bP);
                            _lastPositionToken = ReadingPositionMath.Capture(above, aP, below, bP, scrollY);
                            // Push to the ViewModel so it can be persisted on shutdown for cross-run restore (#434).
                            _viewModel?.UpdateLastPositionToken(_lastPositionToken);
                        }
                    }

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
        // #572: zoom requested from inside the book WebView, where Chromium would otherwise apply its own
        // script-blind, per-origin zoom. One branch for all three because they differ only in the action.
        else if (title != null &&
                 (title.StartsWith("CST_ZOOM_IN:") || title.StartsWith("CST_ZOOM_OUT:") || title.StartsWith("CST_ZOOM_RESET:")))
        {
            try
            {
                var parts = title.Split('|');
                var messageTabId = parts.Length > 1 && parts[1].StartsWith("TAB:") ? parts[1].Substring(4) : "";

                if (messageTabId == _tabId)
                {
                    var command = parts[0];
                    _logger.Debug("*** ZOOM REQUESTED FROM JAVASCRIPT: {Command} ***", command);
                    // Runs on the CEF thread. StepZoom marshals itself, but posting here keeps this branch
                    // consistent with its neighbours and keeps the service call off the CEF thread. (BOOK-2)
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (command.StartsWith("CST_ZOOM_IN")) ZoomIn();
                        else if (command.StartsWith("CST_ZOOM_OUT")) ZoomOut();
                        else ResetZoom();
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error processing zoom request from JavaScript | {Details}", ex.Message);
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
                    Dispatcher.UIThread.Post(() => _viewModel?.RequestShowSource(secondary: false));
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
                    Dispatcher.UIThread.Post(() => _viewModel?.RequestShowSource(secondary: true));
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error processing View Source 2010 request from JavaScript | {Details}", ex.Message);
            }
        }
        // #110: Close Tab requested from JavaScript (⌘W while the book WebView has focus).
        else if (title != null && title.StartsWith("CST_CLOSE_TAB:"))
        {
            try
            {
                var parts = title.Split('|');
                var messageTabId = parts.Length > 1 && parts[1].StartsWith("TAB:") ? parts[1].Substring(4) : "";

                if (messageTabId == _tabId)
                {
                    _logger.Debug("*** CLOSE TAB REQUESTED FROM JAVASCRIPT ***");
                    // OnTitleChanged runs on the CEF thread; closing mutates the dock layout (and disposes
                    // this very WebView), so defer to the UI thread — the current title callback returns
                    // first, then the close runs. (BOOK-2)
                    Dispatcher.UIThread.Post(() => SimpleTabbedWindow.CloseDockableIfClosable(_viewModel));
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error processing Close Tab request from JavaScript | {Details}", ex.Message);
            }
        }
        // #443: Go To requested from JavaScript (⌘G while this book's WebView has focus). The message
        // arrives on the focused book's own view, so it is inherently the right book — no layout
        // resolution, the same reasoning as CST_CLOSE_TAB above.
        else if (title != null && title.StartsWith("CST_GOTO:"))
        {
            try
            {
                var parts = title.Split('|');
                var messageTabId = parts.Length > 1 && parts[1].StartsWith("TAB:") ? parts[1].Substring(4) : "";

                if (messageTabId == _tabId)
                {
                    _logger.Debug("*** GO TO REQUESTED FROM JAVASCRIPT ***");
                    Dispatcher.UIThread.Post(() => _viewModel?.InvokeOpenGoToDialog());
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error processing Go To request from JavaScript | {Details}", ex.Message);
            }
        }
        // #28: Look Up in Dictionary requested from JavaScript (⌘/Ctrl+D while this book's WebView has
        // focus). Like CST_GOTO, the message arrives on the focused book's own view, so it is inherently
        // the right book. LookUpInDictionaryAsync then does its own title round-trip to pull the current
        // selection (CST_LOOKUP_SEL), which is why this must not run on the CEF thread.
        else if (title != null && title.StartsWith("CST_LOOKUP_REQUESTED:"))
        {
            try
            {
                var parts = title.Split('|');
                var messageTabId = parts.Length > 1 && parts[1].StartsWith("TAB:") ? parts[1].Substring(4) : "";

                if (messageTabId == _tabId)
                {
                    _logger.Debug("*** LOOK UP IN DICTIONARY REQUESTED FROM JAVASCRIPT ***");
                    Dispatcher.UIThread.Post(async () =>
                        await SimpleTabbedWindow.LookUpInDictionaryAsync(_viewModel));
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error processing Look Up in Dictionary request from JavaScript | {Details}", ex.Message);
            }
        }
        // #28: Search for Selection requested from JavaScript (⌘/Ctrl+F while this book's WebView has focus).
        // #570: Cmd/Ctrl+F from inside the book WebView.
        else if (title != null && title.StartsWith("CST_FIND_IN_PAGE:"))
        {
            try
            {
                var parts = title.Split('|');
                var messageTabId = parts.Length > 1 && parts[1].StartsWith("TAB:") ? parts[1].Substring(4) : "";
                if (messageTabId == _tabId)
                {
                    _logger.Debug("*** FIND IN PAGE REQUESTED FROM JAVASCRIPT ***");
                    Dispatcher.UIThread.Post(ShowFindBar);   // runs on the CEF thread (BOOK-2)
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error processing find-in-page request from JavaScript | {Details}", ex.Message);
            }
        }
        else if (title != null && title.StartsWith("CST_SEARCH_SELECTION_REQUESTED:"))
        {
            try
            {
                var parts = title.Split('|');
                var messageTabId = parts.Length > 1 && parts[1].StartsWith("TAB:") ? parts[1].Substring(4) : "";

                if (messageTabId == _tabId)
                {
                    _logger.Debug("*** SEARCH FOR SELECTION REQUESTED FROM JAVASCRIPT ***");
                    Dispatcher.UIThread.Post(async () =>
                        await SimpleTabbedWindow.SearchForSelectionAsync(_viewModel));
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error processing Search for Selection request from JavaScript | {Details}", ex.Message);
            }
        }
        // #28: window-level shortcuts forwarded out of a focused book WebView. Unlike the book commands
        // above these are not tab-scoped actions, but they still arrive tab-tagged so a background tab's
        // WebView can never act on a keystroke the user aimed at the visible one.
        else if (title != null && (title.StartsWith("CST_SELECT_BOOK_REQUESTED:")
                                || title.StartsWith("CST_SETTINGS_REQUESTED:")
                                || title.StartsWith("CST_PRINT_REQUESTED:")
                                || title.StartsWith("CST_PRINT_SELECTION_REQUESTED:")))
        {
            try
            {
                var parts = title.Split('|');
                var messageTabId = parts.Length > 1 && parts[1].StartsWith("TAB:") ? parts[1].Substring(4) : "";

                if (messageTabId == _tabId)
                {
                    var command = parts[0];
                    _logger.Debug("*** WINDOW SHORTCUT REQUESTED FROM JAVASCRIPT: {Command} ***", command);

                    // OnTitleChanged runs on the CEF thread; all of these touch the UI (dock layout, a
                    // modal dialog, or the print dialog), so they must be posted. (BOOK-2)
                    Dispatcher.UIThread.Post(() =>
                    {
                        if (command.StartsWith("CST_SELECT_BOOK_REQUESTED"))
                            SimpleTabbedWindow.RevealSelectBookPanel();
                        else if (command.StartsWith("CST_SETTINGS_REQUESTED"))
                            _ = App.ShowSettingsWindow();
                        else if (command.StartsWith("CST_PRINT_SELECTION_REQUESTED"))
                            PrintSelection();
                        else
                            Print();
                    });
                }
            }
            catch (Exception ex)
            {
                _logger.Error("Error processing window shortcut request from JavaScript | {Details}", ex.Message);
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

                                // #572: Cmd/Ctrl + plus/minus/0 (book zoom). Required for the same reason as
                                // Go To and Look Up below, not a special case: while CEF holds focus,
                                // Avalonia never sees the keystroke at all, so the window-level handlers and
                                // the native menu are both unreachable from inside a book.
                                //
                                // It is NOT here to suppress a Chromium built-in. This is an alloy build, so
                                // the keyboard zoom accelerators (which live in Chromium's chrome layer) are
                                // absent — which is exactly why #572 reports Cmd+plus doing nothing on macOS
                                // today, and why the only accidental zoom anyone found was Ctrl+*scroll*.
                                // The wheel handler further down is the one that does suppress a real
                                // built-in. preventDefault here is cheap hygiene, not the mechanism.
                                // (Corrected after fable review; the earlier note claimed the opposite and
                                // would have misdirected anyone optimising this later.)
                                //
                                // Key spellings, all of which reach here: '=' is the unshifted main-row key
                                // (what Cmd++ actually produces), '+' is the shifted one and the numpad's,
                                // '-'/'_' likewise, and '0' covers both main row and numpad.
                                if ((event.metaKey || event.ctrlKey) && !event.altKey) {
                                    var zoomCmd = null;
                                    if (event.key === '=' || event.key === '+') { zoomCmd = 'CST_ZOOM_IN'; }
                                    else if (event.key === '-' || event.key === '_') { zoomCmd = 'CST_ZOOM_OUT'; }
                                    else if (event.key === '0') { zoomCmd = 'CST_ZOOM_RESET'; }

                                    if (zoomCmd !== null) {
                                        window.cstLogger.log('DEBUG', 'Zoom shortcut detected in JavaScript: ' + zoomCmd);
                                        event.preventDefault();
                                        event.stopPropagation();
                                        // Auto-repeat IS forwarded, unlike the modal shortcuts above: holding
                                        // Cmd+ to run the text up several steps is the expected behaviour of a
                                        // zoom key, and the ladder plus the settle debounce already bound it.
                                        document.title = zoomCmd + ':|TAB:{_tabId}|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                                        return false;
                                    }
                                }

                                // #443: Cmd+G / Ctrl+G (Go To) - when this WebView has focus, CEF holds the
                                // native focus so the window's focus resolution comes back empty and the
                                // native-menu Go To falls back to the first split's book. Forward it like
                                // View Source / Close so it always targets THIS book.
                                // Match 'g' AND 'G' (Caps Lock yields 'G' in Chromium); !shiftKey so ⌘⇧G
                                // isn't caught. Without the 'G' case, ⌘G under Caps Lock would fall through
                                // to the native menu and reopen the wrong-split bug. event.repeat is dropped
                                // so holding ⌘G can't stack modal Go To dialogs.
                                if ((event.key === 'g' || event.key === 'G') && !event.shiftKey && (event.metaKey || event.ctrlKey)) {
                                    event.preventDefault();
                                    event.stopPropagation();
                                    if (event.repeat) return false;
                                    window.cstLogger.log('DEBUG', 'Go To shortcut detected in JavaScript');
                                    document.title = 'CST_GOTO:|TAB:{_tabId}|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                                    return false;
                                }

                                // #28: Cmd+D / Ctrl+D (Look Up in Dictionary). Same reason as Go To below -
                                // CEF holds focus while reading, so the window KeyBindings never see this.
                                // On Windows this is the whole shortcut: NativeMenuBar gestures are
                                // display-only there, so without this forward Ctrl+D is a dead key in a book.
                                // Match 'd' and 'D' (Caps Lock yields 'D' in Chromium); !shiftKey keeps any
                                // future ⇧⌘D free. preventDefault also stops Chromium's bookmark dialog.
                                if ((event.key === 'd' || event.key === 'D') && !event.shiftKey && (event.metaKey || event.ctrlKey)) {
                                    event.preventDefault();
                                    event.stopPropagation();
                                    if (event.repeat) return false;
                                    window.cstLogger.log('DEBUG', 'Look Up in Dictionary shortcut detected in JavaScript');
                                    document.title = 'CST_LOOKUP_REQUESTED:|TAB:{_tabId}|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                                    return false;
                                }

                                // #28: Cmd+F / Ctrl+F (Search for Selection). preventDefault also suppresses
                                // Chromium's own find bar, which would otherwise open over the book.
                                // #570: Cmd/Ctrl+SHIFT+F is now Search for Selection (corpus-wide). Plain
                                // Cmd/Ctrl+F was rebound to Find in Page below — the browser-universal
                                // meaning, what CST4 used it for, and what the request asked for.
                                if ((event.key === 'f' || event.key === 'F') && event.shiftKey && (event.metaKey || event.ctrlKey)) {
                                    event.preventDefault();
                                    event.stopPropagation();
                                    if (event.repeat) return false;
                                    window.cstLogger.log('DEBUG', 'Search for Selection shortcut detected in JavaScript');
                                    document.title = 'CST_SEARCH_SELECTION_REQUESTED:|TAB:{_tabId}|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                                    return false;
                                }

                                // #570: Cmd/Ctrl+F -> Find in Page for THIS book. Must be captured here:
                                // while CEF holds focus Avalonia never sees the keystroke, and Chromium
                                // would otherwise open its own find bar, which we do not control and which
                                // would sit outside the app's chrome entirely.
                                if ((event.key === 'f' || event.key === 'F') && !event.shiftKey && (event.metaKey || event.ctrlKey)) {
                                    event.preventDefault();
                                    event.stopPropagation();
                                    if (event.repeat) return false;
                                    window.cstLogger.log('DEBUG', 'Find in Page shortcut detected in JavaScript');
                                    document.title = 'CST_FIND_IN_PAGE:|TAB:{_tabId}|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                                    return false;
                                }

                                // #28: the remaining window-level shortcuts. These are not book commands, but
                                // CEF holds focus while reading, so the window KeyBindings never see them and
                                // they would otherwise be dead keys inside a book. preventDefault also
                                // suppresses Chromium's own Ctrl+O (open file) and Ctrl+P (print) dialogs.
                                if ((event.key === 'o' || event.key === 'O') && !event.shiftKey && (event.metaKey || event.ctrlKey)) {
                                    event.preventDefault();
                                    event.stopPropagation();
                                    if (event.repeat) return false;
                                    window.cstLogger.log('DEBUG', 'Select a Book shortcut detected in JavaScript');
                                    document.title = 'CST_SELECT_BOOK_REQUESTED:|TAB:{_tabId}|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                                    return false;
                                }

                                if ((event.key === 'p' || event.key === 'P') && (event.metaKey || event.ctrlKey)) {
                                    event.preventDefault();
                                    event.stopPropagation();
                                    if (event.repeat) return false;
                                    window.cstLogger.log('DEBUG', 'Print shortcut detected in JavaScript (shift=' + event.shiftKey + ')');
                                    document.title = (event.shiftKey ? 'CST_PRINT_SELECTION_REQUESTED:' : 'CST_PRINT_REQUESTED:')
                                        + '|TAB:{_tabId}|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                                    return false;
                                }

                                if (event.key === ',' && (event.metaKey || event.ctrlKey)) {
                                    event.preventDefault();
                                    event.stopPropagation();
                                    if (event.repeat) return false;
                                    window.cstLogger.log('DEBUG', 'Settings shortcut detected in JavaScript');
                                    document.title = 'CST_SETTINGS_REQUESTED:|TAB:{_tabId}|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                                    return false;
                                }

                                // #110: Cmd+W / Ctrl+W (Close Tab) - CEF eats the key when the book WebView
                                // has focus, so forward it like the View Source shortcuts to close this tab.
                                if (event.key === 'w' && (event.metaKey || event.ctrlKey)) {
                                    window.cstLogger.log('DEBUG', 'Close Tab shortcut detected in JavaScript');
                                    event.preventDefault();
                                    event.stopPropagation();
                                    document.title = 'CST_CLOSE_TAB:|TAB:{_tabId}|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                                    return false;
                                }
                            }, true); // Use capture phase to intercept before other handlers

                            // #571/#572: Ctrl+scroll. This is the gesture a Windows user already found by
                            // accident — it applied Chromium's own layout zoom, which reflowed the text and
                            // lost their place, because nothing on the C# side ever knew it happened. A beta
                            // tester built a copy-a-line-then-search ritual around exactly that (#570).
                            //
                            // Routing it here makes it the same operation as Cmd/Ctrl+plus: it steps the
                            // per-script ladder, persists, and restores the reading position after the
                            // reflow. preventDefault is what stops Chromium's parallel, script-blind zoom.
                            //
                            // macOS is excluded (cstZoomOnWheel is false there). Trackpad pinch arrives as
                            // ctrl+wheel too, and pinch on macOS today is page-scale magnification that
                            // preserves position and does not rewrap — a different, working behaviour that
                            // this issue was not asked to replace.
                            if ({_zoomOnWheel}) {
                                // Trackpads emit a stream of small deltas where a mouse wheel emits one
                                // notch of ~100, so stepping per event would race up the ladder on a
                                // trackpad. Accumulate and step on threshold to make both feel the same.
                                window.__cstWheelAcc = 0;
                                document.addEventListener('wheel', function(event) {
                                    if (!event.ctrlKey) { return; }
                                    event.preventDefault();      // suppress Chromium's own zoom
                                    event.stopPropagation();

                                    window.__cstWheelAcc += event.deltaY;
                                    if (Math.abs(window.__cstWheelAcc) < 50) { return; }
                                    // deltaY is negative scrolling up/away, which is the zoom-IN direction.
                                    var cmd = window.__cstWheelAcc < 0 ? 'CST_ZOOM_IN' : 'CST_ZOOM_OUT';
                                    window.__cstWheelAcc = 0;
                                    document.title = cmd + ':|TAB:{_tabId}|SEQ:' + (window.__cstTitleSeq = (window.__cstTitleSeq || 0) + 1);
                                // passive:false is required to preventDefault a wheel event, and it has a
                                // known cost: a non-passive document-level wheel listener makes Chromium
                                // wait on the main thread before every scroll, not just ctrl+scroll. That is
                                // a scroll-latency tax on the largest books, on the platforms where this is
                                // enabled. Unavoidable while the interception is conditional — the decision
                                // to cancel can only be made in the handler. (fable review)
                                }, { capture: true, passive: false });
                            }
                            
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
                // #571/#572: Ctrl+scroll drives our per-script zoom on Windows/Linux only. On macOS the same
                // ctrl+wheel event is what a trackpad pinch produces, and pinch there is page-scale
                // magnification that keeps the reading position and does not rewrap — working behaviour this
                // change was not asked to replace. Substituted as a JS literal the same way {_tabId} is: the
                // script is a verbatim string, so it cannot be interpolated in place.
                script = script.Replace("{_zoomOnWheel}", OperatingSystem.IsMacOS() ? "false" : "true");

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

    /// <summary>
    /// #112 native-print probe: open the platform print dialog for the whole book via window.print(). Uses
    /// ExecuteScript (fire-and-forget) — EvaluateScript returns null in this CEF build, and printing needs no
    /// return value, so this rides the same JS path the rest of the view uses. On macOS this routes into
    /// Chromium's platform print; whether that dialog is usable in this CEF-120 build is exactly what this
    /// probe validates. If inadequate, the fallback is CefBrowserHost.PrintToPdf (#112).
    /// </summary>
    public void Print()
    {
        if (_webView == null) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(Print);
            return;
        }
        try
        {
            _logger.Information("Print (Cmd+P): invoking window.print() on the book WebView");
            // Defensively drop any stranded print-selection isolation (if a prior selection print's afterprint
            // cleanup never fired) so a whole-book print can't silently print only the old selection. (#112, Fable)
            _webView.ExecuteScript("document.body.classList.remove('cst-printing-selection'); window.print();");
        }
        catch (Exception ex)
        {
            _logger.Error("Print failed | {Details}", ex.Message);
        }
    }

    /// <summary>
    /// #112 print selection: print only the current selection. Selection-isolation pre-step (dialog-agnostic,
    /// works with the native window.print() route): clone the selection into a dedicated print container, add
    /// a body class that an <c>@media print</c> rule uses to hide everything else, print, then clean up on
    /// afterprint. Falls back to whole-book print when there is no selection. (Egret addendum)
    /// </summary>
    public void PrintSelection()
    {
        if (_webView == null) return;
        if (!Dispatcher.UIThread.CheckAccess())
        {
            Dispatcher.UIThread.Post(PrintSelection);
            return;
        }
        try
        {
            _logger.Information("Print Selection (Shift+Cmd+P): isolating the selection and invoking window.print()");
            _webView.ExecuteScript(PrintSelectionScript);
        }
        catch (Exception ex)
        {
            _logger.Error("PrintSelection failed | {Details}", ex.Message);
        }
    }

    // Isolates the current selection for printing: no/empty selection falls back to a whole-book print;
    // otherwise clone the selected range(s) into #cst-print-selection and let the injected @media print rule
    // hide every other direct child of body. Cleaned up on afterprint so the on-screen view is untouched.
    private const string PrintSelectionScript = @"
        (function() {
            // Start from a clean slate in case a prior run's afterprint cleanup never fired.
            document.body.classList.remove('cst-printing-selection');
            var sel = window.getSelection();
            if (!sel || sel.rangeCount === 0 || sel.isCollapsed) { window.print(); return; }
            if (!document.getElementById('cst-print-selection-style')) {
                var style = document.createElement('style');
                style.id = 'cst-print-selection-style';
                style.textContent =
                    // Hidden on screen at all times (so the cloned selection never shows as a duplicate in the
                    // live view, even if afterprint cleanup never fires); shown, and everything else hidden,
                    // only in print media.
                    '#cst-print-selection{display:none;}'
                    + ' @media print { body.cst-printing-selection > *:not(#cst-print-selection){display:none !important;}'
                    + ' #cst-print-selection{display:block !important;} }';
                document.head.appendChild(style);
            }
            var container = document.getElementById('cst-print-selection');
            if (!container) {
                container = document.createElement('div');
                container.id = 'cst-print-selection';
                document.body.appendChild(container);
            }
            container.innerHTML = '';
            for (var i = 0; i < sel.rangeCount; i++) { container.appendChild(sel.getRangeAt(i).cloneContents()); }
            document.body.classList.add('cst-printing-selection');
            var cleanup = function() {
                document.body.classList.remove('cst-printing-selection');
                if (container && container.parentNode) { container.parentNode.removeChild(container); }
                window.removeEventListener('afterprint', cleanup);
            };
            window.addEventListener('afterprint', cleanup);
            window.print();
        })();";

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
                }} else {{
                    // BOTH bracket anchors gone (a corpus-update rename between runs) — restore the fuzzy
                    // nearest-paragraph fallback the coarse ScrollToPageAnchor had, so cross-run restore doesn't
                    // regress to top-of-doc. Use whichever bracket end is a paragraph anchor. (Fable cross-run §1)
                    var fuzzyPara = function(name) {{
                        if (!name || name.indexOf('para') !== 0) return false;
                        var tgt = parseInt(name.substring(4).split('-')[0]);
                        if (isNaN(tgt)) return false;
                        var paras = document.querySelectorAll('a[name^=""para""]');
                        var best = null, bestDiff = Infinity;
                        paras.forEach(function(a) {{
                            var n = parseInt((a.name || '').substring(4).split('-')[0]);
                            if (!isNaN(n)) {{ var d = Math.abs(n - tgt); if (d < bestDiff) {{ bestDiff = d; best = a; }} }}
                        }});
                        // Same distance cap as ScrollToPageAnchor: don't jump to a far paragraph on a drastic
                        // renumber — leave the position instead. (Fable cross-run re-review)
                        var maxAllowedDiff = paras.length < 300 ? 100 : 50;
                        if (best && bestDiff <= maxAllowedDiff) {{ best.scrollIntoView({{ behavior: 'instant', block: 'start' }}); return true; }}
                        return false;
                    }};
                    fuzzyPara({aboveJson}) || fuzzyPara({belowJson});
                }}
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

        if (_resizeSettleTimer != null)   // #434 resize consumer
        {
            _resizeSettleTimer.Stop();
            _resizeSettleTimer.Dispose();
            _resizeSettleTimer = null;
            _resizeInProgress = false;
            _resizeRestoreToken = null;
        }
    }

}