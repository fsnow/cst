using System;
using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Models;

namespace CST.Avalonia.Services;

/// <summary>
/// An immutable, sorted set of dictionary entries for one language, with the lookup semantics ported
/// faithfully from CST4's <c>FormDictionary.Search</c>. Pure and IO-free, so it is directly unit
/// testable; <see cref="DictionaryService"/> owns file loading and IPE normalization.
///
/// <para>Headwords are expected to already be IPE-normalized (see <see cref="DictionaryWord"/>). The
/// list is sorted with <see cref="DictionaryWordComparer"/>, and <see cref="Lookup"/> uses the same
/// ordinal comparison for its binary search — the collation invariant IPE guarantees.</para>
/// </summary>
public sealed class DictionaryIndex
{
    // Sorted ascending by IPE headword (ordinal). Headwords are unique (the loader merges duplicates).
    private readonly List<DictionaryWord> _words;

    /// <summary>
    /// The same entries keyed by their l-folded headword and sorted on that key, consulted only when an
    /// exact lookup misses. (#933)
    ///
    /// <para>A SECOND ordering rather than a changed one: <see cref="_words"/> keeps IPE's collation, every
    /// entry keeps its own headword, and nothing here reaches display. Two entries that fold together stay
    /// two entries.</para>
    /// </summary>
    private readonly List<(string Key, DictionaryWord Word)> _lFolded;

    public DictionaryIndex(IEnumerable<DictionaryWord> words)
    {
        _words = words.ToList();
        _words.Sort(DictionaryWordComparer.Instance);

        _lFolded = _words.Select(w => (Key: FoldL(w.Word), Word: w)).ToList();
        _lFolded.Sort((a, b) => string.CompareOrdinal(a.Key, b.Key));
    }

    /// <summary>
    /// IPE <c>ḷ</c> (U+00E9) read as <c>l</c> (U+00E5), for MATCHING ONLY. (#933)
    ///
    /// <para><b>This is not a claim that they are the same letter.</b> They are distinct phonemes in Pāli.
    /// What it reconciles is two editorial traditions: DPPN follows Malalasekera's PTS spelling, while the
    /// corpus and DPD follow the Burmese/CST one, and the corpus itself disagrees — नाळन्द… occurs 102 times
    /// and नालन्द… 24 times across the 217 files, so four clicks in five on that word landed on the spelling
    /// DPPN does not carry.</para>
    ///
    /// <para>Applied to the query and to the index key alike, so it works in both directions: a query
    /// spelled with <c>ḷ</c> reaches an <c>l</c> headword and the reverse.</para>
    /// </summary>
    internal static string FoldL(string ipe) => ipe.Replace('\u00E9', '\u00E5');

    public int Count => _words.Count;

    /// <summary>
    /// Look up an already-IPE-normalized query and return matching entries in display order.
    ///
    /// <list type="bullet">
    /// <item>Exact hit: the exact entry, followed by every subsequent entry that <c>StartsWith</c> the
    /// query (the prefix run).</item>
    /// <item>Miss: the run of entries that share the longest achievable common prefix with the query —
    /// scanning behind and/or ahead of the insertion point, whichever side(s) tie for the most shared
    /// leading characters (a "best guess").</item>
    /// <item>Empty when the query is empty, the index is empty, or no entry shares even one leading
    /// character with the query.</item>
    /// </list>
    /// </summary>
    public IReadOnlyList<DictionaryWord> Lookup(string ipeWord)
    {
        var results = new List<DictionaryWord>();
        if (string.IsNullOrEmpty(ipeWord) || _words.Count == 0)
            return results;

        int index = BinarySearch(ipeWord);

        // Exact match: add it, then walk forward collecting the StartsWith prefix run.
        if (index >= 0)
        {
            results.Add(_words[index]);
            for (int i = index + 1; i < _words.Count; i++)
            {
                if (_words[i].Word.StartsWith(ipeWord, StringComparison.Ordinal))
                    results.Add(_words[i]);
                else
                    break;
            }
            return results;
        }

        // No exact match: ~index is the insertion point. Compare the neighbors on each side by how many
        // leading characters they share with the query, then collect the run on the winning (or tied) side.
        index = ~index;
        int startIndex = index;

        int commonBehind = 0;
        int commonAhead = 0;
        if (index - 1 >= 0 && index - 1 < _words.Count)
            commonBehind = CountCommonStartLetters(ipeWord, _words[index - 1].Word);
        if (index >= 0 && index < _words.Count)
            commonAhead = CountCommonStartLetters(ipeWord, _words[index].Word);

        // Look behind, collecting the consecutive run tied at commonBehind (pushed to a stack so the
        // results end up in ascending order).
        if (commonBehind >= commonAhead && commonBehind > 0)
        {
            var stack = new Stack<DictionaryWord>();
            for (int i = index - 1; i >= 0 && i < _words.Count; i--)
            {
                if (CountCommonStartLetters(ipeWord, _words[i].Word) == commonBehind)
                    stack.Push(_words[i]);
                else
                    break;
            }
            while (stack.Count > 0)
                results.Add(stack.Pop());
        }

        // Look ahead, collecting the consecutive run tied at commonAhead.
        if (commonAhead >= commonBehind && commonAhead > 0)
        {
            for (int i = startIndex; i < _words.Count; i++)
            {
                if (CountCommonStartLetters(_words[i].Word, ipeWord) == commonAhead)
                    results.Add(_words[i]);
                else
                    break;
            }
        }

        // The guess again with ḷ and l read alike, taken only when it reaches STRICTLY further into the
        // word. (#933)
        //
        // This has to happen on the GUESS, not on the exact search, because the guess is what bridges an
        // inflected form to a headword - and that is where the spelling difference bites. Real case:
        // nāḷandaṃ has no exact entry, shares "nāḷ" (3) with Nāḷika, and shares "nāland" (6) with Nālandā
        // once folded. Folding only the exact arm left the reader on Nāḷika, which is what a lookup already
        // did.
        //
        // Strictly greater, so an exact spelling still wins every tie: folding can only lengthen a common
        // prefix, never shorten one, so anything resolving today keeps the answer it has.
        var folded = FoldGuess(ipeWord, Math.Max(commonBehind, commonAhead));
        return folded ?? results;
    }

    /// <summary>
    /// The best l-folded run, or null when folding gets no closer to the query than
    /// <paramref name="unfoldedBest"/> already did. (#933)
    ///
    /// <para>Scans the folded ordering rather than binary-searching it, because the question is "how far
    /// into the word does the closest entry agree", which is not answered by an insertion point. The set is
    /// one dictionary - 13,548 headwords for DPPN - and this runs only when an exact lookup has already
    /// missed.</para>
    /// </summary>
    private IReadOnlyList<DictionaryWord>? FoldGuess(string ipeWord, int unfoldedBest)
    {
        var key = FoldL(ipeWord);

        int best = 0;
        for (int i = 0; i < _lFolded.Count; i++)
        {
            int common = CountCommonStartLetters(key, _lFolded[i].Key);
            if (common > best) best = common;
        }

        if (best <= unfoldedBest) return null;

        var results = new List<DictionaryWord>();
        for (int i = 0; i < _lFolded.Count; i++)
        {
            if (CountCommonStartLetters(key, _lFolded[i].Key) == best)
                results.Add(_lFolded[i].Word);
        }

        return results;
    }

    /// <summary>
    /// Binary search over the IPE headwords for <paramref name="ipeWord"/>, using the same ordinal
    /// comparison as the sort. Returns the match index, or the bitwise complement of the insertion point
    /// when absent (same contract as <see cref="List{T}.BinarySearch(T)"/>). Keyed on a bare string so no
    /// probe <see cref="DictionaryWord"/> is allocated.
    /// </summary>
    private int BinarySearch(string ipeWord)
    {
        int lo = 0;
        int hi = _words.Count - 1;
        while (lo <= hi)
        {
            int mid = lo + ((hi - lo) >> 1);
            int cmp = string.CompareOrdinal(_words[mid].Word, ipeWord);
            if (cmp == 0)
                return mid;
            if (cmp < 0)
                lo = mid + 1;
            else
                hi = mid - 1;
        }
        return ~lo;
    }

    /// <summary>Length of the common leading-character prefix of two strings. Port of CST4's
    /// <c>CountCommonStartLetters</c>.</summary>
    private static int CountCommonStartLetters(string a, string b)
    {
        if (string.IsNullOrEmpty(a) || string.IsNullOrEmpty(b))
            return 0;

        int shortLen = Math.Min(a.Length, b.Length);
        int i = 0;
        while (i < shortLen && a[i] == b[i])
            i++;
        return i;
    }
}
