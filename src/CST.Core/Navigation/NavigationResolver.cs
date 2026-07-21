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
                // The start of a book always exists, so this is validated regardless of the catalog.
                NavigationReference.WholeBook => Resolved(book, "", "start of book", request, validated: true),
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

            // A book code addresses a sub-book of a Multi volume, so it cannot address anything here. Silently
            // ignoring it produced a wrong-but-plausible navigation (the caller believed it had disambiguated);
            // say so instead. (#314)
            //
            // Two things the wording must NOT claim. A non-Multi book CAN carry a book-div id (s0101m.mul has
            // "dn1", and the XSL does emit para5_dn1), so "that code doesn't exist" would be false. And
            // paragraph numbers are NOT unique here either — they repeat in 95 non-Multi books, because the
            // printed numbering restarts per section. The honest reason to refuse is narrower: a book code is
            // not the thing that would disambiguate them. Where a number does repeat, the repeats sit WITHIN a
            // single book div, so (number, bookCode) is exactly as ambiguous as the bare number and the anchor
            // resolves to the first occurrence. Qualifying by the deepest enclosing div would fix ~97% of that;
            // tracked in #447. (fable LOW-6, corrected)
            if (!isMulti && bookCode is not null)
                return NavigationResult.InvalidReference(
                    $"'{book.FileName}' is not a multi-book volume, so a book code cannot address a paragraph " +
                    $"in it; omit '{bookCode}'. (Paragraph numbers are not necessarily unique in this book " +
                    "either, but a book code is not what would disambiguate them — see #447.)");

            if (isMulti)
            {
                if (bookCode is null)
                {
                    if (anchors is null)
                        return NavigationResult.InvalidReference(
                            $"'{book.FileName}' is a multi-book volume; a book code is required to place " +
                            $"paragraph {p.Number} (anchor catalog unavailable to disambiguate).");

                    var codes = anchors.BookCodesFor(p.Number);
                    // No CODES is not the same as no PARAGRAPH: a Multi book may carry uncoded paragraphs, and
                    // reporting those as out-of-range was a false negative. Fall through uncoded. (#314)
                    if (codes.Count == 0)
                        return anchors.HasParagraph(p.Number)
                            ? Resolved(book, $"para{p.Number}", $"paragraph {p.Number}", req, validated: true)
                            : NavigationResult.OutOfRange($"No paragraph {p.Number} in '{book.FileName}'.");
                    if (codes.Count > 1)
                        return NavigationResult.Ambiguous(
                            codes.Select(c => new NavigationCandidate(
                                $"para{p.Number}_{c}", $"paragraph {p.Number} in sub-book '{c}'")).ToList(),
                            $"Paragraph {p.Number} appears in {codes.Count} sub-books of '{book.FileName}'; " +
                            "specify a book code.");
                    bookCode = codes[0];
                }
                else if (anchors is not null)
                {
                    // Case-insensitive, matching the book lookup above: "AN5" and "an5" are the same sub-book,
                    // and a case mismatch used to read as "no such paragraph". (#314)
                    var match = anchors.BookCodesFor(p.Number)
                        .FirstOrDefault(c => string.Equals(c, bookCode, StringComparison.OrdinalIgnoreCase));
                    if (match is null)
                        return NavigationResult.OutOfRange(
                            $"No paragraph {p.Number} in sub-book '{bookCode}' of '{book.FileName}'.");
                    bookCode = match;   // normalize to the catalog's casing so the anchor matches the document
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
            return Resolved(book, anchor, normalized, req, validated: anchors is not null);
        }

        private NavigationResult ResolvePage(
            Book book, NavigationReference.Page pg, BookAnchors? anchors, NavigationRequest req)
        {
            if (pg.Number < 1)
                return NavigationResult.InvalidReference($"Page number must be positive (got {pg.Number}).");
            if (!Enum.IsDefined(pg.Edition))
                return NavigationResult.InvalidReference(
                    $"Unknown page edition '{(int)pg.Edition}'. Use Vri, Myanmar, Pts, Thai, or Other.");
            if (pg.Volume is < 0)
                return NavigationResult.InvalidReference(
                    $"Volume must not be negative (got {pg.Volume.Value}).");

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
                // Previously this defaulted to volume 0, mirroring Go-To. Without a catalog we cannot tell a
                // single-volume book (where 0 is right) from a multi-volume one (where 0 is a DEAD anchor that
                // resolves "successfully" and then silently fails to scroll). Ask for the volume instead of
                // guessing — a caller can always pass 0 explicitly. (#314)
                // Do NOT suggest 0 as a default: most single-FILE books still carry a print-set volume (e.g.
                // s0101m.mul is volume 1, s0102m.mul is volume 2), so "0 for a single-volume book" would steer a
                // caller straight into the dead anchor this refusal exists to prevent. (fable MED-3)
                return NavigationResult.InvalidReference(
                    $"{editionName} page {pg.Number} needs a volume: '{book.FileName}' has no anchor catalog " +
                    "loaded, so the volume cannot be inferred. Supply the print-edition volume that appears in " +
                    "the page reference (the 1 in V1.0023).");
            }

            string anchor = $"{prefix}{volume}.{pg.Number:D4}";
            string normalized = $"{editionName} page {volume}.{pg.Number}";
            // Three paths reach here and only the two catalog paths above actually checked the page exists —
            // an explicit volume with no catalog is UNCHECKED and must not be stamped as validated. (fable HIGH-1)
            return Resolved(book, anchor, normalized, req, validated: anchors is not null);
        }

        private NavigationResult ResolveChapter(
            Book book, NavigationReference.Chapter c, BookAnchors? anchors, NavigationRequest req)
        {
            if (string.IsNullOrWhiteSpace(c.ChapterId))
                return NavigationResult.InvalidReference("Chapter id is empty.");
            if (anchors is not null)
            {
                // Case-insensitive like the book and sub-book lookups; normalize to the catalog's casing so the
                // emitted anchor matches the document. (#314)
                var match = anchors.ChapterIds
                    .FirstOrDefault(id => string.Equals(id, c.ChapterId, StringComparison.OrdinalIgnoreCase));
                if (match is null)
                    return NavigationResult.OutOfRange($"No chapter '{c.ChapterId}' in '{book.FileName}'.");
                return Resolved(book, match, $"chapter {match}", req, validated: true);
            }
            return Resolved(book, c.ChapterId, $"chapter {c.ChapterId}", req, validated: false);
        }

        private NavigationResult ResolveRawAnchor(
            Book book, NavigationReference.RawAnchor r, BookAnchors? anchors, NavigationRequest req)
        {
            if (string.IsNullOrWhiteSpace(r.Anchor))
                return NavigationResult.InvalidReference("Anchor is empty.");
            // A raw anchor is trusted through as-is; the catalog cannot cheaply validate arbitrary strings
            // yet (it exposes typed anchors, not a flat set). Validation can tighten once that lands.
            // Never "validated": the catalog exposes typed anchors, not a flat set, so an arbitrary string
            // cannot be checked. The caller must surface that this one is unverified. (#314)
            return Resolved(book, r.Anchor, r.Anchor, req, validated: false);
        }

        private static NavigationResult Resolved(
            Book book, string anchor, string normalized, NavigationRequest req, bool validated) =>
            NavigationResult.Resolved(new ResolvedTarget(
                book.FileName,
                book.Index,
                book.DocId,
                anchor,
                normalized,
                req.SearchTerms ?? Array.Empty<string>(),
                validated));

        private static string EditionPrefix(PageEdition e) => e switch
        {
            PageEdition.Vri => "V",
            PageEdition.Myanmar => "M",
            PageEdition.Pts => "P",
            PageEdition.Thai => "T",
            PageEdition.Other => "O",
            _ => "V"   // unreachable: Enum.IsDefined is checked before this
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
