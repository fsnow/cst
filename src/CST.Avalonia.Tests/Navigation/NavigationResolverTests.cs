using System;
using System.Collections.Generic;
using System.Linq;
using CST;
using CST.Navigation;
using Xunit;

namespace CST.Avalonia.Tests.Navigation
{
    /// <summary>
    /// Unit tests for the pure-logic navigation core (#187 / AI_INTEGRATION.md §4.1). Uses the real book
    /// catalog (Books.Inst is an in-memory, hardcoded catalog — no XML/IO) for book resolution + BookType,
    /// and a hand-built fake anchor catalog to exercise range-validation and disambiguation.
    /// </summary>
    public class NavigationResolverTests
    {
        private static readonly Books Catalog = Books.Inst;

        private static Book NonMultiBook() =>
            Catalog.First(b => b.BookType != BookType.Multi && !string.IsNullOrEmpty(b.FileName));

        private static Book MultiBook() =>
            Catalog.First(b => b.BookType == BookType.Multi);

        private static NavigationResolver Resolver(IAnchorCatalog? catalog = null) =>
            new NavigationResolver(Catalog, catalog);

        // --- book resolution -------------------------------------------------

        [Fact]
        public void UnknownBook_returns_UnknownBook()
        {
            var r = Resolver().Resolve(new NavigationRequest("no-such-book.xml", new NavigationReference.WholeBook()));
            Assert.Equal(NavigationStatus.UnknownBook, r.Status);
        }

        [Fact]
        public void FileName_match_is_case_insensitive()
        {
            var b = NonMultiBook();
            var r = Resolver().Resolve(new NavigationRequest(b.FileName.ToUpperInvariant(), new NavigationReference.WholeBook()));
            Assert.Equal(NavigationStatus.Resolved, r.Status);
            Assert.Equal(b.FileName, r.Target!.BookFileName);
        }

        [Fact]
        public void WholeBook_resolves_to_empty_anchor()
        {
            var b = NonMultiBook();
            var r = Resolver().Resolve(new NavigationRequest(b.FileName, new NavigationReference.WholeBook()));
            Assert.Equal(NavigationStatus.Resolved, r.Status);
            Assert.Equal("", r.Target!.Anchor);
        }

        // --- paragraphs ------------------------------------------------------

        [Fact]
        public void Paragraph_nonMulti_builds_para_anchor()
        {
            var b = NonMultiBook();
            var r = Resolver().Resolve(new NavigationRequest(b.FileName, new NavigationReference.Paragraph(123)));
            Assert.Equal(NavigationStatus.Resolved, r.Status);
            Assert.Equal("para123", r.Target!.Anchor);
        }

        [Fact]
        public void Paragraph_zero_or_negative_is_InvalidReference()
        {
            var b = NonMultiBook();
            var r = Resolver().Resolve(new NavigationRequest(b.FileName, new NavigationReference.Paragraph(0)));
            Assert.Equal(NavigationStatus.InvalidReference, r.Status);
        }

        [Fact]
        public void Paragraph_multi_without_code_and_no_catalog_is_InvalidReference()
        {
            var b = MultiBook();
            var r = Resolver().Resolve(new NavigationRequest(b.FileName, new NavigationReference.Paragraph(5)));
            Assert.Equal(NavigationStatus.InvalidReference, r.Status);
        }

        [Fact]
        public void Paragraph_multi_with_explicit_code_builds_suffixed_anchor()
        {
            var b = MultiBook();
            var r = Resolver().Resolve(new NavigationRequest(b.FileName, new NavigationReference.Paragraph(5, "an5")));
            Assert.Equal(NavigationStatus.Resolved, r.Status);
            Assert.Equal("para5_an5", r.Target!.Anchor);
            Assert.Contains("an5", r.Target!.NormalizedReference);
        }

        [Fact]
        public void Paragraph_multi_single_catalog_code_is_auto_suffixed()
        {
            var b = MultiBook();
            var cat = new FakeCatalog(b.FileName, paragraphs: new[] { new ParagraphAnchor(5, "an5") });
            var r = Resolver(cat).Resolve(new NavigationRequest(b.FileName, new NavigationReference.Paragraph(5)));
            Assert.Equal(NavigationStatus.Resolved, r.Status);
            Assert.Equal("para5_an5", r.Target!.Anchor);
        }

        [Fact]
        public void Paragraph_multi_multiple_catalog_codes_is_Ambiguous_with_candidates()
        {
            var b = MultiBook();
            var cat = new FakeCatalog(b.FileName,
                paragraphs: new[] { new ParagraphAnchor(5, "an5"), new ParagraphAnchor(5, "an6") });
            var r = Resolver(cat).Resolve(new NavigationRequest(b.FileName, new NavigationReference.Paragraph(5)));
            Assert.Equal(NavigationStatus.AmbiguousReference, r.Status);
            Assert.Equal(2, r.Candidates!.Count);
            Assert.Contains(r.Candidates!, c => c.Anchor == "para5_an5");
            Assert.Contains(r.Candidates!, c => c.Anchor == "para5_an6");
        }

        [Fact]
        public void Paragraph_nonMulti_missing_in_catalog_is_OutOfRange()
        {
            var b = NonMultiBook();
            var cat = new FakeCatalog(b.FileName, paragraphs: new[] { new ParagraphAnchor(1, null) });
            var r = Resolver(cat).Resolve(new NavigationRequest(b.FileName, new NavigationReference.Paragraph(999)));
            Assert.Equal(NavigationStatus.ReferenceOutOfRange, r.Status);
        }

        // --- pages -----------------------------------------------------------

        [Fact]
        public void Page_with_volume_builds_padded_anchor()
        {
            var b = NonMultiBook();
            var r = Resolver().Resolve(new NavigationRequest(b.FileName, new NavigationReference.Page(PageEdition.Vri, 50, 2)));
            Assert.Equal("V2.0050", r.Target!.Anchor);
        }

        [Fact]
        public void Page_without_volume_defaults_to_zero_without_catalog()
        {
            var b = NonMultiBook();
            var r = Resolver().Resolve(new NavigationRequest(b.FileName, new NavigationReference.Page(PageEdition.Myanmar, 7)));
            Assert.Equal("M0.0007", r.Target!.Anchor);
        }

        [Fact]
        public void Page_without_volume_resolves_single_volume_from_catalog()
        {
            var b = NonMultiBook();
            var cat = new FakeCatalog(b.FileName, pages: new[] { new PageAnchor(PageEdition.Pts, 3, 42) });
            var r = Resolver(cat).Resolve(new NavigationRequest(b.FileName, new NavigationReference.Page(PageEdition.Pts, 42)));
            Assert.Equal(NavigationStatus.Resolved, r.Status);
            Assert.Equal("P3.0042", r.Target!.Anchor);
        }

        [Fact]
        public void Page_without_volume_multiple_volumes_is_Ambiguous()
        {
            var b = NonMultiBook();
            var cat = new FakeCatalog(b.FileName,
                pages: new[] { new PageAnchor(PageEdition.Vri, 1, 10), new PageAnchor(PageEdition.Vri, 2, 10) });
            var r = Resolver(cat).Resolve(new NavigationRequest(b.FileName, new NavigationReference.Page(PageEdition.Vri, 10)));
            Assert.Equal(NavigationStatus.AmbiguousReference, r.Status);
            Assert.Equal(2, r.Candidates!.Count);
        }

        [Fact]
        public void Page_out_of_range_with_catalog_is_OutOfRange()
        {
            var b = NonMultiBook();
            var cat = new FakeCatalog(b.FileName, pages: new[] { new PageAnchor(PageEdition.Vri, 1, 10) });
            var r = Resolver(cat).Resolve(new NavigationRequest(b.FileName, new NavigationReference.Page(PageEdition.Vri, 999, 1)));
            Assert.Equal(NavigationStatus.ReferenceOutOfRange, r.Status);
        }

        // --- chapters / raw / passthrough -----------------------------------

        [Fact]
        public void Chapter_passes_through_id_as_anchor()
        {
            var b = NonMultiBook();
            var r = Resolver().Resolve(new NavigationRequest(b.FileName, new NavigationReference.Chapter("dn1")));
            Assert.Equal(NavigationStatus.Resolved, r.Status);
            Assert.Equal("dn1", r.Target!.Anchor);
        }

        [Fact]
        public void Chapter_missing_in_catalog_is_OutOfRange()
        {
            var b = NonMultiBook();
            var cat = new FakeCatalog(b.FileName, chapterIds: new[] { "dn1" });
            var r = Resolver(cat).Resolve(new NavigationRequest(b.FileName, new NavigationReference.Chapter("zz9")));
            Assert.Equal(NavigationStatus.ReferenceOutOfRange, r.Status);
        }

        [Fact]
        public void RawAnchor_passes_through()
        {
            var b = NonMultiBook();
            var r = Resolver().Resolve(new NavigationRequest(b.FileName, new NavigationReference.RawAnchor("para42_sn1")));
            Assert.Equal(NavigationStatus.Resolved, r.Status);
            Assert.Equal("para42_sn1", r.Target!.Anchor);
        }

        [Fact]
        public void SearchTerms_are_carried_through()
        {
            var b = NonMultiBook();
            var terms = new[] { "metta", "karuna" };
            var r = Resolver().Resolve(new NavigationRequest(b.FileName, new NavigationReference.Paragraph(1), terms));
            Assert.Equal(terms, r.Target!.SearchTerms);
        }

        [Fact]
        public void SearchTerms_default_to_empty_not_null()
        {
            var b = NonMultiBook();
            var r = Resolver().Resolve(new NavigationRequest(b.FileName, new NavigationReference.WholeBook()));
            Assert.NotNull(r.Target!.SearchTerms);
            Assert.Empty(r.Target!.SearchTerms);
        }

        // --- fake catalog ----------------------------------------------------

        private sealed class FakeCatalog : IAnchorCatalog
        {
            private readonly string _file;
            private readonly BookAnchors _anchors;

            public FakeCatalog(
                string file,
                IReadOnlyList<ParagraphAnchor>? paragraphs = null,
                IReadOnlyList<PageAnchor>? pages = null,
                IReadOnlyList<string>? chapterIds = null)
            {
                _file = file;
                _anchors = new BookAnchors(
                    paragraphs ?? Array.Empty<ParagraphAnchor>(),
                    pages ?? Array.Empty<PageAnchor>(),
                    chapterIds ?? Array.Empty<string>());
            }

            public BookAnchors? GetBookAnchors(string bookFileName) =>
                string.Equals(bookFileName, _file, StringComparison.OrdinalIgnoreCase) ? _anchors : null;
        }
    }
}
