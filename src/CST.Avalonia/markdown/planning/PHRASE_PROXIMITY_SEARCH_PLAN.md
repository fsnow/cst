# Phrase and Proximity Search Implementation Plan

**Date**: October 13, 2025
**Feature**: Advanced multi-word search with phrase and proximity matching
**Status**: POC Complete - Ready for Implementation

## Overview

This document outlines the implementation plan for phrase and proximity search features in CST Reader. This feature will enable users to:
1. Search for exact phrases using quotes (e.g., `"evaṃ me"`)
2. Search for terms within a specified word distance (e.g., `rājagahaṃ nāḷandaṃ` within 10 words)
3. Use wildcards in multi-word searches (e.g., `bhikkhu* saṅgha*`)
4. View results with two-color highlighting (yellow for first term, blue for context terms)

## POC Validation Status ✅

All Lucene.NET 4.8+ APIs have been verified through comprehensive POC tests:

- ✅ **POC_01**: Single term position retrieval (`MultiFields.GetTermPositionsEnum`)
- ✅ **POC_02**: Two-term proximity checking with distance calculation
- ✅ **POC_03**: Exact phrase matching with adjacent positions
- ✅ **POC_04**: Wildcard expansion and position retrieval
- ✅ **POC_05**: Combined wildcard + proximity search

**Test File**: `CST.Avalonia.Tests/Services/PhraseProximityPOCTests.cs`
**Test Results**: 5/5 passing (100%)

## Phase 1: SearchService Backend (Core Algorithm)

### 1.1 New Method: SearchMultiWordAsync

**Location**: `Services/SearchService.cs`

**Purpose**: Handle multi-word searches with phrase or proximity matching

**Algorithm** (based on CST4's `GetMatchingTermsWithContext` and POC tests):

```
1. Parse query into words (split on whitespace)
2. Expand wildcards for each word → List<string>[] expandedTerms
3. Filter books: Only keep books containing ALL terms (from at least one expansion)
4. For each book with all terms:
   a. Get DocsAndPositionsEnum for each term
   b. Find proximity/phrase matches:
      - Phrase: term2 at exactly position (term1 + 1)
      - Proximity: term2 within ±maxDistance of term1
   c. Build TermPosition results with:
      - WordIndex (0-based, which word in query)
      - PositionIndex (which occurrence)
      - IsFirstTerm (true for first word, false for others)
      - Word (IPE-encoded term)
5. Return results grouped by book
```

**Signature**:
```csharp
/// <summary>
/// Searches for multiple words with phrase or proximity matching.
/// </summary>
/// <param name="query">Search query with QueryText, IsPhrase, ProximityDistance</param>
/// <param name="cancellationToken">Cancellation token</param>
/// <returns>Search results with unique word pairs and multi-color highlighting data</returns>
public async Task<SearchResult> SearchMultiWordAsync(
    SearchQuery query,
    CancellationToken cancellationToken = default)
{
    // Implementation based on CST4 GetMatchingTermsWithContext
    // See: devdocs/cst4/CST4_SEARCH.md lines 118-361
}
```

### 1.2 Helper Method: ExpandWildcard

**Purpose**: Convert wildcard pattern to list of matching terms from index

**Implementation** (validated in POC_04):
```csharp
/// <summary>
/// Expands a wildcard pattern (e.g., "bhikkhu*") to matching terms in the index.
/// </summary>
/// <param name="pattern">Wildcard pattern (* = any chars, ? = single char)</param>
/// <param name="maxResults">Maximum number of terms to return (default 100)</param>
/// <returns>List of matching IPE-encoded terms</returns>
private List<string> ExpandWildcard(string pattern, int maxResults = 100)
{
    // Convert wildcard to regex: "bhikkhu*" → "^bhikkhu.*$"
    var regexPattern = "^" + Regex.Escape(pattern)
        .Replace("\\*", ".*")
        .Replace("\\?", ".") + "$";
    var regex = new Regex(regexPattern);

    // Enumerate terms in index, filter by regex
    var fields = MultiFields.GetFields(_indexReader);
    var terms = fields.GetTerms("text");
    var termsEnum = terms.GetEnumerator();

    var results = new List<string>();
    while (termsEnum.MoveNext() && results.Count < maxResults)
    {
        var term = termsEnum.Term.Utf8ToString();
        if (regex.IsMatch(term))
            results.Add(term);
    }
    return results;
}
```

### 1.3 Helper Method: FindProximityMatches

**Purpose**: Given two term's positions, find matching pairs within distance

**Implementation** (validated in POC_02):
```csharp
/// <summary>
/// Finds positions where two terms appear within specified distance.
/// </summary>
/// <param name="positions1">Positions of first term</param>
/// <param name="positions2">Positions of second term</param>
/// <param name="maxDistance">Maximum word distance (e.g., 10)</param>
/// <returns>List of matching position pairs with distances</returns>
private List<(int pos1, int pos2, int distance)> FindProximityMatches(
    List<int> positions1,
    List<int> positions2,
    int maxDistance)
{
    var matches = new List<(int, int, int)>();
    foreach (var pos1 in positions1)
    {
        foreach (var pos2 in positions2)
        {
            int distance = Math.Abs(pos1 - pos2);
            if (distance <= maxDistance && distance > 0)
            {
                matches.Add((pos1, pos2, distance));
            }
        }
    }
    return matches;
}
```

### 1.4 Helper Method: FindPhraseMatches

**Purpose**: Check for exact adjacent word order

**Implementation** (validated in POC_03):
```csharp
/// <summary>
/// Finds positions where term2 appears exactly after term1 (adjacent words).
/// </summary>
/// <param name="positions1">Positions of first term</param>
/// <param name="positions2">Positions of second term</param>
/// <returns>List of matching adjacent position pairs</returns>
private List<(int pos1, int pos2)> FindPhraseMatches(
    List<int> positions1,
    List<int> positions2)
{
    var positions2Set = new HashSet<int>(positions2);
    var matches = new List<(int, int)>();

    foreach (var pos1 in positions1)
    {
        // For phrase search, term2 must be exactly at position (term1 + 1)
        if (positions2Set.Contains(pos1 + 1))
        {
            matches.Add((pos1, pos1 + 1));
        }
    }
    return matches;
}
```

### 1.5 Data Models

**Already Complete** - Enhanced `TermPosition` class in `Models/SearchModels.cs` (lines 76-94):

```csharp
public class TermPosition : IComparable<TermPosition>
{
    public int Position { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
    public string? Context { get; set; }

    // For multi-word searches
    public int WordIndex { get; set; }  // Which word in a multi-word search (0-based)
    public int PositionIndex { get; set; }  // Which occurrence of this term at this position
    public bool IsFirstTerm { get; set; }  // True for main search term, false for context words
    public string? Word { get; set; }  // The actual word (IPE encoded)

    public int CompareTo(TermPosition? other)
    {
        if (other == null) return 1;
        return Position.CompareTo(other.Position);
    }
}
```

**Already Complete** - `SearchQuery.ProximityDistance` property (line 34):
```csharp
public int ProximityDistance { get; set; } = 10;
```

## Phase 2: UI Controls

### 2.1 SearchPanel.axaml Updates

**Location**: `Views/SearchPanel.axaml`

**Add Controls**:

1. **Word Distance Slider** (for proximity search):
   - Label: "Word Distance: {value}"
   - Range: 1-50, Default: 10
   - Only enabled when multi-word query without quotes

2. **Phrase Search Detection**:
   - Automatically detect quotes in query text
   - When quotes detected: `IsPhrase = true`, hide slider
   - When no quotes: `IsPhrase = false`, show slider

**XAML Addition**:
```xml
<!-- Add after existing search controls -->
<StackPanel Orientation="Horizontal" Margin="0,5,0,0"
            IsVisible="{Binding IsProximitySearchEnabled}">
    <TextBlock Text="Word Distance:"
               VerticalAlignment="Center"
               Margin="0,0,10,0"/>
    <Slider Name="ProximitySlider"
            Minimum="1"
            Maximum="50"
            Value="{Binding ProximityDistance}"
            Width="200"
            TickFrequency="5"
            TickPlacement="BottomRight"/>
    <TextBlock Text="{Binding ProximityDistance}"
               VerticalAlignment="Center"
               Margin="10,0,0,0"
               MinWidth="30"/>
</StackPanel>

<!-- Add info text for phrase search -->
<TextBlock Text="Phrase search: exact word order"
           IsVisible="{Binding IsPhraseSearch}"
           FontStyle="Italic"
           Foreground="Gray"
           Margin="0,5,0,0"/>
```

### 2.2 SearchViewModel Updates

**Location**: `ViewModels/SearchViewModel.cs`

**Add Properties**:
```csharp
private int _proximityDistance = 10;
public int ProximityDistance
{
    get => _proximityDistance;
    set => this.RaiseAndSetIfChanged(ref _proximityDistance, value);
}

public bool IsProximitySearchEnabled => !IsPhraseSearch && IsMultiWord;
public bool IsPhraseSearch => SearchText?.Contains("\"") ?? false;
public bool IsMultiWord => (SearchText?.Trim().Split(' ').Length ?? 0) > 1;
```

**Update Search Logic**:
```csharp
private async Task PerformSearch()
{
    var queryText = SearchText.Trim();
    var isPhrase = queryText.Contains("\"");

    // Remove quotes for phrase search
    if (isPhrase)
        queryText = queryText.Replace("\"", "");

    var query = new SearchQuery
    {
        QueryText = queryText,
        Mode = SelectedSearchMode.Value,
        IsPhrase = isPhrase,
        IsMultiWord = queryText.Split(' ').Length > 1,
        ProximityDistance = ProximityDistance,
        Filter = BuildBookFilter(),
        PageSize = 100
    };

    // Route to appropriate search method
    var result = query.IsMultiWord
        ? await _searchService.SearchMultiWordAsync(query, _cancellationTokenSource.Token)
        : await _searchService.SearchAsync(query, _cancellationTokenSource.Token);

    // Update UI with results
    DisplaySearchResults(result);
}
```

**Add Property Change Notifications**:
```csharp
// In constructor or property setters
this.WhenAnyValue(x => x.SearchText)
    .Subscribe(_ =>
    {
        this.RaisePropertyChanged(nameof(IsProximitySearchEnabled));
        this.RaisePropertyChanged(nameof(IsPhraseSearch));
        this.RaisePropertyChanged(nameof(IsMultiWord));
    });
```

## Phase 3: Multi-Color Highlighting

### 3.1 XSLT Updates

**Location**: `Resources/book-view.xsl` (or wherever XSLT templates are)

**Current Highlighting** (from CST4 reference):
```xml
<xsl:template name="highlightSearchTerm">
    <xsl:param name="text"/>
    <xsl:param name="positions"/>

    <xsl:for-each select="$positions">
        <xsl:choose>
            <xsl:when test="@isFirstTerm = 'true'">
                <hi rend="hit">
                    <xsl:value-of select="substring($text, @startOffset, @endOffset - @startOffset)"/>
                </hi>
            </xsl:when>
            <xsl:otherwise>
                <hi rend="context">
                    <xsl:value-of select="substring($text, @startOffset, @endOffset - @startOffset)"/>
                </hi>
            </xsl:otherwise>
        </xsl:choose>
    </xsl:for-each>
</xsl:template>
```

### 3.2 CSS Styles

**Location**: Embedded CSS in XSLT or separate stylesheet

```css
/* First term highlighting (main search term) */
hi[rend="hit"] {
    background-color: yellow;
    font-weight: bold;
    color: black;
}

/* Context term highlighting (proximity/phrase context) */
hi[rend="context"] {
    background-color: lightblue;
    color: black;
}
```

### 3.3 TermPosition Usage

The `IsFirstTerm` flag in `TermPosition` will be used by XSLT to choose which style:
- `IsFirstTerm = true` → `<hi rend="hit">` (yellow background)
- `IsFirstTerm = false` → `<hi rend="context">` (blue background)

## Phase 4: Testing

### 4.1 Manual Testing Scenarios

#### Test 1: Simple Phrase Search
- **Query**: `"evaṃ me"` (with quotes)
- **Expected**: Only find exact adjacent matches where "evaṃ" is followed by "me"
- **Verify**:
  - Two-color highlighting (yellow for "evaṃ", blue for "me")
  - No matches where words are separated
  - Accurate occurrence counts

#### Test 2: Basic Proximity Search
- **Query**: `rājagahaṃ nāḷandaṃ` (no quotes)
- **Distance**: 10 words (default)
- **Expected**: Find all occurrences within 10 positions
- **Verify**:
  - Two-color highlighting for both terms
  - Matches include both forward and reverse order
  - Distance calculation is correct

#### Test 3: Wildcard Proximity Search
- **Query**: `bhikkhu* saṅgha*`
- **Distance**: 5 words
- **Expected**: All combinations of expanded terms within distance
- **Verify**:
  - Wildcard expansion works correctly
  - Unique word pairs (no duplicates in results list)
  - Each pair shown only once regardless of occurrence count

#### Test 4: Three-Word Phrase
- **Query**: `"evaṃ me sutaṃ"` (three words)
- **Expected**: Only exact three-word sequences
- **Verify**:
  - Three-color highlighting (or appropriate visual distinction)
  - Correct adjacent position checking (pos1, pos1+1, pos1+2)

#### Test 5: Edge Cases
- Single word with quotes: `"buddha"` → treat as regular search
- Empty quotes: `""` → handle gracefully
- Mixed wildcards and quotes: `"bhikkhu* buddha"` → expand then phrase match
- Very large distance: 50 words → performance acceptable

### 4.2 Performance Testing

**Metrics to Monitor**:
- Search time for phrase vs proximity vs wildcard combinations
- Memory usage with large result sets
- UI responsiveness during long searches

**Benchmarks** (from existing CST4 experience):
- Simple phrase: < 100ms
- Proximity with wildcards: < 500ms
- Complex multi-term: < 1000ms

### 4.3 UI/UX Testing

**Verify**:
- Slider updates search results in real-time (with debounce)
- Quote detection shows/hides slider automatically
- Highlighting colors are visually distinct and accessible
- Search statistics update correctly (unique word pairs count)

## Key Differences from CST4

### API Changes
- **CST4**: Lucene.NET 2.0 (`TermPositions`, `TermDocs`)
- **CST5**: Lucene.NET 4.8+ (`DocsAndPositionsEnum`, `MultiFields.GetTermPositionsEnum`)

### Architecture Changes
- **CST4**: Synchronous blocking calls
- **CST5**: Async/await patterns throughout

### UI Framework
- **CST4**: WinForms with manual event handling
- **CST5**: Avalonia UI with ReactiveUI bindings

### Script Support
- **CST4**: 14 Pali scripts (display and input)
- **CST5**: 14 Pali scripts (display), 9 for input (5 pending: Thai, Telugu, Tibetan, Khmer, Cyrillic)

### Indexing
- **CST4**: Custom positional index
- **CST5**: Lucene.NET with IPE (Internal Phonetic Encoding) and Devanagari analyzers

## Implementation Timeline

### Phase 1: Backend (Estimated 2-3 hours)
- [ ] Implement `ExpandWildcard` helper method (30 min)
- [ ] Implement `FindProximityMatches` helper method (20 min)
- [ ] Implement `FindPhraseMatches` helper method (20 min)
- [ ] Implement `SearchMultiWordAsync` main algorithm (1-1.5 hours)
- [ ] Add error handling and logging (20 min)

### Phase 2: UI Controls (Estimated 1 hour)
- [ ] Add word distance slider to SearchPanel.axaml (20 min)
- [ ] Update SearchViewModel properties and bindings (20 min)
- [ ] Implement quote detection and routing logic (20 min)

### Phase 3: Highlighting (Estimated 30 minutes)
- [ ] Update XSLT templates for two-color highlighting (20 min)
- [ ] Add CSS styles for hit/context rendering (10 min)

### Phase 4: Testing (Estimated 1 hour)
- [ ] Test phrase search with various queries (20 min)
- [ ] Test proximity search with different distances (20 min)
- [ ] Test wildcard combinations (10 min)
- [ ] Verify UI responsiveness and visual feedback (10 min)

**Total Estimated Time**: 4-5 hours for complete feature implementation

## Success Criteria

✅ **Feature Complete When**:
1. Phrase search (with quotes) returns only exact adjacent matches
2. Proximity search (without quotes) returns terms within specified distance
3. Wildcard expansion works with both phrase and proximity modes
4. Two-color highlighting displays correctly in book view
5. UI slider controls search distance dynamically
6. Search statistics show unique word pairs (not duplicate expansions)
7. All manual test scenarios pass
8. Performance meets benchmarks (< 1 second for most queries)

## References

- **CST4 Algorithm**: `devdocs/cst4/CST4_SEARCH.md` (lines 118-361)
- **CST4 Implementation**: `devdocs/cst4/Cst4/Search.cs` (GetMatchingTermsWithContext)
- **POC Tests**: `CST.Avalonia.Tests/Services/PhraseProximityPOCTests.cs`
- **Data Models**: `Models/SearchModels.cs` (TermPosition, SearchQuery)
- **Existing Search**: `Services/SearchService.cs` (single-term implementation)
- **Screenshots**: `/Users/fsnow/Cloud-Drive/Screenshots/cst4-*.png`

## Notes

- The `TermPosition.IsFirstTerm` flag is critical for two-color highlighting
- CST4 used `WordPosition` class; CST5 uses enhanced `TermPosition` class
- Wildcard proximity should show unique **word pairs**, not individual occurrences
- The word distance slider should be disabled during phrase search (quotes detected)
- All term matching happens in IPE encoding internally, display uses current script setting
