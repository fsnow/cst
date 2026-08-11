namespace CST.Avalonia.Services;

/// <summary>
/// Turns Chromium's stream of find replies into the "3 of 47" a user sees. (#570)
///
/// <para>
/// This exists as its own class because a single reply does not carry both numbers, and getting that wrong
/// is invisible until someone reads the counter carefully. Chromium reports a search across several
/// replies: some know which match is active, a later one knows the authoritative total but says nothing
/// meaningful about the active match. Reading either value straight off the final reply produced "0/47"
/// for a search that had genuinely selected match 1 — which then made Next look as though it skipped to 2.
/// </para>
///
/// <para>
/// It is also pure and UI-free so it can be tested. The logic previously lived inline in the view, where
/// the one part of this feature that had already been wrong once was the only part with no test.
/// </para>
/// </summary>
public class FindResultAccumulator
{
    private int _count;
    private int _ordinal;
    private int _newestIdentifier;
    private bool _hasTotal;

    /// <summary>Total matches, or 0 before any authoritative reply has arrived.</summary>
    public int Count => _count;

    /// <summary>1-based index of the active match, or 0 if not yet reported.</summary>
    public int Ordinal => _ordinal;

    /// <summary>Forget everything. Call when a NEW search starts or the query is cleared.</summary>
    public void Reset()
    {
        _count = 0;
        _ordinal = 0;
        _hasTotal = false;
        // _newestIdentifier is deliberately NOT reset: Chromium's request ids keep climbing across
        // searches, so remembering the high-water mark is what lets a late reply from the PREVIOUS
        // search be recognised and dropped.
    }

    /// <summary>
    /// Folds one reply in. Returns false ONLY when the reply is stale and was discarded — it says nothing
    /// about whether there is yet anything worth displaying, which is <see cref="Format"/>'s business.
    /// </summary>
    public bool Accept(int identifier, int count, int activeMatchOrdinal, bool finalUpdate)
    {
        // Replies from a superseded search are worthless and actively harmful: a late final from search N
        // arriving after search N+1 started would install N's total against N+1's query. Chromium's request
        // identifiers increase, so anything below the newest is stale.
        if (identifier < _newestIdentifier) return false;
        _newestIdentifier = identifier;

        // Ordinal 0 means "this reply says nothing about the active match", NOT "no match is active" — so
        // keep the last real one rather than overwriting with a non-answer.
        if (activeMatchOrdinal > 0) _ordinal = activeMatchOrdinal;

        // The count climbs while Chromium is still scanning, so only the final reply's total can be
        // trusted; rendering the intermediate ones makes the number visibly tick upward as you type.
        if (finalUpdate)
        {
            _count = count;
            _hasTotal = true;
        }

        return true;
    }

    /// <summary>The counter text: "" before anything is known, "0/0" for no matches, else "3/47".</summary>
    public string Format()
    {
        // No authoritative total yet. Showing a partial count here is what makes the number tick visibly
        // upward as the user types.
        if (!_hasTotal) return "";
        if (_count == 0) return "0/0";
        // A total with no ordinal yet means the first match is selected but the reply saying so has not
        // arrived; showing 1 is both true and avoids a visible 0 flashing before it corrects.
        return $"{(_ordinal > 0 ? _ordinal : 1)}/{_count}";
    }
}
