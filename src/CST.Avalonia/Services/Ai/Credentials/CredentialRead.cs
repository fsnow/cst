namespace CST.Avalonia.Services.Ai.Credentials;

/// <summary>
/// What happened when we asked the OS for one stored secret. (#926)
///
/// <para><b>Why this is not just <c>string?</c>.</b> It was, and the four outcomes below all arrived as
/// <c>null</c>. A reader who declined a Keychain authorization prompt was told no key was stored, so the
/// remedy the app offered him was to type his API key in again — over a key that was present and correct the
/// whole time. Absence and refusal need different words because they need different remedies.</para>
///
/// <para><b>The distinction that matters to the reader is not "error or not" but "what do I do now".</b>
/// <see cref="NotStored"/> means type a key in. <see cref="Unreadable"/> means the key is there and something
/// else must be settled first — authorize the app, or unlock the keychain — and re-entering it would be
/// wasted work. <see cref="Unavailable"/> means this machine has nowhere to keep one at all.</para>
/// </summary>
public enum CredentialState
{
    /// <summary>The secret was read.</summary>
    Found,

    /// <summary>There is no such item. The reader has not stored this secret, which is an ordinary state —
    /// a local runner needs no key, and a connection may authenticate through headers instead.</summary>
    NotStored,

    /// <summary>
    /// An item exists and this process could not read its value.
    ///
    /// <para>On macOS: the login keychain's per-item ACL named a different binary, or the reader dismissed
    /// the authorization prompt, or the keychain is locked with no UI available to unlock it. On Windows: the
    /// DPAPI blob is present but did not decrypt.</para>
    ///
    /// <para><b>Never a reason to delete anything.</b> Both platforms can recover — authorizing once on
    /// macOS, a roaming master key arriving on Windows — so this is a state to report, not to clean up.</para>
    /// </summary>
    Unreadable,

    /// <summary>There is no credential store on this platform, or it could not be reached at all.</summary>
    Unavailable,
}

/// <summary>
/// One credential lookup: what happened, and the secret when there is one.
///
/// <para><b>A secret is present only for <see cref="CredentialState.Found"/>.</b> Every other state carries
/// null, so a caller that reads <see cref="Secret"/> without checking <see cref="State"/> behaves exactly as
/// the old <c>string?</c> API did — which keeps call sites that genuinely only want the value honest, without
/// making them all handle four cases.</para>
/// </summary>
public readonly record struct CredentialRead(CredentialState State, string? Secret)
{
    public static CredentialRead Found(string secret) => new(CredentialState.Found, secret);

    public static readonly CredentialRead NotStored = new(CredentialState.NotStored, null);
    public static readonly CredentialRead Unreadable = new(CredentialState.Unreadable, null);
    public static readonly CredentialRead Unavailable = new(CredentialState.Unavailable, null);

    /// <summary>Whether an item exists, whether or not its value could be read. False only when nothing is
    /// stored or there is nowhere to store it — so it never claims a key the reader has not provided.</summary>
    public bool Exists => State is CredentialState.Found or CredentialState.Unreadable;

    /// <summary>The word for a log line. Deliberately says nothing about the value.</summary>
    public string Describe() => State switch
    {
        CredentialState.Found => "found",
        CredentialState.NotStored => "none stored",
        CredentialState.Unreadable => "stored, but this build was not allowed to read it",
        _ => "no credential store available",
    };
}
