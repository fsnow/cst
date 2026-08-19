using System;
using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Models.Ai;

namespace CST.Avalonia.Services.Ai
{
    /// <summary>
    /// The named endpoints a reader can add without knowing a URL. (#689, #691)
    ///
    /// <para><b>Derived data, not curation.</b> Every entry is a fact about a third-party API — base URL, wire
    /// protocol, whether a key is required, and the environment variables that conventionally carry one. The
    /// facts come from <c>docs/research/PROVIDER_ENV_VARS_AND_ENDPOINTS.md</c>, mined from
    /// <a href="https://github.com/anomalyco/opencode">opencode</a> (MIT) at commit <c>e14acea</c>, 2026-08-18.
    /// Refreshing this table means re-running that extraction against a newer commit — the plan is to track
    /// their work and fold in changes, since provider support is a moving target nobody keeps up with unaided.</para>
    ///
    /// <para><b>What must never appear here.</b> Anything that ranks, scores, tiers or recommends: no
    /// "recommended", no model lists, no ordering by quality, no notes on which endpoint is better at Pāli.
    /// That is the registry removed in #670/#681, and the rule binds a mined table exactly as it binds a
    /// hand-written one — including on every future sync, because a ranking field can appear in an upstream
    /// release nobody read. The order below is alphabetical by display name, which is mechanical.</para>
    ///
    /// <para><b>Presets are a convenience, never a gate.</b> A custom endpoint typed by hand is first-class and
    /// always available; roughly 150 of 189 catalogued providers are plain
    /// <c>POST {base}/chat/completions</c> with a bearer token, which is exactly what "custom" already is.</para>
    /// </summary>
    public static class AiProviderPresets
    {
        /// <summary>The opencode commit these facts were extracted from. Stamped so "is this current?" is
        /// answerable and a refresh is a diff rather than an audit.</summary>
        public const string SourceCommit = "e14acea";

        /// <summary>
        /// Endpoints that run on this machine. <b>Always present, catalogue or not</b> — models.dev
        /// catalogues hosted providers and will never list Ollama; LM Studio appears with three models,
        /// a number about nothing, since what either serves is whatever the reader has loaded.
        ///
        /// <para>Structurally the simplest presets possible: a fixed loopback URL, no credential method,
        /// no prompts, nothing to template — which is why hardcoding them cannot rot, and why they are
        /// exactly the wrong thing to hide when the network is down.</para>
        /// </summary>
        private static readonly AiProviderPreset[] Local =
        {
            P("lmstudio", "LM Studio (local)", ChatProviderKind.OpenAiCompatible,
                "http://localhost:1234/v1", false),

            P("ollama", "Ollama (local)", ChatProviderKind.OpenAiCompatible,
                "http://localhost:11434/v1", false),
        };

        /// <summary>
        /// Hosted endpoints whose base URL the catalogue does not record, or whose auth shape it does not
        /// describe. (#737)
        ///
        /// <para><b>Why this table exists at all.</b> models.dev records an <c>api</c> URL exactly when a
        /// provider is served by the generic OpenAI-compatible adapter, and omits it when a dedicated SDK
        /// package carries its own default. 26 of its 192 providers are in that state — including OpenAI and
        /// Anthropic — so "no <c>api</c> field" means "packaged differently", never "unsupported".</para>
        ///
        /// <para><b>The wire format is a column here, not an assumption.</b> Each entry carries an explicit
        /// <see cref="ChatProviderKind"/>, and that enum has two members. So a provider speaking a third
        /// protocol cannot be added even by accident: Cohere posts to <c>/chat</c> on
        /// <c>api.cohere.com/v2</c> and would need a value that does not exist. Recording a base URL for it
        /// would produce a preset that looks configured and 404s on every request.</para>
        ///
        /// <para><b>Deliberately absent, with reasons</b> — each is a skip the generator logs rather than a
        /// silent omission:</para>
        /// <list type="bullet">
        /// <item><c>cohere</c> — a third wire protocol, not a missing URL.</item>
        /// <item><c>perplexity</c> — <c>https://api.perplexity.ai</c> has no version segment, and
        /// <c>AiHttp.ResolveEndpoint</c> adds one to a bare host, so it would build
        /// <c>/v1/chat/completions</c> against an endpoint serving <c>/chat/completions</c>. Tracked as
        /// <b>#742</b>; add it here once that lands.</item>
        /// <item><c>google</c>, <c>amazon-bedrock</c>, <c>google-vertex</c> — protocol or credential work
        /// (#700, #702, #703).</item>
        /// <item><c>cloudflare-ai-gateway</c> — two credentials on one request (#701).</item>
        /// </list>
        /// </summary>
        private static readonly AiProviderPreset[] Hosted =
        {
            // Anthropic's own protocol. Kind and BaseUrl stay independent: other providers serve the
            // Anthropic Messages shape at their own URLs, so Kind must never imply this host.
            P("anthropic", "Anthropic", ChatProviderKind.Anthropic,
                "https://api.anthropic.com/v1", true, "ANTHROPIC_API_KEY"),

            // Azure needs the reader's resource name in the URL, and sends the credential in an `api-key`
            // header INSTEAD of Authorization - the one provider so far where adding a header is not enough.
            new AiProviderPreset(
                "azure", "Azure OpenAI", ChatProviderKind.OpenAiCompatible,
                "https://{resourceName}.openai.azure.com/openai/v1",
                new AiCredentialMethod[]
                {
                    new AiCredentialMethod.Key(),
                    new AiCredentialMethod.Env(new[] { "AZURE_API_KEY", "AZURE_OPENAI_API_KEY" }),
                },
                new[] { new AiInputPrompt("resourceName", "Azure resource name", "my-resource") },
                AuthHeaderName: "api-key",
                AuthScheme: null),

            // The five below carry no `api` field but are ordinary bearer endpoints we already ship and
            // whose URLs are proven in use. opencode's own openai-compatible-profile.ts agrees on all five,
            // which is a cross-check rather than the source.
            P("cerebras", "Cerebras", ChatProviderKind.OpenAiCompatible,
                "https://api.cerebras.ai/v1", true, "CEREBRAS_API_KEY"),

            P("deepinfra", "DeepInfra", ChatProviderKind.OpenAiCompatible,
                "https://api.deepinfra.com/v1/openai", true, "DEEPINFRA_API_KEY"),

            P("groq", "Groq", ChatProviderKind.OpenAiCompatible,
                "https://api.groq.com/openai/v1", true, "GROQ_API_KEY"),

            P("togetherai", "Together AI", ChatProviderKind.OpenAiCompatible,
                "https://api.together.xyz/v1", true, "TOGETHER_API_KEY"),

            P("xai", "xAI", ChatProviderKind.OpenAiCompatible,
                "https://api.x.ai/v1", true, "XAI_API_KEY"),

            P("openai", "OpenAI", ChatProviderKind.OpenAiCompatible,
                "https://api.openai.com/v1", true, "OPENAI_API_KEY"),

            // Account id goes in the path, and the catalogue cannot express a prompt for it - which is
            // precisely what this table is for. Ordinary bearer auth otherwise.
            new AiProviderPreset(
                "cloudflare-workers-ai", "Cloudflare Workers AI", ChatProviderKind.OpenAiCompatible,
                "https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/v1",
                new AiCredentialMethod[]
                {
                    new AiCredentialMethod.Key(),
                    new AiCredentialMethod.Env(new[] { "CLOUDFLARE_API_KEY", "CLOUDFLARE_WORKERS_AI_TOKEN" }),
                },
                new[] { new AiInputPrompt("accountId", "Cloudflare account ID", "0123456789abcdef") }),

            // Read out of @ai-sdk/mistral rather than assumed: base https://api.mistral.ai/v1, posts to
            // /chat/completions.
            P("mistral", "Mistral", ChatProviderKind.OpenAiCompatible,
                "https://api.mistral.ai/v1", true, "MISTRAL_API_KEY"),
        };

        /// <summary>Local runners only — what remains offerable when the hosted catalogue is unavailable.</summary>
        public static IReadOnlyList<AiProviderPreset> LocalOnly => Local;

        /// <summary>Everything not derived from the catalogue. Wins over a catalogue entry of the same id:
        /// these carry URLs it does not record and auth shapes it does not describe.</summary>
        public static IReadOnlyList<AiProviderPreset> HandKept => Local.Concat(Hosted).ToList();

        /// <summary>
        /// Every preset derivable without the catalogue service — the hand-kept table plus whatever the
        /// build-time snapshot supplies. Callers holding an <c>IAiPresetSource</c> should use that instead;
        /// this is the answer for code with no access to one.
        /// </summary>
        public static IReadOnlyList<AiProviderPreset> All => AiPresetSource.SnapshotDefaults;

        /// <summary>The preset with this id, or null. Ids are reserved: a custom connection may not take one.</summary>
        /// <summary>The preset with this id, or null. Looks at the whole derivable set, not only the
        /// hand-kept part — an id like <c>openrouter</c> comes from the catalogue, and treating it as unknown
        /// would let a custom connection claim it and would lose the key-required flag.</summary>
        public static AiProviderPreset? ById(string id) =>
            All.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

        /// <summary>True when <paramref name="id"/> names a preset, so a custom connection cannot claim it.</summary>
        public static bool IsReservedId(string id) => ById(id) is not null;

        /// <summary>The common case: a base URL, a bearer token, and the env vars that may already hold it.
        /// Roughly 150 of 189 catalogued providers are exactly this.</summary>
        private static AiProviderPreset P(
            string id, string displayName, ChatProviderKind kind, string baseUrl, bool requiresKey,
            params string[] envVars)
        {
            var methods = new List<AiCredentialMethod>();
            if (requiresKey)
            {
                methods.Add(new AiCredentialMethod.Key());
                if (envVars.Length > 0) methods.Add(new AiCredentialMethod.Env(envVars));
            }
            return new AiProviderPreset(id, displayName, kind, baseUrl, methods);
        }
    }
}
