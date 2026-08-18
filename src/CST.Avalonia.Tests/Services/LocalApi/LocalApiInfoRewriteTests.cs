using System;
using System.IO;
using System.Linq;
using CST.Avalonia.Services.LocalApi;
using Xunit;

namespace CST.Avalonia.Tests.Services.LocalApi;

/// <summary>
/// Rewriting the handshake file while somebody is reading it. (#506)
///
/// <para>On Unix a rename over an open file always succeeds - the reader keeps the old inode - so this whole
/// class of problem is invisible there, which is why it survived until Windows. Windows refuses to replace a
/// file held without <c>FILE_SHARE_DELETE</c>, and the <c>--mcp-bridge</c> relay polls this file while the
/// server may be rewriting it.</para>
/// </summary>
public class LocalApiInfoRewriteTests : IDisposable
{
    private readonly string _dir = Path.Combine(
        Path.GetTempPath(), "cst-handshake-tests", Guid.NewGuid().ToString("N"));

    public LocalApiInfoRewriteTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* best effort */ }
    }

    private static LocalApiInfo Info(int port = 51234, string token = "tok") =>
        new(port, token, Environment.ProcessId, 42);

    [Fact]
    public void The_handshake_round_trips()
    {
        // Baseline: the change to how the file is opened must not alter what it contains.
        Info(5000, "secret").Write(_dir);

        var read = LocalApiInfo.Read(_dir);

        Assert.NotNull(read);
        Assert.Equal(5000, read!.Port);
        Assert.Equal("secret", read.Token);
        Assert.Equal(42, read.StartToken);
    }

    [Fact]
    public void A_rewrite_succeeds_while_a_reader_holds_the_file()
    {
        // The actual defect. The relay polls this file; on Windows a reader opened the ordinary way blocks the
        // server's rewrite outright rather than queueing behind it, so the server's own startup could fail
        // because a client was doing exactly what it is supposed to do.
        //
        // The handle here is opened the way LocalApiInfo.Read now opens it - share-delete - which is what makes
        // the replace legal on Windows. Before the fix, Read used File.ReadAllText and this threw.
        Info(1111, "first").Write(_dir);
        var path = LocalApiInfo.PathIn(_dir);

        using (var held = new FileStream(
                   path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        {
            Info(2222, "second").Write(_dir);   // must not throw
        }

        Assert.Equal(2222, LocalApiInfo.Read(_dir)!.Port);
    }

    [Fact]
    public void Reading_does_not_block_a_rewrite()
    {
        // Stated from the reader's side, because that is the half that has to stay true as Read changes: a
        // reader must never be the reason a write fails. Reads the file, keeps the handle open, and writes.
        Info(3333, "before").Write(_dir);

        var first = LocalApiInfo.Read(_dir);
        using (var held = new FileStream(
                   LocalApiInfo.PathIn(_dir), FileMode.Open, FileAccess.Read,
                   FileShare.ReadWrite | FileShare.Delete))
        {
            Info(4444, "after").Write(_dir);
        }

        Assert.Equal(3333, first!.Port);
        Assert.Equal(4444, LocalApiInfo.Read(_dir)!.Port);
    }

    [Fact]
    public void A_write_leaves_no_temporary_file_behind()
    {
        // The temp is an implementation detail of making the swap atomic; it must never become litter beside a
        // file whose whole job is to be found and parsed by other programs.
        Info().Write(_dir);
        Info(9999, "again").Write(_dir);

        var stray = Directory.EnumerateFiles(_dir)
            .Where(f => f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase))
            .ToList();

        Assert.Empty(stray);
        Assert.Single(Directory.EnumerateFiles(_dir));
    }

    [WindowsFact]
    public void A_hostile_reader_is_retried_and_then_reported_without_leaving_a_temp()
    {
        // A reader we do NOT control - an antivirus scanner, a backup agent, a third-party client using
        // File.ReadAllText - can still hold the file without share-delete. That is retried briefly, and if it
        // never clears the failure is reported rather than swallowed: a handshake that was never written leaves
        // the server running but undiscoverable, which is not something to hide.
        //
        // What must NOT happen is a temp file accumulating per failed attempt.
        Info().Write(_dir);
        var path = LocalApiInfo.PathIn(_dir);

        using (var hostile = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.ThrowsAny<IOException>(() => Info(7777, "blocked").Write(_dir));
        }

        Assert.DoesNotContain(
            Directory.EnumerateFiles(_dir),
            f => f.EndsWith(".tmp", StringComparison.OrdinalIgnoreCase));

        // And once the hostile handle is gone, writing works again - the failure was transient, not terminal.
        Info(8888, "recovered").Write(_dir);
        Assert.Equal(8888, LocalApiInfo.Read(_dir)!.Port);
    }

    [Fact]
    public void An_absent_handshake_reads_as_null_rather_than_throwing()
    {
        Assert.Null(LocalApiInfo.Read(Path.Combine(_dir, "nope")));
    }
}
