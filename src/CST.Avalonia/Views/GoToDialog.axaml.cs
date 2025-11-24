using System;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Markup.Xaml;
using Avalonia.Threading;
using CST.Avalonia.ViewModels;
using Serilog;

namespace CST.Avalonia.Views
{
    public partial class GoToDialog : Window
    {
        private readonly ILogger _logger;
        private GoToDialogViewModel? _viewModel;

        public GoToDialog()
        {
            _logger = Log.ForContext<GoToDialog>();
            InitializeComponent();
#if DEBUG
            this.AttachDevTools();
#endif
        }

        public GoToDialog(GoToDialogViewModel viewModel) : this()
        {
            _viewModel = viewModel;
            DataContext = viewModel;

            // Wire up command completion to close dialog
            viewModel.NavigateCommand.Subscribe(_ =>
            {
                _logger.Debug("Navigate command executed, closing dialog with result=true");
                Close(true);
            });

            viewModel.CancelCommand.Subscribe(_ =>
            {
                _logger.Debug("Cancel command executed, closing dialog with result=false");
                Close(false);
            });

            // Focus the number textbox when dialog opens
            Opened += (s, e) =>
            {
                Dispatcher.UIThread.Post(() =>
                {
                    var textBox = this.FindControl<TextBox>("NumberTextBox");
                    textBox?.Focus();
                    textBox?.SelectAll();

                    // Wire up TextChanged event for letter shortcuts (like CST4)
                    if (textBox != null)
                    {
                        textBox.TextChanged += OnNumberTextBoxChanged;
                    }
                }, DispatcherPriority.Loaded);
            };
        }

        private void InitializeComponent()
        {
            AvaloniaXamlLoader.Load(this);
        }

        private void OnNumberTextBoxChanged(object? sender, EventArgs e)
        {
            if (sender is not TextBox textBox || _viewModel == null)
                return;

            var text = textBox.Text ?? "";

            // CST4 pattern: keyboard shortcut to save a mouse click
            // Type letter in number box to switch radio buttons
            if (text.Length >= 1 && System.Text.RegularExpressions.Regex.IsMatch(text, @"^[VvMmPpTt]"))
            {
                var letter = text.Substring(0, 1).ToUpperInvariant();
                var numberPart = text.Substring(1);

                // Select the appropriate navigation type if available
                var newType = letter switch
                {
                    "V" when _viewModel.IsVriPageAvailable => NavigationType.VriPage,
                    "M" when _viewModel.IsMyanmarPageAvailable => NavigationType.MyanmarPage,
                    "P" when _viewModel.IsPtsPageAvailable => NavigationType.PtsPage,
                    "T" when _viewModel.IsThaiPageAvailable => NavigationType.ThaiPage,
                    _ => (NavigationType?)null
                };

                if (newType.HasValue)
                {
                    _logger.Debug("Auto-selecting navigation type: {Type} from input: {Input}, removing letter", newType.Value, text);

                    // Update the ViewModel's selected type
                    _viewModel.SelectedType = newType.Value;

                    // Remove the letter from the textbox (like CST4)
                    textBox.Text = numberPart;

                    // Keep focus on textbox
                    textBox.Focus();
                }
            }
        }
    }
}
