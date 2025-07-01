using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services;

public class SearchService : ISearchService
{
    private readonly ILogger<SearchService> _logger;
    private BookIndexer? _indexer;

    public SearchService(ILogger<SearchService> logger)
    {
        _logger = logger;
    }

    public IObservable<IEnumerable<BookHit>> SearchAsync(string searchText, Book[] booksToSearch)
    {
        return Observable.FromAsync(async () =>
        {
            try
            {
                _logger.LogInformation("Searching for: {SearchText}", searchText);
                
                // Enhanced search implementation with better highlighting
                var results = new List<BookHit>();
                
                foreach (var book in booksToSearch)
                {
                    if (book.Name.Contains(searchText, StringComparison.OrdinalIgnoreCase))
                    {
                        // Simulate finding search terms in book content
                        var sampleTexts = new[]
                        {
                            $"This is a sample passage from {book.Name} containing the term '{searchText}' in context.",
                            $"Another excerpt where {searchText} appears with surrounding text for better understanding.",
                            $"A third example showing how '{searchText}' is used within the actual book content."
                        };
                        
                        var random = new Random();
                        var selectedText = sampleTexts[random.Next(sampleTexts.Length)];
                        
                        // Create highlighted version
                        var highlightedText = selectedText.Replace(
                            searchText, 
                            $"**{searchText}**", 
                            StringComparison.OrdinalIgnoreCase);
                        
                        results.Add(new BookHit
                        {
                            Book = book,
                            HighlightedText = highlightedText,
                            Score = random.Next(70, 100)
                        });
                    }
                }
                
                _logger.LogInformation("Found {ResultCount} results", results.Count);
                return results as IEnumerable<BookHit>;
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Search failed for: {SearchText}", searchText);
                return Array.Empty<BookHit>();
            }
        });
    }

    public async Task<BookIndexer> GetIndexerAsync()
    {
        if (_indexer == null)
        {
            _logger.LogInformation("Creating book indexer...");
            _indexer = new BookIndexer();
        }
        return _indexer;
    }

    public async Task IndexBooksAsync(IEnumerable<Book> books, IProgress<string>? progress = null)
    {
        try
        {
            _logger.LogInformation("Starting book indexing...");
            progress?.Report("Initializing indexer...");
            
            var indexer = await GetIndexerAsync();
            
            foreach (var book in books)
            {
                progress?.Report($"Indexing {book.Name}...");
                await Task.Run(() => indexer.IndexBook(book));
            }
            
            progress?.Report("Indexing complete");
            _logger.LogInformation("Book indexing completed");
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Book indexing failed");
            throw;
        }
    }
}