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

    public DictionaryIndex(IEnumerable<DictionaryWord> words)
    {
        _words = words.ToList();
        _words.Sort(DictionaryWordComparer.Instance);
    }

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
