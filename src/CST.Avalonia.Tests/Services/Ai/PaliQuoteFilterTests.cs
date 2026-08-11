using System.Linq;
using System.Text;
using CST.Avalonia.Services.Ai;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// The Pāli-quote marker and its v1 reader. (#582)
///
/// <para>The marker is instructed from v1 and only stripped from v1 — so what has to hold now is that no
/// fragment of it ever reaches the panel, on any chunking, and that marker discipline is COUNTED rather than
/// quietly repaired, since those counts are what #587 uses to decide whether script conversion is safe to turn
/// on for a given model.</para>
/// </summary>
public class PaliQuoteFilterTests
{
    /// <summary>Feed a string one character at a time — the worst chunking a provider can produce.</summary>
    private static (string Text, PaliQuoteFilter Filter) FeedByCharacter(string input)
    {
        var filter = new PaliQuoteFilter();
        var output = new StringBuilder();
        foreach (var c in input) output.Append(filter.Feed(c.ToString()));
        output.Append(filter.Flush());
        return (output.ToString(), filter);
    }

    [Fact]
    public void Strips_markers_and_keeps_what_they_wrapped()
    {
        Assert.Equal(
            "the phrase appamādo amatapadaṃ opens the chapter",
            PaliQuoteMarkers.Strip("the phrase [[appamādo amatapadaṃ]] opens the chapter"));
    }

    [Fact]
    public void A_marker_split_across_deltas_never_leaks_a_bracket()
    {
        // The whole difficulty: a delta can end between the two brackets. A naive per-chunk Replace both misses
        // the marker and puts its first half on screen.
        var filter = new PaliQuoteFilter();
        var output = filter.Feed("the phrase [") + filter.Feed("[appamādo]") + filter.Feed("] opens it");
        output += filter.Flush();

        Assert.Equal("the phrase appamādo opens it", output);
        Assert.Equal(1, filter.Quotes);
        Assert.Equal(0, filter.UnbalancedMarkers);
    }

    [Fact]
    public void Character_by_character_streaming_produces_the_same_text()
    {
        var (text, filter) = FeedByCharacter("a [[eko]] b [[dve]] c");

        Assert.Equal("a eko b dve c", text);
        Assert.Equal(2, filter.Quotes);
    }

    [Fact]
    public void An_unclosed_quote_is_stripped_anyway_and_counted()
    {
        // Leaving a literal "[[" on screen is a visible defect in the answer, for a rule the user never asked
        // about — so it goes. The imbalance is real signal about the model, so it is recorded rather than lost.
        var (text, filter) = FeedByCharacter("he said [[appamādo and stopped");

        Assert.Equal("he said appamādo and stopped", text);
        Assert.Equal(0, filter.Quotes);
        Assert.Equal(1, filter.UnbalancedMarkers);
    }

    [Fact]
    public void A_stray_closer_is_stripped_anyway_and_counted()
    {
        var (text, filter) = FeedByCharacter("nothing opened ]] here");

        Assert.Equal("nothing opened  here", text);
        Assert.Equal(1, filter.UnbalancedMarkers);
    }

    [Fact]
    public void A_single_bracket_in_ordinary_prose_survives()
    {
        // Held back while it might grow into a marker, then released as the ordinary text it was.
        var (text, _) = FeedByCharacter("see note [3] and [4]");

        Assert.Equal("see note [3] and [4]", text);
    }

    [Fact]
    public void A_stream_severed_mid_marker_flushes_the_fragment_rather_than_swallowing_it()
    {
        var filter = new PaliQuoteFilter();
        var output = filter.Feed("truncated [");
        output += filter.Flush();

        Assert.Equal("truncated [", output);
    }

    [Fact]
    public void Text_with_no_markers_passes_through_unchanged()
    {
        var (text, filter) = FeedByCharacter("an ordinary answer with no Pali quoted at all");

        Assert.Equal("an ordinary answer with no Pali quoted at all", text);
        Assert.Equal(0, filter.Quotes);
        Assert.Equal(0, filter.UnbalancedMarkers);
    }

    [Fact]
    public void The_marker_does_not_occur_in_the_system_prompt_it_instructs()
    {
        // The system prompt has to SHOW the marker to teach it, which means the prompt is itself a place the
        // marker appears. Guard the one thing that would actually break: the markers must arrive via the
        // {{paliOpen}}/{{paliClose}} placeholders, so the constant and the instruction cannot drift apart.
        var store = new PromptTemplateStore(
            System.IO.Path.Combine(System.IO.Path.GetTempPath(), "cst-no-such-dir"),
            Microsoft.Extensions.Logging.Abstractions.NullLogger<PromptTemplateStore>.Instance);

        var text = store.GetDefault(PromptTemplateNames.System);

        Assert.DoesNotContain(PaliQuoteMarkers.Open, text);
        Assert.Contains("{{" + PromptPlaceholders.PaliOpen + "}}", text);
        Assert.Contains("{{" + PromptPlaceholders.PaliClose + "}}", text);
    }
}
