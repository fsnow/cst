using System.Collections.Generic;

namespace CST.Navigation
{
    /// <summary>
    /// A location within a book to navigate to. The kinds mirror the reader's existing anchor scheme
    /// (paragraph / per-edition page / chapter div / raw anchor), so <see cref="NavigationResolver"/> can
    /// normalize any of them to the same anchor string the reader already understands
    /// (e.g. "para123", "para123_an5", "V2.0050", "dn1").
    /// This is a closed hierarchy — the nested sealed records are the only kinds.
    /// </summary>
    public abstract record NavigationReference
    {
        protected NavigationReference() { }

        /// <summary>Open the book at its start (no specific anchor).</summary>
        public sealed record WholeBook() : NavigationReference;

        /// <summary>
        /// A paragraph by number. <paramref name="BookCode"/> selects the sub-book inside a
        /// <see cref="BookType.Multi"/> volume (e.g. "an5"); leave it null to let the resolver disambiguate
        /// from the anchor catalog.
        /// </summary>
        public sealed record Paragraph(int Number, string? BookCode = null) : NavigationReference;

        /// <summary>
        /// A page in a specific edition. <paramref name="Volume"/> may be null; the resolver then resolves
        /// it from the catalog when available, otherwise defaults to volume 0.
        /// </summary>
        public sealed record Page(PageEdition Edition, int Number, int? Volume = null) : NavigationReference;

        /// <summary>A chapter/division by its id (the div @id, e.g. "dn1", "dn1_1").</summary>
        public sealed record Chapter(string ChapterId) : NavigationReference;

        /// <summary>A pre-formed anchor string, passed through after optional catalog validation.</summary>
        public sealed record RawAnchor(string Anchor) : NavigationReference;
    }

    /// <summary>The page-numbering editions the corpus carries, matching the TEI <c>pb/@ed</c> prefixes.</summary>
    public enum PageEdition
    {
        /// <summary>VRI edition — anchor prefix "V".</summary>
        Vri,
        /// <summary>Myanmar edition — anchor prefix "M".</summary>
        Myanmar,
        /// <summary>PTS edition — anchor prefix "P".</summary>
        Pts,
        /// <summary>Thai edition — anchor prefix "T".</summary>
        Thai,
        /// <summary>Other edition — anchor prefix "O".</summary>
        Other
    }

    /// <summary>
    /// A request to resolve a navigation target: which book, where in it, and any search terms to carry
    /// through for highlighting. <see cref="BookId"/> is the exact book file name (as listed by the anchor
    /// catalog); fuzzy name/abbreviation resolution is a later enhancement.
    /// </summary>
    public sealed record NavigationRequest(
        string BookId,
        NavigationReference Reference,
        IReadOnlyList<string>? SearchTerms = null);
}
