using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.Tests.TestSupport;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// #711: a connection's <c>Headers[]</c> were collected, stored, and then dropped on the floor by this
/// adapter, which had nowhere to put them. That is not a cosmetic gap — <c>Kind = Anthropic</c> does not
/// imply <c>api.anthropic.com</c>, and a gateway speaking the Messages protocol is exactly the case that
/// needs a second header.
///
/// <para>Asserted at the wire, not through the header-building method: the defect these replace was a
/// request shape that looked right in the code and arrived incomplete, so the guarantee worth pinning is
/// what the handler actually saw.</para>
/// </summary>
public class AnthropicAuthTests
{
    private const string MinimalStream = """
        event: message_start
        data: {"type":"message_start","message":{"usage":{"input_tokens":1}}}

        event: content_block_delta
        data: {"type":"content_block_delta","index":0,"delta":{"type":"text_delta","text":"ok"}}

        event: message_stop
        data: {"type":"message_stop"}

        """;

    /// <summary>Runs one turn against a replayed stream and hands back the request the handler received.</summary>
    private static async Task<HttpRequestMessage> SendAsync(
        string? key = "sk-test",
        IReadOnlyDictionary<string, string>? extraHeaders = null)
    {
        var handler = StubHttpMessageHandler.Sse(MinimalStream);
        var provider = new AnthropicMessagesProvider(
            new HttpClient(handler) { Timeout = Timeout.InfiniteTimeSpan },
            new AnthropicOptions(key, BaseUrl: null, ExtraHeaders: extraHeaders),
            NullLogger<AnthropicMessagesProvider>.Instance);

        var request = new ChatRequest(
            "claude-opus-5", 1024, null, new[] { new ChatMessage(ChatRole.User, "Explain this passage.") });

        await foreach (var _ in provider.StreamAsync(request, CancellationToken.None)) { }

        return handler.LastRequest!;
    }

    private static string? Header(HttpRequestMessage m, string name) =>
        m.Headers.TryGetValues(name, out var values) ? string.Join(",", values) : null;

    private static int Count(HttpRequestMessage m, string name) =>
        m.Headers.TryGetValues(name, out var values) ? values.Count() : 0;

    [Fact]
    public async Task The_credential_goes_in_x_api_key_with_the_protocol_version()
    {
        var sent = await SendAsync();

        Assert.Equal("sk-test", Header(sent, "x-api-key"));
        Assert.Equal("2023-06-01", Header(sent, "anthropic-version"));
    }

    /// <summary>The defect itself: a header the reader configured has to reach the endpoint.</summary>
    [Fact]
    public async Task A_connections_extra_headers_reach_the_endpoint()
    {
        var sent = await SendAsync(extraHeaders: new Dictionary<string, string>
        {
            ["cf-aig-authorization"] = "Bearer gateway-token",
            ["X-Title"] = "CST Reader",
        });

        Assert.Equal("Bearer gateway-token", Header(sent, "cf-aig-authorization"));
        Assert.Equal("CST Reader", Header(sent, "X-Title"));
    }

    /// <summary>
    /// The escape hatch #689 promised — "leave the key empty if you manage auth via headers" — with the
    /// header name a gateway most often wants. A blanket skip of <c>Authorization</c> silently defeated
    /// this, and the symptom was an unauthenticated request rather than anything naming the cause.
    /// </summary>
    [Fact]
    public async Task With_no_key_stored_an_authorization_header_still_goes_out()
    {
        var sent = await SendAsync(key: null, extraHeaders: new Dictionary<string, string>
        {
            ["Authorization"] = "Bearer gateway-token",
        });

        Assert.Equal("Bearer gateway-token", Header(sent, "Authorization"));
        Assert.Null(Header(sent, "x-api-key"));
    }

    /// <summary>An extra header may never overwrite the credential, as on the OpenAI-compatible side.</summary>
    [Fact]
    public async Task An_extra_header_cannot_overwrite_the_credential()
    {
        var sent = await SendAsync(extraHeaders: new Dictionary<string, string>
        {
            ["x-api-key"] = "typed-by-hand",
        });

        Assert.Equal("sk-test", Header(sent, "x-api-key"));
        Assert.Equal(1, Count(sent, "x-api-key"));
    }

    /// <summary>
    /// A gateway that pins its own protocol version gets it — and gets exactly one. The hazard here is not
    /// which value wins but arriving at two, because adding a header appends rather than replaces.
    /// </summary>
    [Fact]
    public async Task A_connection_may_pin_its_own_protocol_version_without_sending_two()
    {
        var sent = await SendAsync(extraHeaders: new Dictionary<string, string>
        {
            ["anthropic-version"] = "2026-01-01",
        });

        Assert.Equal("2026-01-01", Header(sent, "anthropic-version"));
        Assert.Equal(1, Count(sent, "anthropic-version"));
    }
}
