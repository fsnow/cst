using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Markup.Xaml;
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
    public AiAssistantPanel() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);

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
