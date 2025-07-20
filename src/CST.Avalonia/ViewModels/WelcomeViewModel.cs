using ReactiveUI;

namespace CST.Avalonia.ViewModels
{
    public class WelcomeViewModel : ViewModelBase
    {
        public string Title => "Chaṭṭha Saṅgāyana Tipiṭaka";
        public string Subtitle => "Pāli Text Reader";
        public string Instructions => "Select a book from the tree on the left to begin reading.";
        
        public string[] QuickActions => new[]
        {
            "📖  Double-click a book in the tree to open it",
            "🔍  Use search to find specific texts",
            "📝  Books open in new tabs for easy navigation", 
            "🔤  Change script using the dropdown above",
            "⚡  Use keyboard shortcuts for faster navigation"
        };
    }
}