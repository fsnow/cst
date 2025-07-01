using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Reactive;
using System.Reactive.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Xml;
using System.Xml.Xsl;
using Avalonia.Threading;
using ReactiveUI;
using CST;
using CST.Conversion;
using CST.Avalonia.Services;

namespace CST.Avalonia.ViewModels
{
    public class BookDisplayViewModel : ViewModelBase
    {
        private readonly ScriptService _scriptService;
        private readonly Book _book;
        private readonly List<string>? _searchTerms;
        private readonly string? _initialAnchor;
        
        public event Action<int>? NavigateToHighlightRequested;
        
        private Script _bookScript;
        private bool _isLoading;
        private string _pageStatusText = "";
        private string _bookInfoText = "";
        private string _hitStatusText = "";
        private int _currentHitIndex;
        private int _totalHits;
        private bool _hasSearchHighlights;
        private bool _hasChapters;
        private bool _hasLinkedBooks;
        private bool _hasMula;
        private bool _hasAtthakatha;
        private bool _hasTika;
        private ChapterModel? _selectedChapter;
        private string _htmlContent = "";
        private bool _isCefGlueAvailable = false;

        public BookDisplayViewModel(Book book, List<string>? searchTerms = null, string? initialAnchor = null)
        {
            // For now, create ScriptService without logger
            _scriptService = new ScriptService();
            _book = book;
            _searchTerms = searchTerms;
            _initialAnchor = initialAnchor;
            _bookScript = _scriptService.CurrentScript;
            
            // Initialize collections - exclude Unknown and IPE from UI dropdown
            AvailableScripts = new ObservableCollection<Script>(
                Enum.GetValues<Script>().Where(s => s != Script.Unknown && s != Script.Ipe));
            Chapters = new ObservableCollection<ChapterModel>();
            
            // Initialize properties
            _totalHits = searchTerms?.Count ?? 0;
            _hasSearchHighlights = _totalHits > 0;
            _bookInfoText = $"{GetBookDisplayName(book)} ({book.Matn})";
            
            // Initialize commands
            FirstHitCommand = ReactiveCommand.Create(NavigateToFirstHit, this.WhenAnyValue(x => x.HasSearchHighlights));
            PreviousHitCommand = ReactiveCommand.Create(NavigateToPreviousHit, this.WhenAnyValue(x => x.CurrentHitIndex, index => index > 1));
            NextHitCommand = ReactiveCommand.Create(NavigateToNextHit, this.WhenAnyValue(x => x.CurrentHitIndex, x => x.TotalHits, (current, total) => current < total));
            LastHitCommand = ReactiveCommand.Create(NavigateToLastHit, this.WhenAnyValue(x => x.HasSearchHighlights));
            
            OpenMulaCommand = ReactiveCommand.Create(OpenMulaBook, this.WhenAnyValue(x => x.HasMula));
            OpenAtthakathaCommand = ReactiveCommand.Create(OpenAtthakathaBook, this.WhenAnyValue(x => x.HasAtthakatha));
            OpenTikaCommand = ReactiveCommand.Create(OpenTikaBook, this.WhenAnyValue(x => x.HasTika));
            
            // Subscribe to script changes - reload from source like CST4 does
            this.WhenAnyValue(x => x.BookScript)
                .Skip(1) // Skip initial value
                .Subscribe(async script => 
                {
                    Console.WriteLine($"Script changed to {script} - reloading from source files");
                    await LoadBookContentAsync();
                });
                
            this.WhenAnyValue(x => x.SelectedChapter)
                .Where(chapter => chapter != null)
                .Subscribe(chapter => NavigateToChapter(chapter!));
                
            // Initialize data
            _ = Task.Run(InitializeAsync);
        }
        
        public ObservableCollection<Script> AvailableScripts { get; }
        public ObservableCollection<ChapterModel> Chapters { get; }
        
        public ReactiveCommand<Unit, Unit> FirstHitCommand { get; }
        public ReactiveCommand<Unit, Unit> PreviousHitCommand { get; }
        public ReactiveCommand<Unit, Unit> NextHitCommand { get; }
        public ReactiveCommand<Unit, Unit> LastHitCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenMulaCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenAtthakathaCommand { get; }
        public ReactiveCommand<Unit, Unit> OpenTikaCommand { get; }

        public Script BookScript
        {
            get => _bookScript;
            set => this.RaiseAndSetIfChanged(ref _bookScript, value);
        }

        public bool IsLoading
        {
            get => _isLoading;
            set => this.RaiseAndSetIfChanged(ref _isLoading, value);
        }

        public string PageStatusText
        {
            get => _pageStatusText;
            set => this.RaiseAndSetIfChanged(ref _pageStatusText, value);
        }

        public string BookInfoText
        {
            get => _bookInfoText;
            set => this.RaiseAndSetIfChanged(ref _bookInfoText, value);
        }

        public string HitStatusText
        {
            get => _hitStatusText;
            set => this.RaiseAndSetIfChanged(ref _hitStatusText, value);
        }

        public int CurrentHitIndex
        {
            get => _currentHitIndex;
            set => this.RaiseAndSetIfChanged(ref _currentHitIndex, value);
        }

        public int TotalHits
        {
            get => _totalHits;
            set => this.RaiseAndSetIfChanged(ref _totalHits, value);
        }

        public bool HasSearchHighlights
        {
            get => _hasSearchHighlights;
            set => this.RaiseAndSetIfChanged(ref _hasSearchHighlights, value);
        }

        public bool HasChapters
        {
            get => _hasChapters;
            set => this.RaiseAndSetIfChanged(ref _hasChapters, value);
        }

        public bool HasLinkedBooks
        {
            get => _hasLinkedBooks;
            set => this.RaiseAndSetIfChanged(ref _hasLinkedBooks, value);
        }

        public bool HasMula
        {
            get => _hasMula;
            set => this.RaiseAndSetIfChanged(ref _hasMula, value);
        }

        public bool HasAtthakatha
        {
            get => _hasAtthakatha;
            set => this.RaiseAndSetIfChanged(ref _hasAtthakatha, value);
        }

        public bool HasTika
        {
            get => _hasTika;
            set => this.RaiseAndSetIfChanged(ref _hasTika, value);
        }

        public ChapterModel? SelectedChapter
        {
            get => _selectedChapter;
            set => this.RaiseAndSetIfChanged(ref _selectedChapter, value);
        }

        public string HtmlContent
        {
            get => _htmlContent;
            set => this.RaiseAndSetIfChanged(ref _htmlContent, value);
        }

        public bool IsCefGlueAvailable
        {
            get => _isCefGlueAvailable;
            set => this.RaiseAndSetIfChanged(ref _isCefGlueAvailable, value);
        }

        public Book Book => _book;
        public string DisplayTitle => $"{GetBookDisplayName(_book)} - {_scriptService.GetScriptDisplayName(_bookScript)}";

        private string GetBookDisplayName(Book book)
        {
            // Use the last part of the ShortNavPath or FileName if no nav path
            if (!string.IsNullOrEmpty(book.ShortNavPath))
            {
                var parts = book.ShortNavPath.Split('/');
                return parts[parts.Length - 1];
            }
            else if (!string.IsNullOrEmpty(book.FileName))
            {
                return Path.GetFileNameWithoutExtension(book.FileName);
            }
            return "Unknown Book";
        }

        private async Task InitializeAsync()
        {
            Console.WriteLine($"BookDisplayViewModel.InitializeAsync starting for: {_book.FileName}");
            
            // Ensure UI property updates happen on UI thread
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = true);
            
            try
            {
                Console.WriteLine("Step 1: Checking CefGlue availability");
                // Check if CefGlue is available
                CheckCefGlueAvailability();
                
                Console.WriteLine("Step 2: Loading chapters");
                // Load chapters if available
                await LoadChaptersAsync();
                
                Console.WriteLine("Step 3: Checking linked books");
                // Check for linked books (must run on UI thread due to ReactiveUI property updates)
                await Dispatcher.UIThread.InvokeAsync(() => CheckLinkedBooks());
                
                Console.WriteLine("Step 4: Loading book content");
                // Load book content
                await LoadBookContentAsync();
                
                Console.WriteLine("Step 5: Handling initial navigation");
                // Navigate to initial position if specified
                if (!string.IsNullOrEmpty(_initialAnchor))
                {
                    Console.WriteLine($"Navigating to anchor: {_initialAnchor}");
                    // TODO: Navigate to anchor
                }
                else if (_searchTerms?.Any() == true)
                {
                    Console.WriteLine($"Setting up search navigation for {_searchTerms.Count} terms");
                    await Dispatcher.UIThread.InvokeAsync(() =>
                    {
                        CurrentHitIndex = 1;
                        UpdateHitStatusText();
                    });
                }
                
                Console.WriteLine("BookDisplayViewModel.InitializeAsync completed successfully");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error in InitializeAsync: {ex.Message}");
                Console.WriteLine($"Stack trace: {ex.StackTrace}");
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
            }
        }

        private void CheckCefGlueAvailability()
        {
            try
            {
                // Re-enable CefGlue for debugging the white screen issue
                Console.WriteLine("CefGlue availability check - enabling for debugging");
                Dispatcher.UIThread.Post(() =>
                {
                    IsCefGlueAvailable = false; // Will be set to true by browser initialization
                    PageStatusText = "Checking CefGlue browser availability...";
                });
            }
            catch (Exception ex)
            {
                Dispatcher.UIThread.Post(() =>
                {
                    IsCefGlueAvailable = false;
                    PageStatusText = $"Using fallback text display: {ex.Message}";
                });
            }
        }
        
        public void SetCefGlueAvailability(bool available, string statusMessage = "")
        {
            IsCefGlueAvailable = available;
            if (!string.IsNullOrEmpty(statusMessage))
            {
                PageStatusText = statusMessage;
            }
        }

        private async Task LoadChaptersAsync()
        {
            await Task.Run(() =>
            {
                // TODO: Load chapters from ChapterLists.Inst[book.Index]
                // For now, simulate no chapters
                HasChapters = false;
            });
        }

        private void CheckLinkedBooks()
        {
            // TODO: Check for related books (Mula/Atthakatha/Tika)
            // For now, set based on current book type
            HasMula = _book.Matn != CommentaryLevel.Mula;
            HasAtthakatha = _book.Matn == CommentaryLevel.Mula;
            HasTika = _book.Matn != CommentaryLevel.Tika;
            HasLinkedBooks = HasMula || HasAtthakatha || HasTika;
        }

        private async Task LoadBookContentAsync()
        {
            await Dispatcher.UIThread.InvokeAsync(() => IsLoading = true);
            
            try
            {
                var htmlContent = await GenerateHtmlContentAsync();
                await Dispatcher.UIThread.InvokeAsync(() =>
                {
                    HtmlContent = htmlContent;
                    PageStatusText = "Content loaded";
                });
            }
            finally
            {
                await Dispatcher.UIThread.InvokeAsync(() => IsLoading = false);
            }
        }

        private async Task<string> GenerateHtmlContentAsync()
        {
            return await Task.Run(() =>
            {
                try
                {
                    // Load XML content
                    var xmlPath = Path.Combine(GetBooksDirectory(), _book.FileName);
                    Console.WriteLine($"Loading XML from: {xmlPath}");
                    
                    if (!File.Exists(xmlPath))
                    {
                        Console.WriteLine($"XML file not found: {xmlPath}");
                        return "<html><body><h1>Book file not found</h1><p>File: " + xmlPath + "</p></body></html>";
                    }

                    var xmlDoc = new XmlDocument();
                    
                    // Use XmlReader with automatic encoding detection
                    Console.WriteLine("Loading XML with automatic encoding detection");
                    using (var fileStream = File.OpenRead(xmlPath))
                    {
                        // Let XmlReader detect encoding automatically
                        using (var xmlReader = XmlReader.Create(fileStream))
                        {
                            xmlDoc.Load(xmlReader);
                        }
                    }
                    Console.WriteLine($"XML loaded successfully, root element: {xmlDoc.DocumentElement?.Name}");

                    // Apply script conversion if needed - use ConvertBook for proper XML handling
                    if (_bookScript != Script.Devanagari)
                    {
                        Console.WriteLine($"Converting script from Devanagari to {_bookScript}");
                        var convertedXml = ScriptConverter.ConvertBook(xmlDoc.OuterXml, _bookScript);
                        xmlDoc.LoadXml(convertedXml);
                    }

                    // Apply search highlighting if needed
                    if (_searchTerms?.Any() == true)
                    {
                        Console.WriteLine($"Applying search highlighting for {_searchTerms.Count} terms");
                        ApplySearchHighlighting(xmlDoc);
                    }

                    // Apply XSL transformation
                    var xslPath = GetXslPath(_bookScript);
                    Console.WriteLine($"Using XSL file: {xslPath}");
                    
                    if (!File.Exists(xslPath))
                    {
                        Console.WriteLine($"XSL file not found: {xslPath}");
                        return "<html><body><h1>XSL file not found</h1><p>File: " + xslPath + "</p></body></html>";
                    }

                    var xslTransform = new XslCompiledTransform();
                    xslTransform.Load(xslPath);

                    using var stringWriter = new StringWriter();
                    xslTransform.Transform(xmlDoc, null, stringWriter);
                    
                    var htmlContent = stringWriter.ToString();
                    Console.WriteLine($"Generated HTML content length: {htmlContent.Length}");
                    Console.WriteLine($"HTML content preview (first 500 chars): {htmlContent.Substring(0, Math.Min(500, htmlContent.Length))}");
                    
                    return htmlContent;
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error generating HTML content: {ex.Message}");
                    Console.WriteLine($"Stack trace: {ex.StackTrace}");
                    return $"<html><body><h1>Error loading book</h1><p>{ex.Message}</p><pre>{ex.StackTrace}</pre></body></html>";
                }
            });
        }

        private void ApplySearchHighlighting(XmlDocument xmlDoc)
        {
            if (_searchTerms == null || !_searchTerms.Any()) return;

            var hitCount = 0;
            
            // Find all text nodes in the document
            var textNodes = new List<XmlNode>();
            CollectTextNodes(xmlDoc.DocumentElement, textNodes);
            
            foreach (var textNode in textNodes)
            {
                if (textNode.Value == null) continue;
                
                var originalText = textNode.Value;
                var modifiedText = originalText;
                var hitPositions = new List<(int start, int length, string term)>();
                
                // Find all search term matches in this text node
                foreach (var term in _searchTerms)
                {
                    if (string.IsNullOrWhiteSpace(term)) continue;
                    
                    var index = 0;
                    while ((index = modifiedText.IndexOf(term, index, StringComparison.OrdinalIgnoreCase)) >= 0)
                    {
                        hitPositions.Add((index, term.Length, term));
                        index += term.Length;
                    }
                }
                
                if (hitPositions.Any())
                {
                    // Sort hits by position (reverse order for easier replacement)
                    hitPositions.Sort((a, b) => b.start.CompareTo(a.start));
                    
                    // Replace the text node with a new structure containing highlighting
                    var parentElement = textNode.ParentNode;
                    if (parentElement != null)
                    {
                        // Create new content with highlighting
                        var lastIndex = originalText.Length;
                        var fragments = new List<XmlNode>();
                        
                        foreach (var hit in hitPositions)
                        {
                            hitCount++;
                            
                            // Add text after this hit
                            if (lastIndex > hit.start + hit.length)
                            {
                                var afterText = originalText.Substring(hit.start + hit.length, lastIndex - hit.start - hit.length);
                                if (!string.IsNullOrEmpty(afterText))
                                {
                                    fragments.Insert(0, xmlDoc.CreateTextNode(afterText));
                                }
                            }
                            
                            // Add the highlighted hit
                            var hitElement = xmlDoc.CreateElement("hi");
                            hitElement.SetAttribute("rend", "hit");
                            hitElement.SetAttribute("id", $"hit_{hitCount}");
                            hitElement.InnerText = originalText.Substring(hit.start, hit.length);
                            fragments.Insert(0, hitElement);
                            
                            lastIndex = hit.start;
                        }
                        
                        // Add any remaining text before the first hit
                        if (lastIndex > 0)
                        {
                            var beforeText = originalText.Substring(0, lastIndex);
                            if (!string.IsNullOrEmpty(beforeText))
                            {
                                fragments.Insert(0, xmlDoc.CreateTextNode(beforeText));
                            }
                        }
                        
                        // Replace the original text node with the fragments
                        foreach (var fragment in fragments)
                        {
                            parentElement.InsertBefore(fragment, textNode);
                        }
                        parentElement.RemoveChild(textNode);
                    }
                }
            }
            
            // Update the total hits count on UI thread since these are bound properties
            Dispatcher.UIThread.Post(() =>
            {
                TotalHits = hitCount;
                HasSearchHighlights = hitCount > 0;
                if (hitCount > 0)
                {
                    CurrentHitIndex = 1;
                    UpdateHitStatusText();
                }
            });
        }

        private void CollectTextNodes(XmlNode? node, List<XmlNode> textNodes)
        {
            if (node == null) return;
            
            if (node.NodeType == XmlNodeType.Text)
            {
                // Skip text nodes that are already inside highlighting elements
                var parent = node.ParentNode;
                if (parent?.Name == "hi" && parent.Attributes?["rend"]?.Value == "hit")
                    return;
                    
                textNodes.Add(node);
            }
            else if (node.HasChildNodes)
            {
                foreach (XmlNode child in node.ChildNodes)
                {
                    CollectTextNodes(child, textNodes);
                }
            }
        }

        private string GetBooksDirectory()
        {
            // Use the CST unit test data directory for now
            return "/Users/fsnow/Cloud-Drive/Projects/CST_UnitTestData/Xml";
        }

        private string GetXslPath(Script script)
        {
            var scriptName = script switch
            {
                Script.Latin => "latn",
                Script.Devanagari => "deva",
                Script.Thai => "thai",
                Script.Myanmar => "mymr",
                Script.Sinhala => "sinh",
                Script.Khmer => "khmr",
                Script.Bengali => "beng",
                Script.Gujarati => "gujr",
                Script.Gurmukhi => "guru",
                Script.Kannada => "knda",
                Script.Malayalam => "mlym",
                Script.Telugu => "telu",
                Script.Tibetan => "tibt",
                _ => "latn"
            };
            
            return Path.Combine("/Users/fsnow/github/fsnow/cst/src/Cst4/Xsl", $"tipitaka-{scriptName}.xsl");
        }

        private void NavigateToFirstHit()
        {
            if (TotalHits > 0)
            {
                CurrentHitIndex = 1;
                UpdateHitStatusText();
                NavigateToHighlightRequested?.Invoke(CurrentHitIndex);
                PageStatusText = $"Navigated to first hit: hit_1";
            }
        }

        private void NavigateToPreviousHit()
        {
            if (CurrentHitIndex > 1)
            {
                CurrentHitIndex--;
                UpdateHitStatusText();
                NavigateToHighlightRequested?.Invoke(CurrentHitIndex);
                PageStatusText = $"Navigated to hit: hit_{CurrentHitIndex}";
            }
        }

        private void NavigateToNextHit()
        {
            if (CurrentHitIndex < TotalHits)
            {
                CurrentHitIndex++;
                UpdateHitStatusText();
                NavigateToHighlightRequested?.Invoke(CurrentHitIndex);
                PageStatusText = $"Navigated to hit: hit_{CurrentHitIndex}";
            }
        }

        private void NavigateToLastHit()
        {
            if (TotalHits > 0)
            {
                CurrentHitIndex = TotalHits;
                UpdateHitStatusText();
                NavigateToHighlightRequested?.Invoke(CurrentHitIndex);
                PageStatusText = $"Navigated to last hit: hit_{TotalHits}";
            }
        }

        private void UpdateHitStatusText()
        {
            if (TotalHits > 0)
            {
                HitStatusText = $"{CurrentHitIndex} of {TotalHits}";
            }
            else
            {
                HitStatusText = "";
            }
        }

        private void NavigateToChapter(ChapterModel chapter)
        {
            // TODO: Navigate to chapter anchor in browser
        }

        private void OpenMulaBook()
        {
            // TODO: Find and open related Mula book
        }

        private void OpenAtthakathaBook()
        {
            // TODO: Find and open related Atthakatha book
        }

        private void OpenTikaBook()
        {
            // TODO: Find and open related Tika book
        }
    }

    public class ChapterModel
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";
        public string Anchor { get; set; } = "";
    }
}