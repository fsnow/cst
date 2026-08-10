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
/// The OpenAI-compatible adapter — the one that has to serve DeepSeek, OpenRouter, Ollama and LM Studio from a
/// single implementation. Most of these pin per-provider quirks rather than the happy path: reasoning that
/// arrives on two different channels, and the base-URL forms users actually paste. (#578)
/// </summary>
public class OpenAiCompatibleProviderTests
{
    private static OpenAiCompatibleProvider Provider(
        StubHttpMessageHandler handler,
        string? baseUrl = "https://api.deepseek.com/v1",
        string? key = "sk-test",
        TimeSpan? idle = null,
        TimeSpan? firstEvent = null) =>
        new(new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan }, new OpenAiCompatibleOptions(baseUrl, key),
            NullLogger<OpenAiCompatibleProvider>.Instance, idle, firstEvent);

    private static ChatRequest Request(string? system = null) =>
        new("deepseek-chat", 1024, system, new[] { new ChatMessage(ChatRole.User, "Explain this passage.") });

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
        data: {"choices":[{"delta":{"content":"Heedfulness "}}]}

        data: {"choices":[{"delta":{"content":"is the path."}}]}

        data: {"usage":{"prompt_tokens":412,"completion_tokens":37},"choices":[]}

        data: [DONE]

        """;

    [Fact]
    public async Task Streams_text_deltas_in_order()
    {
        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(HappyStream)));

        Assert.Equal("Heedfulness is the path.", Text(deltas));
    }

    [Fact]
    public async Task Reports_usage_from_the_final_chunk()
    {
        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(HappyStream)));
        var usage = Assert.Single(deltas, d => d.Kind == ChatDeltaKind.Usage).Usage!;

        Assert.Equal(412, usage.InputTokens);
        Assert.Equal(37, usage.OutputTokens);
    }

    [Fact]
    public async Task Structured_reasoning_never_reaches_the_answer_channel()
    {
        // DeepSeek's reasoning models stream reasoning_content alongside content on the same connection.
        const string stream = """
            data: {"choices":[{"delta":{"reasoning_content":"The user wants a gloss. "}}]}

            data: {"choices":[{"delta":{"reasoning_content":"Check the compound."}}]}

            data: {"choices":[{"delta":{"content":"It means heedfulness."}}]}

            data: [DONE]

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal("It means heedfulness.", Text(deltas));
        Assert.Equal("The user wants a gloss. Check the compound.", Reasoning(deltas));
    }

    [Fact]
    public async Task Inline_think_tags_are_stripped_out_of_the_answer()
    {
        const string stream = """
            data: {"choices":[{"delta":{"content":"<think>weighing options</think>The answer."}}]}

            data: [DONE]

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal("The answer.", Text(deltas));
        Assert.Equal("weighing options", Reasoning(deltas));
    }

    [Fact]
    public async Task A_think_tag_split_across_chunks_is_still_stripped()
    {
        // The hard case: the tag straddles a delta boundary, so no per-chunk replace can see it whole.
        const string stream = """
            data: {"choices":[{"delta":{"content":"<thi"}}]}

            data: {"choices":[{"delta":{"content":"nk>hidden</thi"}}]}

            data: {"choices":[{"delta":{"content":"nk>Visible."}}]}

            data: [DONE]

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal("Visible.", Text(deltas));
        Assert.Equal("hidden", Reasoning(deltas));
    }

    [Fact]
    public async Task Text_that_merely_looks_like_the_start_of_a_tag_is_not_swallowed()
    {
        const string stream = """
            data: {"choices":[{"delta":{"content":"a < b and c <th"}}]}

            data: [DONE]

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal("a < b and c <th", Text(deltas));
    }

    [Fact]
    public async Task The_system_prompt_becomes_a_leading_message()
    {
        var handler = StubHttpMessageHandler.Sse(HappyStream);
        await CollectAsync(Provider(handler), Request(system: "You are a Pali reading assistant."));

        using var body = JsonDocument.Parse(handler.LastRequestBody);
        var messages = body.RootElement.GetProperty("messages");

        Assert.Equal("system", messages[0].GetProperty("role").GetString());
        Assert.Equal("You are a Pali reading assistant.", messages[0].GetProperty("content").GetString());
        Assert.Equal("user", messages[1].GetProperty("role").GetString());
    }

    [Fact]
    public async Task An_api_key_is_sent_when_configured()
    {
        var handler = StubHttpMessageHandler.Sse(HappyStream);
        await CollectAsync(Provider(handler));

        Assert.Equal("Bearer", handler.LastRequest!.Headers.Authorization!.Scheme);
        Assert.Equal("sk-test", handler.LastRequest.Headers.Authorization.Parameter);
    }

    [Fact]
    public async Task An_empty_key_is_a_valid_configuration_for_a_local_runner()
    {
        // Ollama and LM Studio accept anonymous requests; requiring a key would block the fully-local option.
        var handler = StubHttpMessageHandler.Sse(HappyStream);

        var deltas = await CollectAsync(Provider(handler, baseUrl: "http://localhost:11434/v1", key: ""));

        Assert.Null(handler.LastRequest!.Headers.Authorization);
        Assert.Equal("Heedfulness is the path.", Text(deltas));
    }

    [Theory]
    [InlineData("https://api.deepseek.com", "https://api.deepseek.com/v1/chat/completions")]
    [InlineData("https://api.deepseek.com/v1", "https://api.deepseek.com/v1/chat/completions")]
    [InlineData("https://api.deepseek.com/v1/", "https://api.deepseek.com/v1/chat/completions")]
    [InlineData("http://localhost:11434/v1", "http://localhost:11434/v1/chat/completions")]
    [InlineData("https://openrouter.ai/api/v1", "https://openrouter.ai/api/v1/chat/completions")]
    [InlineData("https://x.example/v1/chat/completions", "https://x.example/v1/chat/completions")]
    public async Task Resolves_the_endpoint_from_the_base_url_forms_users_actually_paste(
        string baseUrl, string expected)
    {
        var handler = StubHttpMessageHandler.Sse(HappyStream);
        await CollectAsync(Provider(handler, baseUrl: baseUrl));

        Assert.Equal(expected, handler.RequestedUrls.Single().ToString());
    }

    [Fact]
    public async Task A_missing_endpoint_fails_before_any_request_is_made()
    {
        var handler = StubHttpMessageHandler.Sse(HappyStream);

        var error = await Assert.ThrowsAsync<AiException>(() => CollectAsync(Provider(handler, baseUrl: null)));

        Assert.Equal(AiErrorKind.NotConfigured, error.Error.Kind);
        Assert.Empty(handler.RequestedUrls);
    }

    [Fact]
    public async Task A_non_http_endpoint_is_rejected_as_misconfiguration()
    {
        var handler = StubHttpMessageHandler.Sse(HappyStream);

        var error = await Assert.ThrowsAsync<AiException>(
            () => CollectAsync(Provider(handler, baseUrl: "file:///etc/passwd")));

        Assert.Equal(AiErrorKind.NotConfigured, error.Error.Kind);
        Assert.Empty(handler.RequestedUrls);
    }

    [Fact]
    public async Task Unauthorized_maps_to_its_own_kind_and_does_not_echo_the_provider_prose()
    {
        const string body = """
            {"error":{"code":"invalid_api_key","message":"bad key while handling 'Explain Dhp 21: appamado'"}}
            """;
        var handler = StubHttpMessageHandler.Error(HttpStatusCode.Unauthorized, body);

        var error = await Assert.ThrowsAsync<AiException>(() => CollectAsync(Provider(handler)));

        Assert.Equal(AiErrorKind.Unauthorized, error.Error.Kind);
        Assert.Equal("invalid_api_key", error.Error.ProviderCode);
        Assert.DoesNotContain("appamado", error.Error.Message);
    }

    [Fact]
    public async Task A_context_overflow_is_recognised_from_its_code()
    {
        var handler = StubHttpMessageHandler.Error(
            HttpStatusCode.BadRequest,
            """{"error":{"code":"context_length_exceeded","message":"too long"}}""");

        var error = await Assert.ThrowsAsync<AiException>(() => CollectAsync(Provider(handler)));

        Assert.Equal(AiErrorKind.ContextTooLong, error.Error.Kind);
    }

    [Fact]
    public async Task Rate_limiting_carries_the_retry_after_delay()
    {
        var handler = StubHttpMessageHandler.Error(
            HttpStatusCode.TooManyRequests,
            """{"error":{"code":"rate_limit_exceeded"}}""",
            retryAfter: "12");

        var error = await Assert.ThrowsAsync<AiException>(() => CollectAsync(Provider(handler)));

        Assert.Equal(AiErrorKind.RateLimited, error.Error.Kind);
        Assert.Equal(TimeSpan.FromSeconds(12), error.Error.RetryAfter);
    }

    [Fact]
    public async Task An_error_delivered_as_a_200_chunk_still_surfaces()
    {
        // Some gateways answer 200 and put the failure in the stream.
        const string stream = """
            data: {"choices":[{"delta":{"content":"Heed"}}]}

            data: {"error":{"code":"server_error","message":"upstream exploded"}}

            data: [DONE]

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal("Heed", Text(deltas));
        var error = Assert.Single(deltas, d => d.Kind == ChatDeltaKind.Error).Error!;
        Assert.Equal("server_error", error.ProviderCode);
        Assert.DoesNotContain("exploded", error.Message);
    }

    [Fact]
    public async Task A_dropped_connection_mid_stream_keeps_the_partial_answer()
    {
        const string stream = """
            data: {"choices":[{"delta":{"content":"Heedfulness is "}}]}

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.SseThenDrop(stream)));

        Assert.Equal("Heedfulness is ", Text(deltas));
        Assert.Equal(AiErrorKind.Network, Assert.Single(deltas, d => d.Kind == ChatDeltaKind.Error).Error!.Kind);
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
    public async Task A_numeric_error_code_does_not_crash_the_stream()
    {
        // OpenRouter reports mid-stream failures with a NUMERIC code. Reading it as a string throws
        // InvalidOperationException straight out of the iterator — an unclassified crash where the contract
        // promises an Error delta.
        const string stream = """
            data: {"choices":[{"delta":{"content":"Heed"}}]}

            data: {"error":{"code":403,"message":"moderation"}}

            data: [DONE]

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal("Heed", Text(deltas));
        var error = Assert.Single(deltas, d => d.Kind == ChatDeltaKind.Error).Error!;
        Assert.Equal("403", error.ProviderCode);
    }

    [Fact]
    public async Task Nothing_is_appended_to_the_answer_after_an_error_delta()
    {
        // A gateway can report a failure and keep streaming. The Error delta is terminal by contract.
        const string stream = """
            data: {"choices":[{"delta":{"content":"Heed"}}]}

            data: {"error":{"code":"server_error"}}

            data: {"choices":[{"delta":{"content":" MORE"}}]}

            data: [DONE]

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal("Heed", Text(deltas));
        Assert.Equal(ChatDeltaKind.Error, deltas[^1].Kind);
    }

    [Fact]
    public async Task A_chunk_of_an_unexpected_json_shape_does_not_crash_the_stream()
    {
        const string stream = """
            data: null

            data: {"choices":"not-an-array"}

            data: {"choices":[{"delta":{"content":"Survived."}}]}

            data: [DONE]

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal("Survived.", Text(deltas));
        Assert.DoesNotContain(deltas, d => d.Kind == ChatDeltaKind.Error);
    }

    [Fact]
    public async Task A_200_carrying_no_events_is_reported_rather_than_answered_blank()
    {
        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse("<html>not an api</html>")));

        var error = Assert.Single(deltas, d => d.Kind == ChatDeltaKind.Error).Error!;
        Assert.Equal(AiErrorKind.Provider, error.Kind);
    }

    [Fact]
    public async Task A_stream_that_begins_inside_a_think_block_is_still_segregated()
    {
        // Some runner chat templates pre-fill the opening <think> into the prompt, so the model's output starts
        // inside the block and only the closing tag is ever streamed.
        const string stream = """
            data: {"choices":[{"delta":{"content":"weighing options</think>The answer."}}]}

            data: [DONE]

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal("The answer.", Text(deltas));
        Assert.Equal("weighing options", Reasoning(deltas));
    }

    [Fact]
    public async Task An_unclosed_think_block_is_flushed_as_reasoning_not_as_the_answer()
    {
        const string stream = """
            data: {"choices":[{"delta":{"content":"<think>still musing"}}]}

            data: [DONE]

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal("", Text(deltas));
        Assert.Equal("still musing", Reasoning(deltas));
    }

    [Fact]
    public async Task Pali_diacritics_split_across_a_read_boundary_survive_intact()
    {
        // The corpus is full of multi-byte characters; a UTF-8 sequence straddling a buffer boundary must not
        // be mangled into replacement characters.
        var text = string.Concat(System.Linq.Enumerable.Repeat("appamado amatapadam thana ", 400))
            .Replace("appamado", "appam\u0101do").Replace("amatapadam", "amatapada\u1E41")
            .Replace("thana", "\u1E6Dh\u0101na");
        var stream =
            "data: {\"choices\":[{\"delta\":{\"content\":\"" + text + "\"}}]}\n\n" +
            "data: [DONE]\n\n";

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal(text, Text(deltas));
        Assert.DoesNotContain("\uFFFD", Text(deltas));
    }

    [Theory]
    [InlineData("https://host/v1?api-version=2024-01", "https://host/v1/chat/completions?api-version=2024-01")]
    [InlineData("https://HOST/V1/CHAT/COMPLETIONS", "https://host/V1/CHAT/COMPLETIONS")]
    [InlineData("https://host/mychat/completions", "https://host/mychat/completions/chat/completions")]
    public async Task Endpoint_resolution_survives_the_awkward_base_urls(string baseUrl, string expected)
    {
        var handler = StubHttpMessageHandler.Sse(HappyStream);
        await CollectAsync(Provider(handler, baseUrl: baseUrl));

        Assert.Equal(expected, handler.RequestedUrls.Single().ToString());
    }
}
