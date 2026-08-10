using System;
using System.Collections.Generic;
using System.IO;
using System.Net.Http;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services.Ai;

/// <summary>Configuration for <see cref="AnthropicMessagesProvider"/>.</summary>
/// <param name="ApiKey">Required — Anthropic has no anonymous access.</param>
/// <param name="BaseUrl">Override for a proxy or gateway; defaults to the public API.</param>
public sealed record AnthropicOptions(string? ApiKey, string? BaseUrl = null)
{
    internal const string DefaultBaseUrl = "https://api.anthropic.com";
}

/// <summary>
/// The Anthropic Messages API (<c>POST /v1/messages</c>, SSE). Claude-first is the standing default for
/// surface B — see AI_INTEGRATION.md §11.1. (#578)
///
/// <para><b>Two request-shape rules that are easy to get wrong.</b> <c>max_tokens</c> is REQUIRED — omitting it
/// is a 400, not a default. And current Claude models REJECT <c>temperature</c>/<c>top_p</c>/<c>top_k</c> with a
/// 400, so this adapter deliberately has no sampling knobs to send and the Settings UI must not offer any.</para>
/// </summary>
public sealed class AnthropicMessagesProvider : IChatProvider
{
    private const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _http;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicMessagesProvider> _logger;
    private readonly TimeSpan _idleTimeout;

    public AnthropicMessagesProvider(
        HttpClient http,
        AnthropicOptions options,
        ILogger<AnthropicMessagesProvider> logger,
        TimeSpan? idleTimeout = null)
    {
        _http = http;
        _options = options;
        _logger = logger;
        _idleTimeout = idleTimeout ?? SseReader.DefaultIdleTimeout;
    }

    public string Id => "anthropic";

    public async IAsyncEnumerable<ChatDelta> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        if (string.IsNullOrWhiteSpace(_options.ApiKey))
            throw new AiException(new AiError(AiErrorKind.NotConfigured, "No Anthropic API key is configured."));
        if (string.IsNullOrWhiteSpace(request.Model))
            throw new AiException(new AiError(AiErrorKind.NotConfigured, "No model is configured."));

        var endpoint = AiHttp.ResolveEndpoint(
            string.IsNullOrWhiteSpace(_options.BaseUrl) ? AnthropicOptions.DefaultBaseUrl : _options.BaseUrl!,
            versionedPath: "v1/messages",
            path: "messages");

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(BuildBody(request), Encoding.UTF8, "application/json"),
        };
        message.Headers.TryAddWithoutValidation("x-api-key", _options.ApiKey);
        message.Headers.TryAddWithoutValidation("anthropic-version", AnthropicVersion);

        HttpResponseMessage response;
        try
        {
            response = await _http
                .SendAsync(message, HttpCompletionOption.ResponseHeadersRead, ct)
                .ConfigureAwait(false);
        }
        catch (HttpRequestException ex)
        {
            throw new AiException(new AiError(
                AiErrorKind.Network, "Could not reach the Anthropic API. Check your network connection."), ex);
        }

        using (response)
        {
            if (!response.IsSuccessStatusCode)
                throw new AiException(await ClassifyAsync(response, ct).ConfigureAwait(false));

            var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);

            await foreach (var sse in SseReader.ReadAsync(stream, _idleTimeout, ct).ConfigureAwait(false))
            {
                if (sse.Failure is { } failure)
                {
                    _logger.LogWarning("Anthropic stream ended early: {Kind}", failure.Kind);
                    yield return ChatDelta.ForError(failure);
                    yield break;
                }

                foreach (var delta in Interpret(sse))
                {
                    yield return delta;
                    if (delta.Kind == ChatDeltaKind.Error)
                        yield break;
                }
            }
        }
    }

    private static string BuildBody(ChatRequest request)
    {
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("model", request.Model);
            json.WriteNumber("max_tokens", request.MaxTokens);   // required by the API
            json.WriteBoolean("stream", true);

            if (!string.IsNullOrEmpty(request.System))
                json.WriteString("system", request.System);

            json.WriteStartArray("messages");
            foreach (var turn in request.Messages)
            {
                json.WriteStartObject();
                json.WriteString("role", turn.Role == ChatRole.Assistant ? "assistant" : "user");
                json.WriteString("content", turn.Content);
                json.WriteEndObject();
            }
            json.WriteEndArray();

            // No temperature / top_p / top_k: current Claude models reject them with a 400.
            json.WriteEndObject();
        }

        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    /// <summary>Translate one SSE event into zero or more deltas.</summary>
    private static IEnumerable<ChatDelta> Interpret(SseEvent sse)
    {
        JsonElement root;
        try
        {
            using var document = JsonDocument.Parse(sse.Data);
            root = document.RootElement.Clone();
        }
        catch (JsonException)
        {
            // A single malformed event is not worth abandoning a good stream over; the terminator or the next
            // event will carry us forward. (A truncated FINAL event just ends the stream.)
            yield break;
        }

        var type = root.TryGetProperty("type", out var typeElement) ? typeElement.GetString() : sse.Name;

        switch (type)
        {
            case "content_block_delta":
                if (root.TryGetProperty("delta", out var delta))
                {
                    var deltaType = delta.TryGetProperty("type", out var dt) ? dt.GetString() : null;
                    if (deltaType == "text_delta" && delta.TryGetProperty("text", out var text))
                    {
                        var value = text.GetString();
                        if (!string.IsNullOrEmpty(value)) yield return ChatDelta.ForText(value);
                    }
                    else if (deltaType == "thinking_delta" && delta.TryGetProperty("thinking", out var thinking))
                    {
                        var value = thinking.GetString();
                        if (!string.IsNullOrEmpty(value)) yield return ChatDelta.ForReasoning(value);
                    }
                }
                break;

            case "message_start":
                if (root.TryGetProperty("message", out var start) &&
                    start.TryGetProperty("usage", out var startUsage))
                {
                    yield return ChatDelta.ForUsage(new ChatUsage(
                        InputTokens: ReadInt(startUsage, "input_tokens"),
                        OutputTokens: ReadInt(startUsage, "output_tokens")));
                }
                break;

            case "message_delta":
                if (root.TryGetProperty("usage", out var endUsage))
                {
                    yield return ChatDelta.ForUsage(new ChatUsage(
                        InputTokens: ReadInt(endUsage, "input_tokens"),
                        OutputTokens: ReadInt(endUsage, "output_tokens")));
                }
                break;

            case "error":
                // The API can report a failure mid-stream (overloaded, and other transient states). The caller
                // already has partial text on screen, so this is an Error delta rather than an exception.
                var code = root.TryGetProperty("error", out var error) &&
                           error.TryGetProperty("type", out var errorType)
                    ? errorType.GetString()
                    : null;
                yield return ChatDelta.ForError(new AiError(
                    AiErrorKind.Provider,
                    "The model stopped part-way through: the provider reported an error.",
                    ProviderCode: code));
                break;

            // message_stop / content_block_start / content_block_stop / ping carry nothing we need.
        }
    }

    private static int? ReadInt(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.TryGetInt32(out var number) ? number : null;

    /// <summary>
    /// Classify a non-2xx response. The body IS read — it is the only way to tell a context-window overflow from
    /// any other 400 — but only the short <c>error.type</c> token escapes into the log or the returned error. The
    /// prose in <c>error.message</c> commonly quotes the offending request back, which here means corpus text and
    /// the user's own question, so it is used for classification and then dropped.
    /// </summary>
    private async Task<AiError> ClassifyAsync(HttpResponseMessage response, CancellationToken ct)
    {
        var kind = AiHttp.KindFor(response.StatusCode);
        string? code = null;

        try
        {
            var body = await response.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
            using var document = JsonDocument.Parse(body);
            if (document.RootElement.TryGetProperty("error", out var error))
            {
                code = error.TryGetProperty("type", out var type) ? type.GetString() : null;

                if (kind == AiErrorKind.Provider &&
                    (int)response.StatusCode == 400 &&
                    error.TryGetProperty("message", out var messageElement) &&
                    LooksLikeContextOverflow(messageElement.GetString()))
                {
                    kind = AiErrorKind.ContextTooLong;
                }
            }
        }
        catch (Exception ex) when (ex is JsonException or InvalidOperationException or HttpRequestException or IOException)
        {
            // An unparseable body tells us nothing extra; the status code already classified it.
        }

        _logger.LogWarning(
            "Anthropic request failed: HTTP {Status} {Code}", (int)response.StatusCode, code ?? "(no code)");

        return new AiError(
            kind,
            AiHttp.MessageFor(kind, response.StatusCode),
            StatusCode: (int)response.StatusCode,
            ProviderCode: code,
            RetryAfter: AiHttp.RetryAfter(response));
    }

    /// <summary>
    /// Anthropic has no machine-readable code for a context overflow — it is an <c>invalid_request_error</c>
    /// like any other — so the prose is the only signal available. Matched, never retained.
    /// </summary>
    private static bool LooksLikeContextOverflow(string? message) =>
        message is not null &&
        (message.Contains("prompt is too long", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("exceed the context", StringComparison.OrdinalIgnoreCase) ||
         message.Contains("context window", StringComparison.OrdinalIgnoreCase));
}
