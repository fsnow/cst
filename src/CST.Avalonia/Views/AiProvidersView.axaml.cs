using System.ComponentModel;
using Avalonia.Controls;
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
        /// Puts the top of the tab back in view when the sheet closes.
        ///
        /// <para>Without this, saving leaves the settings pane scrolled to wherever the reader was when they
        /// reached the catalogue — the bottom — while the connection they just added is a row at the very
        /// top. The add then reads as having done nothing, which is exactly the confusion that made every
        /// add open a sheet in the first place.</para>
        /// </summary>
        private void OnViewModelChanged(object? sender, PropertyChangedEventArgs e)
        {
            if (e.PropertyName != nameof(AiConnectionsViewModel.IsListing)) return;
            if (_bound?.IsListing != true) return;
            if (this.FindAncestorOfType<ScrollViewer>() is { } scroll) scroll.Offset = default;
        }
    }
}
