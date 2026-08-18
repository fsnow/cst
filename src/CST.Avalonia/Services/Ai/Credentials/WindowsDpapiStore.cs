using System;
using System.IO;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using CST.Avalonia.Constants;

namespace CST.Avalonia.Services.Ai.Credentials;

/// <summary>
/// The Windows half of surface B's key storage: DPAPI (<see cref="ProtectedData"/>, CurrentUser scope) over
/// one small file per provider under the app data directory. (#579, AI_SURFACE_B.md §6)
///
/// <para>Windows has no Keychain equivalent to hand a secret to. The Credential Manager is the nearest thing,
/// but it is reached through Win32 P/Invoke, is enumerable by any process running as the user, and shows the
/// blob in a Control Panel UI - so it buys visibility we do not want and no protection DPAPI lacks. DPAPI
/// derives its key from the user's own login credentials: another local account cannot read the file even with
/// filesystem access to it, which is the property that matters.</para>
///
/// <para><b>No signing dependency.</b> Unlike the macOS Keychain, whose ACL is tied to the signing identity,
/// DPAPI keys off the USER. An unsigned <c>dotnet run</c> build and an installed one therefore read each
/// other's keys, which is what a developer expects and what makes this testable here at all. (#609)</para>
///
/// <para><b>Not in <c>settings.json</c>.</b> These live in their own directory with their own extension so
/// nobody hand-edits, screenshots or pastes one into a bug report by accident.</para>
/// </summary>
[SupportedOSPlatform("windows")]
internal static class WindowsDpapiStore
{
    /// <summary>
    /// Extra entropy mixed into every blob. Not a secret - it ships in the binary - and not pretending to be
    /// one: it scopes the ciphertext to this application, so a blob copied out cannot be decrypted by another
    /// program running as the same user without also knowing this value. Changing it orphans stored keys.
    /// </summary>
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("CST Reader — AI provider key — v1");

    /// <summary>
    /// Where the blobs live. A directory of its own, so its contents are obviously not user-editable settings
    /// and a future migration can retire the whole thing by name.
    /// </summary>
    internal static string DirectoryFor(string service) =>
        Path.Combine(AppConstants.DataDirectory, "credentials", Sanitize(service));

    private static string FileFor(string service, string account) =>
        Path.Combine(DirectoryFor(service), Sanitize(account) + ".dpapi");

    /// <summary>
    /// DPAPI itself is always present on Windows; what can fail is writing to the data directory. Probing that
    /// here - rather than assuming - keeps <c>IsAvailable</c> honest on a locked-down or full disk, where the
    /// truthful answer is "cannot store a key" rather than a failure at the moment the user saves one.
    /// </summary>
    internal static bool IsAvailable
    {
        get
        {
            if (!OperatingSystem.IsWindows()) return false;
            try
            {
                Directory.CreateDirectory(DirectoryFor("probe"));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }
    }

    /// <summary>The stored secret, or null when there is none - or when the blob can no longer be decrypted.</summary>
    internal static string? Find(string service, string account)
    {
        var path = FileFor(service, account);
        if (!File.Exists(path)) return null;

        try
        {
            var plaintext = ProtectedData.Unprotect(File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
            return Encoding.UTF8.GetString(plaintext);
        }
        catch (CryptographicException)
        {
            // The blob exists but this user can no longer decrypt it. The realistic cause is an
            // ADMINISTRATOR-INITIATED password reset, which discards the user's DPAPI master key - a reset the
            // user performs themselves migrates it, so this is the IT-helpdesk case, not the forgot-my-password
            // case. It also covers a file copied from another machine or another account.
            //
            // Treated as "no key stored", never as an error: the honest thing to tell someone in that state is
            // "please re-enter your key", not a cryptography failure they can do nothing with. Deleting the
            // dead blob keeps the next launch from repeating the work.
            TryDelete(path);
            return null;
        }
        catch (Exception)
        {
            // Unreadable file, transient IO. Report "none stored" and leave the file alone - it may be
            // readable next time, and destroying it on a transient error would turn a hiccup into data loss.
            return null;
        }
    }

    /// <summary>Store or replace a secret. Returns false when it could not be written.</summary>
    internal static bool Save(string service, string account, string secret)
    {
        try
        {
            var path = FileFor(service, account);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);

            var ciphertext = ProtectedData.Protect(
                Encoding.UTF8.GetBytes(secret), Entropy, DataProtectionScope.CurrentUser);

            // Write-then-replace, so an interrupted save cannot leave a truncated blob where a working key was:
            // the failure mode of a half-written file is "your key silently stopped working".
            var temp = path + ".tmp";
            File.WriteAllBytes(temp, ciphertext);
            File.Move(temp, path, overwrite: true);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>Remove a secret. Removing one that is not there counts as success.</summary>
    internal static bool Delete(string service, string account) => TryDelete(FileFor(service, account));

    private static bool TryDelete(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    }

    /// <summary>
    /// Make a service or account name safe as a single path segment. The service name carries an em dash and
    /// spaces today and is free to change, so this must not assume it is already path-shaped.
    /// </summary>
    private static string Sanitize(string name)
    {
        var invalid = Path.GetInvalidFileNameChars();
        var sb = new StringBuilder(name.Length);
        foreach (var c in name)
            sb.Append(Array.IndexOf(invalid, c) >= 0 ? '_' : c);
        return sb.ToString();
    }
}
