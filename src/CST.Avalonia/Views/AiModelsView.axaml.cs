using Avalonia.Controls;
using Avalonia.Markup.Xaml;

namespace CST.Avalonia.Views
{
    /// <summary>The Models tab of the AI settings — each connection's models, with the toggles that decide
    /// what the Assistant offers. (#692, #674)</summary>
    public partial class AiModelsView : UserControl
    {
        public AiModelsView()
        {
            InitializeComponent();
        }

        private void InitializeComponent() => AvaloniaXamlLoader.Load(this);
    }
}
