using System.Linq;
using CST.Avalonia.Services;
using CST.Avalonia.ViewModels;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// Tests for <see cref="MeaningParser"/>, which splits a dictionary definition fragment into renderable
/// segments. The key regression is DICT-1: the merge separator between duplicate-headword definitions
/// must become a <see cref="MeaningSegment.Separator"/>, never a literal text run.
/// </summary>
public class MeaningParserTests
{
    // Identity link display so assertions don't depend on script conversion.
    private static System.Collections.Generic.IReadOnlyList<MeaningSegment> Parse(string html)
        => MeaningParser.Parse(html, s => s);

    [Fact]
    public void EmptyOrNull_YieldsNoSegments()
    {
        Assert.Empty(Parse(""));
        Assert.Empty(MeaningParser.Parse(null, s => s));
    }

    [Fact]
    public void PlainText_IsOneTextSegment()
    {
        var segs = Parse("Agreement, combination");
        var seg = Assert.Single(segs);
        Assert.False(seg.IsLink);
        Assert.False(seg.IsSeparator);
        Assert.Equal("Agreement, combination", seg.Text);
    }

    [Fact]
    public void SeeTag_BecomesLinkSegmentWithTarget()
    {
        var segs = Parse("see <see>dhammo</see>");
        Assert.Equal(2, segs.Count);
        Assert.Equal("see ", segs[0].Text);
        Assert.False(segs[0].IsLink);
        Assert.True(segs[1].IsLink);
        Assert.Equal("dhammo", segs[1].Text);
        Assert.Equal("dhammo", segs[1].Target);
    }

    [Fact]
    public void MergedDefinitions_ProduceSeparator_NotLiteralTag()
    {
        // DICT-1: two definitions joined by the sentinel must render as text + separator + text,
        // and the raw sentinel must never appear in any segment's text.
        var html = "Mysterious; wonderful, portentous"
                   + DictionaryService.MeaningSeparator
                   + "The Marvellous; a gambler's stake";

        var segs = Parse(html);

        Assert.Equal(3, segs.Count);
        Assert.Equal("Mysterious; wonderful, portentous", segs[0].Text);
        Assert.True(segs[1].IsSeparator);
        Assert.False(segs[1].IsLink);
        Assert.Equal("The Marvellous; a gambler's stake", segs[2].Text);
        Assert.DoesNotContain(segs, s => s.Text.Contains(DictionaryService.MeaningSeparator));
    }

    [Fact]
    public void ThreeMergedDefinitions_YieldTwoSeparators()
    {
        var sep = DictionaryService.MeaningSeparator;
        var segs = Parse("a" + sep + "b" + sep + "c");
        Assert.Equal(5, segs.Count);
        Assert.Equal(new[] { false, true, false, true, false }, segs.Select(s => s.IsSeparator).ToArray());
        Assert.Equal(new[] { "a", "b", "c" }, segs.Where(s => !s.IsSeparator).Select(s => s.Text).ToArray());
    }

    [Fact]
    public void LeadingWhitespace_InEachDefinition_IsTrimmed()
    {
        var sep = DictionaryService.MeaningSeparator;
        var segs = Parse(" first" + sep + " second");
        Assert.Equal("first", segs[0].Text);
        Assert.Equal("second", segs[2].Text);
    }

    [Fact]
    public void HtmlEntities_AreDecoded()
    {
        var seg = Assert.Single(Parse("a &amp; b"));
        Assert.Equal("a & b", seg.Text);
    }
}
