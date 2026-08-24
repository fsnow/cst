using System.ComponentModel;
using Avalonia.Controls;
using Avalonia.Interactivity;
using Avalonia.Markup.Xaml;
using Avalonia.VisualTree;
using CST.Avalonia.ViewModels;

namespace CST.Avalonia.Views
{
    /// <summary>
    /// The Providers tab of the AI settings — configured connections above, a catalogue of named endpoints
    /// below. (#691)
    /// </summary>
    public partial class AiProvidersView : UserControl
    {
        private AiConnectionsViewModel? _bound;

        public AiProvidersView()
        {
            InitializeComponent();
            DataContextChanged += OnDataContextChanged;
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

        private void OnDataContextChanged(object? sender, System.EventArgs e)
        {
            if (_bound is not null) _bound.PropertyChanged -= OnViewModelChanged;
            _bound = DataContext as AiConnectionsViewModel;
            if (_bound is not null) _bound.PropertyChanged += OnViewModelChanged;
        }

        /// <summary>
        /// Puts the top of the tab back in view whenever the sheet opens or closes.
        ///
        /// <para><b>Closing:</b> saving would otherwise leave the pane scrolled to wherever the reader was
        /// when they reached the catalogue — the bottom — while the connection they just added is a row at
        /// the very top. The add then reads as having done nothing, which is the confusion that made every
        /// add open a sheet in the first place.</para>
        ///
        /// <para><b>Opening:</b> the sheet replaces the list inside the same scroll viewer, which keeps the
        /// offset it had. Reaching Custom endpoint means scrolling to the bottom of ~166 providers, so the
        /// form arrives scrolled past its own first field — the reader is looking at the Save button of a
        /// form they have not seen the top of. Both directions are the same fix: a screen the reader has not
        /// seen before starts at its beginning.</para>
        /// </summary>
        private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AiConnectionsViewModel.IsListing)) return;
            if (this.FindAncestorOfType<ScrollViewer>() is { } scroll) scroll.Offset = default;
        }

        /// <summary>
        /// Focuses the API key box as the editor sheet appears.
        ///
        /// <para>Pasting a key is the only reason a provider sheet is open, and reaching the box took a click
        /// into the field that was already the obvious next move. With Save marked <c>IsDefault</c>, the whole
        /// interaction becomes paste-and-Enter.</para>
        ///
        /// <para><b>Wired to the box's own Loaded rather than driven from the view.</b> It lives inside a
        /// <c>DataTemplate</c>, so there is no named control for the view to find; and the sheet is created
        /// fresh each time it opens, so Loaded fires exactly when focus should move. It is also why this
        /// cannot steal focus mid-edit: a control that is loading is not one the reader is typing in.</para>
        ///
        /// <para>Disabled where the credential store is unavailable (<c>CanStoreKeys</c>) — focusing a box
        /// that cannot be typed in would be worse than leaving focus alone, so that case is skipped.</para>
        /// </summary>
        private void OnApiKeyBoxLoaded(object? sender, RoutedEventArgs e)
        {
            if (sender is TextBox { IsEnabled: true, IsVisible: true } box) box.Focus();
        }
    }
}
