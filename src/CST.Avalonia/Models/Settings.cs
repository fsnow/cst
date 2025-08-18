using System.Collections.Generic;

namespace CST.Avalonia.Models
{
    public class Settings
    {
        public string XmlBooksDirectory { get; set; } = "";
        public string IndexDirectory { get; set; } = "";  // Empty means use default
        public SearchSettings SearchSettings { get; set; } = new();
        public int MaxRecentBooks { get; set; } = 10;
        public bool ShowWelcomeOnStartup { get; set; } = true;
        public string Theme { get; set; } = "Light";
        public FontSettings FontSettings { get; set; } = new();
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
    
    public class FontSettings
    {
        public Dictionary<string, ScriptFontSetting> ScriptFonts { get; set; } = new();
        public string LocalizationFontFamily { get; set; } = ""; // Empty means use system default
        public int LocalizationFontSize { get; set; } = 12;
        
        public FontSettings()
        {
            // Initialize default font settings for each script
            // Empty font family means use system default for that script
            ScriptFonts = new Dictionary<string, ScriptFontSetting>
            {
                ["Latin"] = new ScriptFontSetting { FontFamily = "", FontSize = 12 },
                ["Devanagari"] = new ScriptFontSetting { FontFamily = "", FontSize = 16 }, // Larger for readability
                ["Bengali"] = new ScriptFontSetting { FontFamily = "", FontSize = 13 },
                ["Cyrillic"] = new ScriptFontSetting { FontFamily = "", FontSize = 12 },
                ["Gujarati"] = new ScriptFontSetting { FontFamily = "", FontSize = 13 },
                ["Gurmukhi"] = new ScriptFontSetting { FontFamily = "", FontSize = 13 },
                ["Kannada"] = new ScriptFontSetting { FontFamily = "", FontSize = 13 },
                ["Khmer"] = new ScriptFontSetting { FontFamily = "", FontSize = 13 },
                ["Malayalam"] = new ScriptFontSetting { FontFamily = "", FontSize = 13 },
                ["Myanmar"] = new ScriptFontSetting { FontFamily = "", FontSize = 13 },
                ["Sinhala"] = new ScriptFontSetting { FontFamily = "", FontSize = 13 },
                ["Telugu"] = new ScriptFontSetting { FontFamily = "", FontSize = 13 },
                ["Thai"] = new ScriptFontSetting { FontFamily = "", FontSize = 13 },
                ["Tibetan"] = new ScriptFontSetting { FontFamily = "", FontSize = 14 }
            };
        }
    }
    
    public class ScriptFontSetting
    {
        public string FontFamily { get; set; } = "";
        public int FontSize { get; set; } = 12;
    }
}