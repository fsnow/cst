using System;
using System.Linq;
using System.Reactive;
using System.Text.RegularExpressions;
using ReactiveUI;
using CST;
using Serilog;

namespace CST.Avalonia.ViewModels
{
    public class GoToDialogViewModel : ReactiveObject
    {
        private readonly ILogger _logger;
        private readonly Book _book;
        private readonly string _currentParagraph;
        private readonly string _vriPage;
        private readonly string _myanmarPage;
        private readonly string _ptsPage;
        private readonly string _thaiPage;
        private readonly string _otherPage;

        private string _inputNumber = "";
        private NavigationType _selectedType = NavigationType.Paragraph;
        private bool _isParagraphAvailable = true; // Always available
        private bool _isVriPageAvailable;
        private bool _isMyanmarPageAvailable;
        private bool _isPtsPageAvailable;
        private bool _isThaiPageAvailable;
        private bool _isOtherPageAvailable;

        public GoToDialogViewModel(BookDisplayViewModel bookViewModel)
        {
            _logger = Log.ForContext<GoToDialogViewModel>();
            _book = bookViewModel.Book;
            _currentParagraph = bookViewModel.CurrentParagraph;
            _vriPage = bookViewModel.VriPage;
            _myanmarPage = bookViewModel.MyanmarPage;
            _ptsPage = bookViewModel.PtsPage;
            _thaiPage = bookViewModel.ThaiPage;
            _otherPage = bookViewModel.OtherPage;

            // Set availability based on current book's page references
            IsVriPageAvailable = bookViewModel.VriPage != "*";
            IsMyanmarPageAvailable = bookViewModel.MyanmarPage != "*";
            IsPtsPageAvailable = bookViewModel.PtsPage != "*";
            IsThaiPageAvailable = bookViewModel.ThaiPage != "*";
            IsOtherPageAvailable = bookViewModel.OtherPage != "*";

            _logger.Information("GoToDialog initialized for book: {Book}, Available pages: V={V}, M={M}, P={P}, T={T}, O={O}",
                _book.FileName, IsVriPageAvailable, IsMyanmarPageAvailable, IsPtsPageAvailable, IsThaiPageAvailable, IsOtherPageAvailable);

            // Commands
            var canNavigate = this.WhenAnyValue(
                x => x.InputNumber,
                x => x.SelectedType,
                (input, type) => IsValidInput(input, type));

            NavigateCommand = ReactiveCommand.Create(Navigate, canNavigate);
            CancelCommand = ReactiveCommand.Create(Cancel);
        }

        public string InputNumber
        {
            get => _inputNumber;
            set => this.RaiseAndSetIfChanged(ref _inputNumber, value);
        }

        public NavigationType SelectedType
        {
            get => _selectedType;
            set => this.RaiseAndSetIfChanged(ref _selectedType, value);
        }

        public bool IsParagraphAvailable
        {
            get => _isParagraphAvailable;
            set => this.RaiseAndSetIfChanged(ref _isParagraphAvailable, value);
        }

        public bool IsVriPageAvailable
        {
            get => _isVriPageAvailable;
            set => this.RaiseAndSetIfChanged(ref _isVriPageAvailable, value);
        }

        public bool IsMyanmarPageAvailable
        {
            get => _isMyanmarPageAvailable;
            set => this.RaiseAndSetIfChanged(ref _isMyanmarPageAvailable, value);
        }

        public bool IsPtsPageAvailable
        {
            get => _isPtsPageAvailable;
            set => this.RaiseAndSetIfChanged(ref _isPtsPageAvailable, value);
        }

        public bool IsThaiPageAvailable
        {
            get => _isThaiPageAvailable;
            set => this.RaiseAndSetIfChanged(ref _isThaiPageAvailable, value);
        }

        public bool IsOtherPageAvailable
        {
            get => _isOtherPageAvailable;
            set => this.RaiseAndSetIfChanged(ref _isOtherPageAvailable, value);
        }

        public string? ConstructedAnchor { get; private set; }
        public bool DialogResult { get; private set; }

        public ReactiveCommand<Unit, Unit> NavigateCommand { get; }
        public ReactiveCommand<Unit, Unit> CancelCommand { get; }

        private void Navigate()
        {
            ConstructedAnchor = BuildAnchorName();
            DialogResult = true;

            _logger.Information("Navigate requested: Type={Type}, Input={Input}, Anchor={Anchor}",
                SelectedType, InputNumber, ConstructedAnchor);
        }

        private void Cancel()
        {
            DialogResult = false;
            _logger.Debug("Dialog cancelled");
        }

        private string BuildAnchorName()
        {
            var input = (InputNumber ?? "").Trim();

            switch (SelectedType)
            {
                case NavigationType.Paragraph:
                    // Paragraph number = the leading run of digits (e.g. "123").
                    var paraDigits = new string(input.TakeWhile(char.IsDigit).ToArray());
                    if (paraDigits.Length == 0)
                    {
                        _logger.Warning("No paragraph number found in input: {Input}", InputNumber);
                        return "";
                    }

                    // Paragraph anchors: "para123" or "para123_an5" (for Multi books)
                    var anchor = $"para{paraDigits}";

                    // Handle Multi-type books (CST4 pattern: use book code from current position)
                    // E.g., if currently at "para100_an5", Go To para 200 navigates to "para200_an5"
                    if (_book.BookType == BookType.Multi)
                    {
                        var bookCode = ExtractBookCode(_currentParagraph);
                        if (!string.IsNullOrEmpty(bookCode))
                        {
                            anchor += $"_{bookCode}";
                            _logger.Debug("Multi-type book: Added suffix. Anchor={Anchor}, BookCode={BookCode}", anchor, bookCode);
                        }
                    }

                    return anchor;

                case NavigationType.VriPage:     return BuildPageAnchor("V", input, _vriPage);
                case NavigationType.MyanmarPage: return BuildPageAnchor("M", input, _myanmarPage);
                case NavigationType.PtsPage:     return BuildPageAnchor("P", input, _ptsPage);
                case NavigationType.ThaiPage:    return BuildPageAnchor("T", input, _thaiPage);
                case NavigationType.OtherPage:   return BuildPageAnchor("O", input, _otherPage);

                default:
                    _logger.Warning("Unknown navigation type: {Type}", SelectedType);
                    return "";
            }
        }

        private static readonly Regex ParagraphInputPattern = new(@"^\s*\d+\s*$", RegexOptions.Compiled);
        private static readonly Regex PageInputPattern = new(@"^\s*\d+(\.\d+)?\s*$", RegexOptions.Compiled);

        /// <summary>
        /// Whether the input is a valid target for the given navigation type: a paragraph number ("123")
        /// for paragraphs, or a page ("23") / volume-qualified page ("2.23") for page editions. Replaces
        /// the old "contains any digit" check that accepted junk like "abc123def". (#66)
        /// </summary>
        internal static bool IsValidInput(string? input, NavigationType type)
        {
            if (string.IsNullOrWhiteSpace(input))
                return false;
            return type == NavigationType.Paragraph
                ? ParagraphInputPattern.IsMatch(input)
                : PageInputPattern.IsMatch(input);
        }

        /// <summary>
        /// Build a page-break anchor matching the book XML's @ed+@n format (e.g. "V1.0023"), where @n is
        /// "&lt;volume&gt;.&lt;page padded to 4&gt;". The input may be a bare page ("23") - the volume is then
        /// derived from the page currently shown for this edition - or an explicit "volume.page" ("2.23").
        /// Corpus volumes run 0-7, so the volume must never be hardcoded. (#66)
        /// </summary>
        internal static string BuildPageAnchor(string prefix, string input, string? currentPageDisplay)
        {
            input = (input ?? "").Trim();
            int volume, page;
            int dot = input.IndexOf('.');
            if (dot >= 0)
            {
                if (!int.TryParse(input.Substring(0, dot), out volume) ||
                    !int.TryParse(input.Substring(dot + 1), out page))
                    return "";
            }
            else
            {
                if (!int.TryParse(input, out page))
                    return "";
                volume = DeriveVolume(currentPageDisplay);
            }

            return $"{prefix}{volume}.{page:D4}";
        }

        /// <summary>
        /// The volume of the page currently shown for an edition. ParsePage renders volume-0 pages with no
        /// "0." prefix, so a display with no dot (or "*"/empty) means volume 0; otherwise the volume is the
        /// part before the dot (e.g. "2.45" -> 2). (#66)
        /// </summary>
        internal static int DeriveVolume(string? currentPageDisplay)
        {
            if (!string.IsNullOrEmpty(currentPageDisplay) && currentPageDisplay != "*")
            {
                int dot = currentPageDisplay.IndexOf('.');
                if (dot > 0 && int.TryParse(currentPageDisplay.Substring(0, dot), out int v))
                    return v;
            }
            return 0;
        }

        /// <summary>
        /// Extract book code from paragraph anchor (CST4 pattern)
        /// E.g., "para123_an5" → "an5"
        /// </summary>
        private string ExtractBookCode(string paragraphAnchor)
        {
            if (string.IsNullOrEmpty(paragraphAnchor))
                return "";

            int underscoreIndex = paragraphAnchor.IndexOf('_');
            if (underscoreIndex > 0 && underscoreIndex < paragraphAnchor.Length - 1)
            {
                return paragraphAnchor.Substring(underscoreIndex + 1);
            }

            return "";
        }
    }

    public enum NavigationType
    {
        Paragraph,
        VriPage,
        MyanmarPage,
        PtsPage,
        ThaiPage,
        OtherPage
    }
}
