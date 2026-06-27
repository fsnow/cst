using System;
using System.Linq;
using System.Text;
using CST.Conversion;
using Lucene.Net.Index;
using Lucene.Net.Search;
using Lucene.Net.Store;
using Lucene.Net.Util;
using Xunit;

namespace CST.Avalonia.Tests;

/// <summary>
/// Regression coverage for #53. The index must store EXCLUSIVE end offsets (standard Lucene:
/// endOffset = one past the token's last source char). Before the fix, DevaXmlTokenizer stored
/// INCLUSIVE end offsets (endOffset = last-char index), so a single-source-char token ("ca" = च)
/// had startOffset == endOffset; SearchService discarded zero-width offsets, so any multi-word
/// query containing such a word returned no results.
///
/// Requires a populated index built with the FIXED tokenizer (delete the index + reindex after
/// changing the offset convention). These assertions FAIL against an index built the old way.
/// </summary>
public class OffsetConventionTests : IDisposable
{
    private readonly DirectoryReader _r;

    public OffsetConventionTests()
    {
        var dir = Environment.GetEnvironmentVariable("CST_INDEX_DIR")
            ?? "/Users/fsnow/Library/Application Support/CSTReader/index";
        _r = DirectoryReader.Open(FSDirectory.Open(dir));
    }

    public void Dispose() => _r.Dispose();

    private (int so, int eo)? FirstOffsetInDoc(string ipe, int docId)
    {
        var dape = MultiFields.GetTermPositionsEnum(_r, MultiFields.GetLiveDocs(_r), "text",
            new BytesRef(Encoding.UTF8.GetBytes(ipe)));
        if (dape == null) return null;
        int doc;
        while ((doc = dape.NextDoc()) != DocIdSetIterator.NO_MORE_DOCS)
        {
            if (doc != docId) continue;
            dape.NextPosition();
            return (dape.StartOffset, dape.EndOffset);
        }
        return null;
    }

    [Fact]
    public void EndOffsetsAreExclusive_SingleCharTokenIsNotZeroWidth()
    {
        var searcher = new IndexSearcher(_r);
        var td = searcher.Search(new TermQuery(new Term("file", "s0101m.mul.xml")), 1);
        Assert.True(td.TotalHits > 0, "s0101m.mul.xml not found in index - build/populate the index first");
        int doc = td.ScoreDocs[0].Doc;

        // "ca" = च, a single Devanagari char. Under the exclusive convention its span must be exactly 1
        // (endOffset == startOffset + 1), never zero-width. This is the direct #53 root-cause guard.
        var ca = FirstOffsetInDoc(Any2Ipe.Convert("ca"), doc);
        Assert.NotNull(ca);
        Assert.True(ca!.Value.eo > ca.Value.so,
            $"single-char 'ca' has non-positive offset span ({ca.Value.so}..{ca.Value.eo}) — end offsets regressed to inclusive (#53)");
        Assert.Equal(1, ca.Value.eo - ca.Value.so);

        // Plain multi-char words (no intra-word quotes/joiners/ZWJ-ZWNJ): exclusive span == Devanagari
        // char count. (Words WITH stripped intra-word chars legitimately span MORE than their length,
        // so only the "end > start" validity above applies to those — not this exact-length check.)
        foreach (var w in new[] { "rājagahaṃ", "antarā", "nāḷandaṃ" })
        {
            var ipe = Any2Ipe.Convert(w);
            var deva = ScriptConverter.Convert(ipe, Script.Ipe, Script.Devanagari);
            var off = FirstOffsetInDoc(ipe, doc);
            Assert.NotNull(off);
            Assert.Equal(deva.Length, off!.Value.eo - off.Value.so);
        }
    }
}
