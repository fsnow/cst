using System;

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

    /// <summary>
    /// What to tell the reader about a named secret that is stored and unreadable. (#926)
    ///
    /// <para><b>One sentence, shared by every surface that reports it.</b> The resolver, the model catalogue
    /// and the connection editor each phrased "no stored value … re-enter it" separately, and drifted.</para>
    ///
    /// <para><b>On macOS it says AUTHORIZE, and nothing else — because nothing else works.</b> Tested
    /// 2026-08-31 against a locked item, and both of the obvious remedies failed in different ways:</para>
    ///
    /// <list type="bullet">
    /// <item>Entering a replacement key <b>succeeds and does not help.</b> The write goes through
    /// (<c>SecItemUpdate</c>, item modified on disk) but an item's ACL is not its value, so the same binary
    /// still cannot read what it just wrote. Advice that appears to work and changes nothing is worse than
    /// advice that fails, because the reader stops looking.</item>
    /// <item>Removing it <b>can fail</b>. Deleting needs authorization too, so a reader who cannot satisfy
    /// the prompt cannot delete their way out either — see <c>AiConnectionEditorViewModel.RemoveKey</c>,
    /// which now reports that rather than claiming success.</item>
    /// </list>
    ///
    /// <para>What is left is granting the authorization, or deleting the item in Keychain Access, which is
    /// trusted for the login keychain in a way this app is not. Windows has neither problem — <c>Save</c>
    /// there writes a fresh file, so re-entering genuinely fixes it — hence the split.</para>
    /// </summary>
    public static string Advice(string what) =>
        OperatingSystem.IsWindows()
            ? $"{what} is stored but could not be decrypted. Enter it again under Settings \u2192 AI."
            : $"{what} is stored, and CST Reader was not allowed to read it. Choose Allow when macOS asks "
              + "for your login keychain password. Replacing the key will not help; if you cannot allow it, "
              + "delete the \u201cCST Reader\u201d entry in Keychain Access and add the key again.";

    /// <summary>
    /// What to tell the reader when the OS refused to delete their stored secret. (#926)
    ///
    /// <para>Its own sentence rather than <see cref="Advice"/>: the reader has just pressed Remove, so
    /// "authorize it" is about a different operation than the one they are attempting, and the fallback is
    /// the only route left.</para>
    /// </summary>
    public static string RemovalRefused(string displayName) =>
        OperatingSystem.IsWindows()
            ? $"{displayName}\u2019s stored key could not be removed. It is still stored."
            : $"{displayName}\u2019s stored key could not be removed \u2014 macOS did not allow it, so it "
              + "is still stored. Try again and choose Allow, or delete the \u201cCST Reader\u201d entry "
              + "in Keychain Access.";

    /// <summary>The word for a log line. Deliberately says nothing about the value.</summary>
    public string Describe() => State switch
    {
        CredentialState.Found => "found",
        CredentialState.NotStored => "none stored",
        CredentialState.Unreadable => "stored, but this build was not allowed to read it",
        _ => "no credential store available",
    };
}
