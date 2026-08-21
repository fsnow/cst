using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using CST.Avalonia.Models;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services.Ai;

/// <summary>Which wire format a configured endpoint speaks.</summary>
public enum ChatProviderKind
{
    /// <summary>The Anthropic Messages API. The standing default (AI_INTEGRATION.md §11.1).</summary>
    Anthropic,

    /// <summary>The OpenAI-compatible Chat Completions shape — DeepSeek, OpenRouter, Ollama, LM Studio, …</summary>
    OpenAiCompatible,
}

/// <summary>
/// Where the API key lives. <b>Declared here, implemented by #579</b> (Keychain on macOS, DPAPI on Windows).
///
/// <para>Resolved with <c>GetService</c> rather than <c>GetRequiredService</c>, exactly as the optional
/// DPD-lemma asset is: its absence is a supported configuration that the resolver reports, not a wiring error.
/// That is what lets surface B run today against a local endpoint that needs no key at all.</para>
/// </summary>
public interface IAiCredentialStore
{
    /// <summary>Whether this platform has somewhere safe to put a key at all.</summary>
    bool IsAvailable { get; }

    /// <summary>Why not, phrased for the user to read. Null when storage is available.</summary>
    string? Unavailable { get; }

    /// <summary>
    /// The stored key for a CONNECTION, or null when none is stored. (#678)
    ///
    /// <para>Keyed by connection id rather than by provider kind, which was a two-member enum — so every
    /// OpenAI-compatible endpoint shared a single slot, and configuring a second one silently overwrote the
    /// first.</para>
    /// </summary>
    string? GetApiKey(string connectionId);

    /// <summary>Store or replace a connection's key. False when the platform cannot.</summary>
    bool SetApiKey(string connectionId, string apiKey);

    /// <summary>Forget a connection's key. Forgetting one never stored counts as success.</summary>
    bool DeleteApiKey(string connectionId);
}

/// <summary>
/// A configured provider, ready to call.
/// </summary>
/// <param name="Model">The model id, verbatim as the user typed it. Never validated against a list — the
/// OpenAI-compatible shape serves arbitrary endpoints, so any list we shipped would be wrong within a month.</param>
public sealed record ChatProviderResolution(IChatProvider Provider, string Model);

/// <summary>Resolves the configured provider, or explains why there isn't one.</summary>
public interface IChatProviderResolver
{
    /// <summary>
    /// The configured provider, or null with <paramref name="problem"/> describing what the user must set.
    /// Returns rather than throws: "not configured" is the ordinary state of a feature that ships off, and the
    /// panel needs to render an explanation, not an exception (AI_SURFACE_B.md §10).
    /// </summary>
    ChatProviderResolution? Resolve(out string? problem);
}

/// <summary>
/// Builds a provider from settings. (#583)
///
/// <para><b>What this deliberately does not own.</b> The API key comes from <see cref="IAiCredentialStore"/>
/// (#579) and the UI that sets any of this is #585. What lives here is only the resolution: settings plus a key
/// in, a callable provider out. That split is why the orchestrator can be built and tested before either of
/// those exists.</para>
///
/// <para><b>A missing key is not always a misconfiguration.</b> The motivating deployment for the
/// OpenAI-compatible adapter is a local runner — Ollama, LM Studio — reached over loopback with no credential
/// at all. So the key is required for Anthropic and optional for OpenAI-compatible, and "no key" is reported as
/// a problem only where it actually is one.</para>
/// </summary>
public sealed class ChatProviderResolver : IChatProviderResolver
{
    private readonly ISettingsService _settings;
    private readonly IAiCredentialStore? _credentials;
    private readonly ILoggerFactory _loggerFactory;
    private readonly HttpClient _http;

    public ChatProviderResolver(
        ISettingsService settings,
        IAiCredentialStore? credentials,
        ILoggerFactory loggerFactory)
        : this(settings, credentials, loggerFactory, CreateHttpClient())
    {
    }

    /// <summary>Test seam: supply a client over a stub handler instead of reaching the network.</summary>
    internal ChatProviderResolver(
        ISettingsService settings,
        IAiCredentialStore? credentials,
        ILoggerFactory loggerFactory,
        HttpClient http)
    {
        _settings = settings;
        _credentials = credentials;
        _loggerFactory = loggerFactory;
        _http = http;
    }

    /// <summary>
    /// One client for the app's lifetime, and it <b>must</b> have an infinite timeout — the providers reject a
    /// finite one outright. <c>HttpClient</c>'s 100-second default would silently kill a long generation, which
    /// is why the adapters carry their own idle and first-event timeouts instead: those bound the gap BETWEEN
    /// events, which is the thing that actually indicates a dead stream, rather than the total duration, which
    /// on a long answer indicates nothing at all.
    ///
    /// <para>Reused rather than created per turn because a fresh <c>HttpClient</c> per request exhausts sockets
    /// under any real usage. The usual counter-argument — stale DNS on a long-lived client — is weak here: the
    /// endpoint is a user-configured address in a desktop app, not a rotating service mesh.</para>
    /// </summary>
    private static HttpClient CreateHttpClient() => new() { Timeout = Timeout.InfiniteTimeSpan };

    public ChatProviderResolution? Resolve(out string? problem)
    {
        var chat = _settings.Settings.Ai.Chat;

        // The active connection replaces the scalar provider/base-URL/model this used to read (#689). Null
        // when nothing is configured yet, which is a different problem from a misconfigured one and gets its
        // own message below.
        var connection = chat.Connections.FirstOrDefault(
            c => string.Equals(c.Id, chat.ActiveConnectionId, StringComparison.Ordinal))
            ?? chat.Connections.FirstOrDefault();

        // Two switches, two messages. One message for both sent a reader whose master switch was already ON
        // to look at a switch that was already on, with nothing to change — the exact failure this class is
        // written to avoid everywhere else, committed in its own first sentence. (#667)
        if (!_settings.Settings.Ai.Enabled)
        {
            problem = "AI features are turned off. Turn on \"Enable AI Features\" in Settings \u2192 AI.";
            return null;
        }

        if (!chat.Enabled)
        {
            problem = "The assistant is turned off. Turn on \"Enable the assistant\" in Settings \u2192 AI.";
            return null;
        }

        if (connection is null)
        {
            problem = "No provider is configured. Add one in Settings \u2192 AI.";
            return null;
        }

        if (string.IsNullOrWhiteSpace(chat.ActiveModelId))
        {
            problem = "No model is configured. Choose one in Settings.";
            return null;
        }

        if (!TryParseKind(connection.Kind, out var kind))
        {
            problem = $"'{connection.Kind}' is not a provider this build knows how to talk to.";
            return null;
        }

        // A connection whose URL still contains an unfilled {placeholder} - Azure without its resource name -
        // must be refused HERE. Sent anyway it fails as a DNS error naming nothing, which tells the reader
        // neither what is wrong nor where to fix it. (#689)
        var baseUrl = CST.Avalonia.Models.Ai.AiTemplate.Expand(connection.BaseUrl, connection.Inputs);
        if (CST.Avalonia.Models.Ai.AiTemplate.HasUnresolvedPlaceholders(baseUrl))
        {
            var missing = string.Join(", ", CST.Avalonia.Models.Ai.AiTemplate.PlaceholdersIn(baseUrl));
            problem = $"This provider still needs: {missing}. Fill it in under Settings \u2192 AI.";
            return null;
        }

        var apiKey = _credentials?.GetApiKey(connection.Id);

        switch (kind)
        {
            case ChatProviderKind.Anthropic
                when string.IsNullOrWhiteSpace(apiKey) && !AnthropicMessagesProvider.HasCredential(
                    new AnthropicOptions(apiKey, null, ExpandHeaders(connection))):
                // Two different problems with two different fixes: "you have not entered a key" is solved in
                // Settings; "this build cannot store one" is not solved there at all, and telling the user to
                // go and add one would send them somewhere that cannot help. (#579)
                //
                // A connection carrying headers is a third case and is NOT refused: the headers may be the
                // credential, which is what "leave the key empty if you manage auth via headers" means. (#711)
                problem = _credentials?.Unavailable
                          ?? "No API key is stored for Claude. Add one in Settings.";
                return null;

            case ChatProviderKind.Anthropic:
                problem = null;
                return new ChatProviderResolution(
                    new AnthropicMessagesProvider(
                        _http,
                        new AnthropicOptions(apiKey, NullIfBlank(baseUrl), ExpandHeaders(connection)),
                        _loggerFactory.CreateLogger<AnthropicMessagesProvider>(),
                        firstEventTimeout: AiEndpoint.FirstEventTimeoutFor(baseUrl)),
                    chat.ActiveModelId!.Trim());

            case ChatProviderKind.OpenAiCompatible when string.IsNullOrWhiteSpace(baseUrl):
                // No default is possible here — the base URL IS the provider. This is the field that makes one
                // adapter serve DeepSeek, OpenRouter, Ollama and LM Studio.
                problem = "No endpoint address is configured. Enter the provider's base URL in Settings.";
                return null;

            case ChatProviderKind.OpenAiCompatible:
                // A key is deliberately NOT required — a local runner on loopback has none.
                problem = null;
                return new ChatProviderResolution(
                    new OpenAiCompatibleProvider(
                        _http,
                        new OpenAiCompatibleOptions(
                            baseUrl.Trim(),
                            NullIfBlank(apiKey),
                            connection.AuthHeaderName,
                            connection.AuthScheme,
                            ExpandHeaders(connection)),
                        _loggerFactory.CreateLogger<OpenAiCompatibleProvider>(),
                        firstEventTimeout: AiEndpoint.FirstEventTimeoutFor(baseUrl)),
                    chat.ActiveModelId!.Trim());

            default:
                problem = $"'{connection.Kind}' is not a provider this build knows how to talk to.";
                return null;
        }
    }

    internal static bool TryParseKind(string? value, out ChatProviderKind kind)
    {
        switch (value?.Trim().ToLowerInvariant())
        {
            case "anthropic" or "claude":
                kind = ChatProviderKind.Anthropic;
                return true;
            // Hyphen and underscore both, because this string is hand-edited in settings.json until #585.
            case "openai-compatible" or "openai_compatible" or "openai":
                kind = ChatProviderKind.OpenAiCompatible;
                return true;
            default:
                kind = default;
                return false;
        }
    }

    private static string? NullIfBlank(string? value) => string.IsNullOrWhiteSpace(value) ? null : value;

    /// <summary>
    /// A connection's headers, with their values expanded against its inputs.
    ///
    /// <para>Header VALUES are templates just as the base URL is — Cloudflare and Azure both put a
    /// reader-supplied input inside one. Shared by both provider kinds so that neither can quietly stop
    /// sending them, which is how the Anthropic adapter came to drop every header it was given. (#711)</para>
    /// </summary>
    private static IReadOnlyDictionary<string, string> ExpandHeaders(AiConnectionRecord connection) =>
        connection.Headers.ToDictionary(
            h => h.Key,
            h => CST.Avalonia.Models.Ai.AiTemplate.Expand(h.Value, connection.Inputs));
}
