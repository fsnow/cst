# CST Avalonia Search Implementation Plan

## Overview
This document outlines the plan for implementing search functionality in CST Avalonia, building on the complete indexing infrastructure already in place. The search UI will be designed as a native macOS-style dockable panel that integrates seamlessly with the existing Dock.Avalonia layout.

## Design Principles
1. **Mac-Native Experience**: Dockable panel instead of modal dialog
2. **Live Search**: Real-time results as you type (with debouncing)
3. **Integrated Workflow**: Search results directly navigate to text locations
4. **Keyboard-First**: Full keyboard navigation support
5. **Cross-Platform Ready**: Architecture supports future Windows-specific UI

## Architecture

### Layer Structure
```
┌─────────────────────────────────────┐
│         SearchPanel.axaml           │  ← Mac-style dockable view
├─────────────────────────────────────┤
│       SearchViewModel.cs            │  ← MVVM with ReactiveUI
├─────────────────────────────────────┤
│        SearchService.cs             │  ← Business logic & Lucene
├─────────────────────────────────────┤
│    Existing Lucene Index            │  ← Already implemented
└─────────────────────────────────────┘
```

## Implementation Phases

### Phase 1: Core Search Service (Backend)
**Goal**: Implement SearchService with Lucene integration

#### 1.1 SearchService Interface
```csharp
public interface ISearchService
{
    Task<SearchResult> SearchAsync(SearchQuery query, CancellationToken ct);
    Task<List<string>> GetAllTermsAsync(string prefix, int limit);
    Task<SearchResult> GetNextPageAsync(string continuationToken);
}
```

#### 1.2 Core Data Models
```csharp
public class SearchQuery
{
    public string QueryText { get; set; }
    public SearchMode Mode { get; set; } // Exact, Wildcard, Regex
    public BookFilter Filter { get; set; }
    public int PageSize { get; set; }
}

public class SearchResult
{
    public List<MatchingTerm> Terms { get; set; }
    public int TotalTermCount { get; set; }
    public int TotalOccurrenceCount { get; set; }
    public string ContinuationToken { get; set; }
}

public class MatchingTerm
{
    public string Term { get; set; }  // IPE encoded
    public string DisplayTerm { get; set; }  // Current script
    public List<BookOccurrence> Occurrences { get; set; }
    public int TotalCount { get; set; }
}

public class BookOccurrence
{
    public Book Book { get; set; }
    public int Count { get; set; }
    public List<TermPosition> Positions { get; set; }
}

public class TermPosition
{
    public int Position { get; set; }
    public int StartOffset { get; set; }
    public int EndOffset { get; set; }
}
```

#### 1.3 Search Implementation
- Single term search using `MultiFields.GetTermPositionsEnum()`
- Multi-term search with proximity support
- Phrase search with exact ordering
- Wildcard/regex support via `TermMatchEvaluator` pattern
- Handle duplicate DocId issue (as per test file)
- Convert between IPE/display scripts

### Phase 2: Search UI Components

#### 2.1 SearchPanel View Structure
```xml
<DockableControl x:Class="SearchPanel">
  <Grid RowDefinitions="Auto,*,Auto">
    
    <!-- Search Input Area -->
    <Border Grid.Row="0" Classes="search-header">
      <StackPanel>
        <!-- Search Field with Clear Button -->
        <TextBox x:Name="SearchInput" 
                 Watermark="Search terms..."
                 Classes="search-field"/>
        
        <!-- Search Mode Selector -->
        <SegmentedControl x:Name="SearchMode">
          <SegmentedItem Content="Exact"/>
          <SegmentedItem Content="Wildcard"/>
          <SegmentedItem Content="Regex"/>
        </SegmentedControl>
        
        <!-- Collection Filter (Placeholder) -->
        <ComboBox x:Name="CollectionFilter">
          <ComboBoxItem>All Books</ComboBoxItem>
          <ComboBoxItem IsEnabled="False">Custom Collections...</ComboBoxItem>
        </ComboBox>
        
        <!-- Book Type Filters -->
        <Expander Header="Filter Books">
          <WrapPanel>
            <ToggleSwitch x:Name="FilterVinaya" Content="Vinaya"/>
            <ToggleSwitch x:Name="FilterSutta" Content="Sutta"/>
            <ToggleSwitch x:Name="FilterAbhi" Content="Abhidhamma"/>
            <!-- Additional filters... -->
          </WrapPanel>
        </Expander>
      </StackPanel>
    </Border>
    
    <!-- Search Results -->
    <Grid Grid.Row="1" ColumnDefinitions="*,2,*">
      <!-- Terms List -->
      <ListBox Grid.Column="0" x:Name="TermsList"
               SelectionMode="Multiple">
        <ListBox.ItemTemplate>
          <DataTemplate>
            <Grid ColumnDefinitions="*,Auto">
              <TextBlock Grid.Column="0" Text="{Binding DisplayTerm}"/>
              <TextBlock Grid.Column="1" Text="{Binding TotalCount}"
                        Classes="count-badge"/>
            </Grid>
          </DataTemplate>
        </ListBox.ItemTemplate>
      </ListBox>
      
      <GridSplitter Grid.Column="1"/>
      
      <!-- Book Occurrences -->
      <ListBox Grid.Column="2" x:Name="OccurrencesList">
        <ListBox.ItemTemplate>
          <DataTemplate>
            <Grid ColumnDefinitions="*,Auto">
              <TextBlock Grid.Column="0" Text="{Binding Book.DisplayName}"/>
              <TextBlock Grid.Column="1" Text="{Binding Count}"
                        Classes="count-badge"/>
            </Grid>
          </DataTemplate>
        </ListBox.ItemTemplate>
      </ListBox>
    </Grid>
    
    <!-- Status Bar -->
    <Border Grid.Row="2" Classes="search-status">
      <Grid ColumnDefinitions="*,*">
        <TextBlock Grid.Column="0" x:Name="TermStats"/>
        <TextBlock Grid.Column="1" x:Name="OccurrenceStats"/>
      </Grid>
    </Border>
    
  </Grid>
</DockableControl>
```

#### 2.2 SearchViewModel Implementation
```csharp
public class SearchViewModel : ViewModelBase
{
    private readonly ISearchService _searchService;
    private readonly IScriptService _scriptService;
    
    // Reactive properties
    public string SearchText { get; set; }
    public SearchMode SelectedMode { get; set; }
    public ObservableCollection<MatchingTerm> Terms { get; }
    public ObservableCollection<BookOccurrence> Occurrences { get; }
    
    // Reactive commands
    public ReactiveCommand<Unit, Unit> SearchCommand { get; }
    public ReactiveCommand<BookOccurrence, Unit> OpenBookCommand { get; }
    
    // Live search with debouncing
    private void SetupLiveSearch()
    {
        this.WhenAnyValue(x => x.SearchText)
            .Throttle(TimeSpan.FromMilliseconds(300))
            .DistinctUntilChanged()
            .SelectMany(async term => await SearchAsync(term))
            .ObserveOn(RxApp.MainThreadScheduler)
            .Subscribe(UpdateResults);
    }
}
```

### Phase 3: Integration with Main Application

#### 3.1 Dock Layout Integration
- Add SearchPanel as dockable tool window
- Default position: Left side, below book tree
- Resizable with splitter
- Can be hidden/shown via View menu
- Persist dock state in ApplicationState

#### 3.2 Search-to-Book Navigation
```csharp
// In SearchViewModel
private async Task OpenBookWithSearch(BookOccurrence occurrence)
{
    var terms = SelectedTerms.Select(t => t.Term).ToList();
    var positions = occurrence.Positions;
    
    // Always open book in new tab (even if same book is already open)
    // This matches CST4 behavior and allows comparing different search results
    await _bookService.OpenNewBookTabAsync(occurrence.Book, terms, positions);
    
    // Scroll to first occurrence
    // Highlight all occurrences
}
```

**Note**: Following CST4 behavior, search results always open in new tabs, allowing users to have multiple instances of the same book open with different search highlights. This is useful for comparing different search results within the same text.

#### 3.3 BookDisplayView Integration
- Add highlighting support for search terms
- Implement navigation between search results
- Add search result counter in toolbar
- Support keyboard shortcuts (F3/Shift+F3 for next/previous)

### Phase 4: Advanced Features

#### 4.1 Multi-Word Search Types
- **Phrase Search**: "term1 term2" - exact sequence
- **Proximity Search**: term1 NEAR/5 term2 - within 5 words
- **Boolean Search**: term1 AND term2, term1 OR term2

#### 4.2 Search History
- Store recent searches
- Quick access via dropdown
- Persist in user settings

#### 4.3 Search Result Export
- Export term list as text/CSV
- Generate occurrence report
- Copy results to clipboard

### Phase 5: Performance Optimization

#### 5.1 Async Operations
- All search operations async with cancellation
- Progress reporting for long searches
- Virtual scrolling for large result sets

#### 5.2 Caching Strategy
- Cache frequently accessed terms
- Cache recent search results
- Invalidate on index update

## UI/UX Specifications

### Visual Design
- **Mac Style**: Native macOS controls via Avalonia
- **Compact Layout**: Optimized for side panel width
- **Typography**: System fonts with proper script support
- **Colors**: Follow system light/dark theme

### Keyboard Shortcuts
- `⌘F`: Focus search field
- `⌘G`: Find next occurrence
- `⌘⇧G`: Find previous occurrence
- `↑↓`: Navigate term list
- `Enter`: Open selected book
- `Esc`: Clear search

### Responsive Behavior
- Minimum panel width: 250px
- Terms/occurrences lists split 50/50 by default
- Adjustable splitter position
- Collapsible filter section

## Testing Strategy

### Unit Tests
- SearchService logic with mock index
- Query parsing and validation
- Script conversion accuracy
- Filter combination logic

### Integration Tests
- End-to-end search flow
- Index interaction
- Multi-threaded search operations
- Memory management under load

### UI Tests
- Keyboard navigation
- Search debouncing
- Result selection
- Book opening workflow

## Migration Path

### From CST4
1. Core search logic compatible
2. Same Lucene index format
3. Similar result structure
4. Enhanced UI for macOS

### Future Windows Support
- Create SearchDialog.axaml for modal style
- Share SearchViewModel between views
- Platform-specific key bindings
- Maintain feature parity

## Implementation Timeline

**Week 1**: Core SearchService
- Basic term search
- Book filtering
- Results pagination

**Week 2**: Search UI
- SearchPanel view
- Basic SearchViewModel
- Dock integration

**Week 3**: Book Integration
- Result highlighting
- Navigation features
- Keyboard shortcuts

**Week 4**: Polish & Testing
- Performance optimization
- Bug fixes
- Documentation

## Dependencies

### Existing Components
- ✅ Lucene index (complete)
- ✅ IndexingService
- ✅ ScriptService
- ✅ BookDisplayView
- ✅ Dock.Avalonia

### New Components Needed
- ISearchService interface
- SearchService implementation
- SearchViewModel
- SearchPanel view
- Search result models

## Placeholder Features (Future)

### Book Collections
```csharp
// Placeholder in SearchService
public class BookCollection
{
    public string Name { get; set; }
    public BitArray BookBits { get; set; }
    // TODO: Implement custom collection editor
}
```

### Advanced Search Dialog
- Complex query builder
- Search templates
- Saved searches
- Batch operations

## Success Criteria

1. **Functional**: All CST4 search features working
2. **Performant**: Sub-second searches for common terms
3. **Native**: Feels like native macOS application
4. **Maintainable**: Clean separation of concerns
5. **Testable**: >80% code coverage
6. **Accessible**: Full keyboard navigation

## Notes

- Start with single-term search, iterate to multi-term
- Focus on Mac experience first, Windows later
- Keep SearchService protocol-agnostic for future gRPC option
- Consider WebView integration for result preview
- Maintain compatibility with existing index format