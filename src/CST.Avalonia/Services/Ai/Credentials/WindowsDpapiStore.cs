using System;
using System.IO;
using System.Linq;
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
    /// Extra entropy mixed into every blob. Not a secret, and not pretending to be one - this repository is
    /// public, so the value is world-readable. What it buys is namespacing: it prevents ANOTHER application
    /// from decrypting this blob by accident, and keeps our ciphertext distinct from anything else the user's
    /// DPAPI key protects. It is NOT protection against a process running as this user - nothing in DPAPI is.
    /// The real guarantee here is DPAPI's own: another local account, or someone reading the disk offline,
    /// cannot decrypt it.
    ///
    /// <para>Written with escapes rather than literal em dashes on purpose. Changing this value orphans every
    /// stored key silently - the user is simply told they have not configured one - so the bytes must not be
    /// at the mercy of an editor resaving the file in a different codepage.</para>
    /// </summary>
    private static readonly byte[] Entropy =
        Encoding.UTF8.GetBytes("CST Reader — AI provider key — v1");

    /// <summary>Serializes the three operations, so a save racing a read cannot see a half-installed file.</summary>
    private static readonly object Gate = new();

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
    /// here - rather than assuming - keeps <c>IsAvailable</c> honest on a locked-down or full disk.
    ///
    /// <para>Probed ONCE and cached, mirroring <see cref="MacOsKeychain.IsAvailable"/>, because this is read on
    /// every credential operation and on every Settings binding refresh. An uncached probe would mean reading a
    /// credential writes to disk, per request, forever.</para>
    ///
    /// <para>It creates the real <c>credentials</c> root - the directory <see cref="Save"/> creates anyway -
    /// rather than a fake service segment. An earlier version probed <c>DirectoryFor("probe")</c> and left a
    /// stray <c>credentials\probe</c> directory behind for anyone who merely opened Settings, which is exactly
    /// the sort of mystery artifact the private-directory design exists to avoid.</para>
    ///
    /// <para>This is a floor, not a promise: creating a directory is a metadata operation, so it can succeed
    /// where writing a file would not. <see cref="Save"/> still reports its own failure, which is what the
    /// user actually sees.</para>
    /// </summary>
    private static readonly Lazy<bool> Probe = new(() =>
    {
        if (!OperatingSystem.IsWindows()) return false;
        try
        {
            Directory.CreateDirectory(Path.Combine(AppConstants.DataDirectory, "credentials"));
            return true;
        }
        catch (Exception)
        {
            return false;
        }
    });

    internal static bool IsAvailable => Probe.Value;

    /// <summary>The stored secret, or null when there is none - or when the blob cannot be decrypted.</summary>
    internal static string? Find(string service, string account)
    {
        var path = FileFor(service, account);

        lock (Gate)
        {
            if (!File.Exists(path)) return null;

            try
            {
                var plaintext = ProtectedData.Unprotect(
                    File.ReadAllBytes(path), Entropy, DataProtectionScope.CurrentUser);
                try
                {
                    return Encoding.UTF8.GetString(plaintext);
                }
                finally
                {
                    // Trims one copy of the key from the heap. The string we return is still pageable and
                    // dumpable - it has to be, to reach an HTTP header - so this is hygiene, not a guarantee.
                    CryptographicOperations.ZeroMemory(plaintext);
                }
            }
            catch (CryptographicException)
            {
                // The blob exists but cannot be decrypted right now. The realistic cause is an
                // ADMINISTRATOR-INITIATED password reset, which discards the user's DPAPI master key - a reset
                // the user performs themselves migrates it, so this is the IT-helpdesk case, not the
                // forgot-my-password case. It also covers a file copied from another machine or account.
                //
                // Reported as "no key stored", never as an error: the honest thing to tell someone in that
                // state is "please re-enter your key", not a cryptography failure they can do nothing with.
                //
                // The file is deliberately LEFT IN PLACE. "Undecryptable now" is not "undecryptable forever":
                // the data directory is ROAMING AppData, and the DPAPI master keys under
                // %APPDATA%\Microsoft\Protect roam with it, so a partially-synced profile can hold this blob
                // without its master key and recover once the sync completes. Domain accounts escrow master
                // keys to the DC and can likewise recover after an admin reset. Deleting here would convert
                // both of those temporary states into permanent loss, to save a sub-millisecond decrypt on the
                // next launch. Re-entering a key overwrites the blob anyway, so nothing accumulates.
                return null;
            }
            catch (Exception)
            {
                // Unreadable file, transient IO, a concurrent writer. Report "none stored" and leave the file
                // alone - destroying it on a hiccup would be data loss.
                return null;
            }
        }
    }

    /// <summary>Store or replace a secret. Returns false when it could not be written.</summary>
    internal static bool Save(string service, string account, string secret)
    {
        var path = FileFor(service, account);
        var temp = path + ".tmp";

        lock (Gate)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(path)!);

                var utf8 = Encoding.UTF8.GetBytes(secret);
                byte[] ciphertext;
                try
                {
                    ciphertext = ProtectedData.Protect(utf8, Entropy, DataProtectionScope.CurrentUser);
                }
                finally
                {
                    CryptographicOperations.ZeroMemory(utf8);
                }

                // Write-then-replace, so an interrupted save cannot leave a truncated blob where a working key
                // was: the failure mode of a half-written file is "your key silently stopped working". Both
                // paths sit in one directory, hence one volume, so this is a rename rather than a copy.
                // Encryption happens before any disk write, so the temp file never holds plaintext either.
                File.WriteAllBytes(temp, ciphertext);
                File.Move(temp, path, overwrite: true);
                return true;
            }
            catch (Exception)
            {
                // A failed move leaves the temp behind; clear it rather than accumulate one per failure.
                TryDelete(temp);
                return false;
            }
        }
    }

    /// <summary>Remove a secret. Removing one that is not there counts as success.</summary>
    internal static bool Delete(string service, string account)
    {
        lock (Gate)
        {
            var deleted = TryDelete(FileFor(service, account));
            PruneIfEmpty(DirectoryFor(service));
            return deleted;
        }
    }

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
    /// Remove the service directory once its last key is gone, so removing a key leaves nothing behind. A user
    /// who deletes their key should not be left with a <c>credentials</c> tree suggesting one is still stored.
    /// </summary>
    private static void PruneIfEmpty(string directory)
    {
        try
        {
            if (Directory.Exists(directory) && !Directory.EnumerateFileSystemEntries(directory).Any())
                Directory.Delete(directory);
        }
        catch (Exception)
        {
            // Cosmetic. A directory we could not remove is not a failure to report.
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
