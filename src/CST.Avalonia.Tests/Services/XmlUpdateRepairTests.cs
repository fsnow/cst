using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using CST.Avalonia.Services;
using Xunit;

namespace CST.Avalonia.Tests.Services;

/// <summary>
/// With automatic updates on, a book on disk that is not upstream's copy is replaced, whether or not upstream
/// has changed it. (#1093)
///
/// <para>[fsnow, 2026-11-02] "as long as "Enable automatic updates" is checked, any deviation from upstream
/// should be detected and repaired." [observed] Before, the update compared upstream with the hash it had
/// recorded at download, never with the file on disk, so a local edit survived until upstream changed that same
/// book; and when any one tracking entry lacked a hash, every file was compared and replaced at once.</para>
/// </summary>
public class XmlUpdateRepairTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "cst-1093-" + Guid.NewGuid().ToString("N"));

    public XmlUpdateRepairTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, true); } catch { } }

    private const string Up = "upstream-sha";
    private const string Old = "older-upstream-sha";
    private const string Edited = "edited-sha";

    private static FileCommitInfo Tracked(string sha, DateTime? indexed = null) =>
        new() { CommitHash = sha, LastIndexedTimestamp = indexed };

    private static XmlUpdateService.SyncPlan Plan(string? local, FileCommitInfo? tracked, string? repo = Up)
    {
        var locals = new Dictionary<string, string?> { ["b.xml"] = local };
        var trackedFiles = tracked == null ? new Dictionary<string, FileCommitInfo>()
                                           : new Dictionary<string, FileCommitInfo> { ["b.xml"] = tracked };
        var repoShas = repo == null ? new Dictionary<string, string>() : new Dictionary<string, string> { ["b.xml"] = repo };
        return XmlUpdateService.PlanSync(new[] { "b.xml" }, locals, trackedFiles, repoShas);
    }

    [Fact]
    public void A_book_that_is_upstreams_copy_is_left_alone_and_keeps_its_entry()
    {
        var indexed = new DateTime(2026, 10, 1, 0, 0, 0, DateTimeKind.Utc);
        var entry = Tracked(Up, indexed);

        var plan = Plan(local: Up, tracked: entry);

        Assert.Empty(plan.ToDownload);
        Assert.Same(entry, plan.UpToDate["b.xml"]);   // the index timestamp survives: no needless re-index
    }

    /// <summary>The case that was reported: an edited book upstream never changed.</summary>
    [Fact]
    public void A_locally_edited_book_upstream_has_not_changed_is_repaired()
    {
        var plan = Plan(local: Edited, tracked: Tracked(Up));

        Assert.Equal(new[] { "b.xml" }, plan.ToDownload);
        Assert.Equal(new[] { "b.xml" }, plan.Repairs);
        Assert.False(plan.UpToDate.ContainsKey("b.xml"));
    }

    /// <summary>The old rule, kept here so the regression is on record: it compared the recorded hash with
    /// upstream's and never read the file, so the same edited book was not downloaded.</summary>
    [Fact]
    public void The_old_rule_kept_the_edited_book()
    {
        var recorded = Tracked(Up).CommitHash;
        var oldRuleDownloads = recorded != Up;

        Assert.False(oldRuleDownloads);
        Assert.NotEmpty(Plan(local: Edited, tracked: Tracked(Up)).ToDownload);
    }

    [Fact]
    public void A_missing_book_is_downloaded()
    {
        var plan = Plan(local: null, tracked: Tracked(Up));

        Assert.Equal(new[] { "b.xml" }, plan.ToDownload);
    }

    [Fact]
    public void A_book_upstream_has_changed_is_downloaded_as_an_update_not_a_repair()
    {
        var plan = Plan(local: Old, tracked: Tracked(Old));

        Assert.Equal(new[] { "b.xml" }, plan.ToDownload);
        Assert.Empty(plan.Repairs);
    }

    /// <summary>No tracking entry: the file is compared, and a match is recorded under upstream's hash with no
    /// index timestamp, so the indexer decides.</summary>
    [Fact]
    public void An_untracked_book_that_matches_is_recorded_not_downloaded()
    {
        var plan = Plan(local: Up, tracked: null);

        Assert.Empty(plan.ToDownload);
        Assert.Equal(Up, plan.UpToDate["b.xml"].CommitHash);
        Assert.Null(plan.UpToDate["b.xml"].LastIndexedTimestamp);
    }

    /// <summary>An entry recorded under another hash, with a file that now matches upstream (an update applied
    /// outside the app, say), takes upstream's hash and leaves indexing to the indexer.</summary>
    [Fact]
    public void A_matching_book_recorded_under_another_hash_takes_upstreams()
    {
        var plan = Plan(local: Up, tracked: Tracked(Old, DateTime.UtcNow));

        Assert.Empty(plan.ToDownload);
        Assert.Equal(Up, plan.UpToDate["b.xml"].CommitHash);
        Assert.Null(plan.UpToDate["b.xml"].LastIndexedTimestamp);
    }

    [Fact]
    public void A_book_not_in_the_repository_is_left_out()
    {
        var plan = Plan(local: Edited, tracked: Tracked(Up), repo: null);

        Assert.Empty(plan.ToDownload);
        Assert.Empty(plan.UpToDate);
    }

    // ---- the local check, which needs no network ---------------------------------------------------------

    [Theory]
    [InlineData(Up, Up, false)]       // the recorded copy
    [InlineData(Edited, Up, true)]    // edited or damaged
    [InlineData(null, Up, true)]      // missing
    [InlineData(Up, "", true)]        // never recorded
    public void The_local_check_finds_every_book_that_is_not_the_recorded_copy(string? local, string recorded, bool differs)
    {
        var deviations = XmlUpdateService.FindLocalDeviations(new[] { "b.xml" },
            new Dictionary<string, string?> { ["b.xml"] = local },
            new Dictionary<string, FileCommitInfo> { ["b.xml"] = Tracked(recorded) });

        Assert.Equal(differs, deviations.Contains("b.xml"));
    }

    [Fact]
    public void The_local_check_counts_a_book_with_no_tracking_entry()
    {
        var deviations = XmlUpdateService.FindLocalDeviations(new[] { "b.xml" },
            new Dictionary<string, string?> { ["b.xml"] = Up }, tracked: null);

        Assert.Equal(new[] { "b.xml" }, deviations);
    }

    [Fact]
    public void Local_hashes_are_git_blob_hashes_and_null_for_a_missing_file()
    {
        var bytes = Encoding.UTF8.GetBytes("<TEI.2/>");
        File.WriteAllBytes(Path.Combine(_dir, "here.xml"), bytes);

        var shas = XmlUpdateService.ComputeLocalShas(_dir, new[] { "here.xml", "gone.xml" });

        Assert.Equal(GitBlobHash.Compute(bytes), shas["here.xml"]);
        Assert.Null(shas["gone.xml"]);
    }
}
