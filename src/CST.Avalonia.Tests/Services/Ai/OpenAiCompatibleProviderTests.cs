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
    public async Task An_unset_cap_omits_max_tokens_entirely()
    {
        // Unlike Anthropic, the field is optional here — so "no cap" is expressed by absence rather than by a
        // number we invented. That matters most on a reasoning model, where a cap covers reasoning as well as
        // the answer and can consume the whole budget before a word is written (#601).
        var handler = StubHttpMessageHandler.Sse(HappyStream);
        await CollectAsync(Provider(handler), Request() with { MaxTokens = null });

        using var body = JsonDocument.Parse(handler.LastRequestBody);

        Assert.False(body.RootElement.TryGetProperty("max_tokens", out _));
    }

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

    // ---- Truncation (#601) ---------------------------------------------------------------------------------

    /// <summary>
    /// The real shape of a capped turn: content, then a chunk whose delta is empty and whose finish_reason says
    /// why, then the usage chunk, then the terminator. Note the order — the reason is NOT last.
    /// </summary>
    private const string TruncatedStream = """
        data: {"choices":[{"delta":{"content":"Heedfulness is the path to the"}}]}

        data: {"choices":[{"delta":{},"finish_reason":"length"}]}

        data: {"usage":{"prompt_tokens":412,"completion_tokens":3177},"choices":[]}

        data: [DONE]

        """;

    [Fact]
    public async Task A_turn_cut_off_at_the_output_limit_is_reported_as_truncated()
    {
        // Otherwise this is SILENT: a stream that stops mid-sentence at the cap ends exactly as a complete one
        // does, so the app would render half a translation under a citation and call it an answer. (#601)
        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(TruncatedStream)));

        Assert.Equal(AiErrorKind.Truncated, deltas[^1].Error!.Kind);
        Assert.Equal("Heedfulness is the path to the", Text(deltas));
    }

    [Fact]
    public async Task The_truncation_error_arrives_after_the_usage_it_explains()
    {
        // An Error delta is terminal by contract, and the token counts arrive in a chunk BEHIND the one carrying
        // finish_reason. Reporting on the spot would discard the number that explains the truncation — which is
        // also the number the user paid.
        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(TruncatedStream)));

        var usage = deltas.FindIndex(d => d.Kind == ChatDeltaKind.Usage);
        var error = deltas.FindIndex(d => d.Kind == ChatDeltaKind.Error);

        Assert.True(usage >= 0, "usage was dropped");
        Assert.True(usage < error, "the terminal error preceded the usage it explains");
        Assert.Equal(3177, deltas[usage].Usage!.OutputTokens);
    }

    [Fact]
    public async Task A_normal_ending_is_not_reported_as_truncation()
    {
        // finish_reason is present on the last chunk of EVERY turn. Firing on anything but the cap would put an
        // error under every well-formed answer.
        const string stream = """
            data: {"choices":[{"delta":{"content":"Heedfulness."},"finish_reason":"stop"}]}

            data: [DONE]

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.DoesNotContain(deltas, d => d.Kind == ChatDeltaKind.Error);
        Assert.Equal("Heedfulness.", Text(deltas));
    }

    [Theory]
    [InlineData("stop")]
    [InlineData("content_filter")]
    [InlineData("tool_calls")]
    [InlineData("")]
    public async Task Only_a_length_ending_counts_as_truncation(string finishReason)
    {
        var stream = $$"""
            data: {"choices":[{"delta":{"content":"x"},"finish_reason":"{{finishReason}}"}]}

            data: [DONE]

            """;

        Assert.DoesNotContain(
            await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream))), d => d.Kind == ChatDeltaKind.Error);
    }

    [Fact]
    public async Task A_gateway_reporting_the_anthropic_spelling_is_understood()
    {
        // "OpenAI-compatible" is an arbitrary user-pasted endpoint, and a gateway fronting Anthropic passes its
        // max_tokens through unchanged. It can only mean the one thing.
        const string stream = """
            data: {"choices":[{"delta":{"content":"x"},"finish_reason":"max_tokens"}]}

            data: [DONE]

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal(AiErrorKind.Truncated, deltas[^1].Error!.Kind);
        Assert.Equal("max_tokens", deltas[^1].Error!.ProviderCode);
    }

    [Fact]
    public async Task Content_on_the_same_chunk_as_the_finish_reason_is_not_dropped()
    {
        // Not every provider sends the reason on a chunk of its own — some attach it to the final content delta.
        // Losing that delta would truncate the answer further than the model did.
        const string stream = """
            data: {"choices":[{"delta":{"content":"the deathless"},"finish_reason":"length"}]}

            data: [DONE]

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal("the deathless", Text(deltas));
        Assert.Equal(AiErrorKind.Truncated, deltas[^1].Error!.Kind);
    }

    [Fact]
    public async Task Held_back_think_tag_text_is_flushed_before_the_truncation_is_reported()
    {
        // An Error delta is terminal, so anything the think-tag filter is still holding has to come out first or
        // it is lost — the same ordering rule the mid-stream error path follows.
        const string stream = """
            data: {"choices":[{"delta":{"content":"<think>weighing it up"}}]}

            data: {"choices":[{"delta":{},"finish_reason":"length"}]}

            data: [DONE]

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal("weighing it up", Reasoning(deltas));
        Assert.Equal(ChatDeltaKind.Error, deltas[^1].Kind);
    }

    [Fact]
    public async Task A_mid_stream_provider_error_still_wins_over_a_later_truncation()
    {
        // The error delta is terminal where it occurs; the truncation check must not append a second terminal
        // event behind it.
        const string stream = """
            data: {"choices":[{"delta":{"content":"Heedful"}}]}

            data: {"error":{"code":"server_error"}}

            data: {"choices":[{"delta":{},"finish_reason":"length"}]}

            data: [DONE]

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal(AiErrorKind.Provider, Assert.Single(deltas, d => d.Kind == ChatDeltaKind.Error).Error!.Kind);
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
    // A single unversioned segment is the docs URL with /v1 dropped — rescue it.
    [InlineData("https://openrouter.ai/api", "https://openrouter.ai/api/v1/chat/completions")]
    [InlineData("https://api.groq.com/openai", "https://api.groq.com/openai/v1/chat/completions")]
    // …but a LONGER path is somebody's documented base and must be taken at its word. These are all real:
    // Gemini's OpenAI-compat base, Azure classic deployments, and a Cloudflare AI Gateway path.
    [InlineData("https://generativelanguage.googleapis.com/v1beta/openai/",
        "https://generativelanguage.googleapis.com/v1beta/openai/chat/completions")]
    [InlineData("https://r.openai.azure.com/openai/deployments/gpt?api-version=2024-02-01",
        "https://r.openai.azure.com/openai/deployments/gpt/chat/completions?api-version=2024-02-01")]
    [InlineData("https://gateway.ai.cloudflare.com/v1/acct/gw/compat",
        "https://gateway.ai.cloudflare.com/v1/acct/gw/compat/chat/completions")]
    // A version segment need not be bare digits.
    [InlineData("https://host/v1beta", "https://host/v1beta/chat/completions")]
    public async Task Endpoint_resolution_survives_the_awkward_base_urls(string baseUrl, string expected)
    {
        var handler = StubHttpMessageHandler.Sse(HappyStream);
        await CollectAsync(Provider(handler, baseUrl: baseUrl));

        Assert.Equal(expected, handler.RequestedUrls.Single().ToString());
    }

    [Fact]
    public void A_client_with_a_finite_timeout_is_refused_at_construction()
    {
        // Pinned on BOTH adapters: a finite HttpClient.Timeout truncates a long stream and reports it as a
        // cancellation, and #583's DI wiring is the place most likely to hand one over by accident.
        var http = new HttpClient(StubHttpMessageHandler.Sse(HappyStream)) { Timeout = TimeSpan.FromSeconds(100) };

        Assert.Throws<ArgumentException>(() =>
            new OpenAiCompatibleProvider(
                http, new OpenAiCompatibleOptions("https://x.example/v1"),
                NullLogger<OpenAiCompatibleProvider>.Instance));
    }

    [Fact]
    public async Task An_overlong_provider_code_is_dropped_rather_than_logged()
    {
        var handler = StubHttpMessageHandler.Error(
            HttpStatusCode.BadRequest,
            "{\"error\":{\"code\":\"" + new string('c', 200) + "\"}}");

        var error = await Assert.ThrowsAsync<AiException>(() => CollectAsync(Provider(handler)));

        Assert.Null(error.Error.ProviderCode);
    }

    [Fact]
    public async Task Held_back_think_text_is_flushed_before_an_error_delta_not_after()
    {
        // A partial tag is in hand when the failure lands; releasing it after the error would put answer text
        // beyond a delta the contract says is terminal.
        const string stream = """
            data: {"choices":[{"delta":{"content":"Answer <th"}}]}

            data: {"error":{"code":"server_error"}}

            data: [DONE]

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal("Answer <th", Text(deltas));
        Assert.Equal(ChatDeltaKind.Error, deltas[^1].Kind);
    }

    [Fact]
    public async Task Reasoning_on_the_undocumented_field_name_is_also_segregated()
    {
        // Captured from a live Ollama serving gpt-oss over its OpenAI-compatible surface: the reasoning arrives
        // on `reasoning`, not the `reasoning_content` DeepSeek documents. Parsing only the documented name
        // dropped the whole reasoning stream, which showed up as usage reporting 80 output tokens beside a
        // one-line answer. OpenRouter uses the same short name.
        const string stream = """
            data: {"choices":[{"index":0,"delta":{"role":"assistant","content":"","reasoning":"The"}}]}

            data: {"choices":[{"index":0,"delta":{"content":"","reasoning":" user asks."}}]}

            data: {"choices":[{"index":0,"delta":{"content":"Appamada is heedfulness."}}]}

            data: [DONE]

            """;

        var deltas = await CollectAsync(Provider(StubHttpMessageHandler.Sse(stream)));

        Assert.Equal("Appamada is heedfulness.", Text(deltas));
        Assert.Equal("The user asks.", Reasoning(deltas));
    }
}
