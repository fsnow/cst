using System;
using System.IO;
using System.Linq;
using System.Runtime.Versioning;
using System.Security.AccessControl;
using System.Security.Principal;
using CST.Avalonia.Services.LocalApi;
using Xunit;

namespace CST.Avalonia.Tests.Services.LocalApi;

/// <summary>
/// Who can read the handshake file. (#303, #505)
///
/// <para>It carries the per-session bearer token for the local API, so "only this user" is the whole
/// requirement. macOS and Linux state that as mode <c>0600</c>; Windows had nothing explicit and relied on
/// <c>%APPDATA%</c> being ACL'd per user - true by default, but inheritance is precisely what a redirected
/// folder or an over-broad grant higher up the tree can undo, silently.</para>
/// </summary>
public class LocalApiInfoPermissionsTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "cst-handshake-perms", Guid.NewGuid().ToString("N"));

    public LocalApiInfoPermissionsTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private void Write() => new LocalApiInfo(51234, "a-session-secret", Environment.ProcessId, 7).Write(_dir);

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void The_token_file_is_detached_from_inherited_permissions()
    {
        // The load-bearing half of the fix. Without protection the file keeps whatever the directory hands
        // down, so a permissive grant higher up the tree silently applies to a session secret.
        Write();

        var security = new FileInfo(LocalApiInfo.PathIn(_dir)).GetAccessControl();

        Assert.True(security.AreAccessRulesProtected,
            "the handshake still inherits permissions from its directory");
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void Only_the_current_user_is_granted_access()
    {
        // Mirrors 0600. Anything else in the list means somebody besides the owner can read the token.
        Write();

        var rules = new FileInfo(LocalApiInfo.PathIn(_dir))
            .GetAccessControl()
            .GetAccessRules(includeExplicit: true, includeInherited: true, typeof(SecurityIdentifier))
            .Cast<FileSystemAccessRule>()
            .ToList();

        var me = WindowsIdentity.GetCurrent().User!;

        Assert.All(rules, rule => Assert.Equal(me, rule.IdentityReference));
        Assert.Contains(rules, rule =>
            rule.AccessControlType == AccessControlType.Allow &&
            rule.FileSystemRights.HasFlag(FileSystemRights.Read));
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void The_owner_can_still_read_what_it_wrote()
    {
        // The obvious way to get this wrong is to protect the ACL and then grant nobody, locking the app out
        // of its own handshake. Reading it back is the check that matters.
        Write();

        var read = LocalApiInfo.Read(_dir);

        Assert.NotNull(read);
        Assert.Equal("a-session-secret", read!.Token);
    }

    [WindowsFact]
    [SupportedOSPlatform("windows")]
    public void Rewriting_keeps_the_file_protected()
    {
        // The swap goes through File.Replace on Windows, which preserves the DESTINATION's ACL rather than the
        // temp's. That happens to be what we want here - but it is a property of ReplaceFile rather than
        // something this code states, so it is worth pinning: a second write must not quietly restore
        // inheritance.
        Write();
        Write();

        var security = new FileInfo(LocalApiInfo.PathIn(_dir)).GetAccessControl();

        Assert.True(security.AreAccessRulesProtected, "a rewrite reinstated inherited permissions");
        Assert.Equal("a-session-secret", LocalApiInfo.Read(_dir)!.Token);
    }

    [Fact]
    public void Unix_permissions_are_unchanged_by_the_Windows_work()
    {
        // The Unix path is the one that was already correct; this guards it against collateral damage.
        if (OperatingSystem.IsWindows()) return;

        Write();

        Assert.Equal(
            UnixFileMode.UserRead | UnixFileMode.UserWrite,
            File.GetUnixFileMode(LocalApiInfo.PathIn(_dir)));
    }
}
