using System;
using System.ComponentModel;
using System.Reactive;
using System.Reactive.Linq;
using Avalonia.Controls;
using Avalonia.Input;
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

        /// <summary>
        /// Escape cancels the connection sheet, and only then is allowed to reach the Settings window.
        /// (#943, fable review)
        ///
        /// <para><b>The sheet is the one place in Settings that is NOT applied as you type.</b> Everything
        /// else pushes straight through to the settings service; this is a draft, committed only by
        /// <c>Save</c> - which is where <c>StoreKey</c> files the API key
        /// (<c>AiConnectionEditorViewModel.Save</c>). The key box is focused the moment the sheet opens,
        /// because pasting a key is the only reason it is open, so "focus in a TextBox with uncommitted
        /// work" is the NORMAL state here rather than an edge case. Escape closing the whole window from
        /// there would discard the pasted key silently.</para>
        ///
        /// <para><b>Why here rather than on the window, and why not <c>IsCancel</c>.</b> A bubbling key
        /// event reaches this view before the window, so handling it here settles the innermost context
        /// first - Escape cancels the sheet, a second Escape closes Settings, which is what the reader who
        /// asked for this expects from VS Code. <c>IsCancel="True"</c> on the sheet's Cancel button would
        /// NOT work: Avalonia runs class handlers before instance handlers at each element, and
        /// <c>IsCancel</c> is an instance handler on the window root, so the window's own
        /// <c>OnKeyDown</c> would close Settings first.</para>
        /// </summary>
        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);

            if (e.Handled || e.Key != Key.Escape) return;
            if (DataContext is not AiConnectionsViewModel { IsEditing: true } vm) return;

            var cancel = vm.Editor?.CancelCommand;
            if (cancel is null) return;

            e.Handled = true;
            cancel.Execute().Subscribe();
        }

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
