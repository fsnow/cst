using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Constants;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services.Ai;

/// <summary>
/// Reads and writes Assistant conversations, one file each. (#849 §3.2)
///
/// <para><b>What it is not.</b> No timer, no backup directory, no retention sweep. The caller writes a session
/// when a turn ends, which is the Claude Code guarantee transposed — a crash loses the turn in flight and
/// nothing else — and <b>[fsnow]</b> chose retention <i>"Forever, no cap"</i>, so there is nothing for a sweep
/// to do. Deletion is a reader's act on one session, confirmed in the UI (<b>[fsnow]</b>: <i>"Yes, with
/// confirmation"</i>); this layer provides the delete, not the question.</para>
///
/// <para><b>Failures are reported, never thrown.</b> A transcript that cannot be read or written must not be
/// able to stop the Assistant panel from opening. So <see cref="LoadAsync"/> answers null and keeps the bad
/// file, <see cref="SaveAsync"/> answers false, and <see cref="Unreadable"/> is how the panel learns enough to
/// say so on screen.</para>
/// </summary>
public interface IAiSessionStore
{
    /// <summary>
    /// One conversation, or null when there is no such file — and also null when the file could not be read,
    /// in which case it has been moved aside and <see cref="Unreadable"/> has fired. Those two cases are
    /// deliberately one return value and two reports: a caller restoring the last session does the same thing
    /// either way (start empty), while a caller that wants to TELL the reader has the event.
    /// </summary>
    Task<AiSession?> LoadAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Write the whole session. False when nothing was written — the caller decides whether that is worth
    /// saying; it must not be worth failing a turn over.
    ///
    /// <para>The whole file, not an append. A transcript is rewritten at the end of each turn because the turn
    /// that just ended is not the only thing that changed about it — <c>LastActive</c> moved, the name may
    /// have been set from the first turn — and because a partially appended JSON array is unreadable in a way
    /// a whole-file replace can never be.</para>
    /// </summary>
    Task<bool> SaveAsync(AiSession session, CancellationToken cancellationToken = default);

    /// <summary>
    /// Every readable session on disk as a list row, newest-active first. Unreadable files are kept aside and
    /// reported through <see cref="Unreadable"/>; they do not appear and do not fail the listing, because one
    /// broken transcript must not cost the reader the list of the other ninety-nine.
    /// </summary>
    Task<IReadOnlyList<AiSessionSummary>> ListAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Remove one conversation. False when there was no such file — <b>not an error</b>: the id can only have
    /// come from a list the reader was looking at or from <c>ActiveAssistantSessionId</c>, and both can name a
    /// session that has since gone (a second window, a hand-deleted file). Treating that as a failure would
    /// mean an error dialog for work already done.
    /// </summary>
    Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default);

    /// <summary>
    /// A session file could not be read and has been kept. Subscribe before loading or listing.
    ///
    /// <para>An event rather than a return value because both <see cref="LoadAsync"/> and
    /// <see cref="ListAsync"/> can hit it, and in the listing case there may be several while the call still
    /// has to succeed. The panel's notice ("that conversation could not be read; it has been kept at …") is
    /// the point of it — the log alone leaves the reader with an empty panel and no explanation.</para>
    /// </summary>
    event Action<AiSessionUnreadable>? Unreadable;
}

/// <inheritdoc cref="IAiSessionStore"/>
public sealed class AiSessionStore : IAiSessionStore
{
    /// <summary>The directory name under <see cref="AppConstants.DataDirectory"/>. One place, so the store and
    /// anything that later reports on the data directory cannot diverge.</summary>
    public const string DirectoryName = "assistant-sessions";

    /// <summary>
    /// How sessions are read and written. <b>Shared rather than mirrored</b> — the tests use THIS property, not
    /// a copy of it, because a copy cannot detect drift from the original, which is the one thing it most needs
    /// to detect: change the naming policy or drop the enum converter and every session file on disk stops
    /// loading while a mirroring suite stays green, since fixture and copy moved together. (#787's lesson,
    /// stated on <c>ApplicationStateService.JsonOptions</c>.)
    ///
    /// <para><b>Two deliberate differences from that one.</b></para>
    ///
    /// <para><see cref="JsonSerializerDefaults"/>-style <c>WhenWritingDefault</c> is NOT used. App state uses
    /// it and pays for it three times over: <c>SearchDialogState</c>, the #224 display bools and #323's
    /// <c>BookScript</c> all carry force-serialize attributes to undo it, because a written-out <c>false</c> or
    /// a zero-valued enum is exactly what must survive when the property's initializer is not the type default.
    /// A new format does not have to inherit that trap, so only nulls are omitted here. It costs a few bytes
    /// per turn and removes a whole class of "it reverted on reload".</para>
    ///
    /// <para>The enum converter is load-bearing rather than tidy: <c>AiTask</c> and
    /// <c>PageEdition</c> (inside <c>CitationRef.Pages</c>) are positional, and <b>[fsnow]</b>'s <i>"Forever,
    /// no cap"</i> means files written today will be read by builds that may have inserted a preset or an
    /// edition. Numbers would silently re-point; names cannot.</para>
    ///
    /// <para><b>Pāli is written as Pāli.</b> The default encoder escapes every non-ASCII character, which
    /// turns a stored passage into pages of <c>ā</c> — these files are read by hand when something is
    /// wrong with an answer, and that is the moment the text needs to be legible. <c>UnicodeRanges.All</c>
    /// relaxes only that: <c>JavaScriptEncoder</c> still escapes the HTML-sensitive characters whatever range
    /// it is given, so nothing here becomes safe to paste into a page that it was not before.</para>
    /// </summary>
    internal static JsonSerializerOptions JsonOptions { get; } = new()
    {
        WriteIndented = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull,
        Converters = { new JsonStringEnumConverter() },
        Encoder = System.Text.Encodings.Web.JavaScriptEncoder.Create(
            System.Text.Unicode.UnicodeRanges.All),
    };

    private readonly string _directory;
    private readonly ILogger<AiSessionStore>? _logger;

    // One write at a time. The temp file is per-session so two sessions could not collide anyway, but the
    // same session being saved twice at once could — and that is the shape of the bug STATE-2 fixed in
    // ApplicationStateService, where a shared .tmp let a half-written file be promoted over good data.
    private readonly SemaphoreSlim _writeLock = new(1, 1);

    public AiSessionStore(ILogger<AiSessionStore>? logger = null)
        : this(Path.Combine(AppConstants.DataDirectory, DirectoryName), logger)
    {
    }

    /// <summary>
    /// Test seam: point the store at a temp directory instead of the real one — the seam
    /// <c>SettingsService</c> has had since #785 and <c>ApplicationStateService</c> gained in #877. Its absence
    /// there is why the load and recovery paths had no coverage at all, so this one starts with it.
    /// </summary>
    internal AiSessionStore(string directory, ILogger<AiSessionStore>? logger = null)
    {
        _directory = directory;
        _logger = logger;
    }

    public event Action<AiSessionUnreadable>? Unreadable;

    // ---- reading ------------------------------------------------------------------------------------

    public async Task<AiSession?> LoadAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!IsWellFormedId(id))
        {
            _logger?.LogWarning("Refusing to read a session under a malformed id (#849)");
            return null;
        }

        var path = PathFor(id);
        if (!File.Exists(path)) return null;

        return await ReadAsync(id, path, cancellationToken).ConfigureAwait(false);
    }

    public async Task<IReadOnlyList<AiSessionSummary>> ListAsync(CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(_directory)) return Array.Empty<AiSessionSummary>();

        string[] files;
        try
        {
            files = Directory.GetFiles(_directory, "*.json");
        }
        catch (Exception ex)
        {
            // A directory that cannot be listed is an empty list, not an exception on the way to the panel.
            _logger?.LogWarning(ex, "Could not list the assistant-sessions directory (#849)");
            return Array.Empty<AiSessionSummary>();
        }

        var summaries = new List<AiSessionSummary>();

        foreach (var file in files)
        {
            cancellationToken.ThrowIfCancellationRequested();

            var name = Path.GetFileNameWithoutExtension(file);

            // Skip our own leavings rather than trying to read them: an `.unreadable-<stamp>.json` file is
            // kept deliberately and is by definition not loadable, and a `.tmp` is not a session yet. Listing
            // them would report the same broken transcript once per launch, forever.
            if (!IsWellFormedId(name)) continue;

            var session = await ReadAsync(name, file, cancellationToken).ConfigureAwait(false);
            if (session is null) continue;

            summaries.Add(new AiSessionSummary(
                // The FILE's identity wins over the field's. They agree in every file this build writes; where
                // they disagree the file name is what Load and Delete address, so a summary carrying the other
                // one would produce a row that cannot be opened.
                name,
                session.Name,
                session.Created,
                session.LastActive,
                session.Turns.Count,
                DistinctBookIds(session)));
        }

        // Newest-active first (§3.3). Id breaks ties so the order is total rather than merely mostly-sorted:
        // several sessions can share a timestamp after a restore, and a list that reshuffles between launches
        // is a list the reader cannot learn.
        return summaries
            .OrderByDescending(s => s.LastActive)
            .ThenBy(s => s.Id, StringComparer.Ordinal)
            .ToList();
    }

    /// <summary>
    /// The books this conversation's citations name, de-duplicated, in first-appearance order.
    ///
    /// <para>First-appearance rather than alphabetical: the row is read as "what this conversation was about",
    /// and the book it started with is the one that says that. Turns with no citation contribute nothing — a
    /// failed turn often has none.</para>
    /// </summary>
    private static IReadOnlyList<string> DistinctBookIds(AiSession session)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var ordered = new List<string>();

        foreach (var turn in session.Turns)
        {
            var bookId = turn.Citation?.BookId;
            if (string.IsNullOrWhiteSpace(bookId)) continue;
            if (seen.Add(bookId!)) ordered.Add(bookId!);
        }

        return ordered;
    }

    /// <summary>
    /// Read one file, or keep it aside and report it. Never throws for the file's own sake — only cancellation
    /// propagates.
    /// </summary>
    private async Task<AiSession?> ReadAsync(string id, string path, CancellationToken cancellationToken)
    {
        try
        {
            var json = await File.ReadAllTextAsync(path, cancellationToken).ConfigureAwait(false);
            var session = JsonSerializer.Deserialize<AiSession>(json, JsonOptions);

            // A file holding the literal token `null` deserializes to null without throwing; an empty file
            // throws. Both are "this is not a session", and both must take the preserve path — the shape of
            // the failure is not the reader's problem.
            if (session is null)
            {
                PreserveUnreadable(id, path, "the file did not contain a session");
                return null;
            }

            // Trust the file name over the field, for the reason given in ListAsync: it is what Load, Save and
            // Delete address. A mismatch means someone renamed a file by hand, and honouring it is the
            // behaviour that makes that work rather than producing a session that saves itself elsewhere.
            session.Id = id;

            // A property initializer does NOT survive an explicit null in the file: `"turns": null` sets the
            // member to null, initializer and all. Everything downstream — this store's own summary, and the
            // panel rebuilding the transcript — then meets a null collection where the type promises one, and
            // a NullReferenceException out of a listing is precisely the "cannot open the panel" failure the
            // rest of this class goes to lengths to avoid. Nothing this build writes produces such a file; a
            // hand-edit or a truncating tool does.
            session.Name ??= string.Empty;
            session.Turns ??= new List<AiTurnRecord>();
            session.Turns.RemoveAll(t => t is null);
            foreach (var turn in session.Turns)
                turn.Notices ??= new List<string>();

            return session;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            PreserveUnreadable(id, path, ex.Message);
            return null;
        }
    }

    /// <summary>
    /// Move an unreadable session aside under a timestamped name, then report it.
    ///
    /// <para>Nothing else would keep it: the next turn in that conversation rewrites the file, and the
    /// reader's transcript is gone with no way back even by hand. Milliseconds in the name, like the app-state
    /// backups, so two failed reads in one second cannot collide. (#877's pattern.)</para>
    /// </summary>
    private void PreserveUnreadable(string id, string path, string error)
    {
        var kept = path;

        try
        {
            kept = Path.Combine(
                _directory,
                $"{id}.unreadable-{DateTime.Now:yyyyMMdd-HHmmss-fff}.json");

            File.Move(path, kept, overwrite: false);

            _logger?.LogError(
                "The assistant session {Id} could not be read ({Error}) and has been kept at {Path}. "
                + "Nothing in it was deleted.", id, error, kept);
        }
        catch (Exception ex)
        {
            // Reporting still happens. A file we could not move is still a file we could not read, and the
            // panel's notice is the reader's only sign of either.
            kept = path;
            _logger?.LogWarning(ex, "Could not preserve the unreadable assistant session {Id}", id);
        }

        Unreadable?.Invoke(new AiSessionUnreadable(id, kept, error));
    }

    // ---- writing ------------------------------------------------------------------------------------

    public async Task<bool> SaveAsync(AiSession session, CancellationToken cancellationToken = default)
    {
        if (session is null) return false;

        if (!IsWellFormedId(session.Id))
        {
            _logger?.LogWarning("Refusing to write a session under a malformed id (#849)");
            return false;
        }

        string json;
        try
        {
            json = JsonSerializer.Serialize(session, JsonOptions);
        }
        catch (Exception ex)
        {
            // Serialize BEFORE touching the file. A type that cannot be written must not be able to take the
            // previous good transcript with it, which is what a stream-straight-to-the-file version would do.
            _logger?.LogError(ex, "Could not serialize assistant session {Id}", session.Id);
            return false;
        }

        var path = PathFor(session.Id);
        var temp = path + ".tmp";

        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(_directory);

            await File.WriteAllTextAsync(temp, json, cancellationToken).ConfigureAwait(false);

            // Temp then replace. The window in which a session file is half-written is the window in which a
            // crash costs the reader the whole conversation rather than the turn in flight, and it closes for
            // free.
            if (File.Exists(path)) File.Replace(temp, path, null);
            else File.Move(temp, path);

            return true;
        }
        catch (Exception ex)
        {
            // Cancellation is caught here too, unlike on the read side where it propagates. The asymmetry is
            // deliberate: a cancelled save has written nothing the caller can use, and "false, and the temp
            // file is gone" is the whole of what it needs to know — while a cancelled READ must not be
            // mistaken for a session that could not be read, which would move a perfectly good file aside.
            _logger?.LogError(ex, "Could not write assistant session {Id} to {Path}", session.Id, path);

            try
            {
                if (File.Exists(temp)) File.Delete(temp);
            }
            catch (Exception cleanupEx)
            {
                _logger?.LogWarning(cleanupEx, "Could not clean up {Path}", temp);
            }

            return false;
        }
        finally
        {
            _writeLock.Release();
        }
    }

    public Task<bool> DeleteAsync(string id, CancellationToken cancellationToken = default)
    {
        if (!IsWellFormedId(id))
        {
            _logger?.LogWarning("Refusing to delete under a malformed id (#849)");
            return Task.FromResult(false);
        }

        var path = PathFor(id);

        try
        {
            if (!File.Exists(path)) return Task.FromResult(false);

            File.Delete(path);
            _logger?.LogInformation("Deleted assistant session {Id}", id);
            return Task.FromResult(true);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Could not delete assistant session {Id}", id);
            return Task.FromResult(false);
        }
    }

    // ---- paths --------------------------------------------------------------------------------------

    private string PathFor(string id) => Path.Combine(_directory, id + ".json");

    /// <summary>
    /// Whether an id may be turned into a file name: letters, digits, <c>-</c> and <c>_</c>, at most 64 of
    /// them.
    ///
    /// <para><b>Why a store validates its own keys.</b> Every id reaching it comes from outside — a directory
    /// listing, <c>ActiveAssistantSessionId</c> in a JSON file a reader can edit, and later whatever the
    /// surface-C API hands in. <c>Path.Combine</c> with <c>"../../settings"</c> is a path outside the data
    /// directory, and the operations here are read, overwrite and delete. The check also keeps the store's own
    /// leavings out of the listing for free: <c>&lt;id&gt;.unreadable-&lt;stamp&gt;</c> contains a <c>.</c> and
    /// is rejected by the same rule.</para>
    /// </summary>
    internal static bool IsWellFormedId(string? id)
    {
        if (string.IsNullOrEmpty(id) || id.Length > 64) return false;

        foreach (var c in id)
        {
            var ok = (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9')
                     || c == '-' || c == '_';
            if (!ok) return false;
        }

        return true;
    }
}
