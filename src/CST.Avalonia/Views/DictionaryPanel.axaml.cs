using System;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.LogicalTree;
using Avalonia.Threading;
using CST.Avalonia.Services;
using CST.Avalonia.ViewModels;
using Microsoft.Extensions.DependencyInjection;
using ReactiveUI;
using WebViewControl;

namespace CST.Avalonia.Views;

public partial class DictionaryPanel : UserControl
{
    private IDisposable? _meaningSub;
    private WebView? _meaningWebView;
    private DictionaryViewModel? _vm;

    public DictionaryPanel()
    {
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;
        // Resolve the singleton VM. No `?? new DictionaryViewModel()` fallback: that ctor dereferences
        // App.ServiceProvider and would throw (also breaking the XAML previewer), never yielding a usable
        // instance. (DICT-6)
        DataContext = App.ServiceProvider?.GetService<DictionaryViewModel>();
    }

    protected override void OnAttachedToLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnAttachedToLogicalTree(e);
        WireMeaning();
    }

    protected override void OnDetachedFromLogicalTree(LogicalTreeAttachmentEventArgs e)
    {
        base.OnDetachedFromLogicalTree(e);
        // Drop the subscription when the panel leaves the tree (e.g. per float/unfloat) so a discarded
        // panel isn't kept alive by the app-lifetime singleton VM. Re-wired on re-attach. (DICT-5)
        _meaningSub?.Dispose();
        _meaningSub = null;
    }

    private void OnDataContextChanged(object? sender, EventArgs e) => WireMeaning();

    /// <summary>
    /// Release the meaning WebView's live CEF browser before this panel is moved across windows. Called by
    /// CstDockFactory.DisposeAndEvictRecycledView on a cross-window drag/float of the dictionary tool: the
    /// panel now hosts a CEF browser, so — exactly like the book/PDF views — it must be disposed-before-move
    /// or the re-parent SIGSEGVs on macOS. The evicted panel is discarded; a fresh one is built (with a fresh
    /// browser) at the destination and re-binds to the singleton VM. (#466 / #458 discipline)
    /// </summary>
    public void Shutdown()
    {
        _meaningSub?.Dispose();
        _meaningSub = null;
        try
        {
            if (_meaningWebView != null)
            {
                _meaningWebView.BeforeNavigate -= OnBeforeNavigate;
                _meaningWebView.Dispose();
            }
        }
        catch (Exception)
        {
            // Already torn down / mid-reparent — nothing more to release.
        }
        _meaningWebView = null;
    }

    private void WireMeaning()
    {
        _meaningSub?.Dispose();
        _meaningSub = null;

        _meaningWebView ??= this.FindControl<WebView>("MeaningWebView");
        if (_meaningWebView != null)
        {
            // Intercept <see>/lemma link clicks (cst-see: … ) — the CSP host page runs no script, so
            // navigation is the channel. Cancel the navigation and look the referenced word up instead;
            // real content is loaded via LoadHtml (about:blank), never navigated to. (#466)
            _meaningWebView.BeforeNavigate -= OnBeforeNavigate;
            _meaningWebView.BeforeNavigate += OnBeforeNavigate;
        }

        if (DataContext is DictionaryViewModel vm)
        {
            _vm = vm;
            // Re-render the meaning WebView whenever the selected definition's document HTML changes
            // (selection, script, or font). A short throttle coalesces the rapid pair a query change emits —
            // Words.Clear() momentarily nulls the selection (an empty document) before the first result is
            // reselected — so only the FINAL document loads. Two LoadHtml calls ~1ms apart otherwise race
            // and the stale/empty one can win, leaving the pane blank. (#466)
            _meaningSub = vm.WhenAnyValue(x => x.MeaningDocumentHtml)
                .Throttle(TimeSpan.FromMilliseconds(60))
                .Subscribe(html => Dispatcher.UIThread.Post(() => LoadMeaning(html)));
        }
    }

    private void LoadMeaning(string? html)
    {
        if (_meaningWebView == null)
            return;
        try
        {
            _meaningWebView.LoadHtml(string.IsNullOrEmpty(html) ? "<html><body></body></html>" : html);
        }
        catch (Exception)
        {
            // The WebView may not be ready during teardown/re-parent; the next selection re-renders.
        }
    }

    private void OnBeforeNavigate(WebViewControl.Request request)
    {
        var url = request.Url ?? string.Empty;
        if (url.StartsWith(DictionaryHtmlRenderer.SeeScheme, StringComparison.OrdinalIgnoreCase))
        {
            request.Cancel();
            var target = Uri.UnescapeDataString(url.Substring(DictionaryHtmlRenderer.SeeScheme.Length));
            var vm = _vm;
            if (vm != null && !string.IsNullOrWhiteSpace(target))
                Dispatcher.UIThread.Post(() => vm.NavigateToWordCommand.Execute(target).Subscribe());
        }
    }
}
