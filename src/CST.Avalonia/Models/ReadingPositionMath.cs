using System;

namespace CST.Avalonia.Models;

/// <summary>
/// Pure, DOM-free fidelity math for the reading-position token (#434), kept out of the WebView JS so it is
/// directly unit-testable. The JS side does only the cheap DOM work — find the anchors bracketing the
/// viewport top, read their live <c>getBoundingClientRect</c> positions, and <c>scrollTo</c> — and feeds
/// those raw numbers here; this class owns the fraction, the interpolation, and every degenerate-case guard
/// (Fable review §3).
///
/// All positions are absolute document offsets in pixels (a live <c>scrollTop</c>-space value), NOT the
/// cached <c>build()</c>-time snapshots — capture reads live rects so the fraction can't be skewed by a
/// reflow since the last cache build (Fable review §1).
/// </summary>
public static class ReadingPositionMath
{
    // Positions within this many pixels are treated as coincident (page + paragraph anchor on one line), which
    // would otherwise make the bracket a zero/near-zero interval and the fraction meaningless.
    private const double CoincidentEpsilon = 0.5;

    /// <summary>
    /// Build a token from the bracketing anchors the JS side found for the current viewport top.
    /// </summary>
    /// <param name="above">Name of the nearest anchor at/above the viewport top, or null if none (document start).</param>
    /// <param name="abovePos">Live position of <paramref name="above"/> (ignored when it is null).</param>
    /// <param name="below">Name of the nearest anchor below the viewport top, or null if none (past last anchor).</param>
    /// <param name="belowPos">Live position of <paramref name="below"/> (ignored when it is null).</param>
    /// <param name="scrollTop">The current viewport-top offset (raw scrollTop; no display fudge factors — Fable §7).</param>
    public static ReadingPositionToken Capture(string? above, double abovePos, string? below, double belowPos, double scrollTop)
    {
        // Both ends present → real interpolation between them.
        if (above != null && below != null)
        {
            var denom = belowPos - abovePos;
            double fraction = denom <= CoincidentEpsilon
                ? 0.0                                   // coincident/inverted bracket → pin to the upper anchor
                : Clamp01((scrollTop - abovePos) / denom);
            return new ReadingPositionToken { Above = above, Below = below, Fraction = fraction };
        }

        // Past the last anchor → clamp to it. At/before the first anchor, or no anchors at all → document start.
        return new ReadingPositionToken { Above = above, Below = below, Fraction = 0.0 };
    }

    /// <summary>
    /// Resolve a token to a target scrollTop using the anchors' CURRENT (post-reload / post-reflow) positions,
    /// as re-read by the JS side at restore time. Returns null only when the token names an anchor bracket but
    /// NEITHER end still exists — the caller then applies its own fuzzy fallback (e.g. nearest-paragraph). A
    /// document-start token (<see cref="ReadingPositionToken.Above"/> == null) resolves to 0 without needing any
    /// position, so restore never has to wait on the anchor cache (Fable §2).
    /// </summary>
    /// <param name="token">The saved token.</param>
    /// <param name="abovePos">Current position of <see cref="ReadingPositionToken.Above"/>, or null if it no longer exists.</param>
    /// <param name="belowPos">Current position of <see cref="ReadingPositionToken.Below"/>, or null if it no longer exists.</param>
    public static double? ResolveTarget(ReadingPositionToken token, double? abovePos, double? belowPos)
    {
        if (token == null) return null;

        if (token.Above == null)
        {
            // A GENUINE document-start token always names the first anchor as its lower end (there IS an anchor
            // below the top), so Above==null WITH a Below resolves to 0 with no positions needed (Fable §2). But
            // a token with BOTH ends null is the "no anchors / empty cache" case — and also what a default-
            // constructed or malformed-deserialized token looks like — so return null for the caller's fuzzy
            // fallback rather than silently scrolling to the top (Fable review §1).
            return token.Below != null ? 0.0 : (double?)null;
        }

        // Past the last anchor when captured → land on the upper anchor's current position.
        if (token.Below == null)
            return abovePos; // null if the anchor vanished → caller fuzzy-fallbacks

        // Full bracket. Interpolate when both ends survive; degrade to whichever end remains (Fable §5).
        if (abovePos is double a && belowPos is double b)
        {
            var denom = b - a;
            // Defend against a NaN/out-of-range Fraction from a hand-built or malformed-persisted token — a NaN
            // would slip through the range clamp below (NaN compares false both ways) and yield scrollTo(NaN).
            var fraction = double.IsNaN(token.Fraction) ? 0.0 : Clamp01(token.Fraction);
            double target = Math.Abs(denom) <= CoincidentEpsilon
                ? a                                     // coincident/inverted → the upper anchor's top edge
                : a + fraction * denom;
            // Never scroll outside the bracket even if reflow inverted or squeezed it (Fable §3).
            return Clamp(target, Math.Min(a, b), Math.Max(a, b));
        }
        if (abovePos is double aOnly) return aOnly;     // below vanished → upper anchor top edge
        if (belowPos is double bOnly) return bOnly;     // above vanished → lower anchor top edge
        return null;                                    // both vanished → caller's fuzzy fallback
    }

    private static double Clamp01(double v) => v < 0 ? 0 : (v > 1 ? 1 : v);

    private static double Clamp(double v, double lo, double hi) => v < lo ? lo : (v > hi ? hi : v);
}
