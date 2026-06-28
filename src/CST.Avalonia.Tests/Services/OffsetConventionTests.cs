using System.Collections.Generic;
using System.IO;
using System.Linq;
using CST;                       // DevaXmlTokenizer
using CST.Conversion;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;
using Xunit;

namespace CST.Avalonia.Tests;

/// <summary>
/// Regression coverage for #53: DevaXmlTokenizer must emit EXCLUSIVE end offsets (endOffset = one past
/// the token's last source char). The old code stored inclusive offsets, so a single-source-char token
/// ("ca" = U+091A) was zero-width and search discarded it, making any multi-word query containing such a
/// word return nothing.
///
/// Hermetic: tokenizes Devanagari text directly, so it tests the tokenizer CODE regardless of any
/// machine's built (or stale) Lucene index. (#53)
/// </summary>
public class OffsetConventionTests
{
    private static List<(string term, int start, int end)> Tokenize(string deva)
    {
        var tokens = new List<(string, int, int)>();
        using var tok = new DevaXmlTokenizer(LuceneVersion.LUCENE_48, new StringReader(deva));
        var termAtt = tok.GetAttribute<ICharTermAttribute>();
        var offAtt = tok.GetAttribute<IOffsetAttribute>();
        tok.Reset();
        while (tok.IncrementToken())
            tokens.Add((termAtt.ToString(), offAtt.StartOffset, offAtt.EndOffset));
        tok.End();
        return tokens;
    }

    [Fact]
    public void EndOffsetsAreExclusive_SingleCharTokenIsNotZeroWidth()
    {
        // Build the Devanagari input from a known Latin phrase (no literal glyphs in source).
        var deva = ScriptConverter.Convert("rājagahaṃ antarā ca nāḷandaṃ", Script.Latin, Script.Devanagari);
        var tokens = Tokenize(deva);

        // "ca" is a single Devanagari consonant char. Under the exclusive convention its source span must
        // be exactly 1 (endOffset == startOffset + 1), never zero-width. Direct #53 root-cause guard.
        var caIpe = Any2Ipe.Convert("ca");
        Assert.Contains(tokens, t => t.term == caIpe);
        var ca = tokens.First(t => t.term == caIpe);
        Assert.True(ca.end > ca.start,
            $"single-char 'ca' has non-positive offset span ({ca.start}..{ca.end}) - offsets regressed to inclusive (#53)");
        Assert.Equal(1, ca.end - ca.start);

        // Plain multi-char words (no intra-word quotes/joiners/ZWJ-ZWNJ): exclusive span == Devanagari
        // char count.
        foreach (var w in new[] { "rājagahaṃ", "antarā", "nāḷandaṃ" })
        {
            var ipe = Any2Ipe.Convert(w);
            var devaWord = ScriptConverter.Convert(ipe, Script.Ipe, Script.Devanagari);
            Assert.Contains(tokens, t => t.term == ipe);
            var t = tokens.First(x => x.term == ipe);
            Assert.Equal(devaWord.Length, t.end - t.start);
        }
    }
}
