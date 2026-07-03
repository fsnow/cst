using CST.Avalonia.ViewModels;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// #66: GoToDialog must reject non-numeric junk and build page anchors that match the book XML's
/// @ed+@n format ("V&lt;volume&gt;.&lt;page4&gt;"), deriving the volume instead of hardcoding "1." -
/// corpus volumes run 0-7, so the old hardcoded "1." broke GoTo for every non-volume-1 book.
/// </summary>
public class GoToDialogViewModelTests
{
    [Theory]
    [InlineData("abc123def")]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("12a")]
    [InlineData("v.p")]
    [InlineData("1.2.3")]
    public void IsValidInput_RejectsNonNumeric(string input)
    {
        Assert.False(GoToDialogViewModel.IsValidInput(input, NavigationType.Paragraph));
        Assert.False(GoToDialogViewModel.IsValidInput(input, NavigationType.VriPage));
    }

    [Theory]
    [InlineData("123")]
    [InlineData("  45  ")]
    public void IsValidInput_AcceptsPlainNumber_ForBothTypes(string input)
    {
        Assert.True(GoToDialogViewModel.IsValidInput(input, NavigationType.Paragraph));
        Assert.True(GoToDialogViewModel.IsValidInput(input, NavigationType.VriPage));
    }

    /// <summary>
    /// SCRIPT-3: validation must be ASCII-only. \d / char.IsDigit match any Unicode Nd digit (e.g. Devanagari
    /// digits pasted from a book), but page parsing uses ASCII-only int.TryParse - so a non-ASCII digit that
    /// passed validation built a dead paragraph anchor or an empty page anchor with the dialog still closing.
    /// </summary>
    [Theory]
    [InlineData("\u0967\u0968\u0969")]  // Devanagari 123
    [InlineData("12\u0969")]            // ASCII "12" + Devanagari 3
    [InlineData("\u0662\u0663")]        // Arabic-Indic 23
    [InlineData("\uFF11\uFF12\uFF13")]  // fullwidth 123
    public void IsValidInput_RejectsNonAsciiDigits(string input)
    {
        Assert.False(GoToDialogViewModel.IsValidInput(input, NavigationType.Paragraph));
        Assert.False(GoToDialogViewModel.IsValidInput(input, NavigationType.VriPage));
    }

    [Fact]
    public void IsValidInput_VolumeDotPage_IsForPagesNotParagraphs()
    {
        Assert.True(GoToDialogViewModel.IsValidInput("2.45", NavigationType.VriPage));
        Assert.False(GoToDialogViewModel.IsValidInput("2.45", NavigationType.Paragraph));
    }

    [Theory]
    [InlineData("1.23", 1)]
    [InlineData("2.45", 2)]
    [InlineData("7.1", 7)]
    [InlineData("23", 0)]   // volume-0 pages display with no "0." prefix
    [InlineData("*", 0)]
    [InlineData("", 0)]
    [InlineData(null, 0)]
    public void DeriveVolume_FromCurrentPageDisplay(string? display, int expected)
        => Assert.Equal(expected, GoToDialogViewModel.DeriveVolume(display));

    [Fact]
    public void BuildPageAnchor_DerivesVolumeFromCurrentPage()
    {
        // current page in volume 2 -> a bare page targets volume 2 (old code hardcoded "V1.")
        Assert.Equal("V2.0050", GoToDialogViewModel.BuildPageAnchor("V", "50", "2.45"));
        // volume-0 book (current page shows no dot) -> "V0.0050", not the old broken "V1.0050"
        Assert.Equal("V0.0050", GoToDialogViewModel.BuildPageAnchor("V", "50", "23"));
    }

    [Fact]
    public void BuildPageAnchor_HonorsExplicitVolumeDotPage()
        => Assert.Equal("M3.0010", GoToDialogViewModel.BuildPageAnchor("M", "3.10", "1.5"));

    [Theory]
    [InlineData("1", "P1.0001")]
    [InlineData("1645", "P1.1645")]
    public void BuildPageAnchor_PadsPageToFourDigits(string input, string expected)
        => Assert.Equal(expected, GoToDialogViewModel.BuildPageAnchor("P", input, "1.2"));
}
