using System;
using System.Collections.Generic;
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
/// <para><b>Fetching a secret is lazy; asking whether one exists is not, and does not need to be.</b> (#925)
/// The rule that matters is the original one: no permission dialog on launch for a feature the user has not
/// asked for, because that teaches them to click through prompts. It stopped holding once every status
/// question — the connection list, the badges, the editor's stored-key note — answered itself by fetching the
/// value, which on macOS is the thing the ACL guards. Measured before the fix: 114 reads across one launch
/// and one Settings window, 76 of them for a single account, and the maintainer pressing Escape fifteen to
/// twenty times.</para>
///
/// <para>So the seam has two verbs. <see cref="Probe"/> answers "is a key configured?" from the item's
/// metadata and never prompts; <see cref="Read"/> fetches the value and is reserved for the request that
/// actually sends it. Everything that only renders a state uses the first.</para>
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

    /// <summary>
    /// What a real read last learned about an account, by account name. (#925)
    ///
    /// <para><b>States, never secrets.</b> Holding the value would keep a credential in memory for the life
    /// of the process to save a lookup nobody is waiting on, and this file's standing rule is that a secret
    /// lives in the OS store and travels no further than the request that needs it.</para>
    ///
    /// <para><b>Written only by <see cref="Read"/>, cleared by <see cref="Set"/> and <see cref="Delete"/>.</b>
    /// Those are the only two ways the app changes a stored secret, which is what makes this safe: the
    /// original objection to caching here was that it "would go stale the moment the user changes the key in
    /// Settings", and a cache both mutators clear cannot.</para>
    /// </summary>
    private readonly Dictionary<string, CredentialState> _known = new(StringComparer.Ordinal);
    private readonly object _knownGate = new();

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

    /// <summary>
    /// What is known about a secret <b>without asking the OS to hand it over</b>. (#925)
    ///
    /// <para><b>Never prompts, so status queries are free.</b> Presence comes from the item's metadata,
    /// which the macOS ACL does not guard — see <see cref="MacOsKeychain.Exists"/>. Everything that only
    /// wants to know whether a key is configured should call this: the connection list, the badges, the
    /// editor's "a key is stored" note. They were fetching the secret to answer a yes/no question, and each
    /// fetch could raise a modal password dialog.</para>
    ///
    /// <para><b><see cref="CredentialState.Found"/> here means PRESENT, not readable.</b> Readability is
    /// knowable only by attempting the read, which is the thing that prompts. So a probe that has never seen
    /// a real read reports a locked key as present — which is true, and is the honest half of what #926
    /// established: the failure to avoid is claiming a key is ABSENT when it is not.</para>
    ///
    /// <para><b>A remembered failure wins.</b> Once a real <see cref="Read"/> has found an item unreadable,
    /// that is recorded and reported here, so the badge catches up the moment anything actually tries to use
    /// the key rather than waiting for the reader to try again.</para>
    /// </summary>
    public CredentialState Probe(string connectionId, string name)
    {
        if (!IsAvailable) return CredentialState.Unavailable;

        var account = AccountFor(connectionId, name);
        lock (_knownGate)
            if (_known.TryGetValue(account, out var remembered)) return remembered;

        var exists = OperatingSystem.IsWindows()
            ? WindowsDpapiStore.Exists(_service, account)
            : MacOsKeychain.Exists(_service, account);

        return exists ? CredentialState.Found : CredentialState.NotStored;
    }

    public string? Get(string connectionId, string name) => Read(connectionId, name).Secret;

    public CredentialRead Read(string connectionId, string name)
    {
        if (!IsAvailable) return CredentialRead.Unavailable;

        var account = AccountFor(connectionId, name);
        var read = OperatingSystem.IsWindows()
            ? WindowsDpapiStore.Find(_service, account)
            : MacOsKeychain.Find(_service, account);

        // What a REAL read learned, for Probe to report without repeating the read. Only the state is kept -
        // never the secret, which would put a credential in memory for the life of the process to save a
        // lookup nobody was waiting on.
        lock (_knownGate) _known[account] = read.State;

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

        var account = AccountFor(connectionId, name);
        var saved = OperatingSystem.IsWindows()
            ? WindowsDpapiStore.Save(_service, account, secret.Trim())
            : MacOsKeychain.Save(_service, account, secret.Trim());

        // Forgotten rather than assumed. A successful Save does not make the item readable BY US - on macOS
        // SecItemUpdate rewrites the value and leaves the ACL alone, which is why replacing a locked key
        // succeeds and changes nothing the reader can see (#926). Recording Found here would have the badge
        // announce a key we still cannot read.
        lock (_knownGate) _known.Remove(account);
        _logger.LogInformation("Stored a secret for {Connection}/{Name}: {Result}",
            connectionId, name, saved ? "ok" : "failed");
        return saved;
    }

    /// <summary>Forget one named secret. Forgetting one that was never stored counts as success.</summary>
    public bool Delete(string connectionId, string name)
    {
        if (!IsAvailable) return false;

        var account = AccountFor(connectionId, name);
        var deleted = OperatingSystem.IsWindows()
            ? WindowsDpapiStore.Delete(_service, account)
            : MacOsKeychain.Delete(_service, account);

        // Forgotten either way. On success there is nothing to remember; on failure the item is still there
        // and in a state we no longer have evidence about.
        lock (_knownGate) _known.Remove(account);
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
