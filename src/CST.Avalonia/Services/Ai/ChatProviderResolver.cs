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
    /// <summary>Whether this platform has somewhere safe to put a secret at all.</summary>
    bool IsAvailable { get; }

    /// <summary>Why not, phrased for the user to read. Null when storage is available.</summary>
    string? Unavailable { get; }

    /// <summary>
    /// One stored secret, or null when none is stored.
    ///
    /// <para><b>Keyed by connection AND name</b> (#759). By connection because a two-member provider enum once
    /// meant every OpenAI-compatible endpoint shared a slot (#678); by name because a single request can need
    /// more than one secret — Cloudflare's gateway wants a gateway token beside the upstream key (#701),
    /// Bedrock a secret access key beside an access key id (#702). A connection with one secret simply calls
    /// it <see cref="AiCredentialNames.Primary"/>.</para>
    /// </summary>
    string? Get(string connectionId, string name);

    /// <summary>Store or replace one named secret. False when the platform cannot.</summary>
    bool Set(string connectionId, string name, string secret);

    /// <summary>Forget one named secret. Forgetting one never stored counts as success.</summary>
    bool Delete(string connectionId, string name);
}

/// <summary>
/// The names a connection files its secrets under. (#759)
///
/// <para><b>A name is part of the storage address</b>, so a typo in one is a credential that cannot be found
/// again rather than an error anyone sees. Most are constants: presets declare which names they need and the
/// reader only ever fills them in. <see cref="Header"/> is the exception — it derives a name from a header
/// name the reader typed — which is why it folds through <see cref="Slug"/> and why
/// <c>AiConnectionService</c> refuses two secret headers that would fold together. (#771)</para>
/// </summary>
public static class AiCredentialNames
{
    /// <summary>The credential the auth header carries — what every provider in the catalogue needs, and for
    /// almost all of them the only one.</summary>
    public const string Primary = "primary";

    /// <summary>
    /// The name a header's secret value is filed under. (#771)
    ///
    /// <para><b>Derived from the header rather than generated.</b> A serial number would be collision-proof
    /// for free, but one of the stated reasons for N accounts over a single blob is that a reader can see in
    /// Keychain Access what this app is holding and revoke one item — and <c>header-1</c> tells them nothing.
    /// Derivation costs a uniqueness check instead, which <see cref="AiConnectionService"/> makes at the point
    /// of save.</para>
    /// </summary>
    public static string Header(string headerName) => "header-" + Slug(headerName);

    /// <summary>
    /// The character folding every part of an account name goes through, defined here so that the code which
    /// checks two names for collision and the code which stores under them cannot disagree.
    ///
    /// <para>Emits only <c>[a-z0-9-_]</c>. That is what makes an account string splittable — see
    /// <c>AiCredentialStore.AccountFor</c>, whose separator is chosen to be a character this can never
    /// produce. Widening this set reopens the collision documented there.</para>
    /// </summary>
    public static string Slug(string value)
    {
        if (string.IsNullOrWhiteSpace(value)) return "default";

        var chars = value.Trim().ToLowerInvariant().ToCharArray();
        for (int i = 0; i < chars.Length; i++)
            if (!(char.IsAsciiLetterOrDigit(chars[i]) || chars[i] is '-' or '_'))
                chars[i] = '-';
        return new string(chars);
    }
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

        // Two headers with the same name. Refused at the service on every write path, so reaching here means
        // a hand-edited settings.json - which is a supported way in, and used to be impossible to get wrong
        // while headers were a dictionary. Named here rather than left to the projection below, which would
        // otherwise surface as "An item with the same key has already been added" through a generic catch.
        // (#771, fable review)
        var duplicate = connection.Headers
            .Where(h => !string.IsNullOrWhiteSpace(h.Name))
            .GroupBy(h => h.Name.Trim(), StringComparer.OrdinalIgnoreCase)
            .FirstOrDefault(g => g.Count() > 1);
        if (duplicate is not null)
        {
            problem = $"This connection has two {duplicate.Key} headers. Remove one under Settings \u2192 AI.";
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

        // Same guard as the base URL above, for the same reason. An unfilled {placeholder} in a header value
        // reaches the wire verbatim and comes back as a 401 the reader would read as a bad key - the header
        // IS the credential in the gateway case, so an unfinished one is an unfinished credential. (#711)
        var headers = ExpandHeaders(connection);

        // Secret values are excluded: they are literals, never templates, so a brace in one is part of the
        // credential rather than an unfilled field. Scanning them refuses a perfectly good key for containing
        // a character, with a message naming a placeholder that does not exist. (#771)
        var secretHeaderNames = connection.Headers
            .Where(h => h.Secret)
            .Select(h => h.Name)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        var unfinished = headers
            .Where(h => !secretHeaderNames.Contains(h.Key))
            .Where(h => CST.Avalonia.Models.Ai.AiTemplate.HasUnresolvedPlaceholders(h.Value))
            .SelectMany(h => CST.Avalonia.Models.Ai.AiTemplate.PlaceholdersIn(h.Value))
            .Distinct()
            .ToList();
        if (unfinished.Count > 0)
        {
            problem = $"This provider still needs: {string.Join(", ", unfinished)}. "
                      + "Fill it in under Settings \u2192 AI.";
            return null;
        }

        // A header marked secret whose secret is not stored - the store was unavailable when it was saved, or
        // the reader deleted the item in Keychain Access. Sending the header empty produces a 401 that reads
        // as a bad key, so say which header is missing instead. (#771)
        var missingSecrets = connection.Headers
            .Where(h => h.Secret && string.IsNullOrEmpty(headers.GetValueOrDefault(h.Name)))
            .Select(h => h.Name)
            .ToList();
        if (missingSecrets.Count > 0)
        {
            problem = $"No stored value for the {string.Join(", ", missingSecrets)} header. "
                      + "Re-enter it under Settings \u2192 AI.";
            return null;
        }

        var apiKey = _credentials?.Get(connection.Id, AiCredentialNames.Primary);

        switch (kind)
        {
            case ChatProviderKind.Anthropic
                when string.IsNullOrWhiteSpace(apiKey) && !AnthropicMessagesProvider.HasCredential(
                    new AnthropicOptions(apiKey, null, headers)):
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
                        new AnthropicOptions(apiKey, NullIfBlank(baseUrl), headers),
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
                            headers),
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
    /// <summary>
    /// The headers as they go on the wire: templated values filled in from the reader's inputs, and secret
    /// values fetched from the credential store at this moment rather than carried around. (#711, #771)
    ///
    /// <para><b>A secret value is not expanded as a template.</b> A credential is a literal; running one
    /// through <see cref="CST.Avalonia.Models.Ai.AiTemplate"/> would mean a key containing braces came out
    /// mangled, and there is no legitimate reason for a secret to reference an input.</para>
    ///
    /// <para>A secret with nothing stored yields an empty string, which the caller treats as an unfinished
    /// credential rather than sending a blank header — see the guard at the call site.</para>
    /// </summary>
    private IReadOnlyDictionary<string, string> ExpandHeaders(AiConnectionRecord connection)
    {
        // Indexer assignment rather than ToDictionary: a duplicate name is refused before this runs, so this
        // cannot be reached with one - and if a later edit moves the guard, the reader should get a wrong
        // header rather than an exception surfacing as "Something went wrong running that request".
        var headers = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var header in connection.Headers)
            headers[header.Name] = header.Secret
                ? _credentials?.Get(connection.Id, AiCredentialNames.Header(header.Name)) ?? string.Empty
                : CST.Avalonia.Models.Ai.AiTemplate.Expand(header.Value ?? string.Empty, connection.Inputs);

        return headers;
    }
}
