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

        private static readonly AiProviderPreset[] Items =
        {
            // Anthropic speaks its own protocol. Kind and BaseUrl stay independent: other providers serve the
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
                new[]
                {
                    new AiInputPrompt("resourceName", "Azure resource name", "my-resource"),
                },
                AuthHeaderName: "api-key",
                AuthScheme: null),

            P("baseten", "Baseten", ChatProviderKind.OpenAiCompatible,
                "https://inference.baseten.co/v1", true, "BASETEN_API_KEY"),

            P("cerebras", "Cerebras", ChatProviderKind.OpenAiCompatible,
                "https://api.cerebras.ai/v1", true, "CEREBRAS_API_KEY"),

            P("chutes", "Chutes", ChatProviderKind.OpenAiCompatible,
                "https://llm.chutes.ai/v1", true, "CHUTES_API_KEY"),

            // Account id goes in the path; ordinary bearer auth otherwise.
            new AiProviderPreset(
                "cloudflare-workers-ai", "Cloudflare Workers AI", ChatProviderKind.OpenAiCompatible,
                "https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/v1",
                new AiCredentialMethod[]
                {
                    new AiCredentialMethod.Key(),
                    new AiCredentialMethod.Env(new[] { "CLOUDFLARE_API_KEY", "CLOUDFLARE_WORKERS_AI_TOKEN" }),
                },
                new[]
                {
                    new AiInputPrompt("accountId", "Cloudflare account ID", "0123456789abcdef"),
                }),

            P("deepinfra", "DeepInfra", ChatProviderKind.OpenAiCompatible,
                "https://api.deepinfra.com/v1/openai", true, "DEEPINFRA_API_KEY"),

            P("deepseek", "DeepSeek", ChatProviderKind.OpenAiCompatible,
                "https://api.deepseek.com/v1", true, "DEEPSEEK_API_KEY"),

            P("fireworks-ai", "Fireworks AI", ChatProviderKind.OpenAiCompatible,
                "https://api.fireworks.ai/inference/v1", true, "FIREWORKS_API_KEY"),

            P("groq", "Groq", ChatProviderKind.OpenAiCompatible,
                "https://api.groq.com/openai/v1", true, "GROQ_API_KEY"),

            P("huggingface", "Hugging Face", ChatProviderKind.OpenAiCompatible,
                "https://router.huggingface.co/v1", true, "HF_TOKEN"),

            // Local runners. No key by default - which is why an absent credential must be a valid state
            // rather than an error, and why CredentialSource has a None member.
            P("lmstudio", "LM Studio (local)", ChatProviderKind.OpenAiCompatible,
                "http://localhost:1234/v1", false),

            P("moonshotai", "Moonshot AI", ChatProviderKind.OpenAiCompatible,
                "https://api.moonshot.ai/v1", true, "MOONSHOT_API_KEY"),

            P("nebius", "Nebius Token Factory", ChatProviderKind.OpenAiCompatible,
                "https://api.tokenfactory.nebius.com/v1", true, "NEBIUS_API_KEY"),

            P("novita-ai", "Novita AI", ChatProviderKind.OpenAiCompatible,
                "https://api.novita.ai/openai", true, "NOVITA_API_KEY"),

            P("nvidia", "Nvidia", ChatProviderKind.OpenAiCompatible,
                "https://integrate.api.nvidia.com/v1", true, "NVIDIA_API_KEY"),

            P("ollama", "Ollama (local)", ChatProviderKind.OpenAiCompatible,
                "http://localhost:11434/v1", false),

            P("openai", "OpenAI", ChatProviderKind.OpenAiCompatible,
                "https://api.openai.com/v1", true, "OPENAI_API_KEY"),

            P("openrouter", "OpenRouter", ChatProviderKind.OpenAiCompatible,
                "https://openrouter.ai/api/v1", true, "OPENROUTER_API_KEY"),

            P("requesty", "Requesty", ChatProviderKind.OpenAiCompatible,
                "https://router.requesty.ai/v1", true, "REQUESTY_API_KEY"),

            P("siliconflow", "SiliconFlow", ChatProviderKind.OpenAiCompatible,
                "https://api.siliconflow.com/v1", true, "SILICONFLOW_API_KEY"),

            P("togetherai", "Together AI", ChatProviderKind.OpenAiCompatible,
                "https://api.together.xyz/v1", true, "TOGETHER_API_KEY"),

            P("xai", "xAI", ChatProviderKind.OpenAiCompatible,
                "https://api.x.ai/v1", true, "XAI_API_KEY"),

            P("zai", "Z.ai", ChatProviderKind.OpenAiCompatible,
                "https://api.z.ai/api/paas/v4", true, "ZHIPU_API_KEY"),

            P("zhipuai", "Zhipu AI", ChatProviderKind.OpenAiCompatible,
                "https://open.bigmodel.cn/api/paas/v4", true, "ZHIPU_API_KEY"),
        };

        /// <summary>
        /// Every preset, ordered alphabetically by display name.
        ///
        /// <para><b>Deliberately absent, and why</b> — these need fields the connection record does not yet
        /// model, and guessing at them would ship endpoints that cannot work:</para>
        /// <list type="bullet">
        /// <item>Azure OpenAI — needs a resource name, and uses an <c>api-key</c> header that requires
        /// <i>removing</i> <c>authorization</c> rather than adding to it.</item>
        /// <item>Amazon Bedrock — SigV4 request signing, or ambient AWS credentials with no environment
        /// variable set at all.</item>
        /// <item>Google Vertex — project plus location, authenticated by ADC rather than a key.</item>
        /// <item>Cloudflare AI Gateway — an account id and <b>two</b> tokens on a single request.</item>
        /// <item>Google Gemini — its native protocol is neither of our two kinds (<c>x-goog-api-key</c>, and
        /// the model id embedded in the path). It does publish an OpenAI-compatible endpoint, but that was not
        /// part of the extraction, so it is left out rather than asserted from memory.</item>
        /// </list>
        /// <para>All remain reachable today by adding a custom endpoint by hand.</para>
        /// </summary>
        public static IReadOnlyList<AiProviderPreset> All => Items;

        /// <summary>The preset with this id, or null. Ids are reserved: a custom connection may not take one.</summary>
        public static AiProviderPreset? ById(string id) =>
            Items.FirstOrDefault(p => string.Equals(p.Id, id, StringComparison.OrdinalIgnoreCase));

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
