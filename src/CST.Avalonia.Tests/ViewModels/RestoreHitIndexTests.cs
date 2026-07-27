using CST.Avalonia.ViewModels;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

// #441: the restored hit COUNTER and the restored VIEWPORT must agree. The viewport clamps a saved hit to
// the last hit (BookDisplayView: Math.Min(savedHit, total)); the counter used to collapse an out-of-range
// index to 1, so the reader showed the last hit while the indicator read "1 of M" — and stepping to the
// next hit then started from the wrong place.
public class RestoreHitIndexTests
{
    // ---- the bug: out-of-range saved index ----

    [Theory]
    [InlineData(14, 13)]   // one past the end
    [InlineData(99, 13)]   // far past the end
    [InlineData(2, 1)]     // book has a single hit
    public void Out_of_range_saved_hit_clamps_to_the_last_hit_not_to_1(int saved, int totalHits)
    {
        // Matches the viewport's own Math.Min(savedHit, total).
        Assert.Equal(totalHits, BookDisplayViewModel.ClampRestoreHitIndex(saved, totalHits));
    }

    [Fact]
    public void Counter_and_viewport_agree_for_every_saved_index_up_to_well_past_the_end()
    {
        const int totalHits = 13;
        for (var saved = 1; saved <= totalHits * 3; saved++)
        {
            var viewport = System.Math.Min(saved, totalHits);   // BookDisplayView's clamp
            var counter = BookDisplayViewModel.ClampRestoreHitIndex(saved, totalHits);
            Assert.Equal(viewport, counter);
        }
    }

    // ---- unchanged behaviour ----

    [Theory]
    [InlineData(1, 13)]
    [InlineData(7, 13)]
    [InlineData(13, 13)]
    public void In_range_saved_hit_is_returned_as_is(int saved, int totalHits)
    {
        Assert.Equal(saved, BookDisplayViewModel.ClampRestoreHitIndex(saved, totalHits));
    }

    [Fact]
    public void No_saved_hit_falls_back_to_the_first_hit()
    {
        Assert.Equal(1, BookDisplayViewModel.ClampRestoreHitIndex(null, 13));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-5)]
    public void Non_positive_saved_hit_falls_back_to_the_first_hit(int saved)
    {
        Assert.Equal(1, BookDisplayViewModel.ClampRestoreHitIndex(saved, 13));
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-3)]
    public void No_hits_in_the_book_yields_1_whatever_was_saved(int totalHits)
    {
        // The highlight pipeline only consults this when totalHits > 0, but the guard must hold:
        // returning a saved index here would put the counter above a zero total.
        Assert.Equal(1, BookDisplayViewModel.ClampRestoreHitIndex(9, totalHits));
        Assert.Equal(1, BookDisplayViewModel.ClampRestoreHitIndex(null, totalHits));
    }
}
