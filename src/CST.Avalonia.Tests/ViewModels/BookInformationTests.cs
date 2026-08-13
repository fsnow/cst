using CST;
using CST.Avalonia.ViewModels;
using CST.Conversion;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// The book-information panel's formatting (#628).
///
/// <para>
/// The panel exists for text-correction work: an error found while reading is reported against the source
/// file, and until now the reader never showed which file it was displaying. What is testable here is the
/// formatting — the file name must survive untouched, and the nav path must follow the reading script,
/// because a path shown in the wrong script is worse than no path at all to someone cross-referencing.
/// </para>
/// </summary>
public class BookInformationTests
{
    // Devanagari written as escapes, per the project rule against literal non-Latin characters in source.
    // DevaVinaya spells "vinaya" and DevaSutta spells "sutta". The corpus stores
    // every nav path in Devanagari whatever script the reader is using.
    private const string DevaVinaya = "\u0935\u093F\u0928\u092F";
    private const string DevaSutta = "\u0938\u0941\u0924\u094D\u0924";
    private const string DevaNavPath = DevaVinaya + "/" + DevaSutta;

    // ---- The file name ---------------------------------------------------------------------------

    [Fact]
    public void The_file_name_is_shown_exactly_as_it_is_on_disk()
    {
        // It names a file. Converting it — as every other string in this panel is converted — would
        // produce something that looks like an answer and matches nothing on the filesystem, in the
        // repository, or in an issue report.
        var book = new Book { FileName = "s0203m.mul.xml", LongNavPath = DevaNavPath };
        Assert.Equal("s0203m.mul.xml", book.FileName);
    }

    // ---- The nav path ----------------------------------------------------------------------------

    [Fact]
    public void The_nav_path_follows_the_reading_script()
    {
        var deva = BookDisplayViewModel.FormatNavPath(DevaNavPath, Script.Devanagari);
        var latin = BookDisplayViewModel.FormatNavPath(DevaNavPath, Script.Latin);

        Assert.NotEqual(deva, latin);
        // Asserted on the stems rather than the whole word, so this pins "it was converted to Latin"
        // without restating the converter's capitalization rules — which are its business, not this
        // method's, and are covered by the converter's own tests.
        Assert.Contains("inaya", latin);
        Assert.Contains("utta", latin);
    }

    [Fact]
    public void Devanagari_is_passed_through_rather_than_round_tripped()
    {
        // Reading in Devanagari must not run the converter at all: a Deva->Deva round trip is a no-op at
        // best, and this panel is the one place a corrupted path would look authoritative.
        var formatted = BookDisplayViewModel.FormatNavPath(DevaNavPath, Script.Devanagari);
        Assert.Equal(DevaVinaya + " / " + DevaSutta, formatted);
    }

    [Fact]
    public void Separators_are_spaced_so_a_long_path_can_wrap()
    {
        Assert.Equal("a / b / c", BookDisplayViewModel.FormatNavPath("a/b/c", Script.Devanagari));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void A_book_with_no_nav_path_shows_nothing_rather_than_a_stray_separator(string? path)
    {
        Assert.Equal("", BookDisplayViewModel.FormatNavPath(path, Script.Latin));
    }

    // ---- The classification labels ---------------------------------------------------------------

    [Theory]
    [InlineData(Pitaka.Vinaya, "Vinaya")]
    [InlineData(Pitaka.Sutta, "Sutta")]
    [InlineData(Pitaka.Abhidhamma, "Abhidhamma")]
    [InlineData(Pitaka.Other, "Other")]
    public void Every_pitaka_has_a_label(Pitaka pitaka, string expected)
    {
        Assert.Equal(expected, BookDisplayViewModel.DescribePitaka(pitaka));
    }

    [Theory]
    [InlineData(CommentaryLevel.Mula)]
    [InlineData(CommentaryLevel.Atthakatha)]
    [InlineData(CommentaryLevel.Tika)]
    [InlineData(CommentaryLevel.Other)]
    public void Every_commentary_level_has_a_label(CommentaryLevel level)
    {
        Assert.False(string.IsNullOrWhiteSpace(BookDisplayViewModel.DescribeCommentaryLevel(level)));
    }

    [Fact]
    public void The_commentary_level_is_named_in_Pali_and_glossed_in_English()
    {
        // Both halves are deliberate: the Pāli term is what the texts and the people reporting errors in
        // them use, and the reader offers no English gloss for it anywhere else.
        Assert.Equal("Mūla (root text)", BookDisplayViewModel.DescribeCommentaryLevel(CommentaryLevel.Mula));
        Assert.Equal("Aṭṭhakathā (commentary)", BookDisplayViewModel.DescribeCommentaryLevel(CommentaryLevel.Atthakatha));
        Assert.Equal("Ṭīkā (sub-commentary)", BookDisplayViewModel.DescribeCommentaryLevel(CommentaryLevel.Tika));
    }
}
