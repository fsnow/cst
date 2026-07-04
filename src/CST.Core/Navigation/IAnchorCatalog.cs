using System.Collections.Generic;
using System.Linq;

namespace CST.Navigation
{
    /// <summary>
    /// Pure-logic enumeration of the <em>valid navigation targets</em> in a book — the counterpart to the
    /// reader's runtime (WebView) anchor cache. This is the precomputed "anchor catalog"
    /// (AI_INTEGRATION.md §4.2) that lets <see cref="NavigationResolver"/> validate references and produce
    /// disambiguation candidates without rendering the book. Implementations precompute/parse from the TEI
    /// XML. A resolver given no catalog degrades to building anchors <em>without</em> range validation.
    /// </summary>
    public interface IAnchorCatalog
    {
        /// <summary>
        /// The anchors for a book by file name, or <c>null</c> if the book is unknown or not yet catalogued.
        /// </summary>
        BookAnchors? GetBookAnchors(string bookFileName);
    }

    /// <summary>The valid anchors in a single book.</summary>
    public sealed record BookAnchors(
        IReadOnlyList<ParagraphAnchor> Paragraphs,
        IReadOnlyList<PageAnchor> Pages,
        IReadOnlyList<string> ChapterIds)
    {
        /// <summary>True if any paragraph with this number exists (in any sub-book).</summary>
        public bool HasParagraph(int number) => Paragraphs.Any(p => p.Number == number);

        /// <summary>The distinct sub-book codes that contain the given paragraph number.</summary>
        public IReadOnlyList<string> BookCodesFor(int number) =>
            Paragraphs.Where(p => p.Number == number && p.BookCode != null)
                      .Select(p => p.BookCode!)
                      .Distinct()
                      .ToList();

        /// <summary>True if the exact edition+volume+page exists.</summary>
        public bool HasPage(PageEdition edition, int volume, int number) =>
            Pages.Any(p => p.Edition == edition && p.Volume == volume && p.Number == number);

        /// <summary>The distinct volumes in which the given edition+page number appears.</summary>
        public IReadOnlyList<int> VolumesFor(PageEdition edition, int number) =>
            Pages.Where(p => p.Edition == edition && p.Number == number)
                 .Select(p => p.Volume)
                 .Distinct()
                 .ToList();

        /// <summary>True if a chapter/division with this id exists.</summary>
        public bool HasChapter(string chapterId) => ChapterIds.Contains(chapterId);
    }

    /// <summary>A paragraph anchor: its number and, in a Multi book, the sub-book code (else null).</summary>
    public sealed record ParagraphAnchor(int Number, string? BookCode);

    /// <summary>A page-break anchor: edition, volume, and page number.</summary>
    public sealed record PageAnchor(PageEdition Edition, int Volume, int Number);
}
