using System;
using System.Collections.Generic;
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
}

public class MatchingTerm
{
    public string Term { get; set; } = string.Empty;  // IPE encoded
    public string DisplayTerm { get; set; } = string.Empty;  // Current script
    public List<BookOccurrence> Occurrences { get; set; } = new();
    public int TotalCount { get; set; }
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