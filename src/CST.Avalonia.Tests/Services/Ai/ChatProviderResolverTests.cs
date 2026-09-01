using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models.Ai;
using System.Net.Http;
using CST.Avalonia.Tests.TestSupport;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.Services.Ai.Credentials;
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
        private readonly IReadOnlyDictionary<string, string>? _byName;

        internal FixedKey(
            string? key, string? unavailable = null, IReadOnlyDictionary<string, string>? byName = null)
        {
            _key = key;
            Unavailable = unavailable;
            _byName = byName;
        }

        public bool IsAvailable => Unavailable is null;
        public string? Unavailable { get; }

        /// <summary>Named secrets take precedence, so a test can store a gateway token without also standing in
        /// for the primary key (#771).</summary>
        public string? Get(string connectionId, string name) => Read(connectionId, name).Secret;

        /// <summary>Names the OS holds and will not hand over. (#926)</summary>
        internal HashSet<string> Unreadable { get; } = new(StringComparer.Ordinal);

        /// <summary>How many times a caller asked for a VALUE. Each is a possible macOS prompt. (#925)</summary>
        internal int ValueReads { get; private set; }

        public CredentialState Probe(string connectionId, string name)
        {
            if (!IsAvailable) return CredentialState.Unavailable;
            if (Unreadable.Contains(name)) return CredentialState.Unreadable;
            return ReadUncounted(connectionId, name).State;
        }

        public CredentialRead Read(string connectionId, string name)
        {
            ValueReads++;
            return ReadUncounted(connectionId, name);
        }

        private CredentialRead ReadUncounted(string connectionId, string name)
        {
            if (!IsAvailable) return CredentialRead.Unavailable;
            if (Unreadable.Contains(name)) return CredentialRead.Unreadable;
            var secret =
                _byName is not null && _byName.TryGetValue(name, out var named) ? named
                : name == AiCredentialNames.Primary ? _key
                : null;
            return secret is null ? CredentialRead.NotStored : CredentialRead.Found(secret);
        }
        public bool Set(string connectionId, string name, string secret) => throw new NotSupportedException();
        public bool Delete(string connectionId, string name) => throw new NotSupportedException();
    }

    private static ChatProviderResolver Resolver(
        Action<ChatSettings> configure, string? apiKey = null, bool aiEnabled = true,
        string? storageUnavailable = null, IReadOnlyDictionary<string, string>? secrets = null,
        IAiEnvironmentKeys? environmentKeys = null, IAiPresetSource? presets = null,
        HttpMessageHandler? handler = null, FixedKey? store = null)
    {
        var settings = new Settings();
        settings.Ai.Enabled = aiEnabled;
        settings.Ai.Chat.Enabled = true;
        configure(settings.Ai.Chat);

        var service = new Mock<ISettingsService>();
        service.SetupGet(s => s.Settings).Returns(settings);

        return new ChatProviderResolver(
            service.Object,
            store ?? (apiKey is null && storageUnavailable is null && secrets is null
                ? null
                : new FixedKey(apiKey, storageUnavailable, secrets)),
            NullLoggerFactory.Instance,
            handler is null
                ? new HttpClient { Timeout = System.Threading.Timeout.InfiniteTimeSpan }
                : new HttpClient(handler) { Timeout = System.Threading.Timeout.InfiniteTimeSpan },
            environmentKeys,
            presets);
    }

    // ---- environment keys (#714) ----

    private sealed class FakePresets : IAiPresetSource
    {
        public IReadOnlyList<AiProviderPreset> Presets { get; }
        public AiPresetState State => AiPresetState.Ready;
        public string? Problem => null;
        public event EventHandler? PresetsChanged { add { } remove { } }
        public Task EnsureLoadedAsync(CancellationToken ct = default) => Task.CompletedTask;
        public Task RefreshAsync(CancellationToken ct = default) => Task.CompletedTask;
        public FakePresets(params AiProviderPreset[] presets) => Presets = presets;
    }

    private static AiProviderPreset EnvPreset(string id, params string[] names) =>
        new(id, id.ToUpperInvariant(), ChatProviderKind.OpenAiCompatible, "https://example.invalid/v1",
            new AiCredentialMethod[] { new AiCredentialMethod.Env(names) }, Array.Empty<AiInputPrompt>());

    private static IAiEnvironmentKeys Env(string name, string? value) =>
        new AiEnvironmentKeys(n => n == name ? value : null);

    [Fact]
    public async Task A_connection_that_opted_in_authenticates_with_the_environment_key()
    {
        // Asserted on the wire, and on the ANTHROPIC kind, which requires a key. The first version used
        // openai-compatible — for which a key is deliberately optional — and asserted only that resolution
        // succeeded, so it passed with the whole environment fallback deleted. The feature's one positive
        // path had no test at all. (fable)
        var handler = StubHttpMessageHandler.Sse(
            "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n");

        var resolver = Resolver(chat =>
        {
            var c = Conn(chat);
            c.PresetId = "anthropic";
            c.UsesEnvironmentKey = true;
            c.EnvironmentVariable = "ANTHROPIC_API_KEY";
            c.Kind = "anthropic";
            chat.ActiveModelId = "claude-opus-5";
        }, environmentKeys: Env("ANTHROPIC_API_KEY", "sk-from-env"),
           presets: new FakePresets(EnvPreset("anthropic", "ANTHROPIC_API_KEY")),
           handler: handler);

        var resolution = resolver.Resolve(out var problem);
        Assert.NotNull(resolution);
        Assert.Null(problem);

        await foreach (var _ in resolution!.Provider.StreamAsync(
            new ChatRequest("claude-opus-5", 256, null,
                new[] { new ChatMessage(ChatRole.User, "hello") }), CancellationToken.None)) { }

        Assert.Equal("sk-from-env", string.Join(",", handler.LastRequest!.Headers.GetValues("x-api-key")));
    }

    // The opt-in is the whole feature. Discovery must never authenticate on its own — that is the difference
    // between this and an app that adopts a forgotten variable and spends the reader's money.
    /// <summary>
    /// A stored key we could not READ, with nothing behind it, is not "you have no key". (#926)
    ///
    /// <para><b>The request path was the half of #926 that was missed.</b> The row badged "Key locked" while
    /// pressing send still said "No API key is stored for Claude. Add one in Settings." — two surfaces
    /// contradicting, which is the failure class <c>Reachability</c> exists to prevent. Worse, the advice
    /// cannot work on macOS: the item exists, so re-entering runs SecItemAdd → duplicate → SecItemUpdate and
    /// needs the very authorization that was just declined. (fable)</para>
    /// </summary>
    [Fact]
    public void A_locked_key_is_not_reported_as_a_missing_one()
    {
        var store = new FixedKey("sk-ant-stored");
        store.Unreadable.Add(AiCredentialNames.Primary);

        var resolver = Resolver(chat =>
        {
            var c = Conn(chat);
            c.Kind = "anthropic";
            chat.ActiveModelId = "claude-opus-5";
        }, store: store);

        Assert.Null(resolver.Resolve(out var problem));
        Assert.NotNull(problem);
        Assert.DoesNotContain("No API key is stored", problem, StringComparison.Ordinal);
        Assert.Contains("stored", problem, StringComparison.OrdinalIgnoreCase);
        if (!OperatingSystem.IsWindows())
            Assert.Contains("Allow", problem, StringComparison.Ordinal);
    }

    /// <summary>
    /// A send reads each secret header once, not twice. (#925, fable)
    ///
    /// <para>The locked-header guard used to <c>Read</c> a header that <c>ExpandHeaders</c> had already read
    /// microseconds earlier — which is <i>why</i> its value was empty. On macOS that is a second
    /// authorization dialog for one send. The guard now asks <c>Probe</c>, which returns exactly what that
    /// read recorded.</para>
    ///
    /// <para>Counted rather than asserted as a message, because the count is the thing that reaches the
    /// reader: by this codebase's own metric, calls are prompts.</para>
    /// </summary>
    [Fact]
    public void A_send_reads_a_secret_header_once()
    {
        var store = new FixedKey("sk-ant-stored");
        store.Unreadable.Add(AiCredentialNames.Header("x-gateway-token"));

        var resolver = Resolver(chat =>
        {
            var c = Conn(chat);
            c.Kind = "anthropic";
            c.Headers = new List<AiHeaderRecord> { new() { Name = "x-gateway-token", Secret = true } };
            chat.ActiveModelId = "claude-opus-5";
        }, store: store);

        Assert.Null(resolver.Resolve(out var problem));
        Assert.NotNull(problem);
        Assert.Equal(1, store.ValueReads);
    }

    /// <summary>
    /// A locked stored key with an adopted variable still sends. (#926)
    ///
    /// <para>The resolver has always been <c>stored ?? environment</c>, and this asserts the fallback still
    /// applies when the stored half is unreadable rather than absent — the behaviour Settings now reports
    /// rather than contradicting. [fsnow: use the env var, and say so]</para>
    /// </summary>
    [Fact]
    public void A_locked_key_still_falls_through_to_an_adopted_environment_variable()
    {
        var store = new FixedKey("sk-ant-stored");
        store.Unreadable.Add(AiCredentialNames.Primary);

        var resolver = Resolver(chat =>
        {
            var c = Conn(chat);
            c.PresetId = "anthropic";
            c.UsesEnvironmentKey = true;
            c.EnvironmentVariable = "ANTHROPIC_API_KEY";
            c.Kind = "anthropic";
            chat.ActiveModelId = "claude-opus-5";
        }, environmentKeys: Env("ANTHROPIC_API_KEY", "sk-from-env"),
           presets: new FakePresets(EnvPreset("anthropic", "ANTHROPIC_API_KEY")),
           store: store);

        Assert.NotNull(resolver.Resolve(out var problem));
        Assert.Null(problem);
    }

    [Fact]
    public void A_connection_that_did_not_opt_in_does_not_use_the_environment_key()
    {
        var resolver = Resolver(chat =>
        {
            var c = Conn(chat);
            c.PresetId = "anthropic";
            c.UsesEnvironmentKey = false;          // discovered, not adopted
            c.Kind = "anthropic";
            chat.ActiveModelId = "claude-opus-5";
        }, environmentKeys: Env("ANTHROPIC_API_KEY", "sk-from-env"),
           presets: new FakePresets(EnvPreset("anthropic", "ANTHROPIC_API_KEY")));

        Assert.Null(resolver.Resolve(out var problem));
        Assert.NotNull(problem);
    }

    // Entering a key is a deliberate act; a variable is often forgotten. A reader who typed one must not find
    // the app quietly authenticating with something else.
    [Fact]
    public async Task A_stored_key_wins_over_one_in_the_environment()
    {
        // Asserted on the CREDENTIAL THAT REACHES THE ENDPOINT, not on the resolution succeeding. Both keys
        // resolve, so a non-null check cannot tell which was used — an earlier version of this test asserted
        // exactly that and passed with the precedence reversed, which is the failure it exists to catch.
        var handler = StubHttpMessageHandler.Sse(
            "event: message_stop\ndata: {\"type\":\"message_stop\"}\n\n");

        var resolver = Resolver(chat =>
        {
            var c = Conn(chat);
            c.PresetId = "anthropic";
            c.UsesEnvironmentKey = true;
            c.Kind = "anthropic";
            chat.ActiveModelId = "claude-opus-5";
        }, apiKey: "sk-stored",
           environmentKeys: Env("ANTHROPIC_API_KEY", "sk-from-env"),
           presets: new FakePresets(EnvPreset("anthropic", "ANTHROPIC_API_KEY")),
           handler: handler);

        var resolution = resolver.Resolve(out var problem);
        Assert.NotNull(resolution);
        Assert.Null(problem);

        await foreach (var _ in resolution!.Provider.StreamAsync(
            new ChatRequest("claude-opus-5", 256, null,
                new[] { new ChatMessage(ChatRole.User, "hello") }), CancellationToken.None)) { }

        Assert.Equal("sk-stored", string.Join(",", handler.LastRequest!.Headers.GetValues("x-api-key")));
    }

    // The reader unset it, which they are allowed to do. That reads as "no key", not as an error.
    [Fact]
    public void A_variable_that_disappeared_reads_as_no_key_rather_than_a_fault()
    {
        var resolver = Resolver(chat =>
        {
            var c = Conn(chat);
            c.PresetId = "anthropic";
            c.UsesEnvironmentKey = true;
            c.Kind = "anthropic";
            chat.ActiveModelId = "claude-opus-5";
        }, environmentKeys: Env("ANTHROPIC_API_KEY", null),
           presets: new FakePresets(EnvPreset("anthropic", "ANTHROPIC_API_KEY")));

        Assert.Null(resolver.Resolve(out var problem));
        Assert.NotNull(problem);
        Assert.Contains("key", problem!, StringComparison.OrdinalIgnoreCase);
    }

    // A custom endpoint records "" as its origin (#766). Matching that against a catalogue slug it happens to
    // resemble is how a reader's own connection would come to authenticate with someone else's key.
    [Fact]
    public async Task A_custom_connection_never_borrows_a_presets_environment_key()
    {
        var handler = StubHttpMessageHandler.Sse("data: [DONE]\n\n");
        var resolver = Resolver(chat =>
        {
            var c = Conn(chat);
            c.Id = "openai";               // an id that LOOKS like the preset
            c.PresetId = "";               // recorded as custom
            c.UsesEnvironmentKey = true;
            c.EnvironmentVariable = null;  // nothing was ever consented to
            c.Kind = "openai-compatible";
            c.BaseUrl = "https://my-own-gateway.invalid/v1";
            chat.ActiveConnectionId = "openai";
            chat.ActiveModelId = "gpt-4";
        }, environmentKeys: Env("OPENAI_API_KEY", "sk-from-env"),
           presets: new FakePresets(EnvPreset("openai", "OPENAI_API_KEY")),
           handler: handler);

        // Asserted on the ABSENCE of a credential on the wire. Non-null resolution proves nothing here:
        // OpenAI-compatible resolves with or without a key, so a version that borrowed the preset's key
        // would have passed this test while sending someone else's credential to the reader's own gateway.
        // (fable)
        var resolution = resolver.Resolve(out _);
        Assert.NotNull(resolution);

        await foreach (var _ in resolution!.Provider.StreamAsync(
            new ChatRequest("gpt-4", 256, null,
                new[] { new ChatMessage(ChatRole.User, "hello") }), CancellationToken.None)) { }

        Assert.False(handler.LastRequest!.Headers.Contains("Authorization"));
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
            conn.Headers.Add(new AiHeaderRecord { Name = "cf-aig-authorization", Value = "Bearer {gatewayToken}" });
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
            conn.Headers.Add(new AiHeaderRecord { Name = "cf-aig-authorization", Value = "Bearer {gatewayToken}" });
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
            conn.Headers.Add(new AiHeaderRecord { Name = "X-Title", Value = "  " });
            c.ActiveModelId = "claude-opus-5";
        });

        Assert.Null(resolver.Resolve(out var problem));
        Assert.Contains("API key", problem);
    }

    // ---- secret headers (#771) --------------------------------------------------------------------------

    /// <summary>
    /// A secret header's value is fetched at the moment the request is built rather than carried on the
    /// connection, which is what keeps it out of settings.json, out of the UI types and out of a log line.
    /// </summary>
    [Fact]
    public void A_secret_header_is_sent_with_its_stored_value()
    {
        var resolver = Resolver(c =>
        {
            var conn = Conn(c);
            conn.Kind = "openai-compatible";
            conn.BaseUrl = "https://gateway.example/v1";
            conn.Headers.Add(new AiHeaderRecord { Name = "cf-aig-authorization", Secret = true });
            c.ActiveModelId = "some-model";
        },
        apiKey: "sk-upstream",
        secrets: new Dictionary<string, string>
        {
            [AiCredentialNames.Header("cf-aig-authorization")] = "cf-token-abc",
        });

        var resolution = resolver.Resolve(out var problem);

        Assert.Null(problem);
        var options = Assert.IsType<OpenAiCompatibleProvider>(resolution!.Provider).Options;
        Assert.Equal("cf-token-abc", options.ExtraHeaders!["cf-aig-authorization"]);
    }

    /// <summary>
    /// The store was unavailable when it was saved, or the reader deleted the item in Keychain Access. Sent
    /// empty this comes back as a 401 that reads as a bad API key, sending them to re-paste the wrong thing.
    /// </summary>
    [Fact]
    public void A_secret_header_with_nothing_stored_is_refused_by_name()
    {
        var resolver = Resolver(c =>
        {
            var conn = Conn(c);
            conn.Kind = "openai-compatible";
            conn.BaseUrl = "https://gateway.example/v1";
            conn.Headers.Add(new AiHeaderRecord { Name = "cf-aig-authorization", Secret = true });
            c.ActiveModelId = "some-model";
        }, apiKey: "sk-upstream");

        Assert.Null(resolver.Resolve(out var problem));
        Assert.Contains("cf-aig-authorization", problem);
    }

    /// <summary>
    /// A credential is a literal, never a template. Expanding one would mangle a key that happens to contain
    /// braces, and there is no legitimate reason for a secret to reference an input.
    /// </summary>
    [Fact]
    public void A_secret_header_value_is_not_expanded_as_a_template()
    {
        var resolver = Resolver(c =>
        {
            var conn = Conn(c);
            conn.Kind = "openai-compatible";
            conn.BaseUrl = "https://gateway.example/v1";
            conn.Headers.Add(new AiHeaderRecord { Name = "x-token", Secret = true });
            conn.Inputs["gatewayToken"] = "substituted";
            c.ActiveModelId = "some-model";
        },
        apiKey: "sk-upstream",
        secrets: new Dictionary<string, string>
        {
            [AiCredentialNames.Header("x-token")] = "literal-{gatewayToken}-value",
        });

        var resolution = resolver.Resolve(out var problem);

        Assert.Null(problem);
        var options = Assert.IsType<OpenAiCompatibleProvider>(resolution!.Provider).Options;
        Assert.Equal("literal-{gatewayToken}-value", options.ExtraHeaders!["x-token"]);
    }

    /// <summary>
    /// The case that could not exist before #771, on the chat path: Anthropic with NO API key, authenticating
    /// entirely by a secret header.
    ///
    /// <para><c>AnthropicMessagesProvider.HasCredential</c> counts only headers with a non-blank VALUE — which
    /// was #764's fix, after a cosmetic <c>X-Title</c> let a keyless connection through and its 401 surfaced
    /// as "the provider rejected the API key", naming a key that did not exist. A secret header is blank in
    /// settings.json, so this resolves only because the value is pulled from the credential store BEFORE the
    /// credential check runs. Reorder those two and #689's "leave the key empty if you manage auth via
    /// headers" is defeated by the feature built to make it safe. (raised in review)</para>
    /// </summary>
    [Fact]
    public void Anthropic_resolves_when_the_only_credential_is_a_secret_header()
    {
        var resolver = Resolver(c =>
        {
            var conn = Conn(c);
            conn.Kind = "anthropic";
            conn.BaseUrl = "https://gateway.example";
            conn.Headers.Add(new AiHeaderRecord { Name = "x-api-key", Secret = true });
            c.ActiveModelId = "claude-opus-5";
        },
        secrets: new Dictionary<string, string>
        {
            [AiCredentialNames.Header("x-api-key")] = "secret-only-credential",
        });

        var resolution = resolver.Resolve(out var problem);

        Assert.Null(problem);
        Assert.NotNull(resolution);
    }

    /// <summary>The mirror of the above: with the secret NOT stored, the same connection must be refused by
    /// name rather than allowed through to send a blank header and 401.</summary>
    [Fact]
    public void Anthropic_is_refused_when_its_only_credential_is_a_secret_header_with_nothing_stored()
    {
        var resolver = Resolver(c =>
        {
            var conn = Conn(c);
            conn.Kind = "anthropic";
            conn.BaseUrl = "https://gateway.example";
            conn.Headers.Add(new AiHeaderRecord { Name = "x-api-key", Secret = true });
            c.ActiveModelId = "claude-opus-5";
        });

        Assert.Null(resolver.Resolve(out var problem));
        Assert.Contains("x-api-key", problem);
    }

    // ---- a secret prompt answer reaching a header template (#777) ---------------------------------------

    /// <summary>
    /// The legitimate destination for a secret prompt: a header template. Cloudflare's gateway token is the
    /// case in view — the value lives in the credential store, the key lives in <c>SecretInputs</c>, and it is
    /// substituted at the last possible moment rather than being written back into <c>Inputs</c>, which is
    /// what the save path persists.
    /// </summary>
    [Fact]
    public void A_header_template_substitutes_a_secret_input_from_the_credential_store()
    {
        var resolver = Resolver(c =>
        {
            var conn = Conn(c);
            conn.Kind = "openai-compatible";
            conn.BaseUrl = "https://gateway.example/v1";
            conn.Headers.Add(new AiHeaderRecord
            {
                Name = "cf-aig-authorization",
                Value = "Bearer {gatewayToken}",
            });
            conn.Inputs["accountId"] = "acct-123";
            conn.SecretInputs = new List<string> { "gatewayToken" };
            c.ActiveModelId = "some-model";
        },
        apiKey: "sk-upstream",
        secrets: new Dictionary<string, string>
        {
            [AiCredentialNames.Input("gatewayToken")] = "tok-live",
        });

        var resolution = resolver.Resolve(out var problem);

        Assert.Null(problem);
        var options = Assert.IsType<OpenAiCompatibleProvider>(resolution!.Provider).Options;
        Assert.Equal("Bearer tok-live", options.ExtraHeaders!["cf-aig-authorization"]);
    }

    /// <summary>
    /// A secret input with nothing stored leaves its placeholder in the header rather than filling it with an
    /// empty string — and the refusal that already guards unfinished header templates then names the field.
    ///
    /// <para>That naming is the whole reason for leaving it unexpanded. Substituting an empty string would
    /// send <c>Bearer </c> on the wire and come back a 401 that reads as a bad credential, which is the #711
    /// complaint arriving from a new direction; the reader would be sent to re-paste a key that was never the
    /// problem. This asserts the message, not just the header, because the message is the feature.</para>
    /// </summary>
    [Fact]
    public void A_secret_input_with_nothing_stored_leaves_the_placeholder_rather_than_blanking_it()
    {
        var resolver = Resolver(c =>
        {
            var conn = Conn(c);
            conn.Kind = "openai-compatible";
            conn.BaseUrl = "https://gateway.example/v1";
            conn.Headers.Add(new AiHeaderRecord
            {
                Name = "cf-aig-authorization",
                Value = "Bearer {gatewayToken}",
            });
            conn.SecretInputs = new List<string> { "gatewayToken" };
            c.ActiveModelId = "some-model";
        },
        apiKey: "sk-upstream",
        secrets: new Dictionary<string, string>());

        var resolution = resolver.Resolve(out var problem);

        Assert.Null(resolution);
        Assert.NotNull(problem);

        // Named, so the reader knows WHICH field is unfilled rather than being told the provider said no.
        Assert.Contains("gatewayToken", problem);
    }

    /// <summary>
    /// A connection with no secret inputs behaves exactly as before — the substitution dictionary is the
    /// plain <c>Inputs</c> object, not a copy, so nothing about the ordinary path changed.
    /// </summary>
    [Fact]
    public void A_plain_input_still_substitutes_into_a_header_template()
    {
        var resolver = Resolver(c =>
        {
            var conn = Conn(c);
            conn.Kind = "openai-compatible";
            conn.BaseUrl = "https://gateway.example/v1";
            conn.Headers.Add(new AiHeaderRecord { Name = "x-account", Value = "{accountId}" });
            conn.Inputs["accountId"] = "acct-123";
            c.ActiveModelId = "some-model";
        },
        apiKey: "sk-upstream");

        var resolution = resolver.Resolve(out var problem);

        Assert.Null(problem);
        var options = Assert.IsType<OpenAiCompatibleProvider>(resolution!.Provider).Options;
        Assert.Equal("acct-123", options.ExtraHeaders!["x-account"]);
    }
}
