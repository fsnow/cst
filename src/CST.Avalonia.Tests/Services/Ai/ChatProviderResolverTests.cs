using System;
using System.Net.Http;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
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
        public string? GetApiKey(ChatProviderKind provider) => _key;
        public bool SetApiKey(ChatProviderKind provider, string apiKey) => throw new NotSupportedException();
        public bool DeleteApiKey(ChatProviderKind provider) => throw new NotSupportedException();
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

    [Fact]
    public void Anthropic_resolves_with_a_stored_key()
    {
        var resolver = Resolver(c => { c.Provider = "anthropic"; c.Model = " claude-opus-5 "; }, apiKey: "sk-ant-x");

        var resolution = resolver.Resolve(out var problem);

        Assert.Null(problem);
        Assert.IsType<AnthropicMessagesProvider>(resolution!.Provider);
        Assert.Equal("claude-opus-5", resolution.Model);   // trimmed, otherwise verbatim
    }

    [Fact]
    public void Anthropic_without_a_key_says_so_rather_than_failing_at_the_wire()
    {
        var resolver = Resolver(c => { c.Provider = "anthropic"; c.Model = "claude-opus-5"; });

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
            c => { c.Provider = "anthropic"; c.Model = "claude-opus-5"; },
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
            c.Provider = "openai-compatible";
            c.BaseUrl = "http://localhost:11434/v1";
            c.Model = "gemma4:cloud";
        });

        var resolution = resolver.Resolve(out var problem);

        Assert.Null(problem);
        Assert.IsType<OpenAiCompatibleProvider>(resolution!.Provider);
    }

    [Fact]
    public void An_openai_compatible_endpoint_without_a_base_url_is_refused()
    {
        // No default is possible: the base URL IS the provider.
        var resolver = Resolver(c => { c.Provider = "openai-compatible"; c.Model = "deepseek-chat"; });

        Assert.Null(resolver.Resolve(out var problem));
        Assert.Contains("base URL", problem);
    }

    [Fact]
    public void No_model_is_refused_before_anything_else_is_examined()
    {
        var resolver = Resolver(c => { c.Provider = "anthropic"; c.Model = null; }, apiKey: "sk-ant-x");

        Assert.Null(resolver.Resolve(out var problem));
        Assert.Contains("model", problem);
    }

    [Fact]
    public void The_master_switch_being_off_is_reported_as_a_setting_not_a_fault()
    {
        var resolver = Resolver(
            c => { c.Provider = "anthropic"; c.Model = "claude-opus-5"; }, apiKey: "sk-ant-x", aiEnabled: false);

        Assert.Null(resolver.Resolve(out var problem));
        Assert.Contains("turned off", problem);
    }

    [Fact]
    public void An_unknown_provider_name_names_itself_in_the_message()
    {
        var resolver = Resolver(c => { c.Provider = "bedrock"; c.Model = "x"; }, apiKey: "k");

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
}
