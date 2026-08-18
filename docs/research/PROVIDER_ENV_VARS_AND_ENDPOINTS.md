# Provider Environment Variables and Endpoints

**Date:** 2026-08-17
**Status:** Research spike for [#682](https://github.com/fsnow/cst/issues/682); input to the `Connections[]` preset list in #678
**Scope:** Facts about third-party provider APIs — environment variable names, base URLs, auth shapes, required headers.

## What this is

CST Reader is reworking its provider/model configuration into a `Connections[]` model, where each
connection is `{ Id, DisplayName, Kind, BaseUrl, HasKey, Headers[], Models[] }`. To offer named
presets, and to recognise credentials the user already has in their environment, we need a
reconciled table of provider facts.

This document was mined from **opencode** — <https://github.com/anomalyco/opencode>, MIT licensed —
at commit **`e14acea58108ad97fef5dcc179a359d9d0f75eac`** (2026-08-17, "update ds flash limit").
Paths of the form `packages/…` below refer to that repository. Everything here is a fact about a
*third-party API* — the env var Anthropic's own SDK reads, the URL xAI serves on — not a design
choice of opencode's. Those facts are the same in any client, and none of them is copied code.
See [Attribution](#attribution) for the one place where that distinction matters.

**Deliberately excluded.** The upstream catalogue (see [Model catalogue](#model-catalogue)) also
carries pricing, benchmark-adjacent capability flags, and per-model status/recommendation data.
CST Reader deleted exactly that kind of table in PR #681 on principle: Pāli ability is an emergent
capability not predicted by benchmarks, price, or parameter count, so we do not maintain quality
judgments about models. The fields that would breach that rule are identified explicitly
[below](#fields-that-are-off-limits-for-us) and are not reproduced anywhere in this document.

---

## The main table

Two columns need reading carefully:

- **Env var(s)** — the names a provider's own SDK/CLI conventionally reads. Where several are
  listed the catalogue lists them as *alternates* with no stated precedence (see
  [Precedence](#precedence-and-fallback-chains) for where precedence *is* explicit in code).
- **Auth shape** — how the secret reaches the wire.

Base URLs marked "SDK default" are not carried in the catalogue; the provider's SDK supplies them.
The `llm` package's own protocol defaults are given where observed in source.

### First-party / protocol-native

| Provider | id / slug | Env var(s) | Base URL | Auth shape | Key required | Notes |
|---|---|---|---|---|---|---|
| Anthropic | `anthropic` | `ANTHROPIC_API_KEY` | `https://api.anthropic.com/v1` (`packages/llm/src/protocols/anthropic-messages.ts:29`) | `x-api-key: <key>` (`packages/llm/src/providers/anthropic.ts:17`); alternate `Authorization: Bearer` when an `authToken` is supplied (`packages/core/src/v1/config/provider-options.ts:68`) | yes | Requires header `anthropic-version: 2023-06-01` (`packages/llm/src/protocols/anthropic-messages.ts:852`). Path `/messages` (`:30`) |
| OpenAI | `openai` | `OPENAI_API_KEY` | `https://api.openai.com/v1` (`packages/llm/src/protocols/openai-responses.ts:29`, `openai-chat.ts:28`) | `Authorization: Bearer` (`packages/llm/src/providers/openai.ts:24`) | yes | Two paths: `/responses` (`openai-responses.ts:30`) and `/chat/completions` (`openai-chat.ts:29`). Optional `OpenAI-Organization` / `OpenAI-Project` headers from `organization`/`project` options (`packages/core/src/v1/config/provider-options.ts:35-36`) |
| Google (Gemini API) | `google` | `GOOGLE_API_KEY`, `GOOGLE_GENERATIVE_AI_API_KEY`, `GEMINI_API_KEY` | `https://generativelanguage.googleapis.com/v1beta` (`packages/llm/src/protocols/gemini.ts:27`) | `x-goog-api-key: <key>` (`packages/llm/src/providers/google.ts:17`) | yes | Path embeds the model id and pins SSE: `/models/{id}:streamGenerateContent?alt=sse` (`gemini.ts:505`). opencode's own code reads only `GOOGLE_GENERATIVE_AI_API_KEY`; the other two names come from the catalogue |
| xAI | `xai` | `XAI_API_KEY` | `https://api.x.ai/v1` (`packages/llm/src/providers/openai-compatible-profile.ts:15`) | `Authorization: Bearer` (`packages/llm/src/providers/xai.ts:17`) | yes | Speaks both OpenAI Responses and OpenAI-compatible chat (`xai.ts:15`) |
| Azure OpenAI | `azure` | `AZURE_API_KEY`, `AZURE_RESOURCE_NAME` | `https://{resourceName}.openai.azure.com/openai/v1` (`packages/llm/src/providers/azure.ts:26`), or explicit `baseURL` | `api-key: <key>` — **not** bearer; the `authorization` header is explicitly removed first (`azure.ts:10,67-71`) | yes | Also needs a resource name or an explicit base URL: `AtLeastOne<{resourceName, baseURL}>` (`azure.ts:14`), `baseURL` wins (`:79`). Query param `api-version` defaults to `v1` (`:33`) and is overridable (`:81`). opencode's own env read is `AZURE_OPENAI_API_KEY` (`azure.ts:69`), which differs from the catalogue's `AZURE_API_KEY` — see [Discrepancies](#discrepancies-worth-knowing) |
| Azure Cognitive Services | `azure-cognitive-services` | `AZURE_COGNITIVE_SERVICES_API_KEY`, `AZURE_COGNITIVE_SERVICES_RESOURCE_NAME` | `https://{resourceName}.cognitiveservices.azure.com/openai` (`packages/core/src/plugin/provider/azure.ts:70`) | as OpenAI-compatible (bearer) | yes | Base URL is assembled from the resource-name env var at catalogue-transform time (`azure.ts:63-72`) |
| Amazon Bedrock | `amazon-bedrock` | `AWS_ACCESS_KEY_ID`, `AWS_SECRET_ACCESS_KEY`, `AWS_REGION`, `AWS_BEARER_TOKEN_BEDROCK`; also honoured: `AWS_PROFILE`, `AWS_SESSION_TOKEN`, `AWS_WEB_IDENTITY_TOKEN_FILE`, `AWS_CONTAINER_CREDENTIALS_RELATIVE_URI`, `AWS_CONTAINER_CREDENTIALS_FULL_URI` | `https://bedrock-runtime.{region}.amazonaws.com` (`packages/llm/src/providers/amazon-bedrock.ts:20`); region defaults to `us-east-1` (`:24`) | SigV4 request signing when no bearer token; `Authorization: Bearer` when `AWS_BEARER_TOKEN_BEDROCK` is set (`amazon-bedrock.ts:29`) | yes (or ambient AWS creds) | Falls back to the full AWS default credential chain — `~/.aws/credentials`, SSO, process creds, instance roles — so **absence of env vars does not mean absence of credentials** (`packages/core/src/plugin/provider/amazon-bedrock.ts:97-102`) |
| Google Vertex | `google-vertex` | `GOOGLE_VERTEX_PROJECT`, `GOOGLE_VERTEX_LOCATION`, `GOOGLE_APPLICATION_CREDENTIALS`; aliases also read: `GOOGLE_CLOUD_PROJECT`, `GCP_PROJECT`, `GCLOUD_PROJECT`, `GOOGLE_CLOUD_LOCATION`, `VERTEX_LOCATION` | `https://{location}-aiplatform.googleapis.com`, or `https://aiplatform.googleapis.com` when location is `global` (`packages/core/src/plugin/provider/google-vertex.ts:27-30`) | ADC (Application Default Credentials) → `Authorization: Bearer <access token>`; scope `https://www.googleapis.com/auth/cloud-platform` (`google-vertex.ts:44-50`) | no API key — OAuth/ADC | Location default `us-central1` (`:23`). Catalogue base URLs are *templates* containing `${GOOGLE_VERTEX_PROJECT}` / `${GOOGLE_VERTEX_LOCATION}` / `${GOOGLE_VERTEX_ENDPOINT}`, expanded after resolution (`:32-39`) |
| Google Vertex (Anthropic) | `google-vertex-anthropic` | same as Vertex | continental multi-regions `eu`/`us` need `https://aiplatform.{location}.rep.googleapis.com/v1/projects/{project}/locations/{location}/publishers/anthropic/models` — the default `{region}-aiplatform.googleapis.com` does not resolve (`google-vertex.ts:156-162`) | ADC bearer | no API key | Location default here is `global`, not `us-central1` (`:132`) |
| GitHub Copilot | `github-copilot` | `GITHUB_TOKEN` | `https://api.githubcopilot.com` (catalogue; also `packages/opencode/test/plugin/github-copilot-models.test.ts:61`) | `Authorization: Bearer` (`packages/core/src/github-copilot/copilot-provider.ts:62`) | yes | opencode's native provider has **no** default URL — the caller must supply `baseURL` (`packages/llm/src/providers/github-copilot.ts:10-14`). Endpoint choice is model-dependent: Responses for `gpt-N` where N ≥ 5 except `gpt-5-mini*`, chat otherwise (`github-copilot.ts:19-25`) |
| Cloudflare Workers AI | `cloudflare-workers-ai` | `CLOUDFLARE_ACCOUNT_ID`, `CLOUDFLARE_API_KEY`; opencode's native path also accepts `CLOUDFLARE_WORKERS_AI_TOKEN` (`packages/llm/src/providers/cloudflare.ts:11`) | `https://api.cloudflare.com/client/v4/accounts/{accountId}/ai/v1` (`cloudflare.ts:56`) | `Authorization: Bearer` (`cloudflare.ts:60`) | yes | Account id is mandatory unless an explicit base URL is given (`:55`). Catalogue base URL is the template `…/accounts/${CLOUDFLARE_ACCOUNT_ID}/ai/v1`, expanded at runtime (`packages/core/src/plugin/provider/cloudflare-workers-ai.ts:74-77`) |
| Cloudflare AI Gateway | `cloudflare-ai-gateway` | `CLOUDFLARE_API_TOKEN`, `CF_AIG_TOKEN`, `CLOUDFLARE_ACCOUNT_ID`, `CLOUDFLARE_GATEWAY_ID` | `https://gateway.ai.cloudflare.com/v1/{accountId}/{gatewayId}/compat`; `gatewayId` defaults to `default` (`packages/llm/src/providers/cloudflare.ts:39`) | **two** headers: `cf-aig-authorization: Bearer <gateway token>` for the gateway, plus `Authorization: Bearer <upstream key>` for the upstream provider (`cloudflare.ts:42-50`) | yes | The clearest example of a provider needing *two independent* credentials on one request |
| OpenRouter | `openrouter` | `OPENROUTER_API_KEY` | `https://openrouter.ai/api/v1` (`packages/llm/src/providers/openai-compatible-profile.ts:13`) | `Authorization: Bearer` (`packages/llm/src/providers/openrouter.ts:84`) | yes | Attribution headers `HTTP-Referer` and `X-Title` are conventional for OpenRouter (`packages/core/src/plugin/provider/openrouter.ts:14-15`) |
| Mistral | `mistral` | `MISTRAL_API_KEY` | SDK default | bearer | yes | |
| Cohere | `cohere` | `COHERE_API_KEY` | SDK default | bearer | yes | |
| Perplexity | `perplexity` | `PERPLEXITY_API_KEY` | SDK default | bearer | yes | |
| GitLab Duo | `gitlab` | `GITLAB_TOKEN`, `GITLAB_INSTANCE_URL` | `https://gitlab.com` (`packages/core/src/plugin/provider/gitlab.ts:19`) | bearer via the `gitlab-ai-provider` package | yes | |
| SAP AI Core | `sap-ai-core` | `AICORE_SERVICE_KEY`; also `AICORE_DEPLOYMENT_ID`, `AICORE_RESOURCE_GROUP` (`packages/core/src/plugin/provider/sap-ai-core.ts:15,34`) | from the service key | service-key JSON, not a plain key | yes | |
| Snowflake Cortex | `snowflake-cortex` | `SNOWFLAKE_ACCOUNT`, `SNOWFLAKE_CORTEX_PAT`; also `SNOWFLAKE_CORTEX_TOKEN` (`packages/core/src/plugin/provider/snowflake-cortex.ts:74-75`) | `https://${SNOWFLAKE_ACCOUNT}.snowflakecomputing.com/api/v2/cortex/v1` (template, catalogue) | bearer | yes | Needs request/response rewriting — see [Endpoint quirks](#endpoint-quirks) |
| Vercel AI Gateway | `vercel` | `AI_GATEWAY_API_KEY` | SDK default | bearer | yes | |
| Databricks | `databricks` | `DATABRICKS_HOST`, `DATABRICKS_TOKEN` | `https://${DATABRICKS_HOST}/ai-gateway/mlflow/v1` (template) | bearer | yes | |
| IBM watsonx | `watsonx` | `WATSONX_AI_APIKEY`, `WATSONX_AI_PROJECT_ID` | SDK default | IAM token exchange | yes | |

### OpenAI-compatible endpoints (`Kind = OpenAiCompatible` for us)

All of the following speak `POST {baseURL}/chat/completions` with `Authorization: Bearer <key>`
unless noted. This is the shape that matters most to CST Reader: **150 of the catalogue's 189
providers use `@ai-sdk/openai-compatible`**, i.e. one code path covers the overwhelming majority.

| Provider | id / slug | Env var | Base URL |
|---|---|---|---|
| Groq | `groq` | `GROQ_API_KEY` | `https://api.groq.com/openai/v1` (`openai-compatible-profile.ts:12`) |
| Cerebras | `cerebras` | `CEREBRAS_API_KEY` | `https://api.cerebras.ai/v1` (`:8`) |
| DeepSeek | `deepseek` | `DEEPSEEK_API_KEY` | `https://api.deepseek.com/v1` (`:10`); catalogue says `https://api.deepseek.com` |
| Together AI | `togetherai` | `TOGETHER_API_KEY` | `https://api.together.xyz/v1` (`:14`) |
| Fireworks AI | `fireworks-ai` | `FIREWORKS_API_KEY` | `https://api.fireworks.ai/inference/v1` (`:11`) |
| DeepInfra | `deepinfra` | `DEEPINFRA_API_KEY` | `https://api.deepinfra.com/v1/openai` (`:9`) |
| Baseten | `baseten` | `BASETEN_API_KEY` | `https://inference.baseten.co/v1` (`:7`) |
| Alibaba (DashScope) | `alibaba` | `DASHSCOPE_API_KEY` | `https://dashscope-intl.aliyuncs.com/compatible-mode/v1` |
| Moonshot AI | `moonshotai` | `MOONSHOT_API_KEY` | `https://api.moonshot.ai/v1` |
| Zhipu AI | `zhipuai` | `ZHIPU_API_KEY` | `https://open.bigmodel.cn/api/paas/v4` |
| Z.ai | `zai` | `ZHIPU_API_KEY` | `https://api.z.ai/api/paas/v4` |
| SiliconFlow | `siliconflow` | `SILICONFLOW_API_KEY` | `https://api.siliconflow.com/v1` |
| Hugging Face | `huggingface` | `HF_TOKEN` | `https://router.huggingface.co/v1` |
| Nvidia | `nvidia` | `NVIDIA_API_KEY` | `https://integrate.api.nvidia.com/v1` |
| Nebius Token Factory | `nebius` | `NEBIUS_API_KEY` | `https://api.tokenfactory.nebius.com/v1` |
| Novita AI | `novita-ai` | `NOVITA_API_KEY` | `https://api.novita.ai/openai` |
| Chutes | `chutes` | `CHUTES_API_KEY` | `https://llm.chutes.ai/v1` |
| Requesty | `requesty` | `REQUESTY_API_KEY` | `https://router.requesty.ai/v1` |
| LLM Gateway | `llmgateway` | `LLMGATEWAY_API_KEY` | `https://api.llmgateway.io/v1` |
| ZenMux | `zenmux` | `ZENMUX_API_KEY` | `https://zenmux.ai/api/v1` |
| Kilo Gateway | `kilo` | `KILO_API_KEY` | `https://api.kilo.ai/api/gateway` |
| Helicone | `helicone` | `HELICONE_API_KEY` | `https://ai-gateway.helicone.ai/v1` |
| Upstage | `upstage` | `UPSTAGE_API_KEY` | `https://api.upstage.ai/v1/solar` |
| Morph | `morph` | `MORPH_API_KEY` | `https://api.morphllm.com/v1` |
| Inception | `inception` | `INCEPTION_API_KEY` | `https://api.inceptionlabs.ai/v1/` |
| Llama (Meta) | `llama` | `LLAMA_API_KEY` | `https://api.llama.com/compat/v1/` |
| Ollama Cloud | `ollama-cloud` | `OLLAMA_API_KEY` | `https://ollama.com/v1` |
| LM Studio (local) | `lmstudio` | `LMSTUDIO_API_KEY` | `http://127.0.0.1:1234/v1` |
| Poe | `poe` | `POE_API_KEY` | `https://api.poe.com/v1` |
| Scaleway | `scaleway` | `SCALEWAY_API_KEY` | `https://api.scaleway.ai/v1` |
| DigitalOcean | `digitalocean` | `DIGITALOCEAN_ACCESS_TOKEN` | `https://inference.do-ai.run/v1` |
| OVHcloud | `ovhcloud` | `OVHCLOUD_API_KEY` | `https://oai.endpoints.kepler.ai.cloud.ovh.net/v1` |
| Friendli | `friendli` | `FRIENDLI_TOKEN` | `https://api.friendli.ai/serverless/v1` |
| ModelScope | `modelscope` | `MODELSCOPE_API_KEY` | `https://api-inference.modelscope.cn/v1` |
| Weights & Biases | `wandb` | `WANDB_API_KEY` | `https://api.inference.wandb.ai/v1` |
| Clarifai | `clarifai` | `CLARIFAI_PAT` | `https://api.clarifai.com/v2/ext/openai/v1` |
| NanoGPT | `nano-gpt` | `NANO_GPT_API_KEY` | `https://nano-gpt.com/api/v1` |
| Venice AI | `venice` | `VENICE_API_KEY` | SDK default (`venice-ai-sdk-provider`) |
| OpenCode Zen | `opencode` | `OPENCODE_API_KEY` | `https://opencode.ai/zen/v1` |

*(The full catalogue lists 189 providers; the above is the subset likely to matter to CST Reader.
The rest are enumerable from the catalogue URL in [Model catalogue](#model-catalogue).)*

### Anthropic-protocol endpoints that are not Anthropic

Useful to know: several providers expose the **Anthropic Messages** wire format at a custom base
URL, so a `Kind = Anthropic` connection with an overridable `BaseUrl` covers them.

| Provider | id / slug | Env var | Base URL |
|---|---|---|---|
| MiniMax | `minimax` | `MINIMAX_API_KEY` | `https://api.minimax.io/anthropic/v1` |
| MiniMax (CN) | `minimax-cn` | `MINIMAX_API_KEY` | `https://api.minimaxi.com/anthropic/v1` |
| Kimi for Coding | `kimi-for-coding` | `KIMI_API_KEY` | `https://api.kimi.com/coding/v1` |
| Subconscious | `subconscious` | `SUBCONSCIOUS_API_KEY` | `https://api.subconscious.dev/v1` |
| Thinking Machines (Tinker) | `thinkingmachines` | `TINKER_API_KEY` | `https://tinker.thinkingmachines.dev/services/tinker-prod/anthropic/api/v1` |
| FreeModel | `freemodel` | `FREEMODEL_API_KEY` | `https://cc.freemodel.dev/v1` |

### Precedence and fallback chains

Where opencode's own code checks several sources, the order is explicit and worth copying:

1. An explicitly-supplied `auth` object short-circuits everything
   (`packages/llm/src/route/auth-options.ts:48`).
2. Otherwise an explicit `apiKey` option is tried first, then each env var in the declared order,
   via a left-to-right `orElse` fold (`auth-options.ts:49-54`). An **empty string counts as
   missing** and falls through (`packages/llm/src/route/auth.ts:72`).
3. Concretely: Cloudflare AI Gateway is `gatewayApiKey` option → `CLOUDFLARE_API_TOKEN` →
   `CF_AIG_TOKEN` (`packages/llm/src/providers/cloudflare.ts:44-46`); Workers AI is
   `CLOUDFLARE_API_KEY` → `CLOUDFLARE_WORKERS_AI_TOKEN` (`:11`, `:60`).
4. Vertex project: option → `GOOGLE_VERTEX_PROJECT` → `GOOGLE_CLOUD_PROJECT` → `GCP_PROJECT` →
   `GCLOUD_PROJECT` (`packages/core/src/plugin/provider/google-vertex.ts:8-14`). Location: option →
   `GOOGLE_VERTEX_LOCATION` → `GOOGLE_CLOUD_LOCATION` → `VERTEX_LOCATION` → `us-central1` (`:17-25`).
5. Bedrock: `AWS_BEARER_TOKEN_BEDROCK` beats the option-supplied bearer token; region is option →
   `AWS_REGION` → `us-east-1` (`packages/core/src/plugin/provider/amazon-bedrock.ts:86-89`).

**No provider base URL is read from the environment.** A repo-wide grep for `*_BASE_URL` in the
provider paths finds only unrelated GitHub/OIDC settings — there is no `OPENAI_BASE_URL` or
`ANTHROPIC_BASE_URL` honoured anywhere. Base URLs come from config, the catalogue, or a hard-coded
default. *(Observed absence, so weaker evidence than a positive finding, but the grep was
exhaustive over `core/src`, `llm/src`, and `opencode/src`.)*

### Discrepancies worth knowing

These are places where the catalogue and opencode's own code disagree, so we should treat the
catalogue as the *convention* and opencode's read as one client's choice:

- **Azure**: catalogue says `AZURE_API_KEY`; opencode reads `AZURE_OPENAI_API_KEY`
  (`packages/llm/src/providers/azure.ts:69`). Microsoft's own docs use both in different places.
  *Uncertain* which is more widely set; supporting both is cheap.
- **Google**: catalogue lists three alternates (`GOOGLE_API_KEY`, `GOOGLE_GENERATIVE_AI_API_KEY`,
  `GEMINI_API_KEY`); opencode's native provider reads only `GOOGLE_GENERATIVE_AI_API_KEY`
  (`packages/llm/src/providers/google.ts:16`). `GEMINI_API_KEY` is the name most users will
  actually have set, from the Gemini CLI.
- **DeepSeek**: opencode's profile uses `https://api.deepseek.com/v1`, the catalogue says
  `https://api.deepseek.com`. Both work; DeepSeek accepts either.

---

## How detection works: theirs vs. upstream

**Short answer: neither, entirely — it is delegated to a third source, the models.dev catalogue,
and opencode contributes only corrections on top.** Three layers, in order:

### 1. The catalogue supplies the env var names (the primary mechanism)

The catalogue's provider record carries an `env: string[]` field
(`packages/core/src/models-dev.ts:126`). Detection is then a one-liner: an integration is
considered connected-via-environment if **any** of the declared names is set.

```ts
// packages/core/src/integration.ts:296-299
const env = (entry?.methods ?? [])
  .filter((method) => method.type === "env")
  .flatMap((method) => method.names.filter((name) => process.env[name]))
  .map((name) => ({ type: "env" as const, name }))
```

The names get registered as an auth "method" per provider at catalogue-load time
(`packages/core/src/plugin/models-dev.ts:128-138`), and resolving one just reads it back
(`packages/core/src/integration.ts:387-388`). The legacy v1 path does the same test
(`packages/opencode/src/provider/provider.ts:182`: `input.env.some((item) => env[item])`).

So: **there is no hand-maintained env-var table in opencode's source.** The table is data,
downloaded at runtime.

### 2. `@ai-sdk/*` packages supply the transport

Which npm package speaks to which provider is also catalogue data (`npm` field), and opencode's
per-provider plugins are thin: each one matches on the package name and calls that package's
`create*` factory. E.g. `packages/core/src/plugin/provider/groq.ts:9-11` — nine lines, no facts.
So the *auth header shape and SDK default base URL* for these providers are upstream Vercel AI SDK
conventions, and should be cited to `@ai-sdk/<name>`, not to opencode:

| Provider | Governing upstream package |
|---|---|
| anthropic | `@ai-sdk/anthropic` |
| openai, meta, perplexity-agent, vivgrid | `@ai-sdk/openai` |
| google | `@ai-sdk/google` |
| google-vertex | `@ai-sdk/google-vertex` |
| google-vertex-anthropic | `@ai-sdk/google-vertex/anthropic` |
| azure, azure-cognitive-services | `@ai-sdk/azure` |
| amazon-bedrock | `@ai-sdk/amazon-bedrock` |
| xai | `@ai-sdk/xai` |
| groq, cerebras, mistral, cohere, perplexity, deepinfra, togetherai | `@ai-sdk/<name>` |
| openrouter | `@openrouter/ai-sdk-provider` |
| vercel | `@ai-sdk/gateway` / `@ai-sdk/vercel` |
| cloudflare-ai-gateway | `ai-gateway-provider` |
| venice | `venice-ai-sdk-provider` |
| gitlab | `gitlab-ai-provider` |
| sap-ai-core | `@jerome-benoit/sap-ai-provider-v2` |
| watsonx | `watsonx-ai-provider` |
| **150 others** | `@ai-sdk/openai-compatible` |

Package versions are pinned in `packages/core/package.json`.

### 3. opencode's own `llm` package: a native, SDK-free reimplementation

`packages/llm/` is a newer in-house HTTP layer that does not use the AI SDK at all — it has its own
protocol implementations (`anthropic-messages`, `gemini`, `openai-chat`, `openai-responses`,
`bedrock-converse`, `openai-compatible-chat`) and its own `Auth` combinators. **This is the layer
with genuine first-party facts** — hard-coded default base URLs, header names, the `x-api-key` vs
bearer distinction — because it had to reimplement each wire protocol from the vendor docs. Most of
the citations in the main table point here for that reason.

### What this means for us

CST Reader has no models.dev dependency and doesn't want a runtime catalogue fetch (see
[What CST Reader should do](#what-cst-reader-should-actually-do)). The transcribed table above is
the substitute: a small static preset list, which is exactly the shape opencode would have if it
weren't downloading one.

---

## The OpenAI-compatible profile mechanism

This is directly analogous to our `OpenAiCompatible` kind, and the headline finding is how *thin*
it is.

### What a "profile" is

Literally two fields (`packages/llm/src/providers/openai-compatible-profile.ts:1-4`):

```ts
export interface OpenAICompatibleProfile {
  readonly provider: string
  readonly baseURL: string
}
```

Nine profiles are declared — baseten, cerebras, deepinfra, deepseek, fireworks, groq, openrouter,
togetherai, xai (`:6-16`) — plus a `byProvider` index (`:18-20`). A profile is used to `define()` a
named provider facade whose only difference from the generic one is a default base URL
(`packages/llm/src/providers/openai-compatible.ts:38-51`).

The generic path is likewise minimal (`openai-compatible.ts:22-36`): take `{provider, baseURL,
apiKey}`, bearer-auth it, and point the shared `openai-compatible-chat` route at it. That route
reuses the OpenAI Chat protocol end-to-end and overrides *only its id*, so providers can be
distinguished without colliding with native OpenAI
(`packages/llm/src/protocols/openai-compatible-chat.ts:10-22`). Path is `/chat/completions`,
framing is SSE.

**Conclusion for us: an OpenAI-compatible preset genuinely is just `{ name, baseURL, envVar }`.**
No per-provider request-shape table is needed for the common case. That is a real, reassuring
finding — the complexity we might have budgeted for does not exist.

### Endpoint quirks

Where per-endpoint adjustments *do* exist, they are isolated exceptions, not a matrix. The complete
inventory found:

**Request-body rewrites**

- **Snowflake Cortex** rejects `max_tokens` and wants `max_completion_tokens`; a fetch wrapper
  renames the field (`packages/core/src/plugin/provider/snowflake-cortex.ts:13-17`).
- **OpenAI Responses (and Azure, and Bedrock "mantle")**: when `store !== true`, every `id` field
  must be stripped from `body.input[]` items, or the stateless request is rejected
  (`packages/core/src/aisdk.ts:101-110`).
- **`stream_options: { include_usage: true }`** is set unconditionally on OpenAI-chat-shaped
  requests so usage is reported (`packages/llm/src/protocols/openai-chat.ts:360`); the
  OpenAI-compatible plugin defaults `includeUsage` to true unless explicitly disabled
  (`packages/core/src/plugin/provider/openai-compatible.ts:11`).
- **Option-name lowering** differs by family: OpenAI Responses nests `reasoningEffort` /
  `reasoningSummary` under `reasoning: {effort, summary}` and `textVerbosity` under
  `text: {verbosity}` (`packages/core/src/v1/config/provider-options.ts:45-57`), whereas the
  OpenAI-compatible family keeps a flat snake_case `reasoning_effort`
  (`provider-options.ts:134-137`). Google nests `thinkingConfig`, `responseModalities`,
  `mediaResolution`, `imageConfig` under `generationConfig` (`:99-103`). Anthropic maps
  `effort`/`taskBudget` to `output_config: {effort, task_budget}` and `metadata.userId` to
  `metadata.user_id` (`:77-84`).

**Response-shape rewrites**

- **Snowflake Cortex** returns HTTP 400 with a "conversation complete" message as a *normal* stop
  condition, and emits `"role": ""` in streaming deltas where the schema requires `"assistant"`;
  both are patched in a fetch wrapper (`snowflake-cortex.ts:23-60`).
- **DeepSeek-style reasoning**: the OpenAI-chat protocol accepts a non-standard
  `reasoning_content` field on messages and deltas
  (`packages/llm/src/protocols/openai-chat.ts:77,147,419-420`).

**Tool-schema projections** — the only thing resembling a quirk *table*, and it has exactly two
entries (`packages/llm/src/schema/options.ts:166`):

```ts
export const ModelToolSchemaCompatibility = Schema.Literals(["gemini", "moonshot"])
```

- `gemini` — convert JSON Schema to Gemini's restricted dialect.
- `moonshot` — collapse tuple `items` arrays, translate `prefixItems` to `items`, drop
  `unevaluatedItems`, and reduce `$ref` nodes to bare refs
  (`packages/llm/src/protocols/utils/tool-schema.ts:26-46`).
- A third projection, `openAI`, flattens `anyOf` into a single object with
  `additionalProperties: false` and strips `null` variants (`:48-64`), but is applied by protocol
  rather than selected per model.

**Header conventions** (attribution/telemetry, not auth — all optional)

- OpenRouter, ZenMux, Kilo, LLM Gateway, Nvidia, Vercel: `HTTP-Referer` + `X-Title` identifying the
  client (`packages/core/src/plugin/provider/openrouter.ts:14-15` and siblings). Nvidia adds
  `X-BILLING-INVOKE-ORIGIN` (`nvidia.ts:16`); LLM Gateway adds `X-Source` (`llmgateway.ts:20`).
- Cerebras: `X-Cerebras-3rd-Party-Integration` (`cerebras.ts:13`).
- Anthropic beta features are opt-in headers, e.g.
  `anthropic-beta: interleaved-thinking-2025-05-14,fine-grained-tool-streaming-2025-05-14`
  (`anthropic.ts:13-14`) and `context-1m-2025-08-07` on the GitLab path (`gitlab.ts:23`).

**Endpoint selection by model id** — worth flagging because it is genuinely awkward: GitHub Copilot
routes `gpt-N` (N ≥ 5, except `gpt-5-mini*`) to `/responses` and everything else to
`/chat/completions` (`packages/llm/src/providers/github-copilot.ts:19-25`,
`packages/core/src/plugin/provider/github-copilot.ts:42-48`). Bedrock similarly needs cross-region
inference prefixes (`us.`, `eu.`, `apac.`, `jp.`, `au.`) for particular model/region pairs
(`packages/core/src/plugin/provider/amazon-bedrock.ts:15-53`).

---

## Model catalogue

**It is models.dev, fetched at runtime.** This answers the open question from our UX study.

- **Source URL:** `https://models.opencode.ai/api.json`, overridable by `OPENCODE_MODELS_URL`
  (`packages/core/src/models-dev.ts:160`). Verified 2026-08-17: this is byte-identical in shape and
  membership to `https://models.dev/api.json` — same 189 provider keys, identical records spot-checked.
  It is a mirror, not a fork.
- **Repository:** <https://github.com/anomalyco/models.dev> (formerly `sst/models.dev`; the old URL
  still redirects). **MIT licensed**, per the GitHub API. Site: <https://models.dev>.
- **Caching:** written to `{cache}/models.json`, 5-minute freshness TTL, refreshed on a 60-minute
  schedule, cross-process file-locked (`models-dev.ts:161-166, 237-257`). Can be disabled with
  `OPENCODE_DISABLE_MODELS_FETCH` or replaced with a local file via `OPENCODE_MODELS_PATH`
  (`:184, 222`). A build-time snapshot (`OPENCODE_MODELS_DEV`) can be embedded (`:136, 198-200`).
- **Not** live `/v1/models` calls. No provider is queried for its model list on the normal path.
  *(One exception observed: `packages/opencode/test/plugin/github-copilot-models.test.ts` exercises
  a live Copilot model listing, so that provider does have a discovery path.)*

### Fields it carries

*Provider* record (`packages/core/src/models-dev.ts:123-130`, confirmed against live JSON):
`id`, `name`, `env[]`, `api` (base URL, sometimes a `${VAR}` template), `npm`, `doc`, `models{}`.

*Model* record (`models-dev.ts:67-120`, plus fields present in the live JSON but not in opencode's
schema): `id`, `name`, `family`, `release_date`, `last_updated`, `knowledge`, `attachment`,
`reasoning`, `reasoning_options`, `temperature`, `tool_call`, `structured_output`, `open_weights`,
`interleaved`, `modalities{input[],output[]}`, `limit{context,input,output}`, `cost{…}`,
`status`, `description`, `experimental{modes{…}}`, `provider{npm,api}`.

### Fields that are off-limits for us

Under the no-curation rule from PR #681, these carry or imply quality/desirability judgments and
must **not** be imported, surfaced, or used to order a model list:

- **`cost` / `tiers` / `context_over_200k`** — full pricing. Price is the single most common proxy
  for "better model", and it is not one for Pāli.
- **`status`** (`alpha` | `beta` | `deprecated`, `models-dev.ts:15`) — a lifecycle judgment that
  reads as a recommendation. Note that opencode uses it to *hide* models
  (`packages/core/src/plugin/models-dev.ts:108`).
- **`description`** — free-text marketing copy from the vendor.
- **Any derived ranking.** opencode additionally disables specific models by id
  (`gpt-5-chat-latest` on OpenAI/Copilot/OpenRouter — `plugin/provider/openai.ts:167-171`,
  `openrouter.ts:17-24`). Those are correctness workarounds, not rankings, but the *mechanism*
  (a curated allow/deny list) is precisely what we removed. Don't rebuild it.

Fields that are **fine** to use, because they are mechanical facts a client needs to send a valid
request: `id`, `name`, `family`, `limit.context`, `limit.output`, `modalities`, `tool_call`,
`reasoning`, `temperature`, `release_date`.

---

## Attribution

Nothing in this document is copied code. Env var names, base URLs, header names and wire-format
requirements are facts about third-party APIs — identical in every client, and not opencode's to
license. Transcribing them into our own table carries no MIT obligation.

Two places would cross that line if we went further, and we should not without carrying the notice:

1. **`packages/llm/src/protocols/*.ts`** — the hand-written protocol implementations (34 KB for
   Anthropic Messages alone). If we ever port streaming/framing logic, that is substantial copying.
2. **`packages/core/src/plugin/provider/snowflake-cortex.ts`** and the tool-schema projections in
   `packages/llm/src/protocols/utils/tool-schema.ts` — these are non-obvious *solutions*, not
   facts. Describing the quirk is fine; lifting the function is not.

If we ever bundle the models.dev catalogue itself, that is MIT-licensed data from
<https://github.com/anomalyco/models.dev> and its notice must travel with it.

---

## What CST Reader should actually do

Aimed at the preset list in #678:

- **Ship a small static preset table, not a catalogue fetch.** The presets are `{ DisplayName, Kind,
  BaseUrl, EnvVarNames[] }` — maybe 15–20 rows from the tables above. A runtime catalogue would
  drag in pricing and status fields we've committed not to carry, plus a network dependency and a
  cache, for facts that change a few times a year. Take the data, decline the mechanism.

- **Make `EnvVarNames` a list, and detect with "any is set".** This is the single most transferable
  piece of the design (`packages/core/src/integration.ts:296-299`). Several providers genuinely have
  multiple conventional names — Google has three, Cloudflare two per surface — and an empty string
  must count as unset (`packages/llm/src/route/auth.ts:72`). Show the user *which* variable was
  found; the name is the useful part of the message.

- **`Kind = OpenAiCompatible` carries the load; a preset is just a base URL.** 150 of 189 catalogue
  providers are plain `POST {baseURL}/chat/completions` + `Authorization: Bearer`. The
  "profile" abstraction that opencode built for this is two fields
  (`packages/llm/src/providers/openai-compatible-profile.ts:1-4`). We do not need a quirks matrix,
  and we should resist building one. Add `Kind = Anthropic` with an overridable `BaseUrl` as well —
  six providers in the table speak Anthropic Messages at a custom URL, and Anthropic's own
  `x-api-key` + `anthropic-version: 2023-06-01` shape is different enough to deserve its own kind.

- **Budget for exactly three auth shapes, not one.** Bearer (`Authorization`), Anthropic
  (`x-api-key`), and Azure (`api-key`, with `authorization` explicitly *removed* —
  `packages/llm/src/providers/azure.ts:10,67-71`). Google's `x-goog-api-key` is a fourth if we
  support the Gemini API natively rather than through its OpenAI-compatible endpoint.

- **Treat "needs more than a key" as a first-class state, not a failure.** Azure needs a resource
  name; Cloudflare needs an account id; Bedrock needs a region and may have ambient credentials with
  no env var set at all; Vertex needs a project and uses ADC. Our `HasKey` boolean can't express
  these. Either scope the preset list to key-only providers for v1 and say so, or let a preset
  declare additional required fields. The Cloudflare AI Gateway case — *two* independent tokens on
  one request (`packages/llm/src/providers/cloudflare.ts:42-50`) — is the stress test for the
  `Headers[]` model.

- **Don't read base URLs from the environment.** No provider convention exists for it; opencode
  reads none. `BaseUrl` belongs in the connection record.
