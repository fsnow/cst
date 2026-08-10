using System.Collections.Generic;
using System.Linq;
using System.Text;
using CST.Avalonia.Services.Ai;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// Direct tests for the think-tag filter. It is the fiddliest class in the provider layer — a small state
/// machine over arbitrarily-chunked text — and exercising it only through SSE fixtures is both coarse and
/// awkward. Two real defects (a split closing tag, and stale visible-text tracking within a chunk) survived
/// provider-level coverage and would have been caught here. (#578)
/// </summary>
public class ThinkTagFilterTests
{
    /// <summary>Feed chunks in order and return the concatenated channels, flush included.</summary>
    private static (string Visible, string Reasoning) Run(params string[] chunks)
    {
        var filter = new ThinkTagFilter();
        var visible = new StringBuilder();
        var reasoning = new StringBuilder();

        foreach (var chunk in chunks)
        {
            var (v, r) = filter.Feed(chunk);
            visible.Append(v);
            reasoning.Append(r);
        }

        var (tailVisible, tailReasoning) = filter.Flush();
        visible.Append(tailVisible);
        reasoning.Append(tailReasoning);

        return (visible.ToString(), reasoning.ToString());
    }

    /// <summary>Every way of cutting a string into n pieces is a legitimate delta boundary.</summary>
    private static IEnumerable<string[]> EverySplit(string text)
    {
        for (var i = 1; i < text.Length; i++)
            yield return new[] { text[..i], text[i..] };
    }

    [Fact]
    public void Passes_ordinary_text_through_untouched()
    {
        Assert.Equal(("Heedfulness is the path.", ""), Run("Heedfulness ", "is the path."));
    }

    [Fact]
    public void Separates_a_complete_think_block()
    {
        Assert.Equal(("The answer.", "musing"), Run("<think>musing</think>The answer."));
    }

    [Fact]
    public void Separates_correctly_no_matter_where_the_chunks_fall()
    {
        // The whole difficulty of the class: a tag can straddle any boundary.
        const string stream = "before<think>hidden</think>after";
        foreach (var split in EverySplit(stream))
            Assert.Equal(("beforeafter", "hidden"), Run(split));
    }

    [Fact]
    public void A_stream_that_begins_inside_a_block_is_still_separated()
    {
        // Some runner chat templates pre-fill the opening tag into the prompt, so the model's output starts
        // inside the block and only the closing tag is ever streamed.
        Assert.Equal(("The answer.", "musing"), Run("musing</think>The answer."));
    }

    [Fact]
    public void No_tag_or_fragment_ever_reaches_the_answer_however_a_closing_tag_is_split()
    {
        // The regression that provider-level coverage missed: holding back only prefixes of the tag being
        // hunted meant a partial "</thi" was released as answer text, after which "nk>" could never be
        // recognised — so a mangled tag rendered in the middle of the answer.
        //
        // Note what is NOT promised. When the stream begins inside a block, text before the closing tag has
        // already been returned to the caller by the time the tag arrives, so it cannot be reclassified; only
        // the single-delta case (below) gets it right. Buffering to fix that would stall the opening of every
        // normal answer to serve a rare one. The guarantee is narrower and absolute: the tag never renders.
        const string stream = "musing</think>The answer.";
        foreach (var split in EverySplit(stream))
        {
            var (visible, _) = Run(split);
            Assert.DoesNotContain("<", visible);
            Assert.DoesNotContain("thi", visible, System.StringComparison.OrdinalIgnoreCase);
            Assert.EndsWith("The answer.", visible);
        }
    }

    [Fact]
    public void Text_already_emitted_is_not_retroactively_reclassified()
    {
        // Once visible text has been returned it cannot be recalled, so a later stray close tag must not move
        // it — but the tag itself is still suppressed rather than rendered.
        Assert.Equal(("Hello done", ""), Run("Hello ", "done</think>"));
    }

    [Fact]
    public void Text_emitted_earlier_in_the_same_chunk_also_counts_as_emitted()
    {
        // The subtle case: `Hello` and `done` are returned in the SAME delta, so `done` is just as
        // un-retractable as text from a previous chunk and must stay visible.
        Assert.Equal(("Hellodone!", "plan"), Run("Hello<think>plan</think>done</think>!"));
    }

    [Fact]
    public void An_unclosed_block_is_flushed_as_reasoning()
    {
        Assert.Equal(("", "still musing"), Run("<think>still musing"));
    }

    [Fact]
    public void A_held_back_partial_tag_is_released_as_text_at_the_end_of_the_stream()
    {
        Assert.Equal(("a < b and c <th", ""), Run("a < b and c <th"));
    }

    [Fact]
    public void Text_that_only_resembles_a_tag_is_not_swallowed()
    {
        Assert.Equal(("<thinker> and <thing>", ""), Run("<thinker>", " and <thing>"));
    }

    [Fact]
    public void Tag_matching_is_case_insensitive()
    {
        Assert.Equal(("after", "hidden"), Run("<THINK>hidden</Think>after"));
    }

    [Fact]
    public void Repeated_blocks_alternate_correctly()
    {
        Assert.Equal(("ac", "b1b2"), Run("<think>b1</think>a<think>b2</think>c"));
    }

    [Fact]
    public void A_pathological_run_of_angle_brackets_loses_no_characters()
    {
        var chunks = Enumerable.Repeat("<", 50).ToArray();
        var (visible, reasoning) = Run(chunks);

        Assert.Equal(new string('<', 50), visible);
        Assert.Equal("", reasoning);
    }

    [Fact]
    public void Held_text_never_grows_without_bound()
    {
        // Only a possible partial tag may be held, so the buffer is bounded by the longer tag's length.
        var filter = new ThinkTagFilter();
        var emitted = 0;
        for (var i = 0; i < 200; i++)
            emitted += filter.Feed("</thin").Visible.Length;

        var flushed = filter.Flush().Visible.Length;
        Assert.Equal(200 * "</thin".Length, emitted + flushed);
    }
}
