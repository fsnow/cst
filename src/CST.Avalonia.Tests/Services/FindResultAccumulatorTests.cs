using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

// #570: Chromium reports one find across SEVERAL replies that carry different information, and folding
// them into "3 of 47" has already been wrong once in this feature — it displayed 0/47 for a search that
// had genuinely selected match 1, which then made Next look as though it skipped to 2. These tests exist
// because that logic used to live in view code-behind where nothing could reach it.
//
// The two rules under test:
//   * an ordinal of 0 means "this reply says nothing about the active match", NOT "no match is active"
//   * only the reply with finalUpdate carries an authoritative total
public class FindResultAccumulatorTests
{
    // The realistic shape of one search's replies: partial counts while scanning, the ordinal arriving in
    // its own reply, then a final total that says nothing about the ordinal.
    private static FindResultAccumulator AfterATypicalSearch(int id = 1, int total = 47, int ordinal = 1)
    {
        var a = new FindResultAccumulator();
        a.Accept(id, count: 3, activeMatchOrdinal: 0, finalUpdate: false);
        a.Accept(id, count: 0, activeMatchOrdinal: ordinal, finalUpdate: false);
        a.Accept(id, count: total, activeMatchOrdinal: 0, finalUpdate: true);
        return a;
    }

    [Fact]
    public void TheActiveOrdinalSurvivesAFinalReplyThatDoesNotMentionIt()
    {
        // The original bug, pinned exactly: the final reply carries ordinal 0, and reading it literally
        // rendered "0/47" for a search sitting on match 1.
        var a = AfterATypicalSearch();
        Assert.Equal("1/47", a.Format());
    }

    [Fact]
    public void PartialCountsAreNotDisplayedAsTheTotal()
    {
        // Rendering intermediate counts makes the number tick visibly upward after every keystroke.
        var a = new FindResultAccumulator();
        a.Accept(1, count: 3, activeMatchOrdinal: 1, finalUpdate: false);
        Assert.Equal(0, a.Count);
        a.Accept(1, count: 47, activeMatchOrdinal: 0, finalUpdate: true);
        Assert.Equal(47, a.Count);
    }

    [Fact]
    public void NoMatchesRendersZeroOfZero()
    {
        var a = new FindResultAccumulator();
        a.Accept(1, count: 0, activeMatchOrdinal: 0, finalUpdate: true);
        Assert.Equal("0/0", a.Format());
    }

    [Fact]
    public void ATotalWithNoOrdinalYetShowsTheFirstMatch_NotZero()
    {
        // Chromium selects the first match on a new search; if the total lands before the reply naming the
        // ordinal, showing 0 would be both wrong and a visible flicker before it corrected.
        var a = new FindResultAccumulator();
        a.Accept(1, count: 12, activeMatchOrdinal: 0, finalUpdate: true);
        Assert.Equal("1/12", a.Format());
    }

    [Fact]
    public void TheOrdinalNeverExceedsTheCount_AcrossANormalNavigation()
    {
        var a = AfterATypicalSearch(total: 3);
        a.Accept(1, count: 0, activeMatchOrdinal: 2, finalUpdate: false);
        Assert.Equal("2/3", a.Format());
        a.Accept(1, count: 0, activeMatchOrdinal: 3, finalUpdate: false);
        Assert.Equal("3/3", a.Format());
    }

    // ---- Stale replies from a superseded search ---------------------------------------------------

    [Fact]
    public void ALateReplyFromAPreviousSearchIsRejected()
    {
        // The scenario that produced a phantom count: type a query in a big book, then keep typing. The
        // earlier search's final reply can arrive after the newer search has started. Installing its total
        // would show a count belonging to a query the user has already moved on from.
        var a = AfterATypicalSearch(id: 5, total: 94);

        a.Reset();                                                   // new search begins
        a.Accept(6, count: 2, activeMatchOrdinal: 1, finalUpdate: true);
        Assert.Equal("1/2", a.Format());

        var accepted = a.Accept(5, count: 94, activeMatchOrdinal: 40, finalUpdate: true);   // late, stale
        Assert.False(accepted);
        Assert.Equal("1/2", a.Format());
    }

    [Fact]
    public void ResetDoesNotForgetTheIdentifierHighWaterMark()
    {
        // Reset clears the displayed numbers but must keep the newest id, or the very next stale reply
        // would be indistinguishable from a fresh one.
        var a = AfterATypicalSearch(id: 9);
        a.Reset();
        Assert.False(a.Accept(8, count: 500, activeMatchOrdinal: 1, finalUpdate: true));
        Assert.Equal(0, a.Count);
    }

    [Fact]
    public void RepliesCarryingTheSameIdentifierAreStillAccepted()
    {
        // All replies for one search share an id, so the staleness test must be "older than", not
        // "different from" — otherwise a single search would only ever register its first reply.
        var a = new FindResultAccumulator();
        Assert.True(a.Accept(3, count: 0, activeMatchOrdinal: 1, finalUpdate: false));
        Assert.True(a.Accept(3, count: 9, activeMatchOrdinal: 0, finalUpdate: true));
        Assert.Equal("1/9", a.Format());
    }

    [Fact]
    public void ResetClearsWhatIsDisplayed()
    {
        var a = AfterATypicalSearch();
        a.Reset();
        Assert.Equal(0, a.Count);
        Assert.Equal(0, a.Ordinal);
        Assert.Equal("", a.Format());   // nothing known again, not "no matches"
    }
}
