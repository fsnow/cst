using System;
using Xunit;

namespace CST.Avalonia.Tests;

/// <summary>
/// A <see cref="FactAttribute"/> that reports SKIPPED off Windows instead of passing.
///
/// <para>The alternative - an early <c>return</c> when the platform is wrong - makes the macOS run report a
/// green test that executed no assertions. That is how a platform quietly stops being covered: the count stays
/// reassuring while the coverage goes to zero, and nothing ever turns red to say otherwise.</para>
///
/// <para>xUnit 2.x has no <c>Assert.Skip</c> (that is v3), and <c>SkippableFact</c> is another dependency for
/// six lines of work - so set <c>Skip</c> in the constructor, which the 2.x runner honours.</para>
/// </summary>
public sealed class WindowsFactAttribute : FactAttribute
{
    public WindowsFactAttribute()
    {
        if (!OperatingSystem.IsWindows())
            Skip = "Windows only: DPAPI is not available on this platform.";
    }
}
