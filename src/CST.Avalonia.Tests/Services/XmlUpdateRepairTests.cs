using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// With automatic updates on, a book on disk that is not upstream's copy is replaced, whether or not upstream
/// has changed it. (#1093)
///
/// <para>[fsnow, 2026-11-02] "as long as "Enable automatic updates" is checked, any deviation from upstream
/// should be detected and repaired." [observed] Before, the update stopped as soon as upstream's commit was the
/// one it had recorded, and otherwise compared upstream with the hash recorded at download, never with the file
/// on disk; so a local edit survived until upstream changed that same book.</para>
/// </summary>
public class XmlUpdateRepairTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cst-1093-" + Guid.NewGuid().ToString("N"));

    public XmlUpdateRepairTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private const string Up = "upstream-sha";
    private const string Old = "older-upstream-sha";
    private const string Edited = "edited-sha";
    private const string Commit = "commit-a";

    private static readonly string[] OneBook = { "b.xml" };

    private static FileCommitInfo Tracked(string sha, DateTime? indexed = null) =>
        new() { CommitHash = sha, LastIndexedTimestamp = indexed };

    private static Dictionary<string, XmlUpdateService.LocalBook> Local(string? sha, bool unreadable = false) =>
        new() { ["b.xml"] = new XmlUpdateService.LocalBook(sha, unreadable) };

    private static Dictionary<string, FileCommitInfo> Tracking(FileCommitInfo? entry) =>
        entry == null ? new() : new() { ["b.xml"] = entry };

    private static XmlUpdateService.SyncPlan Plan(string? local, FileCommitInfo? tracked, string? repo = Up,
                                                 bool unreadable = false) =>
        XmlUpdateService.PlanSync(OneBook, Local(local, unreadable), Tracking(tracked),
            repo == null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["b.xml"] = repo });

    private static XmlUpdateService.LocalCheck Check(string? local, FileCommitInfo? tracked, bool unreadable = false) =>
        XmlUpdateService.CheckLocalBooks(OneBook, Local(local, unreadable), Tracking(tracked));

    // ---- whether the update may stop early -------------------------------------------------------------

    /// <summary>The reported case: upstream had not moved, so the update stopped and never read the edited
    /// book. An unchanged commit is no longer enough on its own.</summary>
    [Fact]
    public void An_edited_book_stops_the_update_skipping_even_when_upstream_has_not_moved()
    {
        Assert.False(XmlUpdateService.CanSkip(Commit, Commit, Check(Edited, Tracked(Up))));
    }

    [Fact]
    public void The_update_skips_only_when_upstream_has_not_moved_and_every_book_is_the_recorded_copy()
    {
        Assert.True(XmlUpdateService.CanSkip(Commit, Commit, Check(Up, Tracked(Up))));
        Assert.False(XmlUpdateService.CanSkip("commit-b", Commit, Check(Up, Tracked(Up))));
        Assert.False(XmlUpdateService.CanSkip(Commit, Commit, Check(null, Tracked(Up))));
        Assert.False(XmlUpdateService.CanSkip(Commit, Commit, Check(Up, null)));
    }

    // ---- which books to download --------------------------------------------------------------------------

    [Fact]
    public void A_book_that_is_upstreams_copy_is_left_alone_and_keeps_its_entry()
    {
        var entry = Tracked(Up, new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc));

        var plan = Plan(local: Up, tracked: entry);

        Assert.Empty(plan.ToDownload);
        Assert.Same(entry, plan.Tracking["b.xml"]);
    }

    [Fact]
    public void A_locally_edited_book_upstream_has_not_changed_is_repaired()
    {
        var plan = Plan(local: Edited, tracked: Tracked(Up));

        Assert.Equal(OneBook, plan.ToDownload);
        Assert.Equal(OneBook, plan.Repairs);
        Assert.False(plan.Tracking.ContainsKey("b.xml"));
    }

    [Fact]
    public void A_missing_book_is_downloaded()
    {
        Assert.Equal(OneBook, Plan(local: null, tracked: Tracked(Up)).ToDownload);
    }

    /// <summary>A file with no read permission, or held open exclusively by another process, is damage to
    /// repair, not a reason to stop.</summary>
    [Fact]
    public void An_unreadable_book_is_downloaded()
    {
        Assert.Equal(OneBook, Plan(local: null, tracked: Tracked(Up), unreadable: true).ToDownload);
    }

    [Fact]
    public void A_book_upstream_has_changed_is_downloaded_as_an_update_not_a_repair()
    {
        var plan = Plan(local: Old, tracked: Tracked(Old));

        Assert.Equal(OneBook, plan.ToDownload);
        Assert.Empty(plan.Repairs);
    }

    [Fact]
    public void An_untracked_book_that_matches_is_recorded_not_downloaded()
    {
        var plan = Plan(local: Up, tracked: null);

        Assert.Empty(plan.ToDownload);
        Assert.Equal(Up, plan.Tracking["b.xml"].CommitHash);
        Assert.Null(plan.Tracking["b.xml"].LastIndexedTimestamp);
    }

    [Fact]
    public void A_matching_book_recorded_under_another_hash_takes_upstreams()
    {
        var plan = Plan(local: Up, tracked: Tracked(Old, DateTime.UtcNow));

        Assert.Empty(plan.ToDownload);
        Assert.Equal(Up, plan.Tracking["b.xml"].CommitHash);
        Assert.Null(plan.Tracking["b.xml"].LastIndexedTimestamp);
    }

    // ---- books the repository does not have ----------------------------------------------------------------

    /// <summary>The plan's tracking set is what is saved, and it replaces the whole record. A book upstream does
    /// not have must stay in it, or the indexer re-adds it with no hash and it counts as a deviation at every
    /// start: the tree fetched and the book re-indexed on every launch.</summary>
    [Fact]
    public void A_book_not_in_the_repository_keeps_its_entry_and_is_not_downloaded()
    {
        var entry = Tracked(Edited, DateTime.UtcNow);

        var plan = Plan(local: Edited, tracked: entry, repo: null);

        Assert.Empty(plan.ToDownload);
        Assert.Equal(new[] { "b.xml" }, plan.NotInRepository);
        Assert.Same(entry, plan.Tracking["b.xml"]);
    }

    [Fact]
    public void A_book_not_in_the_repository_with_no_record_is_recorded_as_it_is_so_it_stops_being_a_deviation()
    {
        var plan = Plan(local: Edited, tracked: Tracked(""), repo: null);

        Assert.Equal(Edited, plan.Tracking["b.xml"].CommitHash);
        Assert.Equal(0, Check(Edited, plan.Tracking["b.xml"]).Count);
    }

    /// <summary>A book upstream no longer has, recorded under its last upstream copy and then edited, has nothing
    /// to be repaired from. Recording it as it is stops it counting as a difference, and costing a tree fetch,
    /// at every start; its index timestamp is kept. (re-review of badb757)</summary>
    [Fact]
    public void An_edited_book_upstream_no_longer_has_stops_being_a_deviation()
    {
        var indexed = DateTime.UtcNow;

        var plan = Plan(local: Edited, tracked: Tracked(Old, indexed), repo: null);

        Assert.Equal(Edited, plan.Tracking["b.xml"].CommitHash);
        Assert.Equal(indexed, plan.Tracking["b.xml"].LastIndexedTimestamp);
        Assert.Equal(0, Check(Edited, plan.Tracking["b.xml"]).Count);
    }

    /// <summary>What the no-download path saves: every book's entry, so nothing is dropped.</summary>
    [Fact]
    public void The_saved_tracking_set_covers_every_book_that_is_not_downloaded()
    {
        var books = new[] { "same.xml", "untracked.xml", "absent.xml", "edited.xml" };
        var local = new Dictionary<string, XmlUpdateService.LocalBook>
        {
            ["same.xml"] = new(Up), ["untracked.xml"] = new(Up), ["absent.xml"] = new(Old), ["edited.xml"] = new(Edited),
        };
        var tracked = new Dictionary<string, FileCommitInfo>
        {
            ["same.xml"] = Tracked(Up), ["absent.xml"] = Tracked(Old), ["edited.xml"] = Tracked(Up),
        };
        var repo = new Dictionary<string, string> { ["same.xml"] = Up, ["untracked.xml"] = Up, ["edited.xml"] = Up };

        var plan = XmlUpdateService.PlanSync(books, local, tracked, repo);

        Assert.Equal(new[] { "edited.xml" }, plan.ToDownload);
        Assert.Equal(new[] { "absent.xml", "same.xml", "untracked.xml" }, plan.Tracking.Keys.OrderBy(k => k));
    }

    // ---- the local check, which needs no network --------------------------------------------------------------

    [Fact]
    public void The_local_check_sorts_books_by_what_is_wrong_with_them()
    {
        Assert.Equal(0, Check(Up, Tracked(Up)).Count);
        Assert.Single(Check(Edited, Tracked(Up)).Differ);
        Assert.Single(Check(null, Tracked(Up)).Missing);
        Assert.Single(Check(null, Tracked(Up), unreadable: true).Unreadable);
        Assert.Single(Check(Up, Tracked("")).Unrecorded);
        Assert.Single(Check(Up, null).Unrecorded);
    }

    /// <summary>A book that cannot be read is reported, and does not stop the others being hashed.</summary>
    [Fact]
    public void An_unreadable_file_does_not_stop_the_others_being_read()
    {
        File.WriteAllBytes(Path.Combine(_dir, "ok.xml"), Encoding.UTF8.GetBytes("<TEI.2/>"));
        var locked = Path.Combine(_dir, "locked.xml");
        File.WriteAllBytes(locked, Encoding.UTF8.GetBytes("<TEI.2/>"));

        using (new FileStream(locked, FileMode.Open, FileAccess.ReadWrite, FileShare.None))
        {
            if (!OperatingSystem.IsWindows())
                File.SetUnixFileMode(locked, UnixFileMode.None);   // macOS/Linux honour no FileShare lock

            var books = XmlUpdateService.ReadLocalBooks(_dir, new[] { "locked.xml", "ok.xml", "gone.xml" });

            Assert.True(books["locked.xml"].Unreadable);
            Assert.NotNull(books["ok.xml"].Sha);
            Assert.True(books["gone.xml"].Missing);
        }
        if (!OperatingSystem.IsWindows())
            File.SetUnixFileMode(locked, UnixFileMode.UserRead | UnixFileMode.UserWrite);
    }

    /// <summary>Hashing streams the file, and gives exactly the Git blob id of its bytes.</summary>
    [Theory]
    [InlineData(0)]
    [InlineData(10)]
    [InlineData(65_536)]
    [InlineData(200_001)]
    public void A_streamed_hash_is_the_git_blob_id_of_the_bytes(int size)
    {
        var bytes = new byte[size];
        new Random(size).NextBytes(bytes);
        var path = Path.Combine(_dir, "f.xml");
        File.WriteAllBytes(path, bytes);

        Assert.Equal(GitBlobHash.Compute(bytes), GitBlobHash.ComputeFile(path));
    }
}
