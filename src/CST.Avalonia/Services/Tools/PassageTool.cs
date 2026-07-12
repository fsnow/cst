using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CST;
using CST.Navigation;
using CST.Search;
using CST.Tools;

namespace CST.Avalonia.Services.Tools
{
    /// <summary>
    /// <see cref="IPassageTool"/> — reads a bounded, paged reading window of a book's text (AI_INTEGRATION.md
    /// surface C). Resolves a paragraph reference (the ref an occurrence reports) or a page cursor to a start
    /// position, reads the book XML, and runs <see cref="TeiPassageReader"/>. Headless; needs the XML dir.
    /// First cut: supports paragraph / whole-book references and cursor paging; page/chapter/anchor references
    /// are not yet resolved (they return an empty window).
    /// </summary>
    public sealed class PassageTool : IPassageTool
    {
        private readonly ISettingsService _settings;

        public PassageTool(ISettingsService settings) => _settings = settings;

        // Upper bound on a single passage read — a client asking for MaxChars:2_000_000_000 shouldn't pin a
        // 4 GB substring off a corpus file. (#305)
        private const int MaxPassageChars = 20_000;

        /// <summary>True only for a bookId that is an exact catalog file name — the confinement check that keeps
        /// a client-supplied bookId from escaping the corpus directory. (#301)</summary>
        internal static bool IsCatalogBook(string? bookId) =>
            !string.IsNullOrEmpty(bookId) &&
            Books.Inst.Any(b => string.Equals(b.FileName, bookId, StringComparison.OrdinalIgnoreCase));

        public async Task<PassageResult> FetchPassageAsync(PassageRequest request, CancellationToken ct = default)
        {
            var dir = _settings.Settings?.XmlBooksDirectory;
            // Confine file access to catalog books: NEVER Path.Combine an unvalidated bookId — an absolute path
            // makes Combine discard `dir`, and `..` escapes the corpus dir (path traversal / arbitrary read). (#301)
            if (string.IsNullOrEmpty(dir) || !IsCatalogBook(request.BookId))
                return Empty(request, "unknown book");
            var path = Path.Combine(dir, request.BookId);
            if (!File.Exists(path))
                return Empty(request, "book not available");

            // Read + parse via the shared bounded cache so paging one book doesn't re-read + re-parse it. (#308 A3-6)
            var (xml, markers) = await BookTextCache.GetAsync(path, ct).ConfigureAwait(false);

            int startPos = request.Cursor ?? ResolveStart(request.Reference, markers);
            if (startPos < 0) return Empty(request, "reference not found");
            startPos = Math.Clamp(startPos, 0, xml.Length);

            // A cursor points AT a hit (mid-sentence); snap the window start back to the enclosing sentence so
            // the hit is read with its governing clause. A paragraph reference already starts clean - no snap.
            var w = TeiPassageReader.ReadWindow(
                xml, startPos, Math.Clamp(request.MaxChars, 1, MaxPassageChars),
                request.IncludeFootnotes, request.OutputScript, markers,
                snapStartToSentence: request.Cursor.HasValue);

            return new PassageResult(
                BookId: request.BookId,
                NormalizedReference: Describe(w.ParagraphNumber, w.ParagraphBookCode),
                Text: w.Text,
                Pages: w.Pages,
                ParagraphNumber: w.ParagraphNumber,
                ParagraphBookCode: w.ParagraphBookCode,
                PrevCursor: w.PrevCursor,
                NextCursor: w.NextCursor,
                NoteCount: w.NoteCount);
        }

        private static int ResolveStart(NavigationReference? reference, BookMarkers markers) => reference switch
        {
            null => 0,
            NavigationReference.WholeBook => 0,
            NavigationReference.Paragraph p => markers.PositionOfParagraph(p.Number, p.BookCode),
            _ => -1   // Page / Chapter / RawAnchor: not resolved in the first cut
        };

        private static string Describe(int? number, string? bookCode) =>
            number is null ? "start of book"
            : bookCode is null ? $"paragraph {number}"
            : $"paragraph {number} ({bookCode})";

        private static PassageResult Empty(PassageRequest request, string note) =>
            new(request.BookId, note, "", Array.Empty<SnippetPageRef>(), null, null, null, null, 0);
    }
}
