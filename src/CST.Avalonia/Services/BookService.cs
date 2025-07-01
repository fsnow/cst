using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services;

public class BookService : IBookService
{
    private readonly CstBooks _cstBooks = CstBooks.Instance;
    private readonly ILogger<BookService> _logger;

    public ObservableCollection<Book> Books { get; } = new();

    public BookService(ILogger<BookService> logger)
    {
        _logger = logger;
    }

    public async Task LoadBooksAsync()
    {
        try
        {
            _logger.LogInformation("Loading books from CST collection...");
            
            // Create adapter books from CST books
            Books.Clear();
            foreach (var cstBook in _cstBooks.Books)
            {
                var book = new Book
                {
                    Id = cstBook.Index.ToString(),
                    Name = cstBook.DisplayName,
                    Path = cstBook.FileName
                };
                Books.Add(book);
            }
            
            _logger.LogInformation("Loaded {BookCount} books from CST collection", Books.Count);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load books");
            throw;
        }
    }

    public async Task<Book?> GetBookAsync(string id)
    {
        return Books.FirstOrDefault(b => b.Id == id);
    }

    public async Task<IEnumerable<Book>> GetBooksInCollectionAsync(string collectionName)
    {
        try
        {
            _logger.LogInformation("Getting books in collection: {CollectionName}", collectionName);
            
            // Filter books by collection/pitaka
            return collectionName.ToLower() switch
            {
                "sutta" or "sutta pitaka" => Books.Where(b => GetCstBook(b)?.Pitaka == Pitaka.Sutta),
                "vinaya" or "vinaya pitaka" => Books.Where(b => GetCstBook(b)?.Pitaka == Pitaka.Vinaya),
                "abhidhamma" or "abhidhamma pitaka" => Books.Where(b => GetCstBook(b)?.Pitaka == Pitaka.Abhidhamma),
                _ => Books.ToList()
            };
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to get books in collection: {CollectionName}", collectionName);
            throw;
        }
    }

    public CstBook? GetCstBook(Book book)
    {
        if (int.TryParse(book.Id, out var index) && index < _cstBooks.Count)
        {
            return _cstBooks[index];
        }
        return null;
    }

    public CstBook? GetCstBookByIndex(int index)
    {
        if (index >= 0 && index < _cstBooks.Count)
        {
            return _cstBooks[index];
        }
        return null;
    }
}