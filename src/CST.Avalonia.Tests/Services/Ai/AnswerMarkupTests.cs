using System.Linq;
using CST.Avalonia.Services.Ai;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// The answer formatter. (#586)
///
/// <para>The reported defect: "It shows single and double asterisks around text rather than applying whatever
/// formatting is intended." Every model emits light Markdown, and we teach it to — the prompts are Markdown,
/// and the word-analysis section is a table.</para>
///
/// <para>The governing rule for all of it: <b>a reader's copy must come out as clean prose</b>. This panel
/// exists so translations can be copied out of it.</para>
/// </summary>
public class AnswerMarkupTests
{
    private static string Rendered(string source) => AnswerMarkup.PlainText(AnswerMarkup.Parse(source));

    private static AnswerStyle StyleOf(string source, string fragment) =>
        AnswerMarkup.Parse(source).First(s => s.Text.Contains(fragment)).Style;

    [Fact]
    public void Bold_and_italic_markers_are_applied_rather_than_shown()
    {
        var source = "The term **appamāda** is rendered *heedfulness* here.";

        Assert.Equal("The term appamāda is rendered heedfulness here.", Rendered(source));
        Assert.Equal(AnswerStyle.Bold, StyleOf(source, "appamāda"));
        Assert.Equal(AnswerStyle.Italic, StyleOf(source, "heedfulness"));
    }

    [Fact]
    public void A_bold_pair_is_not_read_as_two_italics()
    {
        // The failure this ordering prevents: scanning for "*" first turns "**x**" into an italic empty
        // string, a literal "x", and another italic empty string.
        Assert.Equal(AnswerStyle.Bold, StyleOf("**dhamma**", "dhamma"));
        Assert.Equal("dhamma", Rendered("**dhamma**"));
    }

    [Fact]
    public void Headings_become_emphasis_and_lose_their_hashes()
    {
        var spans = AnswerMarkup.Parse("## Word by word\n\nappamādo: heedfulness");

        Assert.Contains(spans, s => s.Text == "Word by word" && s.Style.HasFlag(AnswerStyle.Heading));
        Assert.DoesNotContain("##", Rendered("## Word by word\n\nappamādo: heedfulness"));
    }

    [Fact]
    public void Bullets_become_a_glyph_so_a_copied_answer_still_reads_as_a_list()
    {
        // Not a list control: the whole answer has to stay one selectable run of text. A copied bullet list
        // that has lost its bullets reads as run-together sentences.
        var rendered = Rendered("- first\n- second");

        Assert.Equal("• first\n• second", rendered);
    }

    [Fact]
    public void Indented_bullets_keep_their_indent()
    {
        Assert.Equal("• one\n    • nested", Rendered("- one\n    - nested"));
    }

    [Fact]
    public void An_unclosed_marker_is_left_as_the_reader_would_have_seen_it()
    {
        // Streamed text is unclosed at every flush until it isn't. Styling the rest of the line on an opener
        // would make the whole answer flicker bold as it arrives.
        Assert.Equal("a partial **answer", Rendered("a partial **answer"));
    }

    [Fact]
    public void An_empty_marker_pair_is_punctuation_rather_than_emphasis()
    {
        Assert.Equal("stars ** here", Rendered("stars ** here"));
    }

    [Fact]
    public void Table_rows_keep_their_pipes_in_fixed_pitch()
    {
        // Deliberately not parsed into a grid — that would need separate controls per cell and break
        // selection across the answer. Fixed pitch at least lines the columns up.
        var source = "| word | stem |\n| --- | --- |\n| appamādo | appamāda |";
        var spans = AnswerMarkup.Parse(source);

        Assert.All(spans.Where(s => s.Text.Contains('|')), s => Assert.Equal(AnswerStyle.Monospace, s.Style));
        Assert.Equal(source, Rendered(source));   // nothing is lost
    }

    [Theory]
    [InlineData("")]
    [InlineData(null)]
    public void Nothing_parses_to_nothing(string? source)
    {
        Assert.Empty(AnswerMarkup.Parse(source));
    }

    [Fact]
    public void Plain_prose_survives_untouched()
    {
        // The commonest answer of all, and the one a formatter is most likely to damage.
        const string prose =
            "Heedfulness is the path to the deathless; heedlessness is the path to death.\n\n"
            + "The verse turns on a single pair of opposites.";

        Assert.Equal(prose, Rendered(prose));
        Assert.All(AnswerMarkup.Parse(prose), s => Assert.Equal(AnswerStyle.None, s.Style));
    }

    [Fact]
    public void Adjacent_spans_of_the_same_style_are_merged()
    {
        // A long answer would otherwise be one run per line, and the panel re-lays-out the whole answer on
        // every 100ms flush.
        var spans = AnswerMarkup.Parse("one\ntwo\nthree\nfour");

        Assert.Single(spans);
    }

    [Fact]
    public void A_pali_word_in_the_middle_of_a_sentence_keeps_its_diacritics()
    {
        // The formatter runs over text that has already had its Pāli quote markers stripped, so what it sees
        // is ordinary letters — but a parser that slices by index is exactly where a combining mark gets
        // orphaned.
        Assert.Equal("the line appamādo amatapadaṃ is the opening",
            Rendered("the line **appamādo amatapadaṃ** is the opening"));
    }
}
