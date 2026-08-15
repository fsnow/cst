using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Services.Ai;
using CST.Avalonia.ViewModels;
using CST.Navigation;
using CST.Search;
using Xunit;

namespace CST.Avalonia.Tests.ViewModels;

/// <summary>
/// The assistant panel's own logic (#586) — what it does with a stream, and what it says when there is
/// nothing to stream.
///
/// <para>
/// Every failure here is an ORDINARY state of a feature that ships off: not configured, no book open, a
/// reading position still settling, a dead network. AI_SURFACE_B.md §10 requires each to arrive as a sentence
/// a reader can act on, never as an exception — so these tests are mostly about wording being present and
/// specific, which is the part that rots silently.
/// </para>
/// </summary>
public class AiAssistantViewModelTests
{
    private sealed class StubOrchestrator : IAiChatOrchestrator
    {
        internal List<AiTurnEvent> Events { get; } = new();
        internal int StopCalls { get; private set; }
        internal AiTurnRequest? LastRequest { get; private set; }

        public async IAsyncEnumerable<AiTurnEvent> RunAsync(
            AiTurnRequest request, [EnumeratorCancellation] CancellationToken ct = default)
        {
            LastRequest = request;
            foreach (var e in Events)
            {
                ct.ThrowIfCancellationRequested();
                yield return e;
                await Task.Yield();
            }
        }

        public void Stop() => StopCalls++;
    }

    private sealed class StubReaderState : IReaderStateService
    {
        internal ReaderStateResult Result { get; set; } =
            ReaderStateResult.Ok(new ReaderState("s0101m.mul.xml", 12, null));

        public Task<ReaderStateResult> GetCurrentAsync(CancellationToken ct = default) => Task.FromResult(Result);
    }

    private sealed class StubResolver : IChatProviderResolver
    {
        internal string? Problem { get; set; }

        public ChatProviderResolution? Resolve(out string? problem)
        {
            problem = Problem;
            return Problem == null ? null : null;   // the panel only consults the problem
        }
    }

    private static CitationRef Citation() =>
        new("s0101m.mul.xml", "Sīlakkhandhavaggapāḷi", "para 12", Array.Empty<SnippetPageRef>());

    private static AiTurnContext Context(params string[] notices) =>
        new(AiTask.Explain, "English", Citation(),
            new BookContext("s0101m.mul.xml", "Sīlakkhandhavaggapāḷi", CST.Pitaka.Sutta, CST.CommentaryLevel.Mula),
            notices);

    // ---- Not configured, and other ordinary refusals ---------------------------------------------

    [Fact]
    public async Task An_unconfigured_assistant_says_what_to_set_and_never_calls_the_model()
    {
        var orchestrator = new StubOrchestrator();
        var vm = new AiAssistantViewModel(
            orchestrator, new StubReaderState(), new StubResolver { Problem = "No model is configured." }, null);

        await vm.AskAsync(AiTask.Explain);

        // Checked before the reader is even consulted: telling someone what to set beats making them wait
        // while the app assembles a bundle it cannot send.
        Assert.Contains("No model is configured", vm.Status);
        Assert.Contains("Settings", vm.Status);
        Assert.Null(orchestrator.LastRequest);
    }

    [Theory]
    [InlineData(ReaderStateProblem.NoBookOpen)]
    [InlineData(ReaderStateProblem.PositionUnknown)]
    [InlineData(ReaderStateProblem.AmbiguousInMultiBook)]
    [InlineData(ReaderStateProblem.AmbiguousBookWindow)]
    public void Every_reader_refusal_has_its_own_sentence(ReaderStateProblem problem)
    {
        var text = AiAssistantViewModel.Describe(problem);

        // Distinct wording per cause, because the user's next action differs: open a book, wait a moment,
        // click into the window you mean. A shared "could not determine the passage" would be true and
        // useless.
        Assert.False(string.IsNullOrWhiteSpace(text));
        var others = Enum.GetValues<ReaderStateProblem>()
            .Where(p => p != problem)
            .Select(p => AiAssistantViewModel.Describe(p));
        Assert.DoesNotContain(text, others);
    }

    [Fact]
    public async Task A_refusal_from_the_reader_is_shown_rather_than_a_turn_being_attempted()
    {
        var orchestrator = new StubOrchestrator();
        var reader = new StubReaderState { Result = ReaderStateResult.Fail(ReaderStateProblem.NoBookOpen) };
        var vm = new AiAssistantViewModel(orchestrator, reader, null, null);

        await vm.AskAsync(AiTask.Explain);

        Assert.Contains("Open a book", vm.Status);
        Assert.Null(orchestrator.LastRequest);
    }

    // ---- The request it builds --------------------------------------------------------------------

    [Fact]
    public async Task The_request_carries_the_reader_state_including_an_unreadable_selection()
    {
        var orchestrator = new StubOrchestrator();
        var reader = new StubReaderState
        {
            // A selection the reader could not read is NOT the same as no selection: conflating them is what
            // makes a dropped selection look to the user like the assistant ignored it. (#581)
            Result = ReaderStateResult.Ok(new ReaderState("s0102m.mul.xml", 7, null, SelectionUnavailable: true)),
        };
        var vm = new AiAssistantViewModel(orchestrator, reader, null, null) { Question = "  why anicca?  " };

        await vm.AskAsync(AiTask.Translate);

        var request = Assert.IsType<AiTurnRequest>(orchestrator.LastRequest);
        Assert.Equal(AiTask.Translate, request.Task);
        Assert.Equal("s0102m.mul.xml", request.BookId);
        Assert.True(request.SelectionUnavailable);
        Assert.Equal("why anicca?", request.UserQuestion);
    }

    [Fact]
    public async Task An_empty_question_is_sent_as_none_rather_than_as_blank_text()
    {
        var orchestrator = new StubOrchestrator();
        var vm = new AiAssistantViewModel(orchestrator, new StubReaderState(), null, null) { Question = "   " };

        await vm.AskAsync(AiTask.Explain);

        // The presets work with no question; whitespace would become an empty user turn in the prompt.
        Assert.Null(orchestrator.LastRequest!.UserQuestion);
    }

    // ---- The stream ------------------------------------------------------------------------------

    [Fact]
    public async Task Text_deltas_are_accumulated_in_order()
    {
        var orchestrator = new StubOrchestrator();
        orchestrator.Events.Add(AiTurnEvent.ForStarted(Context()));
        orchestrator.Events.Add(AiTurnEvent.ForText("Not "));
        orchestrator.Events.Add(AiTurnEvent.ForText("negligence "));
        orchestrator.Events.Add(AiTurnEvent.ForText("is the path"));
        orchestrator.Events.Add(AiTurnEvent.ForCompleted(new PaliMarkerReport(0, 0)));

        var vm = new AiAssistantViewModel(orchestrator, new StubReaderState(), null, null);
        await vm.AskAsync(AiTask.Translate);

        Assert.Equal("Not negligence is the path", vm.Answer);
        Assert.True(vm.HasAnswer);
    }

    [Fact]
    public async Task Reasoning_never_reaches_the_answer()
    {
        var orchestrator = new StubOrchestrator();
        orchestrator.Events.Add(AiTurnEvent.ForStarted(Context()));
        orchestrator.Events.Add(AiTurnEvent.ForReasoning("Let me think about matā..."));
        orchestrator.Events.Add(AiTurnEvent.ForText("The verse says"));
        orchestrator.Events.Add(AiTurnEvent.ForCompleted(new PaliMarkerReport(0, 0)));

        var vm = new AiAssistantViewModel(orchestrator, new StubReaderState(), null, null);
        await vm.AskAsync(AiTask.Explain);

        // It is the model thinking aloud, not what it is telling the reader. Concatenating it would put
        // half-formed guesses about a sacred text on screen as if they were the answer.
        Assert.Equal("The verse says", vm.Answer);
        Assert.DoesNotContain("think about", vm.Answer);
    }

    [Fact]
    public async Task A_mid_stream_error_keeps_the_partial_answer()
    {
        var orchestrator = new StubOrchestrator();
        orchestrator.Events.Add(AiTurnEvent.ForStarted(Context()));
        orchestrator.Events.Add(AiTurnEvent.ForText("The first half"));
        orchestrator.Events.Add(AiTurnEvent.ForError(new AiError(AiErrorKind.Network, "The connection dropped.")));

        var vm = new AiAssistantViewModel(orchestrator, new StubReaderState(), null, null);
        await vm.AskAsync(AiTask.Explain);

        // Discarding what arrived would throw away the part the user can still read and check.
        Assert.Equal("The first half", vm.Answer);
        Assert.Equal("The connection dropped.", vm.Status);
    }

    [Fact]
    public async Task The_citation_comes_from_the_bundle_not_from_the_answer()
    {
        var orchestrator = new StubOrchestrator();
        orchestrator.Events.Add(AiTurnEvent.ForStarted(Context()));
        // An answer that claims a different source entirely.
        orchestrator.Events.Add(AiTurnEvent.ForText("As stated in the Dhammapada, verse 183…"));
        orchestrator.Events.Add(AiTurnEvent.ForCompleted(new PaliMarkerReport(0, 0)));

        var vm = new AiAssistantViewModel(orchestrator, new StubReaderState(), null, null);
        await vm.AskAsync(AiTask.Explain);

        // The chrome names what was actually sent. This is what makes it impossible for a garbled or
        // confabulated answer to produce a citation that looks authoritative.
        Assert.Contains("Sīlakkhandhavaggapāḷi", vm.Citation);
        Assert.Contains("para 12", vm.Citation);
        Assert.DoesNotContain("Dhammapada", vm.Citation);
    }

    [Fact]
    public async Task Notices_are_surfaced_and_a_trimmed_passage_raises_the_badge()
    {
        var orchestrator = new StubOrchestrator();
        orchestrator.Events.Add(AiTurnEvent.ForStarted(Context("The passage was trimmed to fit the budget.")));
        orchestrator.Events.Add(AiTurnEvent.ForCompleted(new PaliMarkerReport(0, 0)));

        var vm = new AiAssistantViewModel(orchestrator, new StubReaderState(), null, null);
        await vm.AskAsync(AiTask.Explain);

        // The badge is separate from the notice list because it changes how far the answer can be trusted:
        // the model did not see all of the passage it is being asked about.
        Assert.Single(vm.Notices);
        Assert.True(vm.IsPartialPassage);
    }

    [Fact]
    public async Task A_clean_turn_raises_no_partial_badge()
    {
        var orchestrator = new StubOrchestrator();
        orchestrator.Events.Add(AiTurnEvent.ForStarted(Context()));
        orchestrator.Events.Add(AiTurnEvent.ForCompleted(new PaliMarkerReport(0, 0)));

        var vm = new AiAssistantViewModel(orchestrator, new StubReaderState(), null, null);
        await vm.AskAsync(AiTask.Explain);

        Assert.Empty(vm.Notices);
        Assert.False(vm.IsPartialPassage);
        Assert.Equal("", vm.Status);
    }

    [Fact]
    public async Task The_panel_is_not_busy_once_a_turn_ends()
    {
        var orchestrator = new StubOrchestrator();
        orchestrator.Events.Add(AiTurnEvent.ForStarted(Context()));
        orchestrator.Events.Add(AiTurnEvent.ForCompleted(new PaliMarkerReport(0, 0)));

        var vm = new AiAssistantViewModel(orchestrator, new StubReaderState(), null, null);
        await vm.AskAsync(AiTask.Explain);

        // The stop control is bound to IsBusy and the preset buttons to its inverse: a stuck flag would
        // leave the panel permanently unusable with no error to explain it.
        Assert.False(vm.IsBusy);
        Assert.True(vm.CanAsk);
    }

    [Fact]
    public void The_citation_headline_is_one_short_line()
    {
        // What the first build put on screen, from a real turn: the whole canon path plus eight page
        // references — four editions × two pages — in bold, over four lines, above the answer it was
        // supposed to caption. The headline is now the book and the reference; the rest is on the tooltip.
        var citation = new CitationRef(
            "s0102m.mul.xml", "tipiṭaka (mūla)/sutta piṭaka/dīgha nikāya/mahāvaggapāḷi", "paragraphs 1-4 (dn2)",
            new[]
            {
                new SnippetPageRef(PageEdition.Vri, 2, 1), new SnippetPageRef(PageEdition.Vri, 2, 2),
                new SnippetPageRef(PageEdition.Myanmar, 2, 1), new SnippetPageRef(PageEdition.Myanmar, 2, 2),
                new SnippetPageRef(PageEdition.Pts, 2, 1), new SnippetPageRef(PageEdition.Pts, 2, 2),
                new SnippetPageRef(PageEdition.Thai, 2, 1), new SnippetPageRef(PageEdition.Thai, 2, 2),
            });

        var headline = AiAssistantViewModel.Describe(citation);

        Assert.DoesNotContain("VRI", headline);
        Assert.DoesNotContain("/", headline);
        Assert.Contains("paragraphs 1-4", headline);
        Assert.True(headline.Length < 60, $"headline too long: {headline}");
    }

    [Fact]
    public void The_full_reference_is_kept_for_the_tooltip()
    {
        var citation = new CitationRef(
            "s0102m.mul.xml", "tipiṭaka (mūla)/sutta piṭaka/dīgha nikāya/mahāvaggapāḷi", "paragraphs 1-4",
            new[] { new SnippetPageRef(PageEdition.Vri, 2, 1), new SnippetPageRef(PageEdition.Vri, 2, 2) });

        var detail = AiAssistantViewModel.DescribeCitationDetail(citation);

        // Nothing is lost by shortening the headline — a reader checking a claim against print still has
        // the canon path and the pages.
        Assert.Contains("dīgha nikāya", detail);
        Assert.Contains("VRI", detail);
    }

    [Fact]
    public void Pages_are_grouped_by_edition_and_consecutive_runs_collapse()
    {
        var lines = AiAssistantViewModel.DescribePagesByEdition(new[]
        {
            new SnippetPageRef(PageEdition.Vri, 2, 1),
            new SnippetPageRef(PageEdition.Vri, 2, 2),
            new SnippetPageRef(PageEdition.Myanmar, 2, 5),
        });

        // Eight references for a two-page window across four editions is the same fact stated eight times.
        Assert.Equal(2, lines.Count);
        Assert.Contains(lines, l => l.Contains("VRI") && l.Contains("1\u20132"));
        Assert.Contains(lines, l => l.Contains("Myanmar") && l.Contains("p. 5"));
    }

    [Fact]
    public void A_broken_run_of_pages_is_listed_rather_than_ranged()
    {
        var lines = AiAssistantViewModel.DescribePagesByEdition(new[]
        {
            new SnippetPageRef(PageEdition.Vri, 1, 1),
            new SnippetPageRef(PageEdition.Vri, 1, 5),
        });

        // "pp. 1–5" would be a claim about three pages nobody looked at.
        Assert.Contains("1, 5", Assert.Single(lines));
    }

    [Fact]
    public void The_citation_renders_printed_pages_readably_and_not_as_a_record()
    {
        // SnippetPageRef is a record: ToString() would have put "SnippetPageRef { Edition = Vri, Volume = 1,
        // Number = 6 }" on screen — and since #561 a passage window can cover SEVERAL pages, so it would
        // have been a line of them. Formatted by the prompt builder's own helper so the pages the reader
        // sees and the pages the model was told are the same string.
        var citation = new CitationRef(
            "s0101m.mul.xml", "Sīlakkhandhavaggapāḷi", "para 12",
            new[]
            {
                new SnippetPageRef(PageEdition.Vri, 1, 6),
                new SnippetPageRef(PageEdition.Vri, 1, 7),
            });

        var text = AiAssistantViewModel.DescribeCitationDetail(citation);

        Assert.DoesNotContain("SnippetPageRef", text);
        Assert.DoesNotContain("Edition =", text);
        Assert.Contains("VRI", text);
        Assert.Contains("6", text);
        Assert.Contains("7", text);
    }

    [Fact]
    public void A_citation_with_no_pages_says_nothing_about_pages()
    {
        var text = AiAssistantViewModel.Describe(Citation());

        Assert.Contains("Sīlakkhandhavaggapāḷi", text);
        Assert.Empty(AiAssistantViewModel.DescribePagesByEdition(Array.Empty<SnippetPageRef>()));
    }

    // ---- Waiting ---------------------------------------------------------------------------------

    [Fact]
    public void A_long_wait_says_so_and_names_the_likely_reason()
    {
        // Measured against a real endpoint: a 550B model on a ":free" tier sat for two minutes and returned
        // a gateway timeout, while another request to the same model answered in thirty seconds. Our HTTP
        // timeout is deliberately infinite, so the panel cannot shorten the wait — it can only stop looking
        // like nothing is happening.
        Assert.Equal("Thinking…", AiAssistantViewModel.WaitingMessage(TimeSpan.FromSeconds(2)));
        Assert.Contains("10s", AiAssistantViewModel.WaitingMessage(TimeSpan.FromSeconds(10)));

        var long_ = AiAssistantViewModel.WaitingMessage(TimeSpan.FromSeconds(75));
        Assert.Contains("Still waiting", long_);
        Assert.Contains("queue", long_);
    }

    [Fact]
    public async Task The_progress_counter_never_overwrites_an_error()
    {
        var orchestrator = new StubOrchestrator();
        orchestrator.Events.Add(AiTurnEvent.ForStarted(Context()));
        orchestrator.Events.Add(AiTurnEvent.ForError(
            new AiError(AiErrorKind.Provider, "The provider rejected the request (HTTP 504).", 504)));

        var vm = new AiAssistantViewModel(orchestrator, new StubReaderState(), null, null);
        await vm.AskAsync(AiTask.Explain);

        // The failure that prompted this feature took two minutes to arrive; a counter that kept ticking
        // over the top of the explanation would hide the one thing the user needs to read.
        Assert.Equal("The provider rejected the request (HTTP 504).", vm.Status);
    }

    [Fact]
    public async Task Streaming_text_clears_the_waiting_message()
    {
        var orchestrator = new StubOrchestrator();
        orchestrator.Events.Add(AiTurnEvent.ForStarted(Context()));
        orchestrator.Events.Add(AiTurnEvent.ForText("Bhikkhus,"));
        orchestrator.Events.Add(AiTurnEvent.ForCompleted(new PaliMarkerReport(0, 0)));

        var vm = new AiAssistantViewModel(orchestrator, new StubReaderState(), null, null);
        await vm.AskAsync(AiTask.Explain);

        // Once text is arriving, the text IS the progress report.
        Assert.Equal("", vm.Status);
        Assert.Equal("Bhikkhus,", vm.Answer);
    }

    // ---- Usage -----------------------------------------------------------------------------------

    [Fact]
    public void Usage_reports_whichever_halves_the_provider_gave()
    {
        // Providers differ in what they report, and a missing half must not print as zero — "0 tokens out"
        // is a claim, and the wrong one.
        Assert.Equal("", AiAssistantViewModel.FormatUsage(new AiUsageReport(null, null)));
        Assert.Contains("in", AiAssistantViewModel.FormatUsage(new AiUsageReport(1200, null)));
        Assert.Contains("out", AiAssistantViewModel.FormatUsage(new AiUsageReport(null, 340)));
        var both = AiAssistantViewModel.FormatUsage(new AiUsageReport(1200, 340));
        Assert.Contains("1,200", both);
        Assert.Contains("340", both);
    }
}
