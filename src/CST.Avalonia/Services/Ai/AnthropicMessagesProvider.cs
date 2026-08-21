using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models.Ai;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services.Ai;

/// <summary>Configuration for <see cref="AnthropicMessagesProvider"/>.</summary>
/// <param name="ApiKey">Required — Anthropic has no anonymous access.</param>
/// <param name="BaseUrl">Override for a proxy or gateway; defaults to the public API.</param>
/// <param name="ExtraHeaders">
/// The connection's extra headers. Not a nicety: <c>Kind = Anthropic</c> does not imply
/// <c>api.anthropic.com</c> — several gateways speak the Messages protocol at their own URL and want a
/// token of their own alongside the Anthropic key. That is the case this adapter previously could not
/// serve, because it had nowhere to put a second header. (#711, #689)
/// </param>
public sealed record AnthropicOptions(
    string? ApiKey,
    string? BaseUrl = null,
    IReadOnlyDictionary<string, string>? ExtraHeaders = null)
{
    internal const string DefaultBaseUrl = "https://api.anthropic.com";

    /// <summary>Where the credential goes. Anthropic uses a bare header, not a scheme-prefixed one.</summary>
    internal const string AuthHeader = "x-api-key";

    /// <summary>The protocol version header, which an extra header may deliberately replace.</summary>
    internal const string VersionHeader = "anthropic-version";
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
    internal const string AnthropicVersion = "2023-06-01";

    private readonly HttpClient _http;
    private readonly AnthropicOptions _options;
    private readonly ILogger<AnthropicMessagesProvider> _logger;
    private readonly TimeSpan _idleTimeout;
    private readonly TimeSpan _firstEventTimeout;

    public AnthropicMessagesProvider(
        HttpClient http,
        AnthropicOptions options,
        ILogger<AnthropicMessagesProvider> logger,
        TimeSpan? idleTimeout = null,
        TimeSpan? firstEventTimeout = null)
    {
        _http = AiHttp.EnsureStreamable(http);
        _options = options;
        _logger = logger;
        _idleTimeout = idleTimeout ?? SseReader.DefaultIdleTimeout;
        _firstEventTimeout = firstEventTimeout ?? SseReader.DefaultFirstEventTimeout;
    }

    public string Id => "anthropic";

    /// <summary>
    /// Whether anything on this connection could authenticate a request. (#711)
    ///
    /// <para><b>A header only counts if it will actually be sent and carries something.</b> The first version
    /// of this counted any non-blank header NAME, which let a cosmetic <c>X-Title</c>, an empty value, or a
    /// name HTTP rejects outright stand in for a credential — so a connection with no key sailed past the
    /// guard, sent nothing that could authenticate, and got back a 401 reported as "the provider rejected the
    /// API key" when there was no key to reject. That is a worse outcome than the refusal it replaced, which
    /// named the actual fix.</para>
    ///
    /// <para>This is still a proxy — we cannot know which header a gateway treats as its credential, and
    /// guessing would be worse. It is only a proxy that cannot be satisfied by nothing.</para>
    /// </summary>
    internal static bool HasCredential(AnthropicOptions options) =>
        !string.IsNullOrWhiteSpace(options.ApiKey)
        || (options.ExtraHeaders?.Any(IsSendableHeader) ?? false);

    /// <summary>
    /// A header that will survive to the wire with content in it: a name that is a legal HTTP token, a value
    /// that is not blank, and no unresolved <c>{placeholder}</c> left in either. A templated name is never
    /// expanded, so a braced name is unfinished by construction.
    /// </summary>
    private static bool IsSendableHeader(KeyValuePair<string, string> header) =>
        !string.IsNullOrWhiteSpace(header.Key)
        && !string.IsNullOrWhiteSpace(header.Value)
        && HeaderNameToken.IsMatch(header.Key)
        && !AiTemplate.HasUnresolvedPlaceholders(header.Value);

    /// <summary>RFC 9110 field-name: one or more token characters, and nothing else.</summary>
    private static readonly Regex HeaderNameToken =
        new(@"^[!#$%&'*+\-.^_`|~0-9A-Za-z]+\z", RegexOptions.Compiled);

    /// <summary>
    /// Credential, connection headers, and the protocol version — through the same helper the
    /// OpenAI-compatible adapter uses, so the two cannot drift on what an extra header is allowed to do.
    /// (#711)
    ///
    /// <para><c>anthropic-version</c> is added only when the connection did not supply one. The reason is
    /// mechanical rather than a policy about who should win: <see cref="HttpHeaders.TryAddWithoutValidation"/>
    /// appends, so unconditionally adding ours to a request that already carries one would send
    /// <c>anthropic-version: 2026-01-01, 2023-06-01</c> — two values, which is worse than either. A gateway
    /// that pins its own version gets it; everyone else gets ours.</para>
    /// </summary>
    private void ApplyHeaders(HttpRequestMessage message)
    {
        AiHttp.ApplyAuth(
            message,
            _options.ApiKey,
            AnthropicOptions.AuthHeader,
            authScheme: null,
            _options.ExtraHeaders);

        if (!message.Headers.Contains(AnthropicOptions.VersionHeader))
            message.Headers.TryAddWithoutValidation(AnthropicOptions.VersionHeader, AnthropicVersion);
    }

    public async IAsyncEnumerable<ChatDelta> StreamAsync(
        ChatRequest request,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        // A key OR a header, not a key unconditionally. Requiring the key made the escape hatch inert on this
        // adapter: a gateway holding its own Anthropic credential wants a token of ours and no key at all,
        // and refusing that is refusing the case extra headers exist for. (#711, #689)
        if (!HasCredential(_options))
            throw new AiException(new AiError(
                AiErrorKind.NotConfigured,
                "No Anthropic API key is configured, and this connection sends no headers that could "
                + "authenticate in its place."));
        if (string.IsNullOrWhiteSpace(request.Model))
            throw new AiException(new AiError(AiErrorKind.NotConfigured, "No model is configured."));

        var endpoint = AiHttp.ResolveEndpoint(
            string.IsNullOrWhiteSpace(_options.BaseUrl) ? AnthropicOptions.DefaultBaseUrl : _options.BaseUrl!,
            versionedPath: "v1/messages",
            path: "messages",
            convention: BaseUrlConvention.ExcludesVersion);

        using var message = new HttpRequestMessage(HttpMethod.Post, endpoint)
        {
            Content = new StringContent(BuildBody(request), Encoding.UTF8, "application/json"),
        };
        ApplyHeaders(message);

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
            var finish = new FinishState();
            var produced = false;

            await foreach (var sse in SseReader.ReadAsync(stream, _idleTimeout, _firstEventTimeout, ct)
                               .ConfigureAwait(false))
            {
                if (sse.Failure is { } failure)
                {
                    _logger.LogWarning("Anthropic stream ended early: {Kind}", failure.Kind);
                    yield return ChatDelta.ForError(failure);
                    yield break;
                }

                produced = true;

                foreach (var delta in Interpret(sse, finish))
                {
                    yield return delta;
                    if (delta.Kind == ChatDeltaKind.Error)
                        yield break;
                }
            }

            // A 200 that carried no events at all is not success. The usual cause is an endpoint that is not the
            // API — a proxy or portal answering 200 with HTML — and reporting nothing would leave the user with
            // a blank answer and no way to tell a misconfiguration from a model that declined to speak.
            if (!produced)
            {
                _logger.LogWarning("Anthropic returned a 200 with no stream events");
                yield return ChatDelta.ForError(AiHttp.EmptyResponse());
            }
            else if (finish.Truncated)
            {
                // Emitted after the loop, not from the `message_delta` that reported it. That event carries the
                // final output-token count in the same payload, and an Error delta is terminal by contract — so
                // reporting on the spot would cost the user the one number that explains the truncation.
                _logger.LogInformation("Anthropic turn ended at max_tokens");
                yield return ChatDelta.ForError(AiHttp.Truncated("max_tokens"));
            }
        }
    }

    /// <summary>
    /// How the turn ended, carried out of <see cref="Interpret"/>. Mutable because <c>stop_reason</c> arrives
    /// mid-stream and is acted on after it.
    /// </summary>
    private sealed class FinishState
    {
        internal bool Truncated;
    }

    private static string BuildBody(ChatRequest request)
    {
        var buffer = new MemoryStream();
        using (var json = new Utf8JsonWriter(buffer))
        {
            json.WriteStartObject();
            json.WriteString("model", request.Model);
            // Required by the API, so a null request cap becomes the universal ceiling rather than an omission.
            json.WriteNumber("max_tokens", request.MaxTokens ?? AiLimits.UniversalMaxTokens);
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

    /// <summary>
    /// Translate one SSE event into zero or more deltas. Every property read goes through <see cref="AiJson"/>:
    /// a provider payload is untrusted input, and a raw <c>GetString()</c> on an unexpected kind throws out of
    /// this iterator as an unclassified crash mid-answer.
    /// </summary>
    private static IEnumerable<ChatDelta> Interpret(SseEvent sse, FinishState finish)
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

        if (!AiJson.IsObject(root))
            yield break;   // e.g. a bare `data: null` keep-alive — parses fine, has no properties

        var type = AiJson.String(root, "type") ?? sse.Name;

        switch (type)
        {
            case "content_block_delta":
                if (AiJson.Object(root, "delta") is { } delta)
                {
                    switch (AiJson.String(delta, "type"))
                    {
                        case "text_delta" when AiJson.String(delta, "text") is { Length: > 0 } text:
                            yield return ChatDelta.ForText(text);
                            break;
                        case "thinking_delta" when AiJson.String(delta, "thinking") is { Length: > 0 } thinking:
                            yield return ChatDelta.ForReasoning(thinking);
                            break;
                    }
                }
                break;

            case "message_start":
                if (AiJson.Object(root, "message") is { } start && AiJson.Object(start, "usage") is { } startUsage)
                    yield return UsageDelta(startUsage);
                break;

            case "message_delta":
                // `stop_reason` and the final output-token count arrive together in this one event. Recorded
                // rather than reported here — see the emission site in StreamAsync. (#601)
                if (AiJson.Object(root, "delta") is { } end &&
                    string.Equals(AiJson.String(end, "stop_reason"), "max_tokens", StringComparison.Ordinal))
                {
                    finish.Truncated = true;
                }

                if (AiJson.Object(root, "usage") is { } endUsage)
                    yield return UsageDelta(endUsage);
                break;

            case "error":
                // The API can report a failure mid-stream (overloaded, and other transient states). The caller
                // already has partial text on screen, so this is an Error delta rather than an exception.
                yield return ChatDelta.ForError(new AiError(
                    AiErrorKind.Provider,
                    "The model stopped part-way through: the provider reported an error.",
                    ProviderCode: AiHttp.SanitizeProviderCode(
                        AiJson.Object(root, "error") is { } error ? AiJson.Code(error, "type") : null)));
                break;

            // message_stop / content_block_start / content_block_stop / ping carry nothing we need.
        }
    }

    private static ChatDelta UsageDelta(JsonElement usage) => ChatDelta.ForUsage(new ChatUsage(
        InputTokens: AiJson.Int(usage, "input_tokens"),
        OutputTokens: AiJson.Int(usage, "output_tokens")));

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
                    code = AiHttp.SanitizeProviderCode(AiJson.Code(error, "type"));

                    if (kind == AiErrorKind.Provider &&
                        (int)response.StatusCode == 400 &&
                        LooksLikeContextOverflow(AiJson.String(error, "message")))
                    {
                        kind = AiErrorKind.ContextTooLong;
                    }
                }
            }
            catch (JsonException)
            {
                // An unparseable body tells us nothing extra; the status code already classified it.
            }
        }

        _logger.LogWarning(
            "Anthropic request failed: HTTP {Status} {Code}", (int)response.StatusCode, code ?? "(no code)");

        // The body, at Debug, because "(no code)" is otherwise a dead end: a provider that puts its reason in
        // prose rather than in a machine-readable field leaves the log saying only that something failed.
        // Already bounded by ReadBoundedBodyAsync, and it is the provider's own error text - a request body
        // or a credential never reaches here.
        if (body.Length > 0) _logger.LogDebug("Anthropic error body: {Body}", body);

        // Read once and used twice: the sentence the reader sees and the delay any retry honours must agree,
        // and computing them from separate reads of the same headers is how they drift.
        var wait = AiHttp.RateLimitWait(response);

        return new AiError(
            kind,
            AiHttp.MessageFor(kind, response.StatusCode, wait, !string.IsNullOrWhiteSpace(_options.ApiKey)),
            StatusCode: (int)response.StatusCode,
            ProviderCode: code,
            RetryAfter: wait);
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
