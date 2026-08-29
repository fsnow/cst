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

        /// <summary>
        /// Top-level properties this build does not know about, carried through a round-trip. (#883)
        ///
        /// <para><b>What it prevents:</b> a newer build adds a setting, the reader launches an older build once,
        /// and the older build's next save rewrites the file without it. Silently — an unknown property is not an
        /// error to System.Text.Json, it is simply dropped. Frank runs several builds across machines, so a
        /// downgrade is a routine event rather than an accident.</para>
        ///
        /// <para><b>What it does not:</b> extension data is per-object, and this is the root only. A property a
        /// newer build adds INSIDE a nested section still sheds. Covering every persisted type would mean this
        /// member on each of them, and the root is where new sections actually land — so this is the whole of the
        /// cheap half, deliberately, not an oversight.</para>
        ///
        /// <para>Never written by this build (nothing sets it), so an ordinary file gains nothing. Null when
        /// empty, and <c>WhenWritingDefault</c> keeps it out of the output.</para>
        /// </summary>
        [System.Text.Json.Serialization.JsonExtensionData]
        public System.Collections.Generic.Dictionary<string, System.Text.Json.JsonElement>? UnknownProperties { get; set; }

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

        /// <summary>
        /// Whether to read API keys from the login shell's environment. Default ON, and non-Windows only in
        /// effect. (#820)
        ///
        /// <para><b>On rather than off, deliberately.</b> The reader this serves does not know their key is
        /// invisible — that is the bug (#817) — so a default of off would require them to find a checkbox in
        /// order to discover a problem they cannot see, which rebuilds the bug with an extra step.</para>
        ///
        /// <para>It governs the LOGIN SHELL lookup only. This process's own environment is read regardless,
        /// so a reader who used <c>launchctl setenv</c>, or launched from a terminal, still has their keys
        /// found with this off.</para>
        /// </summary>
        public bool ReadLoginShellEnvironment { get; set; } = true;

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

        /// <summary>
        /// The reasoning effort the reader has chosen, in the provider's own vocabulary, or null for "let the
        /// provider apply its default". (#671)
        ///
        /// <para>One setting rather than one per model, because it is a preference about how the reader wants
        /// to trade cost against depth, not a property of any particular model. It is <b>validated against the
        /// active model's published list at the moment a request is built</b> — so a value left over from a
        /// model that accepted it is simply not sent to one that did not publish it, rather than becoming a
        /// 400 the reader cannot attribute.</para>
        /// </summary>
        public string? ReasoningEffort { get; set; }
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

        /// <summary>
        /// The provider-list entry this connection was added from, or null for a custom endpoint. (#766)
        ///
        /// <para><b>Recorded at the moment it is known, because it cannot be recovered later.</b> It used to
        /// be inferred by matching the id against the preset list, on the reasoning that a custom connection
        /// is refused any preset's id — true when the connection is created, and only against the presets
        /// known then. Since #733 that list is generated from a models.dev catalogue that grows, so a custom
        /// endpoint whose slug later appears in it was silently reclassified: its sheet narrowed to the key
        /// box, hiding its own address and models, and told the reader the address "comes from the provider
        /// list", which was false.</para>
        ///
        /// <para><b>Three states, and the difference between two of them is the whole point.</b></para>
        /// <list type="bullet">
        /// <item>a preset id — added from the provider list, and this is which entry;</item>
        /// <item><b>empty</b> — recorded as a custom endpoint. Known, not merely unstated;</item>
        /// <item><b>null</b> — nothing was recorded, because the file predates this field.</item>
        /// </list>
        ///
        /// <para>Collapsing the last two is what the first cut of this did, and it did not fix the bug: a
        /// custom connection created after the change looked exactly like one from an older file, so it still
        /// fell through to the id match and was still reclassified when the catalogue grew into its slug. An
        /// absence has to be recorded as an absence to be worth anything — the same shape as #728's marking
        /// rules and <c>Reachability</c>'s third state.</para>
        ///
        /// <para>Only null falls back to matching the id, which is the right answer for every connection a
        /// pre-#766 file can hold.</para>
        /// </summary>
        public string? PresetId { get; set; }

        /// <summary>
        /// The reader opted in to the API key their environment already holds. (#714)
        ///
        /// <para><b>This flag is the opt-in itself.</b> Discovery is automatic and use is not: finding
        /// <c>OPENAI_API_KEY</c> set makes a provider AVAILABLE, and only a reader's click makes it
        /// authenticate. Recording that click is what separates this from OpenCode, which adopts an
        /// environment key silently — producing a connected provider the maintainer had not configured, from
        /// a variable he had forgotten was set on his own machine, with no way to disconnect it.</para>
        ///
        /// <para>The key itself is NEVER stored. It is read from the environment at the moment of use, so a
        /// variable the reader changes or unsets takes effect immediately; a copy in the keychain would go on
        /// authenticating with a credential they believe they have revoked, and a row sourced from the
        /// environment offers no remove action to undo it with (#691).</para>
        ///
        /// <para>A connection with this set whose variable is no longer present reads as "no key", not as an
        /// error — the reader unset it, which is a thing they are allowed to do.</para>
        /// </summary>
        public bool UsesEnvironmentKey { get; set; }

        /// <summary>
        /// The environment variable the reader adopted, recorded at the moment they adopted it. (#714)
        ///
        /// <para>Pinned rather than re-derived. The preset's variable list comes from models.dev and is
        /// refreshed: a reordering, or an alias added upstream that the reader happens to have set for
        /// something else, would change which credential goes to this endpoint with no second consent — the
        /// exact silent adoption this feature exists to prevent, arriving later instead of at the start.</para>
        /// </summary>
        public string? EnvironmentVariable { get; set; }

        public string DisplayName { get; set; } = "";

        /// <summary><c>anthropic</c> or <c>openai-compatible</c>. A string rather than the enum so a rename
        /// on our side cannot silently invalidate a reader's settings file.</summary>
        public string Kind { get; set; } = "openai-compatible";

        /// <summary>May contain <c>{key}</c> placeholders filled from <see cref="Inputs"/> — Azure and
        /// Cloudflare have a URL shape rather than a URL.</summary>
        public string BaseUrl { get; set; } = "";

        public List<AiModelRecord> Models { get; set; } = new();

        /// <summary>
        /// Extra request headers. A value that IS a credential is marked secret and kept in the OS credential
        /// store rather than here. (#771)
        /// </summary>
        [System.Text.Json.Serialization.JsonConverter(typeof(AiHeaderRecordListConverter))]
        public List<AiHeaderRecord> Headers { get; set; } = new();

        /// <summary>Answers to the preset's prompts — resource name, account id, region — substituted into
        /// <see cref="BaseUrl"/> and <see cref="Headers"/>.</summary>
        public Dictionary<string, string> Inputs { get; set; } = new();

        /// <summary>
        /// Which prompt answers are credentials, and so live in the OS credential store rather than here.
        /// (#777)
        ///
        /// <para>The key is recorded and the value is not, exactly as a secret header records its name and not
        /// its value (#771). A key listed here is absent from <see cref="Inputs"/>; it is fetched under
        /// <c>AiCredentialNames.Input(key)</c> when a header template needs it.</para>
        ///
        /// <para><b>Nullable rather than <c>= new()</c>.</b> A property initializer is overwritten by an
        /// explicit <c>null</c> in the JSON, so the initializer is not the guarantee it looks like — and
        /// every read here treats absent and empty alike, which is the honest reading for a file written
        /// before this field existed.</para>
        /// </summary>
        public List<string>? SecretInputs { get; set; }

        /// <summary>
        /// Which header carries the credential. Azure uses <c>api-key</c> and expects <c>Authorization</c> to
        /// be ABSENT rather than also present, so this REPLACES the auth header rather than adding one.
        /// </summary>
        public string AuthHeaderName { get; set; } = "Authorization";

        /// <summary>Prefix before the credential, or null for a bare value. Bearer for almost everything;
        /// null for Azure.</summary>
        public string? AuthScheme { get; set; } = "Bearer";
    }

    /// <summary>
    /// One extra request header, as persisted. (#711, #771)
    ///
    /// <para><b>Why a record and not a <c>Dictionary&lt;string, string&gt;</c>.</b> A secret header's value
    /// does not live here, so the shape has to be able to say "this header has a name and no value in this
    /// file". Expressed as a dictionary plus a parallel list of which names are secret, the two can disagree —
    /// a name in both would have a plaintext value AND a stored secret, with nothing to say which wins. Here
    /// that state cannot be written down.</para>
    /// </summary>
    public class AiHeaderRecord
    {
        public string Name { get; set; } = "";

        /// <summary>
        /// The header value, or null when <see cref="Secret"/> — in which case it is in the credential store
        /// under <c>AiCredentialNames.Header(Name)</c> and is never written to this file at any point.
        /// </summary>
        public string? Value { get; set; }

        /// <summary>
        /// Whether the value is a credential. Set by the reader on the row, because only they know: the same
        /// header name is a routing hint at one provider and a token at another.
        /// </summary>
        public bool Secret { get; set; }
    }

    /// <summary>One model a connection offers, as persisted.</summary>
    public class AiModelRecord
    {
        public string Id { get; set; } = "";
        public string DisplayName { get; set; } = "";

        /// <summary>Whether it appears in the per-turn picker. Defaults true: all-on is neutral, whereas a
        /// pre-selected subset would be a quality verdict (#670/#681).</summary>
        public bool Enabled { get; set; } = true;

        /// <summary>
        /// What the provider published about this model when it was added, kept so the assistant can say it
        /// without asking again. (#693)
        ///
        /// <para>The listing is fetched only on the Models tab and only while that window is open, so
        /// anything the per-turn picker wants to show has to have been written down at the moment the reader
        /// promoted the model. Null throughout for a hand-typed id and for every endpoint that publishes no
        /// listing — which the UI must render as silence rather than as zero.</para>
        /// </summary>
        public int? ContextLength { get; set; }

        /// <summary>Null when the provider published no parameter list — not the same as publishing one
        /// without reasoning in it.</summary>
        public bool? SupportsReasoning { get; set; }

        /// <summary>What the model accepts, as the provider words it — "text", "text, image". Null when it
        /// said nothing.</summary>
        public string? Inputs { get; set; }

        /// <summary>
        /// The reasoning-effort values this model published, in the provider's own words and order. (#671)
        ///
        /// <para>Stored for the same reason <see cref="ContextLength"/> is: the listing is fetched only on the
        /// Models tab and only while that window is open, so anything the per-turn picker needs has to have
        /// been written down when the reader promoted the model.</para>
        ///
        /// <para><b>Null and empty are different.</b> Null is a provider that said nothing — every local
        /// runner, and every hosted provider whose listing carries only ids. Empty is a provider that
        /// published a reasoning capability with no effort levels in it. Neither gets a control: there is
        /// nothing to offer, and inventing low/medium/high would be this app deciding what the levels are.</para>
        /// </summary>
        public List<string>? ReasoningEfforts { get; set; }

        /// <summary>The value the provider says it applies when none is sent, or null where it does not say.
        /// Shown as a label on the "Provider default" position — its word, not a choice of ours. (#671)</summary>
        public string? DefaultReasoningEffort { get; set; }

        /// <summary>
        /// Whether the provider's listing no longer carries this model. (#728)
        ///
        /// <para>Written only from a successful, non-empty fetch, and cleared by the next one that carries it
        /// again. False for every model on an endpoint that publishes no listing — silence is not a
        /// removal.</para>
        /// </summary>
        public bool Missing { get; set; }
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
            ScriptFonts = DefaultScriptFonts();
        }

        /// <summary>
        /// The canonical per-script font defaults — every display script, with the sizes chosen for each.
        /// Empty font family means use the system default for that script.
        ///
        /// <para>A method rather than a constructor body because <see cref="SettingsValidator"/> re-seeds
        /// missing keys from it. A second hand-written list there would drift from this one, and drift in
        /// exactly this dictionary is what #881 is: the Appearance panel builds its rows by enumerating it,
        /// so a key that is absent is a script with no font control at all. (#881)</para>
        /// </summary>
        public static Dictionary<string, ScriptFontSetting> DefaultScriptFonts() =>
            new()
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