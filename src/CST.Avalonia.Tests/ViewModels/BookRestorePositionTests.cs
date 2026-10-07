using CST.Avalonia.Models;
using CST.Avalonia.ViewModels;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// Where a restored book opens, and what it saves before the reader looks at it. (#1032)
///
/// <para><b>[fsnow]</b>, testing beta 8: <i>"my position is not restoring correctly in book 185"</i> and <i>"What about
/// the DN1 issue ?"</i>. [observed] Book 185 was opened from search results and saved well past its hit, and
/// reopened at the hit; the DN1 book was a restored tab he had not brought forward, which lost its place at the
/// next quit.</para>
/// </summary>
public class BookRestorePositionTests
{
    public BookRestorePositionTests() => ReactiveUiTestInit.Ensure();

    // ---- What is queued ------------------------------------------------------------------------------

    [Fact]
    public void A_search_book_with_a_saved_position_restores_the_position_and_still_restores_its_hit()
    {
        var plan = BookDisplayViewModel.PlanInitialRestore(
            hasPositionToken: true, hasAnchor: true, hasSearchTerms: true, hasSavedHit: true);

        Assert.True(plan.QueuePositionToken);   // where the reader was
        Assert.False(plan.QueueAnchor);
        Assert.True(plan.SetUpSearch);          // the "N of M" counter and the current hit's highlight
    }

    // A state file from before #434 has an anchor but no token. The hit is more exact than a paragraph start (#36).
    [Fact]
    public void Without_a_saved_position_a_search_book_goes_to_its_hit_not_the_paragraph_anchor()
    {
        var plan = BookDisplayViewModel.PlanInitialRestore(
            hasPositionToken: false, hasAnchor: true, hasSearchTerms: true, hasSavedHit: true);

        Assert.False(plan.QueuePositionToken);
        Assert.False(plan.QueueAnchor);
        Assert.True(plan.SetUpSearch);
    }

    [Fact]
    public void A_plain_book_restores_its_position_and_sets_up_no_search()
    {
        var plan = BookDisplayViewModel.PlanInitialRestore(
            hasPositionToken: true, hasAnchor: true, hasSearchTerms: false, hasSavedHit: false);

        Assert.Equal((true, false, false), plan);
    }

    [Fact]
    public void A_plain_book_with_only_an_anchor_restores_the_anchor()
    {
        var plan = BookDisplayViewModel.PlanInitialRestore(
            hasPositionToken: false, hasAnchor: true, hasSearchTerms: false, hasSavedHit: false);

        Assert.Equal((false, true, false), plan);
    }

    // A fresh open from search results has terms but no saved hit or position: it lands on the hit.
    [Fact]
    public void A_fresh_search_open_lands_on_its_hit()
    {
        var plan = BookDisplayViewModel.PlanInitialRestore(
            hasPositionToken: false, hasAnchor: false, hasSearchTerms: true, hasSavedHit: false);

        Assert.Equal((false, false, true), plan);
    }

    // ---- What an unviewed tab saves ------------------------------------------------------------------

    [Fact]
    public void A_restored_book_saves_its_restored_position_until_the_reader_moves()
    {
        var token = new ReadingPositionToken { Above = "dn1_1", Below = "dn1_2" };

        var vm = new BookDisplayViewModel(CST.Books.Inst[0], initialAnchor: "dn1_1", initialPositionToken: token);

        Assert.Equal("dn1_1", vm.LastCapturedAnchor);
        Assert.Same(token, vm.LastPositionToken);
    }

    // ---- What the view does with it ------------------------------------------------------------------

    [Fact]
    public void A_queued_position_wins_over_a_queued_hit_and_anchor()
    {
        Assert.Equal(CST.Avalonia.Views.BookDisplayView.PendingRestore.Position,
            CST.Avalonia.Views.BookDisplayView.PlanPendingRestore(hasPendingPosition: true, pendingHit: 1, hasPendingAnchor: true));
    }

    [Fact]
    public void Without_a_position_a_queued_hit_wins_over_the_anchor()
    {
        Assert.Equal(CST.Avalonia.Views.BookDisplayView.PendingRestore.Hit,
            CST.Avalonia.Views.BookDisplayView.PlanPendingRestore(false, 2, true));
        Assert.Equal(CST.Avalonia.Views.BookDisplayView.PendingRestore.Anchor,
            CST.Avalonia.Views.BookDisplayView.PlanPendingRestore(false, null, true));
        Assert.Equal(CST.Avalonia.Views.BookDisplayView.PendingRestore.None,
            CST.Avalonia.Views.BookDisplayView.PlanPendingRestore(false, 0, false));
    }

    // [fsnow], asked whether switching back to a search book's tab should keep the reader's position or return to
    // the current hit: "keep your position".
    [Fact]
    public void Switching_back_to_a_search_book_keeps_the_readers_position()
    {
        Assert.Equal(CST.Avalonia.Views.BookDisplayView.ReattachRestore.Position,
            CST.Avalonia.Views.BookDisplayView.PlanReattachRestore(hasLastPosition: true, hasSearchHighlights: true, currentHitIndex: 1));
    }

    [Fact]
    public void A_search_book_with_no_position_yet_lands_on_its_hit()
    {
        Assert.Equal(CST.Avalonia.Views.BookDisplayView.ReattachRestore.CurrentHit,
            CST.Avalonia.Views.BookDisplayView.PlanReattachRestore(false, true, 1));
        Assert.Equal(CST.Avalonia.Views.BookDisplayView.ReattachRestore.None,
            CST.Avalonia.Views.BookDisplayView.PlanReattachRestore(false, false, 0));
    }

    // ---- The way back to a single hit ----------------------------------------------------------------

    // [fsnow]: "if only one hit and the user has navigated away, one of the buttons should be enabled and will take
    // you back to the hit".
    [Fact]
    public void A_single_hit_off_screen_can_be_returned_to()
    {
        Assert.True(BookDisplayViewModel.CanReturnToSingleHit(hasSearchHighlights: true, totalHits: 1, currentHitOnScreen: false));
    }

    [Fact]
    public void A_single_hit_on_screen_or_several_hits_add_nothing()
    {
        Assert.False(BookDisplayViewModel.CanReturnToSingleHit(true, 1, currentHitOnScreen: true));
        Assert.False(BookDisplayViewModel.CanReturnToSingleHit(true, 3, false));   // First/Last already work by index
        Assert.False(BookDisplayViewModel.CanReturnToSingleHit(false, 0, false));
    }
}
