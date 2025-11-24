using System;
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
                x => !string.IsNullOrWhiteSpace(x) && Regex.IsMatch(x, @"\d"));

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
            // Extract numeric part (remove any prefix letters)
            var numericPart = Regex.Replace(InputNumber, @"[^\d]", "");

            if (string.IsNullOrEmpty(numericPart))
            {
                _logger.Warning("No numeric part found in input: {Input}", InputNumber);
                return "";
            }

            switch (SelectedType)
            {
                case NavigationType.Paragraph:
                    // Paragraph anchors: "para123" or "para123_an5" (for Multi books)
                    var anchor = $"para{numericPart}";

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

                case NavigationType.VriPage:
                    return FormatPageAnchor("V", numericPart);

                case NavigationType.MyanmarPage:
                    return FormatPageAnchor("M", numericPart);

                case NavigationType.PtsPage:
                    return FormatPageAnchor("P", numericPart);

                case NavigationType.ThaiPage:
                    return FormatPageAnchor("T", numericPart);

                case NavigationType.OtherPage:
                    return FormatPageAnchor("O", numericPart);

                default:
                    _logger.Warning("Unknown navigation type: {Type}", SelectedType);
                    return "";
            }
        }

        private string FormatPageAnchor(string prefix, string number)
        {
            // CST4 format: "V1.0023" (prefix + "1." + 4-digit padded number)
            if (int.TryParse(number, out int pageNum))
            {
                var anchor = $"{prefix}1.{pageNum:D4}";
                _logger.Debug("Formatted page anchor: {Anchor}", anchor);
                return anchor;
            }

            _logger.Warning("Failed to parse number: {Number}", number);
            return "";
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
