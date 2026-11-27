using System;
using System.Collections.Generic;
using CST;
using Xunit;

namespace CST.Avalonia.Tests.Models
{
    /// <summary>
    /// A book's page mapping can differ between versions of its PDF. Each listed version (by SharePoint
    /// quickXorHash) gets its own mapping; a version that is not listed gets the latest: [fsnow, 2026-11-26]
    /// "If there is a newer file we don't have a mapping for, we will use the latest." (#1136)
    /// </summary>
    public class SourcePdfVersionTests
    {
        // The original scan lacks print pages 156-157; the repaired one has them, so later pages shift.
        private static Sources.Source Original() =>
            new(Sources.SourceType.Burmese1957, 7, "x.pdf", new[] { 156, 157 });
        private static Sources.Source Repaired() => new(Sources.SourceType.Burmese1957, 7, "x.pdf");

        [Fact]
        public void WithNoVersionsListed_EveryFileGetsTheOneMapping()
        {
            var s = Original();
            Assert.Same(s, s.ForVersion("anything"));
            Assert.Same(s, s.ForVersion(null));
            Assert.Same(s, s.Latest);
        }

        [Fact]
        public void EachListedVersion_GetsItsOwnMapping_AndAnUnlistedOneTheLatest()
        {
            var s = Original();
            var repaired = Repaired();
            s.AddVersion(new[] { "v1", "v2" }, s, latest: false);      // addSourceVersions
            s.AddVersion(new[] { "r1" }, repaired, latest: true);     // addVersion

            Assert.Equal(7 + 199 - 2, s.ForVersion("v1").PdfPageFor(200));
            Assert.Equal(7 + 199 - 2, s.ForVersion("v2").PdfPageFor(200));
            Assert.Equal(7 + 199, s.ForVersion("r1").PdfPageFor(200));
            // A newer file nobody has mapped yet, or a hash that could not be read: the latest.
            Assert.Same(repaired, s.ForVersion("r2"));
            Assert.Same(repaired, s.ForVersion(null));
        }

        [Fact]
        public void TheLastAddedVersion_IsTheLatest()
        {
            var s = Original();
            var r1 = Repaired();
            var r2 = new Sources.Source(Sources.SourceType.Burmese1957, 8, "x.pdf");
            s.AddVersion(new[] { "r1" }, r1, latest: true);
            s.AddVersion(new[] { "r2" }, r2, latest: true);
            Assert.Same(r2, s.Latest);
            Assert.Same(r1, s.ForVersion("r1"));
        }

        [Fact]
        public void TwoMappingsForOneVersion_AreAnError()
        {
            var s = Original();
            s.AddVersion(new[] { "h" }, s, latest: false);
            Assert.Throws<InvalidOperationException>(() => s.AddVersion(new[] { "h" }, Repaired(), latest: true));
        }

        [Fact]
        public void InSourcesCs_EveryVersionIsForTheSamePdf_AndOlderCopiesAreListed()
        {
            foreach (var (book, type, source) in AllSources())
            {
                foreach (var (hash, v) in source.Versions)
                {
                    Assert.Equal(source.Path, v.Path);
                    Assert.Equal(source.PageEdition, v.PageEdition);
                    Assert.False(string.IsNullOrWhiteSpace(hash), $"{book} {type}: empty version hash");
                }
                // Once a book has a later mapping, an unlisted file gets it; so the versions its addSource
                // mapping fits must be listed, or a reader with one of those copies cached gets the later mapping.
                if (!ReferenceEquals(source.Latest, source))
                    Assert.True(source.Versions.Values.Contains(source),
                        $"{book} {type}: has addVersion entries but no addSourceVersions");
            }
        }

        [Fact]
        public void InSourcesCs_EveryHashIsAQuickXorHash()
        {
            // A typo in an addSourceVersions hash is the dangerous kind: the copy it was meant for would silently
            // get the latest mapping. A well-formed hash can still be the wrong one, but a malformed one is caught.
            foreach (var (book, type, source) in AllSources())
                foreach (var hash in source.Versions.Keys)
                {
                    Assert.True(hash.Length == 28, $"{book} {type}: {hash} is not 28 characters");
                    Assert.Equal(20, Convert.FromBase64String(hash).Length);
                }
        }

        [Fact]
        public void InSourcesCs_BooksSharingAPdf_ListTheSameVersions()
        {
            // Books that share one PDF file have the same versions of it. Among those with version entries, one
            // listing a hash the other lacks would give that version the latest mapping for one book only.
            var byPath = new Dictionary<string, List<(string Book, HashSet<string> Hashes)>>();
            foreach (var (book, type, source) in AllSources())
                if (source.Versions.Count > 0)
                {
                    if (!byPath.TryGetValue(source.Path, out var list)) byPath[source.Path] = list = new();
                    list.Add(($"{book} {type}", new HashSet<string>(source.Versions.Keys)));
                }
            foreach (var (path, books) in byPath)
                foreach (var (book, hashes) in books)
                    Assert.True(hashes.SetEquals(books[0].Hashes), $"{book} and {books[0].Book} list different versions of {path}");
        }

        private static IEnumerable<(string, Sources.SourceType, Sources.Source)> AllSources()
        {
            foreach (var book in Books.Inst)
                foreach (var t in Sources.Inst.GetAvailableSources(book.FileName))
                    yield return (book.FileName, t, Sources.Inst.GetSource(book.FileName, t)!);
        }
    }
}
