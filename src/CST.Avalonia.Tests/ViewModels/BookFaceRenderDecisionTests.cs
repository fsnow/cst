using CST.Avalonia.ViewModels;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// The two decisions behind re-rendering a book when its face changes. (#613)
///
/// <para>
/// The bug these replace could not be reached through either decision alone, which is why it survived
/// review: the font event compared the new face against what was rendered and against what was on its way,
/// and both comparisons were correct in isolation. What neither could see was ORDER. Pick B, then revert to
/// A while B is still transforming: the revert matches what is currently rendered, so the event correctly
/// concludes it has nothing to do — and then B lands. Settings say A, the book shows B, and because
/// settings already hold A no further event will ever be raised.
/// </para>
///
/// <para>
/// The fix is not a third field. It is asking the question after the fact, when there is a settled answer:
/// <see cref="BookDisplayViewModel.CompletedRenderIsStale"/> runs when a render finishes and compares what
/// it set out to do against what the settings now say. Every interleaving reduces to that one comparison,
/// which is why these tests can drive it as data.
/// </para>
/// </summary>
public class BookFaceRenderDecisionTests
{
    private const string A = "\"Times Ext Roman\", Tahoma";
    private const string B = "\"Doulos SIL\"";

    // ---- The reported bug, as a sequence ---------------------------------------------------------

    [Fact]
    public void Reverting_to_the_current_face_mid_render_is_caught_when_that_render_lands()
    {
        // 1. Showing A. 2. User picks B — a render starts, attempting B.
        var attempted = B;

        // 3. User reverts to A while B is transforming. The event is dropped: a render is in flight, and it
        //    cannot know whether that render already covers this. Dropping it is now SAFE rather than a
        //    silent loss, because of step 4.
        Assert.False(BookDisplayViewModel.FaceChangeNeedsRender(A, renderedFace: A, renderInFlight: true));

        // 4. B lands. The settings say A; this render attempted B. Stale — regenerate.
        Assert.True(BookDisplayViewModel.CompletedRenderIsStale(currentFace: A, attemptedFace: attempted));
    }

    [Fact]
    public void The_correction_settles_rather_than_ping_ponging()
    {
        // The re-render attempts A, and by then the settings still say A — so the chain stops. Convergence
        // is the property that makes a self-scheduling correction safe to have at all.
        Assert.False(BookDisplayViewModel.CompletedRenderIsStale(currentFace: A, attemptedFace: A));
    }

    [Fact]
    public void A_render_that_failed_is_not_perpetually_stale()
    {
        // Why the comparison is against what the render ATTEMPTED rather than what it applied. A book whose
        // XML directory is misconfigured renders an error page and applies no face at all; if staleness were
        // judged against the applied face, every completion would look stale and re-post itself forever.
        Assert.False(BookDisplayViewModel.CompletedRenderIsStale(currentFace: B, attemptedFace: B));
    }

    // ---- The event decision ----------------------------------------------------------------------

    [Fact]
    public void A_change_to_a_new_face_with_nothing_in_flight_renders()
    {
        Assert.True(BookDisplayViewModel.FaceChangeNeedsRender(B, renderedFace: A, renderInFlight: false));
    }

    [Fact]
    public void An_event_that_does_not_change_this_books_face_is_ignored()
    {
        // Font settings are per script and the event is global: touching another script's chrome font must
        // not regenerate every open book.
        Assert.False(BookDisplayViewModel.FaceChangeNeedsRender(A, renderedFace: A, renderInFlight: false));
    }

    [Fact]
    public void The_first_event_on_a_book_that_has_never_rendered_renders()
    {
        Assert.True(BookDisplayViewModel.FaceChangeNeedsRender(A, renderedFace: null, renderInFlight: false));
    }

    [Fact]
    public void Nothing_starts_a_second_render_while_one_is_running()
    {
        // Even for a genuinely different face. The completion check picks it up — and unlike a queued
        // render, it cannot stack up one regeneration per keystroke in the picker.
        Assert.False(BookDisplayViewModel.FaceChangeNeedsRender(B, renderedFace: A, renderInFlight: true));
    }

    // ---- Comparison semantics --------------------------------------------------------------------

    [Fact]
    public void Faces_are_compared_exactly()
    {
        // These are CSS font stacks the app itself produced, not user input: two that differ in case or
        // spacing are two different declarations, and rendering one while believing the other is showing is
        // the whole failure mode here. BookFontResolver has already normalised and quoted anything the user
        // supplied by the time either of these sees it.
        Assert.True(BookDisplayViewModel.FaceChangeNeedsRender(
            "\"Doulos SIL\"", renderedFace: "\"doulos sil\"", renderInFlight: false));
        Assert.True(BookDisplayViewModel.CompletedRenderIsStale(
            "\"Doulos SIL\", Tahoma", attemptedFace: "\"Doulos SIL\",Tahoma"));
    }

    [Fact]
    public void A_book_that_has_never_rendered_counts_as_stale_against_any_face()
    {
        // Guards the null side of the completion check: a first render whose attempt was never recorded
        // must not silently be treated as up to date.
        Assert.True(BookDisplayViewModel.CompletedRenderIsStale(A, attemptedFace: null));
    }
}
