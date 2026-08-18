using System;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services.Ai.Credentials;

/// <summary>
/// Where surface B's API keys live: the OS credential store, never a file we write. (#579, AI_SURFACE_B.md §6)
///
/// <para><b>Never <c>settings.json</c>.</b> That file is hand-edited, screenshotted, attached to bug reports and
/// synced between machines. A key in it is a key in every one of those places, and unlike a wrong port number
/// there is no way to notice after the fact.</para>
///
/// <para><b>Read lazily, on the request that needs it.</b> Nothing here runs at startup, so a user who never
/// turns surface B on never triggers a Keychain access (or, on Windows, a decrypt). That is the same class of
/// mistake as the Chromium Safe Storage prompt this app already works around: a permission dialog on launch,
/// for a feature the user has not asked for, teaches them to click through prompts.</para>
///
/// <para><b>Nothing is cached.</b> A cache would go stale the moment the user changes the key in Settings, and
/// the lookup costs microseconds — the wrong side of that trade is the one where the app keeps using a
/// credential the user has already replaced.</para>
///
/// <para><b>Nothing is ever logged.</b> Not the value, not a prefix, not its length. Outcomes only.</para>
/// </summary>
public sealed class AiCredentialStore : IAiCredentialStore
{
    /// <summary>
    /// The service name: a Keychain service on macOS, a directory name on Windows. Stable and app-scoped:
    /// changing it would orphan every key already stored, with no error — the user would simply be told they
    /// had not configured one.
    /// </summary>
    // Escaped rather than a literal em dash: this string names the Keychain item on macOS and the on-disk
    // directory on Windows, so a codepage-changing resave would orphan every stored key with no error.
    internal const string ServiceName = "CST Reader — AI provider";

    private readonly ILogger<AiCredentialStore> _logger;
    private readonly string _service;

    public AiCredentialStore(ILogger<AiCredentialStore> logger) : this(logger, ServiceName) { }

    /// <summary>
    /// Test seam: a distinct service name, so a test can never overwrite or delete the developer's own stored
    /// key. Sharing the real name would make running the suite a way to lose a credential.
    /// </summary>
    internal AiCredentialStore(ILogger<AiCredentialStore> logger, string service)
    {
        _logger = logger;
        _service = service;
    }

    /// <summary>
    /// Whether this platform has somewhere safe to put a key.
    ///
    /// <para><b>Linux is deliberately false</b> rather than accidentally unhandled. Secret Service / libsecret
    /// is the right answer there and is unbuilt while Linux is unshipped; until then a key-requiring provider
    /// reports itself unconfigured, which is honest. A local endpoint that needs no key is unaffected, so the
    /// privacy-first configuration still works on Linux.</para>
    ///
    /// <para><b>Windows uses DPAPI</b> (CurrentUser scope) over a file per provider in the app data directory,
    /// developed and exercised on a real Windows target — see <see cref="WindowsDpapiStore"/>. It can report
    /// false there too, when the data directory cannot be written.</para>
    /// </summary>
    public bool IsAvailable =>
        OperatingSystem.IsMacOS() ? MacOsKeychain.IsAvailable :
        OperatingSystem.IsWindows() ? WindowsDpapiStore.IsAvailable :
        false;

    /// <summary>Why there is nowhere to store a key, for the message the user actually reads. Null when fine.</summary>
    public string? Unavailable =>
        IsAvailable ? null
        : OperatingSystem.IsWindows()
            // DPAPI is always present on Windows, so reaching here means the data directory could not be
            // written — a full disk or a locked-down profile. Say that, rather than something the user would
            // reasonably read as "this app does not support Windows".
            ? "An API key could not be stored: CST Reader's data folder is not writable. "
              + "An endpoint that needs no API key still works."
        : OperatingSystem.IsMacOS()
            ? "The macOS Keychain could not be reached, so no API key can be stored."
        : "Secure key storage is not available on this platform. "
          + "An endpoint that needs no API key still works.";

    public string? GetApiKey(ChatProviderKind provider)
    {
        if (!IsAvailable) return null;

        var key = OperatingSystem.IsWindows()
            ? WindowsDpapiStore.Find(_service, AccountFor(provider))
            : MacOsKeychain.Find(_service, AccountFor(provider));

        // The OUTCOME, never the value — and not its length either, which narrows a guess.
        _logger.LogDebug("Credential lookup for {Provider}: {Result}",
            provider, key is null ? "none stored" : "found");

        return key;
    }

    /// <summary>Store or replace the key for a provider. Returns false when the platform cannot.</summary>
    public bool SetApiKey(ChatProviderKind provider, string apiKey)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(apiKey)) return false;

        var saved = OperatingSystem.IsWindows()
            ? WindowsDpapiStore.Save(_service, AccountFor(provider), apiKey.Trim())
            : MacOsKeychain.Save(_service, AccountFor(provider), apiKey.Trim());
        _logger.LogInformation("Stored an API key for {Provider}: {Result}",
            provider, saved ? "ok" : "failed");
        return saved;
    }

    /// <summary>Forget the key for a provider. Forgetting one that was never stored counts as success.</summary>
    public bool DeleteApiKey(ChatProviderKind provider)
    {
        if (!IsAvailable) return false;

        var deleted = OperatingSystem.IsWindows()
            ? WindowsDpapiStore.Delete(_service, AccountFor(provider))
            : MacOsKeychain.Delete(_service, AccountFor(provider));
        _logger.LogInformation("Removed the API key for {Provider}: {Result}",
            provider, deleted ? "ok" : "failed");
        return deleted;
    }

    /// <summary>
    /// One item per provider, so a user can keep a Claude key and an OpenAI-compatible key at once — which is
    /// the ordinary case for someone comparing a hosted model against a local one.
    /// </summary>
    internal static string AccountFor(ChatProviderKind provider) => provider switch
    {
        ChatProviderKind.Anthropic => "anthropic",
        ChatProviderKind.OpenAiCompatible => "openai-compatible",
        _ => provider.ToString().ToLowerInvariant(),
    };
}
