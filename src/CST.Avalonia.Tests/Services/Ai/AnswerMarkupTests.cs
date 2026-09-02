using System.Linq;
using CST.Avalonia.Services.Ai;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// The answer formatter. (#586)
///
/// <para>The reported defect: "It shows single and double asterisks around text rather than applying whatever
/// formatting is intended." Every model emits light Markdown, and we teach it to — the prompts are Markdown,
/// and the word-analysis section handed to the model is itself a table.</para>
///
/// <para>The governing rule for all of it: <b>a reader's copy must come out as clean prose</b>. This panel
/// exists so translations can be copied out of it, so <see cref="AnswerMarkup.PlainText"/> is asserted
/// alongside nearly every rendering case.</para>
/// </summary>
public class AnswerMarkupTests
{
    private static string Rendered(string? source) => AnswerMarkup.PlainText(AnswerMarkup.Parse(source));

    private static AnswerSpan[] Spans(string source) =>
        AnswerMarkup.Parse(source).OfType<AnswerParagraph>().SelectMany(p => p.Spans).ToArray();

    private static AnswerStyle StyleOf(string source, string fragment) =>
        Spans(source).First(s => s.Text.Contains(fragment)).Style;

    // ---- Inline emphasis --------------------------------------------------------------------------

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
        Assert.Contains(Spans("## Word by word\n\nappamādo: heedfulness"),
            s => s.Text == "Word by word" && s.Style.HasFlag(AnswerStyle.Heading));
        Assert.DoesNotContain("##", Rendered("## Word by word\n\nappamādo: heedfulness"));
    }

    [Fact]
    public void Bullets_become_a_glyph_so_a_copied_answer_still_reads_as_a_list()
    {
        Assert.Equal("• first\n• second", Rendered("- first\n- second"));
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
        Assert.All(Spans(prose), s => Assert.Equal(AnswerStyle.None, s.Style));
    }

    [Fact]
    public void Adjacent_spans_of_the_same_style_are_merged()
    {
        // A long answer would otherwise be one run per line, and the panel re-lays-out the whole answer on
        // every 100ms flush.
        var paragraph = Assert.IsType<AnswerParagraph>(Assert.Single(AnswerMarkup.Parse("one\ntwo\nthree\nfour")));

        Assert.Single(paragraph.Spans);
    }

    [Fact]
    public void A_pali_word_in_the_middle_of_a_sentence_keeps_its_diacritics()
    {
        Assert.Equal("the line appamādo amatapadaṃ is the opening",
            Rendered("the line **appamādo amatapadaṃ** is the opening"));
    }

    // ---- Tables -----------------------------------------------------------------------------------

    private const string WordTable =
        "| form | stem | form | meaning here |\n"
        + "|---|---|---|---|\n"
        + "| appamādo | appamāda | nom. sg. m. | heedfulness |\n"
        + "| amatapadaṃ | amatapada | nom. sg. n. | the deathless state |";

    [Fact]
    public void A_pipe_table_becomes_a_table_with_its_header_and_rows()
    {
        // We hand the model a Markdown table of word analysis, so a word-by-word answer comes back as one.
        // Setting the pipes in monospace and hoping they line up fails at exactly this panel's width.
        var table = Assert.IsType<AnswerTable>(Assert.Single(AnswerMarkup.Parse(WordTable)));

        Assert.Equal(4, table.Header.Count);
        Assert.Equal(2, table.Rows.Count);
        Assert.Equal("appamādo", string.Concat(table.Rows[0][0].Spans.Select(s => s.Text)));
        Assert.Equal("the deathless state", string.Concat(table.Rows[1][3].Spans.Select(s => s.Text)));
    }

    [Fact]
    public void A_header_row_without_its_delimiter_is_still_ordinary_text()
    {
        // Streaming safety, and the reason the delimiter is what makes a table a table: mid-stream the
        // header line arrives one flush before the delimiter, and a layout that flips from paragraph to
        // table and back would flicker on every answer containing one.
        var blocks = AnswerMarkup.Parse("| form | stem |");

        Assert.IsType<AnswerParagraph>(Assert.Single(blocks));
        Assert.Equal("| form | stem |", Rendered("| form | stem |"));
    }

    [Fact]
    public void Prose_around_a_table_stays_prose()
    {
        var blocks = AnswerMarkup.Parse("Word by word:\n" + WordTable + "\nAnd the running translation.");

        Assert.Equal(3, blocks.Count);
        Assert.IsType<AnswerParagraph>(blocks[0]);
        Assert.IsType<AnswerTable>(blocks[1]);
        Assert.IsType<AnswerParagraph>(blocks[2]);
    }

    [Fact]
    public void Cell_contents_are_styled_like_any_other_text()
    {
        var table = Assert.IsType<AnswerTable>(Assert.Single(AnswerMarkup.Parse(
            "| word | note |\n|---|---|\n| **appamādo** | the *first* word |")));

        Assert.Equal(AnswerStyle.Bold, Assert.Single(table.Rows[0][0].Spans).Style);
        Assert.Contains(table.Rows[0][1].Spans, s => s.Text == "first" && s.Style == AnswerStyle.Italic);
    }

    [Theory]
    [InlineData(":---", AnswerAlign.Left)]
    [InlineData("---", AnswerAlign.Left)]
    [InlineData(":---:", AnswerAlign.Center)]
    [InlineData("---:", AnswerAlign.Right)]
    public void Column_alignment_comes_from_the_delimiter_row(string delimiter, AnswerAlign expected)
    {
        var table = Assert.IsType<AnswerTable>(Assert.Single(AnswerMarkup.Parse(
            $"| a |\n| {delimiter} |\n| x |")));

        Assert.Equal(expected, table.Header[0].Align);
        Assert.Equal(expected, table.Rows[0][0].Align);
    }

    [Fact]
    public void A_ragged_row_does_not_lose_the_cells_it_does_have()
    {
        // Models produce these. Dropping the row, or throwing, would lose analysis the reader can still use.
        var table = Assert.IsType<AnswerTable>(Assert.Single(AnswerMarkup.Parse(
            "| a | b | c |\n|---|---|---|\n| one | two |\n| 1 | 2 | 3 |")));

        Assert.Equal(2, table.Rows[0].Count);
        Assert.Equal(3, table.Rows[1].Count);
    }

    [Fact]
    public void A_copied_table_is_aligned_and_still_a_markdown_table()
    {
        // What lands in the reader's document. Columns padded so it reads in a plain-text editor, and the
        // separator kept so it is still a table anywhere that understands Markdown.
        var copied = Rendered(WordTable);

        Assert.Contains("| appamādo   | appamāda  |", copied);
        Assert.Contains("| amatapadaṃ | amatapada |", copied);
        Assert.Contains("|---", copied.Replace(" ", ""));
    }

    [Fact]
    public void A_table_with_no_body_rows_yet_still_renders_its_header()
    {
        // The state between the delimiter arriving and the first row: the header should appear rather than
        // the block vanishing until the model gets round to the data.
        var table = Assert.IsType<AnswerTable>(Assert.Single(AnswerMarkup.Parse("| a | b |\n|---|---|")));

        Assert.Equal(2, table.Header.Count);
        Assert.Empty(table.Rows);
    }

    /// <summary>
    /// Model output is untrusted input, and the renderer is on the UI thread. (R6-3)
    ///
    /// <para><c>AnswerTableView.Apply</c> builds rows×columns controls and rebuilds the whole grid on every
    /// 100 ms streaming flush, so a malfunctioning model emitting a few-hundred-column row freezes the panel
    /// and the app rather than degrading.</para></summary>
    [Fact]
    public void A_runaway_row_is_clamped_rather_than_rendered()
    {
        var wide = "|" + string.Join("|", Enumerable.Repeat("x", 400)) + "|";
        var source = wide + "\n|" + string.Join("|", Enumerable.Repeat("---", 400)) + "|\n" + wide;

        var table = Assert.IsType<AnswerTable>(Assert.Single(AnswerMarkup.Parse(source)));

        Assert.Equal(AnswerMarkup.MaxColumns, table.Header.Count);
        Assert.Equal(AnswerMarkup.MaxColumns, Assert.Single(table.Rows).Count);
    }

    /// <summary>Height is capped the same way, and the overflow rows are still CONSUMED — left unread they
    /// would come back as a wall of pipe characters rendered as prose.</summary>
    [Fact]
    public void A_runaway_row_count_is_clamped_and_not_re_read_as_prose()
    {
        var row = "| a | b |";
        var source = "| h1 | h2 |\n| --- | --- |\n" +
                     string.Join("\n", Enumerable.Repeat(row, AnswerMarkup.MaxRows + 50));

        var blocks = AnswerMarkup.Parse(source);
        var table = Assert.IsType<AnswerTable>(Assert.Single(blocks));

        Assert.Equal(AnswerMarkup.MaxRows, table.Rows.Count);
    }

    /// <summary>An ordinary table is untouched — the caps are far above anything a reader would want, and a
    /// word-by-word gloss is the real shape.</summary>
    [Fact]
    public void An_ordinary_table_is_not_clamped()
    {
        var table = Assert.IsType<AnswerTable>(Assert.Single(AnswerMarkup.Parse(WordTable)));

        Assert.True(table.Header.Count < AnswerMarkup.MaxColumns);
        Assert.True(table.Rows.Count < AnswerMarkup.MaxRows);
    }

    [Fact]
    public void A_line_of_dashes_that_is_not_a_table_is_left_alone()
    {
        // The delimiter test must not fire on ordinary prose punctuation, or a line of em-dashes eats the
        // paragraph after it.
        Assert.Equal("--- a thematic break ---", Rendered("--- a thematic break ---"));
    }
}
