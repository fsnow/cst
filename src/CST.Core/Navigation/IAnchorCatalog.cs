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
    /// <remarks>
    /// <para>
    /// CONTRACT FOR IMPLEMENTORS — <see cref="NavigationResolver"/>'s correctness depends on all of these, and
    /// nothing enforces them:
    /// </para>
    /// <list type="bullet">
    /// <item><description>
    /// <b>Preserve the TEI XML's casing verbatim</b> in <see cref="ParagraphAnchor.BookCode"/> and
    /// <see cref="BookAnchors.ChapterIds"/>. The resolver matches a caller's reference case-insensitively but
    /// then emits <em>the catalog's</em> casing as the anchor, and the reader looks that up in the DOM
    /// case-SENSITIVELY. A catalog that lower-cases or otherwise normalizes ids would make the resolver emit
    /// dead anchors — and mark them validated.
    /// </description></item>
    /// <item><description>
    /// <b>Uncoded paragraphs in a Multi book are legal.</b> <see cref="BookAnchors.BookCodesFor"/> deliberately
    /// excludes them, so "no codes" does NOT mean "no paragraph" — e.g. vin02t.tik is a Multi book whose 654
    /// paragraphs carry no book-div id at all. Callers must consult <see cref="BookAnchors.HasParagraph"/>
    /// before concluding a paragraph is out of range.
    /// </description></item>
    /// <item><description>
    /// <b>Ranged paragraph numbers exist and this model cannot represent them.</b> ~86 corpus files use a ranged
    /// <c>@n</c> (e.g. <c>n="16-26"</c>), whose HTML anchor is literally <c>para16-26</c>, while
    /// <see cref="ParagraphAnchor"/> holds a single int. A naive catalog will report paragraph 20 out of range
    /// when it lives inside such a block. Resolving this belongs to the reference-model epic (#422); tracked as #444.
    /// </description></item>
    /// </list>
    /// </remarks>
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

        /// <summary>True if a chapter/division with this id exists. Case-insensitive, matching the book and
        /// sub-book lookups — an "AN5" vs "an5" mismatch used to read as "no such chapter". (#314)</summary>
        public bool HasChapter(string chapterId) =>
            ChapterIds.Any(id => string.Equals(id, chapterId, System.StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>A paragraph anchor: its number and, in a Multi book, the sub-book code (else null).</summary>
    public sealed record ParagraphAnchor(int Number, string? BookCode);

    /// <summary>A page-break anchor: edition, volume, and page number.</summary>
    public sealed record PageAnchor(PageEdition Edition, int Volume, int Number);
}
