using System;
using System.IO;
using System.Reactive;
using System.Threading.Tasks;
using ReactiveUI;
using CST.Avalonia.Services;
using CST.Avalonia.ViewModels.Dock;
using Serilog;

namespace CST.Avalonia.ViewModels
{
    /// <summary>
    /// ViewModel for displaying PDF source documents in a dockable tab.
    /// Downloads PDFs from SharePoint via Graph API and displays them in a WebView.
    /// </summary>
    public class PdfDisplayViewModel : ReactiveDocument
    {
        private readonly ILogger _logger;
        private readonly ISharePointService? _sharePointService;
        private readonly CstDockFactory? _dockFactory;

        private string _bookFilename;
        private Sources.SourceType _sourceType;
        private int _targetPage;
        private string _statusText = "Loading...";
        private bool _isLoading = true;
        private bool _isWebViewAvailable = true;
        private bool _isFloating = false;
        private string _pdfLocalPath = "";
        private string _pdfUrl = "";
        private WebViewLifecycleOperation _webViewLifecycleOperation = WebViewLifecycleOperation.None;
        private PdfWebViewState? _savedWebViewState = null;

        /// <summary>
        /// Event raised when the PDF URL is ready to be loaded in the WebView.
        /// </summary>
        public event Action<string>? LoadPdfRequested;

        public PdfDisplayViewModel(
            string bookFilename,
            Sources.SourceType sourceType,
            int targetPage,
            ISharePointService? sharePointService = null,
            CstDockFactory? dockFactory = null,
            string? windowId = null)
        {
            _logger = Log.ForContext<PdfDisplayViewModel>();
            _bookFilename = bookFilename;
            _sourceType = sourceType;
            _targetPage = targetPage;
            _sharePointService = sharePointService;
            _dockFactory = dockFactory;

            // Configure Dock properties
            if (windowId != null)
            {
                Id = windowId;
            }
            else
            {
                Id = $"Pdf_{bookFilename}_{sourceType}_{Guid.NewGuid():N}";
            }
            Title = $"{GetSourceTypeName(sourceType)} - Page {targetPage}";
            CanClose = true;
            CanFloat = false;  // Float/unfloat not yet implemented for PDF windows
            CanPin = false;

            // Initialize commands
            FloatWindowCommand = ReactiveCommand.Create(FloatWindow);
            UnfloatWindowCommand = ReactiveCommand.Create(UnfloatWindow);
            RefreshCommand = ReactiveCommand.CreateFromTask(LoadPdfAsync);

            _logger.Information("PdfDisplayViewModel created for {Book}, {Source}, page {Page}",
                bookFilename, sourceType, targetPage);

            // Start loading the PDF
            _ = LoadPdfAsync();
        }

        #region Properties

        public string BookFilename
        {
            get => _bookFilename;
            set => this.RaiseAndSetIfChanged(ref _bookFilename, value);
        }

        public Sources.SourceType SourceType
        {
            get => _sourceType;
            set => this.RaiseAndSetIfChanged(ref _sourceType, value);
        }

        public int TargetPage
        {
            get => _targetPage;
            set => this.RaiseAndSetIfChanged(ref _targetPage, value);
        }

        public string StatusText
        {
            get => _statusText;
            set => this.RaiseAndSetIfChanged(ref _statusText, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => this.RaiseAndSetIfChanged(ref _isLoading, value);
        }

        public bool IsWebViewAvailable
        {
            get => _isWebViewAvailable;
            set => this.RaiseAndSetIfChanged(ref _isWebViewAvailable, value);
        }

        public bool IsFloating
        {
            get => _isFloating;
            set => this.RaiseAndSetIfChanged(ref _isFloating, value);
        }

        public string PdfUrl
        {
            get => _pdfUrl;
            set => this.RaiseAndSetIfChanged(ref _pdfUrl, value);
        }

        public string PdfLocalPath
        {
            get => _pdfLocalPath;
            set => this.RaiseAndSetIfChanged(ref _pdfLocalPath, value);
        }

        public WebViewLifecycleOperation WebViewLifecycleOperation
        {
            get => _webViewLifecycleOperation;
            set => this.RaiseAndSetIfChanged(ref _webViewLifecycleOperation, value);
        }

        public PdfWebViewState? SavedWebViewState
        {
            get => _savedWebViewState;
            set => this.RaiseAndSetIfChanged(ref _savedWebViewState, value);
        }

        public string SourceTypeName => GetSourceTypeName(_sourceType);

        #endregion

        #region Commands

        public ReactiveCommand<Unit, Unit> FloatWindowCommand { get; }
        public ReactiveCommand<Unit, Unit> UnfloatWindowCommand { get; }
        public ReactiveCommand<Unit, Unit> RefreshCommand { get; }

        #endregion

        #region Methods

        private async Task LoadPdfAsync()
        {
            IsLoading = true;
            StatusText = "Downloading PDF...";

            try
            {
                // Get source info
                var source = Sources.Inst.GetSource(_bookFilename, _sourceType);
                if (source == null)
                {
                    StatusText = $"No {SourceTypeName} source available for this book";
                    IsLoading = false;
                    _logger.Warning("No source found for {Book}, {Source}", _bookFilename, _sourceType);
                    return;
                }

                if (_sharePointService == null)
                {
                    StatusText = "SharePoint service not available";
                    IsLoading = false;
                    _logger.Error("SharePointService is null");
                    return;
                }

                // Download the PDF
                _logger.Information("Downloading PDF: {Path}", source.Path);
                var localPath = await _sharePointService.DownloadPdfAsync(source.Path);

                if (string.IsNullOrEmpty(localPath) || !File.Exists(localPath))
                {
                    StatusText = "Failed to download PDF";
                    IsLoading = false;
                    _logger.Error("PDF download failed for {Path}", source.Path);
                    return;
                }

                PdfLocalPath = localPath;

                // _targetPage is already the calculated PDF page number
                // (BookDisplayViewModel already did: source.PageStart + (myanmarPage - 1))
                int pdfPage = _targetPage;
                if (pdfPage < 1) pdfPage = 1;

                // Build the file:// URL with page fragment
                // CEF's PDFium supports #page=N fragments
                PdfUrl = $"file://{localPath}#page={pdfPage}";

                Title = $"{Path.GetFileNameWithoutExtension(localPath)} - Page {pdfPage}";
                StatusText = $"{SourceTypeName} - Page {pdfPage}";
                IsLoading = false;

                _logger.Information("PDF ready: {Url}", PdfUrl);

                // Notify the View to load the PDF
                LoadPdfRequested?.Invoke(PdfUrl);
            }
            catch (Exception ex)
            {
                StatusText = $"Error: {ex.Message}";
                IsLoading = false;
                _logger.Error(ex, "Failed to load PDF");
            }
        }

        private void FloatWindow()
        {
            // Float/unfloat for PDF not yet implemented
            // Would need to add FloatDockableWithoutRecycling overload for PdfDisplayViewModel in CstDockFactory
            _logger.Warning("Float not yet implemented for PDF windows");
        }

        private void UnfloatWindow()
        {
            // Float/unfloat for PDF not yet implemented
            _logger.Warning("Unfloat not yet implemented for PDF windows");
        }

        private static string GetSourceTypeName(Sources.SourceType sourceType)
        {
            return sourceType switch
            {
                Sources.SourceType.Burmese1957 => "Burmese 1957",
                Sources.SourceType.Burmese2010 => "Burmese 2010",
                Sources.SourceType.VriPrint => "VRI Print",
                _ => sourceType.ToString()
            };
        }

        #endregion
    }

    /// <summary>
    /// Saved WebView state for PDF restoration after float/unfloat operations.
    /// </summary>
    public class PdfWebViewState
    {
        public string? Url { get; set; }
        public int Page { get; set; }
    }
}
