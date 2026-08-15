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
    private readonly System.Diagnostics.Stopwatch _elapsed = new();
    /// <summary>
    /// True while the turn has produced nothing yet, so the tick may own <see cref="Status"/>. Cleared the
    /// moment anything real arrives — an error message must never be overwritten by a progress counter.
    /// </summary>
    private bool _awaitingFirstToken;
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

    /// <summary>The full citation — canon path and every printed page — for the headline's tooltip.</summary>
    public string CitationDetail
    {
        get => _citationDetail;
        private set => this.RaiseAndSetIfChanged(ref _citationDetail, value);
    }
    private string _citationDetail = "";

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

    public bool HasNotices => Notices.Count > 0;

    /// <summary>
    /// The collapsed header. Says how the request was ASSEMBLED, not that something failed: every one of
    /// these is a fact about the input, and at full weight beside the answer they read as a list of errors.
    /// </summary>
    public string NoticesHeader => Notices.Count == 1
        ? "1 note about this request"
        : $"{Notices.Count} notes about this request";

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
                CitationDetail = DescribeCitationDetail(context.Citation);
                Notices.Clear();
                foreach (var notice in context.Notices) Notices.Add(notice);
                this.RaisePropertyChanged(nameof(HasNotices));
                this.RaisePropertyChanged(nameof(NoticesHeader));
                IsPartialPassage = context.PassageTrimmed;
                Status = "Thinking…";
                _awaitingFirstToken = true;
                break;

            case AiTurnEventKind.Text when e.Text is { Length: > 0 }:
                lock (_pendingGate) _pending.Append(e.Text);
                HasAnswer = true;
                // Text on screen IS the progress report; the counter has nothing left to say.
                _awaitingFirstToken = false;
                Status = "";
                break;

            case AiTurnEventKind.Reasoning:
                // Segregated by contract and dropped here. Never concatenated into the answer: it is the
                // model thinking aloud, not what it is telling the reader.
                break;

            case AiTurnEventKind.Usage when e.Usage is { } usage:
                Usage = FormatUsage(usage);
                break;

            case AiTurnEventKind.Error when e.Error is { } error:
                _awaitingFirstToken = false;
                Flush();
                // Partial text stands — a mid-stream failure keeps what arrived.
                Status = error.Message;
                break;

            case AiTurnEventKind.Completed:
                _awaitingFirstToken = false;
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
        CitationDetail = "";
        Usage = "";
        Notices.Clear();
        this.RaisePropertyChanged(nameof(HasNotices));
        this.RaisePropertyChanged(nameof(NoticesHeader));
        IsPartialPassage = false;
        HasAnswer = false;
        Status = "Preparing…";
        lock (_pendingGate) _pending.Clear();

        _turn?.Dispose();
        _turn = new CancellationTokenSource();
        IsBusy = true;

        _elapsed.Restart();
        _flushTimer = new DispatcherTimer { Interval = TimeSpan.FromMilliseconds(FlushIntervalMs) };
        _flushTimer.Tick += (_, _) =>
        {
            Flush();
            if (_awaitingFirstToken) Status = WaitingMessage(_elapsed.Elapsed);
        };
        _flushTimer.Start();
    }

    /// <summary>
    /// What to say while nothing has come back yet.
    ///
    /// <para>
    /// A silent wait is the norm on a free or shared endpoint, not a fault: an observed turn against a 550B
    /// model on a <c>:free</c> tier sat for two minutes and then returned a gateway timeout, while a
    /// different request to the same model answered in thirty seconds. The app cannot shorten that — our
    /// HTTP timeout is deliberately infinite, because a finite one truncates long streams and reports it as
    /// a cancellation — so the least it can do is look like waiting rather than like nothing happening, and
    /// name the likely reason before the user concludes the button is broken.
    /// </para>
    /// </summary>
    internal static string WaitingMessage(TimeSpan elapsed) => elapsed.TotalSeconds switch
    {
        < 5 => "Thinking…",
        < 30 => $"Thinking… {elapsed.TotalSeconds:0}s",
        _ => $"Still waiting… {elapsed.TotalSeconds:0}s. Free and shared endpoints can queue behind other "
             + "requests, and a large model can take minutes.",
    };

    private void EndTurn()
    {
        _flushTimer?.Stop();
        _flushTimer = null;
        _awaitingFirstToken = false;
        _elapsed.Stop();
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
    /// The citation as ONE quiet line, built from the bundle rather than parsed out of the answer: the
    /// book's own name and where in it.
    ///
    /// <para>
    /// Deliberately not the full nav path, and deliberately not every page. The bundle's book name is a
    /// path — <c>tipiṭaka (mūla)/sutta piṭaka/dīgha nikāya/mahāvaggapāḷi</c> — and since #561 the pages
    /// cover every edition the window touches, which for a four-paragraph window is eight references. Both
    /// in full, in bold, ran to four lines and buried the answer they were supposed to caption. The full
    /// version lives in <see cref="DescribeCitationDetail"/>, on the tooltip.
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
    /// Everything the headline leaves out: where the book sits in the canon, and every printed page the
    /// window covers, one line per edition. On the tooltip because a reader checking a claim against print
    /// wants it, and a reader reading the answer does not.
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
