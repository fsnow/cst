using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai;
using CST.Navigation;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// What survives a reopened Assistant conversation, and what happens when a transcript cannot be read.
/// (#849 P2)
///
/// <para>The store is the whole of the persistence layer: the panel is wired to it separately, so everything
/// here is exercised against a temp directory through the store's own directory seam — the seam
/// <c>SettingsService</c> has had since #785 and whose absence left <c>ApplicationStateService</c>'s load path
/// uncovered until #877.</para>
///
/// <para>The fixtures are built from the stored types rather than from hand-written JSON, because the question
/// these tests answer is whether a turn the panel produced comes back as the turn the panel produced. The one
/// hand-written artefact is the golden file, and it is checked in for the opposite reason: so that a change to
/// the format shows up as a diff rather than as a test that still passes.</para>
/// </summary>
public sealed class AiSessionStoreTests : IDisposable
{
    private readonly string _dir;

    public AiSessionStoreTests()
    {
        _dir = Path.Combine(Path.GetTempPath(), $"assistant-sessions-{Guid.NewGuid():N}");
        Directory.CreateDirectory(_dir);
    }

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); } catch { /* a temp directory */ }
    }

    private AiSessionStore Store() => new(_dir);

    private string PathFor(string id) => Path.Combine(_dir, id + ".json");

    // ---- fixtures ---------------------------------------------------------------------------------------

    /// <summary>Fixed timestamps, fixed ids and fixed text: a golden file cannot be built from anything else,
    /// and a round-trip assertion is easier to read when the values are recognisable.</summary>
    private static readonly DateTimeOffset Created = new(2026, 9, 12, 10, 15, 0, TimeSpan.Zero);

    private static AiCitationRecord Citation(string bookId, string name, string reference) => new()
    {
        BookId = bookId,
        BookName = name,
        NormalizedReference = reference,
        Pages = { new AiPageRecord { Edition = PageEdition.Vri, Volume = 1, Number = 23 } },
    };

    /// <summary>
    /// A conversation with the three turns that between them cover every field: a clean one with reasoning,
    /// notices, usage and a reading position; a failed follow-up with a partial answer, no citation and a
    /// replayed history; and a bare one, which is what most of the file's nullable members look like in
    /// practice.
    /// </summary>
    private static AiSession Conversation(string id = "session-one") => new()
    {
        Id = id,
        Name = "Explain · Mahāvaggapāḷi 1.1",
        Created = Created,
        LastActive = Created.AddMinutes(12),
        Turns =
        {
            new AiTurnRecord
            {
                Id = "turn-one",
                Task = AiTask.Explain,
                Question = null,
                AskedLine = "«Explain» — Mahāvaggapāḷi 1.1",
                Answer = "The paragraph opens the Mahāvagga.",
                Reasoning = "First, identify the speaker…",
                Citation = Citation("s0402m.mul.xml", "Mahāvaggapāḷi", "1.1"),
                Subject = "tena samayena buddho bhagavā",
                Notices = { "The word list was not available.", "The apparatus was empty." },
                IsPartialPassage = true,
                InputTokens = 1024,
                OutputTokens = 512,
                ElapsedMs = 4200,
                Status = "",
                Failed = false,
                Sent = new AiSentRecord
                {
                    Fields =
                    {
                        new AiSentFieldRecord { Name = "Provider", Value = "anthropic" },
                        new AiSentFieldRecord { Name = "Model", Value = "claude-opus-4-1" },
                    },
                    SystemPrompt = "You are reading Pāli.",
                    UserContent = "Passage: tena samayena…",
                },
                ProviderId = "anthropic",
                ModelId = "claude-opus-4-1",
                When = Created,
                ReadingPosition = new ReadingPositionToken
                {
                    Above = "para1",
                    Below = "para2",
                    Fraction = 0.375,
                },
            },
            new AiTurnRecord
            {
                Id = "turn-two",
                Task = AiTask.Ask,
                Question = "What does bhagavā mean here?",
                AskedLine = "«Question» — Mahāvaggapāḷi 1.1: What does bhagavā mean here?",
                // A mid-stream failure keeps what arrived — so the record does too.
                Answer = "It is an epithet",
                Citation = null,
                Status = "The provider closed the connection.",
                Failed = true,
                ElapsedMs = 63000,
                Sent = new AiSentRecord
                {
                    SystemPrompt = "You are reading Pāli.",
                    UserContent = "Passage: tena samayena… What does bhagavā mean here?",
                    // The replayed conversation, by reference. Never the earlier answer's text.
                    ReplayedTurnIds = { "turn-one" },
                },
                ProviderId = "anthropic",
                ModelId = "claude-opus-4-1",
                When = Created.AddMinutes(6),
            },
            new AiTurnRecord
            {
                Id = "turn-three",
                Task = AiTask.Translate,
                Answer = "At that time the Blessed One…",
                Citation = Citation("s0403m.mul.xml", "Cūḷavaggapāḷi", "2.4"),
                When = Created.AddMinutes(12),
            },
        },
    };

    // ---- round-trip -------------------------------------------------------------------------------------

    /// <summary>
    /// Every field a turn carries comes back. Asserted one by one rather than by comparing objects: the
    /// failure this guards against is a single member silently dropped — by a naming policy, an ignore
    /// condition, or a type that cannot be deserialized — and a whole-object comparison says only that
    /// something differs.
    /// </summary>
    [Fact]
    public async Task Every_field_of_a_turn_survives_a_round_trip()
    {
        var store = Store();
        var saved = Conversation();

        Assert.True(await store.SaveAsync(saved));
        var loaded = await store.LoadAsync(saved.Id);

        Assert.NotNull(loaded);
        Assert.Equal(saved.Id, loaded!.Id);
        Assert.Equal(saved.Name, loaded.Name);
        Assert.Equal(saved.Created, loaded.Created);
        Assert.Equal(saved.LastActive, loaded.LastActive);
        Assert.Equal(3, loaded.Turns.Count);

        var first = loaded.Turns[0];
        Assert.Equal("turn-one", first.Id);
        Assert.Equal(AiTask.Explain, first.Task);
        Assert.Null(first.Question);
        Assert.Equal("«Explain» — Mahāvaggapāḷi 1.1", first.AskedLine);
        Assert.Equal("The paragraph opens the Mahāvagga.", first.Answer);
        Assert.Equal("First, identify the speaker…", first.Reasoning);
        Assert.Equal("tena samayena buddho bhagavā", first.Subject);
        Assert.Equal(
            new[] { "The word list was not available.", "The apparatus was empty." },
            first.Notices);
        Assert.True(first.IsPartialPassage);
        Assert.Equal(1024, first.InputTokens);
        Assert.Equal(512, first.OutputTokens);
        Assert.Equal(4200, first.ElapsedMs);
        Assert.False(first.Failed);
        Assert.Equal("anthropic", first.ProviderId);
        Assert.Equal("claude-opus-4-1", first.ModelId);
        Assert.Equal(Created, first.When);

        // The citation is the structured reference, not the rendered line — it is what makes "take me back"
        // (P5) possible at all. [fsnow]: "A restored turn should be able to reopen its book and restore the
        // selection, as something the reader chooses rather than something that happens on load."
        Assert.NotNull(first.Citation);
        Assert.Equal("s0402m.mul.xml", first.Citation!.BookId);
        Assert.Equal("Mahāvaggapāḷi", first.Citation.BookName);
        Assert.Equal("1.1", first.Citation.NormalizedReference);
        var page = Assert.Single(first.Citation.Pages);
        Assert.Equal(PageEdition.Vri, page.Edition);
        Assert.Equal(1, page.Volume);
        Assert.Equal(23, page.Number);

        // [fsnow]: "The Assistant's memory should carry the same scroll context saved elsewhere: two anchors
        // and a fraction between them." — #434's token, not a pixel offset.
        Assert.NotNull(first.ReadingPosition);
        Assert.Equal("para1", first.ReadingPosition!.Above);
        Assert.Equal("para2", first.ReadingPosition.Below);
        Assert.Equal(0.375, first.ReadingPosition.Fraction);

        var failed = loaded.Turns[1];
        Assert.True(failed.Failed);
        Assert.Equal("The provider closed the connection.", failed.Status);
        // Partial text stands. A failed turn is not an empty one, and a record that dropped what arrived
        // would restore the conversation as something the reader never saw.
        Assert.Equal("It is an epithet", failed.Answer);
        Assert.Equal("What does bhagavā mean here?", failed.Question);
        Assert.Equal(63000, failed.ElapsedMs);
        Assert.Null(failed.Citation);
        Assert.Null(failed.ReadingPosition);
        Assert.Null(failed.InputTokens);
        Assert.Empty(failed.Notices);

        var bare = loaded.Turns[2];
        Assert.Equal(AiTask.Translate, bare.Task);
        Assert.Null(bare.Sent);
        Assert.Null(bare.Reasoning);
        Assert.Null(bare.Status);
        Assert.False(bare.IsPartialPassage);
    }

    /// <summary>
    /// The sent prompt is stored whole. <b>[fsnow]</b> chose <i>"Yes, in full"</i>.
    ///
    /// <para>In a stored type of the format's own, not the live <c>SentContext</c>: that one is a positional
    /// record with no extension data and, since #991, a computed <c>HasHistory</c> getter, so storing it
    /// directly both wrote a derived boolean into a "forever" format and dropped a newer build's nested field
    /// on the first rewrite. Mapping the two is the wiring layer's job.</para>
    /// </summary>
    [Fact]
    public async Task The_sent_prompt_is_stored_in_full()
    {
        var store = Store();
        var saved = Conversation();

        await store.SaveAsync(saved);
        var loaded = await store.LoadAsync(saved.Id);

        var sent = loaded!.Turns[0].Sent;
        Assert.NotNull(sent);
        Assert.Equal("You are reading Pāli.", sent!.SystemPrompt);
        Assert.Equal("Passage: tena samayena…", sent.UserContent);
        Assert.Equal(2, sent.Fields.Count);
        Assert.Equal("Provider", sent.Fields[0].Name);
        Assert.Equal("anthropic", sent.Fields[0].Value);
        Assert.Equal("Model", sent.Fields[1].Name);
        Assert.Equal("claude-opus-4-1", sent.Fields[1].Value);
    }

    /// <summary>
    /// The replayed conversation is stored by reference, and the ids resolve.
    ///
    /// <para>#991 replays every earlier turn that has an answer, so storing that history by value puts every
    /// earlier answer inside every later turn — turn 30 of a 30-turn conversation carrying 29 of them, 435
    /// copies across the file. None of it is new information: a written turn never changes, so the ids plus
    /// each referenced turn's <c>AskedLine</c> and <c>Answer</c> rebuild what was sent exactly.</para>
    /// </summary>
    [Fact]
    public async Task The_replayed_history_is_stored_as_turn_ids_rather_than_as_copies()
    {
        var store = Store();
        await store.SaveAsync(Conversation());
        var loaded = await store.LoadAsync("session-one");

        var followUp = loaded!.Turns[1];
        Assert.Equal(new[] { "turn-one" }, followUp.Sent!.ReplayedTurnIds);

        // Every replayed id names a turn in this session, and that turn carries what the replay needs.
        var byId = loaded.Turns.ToDictionary(t => t.Id);
        foreach (var id in followUp.Sent.ReplayedTurnIds)
        {
            Assert.True(byId.ContainsKey(id));
            Assert.False(string.IsNullOrEmpty(byId[id].AskedLine));
            Assert.False(string.IsNullOrEmpty(byId[id].Answer));
        }

        // And the earlier answer appears in the file exactly once — in the turn that produced it, nowhere in
        // the turn that replayed it. That is the whole point of the ids.
        var onDisk = File.ReadAllText(PathFor("session-one"));
        Assert.Equal(1, onDisk.Split("The paragraph opens the Mahāvagga.").Length - 1);
        Assert.Equal(1, onDisk.Split("«Explain» — Mahāvaggapāḷi 1.1").Length - 1);
    }

    /// <summary>
    /// A property a NEWER build wrote is carried through rather than stripped — <b>at every level</b>,
    /// including inside a stored citation and a stored sent record. (#883's mechanism.)
    ///
    /// <para>A session file is rewritten at the end of every turn, so without this one launch of an older
    /// build over a newer build's session silently drops whatever that build added, and <b>[fsnow]</b>'s
    /// <i>"Forever, no cap"</i> means these files outlive the builds that wrote them by design. The nested
    /// levels are here because the first cut stored the live positional records, which have no extension data:
    /// a review probe showed a newer build's field inside <c>sent</c> vanishing on the first rewrite while the
    /// outer levels round-tripped perfectly.</para>
    /// </summary>
    [Fact]
    public async Task A_property_this_build_does_not_know_survives_a_round_trip_at_every_level()
    {
        File.WriteAllText(PathFor("session-future"), """
            {
              "version": "1.0",
              "id": "session-future",
              "name": "From a later build",
              "somethingNewer": { "kept": true },
              "turns": [
                {
                  "task": "Explain",
                  "answer": "Hello",
                  "turnFieldFromLater": 7,
                  "citation": {
                    "bookId": "s0402m.mul.xml",
                    "bookName": "Mahāvaggapāḷi",
                    "normalizedReference": "1.1",
                    "pages": [ { "edition": "Vri", "volume": 1, "number": 2, "pageFieldFromLater": "p" } ],
                    "citationFieldFromLater": true
                  },
                  "sent": {
                    "fields": [ { "name": "Provider", "value": "x", "fieldFieldFromLater": 1 } ],
                    "systemPrompt": "s",
                    "userContent": "u",
                    "sentFieldFromLater": [ 1, 2 ]
                  }
                }
              ],
              "compactions": [ { "when": "2026-09-12T10:15:00+00:00", "summary": "s",
                                 "summarisedTurnIds": [ "t" ], "compactionFieldFromLater": 0 } ]
            }
            """);

        var store = Store();
        var loaded = await store.LoadAsync("session-future");
        Assert.NotNull(loaded);
        await store.SaveAsync(loaded!);

        var rewritten = File.ReadAllText(PathFor("session-future"));
        Assert.Contains("somethingNewer", rewritten);
        Assert.Contains("turnFieldFromLater", rewritten);
        Assert.Contains("citationFieldFromLater", rewritten);
        Assert.Contains("pageFieldFromLater", rewritten);
        Assert.Contains("sentFieldFromLater", rewritten);
        Assert.Contains("fieldFieldFromLater", rewritten);
        Assert.Contains("compactionFieldFromLater", rewritten);
    }

    /// <summary>The P4 slot round-trips, so the format does not have to change when compaction arrives. Nothing
    /// writes it yet. <b>[fsnow]</b>: <i>"Manual and auto at a fraction of context length"</i>,
    /// <i>"Last 4 turns"</i>.</summary>
    [Fact]
    public async Task A_compaction_summary_round_trips()
    {
        var store = Store();
        var session = Conversation();
        session.Compactions.Add(new AiCompactionRecord
        {
            When = Created.AddMinutes(30),
            Summary = "Twelve earlier turns, about the opening of the Mahāvagga.",
            SummarisedTurnIds = { "turn-one", "turn-two" },
        });

        await store.SaveAsync(session);
        var loaded = await store.LoadAsync(session.Id);

        var compaction = Assert.Single(loaded!.Compactions);
        Assert.Equal(Created.AddMinutes(30), compaction.When);
        Assert.Equal("Twelve earlier turns, about the opening of the Mahāvagga.", compaction.Summary);
        Assert.Equal(new[] { "turn-one", "turn-two" }, compaction.SummarisedTurnIds);
    }

    /// <summary>An unnamed session is legal. Naming is P3's ("Auto from the first turn, renamable"), and a
    /// store that required a name would make the first turn's write order significant for no reason.</summary>
    [Fact]
    public async Task A_session_with_no_turns_and_no_name_round_trips()
    {
        var store = Store();
        var session = new AiSession { Id = "empty-one", Created = Created, LastActive = Created };

        Assert.True(await store.SaveAsync(session));
        var loaded = await store.LoadAsync("empty-one");

        Assert.NotNull(loaded);
        Assert.Equal(string.Empty, loaded!.Name);
        Assert.Empty(loaded.Turns);
        Assert.Empty(loaded.Compactions);
    }

    [Fact]
    public async Task A_session_that_was_never_written_loads_as_null()
    {
        Assert.Null(await Store().LoadAsync("never-written"));
    }

    /// <summary>
    /// A file whose collections are an explicit <c>null</c> loads as empty ones rather than as a
    /// <c>NullReferenceException</c> two layers later.
    ///
    /// <para>A property initializer does not survive an explicit null in the file — <c>"turns": null</c> sets
    /// the member to null, initializer and all — and the crash then lands in the listing, or in the panel
    /// rebuilding the transcript, or in <c>Describe(citation)</c> iterating the pages. Nothing this build
    /// writes produces such a file; a hand-edit does, and a hand-edited session is a thing a reader may well
    /// try once the format is documented.</para>
    /// </summary>
    [Fact]
    public async Task Collections_written_as_null_load_as_empty()
    {
        File.WriteAllText(PathFor("session-nulls"), """
            {
              "id": "session-nulls",
              "name": null,
              "compactions": null,
              "turns": [
                {
                  "task": "Explain",
                  "notices": null,
                  "citation": { "bookId": "b", "bookName": "n", "normalizedReference": "1", "pages": null },
                  "sent": { "fields": null, "systemPrompt": null, "userContent": null, "replayedTurnIds": null }
                },
                null
              ]
            }
            """);

        var store = Store();
        var loaded = await store.LoadAsync("session-nulls");

        Assert.NotNull(loaded);
        Assert.Equal(string.Empty, loaded!.Name);
        Assert.Empty(loaded.Compactions);
        Assert.Single(loaded.Turns);

        var turn = loaded.Turns[0];
        Assert.Empty(turn.Notices);
        Assert.Empty(turn.Citation!.Pages);
        Assert.Empty(turn.Sent!.Fields);
        Assert.Empty(turn.Sent.ReplayedTurnIds);
        Assert.Equal(string.Empty, turn.Sent.SystemPrompt);
        // A turn with no id of its own gets one, so nothing can point at "" and mean two different turns.
        Assert.False(string.IsNullOrEmpty(turn.Id));

        var row = Assert.Single(await store.ListAsync());
        Assert.Equal(1, row.TurnCount);
        Assert.Equal(new[] { "b" }, row.BookIds);
    }

    /// <summary>An empty status and a missing one are one thing in the file, so a clean turn compares equal to
    /// a clean turn. The live view model writes <c>""</c>; the record folds it to null.</summary>
    [Fact]
    public void An_empty_status_is_stored_as_no_status()
    {
        var turn = new AiTurnRecord { Status = "" };
        Assert.Null(turn.Status);

        var json = JsonSerializer.Serialize(turn, AiSessionStore.JsonOptions);
        Assert.DoesNotContain("status", json);

        var back = JsonSerializer.Deserialize<AiTurnRecord>(
            """{ "status": "" }""", AiSessionStore.JsonOptions);
        Assert.Null(back!.Status);
    }

    // ---- the list ---------------------------------------------------------------------------------------

    /// <summary>Newest-active first (§3.3) — the order the session picker shows, and what "restore the last
    /// session" resolves against.</summary>
    [Fact]
    public async Task Sessions_are_listed_newest_active_first()
    {
        var store = Store();

        await store.SaveAsync(new AiSession { Id = "older", LastActive = Created });
        await store.SaveAsync(new AiSession { Id = "newest", LastActive = Created.AddHours(3) });
        await store.SaveAsync(new AiSession { Id = "middle", LastActive = Created.AddHours(1) });

        var rows = await store.ListAsync();

        Assert.Equal(new[] { "newest", "middle", "older" }, rows.Select(r => r.Id));
    }

    /// <summary>
    /// A row says what the conversation was about: the distinct books its citations name, in the order they
    /// first appear — which a reader recognises long before a name they never typed.
    /// </summary>
    [Fact]
    public async Task A_row_names_the_distinct_books_its_citations_name()
    {
        var store = Store();
        var session = Conversation();

        // A fourth turn back in the first book: the list must not repeat it, and must not reorder on account
        // of it either.
        session.Turns.Add(new AiTurnRecord
        {
            Id = "turn-four",
            Task = AiTask.Grammar,
            Citation = Citation("s0402m.mul.xml", "Mahāvaggapāḷi", "1.2"),
            When = Created.AddMinutes(20),
        });

        await store.SaveAsync(session);
        var row = Assert.Single(await store.ListAsync());

        Assert.Equal("session-one", row.Id);
        Assert.Equal("Explain · Mahāvaggapāḷi 1.1", row.Name);
        Assert.Equal(Created, row.Created);
        Assert.Equal(Created.AddMinutes(12), row.LastActive);
        Assert.Equal(4, row.TurnCount);
        // The failed turn has no citation and contributes nothing, which is the ordinary case rather than a
        // corner of it: a turn that failed before the context was assembled has no book to name.
        Assert.Equal(new[] { "s0402m.mul.xml", "s0403m.mul.xml" }, row.BookIds);
    }

    [Fact]
    public async Task Listing_a_directory_that_is_not_there_is_not_an_error()
    {
        var absent = Path.Combine(_dir, "not-created-yet");
        Assert.Empty(await new AiSessionStore(absent).ListAsync());
    }

    // ---- delete -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Delete_removes_the_file()
    {
        var store = Store();
        await store.SaveAsync(Conversation());

        Assert.True(await store.DeleteAsync("session-one"));
        Assert.False(File.Exists(PathFor("session-one")));
        Assert.Empty(await store.ListAsync());
    }

    /// <summary>
    /// Deleting a session that is not there is not an error.
    ///
    /// <para>The id can only have come from a list the reader was looking at or from
    /// <c>ActiveAssistantSessionId</c>, and both can name a session that has since gone — a second window, a
    /// file deleted by hand. An error for work already done is worse than silence.</para>
    /// </summary>
    [Fact]
    public async Task Deleting_a_session_that_is_not_there_is_not_an_error()
    {
        Assert.False(await Store().DeleteAsync("never-existed"));
    }

    // ---- unreadable files -------------------------------------------------------------------------------

    /// <summary>
    /// A transcript that cannot be read is kept, reported, and does not throw.
    ///
    /// <para>Nothing else would keep it: the next turn in that conversation rewrites the file. The panel's
    /// notice is what the event is for — the log alone leaves the reader with an empty panel and no
    /// explanation. (#877's pattern, on a file whose loss is a conversation rather than a window layout.)</para>
    /// </summary>
    [Fact]
    public async Task A_truncated_session_file_is_kept_aside_and_reported()
    {
        File.WriteAllText(PathFor("session-broken"), """{ "id": "session-broken", "turns": [ { "task": """);

        var store = Store();
        var reports = new List<AiSessionUnreadable>();
        store.Unreadable += reports.Add;

        Assert.Null(await store.LoadAsync("session-broken"));

        var report = Assert.Single(reports);
        Assert.Equal("session-broken", report.Id);
        Assert.False(string.IsNullOrWhiteSpace(report.Error));

        var kept = Directory.GetFiles(_dir, "session-broken.unreadable-*.json");
        Assert.Single(kept);
        Assert.Equal(kept[0], report.KeptPath);
        Assert.Contains("session-broken", File.ReadAllText(kept[0]));
        Assert.False(File.Exists(PathFor("session-broken")));
    }

    /// <summary>A file holding the literal token <c>null</c> deserializes to null without throwing, where an
    /// empty one throws. Both are "this is not a session", and both must take the preserve path — which half
    /// of System.Text.Json's behaviour was hit is not the reader's problem.</summary>
    [Fact]
    public async Task A_session_file_holding_null_is_kept_aside_too()
    {
        File.WriteAllText(PathFor("session-null"), "null");

        var store = Store();
        var reports = new List<AiSessionUnreadable>();
        store.Unreadable += reports.Add;

        Assert.Null(await store.LoadAsync("session-null"));
        Assert.Single(reports);
        Assert.Single(Directory.GetFiles(_dir, "session-null.unreadable-*.json"));
    }

    /// <summary>One broken transcript must not cost the reader the list of the others.</summary>
    [Fact]
    public async Task An_unreadable_file_does_not_cost_the_listing_the_other_sessions()
    {
        var store = Store();
        await store.SaveAsync(new AiSession { Id = "good-one", LastActive = Created });
        File.WriteAllText(PathFor("bad-one"), "{ not json");

        var reports = new List<AiSessionUnreadable>();
        store.Unreadable += reports.Add;

        var rows = await store.ListAsync();

        Assert.Equal("good-one", Assert.Single(rows).Id);
        Assert.Equal("bad-one", Assert.Single(reports).Id);
    }

    /// <summary>
    /// A kept file is never listed or re-reported. Without this the same broken transcript would be found,
    /// moved and announced once per launch — and each launch would leave another copy of it.
    /// </summary>
    [Fact]
    public async Task A_kept_unreadable_file_is_not_itself_read_as_a_session()
    {
        var store = Store();
        File.WriteAllText(PathFor("bad-one"), "{ not json");

        var reports = new List<AiSessionUnreadable>();
        store.Unreadable += reports.Add;

        Assert.Empty(await store.ListAsync());
        Assert.Single(reports);

        // Second pass: the kept file is sitting in the directory, and is not a session.
        reports.Clear();
        Assert.Empty(await store.ListAsync());
        Assert.Empty(reports);
        Assert.Single(Directory.GetFiles(_dir, "bad-one.unreadable-*.json"));
    }

    // ---- writing ----------------------------------------------------------------------------------------

    /// <summary>The temp file is an implementation detail and must not outlive the save — a directory the
    /// listing walks is no place to leave litter, and a <c>.tmp</c> left behind is the visible half of a
    /// promote that did not happen.</summary>
    [Fact]
    public async Task A_save_leaves_no_temporary_file_behind()
    {
        var store = Store();

        await store.SaveAsync(Conversation());
        await store.SaveAsync(Conversation());

        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
        Assert.Single(Directory.GetFiles(_dir, "*.json"));
    }

    /// <summary>
    /// A leftover temp file from an interrupted save is never promoted over the session that is there.
    ///
    /// <para>This is the shape of the bug STATE-2 fixed in <c>ApplicationStateService</c>, where two saves
    /// shared one <c>.tmp</c> path and a half-written file could be promoted over good state. Here the write
    /// always produces the temp's contents itself before replacing, so a stale temp is overwritten rather than
    /// trusted.</para>
    /// </summary>
    [Fact]
    public async Task A_leftover_temporary_file_is_never_promoted_over_a_good_session()
    {
        var store = Store();
        await store.SaveAsync(Conversation());

        File.WriteAllText(PathFor("session-one") + ".tmp", "{ half a session");

        var again = Conversation();
        again.Name = "Renamed";
        Assert.True(await store.SaveAsync(again));

        var loaded = await store.LoadAsync("session-one");
        Assert.NotNull(loaded);
        Assert.Equal("Renamed", loaded!.Name);
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));
    }

    /// <summary>
    /// A save that fails part way through leaves the session that was there byte-identical, and promotes
    /// nothing.
    ///
    /// <para><b>Fault injection rather than inspection</b>, because the leftover-temp test above would pass an
    /// implementation with no atomicity at all: it only shows that a stale temp is not trusted. Here the write
    /// of the temp file itself is made to fail — the temp path is occupied by a directory, which no
    /// <c>WriteAllText</c> on any platform can write over — after a good session is already on disk. What the
    /// reader must not lose is the conversation they already had.</para>
    /// </summary>
    [Fact]
    public async Task A_save_that_fails_mid_write_leaves_the_previous_session_untouched()
    {
        var store = Store();
        await store.SaveAsync(Conversation());

        var before = File.ReadAllBytes(PathFor("session-one"));

        // Occupy the temp path with a directory: the write cannot produce the new contents, so nothing can be
        // promoted over the good file.
        Directory.CreateDirectory(PathFor("session-one") + ".tmp");

        var doomed = Conversation();
        doomed.Name = "Should never be written";
        Assert.False(await store.SaveAsync(doomed));

        Assert.Equal(before, File.ReadAllBytes(PathFor("session-one")));
        var loaded = await store.LoadAsync("session-one");
        Assert.Equal("Explain · Mahāvaggapāḷi 1.1", loaded!.Name);

        // And the store is still usable once the obstruction is gone.
        Directory.Delete(PathFor("session-one") + ".tmp");
        Assert.True(await store.SaveAsync(doomed));
    }

    /// <summary>
    /// A cancelled token is reported, not thrown — and the write lock survives it.
    ///
    /// <para>The wiring saves at the end of a turn with the turn's own token, which is the one the reader may
    /// have just cancelled by pressing Stop. A store whose contract says "reported, never thrown" must not
    /// answer that with a <c>TaskCanceledException</c>, and must not leave its own lock held: the first cut
    /// took the lock outside the try and did both.</para>
    /// </summary>
    [Fact]
    public async Task A_cancelled_save_is_reported_rather_than_thrown()
    {
        var store = Store();
        await store.SaveAsync(Conversation());
        var before = File.ReadAllBytes(PathFor("session-one"));

        using var cancelled = new CancellationTokenSource();
        cancelled.Cancel();

        var renamed = Conversation();
        renamed.Name = "Not saved";
        Assert.False(await store.SaveAsync(renamed, cancelled.Token));

        Assert.Equal(before, File.ReadAllBytes(PathFor("session-one")));
        Assert.Empty(Directory.GetFiles(_dir, "*.tmp"));

        // The lock was released, so the next save still works. Without this the panel would simply stop
        // persisting for the rest of the session, silently.
        Assert.True(await store.SaveAsync(renamed));
        Assert.Equal("Not saved", (await store.LoadAsync("session-one"))!.Name);
    }

    /// <summary>
    /// An id that would address a path of its own choosing is refused, on every operation.
    ///
    /// <para>Every id reaching the store comes from outside it — a directory listing,
    /// <c>ActiveAssistantSessionId</c> in a file the reader can edit, and later whatever the local API hands
    /// in — and the operations here are read, overwrite and delete.</para>
    /// </summary>
    [Theory]
    [InlineData("../escaped")]
    [InlineData("../../settings")]
    [InlineData("sub/dir")]
    [InlineData("has space")]
    [InlineData("")]
    public async Task An_id_that_is_not_a_plain_file_name_is_refused(string id)
    {
        var store = Store();

        Assert.False(await store.SaveAsync(new AiSession { Id = id }));
        Assert.Null(await store.LoadAsync(id));
        Assert.False(await store.DeleteAsync(id));

        Assert.Empty(Directory.GetFiles(_dir, "*", SearchOption.AllDirectories));
    }

    // ---- the format itself ------------------------------------------------------------------------------

    /// <summary>
    /// The on-disk format, pinned to a checked-in file.
    ///
    /// <para>Everything else here would still pass if the format changed wholesale — a round-trip is happy as
    /// long as both ends move together, which is exactly what makes an old session file stop loading while the
    /// suite stays green. This is the test that makes such a change a <b>diff</b>, which is the only form in
    /// which a "forever, no cap" format change can be reviewed.</para>
    ///
    /// <para><b>#991 will not move it.</b> An earlier version of this comment predicted that landing the
    /// conversation replay would change the format, because the record stored the live <c>SentContext</c>. A
    /// review probe showed what that change would actually have been — a leaked <c>"hasHistory": false</c> on
    /// every stored turn, from the computed getter #991 adds — and the format now owns its own types, so
    /// nothing on the live side reaches the file. A failure here means someone changed the stored shape:
    /// regenerate from the path the failure names, and read the diff before accepting it.</para>
    ///
    /// <para>It uses <c>AiSessionStore.JsonOptions</c> rather than a copy: a mirrored copy cannot detect drift
    /// from the original, which is the one thing it most needs to detect. (#787)</para>
    /// </summary>
    [Fact]
    public void The_on_disk_format_matches_the_golden_file()
    {
        var session = Conversation("golden-session");
        session.Compactions.Add(new AiCompactionRecord
        {
            When = Created.AddMinutes(30),
            Summary = "Two earlier turns, about the opening of the Mahāvagga.",
            SummarisedTurnIds = { "turn-one", "turn-two" },
        });

        var actual = Normalize(JsonSerializer.Serialize(session, AiSessionStore.JsonOptions));

        var goldenPath = Path.Combine(
            RepoRoot(), "src", "CST.Avalonia.Tests", "Services", "Ai", "assistant-session.golden.json");

        if (!File.Exists(goldenPath) || Normalize(File.ReadAllText(goldenPath)) != actual)
        {
            var written = Path.Combine(Path.GetTempPath(), "assistant-session.actual.json");
            File.WriteAllText(written, actual);

            Assert.Fail(
                $"The stored session format no longer matches {goldenPath}. "
                + $"The format this build writes has been put at {written}; review the diff, and if the "
                + "change is intended copy it over the golden file.");
        }

        // And the golden file loads back — a format pinned but unreadable would be worse than no pin at all.
        var loaded = JsonSerializer.Deserialize<AiSession>(
            File.ReadAllText(goldenPath), AiSessionStore.JsonOptions);

        Assert.NotNull(loaded);
        Assert.Equal(3, loaded!.Turns.Count);
        Assert.Equal("Mahāvaggapāḷi", loaded.Turns[0].Citation!.BookName);
        Assert.Equal(0.375, loaded.Turns[0].ReadingPosition!.Fraction);
        Assert.NotNull(loaded.Turns[0].Sent);
        Assert.Equal(new[] { "turn-one" }, loaded.Turns[1].Sent!.ReplayedTurnIds);
        Assert.Single(loaded.Compactions);
    }

    /// <summary>
    /// Enums are written as names. <c>AiTask</c> and <c>PageEdition</c> are positional, and a session file
    /// written today will be read by builds that may have inserted a preset or an edition: numbers would
    /// silently re-point, names cannot.
    /// </summary>
    [Fact]
    public void Enums_are_written_as_names_rather_than_numbers()
    {
        var json = JsonSerializer.Serialize(Conversation(), AiSessionStore.JsonOptions);

        Assert.Contains("\"task\": \"Explain\"", json);
        Assert.Contains("\"edition\": \"Vri\"", json);
    }

    /// <summary>
    /// Pāli is written as Pāli. These files are read by hand when an answer looks wrong, which is the moment
    /// the passage needs to be legible rather than a page of <c>ā</c>. The HTML-sensitive characters are
    /// still escaped — <c>JavaScriptEncoder</c> escapes those whatever range it is given.
    /// </summary>
    [Fact]
    public void Pali_text_is_stored_readable_and_html_sensitive_characters_are_still_escaped()
    {
        var session = Conversation();
        session.Turns[0].Answer = "bhagavā <b>&</b>";

        var json = JsonSerializer.Serialize(session, AiSessionStore.JsonOptions);

        Assert.Contains("bhagavā", json);
        Assert.DoesNotContain("\\u0101", json);   // ā, escaped by the default encoder
        Assert.DoesNotContain("<b>", json);       // still escaped, whatever range is allowed
        Assert.Contains("\\u003C", json);
    }

    /// <summary>
    /// The one line #849 adds to <c>application-state.json</c>: which conversation to reopen.
    ///
    /// <para><b>[fsnow]</b> chose <i>"Restore the last session silently"</i>. Asserted through the state
    /// service's own options — the same no-mirroring rule (#787) — because the risk is the usual one for that
    /// file: an ignore condition or naming policy that drops the property while every round-trip in the suite
    /// still passes.</para>
    /// </summary>
    [Fact]
    public void The_active_session_id_round_trips_through_the_state_file()
    {
        var json = JsonSerializer.Serialize(
            new ApplicationState { ActiveAssistantSessionId = "session-one" },
            ApplicationStateService.JsonOptions);

        Assert.Contains("\"activeAssistantSessionId\": \"session-one\"", json);

        var state = JsonSerializer.Deserialize<ApplicationState>(json, ApplicationStateService.JsonOptions);
        Assert.Equal("session-one", state!.ActiveAssistantSessionId);

        // No session yet is null, not an empty string: the store is asked for a session only when there is one
        // to ask for, and WhenWritingDefault keeps the property out of the file entirely until there is.
        var fresh = JsonSerializer.Serialize(new ApplicationState(), ApplicationStateService.JsonOptions);
        Assert.DoesNotContain("activeAssistantSessionId", fresh);
    }

    // ---- helpers ----------------------------------------------------------------------------------------

    private static string Normalize(string json) => json.Replace("\r\n", "\n").TrimEnd('\n');

    /// <summary>Walk up to the repository root, the way the other tests that read checked-in files do.</summary>
    private static string RepoRoot()
    {
        var dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null && !Directory.Exists(Path.Combine(dir.FullName, "src")))
            dir = dir.Parent;
        Assert.NotNull(dir);
        return dir!.FullName;
    }
}
