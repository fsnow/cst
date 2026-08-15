using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.ViewModels.Dock;
using CST.Navigation;
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
/// </summary>
public class AiAssistantViewModel : ReactiveTool
{
    private readonly IAiChatOrchestrator? _orchestrator;
    private readonly IReaderStateService? _readerState;
    private readonly IChatProviderResolver? _resolver;
    private readonly ISettingsService? _settings;
    private readonly ILogger _logger = Log.ForContext<AiAssistantViewModel>();

    /// <summary>
    /// Accumulates deltas between UI flushes. A stream can deliver dozens of tokens a second, and binding
    /// every one of them repaints and re-measures the whole answer — the panel would fight the model for the
    /// UI thread on exactly the machines least able to spare it.
    /// </summary>
    private readonly StringBuilder _pending = new();
    private readonly object _pendingGate = new();
    private DispatcherTimer? _flushTimer;

    /// <summary>
    /// How often streamed text reaches the screen. Fast enough to read as live, slow enough that a fast
    /// stream cannot saturate the UI thread.
    /// </summary>
    private const int FlushIntervalMs = 100;

    private CancellationTokenSource? _turn;
    private string _answer = "";
    private string _question = "";
    private string _citation = "";
    private string _usage = "";
    private string _status = "";
    private bool _isBusy;
    private bool _hasAnswer;

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
        StopCommand = ReactiveCommand.Create(Stop);
    }

    public ReactiveCommand<Unit, Unit> ExplainCommand { get; }
    public ReactiveCommand<Unit, Unit> TranslateCommand { get; }
    public ReactiveCommand<Unit, Unit> GrammarCommand { get; }
    public ReactiveCommand<Unit, Unit> WordByWordCommand { get; }
    public ReactiveCommand<Unit, Unit> StopCommand { get; }

    /// <summary>The user's own question, optional. The presets work with it empty.</summary>
    public string Question
    {
        get => _question;
        set => this.RaiseAndSetIfChanged(ref _question, value);
    }

    /// <summary>The answer so far. Bound to a SELECTABLE control — readers copy translations.</summary>
    public string Answer
    {
        get => _answer;
        private set => this.RaiseAndSetIfChanged(ref _answer, value);
    }

    /// <summary>
    /// What the answer is about, rendered by us from the bundle's citation — never from model output.
    /// </summary>
    public string Citation
    {
        get => _citation;
        private set => this.RaiseAndSetIfChanged(ref _citation, value);
    }

    /// <summary>Tokens in and out, once the provider reports them. The user is paying for these.</summary>
    public string Usage
    {
        get => _usage;
        private set => this.RaiseAndSetIfChanged(ref _usage, value);
    }

    /// <summary>
    /// The one line that says what is happening: not configured, thinking, offline, failed. Never an
    /// exception message — §10 requires every failure to arrive as a sentence.
    /// </summary>
    public string Status
    {
        get => _status;
        private set => this.RaiseAndSetIfChanged(ref _status, value);
    }

    public bool IsBusy
    {
        get => _isBusy;
        private set
        {
            this.RaiseAndSetIfChanged(ref _isBusy, value);
            this.RaisePropertyChanged(nameof(CanAsk));
        }
    }

    /// <summary>True once a turn has produced anything, so the answer area only appears when it has content.</summary>
    public bool HasAnswer
    {
        get => _hasAnswer;
        private set => this.RaiseAndSetIfChanged(ref _hasAnswer, value);
    }

    public bool CanAsk => !IsBusy;

    /// <summary>
    /// Degradations the user should see: a trimmed passage, a selection outside the window, a missing asset.
    /// Rendered as a list rather than folded into the answer, because they are statements about the INPUT and
    /// the answer is the model's.
    /// </summary>
    public ObservableCollection<string> Notices { get; } = new();

    /// <summary>
    /// Whether the passage was trimmed to fit the budget — #586's partial-passage badge. Distinct from a
    /// notice because it changes how far the answer can be trusted: the model did not see all of it.
    /// </summary>
    public bool IsPartialPassage
    {
        get => _isPartialPassage;
        private set => this.RaiseAndSetIfChanged(ref _isPartialPassage, value);
    }
    private bool _isPartialPassage;

    /// <summary>
    /// Runs one turn. Every expected failure — not configured, no book, an unreadable position, a dead
    /// network — arrives as <see cref="Status"/> text rather than an exception.
    /// </summary>
    internal async Task AskAsync(AiTask task)
    {
        if (IsBusy) return;

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
                Status = problem + " (Settings → AI)";
                return;
            }
        }

        var reader = await _readerState.GetCurrentAsync();
        if (reader.State is not { } state)
        {
            Status = Describe(reader.Problem);
            return;
        }

        StartTurn();

        try
        {
            var request = new AiTurnRequest(
                task,
                state.BookId,
                new NavigationReference.Paragraph(state.Paragraph),
                state.SelectionText,
                string.IsNullOrWhiteSpace(Question) ? null : Question.Trim(),
                // Carried rather than collapsed into "no selection": a selection the reader could not read is
                // a different state, and conflating them is what makes a dropped selection look to the user
                // like the assistant ignored it. (#581)
                state.SelectionUnavailable);

            await foreach (var e in _orchestrator.RunAsync(request, _turn!.Token))
                Handle(e);
        }
        catch (OperationCanceledException)
        {
            // The user's own stop. Whatever streamed already stands.
            Status = "Stopped.";
        }
        catch (Exception ex)
        {
            // The orchestrator promises not to throw for expected states, so anything here is a defect —
            // logged as one, and still shown as a sentence rather than a stack trace.
            _logger.Error(ex, "Assistant turn failed unexpectedly");
            Status = "Something went wrong running that request.";
        }
        finally
        {
            EndTurn();
        }
    }

    private void Handle(AiTurnEvent e)
    {
        switch (e.Kind)
        {
            case AiTurnEventKind.Started when e.Context is { } context:
                // Chrome first: the citation arrives before any text, so the panel can say what it is about
                // while the model is still thinking.
                Citation = Describe(context.Citation);
                Notices.Clear();
                foreach (var notice in context.Notices) Notices.Add(notice);
                IsPartialPassage = context.Notices.Count > 0 &&
                                   context.Notices.Any(n => n.Contains("trim", StringComparison.OrdinalIgnoreCase)
                                                            || n.Contains("shorten", StringComparison.OrdinalIgnoreCase));
                Status = "Thinking…";
                break;

            case AiTurnEventKind.Text when e.Text is { Length: > 0 }:
                lock (_pendingGate) _pending.Append(e.Text);
                HasAnswer = true;
                break;

            case AiTurnEventKind.Reasoning:
                // Segregated by contract and dropped here. Never concatenated into the answer: it is the
                // model thinking aloud, not what it is telling the reader.
                break;

            case AiTurnEventKind.Usage when e.Usage is { } usage:
                Usage = FormatUsage(usage);
                break;

            case AiTurnEventKind.Error when e.Error is { } error:
                Flush();
                // Partial text stands — a mid-stream failure keeps what arrived.
                Status = error.Message;
                break;

            case AiTurnEventKind.Completed:
                Flush();
                Status = "";
                break;
        }
    }

    /// <summary>Stops the turn in flight. What the visible stop control calls.</summary>
    private void Stop()
    {
        _orchestrator?.Stop();
        _turn?.Cancel();
    }

    private void StartTurn()
    {
        Answer = "";
        Citation = "";
        Usage = "";
        Notices.Clear();
        IsPartialPassage = false;
        HasAnswer = false;
        Status = "Preparing…";
        lock (_pendingGate) _pending.Clear();

        _turn?.Dispose();
        _turn = new CancellationTokenSource();
        IsBusy = true;

        _flushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FlushIntervalMs) };
        _flushTimer.Tick += (_, _) => Flush();
        _flushTimer.Start();
    }

    private void EndTurn()
    {
        _flushTimer?.Stop();
        _flushTimer = null;
        Flush();
        IsBusy = false;
    }

    /// <summary>Moves whatever has accumulated into the bound property, in one property change.</summary>
    private void Flush()
    {
        string chunk;
        lock (_pendingGate)
        {
            if (_pending.Length == 0) return;
            chunk = _pending.ToString();
            _pending.Clear();
        }

        Answer += chunk;
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
    /// The citation line, built from the bundle rather than from the answer. Deliberately plain: it names the
    /// book and where in it, which is what a reader needs to check the claim against the text.
    /// </summary>
    internal static string Describe(CitationRef citation)
    {
        if (citation is null) return "";

        // The printed pages the passage covers — a VRI page number is what lets a reader put a finger on the
        // text and check the claim. Formatted by the PROMPT BUILDER's own helper, deliberately: the pages
        // named on screen and the pages named to the model must be the same string, or a reader comparing
        // them finds a discrepancy that does not exist. SnippetPageRef is a record, so its ToString would
        // also have rendered "SnippetPageRef { Edition = Vri, … }" straight into the panel — and since #561
        // a window can cover several pages, that would have been a line of them.
        var pages = citation.Pages is { Count: > 0 }
            ? " · " + string.Join(", ", citation.Pages.Select(PromptBuilder.PageRef))
            : "";

        return string.IsNullOrWhiteSpace(citation.NormalizedReference)
            ? citation.BookName + pages
            : $"{citation.BookName} — {citation.NormalizedReference}{pages}";
    }
}
