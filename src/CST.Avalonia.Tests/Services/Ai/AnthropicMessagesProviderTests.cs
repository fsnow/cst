using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// The Anthropic adapter against recorded SSE. No network, no key, no spend — the wire format is replayed
/// verbatim, so these pin the parts of the contract a live smoke test would not reach: the failure taxonomy,
/// a stream that dies mid-answer, and the request shape. (#578)
/// </summary>
public class AnthropicMessagesProviderTests
{
    private static AnthropicMessagesProvider Provider(
        StubHttpMessageHandler handler, string? key = "sk-test", string? baseUrl = null,
        TimeSpan? idle = null, TimeSpan? firstEvent = null) =>
        new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, new AnthropicOptions(key, baseUrl),
            NullLogger<AnthropicMessagesProvider>.Instance, idle, firstEvent);

    private static ChatRequest Request(string? system = null) =>
        new("claude-opus-5", 1024, system, new[] { new ChatMessage(ChatRole.User, "Explain this passage.") });

    private static async Task<List<ChatDelta>> CollectAsync(
        IChatProvider provider, ChatRequest? request = null, CancellationToken ct = default)
    {
        var deltas = new List<ChatDelta>();
        await foreach (var delta in provider.StreamAsync(request ?? Request(), ct))
            deltas.Add(delta);
        return deltas;
    }

    private static string Text(IEnumerable<ChatDelta> deltas) =>
        string.Concat(deltas.Where(d => d.Kind == ChatDeltaKind.Text).Select(d => d.Text));

    private static string Reasoning(IEnumerable<ChatDelta> deltas) =>
        string.Concat(deltas.Where(d => d.Kind == ChatDeltaKind.Reasoning).Select(d => d.Text));

    private const string HappyStream = """
        event: message_start
        data: {"type":"message_start","message":{"usage":{"input_tokens":412}}}

        event: content_block_start
        data: {"type":"content_block_start","index":0,"content_block":{"type":"text","text":""}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Heedfulness "}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"is the path."}}

        event: message_delta
        data: {"type":"message_delta","delta":{"stop_reason":"end_turn"},"usage":{"output_tokens":37}}

        event: message_stop
        data: {"type":"message_stop"}

        """;

    [Fact]
    public async Task Streams_text_deltas_in_order()
    {
        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(HappyStream)));

        Assert.Equal("Heedfulness is the path.", Text(deltas));
        Assert.DoesNotContain(deltas, d => d.Kind == ChatDeltaKind.Error);
    }

    [Fact]
    public async Task Reports_usage_from_both_ends_of_the_stream()
    {
        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(HappyStream)));
        var usage = deltas.Where(d => d.Kind == ChatDeltaKind.Usage).Select(d => d.Usage!).ToList();

        Assert.Contains(usage, u => u.InputTokens == 412);
        Assert.Contains(usage, u => u.OutputTokens == 37);
    }

    [Fact]
    public async Task Thinking_deltas_are_segregated_from_the_answer()
    {
        const string stream = """
            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"thinking_delta","thinking":"The compound is a dvanda."}}

            event: content_block_delta
            data: {"type":"content_block_delta","index":1,"delta":{"type":"text_delta","text":"It means X."}}

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal("It means X.", Text(deltas));
        Assert.Equal("The compound is a dvanda.", Reasoning(deltas));
    }

    [Fact]
    public async Task Request_carries_max_tokens_and_no_sampling_parameters()
    {
        // max_tokens is required by the API; temperature/top_p/top_k are REJECTED with a 400 by current models.
        var handler = StubHttpMessageHandler.Sse(HappyStream);
        await CollectAsync(Provider(handler), Request(system: "You are a Pali reading assistant."));

        using var body = JsonDocument.Parse(handler.LastRequestBody);
        var root = body.RootElement;

        Assert.Equal(1024, root.GetProperty("max_tokens").GetInt32());
        Assert.True(root.GetProperty("stream").GetBoolean());
        Assert.Equal("You are a Pali reading assistant.", root.GetProperty("system").GetString());
        Assert.False(root.TryGetProperty("temperature", out _));
        Assert.False(root.TryGetProperty("top_p", out _));
        Assert.False(root.TryGetProperty("top_k", out _));
    }

    [Fact]
    public async Task An_unset_cap_becomes_the_ceiling_every_current_model_accepts()
    {
        // The API requires the field, so null cannot mean "omit" here. 64K rather than 128K because the model
        // id is whatever the user typed: Opus 5, Sonnet 5 and the Opus 4.x family all allow 128K, but Haiku 4.5
        // caps at 64K, and this adapter cannot tell which one it is talking to.
        var handler = StubHttpMessageHandler.Sse(HappyStream);
        await CollectAsync(Provider(handler), Request() with { MaxTokens = null });

        using var body = JsonDocument.Parse(handler.LastRequestBody);

        Assert.Equal(AiLimits.UniversalMaxTokens, body.RootElement.GetProperty("max_tokens").GetInt32());
    }

    [Fact]
    public async Task Request_carries_the_api_key_and_version_headers()
    {
        var handler = StubHttpMessageHandler.Sse(HappyStream);
        await CollectAsync(Provider(handler));

        Assert.Equal("sk-test", handler.LastRequest!.Headers.GetValues("x-api-key").Single());
        Assert.Equal("2023-06-01", handler.LastRequest.Headers.GetValues("anthropic-version").Single());
    }

    [Fact]
    public async Task A_missing_key_fails_before_any_request_is_made()
    {
        var handler = StubHttpMessageHandler.Sse(HappyStream);

        var error = await Assert.ThrowsAsync<AiException>(() => CollectAsync(Provider(handler, key: null)));

        Assert.Equal(AiErrorKind.NotConfigured, error.Error.Kind);
        Assert.Empty(handler.RequestedUrls);
    }

    [Theory]
    [InlineData(null, "https://api.anthropic.com/v1/messages")]
    [InlineData("https://api.anthropic.com", "https://api.anthropic.com/v1/messages")]
    [InlineData("https://gateway.example.com/anthropic", "https://gateway.example.com/anthropic/v1/messages")]
    [InlineData("https://gateway.example.com/v1/messages", "https://gateway.example.com/v1/messages")]
    public async Task Resolves_the_endpoint_from_assorted_base_urls(string? baseUrl, string expected)
    {
        var handler = StubHttpMessageHandler.Sse(HappyStream);
        await CollectAsync(Provider(handler, baseUrl: baseUrl));

        Assert.Equal(expected, handler.RequestedUrls.Single().ToString());
    }

    [Fact]
    public async Task Unauthorized_maps_to_its_own_kind_and_does_not_echo_the_provider_prose()
    {
        // The body quotes request material back; none of it may reach the error we surface or log.
        const string body = """
            {"type":"error","error":{"type":"authentication_error","message":"invalid x-api-key for prompt 'Explain Dhp 21: appamado amatapadam'"}}
            """;
        var handler = StubHttpMessageHandler.Error(HttpStatusCode.Unauthorized, body);

        var error = await Assert.ThrowsAsync<AiException>(() => CollectAsync(Provider(handler)));

        Assert.Equal(AiErrorKind.Unauthorized, error.Error.Kind);
        Assert.Equal("authentication_error", error.Error.ProviderCode);
        Assert.DoesNotContain("appamado", error.Error.Message);
        Assert.DoesNotContain("Explain Dhp", error.Error.Message);
    }

    [Fact]
    public async Task Rate_limiting_carries_the_retry_after_delay()
    {
        var handler = StubHttpMessageHandler.Error(
            HttpStatusCode.TooManyRequests,
            """{"type":"error","error":{"type":"rate_limit_error","message":"slow down"}}""",
            retryAfter: "30");

        var error = await Assert.ThrowsAsync<AiException>(() => CollectAsync(Provider(handler)));

        Assert.Equal(AiErrorKind.RateLimited, error.Error.Kind);
        Assert.Equal(TimeSpan.FromSeconds(30), error.Error.RetryAfter);
    }

    [Fact]
    public async Task A_context_overflow_is_distinguished_from_other_bad_requests()
    {
        // Anthropic has no machine-readable code for this — the prose is the only signal.
        var handler = StubHttpMessageHandler.Error(
            HttpStatusCode.BadRequest,
            """{"type":"error","error":{"type":"invalid_request_error","message":"prompt is too long: 1200000 tokens > 1000000"}}""");

        var error = await Assert.ThrowsAsync<AiException>(() => CollectAsync(Provider(handler)));

        Assert.Equal(AiErrorKind.ContextTooLong, error.Error.Kind);
    }

    [Fact]
    public async Task An_ordinary_bad_request_stays_a_provider_error()
    {
        var handler = StubHttpMessageHandler.Error(
            HttpStatusCode.BadRequest,
            """{"type":"error","error":{"type":"invalid_request_error","message":"max_tokens: must be >= 1"}}""");

        var error = await Assert.ThrowsAsync<AiException>(() => CollectAsync(Provider(handler)));

        Assert.Equal(AiErrorKind.Provider, error.Error.Kind);
    }

    [Fact]
    public async Task An_error_event_mid_stream_keeps_the_text_already_streamed()
    {
        const string stream = """
            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Heedfulness "}}

            event: error
            data: {"type":"error","error":{"type":"overloaded_error","message":"overloaded"}}

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal("Heedfulness ", Text(deltas));
        var error = Assert.Single(deltas, d => d.Kind == ChatDeltaKind.Error).Error!;
        Assert.Equal(AiErrorKind.Provider, error.Kind);
        Assert.Equal("overloaded_error", error.ProviderCode);
    }

    [Fact]
    public async Task A_dropped_connection_mid_stream_keeps_the_partial_answer()
    {
        const string stream = """
            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Heedfulness is "}}

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.SseThenDrop(stream)));

        Assert.Equal("Heedfulness is ", Text(deltas));
        var error = Assert.Single(deltas, d => d.Kind == ChatDeltaKind.Error).Error!;
        Assert.Equal(AiErrorKind.Network, error.Kind);
    }

    [Fact]
    public async Task A_malformed_event_is_skipped_rather_than_ending_a_healthy_stream()
    {
        const string stream = """
            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Heed"}}

            event: content_block_delta
            data: {"type":"content_block_delta","delta":{"type":"text_delta","text":"trunc

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"fulness"}}

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal("Heedfulness", Text(deltas));
        Assert.DoesNotContain(deltas, d => d.Kind == ChatDeltaKind.Error);
    }

    [Fact]
    public async Task Cancellation_throws_rather_than_reporting_an_error_delta()
    {
        using var cts = new CancellationTokenSource();
        var provider = Provider(StubHttpMessageHandler.Hangs());

        var pump = Task.Run(async () => await CollectAsync(provider, ct: cts.Token));
        cts.CancelAfter(TimeSpan.FromMilliseconds(100));

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => pump);
    }

    [Fact]
    public async Task A_stream_that_never_starts_is_abandoned_by_the_first_event_timeout()
    {
        // Time-to-first-token has its own, longer window than the between-lines idle timeout: a local runner
        // evaluating a large injected passage can legitimately sit silent for minutes before saying anything.
        var provider = Provider(
            StubHttpMessageHandler.Hangs(),
            idle: TimeSpan.FromMinutes(5),
            firstEvent: TimeSpan.FromMilliseconds(150));

        var deltas = await CollectAsync(provider);

        var error = Assert.Single(deltas, d => d.Kind == ChatDeltaKind.Error).Error!;
        Assert.Equal(AiErrorKind.Network, error.Kind);
    }

    [Fact]
    public async Task A_200_carrying_no_events_is_reported_rather_than_answered_blank()
    {
        // The wrong-endpoint case: something answers 200 with a page that is not a stream. Saying nothing would
        // leave the user unable to tell a misconfiguration from a model that declined to speak.
        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse("<html>not an api</html>")));

        var error = Assert.Single(deltas, d => d.Kind == ChatDeltaKind.Error).Error!;
        Assert.Equal(AiErrorKind.Provider, error.Kind);
    }

    [Fact]
    public async Task A_chunk_of_an_unexpected_json_shape_does_not_crash_the_stream()
    {
        // `data: null` parses successfully and then faults on the first property read; a `delta` that is a
        // string rather than an object faults the same way. Both must be skipped, not thrown out of the
        // iterator as an unclassified crash mid-answer. (A numeric `type` is NOT in this fixture: it falls
        // back to the SSE event name, which is the right behaviour rather than a skip.)
        const string stream = """
            event: ping
            data: null

            event: content_block_delta
            data: {"type":"content_block_delta","delta":"not-an-object"}

            event: content_block_delta
            data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"Survived."}}

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal("Survived.", Text(deltas));
        Assert.DoesNotContain(deltas, d => d.Kind == ChatDeltaKind.Error);
    }

    [Fact]
    public async Task A_provider_code_that_is_not_token_shaped_is_dropped_rather_than_logged()
    {
        // A wrong endpoint can put anything in `type`, including echoed request material.
        var handler = StubHttpMessageHandler.Error(
            HttpStatusCode.BadRequest,
            """{"type":"error","error":{"type":"rejected while handling 'Explain Dhp 21: appamado amatapadam'"}}""");

        var error = await Assert.ThrowsAsync<AiException>(() => CollectAsync(Provider(handler)));

        Assert.Null(error.Error.ProviderCode);
        Assert.DoesNotContain("appamado", error.Error.Message);
    }

    [Fact]
    public void A_client_with_a_finite_timeout_is_refused_at_construction()
    {
        // A finite HttpClient.Timeout truncates a long stream and reports it as a cancellation — close to
        // undiagnosable from a bug report, so it fails loudly here instead.
        var http = new HttpClient(StubHttpMessageHandler.Sse(HappyStream)) { Timeout = TimeSpan.FromSeconds(100) };

        Assert.Throws<ArgumentException>(() =>
            new AnthropicMessagesProvider(
                http, new AnthropicOptions("sk-test"), NullLogger<AnthropicMessagesProvider>.Instance));
    }
}
