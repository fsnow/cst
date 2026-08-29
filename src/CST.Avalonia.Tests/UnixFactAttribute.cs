using System;
using Xunit;

namespace CST.Avalonia.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that reports SKIPPED on Windows instead of passing.
///
/// <para>The twin of <see cref="WindowsFactAttribute"/>, and for the reason given there: an early
/// <c>return</c> leaves a green test that executed no assertions, which is how a platform stops being covered
/// without anything turning red.</para>
///
/// <para>Used by the tests that spawn a real shell (#817) — those cannot run on Windows, where there is no
/// probe to test, and should not be faked there, because what they exist to check is the behaviour of an
/// actual process and an actual pipe — and by the asset-replacement tests (#869), whose whole subject is a
/// rename succeeding over open handles, which is a POSIX behaviour Windows does not have.</para>
///
/// <para>The reason is a parameter because those two are not the same reason, and a skip that explains the
/// wrong one is worse than no explanation: whoever reads the skipped run has to go and find out which.</para>
/// </summary>
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute(
        string because = "there is no shell probe on Windows, which inherits its environment normally")
    {
        if (OperatingSystem.IsWindows())
            Skip = $"Unix only: {because}.";
    }
}
