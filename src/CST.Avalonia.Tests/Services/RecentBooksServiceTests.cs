using System.Linq;
using CST;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Conversion;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// The recently-opened-books (MRU) list (#44): promote-on-open (de-duplicated), cap by MaxRecentBooks
/// (0 = disabled), clear, and trim-on-shrink. Backed by ApplicationState.Preferences.RecentBooks.
/// </summary>
public sealed class RecentBooksServiceTests
{
    private static Book Bk(int index, string file, string nav = "") =>
        new() { Index = index, FileName = file, LongNavPath = nav };

    private static (RecentBooksService svc, ApplicationState state) Build(int max = 10)
    {
        var state = new ApplicationState();
        state.Preferences.MaxRecentBooks = max;
        var mock = new Mock<IApplicationStateService>();
        mock.SetupGet(s => s.Current).Returns(state);
        return (new RecentBooksService(mock.Object), state);
    }

    [Fact]
    public void Record_puts_the_newest_book_first()
    {
        var (svc, _) = Build();
        svc.Record(Bk(1, "a.xml"));
        svc.Record(Bk(2, "b.xml"));
        svc.Record(Bk(3, "c.xml"));

        Assert.Equal(new[] { 3, 2, 1 }, svc.Items.Select(i => i.BookIndex));
    }

    [Fact]
    public void Re_opening_a_book_promotes_it_without_duplicating()
    {
        var (svc, _) = Build();
        svc.Record(Bk(1, "a.xml"));
        svc.Record(Bk(2, "b.xml"));
        svc.Record(Bk(1, "a.xml"));   // re-open the first

        Assert.Equal(new[] { 1, 2 }, svc.Items.Select(i => i.BookIndex));
        Assert.Single(svc.Items, i => i.BookIndex == 1);
    }

    [Fact]
    public void The_list_is_capped_at_MaxRecentBooks()
    {
        var (svc, _) = Build(max: 3);
        for (int i = 1; i <= 5; i++)
            svc.Record(Bk(i, $"{i}.xml"));

        Assert.Equal(new[] { 5, 4, 3 }, svc.Items.Select(i => i.BookIndex));
    }

    [Fact]
    public void Max_of_zero_disables_the_list()
    {
        var (svc, _) = Build(max: 0);
        svc.Record(Bk(1, "a.xml"));
        svc.Record(Bk(2, "b.xml"));

        Assert.Empty(svc.Items);
    }

    [Fact]
    public void Setting_max_to_zero_clears_a_pre_existing_list_on_the_next_record()
    {
        var (svc, state) = Build(max: 10);
        svc.Record(Bk(1, "a.xml"));
        svc.Record(Bk(2, "b.xml"));

        state.Preferences.MaxRecentBooks = 0;   // disabled after the fact
        svc.Record(Bk(3, "c.xml"));

        Assert.Empty(svc.Items);
    }

    [Fact]
    public void Record_null_is_a_safe_no_op()
    {
        var (svc, _) = Build();
        svc.Record(null!);
        Assert.Empty(svc.Items);
    }

    [Fact]
    public void Re_opening_a_book_whose_index_shifted_promotes_by_filename_without_duplicating()
    {
        // A persisted entry from an older corpus (index 1, a.xml); the same file now has a different index.
        var (svc, _) = Build();
        svc.Record(Bk(1, "a.xml"));
        svc.Record(Bk(7, "a.xml"));   // same file, new index

        Assert.Single(svc.Items, i => i.BookFileName == "a.xml");
        Assert.Equal(7, svc.Items[0].BookIndex);
    }

    [Fact]
    public void Clear_empties_the_list_and_raises_Changed_once()
    {
        var (svc, _) = Build();
        svc.Record(Bk(1, "a.xml"));
        var changed = 0;
        svc.Changed += (_, _) => changed++;

        svc.Clear();
        Assert.Empty(svc.Items);
        Assert.Equal(1, changed);

        svc.Clear();                 // already empty — no-op
        Assert.Equal(1, changed);
    }

    [Fact]
    public void TrimToMax_shrinks_the_list_when_the_cap_is_lowered()
    {
        var (svc, state) = Build(max: 10);
        for (int i = 1; i <= 5; i++)
            svc.Record(Bk(i, $"{i}.xml"));

        state.Preferences.MaxRecentBooks = 2;   // user lowers the cap in Settings
        svc.TrimToMax();

        Assert.Equal(new[] { 5, 4 }, svc.Items.Select(i => i.BookIndex));
    }

    [Fact]
    public void Record_marks_state_dirty()
    {
        var state = new ApplicationState();
        var mock = new Mock<IApplicationStateService>();
        mock.SetupGet(s => s.Current).Returns(state);
        var svc = new RecentBooksService(mock.Object);

        svc.Record(Bk(1, "a.xml"));

        mock.Verify(s => s.MarkDirty(), Times.Once);
    }

    [Fact]
    public void DisplayName_uses_the_last_nav_segment_converted_to_the_target_script()
    {
        // LongNavPath is stored in Devanagari; the label converts to the requested script (capitalized),
        // matching a book tab's title.
        var deva = ScriptConverter.Convert("dīghanikāyo", Script.Latin, Script.Devanagari);
        var book = Bk(1, "s0101m.mul.xml", nav: "Something/" + deva);

        var latn = RecentBooksService.DisplayName(book, Script.Latin);

        Assert.Equal("Dīghanikāyo", latn);
    }

    [Fact]
    public void DisplayName_falls_back_to_the_filename_when_no_nav_path()
    {
        var book = Bk(1, "s0101m.mul.xml");
        Assert.Equal("s0101m.mul", RecentBooksService.DisplayName(book, Script.Latin));
    }
}
