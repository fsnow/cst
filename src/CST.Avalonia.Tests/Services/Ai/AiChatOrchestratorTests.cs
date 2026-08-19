using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CST;
using CST.Avalonia.Models;
using CST.Avalonia.Services;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services.Ai;
using CST.Navigation;
using CST.Search;
using CST.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// The orchestrator end to end, against a fake provider that echoes the assembled request back. (#583)
///
/// <para>What is actually under test is the contract a panel depends on: that no expected failure throws, that
/// a superseded turn ends quietly while the caller's own cancellation does not, that usage survives a
/// mid-stream failure, and that a turn which produced no answer is reported as one rather than as success.</para>
/// </summary>
public class AiChatOrchestratorTests
{
    // ---- Doubles ------------------------------------------------------------------------------------------

    /// <summary>Records the request it was given and replays a scripted stream.</summary>
    private sealed class FakeProvider : IChatProvider
    {
        private readonly IReadOnlyList<ChatDelta> _deltas;
        private readonly AiException? _throwBeforeStreaming;
        private readonly TaskCompletionSource? _blockAfterFirst;

        internal FakeProvider(
            IEnumerable<ChatDelta>? deltas = null,
            AiException? throwBeforeStreaming = null,
            TaskCompletionSource? blockAfterFirst = null)
        {
            _deltas = deltas?.ToList() ?? new List<ChatDelta> { ChatDelta.ForText("Heedfulness is the path.") };
            _throwBeforeStreaming = throwBeforeStreaming;
            _blockAfterFirst = blockAfterFirst;
        }

        public string Id => "fake";
        internal ChatRequest? LastRequest { get; private set; }

        // Blocking is ONE-SHOT per provider, not per stream. The supersede test resolves the same provider for
        // both turns, so a per-stream block would park the replacement too and nothing would ever cancel it.
        private bool _hasBlocked;

        public async IAsyncEnumerable<ChatDelta> StreamAsync(
            ChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            LastRequest = request;
            if (_throwBeforeStreaming is not null) throw _throwBeforeStreaming;

            var first = true;
            foreach (var delta in _deltas)
            {
                yield return delta;

                if (first && _blockAfterFirst is not null && !_hasBlocked)
                {
                    first = false;
                    _hasBlocked = true;
                    // Park until cancelled — lets a test supersede a turn that is genuinely mid-stream.
                    _blockAfterFirst.TrySetResult();
                    await Task.Delay(Timeout.Infinite, ct).ConfigureAwait(false);
                }
            }
        }
    }

    private sealed class FixedResolver : IChatProviderResolver
    {
        private readonly ChatProviderResolution? _resolution;
        private readonly string? _problem;

        internal FixedResolver(IChatProvider? provider, string? problem = null)
        {
            _resolution = provider is null ? null : new ChatProviderResolution(provider, "test-model");
            _problem = problem;
        }

        public ChatProviderResolution? Resolve(out string? problem)
        {
            problem = _problem;
            return _resolution;
        }
    }

    private sealed class StubBundler : IAiContextBundler
    {
        private readonly Exception? _throw;

        internal StubBundler(Exception? toThrow = null) => _throw = toThrow;

        internal AiContextRequest? LastRequest { get; private set; }

        public Task<AiContextBundle> BuildAsync(AiContextRequest request, CancellationToken ct = default)
        {
            LastRequest = request;
            if (_throw is not null) throw _throw;

            var pages = Array.Empty<SnippetPageRef>();
            var passage = new PassageResult(
                request.BookId, "paragraph 21 (dhp)", "Appamādo amatapadaṃ.", pages, 21, "dhp", null, null, 0,
                Array.Empty<ApparatusNote>());

            return Task.FromResult(new AiContextBundle(
                request.Task, request.OutputLanguage, request.UserQuestion, passage,
                Selection: null,
                Lemmas: Array.Empty<LemmaEntry>(),
                Book: new BookContext(request.BookId, "Dhammapadapāḷi", Pitaka.Sutta, CommentaryLevel.Mula),
                Citation: new CitationRef(request.BookId, "Dhammapadapāḷi", "paragraph 21 (dhp)", pages),
                Provenance: new Provenance("test", null),
                Budget: new BudgetReport(
                    new[] { new BundlePart(BundlePartNames.Passage, BundlePartState.Included, "a window") },
                    100, ParagraphsCovered: 1)));
        }
    }

    // ---- Fixture ------------------------------------------------------------------------------------------

    private static ISettingsService Settings(string language = "English")
    {
        var settings = new Settings();
        settings.Ai.Chat.AnswerLanguage = language;
        var mock = new Mock<ISettingsService>();
        mock.SetupGet(s => s.Settings).Returns(settings);
        return mock.Object;
    }

    private static AiChatOrchestrator Orchestrator(
        IChatProvider? provider = null,
        string? notConfigured = null,
        IAiContextBundler? bundler = null,
        string language = "English",
        IAiConnectionService? connections = null)
    {
        // A store pointed at a directory that does not exist: no user override can be picked up, so every test
        // runs against the shipped templates.
        var templates = new PromptTemplateStore(
            Path.Combine(Path.GetTempPath(), "cst-orch-" + Guid.NewGuid().ToString("N")),
            NullLogger<PromptTemplateStore>.Instance);

        return new AiChatOrchestrator(
            new FixedResolver(provider, notConfigured),
            bundler ?? new StubBundler(),
            new PromptBuilder(templates),
            Settings(language),
            NullLogger<AiChatOrchestrator>.Instance,
            connections);
    }

    private static AiTurnRequest Request(AiTask task = AiTask.Explain) =>
        new(task, "s0502m.mul.xml", new NavigationReference.Paragraph(21));

    private static async Task<List<AiTurnEvent>> CollectAsync(
        IAiChatOrchestrator orchestrator, AiTurnRequest? request = null, CancellationToken ct = default)
    {
        var events = new List<AiTurnEvent>();
        await foreach (var e in orchestrator.RunAsync(request ?? Request(), ct))
            events.Add(e);
        return events;
    }

    private static string TextOf(IEnumerable<AiTurnEvent> events) =>
        string.Concat(events.Where(e => e.Kind == AiTurnEventKind.Text).Select(e => e.Text));

    // ---- The happy path -----------------------------------------------------------------------------------

    [Fact]
    public async Task A_turn_assembles_a_request_and_streams_an_answer()
    {
        var provider = new FakeProvider();

        var events = await CollectAsync(Orchestrator(provider));

        Assert.Equal(AiTurnEventKind.Started, events[0].Kind);
        Assert.Equal(AiTurnEventKind.Completed, events[^1].Kind);
        Assert.Equal("Heedfulness is the path.", TextOf(events));

        // The assembled request is the thing worth asserting: the prompt layer's output reaches the wire.
        var sent = provider.LastRequest!;
        Assert.Equal("test-model", sent.Model);
        Assert.Contains("Appamādo amatapadaṃ.", sent.Messages.Single().Content);
        Assert.Contains("paragraph 21 (dhp)", sent.System);
    }

    [Fact]
    public async Task The_citation_reaches_the_caller_before_any_text()
    {
        // The panel draws its chrome from this while the model is still thinking; arriving late would mean an
        // answer rendered with no scope beside it.
        var events = await CollectAsync(Orchestrator(new FakeProvider()));

        var started = Assert.IsType<AiTurnContext>(events[0].Context);
        Assert.Equal("paragraph 21 (dhp)", started.Citation.NormalizedReference);
        Assert.Equal("Dhammapadapāḷi", started.Book.Name);
    }

    [Fact]
    public async Task The_configured_answer_language_reaches_the_bundle()
    {
        var bundler = new StubBundler();

        await CollectAsync(Orchestrator(new FakeProvider(), bundler: bundler, language: "Burmese"));

        Assert.Equal("Burmese", bundler.LastRequest!.OutputLanguage);
    }

    [Fact]
    public async Task Pali_markers_are_stripped_from_the_answer_and_counted()
    {
        // Split across deltas on purpose — the marker can straddle a chunk boundary, and the count is what
        // #587 uses to decide whether script conversion is safe for a model.
        var provider = new FakeProvider(new[]
        {
            ChatDelta.ForText("The phrase [" ),
            ChatDelta.ForText("[appamādo]"),
            ChatDelta.ForText("] opens it."),
        });

        var events = await CollectAsync(Orchestrator(provider));

        Assert.Equal("The phrase appamādo opens it.", TextOf(events));
        Assert.Equal(new PaliMarkerReport(1, 0), events[^1].Markers);
    }

    [Fact]
    public async Task Reasoning_is_delivered_separately_from_the_answer()
    {
        var provider = new FakeProvider(new[]
        {
            ChatDelta.ForReasoning("Let me parse the compound."),
            ChatDelta.ForText("It is a dvanda."),
        });

        var events = await CollectAsync(Orchestrator(provider));

        Assert.Equal("It is a dvanda.", TextOf(events));
        Assert.Equal("Let me parse the compound.",
            events.Single(e => e.Kind == AiTurnEventKind.Reasoning).Text);
    }

    [Fact]
    public async Task Usage_is_merged_per_field_across_the_stream()
    {
        // The Anthropic stream reports the halves at opposite ends of the turn, so a consumer that lets a later
        // delta supersede an earlier one erases the input count.
        var provider = new FakeProvider(new[]
        {
            ChatDelta.ForUsage(new ChatUsage(1200, null)),
            ChatDelta.ForText("An answer."),
            ChatDelta.ForUsage(new ChatUsage(null, 350)),
        });

        var events = await CollectAsync(Orchestrator(provider));

        var usage = events.Single(e => e.Kind == AiTurnEventKind.Usage).Usage!;
        Assert.Equal(1200, usage.InputTokens);
        Assert.Equal(350, usage.OutputTokens);
    }

    // ---- Failures -----------------------------------------------------------------------------------------

    [Fact]
    public async Task Not_configured_is_an_event_rather_than_an_exception()
    {
        // The panel has to render this as a sentence pointing at Settings — never a raw error. (§10)
        var events = await CollectAsync(
            Orchestrator(provider: null, notConfigured: "No API key is stored for Claude."));

        var error = Assert.Single(events).Error!;
        Assert.Equal(AiErrorKind.NotConfigured, error.Kind);
        Assert.Contains("No API key", error.Message);
    }

    [Fact]
    public async Task An_unassemblable_passage_fails_before_anything_leaves_the_machine()
    {
        var provider = new FakeProvider();

        var events = await CollectAsync(Orchestrator(
            provider, bundler: new StubBundler(new AiContextException("No passage text for 's0502m.mul.xml'."))));

        var error = Assert.Single(events).Error!;
        Assert.Equal(AiErrorKind.ContextUnavailable, error.Kind);
        Assert.Null(provider.LastRequest);   // nothing was sent
    }

    [Fact]
    public async Task A_pre_stream_provider_failure_becomes_a_terminal_error_event()
    {
        var provider = new FakeProvider(
            throwBeforeStreaming: new AiException(new AiError(AiErrorKind.Unauthorized, "The API key was rejected.")));

        var events = await CollectAsync(Orchestrator(provider));

        Assert.Equal(AiErrorKind.Unauthorized, events[^1].Error!.Kind);
        Assert.Equal(AiTurnEventKind.Started, events[0].Kind);   // the citation still rendered
    }

    // ---- what a turn teaches us about the endpoint (#673) --------------------------------------------------

    private static (IAiConnectionService Service, string Id) Connected()
    {
        var settings = new CST.Avalonia.Models.Settings();
        var mock = new Mock<ISettingsService>();
        mock.SetupGet(s => s.Settings).Returns(settings);
        var service = new AiConnectionService(mock.Object);
        service.Add("mine", new AiConnectionDraft(
            "Mine", ChatProviderKind.OpenAiCompatible, "https://example.test/v1",
            new[] { new AiModelEntry("m", "M") },
            new Dictionary<string, string>(), new Dictionary<string, string>()));
        return (service, "mine");
    }

    /// <summary>
    /// A refusal with a status code proves the endpoint answered.
    ///
    /// <para>Reported from use: a Cerebras connection returned HTTP 402 and was still described as never
    /// checked. The old rule only ever recorded a NETWORK failure (unreachable) or a turn that produced
    /// output (reachable), so every HTTP-level refusal taught the app nothing — though a status code is
    /// precisely the evidence that something was there to send one.</para>
    /// </summary>
    [Theory]
    [InlineData(402)]
    [InlineData(401)]
    [InlineData(429)]
    public async Task A_refusal_with_a_status_code_marks_the_endpoint_reachable(int status)
    {
        var (service, id) = Connected();
        var provider = new FakeProvider(throwBeforeStreaming: new AiException(
            new AiError(AiErrorKind.Provider, "refused", StatusCode: status)));

        await CollectAsync(Orchestrator(provider, connections: service));

        Assert.Equal(Reachability.Reachable, service.Connections.Single(c => c.Id == id).State);
    }

    /// <summary>A network failure is the one that means unreachable — nothing answered, so there is no status
    /// code to have come from anywhere.</summary>
    [Fact]
    public async Task A_network_failure_marks_the_endpoint_unreachable()
    {
        var (service, id) = Connected();
        var provider = new FakeProvider(throwBeforeStreaming: new AiException(
            new AiError(AiErrorKind.Network, "The endpoint could not be reached.")));

        await CollectAsync(Orchestrator(provider, connections: service));

        Assert.Equal(Reachability.Unreachable, service.Connections.Single(c => c.Id == id).State);
    }

    /// <summary>A failure that never left the machine says nothing about the endpoint: no request was sent,
    /// so there is nothing to have learned.</summary>
    [Fact]
    public async Task A_failure_with_no_status_code_teaches_nothing()
    {
        var (service, id) = Connected();
        var provider = new FakeProvider(throwBeforeStreaming: new AiException(
            new AiError(AiErrorKind.Provider, "something went wrong locally")));

        await CollectAsync(Orchestrator(provider, connections: service));

        Assert.Equal(Reachability.Configured, service.Connections.Single(c => c.Id == id).State);
    }

    [Fact]
    public async Task A_mid_stream_failure_keeps_the_partial_answer_and_still_reports_usage()
    {
        // Losing the partial would be worse than showing it with an error under it — and the tokens were spent
        // either way, so the user is owed the count.
        var provider = new FakeProvider(new[]
        {
            ChatDelta.ForText("Heedfulness is "),
            ChatDelta.ForUsage(new ChatUsage(900, 12)),
            ChatDelta.ForError(new AiError(AiErrorKind.Network, "The stream ended unexpectedly.")),
            ChatDelta.ForText("never reached"),
        });

        var events = await CollectAsync(Orchestrator(provider));

        Assert.Equal("Heedfulness is ", TextOf(events));
        Assert.Equal(900, events.Single(e => e.Kind == AiTurnEventKind.Usage).Usage!.InputTokens);
        Assert.Equal(AiErrorKind.Network, events[^1].Error!.Kind);
    }

    [Fact]
    public async Task A_turn_that_produced_only_reasoning_is_reported_as_an_empty_answer()
    {
        // #601: the model spends its whole budget thinking. The provider layer is correct to segregate
        // reasoning, and that correctness is what would otherwise leave the caller a successful blank turn.
        var provider = new FakeProvider(new[] { ChatDelta.ForReasoning("Thinking at length...") });

        var events = await CollectAsync(Orchestrator(provider));

        var error = events[^1].Error!;
        Assert.Equal(AiErrorKind.EmptyAnswer, error.Kind);
        Assert.Contains("reasoning", error.Message);
        Assert.DoesNotContain(events, e => e.Kind == AiTurnEventKind.Completed);
    }

    [Fact]
    public async Task A_wholly_empty_response_is_also_an_empty_answer()
    {
        var events = await CollectAsync(Orchestrator(new FakeProvider(Array.Empty<ChatDelta>())));

        Assert.Equal(AiErrorKind.EmptyAnswer, events[^1].Error!.Kind);
    }

    // ---- Truncation (#601) ---------------------------------------------------------------------------------

    private static FakeProvider TruncatedAfter(params ChatDelta[] deltas) =>
        new(deltas.Append(ChatDelta.ForError(new AiError(
            AiErrorKind.Truncated, "The model reached its output limit and stopped before finishing.",
            ProviderCode: "length"))));

    [Fact]
    public async Task A_truncated_answer_keeps_its_text_and_says_it_is_incomplete()
    {
        // THE case this exists for. Without it a half-finished translation renders under a citation exactly like
        // a finished one — the stream ends the same way either way — and nothing on screen says otherwise.
        var events = await CollectAsync(Orchestrator(TruncatedAfter(
            ChatDelta.ForText("Heedfulness is the path to the deathless; heedlessness is"))));

        var error = events[^1].Error!;
        Assert.Equal(AiErrorKind.Truncated, error.Kind);
        Assert.Contains("incomplete", error.Message);
        Assert.Equal("Heedfulness is the path to the deathless; heedlessness is", TextOf(events));
        Assert.DoesNotContain(events, e => e.Kind == AiTurnEventKind.Completed);
    }

    [Fact]
    public async Task A_turn_truncated_before_any_answer_names_the_reasoning_as_the_cause()
    {
        // #601's original observation: minimax-m3 spent 3,177 completion tokens reasoning about a two-line verse
        // and never wrote the translation. "Raise the limit" is the actionable advice; a retry is not.
        var events = await CollectAsync(Orchestrator(TruncatedAfter(ChatDelta.ForReasoning("Thinking..."))));

        var error = events[^1].Error!;
        Assert.Equal(AiErrorKind.Truncated, error.Kind);
        Assert.Contains("reasoning", error.Message);
        Assert.Contains("higher output limit", error.Message);
    }

    [Fact]
    public async Task A_turn_truncated_before_anything_at_all_says_so_plainly()
    {
        var error = (await CollectAsync(Orchestrator(TruncatedAfter())))[^1].Error!;

        Assert.Equal(AiErrorKind.Truncated, error.Kind);
        Assert.DoesNotContain("reasoning", error.Message);
    }

    [Fact]
    public async Task Truncation_does_not_report_itself_as_an_empty_answer()
    {
        // Both describe a blank panel, but only one of them KNOWS why. Measured beats inferred: EmptyAnswer must
        // not shadow a truncation the provider actually observed.
        var events = await CollectAsync(Orchestrator(TruncatedAfter(ChatDelta.ForReasoning("Thinking..."))));

        Assert.DoesNotContain(
            events, e => e.Kind == AiTurnEventKind.Error && e.Error!.Kind == AiErrorKind.EmptyAnswer);
    }

    [Fact]
    public async Task A_truncated_turn_still_reports_what_it_spent()
    {
        // The whole point of a cap being hit is that the user paid for the tokens. Reporting the failure without
        // the number would hide the one fact that explains it.
        var events = await CollectAsync(Orchestrator(TruncatedAfter(
            ChatDelta.ForText("Heedfulness"),
            ChatDelta.ForUsage(new ChatUsage(412, 3177)))));

        Assert.Equal(3177, events.Single(e => e.Kind == AiTurnEventKind.Usage).Usage!.OutputTokens);
        Assert.Equal(AiErrorKind.Truncated, events[^1].Error!.Kind);
    }

    [Fact]
    public async Task The_provider_code_survives_onto_the_reported_error()
    {
        // The message is rewritten here; the machine-readable token from the provider must not be lost with it.
        var error = (await CollectAsync(Orchestrator(TruncatedAfter(ChatDelta.ForText("x")))))[^1].Error!;

        Assert.Equal("length", error.ProviderCode);
    }

    // ---- Cancellation and cancel-and-replace --------------------------------------------------------------

    [Fact]
    public async Task Stopping_a_turn_ends_it_quietly_and_keeps_what_was_written()
    {
        // Stop is a user action, not a failure — an error banner under an answer they chose to halt would be
        // both wrong and alarming.
        var streaming = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeProvider(
            new[] { ChatDelta.ForText("Heedfulness is "), ChatDelta.ForText("never reached") },
            blockAfterFirst: streaming);
        var orchestrator = Orchestrator(provider);

        var events = new List<AiTurnEvent>();
        var run = Task.Run(async () =>
        {
            await foreach (var e in orchestrator.RunAsync(Request()))
                events.Add(e);
        });

        await streaming.Task;
        orchestrator.Stop();
        await run;

        Assert.Equal("Heedfulness is ", TextOf(events));
        Assert.DoesNotContain(events, e => e.Kind == AiTurnEventKind.Error);
        Assert.DoesNotContain(events, e => e.Kind == AiTurnEventKind.Completed);
    }

    [Fact]
    public async Task A_second_turn_supersedes_the_first_rather_than_queueing_behind_it()
    {
        // Cancel-and-replace: a reader who re-asks wants the new answer, not to watch one they abandoned.
        var streaming = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var first = new FakeProvider(
            new[] { ChatDelta.ForText("first"), ChatDelta.ForText("never reached") },
            blockAfterFirst: streaming);
        var orchestrator = Orchestrator(first);

        var firstEvents = new List<AiTurnEvent>();
        var firstRun = Task.Run(async () =>
        {
            await foreach (var e in orchestrator.RunAsync(Request()))
                firstEvents.Add(e);
        });

        await streaming.Task;

        var secondEvents = await CollectAsync(orchestrator, Request(AiTask.Translate));
        await firstRun;

        Assert.Equal("first", TextOf(firstEvents));                       // partial, no error
        Assert.DoesNotContain(firstEvents, e => e.Kind == AiTurnEventKind.Error);
        Assert.Equal(AiTurnEventKind.Completed, secondEvents[^1].Kind);   // the replacement ran to completion
    }

    [Fact]
    public async Task The_callers_own_cancellation_throws_as_any_async_method_would()
    {
        // Distinct from being superseded: a consumer that cancels its own enumeration must not be quietly told
        // the turn ended normally.
        var streaming = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
        var provider = new FakeProvider(
            new[] { ChatDelta.ForText("partial"), ChatDelta.ForText("never reached") },
            blockAfterFirst: streaming);
        using var cts = new CancellationTokenSource();

        var run = CollectAsync(Orchestrator(provider), ct: cts.Token);
        await streaming.Task;
        cts.Cancel();

        await Assert.ThrowsAnyAsync<OperationCanceledException>(() => run);
    }

    [Fact]
    public void Stopping_with_no_turn_in_flight_is_harmless()
    {
        Orchestrator(new FakeProvider()).Stop();
    }

    [Fact]
    public async Task The_started_event_carries_what_was_actually_sent()
    {
        // Assembled in the orchestrator because it is the only place holding the bundle, the rendered prompt
        // and the resolved provider at once -- so it is also the only place that can be wrong about them
        // without anything noticing.
        var events = await CollectAsync(Orchestrator(new FakeProvider()));

        var started = events.First(e => e.Kind == AiTurnEventKind.Started);
        var sent = started.Context!.Sent;

        Assert.NotNull(sent);
        Assert.False(string.IsNullOrWhiteSpace(sent!.SystemPrompt));
        Assert.False(string.IsNullOrWhiteSpace(sent.UserContent));

        // The named fields a reader needs to tell one turn from another.
        Assert.Contains(sent.Fields, f => f.Name == "Model");
        Assert.Contains(sent.Fields, f => f.Name == "Provider");
        Assert.Contains(sent.Fields, f => f.Name == "Book");
        Assert.Contains(sent.Fields, f => f.Name == "Estimated context");

        // Including every gathered part, present or absent: an absence is as much a part of what was sent as
        // a presence, and much harder to notice.
        Assert.Contains(sent.Fields, f => f.Name.StartsWith("Part: "));
    }
}
