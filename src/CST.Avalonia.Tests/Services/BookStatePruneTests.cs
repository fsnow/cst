using System.Collections.Generic;
using System.Linq;
using CST.Avalonia.Models;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// #624: the persisted book set becomes DERIVED from the live docks rather than maintained by removal events.
///
/// <para>
/// #623's fix narrowed state deletion to <c>CloseDockable</c>, the one removal that genuinely means "closed".
/// That is correct but still a classification scheme, and Dock gives no way to classify: a move and a close
/// are the same removal. A set derived from what is actually open cannot be wrong about the distinction,
/// because it never asks.
/// </para>
///
/// <para>
/// These tests are about <b>which set is sampled, and when</b> — the part that decides whether this is a
/// cleanup or a data-loss bug. The threading that makes the sample trustworthy (a synchronous re-walk on the
/// UI thread, with no <c>await</c> between the check and the removals) lives in
/// <c>CstDockFactory.PruneStateForVanishedBooks</c> and is argued in its remarks.
/// </para>
/// </summary>
public class BookStatePruneTests
{
    private static BookWindowState Book(string windowId) =>
        new() { WindowId = windowId, BookIndex = 0 };

    private static List<BookWindowState> Persisted(params string[] ids) =>
        ids.Select(Book).ToList();

    // ---- The guard that stops this destroying a session ------------------------------------------

    [Fact]
    public void An_empty_live_set_prunes_nothing()
    {
        // THE case this must never get wrong. At startup the layout is built with only the Welcome page,
        // which becomes the active dockable, which fires the tab-change save — and that save runs BEFORE the
        // restore path has copied the entries it is about to reopen. It walks real docks and finds no books.
        // Pruning here would delete the entire saved session on every single launch, before anything read it.
        Assert.Empty(BookStatePrune.Vanished(Persisted("a", "b", "c"), new List<string>()));
    }

    [Fact]
    public void A_null_live_set_prunes_nothing()
    {
        Assert.Empty(BookStatePrune.Vanished(Persisted("a"), null));
    }

    [Fact]
    public void Nothing_persisted_yields_nothing_to_prune()
    {
        Assert.Empty(BookStatePrune.Vanished(null, new[] { "a" }));
        Assert.Empty(BookStatePrune.Vanished(new List<BookWindowState>(), new[] { "a" }));
    }

    // ---- What it is for --------------------------------------------------------------------------

    [Fact]
    public void An_entry_with_no_live_book_is_pruned()
    {
        var vanished = BookStatePrune.Vanished(Persisted("open", "gone"), new[] { "open" });

        Assert.Equal(new[] { "gone" }, vanished);
    }

    [Fact]
    public void Every_live_book_survives_however_many_docks_it_took_to_find_them()
    {
        // The live set is gathered across the main dock, every split and every floating window. A book is
        // live wherever it lives — that is the whole point, and getting it wrong here would silently delete
        // floated books, which is the bug #623 fixed.
        var live = new[] { "main", "split", "floated" };

        Assert.Empty(BookStatePrune.Vanished(Persisted("main", "split", "floated"), live));
    }

    [Fact]
    public void A_book_present_at_sampling_time_is_kept_even_if_it_moved_during_the_save()
    {
        // The drag case, expressed as what it actually is: a question of WHEN the live set is sampled. The
        // save loop is interleaved with awaits and can see a dragged book transiently in no dock; the prune
        // samples afterwards, synchronously, and sees it in its new home. Passing the later sample is what
        // keeps it.
        var liveAfterTheDragLanded = new[] { "dragged" };

        Assert.Empty(BookStatePrune.Vanished(Persisted("dragged"), liveAfterTheDragLanded));
    }

    // ---- Shape ------------------------------------------------------------------------------------

    [Fact]
    public void Entries_with_no_window_id_are_ignored_rather_than_pruned_by_id()
    {
        // Removal is keyed by WindowId, so an entry without one cannot be addressed. Returning it would
        // produce a removal call that silently matches nothing — or, worse, matches another blank entry.
        var persisted = new List<BookWindowState> { Book(""), Book("real-and-gone") };

        Assert.Equal(new[] { "real-and-gone" }, BookStatePrune.Vanished(persisted, new[] { "live" }));
    }

    [Fact]
    public void A_duplicated_entry_is_reported_once()
    {
        // Duplicates should not exist, but UpdateBookWindowState is add-if-missing and a stale writer could
        // produce one. Removing by the same id twice is harmless; reporting it twice is noise in the log.
        var persisted = Persisted("gone", "gone");

        Assert.Equal(new[] { "gone" }, BookStatePrune.Vanished(persisted, new[] { "live" }));
    }

    [Fact]
    public void Window_ids_are_matched_exactly()
    {
        // They are GUIDs generated by the app, not user text. Case-insensitive matching would only create a
        // way for two distinct books to be treated as one.
        var vanished = BookStatePrune.Vanished(Persisted("ABC"), new[] { "abc" });

        Assert.Equal(new[] { "ABC" }, vanished);
    }
}
