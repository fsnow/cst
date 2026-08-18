using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Text;
using CST.Avalonia.Services.Ai.Credentials;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// The Windows DPAPI store. (#579, AI_SURFACE_B.md §6)
///
/// <para><see cref="AiCredentialStoreTests"/> already covers the behaviour every platform shares - round trip,
/// replace-in-place, per-provider separation, and the acceptance test that the key never reaches a log. Those
/// now exercise DPAPI for real when the suite runs on Windows. What is left here is what only this
/// implementation can be asked: that the bytes on disk are actually encrypted, and that a blob this user cannot
/// currently decrypt is treated as "no key" without being destroyed.</para>
///
/// <para>These are <see cref="WindowsFactAttribute"/> rather than tests that return early off Windows, so the
/// macOS run reports skips instead of green tests that asserted nothing.</para>
/// </summary>
// Every test here is gated by <see cref="WindowsFactAttribute"/>, which the platform analyzer cannot see -
// so state the platform for it. Without this the class raises a CA1416 per call; the previous
// early-return-off-Windows pattern doubled as the analyzer's evidence, and skipping properly gives that up.
[SupportedOSPlatform("windows")]
public class WindowsDpapiStoreTests : IDisposable
{
    private const string Secret = "sk-ant-do-not-write-me-in-the-clear-8b31d7";

    // A unique service per test class run, so this can never read, overwrite or delete a key the developer has
    // actually stored - running the suite must not be a way to lose a credential.
    private readonly string _service = "CST Reader test " + Guid.NewGuid().ToString("N");

    public void Dispose()
    {
        if (!OperatingSystem.IsWindows()) return;
        try
        {
            var dir = WindowsDpapiStore.DirectoryFor(_service);
            if (Directory.Exists(dir)) Directory.Delete(dir, recursive: true);
        }
        catch { /* best effort */ }
    }

    private string TheFile => Directory.EnumerateFiles(WindowsDpapiStore.DirectoryFor(_service)).Single();

    [WindowsFact]
    public void The_key_is_not_written_to_disk_in_the_clear()
    {
        // The whole point of the exercise. A file with the key readable in it would be worse than settings.json,
        // because nobody would think to look.
        Assert.True(WindowsDpapiStore.Save(_service, "anthropic", Secret));

        var bytes = File.ReadAllBytes(TheFile);

        Assert.DoesNotContain(Encoding.UTF8.GetBytes(Secret), bytes);
        // Not even a fragment, and not in UTF-16 either - a naive encoding slip would still be a plaintext key.
        Assert.DoesNotContain(Encoding.UTF8.GetBytes(Secret[..16]), bytes);
        Assert.DoesNotContain(Encoding.Unicode.GetBytes(Secret[..16]), bytes);
    }

    [WindowsFact]
    public void Keys_live_under_the_data_directory_not_in_settings()
    {
        // settings.json is hand-edited, screenshotted and pasted into bug reports. The blobs get their own
        // directory and extension so nobody mistakes one for a setting.
        WindowsDpapiStore.Save(_service, "anthropic", Secret);

        Assert.EndsWith(".dpapi", TheFile, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("CSTReader", "credentials"), TheFile, StringComparison.Ordinal);
        Assert.DoesNotContain("settings.json", TheFile, StringComparison.Ordinal);
    }

    [WindowsFact]
    public void An_undecryptable_blob_reads_as_no_key_rather_than_throwing()
    {
        // The administrator-initiated password reset case: the user's DPAPI master key is discarded, so every
        // CurrentUser blob becomes unreadable. A user in that state should be asked to re-enter their key, not
        // shown a cryptography error they can do nothing about. Also covers a file copied from another machine.
        WindowsDpapiStore.Save(_service, "anthropic", Secret);
        File.WriteAllBytes(TheFile, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });   // not a DPAPI blob at all

        Assert.Null(WindowsDpapiStore.Find(_service, "anthropic"));
    }

    [WindowsFact]
    public void An_undecryptable_blob_is_kept_because_it_may_become_readable_again()
    {
        // The data directory is ROAMING AppData and DPAPI master keys roam too, so a partially-synced profile
        // can hold a blob whose master key has not arrived yet; a domain account can recover one after an admin
        // reset. Deleting on the first failed decrypt would turn both of those temporary states into permanent
        // loss. "Cannot decrypt right now" must never be treated as "will never decrypt".
        WindowsDpapiStore.Save(_service, "anthropic", Secret);
        var file = TheFile;
        var before = File.ReadAllBytes(file);

        File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });
        WindowsDpapiStore.Find(_service, "anthropic");

        Assert.True(File.Exists(file));

        // And re-entering a key overwrites the dead blob, so nothing accumulates from keeping it.
        Assert.True(WindowsDpapiStore.Save(_service, "anthropic", "sk-ant-replacement"));
        Assert.Equal("sk-ant-replacement", WindowsDpapiStore.Find(_service, "anthropic"));
        Assert.NotEqual(before, File.ReadAllBytes(file));
    }

    [WindowsFact]
    public void A_blob_that_cannot_be_opened_is_left_alone_rather_than_discarded()
    {
        // The other half of the same principle, and the distinction the implementation draws deliberately: a
        // transient IO failure - a backup tool or sync client holding the file open - must not cost the user
        // their key. It reads as "none stored" for that moment and works again afterwards.
        WindowsDpapiStore.Save(_service, "anthropic", Secret);
        var file = TheFile;

        using (var _ = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
        {
            Assert.Null(WindowsDpapiStore.Find(_service, "anthropic"));
            Assert.True(File.Exists(file));
        }

        Assert.Equal(Secret, WindowsDpapiStore.Find(_service, "anthropic"));
    }

    [WindowsFact]
    public void A_service_name_with_punctuation_still_produces_one_path_segment()
    {
        // The real service name carries an em dash and spaces, and is free to change. Sanitizing is what keeps
        // a rename from silently producing an unwritable path.
        var awkward = _service + " — with / \\ : * ? \" < > |";

        Assert.True(WindowsDpapiStore.Save(awkward, "anthropic", Secret));
        Assert.Equal(Secret, WindowsDpapiStore.Find(awkward, "anthropic"));

        try { Directory.Delete(WindowsDpapiStore.DirectoryFor(awkward), recursive: true); } catch { }
    }

    [WindowsFact]
    public void Reading_a_provider_that_was_never_stored_leaves_no_file_behind()
    {
        Assert.Null(WindowsDpapiStore.Find(_service, "openai-compatible"));

        var dir = WindowsDpapiStore.DirectoryFor(_service);
        Assert.True(!Directory.Exists(dir) || !Directory.EnumerateFiles(dir).Any());
    }

    [WindowsFact]
    public void Deleting_is_idempotent()
    {
        Assert.True(WindowsDpapiStore.Delete(_service, "anthropic"));   // never stored
        WindowsDpapiStore.Save(_service, "anthropic", Secret);
        Assert.True(WindowsDpapiStore.Delete(_service, "anthropic"));
        Assert.True(WindowsDpapiStore.Delete(_service, "anthropic"));   // again
        Assert.Null(WindowsDpapiStore.Find(_service, "anthropic"));
    }

    [WindowsFact]
    public void Removing_the_last_key_leaves_no_directory_behind()
    {
        // A user who deletes their key should not be left with a credentials tree implying one is still there -
        // and the test suite, which stores under a fresh service name every run, must not silt up the real
        // AppData with empty directories forever.
        WindowsDpapiStore.Save(_service, "anthropic", Secret);
        WindowsDpapiStore.Save(_service, "openai-compatible", Secret);

        WindowsDpapiStore.Delete(_service, "anthropic");
        Assert.True(Directory.Exists(WindowsDpapiStore.DirectoryFor(_service)));   // the other key is still there

        WindowsDpapiStore.Delete(_service, "openai-compatible");
        Assert.False(Directory.Exists(WindowsDpapiStore.DirectoryFor(_service)));
    }

    [WindowsFact]
    public void A_replaced_key_leaves_no_temporary_file_behind()
    {
        // Save writes to a temp file and moves it into place, so an interrupted write cannot leave a truncated
        // blob where a working key was. The temp must not survive a successful write.
        WindowsDpapiStore.Save(_service, "anthropic", Secret);
        WindowsDpapiStore.Save(_service, "anthropic", "sk-ant-second");

        var files = Directory.EnumerateFiles(WindowsDpapiStore.DirectoryFor(_service)).ToList();
        Assert.Single(files);
        Assert.DoesNotContain(files, f => f.EndsWith(".tmp", StringComparison.Ordinal));
        Assert.Equal("sk-ant-second", WindowsDpapiStore.Find(_service, "anthropic"));
    }

    [WindowsFact]
    public void A_failed_write_leaves_no_temporary_file_behind()
    {
        // The failure path of the same mechanism. A save that cannot complete must not accumulate a .tmp per
        // attempt beside the user's working key.
        //
        // Locking the TARGET, not the temp: the temp write then succeeds and the move is what fails, which is
        // the real interruption this guards against. (Locking the temp path would only prove the test can
        // create a file the store cannot delete.)
        WindowsDpapiStore.Save(_service, "anthropic", Secret);
        var file = TheFile;
        var temp = file + ".tmp";

        using (var _ = new FileStream(file, FileMode.Open, FileAccess.Read, FileShare.None))
            Assert.False(WindowsDpapiStore.Save(_service, "anthropic", "sk-ant-never-lands"));

        Assert.False(File.Exists(temp));
        Assert.Equal(Secret, WindowsDpapiStore.Find(_service, "anthropic"));   // the working key survived
    }
}
