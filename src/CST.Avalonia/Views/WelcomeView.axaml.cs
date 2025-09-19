using System;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Threading;
using WebViewControl;
using CST.Avalonia.ViewModels;
using Serilog;

namespace CST.Avalonia.Views
{
    public partial class WelcomeView : UserControl
    {
        private WebView? _webView;
        private WelcomeViewModel? _viewModel;
        private bool _isWebViewReady = false;
        private bool _hasLoadedContent = false;

        public WelcomeView()
        {
            InitializeComponent();

            // Get WebView reference
            _webView = this.FindControl<WebView>("WelcomeWebView");

            if (_webView != null)
            {
                _webView.WebViewInitialized += OnWebViewInitialized;
                _webView.Navigated += OnNavigated;

                // Log WebView state
                Log.Information("WelcomeView: WebView control found and event handlers attached");
            }
            else
            {
                Log.Error("WelcomeView: WebView control not found!");
            }

            // Subscribe to DataContext changes to get the ViewModel
            DataContextChanged += OnDataContextChanged;

            // Also check if DataContext is already set
            if (DataContext is WelcomeViewModel vm)
            {
                SetupViewModel(vm);
            }
        }

        private void OnDataContextChanged(object? sender, EventArgs e)
        {
            Log.Information("WelcomeView: DataContext changed to {Type}", DataContext?.GetType().Name ?? "null");

            if (DataContext is WelcomeViewModel viewModel)
            {
                SetupViewModel(viewModel);
            }
        }

        private void SetupViewModel(WelcomeViewModel viewModel)
        {
            if (_viewModel == viewModel)
                return; // Already set up

            _viewModel = viewModel;
            Log.Information("WelcomeView: ViewModel set, HTML content length: {Length}",
                _viewModel.HtmlContent?.Length ?? 0);

            // Subscribe to HTML content changes
            _viewModel.PropertyChanged += (s, args) =>
            {
                if (args.PropertyName == nameof(WelcomeViewModel.HtmlContent))
                {
                    Log.Information("WelcomeView: HtmlContent property changed, length: {Length}",
                        _viewModel.HtmlContent?.Length ?? 0);

                    // Try loading content when it changes
                    _ = TryLoadHtmlContent();
                }
            };

            // Try to load content immediately
            _ = TryLoadHtmlContent();
        }

        private void OnWebViewInitialized()
        {
            Log.Information("WelcomeView: WebView initialized event fired");
            _isWebViewReady = true;

            // Try loading content now that WebView is ready
            _ = TryLoadHtmlContent();
        }

        private void OnNavigated(string url, string frameName)
        {
            Log.Information("WelcomeView: WebView navigated to: {Url} in frame: {Frame}", url, frameName);
        }

        private async Task TryLoadHtmlContent()
        {
            // Add a small delay to ensure WebView is fully initialized
            await Task.Delay(100);

            await Dispatcher.UIThread.InvokeAsync(() =>
            {
                try
                {
                    if (_webView == null)
                    {
                        Log.Warning("WelcomeView: Cannot load HTML - WebView is null");
                        return;
                    }

                    if (_viewModel == null)
                    {
                        Log.Warning("WelcomeView: Cannot load HTML - ViewModel is null");
                        return;
                    }

                    if (string.IsNullOrEmpty(_viewModel.HtmlContent))
                    {
                        Log.Warning("WelcomeView: Cannot load HTML - HtmlContent is empty");
                        return;
                    }

                    if (!_isWebViewReady)
                    {
                        Log.Warning("WelcomeView: WebView not ready yet, will retry when initialized");
                        return;
                    }

                    if (_hasLoadedContent)
                    {
                        Log.Debug("WelcomeView: Content already loaded, skipping");
                        return;
                    }

                    Log.Information("WelcomeView: Loading HTML content (length: {Length})",
                        _viewModel.HtmlContent.Length);

                    // Load the HTML content
                    _webView.LoadHtml(_viewModel.HtmlContent);
                    _hasLoadedContent = true;

                    Log.Information("WelcomeView: LoadHtml called successfully");
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "WelcomeView: Failed to load HTML content");
                }
            });
        }
    }
}