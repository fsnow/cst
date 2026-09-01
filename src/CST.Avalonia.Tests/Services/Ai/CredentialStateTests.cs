using System;
using System.Linq;
using CST.Avalonia.Services.Ai.Credentials;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// A stored key this build cannot read is not a missing key. (#926)
///
/// <para><b>The defect these pin.</b> Installing a signed build over a machine that had been running
/// development builds produced eight macOS authorization prompts, because the login keychain binds each item's
/// ACL to the binary that created it. Dismissing them made every lookup return <c>errSecAuthFailed</c>, which
/// the store reported as "none stored" — so three providers whose keys were present, correct and one
/// authorization away were presented as unconfigured, and the remedy the app offered was to type them in
/// again.</para>
///
/// <para><b>These cover the seam itself.</b> What the reader is then told is asserted where those helpers
/// already live: the badge, the removal affordance and the environment fallthrough in
/// <c>AiConnectionsViewModelTests</c>, and <c>SourceFor</c> in <c>AiConnectionServiceTests</c>.</para>
/// </summary>
public class CredentialStateTests
{
    // ---- classifying a Keychain status ---------------------------------------------------------------------

    /// <summary>
    /// The line the whole issue is about: a declined authorization is not an absent item. (#926)
    ///
    /// <para><b>Why this is a separate function at all.</b> It lived inside <c>Find</c>, wrapped in P/Invoke
    /// calls no test can drive — so a mutation putting <c>errSecAuthFailed</c> back to "not stored" killed no
    /// test, on the one line that caused the bug. Extracting the mapping is what makes it assertable.</para>
    /// </summary>
    [Theory]
    [InlineData(MacOsKeychain.ErrSecAuthFailed)]              // the item's ACL names a different binary
    [InlineData(MacOsKeychain.ErrSecUserCanceled)]            // the reader dismissed the prompt
    [InlineData(MacOsKeychain.ErrSecInteractionNotAllowed)]   // locked, with no UI available to ask
    public void A_read_the_os_refused_is_unreadable_not_absent(int status)
    {
        Assert.Equal(CredentialState.Unreadable, MacOsKeychain.Classify(status));
    }

    /// <summary>
    /// The status numbers themselves, against Apple's <c>SecBase.h</c>. (#926)
    ///
    /// <para>Nothing else can catch a wrong one. <see cref="MacOsKeychain.Classify"/> is tested through these
    /// same constants, so the test and the code would move together and a typo would stay green — the same
    /// hazard <c>WindowsDpapiStoreTests.The_entropy_is_stable</c> exists to cover. A wrong number fails
    /// silently in the worst direction: the status stops matching its case, falls to the default, and a key
    /// that genuinely is not there begins reporting itself as stored-but-unreadable.</para>
    /// </summary>
    [Fact]
    public void The_keychain_status_codes_are_the_ones_apple_documents()
    {
        Assert.Equal(0, MacOsKeychain.ErrSecSuccess);
        Assert.Equal(-25300, MacOsKeychain.ErrSecItemNotFound);
        Assert.Equal(-25293, MacOsKeychain.ErrSecAuthFailed);
        Assert.Equal(-128, MacOsKeychain.ErrSecUserCanceled);
        Assert.Equal(-25308, MacOsKeychain.ErrSecInteractionNotAllowed);
    }

    [Fact]
    public void Only_item_not_found_means_nothing_is_stored()
    {
        // The one status that should send the reader to type a key in. Everything else must not.
        Assert.Equal(CredentialState.NotStored, MacOsKeychain.Classify(MacOsKeychain.ErrSecItemNotFound));
        Assert.Equal(CredentialState.Found, MacOsKeychain.Classify(MacOsKeychain.ErrSecSuccess));
    }

    [Fact]
    public void An_unrecognised_status_is_unreadable_rather_than_absent()
    {
        // A Security.framework failure nobody anticipated is not evidence that an item is gone. Claiming
        // absence we cannot see is the harm being fixed, so the unknown case errs towards under-claiming.
        Assert.Equal(CredentialState.Unreadable, MacOsKeychain.Classify(-25291));   // errSecNotAvailable
        Assert.Equal(CredentialState.Unreadable, MacOsKeychain.Classify(-50));      // errSecParam
        Assert.Equal(CredentialState.Unreadable, MacOsKeychain.Classify(int.MinValue));
    }

    // ---- the outcome type ----------------------------------------------------------------------------------

    [Fact]
    public void An_unreadable_read_carries_no_secret_but_still_reports_the_item_exists()
    {
        var read = CredentialRead.Unreadable;

        Assert.Equal(CredentialState.Unreadable, read.State);
        Assert.Null(read.Secret);
        Assert.True(read.Exists);
    }

    [Fact]
    public void Nothing_stored_does_not_claim_an_item_exists()
    {
        // The other side of Exists, and the one that matters: if this ever returned true, every unconfigured
        // provider would be described as holding a key it cannot read.
        Assert.False(CredentialRead.NotStored.Exists);
        Assert.False(CredentialRead.Unavailable.Exists);
        Assert.True(CredentialRead.Found("sk-test").Exists);
    }

    [Fact]
    public void The_four_outcomes_describe_themselves_differently()
    {
        // The log line was one word for all four. A future change that collapses any two of them again fails
        // here rather than in a bug report six weeks later.
        var words = new[]
        {
            CredentialRead.Found("sk-test").Describe(),
            CredentialRead.NotStored.Describe(),
            CredentialRead.Unreadable.Describe(),
            CredentialRead.Unavailable.Describe(),
        };

        Assert.Equal(4, words.Distinct(StringComparer.Ordinal).Count());

        // And none of them says anything about the value, which is the standing rule for this whole path.
        Assert.All(words, w => Assert.DoesNotContain("sk-test", w, StringComparison.Ordinal));
    }

    [Fact]
    public void Get_still_yields_null_for_every_outcome_that_has_no_secret()
    {
        // Callers that only want the value keep working unchanged. This is what makes the new state safe to
        // add without auditing every call site at once.
        Assert.Equal("sk-test", CredentialRead.Found("sk-test").Secret);
        Assert.Null(CredentialRead.NotStored.Secret);
        Assert.Null(CredentialRead.Unreadable.Secret);
        Assert.Null(CredentialRead.Unavailable.Secret);
    }

}
