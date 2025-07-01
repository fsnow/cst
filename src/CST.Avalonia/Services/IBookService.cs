using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;

namespace CST.Avalonia.Services;

// Placeholder Book class until project references are restored
public class Book
{
    public string Id { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Path { get; set; } = string.Empty;
}

public interface IBookService
{
    ObservableCollection<Book> Books { get; }
    Task LoadBooksAsync();
    Task<Book?> GetBookAsync(string id);
    Task<IEnumerable<Book>> GetBooksInCollectionAsync(string collectionName);
}