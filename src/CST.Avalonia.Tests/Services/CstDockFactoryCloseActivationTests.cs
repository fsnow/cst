using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using CST.Avalonia.Services;
using CST.Avalonia.ViewModels;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Dock.Model.Mvvm.Core;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// Closing a tab does not leave Welcome showing while another document is open. (#1066)
///
/// <para><b>[fsnow]</b>, after repeated ⌘W stopped closing tabs: "the Welcome tab is selected ... And the Welcome
/// tab is eating the cmd+W", and "A close should then select any other open book, not Welcome". [observed] Dock
/// makes the tab to the LEFT of a closed tab active; book tabs are appended after Welcome, so closing the first
/// book activated Welcome even with books open to its right.</para>
/// </summary>
public class CstDockFactoryCloseActivationTests
{
    private sealed class TestDockFactory : CstDockFactory
    {
        internal override IEnumerable<IDock> GetFloatingLayouts() => Enumerable.Empty<IDock>();
    }

    public CstDockFactoryCloseActivationTests() => ReactiveUiTestInit.Ensure();

    private static (TestDockFactory Factory, DocumentDock Documents, List<BookDisplayViewModel> Books) Make(int books)
    {
        var welcome = new WelcomeViewModel(new WelcomeUpdateService(), fontService: null);
        var documents = new DocumentDock
        {
            Id = "Documents", VisibleDockables = new ObservableCollection<IDockable> { welcome }
        };
        welcome.Owner = documents;
        var root = new RootDock { Id = "MainRoot", VisibleDockables = new ObservableCollection<IDockable> { documents } };
        documents.Owner = root;
        var factory = new TestDockFactory { _context = root };
        welcome.Factory = factory;

        for (var i = 0; i < books; i++)
            factory.OpenBook(CST.Books.Inst[i], anchor: null, bookScript: null,
                windowId: $"Book_{i}_close_test_{System.Guid.NewGuid():N}");   // an id skips the 1-second duplicate-open guard
        // OpenBook adds the tab; in the app Dock sets each dockable's Owner when it initialises the layout, and
        // its close does nothing without one.
        var opened = documents.VisibleDockables!.OfType<BookDisplayViewModel>().ToList();
        foreach (var book in opened) book.Owner = documents;
        return (factory, documents, opened);
    }

    [Fact]
    public void Closing_the_first_book_shows_the_next_book_not_Welcome()
    {
        var (factory, documents, books) = Make(3);
        documents.ActiveDockable = books[0];

        factory.CloseDockable(books[0]);

        Assert.Same(books[1], documents.ActiveDockable);
    }

    // Dock's own choice, the tab to the left, is kept whenever it is a book.
    [Fact]
    public void Closing_a_later_book_still_shows_the_book_to_its_left()
    {
        var (factory, documents, books) = Make(3);
        documents.ActiveDockable = books[2];

        factory.CloseDockable(books[2]);

        Assert.Same(books[1], documents.ActiveDockable);
    }

    [Fact]
    public void Closing_the_last_book_shows_Welcome()
    {
        var (factory, documents, books) = Make(1);
        documents.ActiveDockable = books[0];

        factory.CloseDockable(books[0]);

        Assert.IsType<WelcomeViewModel>(documents.ActiveDockable);
    }

    // Closing a tab that is not showing changes nothing about what is showing.
    [Fact]
    public void Closing_a_background_tab_keeps_the_active_one()
    {
        var (factory, documents, books) = Make(3);
        documents.ActiveDockable = books[2];

        factory.CloseDockable(books[0]);

        Assert.Same(books[2], documents.ActiveDockable);
    }

    // ---- The choice itself ------------------------------------------------------------------------------

    private sealed class Tab : DockableBase
    {
        public Tab(string id) => Id = id;
    }

    private static bool IsWelcome(IDockable d) => d.Id == "W";

    [Fact]
    public void Dock_choosing_a_book_is_left_alone()
    {
        var remaining = new List<IDockable> { new Tab("W"), new Tab("a"), new Tab("b") };
        Assert.Null(CstDockFactory.NextActiveInsteadOfWelcome(remaining, 2, remaining[1], IsWelcome));
    }

    [Fact]
    public void Welcome_chosen_with_tabs_to_the_right_gives_the_nearest_one()
    {
        // [W, (removed at 1), a, b]
        var remaining = new List<IDockable> { new Tab("W"), new Tab("a"), new Tab("b") };
        Assert.Same(remaining[1], CstDockFactory.NextActiveInsteadOfWelcome(remaining, 1, remaining[0], IsWelcome));
    }

    // Welcome need not be first - a reordered strip still prefers the left, as Dock does.
    [Fact]
    public void Welcome_chosen_with_a_tab_further_left_gives_that_tab()
    {
        // [a, W, (removed at 2), b]
        var remaining = new List<IDockable> { new Tab("a"), new Tab("W"), new Tab("b") };
        Assert.Same(remaining[0], CstDockFactory.NextActiveInsteadOfWelcome(remaining, 2, remaining[1], IsWelcome));
    }

    [Fact]
    public void Welcome_alone_stays()
    {
        var remaining = new List<IDockable> { new Tab("W") };
        Assert.Null(CstDockFactory.NextActiveInsteadOfWelcome(remaining, 1, remaining[0], IsWelcome));
    }
}
