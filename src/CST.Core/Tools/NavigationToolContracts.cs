using System.Collections.Generic;
using CST.Navigation;

namespace CST.Tools
{
    /// <summary>
    /// The navigation/catalog tool exposed to agents (AI_INTEGRATION.md surface C + §4.1/§4.2). Resolves a
    /// reference to a validated target and enumerates what's addressable — so an agent knows the valid
    /// navigation targets before it asks. Reuses the <see cref="CST.Navigation"/> core: resolution and the
    /// anchor catalog are the same spine as the #187 navigation core.
    /// </summary>
    public interface INavigationTool
    {
        /// <summary>
        /// Resolve a reference to a normalized, validated target — or a structured failure
        /// (unknown book / invalid / ambiguous + candidates / out-of-range). Never a silent best-guess.
        /// </summary>
        NavigationResult Resolve(NavigationRequest request);

        /// <summary>List the books that are addressable, optionally filtered by piṭaka / commentary level.</summary>
        IReadOnlyList<BookSummary> ListBooks(BookListFilter? filter = null);

        /// <summary>
        /// The valid anchors in a book — paragraphs (with sub-book codes), per-edition pages, chapters —
        /// or <c>null</c> if the book is unknown / not yet catalogued.
        /// </summary>
        BookAnchors? ListAnchors(string bookId);
    }

    /// <summary>A book as seen by an agent: its id (file name), names, classification, and index state.</summary>
    public sealed record BookSummary(
        string BookId,
        string Name,
        string ShortName,
        Pitaka Pitaka,
        CommentaryLevel CommentaryLevel,
        BookType BookType,
        bool Indexed);

    /// <summary>Which books to list. Defaults include everything.</summary>
    public sealed record BookListFilter(
        bool Vinaya = true,
        bool Sutta = true,
        bool Abhidhamma = true,
        bool Mula = true,
        bool Atthakatha = true,
        bool Tika = true,
        bool Other = true);
}
