using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CST.Avalonia.Views;

/// <summary>
/// The in-app assistant's view. (#586)
///
/// <para>
/// Deliberately code-free: everything it shows is bound, and everything it does is a command on
/// <c>AiAssistantViewModel</c>. There is no WebView here and there must never be one — see the panel's XAML
/// header and AI_SURFACE_B.md §8.
/// </para>
/// </summary>
public partial class AiAssistantPanel : UserControl
{
    public AiAssistantPanel() => InitializeComponent();

    private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
}
