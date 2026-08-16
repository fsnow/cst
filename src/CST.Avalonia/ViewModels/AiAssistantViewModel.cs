using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.ViewModels.Dock;
using CST.Navigation;
using CST.Search;
using ReactiveUI;
using Serilog;

namespace CST.Avalonia.ViewModels;

/// <summary>
/// The in-app assistant panel. (#586, AI_SURFACE_B.md §8)
///
/// <para>
/// <b>Generated text lives here, in Avalonia controls, never in the book's CEF WebView.</b> Two payoffs from
/// one decision: AI_INTEGRATION.md §11.1 requires generated text to be visually distinguishable from canonical
/// text, and a different <i>widget</i> makes that structural rather than a styling convention that erodes; and
/// streaming tokens into the CEF DOM would put live mutation on the component that SIGSEGVs on re-parent and
/// would destroy the answer on every float/unfloat.
/// </para>
///
/// <para>
/// <b>The chrome around the answer is ours, built from <see cref="CitationRef"/>.</b> Nothing on screen is
/// parsed out of model output, which is what makes it impossible for a garbled answer to produce a citation
/// that looks authoritative and is wrong.
/// </para>
///
/// <para>
/// <b>It is a transcript.</b> Turns accumulate; the panel scrolls; the question box and its controls stay put
/// at the bottom. The first build held exactly one answer and cleared it at the start of the next request, so
/// asking a second question destroyed the first answer — for a reader working through a passage, which is the
/// only kind of reader this has, that is the ordinary case rather than an edge one.
/// </para>
/// </summary>
public class AiAssistantViewModel : ReactiveTool
{
    private readonly IAiChatOrchestrator? _orchestrator;
    private readonly IReaderStateService? _readerState;
    private readonly IChatProviderResolver? _resolver;
    private readonly ISettingsService? _settings;
    private readonly ILogger _logger = Log.ForContext<AiAssistantViewModel>();

    /// <summary>
    /// How often streamed text reaches the screen. Fast enough to read as live, slow enough that a fast stream
    /// cannot saturate the UI thread: binding every delta re-parses and re-measures the whole answer.
    /// </summary>
    private const int FlushIntervalMs = 100;

    private readonly object _pendingGate = new();
    private bool _pendingAnswer;
    private bool _pendingReasoning;

    private DispatcherTimer? _flushTimer;
    private CancellationTokenSource? _turnCancellation;
    private readonly System.Diagnostics.Stopwatch _elapsed = new();
    private AiTurnViewModel? _current;
    private bool _sawText;
    private bool _sawReasoning;

    private string _question = "";
    private bool _isBusy;

    public AiAssistantViewModel()
        : this(null, null, null, null)
    {
    }

    public AiAssistantViewModel(
        IAiChatOrchestrator? orchestrator,
        IReaderStateService? readerState,
        IChatProviderResolver? resolver,
        ISettingsService? settings)
    {
        _orchestrator = orchestrator;
        _readerState = readerState;
        _resolver = resolver;
        _settings = settings;

        Id = "AiAssistantTool";
        Title = "Assistant";
        CanClose = false;
        CanFloat = true;
        CanPin = false;

        ExplainCommand = ReactiveCommand.CreateFromTask(() => AskAsync(AiTask.Explain));
        TranslateCommand = ReactiveCommand.CreateFromTask(() => AskAsync(AiTask.Translate));
        GrammarCommand = ReactiveCommand.CreateFromTask(() => AskAsync(AiTask.Grammar));
        WordByWordCommand = ReactiveCommand.CreateFromTask(() => AskAsync(AiTask.WordByWord));
        AskQuestionCommand = ReactiveCommand.CreateFromTask(() => AskAsync(AiTask.Ask));
        StopCommand = ReactiveCommand.Create(Stop);
        // No canExecute observable: WhenAnyValue needs ReactiveUI's builder initialised, which a plain
        // unit-test host does not do. The button binds IsEnabled to CanAsk instead, and a click that slips
        // through while a turn runs is harmless — AskAsync's guard returns without touching anything, now
        // that Retry no longer writes to the question box.
        RetryCommand = ReactiveCommand.CreateFromTask<AiTurnViewModel?>(RetryAsync);
        CopyCommand = ReactiveCommand.CreateFromTask<AiTurnViewModel>(CopyAsync);
        ClearCommand = ReactiveCommand.Create(Clear);
    }

    public ReactiveCommand<Unit, Unit> ExplainCommand { get; }
    public ReactiveCommand<Unit, Unit> TranslateCommand { get; }
    public ReactiveCommand<Unit, Unit> GrammarCommand { get; }
    public ReactiveCommand<Unit, Unit> WordByWordCommand { get; }

    /// <summary>
    /// Sends the typed question as the request itself, rather than as a rider on a preset.
    ///
    /// <para>Until this existed a reader with a question had to pick a preset to carry it — in practice
    /// Explain — so the model was told to explain the passage AND answer a question, and the preset's
    /// instructions competed with the question for what the answer should be about.</para>
    /// </summary>
    public ReactiveCommand<Unit, Unit> AskQuestionCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }
    public ReactiveCommand<AiTurnViewModel?, Unit> RetryCommand { get; }

    /// <summary>Copies one turn's answer. Explicit rather than left to drag-select, which stopped covering
    /// the whole answer once tables put each cell in its own control.</summary>
    public ReactiveCommand<AiTurnViewModel, Unit> CopyCommand { get; }
    public ReactiveCommand<Unit, Unit> ClearCommand { get; }

    private double _reasoningMaxHeight = 240;

    /// <summary>
    /// The MOST reasoning shown at once, shared by every turn and dragged by the handle under it.
    ///
    /// <para><b>A ceiling, not a height.</b> Bound as one, a short reasoning got a box twice its size and the
    /// empty remainder pushed the answer down for nothing. As a maximum the box takes the height its text
    /// needs and starts scrolling only once there is more than the reader asked to see.</para>
    ///
    /// <para>Shared rather than per-turn: a reader who has decided how much reasoning they want to see has
    /// decided it for the session, not for one answer. An earlier attempt used a GridSplitter per turn, on
    /// the reasoning that a RowDefinition cannot bind to an ancestor's property — true, and beside the point,
    /// because the ScrollViewer whose height actually matters is an ordinary control that binds like any
    /// other.</para>
    /// </summary>
    public double ReasoningMaxHeight
    {
        get => _reasoningMaxHeight;
        set => this.RaiseAndSetIfChanged(ref _reasoningMaxHeight, Math.Clamp(value, 60, 1200));
    }

    /// <summary>Drag the ceiling up or down. Clamped, so it cannot be dragged away to nothing.</summary>
    public void ResizeReasoning(double delta) => ReasoningMaxHeight += delta;

    /// <summary>Every turn this session, oldest first. The panel scrolls; this is what it scrolls.</summary>
    public ObservableCollection<AiTurnViewModel> Turns { get; } = new();

    public bool HasTurns => Turns.Count > 0;

    /// <summary>The most recent turn — what a test asserts on, and what Retry repeats.</summary>
    public AiTurnViewModel? LastTurn => Turns.Count > 0 ? Turns[^1] : null;

    /// <summary>The user's own question, optional. The presets work with it empty.</summary>
    public string Question
    {
        get => _question;
        set
        {
            this.RaiseAndSetIfChanged(ref _question, value);
            this.RaisePropertyChanged(nameof(CanAskQuestion));
        }
    }

    /// <summary>
    /// Whether there is a question to send. The four presets work with an empty box; this one is the
    /// question, so an empty box means there is nothing to ask.
    /// </summary>
    public bool CanAskQuestion => CanAsk && !string.IsNullOrWhiteSpace(Question);

    /// <summary>
    /// Refusals that belong to the panel rather than to any turn — not configured, no book open. Kept off the
    /// transcript because no request was made: a transcript entry for a request that never happened would be a
    /// record of something the user did not do.
    /// </summary>
    private string _status = "";
    public string Status
    {
        get => _status;
        private set
        {
            this.RaiseAndSetIfChanged(ref _status, value);
            this.RaisePropertyChanged(nameof(HasStatus));
        }
    }

    public bool HasStatus => !string.IsNullOrEmpty(Status);

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isBusy, value);
            this.RaisePropertyChanged(nameof(CanAsk));
            this.RaisePropertyChanged(nameof(CanAskQuestion));
        }
    }

    public bool CanAsk => !IsBusy;

    /// <summary>
    /// Runs one turn. Every expected failure — not configured, no book, an unreadable position, a dead
    /// network — arrives as a sentence rather than an exception.
    /// </summary>
    internal async Task AskAsync(AiTask task, string? questionOverride = null)
    {
        if (IsBusy) return;

        // Claimed HERE, not in StartTurn. StartTurn runs after an await on the reader, which in the app does
        // real WebView work — and Explain and Translate are different commands, so ReactiveCommand's own
        // serialization does not hold between them. Two presets clicked inside that window both passed the
        // guard and started turns: the first turn's flush timer was overwritten without being stopped and
        // ticked for the rest of the session, and its cancellation source was disposed while its stream was
        // still using it.
        IsBusy = true;
        try
        {
            await RunAsync(task, questionOverride);
        }
        finally
        {
            IsBusy = false;
        }
    }

    private async Task RunAsync(AiTask task, string? questionOverride)
    {
        Status = "";

        if (_orchestrator == null || _readerState == null)
        {
            Status = "The assistant is not available in this build.";
            return;
        }

        // Checked BEFORE touching the reader, so an unconfigured user is told what to set rather than being
        // asked to wait while the app assembles a bundle it cannot send.
        if (_resolver != null)
        {
            _resolver.Resolve(out var problem);
            if (problem != null)
            {
                Status = problem;
                return;
            }
        }

        var reader = await _readerState.GetCurrentAsync();
        if (reader.State is not { } state)
        {
            Status = Describe(reader.Problem);
            return;
        }

        // An override comes from Retry, which must repeat the turn's OWN question rather than whatever is in
        // the box — the box now holds unsent drafts, and those are precious.
        var typed = questionOverride ?? Question;
        var question = string.IsNullOrWhiteSpace(typed) ? null : typed.Trim();
        var turn = StartTurn(task, question, state.SelectionText, clearBox: questionOverride is null);

        try
        {
            var request = new AiTurnRequest(
                task,
                state.BookId,
                // The SELECTION's paragraph when there is one, not the scroll's. Building the window around
                // the viewport is what let a selection near the bottom of the screen fall outside the window
                // meant to explain it. Falls back to the reading position when nothing is selected, or when
                // the anchor cache could not place what was. (#649)
                new NavigationReference.Paragraph(state.SelectionParagraph ?? state.Paragraph),
                state.SelectionText,
                question,
                // Carried rather than collapsed into "no selection": a selection the reader could not read is
                // a different state, and conflating them is what makes a dropped selection look to the user
                // like the assistant ignored it. (#581)
                state.SelectionUnavailable);

            await foreach (var e in _orchestrator.RunAsync(request, _turnCancellation!.Token))
                Handle(turn, e);
        }
        catch (OperationCanceledException)
        {
            // The user's own stop. Whatever streamed already stands.
            turn.Status = "Stopped.";
            turn.Failed = false;
        }
        catch (Exception ex)
        {
            // The orchestrator promises not to throw for expected states, so anything here is a defect —
            // logged as one, and still shown as a sentence rather than a stack trace.
            _logger.Error(ex, "Assistant turn failed unexpectedly");
            turn.Status = "Something went wrong running that request.";
            turn.Failed = true;
        }
        finally
        {
            EndTurn(turn);
        }
    }

    /// <summary>Repeats the last turn. The evidence for offering this at all: an observed request returned a
    /// gateway 504 after 120s while the identical request to the same model answered in 33s.</summary>
    private async Task RetryAsync(AiTurnViewModel? turn)
    {
        // The turn to repeat is the one whose button was pressed, not the newest. Old failed turns keep their
        // Try again button, so retrying turn 1 after turn 2 has run used to re-send turn 2.
        turn ??= LastTurn;
        if (turn is null) return;

        // Its own question, passed directly. Writing it into the box first destroyed any draft the reader had
        // started typing there — and when a turn was already running, AskAsync's IsBusy guard then returned
        // without sending anything, so the draft was gone and nothing had happened.
        await AskAsync(turn.Task, turn.Question ?? string.Empty);
    }

    private static async Task CopyAsync(AiTurnViewModel turn)
    {
        // global:: throughout: the app's own CST.Avalonia namespace shadows the framework's Avalonia one.
        var clipboard = (global::Avalonia.Application.Current?.ApplicationLifetime
                as global::Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime)
            ?.MainWindow?.Clipboard;

        if (clipboard is null || turn is null) return;
        await clipboard.SetTextAsync(turn.CopyText);
    }

    private void Clear()
    {
        if (IsBusy) return;
        Turns.Clear();
        this.RaisePropertyChanged(nameof(HasTurns));
        Status = "";
    }

    private void Handle(AiTurnViewModel turn, AiTurnEvent e)
    {
        switch (e.Kind)
        {
            case AiTurnEventKind.Started when e.Context is { } context:
                // Chrome first: the citation arrives before any text, so the panel can say what it is about
                // while the model is still thinking.
                turn.Citation = Describe(context.Citation);
                turn.CitationDetail = DescribeCitationDetail(context.Citation);
                turn.Notices.Clear();
                foreach (var notice in context.Notices) turn.Notices.Add(notice);
                turn.RaiseNoticesChanged();
                turn.IsPartialPassage = context.PassageTrimmed;
                turn.Sent = context.Sent;
                turn.Status = WaitingMessage(_elapsed.Elapsed, sawReasoning: false);
                break;

            case AiTurnEventKind.Text when e.Text is { Length: > 0 }:
                lock (_pendingGate)
                {
                    turn.AppendAnswer(e.Text);
                    _pendingAnswer = true;
                }
                // Text on screen IS the progress report; the counter has nothing left to say.
                _sawText = true;
                turn.Status = "";
                break;

            case AiTurnEventKind.Reasoning when e.Text is { Length: > 0 }:
                // Never concatenated into the answer — it is the model thinking aloud, not what it is telling
                // the reader — but no longer discarded either. Its ARRIVAL is the difference between a slow
                // request and a dead one, and the panel used to report both as "still waiting".
                lock (_pendingGate)
                {
                    turn.AppendReasoning(e.Text);
                    _pendingReasoning = true;
                }
                _sawReasoning = true;
                break;

            case AiTurnEventKind.Usage when e.Usage is { } usage:
                turn.Usage = FormatUsage(usage);
                break;

            case AiTurnEventKind.Error when e.Error is { } error:
                Flush(turn);
                // Partial text stands — a mid-stream failure keeps what arrived.
                turn.Status = error.Message;
                turn.Failed = true;
                break;

            case AiTurnEventKind.Completed:
                Flush(turn);
                turn.Status = "";
                turn.Failed = false;
                break;
        }
    }

    /// <summary>Stops the turn in flight. What the visible stop control calls.</summary>
    private void Stop()
    {
        _orchestrator?.Stop();
        _turnCancellation?.Cancel();
    }

    private AiTurnViewModel StartTurn(AiTask task, string? question, string? selection, bool clearBox)
    {
        var turn = new AiTurnViewModel(task, question)
        {
            // On screen from the first second, so a stale selection — one made before scrolling away and
            // forgotten — is visible immediately rather than discovered in the answer.
            Subject = Summarize(selection),
            Status = "Contacting the provider…",
        };

        Turns.Add(turn);
        this.RaisePropertyChanged(nameof(HasTurns));
        this.RaisePropertyChanged(nameof(LastTurn));

        // The question has moved into the transcript, so the box empties — the same way every chat box the
        // reader has ever used behaves. Nothing is lost: the turn keeps the question and shows it above its
        // own answer, and Retry puts it back in the box.
        //
        // Cleared HERE rather than on the click, because the refusals above this point never create a turn.
        // Clearing on the click would throw away what the reader typed to tell them the assistant is not
        // configured, or that no book is open — and then they would have to type it again to act on the
        // advice.
        //
        // Not cleared for a Retry, which never took the question from the box in the first place: doing so
        // would wipe a draft the reader is part-way through writing.
        if (clearBox) Question = "";

        _current = turn;
        _sawText = false;
        _sawReasoning = false;
        lock (_pendingGate)
        {
            _pendingAnswer = false;
            _pendingReasoning = false;
        }

        _turnCancellation?.Dispose();
        _turnCancellation = new CancellationTokenSource();

        _elapsed.Restart();
        _flushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FlushIntervalMs) };
        _flushTimer.Tick += (_, _) => Tick(turn);
        _flushTimer.Start();

        return turn;
    }

    private void Tick(AiTurnViewModel turn)
    {
        Flush(turn);
        turn.Elapsed = FormatElapsed(_elapsed.Elapsed);

        // The tick owns the status line only while nothing real has arrived, and surrenders it the instant
        // anything does — a progress counter must never overwrite an error message.
        if (!_sawText && !turn.Failed)
            turn.Status = WaitingMessage(_elapsed.Elapsed, _sawReasoning);
    }

    /// <summary>
    /// What to say while nothing has come back yet — a ladder, because "slow" and "broken" look identical from
    /// the outside and only the app can tell them apart.
    ///
    /// <para>
    /// A silent wait is the norm on a free or shared endpoint, not a fault: an observed turn against a 550B
    /// model on a <c>:free</c> tier sat for two minutes and then returned a gateway timeout, while a different
    /// request to the same model answered in thirty seconds. The app cannot shorten that — the HTTP timeout is
    /// deliberately infinite, because a finite one truncates long streams and reports it as a cancellation —
    /// so the least it can do is look like waiting rather than like nothing happening, and name the likely
    /// reason before the user concludes the button is broken.
    /// </para>
    ///
    /// <para>
    /// Reasoning outranks the clock: once reasoning is arriving the request is demonstrably alive, and saying
    /// so is worth more than any elapsed figure.
    /// </para>
    /// </summary>
    internal static string WaitingMessage(TimeSpan elapsed, bool sawReasoning)
    {
        if (sawReasoning) return $"Reasoning… {FormatElapsed(elapsed)}";

        return elapsed.TotalSeconds switch
        {
            < 5 => "Waiting for the model…",
            < 30 => $"Waiting for the model… {FormatElapsed(elapsed)}",
            _ => $"Still waiting… {FormatElapsed(elapsed)}. Free and shared endpoints can queue behind other "
                 + "requests, and a large model can take minutes.",
        };
    }

    /// <summary>Elapsed time the way a transcript reports it: seconds until it is minutes.</summary>
    internal static string FormatElapsed(TimeSpan elapsed) =>
        elapsed.TotalMinutes >= 1
            ? $"{(int)elapsed.TotalMinutes}m {elapsed.Seconds}s"
            : $"{elapsed.TotalSeconds:0.0}s";

    private void EndTurn(AiTurnViewModel turn)
    {
        _flushTimer?.Stop();
        _flushTimer = null;
        _elapsed.Stop();
        Flush(turn);
        turn.Elapsed = FormatElapsed(_elapsed.Elapsed);
        turn.IsRunning = false;
        _current = null;
    }

    /// <summary>Moves whatever has accumulated onto the screen, in one property change per kind.</summary>
    private void Flush(AiTurnViewModel turn)
    {
        bool answer, reasoning;
        lock (_pendingGate)
        {
            answer = _pendingAnswer;
            reasoning = _pendingReasoning;
            _pendingAnswer = false;
            _pendingReasoning = false;
        }

        if (answer) turn.PublishAnswer();
        if (reasoning) turn.PublishReasoning();
    }

    internal static string FormatUsage(AiUsageReport usage) =>
        (usage.InputTokens, usage.OutputTokens) switch
        {
            (null, null) => "",
            (var i, null) => $"{i:N0} tokens in",
            (null, var o) => $"{o:N0} tokens out",
            var (i, o) => $"{i:N0} in · {o:N0} out",
        };

    /// <summary>
    /// The selected text, shortened for the subject line. Elided in the middle rather than at the end: the two
    /// places a reader checks to recognise their own selection are where it starts and where it stops.
    /// </summary>
    internal static string Summarize(string? selection, int max = 90)
    {
        if (string.IsNullOrWhiteSpace(selection)) return "";

        var text = System.Text.RegularExpressions.Regex.Replace(selection.Trim(), @"\s+", " ");
        if (text.Length <= max) return text;

        var half = (max - 1) / 2;
        return $"{text[..half].TrimEnd()}…{text[^half..].TrimStart()}";
    }

    /// <summary>
    /// The refusal, phrased for a reader. Each of these is an ordinary state of the app rather than an error:
    /// nothing open yet, a page still settling, a volume whose paragraph numbering needs a sub-book code.
    /// </summary>
    internal static string Describe(ReaderStateProblem? problem) => problem switch
    {
        ReaderStateProblem.NoBookOpen => "Open a book first, then ask about the passage you are reading.",
        ReaderStateProblem.PositionUnknown =>
            "The reading position is still settling. Try again in a moment.",
        ReaderStateProblem.AmbiguousInMultiBook =>
            "This volume contains several books, and the reader cannot yet say which one this paragraph "
            + "belongs to — so the passage would be ambiguous.",
        ReaderStateProblem.AmbiguousBookWindow =>
            "More than one book window is open and none is clearly the one in use. Click into the book you "
            + "mean, then ask again.",
        _ => "The reader could not say which passage you are on.",
    };

    /// <summary>
    /// The citation as ONE quiet line, built from the bundle rather than parsed out of the answer: the book's
    /// own name and where in it.
    ///
    /// <para>
    /// Deliberately not the full nav path, and deliberately not every page. The bundle's book name is a path —
    /// <c>tipiṭaka (mūla)/sutta piṭaka/dīgha nikāya/mahāvaggapāḷi</c> — and since #561 the pages cover every
    /// edition the window touches, which for a four-paragraph window is eight references. Both in full, in
    /// bold, ran to four lines and buried the answer they were supposed to caption. The full version lives in
    /// <see cref="DescribeCitationDetail"/>, on the tooltip.
    /// </para>
    /// </summary>
    internal static string Describe(CitationRef citation)
    {
        if (citation is null) return "";

        var book = LeafBookName(citation.BookName);
        return string.IsNullOrWhiteSpace(citation.NormalizedReference)
            ? book
            : $"{book} — {citation.NormalizedReference}";
    }

    /// <summary>
    /// Everything the headline leaves out: where the book sits in the canon, and every printed page the window
    /// covers, one line per edition. On the tooltip because a reader checking a claim against print wants it,
    /// and a reader reading the answer does not.
    /// </summary>
    internal static string DescribeCitationDetail(CitationRef citation)
    {
        if (citation is null) return "";

        var lines = new List<string> { citation.BookName };
        if (!string.IsNullOrWhiteSpace(citation.NormalizedReference))
            lines.Add(citation.NormalizedReference);
        lines.AddRange(DescribePagesByEdition(citation.Pages));
        return string.Join("\n", lines);
    }

    /// <summary>
    /// Printed pages, one line per edition with consecutive numbers collapsed: "VRI vol. 2 pp. 1–2".
    ///
    /// <para>
    /// Eight separate references for a window spanning two pages of four editions is the same fact stated
    /// eight times. Grouping is what makes it readable; the ranges are what make it short.
    /// </para>
    /// </summary>
    internal static IReadOnlyList<string> DescribePagesByEdition(IReadOnlyList<SnippetPageRef>? pages)
    {
        if (pages is not { Count: > 0 }) return Array.Empty<string>();

        return pages
            .GroupBy(p => (p.Edition, p.Volume))
            .Select(g =>
            {
                var numbers = g.Select(p => p.Number).Distinct().OrderBy(n => n).ToList();
                var volume = g.Key.Volume > 0 ? $"vol. {g.Key.Volume} " : "";
                var label = EditionLabel(g.Key.Edition);
                return numbers.Count == 1
                    ? $"{label} {volume}p. {numbers[0]}"
                    // En dash, and only when the run is unbroken — "pp. 1-5" for pages 1 and 5 would be a
                    // claim about three pages nobody looked at.
                    : IsUnbroken(numbers)
                        ? $"{label} {volume}pp. {numbers[0]}\u2013{numbers[^1]}"
                        : $"{label} {volume}pp. {string.Join(", ", numbers)}";
            })
            .ToList();
    }

    private static bool IsUnbroken(IReadOnlyList<int> numbers)
    {
        for (var i = 1; i < numbers.Count; i++)
            if (numbers[i] != numbers[i - 1] + 1) return false;
        return true;
    }

    private static string EditionLabel(PageEdition edition) => edition switch
    {
        PageEdition.Vri => "VRI",
        PageEdition.Myanmar => "Myanmar",
        PageEdition.Pts => "PTS",
        PageEdition.Thai => "Thai",
        _ => "Other",
    };

    /// <summary>
    /// The book's own name from the bundle's path. The path is useful context and a poor caption: what a
    /// reader needs beside an answer is which book, not the four levels of canon above it.
    /// </summary>
    internal static string LeafBookName(string? bookName)
    {
        if (string.IsNullOrWhiteSpace(bookName)) return "";
        var segments = bookName.Split('/', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return segments.Length == 0 ? bookName.Trim() : segments[^1];
    }
}
