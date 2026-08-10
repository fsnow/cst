using System;
using System.Text;

namespace CST.Avalonia.Services.Ai;

/// <summary>
/// Splits inline <c>&lt;think&gt;…&lt;/think&gt;</c> reasoning out of a streamed content channel.
///
/// <para>Some reasoning models served over the OpenAI-compatible shape do not use the structured
/// <c>reasoning_content</c> field — they emit their reasoning inline, wrapped in think-tags, in the same
/// <c>content</c> the answer arrives in. Without this the tags and everything between them render as answer text.</para>
///
/// <para><b>Tags split across chunk boundaries are the whole difficulty.</b> A delta can end mid-tag —
/// <c>"…done &lt;thi"</c> — so a naive per-chunk <c>Replace</c> both misses the tag and leaks its first half to
/// the user. This holds back any trailing run that could still become a tag and re-examines it once more text
/// arrives. <see cref="Flush"/> releases a held-back run that turned out to be ordinary text at end of stream.</para>
///
/// <para><b>What is guaranteed, and what is not.</b> Guaranteed: no tag, and no fragment of one, ever reaches
/// the answer channel — on any chunking. NOT guaranteed: correct classification of text that precedes a closing
/// tag when the stream <i>began</i> inside a block. That text has already been returned to the caller by the
/// time the tag arrives, so it cannot be recalled; only when both fall in the same delta is it classified as
/// reasoning. Fixing that properly would mean withholding output until a tag appears or the stream ends, which
/// stalls the opening of every ordinary answer to serve a rare case. A literal <c>&lt;/think&gt;</c> written
/// deliberately in answer prose is swallowed for the same reason — an accepted cost of the heuristic.</para>
/// </summary>
internal sealed class ThinkTagFilter
{
    private const string Open = "<think>";
    private const string Close = "</think>";

    private bool _inside;
    private string _held = string.Empty;
    private bool _emittedVisible;

    /// <summary>Feed one content delta. Returns the text to show and the text to treat as reasoning.</summary>
    internal (string Visible, string Reasoning) Feed(string chunk)
    {
        var buffer = _held + chunk;
        _held = string.Empty;

        var visible = new StringBuilder();
        var reasoning = new StringBuilder();
        var position = 0;

        while (position < buffer.Length)
        {
            var tag = _inside ? Close : Open;
            var sink = _inside ? reasoning : visible;

            // A close tag arriving while we are NOT inside means the stream began mid-reasoning: some runner
            // chat templates pre-fill the opening <think> into the prompt, so the model's output starts inside
            // the block and only the closing tag is ever streamed. Without this the whole reasoning renders as
            // the answer, followed by a literal </think> — the exact failure this filter exists to prevent.
            if (!_inside)
            {
                var open = buffer.IndexOf(Open, position, StringComparison.OrdinalIgnoreCase);
                var stray = buffer.IndexOf(Close, position, StringComparison.OrdinalIgnoreCase);
                if (stray >= 0 && (open < 0 || stray < open))
                {
                    // Text before it is reasoning if nothing has reached the user yet. Once visible text has
                    // been emitted we cannot retract it — but we can still refuse to render the tag itself.
                    // `visible.Length` matters as much as the flag: text appended earlier in THIS call is
                    // returned in the same delta, so it is just as un-retractable as text from a prior one.
                    var emitted = _emittedVisible || visible.Length > 0;
                    (emitted ? visible : reasoning).Append(buffer, position, stray - position);
                    position = stray + Close.Length;
                    continue;
                }
            }

            var hit = buffer.IndexOf(tag, position, StringComparison.OrdinalIgnoreCase);
            if (hit >= 0)
            {
                sink.Append(buffer, position, hit - position);
                position = hit + tag.Length;
                _inside = !_inside;
                continue;
            }

            // No complete tag ahead. Anything that could still GROW into one has to wait for the next chunk.
            //
            // While OUTSIDE a block, BOTH tags matter. Holding back only prefixes of the tag we are hunting for
            // is what let a split closing tag through: a stream that begins mid-reasoning ends "…options</thi",
            // and if that tail is released as answer text the next chunk's "nk>" can never be recognised — the
            // reasoning and the mangled tag both render as the answer, which is the failure this class exists
            // to prevent, merely relocated to a chunk boundary.
            var hold = _inside
                ? LongestTagPrefixSuffix(buffer, position, Close)
                : Math.Max(
                    LongestTagPrefixSuffix(buffer, position, Open),
                    LongestTagPrefixSuffix(buffer, position, Close));

            var rest = buffer.Length - position;
            var holdFrom = buffer.Length - hold;
            sink.Append(buffer, position, holdFrom - position);
            _held = buffer[holdFrom..];
            position += rest;
        }

        if (visible.Length > 0) _emittedVisible = true;
        return (visible.ToString(), reasoning.ToString());
    }

    /// <summary>
    /// End of stream: whatever is still held back was never going to complete a tag, so it is ordinary text.
    /// Attributed to whichever channel was open at the time.
    /// </summary>
    internal (string Visible, string Reasoning) Flush()
    {
        var tail = _held;
        _held = string.Empty;
        if (tail.Length == 0) return (string.Empty, string.Empty);
        return _inside ? (string.Empty, tail) : (tail, string.Empty);
    }

    /// <summary>
    /// Length of the longest suffix of <paramref name="buffer"/> (at or after <paramref name="from"/>) that is a
    /// proper prefix of <paramref name="tag"/> — i.e. how much text must be held back as a possible partial tag.
    /// </summary>
    private static int LongestTagPrefixSuffix(string buffer, int from, string tag)
    {
        var max = Math.Min(tag.Length - 1, buffer.Length - from);
        for (var length = max; length > 0; length--)
        {
            if (string.Compare(buffer, buffer.Length - length, tag, 0, length, StringComparison.OrdinalIgnoreCase) == 0)
                return length;
        }
        return 0;
    }
}
