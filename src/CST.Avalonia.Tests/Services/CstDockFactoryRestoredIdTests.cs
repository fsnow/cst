using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CST.Avalonia.Services;
using CST.Avalonia.ViewModels;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// A restored book tab keeps the id it was saved with. (#1032)
///
/// <para><b>[fsnow]</b>, testing beta 8: <i>"The selected tab (book 185) is not getting the selection"</i>, and
/// <i>"we allow for multiple instances of the same window to be open, which is why they have a unique ID. but they
/// should be reopened with the same ID"</i>. <c>CstDockFactory.OpenBook</c> passed <c>null</c> where the restored id
/// belongs, so every restored book got a fresh id and the saved selection never matched.</para>
/// </summary>
public class CstDockFactoryRestoredIdTests
{
    private sealed class TestDockFactory : CstDockFactory
    {
        internal override IEnumerable<IDock> GetFloatingLayouts() => Enumerable.Empty<IDock>();
    }

    public CstDockFactoryRestoredIdTests() => ReactiveUiTestInit.Ensure();

    private static (TestDockFactory Factory, DocumentDock Documents) Make()
    {
        var documents = new DocumentDock { Id = "Documents", VisibleDockables = new ObservableCollection<IDockable>() };
        var root = new RootDock { Id = "MainRoot", VisibleDockables = new ObservableCollection<IDockable> { documents } };
        documents.Owner = root;
        var factory = new TestDockFactory { _context = root };
        return (factory, documents);
    }

    private static CST.Book AnyBook => CST.Books.Inst[0];

    private static List<string> BookIds(DocumentDock dock) =>
        dock.VisibleDockables!.OfType<BookDisplayViewModel>().Select(b => b.Id).ToList();

    [Fact]
    public void A_restored_book_keeps_its_saved_id()
    {
        var (factory, documents) = Make();

        factory.OpenBook(AnyBook, anchor: null, bookScript: null, windowId: "Book_0_saved_A");

        Assert.Equal(new[] { "Book_0_saved_A" }, BookIds(documents));
    }

    [Fact]
    public void Two_copies_of_one_book_keep_their_own_saved_ids()
    {
        var (factory, documents) = Make();

        factory.OpenBook(AnyBook, null, null, "Book_0_saved_A");
        factory.OpenBook(AnyBook, null, null, "Book_0_saved_B");

        Assert.Equal(new[] { "Book_0_saved_A", "Book_0_saved_B" }, BookIds(documents));
    }

    // Ids must stay unique among open documents: the dock and state lookups by id assume it.
    [Fact]
    public void A_saved_id_already_in_use_gets_a_fresh_one()
    {
        var (factory, documents) = Make();

        factory.OpenBook(AnyBook, null, null, "Book_0_saved_A");
        factory.OpenBook(AnyBook, null, null, "Book_0_saved_A");

        var ids = BookIds(documents);
        Assert.Equal(2, ids.Count);
        Assert.Equal("Book_0_saved_A", ids[0]);
        Assert.NotEqual(ids[0], ids[1]);
        Assert.StartsWith($"Book_{AnyBook.Index}_", ids[1]);
    }

    // An empty id is kept by nothing downstream (saves skip it when matching, prunes filter it out), so the
    // state file would grow by one entry per save.
    [Fact]
    public void An_empty_saved_id_gets_a_fresh_one()
    {
        var (factory, documents) = Make();

        factory.OpenBook(AnyBook, null, null, "");

        var id = Assert.Single(BookIds(documents));
        Assert.StartsWith($"Book_{AnyBook.Index}_", id);
    }

    [Fact]
    public void A_fresh_open_gets_a_fresh_id()
    {
        var (factory, documents) = Make();

        factory.OpenBook(AnyBook, null, null, windowId: null);

        Assert.StartsWith($"Book_{AnyBook.Index}_", Assert.Single(BookIds(documents)));
    }
}
