using System;
using System.Collections.Generic;
using CST.Avalonia.ViewModels;
using CST.Navigation;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// #457 / #541: which page-numbering systems a book carries is one fact, and both the Go To dialog's
/// greying-out and the status bar's field list must read it from the same place. Before this, Go To derived
/// availability from the page in effect at the current scroll position, and the status bar showed all five
/// systems unconditionally — opposite symptoms of the same missing fact.
/// </summary>
public class PageNumberingTests
{
    private static IReadOnlyList<PageEdition> Eds(params PageEdition[] e) => e;

    private static string Page(PageEdition e) => e switch
    {
        PageEdition.Vri => "1.0023",
        PageEdition.Myanmar => "2.0117",
        PageEdition.Pts => "1.0045",
        PageEdition.Thai => "3.0210",
        PageEdition.Other => "1.0002",
        _ => "?",
    };

    // ---- Has ------------------------------------------------------------------------------------------

    [Fact]
    public void Has_IsTrue_OnlyForEditionsTheBookCarries()
    {
        var editions = Eds(PageEdition.Vri, PageEdition.Myanmar);

        Assert.True(PageNumbering.Has(editions, PageEdition.Vri));
        Assert.True(PageNumbering.Has(editions, PageEdition.Myanmar));
        Assert.False(PageNumbering.Has(editions, PageEdition.Pts));
        Assert.False(PageNumbering.Has(editions, PageEdition.Thai));
        Assert.False(PageNumbering.Has(editions, PageEdition.Other));
    }

    /// <summary>
    /// An empty list is a real answer — "this book carries no page numbering at all" — and must NOT be
    /// confused with not knowing yet. Every system is unavailable.
    /// </summary>
    [Theory]
    [InlineData(PageEdition.Vri)]
    [InlineData(PageEdition.Myanmar)]
    [InlineData(PageEdition.Pts)]
    [InlineData(PageEdition.Thai)]
    [InlineData(PageEdition.Other)]
    public void Has_IsFalse_ForEveryEdition_WhenTheBookCarriesNone(PageEdition edition)
    {
        Assert.False(PageNumbering.Has(Array.Empty<PageEdition>(), edition));
    }

    /// <summary>
    /// The #457 symptom that made the dialog useless: markers not yet built. Unknown must answer "available",
    /// not "absent" — withholding a system the book has leaves the reader no route in, while offering one it
    /// lacks costs a single failed lookup. This is the test that fails if someone "simplifies" null to empty.
    /// </summary>
    [Theory]
    [InlineData(PageEdition.Vri)]
    [InlineData(PageEdition.Myanmar)]
    [InlineData(PageEdition.Pts)]
    [InlineData(PageEdition.Thai)]
    [InlineData(PageEdition.Other)]
    public void Has_IsTrue_ForEveryEdition_WhenEditionsAreNotYetKnown(PageEdition edition)
    {
        Assert.True(PageNumbering.Has(null, edition));
    }

    // ---- DefaultType ----------------------------------------------------------------------------------

    [Fact]
    public void DefaultType_PrefersVri_WhenThePresentEditionsIncludeIt()
    {
        Assert.Equal(
            NavigationType.VriPage,
            PageNumbering.DefaultType(Eds(PageEdition.Thai, PageEdition.Vri, PageEdition.Myanmar)));
    }

    /// <summary>
    /// Order is by edition, not by the order the book happens to list them — a book that opens with a Thai
    /// page break still defaults to VRI if it has VRI anywhere.
    /// </summary>
    [Fact]
    public void DefaultType_IgnoresTheOrderTheEditionsArriveIn()
    {
        var a = PageNumbering.DefaultType(Eds(PageEdition.Vri, PageEdition.Myanmar));
        var b = PageNumbering.DefaultType(Eds(PageEdition.Myanmar, PageEdition.Vri));
        Assert.Equal(a, b);
        Assert.Equal(NavigationType.VriPage, a);
    }

    [Fact]
    public void DefaultType_FallsToTheNextEdition_WhenVriIsAbsent()
    {
        Assert.Equal(
            NavigationType.MyanmarPage,
            PageNumbering.DefaultType(Eds(PageEdition.Other, PageEdition.Myanmar)));
    }

    /// <summary>
    /// Paragraph is the fallback, not the default: it is ambiguous in 102 of 217 books (#447), so it is what
    /// we use only when the book offers nothing better.
    /// </summary>
    [Fact]
    public void DefaultType_IsParagraph_OnlyWhenTheBookCarriesNoPageNumbering()
    {
        Assert.Equal(NavigationType.Paragraph, PageNumbering.DefaultType(Array.Empty<PageEdition>()));
        Assert.Equal(NavigationType.Paragraph, PageNumbering.DefaultType(null));
    }

    [Theory]
    [InlineData(PageEdition.Vri, NavigationType.VriPage)]
    [InlineData(PageEdition.Myanmar, NavigationType.MyanmarPage)]
    [InlineData(PageEdition.Pts, NavigationType.PtsPage)]
    [InlineData(PageEdition.Thai, NavigationType.ThaiPage)]
    [InlineData(PageEdition.Other, NavigationType.OtherPage)]
    public void ToNavigationType_MapsEveryEdition(PageEdition edition, NavigationType expected)
    {
        Assert.Equal(expected, PageNumbering.ToNavigationType(edition));
    }

    // ---- ComposeStatus --------------------------------------------------------------------------------

    /// <summary>The #541 defect: a book with no PTS pagination showed a permanently blank "PTS:" field,
    /// which reads as missing data rather than as not applicable.</summary>
    [Fact]
    public void ComposeStatus_OmitsSystemsTheBookDoesNotCarry()
    {
        var text = PageNumbering.ComposeStatus(Eds(PageEdition.Vri, PageEdition.Myanmar), Page);

        Assert.Equal("VRI: 1.0023   Myanmar: 2.0117", text);
        Assert.DoesNotContain("PTS", text);
        Assert.DoesNotContain("Thai", text);
        Assert.DoesNotContain("Other", text);
    }

    /// <summary>The paragraph number was in the shipped status bar behind a comment calling it "for
    /// debugging" (#541).</summary>
    [Fact]
    public void ComposeStatus_DoesNotCarryTheParagraphDebugField()
    {
        var text = PageNumbering.ComposeStatus(Eds(PageEdition.Vri), Page);
        Assert.DoesNotContain("Para", text);
    }

    [Fact]
    public void ComposeStatus_IsEmpty_WhenTheBookCarriesNoPageNumbering()
    {
        Assert.Equal("", PageNumbering.ComposeStatus(Array.Empty<PageEdition>(), Page));
    }

    /// <summary>Before markers are built we show everything, so the bar does not visibly fill in on load —
    /// a bar that flickers from empty to populated reads as a fault.</summary>
    [Fact]
    public void ComposeStatus_ShowsEverySystem_WhenEditionsAreNotYetKnown()
    {
        var text = PageNumbering.ComposeStatus(null, Page);

        Assert.Equal("VRI: 1.0023   Myanmar: 2.0117   PTS: 1.0045   Thai: 3.0210   Other: 1.0002", text);
    }

    /// <summary>
    /// "*" means "no page of this edition is in effect at this scroll position" — a real state, and NOT a
    /// reason to drop the field. Dropping it would make the bar's field list change as the reader scrolls,
    /// which is the position-dependence #457 exists to remove.
    /// </summary>
    [Fact]
    public void ComposeStatus_KeepsAFieldWhoseCurrentPageIsUnset()
    {
        var text = PageNumbering.ComposeStatus(
            Eds(PageEdition.Vri, PageEdition.Myanmar),
            e => e == PageEdition.Myanmar ? "*" : "1.0023");

        Assert.Equal("VRI: 1.0023   Myanmar: *", text);
    }

    /// <summary>Fields appear in a fixed order regardless of how the book lists its editions, so the bar
    /// does not reorder itself between books.</summary>
    [Fact]
    public void ComposeStatus_UsesAFixedFieldOrder()
    {
        var forward = PageNumbering.ComposeStatus(Eds(PageEdition.Vri, PageEdition.Thai), Page);
        var reversed = PageNumbering.ComposeStatus(Eds(PageEdition.Thai, PageEdition.Vri), Page);

        Assert.Equal(forward, reversed);
        Assert.Equal("VRI: 1.0023   Thai: 3.0210", forward);
    }

    /// <summary>A single system produces no leading or trailing separator.</summary>
    [Fact]
    public void ComposeStatus_DoesNotPadASingleField()
    {
        Assert.Equal("Thai: 3.0210", PageNumbering.ComposeStatus(Eds(PageEdition.Thai), Page));
    }

    // ---- Offers / Resolve (#844) ----------------------------------------------------------------------

    [Fact]
    public void Offers_Paragraph_ForEveryBook_IncludingOneWithNoPagination()
    {
        // Paragraph is the fallback precisely because it is the one address every book has.
        Assert.True(PageNumbering.Offers(Eds(), NavigationType.Paragraph));
        Assert.True(PageNumbering.Offers(Eds(PageEdition.Thai), NavigationType.Paragraph));
        Assert.True(PageNumbering.Offers(null, NavigationType.Paragraph));
    }

    [Fact]
    public void Offers_APageSystem_OnlyWhenTheBookCarriesIt()
    {
        var editions = Eds(PageEdition.Vri, PageEdition.Myanmar);

        Assert.True(PageNumbering.Offers(editions, NavigationType.VriPage));
        Assert.True(PageNumbering.Offers(editions, NavigationType.MyanmarPage));
        Assert.False(PageNumbering.Offers(editions, NavigationType.PtsPage));
        Assert.False(PageNumbering.Offers(editions, NavigationType.ThaiPage));
        Assert.False(PageNumbering.Offers(editions, NavigationType.OtherPage));
    }

    [Fact]
    public void Offers_TreatsUnbuiltMarkersAsAvailable_LikeHas()
    {
        // null is "not built yet", not "none" — see Has. Withholding a system the book has leaves the
        // reader no route in; offering one it lacks costs a failed lookup.
        Assert.True(PageNumbering.Offers(null, NavigationType.PtsPage));
    }

    [Fact]
    public void Resolve_KeepsTheReadersSystem_WhenTheBookHasIt()
    {
        var editions = Eds(PageEdition.Vri, PageEdition.Pts);

        // VRI wins the DefaultType precedence, so a PTS answer here can only come from the preference.
        Assert.Equal(NavigationType.VriPage, PageNumbering.DefaultType(editions));
        Assert.Equal(NavigationType.PtsPage, PageNumbering.Resolve(NavigationType.PtsPage, editions));
    }

    [Fact]
    public void Resolve_FallsBackToTheBooksDefault_WhenTheBookLacksIt()
    {
        // The PTS reader opens a Myanmar-only text. The dialog has to be usable; the preference itself is
        // the caller's to keep, and Resolve cannot touch it because it never sees it.
        var editions = Eds(PageEdition.Myanmar);

        Assert.Equal(NavigationType.MyanmarPage, PageNumbering.Resolve(NavigationType.PtsPage, editions));
    }

    [Fact]
    public void Resolve_WithNoPreference_BehavesExactlyAsBefore()
    {
        // A first run, or a state file written before #844 existed.
        foreach (var editions in new[] { Eds(), Eds(PageEdition.Thai), Eds(PageEdition.Vri, PageEdition.Pts) })
            Assert.Equal(PageNumbering.DefaultType(editions), PageNumbering.Resolve(null, editions));
    }

    [Fact]
    public void Resolve_HonoursAPreferenceForParagraph_OverAPaginatedBook()
    {
        // The one case where the preference must beat a "better" address: a reader who works in paragraph
        // numbers has chosen that, and DefaultType would override them on every book with pagination.
        var editions = Eds(PageEdition.Vri, PageEdition.Pts);

        Assert.Equal(NavigationType.VriPage, PageNumbering.DefaultType(editions));
        Assert.Equal(NavigationType.Paragraph, PageNumbering.Resolve(NavigationType.Paragraph, editions));
    }
}
