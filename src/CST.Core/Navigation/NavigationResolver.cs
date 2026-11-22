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
        /// detect <see cref="NavigationStatus.ReferenceOutOfRange"/> or disambiguate a repeated paragraph number.
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

            // The qualifier is the id of any div enclosing the paragraph: a Multi volume's sub-book code
            // ("an5"), or a section ("kn1_2"). A printed paragraph number repeats within a book wherever the
            // print restarts its numbering per section, so in a non-Multi book a section is exactly what
            // disambiguates it. [fsnow, 2026-11-21] accepted "emit the deepest, accept any level that's
            // unique": any enclosing div qualifies, and the anchor emitted is the deepest form. (#447)
            string? qualifier = string.IsNullOrWhiteSpace(p.BookCode) ? null : p.BookCode;

            if (anchors is null)
            {
                // Without a catalog nothing can be checked, so the anchor is built from what was given and
                // marked unvalidated. A Multi volume still needs its sub-book named: its paragraph 1 exists
                // once per sub-book, and guessing one is the failure #314 removed.
                if (qualifier is null && book.BookType == BookType.Multi)
                    return NavigationResult.InvalidReference(
                        $"'{book.FileName}' is a multi-book volume; a book code is required to place " +
                        $"paragraph {p.Number} (anchor catalog unavailable to disambiguate).");
                return qualifier is null
                    ? Resolved(book, $"para{p.Number}", $"paragraph {p.Number}", req, validated: false)
                    : Resolved(book, $"para{p.Number}_{qualifier}", $"paragraph {p.Number} ({qualifier})", req,
                        validated: false);
            }

            var matches = anchors.Paragraphs
                .Where(a => a.Number == p.Number && (qualifier is null || a.EnclosedBy(qualifier)))
                .ToList();

            if (matches.Count == 0)
                return NavigationResult.OutOfRange(qualifier is null
                    ? $"No paragraph {p.Number} in '{book.FileName}'."
                    : $"No paragraph {p.Number} in '{qualifier}' of '{book.FileName}'.");

            // One candidate per distinct anchor. Two paragraphs can share a number AND a section (a duplicated
            // printed number, as in #1087); no anchor tells those apart, so they collapse to one, and the
            // reader lands on the first of them.
            var candidates = matches
                .GroupBy(Anchor)
                .Select(g => (Anchor: g.Key, Match: g.First(), Count: g.Count()))
                .ToList();

            if (candidates.Count > 1)
                return NavigationResult.Ambiguous(
                    candidates.Select(c => new NavigationCandidate(c.Anchor, Describe(p.Number, c.Match))).ToList(),
                    $"Paragraph {p.Number} appears {candidates.Count} times in '{book.FileName}'" +
                    (qualifier is null ? "" : $" within '{qualifier}'") +
                    "; name the section it is in.");

            var only = candidates[0];
            string normalized = Describe(p.Number, only.Match);
            if (only.Count > 1) normalized += $" (the first of {only.Count} with this number there)";
            return Resolved(book, only.Anchor, normalized, req, validated: true);

            // The deepest form where the paragraph has a section, so what a caller stores depends only on where
            // the paragraph sits. A paragraph outside every div with an id keeps the bare form.
            static string Anchor(ParagraphAnchor a) =>
                a.SectionId is { } section ? $"para{a.Number}_{section}" : $"para{a.Number}";

            static string Describe(int number, ParagraphAnchor a) =>
                a.SectionId is { } section ? $"paragraph {number} ({section})" : $"paragraph {number}";
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
