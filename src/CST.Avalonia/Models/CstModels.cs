using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Runtime.CompilerServices;

namespace CST.Avalonia.Models;

/// <summary>
/// Represents a book in the Chaṭṭha Saṅgāyana Tipiṭaka collection
/// Based on the actual CST Book class structure
/// </summary>
public class CstBook : INotifyPropertyChanged
{
    private string _fileName = string.Empty;
    private string _longNavPath = string.Empty;
    private string _shortNavPath = string.Empty;
    private int _index = -1;
    private int _docId = -1;
    private string _chapterListTypes = string.Empty;

    // Core properties matching CST Book class
    public string FileName
    {
        get => _fileName;
        set
        {
            _fileName = value;
            OnPropertyChanged();
        }
    }

    public string LongNavPath
    {
        get => _longNavPath;
        set
        {
            _longNavPath = value;
            OnPropertyChanged();
        }
    }

    public string ShortNavPath
    {
        get => _shortNavPath;
        set
        {
            _shortNavPath = value;
            OnPropertyChanged();
        }
    }

    public int Index
    {
        get => _index;
        set
        {
            _index = value;
            OnPropertyChanged();
        }
    }

    public int DocId
    {
        get => _docId;
        set
        {
            _docId = value;
            OnPropertyChanged();
        }
    }

    public string ChapterListTypes
    {
        get => _chapterListTypes;
        set
        {
            _chapterListTypes = value;
            OnPropertyChanged();
        }
    }

    // Classification properties
    public Pitaka Pitaka { get; set; } = Pitaka.Other;
    public string PitakaField => Pitaka switch
    {
        Pitaka.Sutta => "sut",
        Pitaka.Vinaya => "vin",
        Pitaka.Abhidhamma => "abh",
        _ => "other"
    };

    public CommentaryLevel Matn { get; set; } = CommentaryLevel.Other;
    public string MatnField => Matn switch
    {
        CommentaryLevel.Mula => "mul",
        CommentaryLevel.Atthakatha => "att",
        CommentaryLevel.Tika => "tik",
        _ => "other"
    };

    public BookType BookType { get; set; } = BookType.Other;

    // Relationship properties
    public int MulaIndex { get; set; } = -1;
    public int AtthakathaIndex { get; set; } = -1;
    public int TikaIndex { get; set; } = -1;

    // Convenience properties for UI
    public string DisplayName => !string.IsNullOrEmpty(ShortNavPath) ? ShortNavPath : FileName;
    public string Id => Index.ToString();
    public string Name => DisplayName;
    public string Path => FileName;

    public bool IsIndexed => DocId >= 0;
    public bool HasMulaText => MulaIndex >= 0;
    public bool HasAtthakatha => AtthakathaIndex >= 0;
    public bool HasTika => TikaIndex >= 0;

    // INotifyPropertyChanged implementation
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Scripture collection classification
/// </summary>
public enum Pitaka
{
    Sutta,      // Sutta Piṭaka - Collection of discourses
    Vinaya,     // Vinaya Piṭaka - Monastic rules and regulations  
    Abhidhamma, // Abhidhamma Piṭaka - Philosophical analysis
    Other       // Other texts
}

/// <summary>
/// Commentary level classification
/// </summary>
public enum CommentaryLevel
{
    Mula,       // Root texts (original Pali)
    Atthakatha, // Commentaries
    Tika,       // Sub-commentaries
    Other       // Other types
}

/// <summary>
/// Book type classification - matches CST.BookType
/// </summary>
public enum BookType
{
    Unknown,    // Unknown type
    Whole,      // Complete book
    Chapter,    // Chapter
    Sutta,      // Discourse
    Other       // Other types
}

/// <summary>
/// Search result containing a book and hit information
/// Based on the actual CST BookHit class
/// </summary>
public class CstBookHit : INotifyPropertyChanged
{
    private int _count;
    private CstBook _book = new();
    private string _highlightedText = string.Empty;
    private int _score;

    public int Count
    {
        get => _count;
        set
        {
            _count = value;
            OnPropertyChanged();
        }
    }

    public CstBook Book
    {
        get => _book;
        set
        {
            _book = value;
            OnPropertyChanged();
        }
    }

    public string HighlightedText
    {
        get => _highlightedText;
        set
        {
            _highlightedText = value;
            OnPropertyChanged();
        }
    }

    public int Score
    {
        get => _score;
        set
        {
            _score = value;
            OnPropertyChanged();
        }
    }

    // INotifyPropertyChanged implementation
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Book collection management
/// Based on the actual CST Books singleton pattern
/// </summary>
public class CstBooks : INotifyPropertyChanged
{
    private static CstBooks? _instance;
    private readonly ObservableCollection<CstBook> _books = new();
    private readonly Dictionary<string, CstBook> _booksByFile = new();
    private readonly Dictionary<int, CstBook> _booksByDocId = new();

    private CstBooks()
    {
        PopulateBookList();
    }

    public static CstBooks Instance => _instance ??= new CstBooks();

    public ObservableCollection<CstBook> Books => _books;

    public CstBook this[int index] => _books[index];

    public int Count => _books.Count;

    public CstBook? GetBookByFileName(string fileName)
    {
        _booksByFile.TryGetValue(fileName, out var book);
        return book;
    }

    public CstBook? GetBookByDocId(int docId)
    {
        _booksByDocId.TryGetValue(docId, out var book);
        return book;
    }

    public void SetDocId(string fileName, int docId)
    {
        var book = GetBookByFileName(fileName);
        if (book != null)
        {
            if (book.DocId >= 0 && _booksByDocId.ContainsKey(book.DocId))
            {
                _booksByDocId.Remove(book.DocId);
            }
            
            book.DocId = docId;
            
            if (docId >= 0)
            {
                _booksByDocId[docId] = book;
            }
        }
    }

    public IEnumerable<CstBook> GetBooksByPitaka(Pitaka pitaka)
    {
        return _books.Where(b => b.Pitaka == pitaka);
    }

    public IEnumerable<CstBook> GetBooksByCommentaryLevel(CommentaryLevel level)
    {
        return _books.Where(b => b.Matn == level);
    }

    public IEnumerable<CstBook> GetBooksByType(BookType type)
    {
        return _books.Where(b => b.BookType == type);
    }

    private void PopulateBookList()
    {
        _books.Clear();
        _booksByFile.Clear();
        _booksByDocId.Clear();
        
        // Use the actual CST Books.Inst collection
        var cstBooks = CST.Books.Inst;
        int index = 0;
        
        foreach (var book in cstBooks)
        {
            var cstBook = new CstBook
            {
                FileName = book.FileName,
                LongNavPath = book.LongNavPath,
                ShortNavPath = book.ShortNavPath,
                Pitaka = (Pitaka)book.Pitaka,
                Matn = (CommentaryLevel)book.Matn,
                Index = index++,
                DocId = book.DocId,
                BookType = (BookType)book.BookType,
                MulaIndex = book.MulaIndex,
                AtthakathaIndex = book.AtthakathaIndex,
                TikaIndex = book.TikaIndex,
                ChapterListTypes = book.ChapterListTypes
            };

            _books.Add(cstBook);
            _booksByFile[cstBook.FileName] = cstBook;
            
            if (cstBook.DocId >= 0)
            {
                _booksByDocId[cstBook.DocId] = cstBook;
            }
        }

        OnPropertyChanged(nameof(Count));
    }

    // INotifyPropertyChanged implementation
    public event PropertyChangedEventHandler? PropertyChanged;

    protected virtual void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}

/// <summary>
/// Highlight position information for search term highlighting
/// </summary>
public class CstHighlightPosition
{
    public int Start { get; set; }
    public int Length { get; set; }
    public string Term { get; set; } = string.Empty;
    public int DocumentPosition { get; set; }
    public string Context { get; set; } = string.Empty;
}