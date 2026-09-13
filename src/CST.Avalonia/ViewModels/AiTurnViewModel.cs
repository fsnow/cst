using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
using CST.Avalonia.Models;
using CST.Avalonia.Services.Ai;
using CST.Search;
using ReactiveUI;

namespace CST.Avalonia.ViewModels;

/// <summary>
/// One question and its answer. (#586)
///
/// <para><b>A turn is a record, not a slot.</b> The panel used to hold exactly one answer and clear it at the
/// start of the next request, so asking for a translation destroyed the explanation you were reading and there
/// was no way to compare the two, scroll back, or see what you had already asked. A reader working through a
/// passage asks several things about it; each one is kept.</para>
///
/// <para><b>And it outlives the session.</b> A turn is written to disk when it ends and rebuilt at the next
/// launch (<see cref="ToRecord"/> / <see cref="FromRecord"/>, #849), so everything the panel renders has to be
/// reachable from the turn rather than computed from something the panel happens to still be holding. That is
/// why the structured originals now sit beside the display strings they produce: the citation beside its two
/// rendered lines, the token report beside <see cref="Usage"/>, the duration beside <see cref="Elapsed"/>.
/// Nothing bound in the panel changed when they arrived — the strings are still the strings.</para>
/// </summary>
public sealed class AiTurnViewModel : ReactiveObject
{
    private readonly StringBuilder _answer = new();
    private readonly StringBuilder _markedAnswer = new();
    private readonly StringBuilder _reasoning = new();

    public AiTurnViewModel(AiTask task, string? question)
    {
        Task = task;
        Question = question;
        PresetLabel = LabelFor(task);
    }

    public AiTask Task { get; }

    /// <summary>
    /// Identity within the conversation, so a later turn's record can say which turns were replayed to the
    /// model ahead of it (<c>AiSentRecord.ReplayedTurnIds</c>) without copying their text. (#849)
    ///
    /// <para>Minted here rather than at save time, because the id has to be stable while the turn is on screen:
    /// the turn that replays this one is written before this one is written again.</para>
    /// </summary>
    internal string Id { get; private set; } = AiSession.NewId();

    /// <summary>When the reader asked. Set at construction; carried through a save and a restore, so a
    /// reopened conversation reports when it happened rather than when it was reloaded. (#849)</summary>
    internal DateTimeOffset When { get; private set; } = DateTimeOffset.Now;

    /// <summary>The reader's own question, kept so the turn is legible later and so Retry can repeat it.</summary>
    public string? Question { get; }

    public bool HasQuestion => !string.IsNullOrWhiteSpace(Question);

    public string PresetLabel { get; }

    internal static string LabelFor(AiTask task) => task switch
    {
        AiTask.Explain => "Explain",
        AiTask.Translate => "Translate",
        AiTask.Grammar => "Grammar",
        AiTask.WordByWord => "Word by word",
        AiTask.Ask => "Question",
        _ => task.ToString(),
    };

    // ---- The answer ------------------------------------------------------------------------------

    private IReadOnlyList<AnswerBlock> _blocks = Array.Empty<AnswerBlock>();

    /// <summary>
    /// The answer as rendered blocks — prose and tables. Models emit light Markdown, and we teach them to,
    /// since the prompts are Markdown and the word-analysis section is itself a table; rendering the answer
    /// as plain text put literal asterisks and unaligned pipes on screen.
    /// </summary>
    public IReadOnlyList<AnswerBlock> Blocks
    {
        get => _blocks;
        private set => this.RaiseAndSetIfChanged(ref _blocks, value);
    }

    /// <summary>The raw answer, markup and all. What Copy hands over, and what a test asserts on.</summary>
    public string Answer => _answer.ToString();

    private bool _hasAnswer;
    public bool HasAnswer
    {
        get => _hasAnswer;
        private set => this.RaiseAndSetIfChanged(ref _hasAnswer, value);
    }

    /// <summary>Append streamed text. Accumulated in a builder rather than by string concatenation: the panel
    /// re-renders on every flush, and <c>Answer += chunk</c> re-allocates the whole answer each time.</summary>
    internal void AppendAnswer(string text)
    {
        _answer.Append(text);
        HasAnswer = _answer.Length > 0;
    }

    /// <summary>
    /// The same answer as the model wrote it, <c>[[…]]</c> Pāli markers and all. (#991)
    ///
    /// <para><b>Not bound to anything, and not for the reader.</b> The markers exist so the app can convert
    /// quoted Pāli into the reader's script; on screen they are stripped, which is what <see cref="Answer"/>
    /// holds. This half exists because a later turn replays this one TO THE MODEL, and the system prompt tells
    /// it to wrap every Pāli span in those markers — replaying the stripped form would show it a transcript of
    /// its own answers disobeying that instruction, which is the kind of thing a model imitates.</para>
    ///
    /// <para>Unbalanced markers are kept as written. <see cref="PaliQuoteFilter"/> strips those from the
    /// display, rightly; what the model is shown of its own output should still be what it produced.</para>
    /// </summary>
    internal string MarkedAnswer => _markedAnswer.ToString();

    internal void AppendMarkedAnswer(string text) => _markedAnswer.Append(text);

    /// <summary>Re-parse and publish. Called once per flush, not once per delta.</summary>
    internal void PublishAnswer()
    {
        Blocks = AnswerMarkup.Parse(_answer.ToString());
        this.RaisePropertyChanged(nameof(Answer));
    }

    /// <summary>
    /// The answer as a reader would want it pasted: emphasis applied rather than spelled with asterisks,
    /// bullets as glyphs, tables as aligned pipe rows.
    ///
    /// <para>An explicit operation rather than a consequence of drag-select. Tables put each cell in its own
    /// control and a drag cannot cross controls, so once the panel renders tables, "select all of it and
    /// copy" stops working — the copy path has to be owned rather than inherited.</para>
    /// </summary>
    public string CopyText => AnswerMarkup.PlainText(Blocks);

    // ---- Reasoning -------------------------------------------------------------------------------

    /// <summary>
    /// The model thinking aloud. <b>Kept out of the answer and offered separately, collapsed.</b>
    ///
    /// <para>It was previously discarded outright, which was half right and half a defect: it must never be
    /// concatenated into the answer — half-formed guesses about a sacred text are not what the model is
    /// telling the reader — but throwing it away meant the panel showed "Still waiting…" while the model was
    /// demonstrably alive and streaming. A wait with reasoning arriving is a working request; a wait with
    /// nothing arriving is not, and the reader could not tell the two apart.</para>
    /// </summary>
    public string Reasoning => _reasoning.ToString();

    private bool _hasReasoning;
    public bool HasReasoning
    {
        get => _hasReasoning;
        private set => this.RaiseAndSetIfChanged(ref _hasReasoning, value);
    }

    public string ReasoningHeader => $"Reasoning ({_reasoning.Length:N0} characters)";

    internal void AppendReasoning(string text)
    {
        _reasoning.Append(text);
        HasReasoning = _reasoning.Length > 0;
    }

    internal void PublishReasoning()
    {
        this.RaisePropertyChanged(nameof(Reasoning));
        this.RaisePropertyChanged(nameof(ReasoningHeader));
    }

    // ---- Chrome ----------------------------------------------------------------------------------

    private string _citation = "";
    /// <summary>What the answer is about, built by us from the bundle — never parsed out of model output.</summary>
    public string Citation
    {
        get => _citation;
        internal set => this.RaiseAndSetIfChanged(ref _citation, value);
    }

    /// <summary>
    /// The citation the two rendered lines above were built FROM — book id, book name, reference and pages.
    /// (#849)
    ///
    /// <para><b>Kept because the rendered lines cannot be un-rendered.</b> <b>[fsnow]</b>: <i>"A restored turn
    /// should be able to reopen its book and restore the selection, as something the reader chooses rather
    /// than something that happens on load."</i> A turn holding only <c>"Mahāvaggapāḷi — para 12"</c> is
    /// readable and unnavigable — nothing in that string names a file to open. The panel had this object for
    /// exactly the length of one <c>Started</c> event and dropped it.</para>
    ///
    /// <para>Not bound to anything: the chrome stays derived from it, which is the rule that makes a false
    /// citation impossible.</para>
    /// </summary>
    internal CitationRef? StructuredCitation { get; set; }

    private string _citationDetail = "";
    public string CitationDetail
    {
        get => _citationDetail;
        internal set => this.RaiseAndSetIfChanged(ref _citationDetail, value);
    }

    private string _subject = "";
    /// <summary>
    /// The selected text this turn is about, shown before the answer arrives.
    ///
    /// <para><b>The mitigation that makes selection-as-subject safe.</b> A browser selection persists
    /// invisibly: select a word, scroll three screens, read for ten minutes, press Explain, and the answer is
    /// about the forgotten word. Passage-as-subject was robust to that; selection-as-subject is maximally
    /// sensitive to it. So the subject is on screen from the first second, next to the Stop button, where a
    /// wrong one is obvious.</para>
    /// </summary>
    public string Subject
    {
        get => _subject;
        internal set
        {
            this.RaiseAndSetIfChanged(ref _subject, value);
            this.RaisePropertyChanged(nameof(HasSubject));
        }
    }

    public bool HasSubject => !string.IsNullOrWhiteSpace(Subject);

    public ObservableCollection<string> Notices { get; } = new();

    public bool HasNotices => Notices.Count > 0;

    /// <summary>Says how the request was ASSEMBLED, not that something failed — every one of these is a fact
    /// about the input, and at full weight beside the answer they read as a list of errors.</summary>
    public string NoticesHeader => Notices.Count == 1
        ? "1 note about this request"
        : $"{Notices.Count} notes about this request";

    internal void RaiseNoticesChanged()
    {
        this.RaisePropertyChanged(nameof(HasNotices));
        this.RaisePropertyChanged(nameof(NoticesHeader));
    }

    private bool _isPartialPassage;
    /// <summary>The model did not see all of the passage — the one caveat that changes how far the answer can
    /// be trusted, which is why it sits in the open rather than under the collapsed notices.</summary>
    public bool IsPartialPassage
    {
        get => _isPartialPassage;
        internal set => this.RaiseAndSetIfChanged(ref _isPartialPassage, value);
    }

    private SentContext? _sent;

    /// <summary>
    /// Everything this turn sent — named fields and both prompt halves. (#665)
    ///
    /// <para>Collapsed, like the notices, because it is reference material rather than part of the answer.
    /// But unlike the notices, which say what was DEGRADED, this says what was SENT: the question a reader
    /// most wants answered about a surprising answer is what the model actually saw, and until now that was
    /// only visible at Debug level in a log file.</para>
    /// </summary>
    public SentContext? Sent
    {
        get => _sent;
        internal set
        {
            this.RaiseAndSetIfChanged(ref _sent, value);
            this.RaisePropertyChanged(nameof(HasSent));
            this.RaisePropertyChanged(nameof(SentHeader));
        }
    }

    public bool HasSent => _sent is not null;

    public string SentHeader => _sent is null
        ? "Context sent"
        : $"Context sent ({_sent.Fields.Count} fields)";

    private string _usage = "";
    public string Usage
    {
        get => _usage;
        internal set
        {
            this.RaiseAndSetIfChanged(ref _usage, value);
            this.RaisePropertyChanged(nameof(Footer));
            this.RaisePropertyChanged(nameof(HasFooter));
        }
    }

    /// <summary>
    /// The token counts <see cref="Usage"/> was printed from, as the provider reported them. (#849)
    ///
    /// <para>Stored as numbers rather than as that line, for two reasons the line cannot satisfy: it is run
    /// through <c>:N0</c>, so the same turn reads <c>1,024</c> or <c>1.024</c> depending on the machine that
    /// wrote the file, and a future "what has this conversation cost" can add numbers up and can never add up
    /// strings.</para>
    /// </summary>
    internal AiUsageReport? UsageReport { get; set; }

    private string _elapsed = "";
    /// <summary>How long this turn took, kept after it finishes. A reader deciding whether to ask a slow model
    /// another question wants the last one's cost in front of them.</summary>
    public string Elapsed
    {
        get => _elapsed;
        internal set
        {
            this.RaiseAndSetIfChanged(ref _elapsed, value);
            this.RaisePropertyChanged(nameof(Footer));
            this.RaisePropertyChanged(nameof(HasFooter));
        }
    }

    /// <summary>
    /// The duration <see cref="Elapsed"/> was printed from. The panel's stopwatch is restarted by the next
    /// turn, so this is the only thing that still knows how long this one took once it has ended. (#849)
    ///
    /// <para>A duration rather than <c>"4.2s"</c> for the same reason as the token counts — and because
    /// <c>"1m 3s"</c> cannot be compared, summed, or reformatted.</para>
    /// </summary>
    internal TimeSpan? ElapsedTime { get; set; }

    /// <summary>Time and tokens on one quiet line, the way a transcript reports what a turn cost.</summary>
    public string Footer => string.Join(" · ", new[] { Elapsed, Usage }.Where(s => !string.IsNullOrEmpty(s)));

    public bool HasFooter => Footer.Length > 0;

    // ---- State -----------------------------------------------------------------------------------

    private string _status = "";
    /// <summary>The one line that says what is happening or what went wrong. Never an exception message.</summary>
    public string Status
    {
        get => _status;
        internal set
        {
            this.RaiseAndSetIfChanged(ref _status, value);
            this.RaisePropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(Status);

    private bool _failed;
    /// <summary>Whether <see cref="Status"/> is a failure rather than progress. They are drawn differently and
    /// only one of them offers Retry — a progress line and an error line reading identically is how a user
    /// ends up staring at a dead panel.</summary>
    public bool Failed
    {
        get => _failed;
        internal set => this.RaiseAndSetIfChanged(ref _failed, value);
    }

    private bool _isRunning = true;
    public bool IsRunning
    {
        get => _isRunning;
        internal set => this.RaiseAndSetIfChanged(ref _isRunning, value);
    }

    // ---- What the record needs and the screen does not show ---------------------------------------

    /// <summary>Which connection answered, and which model. (#849) An answer is not attributable without them,
    /// and a conversation spanning a model change is the ordinary case rather than a corner of it. They reach
    /// the turn on the <c>Started</c> event — see <c>AiTurnContext.ProviderId</c>.</summary>
    internal string? ProviderId { get; set; }

    /// <inheritdoc cref="ProviderId"/>
    internal string? ModelId { get; set; }

    /// <summary>
    /// Where the reader was standing when they asked — #434's two-anchor token, captured at turn start from the
    /// book window's rolling position. (#849)
    ///
    /// <para>Not redundant with <see cref="StructuredCitation"/>: the citation is where the answer POINTS, this
    /// is where the reader WAS. Null where the anchor cache had not built; a turn is worth keeping
    /// without one.</para>
    /// </summary>
    internal ReadingPositionToken? ReadingPosition { get; set; }

    /// <summary>
    /// The turns that were actually replayed to the model ahead of this one, oldest first, as
    /// <see cref="Id"/> values. (#849)
    ///
    /// <para>Written by the panel's <c>HistoryFor</c> — the same pass that builds the exchanges — rather than
    /// recomputed at save time. Recomputing it would answer "which turns would be replayed NOW", and by the
    /// time a turn is written a later one may have changed that: a record of what a model saw has to be taken
    /// from the sending, not from the transcript afterwards.</para>
    /// </summary>
    internal IReadOnlyList<string> ReplayedTurnIds { get; set; } = Array.Empty<string>();

    /// <summary>
    /// The question line this turn was replayed to the model WITH, as it was stored — set only on a turn
    /// rebuilt from disk. (#849)
    ///
    /// <para><b>Why a restored turn does not re-render its own history line.</b>
    /// <c>AiAssistantViewModel.DescribeAsked</c> composes that line from today's wording; a later change to it
    /// — a different dash, the preset label dropped — would make every restored conversation replay something
    /// different from what the model was shown the first time. The record stores the line for exactly that
    /// reason, so the restore path honours it and <c>DescribeAsked</c> prefers it when it is there.</para>
    /// </summary>
    internal string? RestoredAskedLine { get; private set; }

    // ---- Stored and restored (#849) --------------------------------------------------------------

    /// <summary>
    /// This turn as it is kept on disk.
    ///
    /// <para><b>Deliberately next door to <see cref="FromRecord"/>.</b> The two have to stay symmetrical — a
    /// field written and not read back is a turn that reopens wrong, and nothing else in the app would notice
    /// — and keeping them apart is how that happens. The rendered forms are NOT stored: the blocks are
    /// re-parsed from the answer, the citation lines are rebuilt from
    /// <see cref="StructuredCitation"/>, and usage and elapsed are re-formatted, so a change to any of those
    /// renderers reaches old conversations too.</para>
    /// </summary>
    internal AiTurnRecord ToRecord() => new()
    {
        Id = Id,
        Task = Task,
        Question = Question,
        // The line as the model saw it. Prefers a restored turn's own stored line over re-rendering it.
        AskedLine = AiAssistantViewModel.DescribeAsked(this),
        Answer = NullIfEmpty(Answer),
        MarkedAnswer = NullIfEmpty(MarkedAnswer),
        Reasoning = NullIfEmpty(Reasoning),
        Citation = ToRecord(StructuredCitation),
        Subject = NullIfEmpty(Subject),
        Notices = Notices.ToList(),
        IsPartialPassage = IsPartialPassage,
        InputTokens = UsageReport?.InputTokens,
        OutputTokens = UsageReport?.OutputTokens,
        ElapsedMs = ElapsedTime is { } elapsed ? (long)elapsed.TotalMilliseconds : null,
        // The record's setter folds "" to null, so the live view model's empty-string convention needs no
        // translation here.
        Status = Status,
        Failed = Failed,
        Sent = ToRecord(Sent, ReplayedTurnIds),
        ProviderId = ProviderId,
        ModelId = ModelId,
        When = When,
        ReadingPosition = ReadingPosition,
    };

    /// <summary>
    /// A turn rebuilt from disk, rendering as the live one that produced it did. (#849)
    ///
    /// <para><b>[fsnow]</b> chose <i>"Restore the last session silently"</i>, so this makes no model call and
    /// asks nothing of the reader: it is the same kind of restore as a book and its reading position.</para>
    ///
    /// <para><see cref="IsRunning"/> is false and the answer is published — <see cref="Blocks"/> only exist
    /// after <see cref="PublishAnswer"/>, so a turn restored without it would show an empty answer with a
    /// correct <see cref="Answer"/> string behind it.</para>
    /// </summary>
    /// <param name="byId">
    /// Every turn in the session, by id, so <c>Sent.History</c> can be rebuilt from the ids the record stores
    /// instead of from copies of the answers. An id that resolves to nothing is skipped rather than refused —
    /// a turn deleted from a session by hand leaves one, and losing the whole conversation over a dangling
    /// reference is the failure this layer exists to avoid.
    /// </param>
    internal static AiTurnViewModel FromRecord(
        AiTurnRecord record, IReadOnlyDictionary<string, AiTurnRecord>? byId = null)
    {
        var turn = new AiTurnViewModel(record.Task, record.Question)
        {
            Id = string.IsNullOrEmpty(record.Id) ? AiSession.NewId() : record.Id,
            When = record.When,
            StructuredCitation = ToLive(record.Citation),
            Subject = record.Subject ?? "",
            IsPartialPassage = record.IsPartialPassage,
            ProviderId = record.ProviderId,
            ModelId = record.ModelId,
            ReadingPosition = record.ReadingPosition,
            ReplayedTurnIds = record.Sent?.ReplayedTurnIds?.ToList() ?? (IReadOnlyList<string>)Array.Empty<string>(),
            RestoredAskedLine = NullIfEmpty(record.AskedLine),
            Status = record.Status ?? "",
            Failed = record.Failed,
            // Never running. A turn read off disk has nowhere to stream to.
            IsRunning = false,
        };

        // The chrome, rebuilt from the citation rather than read back as text — the same rule as on a live
        // turn, and the reason the structured citation is what gets stored.
        turn.Citation = AiAssistantViewModel.Describe(turn.StructuredCitation!);
        turn.CitationDetail = AiAssistantViewModel.DescribeCitationDetail(turn.StructuredCitation!);

        if (record.Answer is { Length: > 0 } answer) turn.AppendAnswer(answer);

        // The marked half falls back to the stripped one, which is what a session written before #991 has.
        // Replaying nothing would be worse than replaying a marker-free answer: the follow-up would lose the
        // turn entirely.
        turn.AppendMarkedAnswer(record.MarkedAnswer ?? record.Answer ?? "");

        if (record.Reasoning is { Length: > 0 } reasoning) turn.AppendReasoning(reasoning);

        foreach (var notice in record.Notices) turn.Notices.Add(notice);
        turn.RaiseNoticesChanged();

        if (record.InputTokens is not null || record.OutputTokens is not null)
        {
            turn.UsageReport = new AiUsageReport(record.InputTokens, record.OutputTokens);
            turn.Usage = AiAssistantViewModel.FormatUsage(turn.UsageReport);
        }

        if (record.ElapsedMs is { } ms)
        {
            turn.ElapsedTime = TimeSpan.FromMilliseconds(ms);
            turn.Elapsed = AiAssistantViewModel.FormatElapsed(turn.ElapsedTime.Value);
        }

        turn.Sent = ToLive(record.Sent, byId);

        // Last, because the blocks are parsed from whatever the builder holds by then.
        turn.PublishAnswer();
        turn.PublishReasoning();

        return turn;
    }

    /// <summary>The stored form of a citation. Null stays null: a failed turn often has none.</summary>
    private static AiCitationRecord? ToRecord(CitationRef? citation) => citation is null
        ? null
        : new AiCitationRecord
        {
            BookId = citation.BookId,
            BookName = citation.BookName,
            NormalizedReference = citation.NormalizedReference,
            Pages = citation.Pages
                .Select(p => new AiPageRecord { Edition = p.Edition, Volume = p.Volume, Number = p.Number })
                .ToList(),
        };

    /// <inheritdoc cref="ToRecord(CitationRef?)"/>
    private static CitationRef? ToLive(AiCitationRecord? record) => record is null
        ? null
        : new CitationRef(
            record.BookId,
            record.BookName,
            record.NormalizedReference,
            record.Pages.Select(p => new SnippetPageRef(p.Edition, p.Volume, p.Number)).ToList());

    /// <summary>
    /// The stored form of what a turn sent: the fields and both prompt halves by value, the replayed
    /// conversation by REFERENCE.
    ///
    /// <para>By reference because #991 replays every earlier answered turn, so copying the history into each
    /// turn would put 29 answers inside turn 30 and 435 copies of them in a 30-turn file. Nothing is lost: a
    /// written turn never changes, so the ids plus each referenced turn's stored asked-line and marked answer
    /// rebuild it exactly.</para>
    /// </summary>
    private static AiSentRecord? ToRecord(SentContext? sent, IReadOnlyList<string> replayedTurnIds) =>
        sent is null
            ? null
            : new AiSentRecord
            {
                Fields = sent.Fields.Select(f => new AiSentFieldRecord { Name = f.Name, Value = f.Value })
                    .ToList(),
                SystemPrompt = sent.SystemPrompt,
                UserContent = sent.UserContent,
                ReplayedTurnIds = replayedTurnIds.ToList(),
            };

    /// <inheritdoc cref="ToRecord(SentContext?, IReadOnlyList{string})"/>
    private static SentContext? ToLive(
        AiSentRecord? record, IReadOnlyDictionary<string, AiTurnRecord>? byId)
    {
        if (record is null) return null;

        var history = new List<ChatMessage>();
        foreach (var id in record.ReplayedTurnIds)
        {
            if (byId is null || !byId.TryGetValue(id, out var earlier)) continue;

            var asked = earlier.AskedLine;
            // The same fallback as on the turn itself, for a pre-#991 session.
            var answer = earlier.MarkedAnswer ?? earlier.Answer;

            // The orchestrator's own replay rule: a pair with either half missing was never sent, so it must
            // not appear in the record of what was.
            if (string.IsNullOrWhiteSpace(asked) || string.IsNullOrWhiteSpace(answer)) continue;

            history.Add(new ChatMessage(ChatRole.User, asked!));
            history.Add(new ChatMessage(ChatRole.Assistant, answer!));
        }

        // Null rather than an empty list, so HasHistory reads false for a one-shot turn exactly as it did live.
        return new SentContext(
            record.Fields.Select(f => new SentField(f.Name, f.Value)).ToList(),
            record.SystemPrompt,
            record.UserContent,
            history.Count == 0 ? null : history);
    }

    private static string? NullIfEmpty(string? text) => string.IsNullOrEmpty(text) ? null : text;
}
