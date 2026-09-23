using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Threading;
using Avalonia.VisualTree;
using CST.Avalonia.ViewModels;
using Serilog;

namespace CST.Avalonia.Views;

/// <summary>
/// The in-app assistant's view. (#586)
///
/// <para>
/// Everything it shows is bound. What it does is a command on <c>AiAssistantViewModel</c>, except what is below:
/// a drag, which has no command form, and the session list's row actions and Compact, which call the view
/// model's methods directly because each also has view work to do (closing a flyout, focusing a box). There is no WebView here and there must never be one — see
/// the panel's XAML header and AI_SURFACE_B.md §8.
/// </para>
/// </summary>
public partial class AiAssistantPanel : UserControl
{
    private AiModelPickerViewModel? _picker;

    private bool _syncingFlyout;
    private AiEffortPickerViewModel? _effort;
    private bool _syncingEffortFlyout;

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

        // The same wiring for the effort chip (#671). It is not optional decoration: a Flyout owns its own
        // open state, so without this, choosing a level leaves the list sitting over the composer and the
        // reader's next act is to dismiss a popup rather than ask the question they opened it for. The effort
        // picker had the IsOpen state and nothing observing it, which is the same bug this block already
        // exists to prevent — it just had not been extended to the second chip. (fable review)
        // A rename box or a "Delete?" left open when the list is dismissed is not waiting for anything: the
        // reader looked away, which is an answer. Reopening the list shows every row plain. (#997)
        if (SessionsChip.Flyout is { } sessionsFlyout)
            sessionsFlyout.Closed += (_, _) => ResetSessionRows();

        if (EffortChip.Flyout is { } effortFlyout)
        {
            effortFlyout.Opened += (_, _) => SetEffortOpen(true);
            effortFlyout.Closed += (_, _) => SetEffortOpen(false);
        }
    }

    private void SetOpen(bool open)
    {
        if (_picker is null) return;
        _syncingFlyout = true;
        try { _picker.IsOpen = open; }
        finally { _syncingFlyout = false; }
    }

    private void SetEffortOpen(bool open)
    {
        if (_effort is null) return;
        _syncingEffortFlyout = true;
        try { _effort.IsOpen = open; }
        finally { _syncingEffortFlyout = false; }
    }

    private void OnDataContextChanged(object? sender, System.EventArgs e)
    {
        if (_picker is not null) _picker.PropertyChanged -= OnPickerChanged;
        _picker = (DataContext as AiAssistantViewModel)?.ModelPicker;
        if (_picker is not null) _picker.PropertyChanged += OnPickerChanged;

        if (_effort is not null) _effort.PropertyChanged -= OnEffortChanged;
        _effort = (DataContext as AiAssistantViewModel)?.EffortPicker;
        if (_effort is not null) _effort.PropertyChanged += OnEffortChanged;
    }

    /// <summary>Closes the effort flyout once a level is chosen — see <see cref="OnPickerChanged"/> for why
    /// this cannot be a binding. (#671)</summary>
    private void OnEffortChanged(object? sender, System.ComponentModel.PropertyChangedEventArgs e)
    {
        if (_syncingEffortFlyout) return;
        if (e.PropertyName != nameof(AiEffortPickerViewModel.IsOpen)) return;
        if (_effort?.IsOpen == false) EffortChip.Flyout?.Hide();
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

    // ---- Compact (#998). CompactAsync is called directly, not CompactCommand, for the reason Forget gives;
    // with no argument it uses CompactInstructions. The flyout closes when the summary starts, so the reader
    // sees the transcript it is working on. ----

    private void OnCompact(object? sender, RoutedEventArgs e) => Compact();

    private void OnCompactInstructionsKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;
        e.Handled = true;
        Compact();
    }

    private void Compact()
    {
        if (DataContext is not AiAssistantViewModel vm || !vm.CanCompact) return;
        CompactChip.Flyout?.Hide();
        Forget(vm.CompactAsync());
    }

    // ---- The session list (#997). Code-behind because each action carries view work beside it - closing the
    // list on a switch, focusing the rename box, closing other rows' prompts - and a Flyout has no bindable
    // open state (see OnPickerChanged). The handlers take the row from the clicked control's DataContext and
    // call the panel's own methods, which is where the rules (busy, refused names, which row may go) live. ----

    private static AiSessionRowViewModel? RowOf(object? sender) =>
        (sender as Control)?.DataContext as AiSessionRowViewModel;

    /// <summary>
    /// Run a session operation without waiting for it. The panel's methods, not its commands: a ReactiveCommand
    /// refuses to run while its previous run is still going, and a rename or delete takes a store write plus a
    /// full re-listing - so the second of two quick deletes, the natural way to prune a long list, was refused
    /// after its row had already closed its prompt, and looked accepted (review of #1009). The methods keep
    /// their own guards (busy, refused names, which row may go) and catch their own store failures.
    /// </summary>
    private static void Forget(Task operation) =>
        operation.ContinueWith(
            t => Log.Error(t.Exception, "An Assistant session operation failed"),
            CancellationToken.None, TaskContinuationOptions.OnlyOnFaulted, TaskScheduler.Default);

    private void OnSwitchSession(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AiAssistantViewModel vm || RowOf(sender) is not { } row) return;
        SessionsChip.Flyout?.Hide();
        Forget(vm.SwitchToSessionAsync(row.Id));
    }

    private void OnBeginRename(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        foreach (var other in OtherRows(row)) { other.CancelRename(); other.CancelDelete(); }
        row.BeginRename();

        // The box becomes visible on the next layout pass; focus it then, with the old name selected so typing
        // replaces it — what a rename box is for.
        var box = (sender as Control)?.FindAncestorOfType<StackPanel>()
            ?.GetVisualDescendants().OfType<TextBox>().FirstOrDefault();
        if (box is null) return;
        Dispatcher.UIThread.Post(() =>
        {
            box.Focus();
            box.SelectAll();
        }, DispatcherPriority.Loaded);
    }

    /// <summary>
    /// A rename box that appears already open is a row whose container was rebuilt - the list reordered under
    /// it (a finished answer moves its conversation to the top) and <c>Move</c> gave the row a new container.
    /// The row's state survived; give the new box the focus the old one had, caret at the end, so typing
    /// carries on. (review of #1009, measured headless)
    /// </summary>
    private void OnRenameBoxAttached(object? sender, global::Avalonia.VisualTreeAttachmentEventArgs e)
    {
        if (sender is not TextBox box) return;
        Dispatcher.UIThread.Post(() =>
        {
            if (box.DataContext is AiSessionRowViewModel { IsRenaming: true } && box.IsEffectivelyVisible)
            {
                box.Focus();
                box.CaretIndex = box.Text?.Length ?? 0;
            }
        }, DispatcherPriority.Loaded);
    }

    private void OnCommitRename(object? sender, RoutedEventArgs e) => CommitRename(RowOf(sender));

    private void OnCancelRename(object? sender, RoutedEventArgs e) => RowOf(sender)?.CancelRename();

    private void OnRenameKeyDown(object? sender, KeyEventArgs e)
    {
        switch (e.Key)
        {
            case Key.Enter:
                CommitRename(RowOf(sender));
                e.Handled = true;
                break;
            case Key.Escape:
                // Handled, so Escape closes the box and not the whole list.
                RowOf(sender)?.CancelRename();
                e.Handled = true;
                break;
        }
    }

    private void CommitRename(AiSessionRowViewModel? row)
    {
        if (DataContext is not AiAssistantViewModel vm || row is null) return;
        if (row.CommitRename() is { } rename) Forget(vm.RenameSessionAsync(rename.Id, rename.Name));
    }

    private void OnBeginDelete(object? sender, RoutedEventArgs e)
    {
        if (RowOf(sender) is not { } row) return;
        foreach (var other in OtherRows(row)) { other.CancelRename(); other.CancelDelete(); }
        row.BeginDelete();
    }

    private void OnCancelDelete(object? sender, RoutedEventArgs e) => RowOf(sender)?.CancelDelete();

    private void OnConfirmDelete(object? sender, RoutedEventArgs e)
    {
        if (DataContext is not AiAssistantViewModel vm || RowOf(sender) is not { } row) return;
        row.CancelDelete();
        Forget(vm.DeleteSessionAsync(row.Id));
    }

    private IEnumerable<AiSessionRowViewModel> OtherRows(AiSessionRowViewModel row) =>
        (DataContext as AiAssistantViewModel)?.Sessions.Where(r => !ReferenceEquals(r, row))
        ?? Enumerable.Empty<AiSessionRowViewModel>();

    private void ResetSessionRows()
    {
        if (DataContext is not AiAssistantViewModel vm) return;
        foreach (var row in vm.Sessions) { row.CancelRename(); row.CancelDelete(); }
    }
}
