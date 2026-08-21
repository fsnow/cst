using System;
using System.Net.Http;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using System.Linq;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// Settings + a key in, a callable provider out — or a sentence explaining what is missing. (#583)
///
/// <para>The refusals carry the weight: every one of them is text a user reads in the panel, so the test is as
/// much about whether the message names something they can act on as about the null.</para>
/// </summary>
public class ChatProviderResolverTests
{
    private sealed class FixedKey : IAiCredentialStore
    {
        private readonly string? _key;

        internal FixedKey(string? key, string? unavailable = null)
        {
            _key = key;
            Unavailable = unavailable;
        }

        public bool IsAvailable => Unavailable is null;
        public string? Unavailable { get; }
        public string? Get(string connectionId, string name) => _key;
        public bool Set(string connectionId, string name, string secret) => throw new NotSupportedException();
        public bool Delete(string connectionId, string name) => throw new NotSupportedException();
    }

    private static ChatProviderResolver Resolver(
        Action<ChatSettings> configure, string? apiKey = null, bool aiEnabled = true,
        string? storageUnavailable = null)
    {
        var settings = new Settings();
        settings.Ai.Enabled = aiEnabled;
        settings.Ai.Chat.Enabled = true;
        configure(settings.Ai.Chat);

        var service = new Mock<ISettingsService>();
        service.SetupGet(s => s.Settings).Returns(settings);

        return new ChatProviderResolver(
            service.Object,
            apiKey is null && storageUnavailable is null ? null : new FixedKey(apiKey, storageUnavailable),
            NullLoggerFactory.Instance,
            new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan });
    }

    /// <summary>
    /// The active connection, created on demand. #689 replaced the scalar Provider/BaseUrl/Model on
    /// ChatSettings with a list of connections plus an active id, so these tests configure a connection
    /// rather than three loose fields.
    /// </summary>
    private static CST.Avalonia.Models.AiConnectionRecord Conn(CST.Avalonia.Models.ChatSettings chat)
    {
        var existing = chat.Connections.FirstOrDefault();
        if (existing is not null) return existing;

        var created = new CST.Avalonia.Models.AiConnectionRecord { Id = "test", DisplayName = "test" };
        chat.Connections.Add(created);
        chat.ActiveConnectionId = created.Id;
        return created;
    }

    [Fact]
    public void Anthropic_resolves_with_a_stored_key()
    {
        var resolver = Resolver(c => { Conn(c).Kind = "anthropic"; c.ActiveModelId = " claude-opus-5 "; }, apiKey: "sk-ant-x");

        var resolution = resolver.Resolve(out var problem);

        Assert.Null(problem);
        Assert.IsType<AnthropicMessagesProvider>(resolution!.Provider);
        Assert.Equal("claude-opus-5", resolution.Model);   // trimmed, otherwise verbatim
    }

    [Fact]
    public void Anthropic_without_a_key_says_so_rather_than_failing_at_the_wire()
    {
        var resolver = Resolver(c => { Conn(c).Kind = "anthropic"; c.ActiveModelId = "claude-opus-5"; });

        Assert.Null(resolver.Resolve(out var problem));
        Assert.Contains("API key", problem);
    }

    [Fact]
    public void A_platform_with_nowhere_to_store_a_key_says_that_rather_than_send_the_user_to_settings()
    {
        // Two different problems with two different fixes. "You have not entered a key" is solved in Settings;
        // "this build cannot store one" is not solved there at all, and sending the user there to fix it wastes
        // their time on a screen that cannot help. (#579)
        // The fixture is Linux's message, the one platform with genuinely nowhere to put a key. It used to be
        // Windows', which stopped being true when DPAPI landed (#579) - a fixture that quotes a real message
        // should not outlive it.
        var resolver = Resolver(
            c => { Conn(c).Kind = "anthropic"; c.ActiveModelId = "claude-opus-5"; },
            storageUnavailable: "Secure key storage is not available on this platform.");

        Assert.Null(resolver.Resolve(out var problem));
        Assert.Contains("not available on this platform", problem);
        Assert.DoesNotContain("Add one in Settings", problem);
    }

    [Fact]
    public void An_openai_compatible_endpoint_needs_no_key()
    {
        // The motivating deployment: Ollama or LM Studio on loopback, which has no credential at all. Requiring
        // one here would lock out the configuration surface B was built to serve.
        var resolver = Resolver(c =>
        {
            Conn(c).Kind = "openai-compatible";
            Conn(c).BaseUrl = "http://localhost:11434/v1";
            c.ActiveModelId = "gemma4:cloud";
        });

        var resolution = resolver.Resolve(out var problem);

        Assert.Null(problem);
        Assert.IsType<OpenAiCompatibleProvider>(resolution!.Provider);
    }

    [Fact]
    public void An_openai_compatible_endpoint_without_a_base_url_is_refused()
    {
        // No default is possible: the base URL IS the provider.
        var resolver = Resolver(c => { Conn(c).Kind = "openai-compatible"; c.ActiveModelId = "deepseek-chat"; });

        Assert.Null(resolver.Resolve(out var problem));
        Assert.Contains("base URL", problem);
    }

    [Fact]
    public void No_model_is_refused_before_anything_else_is_examined()
    {
        var resolver = Resolver(c => { Conn(c).Kind = "anthropic"; c.ActiveModelId = null; }, apiKey: "sk-ant-x");

        Assert.Null(resolver.Resolve(out var problem));
        Assert.Contains("model", problem);
    }

    [Fact]
    public void The_master_switch_being_off_is_reported_as_a_setting_not_a_fault()
    {
        var resolver = Resolver(
            c => { Conn(c).Kind = "anthropic"; c.ActiveModelId = "claude-opus-5"; }, apiKey: "sk-ant-x", aiEnabled: false);

        Assert.Null(resolver.Resolve(out var problem));
        Assert.Contains("turned off", problem);
    }

    [Fact]
    public void An_unknown_provider_name_names_itself_in_the_message()
    {
        var resolver = Resolver(c => { Conn(c).Kind = "bedrock"; c.ActiveModelId = "x"; }, apiKey: "k");

        Assert.Null(resolver.Resolve(out var problem));
        Assert.Contains("bedrock", problem);
    }

    [Theory]
    [InlineData("anthropic", ChatProviderKind.Anthropic)]
    [InlineData("Claude", ChatProviderKind.Anthropic)]
    [InlineData("openai-compatible", ChatProviderKind.OpenAiCompatible)]
    [InlineData("openai_compatible", ChatProviderKind.OpenAiCompatible)]
    [InlineData("  OpenAI  ", ChatProviderKind.OpenAiCompatible)]
    public void Provider_names_are_forgiving_because_the_field_is_hand_edited(string value, ChatProviderKind expected)
    {
        // Until #585 there is no UI: this string is typed into settings.json by hand.
        Assert.True(ChatProviderResolver.TryParseKind(value, out var kind));
        Assert.Equal(expected, kind);
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("gpt")]
    public void An_unrecognised_provider_name_does_not_silently_pick_one(string? value)
    {
        Assert.False(ChatProviderResolver.TryParseKind(value, out _));
    }

    /// <summary>
    /// A header template nobody filled in is refused before sending, exactly as an unfilled base URL is.
    /// Sent, it reaches the wire as the literal text "Bearer {gatewayToken}" and returns a 401 the reader
    /// reads as a bad key — and in the gateway case that header IS the credential, so an unfinished one is
    /// an unfinished credential. (Fable review of #764, finding 4)
    /// </summary>
    [Fact]
    public void An_unfilled_header_template_is_refused_and_names_what_is_missing()
    {
        var resolver = Resolver(c =>
        {
            var conn = Conn(c);
            conn.Kind = "openai-compatible";
            conn.BaseUrl = "https://gateway.example/v1";
            conn.Headers["cf-aig-authorization"] = "Bearer {gatewayToken}";
            c.ActiveModelId = "some-model";
        }, apiKey: "sk-x");

        Assert.Null(resolver.Resolve(out var problem));
        Assert.Contains("gatewayToken", problem);
    }

    /// <summary>A header whose template WAS filled in resolves normally — the guard must not refuse everything
    /// with a brace in its history.</summary>
    [Fact]
    public void A_filled_header_template_resolves()
    {
        var resolver = Resolver(c =>
        {
            var conn = Conn(c);
            conn.Kind = "openai-compatible";
            conn.BaseUrl = "https://gateway.example/v1";
            conn.Headers["cf-aig-authorization"] = "Bearer {gatewayToken}";
            conn.Inputs["gatewayToken"] = "real-token";
            c.ActiveModelId = "some-model";
        }, apiKey: "sk-x");

        Assert.NotNull(resolver.Resolve(out var problem));
        Assert.Null(problem);
    }

    /// <summary>
    /// The widening from #711 must not swallow the keyless refusal for a connection that only carries
    /// something cosmetic. A header that cannot be a credential leaves the actionable message in place.
    /// </summary>
    [Fact]
    public void A_cosmetic_header_does_not_excuse_a_missing_anthropic_key()
    {
        var resolver = Resolver(c =>
        {
            var conn = Conn(c);
            conn.Kind = "anthropic";
            conn.Headers["X-Title"] = "  ";
            c.ActiveModelId = "claude-opus-5";
        });

        Assert.Null(resolver.Resolve(out var problem));
        Assert.Contains("API key", problem);
    }
}
