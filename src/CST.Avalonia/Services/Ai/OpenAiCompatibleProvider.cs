using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services.Ai;

/// <summary>Configuration for <see cref="OpenAiCompatibleProvider"/>.</summary>
/// <param name="BaseUrl">Required. This one field is what makes a single adapter serve DeepSeek, OpenRouter,
/// Together, Ollama and LM Studio.</param>
/// <param name="ApiKey">Optional — local runners (Ollama, LM Studio) accept anonymous requests, so an empty key
/// is a valid configuration rather than a misconfiguration. A remote provider will answer 401 on its own.</param>
/// <param name="AuthHeaderName">Which header carries the credential. Almost always <c>Authorization</c>;
/// Azure uses <c>api-key</c> and expects <c>Authorization</c> to be ABSENT, so naming a different header
/// REPLACES the standard one rather than adding to it. (#689)</param>
/// <param name="AuthScheme">Prefix before the credential, or null for a bare value.</param>
/// <param name="ExtraHeaders">Static or templated headers the endpoint needs — never the credential itself.</param>
public sealed record OpenAiCompatibleOptions(
    string? BaseUrl,
    string? ApiKey = null,
    string AuthHeaderName = "Authorization",
    string? AuthScheme = "Bearer",
    IReadOnlyDictionary<string, string>? ExtraHeaders = null);

/// <summary>
/// The OpenAI-compatible Chat Completions shape (<c>POST {baseUrl}/chat/completions</c>, SSE). One adapter for
/// every provider and local runner that speaks it — the BYO base URL is the whole point. (#578)
///
/// <para><b>Reasoning models need care here.</b> Providers serving reasoning models over this shape stream the
/// reasoning in one of two ways: a separate <c>reasoning_content</c> delta field, or inline
/// <c>&lt;think&gt;</c> tags inside ordinary <c>content</c>. Both are segregated onto
/// <see cref="ChatDeltaKind.Reasoning"/>; concatenating deltas naively would render chain-of-thought at the
/// user as though it were the answer.</para>
/// </summary>
public sealed class OpenAiCompatibleProvider : IChatProvider
{
    private readonly HttpClient _http;
    private readonly OpenAiCompatibleOptions _options;
    private readonly ILogger<OpenAiCompatibleProvider> _logger;
    private readonly TimeSpan _idleTimeout;
    private readonly TimeSpan _firstEventTimeout;

    public OpenAiCompatibleProvider(
        HttpClient http,
        OpenAiCompatibleOptions options,
        ILogger<OpenAiCompatibleProvider> logger,
        TimeSpan? idleTimeout = null,
        TimeSpan? firstEventTimeout = null)
    {
        _http = AiHttp.EnsureStreamable(http);
        _options = options;
        _logger = logger;
        _idleTimeout = idleTimeout ?? SseReader.DefaultIdleTimeout;
        _firstEventTimeout = firstEventTimeout ?? SseReader.DefaultFirstEventTimeout;
    }

    public string Id => "openai-compatible";

    /// <summary>
    /// Attaches the credential in whatever shape this endpoint expects, plus any extra headers. (#689)
    ///
    /// <para><b>Naming a non-standard auth header replaces <c>Authorization</c>; it does not add a second
    /// one.</b> Azure rejects a request carrying both, which is why this cannot be expressed as an entry in
    /// <see cref="OpenAiCompatibleOptions.ExtraHeaders"/> — an extra header is additive by definition, and the
    /// requirement here is an absence.</para>
    ///
    /// <para>Extra headers are applied first so they can never overwrite the credential: a mis-typed header
    /// named <c>Authorization</c> in settings would otherwise silently replace the real key with whatever the
    /// reader pasted.</para>
    /// </summary>
    private void ApplyAuth(HttpRequestMessage message) => AiHttp.ApplyAuth(
        message, _options.ApiKey, _options.AuthHeaderName, _options.AuthScheme, _options.ExtraHeaders);

    public async IAsyncEnumerable<ChatDelta> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.BaseUrl))
            throw new AiException(new AiError(AiErrorKind.NotConfigured, "No endpoint URL is configured."));
        if (string.IsNullOrWhiteSpace(request.Model))
            throw new AiException(new AiError(AiErrorKind.NotConfigured, "No model is configured."));

        var endpoint = AiHttp.ResolveEndpoint(
            _options.BaseUrl!, versionedPath: "v1/chat/completions", path: "chat/completions",
            convention: BaseUrlConvention.IncludesVersion);

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(BuildBody(request), Encoding.UTF8, "application/json"),
        };
        ApplyAuth(message);

        HttpResponseMessage response;
        try
        {
            response = await _http
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AiException(
                new AiError(AiErrorKind.Network, "Could not reach the endpoint. Check the URL and your network."),
                ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new AiException(await ClassifyAsync(response, ct).ConfigureAwait(false));

            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            var think = new ThinkTagFilter();
            var finish = new FinishState();
            var produced = false;

            await foreach (var sse in SseReader.ReadAsync(stream, _idleTimeout, _firstEventTimeout, ct)
                               .ConfigureAwait(false))
            {
                if (sse.Failure is { } failure)
                {
                    _logger.LogWarning("OpenAI-compatible stream ended early: {Kind}", failure.Kind);
                    foreach (var tail in FlushThink(think)) yield return tail;
                    yield return ChatDelta.ForError(failure);
                    yield break;
                }

                produced = true;

                if (sse.Data == "[DONE]")
                    break;

                foreach (var delta in Interpret(sse, think, finish))
                {
                    // An Error delta is terminal by contract (see ChatDeltaKind.Error) — a gateway that keeps
                    // streaming after reporting a failure must not have that content appended to the answer.
                    // Held-back think-tag text is flushed FIRST so it cannot arrive after the error.
                    if (delta.Kind == ChatDeltaKind.Error)
                    {
                        foreach (var tail in FlushThink(think)) yield return tail;
                        yield return delta;
                        yield break;
                    }

                    yield return delta;
                }
            }

            foreach (var tail in FlushThink(think)) yield return tail;

            // A 200 that carried no events at all is not success. The usual cause is an endpoint that is not the
            // API — a proxy or portal answering 200 with HTML — and reporting nothing would leave the user with
            // a blank answer and no way to tell a misconfiguration from a model that declined to speak.
            if (!produced)
            {
                _logger.LogWarning("OpenAI-compatible endpoint returned a 200 with no stream events");
                yield return ChatDelta.ForError(AiHttp.EmptyResponse());
            }
            else if (finish.Truncated)
            {
                // Deliberately AFTER the loop rather than at the chunk that reported it. The finish-reason chunk
                // is not the last one: with `include_usage` the token counts arrive in a further chunk behind it,
                // and an Error delta is terminal by contract — emitting it on the spot would discard exactly the
                // number the user needs, which is what this turn cost them for a half-written answer.
                _logger.LogInformation("OpenAI-compatible turn ended at the output limit ({Reason})", finish.Reason);
                yield return ChatDelta.ForError(AiHttp.Truncated(finish.Reason!));
            }
        }
    }

    /// <summary>
    /// How the turn ended, carried out of <see cref="Interpret"/>. Mutable because the finish reason arrives
    /// mid-stream and is acted on after it.
    /// </summary>
    private sealed class FinishState
    {
        internal bool Truncated;
        internal string? Reason;
    }

    private static IEnumerable<ChatDelta> FlushThink(ThinkTagFilter think)
    {
        var (visible, reasoning) = think.Flush();
        if (reasoning.Length > 0) yield return ChatDelta.ForReasoning(reasoning);
        if (visible.Length > 0) yield return ChatDelta.ForText(visible);
    }

    private static string BuildBody(ChatRequest request)
    {
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("model", request.Model);
            // Optional here, unlike Anthropic — omitting it is the honest expression of "no cap", and it lets
            // a reasoning model spend what it needs on reasoning without starving the answer (#601).
            if (request.MaxTokens is { } maxTokens) json.WriteNumber("max_tokens", maxTokens);
            json.WriteBoolean("stream", true);

            // Ask for usage on the final chunk. Providers that do not know the option ignore it.
            json.WriteStartObject("stream_options");
            json.WriteBoolean("include_usage", true);
            json.WriteEndObject();

            json.WriteStartArray("messages");
            if (!string.IsNullOrEmpty(request.System))
            {
                json.WriteStartObject();
                json.WriteString("role", "system");
                json.WriteString("content", request.System);
                json.WriteEndObject();
            }
            foreach (var turn in request.Messages)
            {
                json.WriteStartObject();
                json.WriteString("role", turn.Role == ChatRole.Assistant ? "assistant" : "user");
                json.WriteString("content", turn.Content);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>
    /// Translate one SSE chunk into zero or more deltas. Every property read goes through <see cref="AiJson"/>:
    /// a provider payload is untrusted input, and a raw <c>GetString()</c> on an unexpected kind throws out of
    /// this iterator as an unclassified crash mid-answer. OpenRouter reporting a NUMERIC <c>error.code</c> is a
    /// real instance of exactly that.
    /// </summary>
    private static IEnumerable<ChatDelta> Interpret(SseEvent sse, ThinkTagFilter think, FinishState finish)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(sse.Data);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            yield break;   // one bad chunk does not end an otherwise healthy stream
        }

        if (!AiJson.IsObject(root))
            yield break;

        // Some providers report an error as a normal 200 chunk rather than an HTTP status.
        if (AiJson.Object(root, "error") is { } error)
        {
            yield return ChatDelta.ForError(new AiError(
                AiErrorKind.Provider,
                "The model stopped part-way through: the provider reported an error.",
                ProviderCode: AiHttp.SanitizeProviderCode(
                    AiJson.Code(error, "code") ?? AiJson.Code(error, "type"))));
            yield break;
        }

        // Not an early return: the final chunk carries `choices: []` alongside the usage read at the end of this
        // method, so bailing out here would drop the token counts.
        if (AiJson.Array(root, "choices") is { } choices && choices.GetArrayLength() > 0)
        {
            var choice = choices[0];

            // finish_reason is a sibling of `delta`, not a member of it, and is null on every chunk but the last.
            // Read outside the delta check because a provider may report it on a chunk carrying no delta at all —
            // and inside the same chunk it can accompany the final content, which must still be yielded. (#601)
            if (TruncationReason(AiJson.String(choice, "finish_reason")) is { } stopped)
            {
                finish.Truncated = true;
                finish.Reason = stopped;
            }

            if (AiJson.Object(choice, "delta") is { } delta)
            {
                // Structured reasoning — never merged into the answer. The field name is NOT standardised:
                // DeepSeek documents `reasoning_content`, while Ollama's OpenAI-compat surface (and OpenRouter)
                // use plain `reasoning`. Verified against a live Ollama serving gpt-oss: 73 of its deltas
                // carried `reasoning` and none carried `reasoning_content`, so parsing only the documented name
                // dropped the model's entire reasoning stream — leaving usage that says 80 output tokens next to
                // a one-line answer.
                if (AiJson.String(delta, "reasoning_content") is { Length: > 0 } reasoningContent)
                    yield return ChatDelta.ForReasoning(reasoningContent);
                else if (AiJson.String(delta, "reasoning") is { Length: > 0 } reasoning)
                    yield return ChatDelta.ForReasoning(reasoning);

                if (AiJson.String(delta, "content") is { Length: > 0 } content)
                {
                    // Inline <think> tags are the other reasoning convention; strip across chunk boundaries.
                    var (visible, inlineReasoning) = think.Feed(content);
                    if (inlineReasoning.Length > 0) yield return ChatDelta.ForReasoning(inlineReasoning);
                    if (visible.Length > 0) yield return ChatDelta.ForText(visible);
                }
            }
        }

        if (AiJson.Object(root, "usage") is { } usage)
        {
            yield return ChatDelta.ForUsage(new ChatUsage(
                InputTokens: AiJson.Int(usage, "prompt_tokens"),
                OutputTokens: AiJson.Int(usage, "completion_tokens")));
        }
    }

    /// <summary>
    /// The canonical finish reason when the model stopped at its output limit, or null for every other ending.
    ///
    /// <para>OpenAI spells it <c>length</c>; gateways fronting Anthropic pass its <c>max_tokens</c> through
    /// unchanged, and both can only mean the one thing. Every other value — <c>stop</c>, <c>tool_calls</c>,
    /// <c>content_filter</c> — is not this failure and must not be reported as one.</para>
    ///
    /// <para>The MATCHED CONSTANT is returned rather than the provider's own string, which is what makes it safe
    /// to put in <see cref="AiError.ProviderCode"/> and thence into the log: the field is provider-controlled on
    /// a user-pasted endpoint, and this narrows it to one of two literals we wrote.</para>
    /// </summary>
    private static string? TruncationReason(string? finishReason) =>
        string.Equals(finishReason, "length", StringComparison.OrdinalIgnoreCase) ? "length"
        : string.Equals(finishReason, "max_tokens", StringComparison.OrdinalIgnoreCase) ? "max_tokens"
        : null;

    /// <summary>
    /// Classify a non-2xx response. As with the Anthropic adapter the body is read for classification only, and
    /// bounded: <c>error.code</c> is a short token and is kept (after sanitizing), while <c>error.message</c> can
    /// quote the request back and is discarded.
    /// </summary>
    private async Task<AiError> ClassifyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var kind = AiHttp.KindFor(response.StatusCode);
        string? code = null;

        var body = await AiHttp
            .ReadBoundedBodyAsync(response.Content, TimeSpan.FromSeconds(10), ct)
            .ConfigureAwait(false);

        if (body.Length > 0)
        {
            try
            {
                using var document = JsonDocument.Parse(body);
                if (AiJson.Object(document.RootElement, "error") is { } error)
                {
                    code = AiHttp.SanitizeProviderCode(
                        AiJson.Code(error, "code") ?? AiJson.Code(error, "type"));

                    if (string.Equals(code, "context_length_exceeded", StringComparison.OrdinalIgnoreCase))
                        kind = AiErrorKind.ContextTooLong;
                }
            }
            catch (JsonException)
            {
            }
        }

        _logger.LogWarning(
            "OpenAI-compatible request failed: HTTP {Status} {Code}", (int)response.StatusCode, code ?? "(no code)");

        // The body, at Debug, because "(no code)" is otherwise a dead end: a provider that puts its reason in
        // prose rather than in a machine-readable field leaves the log saying only that something failed.
        // Already bounded by ReadBoundedBodyAsync, and it is the provider's own error text - a request body
        // or a credential never reaches here.
        if (body.Length > 0) _logger.LogDebug("OpenAI-compatible error body: {Body}", body);

        // Read once and used twice: the sentence the reader sees and the delay any retry honours must agree,
        // and computing them from separate reads of the same headers is how they drift.
        var wait = AiHttp.RateLimitWait(response);

        return new AiError(
            kind,
            AiHttp.MessageFor(kind, response.StatusCode, wait),
            StatusCode: (int)response.StatusCode,
            ProviderCode: code,
            RetryAfter: wait);
    }
}
