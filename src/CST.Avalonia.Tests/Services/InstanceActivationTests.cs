using System;
using System.IO;
using System.Linq;
using System.Threading;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// Bringing the running instance forward when a second launch happens. (#568)
///
/// <para>The pipe name is what decides WHICH instance is asked, so it carries the whole correctness argument:
/// too loose and two copies running against different data directories would activate each other; too strict
/// and the second launch fails to find the first over something as trivial as a trailing separator.</para>
/// </summary>
public class InstanceActivationTests
{
    private static string TempDir(string leaf) =>
        Path.Combine(Path.GetTempPath(), "cst-activation-tests", leaf);

    [Fact]
    public void The_same_data_directory_always_resolves_to_the_same_pipe()
    {
        // Both processes compute the name independently; if they ever disagreed the second launch would sit
        // waiting on a pipe nobody is serving, and the user would be back to the silent exit.
        var dir = TempDir("alpha");

        Assert.Equal(InstanceActivation.PipeNameFor(dir), InstanceActivation.PipeNameFor(dir));
    }

    [Fact]
    public void Different_data_directories_get_different_pipes()
    {
        // The lock is per data directory, so activation must be too. Two copies pointed at separate data
        // directories are both legitimately running, and neither should be able to raise the other.
        Assert.NotEqual(
            InstanceActivation.PipeNameFor(TempDir("alpha")),
            InstanceActivation.PipeNameFor(TempDir("beta")));
    }

    [Fact]
    public void A_trailing_separator_does_not_change_the_pipe()
    {
        // The same directory can reach this with or without a trailing slash depending on how it was composed.
        var dir = TempDir("gamma");

        Assert.Equal(
            InstanceActivation.PipeNameFor(dir),
            InstanceActivation.PipeNameFor(dir + Path.DirectorySeparatorChar));
    }

    [Fact]
    public void Case_follows_the_filesystem_rather_than_the_string()
    {
        // Windows and macOS treat these as one directory, so the pipe must agree - otherwise a shortcut whose
        // target is cased differently from the running instance's path silently fails to activate it.
        var lower = TempDir("delta");
        var upper = lower.ToUpperInvariant();

        if (OperatingSystem.IsWindows() || OperatingSystem.IsMacOS())
            Assert.Equal(InstanceActivation.PipeNameFor(lower), InstanceActivation.PipeNameFor(upper));
        else
            Assert.NotEqual(InstanceActivation.PipeNameFor(lower), InstanceActivation.PipeNameFor(upper));
    }

    [Fact]
    public void The_pipe_name_is_legal_and_short_enough_to_use()
    {
        // A data directory can be long and can hold characters that are not valid in a pipe name, which is why
        // the path is hashed rather than embedded. This pins that it stays true for a hostile path.
        var awkward = Path.Combine(Path.GetTempPath(), new string('x', 300), "a b:c*d?e");

        var name = InstanceActivation.PipeNameFor(awkward);

        Assert.True(name.Length < 200, $"pipe name is {name.Length} chars");
        Assert.DoesNotContain(Path.DirectorySeparatorChar, name);
        Assert.DoesNotContain(Path.AltDirectorySeparatorChar, name);
        Assert.All(name, c => Assert.True(char.IsLetterOrDigit(c) || c == '-', $"illegal character '{c}'"));
    }

    [Fact]
    public void Asking_when_nobody_is_listening_reports_failure_rather_than_throwing()
    {
        // The realistic case is a running instance from an OLDER build, which holds the lock but serves no
        // pipe. That has to degrade to "just exit" - the previous behaviour - not to an exception on a path
        // whose whole purpose is to make a second launch feel normal.
        var nobody = TempDir("nobody-" + Guid.NewGuid().ToString("N"));

        Assert.False(InstanceActivation.RequestActivation(nobody));
    }

    [WindowsFact]
    public void A_listening_instance_is_asked_to_come_forward()
    {
        // The mechanism end to end, in-process: a listener is started, a request is sent, and the callback the
        // app uses to raise its window actually runs.
        var dir = TempDir("roundtrip-" + Guid.NewGuid().ToString("N"));
        using var raised = new ManualResetEventSlim(false);

        InstanceActivation.StartListener(dir, () => raised.Set());

        // The listener binds on a background thread; give it a moment to be accepting before asking.
        Assert.True(SpinUntil(() => InstanceActivation.RequestActivation(dir), TimeSpan.FromSeconds(10)),
            "the request was never delivered");
        Assert.True(raised.Wait(TimeSpan.FromSeconds(10)), "the activation callback never ran");
    }

    private static bool SpinUntil(Func<bool> condition, TimeSpan timeout)
    {
        var deadline = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < deadline)
        {
            if (condition()) return true;
            Thread.Sleep(100);
        }
        return false;
    }
}
