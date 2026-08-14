using System;
using System.Runtime.CompilerServices;
using System.Linq;
using CST.Avalonia.Services;
using Dock.Model.Core;
using Dock.Model.Mvvm.Controls;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// The interaction history behind #621.
///
/// <para>
/// It records order and nothing else — no filtering, no notion of what a document is. That is the design:
/// the resolver filters on read by asking a layout what it contains, which is what lets one flat history
/// serve every window and what keeps tool activations from displacing documents. These tests pin the
/// recording rules; <c>DocumentTargetResolverTests</c> pins what reading them means.
/// </para>
/// </summary>
public class ActiveDocumentTrackerTests
{
    private sealed class TestDocument : Document
    {
    }

    private static TestDocument Doc(string id) => new() { Id = id, Title = id };

    [Fact]
    public void The_most_recent_interaction_comes_first()
    {
        var tracker = new ActiveDocumentTracker();
        var a = Doc("a");
        var b = Doc("b");

        tracker.Note(a);
        tracker.Note(b);

        Assert.Equal(new IDockable[] { b, a }, tracker.Recent);
    }

    [Fact]
    public void Re_noting_moves_to_the_front_rather_than_duplicating()
    {
        var tracker = new ActiveDocumentTracker();
        var a = Doc("a");
        var b = Doc("b");

        tracker.Note(a);
        tracker.Note(b);
        tracker.Note(a);

        // The feeds overlap by design — a tab click raises an activation AND an Avalonia focus change —
        // so redundant notes are the normal case, not an edge case.
        Assert.Equal(new IDockable[] { a, b }, tracker.Recent);
    }

    [Fact]
    public void Identity_is_by_reference_not_by_id()
    {
        var tracker = new ActiveDocumentTracker();
        // Splits produce several docks all carrying the id "MainDocumentDock", and two tabs of the same
        // book would compare equal on anything weaker than reference identity.
        var first = new DocumentDock { Id = "MainDocumentDock" };
        var second = new DocumentDock { Id = "MainDocumentDock" };

        tracker.Note(first);
        tracker.Note(second);

        Assert.Equal(2, tracker.Recent.Count);
        Assert.Same(second, tracker.Recent[0]);
        Assert.Same(first, tracker.Recent[1]);
    }

    [Fact]
    public void History_is_bounded()
    {
        var tracker = new ActiveDocumentTracker();
        var docs = Enumerable.Range(0, 30).Select(i => Doc($"d{i}")).ToList();

        foreach (var d in docs) tracker.Note(d);

        Assert.True(tracker.Recent.Count <= 8);
        Assert.Same(docs[^1], tracker.Recent[0]);
    }

    [Fact]
    public void A_closed_document_is_forgotten()
    {
        var tracker = new ActiveDocumentTracker();
        var a = Doc("a");
        var b = Doc("b");
        tracker.Note(a);
        tracker.Note(b);

        tracker.Forget(b);

        Assert.Equal(new IDockable[] { a }, tracker.Recent);
    }

    [Fact]
    public void Forgetting_something_never_recorded_is_harmless()
    {
        var tracker = new ActiveDocumentTracker();
        tracker.Note(Doc("a"));

        tracker.Forget(Doc("never-seen"));

        Assert.Single(tracker.Recent);
    }

    [Fact]
    public void Null_is_ignored_rather_than_recorded()
    {
        var tracker = new ActiveDocumentTracker();
        // Every feed can produce one: a DataContext that is not a dockable, a view whose ViewModel has not
        // been bound yet, an activation event for a dockable already gone.
        tracker.Note(null);
        tracker.Forget(null);

        Assert.Empty(tracker.Recent);
    }

    [Fact]
    public void Entries_do_not_keep_a_closed_documents_ViewModel_alive()
    {
        // The reason entries are weak. A book's ViewModel owns a CEF WebView; the history holding the last
        // eight for the session would be a leak with a browser attached to it. Forget() covers the normal
        // close path — this covers every other way a dockable can go away.
        var tracker = new ActiveDocumentTracker();
        var reference = NoteAndDrop(tracker);

        GC.Collect();
        GC.WaitForPendingFinalizers();
        GC.Collect();

        Assert.False(reference.IsAlive);
        Assert.Empty(tracker.Recent);
    }

    // Kept out of the test body so the strong local cannot survive on the stack in a Debug build.
    [MethodImpl(MethodImplOptions.NoInlining)]
    private static WeakReference NoteAndDrop(ActiveDocumentTracker tracker)
    {
        var doomed = Doc("doomed");
        tracker.Note(doomed);
        return new WeakReference(doomed);
    }
}
