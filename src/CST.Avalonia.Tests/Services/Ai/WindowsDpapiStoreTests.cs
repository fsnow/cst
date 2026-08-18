using System;
using System.IO;
using System.Linq;
using System.Text;
using CST.Avalonia.Services.Ai.Credentials;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// The Windows DPAPI store. (#579, AI_SURFACE_B.md §6)
///
/// <para><see cref="AiCredentialStoreTests"/> already covers the behaviour every platform shares — round trip,
/// replace-in-place, per-provider separation, and the acceptance test that the key never reaches a log. Those
/// now exercise DPAPI for real when the suite runs on Windows. What is left here is what only this
/// implementation can be asked: that the bytes on disk are actually encrypted, and that a blob this user can no
/// longer decrypt is treated as "no key" rather than as an error.</para>
///
/// <para>Every test no-ops off Windows rather than failing — the macOS dev machines run this suite too, and a
/// red result there would say "broken" where the truth is "not this platform".</para>
/// </summary>
public class WindowsDpapiStoreTests : IDisposable
{
    private const string Secret = "sk-ant-do-not-write-me-in-the-clear-8b31d7";

    // A unique service per test class run, so this can never read, overwrite or delete a key the developer has
    // actually stored — running the suite must not be a way to lose a credential.
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

    [Fact]
    public void The_key_is_not_written_to_disk_in_the_clear()
    {
        // The whole point of the exercise. A file with the key readable in it would be worse than settings.json,
        // because nobody would think to look.
        if (!OperatingSystem.IsWindows()) return;

        Assert.True(WindowsDpapiStore.Save(_service, "anthropic", Secret));

        var file = Directory.EnumerateFiles(WindowsDpapiStore.DirectoryFor(_service)).Single();
        var bytes = File.ReadAllBytes(file);

        Assert.DoesNotContain(Encoding.UTF8.GetBytes(Secret), bytes);
        // Not even a fragment, and not in UTF-16 either — a naive encoding slip would still be a plaintext key.
        Assert.DoesNotContain(Encoding.UTF8.GetBytes(Secret[..16]), bytes);
        Assert.DoesNotContain(Encoding.Unicode.GetBytes(Secret[..16]), bytes);
    }

    [Fact]
    public void Keys_live_under_the_data_directory_not_in_settings()
    {
        // settings.json is hand-edited, screenshotted and pasted into bug reports. The blobs get their own
        // directory and extension so nobody mistakes one for a setting.
        if (!OperatingSystem.IsWindows()) return;

        WindowsDpapiStore.Save(_service, "anthropic", Secret);

        var file = Directory.EnumerateFiles(WindowsDpapiStore.DirectoryFor(_service)).Single();
        Assert.EndsWith(".dpapi", file, StringComparison.Ordinal);
        Assert.Contains(Path.Combine("CSTReader", "credentials"), file, StringComparison.Ordinal);
        Assert.DoesNotContain("settings.json", file, StringComparison.Ordinal);
    }

    [Fact]
    public void An_undecryptable_blob_reads_as_no_key_rather_than_throwing()
    {
        // The administrator-initiated password reset case: the user's DPAPI master key is discarded, so every
        // CurrentUser blob becomes unreadable. A user in that state should be asked to re-enter their key, not
        // shown a cryptography error they can do nothing about. Also covers a file copied from another machine.
        if (!OperatingSystem.IsWindows()) return;

        WindowsDpapiStore.Save(_service, "anthropic", Secret);
        var file = Directory.EnumerateFiles(WindowsDpapiStore.DirectoryFor(_service)).Single();
        File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });   // not a DPAPI blob at all

        Assert.Null(WindowsDpapiStore.Find(_service, "anthropic"));
    }

    [Fact]
    public void An_undecryptable_blob_is_cleared_so_the_next_launch_does_not_repeat_the_work()
    {
        if (!OperatingSystem.IsWindows()) return;

        WindowsDpapiStore.Save(_service, "anthropic", Secret);
        var file = Directory.EnumerateFiles(WindowsDpapiStore.DirectoryFor(_service)).Single();
        File.WriteAllBytes(file, new byte[] { 1, 2, 3, 4, 5, 6, 7, 8 });

        WindowsDpapiStore.Find(_service, "anthropic");

        Assert.False(File.Exists(file));
        // And storing again afterwards works, so "re-enter your key" actually resolves it.
        Assert.True(WindowsDpapiStore.Save(_service, "anthropic", "sk-ant-replacement"));
        Assert.Equal("sk-ant-replacement", WindowsDpapiStore.Find(_service, "anthropic"));
    }

    [Fact]
    public void A_service_name_with_punctuation_still_produces_one_path_segment()
    {
        // The real service name carries an em dash and spaces, and is free to change. Sanitizing is what keeps
        // a rename from silently producing an unwritable path.
        if (!OperatingSystem.IsWindows()) return;

        var awkward = _service + @" — with / \ : * ? "" < > |";

        Assert.True(WindowsDpapiStore.Save(awkward, "anthropic", Secret));
        Assert.Equal(Secret, WindowsDpapiStore.Find(awkward, "anthropic"));

        try { Directory.Delete(WindowsDpapiStore.DirectoryFor(awkward), recursive: true); } catch { }
    }

    [Fact]
    public void Reading_a_provider_that_was_never_stored_leaves_no_file_behind()
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.Null(WindowsDpapiStore.Find(_service, "openai-compatible"));

        var dir = WindowsDpapiStore.DirectoryFor(_service);
        Assert.True(!Directory.Exists(dir) || !Directory.EnumerateFiles(dir).Any());
    }

    [Fact]
    public void Deleting_is_idempotent()
    {
        if (!OperatingSystem.IsWindows()) return;

        Assert.True(WindowsDpapiStore.Delete(_service, "anthropic"));   // never stored
        WindowsDpapiStore.Save(_service, "anthropic", Secret);
        Assert.True(WindowsDpapiStore.Delete(_service, "anthropic"));
        Assert.True(WindowsDpapiStore.Delete(_service, "anthropic"));   // again
        Assert.Null(WindowsDpapiStore.Find(_service, "anthropic"));
    }

    [Fact]
    public void A_replaced_key_leaves_no_temporary_file_behind()
    {
        // Save writes to a temp file and moves it into place, so an interrupted write cannot leave a truncated
        // blob where a working key was. The temp must not survive a successful write.
        if (!OperatingSystem.IsWindows()) return;

        WindowsDpapiStore.Save(_service, "anthropic", Secret);
        WindowsDpapiStore.Save(_service, "anthropic", "sk-ant-second");

        var files = Directory.EnumerateFiles(WindowsDpapiStore.DirectoryFor(_service)).ToList();
        Assert.Single(files);
        Assert.DoesNotContain(files, f => f.EndsWith(".tmp", StringComparison.Ordinal));
        Assert.Equal("sk-ant-second", WindowsDpapiStore.Find(_service, "anthropic"));
    }
}
