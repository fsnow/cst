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

    public OpenAiCompatibleProvider(
        HttpClient http,
        OpenAiCompatibleOptions options,
        ILogger<OpenAiCompatibleProvider> logger,
        TimeSpan? idleTimeout = null)
    {
        _http = http;
        _options = options;
        _logger = logger;
        _idleTimeout = idleTimeout ?? SseReader.DefaultIdleTimeout;
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
            _options.BaseUrl!, versionedPath: "v1/chat/completions", path: "chat/completions");

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

            await foreach (var sse in SseReader.ReadAsync(stream, _idleTimeout, ct).ConfigureAwait(false))
            {
                if (sse.Failure is { } failure)
                {
                    _logger.LogWarning("OpenAI-compatible stream ended early: {Kind}", failure.Kind);
                    foreach (var tail in FlushThink(think)) yield return tail;
                    yield return ChatDelta.ForError(failure);
                    yield break;
                }

                if (sse.Data == "[DONE]")
                    break;

                foreach (var delta in Interpret(sse, think))
                    yield return delta;
            }

            foreach (var tail in FlushThink(think)) yield return tail;
        }
    }

    private static IEnumerable<ChatDelta> FlushThink(ThinkTagFilter think)
    {
        var (visible, reasoning) = think.Flush();
        if (visible.Length > 0) yield return ChatDelta.ForText(visible);
        if (reasoning.Length > 0) yield return ChatDelta.ForReasoning(reasoning);
    }

    private static string BuildBody(ChatRequest request)
    {
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("model", request.Model);
            json.WriteNumber("max_tokens", request.MaxTokens);
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

        // Some providers report an error as a normal 200 chunk rather than an HTTP status.
        if (root.TryGetProperty("error", out var error) && error.ValueKind == JsonValueKind.Object)
        {
            var code = error.TryGetProperty("code", out var codeElement) ? codeElement.GetString() : null;
            code ??= error.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;
            yield return ChatDelta.ForError(new AiError(
                AiErrorKind.Provider,
                "The model stopped part-way through: the provider reported an error.",
                ProviderCode: code));
            yield break;
        }

        if (root.TryGetProperty("choices", out var choices) &&
            choices.ValueKind == JsonValueKind.Array &&
            choices.GetArrayLength() > 0 &&
            choices[0].TryGetProperty("delta", out var delta))
        {
            // Structured reasoning (DeepSeek and others) — never merged into the answer.
            if (delta.TryGetProperty("reasoning_content", out var reasoning) &&
                reasoning.ValueKind == JsonValueKind.String)
            {
                var value = reasoning.GetString();
                if (!string.IsNullOrEmpty(value)) yield return ChatDelta.ForReasoning(value);
            }

            if (delta.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.String)
            {
                var value = content.GetString();
                if (!string.IsNullOrEmpty(value))
                {
                    // Inline <think> tags are the other reasoning convention; strip them across chunk boundaries.
                    var (visible, inlineReasoning) = think.Feed(value);
                    if (inlineReasoning.Length > 0) yield return ChatDelta.ForReasoning(inlineReasoning);
                    if (visible.Length > 0) yield return ChatDelta.ForText(visible);
                }
            }
        }

        if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object)
        {
            yield return ChatDelta.ForUsage(new ChatUsage(
                InputTokens: ReadInt(usage, "prompt_tokens"),
                OutputTokens: ReadInt(usage, "completion_tokens")));
        }
    }

    private static int? ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    /// <summary>
    /// Classify a non-2xx response. As with the Anthropic adapter the body is read for classification only:
    /// <c>error.code</c> is a bounded token and is kept, while <c>error.message</c> can quote the request back
    /// and is discarded.
    /// </summary>
    private async Task<AiError> ClassifyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var kind = AiHttp.KindFor(response.StatusCode);
        string? code = null;

        try
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error) &&
                error.ValueKind == JsonValueKind.Object)
            {
                code = error.TryGetProperty("code", out var codeElement) && codeElement.ValueKind == JsonValueKind.String
                    ? codeElement.GetString()
                    : null;
                code ??= error.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : null;

                if (string.Equals(code, "context_length_exceeded", StringComparison.OrdinalIgnoreCase))
                    kind = AiErrorKind.ContextTooLong;
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or HttpRequestException or IOException)
        {
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
