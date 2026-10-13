using System.Collections.Generic;
using System.IO;
using CST;                       // DevaXmlAnalyzer
using CST.Conversion;
using Lucene.Net.Analysis;
using Lucene.Net.Analysis.TokenAttributes;
using Lucene.Net.Util;
using Xunit;

namespace CST.Avalonia.Tests;

/// <summary>
/// SRCH-14: DevaXmlAnalyzer must not share tokenizer state across documents. Reusing one analyzer
/// instance (Lucene's normal path, via GetTokenStream) must yield each document's own tokens — the
/// per-document buffering now happens in DevaXmlTokenizer.Reset() from the tokenizer's own reader, not
/// through a shared analyzer field. Diacritics are written directly (Latin-extended), matching
/// OffsetConventionTests.
/// </summary>
public class DevaXmlAnalyzerTests
{
    private static List<string> Terms(Analyzer analyzer, string deva)
    {
        var terms = new List<string>();
        using var ts = analyzer.GetTokenStream("text", new StringReader(deva));
        var termAtt = ts.GetAttribute<ICharTermAttribute>();
        ts.Reset();
        while (ts.IncrementToken())
            terms.Add(termAtt.ToString());
        ts.End();
        return terms;
    }

    [Fact]
    public void ReusedAnalyzer_ProducesEachDocumentsOwnTokens()
    {
        using var analyzer = new DevaXmlAnalyzer(LuceneVersion.LUCENE_48);
        var docA = ScriptConverter.Convert("buddho dhammo", Script.Latin, Script.Devanagari);
        var docB = ScriptConverter.Convert("saṅgho", Script.Latin, Script.Devanagari);

        var a1 = Terms(analyzer, docA);
        var b = Terms(analyzer, docB);   // reuse: must re-buffer from B's reader, not carry A's text over
        var a2 = Terms(analyzer, docA);  // reuse again

        Assert.Equal(new[] { Any2Ipe.Convert("buddho"), Any2Ipe.Convert("dhammo") }, a1);
        Assert.Equal(new[] { Any2Ipe.Convert("saṅgho") }, b);
        Assert.Equal(a1, a2);
    }

    [Fact]
    public void SanskritLetters_StayInsideTheirWord()
    {
        // The tokenizer keeps a fixed list of word characters. Before #1049 it held only the Pali letters, so
        // a word with a Sanskrit letter was split at that letter and its pieces indexed as separate words.
        using var analyzer = new DevaXmlAnalyzer(LuceneVersion.LUCENE_48);
        // vaishyah shudro krtah kai kau (Latin keeps ai/au as hiatus, so build these from Devanagari)
        var doc = "\u0935\u0948\u0936\u094D\u092F\u0903 \u0936\u0942\u0926\u094D\u0930\u094B "
                  + "\u0915\u0943\u0924\u0903 \u0915\u0948 \u0915\u094C \u0915\u094D\u0937";
        var expected = new[]
        {
            "\u0935\u0948\u0936\u094D\u092F\u0903", "\u0936\u0942\u0926\u094D\u0930\u094B",
            "\u0915\u0943\u0924\u0903", "\u0915\u0948", "\u0915\u094C", "\u0915\u094D\u0937",
        };
        Assert.Equal(System.Linq.Enumerable.ToArray(System.Linq.Enumerable.Select(expected, Deva2Ipe.Convert)),
                     Terms(analyzer, doc));
    }
}
