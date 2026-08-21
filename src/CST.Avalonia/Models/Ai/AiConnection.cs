using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Services.Ai;

namespace CST.Avalonia.Models.Ai
{
    /// <summary>Where a connection's API key came from. (#689)</summary>
    public enum CredentialSource
    {
        /// <summary>No key stored. Legitimate: a local runner usually needs none, and a connection may
        /// authenticate entirely through <see cref="AiConnection.Headers"/> instead.</summary>
        None,

        /// <summary>Stored by us in the OS credential store — Keychain on macOS, DPAPI on Windows (#579).</summary>
        Keychain,

        /// <summary>
        /// Picked up from an environment variable the app did not set.
        ///
        /// <para><b>This is why the enum exists rather than a bool.</b> The app cannot delete a credential it
        /// never stored, so a row sourced this way must offer no remove action — an empty slot, not a button
        /// that would lie. OpenCode auto-connects from the environment and offers no way out at all, which
        /// surprised the maintainer on his own machine with a key he had forgotten was set.</para>
        /// </summary>
        Environment,
    }

    /// <summary>
    /// Whether a connection has been shown to work. (#689, #673)
    ///
    /// <para><b>Three states, not two.</b> "Configured" and "reachable" are different facts, and conflating
    /// them is what lets a settings page claim "Connected" while the assistant reports it cannot connect —
    /// observed in OpenCode, where the two surfaces contradict each other and the one a reader consults to
    /// diagnose is the one that is wrong.</para>
    /// </summary>
    public enum Reachability
    {
        /// <summary>Configured, never contacted. The honest default — <b>never render this as
        /// "Connected"</b>.</summary>
        Configured,

        /// <summary>Contacted successfully at some point.</summary>
        Reachable,

        /// <summary>A request to it failed to connect. Set by a use-time failure writing back, so settings and
        /// the assistant read one shared fact rather than each guessing.</summary>
        Unreachable,
    }

    /// <summary>
    /// One model a connection offers, and whether the reader wants it on their short list.
    /// </summary>
    /// <param name="Enabled">Whether it appears in the per-turn picker (#693). <b>Default all-on</b> for a new
    /// connection: all-on is neutral ("here is what this offers"), all-off is neutral but unusable, and a
    /// pre-selected <i>subset</i> is a verdict — which is the model registry deleted in #670/#681 wearing a
    /// toggle.</param>
    /// <param name="ContextLength">What the provider published when this model was added, or null. Kept on
    /// the record because the listing is only fetched while the Models tab is open, and the per-turn picker
    /// (#693) has to be able to say it without asking again.</param>
    /// <param name="Missing">
    /// Set when the provider's own listing, fetched successfully, did not carry this model. (#728)
    ///
    /// <para><b>Only ever written from a listing we can trust</b>: a fetch that failed marks nothing, an empty
    /// listing marks nothing, and an endpoint that publishes no listing at all can never mark anything. The
    /// alternative — treating absence of evidence as evidence — turns one offline moment into a screen of
    /// false alarms, which is the same reasoning that makes <see cref="Reachability.Configured"/> a third
    /// state rather than a synonym for unreachable.</para>
    ///
    /// <para><b>A mark, never a removal.</b> The reader chose this model; a provider's listing is not
    /// authority over their configuration, and an endpoint that publishes an incomplete one would otherwise
    /// silently delete valid entries. It stays enabled, stays pickable, and says what it is.</para>
    /// </param>
    public sealed record AiModelEntry(
        string Id,
        string DisplayName,
        bool Enabled = true,
        int? ContextLength = null,
        bool? SupportsReasoning = null,
        string? Inputs = null,
        bool Missing = false);

    /// <summary>
    /// One extra request header on a connection. (#711, #771)
    /// </summary>
    /// <param name="Value">Null when <paramref name="Secret"/>: the value is in the credential store, and this
    /// record is handed to the UI. Keeping it null here is what stops a secret reaching a screen, a log or a
    /// settings file by simply being carried along — the request path fetches it at the moment it sends.</param>
    /// <param name="Secret">Whether the value is a credential rather than a routing hint.</param>
    public sealed record AiHeader
    {
        /// <summary>
        /// A secret header carries a name and a mark, never a value — normalised here rather than trusted from
        /// each caller.
        ///
        /// <para>Two places already drop a secret's value on the way past: the sheet building this record and
        /// the service writing the settings record. A mutation showed the first can be deleted with every test
        /// still green, because the second catches it — two guards each believing the other is the belt, which
        /// is how a leak eventually gets through.</para>
        ///
        /// <para><b>Deliberately not a positional record.</b> As one, the generated <c>init</c> accessors made
        /// <c>header with { Secret = true }</c> produce a secret carrying a value: the copy constructor copies
        /// the backing fields and re-runs the initialiser only for members the <c>with</c> names. So the
        /// invariant held at construction and nowhere else, while the comment claimed otherwise. Get-only
        /// properties make <c>with</c> refuse to compile against any of them. (#771, fable review)</para>
        /// </summary>
        public AiHeader(string Name, string? Value, bool Secret = false)
        {
            this.Name = Name;
            this.Secret = Secret;
            this.Value = Secret ? null : Value;
        }

        public string Name { get; }

        /// <summary>Null whenever <see cref="Secret"/> — the value is in the credential store, and the request
        /// path fetches it at the moment it sends.</summary>
        public string? Value { get; }

        public bool Secret { get; }
    }

    /// <summary>
    /// One configured endpoint: where to send a request, how to authenticate, and which models it offers.
    /// Replaces the single scalar provider/base-URL/model/key that surface B shipped with. (#689)
    /// </summary>
    /// <param name="Id">Stable slug, <b>immutable</b>, and the account name the credential is stored under.
    /// Deliberately user-supplied rather than derived from <paramref name="BaseUrl"/>: a URL-derived key
    /// orphans the credential the moment someone changes a port or swaps <c>localhost</c> for
    /// <c>127.0.0.1</c>, and the resulting failure presents as a bad key rather than a lost one.</param>
    /// <param name="Kind">The wire protocol. <b>Independent of <paramref name="BaseUrl"/></b> — several
    /// providers speak the Anthropic Messages protocol at their own URLs, so this must never be inferred from
    /// the host.</param>
    /// <param name="Headers">Extra request headers. The escape hatch that makes an absent key coherent:
    /// Azure's <c>api-key</c>, gateway tokens, and anything non-bearer live here.</param>
    /// <param name="BaseUrl">May be a template — see <see cref="AiTemplate"/>. Azure and Cloudflare do not
    /// have a fixed address so much as a shape with the reader's own resource name or account id in it.</param>
    /// <param name="Inputs">The reader's answers to the preset's prompts (resource name, account id, region,
    /// project). Substituted into <paramref name="BaseUrl"/> and <paramref name="Headers"/>. Empty for the
    /// ~150 of 189 providers that are a plain base URL and a bearer token.</param>
    public sealed record AiConnection(
        string Id,
        string DisplayName,
        ChatProviderKind Kind,
        string BaseUrl,
        IReadOnlyList<AiModelEntry> Models,
        IReadOnlyList<AiHeader> Headers,
        IReadOnlyDictionary<string, string> Inputs,
        CredentialSource KeySource = CredentialSource.None,
        Reachability State = Reachability.Configured,
        string AuthHeaderName = "Authorization",
        string? AuthScheme = "Bearer")
    {
        /// <summary>The base URL with <see cref="Inputs"/> substituted in — what a request actually goes to.</summary>
        public string ResolvedBaseUrl => AiTemplate.Expand(BaseUrl, Inputs);

        /// <summary>True when an input the URL needs has not been supplied, so this connection cannot be used
        /// yet. Checked before sending rather than discovered as a DNS failure naming nothing.</summary>
        public bool IsIncomplete => AiTemplate.HasUnresolvedPlaceholders(ResolvedBaseUrl);
    }

    /// <summary>
    /// The editable fields of a connection — everything except <see cref="AiConnection.Id"/>, which cannot
    /// change once the credential is filed under it, and the derived state.
    /// </summary>
    public sealed record AiConnectionDraft(
        string DisplayName,
        ChatProviderKind Kind,
        string BaseUrl,
        IReadOnlyList<AiModelEntry> Models,
        IReadOnlyList<AiHeader> Headers,
        IReadOnlyDictionary<string, string> Inputs,
        string AuthHeaderName = "Authorization",
        string? AuthScheme = "Bearer");

    /// <summary>
    /// A named endpoint a reader can add without knowing its URL. (#689, #691)
    ///
    /// <para><b>A preset carries a base URL and a key-required flag and nothing else</b> — no model list, no
    /// ranking, no quality claim. It reintroduces nothing from #670/#681, which concerned <i>models</i> rather
    /// than endpoints. Its entire value is that a reader should not have to already know
    /// <c>https://openrouter.ai/api/v1</c> in order to use OpenRouter.</para>
    /// </summary>
    /// <param name="BaseUrl">May contain <c>{key}</c> placeholders naming <paramref name="Prompts"/>.</param>
    /// <param name="Methods">How a credential may be obtained. An empty list means none is needed — a local
    /// runner. See <see cref="AiCredentialMethod"/> for why this is a union rather than a bool plus a list of
    /// variable names.</param>
    /// <param name="Prompts">Extra values this provider needs beyond a key, and what to ask for them.
    /// Substituted into <paramref name="BaseUrl"/> and <paramref name="Headers"/>.</param>
    /// <param name="Headers">Static or templated headers, NOT the credential. Values may contain <c>{key}</c>.</param>
    /// <param name="AuthHeaderName">Which header carries the credential. Azure uses <c>api-key</c>, and
    /// crucially expects <c>Authorization</c> to be <i>absent</i> rather than also present — so this replaces
    /// the auth header rather than adding one.</param>
    /// <param name="AuthScheme">Prefix before the credential, or null for a bare value. Bearer for almost
    /// everything; null for Azure's <c>api-key</c>.</param>
    /// <param name="Doc">
    /// The provider's own documentation link, as the catalogue publishes it.
    ///
    /// <para><b>In practice a models page</b>, not an account or key page — sampled across the catalogue, nine
    /// in ten point at a list of model ids. So its use is for a reader with a working connection who needs to
    /// know what to run on it, not for one who cannot find their key: anyone pasting a key has already been to
    /// the provider. Null only where the catalogue has nothing to give: the local runners, which it does not
    /// carry at all, and a custom endpoint, which has no provider identity. <b>A hand-kept preset still gets
    /// one</b> — those are hand-kept because the catalogue omits their <c>api</c> URL, not because they are
    /// absent from it, and reading that the other way round cost the ten most prominent providers their
    /// link.</para>
    /// </param>
    public sealed record AiProviderPreset(
        string Id,
        string DisplayName,
        ChatProviderKind Kind,
        string BaseUrl,
        IReadOnlyList<AiCredentialMethod> Methods,
        IReadOnlyList<AiInputPrompt>? Prompts = null,
        IReadOnlyDictionary<string, string>? Headers = null,
        string AuthHeaderName = "Authorization",
        string? AuthScheme = "Bearer",
        string? Doc = null)
    {
        /// <summary>True when a key must be supplied or found. False for a local runner.</summary>
        public bool RequiresKey => Methods.Any(m => m is AiCredentialMethod.Key or AiCredentialMethod.Env);

        /// <summary>Environment variables that may already hold this provider's key, in precedence order.
        /// Used to DETECT a credential, never to adopt one — discovery makes a provider available, not
        /// connected.</summary>
        public IReadOnlyList<string> EnvironmentVariables =>
            Methods.OfType<AiCredentialMethod.Env>().SelectMany(m => m.Names).ToList();
    }
}
