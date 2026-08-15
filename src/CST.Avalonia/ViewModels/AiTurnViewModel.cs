using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using System.Text;
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
/// </summary>
public sealed class AiTurnViewModel : ReactiveObject
{
    private readonly StringBuilder _answer = new();
    private readonly StringBuilder _reasoning = new();

    public AiTurnViewModel(AiTask task, string? question)
    {
        Task = task;
        Question = question;
        PresetLabel = LabelFor(task);
    }

    public AiTask Task { get; }

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
        _ => task.ToString(),
    };

    // ---- The answer ------------------------------------------------------------------------------

    private IReadOnlyList<AnswerSpan> _spans = Array.Empty<AnswerSpan>();

    /// <summary>
    /// The answer as styled spans. Bound to a single selectable control: models emit light Markdown — and we
    /// teach them to, since the prompts are Markdown — so rendering it as plain text put literal asterisks on
    /// screen.
    /// </summary>
    public IReadOnlyList<AnswerSpan> Spans
    {
        get => _spans;
        private set => this.RaiseAndSetIfChanged(ref _spans, value);
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

    /// <summary>Re-parse and publish. Called once per flush, not once per delta.</summary>
    internal void PublishAnswer()
    {
        Spans = AnswerMarkup.Parse(_answer.ToString());
        this.RaisePropertyChanged(nameof(Answer));
    }

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
}
