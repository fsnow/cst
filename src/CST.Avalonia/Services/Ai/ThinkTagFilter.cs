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
/// </summary>
internal sealed class ThinkTagFilter
{
    private const string Open = "<think>";
    private const string Close = "</think>";

    private bool _inside;
    private string _held = string.Empty;

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

            var hit = buffer.IndexOf(tag, position, StringComparison.OrdinalIgnoreCase);
            if (hit >= 0)
            {
                sink.Append(buffer, position, hit - position);
                position = hit + tag.Length;
                _inside = !_inside;
                continue;
            }

            // No complete tag ahead. Anything that could still GROW into one has to wait for the next chunk.
            var rest = buffer.Length - position;
            var holdFrom = buffer.Length - LongestTagPrefixSuffix(buffer, position, tag);
            sink.Append(buffer, position, holdFrom - position);
            _held = buffer[holdFrom..];
            position += rest;
        }

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
