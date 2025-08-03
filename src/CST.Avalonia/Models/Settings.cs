using System.Collections.Generic;

namespace CST.Avalonia.Models
{
    public class Settings
    {
        public string XmlBooksDirectory { get; set; } = "";
        public SearchSettings SearchSettings { get; set; } = new();
        public int MaxRecentBooks { get; set; } = 10;
        public bool ShowWelcomeOnStartup { get; set; } = true;
        public string Theme { get; set; } = "Light";
        public DeveloperSettings DeveloperSettings { get; set; } = new();
    }

    public class SearchSettings
    {
        public bool CaseSensitive { get; set; } = false;
        public bool WholeWords { get; set; } = false;
        public bool UseRegex { get; set; } = false;
        public int MaxSearchResults { get; set; } = 1000;
    }
    
    public class DeveloperSettings
    {
        public string LogLevel { get; set; } = "Information";
    }
}