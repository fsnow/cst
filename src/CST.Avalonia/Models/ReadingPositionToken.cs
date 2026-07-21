namespace CST.Avalonia.Models;

/// <summary>
/// Canonical reading-position representation (#434): the two nearest anchors that bracket the viewport top,
/// plus the fraction of the way from the upper anchor to the lower one.
///
/// It stores a RELATIVE position between two stable anchor NAMES, not an absolute pixel offset, so it is
/// robust to reflow and to different window sizes — restoring interpolates between the anchors' CURRENT
/// positions, so the reading position is preserved even when the content between them re-lays-out. Anchor
/// names (page V/M/P/T/O, paragraph, chapter) derive from the source XML rather than the rendered script, so
/// the token also survives a script-change reload by construction.
///
/// Semantics of the two ends:
/// <list type="bullet">
///   <item><see cref="Above"/> = null, <see cref="Below"/> set → the position is at the document start
///     (above the first anchor); it resolves to scrollTop 0.</item>
///   <item><see cref="Below"/> = null, <see cref="Above"/> set → the position is past the last anchor
///     (clamped to it).</item>
///   <item><b>Both</b> null → no anchors / empty cache — and also what a default-constructed or malformed
///     token looks like — so it is treated as UNRESOLVABLE (the caller applies its own fuzzy fallback),
///     NOT as document start. A genuine document-start token always names a real first anchor as its
///     <see cref="Below"/>.</item>
/// </list>
/// </summary>
public sealed class ReadingPositionToken
{
    /// <summary>Name of the nearest anchor at or ABOVE the viewport top; null means "at document start".</summary>
    public string? Above { get; set; }

    /// <summary>Name of the nearest anchor BELOW the viewport top; null means "past the last anchor".</summary>
    public string? Below { get; set; }

    /// <summary>How far the viewport top sits from <see cref="Above"/> (0) toward <see cref="Below"/> (1).
    /// Always in [0, 1] (clamped at capture).</summary>
    public double Fraction { get; set; }
}
