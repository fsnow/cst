using System;
using System.Collections.Generic;
using System.Linq;

namespace CST.Navigation
{
    /// <summary>
    /// Resolves a <see cref="NavigationRequest"/> to a normalized, validated <see cref="NavigationResult"/>
    /// — the pure-logic half of the "navigation / present-in-context core" (AI_INTEGRATION.md §4.1). It owns
    /// the reference→anchor rules (mirroring the reader's Go-To scheme) and, when an <see cref="IAnchorCatalog"/>
    /// is supplied, range-validation and disambiguation. It does NOT drive the UI: the caller feeds the
    /// resulting <see cref="ResolvedTarget.Anchor"/> to the reader's existing anchor navigation.
    /// </summary>
    public sealed class NavigationResolver
    {
        private readonly Books _books;
        private readonly IAnchorCatalog? _catalog;

        /// <param name="books">The book catalog (e.g. <see cref="Books.Inst"/>).</param>
        /// <param name="catalog">
        /// Optional anchor catalog. When null, the resolver still normalizes references to anchors but cannot
        /// detect <see cref="NavigationStatus.ReferenceOutOfRange"/> or disambiguate Multi-book paragraphs.
        /// </param>
        public NavigationResolver(Books books, IAnchorCatalog? catalog = null)
        {
            _books = books ?? throw new ArgumentNullException(nameof(books));
            _catalog = catalog;
        }

        public NavigationResult Resolve(NavigationRequest request)
        {
            if (request is null) throw new ArgumentNullException(nameof(request));

            var book = FindBook(request.BookId);
            if (book is null) return NavigationResult.UnknownBook(request.BookId);

            var anchors = _catalog?.GetBookAnchors(book.FileName);

            return request.Reference switch
            {
                NavigationReference.WholeBook => Resolved(book, anchor: "", normalized: "start of book", request),
                NavigationReference.Paragraph p => ResolveParagraph(book, p, anchors, request),
                NavigationReference.Page pg => ResolvePage(book, pg, anchors, request),
                NavigationReference.Chapter c => ResolveChapter(book, c, anchors, request),
                NavigationReference.RawAnchor r => ResolveRawAnchor(book, r, anchors, request),
                _ => NavigationResult.InvalidReference("Unsupported reference kind.")
            };
        }

        private Book? FindBook(string? bookId)
        {
            if (string.IsNullOrWhiteSpace(bookId)) return null;
            // Books.this[string] throws on a miss; scan the (small, in-memory) catalog for a safe lookup and
            // to avoid mutating Books. Case-insensitive so "S0101M.MUL.XML" resolves like the lower-case name.
            foreach (var b in _books)
                if (string.Equals(b.FileName, bookId, StringComparison.OrdinalIgnoreCase))
                    return b;
            return null;
        }

        private NavigationResult ResolveParagraph(
            Book book, NavigationReference.Paragraph p, BookAnchors? anchors, NavigationRequest req)
        {
            if (p.Number < 1)
                return NavigationResult.InvalidReference($"Paragraph number must be positive (got {p.Number}).");

            bool isMulti = book.BookType == BookType.Multi;
            string? bookCode = p.BookCode;

            if (isMulti)
            {
                if (bookCode is null)
                {
                    if (anchors is null)
                        return NavigationResult.InvalidReference(
                            $"'{book.FileName}' is a multi-book volume; a book code is required to place " +
                            $"paragraph {p.Number} (anchor catalog unavailable to disambiguate).");

                    var codes = anchors.BookCodesFor(p.Number);
                    if (codes.Count == 0)
                        return NavigationResult.OutOfRange($"No paragraph {p.Number} in '{book.FileName}'.");
                    if (codes.Count > 1)
                        return NavigationResult.Ambiguous(
                            codes.Select(c => new NavigationCandidate(
                                $"para{p.Number}_{c}", $"paragraph {p.Number} in sub-book '{c}'")).ToList(),
                            $"Paragraph {p.Number} appears in {codes.Count} sub-books of '{book.FileName}'; " +
                            "specify a book code.");
                    bookCode = codes[0];
                }
                else if (anchors is not null && !anchors.BookCodesFor(p.Number).Contains(bookCode))
                {
                    return NavigationResult.OutOfRange(
                        $"No paragraph {p.Number} in sub-book '{bookCode}' of '{book.FileName}'.");
                }
            }
            else if (anchors is not null && !anchors.HasParagraph(p.Number))
            {
                return NavigationResult.OutOfRange($"No paragraph {p.Number} in '{book.FileName}'.");
            }

            string anchor = $"para{p.Number}";
            string normalized = $"paragraph {p.Number}";
            if (isMulti && bookCode is not null)
            {
                anchor += $"_{bookCode}";
                normalized += $" ({bookCode})";
            }
            return Resolved(book, anchor, normalized, req);
        }

        private NavigationResult ResolvePage(
            Book book, NavigationReference.Page pg, BookAnchors? anchors, NavigationRequest req)
        {
            if (pg.Number < 1)
                return NavigationResult.InvalidReference($"Page number must be positive (got {pg.Number}).");

            string prefix = EditionPrefix(pg.Edition);
            string editionName = EditionName(pg.Edition);
            int volume;

            if (pg.Volume.HasValue)
            {
                volume = pg.Volume.Value;
                if (anchors is not null && !anchors.HasPage(pg.Edition, volume, pg.Number))
                    return NavigationResult.OutOfRange(
                        $"No {editionName} page {volume}.{pg.Number} in '{book.FileName}'.");
            }
            else if (anchors is not null)
            {
                var volumes = anchors.VolumesFor(pg.Edition, pg.Number);
                if (volumes.Count == 0)
                    return NavigationResult.OutOfRange(
                        $"No {editionName} page {pg.Number} in '{book.FileName}'.");
                if (volumes.Count > 1)
                    return NavigationResult.Ambiguous(
                        volumes.Select(v => new NavigationCandidate(
                            $"{prefix}{v}.{pg.Number:D4}", $"{editionName} page {v}.{pg.Number}")).ToList(),
                        $"{editionName} page {pg.Number} exists in volumes " +
                        $"{string.Join(", ", volumes)}; specify a volume.");
                volume = volumes[0];
            }
            else
            {
                // No current-position context and no catalog: mirror Go-To's volume-0 default.
                volume = 0;
            }

            string anchor = $"{prefix}{volume}.{pg.Number:D4}";
            string normalized = $"{editionName} page {volume}.{pg.Number}";
            return Resolved(book, anchor, normalized, req);
        }

        private NavigationResult ResolveChapter(
            Book book, NavigationReference.Chapter c, BookAnchors? anchors, NavigationRequest req)
        {
            if (string.IsNullOrWhiteSpace(c.ChapterId))
                return NavigationResult.InvalidReference("Chapter id is empty.");
            if (anchors is not null && !anchors.HasChapter(c.ChapterId))
                return NavigationResult.OutOfRange($"No chapter '{c.ChapterId}' in '{book.FileName}'.");
            return Resolved(book, c.ChapterId, $"chapter {c.ChapterId}", req);
        }

        private NavigationResult ResolveRawAnchor(
            Book book, NavigationReference.RawAnchor r, BookAnchors? anchors, NavigationRequest req)
        {
            if (string.IsNullOrWhiteSpace(r.Anchor))
                return NavigationResult.InvalidReference("Anchor is empty.");
            // A raw anchor is trusted through as-is; the catalog cannot cheaply validate arbitrary strings
            // yet (it exposes typed anchors, not a flat set). Validation can tighten once that lands.
            return Resolved(book, r.Anchor, r.Anchor, req);
        }

        private static NavigationResult Resolved(Book book, string anchor, string normalized, NavigationRequest req) =>
            NavigationResult.Resolved(new ResolvedTarget(
                book.FileName,
                book.Index,
                book.DocId,
                anchor,
                normalized,
                req.SearchTerms ?? Array.Empty<string>()));

        private static string EditionPrefix(PageEdition e) => e switch
        {
            PageEdition.Vri => "V",
            PageEdition.Myanmar => "M",
            PageEdition.Pts => "P",
            PageEdition.Thai => "T",
            PageEdition.Other => "O",
            _ => "V"
        };

        private static string EditionName(PageEdition e) => e switch
        {
            PageEdition.Vri => "VRI",
            PageEdition.Myanmar => "Myanmar",
            PageEdition.Pts => "PTS",
            PageEdition.Thai => "Thai",
            PageEdition.Other => "other",
            _ => e.ToString()
        };
    }
}
