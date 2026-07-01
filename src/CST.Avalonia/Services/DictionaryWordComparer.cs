using System;
using System.Collections.Generic;
using CST.Avalonia.Models;

namespace CST.Avalonia.Services;

/// <summary>
/// Ordinal, char-by-char comparison of IPE headwords with a length tiebreak.
///
/// <para>IPE is designed so that codepoint order equals Pāli alphabetical order, so this trivial
/// comparison collates Pāli correctly with no culture rules or collation table. It is the sign
/// equivalent of CST4's hand-written <c>DictionaryWordComparer</c> (compare chars; on the first
/// difference return its sign; otherwise the shorter string sorts first) and of
/// <see cref="string.CompareOrdinal(string, string)"/>.</para>
///
/// <para>The SAME comparer must drive both the sort and the binary search. Never substitute a
/// culture-aware comparer (e.g. <c>StringComparer.CurrentCulture</c>) — it would reorder IPE and break
/// the binary search.</para>
/// </summary>
public sealed class DictionaryWordComparer : IComparer<DictionaryWord>, IComparer<string>
{
    public static readonly DictionaryWordComparer Instance = new();

    public int Compare(DictionaryWord? x, DictionaryWord? y)
        => string.CompareOrdinal(x?.Word, y?.Word);

    public int Compare(string? x, string? y)
        => string.CompareOrdinal(x, y);
}
