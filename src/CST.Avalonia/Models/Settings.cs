using System.Collections.Generic;

namespace CST.Avalonia.Models
{
    public class Settings
    {
        public string XmlBooksDirectory { get; set; } = "";
        public string IndexDirectory { get; set; } = "";  // Empty means use default
        public FontSettings FontSettings { get; set; } = new();
        public DeveloperSettings DeveloperSettings { get; set; } = new();
        public XmlUpdateSettings XmlUpdateSettings { get; set; } = new();
    }
    
    public class DeveloperSettings
    {
        public string LogLevel { get; set; } = "Information";
    }
    
    public class XmlUpdateSettings
    {
        public bool EnableAutomaticUpdates { get; set; } = true;
        public string XmlRepositoryOwner { get; set; } = "VipassanaTech";
        public string XmlRepositoryName { get; set; } = "tipitaka-xml";
        public string XmlRepositoryPath { get; set; } = "deva master";
        public string XmlRepositoryBranch { get; set; } = "main";
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