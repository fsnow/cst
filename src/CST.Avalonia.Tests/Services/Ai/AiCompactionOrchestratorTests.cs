using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CST;
using CST.Avalonia.Models;
using CST.Avalonia.Models.Ai;
using CST.Avalonia.Services;
using CST.Avalonia.Services.Ai;
using CST.Navigation;
using CST.Search;
using CST.Tools;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// Compaction as the orchestrator does it: the summariser call, the automatic trigger, and compact-and-retry on a
/// too-long rejection. (#998)
///
/// <para><b>[fsnow]</b>: <i>"Manual and auto at a fraction of context length"</i>, <i>"95%, but make this a
/// setting"</i>, <i>"Last 4 turns"</i>. What is pinned here is the contract a panel depends on: that the summariser
/// is sent ONLY the turns being compacted, markers intact; that the summary is the first replayed exchange with the
/// roles alternating and nothing empty; that the trigger fires at the threshold and not one token below it; and that
/// an unknown context length turns the trigger off and says so.</para>
///
/// <para>The provider is scripted per CALL, because a compacting turn makes two or three of them — the summary, then
/// the turn, and on a too-long rejection the turn again — and each has to be told apart and asserted on.</para>
/// </summary>
public class AiCompactionOrchestratorTests
{
    // ---- Doubles ------------------------------------------------------------------------------------------

    /// <summary>One scripted response: deltas to stream, or an exception to throw before streaming.</summary>
    private sealed record Response(IReadOnlyList<ChatDelta>? Deltas = null, AiException? Throw = null)
    {
        internal static Response Say(string text) => new(new[] { ChatDelta.ForText(text) });
    }

    /// <summary>Serves the script in order and records every request. Past the end of the script it answers
    /// "An answer." so a test only scripts the calls it cares about.</summary>
    private sealed class ScriptedProvider : IChatProvider
    {
        private readonly Queue<Response> _script;

        internal ScriptedProvider(params Response[] script) => _script = new Queue<Response>(script);

        public string Id => "fake";

        internal List<ChatRequest> Requests { get; } = new();

        public async IAsyncEnumerable<ChatDelta> StreamAsync(
            ChatRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            Requests.Add(request);
            var response = _script.Count > 0 ? _script.Dequeue() : Response.Say("An answer.");
            if (response.Throw is not null) throw response.Throw;

            foreach (var delta in response.Deltas ?? Array.Empty<ChatDelta>())
            {
                ct.ThrowIfCancellationRequested();
                yield return delta;
                await Task.Yield();
            }
        }
    }

    private sealed class FixedResolver : IChatProviderResolver
    {
        private readonly IChatProvider _provider;

        internal FixedResolver(IChatProvider provider) => _provider = provider;

        public ChatProviderResolution? Resolve(out string? problem)
        {
            problem = null;
            return new ChatProviderResolution(_provider, "test-model");
        }
    }

    private sealed class NotConfigured : IChatProviderResolver
    {
        public ChatProviderResolution? Resolve(out string? problem)
        {
            problem = "No provider is configured.";
            return null;
        }
    }

    private sealed class StubBundler : IAiContextBundler
    {
        public Task<AiContextBundle> BuildAsync(AiContextRequest request, CancellationToken ct = default)
        {
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

    private const string Passage = "Appamādo amatapadaṃ.";

    /// <summary>
    /// An orchestrator over <paramref name="provider"/>, with automatic compaction at <paramref name="percent"/>
    /// and the active model publishing <paramref name="contextLength"/> (null: a model that published none).
    /// </summary>
    private static AiChatOrchestrator Orchestrator(
        IChatProvider provider, int? contextLength, int percent = 95, IChatProviderResolver? resolver = null)
    {
        var settings = new Settings();
        settings.Ai.Chat.AutoCompactPercent = percent;
        var settingsService = new Mock<ISettingsService>();
        settingsService.SetupGet(s => s.Settings).Returns(settings);

        // The connection's model is the one FixedResolver names, so the window checked is the window of the model
        // the request goes to.
        var connections = new AiConnectionService(settingsService.Object);
        connections.Add("mine", new AiConnectionDraft(
            "Mine", ChatProviderKind.OpenAiCompatible, "https://example.test/v1",
            new[] { new AiModelEntry("test-model", "M", ContextLength: contextLength) },
            Array.Empty<AiHeader>(), new Dictionary<string, string>()));
        connections.SetActive("mine", "test-model");

        var templates = new PromptTemplateStore(
            Path.Combine(Path.GetTempPath(), "cst-compact-" + Guid.NewGuid().ToString("N")),
            NullLogger<PromptTemplateStore>.Instance);

        return new AiChatOrchestrator(
            resolver ?? new FixedResolver(provider),
            new StubBundler(),
            new PromptBuilder(templates),
            settingsService.Object,
            NullLogger<AiChatOrchestrator>.Instance,
            connections);
    }

    /// <summary><paramref name="count"/> answered turns, each with a Pāli marker in its answer, numbered from 1 so
    /// a test can say which ones reached which request.</summary>
    private static List<AiExchange> Turns(int count, int from = 1) =>
        Enumerable.Range(from, count)
            .Select(i => new AiExchange($"«Question» — Dhammapadapāḷi 21: question {i}",
                                        $"Answer {i} on [[appamāda]]."))
            .ToList();

    private static AiTurnRequest Request(IReadOnlyList<AiExchange> history, AiExchange? summary = null) =>
        new(AiTask.Explain, "s0502m.mul.xml", new NavigationReference.Paragraph(21),
            History: history, Summary: summary);

    private static async Task<List<AiTurnEvent>> CollectAsync(IAiChatOrchestrator orchestrator, AiTurnRequest request)
    {
        var events = new List<AiTurnEvent>();
        await foreach (var e in orchestrator.RunAsync(request))
            events.Add(e);
        return events;
    }

    /// <summary>The estimate of what a request would send, measured by sending it with compaction off — the same
    /// figure the orchestrator compares, derived from the request it actually built rather than restated here.</summary>
    private static async Task<int> EstimateOf(AiTurnRequest request)
    {
        var provider = new ScriptedProvider();
        await CollectAsync(Orchestrator(provider, contextLength: null, percent: 0), request);
        var sent = provider.Requests.Single();
        return AiTokens.Estimate(new[] { sent.System }.Concat(sent.Messages.Select(m => m.Content)));
    }

    /// <summary>The largest window at which <paramref name="estimate"/> still reaches 95% — one more token of window
    /// and it does not.</summary>
    private static int WindowAtThreshold(int estimate) => (int)(estimate * 100L / 95);

    private static void AssertAlternatesWithNothingEmpty(IReadOnlyList<ChatMessage> messages)
    {
        for (var i = 0; i < messages.Count; i++)
        {
            Assert.Equal(i % 2 == 0 ? ChatRole.User : ChatRole.Assistant, messages[i].Role);
            Assert.False(string.IsNullOrWhiteSpace(messages[i].Content), $"message {i} is empty");
        }
    }

    // ---- The automatic trigger ----------------------------------------------------------------------------

    /// <summary>
    /// At the threshold: the older turns are summarised first, and the turn is sent with the summary as its first
    /// replayed exchange, then the last four turns word for word, then the current message. <b>[fsnow]</b>:
    /// <i>"Last 4 turns"</i>.
    /// </summary>
    [Fact]
    public async Task At_the_threshold_the_older_turns_are_summarised_and_the_last_four_kept()
    {
        var history = Turns(7);
        var request = Request(history);
        var window = WindowAtThreshold(await EstimateOf(request));

        var provider = new ScriptedProvider(Response.Say("Summary of [[appamāda]] turns 1-3."));
        var events = await CollectAsync(Orchestrator(provider, window), request);

        // Compacted comes before Started, so the caller has recorded it before the turn's context arrives.
        // Compacting first, so the reader is told what the wait is for (review M-2).
        var kinds = events.Select(e => e.Kind).ToList();
        Assert.Equal(AiTurnEventKind.Compacting, kinds[0]);
        Assert.True(kinds.IndexOf(AiTurnEventKind.Compacted) < kinds.IndexOf(AiTurnEventKind.Started));
        Assert.Equal(AiTurnEventKind.Completed, kinds[^1]);

        var compacted = events.Single(e => e.Kind == AiTurnEventKind.Compacted).Compaction!;
        Assert.Equal(3, compacted.SummarisedExchanges);
        Assert.Equal("Summary of [[appamāda]] turns 1-3.", compacted.Summary);
        Assert.Equal(AiCompaction.SummaryAskedLine, compacted.AskedLine);

        // Two calls: the summary, then the turn.
        Assert.Equal(2, provider.Requests.Count);
        var sent = provider.Requests[1].Messages;

        Assert.Equal(AiCompaction.SummaryAskedLine, sent[0].Content);
        Assert.Equal("Summary of [[appamāda]] turns 1-3.", sent[1].Content);
        Assert.Equal(history.Skip(3).SelectMany(t => new[] { t.Question, t.Answer }),
                     sent.Skip(2).Take(8).Select(m => m.Content));
        Assert.Contains(Passage, sent[^1].Content);
        Assert.Equal(11, sent.Count);
        AssertAlternatesWithNothingEmpty(sent);

        // The Sent block shows the summary as it was sent, and the estimate names it.
        var context = events.Single(e => e.Kind == AiTurnEventKind.Started).Context!;
        Assert.Equal(sent.Take(10).Select(m => (m.Role, m.Content)),
                     context.Sent!.History!.Select(m => (m.Role, m.Content)));
        Assert.Contains("a summary and 4 earlier turns",
                        context.Sent.Fields.Single(f => f.Name == "Estimated context").Value);

        // And the reader is told it happened.
        Assert.Contains(context.Notices, n => n.Contains("3 earlier turns were summarised"));
    }

    /// <summary>
    /// The summariser is sent ONLY the turns being compacted — not the four being kept, not the passage, not the
    /// system prompt — and their answers go with their <c>[[…]]</c> markers intact, since the summary is replayed
    /// and must not teach the model to drop them.
    /// </summary>
    [Fact]
    public async Task The_summariser_is_sent_only_the_compacted_turns_with_their_markers()
    {
        var history = Turns(6);
        var request = Request(history);
        var window = WindowAtThreshold(await EstimateOf(request));

        var provider = new ScriptedProvider(Response.Say("A summary."));
        await CollectAsync(Orchestrator(provider, window), request);

        var summaryCall = provider.Requests[0];
        var prompt = Assert.Single(summaryCall.Messages).Content;
        Assert.Equal(ChatRole.User, summaryCall.Messages[0].Role);
        Assert.Null(summaryCall.System);

        Assert.Contains("question 1", prompt);
        Assert.Contains("question 2", prompt);
        Assert.Contains("Answer 1 on [[appamāda]].", prompt);
        foreach (var kept in new[] { 3, 4, 5, 6 })
            Assert.DoesNotContain($"question {kept}", prompt);

        Assert.DoesNotContain(Passage, prompt);
        // The template's marker instruction, rendered with the real markers.
        Assert.Contains($"{PaliQuoteMarkers.Open} and {PaliQuoteMarkers.Close}", prompt);
        Assert.Contains("The reader gave no further instructions.", prompt);
    }

    /// <summary>One token of window more, and the same request is below the threshold: nothing is summarised and
    /// the provider is called once.</summary>
    [Fact]
    public async Task Below_the_threshold_nothing_is_compacted()
    {
        var request = Request(Turns(7));
        var window = WindowAtThreshold(await EstimateOf(request)) + 1;

        var provider = new ScriptedProvider();
        var events = await CollectAsync(Orchestrator(provider, window), request);

        Assert.DoesNotContain(events, e => e.Kind == AiTurnEventKind.Compacted);
        Assert.Single(provider.Requests);
        Assert.Equal(15, provider.Requests[0].Messages.Count);
        Assert.Empty(events.Single(e => e.Kind == AiTurnEventKind.Started).Context!.Notices);
    }

    /// <summary>
    /// No published context length: no automatic trigger however long the conversation, and the turn carries a
    /// notice saying why — a notice, not an error; the turn runs.
    /// </summary>
    [Fact]
    public async Task With_no_context_length_there_is_no_trigger_and_a_notice_says_why()
    {
        var provider = new ScriptedProvider();
        var events = await CollectAsync(Orchestrator(provider, contextLength: null), Request(Turns(9)));

        Assert.DoesNotContain(events, e => e.Kind == AiTurnEventKind.Compacted);
        Assert.Single(provider.Requests);
        Assert.Equal(AiTurnEventKind.Completed, events[^1].Kind);

        var notices = events.Single(e => e.Kind == AiTurnEventKind.Started).Context!.Notices;
        Assert.Contains(AiChatOrchestrator.UnknownContextNotice, notices);
    }

    /// <summary>[suggestion] The notice waits until compaction could do something: with four answered turns or
    /// fewer there is nothing to summarise, and a notice under every early answer would be about nothing.</summary>
    [Fact]
    public async Task With_no_context_length_and_nothing_to_compact_there_is_no_notice()
    {
        var events = await CollectAsync(
            Orchestrator(new ScriptedProvider(), contextLength: null), Request(Turns(AiCompaction.KeepVerbatim)));

        Assert.Empty(events.Single(e => e.Kind == AiTurnEventKind.Started).Context!.Notices);
    }

    /// <summary>0 is off: no trigger at any size, and no notice about an unknown window either.</summary>
    [Fact]
    public async Task Zero_turns_automatic_compaction_off()
    {
        var provider = new ScriptedProvider();
        var events = await CollectAsync(Orchestrator(provider, contextLength: 10, percent: 0), Request(Turns(8)));

        Assert.DoesNotContain(events, e => e.Kind == AiTurnEventKind.Compacted);
        Assert.Single(provider.Requests);
        Assert.Empty(events.Single(e => e.Kind == AiTurnEventKind.Started).Context!.Notices);
    }

    /// <summary>
    /// The boundary at four, from the other side: past the threshold with exactly four answered turns there is
    /// nothing older to summarise, so the request goes as it is, and the reader is told it is near the limit.
    /// </summary>
    [Fact]
    public async Task Past_the_threshold_with_only_four_turns_nothing_is_summarised()
    {
        var provider = new ScriptedProvider();
        var events = await CollectAsync(
            Orchestrator(provider, contextLength: 10), Request(Turns(AiCompaction.KeepVerbatim)));

        Assert.DoesNotContain(events, e => e.Kind == AiTurnEventKind.Compacted);
        Assert.Single(provider.Requests);
        Assert.Contains(events.Single(e => e.Kind == AiTurnEventKind.Started).Context!.Notices,
                        n => n.Contains("nothing older than the last 4 turns"));
    }

    /// <summary>Five answered turns is the smallest conversation that compacts: one summarised, four kept.</summary>
    [Fact]
    public async Task Five_turns_compact_one()
    {
        var provider = new ScriptedProvider(Response.Say("One turn, summarised."));
        var events = await CollectAsync(Orchestrator(provider, contextLength: 10), Request(Turns(5)));

        Assert.Equal(1, events.Single(e => e.Kind == AiTurnEventKind.Compacted).Compaction!.SummarisedExchanges);
        Assert.Equal((1 + 4) * 2 + 1, provider.Requests[1].Messages.Count);
    }

    /// <summary>
    /// A second compaction summarises the first: the standing summary goes to the summariser with the turns since
    /// it, and the new summary replaces both — one summary per request, never two.
    /// </summary>
    [Fact]
    public async Task A_second_compaction_folds_the_first_summary_in()
    {
        var earlier = new AiExchange(AiCompaction.SummaryAskedLine, "The first summary, about [[appamāda]].");
        var provider = new ScriptedProvider(Response.Say("The second summary."));

        var events = await CollectAsync(
            Orchestrator(provider, contextLength: 10), Request(Turns(6, from: 10), earlier));

        var prompt = provider.Requests[0].Messages.Single().Content;
        Assert.Contains("The first summary, about [[appamāda]].", prompt);
        Assert.Contains("question 10", prompt);
        Assert.Contains("question 11", prompt);
        Assert.DoesNotContain("question 12", prompt);

        // Counts only the history entries it replaced; the earlier summary is folded in by definition.
        Assert.Equal(2, events.Single(e => e.Kind == AiTurnEventKind.Compacted).Compaction!.SummarisedExchanges);

        var sent = provider.Requests[1].Messages;
        Assert.Equal("The second summary.", sent[1].Content);
        Assert.DoesNotContain(sent, m => m.Content.Contains("The first summary"));
        AssertAlternatesWithNothingEmpty(sent);
    }

    /// <summary>A standing summary with no compaction due is simply replayed first, ahead of the turns.</summary>
    [Fact]
    public async Task A_standing_summary_is_replayed_first()
    {
        var summary = new AiExchange(AiCompaction.SummaryAskedLine, "Earlier: [[appamāda]].");
        var provider = new ScriptedProvider();

        var events = await CollectAsync(Orchestrator(provider, contextLength: null), Request(Turns(2), summary));

        var sent = provider.Requests.Single().Messages;
        Assert.Equal(AiCompaction.SummaryAskedLine, sent[0].Content);
        Assert.Equal("Earlier: [[appamāda]].", sent[1].Content);
        Assert.Equal(2 + 2 * 2 + 1, sent.Count);
        AssertAlternatesWithNothingEmpty(sent);

        Assert.Contains("a summary and 2 earlier turns",
                        events.Single(e => e.Kind == AiTurnEventKind.Started).Context!.Sent!.Fields
                            .Single(f => f.Name == "Estimated context").Value);
    }

    /// <summary>A failed summary costs the compaction, not the turn: the whole conversation is sent and the reader
    /// is told why.</summary>
    [Fact]
    public async Task A_failed_summary_sends_the_whole_conversation_with_a_notice()
    {
        var provider = new ScriptedProvider(
            new Response(Throw: new AiException(new AiError(AiErrorKind.RateLimited, "Too many requests."))));

        var events = await CollectAsync(Orchestrator(provider, contextLength: 10), Request(Turns(6)));

        Assert.DoesNotContain(events, e => e.Kind == AiTurnEventKind.Compacted);
        Assert.Equal(AiTurnEventKind.Completed, events[^1].Kind);
        Assert.Equal(6 * 2 + 1, provider.Requests[1].Messages.Count);
        Assert.Contains(events.Single(e => e.Kind == AiTurnEventKind.Started).Context!.Notices,
                        n => n.Contains("could not be summarised") && n.Contains("Too many requests."));
    }

    // ---- Compact and retry on a too-long rejection ---------------------------------------------------------

    /// <summary>
    /// [suggestion, accepted] The provider measured what the estimate guesses at. A too-long rejection before
    /// anything streamed compacts and sends again, once — here on a model with no published window, the case where
    /// it is the only signal there is. The second Started describes the request that was answered.
    /// </summary>
    [Fact]
    public async Task A_too_long_rejection_compacts_and_sends_again_once()
    {
        var tooLong = new AiException(new AiError(AiErrorKind.ContextTooLong, "Too long.", StatusCode: 400));
        var provider = new ScriptedProvider(
            new Response(Throw: tooLong),
            Response.Say("A summary."),
            Response.Say("The answer."));

        var events = await CollectAsync(Orchestrator(provider, contextLength: null), Request(Turns(6)));

        Assert.Equal(
            new[] { AiTurnEventKind.Started, AiTurnEventKind.Compacted, AiTurnEventKind.Started },
            events.Select(e => e.Kind).Where(k => k is AiTurnEventKind.Started or AiTurnEventKind.Compacted));
        Assert.Equal(AiTurnEventKind.Completed, events[^1].Kind);
        Assert.DoesNotContain(events, e => e.Kind == AiTurnEventKind.Error);

        Assert.Equal(3, provider.Requests.Count);
        Assert.Equal("A summary.", provider.Requests[2].Messages[1].Content);
        Assert.Equal((1 + 4) * 2 + 1, provider.Requests[2].Messages.Count);

        var second = events.Where(e => e.Kind == AiTurnEventKind.Started).Last().Context!;
        Assert.Equal("A summary.", second.Sent!.History![1].Content);
        Assert.Contains(second.Notices, n => n.Contains("too long"));
    }

    /// <summary>
    /// Review probe P3: the threshold summary fails, the send is rejected as too long, the retry's summary works.
    /// The final Started — the one the stored turn keeps — must describe the request that was answered: it says the
    /// summary failed AT FIRST, and never that "the whole conversation was sent". (review M-1)
    /// </summary>
    [Fact]
    public async Task A_retry_after_a_failed_threshold_summary_reports_only_what_was_finally_sent()
    {
        var tooLong = new AiException(new AiError(AiErrorKind.ContextTooLong, "Too long.", StatusCode: 400));
        var provider = new ScriptedProvider(
            new Response(Throw: new AiException(new AiError(AiErrorKind.RateLimited, "Too many requests."))),
            new Response(Throw: tooLong),
            Response.Say("A summary."),
            Response.Say("The answer."));

        var events = await CollectAsync(Orchestrator(provider, contextLength: 10), Request(Turns(6)));

        Assert.Equal(
            new[]
            {
                AiTurnEventKind.Compacting, AiTurnEventKind.Started,
                AiTurnEventKind.Compacting, AiTurnEventKind.Compacted, AiTurnEventKind.Started,
            },
            events.Select(e => e.Kind).Where(k => k is AiTurnEventKind.Started or AiTurnEventKind.Compacted
                                                  or AiTurnEventKind.Compacting));
        Assert.Equal(AiTurnEventKind.Completed, events[^1].Kind);

        // The first Started told the truth about the first attempt...
        var first = events.First(e => e.Kind == AiTurnEventKind.Started).Context!.Notices;
        Assert.Contains(first, n => n.Contains("so the whole conversation was sent"));

        // ...and the last tells the truth about the one that was answered.
        var last = events.Last(e => e.Kind == AiTurnEventKind.Started).Context!.Notices;
        Assert.DoesNotContain(last, n => n.Contains("the whole conversation was sent"));
        var line = Assert.Single(last);
        Assert.Contains("could not be summarised at first (Too many requests.)", line);
        Assert.Contains("2 earlier turns were summarised and it was sent again", line);
    }

    /// <summary>
    /// The summary call's tokens are folded into the turn's usage (the reader paid for them as part of this turn), and
    /// a rejected attempt's own counts are not carried into the retry. (review M-2, L-4)
    /// </summary>
    [Fact]
    public async Task Summary_tokens_are_added_to_the_turn_and_a_rejected_attempts_are_not()
    {
        var provider = new ScriptedProvider(
            // The rejected attempt reports usage, then the rejection.
            new Response(new[]
            {
                ChatDelta.ForUsage(new ChatUsage(1000, null)),
                ChatDelta.ForError(new AiError(AiErrorKind.ContextTooLong, "Too long.", StatusCode: 400)),
            }),
            new Response(new[] { ChatDelta.ForText("A summary."), ChatDelta.ForUsage(new ChatUsage(10, 5)) }),
            // The retry reports output only — so a stale input count from the rejected attempt would survive the
            // per-field merge, which is exactly what the reset between attempts prevents.
            new Response(new[] { ChatDelta.ForText("The answer."), ChatDelta.ForUsage(new ChatUsage(null, 20)) }));

        var events = await CollectAsync(Orchestrator(provider, contextLength: null), Request(Turns(6)));

        Assert.Equal(new AiUsageReport(10, 25), events.Single(e => e.Kind == AiTurnEventKind.Usage).Usage);
        Assert.Equal(new AiUsageReport(10, 5), events.Single(e => e.Kind == AiTurnEventKind.Compacted).Compaction!.Usage);
    }

    /// <summary>The manual summariser reports what it cost, too.</summary>
    [Fact]
    public async Task Compact_reports_its_usage()
    {
        var provider = new ScriptedProvider(
            new Response(new[] { ChatDelta.ForText("A summary."), ChatDelta.ForUsage(new ChatUsage(40, 8)) }));

        var result = await Orchestrator(provider, contextLength: null)
            .CompactAsync(new AiCompactionRequest(null, Turns(2)));

        Assert.Equal(new AiUsageReport(40, 8), result.Usage);
    }

    /// <summary>The unknown-context notice names no control: it has to stay true before the Compact button exists.
    /// (review M-4)</summary>
    [Fact]
    public void The_unknown_context_notice_names_no_control()
    {
        Assert.DoesNotContain("Compact", AiChatOrchestrator.UnknownContextNotice);
        Assert.Contains("too long", AiChatOrchestrator.UnknownContextNotice);
    }

    /// <summary>Once only: a second rejection is the turn's error, not another round.</summary>
    [Fact]
    public async Task A_second_too_long_rejection_is_reported()
    {
        var tooLong = new AiException(new AiError(AiErrorKind.ContextTooLong, "Too long.", StatusCode: 400));
        var provider = new ScriptedProvider(
            new Response(Throw: tooLong), Response.Say("A summary."), new Response(Throw: tooLong));

        var events = await CollectAsync(Orchestrator(provider, contextLength: null), Request(Turns(6)));

        Assert.Equal(AiErrorKind.ContextTooLong, events[^1].Error!.Kind);
        Assert.Equal(3, provider.Requests.Count);
    }

    /// <summary>"Off" means off: with automatic compaction at 0 a too-long rejection is the error it always was.</summary>
    [Fact]
    public async Task No_retry_when_automatic_compaction_is_off()
    {
        var tooLong = new AiException(new AiError(AiErrorKind.ContextTooLong, "Too long.", StatusCode: 400));
        var provider = new ScriptedProvider(new Response(Throw: tooLong));

        var events = await CollectAsync(
            Orchestrator(provider, contextLength: null, percent: 0), Request(Turns(6)));

        Assert.Equal(AiErrorKind.ContextTooLong, events[^1].Error!.Kind);
        Assert.Single(provider.Requests);
    }

    // ---- The manual summariser ----------------------------------------------------------------------------

    /// <summary>
    /// <see cref="IAiChatOrchestrator.CompactAsync"/> — Claude Code's <c>/compact [instructions]</c>: the reader's
    /// instructions reach the prompt, the summary comes back whole with its markers, and it is attributed.
    /// </summary>
    [Fact]
    public async Task Compact_summarises_what_it_is_given_and_carries_the_instructions()
    {
        var provider = new ScriptedProvider(new Response(new[]
        {
            ChatDelta.ForReasoning("Thinking…"),
            ChatDelta.ForText("Turns 1 and 2 discussed "),
            ChatDelta.ForText("[[appamāda]]."),
        }));

        var result = await Orchestrator(provider, contextLength: null).CompactAsync(
            new AiCompactionRequest(null, Turns(2), "Keep the grammar points."));

        Assert.True(result.Succeeded);
        Assert.Equal("Turns 1 and 2 discussed [[appamāda]].", result.Summary);
        Assert.Equal(AiCompaction.SummaryAskedLine, result.AskedLine);
        Assert.Equal("fake", result.ProviderId);
        Assert.Equal("test-model", result.ModelId);

        var prompt = provider.Requests.Single().Messages.Single().Content;
        Assert.Contains("Keep the grammar points.", prompt);
        Assert.Contains("Answer 2 on [[appamāda]].", prompt);
    }

    /// <summary>Failures are results, never exceptions: not configured, a provider error, an empty summary, and a
    /// summary cut off at the output limit — which is refused rather than used, since half a summary silently drops
    /// the turns it did not reach.</summary>
    [Fact]
    public async Task Compact_failures_are_results_not_exceptions()
    {
        var notConfigured = await Orchestrator(new ScriptedProvider(), null, resolver: new NotConfigured())
            .CompactAsync(new AiCompactionRequest(null, Turns(2)));
        Assert.False(notConfigured.Succeeded);
        Assert.Equal(AiErrorKind.NotConfigured, notConfigured.Error!.Kind);

        var failing = await Orchestrator(new ScriptedProvider(
                new Response(Throw: new AiException(new AiError(AiErrorKind.Network, "Unreachable.")))), null)
            .CompactAsync(new AiCompactionRequest(null, Turns(2)));
        Assert.Equal(AiErrorKind.Network, failing.Error!.Kind);

        var empty = await Orchestrator(new ScriptedProvider(Response.Say("   ")), null)
            .CompactAsync(new AiCompactionRequest(null, Turns(2)));
        Assert.Equal(AiErrorKind.EmptyAnswer, empty.Error!.Kind);

        var truncated = await Orchestrator(new ScriptedProvider(new Response(new[]
            {
                ChatDelta.ForText("Half a summ"),
                ChatDelta.ForError(new AiError(AiErrorKind.Truncated, "Stopped.")),
            })), null)
            .CompactAsync(new AiCompactionRequest(null, Turns(2)));
        Assert.False(truncated.Succeeded);
        Assert.Null(truncated.Summary);
        Assert.Contains("cut off", truncated.Error!.Message);

        var nothing = await Orchestrator(new ScriptedProvider(), null)
            .CompactAsync(new AiCompactionRequest(null, Array.Empty<AiExchange>()));
        Assert.False(nothing.Succeeded);
    }

    [Fact]
    public void The_threshold_is_reached_at_the_percentage_and_not_below()
    {
        Assert.True(AiCompaction.Reaches(95, 100, 95));
        Assert.False(AiCompaction.Reaches(94, 100, 95));
        Assert.False(AiCompaction.Reaches(1_000_000, 100, 0));
        // Wide enough that a large window cannot overflow the comparison.
        Assert.True(AiCompaction.Reaches(int.MaxValue, int.MaxValue, 100));
    }
}
