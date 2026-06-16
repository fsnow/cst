using System;
using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Models;

namespace CST.Avalonia.Search;

/// <summary>
/// A parsed query unit: either a single word or a phrase (an ordered run of words that must
/// appear adjacently in the text). Words may contain wildcard/regex syntax; expansion to
/// concrete index terms happens later (in the Lucene-aware layer), not here.
/// </summary>
public class SearchUnit
{
    public List<string> Words { get; }

    /// <summary>True when this unit is a multi-word phrase (requires internal adjacency).</summary>
    public bool IsPhrase => Words.Count > 1;

    public SearchUnit(IEnumerable<string> words)
    {
        Words = words.ToList();
    }
}

/// <summary>
/// One matched occurrence of a single unit within one book: the anchor (first word) position
/// plus all of the member word positions, retained for highlighting.
/// </summary>
public class UnitOccurrence
{
    public int AnchorPosition { get; init; }
    public List<TermPosition> Members { get; init; } = new();
}

/// <summary>
/// Pure, UI- and Lucene-free multi-word / phrase search logic. Parsing turns a query string into
/// units; matching turns per-unit candidate positions into hits. This is deliberately independent
/// of the Lucene index and the <c>Books</c> singleton so it can be unit-tested directly.
///
/// Query model (richer than CST4, which had a single phrase flag applied to the whole query and
/// stripped all quotes — so it could neither mix a phrase with loose words nor express two
/// separate phrases):
///   - a query is a sequence of <see cref="SearchUnit"/>s; each unit is a word or a quoted phrase
///   - within a phrase: strict adjacency, in query order (word slot k sits at anchor + k)
///   - between units: an all-within-a-window proximity test, with the window measured between
///     unit anchors (each phrase anchored at its first word). This window semantic is also
///     intentionally stricter than CST4, which measured each context word's distance from the
///     first word only.
/// </summary>
public static class MultiWordSearch
{
    /// <summary>
    /// Parse an (already IPE-converted) query string into units, honoring quoted phrases.
    /// Examples: <c>a b c</c> => three word units; <c>"a b" c</c> => phrase(a,b) + word(c);
    /// <c>"a b" "c d"</c> => two phrase units. An unbalanced trailing quote closes at end of input.
    /// </summary>
    public static List<SearchUnit> ParseUnits(string ipeQuery)
    {
        var units = new List<SearchUnit>();
        if (string.IsNullOrWhiteSpace(ipeQuery))
            return units;

        int i = 0;
        int n = ipeQuery.Length;
        while (i < n)
        {
            while (i < n && char.IsWhiteSpace(ipeQuery[i])) i++;
            if (i >= n) break;

            if (ipeQuery[i] == '"')
            {
                i++; // consume opening quote
                int start = i;
                while (i < n && ipeQuery[i] != '"') i++;
                var inner = ipeQuery.Substring(start, i - start);
                if (i < n) i++; // consume closing quote
                var words = inner.Split(' ', StringSplitOptions.RemoveEmptyEntries);
                if (words.Length > 0)
                    units.Add(new SearchUnit(words));
            }
            else
            {
                int start = i;
                while (i < n && !char.IsWhiteSpace(ipeQuery[i]) && ipeQuery[i] != '"') i++;
                var word = ipeQuery.Substring(start, i - start);
                if (word.Length > 0)
                    units.Add(new SearchUnit(new[] { word }));
            }
        }
        return units;
    }

    /// <summary>
    /// Find occurrences of one unit within a book given the positions of each of its word slots.
    /// <paramref name="wordSlots"/>[k] holds every position where any expansion of the unit's
    /// k-th word occurs (each list sorted by position). A single-word unit makes every position an
    /// occurrence; a phrase requires slot k to sit at anchor + k (strict adjacency, query order).
    /// </summary>
    public static List<UnitOccurrence> FindUnitOccurrences(List<TermPosition>[] wordSlots)
    {
        var result = new List<UnitOccurrence>();
        if (wordSlots.Length == 0)
            return result;

        if (wordSlots.Length == 1)
        {
            foreach (var tp in wordSlots[0])
                result.Add(new UnitOccurrence { AnchorPosition = tp.Position, Members = new List<TermPosition> { tp } });
            return result;
        }

        int k = wordSlots.Length;

        // position -> TermPosition for slots 1..k-1 (first occurrence at a position wins)
        var maps = new Dictionary<int, TermPosition>[k];
        for (int s = 1; s < k; s++)
        {
            maps[s] = new Dictionary<int, TermPosition>();
            foreach (var tp in wordSlots[s])
                if (!maps[s].ContainsKey(tp.Position))
                    maps[s][tp.Position] = tp;
        }

        foreach (var tp0 in wordSlots[0])
        {
            var members = new List<TermPosition>(k) { tp0 };
            bool ok = true;
            for (int s = 1; s < k; s++)
            {
                if (maps[s].TryGetValue(tp0.Position + s, out var tps))
                    members.Add(tps);
                else { ok = false; break; }
            }
            if (ok)
                result.Add(new UnitOccurrence { AnchorPosition = tp0.Position, Members = members });
        }
        return result;
    }

    /// <summary>
    /// Combine per-unit occurrences into hits. A single unit yields its occurrences directly
    /// (so a lone phrase or word just returns its matches). Multiple units must co-occur within
    /// <paramref name="window"/> positions of each other, measured between unit anchors
    /// (all-within-a-window). Each hit returns the member positions of every matched unit, with
    /// unit 0 tagged as the first term (blue) and the rest as context (green).
    /// </summary>
    public static List<List<TermPosition>> FindHits(List<List<UnitOccurrence>> unitOccurrences, int window)
    {
        var hits = new List<List<TermPosition>>();
        int u = unitOccurrences.Count;
        if (u == 0 || unitOccurrences.Any(o => o.Count == 0))
            return hits;

        if (u == 1)
        {
            foreach (var occ in unitOccurrences[0])
                hits.Add(BuildHit(new[] { occ }));
            return hits;
        }

        if (window < 1) window = 1;

        // Merge all occurrences, tagged by unit index, sorted by anchor position.
        var merged = new List<(int anchor, int unit, UnitOccurrence occ)>();
        for (int ui = 0; ui < u; ui++)
            foreach (var occ in unitOccurrences[ui])
                merged.Add((occ.AnchorPosition, ui, occ));
        merged.Sort((a, b) => a.anchor != b.anchor ? a.anchor.CompareTo(b.anchor) : a.unit.CompareTo(b.unit));

        var have = new int[u];
        int covered = 0;
        int lo = 0;
        for (int hi = 0; hi < merged.Count; hi++)
        {
            if (have[merged[hi].unit]++ == 0)
                covered++;

            if (covered == u)
            {
                // Shrink from the left to the minimal window still covering all units.
                while (have[merged[lo].unit] > 1)
                {
                    have[merged[lo].unit]--;
                    lo++;
                }

                if (merged[hi].anchor - merged[lo].anchor <= window)
                {
                    // Pick the first occurrence of each unit within [lo..hi].
                    var picked = new UnitOccurrence[u];
                    int need = u;
                    for (int x = lo; x <= hi && need > 0; x++)
                    {
                        int t = merged[x].unit;
                        if (picked[t] == null) { picked[t] = merged[x].occ; need--; }
                    }

                    hits.Add(BuildHit(picked));
                }

                // Advance past this window's start to look for the next minimal window.
                have[merged[lo].unit]--;
                covered--;
                lo++;
            }
        }
        return hits;
    }

    /// <summary>
    /// Assemble the member positions of one hit (units in query order). Exactly one position is
    /// the navigable anchor (IsFirstTerm = true → blue, gets a hit id, counted in the hit total):
    /// the first word of the first query unit. Every other matched word is context (IsFirstTerm =
    /// false → green, highlighted but not a separate navigation stop). This keeps one hit per
    /// match occurrence, so Next/Prev steps occurrence-by-occurrence and the in-book hit count
    /// equals the number of matches rather than the number of highlighted words.
    /// </summary>
    private static List<TermPosition> BuildHit(IReadOnlyList<UnitOccurrence> unitsInHit)
    {
        var members = new List<TermPosition>();
        for (int ui = 0; ui < unitsInHit.Count; ui++)
            foreach (var m in unitsInHit[ui].Members)
            {
                members.Add(new TermPosition
                {
                    Position = m.Position,
                    StartOffset = m.StartOffset,
                    EndOffset = m.EndOffset,
                    WordIndex = ui,
                    IsFirstTerm = false,
                    Word = m.Word
                });
            }

        if (members.Count > 0)
            members[0].IsFirstTerm = true; // first word of the first unit = the single navigable anchor

        return members;
    }
}
