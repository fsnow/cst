using System;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ReactiveUI;
using WebViewControl;
using CST.Avalonia.Input;
using CST.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using Serilog;

namespace CST.Avalonia.Views;

public partial class PdfDisplayView : UserControl, Services.IBrowserDocumentView
{
    private readonly ILogger _logger;
    private PdfDisplayViewModel? _viewModel;
    private WebView? _webView;
    private IDisposable? _lifecycleSubscription;
    private bool _hasPdfLoaded = false;

    public PdfDisplayView()
    {
        InitializeComponent();

        _logger = Log.ForContext<PdfDisplayView>();

        // Try to create WebView
        TryCreateWebView();
    }

    private void TryCreateWebView()
    {
        try
        {
            _webView = this.FindControl<WebView>("webView");
            if (_webView != null)
            {
                _webView.Navigated += OnNavigationCompleted;
                _webView.TitleChanged += OnShortcutTitleChanged;   // #518

                // #621: the PDF body is the case that motivated this — clicking into it left the keyboard
                // acting on whatever book was in the first dock. Verified to fire even though Chromium
                // renders the page in an internal plugin frame, which is where the #518 relay dies.
                if (_webView is Controls.CstWebView focusReporter)
                    focusReporter.BrowserGotFocus += () =>
                        App.ServiceProvider?.GetService<Services.ActiveDocumentTracker>()
                            ?.Note(DataContext as ViewModels.PdfDisplayViewModel, "browser-focus:pdf");
                _logger.Debug("PDF WebView control found and events attached");
            }
            else
            {
                _logger.Error("Failed to find WebView control in PdfDisplayView");
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Failed to initialize WebView for PDF display");
            _webView = null;
        }
    }

    // Tags this view's shortcut messages so a background WebView can't act on a keystroke aimed at the
    // visible one - the same guard BookDisplayView applies with its tab id. (#518)
    private readonly string _shortcutViewId = "pdf_" + Guid.NewGuid().ToString("n").Substring(0, 8);

    // #518: the PDF viewer is a CEF WebView with no keyboard capture, so window shortcuts are dead while
    // it has focus. Unlike the other two this loads a URL rather than LoadHtml, but the injection point is
    // the same - after navigation completes.
    //
    // MEASURED LIMITATION (Windows, 2026-08-05): this does NOT actually restore shortcuts here. The relay
    // injects cleanly - twice, in fact, since Navigated fires per frame - but Ctrl+D and Ctrl+F produce
    // nothing. Chromium renders the PDF in an internal plugin frame that ExecuteScript cannot reach and
    // that consumes keystrokes itself, so our listener never sees them. Predicted in review before it was
    // tested, and confirmed.
    //
    // Kept rather than removed: it is inert, it costs nothing, and it starts working the moment this view
    // shows anything that is not a plugin-rendered PDF. Ctrl+F is deliberately excluded (includeFind:
    // false) so that if the plugin frame ever does become reachable, we do not take find-in-page away.
    // The PDF viewer therefore remains an open item on #518.
    private void InjectShortcutRelay()
    {
        try
        {
            // includeFind: false — leave Ctrl+F to Chromium's find-in-page, which is the shortcut that
            // actually matters in a PDF. (fable review)
            _webView?.ExecuteScript(WebViewShortcutRelay.BuildScript(_shortcutViewId, includeFind: false));
            _logger.Debug("PdfDisplayView: shortcut relay injected (view {ViewId})", _shortcutViewId);
        }
        catch (Exception ex)
        {
            // Non-fatal: the PDF still displays, the shortcuts simply stay dead here.
            _logger.Warning("PdfDisplayView: failed to inject shortcut relay | {Details}", ex.Message);
        }
    }

    private void OnShortcutTitleChanged()
    {
        WebViewShortcutRelay.TryHandle(_webView?.Title, _shortcutViewId, _logger);
    }

    private void DisposeWebView()
    {
        if (_webView != null)
        {
            try
            {
                _logger.Information("Disposing PDF WebView");
                _webView.Navigated -= OnNavigationCompleted;
                _webView.TitleChanged -= OnShortcutTitleChanged;   // #518
                _webView.Dispose();
                _webView = null;
                _hasPdfLoaded = false;  // Reset so PDF reloads after recreate
                _logger.Information("PDF WebView disposed successfully");
            }
            catch (Exception ex)
            {
                _logger.Error(ex, "Error while disposing PDF WebView");
                _webView = null;
            }
        }
    }

    // Called by the dock factory when the PDF tab is permanently closed, to release the CEF WebView.
    // Like BookDisplayView.Shutdown: the WebView deliberately survives tab-switch/detach (so it isn't
    // torn down on recycling), so it must be disposed explicitly on close or the closed PDF tab leaks
    // a live browser + renderer for the session. Idempotent (DisposeWebView null-guards). (PDF close leak)
    public void Shutdown() => DisposeWebView();

    protected override void OnLoaded(RoutedEventArgs e)
    {
        base.OnLoaded(e);

        _viewModel = DataContext as PdfDisplayViewModel;
        if (_viewModel == null)
        {
            _logger.Warning("DataContext is not PdfDisplayViewModel");
            return;
        }

        _logger.Information("PdfDisplayView loaded for {Book}, {Source}",
            _viewModel.BookFilename, _viewModel.SourceType);

        // Subscribe to LoadPdfRequested event from ViewModel
        _viewModel.LoadPdfRequested += OnLoadPdfRequested;

        // Subscribe to WebViewLifecycleOperation changes for float/unfloat
        _lifecycleSubscription = _viewModel
            .WhenAnyValue(vm => vm.WebViewLifecycleOperation)
            .ObserveOn(new global::CST.Avalonia.AvaloniaUIThreadScheduler())
            .Subscribe(OnWebViewLifecycleOperationChanged);

        // If PDF URL is already available (e.g., restored from state), load it
        // But only load once - don't reload on tab switches (preserves user's current page)
        if (!string.IsNullOrEmpty(_viewModel.PdfUrl) && !_hasPdfLoaded)
        {
            LoadPdf(_viewModel.PdfUrl);
            _hasPdfLoaded = true;
        }
    }

    protected override void OnUnloaded(RoutedEventArgs e)
    {
        base.OnUnloaded(e);

        // Don't dispose WebView on tab switch - only unsubscribe from events
        // WebView disposal only happens during float/unfloat or when document is closed
        if (_viewModel != null)
        {
            _viewModel.LoadPdfRequested -= OnLoadPdfRequested;
        }

        _lifecycleSubscription?.Dispose();
        _lifecycleSubscription = null;

        _logger.Information("PdfDisplayView unloaded (WebView kept alive)");
    }

    private void OnLoadPdfRequested(string url)
    {
        LoadPdf(url);
        _hasPdfLoaded = true;
    }

    private void LoadPdf(string url)
    {
        if (_webView == null)
        {
            _logger.Warning("Cannot load PDF - WebView not available");
            return;
        }

        try
        {
            _logger.Information("Loading PDF: {Url}", url);
            _webView.Address = url;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Error loading PDF: {Url}", url);
        }
    }

    // WebViewControl's Navigated delegate is (string url, string frameName). This previously declared
    // (object? sender, string url), which bound contravariantly and silently shifted the arguments - the
    // log printed the frame name as the URL. (fable review)
    private void OnNavigationCompleted(string url, string frameName)
    {
        _logger.Information("PDF navigation completed: {Url} (frame: {Frame})", url, frameName);

        // Re-inject after every navigation - a new document drops the previous listener. The script
        // guards itself against double-binding, so repeating it is safe. (#518)
        InjectShortcutRelay();
    }

    private void OnWebViewLifecycleOperationChanged(WebViewLifecycleOperation operation)
    {
        switch (operation)
        {
            case WebViewLifecycleOperation.PrepareForFloat:
            case WebViewLifecycleOperation.PrepareForUnfloat:
                _logger.Information("PDF: Preparing for float/unfloat - saving state and disposing WebView");
                SaveWebViewState();
                DisposeWebView();
                break;

            case WebViewLifecycleOperation.RestoreAfterFloat:
            case WebViewLifecycleOperation.RestoreAfterUnfloat:
                _logger.Information("PDF: Restoring after float/unfloat - recreating WebView");
                RecreateWebView();
                RestoreWebViewState();
                if (_viewModel != null)
                {
                    _viewModel.WebViewLifecycleOperation = WebViewLifecycleOperation.None;
                }
                break;
        }
    }

    private void SaveWebViewState()
    {
        if (_viewModel != null && !string.IsNullOrEmpty(_viewModel.PdfUrl))
        {
            _viewModel.SavedWebViewState = new PdfWebViewState
            {
                Url = _viewModel.PdfUrl,
                Page = _viewModel.TargetPage
            };
            _logger.Debug("PDF state saved: {Url}", _viewModel.PdfUrl);
        }
    }

    private void RecreateWebView()
    {
        if (_webView == null)
        {
            TryCreateWebView();
        }
    }

    private void RestoreWebViewState()
    {
        if (_viewModel?.SavedWebViewState != null && _webView != null)
        {
            var state = _viewModel.SavedWebViewState;
            if (!string.IsNullOrEmpty(state.Url))
            {
                _logger.Information("Restoring PDF state: {Url}", state.Url);
                LoadPdf(state.Url);
            }
            _viewModel.SavedWebViewState = null;
        }
    }
}
