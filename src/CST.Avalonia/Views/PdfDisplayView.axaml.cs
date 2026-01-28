using System;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Interactivity;
using ReactiveUI;
using WebViewControl;
using CST.Avalonia.ViewModels;
using Serilog;

namespace CST.Avalonia.Views;

public partial class PdfDisplayView : UserControl
{
    private readonly ILogger _logger;
    private PdfDisplayViewModel? _viewModel;
    private WebView? _webView;
    private IDisposable? _lifecycleSubscription;
    private bool _isBrowserInitialized = false;
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

    private void DisposeWebView()
    {
        if (_webView != null)
        {
            try
            {
                _logger.Information("Disposing PDF WebView");
                _webView.Navigated -= OnNavigationCompleted;
                _webView.Dispose();
                _webView = null;
                _isBrowserInitialized = false;
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
            .ObserveOn(RxApp.MainThreadScheduler)
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

    private void OnNavigationCompleted(object? sender, string url)
    {
        _isBrowserInitialized = true;
        _logger.Information("PDF navigation completed: {Url}", url);
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
