using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;
using CST.Avalonia.Models;

namespace CST.Avalonia.Services.Ai;

/// <summary>
/// One Assistant conversation, as it is kept on disk. (#849)
///
/// <para><b>Why a session file rather than a section of <c>application-state.json</c>.</b> That file is read
/// synchronously at launch, backed up on a timer, and holds window rectangles and file names; a transcript
/// with forty answers in it is a different weight class and a different failure mode, and everything sharing
/// that file shares its failure mode. So <see cref="ApplicationState.ActiveAssistantSessionId"/> is the only
/// thing state keeps — one string — and the conversations live one file each under
/// <c>&lt;DataDirectory&gt;/assistant-sessions/</c>.</para>
///
/// <para><b>[fsnow]</b> on the shape of the collection: <i>"One global list"</i>, not per book, and retention
/// is <i>"Forever, no cap"</i> — nothing here expires, and deletion is a deliberate per-session act
/// (<see cref="IAiSessionStore.DeleteAsync"/>). The Claude Code model this is transposed from is
/// <b>[fsnow]</b>'s own: <i>"Claude Code itself is my model and we should aim for feature parity with CC in
/// context management, named sessions, session restoration, etc."</i></para>
///
/// <para>A mutable class with settable properties rather than a positional record, for two reasons that both
/// come from how it is used: the wiring PR fills a turn in as it streams (the answer arrives in pieces, usage
/// and elapsed at the end), and the persisted app-state types next door are written the same way, so the JSON
/// behaviour is the one this codebase already reasons about.</para>
/// </summary>
public sealed class AiSession
{
    /// <summary>
    /// Shape of this file, for the migration that a "forever, no cap" retention rule eventually forces: a
    /// session written by today's build is expected to be readable years and several formats later, which is
    /// true of nothing else the app keeps. Same role as <see cref="ApplicationState.Version"/>.
    /// <b>[suggestion]</b> — nothing reads it yet, and the first thing that needs to will be glad it is there.
    /// </summary>
    public string Version { get; set; } = "1.0";

    /// <summary>
    /// Identity, and also the file name (<c>&lt;id&gt;.json</c>). Constrained to letters, digits, <c>-</c> and
    /// <c>_</c> — see <see cref="AiSessionStore.IsWellFormedId"/>, which refuses anything else rather than
    /// letting an id that arrived from a directory listing or from <c>application-state.json</c> address a
    /// path of its own choosing.
    /// </summary>
    public string Id { get; set; } = NewId();

    /// <summary>
    /// What the reader sees in the session list. <b>[fsnow]</b> chose <i>"Auto from the first turn,
    /// renamable"</i>, so this is written by the naming rule at the first turn and then by rename — never by a
    /// model. Empty until named; the store neither invents nor requires a name (naming is P3's, and a store
    /// that refused an unnamed session would make the first turn's write order significant for no reason).
    /// </summary>
    public string Name { get; set; } = string.Empty;

    /// <summary>When the conversation was started.</summary>
    public DateTimeOffset Created { get; set; }

    /// <summary>
    /// When it was last added to or renamed — the sort key of the session list, newest first, and what
    /// "restore the last session" resolves against.
    /// </summary>
    public DateTimeOffset LastActive { get; set; }

    /// <summary>The turns, oldest first — the order the panel renders and the order history is replayed in.</summary>
    public List<AiTurnRecord> Turns { get; set; } = new();

    /// <summary>
    /// Properties a NEWER build wrote that this one does not know, carried through a round-trip. (#883's
    /// mechanism, applied here.)
    ///
    /// <para>The exposure is realer for a session than for app state: a session file is rewritten at the end
    /// of every turn, so one launch of an older build over a newer build's session strips whatever that build
    /// added — silently, because an unknown property is not an error to System.Text.Json, it is simply
    /// dropped. <b>[suggestion]</b>; the cost is one member and it is what makes "forever" survive a reader
    /// who runs several builds. <see cref="AiTurnRecord.UnknownProperties"/> covers the level where new fields
    /// will actually land.</para>
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? UnknownProperties { get; set; }

    /// <summary>A fresh session id. A GUID, like <c>BookWindowState.WindowId</c>: nothing orders sessions by
    /// file name — <see cref="LastActive"/> does — so an opaque id is all this has to be.</summary>
    public static string NewId() => Guid.NewGuid().ToString();
}

/// <summary>
/// One stored turn: everything the panel shows, so a restored turn renders identically to the live one that
/// produced it. (#849)
///
/// <para>The field list is taken from <c>AiTurnViewModel</c> rather than designed — what is missing from here
/// is what a reader would find missing from a reopened conversation. Nothing has to be RECOMPUTED to write
/// one; the turn already carries all of it, with the single exception of <see cref="ReadingPosition"/>, which
/// the wiring PR captures at the start of a turn.</para>
///
/// <para><b>What is deliberately not here:</b> the rendered answer blocks (<c>AnswerMarkup.Parse</c> rebuilds
/// them from <see cref="Answer"/>, and storing a parse would freeze today's renderer into the file), the
/// rendered citation strings (the panel builds those from <see cref="Citation"/>, which is the rule that makes
/// a false citation impossible — chrome is built from bundle data, never parsed out of model output), and
/// <c>IsRunning</c> (a restored turn is never running).</para>
/// </summary>
public sealed class AiTurnRecord
{
    /// <summary>Which preset was asked for. Serialized as its NAME — see
    /// <see cref="AiSessionStore.JsonOptions"/> — because the numbers are positional and these files outlive
    /// builds that may insert a preset.</summary>
    public AiTask Task { get; set; }

    /// <summary>The reader's own question, where the preset takes one. Null for the four that do not.</summary>
    public string? Question { get; set; }

    /// <summary>
    /// The answer as the model sent it, markup and all — the same string <c>AiTurnViewModel.Answer</c> holds
    /// and Copy hands over. A partial answer from a failed or stopped turn is stored as it stands: it is on
    /// screen, so it belongs in the record.
    /// </summary>
    public string? Answer { get; set; }

    /// <summary>
    /// The model thinking aloud, kept segregated exactly as the live turn keeps it. Stored because a turn
    /// without it does not render identically — the panel offers it collapsed — and <b>never</b> merged into
    /// <see cref="Answer"/>: half-formed guesses about the text are not what the model is telling the reader.
    /// (It is also never replayed to a model; that is P1's rule, and this field is not an argument against
    /// it.)
    /// </summary>
    public string? Reasoning { get; set; }

    /// <summary>
    /// What the answer is about, as the structured reference rather than the rendered line.
    ///
    /// <para><b>[fsnow]</b>: <i>"A restored turn should be able to reopen its book and restore the selection,
    /// as something the reader chooses rather than something that happens on load."</i> That is what makes
    /// this field required in spirit even where the type allows null: <c>CitationRef</c> carries
    /// <c>BookId</c>, <c>BookName</c>, <c>NormalizedReference</c> and the page references, which is everything
    /// "take me back" (P5) needs to reopen the right book at the right paragraph. Storing the rendered string
    /// instead would leave a restored turn readable and unnavigable.</para>
    /// </summary>
    public CitationRef? Citation { get; set; }

    /// <summary>
    /// The selection summary shown beside the Stop button — display text only. The full selection is in
    /// <see cref="Sent"/>; this is the short form the panel puts on screen so a wrong subject is obvious.
    /// </summary>
    public string? Subject { get; set; }

    /// <summary>
    /// How the request was ASSEMBLED — a trimmed part, a missing optional asset, a rejected prompt edit.
    /// Facts about the input rather than failures, and a restored turn that dropped them would overstate how
    /// complete its answer was.
    /// </summary>
    public List<string> Notices { get; set; } = new();

    /// <summary>The model did not see all of the passage. The one caveat that changes how far the answer can
    /// be trusted, which is why the panel shows it in the open rather than under the notices.</summary>
    public bool IsPartialPassage { get; set; }

    /// <summary>Tokens as the panel prints them. The display string rather than the numbers, because that is
    /// what the turn holds — there is no structured usage on the view model to store.</summary>
    public string? Usage { get; set; }

    /// <summary>How long the turn took, already formatted. Kept for the reason the live turn keeps it: a
    /// reader deciding whether to ask a slow model another question wants the last one's cost in front of
    /// them.</summary>
    public string? Elapsed { get; set; }

    /// <summary>The one line that said what went wrong, when something did. Empty on a clean turn.</summary>
    public string? Status { get; set; }

    /// <summary>Whether <see cref="Status"/> is a failure rather than progress. They are drawn differently and
    /// only one of them offers Retry, so storing the line without this flag would restore an error as a
    /// progress note.</summary>
    public bool Failed { get; set; }

    /// <summary>
    /// Everything the turn sent. <b>[fsnow]</b> chose to store the prompt <i>"Yes, in full"</i> — so this is
    /// the whole of #665's record, both prompt halves and the named fields, not a summary of it.
    ///
    /// <para><b>Stored as the type, not field by field.</b> P1 (#991) is adding the replayed conversation to
    /// <c>SentContext</c>; a record that copied its parts by hand would go on loading and saving without them
    /// and nothing would fail — the reader would simply find that a restored turn's "Context sent" had lost
    /// the history. Serializing the type as a whole means that addition arrives here with no change to this
    /// file. It also makes <c>SentContext</c>'s own shape a persisted format from now on: whatever P1 adds
    /// must itself round-trip through <see cref="AiSessionStore.JsonOptions"/>, which is why the golden file
    /// in the test suite covers this member.</para>
    ///
    /// <para>It carries no secret. An API key is a header the provider adds, never part of a prompt; what is
    /// here is corpus text, the reader's own question, and the app's own instructions.</para>
    /// </summary>
    public SentContext? Sent { get; set; }

    /// <summary>Which connection answered, and which model — an answer is not attributable without them, and a
    /// session spanning a model change is the ordinary case rather than a corner of it.</summary>
    public string? ProviderId { get; set; }

    /// <inheritdoc cref="ProviderId"/>
    public string? ModelId { get; set; }

    /// <summary>When the turn was asked.</summary>
    public DateTimeOffset When { get; set; }

    /// <summary>
    /// Where the reader was reading when they asked. <b>[fsnow]</b>: <i>"The Assistant's memory should carry
    /// the same scroll context saved elsewhere: two anchors and a fraction between them."</i> — so this is
    /// #434's <see cref="ReadingPositionToken"/>, the representation <c>ApplicationState</c> already persists
    /// per book, not a pixel offset.
    ///
    /// <para>It is not redundant with <see cref="Citation"/>: the citation is where the answer POINTS, this is
    /// where the reader was STANDING. And a stored turn is exactly the case a raw offset cannot survive —
    /// everything that invalidates one (font face or size, zoom, script, window size, floating the pane) is
    /// something a reader does between sessions, while an anchor bracket is re-interpolated at restore.</para>
    ///
    /// <para>Null where it was not captured: reading positions come from the WebView, which can be unready,
    /// and a turn is worth keeping without one. Capture is the wiring PR's job — nothing in this store fills
    /// it in.</para>
    /// </summary>
    public ReadingPositionToken? ReadingPosition { get; set; }

    /// <inheritdoc cref="AiSession.UnknownProperties"/>
    [JsonExtensionData]
    public Dictionary<string, System.Text.Json.JsonElement>? UnknownProperties { get; set; }
}

/// <summary>
/// One row of the session list: enough to choose a conversation without reading it. (#849 §3.3)
///
/// <para>Separate from <see cref="AiSession"/> because listing must not mean keeping — the list is drawn from
/// every session file on disk, forever, and a reader with two hundred conversations should not have two
/// hundred transcripts in memory to pick one. The store does read the files to build these (there is no index
/// to go stale, which is the right trade while a session list is tens of files), but only these leave it.</para>
/// </summary>
/// <param name="BookIds">The distinct books this conversation's citations name, in the order they first
/// appear. What the list row shows instead of the conversation's text — a reader recognises "the one about
/// Mahāvagga" long before they recognise a name they never typed.</param>
public sealed record AiSessionSummary(
    string Id,
    string Name,
    DateTimeOffset Created,
    DateTimeOffset LastActive,
    int TurnCount,
    IReadOnlyList<string> BookIds);

/// <summary>
/// A session file that could not be read, and where it was kept instead. (#849)
///
/// <para>Reported rather than thrown: an unreadable transcript must not be able to stop the panel from
/// opening, and it must not be quietly deleted by the next save either. Both halves of that are the
/// <c>application-state.unreadable-*</c> pattern (#877), which exists because the alternative — the next save
/// writing over the only copy — is how a reader loses a session with no way back even by hand.</para>
/// </summary>
/// <param name="Id">The session the file claimed to be.</param>
/// <param name="KeptPath">Where the unreadable file now is. Nothing in it was deleted.</param>
/// <param name="Error">Why it could not be read. For the log, not for the reader.</param>
public sealed record AiSessionUnreadable(string Id, string KeptPath, string Error);
