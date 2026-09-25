using System;
using Xunit;

namespace CST.Avalonia.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> for tests that take a file's permissions away and check what the code does when it
/// cannot open it. Reports SKIPPED where that cannot be arranged, rather than passing having proved nothing:
/// <list type="bullet">
/// <item>on Windows, which has no Unix file mode to clear;</item>
/// <item>when running as root (or another privileged process), for which a cleared mode is not enforced and the
/// file opens anyway.</item>
/// </list>
/// Its own attribute rather than <see cref="UnixFactAttribute"/>, whose skip reasons are about shell probes and
/// POSIX renames, and whose check is fixed at the platform — the privilege half cannot be a parameter.
/// </summary>
public sealed class UnixPermissionFactAttribute : FactAttribute
{
    public UnixPermissionFactAttribute()
    {
        if (OperatingSystem.IsWindows())
            Skip = "Unix only: the test clears a file's Unix mode, which Windows does not have.";
        else if (Environment.IsPrivilegedProcess)
            Skip = "Not as root: a privileged process opens a file whatever its mode, so the unreadable file cannot be arranged.";
    }
}
