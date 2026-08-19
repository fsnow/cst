using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using CST.Avalonia.ViewModels;

namespace CST.Avalonia.Views;

/// <summary>
/// The in-app assistant's view. (#586)
///
/// <para>
/// Everything it shows is bound and everything it does is a command on <c>AiAssistantViewModel</c>, with one
/// exception below: a drag has no command form. There is no WebView here and there must never be one — see
/// the panel's XAML header and AI_SURFACE_B.md §8.
/// </para>
/// </summary>
public partial class AiAssistantPanel : UserControl
{
    private AiModelPickerViewModel? _picker;

    private bool _syncingFlyout;

    public AiAssistantPanel()
    {
        // The generated InitializeComponent, NOT a hand-written AvaloniaXamlLoader.Load(this). This file
        // used to carry the hand-written one, which was harmless until a control here gained an x:Name:
        // only the generated initializer assigns the fields those names produce, so a hand-written one
        // loads the XAML and leaves ModelChip null. The result was a NullReferenceException three lines
        // below, during the dock's layout pass, which presents as the app failing to start at all.
        InitializeComponent();
        DataContextChanged += OnDataContextChanged;

        // Mirror the flyout's own state into the view model. Needed in both directions: the view model has
        // to KNOW it is open for its later close to register as a change, and a reader who dismisses the
        // list by clicking away must not leave it believing otherwise.
        if (ModelChip.Flyout is { } flyout)
        {
            flyout.Opened += (_, _) => SetOpen(true);
            flyout.Closed += (_, _) => SetOpen(false);
        }
    }

    private void SetOpen(bool open)
    {
        if (_picker is null) return;
        _syncingFlyout = true;
        try { _picker.IsOpen = open; }
        finally { _syncingFlyout = false; }
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_picker is not null) _picker.PropertyChanged -= OnPickerChanged;
        _picker = (DataContext as AiAssistantViewModel)?.ModelPicker;
        if (_picker is not null) _picker.PropertyChanged += OnPickerChanged;
    }

    /// <summary>
    /// Closes the model flyout once a choice is made. (#693)
    ///
    /// <para>Code-behind because a <c>Flyout</c> owns its own open state: it opens itself when the chip is
    /// clicked and there is no bindable property to close it through. Without this, picking a model leaves
    /// the list sitting over the composer — and the reader's next act is to dismiss a popup rather than to
    /// ask the question they opened it for.</para>
    ///
    /// <para>Driven from the view model's own <c>IsOpen</c> rather than from each row's click handler, so
    /// every route that ends the choice — picking a model, or leaving for Settings — closes it the same
    /// way.</para>
    /// </summary>
    private void OnPickerChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_syncingFlyout) return;   // the flyout told us; do not tell it back
        if (e.PropertyName != nameof(AiModelPickerViewModel.IsOpen)) return;
        if (_picker?.IsOpen == false) ModelChip.Flyout?.Hide();
    }

    /// <summary>
    /// Drag the reasoning panel taller or shorter. The one piece of code-behind here, because
    /// <c>DragDelta</c> carries the movement itself and there is no binding that expresses "add this delta to
    /// that property" — the alternative is a behaviour class doing the same three lines further away from
    /// the control it serves.
    /// </summary>
    private void OnReasoningResize(object? sender, global::Avalonia.Input.VectorEventArgs e)
    {
        if (DataContext is AiAssistantViewModel vm)
            vm.ResizeReasoning(e.Vector.Y);
    }
}
