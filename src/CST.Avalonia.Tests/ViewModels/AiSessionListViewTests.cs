using System;
using System.Globalization;
using System.Linq;
using System.Reactive.Linq;
using System.Threading.Tasks;
using CST.Avalonia.Converters;
using CST.Avalonia.Models;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.ViewModels;
using Xunit;

using static CST.Avalonia.Tests.ViewModels.AiAssistantViewModelTests;
using static CST.Avalonia.Tests.ViewModels.AiAssistantSessionWiringTests;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// What the session list's view adds on top of the #997 backend: the switcher's label, each row's rename and
/// delete-confirmation state, and how a row's books, time and turn count read. (#997 UI)
///
/// <para><b>[fsnow]</b>: delete <i>"Yes, with confirmation"</i>; naming <i>"Auto from the first turn,
/// renamable"</i>. The backend's own rules (busy, refused names, which row may go) are
/// <c>AiAssistantSessionListTests</c>'; these do not repeat them.</para>
/// </summary>
public class AiSessionListViewTests
{
    private static readonly DateTimeOffset Monday = new(2026, 9, 21, 9, 0, 0, TimeSpan.Zero);

    private static AiSession Stored(string id, string name, DateTimeOffset lastActive, int turns = 1)
    {
        var session = new AiSession { Id = id, Name = name, Created = lastActive, LastActive = lastActive };
        for (var i = 0; i < turns; i++)
            session.Turns.Add(new AiTurnRecord
            {
                Task = AiTask.Explain,
                Answer = "an answer",
                MarkedAnswer = "an answer",
                Citation = new AiCitationRecord { BookId = "s0101m.mul.xml", BookName = "book" },
                When = lastActive,
            });
        return session;
    }

    private static async Task<AiAssistantViewModel> TwoConversations(string? active = "first")
    {
        var store = new FakeStore();
        store.Seed(Stored("first", "Explain · First", Monday.AddHours(1)));
        store.Seed(Stored("second", "Second", Monday));
        var (vm, _, _, _) = Panel(new StubOrchestrator(), store: store,
            state: new ApplicationState { ActiveAssistantSessionId = active });
        await vm.RestoreAsync();
        return vm;
    }

    // ---- The switcher's label -----------------------------------------------------------------------

    [Fact]
    public async Task The_label_names_the_conversation_on_screen_and_follows_every_change_to_it()
    {
        var vm = await TwoConversations();
        Assert.Equal("Explain · First", vm.ActiveSessionName);

        await vm.RenameSessionAsync("first", "Renamed");
        Assert.Equal("Renamed", vm.ActiveSessionName);

        await vm.RenameSessionAsync("second", "Not on screen");
        Assert.Equal("Renamed", vm.ActiveSessionName);

        await vm.SwitchToSessionAsync("second");
        Assert.Equal("Not on screen", vm.ActiveSessionName);

        vm.NewConversationCommand.Execute().Subscribe();
        Assert.Equal(AiAssistantViewModel.NewConversationName, vm.ActiveSessionName);
    }

    [Fact]
    public async Task The_label_reads_new_conversation_when_nothing_is_active_or_the_active_one_is_deleted()
    {
        var vm = await TwoConversations(active: null);
        Assert.Equal(AiAssistantViewModel.NewConversationName, vm.ActiveSessionName);

        await vm.SwitchToSessionAsync("first");
        Assert.Equal("Explain · First", vm.ActiveSessionName);

        await vm.DeleteSessionAsync("first");
        Assert.Equal(AiAssistantViewModel.NewConversationName, vm.ActiveSessionName);
    }

    // ---- A row's rename and delete state ------------------------------------------------------------

    [Fact]
    public async Task Rename_starts_from_the_current_name_and_sends_only_a_real_change()
    {
        var vm = await TwoConversations();
        var row = vm.Sessions.Single(r => r.Id == "second");

        row.BeginRename();
        Assert.True(row.IsRenaming);
        Assert.False(row.IsIdle);
        Assert.Equal("Second", row.RenameText);

        // Unchanged, after trimming: nothing to send.
        row.RenameText = "  Second ";
        Assert.Null(row.CommitRename());
        Assert.False(row.IsRenaming);
        Assert.True(row.IsIdle);

        // Blank: nothing to send, and the box still closes (the old name stays).
        row.BeginRename();
        row.RenameText = "   ";
        Assert.Null(row.CommitRename());
        Assert.False(row.IsRenaming);

        row.BeginRename();
        row.RenameText = "  A better name ";
        Assert.Equal(new AiSessionRename("second", "A better name"), row.CommitRename());
    }

    [Fact]
    public async Task Rename_and_delete_confirmation_are_never_open_together()
    {
        var vm = await TwoConversations();
        var row = vm.Sessions[0];

        row.BeginRename();
        row.BeginDelete();
        Assert.False(row.IsRenaming);
        Assert.True(row.IsConfirmingDelete);
        Assert.False(row.IsIdle);

        row.BeginRename();
        Assert.True(row.IsRenaming);
        Assert.False(row.IsConfirmingDelete);

        row.CancelRename();
        Assert.True(row.IsIdle);

        row.BeginDelete();
        row.CancelDelete();
        Assert.True(row.IsIdle);
    }

    /// <summary>The list refreshes at the end of every answer. An open rename box must survive it, which is
    /// why the state lives on a row that keeps its identity rather than in a rebuilt template.</summary>
    [Fact]
    public async Task An_open_rename_box_survives_a_list_refresh()
    {
        var vm = await TwoConversations();
        var row = vm.Sessions.Single(r => r.Id == "second");
        row.BeginRename();
        row.RenameText = "half-typ";

        await vm.RefreshSessionsAsync();

        Assert.Same(row, vm.Sessions.Single(r => r.Id == "second"));
        Assert.True(row.IsRenaming);
        Assert.Equal("half-typ", row.RenameText);
    }

    // ---- How a row reads -----------------------------------------------------------------------------

    [Fact]
    public void Books_read_as_their_own_names_in_Latin_script()
    {
        // s0101m.mul.xml is the Silakkhandhavaggapali: its long path's last segment, converted from Devanagari.
        Assert.Equal("Sīlakkhandhavaggapāḷi",
            AiSessionBooksConverter.Format(new[] { "s0101m.mul.xml" }));
    }

    [Fact]
    public void A_book_the_list_does_not_know_is_shown_as_its_id_not_dropped()
    {
        Assert.Equal("Sīlakkhandhavaggapāḷi, nothing.xml",
            AiSessionBooksConverter.Format(new[] { "s0101m.mul.xml", "nothing.xml" }));
        Assert.Equal("", AiSessionBooksConverter.Format(Array.Empty<string>()));
    }

    [Fact]
    public void Last_active_reads_as_a_time_today_a_date_this_year_and_a_full_date_before()
    {
        var culture = CultureInfo.InvariantCulture;
        var now = new DateTimeOffset(2026, 9, 22, 18, 0, 0, TimeSpan.Zero);

        Assert.Equal("09:30", AiSessionTimeConverter.Format(now.AddHours(-8.5), now, culture));
        Assert.Equal("Sep 21", AiSessionTimeConverter.Format(now.AddDays(-1), now, culture));
        Assert.Equal("Dec 30, 2025", AiSessionTimeConverter.Format(new DateTimeOffset(2025, 12, 30, 12, 0, 0, TimeSpan.Zero), now, culture));
    }

    [Fact]
    public void Turn_counts_are_singular_for_one()
    {
        var c = AiSessionTurnCountConverter.Instance;
        Assert.Equal("1 turn", c.Convert(1, typeof(string), null, CultureInfo.InvariantCulture));
        Assert.Equal("3 turns", c.Convert(3, typeof(string), null, CultureInfo.InvariantCulture));
    }
}
