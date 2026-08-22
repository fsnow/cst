using CST.Avalonia.Services.Ai.Credentials;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// #824: when a probe that produced output counts as having succeeded.
///
/// <para><b>The incident.</b> Inside the running app on macOS the login-shell probe reported a timeout every
/// single time, and the Providers tab told the reader their shell profile was too slow to load. It was not:
/// the shell finished in about forty milliseconds, wrote its entire environment, and stdout reached EOF —
/// while <c>Process.HasExited</c> was still false when the five-second budget expired. The child's exit
/// status is consumed before the runtime's own bookkeeping sees it, so the exit could never be observed
/// however long anything waited.</para>
///
/// <para><b>Why these tests are shaped around a null exit code.</b> The old policy was
/// <c>process.ExitCode == 0</c>, which cannot express "the work finished and there is no status to read".
/// Reading <c>ExitCode</c> in that state throws, so the code took the only branch left and threw the payload
/// away. The first test below is the one that fails against the old policy; the rest hold it to everything it
/// was already right about, so restoring the strictness cannot be mistaken for a fix.</para>
///
/// <para>The spawn path itself has no test and cannot usefully have one: the failure needs CEF loaded in the
/// same process, so a test that ran a real shell would pass either way. What is testable is the policy, and
/// pinning it is what stops a later refactor quietly reinstating exit-or-nothing.</para>
/// </summary>
public class ShellProbeOutcomeTests
{
    /// <summary>
    /// A complete payload with no observed exit is a success.
    ///
    /// <para>This is #824 itself. Against the old policy it fails, because there is no exit code to compare
    /// against zero and asking for one throws.</para>
    /// </summary>
    [Fact]
    public void Output_that_arrived_is_kept_even_when_the_exit_was_never_observed()
    {
        Assert.True(ProcessShellProbeRunner.Decide(payloadLength: 2553, exitCode: null));
    }

    /// <summary>The ordinary case, unchanged: the shell exited cleanly and wrote something.</summary>
    [Fact]
    public void A_clean_exit_with_output_is_a_success()
    {
        Assert.True(ProcessShellProbeRunner.Decide(payloadLength: 2553, exitCode: 0));
    }

    /// <summary>
    /// A shell that exited non-zero is still refused.
    ///
    /// <para>Kept deliberately. Where the runtime DID observe the exit, its verdict is real evidence and the
    /// loosening in #824 was only ever about the case where no verdict exists.</para>
    /// </summary>
    [Fact]
    public void A_failing_exit_code_is_still_a_failure()
    {
        Assert.False(ProcessShellProbeRunner.Decide(payloadLength: 2553, exitCode: 1));
    }

    /// <summary>
    /// Nothing written is a failure however the process ended.
    ///
    /// <para>Without this, "no exit code" plus "no output" would read as success and the parser would be
    /// handed an empty buffer — reported to the reader as a shell that holds no provider keys, which is a
    /// different and equally wrong sentence.</para>
    /// </summary>
    [Theory]
    [InlineData(null)]
    [InlineData(0)]
    [InlineData(1)]
    public void An_empty_payload_is_never_a_success(int? exitCode)
    {
        Assert.False(ProcessShellProbeRunner.Decide(payloadLength: 0, exitCode));
    }
}
