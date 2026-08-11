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
public sealed record OpenAiCompatibleOptions(string? BaseUrl, string? ApiKey = null);

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
        if (!string.IsNullOrWhiteSpace(_options.ApiKey))
            message.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _options.ApiKey);

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

                foreach (var delta in Interpret(sse, think))
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
        }
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
    private static IEnumerable<ChatDelta> Interpret(SseEvent sse, ThinkTagFilter think)
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

        if (AiJson.Array(root, "choices") is { } choices &&
            choices.GetArrayLength() > 0 &&
            AiJson.Object(choices[0], "delta") is { } delta)
        {
            // Structured reasoning — never merged into the answer. The field name is NOT standardised: DeepSeek
            // documents `reasoning_content`, while Ollama's OpenAI-compat surface (and OpenRouter) use plain
            // `reasoning`. Verified against a live Ollama serving gpt-oss: 73 of its deltas carried `reasoning`
            // and none carried `reasoning_content`, so parsing only the documented name dropped the model's
            // entire reasoning stream — leaving usage that says 80 output tokens next to a one-line answer.
            if (AiJson.String(delta, "reasoning_content") is { Length: > 0 } reasoningContent)
                yield return ChatDelta.ForReasoning(reasoningContent);
            else if (AiJson.String(delta, "reasoning") is { Length: > 0 } reasoning)
                yield return ChatDelta.ForReasoning(reasoning);

            if (AiJson.String(delta, "content") is { Length: > 0 } content)
            {
                // Inline <think> tags are the other reasoning convention; strip them across chunk boundaries.
                var (visible, inlineReasoning) = think.Feed(content);
                if (inlineReasoning.Length > 0) yield return ChatDelta.ForReasoning(inlineReasoning);
                if (visible.Length > 0) yield return ChatDelta.ForText(visible);
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

        return new AiError(
            kind,
            AiHttp.MessageFor(kind, response.StatusCode),
            StatusCode: (int)response.StatusCode,
            ProviderCode: code,
            RetryAfter: AiHttp.RetryAfter(response));
    }
}
