using System.Collections.Generic;
using System.Text.Json.Serialization;
using CST.Conversion;

namespace CST.Avalonia.Models
{
    public class Settings
    {
        /// <summary>
        /// Settings-file schema version, for backward-compatible migration. (#78)
        /// </summary>
        public string Version { get; set; } = "1.0";

        public string XmlBooksDirectory { get; set; } = "";
        public string IndexDirectory { get; set; } = "";  // Empty means use default
        public FontSettings FontSettings { get; set; } = new();
        public DeveloperSettings DeveloperSettings { get; set; } = new();
        public XmlUpdateSettings XmlUpdateSettings { get; set; } = new();
        public AiSettings Ai { get; set; } = new();
    }

    /// <summary>
    /// The "AI" settings area. <see cref="Enabled"/> is the master "Enable AI Features" switch (default OFF);
    /// the sub-permissions default ON, so enabling the master turns everything on and the user can then pare
    /// back. Effective state is always master AND the specific permission, so unchecking the master disables
    /// everything at once. Secrets (the local-API port + bearer token) are never stored here — they are
    /// written to <c>local-api.json</c> at runtime.
    /// </summary>
    public class AiSettings
    {
        /// <summary>Master switch — "Enable AI Features". Default OFF (opt-in); nothing AI-related runs while false.</summary>
        public bool Enabled { get; set; } = false;

        public LocalApiSettings LocalApi { get; set; } = new();

        /// <summary>The local API server should run only when the master and the local-API permission are both on.</summary>
        [JsonIgnore]
        public bool LocalApiEnabled => Enabled && LocalApi.Enabled;

        /// <summary>Agents may drive the reader only when the local API is enabled and remote control is permitted.</summary>
        [JsonIgnore]
        public bool RemoteControlAllowed => LocalApiEnabled && LocalApi.AllowRemoteControl;
    }

    /// <summary>Permissions for the loopback API server that exposes the corpus tools to agents (surface C).</summary>
    public class LocalApiSettings
    {
        /// <summary>Run the local API (corpus data access). On by default under the master switch.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Let agents drive the reader (navigate/highlight) vs. read-only. On by default under the master.</summary>
        public bool AllowRemoteControl { get; set; } = true;

        /// <summary>Loopback port; 0 = ephemeral (recommended, advertised via local-api.json). Fixed only for debugging.</summary>
        public int Port { get; set; } = 0;
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

        /// <summary>
        /// Typed lookup of a script's font setting. Centralizes the <see cref="ScriptKeys"/> mapping so callers
        /// use the <see cref="Script"/> enum instead of raw string keys. (#78)
        /// </summary>
        public bool TryGetFont(Script script, out ScriptFontSetting? setting) =>
            ScriptFonts.TryGetValue(ScriptKeys.Of(script), out setting);
    }
    
    public class ScriptFontSetting
    {
        public string FontFamily { get; set; } = "";
        public int FontSize { get; set; } = 12;
    }
}