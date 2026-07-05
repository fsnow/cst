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
    /// <param name="IncludeVariantReadings">Include <c>&lt;note&gt;</c> (variant-reading) text (default off).</param>
    /// <param name="MinChars">Prose floor: extend to neighboring sentences until at least this many rendered chars.</param>
    /// <param name="MaxChars">Prose ceiling: a longer single sentence is trimmed+ellipsized around the hit.</param>
    public sealed record SnippetOptions(
        Script OutputScript = Script.Latin,
        bool IncludeVariantReadings = false,
        int MinChars = 60,
        int MaxChars = 320);

    /// <summary>A page reference at a hit, in one edition's numbering.</summary>
    public sealed record SnippetPageRef(PageEdition Edition, int Volume, int Number);

    /// <summary>
    /// A rendered snippet plus the citation refs at the hit. <see cref="Snippet"/> is the concordance line
    /// (term-centered, romanized); the matched term occupies <c>[HitStart, HitStart+HitLength)</c> within it.
    /// </summary>
    public sealed record SnippetResult(
        string Snippet,
        int HitStart,
        int HitLength,
        int? ParagraphNumber,
        string? ParagraphBookCode,
        IReadOnlyList<SnippetPageRef> Pages,
        bool IncludedVariants);
}
