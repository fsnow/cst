using System;
using System.Collections.Generic;
using System.Linq;
using System.Reactive;
using System.Threading.Tasks;

namespace CST.Avalonia.Services;

// Placeholder classes until project references are restored
public class BookHit
{
    public Book Book { get; set; } = new();
    public string HighlightedText { get; set; } = string.Empty;
    public int Score { get; set; }
}

public class BookIndexer
{
    public void IndexBook(Book book) { }
}

public interface ISearchService
{
    IObservable<IEnumerable<BookHit>> SearchAsync(string searchText, Book[] booksToSearch);
    Task<BookIndexer> GetIndexerAsync();
    Task IndexBooksAsync(IEnumerable<Book> books, IProgress<string>? progress = null);
}