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
        public DpdUpdateSettings DpdUpdateSettings { get; set; } = new();
        public AiSettings Ai { get; set; } = new();

        /// <summary>
        /// When false, hardware acceleration is disabled for the embedded WebView (forces software compositing).
        /// Mitigates the CEF off-screen-rendering "black view" stall seen under some GPUs / virtualized drivers on
        /// Windows (#401). Default true (accelerated). Takes effect on next launch (applied before CEF initializes).
        /// </summary>
        public bool UseHardwareAcceleration { get; set; } = true;
    }

    /// <summary>
    /// The "AI" settings area. <see cref="Enabled"/> is the master "Enable AI Features" switch (default OFF);
    /// the sub-permissions default ON, so enabling the master turns everything on and the user can then pare
    /// back. Effective state is always master AND the specific permission, so unchecking the master disables
    /// everything at once. No secrets are stored here: the loopback port is ephemeral and the bearer token is
    /// minted per session, both written only to <c>local-api.json</c> at runtime. (True again since #280 removed
    /// the interim persisted port/token fields.)
    /// </summary>
    public class AiSettings
    {
        /// <summary>Master switch — "Enable AI Features". Default OFF (opt-in); nothing AI-related runs while false.</summary>
        public bool Enabled { get; set; } = false;

        public LocalApiSettings LocalApi { get; set; } = new();

        /// <summary>Surface B — the assistant inside the reader. Off until configured.</summary>
        public ChatSettings Chat { get; set; } = new();

        /// <summary>The REST (/v1) surface runs only when the master and the local-API permission are both on.</summary>
        [JsonIgnore]
        public bool LocalApiEnabled => Enabled && LocalApi.Enabled;

        /// <summary>The MCP (/mcp) surface runs only when the master and the MCP permission are both on. Separate
        /// from <see cref="LocalApiEnabled"/> so a user can expose the /v1 REST surface (code agents) without the
        /// /mcp chat-client surface, or vice versa.</summary>
        [JsonIgnore]
        public bool McpEnabled => Enabled && LocalApi.EnableMcpServer;

        /// <summary>The loopback Kestrel host runs if EITHER surface is enabled — /v1 and /mcp ride the same server.</summary>
        [JsonIgnore]
        public bool ServerShouldRun => LocalApiEnabled || McpEnabled;

        /// <summary>Agents may drive the reader only when a server surface is running and remote control is
        /// permitted. Deliberately keyed to <see cref="ServerShouldRun"/> rather than <see cref="LocalApiEnabled"/>:
        /// navigate is offered over BOTH /v1 and /mcp, so tying consent to the REST transport would leave an
        /// MCP-only configuration denying every navigate while telling the user to enable a checkbox that is
        /// already ticked. Unreachable through today's Settings UI, but #280 gives MCP its own toggle. (fable LOW-5)</summary>
        [JsonIgnore]
        public bool RemoteControlAllowed => ServerShouldRun && LocalApi.AllowRemoteControl;
    }

    /// <summary>
    /// The in-app assistant (surface B). Everything here is user-visible configuration EXCEPT the API key,
    /// which is deliberately absent: keys belong in the OS credential store (#579), never in a settings file
    /// that gets pasted into bug reports. The UI that edits these is #585; until it exists they are hand-edited.
    /// </summary>
    public class ChatSettings
    {
        /// <summary>Effective only under the AI master switch, like every other surface-B/C permission.</summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// Every endpoint the reader has configured. (#689)
        ///
        /// <para>Replaces the single scalar <c>Provider</c>/<c>BaseUrl</c>/<c>Model</c> this shipped with.
        /// Those were deleted outright rather than deprecated: <c>ChatSettings</c> postdates the beta 5 tag
        /// (<c>git grep -l ChatSettings v5.0.0-beta.5</c> returns nothing), so there is no persisted state in
        /// the wild to migrate and no reader to break.</para>
        ///
        /// <para>Plural because switching endpoints is the point — comparing two models on the same passage
        /// is the task the assistant exists for, and it was impossible while one base URL and one credential
        /// slot had to be overwritten each time.</para>
        /// </summary>
        public List<AiConnectionRecord> Connections { get; set; } = new();

        /// <summary>The connection a request goes to, by <see cref="AiConnectionRecord.Id"/>. Null until one
        /// is configured.</summary>
        public string? ActiveConnectionId { get; set; }

        /// <summary>The model within that connection. Null until one is chosen.</summary>
        public string? ActiveModelId { get; set; }

        /// <summary>
        /// Language the ANSWER is written in — a separate axis from the script quoted Pāli is rendered in
        /// (AI_SURFACE_B.md §9). Not optional in the bundle: "translate" has to mean translate into something.
        /// </summary>
        public string AnswerLanguage { get; set; } = "English";
    }

    /// <summary>
    /// One configured endpoint, as persisted. (#689)
    ///
    /// <para>Deliberately a mutable class with plain collections rather than the
    /// <c>CST.Avalonia.Models.Ai.AiConnection</c> record: this is what
    /// <c>System.Text.Json</c> round-trips into <c>settings.json</c>, and it carries only what is actually
    /// stored. The runtime record adds what is <i>derived</i> — where the credential came from, and whether
    /// the endpoint has been reached — neither of which belongs in a settings file.</para>
    /// </summary>
    public class AiConnectionRecord
    {
        /// <summary>Stable slug, immutable once created, and the account the credential is filed under.</summary>
        public string Id { get; set; } = "";

        public string DisplayName { get; set; } = "";

        /// <summary><c>anthropic</c> or <c>openai-compatible</c>. A string rather than the enum so a rename
        /// on our side cannot silently invalidate a reader's settings file.</summary>
        public string Kind { get; set; } = "openai-compatible";

        /// <summary>May contain <c>{key}</c> placeholders filled from <see cref="Inputs"/> — Azure and
        /// Cloudflare have a URL shape rather than a URL.</summary>
        public string BaseUrl { get; set; } = "";

        public List<AiModelRecord> Models { get; set; } = new();

        /// <summary>Extra request headers. Never the credential, which lives in the OS credential store.</summary>
        public Dictionary<string, string> Headers { get; set; } = new();

        /// <summary>Answers to the preset's prompts — resource name, account id, region — substituted into
        /// <see cref="BaseUrl"/> and <see cref="Headers"/>.</summary>
        public Dictionary<string, string> Inputs { get; set; } = new();

        /// <summary>
        /// Which header carries the credential. Azure uses <c>api-key</c> and expects <c>Authorization</c> to
        /// be ABSENT rather than also present, so this REPLACES the auth header rather than adding one.
        /// </summary>
        public string AuthHeaderName { get; set; } = "Authorization";

        /// <summary>Prefix before the credential, or null for a bare value. Bearer for almost everything;
        /// null for Azure.</summary>
        public string? AuthScheme { get; set; } = "Bearer";
    }

    /// <summary>One model a connection offers, as persisted.</summary>
    public class AiModelRecord
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";

        /// <summary>Whether it appears in the per-turn picker. Defaults true: all-on is neutral, whereas a
        /// pre-selected subset would be a quality verdict (#670/#681).</summary>
        public bool Enabled { get; set; } = true;
    }

    /// <summary>Permissions for the loopback API server that exposes the corpus tools to agents (surface C).</summary>
    public class LocalApiSettings
    {
        /// <summary>Expose the /v1 REST surface (corpus data access for code-capable agents). On by default under the master.</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>Expose the /mcp MCP surface (for chat clients, via the app's <c>--mcp-bridge</c> relay). On by
        /// default under the master.</summary>
        public bool EnableMcpServer { get; set; } = true;

        /// <summary>Let agents drive the reader (navigate/highlight) vs. read-only. On by default under the master.</summary>
        public bool AllowRemoteControl { get; set; } = true;

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

    /// <summary>
    /// Update settings for the derived dictionary assets (dpd-cst-subset, dppn, …). <see cref="EnableAutomaticUpdates"/>
    /// (named to match <see cref="XmlUpdateSettings.EnableAutomaticUpdates"/>) toggles the background
    /// check-for-a-newer-release, NOT whether the features work: availability is driven purely by each asset FILE
    /// being present, so a manually dropped-in file works regardless of this flag. Points at the cst-dictionaries
    /// repo's releases. (#390/#468)
    /// </summary>
    public class DpdUpdateSettings
    {
        public bool EnableAutomaticUpdates { get; set; } = true;
        public string RepositoryOwner { get; set; } = "fsnow";
        public string RepositoryName { get; set; } = "cst-dictionaries";
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

        /// <summary>
        /// The book-text font FACE for this script, as a CSS font stack. Empty means "use the shipped
        /// default" from <c>BookFontDefaults</c> — stored as empty rather than as a copy of the default, so
        /// a user who never chose a face picks up any future change to the shipped stack. (#42)
        ///
        /// <para>
        /// This is the third distinct font system in the app and must not be confused with
        /// <see cref="FontFamily"/> above, which sizes and styles app CHROME for this script — the book
        /// tree, search results, the dictionary pane. This one applies only to book content, and reaches it
        /// by being injected into the stylesheet at transform time rather than through any CSS the app
        /// writes.
        /// </para>
        /// </summary>
        public string BookFontFamily { get; set; } = "";

        /// <summary>
        /// Book-text zoom for this script, as a multiplier on the stylesheet's own sizes. 1.0 renders the
        /// shipped ladder exactly (body 12pt / chapter 18pt / book 21pt / nikaya 24pt). (#572)
        ///
        /// <para>
        /// NOT a multiplier on <see cref="FontSize"/> above, despite sharing this class. <see cref="FontSize"/>
        /// sizes app <i>chrome</i> for this script — the book tree, search results, the dictionary pane — and
        /// zoom never touches any of those. Zoom applies only to book content, through Chromium's own
        /// browser-level zoom, so it scales every stylesheet class proportionally including headings.
        /// </para>
        ///
        /// <para>
        /// It lives here because zoom is per script for the same reason the face is: #574 flattened the
        /// stylesheets to one shared size ladder, so zoom became the only per-script size control, and it is
        /// calibration for whatever face this script resolves to. Switching a book's script therefore has to
        /// switch face and zoom together — keeping them in one record is what makes that a single lookup.
        /// </para>
        ///
        /// <para>
        /// Persisted through <c>SettingsService</c>, whose serializer does not set
        /// <c>WhenWritingDefault</c> — so unlike <c>ApplicationState</c>, a 1.0 here survives a round-trip
        /// rather than being silently omitted. Values are clamped on read regardless; see
        /// <c>BookZoomService</c>.
        /// </para>
        /// </summary>
        public double BookZoom { get; set; } = 1.0;
    }
}