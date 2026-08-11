using System;
using System.Text;

namespace CST.Avalonia.Services.Ai;

/// <summary>
/// The marker a model wraps quoted Pāli in, and the v1 reader that strips it back out. (#582, AI_SURFACE_B.md §9)
///
/// <para><b>Why this ships in v1 when v1 does nothing with it.</b> Answer prose and quoted Pāli are two axes:
/// the user picks the answer language, and separately (from v1.1) the script the Pāli is rendered in. Converting
/// a whole answer would mangle the prose around the quotes, so the model has to tell us which spans are Pāli.
/// v1 renders those spans verbatim in Latin — but the <i>instruction</i> is in the system prompt from day one,
/// because if it arrived in v1.1 the prompt shape would change after #587 had already baselined, and we would
/// have learned nothing about per-model marker discipline in the meantime. That discipline is exactly the
/// evidence that decides whether conversion is safe to enable for a given model tier.</para>
///
/// <para><b>Why <c>[[…]]</c>.</b> Two constraints. It must not occur in the corpus, and it must not occur in
/// ordinary answer prose in any language the user might pick. A survey of all 217 XML books found zero
/// occurrences of <c>[[</c>, <c>]]</c>, <c>«</c>, <c>»</c>, <c>⟦</c>, <c>⟧</c>, <c>{</c> and <c>}</c> — so corpus
/// collision rules none of them out. (The braces the passage reader puts around inline notes are added by the
/// renderer, not present in the source.) Prose collision rules out the guillemets: <c>« »</c> is the standard
/// quotation mark in Russian and French, so a user who sets either as their answer language would have every
/// ordinary quotation read as marked Pāli. The exotic bracket pair <c>⟦ ⟧</c> is collision-free but tests a weak
/// model's Unicode fidelity rather than its instruction-following, which inverts what #587 is trying to measure.
/// <c>[[…]]</c> is emittable by any model, absent from the corpus, and claimed by no language's punctuation.</para>
/// </summary>
public static class PaliQuoteMarkers
{
    public const string Open = "[[";
    public const string Close = "]]";

    /// <summary>
    /// Strip markers from a complete string. For streamed text use <see cref="PaliQuoteFilter"/> instead — a
    /// delta can end between the two brackets, and this would leak the half.
    /// </summary>
    public static string Strip(string text)
    {
        var filter = new PaliQuoteFilter();
        return filter.Feed(text) + filter.Flush();
    }
}

/// <summary>
/// Removes <see cref="PaliQuoteMarkers"/> from streamed answer text, holding back a trailing bracket that could
/// still be the first half of a marker.
///
/// <para><b>Unbalanced markers are stripped anyway, and counted.</b> A model that opens a quote and never closes
/// it — or that doubles or nests the markers, which wikitext-trained models do — should not put a literal
/// <c>[[</c> on screen. That is a visible defect in the answer for a rule the user never asked about. But the
/// imbalance is real signal about that model's marker discipline, so <see cref="UnbalancedMarkers"/> records it
/// for #587 rather than discarding it silently. <see cref="Quotes"/> counts the properly closed ones, which is
/// the other half of the same measurement.</para>
///
/// <para><b>What this deliberately does not do:</b> it does not preserve span boundaries. v1 renders the Pāli
/// verbatim, so it needs only removal; v1.1's converter needs the spans themselves and should read them from the
/// accumulated answer rather than from this filter, because a span can straddle any number of deltas and
/// buffering one to completion would stall the stream mid-sentence.</para>
/// </summary>
public sealed class PaliQuoteFilter
{
    private string _held = string.Empty;
    private bool _inside;

    /// <summary>Properly opened and closed quotes seen so far.</summary>
    public int Quotes { get; private set; }

    /// <summary>Markers that had no partner — an opener never closed, or a closer with nothing open.</summary>
    public int UnbalancedMarkers { get; private set; }

    /// <summary>Feed one delta of answer text; returns what should be rendered.</summary>
    public string Feed(string chunk)
    {
        if (string.IsNullOrEmpty(chunk) && _held.Length == 0) return string.Empty;

        var buffer = _held + chunk;
        _held = string.Empty;

        var output = new StringBuilder();
        var position = 0;

        while (position < buffer.Length)
        {
            var marker = _inside ? PaliQuoteMarkers.Close : PaliQuoteMarkers.Open;

            // A marker that cannot be the one we are hunting for is still a marker, and rendering it is the one
            // outcome worse than losing the span. So whichever comes first wins: outside a quote a stray CLOSER
            // is stripped and counted; inside one a second OPENER is stripped and counted, and the quote stays
            // open. The second case is not hypothetical — [[…]] is wikitext link syntax, so a model that has
            // seen a lot of it will double or nest the markers, and without this "[[[[appamāda]]]]" put a
            // literal "[[" on screen.
            var other = _inside ? PaliQuoteMarkers.Open : PaliQuoteMarkers.Close;
            var mine = buffer.IndexOf(marker, position, StringComparison.Ordinal);
            var theirs = buffer.IndexOf(other, position, StringComparison.Ordinal);
            if (theirs >= 0 && (mine < 0 || theirs < mine))
            {
                output.Append(buffer, position, theirs - position);
                position = theirs + other.Length;
                UnbalancedMarkers++;
                continue;
            }

            if (mine >= 0)
            {
                output.Append(buffer, position, mine - position);
                position = mine + marker.Length;
                if (_inside) Quotes++;
                _inside = !_inside;
                continue;
            }

            // No complete marker ahead. A single trailing bracket could still become one on the next delta, so
            // it waits. Both markers begin with a bracket and end with the same character, so this is one char
            // either way — but it has to be checked against both, or a "]" held while outside would be released
            // as text and its partner never recognised.
            var hold = Math.Max(
                TrailingMarkerPrefix(buffer, position, PaliQuoteMarkers.Open),
                TrailingMarkerPrefix(buffer, position, PaliQuoteMarkers.Close));

            var holdFrom = buffer.Length - hold;
            output.Append(buffer, position, holdFrom - position);
            _held = buffer[holdFrom..];
            position = buffer.Length;
        }

        return output.ToString();
    }

    /// <summary>
    /// End of stream. A held bracket was never going to complete a marker, so it was ordinary text; an
    /// unclosed quote is counted as unbalanced now that no closer can arrive.
    /// </summary>
    public string Flush()
    {
        var tail = _held;
        _held = string.Empty;
        if (_inside)
        {
            UnbalancedMarkers++;
            _inside = false;
        }
        return tail;
    }

    /// <summary>How much of the buffer's tail is a proper prefix of <paramref name="marker"/>.</summary>
    private static int TrailingMarkerPrefix(string buffer, int from, string marker)
    {
        var max = Math.Min(marker.Length - 1, buffer.Length - from);
        for (var length = max; length > 0; length--)
        {
            if (string.CompareOrdinal(buffer, buffer.Length - length, marker, 0, length) == 0)
                return length;
        }
        return 0;
    }
}
