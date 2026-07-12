using System.Collections.Generic;
using CST.Conversion;
using CST.Navigation;

namespace CST.Search
{
    /// <summary>
    /// Options for extracting a search-hit snippet ("search results in context" — the CSCD concordance
    /// feature). Context length is measured in <em>rendered</em> Pāli characters (XML tags and, when
    /// excluded, footnotes are transparent and don't count).
    /// </summary>
    /// <param name="OutputScript">Script for the snippet text (default romanized Latin).</param>
    /// <param name="IncludeFootnotes">Include <c>&lt;note&gt;</c> (variant-reading) text (default off).</param>
    /// <param name="MinChars">Prose floor: extend to neighboring sentences until at least this many rendered chars.</param>
    /// <param name="MaxChars">Prose ceiling: a longer single sentence is trimmed+ellipsized around the hit.</param>
    public sealed record SnippetOptions(
        Script OutputScript = Script.Latin,
        bool IncludeFootnotes = false,
        int MinChars = 60,
        int MaxChars = 320);

    /// <summary>A page reference at a hit, in one edition's numbering.</summary>
    public sealed record SnippetPageRef(PageEdition Edition, int Volume, int Number);

    /// <summary>
    /// One matched span to mark in a snippet, as source-XML char offsets. <see cref="IsAnchor"/> flags the
    /// single navigable span (the first unit of a proximity/phrase hit, or the lone term of a single-word hit);
    /// the rest are context. Input to <see cref="TeiSnippetExtractor.Extract(string, IReadOnlyList{SnippetMark}, BookMarkers, SnippetOptions)"/>.
    /// </summary>
    public sealed record SnippetMark(int Start, int End, bool IsAnchor);

    /// <summary>
    /// One highlight within a rendered snippet, in SNIPPET-LOCAL char offsets (into <see cref="SnippetResult.Snippet"/>):
    /// the span occupies <c>[Start, Start+Length)</c>. A single-term hit yields one; a proximity/phrase hit yields
    /// one per matched word, exactly one of which has <see cref="IsAnchor"/> true.
    /// </summary>
    public sealed record SnippetHighlight(int Start, int Length, bool IsAnchor);

    /// <summary>
    /// A rendered snippet plus the citation refs at the hit. <see cref="Snippet"/> is the concordance line
    /// (term-centered, romanized). <see cref="Highlights"/> is the ordered set of marked spans; for a single-term
    /// hit it has one entry and <see cref="HitStart"/>/<see cref="HitLength"/> mirror the anchor for continuity.
    /// </summary>
    public sealed record SnippetResult(
        string Snippet,
        int HitStart,
        int HitLength,
        int? ParagraphNumber,
        string? ParagraphBookCode,
        IReadOnlyList<SnippetPageRef> Pages,
        bool IncludedFootnotes,
        IReadOnlyList<SnippetHighlight> Highlights,
        int NoteCount);
}
