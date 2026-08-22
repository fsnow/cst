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
        Assert.True(ProcessShellProbeRunner.Decide(payloadLength: 2553, wellFormed: true, exitCode: null));
    }

    /// <summary>The ordinary case, unchanged: the shell exited cleanly and wrote something.</summary>
    [Fact]
    public void A_clean_exit_with_output_is_a_success()
    {
        Assert.True(ProcessShellProbeRunner.Decide(payloadLength: 2553, wellFormed: true, exitCode: 0));
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
        Assert.False(ProcessShellProbeRunner.Decide(payloadLength: 2553, wellFormed: true, exitCode: 1));
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
        Assert.False(ProcessShellProbeRunner.Decide(payloadLength: 0, wellFormed: false, exitCode));
    }

    /// <summary>
    /// A payload that does not end where <c>env -0</c> would end it is refused when there is no exit code to
    /// consult. (#824, review)
    ///
    /// <para>The case: a profile that mangles PATH until <c>env</c> cannot be found leaves nothing behind but
    /// the sentinel, and the first version of this fix accepted it because one byte is more than zero. It
    /// reached the reader as "your login shell exports none of the variables we know about", and — being a
    /// success — stopped the ladder retrying with <c>-l</c>, which is where the keys were.</para>
    /// </summary>
    [Fact]
    public void A_payload_that_does_not_end_cleanly_is_refused_when_no_exit_code_is_available()
    {
        Assert.False(ProcessShellProbeRunner.Decide(payloadLength: 1, wellFormed: false, exitCode: null));
    }

    /// <summary>
    /// An observed clean exit still carries a payload that ended mid-entry.
    ///
    /// <para>Deliberate: where the runtime saw the shell exit cleanly there is real evidence the write
    /// finished, and this branch must not become a second, stricter gate on the ordinary path.</para>
    /// </summary>
    [Fact]
    public void A_clean_observed_exit_does_not_need_the_payload_to_vouch_for_itself()
    {
        Assert.True(ProcessShellProbeRunner.Decide(payloadLength: 2553, wellFormed: false, exitCode: 0));
    }

    /// <summary>What <see cref="ProcessShellProbeRunner.WellFormed"/> accepts, and what it must not.</summary>
    [Theory]
    [InlineData(new byte[] { 0x41, 0x3d, 0x31, 0x00 }, true)]   // A=1\0 — a complete entry
    [InlineData(new byte[] { 0x00, 0x41, 0x3d, 0x31 }, false)]  // truncated mid-entry
    [InlineData(new byte[] { 0x00 }, false)]                    // sentinel alone: env never ran
    [InlineData(new byte[0], false)]
    public void Only_a_payload_that_ends_where_env_would_end_it_is_well_formed(byte[] payload, bool expected)
    {
        Assert.Equal(expected, ProcessShellProbeRunner.WellFormed(payload));
    }

    /// <summary>
    /// Chatter before the sentinel is still well formed.
    ///
    /// <para>The review proposed requiring a LEADING NUL as well. That would have been wrong: a profile's
    /// banners reach stdout before the probe command runs, so the first byte is usually theirs. The sentinel
    /// exists to survive exactly that, and testing for it at position zero would reject every chatty
    /// profile.</para>
    /// </summary>
    [Fact]
    public void A_banner_before_the_sentinel_does_not_make_a_payload_malformed()
    {
        // "hi" NUL "A=1" NUL
        var payload = new byte[] { 0x68, 0x69, 0x00, 0x41, 0x3d, 0x31, 0x00 };
        Assert.True(ProcessShellProbeRunner.WellFormed(payload));
    }
}
