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
/// <para>Used by the tests that spawn a real shell (#817). Those cannot run on Windows, where there is no
/// probe to test — and should not be faked there, because what they exist to check is the behaviour of an
/// actual process and an actual pipe.</para>
/// </summary>
public sealed class UnixFactAttribute : FactAttribute
{
    public UnixFactAttribute()
    {
        if (OperatingSystem.IsWindows())
            Skip = "Unix only: there is no shell probe on Windows, which inherits its environment normally.";
    }
}
