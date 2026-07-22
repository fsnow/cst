using System.Collections.Generic;

namespace CST.Lexicon
{
    /// <summary>What kind of reference a lexicon is — a rendering/labeling hint, not a lookup change.</summary>
    public enum LexiconKind
    {
        /// <summary>Word glosses (Childers, DPD).</summary>
        General,
        /// <summary>Entity/proper-name reference (DPPN).</summary>
        ProperNames
    }

    /// <summary>
    /// Source identity + attribution for a lexicon, stored in its <c>meta</c> table. Mirrors the fields the
    /// existing dictionary surface already carries (#268) so a lexicon source slots into the same attribution
    /// display. Every attribution field is optional; an entirely blank attribution means "unattributed".
    /// </summary>
    public sealed record LexiconMeta(
        string SourceId,
        string DisplayName,
        string DefinitionLanguage,
        LexiconKind Kind,
        // attribution (all optional)
        string? Title = null,
        string? Author = null,
        string? Reviser = null,
        string? Year = null,
        string? Publisher = null,
        string? License = null,
        string? Url = null,
        // provenance / staleness stamps
        string? SourceVersion = null,
        int ConverterVersion = 1);

    /// <summary>
    /// A raw entry as a converter hands it in: the published headword (may contain HTML and a trailing homonym
    /// number) and the definition as an HTML fragment. The builder derives the key, splits the homonym, and
    /// strips HTML from the headword. <c>BodyHtml</c> is stored verbatim — the caller is responsible for having
    /// sanitized it (our converters do so at build time; see the project notes).
    /// </summary>
    public sealed record RawEntry(string Headword, string BodyHtml);

    /// <summary>A stored/looked-up lexicon entry.</summary>
    /// <param name="IpeKey">The IPE lookup/sort key (homonym + HTML removed).</param>
    /// <param name="Headword">The published headword, HTML stripped, homonym number kept (e.g. "Nāgita 1").</param>
    /// <param name="Homonym">The parsed homonym number, or 0.</param>
    /// <param name="BodyHtml">The definition HTML fragment.</param>
    public sealed record LexiconEntry(string IpeKey, string Headword, int Homonym, string BodyHtml);
}
