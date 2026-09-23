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
///
/// <para>
/// <b>And it is the conversation.</b> What is on screen is what the model is shown of the turns before this
/// one — see <see cref="HistoryFor"/> — so a follow-up question means what the reader means by it. The panel
/// was a transcript to the reader and a series of unrelated requests to the model until #991, which is why
/// "what did you mean by the third word?" used to answer about nothing.
/// </para>
///
/// <para>
/// <b>And it persists.</b> The panel owns one <see cref="AiSession"/> at a time: it is created at the first
/// turn that ends, rewritten in full at the end of every turn after that, and reloaded at the next launch.
/// <b>[fsnow]</b>: <i>"Restore the last session silently"</i>. That is the way books and reading positions are
/// already restored, and it costs no model call. <c>ApplicationState.ActiveAssistantSessionId</c> is the one
/// thing application state keeps; the transcripts live one file each (#849).
/// </para>
///
/// <para>
/// <b>A failed save never fails a turn.</b> The store answers false rather than throwing, and the worst this
/// panel does about it is put one sentence in <see cref="Status"/>. The answer is on screen and the reader is
/// reading it; losing it to an error dialog about a file would be the larger harm.
/// </para>
///
/// <para>
/// <b>There is no Clear.</b> <b>[fsnow]</b>: <i>"'Clear' was introduced by Claude at some point and is not
/// relevant. I would like to create new conversations like in Claude Code, maybe also with a plus button,
/// while saving the current one."</i> So the command is <see cref="NewConversationCommand"/>, and nothing is
/// destroyed by it — the conversation it leaves behind was already written at its last turn.
/// </para>
/// </summary>
public class AiAssistantViewModel : ReactiveTool
{
    private readonly IAiChatOrchestrator? _orchestrator;
    private readonly IReaderStateService? _readerState;
    private readonly IChatProviderResolver? _resolver;
    private readonly ISettingsService? _settings;
    private readonly IAiSessionStore? _store;
    private readonly IApplicationStateService? _appState;
    private readonly ILogger _logger = Log.ForContext<AiAssistantViewModel>();

    /// <summary>
    /// The conversation being added to, or null when the panel is fresh. (#849)
    ///
    /// <para>Created lazily, at the first turn that ENDS rather than at the first that starts: a session whose
    /// only turn was refused before it reached the model would be a file recording something the reader did not
    /// do, and the refusals above <c>StartTurn</c> never create a turn at all.</para>
    /// </summary>
    private AiSession? _session;

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
        : this(null, null, null, null, null)
    {
    }

    public AiAssistantViewModel(
        IAiChatOrchestrator? orchestrator,
        IReaderStateService? readerState,
        IChatProviderResolver? resolver,
        ISettingsService? settings,
        IAiConnectionService? connections = null,
        Services.Ai.Credentials.IAiEnvironmentKeys? environmentKeys = null,
        // Optional like everything above, and for the same reason: a panel with no store is a panel that
        // forgets, not a panel that fails. (#849)
        IAiSessionStore? store = null,
        IApplicationStateService? appState = null)
    {
        _orchestrator = orchestrator;
        _readerState = readerState;
        _resolver = resolver;
        _settings = settings;
        _store = store;
        _appState = appState;

        // Before anything is loaded, as the store's contract asks. An unreadable transcript is the one failure
        // the reader has to be told about — it has been kept aside rather than deleted, and the log alone
        // leaves them with an empty panel and no explanation.
        if (_store is not null) _store.Unreadable += OnSessionUnreadable;

        // Changing model is the reason the provider rework exists, so it belongs here rather than in
        // Settings (#693). Readiness is re-asked on every switch: picking a model on a connection with no
        // key must change the standing notice, not wait to fail at send time.
        ModelPicker = new AiModelPickerViewModel(connections, RefreshReadiness);

        // Beside the model chip rather than in Settings, and for the same reason (#671): effort is the lever
        // that trades cost against depth on the turn about to be sent, and on a free tier it is the difference
        // between a usable answer and a 504. It hides itself where the model published no levels.
        EffortPicker = new AiEffortPickerViewModel(connections, settings);

        Id = "AiAssistantTool";
        Title = "AI Assistant";
        CanClose = false;
        CanFloat = true;
        CanPin = false;

        ExplainCommand = ReactiveCommand.CreateFromTask(() => AskAsync(AiTask.Explain));
        TranslateCommand = ReactiveCommand.CreateFromTask(() => AskAsync(AiTask.Translate));
        GrammarCommand = ReactiveCommand.CreateFromTask(() => AskAsync(AiTask.Grammar));
        WordByWordCommand = ReactiveCommand.CreateFromTask(() => AskAsync(AiTask.WordByWord));
        AskQuestionCommand = ReactiveCommand.CreateFromTask(() => AskAsync(AiTask.Ask));
        StopCommand = ReactiveCommand.Create(Stop);
        RefreshReadinessCommand = ReactiveCommand.Create(RefreshReadiness);
        // No canExecute observable: WhenAnyValue needs ReactiveUI's builder initialised, which a plain
        // unit-test host does not do. The button binds IsEnabled to CanAsk instead, and a click that slips
        // through while a turn runs is harmless — AskAsync's guard returns without touching anything, now
        // that Retry no longer writes to the question box.
        RetryCommand = ReactiveCommand.CreateFromTask<AiTurnViewModel?>(RetryAsync);
        CopyCommand = ReactiveCommand.CreateFromTask<AiTurnViewModel>(CopyAsync);
        NewConversationCommand = ReactiveCommand.Create(NewConversation);
        SwitchToSessionCommand = ReactiveCommand.CreateFromTask<string?>(id => SwitchToSessionAsync(id));
        RenameSessionCommand = ReactiveCommand.CreateFromTask<AiSessionRename?>(
            rename => rename is null ? Task.CompletedTask : (Task)RenameSessionAsync(rename.Id, rename.Name));
        DeleteSessionCommand = ReactiveCommand.CreateFromTask<string?>(id => DeleteSessionAsync(id));

        // An unhandled exception in a ReactiveCommand goes to RxApp.DefaultExceptionHandler, which
        // TERMINATES THE APP. Everything these commands call catches internally today, so no failing case is
        // currently constructible - which is exactly what makes this worth wiring: the trap is armed for the
        // first future edit that lets something throw, and it springs as a crash in a reading app rather
        // than a message in a panel. SearchViewModel already does this for its own command. (R6-2)
        //
        // The pre-turn path is the live risk: _environmentKeys.Ready, _resolver.Resolve and GetCurrentAsync
        // all run OUTSIDE RunAsync's try, as does CopyAsync's clipboard call.
        foreach (var command in new IHandleObservableErrors[]
                 {
                     ExplainCommand, TranslateCommand, GrammarCommand, WordByWordCommand, AskQuestionCommand,
                     StopCommand, RefreshReadinessCommand, RetryCommand, CopyCommand, NewConversationCommand,
                     SwitchToSessionCommand, RenameSessionCommand, DeleteSessionCommand,
                 })
        {
            command.ThrownExceptions.Subscribe(ex =>
            {
                _logger.Error(ex, "AI Assistant command failed");
                if (_current is { } turn)
                {
                    turn.Status = "Something went wrong. See the log for details.";
                    turn.Failed = true;
                }
                IsBusy = false;
            });
        }

        // Asked once at construction so the panel can say it is not configured before anything is pressed.
        RefreshReadiness();

        // A key exported from a shell profile arrives seconds after launch, not at it (#817). Without this
        // the panel opens saying the assistant is not configured and keeps saying it until something else
        // happens to re-ask — for a reader whose key IS set, in the variable the app is about to use.
        _environmentKeys = environmentKeys;
        if (_environmentKeys is not null)
            _environmentKeys.Changed += (_, _) => Dispatcher.UIThread.Post(RefreshReadiness);
    }

    private readonly Services.Ai.Credentials.IAiEnvironmentKeys? _environmentKeys;

    /// <summary>The per-turn model chip and its list. (#693)</summary>
    public AiModelPickerViewModel ModelPicker { get; }

    public AiEffortPickerViewModel EffortPicker { get; }

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

    /// <summary>
    /// Start a new conversation, keeping the one on screen. (#849, #850)
    ///
    /// <para><b>[fsnow]</b>: <i>"'Clear' was introduced by Claude at some point and is not relevant. I would
    /// like to create new conversations like in Claude Code, maybe also with a plus button, while saving the
    /// current one."</i></para>
    ///
    /// <para><b>It saves nothing, because there is nothing left to save.</b> Every turn was written when it
    /// ended, so this only lets go: the transcript clears, the session reference drops, and the active id in
    /// application state is cleared so the next launch restores an empty panel rather than the conversation the
    /// reader had just set aside. Nothing is destroyed, so it asks nothing.</para>
    ///
    /// <para>Guarded by <see cref="IsBusy"/> like every other command: letting go of a session whose turn is
    /// still streaming would leave that turn's save writing to a conversation the panel no longer shows.</para>
    /// </summary>
    public ReactiveCommand<Unit, Unit> NewConversationCommand { get; }

    // ---- Sessions (#997) --------------------------------------------------------------------------
    //
    // [fsnow]: "Claude Code itself is my model and we should aim for feature parity with CC in context management,
    // named sessions, session restoration, etc." — the reference here is CC's /resume picker and /rename. Scope is
    // "One global list", retention "Forever, no cap", and delete "Yes, with confirmation" (the confirmation is the
    // view's; what is here is the delete).
    //
    // On the panel rather than on a list view model of its own [suggestion]: every one of these operations is a
    // change to the panel's own state — which session it is adding to, what Turns holds, whether a turn is in
    // flight — and the list's only derived fact (which row is active) is read from that same state. A separate
    // object would need a reference back to all of it, and the view binds the rows' buttons to the panel either way.

    /// <summary>
    /// Every conversation on disk, newest-active first — the order <see cref="IAiSessionStore.ListAsync"/> answers
    /// in, kept as it is. One row is <see cref="AiSessionRowViewModel.IsActive"/> when the panel is showing a
    /// saved conversation; none is on a fresh panel.
    ///
    /// <para>Refreshed by <see cref="RefreshSessionsAsync"/> after every save, rename, delete, switch, new
    /// conversation and restore. <b>A conversation that exists on disk but is not the active one is simply a
    /// row here</b> — which is what makes the shutdown gap recorded in ASSISTANT_SESSIONS.md §3.2 (a first turn
    /// saved after <c>ActiveAssistantSessionId</c> last reached disk) recoverable: the reader relaunches to an
    /// empty panel and the conversation is at the top of this list.</para>
    /// </summary>
    public ObservableCollection<AiSessionRowViewModel> Sessions { get; } = new();

    public bool HasSessions => Sessions.Count > 0;

    /// <summary>
    /// The name of the conversation on screen, for the session switcher's label: the active row's name, or
    /// "New conversation" when no row is active (a fresh panel, or one whose first turn is not yet saved).
    /// Set in one place, <see cref="UpdateRowAvailability"/>, which is where the active row is decided.
    /// </summary>
    public string ActiveSessionName
    {
        get => _activeSessionName;
        private set => this.RaiseAndSetIfChanged(ref _activeSessionName, value);
    }

    private string _activeSessionName = NewConversationName;

    internal const string NewConversationName = "New conversation";

    /// <summary>
    /// Show a listed conversation in the panel. Parameter: the session id (<see cref="AiSessionRowViewModel.Id"/>).
    /// See <see cref="SwitchToSessionAsync"/>.
    /// </summary>
    public ReactiveCommand<string?, Unit> SwitchToSessionCommand { get; }

    /// <summary>
    /// Rename any listed conversation, the active one included. Parameter: an <see cref="AiSessionRename"/>. See
    /// <see cref="RenameSessionAsync"/>.
    /// </summary>
    public ReactiveCommand<AiSessionRename?, Unit> RenameSessionCommand { get; }

    /// <summary>
    /// Delete a listed conversation, <b>without asking</b> — the view asks. Parameter: the session id. See
    /// <see cref="DeleteSessionAsync"/>.
    /// </summary>
    public ReactiveCommand<string?, Unit> DeleteSessionCommand { get; }

    /// <summary>
    /// Whether a switch may be offered now: not while a turn is in flight, nor while another switch is loading
    /// (which holds <see cref="IsBusy"/> too). A bindable flag rather than a <c>canExecute</c> observable for the
    /// reason recorded at <c>RetryCommand</c>'s construction; <see cref="SwitchToSessionAsync"/> checks again.
    /// </summary>
    public bool CanSwitchSession => !IsBusy;

    /// <summary>
    /// Whether a rename may be offered now. Always, when there is a store: a rename touches only a name, and the
    /// running turn's own save writes the same session object, so the two cannot disagree about it.
    /// </summary>
    public bool CanRenameSession => _store is not null;

    // Delete has no panel-level flag: it is refused for one row, not for all of them — see
    // AiSessionRowViewModel.CanDelete.

    /// <summary>Re-ask the resolver whether the assistant is configured. Bound to the panel's own refresh, so
    /// a reader who has just been sent to Settings can come back and see the answer change.</summary>
    public ReactiveCommand<Unit, Unit> RefreshReadinessCommand { get; }

    /// <summary>
    /// Why the assistant cannot run, shown BEFORE anything is pressed. (#667)
    ///
    /// <para>It used to appear only after a button was clicked, so the commonest first-run state — a fresh
    /// settings file with no assistant configured — presented as four buttons that did nothing. "The
    /// Assistant buttons are a no op" is the correct reading of that, and the app had no business making a
    /// reader guess.</para>
    /// </summary>
    private string _notReady = "";
    public string NotReady
    {
        get => _notReady;
        private set
        {
            this.RaiseAndSetIfChanged(ref _notReady, value);
            this.RaisePropertyChanged(nameof(IsNotReady));
        }
    }

    public bool IsNotReady => !string.IsNullOrEmpty(NotReady);

    /// <summary>Ask the same resolver the turn will ask, so the panel and the request cannot disagree.</summary>
    public void RefreshReadiness()
    {
        if (_resolver is null) { NotReady = ""; return; }

        _resolver.Resolve(out var problem);
        NotReady = problem ?? "";
    }

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
            this.RaisePropertyChanged(nameof(CanSwitchSession));
            UpdateRowAvailability();
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
            // A turn just told us what the resolver thinks; keep the standing line in step with it.
            RefreshReadiness();
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

        // Settled before asking the resolver anything (#817). Already-complete unless a shell probe is in
        // flight, so this changes nothing for anyone whose key is in the process environment; for the reader
        // whose key is in a shell profile it is the difference between "not configured" on the first send of
        // the session and an answer.
        if (_environmentKeys is not null)
            await _environmentKeys.Ready.ConfigureAwait(true);

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

        // The reader is here, clicking, so the app knows which book they were last in — resolve rather
        // than refuse (#938). Without this the panel inherited the external agent's blindness: a book
        // floated beside a docked one made every question ambiguous, and "click into the book you mean" was
        // advice this path could not act on, since it never consulted focus at all.
        var reader = await _readerState.GetCurrentAsync(ReaderFocusSignal.LastFocusedBook);
        if (reader.State is not { } state)
        {
            Status = Describe(reader.Problem);
            return;
        }

        // An override comes from Retry, which must repeat the turn's OWN question rather than whatever is in
        // the box — the box now holds unsent drafts, and those are precious.
        var typed = questionOverride ?? Question;
        var question = string.IsNullOrWhiteSpace(typed) ? null : typed.Trim();
        var turn = StartTurn(
            task, question, state.SelectionText, clearBox: questionOverride is null,
            // Where the reader was standing when they asked, captured at the START of the turn rather than at
            // its end: by the time an answer arrives they may have scrolled on, and the position a stored turn
            // is asking to be taken back to is the one it was asked from. (#849)
            readingPosition: state.ReadingPosition);

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
                state.SelectionUnavailable,
                // Read from the transcript AFTER StartTurn, so the turn just added is excluded by identity
                // rather than by an index the next change to this method could invalidate. (#991)
                HistoryFor(turn));

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

            // Written here, after the turn is closed, so the record holds the final status, the final elapsed
            // figure and whatever text stood when it stopped. Awaited rather than fired and forgotten: the
            // caller's IsBusy is still true, which is what keeps a new conversation from being started out from
            // under the write. (#849)
            await SaveTurnAsync(turn);
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

    /// <inheritdoc cref="NewConversationCommand"/>
    private void NewConversation()
    {
        if (IsBusy) return;

        LetGoOfSession();
        RefreshSessionsSoon();
    }

    /// <summary>
    /// Empty the panel and forget which conversation it was in — what a new conversation does, and what deleting
    /// the active one leaves behind. Writes nothing: every turn was saved when it ended.
    /// </summary>
    private void LetGoOfSession()
    {
        Turns.Clear();
        this.RaisePropertyChanged(nameof(HasTurns));
        this.RaisePropertyChanged(nameof(LastTurn));

        _session = null;

        if (_appState is not null)
        {
            _appState.Current.ActiveAssistantSessionId = null;
            _appState.MarkDirty();
        }

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
                // And the citation ITSELF, not only the two lines drawn from it. This event was the only place
                // it ever appeared and the panel used to drop it here, which left a stored turn readable and
                // unnavigable — nothing in "Mahāvaggapāḷi — para 12" names a file to reopen. (#849)
                turn.StructuredCitation = context.Citation;
                // Which model answered. Structured for the same reason (#849): recovering it from the Sent
                // block would mean matching a field by its English label.
                turn.ProviderId = context.ProviderId;
                turn.ModelId = context.ModelId;
                turn.Notices.Clear();
                foreach (var notice in context.Notices) turn.Notices.Add(notice);
                turn.RaiseNoticesChanged();
                turn.IsPartialPassage = context.PassageTrimmed;
                turn.Sent = context.Sent;
                turn.Status = WaitingMessage(_elapsed.Elapsed, sawReasoning: false);
                break;

            case AiTurnEventKind.Text:
            {
                // Two views of one delta, and either half can be empty (see AiTurnEventKind.Text). The marked
                // half is what the model wrote, markers and all — kept for replay to the model and bound to
                // nothing; the visible half is what the reader sees.
                var visible = e.Text is { Length: > 0 };
                lock (_pendingGate)
                {
                    if (e.MarkedText is { Length: > 0 } marked) turn.AppendMarkedAnswer(marked);
                    if (visible)
                    {
                        turn.AppendAnswer(e.Text!);
                        _pendingAnswer = true;
                    }
                }

                // Only renderable text is progress: a delta the filter swallowed whole has put nothing on
                // screen, so the waiting message still has something to say.
                if (!visible) break;

                // Text on screen IS the progress report; the counter has nothing left to say.
                _sawText = true;
                turn.Status = "";
                break;
            }

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
                // The report as well as the line printed from it: the line has been through :N0, so the same
                // turn reads 1,024 or 1.024 depending on the machine that stored it, and no arithmetic can be
                // done on it afterwards. (#849)
                turn.UsageReport = usage;
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

    /// <summary>
    /// Stops the turn in flight. What the visible stop control calls.
    ///
    /// <para><b>The caller's token first, and the order is the whole point.</b> The orchestrator ends a turn
    /// quietly when the cancellation was not the caller's — <c>catch (OperationCanceledException) when
    /// (!callerToken.IsCancellationRequested)</c> — because being superseded is not a failure. Cancelling
    /// its internal source first opens a gap: if the stream's exception reaches that filter before the
    /// caller token is cancelled, the filter matches, the turn ends by the superseded path, and the reader
    /// gets a partial answer with no "Stopped." line, indistinguishable from a finished one.</para>
    ///
    /// <para>Cancelling the caller's token first makes the filter fail deterministically, so a stop the
    /// reader asked for always reports itself as one. (R6-4)</para>
    /// </summary>
    private void Stop()
    {
        _turnCancellation?.Cancel();
        _orchestrator?.Stop();
    }

    private AiTurnViewModel StartTurn(
        AiTask task, string? question, string? selection, bool clearBox,
        Models.ReadingPositionToken? readingPosition = null)
    {
        var turn = new AiTurnViewModel(task, question)
        {
            // On screen from the first second, so a stale selection — one made before scrolling away and
            // forgotten — is visible immediately rather than discovered in the answer.
            Subject = Summarize(selection),
            Status = "Contacting the provider…",
            ReadingPosition = readingPosition,
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
        turn.ElapsedTime = _elapsed.Elapsed;
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
        // The duration as well as the line printed from it — the stopwatch is restarted by the next turn, so
        // after this the turn itself is the only thing that still knows. (#849)
        turn.ElapsedTime = _elapsed.Elapsed;
        turn.Elapsed = FormatElapsed(_elapsed.Elapsed);
        turn.IsRunning = false;
        _current = null;
    }

    // ---- Persistence (#849) ----------------------------------------------------------------------

    /// <summary>
    /// Write the conversation, including the turn that has just ended.
    ///
    /// <para><b>The whole session, every turn</b> — not an append. The turn that ended is not the only thing
    /// that changed about the file: <c>LastActive</c> moved, and on the first turn the name was set. A
    /// whole-file replace is also the only version of this that cannot leave unreadable JSON behind.</para>
    ///
    /// <para><b>A failure is a sentence, never an exception.</b> The store answers false; the answer is on
    /// screen either way, and the reader is reading it. What they are told is that the conversation will not be
    /// there next time — which is the part they can act on, by copying the answer.</para>
    /// </summary>
    private async Task SaveTurnAsync(AiTurnViewModel turn)
    {
        if (_store is null) return;

        try
        {
            var session = _session ??= CreateSession(turn);

            session.Turns.Add(turn.ToRecord());
            session.LastActive = DateTimeOffset.Now;

            // Deliberately NOT the turn's cancellation token. A stopped turn is exactly the one whose partial
            // answer most needs keeping, and handing the save the token the reader has just cancelled would
            // abandon the write that was supposed to keep it.
            if (!await _store.SaveAsync(session))
            {
                // The store has already logged why. What is left to say is what it means for the reader.
                Status = "That answer could not be saved, so this conversation will not reopen next time.";
            }
        }
        catch (Exception ex)
        {
            // The store promises not to throw; this catch is for the mapping above it, and the rule is the same
            // either way — a turn the reader can read must not be lost to a problem with a file.
            _logger.Error(ex, "Could not record the assistant turn");
            Status = "That answer could not be saved, so this conversation will not reopen next time.";
        }

        // Whatever the save did: the row's turn count and recency moved if it worked, and the first turn of a
        // conversation is what puts it in the list at all. (#997)
        //
        // NOT awaited. This runs in RunAsync's finally, before AskAsync clears IsBusy, and the store lists by
        // reading every session file in full: awaited, every answer held Stop visible and every command disabled
        // while the whole list was parsed — measured by review at 361–488 ms and ~820 MB allocated per turn with
        // 200 sessions of 30 turns. The list is cosmetic here; nothing in the turn waits on it.
        RefreshSessionsSoon();
    }

    /// <summary>
    /// Start a list refresh without waiting for it. <see cref="RefreshSessionsAsync"/> catches its own failures,
    /// so the continuation is a second net — an exception must reach the log, not the unobserved-task handler.
    /// </summary>
    private void RefreshSessionsSoon()
    {
        _ = RefreshSessionsAsync().ContinueWith(
            t => _logger.Error(t.Exception, "Could not refresh the assistant session list"),
            CancellationToken.None,
            TaskContinuationOptions.OnlyOnFaulted,
            TaskScheduler.Default);
    }

    /// <summary>
    /// The session this panel is now adding to, named from the turn that created it.
    ///
    /// <para><b>[fsnow]</b> chose <i>"Auto from the first turn, renamable"</i>.</para>
    ///
    /// <para>So the name is composed here, once, and never by a model. A preset turn is named for what was asked
    /// and where (<c>Explain · Mahāvaggapāḷi para 12</c>); a question is named by its own first words, because
    /// that is what the reader will recognise in a list and the preset label would be the same on every one of
    /// them.</para>
    /// </summary>
    private AiSession CreateSession(AiTurnViewModel turn)
    {
        var now = DateTimeOffset.Now;
        var session = new AiSession
        {
            Id = AiSession.NewId(),
            Name = NameFor(turn),
            Created = now,
            LastActive = now,
        };

        if (_appState is not null)
        {
            // The one thing application state keeps, so the next launch knows which file to reopen. Dirtied
            // rather than saved: the state service owns when it writes.
            _appState.Current.ActiveAssistantSessionId = session.Id;
            _appState.MarkDirty();
        }

        return session;
    }

    /// <summary>The auto-name. <b>[fsnow]</b>: <i>"Auto from the first turn, renamable"</i>.</summary>
    internal static string NameFor(AiTurnViewModel turn)
    {
        // A question names itself. Its first words are what distinguishes it from the next question about the
        // same passage, which the preset label and citation would not.
        if (turn.Task == AiTask.Ask && !string.IsNullOrWhiteSpace(turn.Question))
            return Shorten(turn.Question!, 60);

        var citation = turn.Citation;
        return string.IsNullOrWhiteSpace(citation)
            // No citation to name it by — a turn that failed before the context was assembled. The preset alone
            // is a poor name and still better than an empty row; a rename is one gesture away.
            ? turn.PresetLabel
            : $"{turn.PresetLabel} · {citation}";
    }

    /// <summary>
    /// The first <paramref name="max"/> characters, whitespace collapsed, with an ellipsis where there was more.
    /// Cut at the end rather than elided in the middle, unlike <see cref="Summarize"/>: a name is read from its
    /// start, and a list of names sharing a prefix is the case the reader is scanning for.
    ///
    /// <para><b>Cut on a text element, not a UTF-16 index.</b> <c>collapsed[..max]</c> lands between the halves
    /// of a surrogate pair whenever the 60th character is one — the JSON writer then substitutes U+FFFD, so the
    /// session is named with a replacement character rather than the word the reader typed. Nothing is lost and
    /// it looks like corruption, which for an auto-name is most of the damage. (fable review)</para>
    /// </summary>
    private static string Shorten(string text, int max)
    {
        var collapsed = System.Text.RegularExpressions.Regex.Replace(text.Trim(), @"\s+", " ");
        if (collapsed.Length <= max) return collapsed;

        // Walk text elements — a pair, a combining sequence or an emoji cluster counts once, the way a reader
        // counts characters — and stop at the last boundary that fits.
        var enumerator = System.Globalization.StringInfo.GetTextElementEnumerator(collapsed);
        var cut = 0;
        var taken = 0;
        while (enumerator.MoveNext())
        {
            if (taken == max) break;
            cut = enumerator.ElementIndex + ((string)enumerator.Current).Length;
            taken++;
        }

        return $"{collapsed[..cut].TrimEnd()}…";
    }

    /// <summary>
    /// Reload the last conversation, silently. <b>[fsnow]</b>: <i>"Restore the last session silently"</i>.
    ///
    /// <para><b>Called by the app after application state has loaded, not from the constructor.</b> The panel is
    /// built during the dock layout build, which runs BEFORE <c>LoadStateAsync</c> completes — so a constructor
    /// reading <c>ActiveAssistantSessionId</c> would read the default empty state and restore nothing, every
    /// time. The same sequencing <c>SearchViewModel.ApplyState</c> (#87) and <c>DictionaryViewModel.ApplyState</c>
    /// (#479) exist for.</para>
    ///
    /// <para>Idempotent, and it never overwrites a conversation in progress: a panel that already has turns has
    /// been used, and this is a launch-time restore rather than a switch (<see cref="SwitchToSessionAsync"/>).
    /// Both go through <see cref="ReadTranscriptAsync"/> and <see cref="ShowSession"/>, so a conversation reopened
    /// at launch and one switched to from the list render identically — two paths would be two chances to
    /// differ.</para>
    ///
    /// <para><b>The list is filled here too, whether or not anything is restored</b> (#997): a first launch, or
    /// one whose active id never reached disk, still has conversations to offer.</para>
    /// </summary>
    public async Task RestoreAsync(CancellationToken ct = default)
    {
        try
        {
            await RestoreActiveAsync(ct);
        }
        finally
        {
            await RefreshSessionsAsync(ct);
        }
    }

    private async Task RestoreActiveAsync(CancellationToken ct)
    {
        if (_store is null || _appState is null) return;
        if (_session is not null || Turns.Count > 0) return;

        var id = _appState.Current.ActiveAssistantSessionId;
        if (string.IsNullOrEmpty(id)) return;

        var read = await ReadTranscriptAsync(id, ct);

        switch (read.Outcome)
        {
            case TranscriptOutcome.Missing:
            case TranscriptOutcome.Unreadable:
                // The panel starts empty either way, and only the unreadable case has anything to say — which
                // OnSessionUnreadable has said, because this load was announced.
                return;

            case TranscriptOutcome.Failed:
                Status = "The last conversation could not be reopened. It is still on disk.";
                return;
        }

        // Re-checked after the await: the reader can ask a question while the file is being read, and the two
        // must not interleave. A live turn wins — it is on screen, and this is only a restore.
        if (_session is not null || Turns.Count > 0)
        {
            _logger.Information(
                "Not restoring assistant session {Id}: the panel was used while it was loading", id);
            return;
        }

        ShowSession(read.Session!, read.Turns!);

        _logger.Information(
            "Restored assistant session {Id} with {Count} turn(s)", id, read.Turns!.Count);
    }

    private enum TranscriptOutcome
    {
        Loaded,

        /// <summary>No such file.</summary>
        Missing,

        /// <summary>A file the store could not read; it has been kept aside, and the reader told where.</summary>
        Unreadable,

        /// <summary>The file read, and mapping it to turns threw.</summary>
        Failed,
    }

    private readonly record struct Transcript(
        TranscriptOutcome Outcome, AiSession? Session, IReadOnlyList<AiTurnViewModel>? Turns);

    /// <summary>
    /// Read a conversation and map it to turns, touching nothing on the panel. Shared by the launch restore and
    /// by a switch, so the two cannot drift.
    ///
    /// <para><b>Reading and MAPPING are both inside the guard.</b> The store hardens what it reads, but nothing
    /// hardens what the mapping then does with it — a stored duration outside TimeSpan's range is an
    /// OverflowException in FromRecord, from a file the store considered well-formed. Left outside, that exception
    /// unwound through the app's awaited restore call and skipped every restore after it: window state, the
    /// active tool, and the reader's open books. A malformed transcript must cost the transcript, nothing else.
    /// (fable review)</para>
    ///
    /// <para><b>Whole or nothing.</b> Every turn is mapped before any reaches the caller, so a transcript is shown
    /// whole or not at all — half a conversation on screen would read as a conversation.</para>
    /// </summary>
    private async Task<Transcript> ReadTranscriptAsync(string id, CancellationToken ct)
    {
        try
        {
            var (session, unreadable) = await LoadAnnouncedAsync(id, ct);
            if (session is null)
                return new Transcript(
                    unreadable ? TranscriptOutcome.Unreadable : TranscriptOutcome.Missing, null, null);

            // By id, so a turn's replayed-history can be rebuilt from the ids it stored rather than from copies
            // of earlier answers. Built over the whole session first: the references point backwards today, and
            // a restore that depended on that would break the day compaction reorders anything.
            var byId = new Dictionary<string, AiTurnRecord>(StringComparer.Ordinal);
            foreach (var record in session.Turns)
                byId[record.Id] = record;

            var turns = new List<AiTurnViewModel>(session.Turns.Count);
            foreach (var record in session.Turns)
                turns.Add(AiTurnViewModel.FromRecord(record, byId));

            return new Transcript(TranscriptOutcome.Loaded, session, turns);
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not open the assistant session {Id}", id);
            return new Transcript(TranscriptOutcome.Failed, null, null);
        }
    }

    /// <summary>
    /// Put a read conversation on screen and make it the one the panel adds to — and the one the next launch
    /// reopens. Application state is dirtied only when the id actually changes, so a launch-time restore (which
    /// read the id from state) does not schedule a save for nothing.
    /// </summary>
    private void ShowSession(AiSession session, IReadOnlyList<AiTurnViewModel> turns)
    {
        Turns.Clear();
        foreach (var turn in turns) Turns.Add(turn);

        this.RaisePropertyChanged(nameof(HasTurns));
        this.RaisePropertyChanged(nameof(LastTurn));

        _session = session;

        if (_appState is not null && _appState.Current.ActiveAssistantSessionId != session.Id)
        {
            _appState.Current.ActiveAssistantSessionId = session.Id;
            _appState.MarkDirty();
        }
    }

    /// <summary>
    /// The id a switch is loading, while it loads. Delete refuses it, as it refuses the active session while a
    /// turn runs: the switch is about to make it active, and showing a conversation whose file has just gone would
    /// have the next turn's save write it straight back.
    /// </summary>
    private string? _switchingTo;

    /// <summary>
    /// Show a listed conversation in the panel, in place of the one there. (#997)
    ///
    /// <para><b>Nothing is saved on the way out</b> — the conversation being left was written when its last turn
    /// ended, exactly as for <see cref="NewConversationCommand"/>. <b>[fsnow]</b>: <i>"…while saving the current
    /// one."</i></para>
    ///
    /// <para><b>Refused while a turn is in flight</b>, like every other command: a turn's save writes to the
    /// session the panel holds when it ENDS, and swapping that session mid-stream would file the answer in the
    /// conversation switched to. The switch holds <see cref="IsBusy"/> itself while it reads, for the same reason
    /// in the other direction — a question asked mid-load would be saved into the conversation being left, and
    /// then wiped from the screen by the load finishing.</para>
    ///
    /// <para>Switching to the conversation already on screen does nothing. <b>A target that cannot be read leaves
    /// the current conversation on screen</b> and says why in <see cref="Status"/>: an unreadable file has been
    /// kept aside and <c>Unreadable</c> has said where; a missing one (deleted from another window, or by hand)
    /// gets its own sentence. The list is refreshed either way, which removes the row that could not be
    /// opened.</para>
    ///
    /// <para>[observed] <b>For the view:</b> because a switch holds <see cref="IsBusy"/> while it reads, everything
    /// bound to it behaves as during a turn — the Stop control shows (and has nothing to stop), and the presets and
    /// + disable — for as long as the file takes to read.</para>
    /// </summary>
    public async Task SwitchToSessionAsync(string? id)
    {
        if (_store is null || string.IsNullOrEmpty(id)) return;
        if (IsBusy) return;
        if (_session is not null && string.Equals(_session.Id, id, StringComparison.Ordinal)) return;

        IsBusy = true;
        _switchingTo = id;
        var done = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        _switchDone = done.Task;
        UpdateRowAvailability();

        try
        {
            var read = await ReadTranscriptAsync(id, CancellationToken.None);

            switch (read.Outcome)
            {
                case TranscriptOutcome.Loaded:
                    ShowSession(read.Session!, read.Turns!);
                    Status = "";
                    _logger.Information(
                        "Switched to assistant session {Id} with {Count} turn(s)", id, read.Turns!.Count);
                    break;

                case TranscriptOutcome.Unreadable:
                    // OnSessionUnreadable has told the reader where THIS file was kept; saying "not there" on top
                    // of it would be wrong.
                    break;

                case TranscriptOutcome.Missing:
                    Status = "That conversation is no longer on disk.";
                    break;

                case TranscriptOutcome.Failed:
                    Status = "That conversation could not be opened. It is still on disk.";
                    break;
            }
        }
        finally
        {
            _switchingTo = null;
            IsBusy = false;
            done.TrySetResult();
        }

        await RefreshSessionsAsync();
    }

    /// <summary>Completes when the switch in progress (if any) has landed or failed. A rename of the session being
    /// switched to waits on it — see <see cref="RenameSessionAsync"/>.</summary>
    private Task _switchDone = Task.CompletedTask;

    /// <summary>
    /// Give a conversation the reader's own name. <b>[fsnow]</b>: <i>"Auto from the first turn, renamable"</i> —
    /// Claude Code's <c>/rename</c>. Answers whether a name was written. (#997)
    ///
    /// <para><b>Trimmed, and an empty or whitespace name is refused</b>, keeping the old one: a row with no name is
    /// a row the reader cannot find again, and the auto-name was at least something. Nothing else is imposed on
    /// the name — no length cap was asked for, so none is invented.</para>
    ///
    /// <para><b>A rename does not move the conversation in the list.</b> The list is ordered by
    /// <see cref="AiSession.LastActive"/>, which only a turn ending sets; a rename writes the same field back
    /// unchanged. [suggestion] Two reasons: "last active" is shown on the row as when the conversation was last
    /// used, and tidying a name is not using it; and a row that jumped to the top the moment it was renamed would
    /// move out from under the reader working down the list. The store sorts by that field, not by file time,
    /// so the rewrite itself cannot reorder anything.</para>
    ///
    /// <para><b>The active conversation is renamed on the object the panel holds</b>, not on a fresh copy read off
    /// disk: the next turn's save writes that object in full, and a copy renamed beside it would be overwritten
    /// by the old name. For the same reason a listed conversation that becomes active while its file is being read
    /// here is renamed on the panel's object instead.</para>
    ///
    /// <para><b>A rename of the conversation a switch is loading waits for the switch to land</b>, then renames the
    /// object it put on screen. Without the wait, the rename wrote the new name to disk and the switch — which had
    /// already read the file — showed the old one, and the next turn's save wrote the old name back. (review probe
    /// P3) Deferring rather than refusing keeps what the reader typed.</para>
    ///
    /// <para><b>Allowed while a turn runs.</b> The rename's save and the turn's save both write the whole session
    /// object, both from this thread. That is safe because of an ordering in <c>AiSessionStore.SaveAsync</c> that its
    /// contract does not state [observed]: it serializes the session <i>synchronously, before</i> it awaits its write
    /// lock, so each save's JSON is a snapshot taken at the moment it was called, and the two cannot interleave
    /// inside one serialization. Writes then go through the lock in the order the saves were called, so the later
    /// call — which holds everything the earlier one did — lands last. A store that serialized after taking the lock,
    /// or off this thread, would need this reconsidered.</para>
    /// </summary>
    public async Task<bool> RenameSessionAsync(string? id, string? name)
    {
        if (_store is null || string.IsNullOrEmpty(id)) return false;

        var trimmed = name?.Trim();
        if (string.IsNullOrEmpty(trimmed)) return false;

        var written = false;
        try
        {
            if (string.Equals(_switchingTo, id, StringComparison.Ordinal)) await _switchDone;

            AiSession? target = ActiveSession(id);
            var unreadable = false;
            if (target is null)
            {
                (var loaded, unreadable) = await LoadAnnouncedAsync(id, CancellationToken.None);

                // Switched to while it was being read: the panel's object is the one that will be saved from now on.
                target = ActiveSession(id) ?? loaded;
            }

            if (target is null)
            {
                // An unreadable file has been announced with where it was kept; that sentence stays.
                if (!unreadable) Status = "That conversation is no longer on disk.";
            }
            else if (target.Name == trimmed)
            {
                written = true;
            }
            else
            {
                target.Name = trimmed;
                written = await _store.SaveAsync(target);
                if (!written) Status = "That conversation could not be renamed.";
            }
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not rename the assistant session {Id}", id);
            Status = "That conversation could not be renamed.";
        }

        await RefreshSessionsAsync();
        return written;
    }

    private AiSession? ActiveSession(string id) =>
        _session is not null && string.Equals(_session.Id, id, StringComparison.Ordinal) ? _session : null;

    /// <summary>
    /// Delete a conversation. <b>No confirmation here</b> — <b>[fsnow]</b> chose <i>"Yes, with confirmation"</i>,
    /// and the question belongs to the view that shows the row; this is what runs once the reader has said yes.
    /// (#997)
    ///
    /// <para><b>Deleting the conversation on screen empties the panel</b>, exactly as <see
    /// cref="NewConversationCommand"/> would: the transcript clears and <c>ActiveAssistantSessionId</c> is cleared,
    /// so neither the next turn nor the next launch brings it back. "On screen" includes the case where the panel
    /// is empty because the active session could not be read — the id in application state is what the next
    /// launch would try, so it goes too.</para>
    ///
    /// <para><b>Refused only for the conversation a turn is running in, or the one a switch is loading</b>: that
    /// turn's save, or the next one after the switch lands, would write the file straight back. Any other row can
    /// be deleted mid-turn.</para>
    ///
    /// <para><b>The active conversation is let go of BEFORE anything is awaited.</b> Nothing is running (the guard
    /// above), so nothing can start between the check and the let-go, and no later turn can save into the session
    /// being deleted. The first cut let go only after re-reading the list, and a newer refresh overtaking that one
    /// left the check reading a stale list: the reader was told the delete failed, the transcript stayed on screen,
    /// and the next turn wrote the file back. (review probes P1, P1b)</para>
    ///
    /// <para><b>Whether it worked is decided by the delete, not by the list.</b> The store answers false both for
    /// "no such file" (the work is done) and for a file it could not remove, so a false is followed by a load: a
    /// session still there is a delete that failed. Then the conversation is put back on screen, if the panel has
    /// not been used since. The list refresh afterwards is cosmetic.</para>
    /// </summary>
    public async Task DeleteSessionAsync(string? id)
    {
        if (_store is null || string.IsNullOrEmpty(id)) return;

        if (IsBusy && (IsActiveId(id) || string.Equals(_switchingTo, id, StringComparison.Ordinal)))
            return;

        var wasActive = IsActiveId(id);
        var heldSession = wasActive ? _session : null;
        var heldTurns = wasActive ? Turns.ToList() : null;
        if (wasActive) LetGoOfSession();

        bool gone;
        try
        {
            // Not an announced load: a file that turns out unreadable here is gone from the list either way, and
            // telling the reader about it in the middle of deleting it would be noise. It is logged.
            gone = await _store.DeleteAsync(id) || await _store.LoadAsync(id) is null;
        }
        catch (Exception ex)
        {
            // The store promises not to throw; this is the second net, as everywhere else in the panel.
            _logger.Error(ex, "Could not delete the assistant session {Id}", id);
            gone = false;
        }

        if (!gone)
        {
            if (wasActive && _session is null && Turns.Count == 0 && !IsBusy
                && string.IsNullOrEmpty(_appState?.Current.ActiveAssistantSessionId))
            {
                if (heldSession is not null)
                {
                    ShowSession(heldSession, heldTurns!);
                }
                else if (_appState is not null)
                {
                    _appState.Current.ActiveAssistantSessionId = id;
                    _appState.MarkDirty();
                }
            }

            Status = "That conversation could not be deleted.";
        }

        await RefreshSessionsAsync();
    }

    private bool IsActiveId(string id) =>
        ActiveSession(id) is not null
        || string.Equals(_appState?.Current.ActiveAssistantSessionId, id, StringComparison.Ordinal);

    private int _sessionsGeneration;

    /// <summary>
    /// Re-read the session list and bring <see cref="Sessions"/> into line with it: rows for sessions that are
    /// gone are removed, rows that remain are updated in place and moved into the store's order, new sessions get
    /// new rows. Never throws — a list that cannot be read keeps the rows it had.
    ///
    /// <para><b>Only the newest refresh lands.</b> Refreshes overlap (a turn's save and a rename can both trigger
    /// one), and the store reads files, so an older listing can finish after a newer one; applying it would put
    /// back a row that has just been deleted.</para>
    ///
    /// <para>[observed] The store reads every session file in full to list them — there is no index, a trade it
    /// records as right "while a session list is tens of files". With retention <i>"Forever, no cap"</i> and a
    /// refresh after every turn, that is the cost to watch; the reads happen off the UI thread.</para>
    /// </summary>
    internal async Task RefreshSessionsAsync(CancellationToken ct = default)
    {
        if (_store is null) return;

        var generation = ++_sessionsGeneration;

        IReadOnlyList<AiSessionSummary> summaries;
        try
        {
            summaries = await _store.ListAsync(ct);
        }
        catch (OperationCanceledException)
        {
            return;
        }
        catch (Exception ex)
        {
            _logger.Error(ex, "Could not list the assistant sessions");
            return;
        }

        if (generation != _sessionsGeneration) return;

        var wanted = new HashSet<string>(summaries.Select(s => s.Id), StringComparer.Ordinal);
        for (var i = Sessions.Count - 1; i >= 0; i--)
            if (!wanted.Contains(Sessions[i].Id)) Sessions.RemoveAt(i);

        for (var i = 0; i < summaries.Count; i++)
        {
            var summary = summaries[i];
            var at = IndexOfRow(summary.Id);

            if (at < 0)
            {
                Sessions.Insert(i, new AiSessionRowViewModel(summary));
                continue;
            }

            Sessions[at].Update(summary);
            if (at != i) Sessions.Move(at, i);
        }

        UpdateRowAvailability();
        this.RaisePropertyChanged(nameof(HasSessions));
    }

    private int IndexOfRow(string id)
    {
        for (var i = 0; i < Sessions.Count; i++)
            if (string.Equals(Sessions[i].Id, id, StringComparison.Ordinal)) return i;
        return -1;
    }

    /// <summary>
    /// Which row is the active one, and which rows may be deleted now. Called whenever either input changes: the
    /// list, <see cref="IsBusy"/>, or the session the panel holds (every change of which is followed by a
    /// refresh).
    ///
    /// <para>Active is the session the panel HOLDS, not the id in application state: the two differ only when the
    /// active conversation could not be read, and then nothing is on screen for a row to claim.</para>
    /// </summary>
    private void UpdateRowAvailability()
    {
        foreach (var row in Sessions)
        {
            var active = _session is not null && string.Equals(row.Id, _session.Id, StringComparison.Ordinal);
            row.IsActive = active;
            row.CanDelete = !(IsBusy
                              && (active || string.Equals(row.Id, _switchingTo, StringComparison.Ordinal)));
        }

        ActiveSessionName = Sessions.FirstOrDefault(r => r.IsActive)?.Name ?? NewConversationName;
    }

    /// <summary>
    /// A transcript that could not be read. It has been kept aside, not deleted, and this is the only place the
    /// reader learns either fact.
    ///
    /// <para><b>Posted, because this arrives off the UI thread.</b> The store raises it from inside the catch in
    /// its own read, after an <c>await … ConfigureAwait(false)</c> — so a thread-pool continuation. Assigning
    /// <see cref="Status"/> there writes a bound property, Avalonia verifies thread access on that write, and
    /// the resulting exception unwinds through the store's catch and out of its load: the file would be kept
    /// aside correctly and the reader would be told nothing, which is the one outcome this handler exists to
    /// prevent. (fable review)</para>
    ///
    /// <para><b>Guarded rather than always posted</b>, so the sentence still arrives when the report does come
    /// from the UI thread — and so a test host, which has no dispatcher loop to pump a posted callback, can see
    /// it at all. <c>CheckAccess</c> is true on every thread until an <c>Application</c> binds the dispatcher
    /// (measured), which is exactly the difference between the two cases.</para>
    /// </summary>
    private void OnSessionUnreadable(AiSessionUnreadable report)
    {
        // Only a load the reader asked for is announced: the launch restore of the active conversation, a switch,
        // a rename. The list is read at launch and after every turn, and a broken file found THERE is about a
        // conversation the reader never opened — announcing it would put a sentence about some other conversation
        // on the panel after every answer until the file was moved. It is kept aside by the store either way, and
        // logged. (#997 review)
        //
        // Marked before anything is posted, so the caller can tell "unreadable, and already said so" from "simply
        // gone" when its load returns, without waiting on the dispatcher. Keyed by the report's own id, so a
        // listing that trips over file Y while a switch is loading X cannot be mistaken for X's report.
        if (!_announceUnreadable.TryUpdate(report.Id, true, false)
            && !_announceUnreadable.ContainsKey(report.Id))
        {
            _logger.Warning(
                "An assistant session found while listing ({Id}) could not be read and has been kept at {Path}",
                report.Id, report.KeptPath);
            return;
        }

        var sentence = $"That conversation could not be read. It has been kept at {report.KeptPath}.";

        if (Dispatcher.UIThread.CheckAccess()) Status = sentence;
        else Dispatcher.UIThread.Post(() => Status = sentence);
    }

    /// <summary>
    /// The ids whose load the reader asked for, while it runs — and whether an unreadable report has arrived for
    /// each. See <see cref="OnSessionUnreadable"/>. Concurrent because the store raises its report from a
    /// thread-pool continuation.
    /// </summary>
    private readonly System.Collections.Concurrent.ConcurrentDictionary<string, bool> _announceUnreadable =
        new(StringComparer.Ordinal);

    /// <summary>
    /// Load a session the reader asked for, so that an unreadable file is announced on the panel rather than only
    /// logged. Answers the session (null when missing or unreadable) and whether it was reported unreadable.
    /// </summary>
    private async Task<(AiSession? Session, bool Unreadable)> LoadAnnouncedAsync(string id, CancellationToken ct)
    {
        _announceUnreadable[id] = false;
        try
        {
            var session = await _store!.LoadAsync(id, ct);
            return (session, _announceUnreadable.TryGetValue(id, out var reported) && reported);
        }
        finally
        {
            _announceUnreadable.TryRemove(id, out _);
        }
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
    /// The conversation as the model is shown it: every earlier turn on screen, oldest first. (#991)
    ///
    /// <para><b>Assembled from the transcript rather than kept as a second list.</b> What the reader can see is
    /// what the model is told, with no third place for the two to drift apart — and it is why Retry re-asks with
    /// the history as it stands now rather than as it stood when the turn it repeats was first sent.</para>
    ///
    /// <para><b>A turn with no answer text is left out.</b> A failed turn is a request the model never answered,
    /// so there is no assistant reply to replay and a lone user message would tell it a question was asked and
    /// silently dropped. A turn with PARTIAL text goes in as it stands: it is on screen, the reader is reading
    /// it, and a follow-up will be about what they read.</para>
    ///
    /// <para><b>What goes back is <see cref="AiTurnViewModel.MarkedAnswer"/>, never <c>Answer</c>.</b> The
    /// screen shows the answer with its <c>[[…]]</c> Pāli markers stripped; the system prompt tells the model to
    /// put those markers on every Pāli span. Replaying the stripped text would hand it a transcript of its own
    /// answers ignoring that instruction, and a model shown its own apparent practice follows it. <b>[fsnow]</b>,
    /// on fixing this before #991 merged: <i>"I want to fix this before we merge."</i></para>
    ///
    /// <para>The turn being started is excluded — it is already in <see cref="Turns"/> by the time this runs,
    /// and its own context is what the current message carries.</para>
    ///
    /// <para><b>A restored turn replays like any other</b> (#849): it carries the marked answer it was written
    /// with, and its question line is the one it was stored with rather than one re-rendered from today's
    /// wording — see <see cref="AiTurnViewModel.RestoredAskedLine"/>. So a follow-up asked after a restart
    /// continues the conversation instead of starting one.</para>
    ///
    /// <para><b>Which turns went in is recorded on the turn</b>, because the stored record says what the model
    /// was sent and that cannot be recomputed later: by the time this turn is written, a newer turn may have
    /// changed what "the turns with answers" means.</para>
    /// </summary>
    private IReadOnlyList<AiExchange> HistoryFor(AiTurnViewModel current)
    {
        // Whitespace-only counts as no answer, matching AiChatOrchestrator.Replay, which drops a pair with an
        // empty half so no request carries an empty content block. Selecting on HasAnswer alone recorded such a
        // turn in ReplayedTurnIds and then never sent it, so the stored record claimed the model saw a turn it
        // did not. One rule, applied in both places. (fable review)
        var replayed = Turns
            .Where(t => !ReferenceEquals(t, current) && !string.IsNullOrWhiteSpace(t.MarkedAnswer))
            .ToList();

        current.ReplayedTurnIds = replayed.Select(t => t.Id).ToList();

        return replayed
            .Select(t => new AiExchange(DescribeAsked(t), t.MarkedAnswer))
            .ToList();
    }

    /// <summary>
    /// The question side of an earlier turn, as the model is shown it again: which preset, which passage, and
    /// the reader's own words where there were any. (#991)
    ///
    /// <para><b>Built from the app's own chrome, never re-gathered and never parsed out of anything the model
    /// said.</b> The citation is the line the panel already drew from <see cref="CitationRef"/>, which is what
    /// keeps a replayed conversation as trustworthy as the screen it came from; the preset label is there
    /// because "Translate" and "Grammar" asked different things of the same passage and the answers alone do not
    /// say which was asked.</para>
    ///
    /// <para><b>The passage itself is not here.</b> Replaying each turn's full context would send the same
    /// paragraph once per turn; the citation tells the model which passage an earlier answer was about, which is
    /// what a follow-up needs once the reader has moved on. A follow-up that needs the text of a passage the
    /// reader has left will not get it — ask about the passage you are in.</para>
    ///
    /// <para>Nothing in here moves between turns: no clock, no turn number, no count — so an earlier turn is
    /// replayed identically for as long as it is on screen. That is the property a future prompt-cache prefix
    /// would need from this half of the request; the system prompt does not have it today, for reasons recorded
    /// on <see cref="AiChatOrchestrator"/>.</para>
    /// </summary>
    internal static string DescribeAsked(AiTurnViewModel turn)
    {
        // A turn read off disk replays the line it was stored with. The record keeps that line precisely so a
        // later change to the wording below cannot alter what a restored conversation tells the model it was
        // asked — a record of what a model saw that re-renders itself is not a record. (#849)
        if (turn.RestoredAskedLine is { Length: > 0 } stored) return stored;

        var opening = $"\u00ab{turn.PresetLabel}\u00bb";
        var head = string.IsNullOrWhiteSpace(turn.Citation) ? opening : $"{opening} \u2014 {turn.Citation}";
        return turn.HasQuestion ? $"{head}: {turn.Question!.Trim()}" : head;
    }

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
