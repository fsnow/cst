using System;
using System.IO;
using System.Linq;
using System.Text.Json.Nodes;
using CST.Avalonia.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// A cached chapter list is rebuilt when what it was built from changes. (#1075)
///
/// <para><b>[fsnow]</b> "stale chapter list bug ! definitely file, beta 9 milestone". [observed] Before the fix, a
/// list was generated once and never again, so a corrected book reached a reader who had already opened it
/// with its old chapter list. Structural markup is being added to the corpus, so corrections are coming.</para>
/// </summary>
public class ChapterListsServiceTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cst-chapters-" + Guid.NewGuid().ToString("N"));
    private readonly string _xml;
    private readonly string _cache;
    private static readonly CST.Book Book = CST.Books.Inst[0];   // s0101m.mul.xml, ChapterListTypes "book,sutta"

    public ChapterListsServiceTests()
    {
        _xml = Path.Combine(_dir, "xml");
        Directory.CreateDirectory(_xml);
        _cache = Path.Combine(_dir, "chapter-lists.json");
        Assert.Equal("book,sutta", Book.ChapterListTypes);   // the premise the documents below are written for
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { }
    }

    private ChapterListsService Service() =>
        new(NullLogger<ChapterListsService>.Instance, null, _cache, _xml);

    private string BookPath => Path.Combine(_xml, Book.FileName);

    // A book with the given suttas, and a write time a given number of minutes after a fixed point, so a
    // rewrite is never mistaken for the same file by a coarse file-system clock.
    private void WriteBook(int minutes, params string[] suttas)
    {
        var divs = string.Concat(suttas.Select((s, i) =>
            $"<div id=\"dn1_{i + 1}\" n=\"dn1_{i + 1}\" type=\"sutta\"><head rend=\"chapter\">{s}</head><p>…</p></div>"));
        File.WriteAllText(BookPath,
            "<?xml version=\"1.0\" encoding=\"utf-8\"?><TEI.2><text><body>" +
            $"<div id=\"dn1\" n=\"dn1\" type=\"book\"><head rend=\"book\">Book</head>{divs}</div>" +
            "</body></text></TEI.2>");
        File.SetLastWriteTimeUtc(BookPath, new DateTime(2026, 10, 19, 12, 0, 0, DateTimeKind.Utc).AddMinutes(minutes));
    }

    private string[] Headings(ChapterListsService s) =>
        s.GetChapterList(Book.Index)!.Select(c => c.Heading.Trim()).ToArray();

    [Fact]
    public void A_list_is_rebuilt_when_the_books_xml_changes()
    {
        WriteBook(0, "One");
        var service = Service();
        Assert.Equal(new[] { "Book", "One" }, Headings(service));

        WriteBook(1, "One", "Two");

        Assert.Equal(new[] { "Book", "One", "Two" }, Headings(service));
    }

    // The case readers hit: the book is updated while the app is closed.
    [Fact]
    public void A_list_saved_by_one_run_is_rebuilt_by_the_next_when_the_xml_changed_in_between()
    {
        WriteBook(0, "One");
        Headings(Service());

        WriteBook(1, "One", "Two");

        Assert.Equal(new[] { "Book", "One", "Two" }, Headings(Service()));
    }

    [Fact]
    public void An_unchanged_book_is_served_from_the_cache()
    {
        WriteBook(0, "One");
        Headings(Service());
        EditCachedEntry(entry => entry["Chapters"]![1]!["heading"] = "FROM THE CACHE");

        Assert.Equal(new[] { "Book", "FROM THE CACHE" }, Headings(Service()));
    }

    [Fact]
    public void A_list_built_with_other_chapter_list_types_is_rebuilt()
    {
        WriteBook(0, "One");
        Headings(Service());
        EditCachedEntry(entry =>
        {
            entry["Types"] = "book";
            entry["Chapters"]![1]!["heading"] = "FROM THE CACHE";
        });

        Assert.Equal(new[] { "Book", "One" }, Headings(Service()));
    }

    // The format before #1075 records nothing to check against, so its lists cannot be trusted.
    [Fact]
    public void A_cache_file_in_the_old_format_is_not_trusted()
    {
        WriteBook(0, "One");
        File.WriteAllText(_cache,
            $"{{\"{Book.Index}\": [{{\"id\": \"dn1_1\", \"heading\": \"STALE\", \"indentLevel\": 1}}]}}");

        Assert.Equal(new[] { "Book", "One" }, Headings(Service()));
    }

    private void EditCachedEntry(Action<JsonNode> edit)
    {
        var root = JsonNode.Parse(File.ReadAllText(_cache))!;
        edit(root["Books"]![Book.Index.ToString()]!);
        File.WriteAllText(_cache, root.ToJsonString());
    }
}
