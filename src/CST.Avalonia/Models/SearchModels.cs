using System;
using System.Collections.Generic;
using System.Linq;
using CST;

namespace CST.Avalonia.Models;

public enum SearchMode
{
    Exact,
    Wildcard,
    Regex
}

public class SearchModeItem
{
    public SearchMode Value { get; set; }
    public string DisplayName { get; set; } = string.Empty;

    public SearchModeItem(SearchMode value, string displayName)
    {
        Value = value;
        DisplayName = displayName;
    }
}

public class SearchQuery
{
    public string QueryText { get; set; } = string.Empty;
    public SearchMode Mode { get; set; } = SearchMode.Exact;
    public BookFilter Filter { get; set; } = new BookFilter();
    public int PageSize { get; set; } = 100;
    /// <summary>How many filter-surviving terms to skip before the page (0 = first page). Paging over the
    /// single-term enumeration; the UI leaves it 0.</summary>
    public int Skip { get; set; } = 0;
    /// <summary>Counts-only fast path: when true AND no book filter is applied, the single-term enumeration
    /// takes each term's total-count and book-count straight from the index (no per-term postings reads) and
    /// leaves per-book Occurrences empty. The UI leaves this false (it renders the per-book breakdown).</summary>
    public bool CountsOnly { get; set; } = false;
    public bool IsPhrase { get; set; }
    public bool IsMultiWord { get; set; }
    public int ProximityDistance { get; set; } = 10;
}

public class BookFilter
{
    public bool IncludeVinaya { get; set; } = true;
    public bool IncludeSutta { get; set; } = true;
    public bool IncludeAbhidhamma { get; set; } = true;
    public bool IncludeMula { get; set; } = true;
    public bool IncludeAttha { get; set; } = true;
    public bool IncludeTika { get; set; } = true;
    public bool IncludeOther { get; set; } = true;
    
    // Placeholder for future custom collections
    public string? CustomCollectionName { get; set; }
}

public class SearchResult
{
    public List<MatchingTerm> Terms { get; set; } = new();
    public int TotalTermCount { get; set; }
    public int TotalOccurrenceCount { get; set; }
    public int TotalBookCount { get; set; }
    public string? ContinuationToken { get; set; }
    public TimeSpan SearchDuration { get; set; }

    // True when results were capped (wildcard expansion limit or single-term page size).
    // Surfaced in the UI so users know the result set may be incomplete.
    public bool ResultsTruncated { get; set; }
    public string? TruncationMessage { get; set; }

    // True when at least one more filter-surviving term exists after this page (Skip + PageSize).
    // The precise paging signal (fetch-N+1); an agent pages while this is true.
    public bool HasMore { get; set; }

    // True ONLY when a wildcard/regex overflowed the engine's expansion limit (WildcardExpansionLimit),
    // so some matches were never enumerated. Distinct from ResultsTruncated (which also covers the ordinary
    // "more than one page" case): this means "narrow the pattern", not "page for more". Agent-facing `truncated`.
    public bool ExpansionCapped { get; set; }
}

public class MatchingTerm
{
    public string Term { get; set; } = string.Empty;  // IPE encoded
    public string DisplayTerm { get; set; } = string.Empty;  // Current script
    public List<BookOccurrence> Occurrences { get; set; } = new();
    public int TotalCount { get; set; }

    // Number of books the term occurs in. Set from the index (DocFreq) on the counts-only path where
    // Occurrences is left empty; the postings path leaves it 0 and callers fall back to Occurrences.Count.
    public int BookCount { get; set; }
}

public class BookOccurrence
{
    public Book Book { get; set; } = null!;
    public int Count { get; set; }
    public List<TermPosition> Positions { get; set; } = new();
}

public class TermPosition : IComparable<TermPosition>
{
    public int Position { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public string? Context { get; set; }  // Optional surrounding text

    // For multi-word searches
    public int WordIndex { get; set; }  // Which word in a multi-word search (0-based)
    public int PositionIndex { get; set; }  // Which occurrence of this term at this position
    public bool IsFirstTerm { get; set; }  // True for main search term, false for context words (enables two-color highlighting)
    public string? Word { get; set; }  // The actual word (IPE encoded) - needed when WordPosition outlives the word array

    public int CompareTo(TermPosition? other)
    {
        if (other == null) return 1;
        return Position.CompareTo(other.Position);
    }
}

/// <summary>
/// Shared preparation of hit positions for HIGHLIGHTING.
/// </summary>
public static class HighlightPositions
{
    /// <summary>
    /// Collapse positions that share a source offset, then order them for the highlighter.
    ///
    /// Overlapping multi-word hits SHARE word positions — in "yena yena yena" the proximity windows (1,2) and
    /// (2,3) both contain the middle word, so the flattened hit list holds two entries at the same StartOffset.
    /// The highlighter rewrites the text back-to-front, so a second entry at an offset would delete the markup
    /// the first one just inserted, corrupting the rendered book; the duplicates would also inflate the hit
    /// count that drives "N of M" navigation.
    ///
    /// Where a duplicate exists, the first-term marking wins: IsFirstTerm is what makes a word the navigable
    /// anchor and gives it the primary highlight colour, so losing it to a context-word duplicate would silently
    /// drop a hit.
    /// </summary>
    public static List<TermPosition> Dedupe(IEnumerable<TermPosition> positions)
    {
        var byOffset = new Dictionary<int, TermPosition>();
        foreach (var tp in positions)
        {
            if (!byOffset.TryGetValue(tp.StartOffset, out var existing) || (tp.IsFirstTerm && !existing.IsFirstTerm))
                byOffset[tp.StartOffset] = tp;
        }
        return byOffset.Values.OrderBy(p => p.Position).ToList();
    }
}

// For multi-word searches
public class MatchingMultiWord
{
    public List<string> Terms { get; set; } = new();  // IPE encoded
    public List<string> DisplayTerms { get; set; } = new();  // Current script
    public List<MultiWordBookOccurrence> Occurrences { get; set; } = new();
    public int TotalCount { get; set; }
}

public class MultiWordBookOccurrence
{
    public Book Book { get; set; } = null!;
    public int Count { get; set; }
    public List<MultiWordPosition> Positions { get; set; } = new();
}

public class MultiWordPosition
{
    public Dictionary<string, TermPosition> TermPositions { get; set; } = new();
    public int StartOffset { get; set; }  // Overall start
    public int EndOffset { get; set; }    // Overall end
    public string? Context { get; set; }
}