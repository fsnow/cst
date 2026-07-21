using CST.Avalonia.Models;
using Xunit;

namespace CST.Avalonia.Tests.Models
{
    /// <summary>
    /// Fidelity math for the #434 reading-position token: capture a bracket + fraction from live positions,
    /// restore it against the anchors' current positions. Covers the degenerate guards from the Fable review
    /// (§3: zero/inverted bracket, out-of-range clamps; §5: an anchor vanishing on reload).
    /// </summary>
    public class ReadingPositionMathTests
    {
        // ---- Capture ----

        [Fact]
        public void Capture_bracketed_computes_fraction()
        {
            // scrollTop 130 sits 30 of 100 px from A(100) toward B(200) → 0.3
            var t = ReadingPositionMath.Capture("A", 100, "B", 200, 130);
            Assert.Equal("A", t.Above);
            Assert.Equal("B", t.Below);
            Assert.Equal(0.3, t.Fraction, 6);
        }

        [Theory]
        [InlineData(90, 0.0)]    // above the upper anchor → clamped to 0
        [InlineData(100, 0.0)]   // exactly on A
        [InlineData(150, 0.5)]
        [InlineData(200, 1.0)]   // exactly on B
        [InlineData(260, 1.0)]   // below the lower anchor → clamped to 1
        public void Capture_fraction_is_clamped_0_1(double scrollTop, double expected)
        {
            var t = ReadingPositionMath.Capture("A", 100, "B", 200, scrollTop);
            Assert.Equal(expected, t.Fraction, 6);
        }

        [Fact]
        public void Capture_coincident_bracket_pins_to_upper_no_nan()
        {
            // A and B on the same line (page + paragraph anchor) → denominator ~0 → fraction 0, not NaN.
            var t = ReadingPositionMath.Capture("A", 500, "B", 500, 500);
            Assert.Equal(0.0, t.Fraction, 6);
            Assert.False(double.IsNaN(t.Fraction));
        }

        [Fact]
        public void Capture_inverted_bracket_pins_to_upper_no_nan()
        {
            // JS handed an inverted bracket (belowPos < abovePos). Capture's SIGNED epsilon guard catches it
            // (negative denom <= 0.5) → fraction pinned to 0, not a negative/garbage value. (guards the
            // signed-vs-abs asymmetry between Capture and ResolveTarget)
            var t = ReadingPositionMath.Capture("A", 200, "B", 100, 150);
            Assert.Equal(0.0, t.Fraction, 6);
            Assert.False(double.IsNaN(t.Fraction));
        }

        [Fact]
        public void Capture_document_start_when_no_anchor_above()
        {
            var t = ReadingPositionMath.Capture(null, 0, "B", 200, 40);
            Assert.Null(t.Above);
            Assert.Equal("B", t.Below);
            Assert.Equal(0.0, t.Fraction, 6);
        }

        [Fact]
        public void Capture_past_last_anchor_when_no_anchor_below()
        {
            var t = ReadingPositionMath.Capture("Z", 9000, null, 0, 9500);
            Assert.Equal("Z", t.Above);
            Assert.Null(t.Below);
            Assert.Equal(0.0, t.Fraction, 6);
        }

        // ---- ResolveTarget ----

        [Fact]
        public void Resolve_bracketed_interpolates_current_positions()
        {
            var t = new ReadingPositionToken { Above = "A", Below = "B", Fraction = 0.3 };
            // After reflow A is at 300, B at 500 → 300 + 0.3*200 = 360
            Assert.Equal(360.0, ReadingPositionMath.ResolveTarget(t, 300, 500));
        }

        [Fact]
        public void Resolve_document_start_needs_no_positions()
        {
            var t = new ReadingPositionToken { Above = null, Below = "B", Fraction = 0.0 };
            Assert.Equal(0.0, ReadingPositionMath.ResolveTarget(t, null, null));
        }

        [Fact]
        public void Resolve_both_ends_null_returns_null_not_top()
        {
            // No-anchors / empty-cache token → unresolvable → caller fuzzy-fallbacks (must NOT silently scroll
            // to 0). Distinguishes it from a genuine document-start token, which names a real Below anchor.
            var t = new ReadingPositionToken { Above = null, Below = null, Fraction = 0.0 };
            Assert.Null(ReadingPositionMath.ResolveTarget(t, null, null));
        }

        [Fact]
        public void Resolve_default_or_malformed_token_returns_null_not_top()
        {
            // A default-constructed token (e.g. a truncated {} deserialization) is all-null → unresolvable,
            // NOT document-start. (Fable review §1)
            Assert.Null(ReadingPositionMath.ResolveTarget(new ReadingPositionToken(), 100, 200));
        }

        [Fact]
        public void Resolve_nan_fraction_stays_finite_in_bracket()
        {
            // A NaN Fraction (hand-built / malformed persisted) must not slip through the range clamp.
            var t = new ReadingPositionToken { Above = "A", Below = "B", Fraction = double.NaN };
            var target = ReadingPositionMath.ResolveTarget(t, 300, 500);
            Assert.NotNull(target);
            Assert.False(double.IsNaN(target!.Value));
            Assert.InRange(target.Value, 300, 500);
        }

        [Fact]
        public void Resolve_past_last_returns_upper_position()
        {
            var t = new ReadingPositionToken { Above = "Z", Below = null, Fraction = 0.0 };
            Assert.Equal(9000.0, ReadingPositionMath.ResolveTarget(t, 9000, null));
        }

        [Fact]
        public void Resolve_past_last_upper_missing_returns_null_for_fallback()
        {
            var t = new ReadingPositionToken { Above = "Z", Below = null, Fraction = 0.0 };
            Assert.Null(ReadingPositionMath.ResolveTarget(t, null, null));
        }

        [Fact]
        public void Resolve_clamps_target_into_the_bracket_on_inverted_reflow()
        {
            // Pathological: after reflow A(500) is BELOW B(400). Target must stay within [400, 500].
            var t = new ReadingPositionToken { Above = "A", Below = "B", Fraction = 0.9 };
            var target = ReadingPositionMath.ResolveTarget(t, 500, 400);
            Assert.NotNull(target);
            Assert.InRange(target!.Value, 400, 500);
        }

        [Fact]
        public void Resolve_coincident_current_positions_no_nan()
        {
            var t = new ReadingPositionToken { Above = "A", Below = "B", Fraction = 0.7 };
            var target = ReadingPositionMath.ResolveTarget(t, 500, 500);
            Assert.Equal(500.0, target);
        }

        [Fact]
        public void Resolve_above_vanished_falls_back_to_lower_edge()
        {
            var t = new ReadingPositionToken { Above = "A", Below = "B", Fraction = 0.5 };
            Assert.Equal(700.0, ReadingPositionMath.ResolveTarget(t, null, 700));
        }

        [Fact]
        public void Resolve_below_vanished_falls_back_to_upper_edge()
        {
            var t = new ReadingPositionToken { Above = "A", Below = "B", Fraction = 0.5 };
            Assert.Equal(300.0, ReadingPositionMath.ResolveTarget(t, 300, null));
        }

        [Fact]
        public void Resolve_both_vanished_returns_null_for_fuzzy_fallback()
        {
            var t = new ReadingPositionToken { Above = "A", Below = "B", Fraction = 0.5 };
            Assert.Null(ReadingPositionMath.ResolveTarget(t, null, null));
        }

        // ---- Round trip: capture then restore at the SAME positions lands back on scrollTop ----

        [Theory]
        [InlineData(130)]
        [InlineData(100)]
        [InlineData(199)]
        public void Roundtrip_same_positions_returns_scrolltop(double scrollTop)
        {
            var t = ReadingPositionMath.Capture("A", 100, "B", 200, scrollTop);
            var restored = ReadingPositionMath.ResolveTarget(t, 100, 200);
            Assert.NotNull(restored);
            Assert.Equal(scrollTop, restored!.Value, 6);
        }
    }
}
