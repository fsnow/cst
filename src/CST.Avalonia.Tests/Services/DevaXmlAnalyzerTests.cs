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
}
