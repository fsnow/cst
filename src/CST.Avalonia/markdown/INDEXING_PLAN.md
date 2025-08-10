# CST.Avalonia Lucene Indexing Implementation Plan

## Executive Summary

This plan outlines the implementation of Lucene.NET indexing functionality for CST.Avalonia, recreating the core search infrastructure from CST4. The implementation will enable fast, position-based text search across 217 Buddhist XML texts with support for wildcard, regex, and proximity searches.

## Architecture Overview

### Key Components to Implement

1. **CST.Lucene Project** - Core indexing library (.NET 9 / Lucene.NET 4.8+)
2. **IndexingService** - Avalonia service for managing index lifecycle
3. **XmlFileDatesService** - Track file modifications for incremental updates
4. **Background Indexing** - Non-blocking index updates with progress reporting
5. **Index Storage** - Platform-specific index directory management

## Phase 1: Integrate Existing CST.Lucene Implementation (Week 1)

### 1.1 Use Existing Modernized CST.Lucene
The CST.Lucene project at `/Users/fsnow/Cloud-Drive/Projects/ConsoleAppNETCoreTest/CST.Lucene/` has already been:
- **Upgraded to .NET 9.0**
- **Using Lucene.NET 4.8.0-beta00016**
- **Modernized with `IndexWriter`** replacing deprecated `IndexModifier`
- **Tested against CST4 results**

### 1.2 Copy and Integrate Existing Classes
Copy the following tested implementations to `src/CST.Lucene/`:
- **BookIndexer.cs**: Already modernized with:
  - `IndexWriter` instead of `IndexModifier`
  - `DirectoryReader` and `FSDirectory` APIs
  - Modern `FieldType` configuration
  - Proper term vector storage with positions and offsets
  
- **DevaXmlAnalyzer.cs**: Custom analyzer already ported
  - Uses `LuceneVersion.LUCENE_48`
  - Implements modern `CreateComponents` pattern
  
- **DevaXmlTokenizer.cs**: Custom tokenizer (copy from source)
- **Other support files** as needed (IpeAnalyzer, IpeFilter if required)

### 1.3 Adapt for Avalonia Integration
- **Add async wrapper methods** for non-blocking UI operations
- **Add `IProgress<T>` support** to existing `IndxMsgDelegate` callbacks
- **Update namespace** if needed to match CST.Avalonia structure

## Phase 2: Implement Core Services (Week 2)

### 2.1 XmlFileDatesService
```csharp
public interface IXmlFileDatesService
{
    Task<List<int>> GetChangedBooksAsync();
    Task SaveFileDatesAsync();
    void UpdateFileDate(int bookIndex, DateTime lastWriteTime);
}
```
- Store file modification dates in JSON format (replace binary serialization)
- Support cross-platform file paths
- Implement change detection logic

### 2.2 IndexingService
```csharp
public interface IIndexingService
{
    Task<bool> IsIndexValidAsync();
    Task BuildIndexAsync(IProgress<IndexingProgress> progress);
    Task UpdateIndexAsync(List<int> changedBooks, IProgress<IndexingProgress> progress);
    Task OptimizeIndexAsync();
    string IndexDirectory { get; }
}
```
- Manage `BookIndexer` lifecycle
- Coordinate with `XmlFileDatesService`
- Provide progress updates to UI
- Handle index locking and cleanup

### 2.3 IndexingProgress Model
```csharp
public class IndexingProgress
{
    public int CurrentBook { get; set; }
    public int TotalBooks { get; set; }
    public string CurrentFileName { get; set; }
    public string StatusMessage { get; set; }
    public bool IsComplete { get; set; }
}
```

## Phase 3: Integration with CST.Avalonia (Week 3)

### 3.1 Startup Integration
- **App.axaml.cs**: Register new services with DI container
  ```csharp
  services.AddSingleton<IXmlFileDatesService, XmlFileDatesService>();
  services.AddSingleton<IIndexingService, IndexingService>();
  ```

### 3.2 Splash Screen Updates
- Modify `SplashScreen.axaml.cs` to show indexing progress
- Add progress bar for visual feedback
- Display current book being indexed
- Handle cancellation gracefully

### 3.3 Background Processing
- Use `Task.Run()` for CPU-intensive indexing
- Ensure UI remains responsive
- Implement proper exception handling
- Add retry logic for transient failures

### 3.4 Index Directory Management
- **Platform-specific paths**:
  - Windows: `%APPDATA%\CST.Avalonia\Index`
  - macOS: `~/Library/Application Support/CST.Avalonia/Index`
  - Linux: `~/.config/CST.Avalonia/Index`
- Create directories if they don't exist
- Handle permissions issues gracefully

## Phase 4: Testing & Optimization (Week 4)

### 4.1 Unit Tests
- Port existing test from `Tests_Indexing_01.cs`
- Add tests for:
  - Change detection logic
  - Incremental indexing
  - Index corruption recovery
  - Multi-script support

### 4.2 Integration Tests
- Full indexing of all 217 books
- Incremental update scenarios
- Concurrent access handling
- Cross-platform path handling

### 4.3 Performance Optimization
- Benchmark indexing speed
- Optimize memory usage
- Implement index compression if needed
- Add index integrity checks

## Phase 5: Search Implementation Foundation

### 5.1 SearchService Preparation
While search UI is out of scope for this phase, prepare the foundation:
- Ensure index structure supports position-based searches
- Verify term vectors are properly stored
- Test index can be opened by search components

### 5.2 Index Field Structure
Maintain CST4's field configuration for compatibility:
- **text**: Tokenized, with positions and offsets
- **FileName**: Stored, not indexed
- **MatnField**: Stored, not indexed  
- **PitakaField**: Stored, not indexed

## Implementation Timeline

| Week | Phase | Deliverables |
|------|-------|-------------|
| 1 | Integrate CST.Lucene | Copy tested implementation, add async wrappers |
| 2 | Core Services | XmlFileDatesService, IndexingService |
| 3 | Integration | Splash screen updates, background processing |
| 4 | Testing | Unit tests, integration tests, optimization |

## Technical Considerations

### Dependencies
- **Lucene.NET**: 4.8.0-beta00016 or newer
- **System.Text.Json**: For file dates serialization
- **Microsoft.Extensions.Logging**: For diagnostic output

### Backward Compatibility
- Support migration from CST4 index if present
- Handle missing or corrupted index gracefully
- Provide clean rebuild option

### Error Handling
- Graceful degradation if indexing fails
- Clear error messages to user
- Automatic retry for transient failures
- Manual index rebuild option in settings

### Platform-Specific Issues
- File path separators
- Case sensitivity on Linux
- File locking behavior differences
- Permission requirements

## Success Criteria

1. ✅ All 217 books indexed successfully on first run
2. ✅ Incremental updates detect and index only changed files
3. ✅ Index builds in under 2 minutes on modern hardware
4. ✅ UI remains responsive during indexing
5. ✅ Cross-platform compatibility (Windows, macOS, Linux)
6. ✅ Index format compatible with future search implementation

## Next Steps After Implementation

Once indexing is complete, the next phase will be:
1. Implement SearchService using the index
2. Create search UI components
3. Integrate highlighting in BookDisplayView
4. Add advanced search features (proximity, wildcards, regex)

## Notes

- The existing modernized `CST.Lucene` implementation from `/Users/fsnow/Cloud-Drive/Projects/ConsoleAppNETCoreTest/` should be used as-is
- This implementation has already been tested against CST4 results and proven to work correctly
- The test implementation in `Tests_Indexing_01.cs` validates the core approach
- Priority is on integrating the tested implementation with Avalonia's UI and service architecture
- Focus on reliability and maintaining the proven indexing logic while adding UI integration