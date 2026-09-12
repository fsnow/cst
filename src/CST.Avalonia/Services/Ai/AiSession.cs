using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;
using CST.Avalonia.Models;
using CST.Navigation;

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
/// <para><b>Every type in this file is a STORED type, owned by the format rather than shared with the live
/// pipeline.</b> The first draft stored the live <c>SentContext</c> and <c>CitationRef</c> directly, and a
/// review probe showed what that costs: those are positional records with no extension data, so a field a
/// newer build added inside one was silently dropped the first time an older build rewrote the file — the
/// opposite of what the extension data on the outer types promises — and a computed <c>=&gt;</c> property on
/// such a record (#991 adds <c>SentContext.HasHistory</c>) is serialized like any other, freezing a derived
/// boolean into a format meant to last. Mapping between the live types and these is the wiring layer's job,
/// and it is the price of being able to change either side without changing the other.</para>
///
/// <para>Mutable classes with settable properties rather than positional records, for three reasons that all
/// come from how they are used: the wiring PR fills a turn in as it streams, the persisted app-state types
/// next door are written the same way, and <see cref="JsonExtensionData"/> wants a settable property.</para>
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
    /// Summaries that stand in for older turns in what the model is SENT. Empty until P4 exists.
    ///
    /// <para>A slot rather than a feature: compaction (<b>[fsnow]</b>: <i>"Manual and auto at a fraction of
    /// context length"</i>, <i>"Last 4 turns"</i> verbatim) replaces a run of turns in the request with one
    /// summary while the transcript on screen keeps every turn. That summary is model output that cost a call
    /// and cannot be recomputed from the file, so it belongs in the file — and it belongs at the session level
    /// rather than on a turn, because it is about a span of them.</para>
    ///
    /// <para>Here now because adding it later would mean a format change to every session on disk; empty now
    /// because nothing writes it. <b>[suggestion]</b>.</para>
    /// </summary>
    public List<AiCompactionRecord> Compactions { get; set; } = new();

    /// <summary>
    /// Properties a NEWER build wrote that this one does not know, carried through a round-trip. (#883's
    /// mechanism.)
    ///
    /// <para>The exposure is realer for a session than for app state: a session file is rewritten at the end
    /// of every turn, so one launch of an older build over a newer build's session strips whatever that build
    /// added — silently, because an unknown property is not an error to System.Text.Json, it is simply
    /// dropped. <b>[suggestion]</b>; the cost is one member and it is what makes "forever" survive a reader
    /// who runs several builds.</para>
    ///
    /// <para><b>Extension data is per-object and covers only its own level</b> — which is why every stored
    /// type in this file carries one, down to a page reference and a named field. A review probe demonstrated
    /// the failure it prevents: with extension data only on the session and the turn, a newer build's field
    /// inside <c>sent</c> was dropped on the first rewrite while the outer levels round-tripped perfectly.</para>
    /// </summary>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownProperties { get; set; }

    /// <summary>A fresh id, for a session or a turn. A GUID, like <c>BookWindowState.WindowId</c>: nothing
    /// orders sessions by file name — <see cref="LastActive"/> does — so an opaque id is all this has to
    /// be.</summary>
    public static string NewId() => Guid.NewGuid().ToString();
}

/// <summary>
/// One stored turn: everything the panel shows, so a restored turn renders identically to the live one that
/// produced it. (#849)
///
/// <para>The field list is taken from <c>AiTurnViewModel</c> rather than designed — what is missing from here
/// is what a reader would find missing from a reopened conversation. What is <b>not</b> taken from it is the
/// display formatting: usage and elapsed are stored as numbers (see <see cref="InputTokens"/>,
/// <see cref="ElapsedMs"/>), because the view model holds them already run through <c>FormatUsage</c> and
/// <c>FormatElapsed</c> and storing those strings would freeze today's wording — and today's culture's digit
/// grouping — into the file.</para>
///
/// <para><b>What the wiring PR has to carry that the view model does not hold today:</b> the
/// <c>AiUsageReport</c> from the <c>Usage</c> event (<c>Handle</c> formats it and drops it), the
/// <c>Stopwatch</c>'s own value, the structured <c>CitationRef</c> from the <c>Started</c> event (the view
/// model keeps only the rendered lines), and the provider and model ids — which are on neither
/// <c>AiTurnContext</c> nor the view model, so carrying them needs an orchestrator change rather than a
/// read from the panel.</para>
///
/// <para><b>What is deliberately not here:</b> the rendered answer blocks (<c>AnswerMarkup.Parse</c> rebuilds
/// them from <see cref="Answer"/>, and storing a parse would freeze today's renderer into the file), the
/// rendered citation strings (the panel builds those from <see cref="Citation"/>, which is the rule that makes
/// a false citation impossible — chrome is built from bundle data, never parsed out of model output), and
/// <c>IsRunning</c> (a restored turn is never running).</para>
/// </summary>
public sealed class AiTurnRecord
{
    /// <summary>
    /// Identity within the session, so other records can point at this turn instead of copying it — see
    /// <see cref="AiSentRecord.ReplayedTurnIds"/> and <see cref="AiCompactionRecord.SummarisedTurnIds"/>.
    /// Generated the way a session id is; never a file name, so nothing validates it.
    /// </summary>
    public string Id { get; set; } = AiSession.NewId();

    /// <summary>Which preset was asked for. Serialized as its NAME — see
    /// <see cref="AiSessionStore.JsonOptions"/> — because the numbers are positional and these files outlive
    /// builds that may insert a preset.</summary>
    public AiTask Task { get; set; }

    /// <summary>The reader's own question, where the preset takes one. Null for the four that do not.</summary>
    public string? Question { get; set; }

    /// <summary>
    /// The question side of this turn as it is replayed to a model — the panel's
    /// <c>DescribeAsked</c> output: preset label, citation line, and the reader's words where there were any.
    /// (#991)
    ///
    /// <para><b>Stored even though it is derivable</b>, which is the opposite of the rule the rest of this
    /// record follows. The reason is that it is derivable from <i>today's</i> wording: a later change to
    /// <c>DescribeAsked</c> — a different dash, the preset label dropped — would make every reconstructed
    /// history differ from what was actually sent, and a record of what a model saw that quietly re-renders
    /// itself is not a record. A few dozen bytes a turn buys that.</para>
    /// </summary>
    public string? AskedLine { get; set; }

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
    /// (It is also never replayed to a model; that is #991's rule, and this field is not an argument against
    /// it.)
    /// </summary>
    public string? Reasoning { get; set; }

    /// <summary>
    /// What the answer is about.
    ///
    /// <para><b>[fsnow]</b>: <i>"A restored turn should be able to reopen its book and restore the selection,
    /// as something the reader chooses rather than something that happens on load."</i> That is what makes
    /// this field required in spirit even where the type allows null: it carries the book, the reference and
    /// the pages, which is everything "take me back" (P5) needs to reopen the right book at the right
    /// paragraph. Storing the rendered line instead would leave a restored turn readable and
    /// unnavigable.</para>
    /// </summary>
    public AiCitationRecord? Citation { get; set; }

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

    /// <summary>
    /// Tokens as the provider reported them, null where it reported that half not at all — the two numbers
    /// from <c>AiUsageReport</c> rather than the line the panel prints from them.
    ///
    /// <para>The display string would have frozen both today's wording and today's <b>culture</b> into the
    /// file: <c>FormatUsage</c> runs the counts through <c>:N0</c>, so a session written on a de-DE machine
    /// says <c>1.024</c> and one written beside it says <c>1,024</c> for the same number. Numbers also let a
    /// future "what has this conversation cost" add up, which strings never could.</para>
    /// </summary>
    public int? InputTokens { get; set; }

    /// <inheritdoc cref="InputTokens"/>
    public int? OutputTokens { get; set; }

    /// <summary>
    /// How long the turn took, in milliseconds. Kept for the reason the live turn keeps it: a reader deciding
    /// whether to ask a slow model another question wants the last one's cost in front of them. A duration
    /// rather than <c>FormatElapsed</c>'s <c>"4.2s"</c>, for the same reason as the token counts — and because
    /// <c>"1m 3s"</c> cannot be compared, summed, or reformatted.
    /// </summary>
    public long? ElapsedMs { get; set; }

    /// <summary>
    /// The one line that said what went wrong, when something did. <b>Null on a clean turn</b> — the setter
    /// folds an empty string to null, so the file has one encoding for "nothing to say" rather than two that
    /// mean the same thing and compare unequal. (The live view model uses <c>""</c>; the mapping is the wiring
    /// layer's, and this setter makes it impossible to get wrong.)
    /// </summary>
    public string? Status
    {
        get => _status;
        set => _status = string.IsNullOrEmpty(value) ? null : value;
    }

    private string? _status;

    /// <summary>Whether <see cref="Status"/> is a failure rather than progress. They are drawn differently and
    /// only one of them offers Retry, so storing the line without this flag would restore an error as a
    /// progress note.</summary>
    public bool Failed { get; set; }

    /// <summary>
    /// Everything the turn sent. <b>[fsnow]</b> chose to store the prompt <i>"Yes, in full"</i> — so this is
    /// the whole of #665's record, both prompt halves and the named fields, not a summary of it.
    ///
    /// <para>A stored type rather than the live <c>SentContext</c>. See the note on <see cref="AiSession"/>:
    /// the live one is a positional record with no extension data and, since #991, a computed
    /// <c>HasHistory</c> property — so storing it directly both leaked a derived boolean into the format and
    /// dropped any newer build's nested field on the first rewrite. Mapping the two is the wiring layer's job.</para>
    ///
    /// <para>It carries no secret. An API key is a header the provider adds, never part of a prompt; what is
    /// here is corpus text, the reader's own question, and the app's own instructions.</para>
    /// </summary>
    public AiSentRecord? Sent { get; set; }

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
    /// #434's <see cref="ReadingPositionToken"/> itself, the one type <b>shared</b> with the app rather than
    /// restated here, because it is already a persisted type in exactly this shape (<c>ApplicationState</c>
    /// keeps one per book), it is already a mutable class rather than a positional record, and its three
    /// fields are a closed set with <c>ReadingPositionMath</c> owning their interpretation. Sharing the app's
    /// persisted representation is the whole of what he asked for.
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
    public Dictionary<string, JsonElement>? UnknownProperties { get; set; }
}

/// <summary>
/// What a turn sent, stored. The prompt in full (<b>[fsnow]</b>: <i>"Yes, in full"</i>) — with one exception,
/// and it is the point of this type.
///
/// <para><b>The replayed conversation is stored by reference, not by value.</b> #991 replays every earlier
/// turn that has an answer, so a by-value copy puts every earlier answer inside every later turn: turn 30 of a
/// 30-turn conversation would carry 29 answers, and the session file would carry 435 copies of them. All of it
/// is reconstructible, because a written turn never changes — the ids here plus each referenced turn's
/// <see cref="AiTurnRecord.AskedLine"/> and <see cref="AiTurnRecord.Answer"/> rebuild
/// <c>SentContext.History</c> exactly, which is the restore path's job.</para>
///
/// <para>What is NOT reconstructible is kept: <see cref="SystemPrompt"/> and <see cref="UserContent"/> are the
/// prompt this turn actually sent, including the passage as it read at the time, and a corrected corpus or an
/// edited template would make a re-gathered version differ.</para>
/// </summary>
public sealed class AiSentRecord
{
    /// <summary>Named values in display order — provider, model, task, language, book, reference, pages, the
    /// estimated token count, and what each gathered part contributed.</summary>
    public List<AiSentFieldRecord> Fields { get; set; } = new();

    /// <summary>The shared system prompt as it was sent.</summary>
    public string SystemPrompt { get; set; } = string.Empty;

    /// <summary>The final user message: context blocks and the preset instruction, as they were sent.</summary>
    public string UserContent { get; set; } = string.Empty;

    /// <summary>
    /// The turns replayed ahead of this one, oldest first, as <see cref="AiTurnRecord.Id"/> values. Empty for
    /// a one-shot turn, which is what every turn was before #991.
    ///
    /// <para>An id that names no turn in the session is not an error — a turn deleted from a session by hand
    /// leaves one. The restore path skips what it cannot resolve; the alternative, refusing the whole
    /// conversation over a dangling reference, is the failure this layer exists to avoid.</para>
    /// </summary>
    public List<string> ReplayedTurnIds { get; set; } = new();

    /// <inheritdoc cref="AiSession.UnknownProperties"/>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownProperties { get; set; }
}

/// <summary>One named value in <see cref="AiSentRecord.Fields"/>.</summary>
public sealed class AiSentFieldRecord
{
    public string Name { get; set; } = string.Empty;

    public string Value { get; set; } = string.Empty;

    /// <inheritdoc cref="AiSession.UnknownProperties"/>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownProperties { get; set; }
}

/// <summary>
/// Everything needed to cite the passage a turn was about, and to go back to it — the stored form of
/// <c>CitationRef</c>.
///
/// <para>Built by the app from bundle data and never parsed back out of model output, which is what makes it
/// impossible for a garbled answer to produce a false citation on screen. That rule is why this must be the
/// structured record rather than the line the panel draws from it.</para>
/// </summary>
public sealed class AiCitationRecord
{
    /// <summary>The open book's file name, as <c>Book.FileName</c> spells it — what "take me back" opens.</summary>
    public string BookId { get; set; } = string.Empty;

    public string BookName { get; set; } = string.Empty;

    /// <summary>Where in the book, normalized — the paragraph "take me back" goes to.</summary>
    public string NormalizedReference { get; set; } = string.Empty;

    /// <summary>The print pages the window touched, in whichever editions it names. May be empty; a window
    /// outside the mūla texts often has no page apparatus at all.</summary>
    public List<AiPageRecord> Pages { get; set; } = new();

    /// <inheritdoc cref="AiSession.UnknownProperties"/>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownProperties { get; set; }
}

/// <summary>One page reference in one edition's numbering — the stored form of <c>SnippetPageRef</c>.</summary>
public sealed class AiPageRecord
{
    /// <summary>Serialized as its name: <c>PageEdition</c> is positional and <c>Vri</c> is its zero.</summary>
    public PageEdition Edition { get; set; }

    public int Volume { get; set; }

    public int Number { get; set; }

    /// <inheritdoc cref="AiSession.UnknownProperties"/>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownProperties { get; set; }
}

/// <summary>
/// One compaction: a run of turns replaced, in what the model is sent, by a summary of them. Written by P4;
/// nothing writes it yet.
///
/// <para>The summary is model output that cost a call and cannot be recomputed from the turns it replaced —
/// the same model may not be configured tomorrow, and a re-summary would differ — so it is stored rather than
/// derived. The transcript on screen keeps every turn regardless: compaction changes what is <b>sent</b>, not
/// what is <b>shown</b>.</para>
/// </summary>
public sealed class AiCompactionRecord
{
    /// <summary>When the summary was made.</summary>
    public DateTimeOffset When { get; set; }

    /// <summary>The summary itself, as the model wrote it.</summary>
    public string Summary { get; set; } = string.Empty;

    /// <summary>The turns it stands in for, as <see cref="AiTurnRecord.Id"/> values, oldest first.</summary>
    public List<string> SummarisedTurnIds { get; set; } = new();

    /// <inheritdoc cref="AiSession.UnknownProperties"/>
    [JsonExtensionData]
    public Dictionary<string, JsonElement>? UnknownProperties { get; set; }
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
