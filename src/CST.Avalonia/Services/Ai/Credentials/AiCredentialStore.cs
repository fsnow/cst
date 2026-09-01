using System;
using CST.Avalonia.Services.Ai;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services.Ai.Credentials;

/// <summary>
/// Where surface B's API keys live: the OS credential store, never a file we write. (#579, AI_SURFACE_B.md §6)
///
/// <para><b>Never <c>settings.json</c>.</b> That file is hand-edited, screenshotted, attached to bug reports and
/// synced between machines. A key in it is a key in every one of those places, and unlike a wrong port number
/// there is no way to notice after the fact.</para>
///
/// <para><b>Reads should be lazy, on the request that needs it, and today they are not</b> — tracked as #925.
/// The intent was that nothing here runs at startup, so a user who never turns surface B on never triggers a
/// Keychain access (or, on Windows, a decrypt): a permission dialog on launch, for a feature the user has not
/// asked for, teaches them to click through prompts. That is exactly what went on to happen — the maintainer
/// met eight authorization prompts before a beta candidate was usable. Status queries reach
/// <see cref="Read"/> at startup and on every refresh, and nothing caches, so one connection was read 153
/// times in a single session. Do not read this paragraph as a description of current behaviour.</para>
///
/// <para><b>Nothing is ever logged.</b> Not the value, not a prefix, not its length. Outcomes only —
/// <see cref="CredentialRead.Describe"/> is the whole vocabulary, and it says nothing about the value.</para>
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
    internal const string ServiceName = "CST Reader \u2014 AI provider";

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

    public string? Get(string connectionId, string name) => Read(connectionId, name).Secret;

    public CredentialRead Read(string connectionId, string name)
    {
        if (!IsAvailable) return CredentialRead.Unavailable;

        var account = AccountFor(connectionId, name);
        var read = OperatingSystem.IsWindows()
            ? WindowsDpapiStore.Find(_service, account)
            : MacOsKeychain.Find(_service, account);

        // The OUTCOME, never the value — and not its length either, which narrows a guess.
        //
        // The NAME is logged, and after #771 that is no longer simply "ours": a secret header's name is
        // derived from a header name the reader typed. Logged anyway, deliberately — an HTTP header name
        // travels on every request in the clear, so it is public in a way its value never is, and "which of
        // this connection's secrets was missing" is the whole diagnosis when a two-credential provider 401s.
        // The line to hold is the value, and it is held here and at every other call. (fable review)
        //
        // Four outcomes, not two (#926). The line that read "none stored" for every one of them is what made
        // eight declined authorization prompts look like three connections with no keys.
        _logger.LogDebug("Credential lookup for {Connection}/{Name}: {Result}",
            connectionId, name, read.Describe());

        return read;
    }

    /// <summary>Store or replace one named secret. Returns false when the platform cannot.</summary>
    public bool Set(string connectionId, string name, string secret)
    {
        if (!IsAvailable || string.IsNullOrWhiteSpace(secret)) return false;

        var saved = OperatingSystem.IsWindows()
            ? WindowsDpapiStore.Save(_service, AccountFor(connectionId, name), secret.Trim())
            : MacOsKeychain.Save(_service, AccountFor(connectionId, name), secret.Trim());
        _logger.LogInformation("Stored a secret for {Connection}/{Name}: {Result}",
            connectionId, name, saved ? "ok" : "failed");
        return saved;
    }

    /// <summary>Forget one named secret. Forgetting one that was never stored counts as success.</summary>
    public bool Delete(string connectionId, string name)
    {
        if (!IsAvailable) return false;

        var deleted = OperatingSystem.IsWindows()
            ? WindowsDpapiStore.Delete(_service, AccountFor(connectionId, name))
            : MacOsKeychain.Delete(_service, AccountFor(connectionId, name));
        _logger.LogInformation("Removed a secret for {Connection}/{Name}: {Result}",
            connectionId, name, deleted ? "ok" : "failed");
        return deleted;
    }

    /// <summary>
    /// The character joining a connection id to a credential name in one account string.
    ///
    /// <para>It differs by platform because the two namespaces do. A Keychain account is an arbitrary string,
    /// so <c>:</c> — the conventional spelling — survives. A DPAPI account is a <i>filename</i>
    /// (<see cref="WindowsDpapiStore"/>), and <c>:</c> is an invalid filename character on Windows, so that
    /// store would rewrite it to <c>_</c>: a character an id is allowed to contain, which is how the collision
    /// below would come back on one platform only. <c>.</c> is legal in a filename and excluded from ids.</para>
    /// </summary>
    private static char Separator => OperatingSystem.IsWindows() ? '.' : ':';

    /// <summary>
    /// The account one named secret of one connection is filed under. (#678, #759)
    ///
    /// <para><b>Was keyed by <c>ChatProviderKind</c>, a two-member enum</b> — so every OpenAI-compatible
    /// endpoint shared one slot. A reader who stored an OpenRouter key and then pointed the app at DeepSeek
    /// silently sent the wrong key and got a 401 naming neither cause (#678).</para>
    ///
    /// <para><b>Why the connection id and not the base URL.</b> A URL-derived account orphans the credential
    /// the moment someone changes a port or swaps <c>localhost</c> for <c>127.0.0.1</c> — and the resulting
    /// failure presents as a bad key rather than a lost one, which sends the reader to re-paste a key that
    /// was fine. The id is immutable for exactly this reason.</para>
    ///
    /// <para><b>Each part is sanitised, then they are joined — never the other way round.</b> Sanitising the
    /// joined string would fold the separator into <c>-</c>, which ids may contain: connection <c>gw</c> with
    /// secret <c>primary</c> and a connection called <c>gw-primary</c> would land on one account and silently
    /// overwrite each other. That is #678 again, one layer down, and it is invisible because the symptom is a
    /// 401 rather than an error. Joining afterwards makes it unreachable instead of unlikely:
    /// <see cref="Sanitize"/> emits only <c>[a-z0-9-_]</c>, so neither separator can occur inside a part.</para>
    /// </summary>
    internal static string AccountFor(string connectionId, string name) =>
        Sanitize(connectionId) + Separator + Sanitize(name);

    /// <summary>
    /// Ids are validated as slugs and names are our own constants, so this is a belt-and-braces guard rather
    /// than the primary defence: a stray separator in an account name is the kind of thing that silently
    /// writes a credential to a path nobody looks at again.
    ///
    /// <para>The allowed set is what <see cref="AccountFor"/> depends on. Widening it to admit <c>:</c> or
    /// <c>.</c> would reintroduce the collision documented there.</para>
    /// </summary>
    private static string Sanitize(string id) => AiCredentialNames.Slug(id);
}
