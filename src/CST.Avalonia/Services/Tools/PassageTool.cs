using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CST;
using CST.Conversion;
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

        /// <summary>
        /// Where the reader's selection sits in the raw XML, or null when there is none or it cannot be
        /// placed. Bounded to the paragraph the reader's own anchor reports.
        /// </summary>
        /// <remarks>
        /// Converted to the corpus script before matching. The selection arrives in Latin — every downstream
        /// lookup wants it that way — but the XML is Devanagari, and script conversion is not
        /// length-preserving, so there is no offset arithmetic that could relate the two. Converting the
        /// short needle is exact and cheap; converting the haystack would be neither.
        /// </remarks>
        private static (int Start, int End)? LocateSelection(
            string xml, int startPos, BookMarkers markers, string? selectionLatin)
        {
            if (string.IsNullOrWhiteSpace(selectionLatin)) return null;

            string needle;
            try
            {
                needle = ScriptConverter.Convert(selectionLatin, Script.Latin, Script.Devanagari);
            }
            catch (Exception)
            {
                // A selection that will not convert is not a reason to fail the whole request: fall back to
                // the paragraph window, which is what every caller got before selections were considered.
                return null;
            }

            // The paragraph the anchor named, bounded by its section so the search cannot wander.
            var (_, sectionEnd) = markers.EnclosingDivRange(startPos);
            return TeiPassageReader.LocateSelection(xml, startPos, sectionEnd, needle);
        }

        /// <summary>
        /// The end of the paragraph starting at <paramref name="from"/> — its own closing tag, or the end of
        /// its section. Used as the stand-in span when a selection could not be located, so the window is
        /// still section-bounded rather than free-running.
        /// </summary>
        private static int ParagraphEnd(string xml, int from, BookMarkers markers)
        {
            var sectionEnd = markers.EnclosingDivRange(from).End;
            var close = xml.IndexOf("</p>", from, StringComparison.Ordinal);
            return close < 0 || close > sectionEnd ? sectionEnd : close;
        }

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

            var budget = Math.Clamp(request.MaxChars, 1, MaxPassageChars);

            // With a selection, the window is built AROUND it instead of from the paragraph's start, so the
            // words the reader highlighted are always inside the context sent to explain them. (#649)
            //
            // The search is bounded to the referenced paragraph, which is where the reader's own anchor says
            // the selection is. That bound is load-bearing rather than an optimisation: this canon is
            // pericope-built and formulaic passages repeat verbatim across books, so an unbounded search
            // could centre the window on a different occurrence entirely and caption it confidently.
            var selectionSpan = LocateSelection(xml, startPos, markers, request.SelectionText);

            if (!string.IsNullOrWhiteSpace(request.SelectionText))
            {
                Serilog.Log.Information(
                    "Passage: reference resolved to {StartPos}; selection {Located} in the XML",
                    startPos, selectionSpan is null ? "NOT located" : $"located at {selectionSpan.Value.Start}");
            }

            // When a selection was supplied but could not be located, the window is still built the
            // selection way -- around the referenced PARAGRAPH's own span. Falling back to ReadWindow was
            // wrong in a way that only showed up on this path: it walks with no section bound at all, so a
            // request whose selection opened a sutta grew backwards into the previous one. Not crossing a
            // <div> is a property of the context, not a reward for having found the selection.
            var fallbackSpan = selectionSpan
                ?? (string.IsNullOrWhiteSpace(request.SelectionText)
                    ? null
                    : (startPos, ParagraphEnd(xml, startPos, markers)));

            var w = fallbackSpan is { } span
                ? TeiPassageReader.ReadWindowAroundSelection(
                    xml, span.Start, span.End, budget,
                    request.IncludeFootnotes, request.OutputScript, markers,
                    structuredNotes: request.StructuredNotes)
                // A cursor points AT a hit (mid-sentence); snap the window start back to the enclosing
                // sentence so the hit is read with its governing clause. A paragraph reference already
                // starts clean - no snap.
                : TeiPassageReader.ReadWindow(
                    xml, startPos, budget,
                    request.IncludeFootnotes, request.OutputScript, markers,
                    snapStartToSentence: request.Cursor.HasValue,
                    structuredNotes: request.StructuredNotes);

            return new PassageResult(
                BookId: request.BookId,
                NormalizedReference: Describe(
                    w.ParagraphNumber, w.ParagraphBookCode, w.EndParagraphNumber,
                    w.EndParagraphBookCode, w.ParagraphsContiguous),
                Text: w.Text,
                Pages: w.Pages,
                ParagraphNumber: w.ParagraphNumber,
                ParagraphBookCode: w.ParagraphBookCode,
                PrevCursor: w.PrevCursor,
                NextCursor: w.NextCursor,
                NoteCount: w.NoteCount,
                Notes: w.Notes,
                EndParagraphNumber: w.EndParagraphNumber,
                EndParagraphBookCode: w.EndParagraphBookCode,
                SelectionTruncated: w.SelectionTruncated,
                ParagraphsContiguous: w.ParagraphsContiguous);
        }

        private static int ResolveStart(NavigationReference? reference, BookMarkers markers) => reference switch
        {
            null => 0,
            NavigationReference.WholeBook => 0,
            NavigationReference.Paragraph p => markers.PositionOfParagraph(p.Number, p.BookCode),
            _ => -1   // Page / Chapter / RawAnchor: not resolved in the first cut
        };

        /// <summary>
        /// A human-readable reference for the window — a RANGE when it spans more than one paragraph.
        ///
        /// <para>The window is budgeted in rendered characters and is structurally blind, so on a verse text a
        /// modest budget covers many paragraphs: 2,400 characters of Dhammapada is ~30 verses across several
        /// chapters. Naming only the first would understate that badly, and this string is what surface B
        /// renders beside a generated answer as the app's own attestation of scope. (#602)</para>
        ///
        /// <para>A range ONLY where the numbering runs straight through — <paramref name="contiguous"/>, which
        /// the window answers positionally. Where it does not, both ends are named with their own book codes
        /// and the string says outright that it is not a range, because the alternative is a citation that
        /// reads perfectly and names text the window does not hold. (#914)</para>
        /// </summary>
        internal static string Describe(int? number, string? bookCode, int? endNumber = null,
            string? endBookCode = null, bool contiguous = true)
        {
            if (number is null) return "start of book";

            if (endNumber is not int last || last == number)
                return Name(number.Value, bookCode);

            // A range is a claim about everything between its ends. It is only true where the numbering runs
            // straight through: paragraph numbers restart per section, so a window can open at 55 near the
            // end of one and close at 57 in the next, having passed through that section's own 1, 2, 3. The
            // reversed case ("paragraphs 289-3") reads as wrong on sight; this one does not, which is why it
            // is the one worth spelling out. (#914)
            if (!contiguous)
                return $"{Name(number.Value, bookCode)} through {Name(last, endBookCode)}, not a continuous range";

            var range = $"paragraphs {number}-{last}";
            return bookCode is null ? range : $"{range} ({bookCode})";
        }

        // One end of a citation: the number, and the sub-book it belongs to where there is one. Each end
        // carries its OWN code, because the ends can sit in different sub-books of a Multi book and labelling
        // the whole span with the start's code was its own small lie.
        private static string Name(int number, string? bookCode) =>
            bookCode is null ? $"paragraph {number}" : $"paragraph {number} ({bookCode})";

        private static PassageResult Empty(PassageRequest request, string note) =>
            new(request.BookId, note, "", Array.Empty<SnippetPageRef>(), null, null, null, null, 0,
                Array.Empty<CST.Search.ApparatusNote>());
    }
}
