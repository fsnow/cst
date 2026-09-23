using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using CST.Navigation;
using CST.Search;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services.Ai;

/// <summary>Runs one turn of the in-app assistant. See <see cref="AiChatOrchestrator"/>.</summary>
public interface IAiChatOrchestrator
{
    /// <summary>
    /// Run a turn, streaming events as they happen.
    ///
    /// <para><b>Nothing expected is thrown.</b> Not-configured, an unreadable passage, a dead network, a 401 —
    /// all arrive as a terminal <see cref="AiTurnEventKind.Error"/> event. A panel has to render every one of
    /// those as a sentence rather than an exception (AI_SURFACE_B.md §10), and collapsing the provider layer's
    /// two failure shapes into one here is what spares every future caller from re-deriving that.</para>
    ///
    /// <para><b>Starting a turn cancels the one in flight</b> — cancel-and-replace, not queue. The replacement
    /// happens when the new turn is first enumerated, not when this method returns, because an async iterator
    /// runs no code until then.</para>
    /// </summary>
    IAsyncEnumerable<AiTurnEvent> RunAsync(AiTurnRequest request, CancellationToken ct = default);

    /// <summary>Stop the turn in flight, if any. What the panel's stop control calls.</summary>
    void Stop();

    /// <summary>
    /// Summarise older turns, for the manual Compact action — Claude Code's <c>/compact [instructions]</c>. (#998)
    ///
    /// <para>Uses the active provider and model, as a turn does. <b>Nothing expected is thrown</b>: not configured, a
    /// failed call, an empty or truncated summary all come back as <see cref="AiCompactionResult.Error"/>.
    /// Cancelling <paramref name="ct"/> throws, the ordinary .NET way. Independent of the turn in flight — the
    /// caller does not compact while a turn runs.</para>
    ///
    /// <para>A default body so a test double that has no use for compaction need not implement it.</para>
    /// </summary>
    Task<AiCompactionResult> CompactAsync(AiCompactionRequest request, CancellationToken ct = default) =>
        Task.FromResult(AiCompactionResult.Failed(
            new AiError(AiErrorKind.NotConfigured, "Summarising is not available in this build.")));
}

/// <summary>
/// Bundle → prompt → provider → events. The layer that makes surface B a feature rather than four libraries.
/// (#583, AI_SURFACE_B.md §5, §10)
///
/// <para><b>Cancel-and-replace, not queue.</b> Asking a second question supersedes the first: a reader who
/// re-asks wants the new answer, and a queue would make them watch an answer they have already abandoned. A
/// superseded turn ends QUIETLY — its partial text stands and no error is reported, because being replaced is
/// not a failure. The caller's own token behaves the ordinary .NET way and throws, so a consumer that cancels
/// its own enumeration is not silently told the turn succeeded.</para>
///
/// <para><b>An empty answer is reported as a failure.</b> A model can end a turn having produced only
/// reasoning — the whole output budget spent thinking, nothing written down (#601). The provider layer is
/// right to segregate reasoning from answer, and that correctness is exactly what makes this failure invisible:
/// the caller gets a well-formed, successful, blank turn. So a turn that emitted no text is turned into a
/// named error here, where it is still possible to say something useful about it.</para>
///
/// <para><b>A turn is part of a conversation, not a request on its own.</b> The caller hands over the earlier
/// turns (<see cref="AiTurnRequest.History"/>) and they are replayed as <c>user</c>/<c>assistant</c> pairs
/// ahead of this turn's message, which is what makes a follow-up question mean anything — until #991 every turn
/// was a single user message and the model had never seen the one before it. What is replayed is each turn's
/// citation line, question and answer; the passage, selection and lemma blocks are sent for the CURRENT turn
/// only, so a ten-turn conversation about one paragraph sends that paragraph once rather than ten times. The
/// answer that goes back is the model's own marked text, not the stripped text on screen — see
/// <see cref="AiTurnEvent.MarkedText"/>. Reasoning is never replayed.</para>
///
/// <para><b>The replayed half is byte-stable; the system prompt is not.</b> [observed 2026-09-12] Nothing here
/// puts the time, the turn count, or anything else that moves into a replayed message — what goes back is the
/// strings the earlier turns were built from, never anything re-rendered now. The system prompt does move:
/// <c>Resources/Ai/system.md</c> embeds <c>{{scope}}</c> and <c>{{outputLanguage}}</c>, and
/// <c>PromptBuilder.Scope</c> renders the book name, the reference, how many paragraphs the window covers and a
/// sentence that depends on whether anything is selected — so it holds only while the reader stays on the same
/// reference with the same selection state and answer language, and changes as soon as any of those does.
/// Neither adapter sets Anthropic's <c>cache_control</c>, and nothing else here asks for caching, so no
/// behaviour depends on a stable prefix today. One that could be relied on would mean moving the scope
/// statement out of the system prompt and into the per-turn message, which is not this layer's to decide.
/// Keeping the replayed half stable is worth doing regardless, because it is the half that grows.</para>
///
/// <para><b>Compaction happens here when it has to happen mid-turn.</b> (#998) Only this layer knows the size of
/// the whole request — the system prompt, the passage and the replayed conversation together — so the automatic
/// trigger lives here: when the estimate reaches <c>ChatSettings.AutoCompactPercent</c> of the model's published
/// context length, the older turns are summarised first (<see cref="AiTurnEventKind.Compacted"/>) and the turn is
/// sent with the summary in their place. The caller owns the record; this only makes the summary and says which
/// history entries it replaced.</para>
///
/// <para><b>What is never logged above Debug.</b> The prompt contains corpus text and the user's own question,
/// and the answer contains both back again. Above Debug this logs only shapes and counts. (§10)</para>
/// </summary>
public sealed class AiChatOrchestrator : IAiChatOrchestrator
{
    private readonly IChatProviderResolver _resolver;
    private readonly IAiContextBundler _bundler;
    private readonly IPromptBuilder _prompts;
    private readonly ISettingsService _settings;
    private readonly ILogger<AiChatOrchestrator> _logger;

    private readonly object _gate = new();
    private CancellationTokenSource? _current;

    public AiChatOrchestrator(
        IChatProviderResolver resolver,
        IAiContextBundler bundler,
        IPromptBuilder prompts,
        ISettingsService settings,
        ILogger<AiChatOrchestrator> logger,
        IAiConnectionService? connections = null)
    {
        _resolver = resolver;
        _bundler = bundler;
        _prompts = prompts;
        _settings = settings;
        _logger = logger;
        _connections = connections;
    }

    /// <summary>
    /// Optional so the orchestrator stays constructible in tests that care about nothing else. Its only job
    /// here is the reachability write-back (#673): a turn is the app's best evidence about whether an endpoint
    /// answers, and without this the knowledge dies in the panel while Settings goes on saying "Connected".
    /// </summary>
    private readonly IAiConnectionService? _connections;

    /// <summary>
    /// The reasoning effort to send, or null to send none. (#671)
    ///
    /// <para><b>Read at resolution time, not at send time.</b> Bundling the context is asynchronous and can
    /// take seconds, and the active connection is mutable throughout — the chip is right there in the
    /// composer. Reading it afterwards meant validating against whatever connection was active by then while
    /// sending on the provider resolved at the start, so a reader who switched connections mid-turn could
    /// have an effort validated against one model and sent to another. Two ordinary reads of mutable state
    /// seconds apart is all it takes. (fable review)</para>
    ///
    /// <para><b>Validated here rather than trusted from the setting</b>, because this is the last point before
    /// the wire and the only one that knows which model the request is actually going to. A reader who chooses
    /// "high" on a model that offers it and then switches to one that does not would otherwise send a field
    /// that model never published — and an unsupported parameter can be a 400 rather than an ignored key. The
    /// picker not offering it is presentation; this is the part that has to be right.</para>
    ///
    /// <para>Matched against what the provider published for THIS model, ordinally: the vocabularies differ
    /// between providers and a value is only meaningful in the list it came from.</para>
    /// </summary>
    private string? ReasoningEffortFor(string model)
    {
        var chosen = _settings.Settings.Ai.Chat.ReasoningEffort;
        if (string.IsNullOrWhiteSpace(chosen)) return null;
        if (_connections?.Active is not { } connection) return null;

        var entry = connection.Models.FirstOrDefault(
            m => string.Equals(m.Id, model, StringComparison.Ordinal));

        return entry?.ReasoningEfforts?.Any(v => string.Equals(v, chosen, StringComparison.Ordinal)) == true
            ? chosen
            : null;
    }

    /// <summary>
    /// The context window the provider published for this model, or null where it published none — a hand-typed
    /// id, an endpoint with no listing. (#998)
    ///
    /// <para>Read from the active connection's stored model list, at resolution time, for the reason the effort is
    /// (see <see cref="ReasoningEffortFor"/>). Never guessed: a window this app assumed would be a capability table
    /// by another name (#670), and null is what turns the automatic trigger off and puts a notice on the turn.</para>
    /// </summary>
    private int? ContextLengthFor(string model)
    {
        if (_connections?.Active is not { } connection) return null;

        var entry = connection.Models.FirstOrDefault(
            m => string.Equals(m.Id, model, StringComparison.Ordinal));

        return entry?.ContextLength is > 0 ? entry.ContextLength : null;
    }

    /// <summary>The automatic-compaction threshold as a percentage, 0 for off. Clamped rather than trusted: the
    /// validator repairs the file on load, and a value set in memory since has not been through it.</summary>
    private int AutoCompactPercent() => Math.Clamp(_settings.Settings.Ai.Chat.AutoCompactPercent, 0, 100);

    public void Stop()
    {
        CancellationTokenSource? running;
        lock (_gate)
        {
            running = _current;
            _current = null;
        }

        if (running is null) return;
        _logger.LogDebug("Stopping the AI turn in flight");
        TryCancel(running);
    }

    public async IAsyncEnumerable<AiTurnEvent> RunAsync(
        AiTurnRequest request, [EnumeratorCancellation] CancellationToken ct = default)
    {
        // Supersede whatever is running. Each turn disposes only the source it owns (in the finally below), so
        // this never disposes a source another iterator is still linked to.
        var mine = new CancellationTokenSource();
        CancellationTokenSource? superseded;
        lock (_gate)
        {
            superseded = _current;
            _current = mine;
        }

        if (superseded is not null)
        {
            _logger.LogDebug("Superseding the AI turn in flight");
            TryCancel(superseded);
        }

        using var linked = CancellationTokenSource.CreateLinkedTokenSource(ct, mine.Token);
        var token = linked.Token;

        try
        {
            await foreach (var turnEvent in RunCoreAsync(request, ct, token).ConfigureAwait(false))
                yield return turnEvent;
        }
        finally
        {
            lock (_gate)
            {
                if (ReferenceEquals(_current, mine)) _current = null;
            }
            mine.Dispose();
        }
    }

    /// <param name="callerToken">The consumer's own token. Cancelling it must throw, as any .NET async method
    /// would; cancelling only ours means superseded or stopped, which ends the turn quietly.</param>
    /// <param name="token">The linked token actually passed downstream. Carries
    /// [EnumeratorCancellation] because it is the one with teeth: a token handed to
    /// GetAsyncEnumerator has to reach the provider call to stop anything, and callerToken never
    /// leaves this method — it only classifies a cancellation once one happens.</param>
    private async IAsyncEnumerable<AiTurnEvent> RunCoreAsync(
        AiTurnRequest request,
        CancellationToken callerToken,
        [EnumeratorCancellation] CancellationToken token)
    {
        var provider = _resolver.Resolve(out var problem);
        if (provider is null)
        {
            // Never a raw error: the panel shows this sentence and points at Settings. (§10)
            yield return AiTurnEvent.ForError(new AiError(
                AiErrorKind.NotConfigured, problem ?? "The assistant is not configured yet."));
            yield break;
        }

        // Read here, beside the resolution it is validated against, rather than at send time - see
        // ReasoningEffortFor. Bundling below is asynchronous, and the chip that changes this sits in the
        // composer the reader is looking at. (#671)
        var effort = ReasoningEffortFor(provider.Model);

        // Beside the effort, for the same reason: the window checked against has to be the window of the model
        // the request goes to. (#998)
        var contextLength = ContextLengthFor(provider.Model);
        var autoPercent = AutoCompactPercent();

        var language = _settings.Settings.Ai.Chat.AnswerLanguage;
        if (string.IsNullOrWhiteSpace(language)) language = "English";

        // ---- Assemble. Everything here happens before a byte leaves the machine, so a failure is clean.
        AiContextBundle? bundle = null;
        RenderedPrompt? prompt = null;
        AiError? assemblyFailure = null;
        var superseded = false;

        // `yield return` is illegal inside a catch clause, so every failure is captured and reported below.
        try
        {
            bundle = await _bundler.BuildAsync(
                new AiContextRequest(
                    request.Task, request.BookId, language, request.Reference,
                    request.SelectionText, request.UserQuestion, request.SelectionUnavailable),
                token).ConfigureAwait(false);

            prompt = _prompts.Build(bundle);
        }
        catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
        {
            superseded = true;
        }
        catch (AiContextException ex)
        {
            // Reachable on ordinary data, not only on bugs: a ranged paragraph anchor is not in the marker
            // index, and a catalogued book whose XML never downloaded behaves the same way.
            _logger.LogInformation("Could not assemble context for {Task} on {BookId}: {Reason}",
                request.Task, request.BookId, ex.Message);
            assemblyFailure = new AiError(AiErrorKind.ContextUnavailable, ex.Message);
        }
        catch (PromptTemplateException ex)
        {
            // Only reachable if a built-in template resource is missing from the build — a bad package, not a
            // configuration mistake, so it says so rather than sending the user to Settings.
            _logger.LogError(ex, "The prompt templates could not be loaded");
            assemblyFailure = new AiError(
                AiErrorKind.Provider, "The assistant's prompts could not be loaded. This build may be damaged.");
        }

        if (superseded) yield break;
        if (assemblyFailure is not null)
        {
            yield return AiTurnEvent.ForError(assemblyFailure);
            yield break;
        }

        // Unreachable: the two are assigned together, and every failure above sets `superseded` or
        // `assemblyFailure`. Stated anyway, because the compiler cannot relate the three locals (CS8602)
        // and because the alternative — a null-forgiving `!` — would turn a future break in that invariant
        // into a NullReferenceException mid-turn, which the panel would show as a raw crash.
        if (bundle is null || prompt is null)
        {
            yield return AiTurnEvent.ForError(new AiError(
                AiErrorKind.Provider, "The assistant could not assemble this request."));
            yield break;
        }

        // Content at Debug only — the prompt carries corpus text and the user's question. (§10)
        _logger.LogDebug("AI turn prompt for {Task}:\n{System}\n---\n{User}",
            request.Task, prompt.System, prompt.UserContent);
        // The conversation so far, replayed ahead of this turn. Built before the estimate and before the
        // Started event, because both of them have to account for it: a history the reader cannot see in the
        // Sent block, or that the token figure does not count, is a request the app is misreporting. (#991)
        // The standing summary, where there is one, goes first. (#998)
        var summary = request.Summary;
        IReadOnlyList<AiExchange> history = request.History ?? Array.Empty<AiExchange>();
        var replayed = Replay(summary, history);

        // Estimated over the WHOLE REQUEST — the strings that actually go on the wire — rather than over the
        // bundle, which is a subset of them. The bundle figure omitted the system prompt, the preset's template
        // and the reader's own question, all of which are sent (#672); leaving the replayed conversation out
        // would repeat that mistake on the one part of the request that grows by itself. (#991)
        var estimatedTokens = EstimateRequest(prompt, replayed);

        // Notices are the prompt's, plus what compaction has to say. A copy per Started event, because a retry
        // (below) adds to them after the first one has been handed over.
        var notices = new List<string>(prompt.Notices);
        AiCompacted? compacted = null;

        // ---- Automatic compaction. (#998) [fsnow]: "Manual and auto at a fraction of context length"; the fraction
        // "95%, but make this a setting". Measured against the SAME estimate the Sent block reports, so the figure
        // the reader sees and the figure that triggers this cannot disagree.
        var compactFrom = CompactableCount(history);
        if (autoPercent > 0 && contextLength is null && compactFrom > 0)
        {
            // [suggestion] Only once there is something compaction could do. Before the fifth answered turn the
            // notice would be about nothing, and it would sit under every early answer on a model with no
            // published window — which is most local runners.
            notices.Add(UnknownContextNotice);
        }
        else if (autoPercent > 0 && contextLength is int window
                 && AiCompaction.Reaches(estimatedTokens, window, autoPercent))
        {
            if (compactFrom == 0)
            {
                notices.Add(
                    $"This request is about {estimatedTokens * 100L / window}% of the model's context length, and "
                    + $"there is nothing older than the last {AiCompaction.KeepVerbatim} turns to summarise.");
            }
            else
            {
                _logger.LogInformation(
                    "AI turn: ~{Tokens} of {Window} context tokens reaches the {Percent}% threshold; compacting",
                    estimatedTokens, window, autoPercent);

                AiCompactionResult? result = null;
                try
                {
                    result = await SummariseAsync(
                        provider, new AiCompactionRequest(summary, history.Take(compactFrom).ToList()),
                        effort, language, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
                {
                    superseded = true;
                }

                if (superseded) yield break;

                if (result!.Succeeded)
                {
                    compacted = Compacted(result, compactFrom);
                    var summarisedTurns = Answered(history.Take(compactFrom));
                    summary = new AiExchange(result.AskedLine, result.Summary!);
                    history = history.Skip(compactFrom).ToList();
                    replayed = Replay(summary, history);
                    estimatedTokens = EstimateRequest(prompt, replayed);
                    notices.AddRange(result.Notices ?? Array.Empty<string>());
                    notices.Add(
                        $"The conversation reached {autoPercent}% of this model's context length, so "
                        + $"{TurnsWere(summarisedTurns)} summarised before this was sent.");
                    yield return AiTurnEvent.ForCompacted(compacted);
                }
                else
                {
                    notices.Add(
                        $"Earlier turns could not be summarised ({result.Error!.Message}), so the whole "
                        + "conversation was sent.");
                }
            }
        }

        // Read off the budget report rather than off the notice wording: the panel raises its partial-passage
        // badge from this, and a badge that depends on how a sentence is phrased stops working the first time
        // the sentence is rewritten — which is exactly what had happened.
        var passageTrimmed = bundle.Budget.Parts.Any(
            p => p.Name == BundlePartNames.Passage && p.State == BundlePartState.TrimmedForBudget);

        var markers = new PaliQuoteFilter();
        int? inputTokens = null, outputTokens = null;
        var sawText = false;
        var sawReasoning = false;
        AiError? failure = null;

        // At most two attempts: the second exists only for a request the provider rejected as too long, sent again
        // after compacting (see the retry below the stream).
        for (var attempt = 0; ; attempt++)
        {
            _logger.LogInformation(
                "AI turn: {Task} on {BookId} via {Provider}/{Model}, ~{Tokens} context tokens, "
                + "{History} replayed turn(s), {Notices} notice(s)",
                request.Task, request.BookId, provider.Provider.Id, provider.Model,
                estimatedTokens, replayed.Count / 2, notices.Count);

            yield return AiTurnEvent.ForStarted(new AiTurnContext(
                bundle.Task, bundle.OutputLanguage, bundle.Citation, bundle.Book, notices.ToList(), passageTrimmed,
                Describe(bundle, prompt, provider, replayed, HasSummary(summary)),
                // Structured as well as printed in the Sent block. A stored turn has to say which model answered
                // it (#849), and the alternative — the panel matching a SentField by its English label — is the
                // same mistake as deriving the partial-passage flag from a notice's wording.
                provider.Provider.Id, provider.Model));

            // ---- Stream. The conversation, then this turn. This turn's message goes LAST and carries the full
            // rendered prompt, so the passage the model is being asked about is the last thing it reads.
            var messages = new List<ChatMessage>(replayed.Count + 1);
            messages.AddRange(replayed);
            messages.Add(new ChatMessage(ChatRole.User, prompt.UserContent));

            var chat = new ChatRequest(
                provider.Model,
                prompt.MaxOutputTokens,
                prompt.System,
                messages,
                effort);

            // The manual enumerator is required, not stylistic: `yield return` is illegal inside a try that has a
            // catch clause, and every failure below has to be turned into an event rather than propagated.
            await using (var deltas = provider.Provider.StreamAsync(chat, token).GetAsyncEnumerator(token))
            {
                while (true)
                {
                    ChatDelta delta;
                    try
                    {
                        if (!await deltas.MoveNextAsync().ConfigureAwait(false)) break;
                        delta = deltas.Current;
                    }
                    catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
                    {
                        // Superseded or stopped. Keep whatever is on screen and end quietly — being replaced is
                        // not a failure, and reporting one would put an error under an answer the user abandoned.
                        _logger.LogDebug("AI turn ended early (superseded or stopped)");
                        yield break;
                    }
                    catch (AiException ex)
                    {
                        failure = ex.Error;
                        break;
                    }

                    switch (delta.Kind)
                    {
                        case ChatDeltaKind.Text when delta.Text is { Length: > 0 } text:
                        {
                            var visible = markers.Feed(text);
                            if (visible.Length > 0) sawText = true;

                            // Yielded even when the filter held everything back, because the two halves are not
                            // interchangeable: `visible` is what the panel renders, `text` is what the model wrote,
                            // and the marked half has to reach the transcript whole so a later turn can replay it
                            // (#991). A delta ending between the two brackets of a marker is the ordinary case, not
                            // an edge one, so dropping the event when nothing is renderable yet would lose exactly
                            // the spans this exists to preserve. `sawText` still follows the VISIBLE half: a turn
                            // that produced only markers produced no answer.
                            yield return AiTurnEvent.ForText(visible, text);
                            break;
                        }

                        case ChatDeltaKind.Reasoning when delta.Text is { Length: > 0 } reasoning:
                            sawReasoning = true;
                            yield return AiTurnEvent.ForReasoning(reasoning);
                            break;

                        // Merged PER FIELD, never wholesale: the Anthropic stream reports the two halves at
                        // opposite ends of the turn, so letting a later delta supersede an earlier one erases the
                        // input count. A null field means "not reported in this delta", never zero.
                        case ChatDeltaKind.Usage when delta.Usage is { } usage:
                            inputTokens = usage.InputTokens ?? inputTokens;
                            outputTokens = usage.OutputTokens ?? outputTokens;
                            break;

                        case ChatDeltaKind.Error when delta.Error is { } error:
                            failure = error;
                            break;
                    }

                    if (failure is not null) break;
                }
            }

            // ---- Compact and retry, once. (#998) [suggestion, from the plan, accepted] A provider that rejects the
            // request as too long has measured what the estimate only guesses at: AiTokens is a chars-per-token ratio
            // taken below one tokenizer's measurement and ABOVE another's (1.73 characters per token on cl100k_base
            // against the 2.0 assumed), so on some models the real count runs ahead of the estimate by more than the 5%
            // the default threshold leaves — and on a model with no published context length there is no estimate to
            // compare at all. Either way the reader would get a dead turn in a conversation that compaction exists to
            // keep alive. Narrowly: only before anything streamed (so nothing on screen is discarded), only once, and
            // only while automatic compaction is on — "off" means off. A turn already compacted by the threshold has
            // at most four answered turns left, so there is nothing more to take (retryFrom is 0).
            var retryFrom = CompactableCount(history);
            if (attempt == 0 && failure is { Kind: AiErrorKind.ContextTooLong } && !sawText && !sawReasoning
                && autoPercent > 0 && retryFrom > 0)
            {
                _logger.LogInformation("AI turn rejected as too long; compacting and sending again");

                AiCompactionResult? result = null;
                try
                {
                    result = await SummariseAsync(
                        provider, new AiCompactionRequest(summary, history.Take(retryFrom).ToList()),
                        effort, language, token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) when (!callerToken.IsCancellationRequested)
                {
                    superseded = true;
                }

                if (superseded) yield break;

                if (result!.Succeeded)
                {
                    compacted = Compacted(result, retryFrom);
                    var summarisedTurns = Answered(history.Take(retryFrom));
                    summary = new AiExchange(result.AskedLine, result.Summary!);
                    history = history.Skip(retryFrom).ToList();
                    replayed = Replay(summary, history);
                    estimatedTokens = EstimateRequest(prompt, replayed);
                    notices.AddRange(result.Notices ?? Array.Empty<string>());
                    notices.Add(
                        "The provider said the request was too long for this model, so "
                        + $"{TurnsWere(summarisedTurns)} summarised and it was sent again.");
                    yield return AiTurnEvent.ForCompacted(compacted);

                    failure = null;
                    markers = new PaliQuoteFilter();
                    continue;
                }

                _logger.LogInformation("Compacting after a too-long rejection failed: {Kind}", result.Error!.Kind);
            }

            break;
        }

        // A bracket held back that never completed a marker: ordinary text after all. No marked half — it was
        // already carried, markers and all, by the delta it arrived in.
        var tail = markers.Flush();
        if (tail.Length > 0)
        {
            sawText = true;
            yield return AiTurnEvent.ForText(tail);
        }

        // Usage before the terminal event, and on the failure path too — a turn that died mid-stream still
        // spent tokens, and the user is paying for those either way.
        if (inputTokens is not null || outputTokens is not null)
            yield return AiTurnEvent.ForUsage(new AiUsageReport(inputTokens, outputTokens));

        // Anything that arrived at all proves the endpoint answered, whatever went wrong afterwards.
        if (sawText || sawReasoning || inputTokens is not null || outputTokens is not null)
            ReportReachability(true);

        if (failure is not null)
        {
            // The provider knows THAT the output limit was hit; only this loop knows what the user got for it.
            // Those are three different problems with three different fixes, so they get three messages. (#601)
            if (failure.Kind == AiErrorKind.Truncated)
                failure = failure with { Message = TruncationMessage(sawText, sawReasoning) };

            _logger.LogInformation("AI turn failed: {Kind} ({Code})", failure.Kind, failure.ProviderCode ?? "-");

            // Only a NETWORK failure says the endpoint could not be reached. A 401, a rate limit or an
            // over-long context all prove it answered - marking those unreachable would be wrong, and would
            // put a red mark on a perfectly good connection whose key simply needs fixing.
            //
            // And they prove it POSITIVELY, which this used to leave on the table: a status code means
            // something was there to send one, so it is contact and should be recorded as such. Reported
            // from use - a connection that had just returned a 402 was still described as never checked.
            if (failure.Kind == AiErrorKind.Network)
                ReportReachability(false);
            else if (failure.StatusCode is not null)
                ReportReachability(true);
            yield return AiTurnEvent.ForError(failure);
            yield break;
        }

        if (!sawText)
        {
            // See the class remarks: a well-formed turn that wrote nothing down. Naming the reasoning case
            // separately matters because the fix differs — raise the cap, versus try a different model.
            _logger.LogInformation(
                "AI turn produced no answer text (reasoning seen: {Reasoning})", sawReasoning);
            yield return AiTurnEvent.ForError(new AiError(
                AiErrorKind.EmptyAnswer,
                sawReasoning
                    ? "The model spent its whole response on reasoning and never wrote an answer. "
                      + "Try a higher output limit, or a different model."
                    : "The model returned an empty response."));
            yield break;
        }

        _logger.LogDebug("AI turn complete: {Quotes} marked quote(s), {Unbalanced} unbalanced",
            markers.Quotes, markers.UnbalancedMarkers);

        yield return AiTurnEvent.ForCompleted(
            new PaliMarkerReport(markers.Quotes, markers.UnbalancedMarkers));
    }

    /// <summary>
    /// The conversation the caller handed over, as wire messages: each earlier turn's question side as a
    /// <c>user</c> message and its answer as the <c>assistant</c> reply, oldest first. (#991)
    ///
    /// <para><b>An exchange missing either half is dropped here as well as by the caller.</b> The panel already
    /// omits a failed turn that produced no answer text, but this is the last point before the wire and the
    /// cost of trusting the caller is a rejected request rather than a degraded one: the Anthropic Messages API
    /// refuses an empty text block outright, and an assistant turn with nothing in it means nothing to any
    /// model even where it is accepted.</para>
    ///
    /// <para><b>The answers come back marked.</b> What the caller replays is the model's own text with its
    /// <c>[[…]]</c> Pāli markers intact, not the stripped text on screen — the system prompt asks for those
    /// markers on every Pāli span, so replaying the stripped form would show the model a transcript of itself
    /// ignoring the instruction. Nothing here inspects or repairs them.</para>
    ///
    /// <para>Nothing is re-rendered and nothing is trimmed. These strings were already sent or already shown,
    /// and rewriting one would make the replayed half of the request differ from turn to turn for no reason —
    /// see the class remarks for what does and does not hold about a cacheable prefix.</para>
    ///
    /// <para><b>The summary goes first</b> (#998): <see cref="AiCompaction.SummaryAskedLine"/> as the user side and
    /// the summary as the assistant side — the model wrote it, so it comes back as its own words, and the roles keep
    /// alternating. See <see cref="AiCompaction"/> for why that shape rather than another.</para>
    /// </summary>
    private static IReadOnlyList<ChatMessage> Replay(AiExchange? summary, IReadOnlyList<AiExchange>? history)
    {
        var messages = new List<ChatMessage>(((history?.Count ?? 0) + 1) * 2);

        if (HasSummary(summary))
        {
            messages.Add(new ChatMessage(ChatRole.User, summary!.Question));
            messages.Add(new ChatMessage(ChatRole.Assistant, summary.Answer));
        }

        foreach (var exchange in history ?? Array.Empty<AiExchange>())
        {
            if (!IsAnswered(exchange)) continue;

            messages.Add(new ChatMessage(ChatRole.User, exchange.Question));
            messages.Add(new ChatMessage(ChatRole.Assistant, exchange.Answer));
        }

        return messages;
    }

    /// <summary>The replay rule, in one place: an exchange missing either half is never sent, and so is never
    /// counted or summarised either.</summary>
    private static bool IsAnswered(AiExchange exchange) =>
        !string.IsNullOrWhiteSpace(exchange.Question) && !string.IsNullOrWhiteSpace(exchange.Answer);

    private static bool HasSummary(AiExchange? summary) => summary is not null && IsAnswered(summary);

    private static int Answered(IEnumerable<AiExchange> exchanges) => exchanges.Count(IsAnswered);

    /// <summary>
    /// How many entries from the front of <paramref name="history"/> a compaction would summarise: everything
    /// before the last <see cref="AiCompaction.KeepVerbatim"/> answered turns — <b>[fsnow]</b>: <i>"Last 4
    /// turns"</i>. Zero when there are that many answered turns or fewer, which is "nothing to compact". (#998)
    ///
    /// <para>Counted in entries <b>as the caller passed them</b>, unanswered ones included, so the caller can map the
    /// count straight back onto its own list. The panel filters before it sends, so for it the two agree.</para>
    /// </summary>
    private static int CompactableCount(IReadOnlyList<AiExchange> history)
    {
        var answered = new List<int>();
        for (var i = 0; i < history.Count; i++)
            if (IsAnswered(history[i])) answered.Add(i);

        return answered.Count > AiCompaction.KeepVerbatim
            ? answered[answered.Count - AiCompaction.KeepVerbatim]
            : 0;
    }

    private static AiCompacted Compacted(AiCompactionResult result, int summarisedExchanges) =>
        new(result.AskedLine, result.Summary!, summarisedExchanges, result.ProviderId, result.ModelId);

    private static string TurnsWere(int count) =>
        count == 1 ? "1 earlier turn was" : $"{count} earlier turns were";

    /// <summary>
    /// What a turn says when automatic compaction cannot fire for this model. The notice the plan asks for
    /// ("a notice on the turn that says why"), worded around what the reader can do. It says the retry still
    /// applies, because it does: a too-long rejection needs no published window.
    /// </summary>
    internal const string UnknownContextNotice =
        "This model's context length is not known, so earlier turns are summarised automatically only if the "
        + "provider says a request is too long. Compact summarises them now.";

    public async Task<AiCompactionResult> CompactAsync(AiCompactionRequest request, CancellationToken ct = default)
    {
        var provider = _resolver.Resolve(out var problem);
        if (provider is null)
        {
            return AiCompactionResult.Failed(new AiError(
                AiErrorKind.NotConfigured, problem ?? "The assistant is not configured yet."));
        }

        var language = _settings.Settings.Ai.Chat.AnswerLanguage;
        if (string.IsNullOrWhiteSpace(language)) language = "English";

        return await SummariseAsync(provider, request, ReasoningEffortFor(provider.Model), language, ct)
            .ConfigureAwait(false);
    }

    /// <summary>
    /// One summary call: the compaction template over the turns being compacted, sent as a single user message to
    /// the resolved model. (#998)
    ///
    /// <para><b>Collected, not streamed to anyone.</b> A summary is only useful whole — half of one would silently
    /// drop the turns it did not reach — so the text is gathered and handed over at the end, and a summary cut off
    /// at the output limit is refused rather than used. <b>Reasoning is dropped</b>, as it is never replayed.</para>
    ///
    /// <para><b>The raw text is kept</b>, markers and all: no <see cref="PaliQuoteFilter"/> here, because this text
    /// is replayed to the model and never rendered.</para>
    ///
    /// <para>Every expected failure is an <see cref="AiError"/>; only the caller's cancellation throws.</para>
    /// </summary>
    private async Task<AiCompactionResult> SummariseAsync(
        ChatProviderResolution provider, AiCompactionRequest request, string? effort, string language,
        CancellationToken ct)
    {
        if (!request.Turns.Any(IsAnswered))
        {
            return AiCompactionResult.Failed(new AiError(
                AiErrorKind.Provider,
                $"There is nothing older than the last {AiCompaction.KeepVerbatim} turns to summarise."));
        }

        RenderedCompaction prompt;
        try
        {
            prompt = _prompts.BuildCompaction(request, language);
        }
        catch (PromptTemplateException ex)
        {
            _logger.LogError(ex, "The compaction prompt could not be loaded");
            return AiCompactionResult.Failed(new AiError(
                AiErrorKind.Provider, "The assistant's prompts could not be loaded. This build may be damaged."));
        }

        _logger.LogDebug("AI compaction prompt:\n{Prompt}", prompt.UserContent);
        _logger.LogInformation(
            "Summarising {Turns} earlier turn(s){Previous} via {Provider}/{Model}, ~{Tokens} tokens",
            Answered(request.Turns), HasSummary(request.PreviousSummary) ? " and the earlier summary" : "",
            provider.Provider.Id, provider.Model, AiTokens.Estimate(prompt.UserContent));

        var chat = new ChatRequest(
            provider.Model, null, null, new[] { new ChatMessage(ChatRole.User, prompt.UserContent) }, effort);

        var text = new System.Text.StringBuilder();
        var heard = false;
        AiError? failure = null;
        try
        {
            await foreach (var delta in provider.Provider.StreamAsync(chat, ct).WithCancellation(ct)
                               .ConfigureAwait(false))
            {
                heard = true;
                if (delta.Kind == ChatDeltaKind.Text && delta.Text is { Length: > 0 } piece)
                {
                    text.Append(piece);
                }
                else if (delta.Kind == ChatDeltaKind.Error && delta.Error is { } error)
                {
                    failure = error;
                    break;
                }
            }
        }
        catch (AiException ex)
        {
            failure = ex.Error;
        }
        catch (OperationCanceledException)
        {
            throw;
        }
        catch (Exception ex)
        {
            // The provider contract says this cannot happen; the promise to the caller is still a sentence.
            _logger.LogError(ex, "Summarising earlier turns failed unexpectedly");
            failure = new AiError(AiErrorKind.Provider, "Something went wrong while summarising.");
        }

        if (heard || failure?.StatusCode is not null) ReportReachability(true);
        else if (failure?.Kind == AiErrorKind.Network) ReportReachability(false);

        if (failure is not null)
        {
            if (failure.Kind == AiErrorKind.Truncated)
            {
                failure = failure with
                {
                    Message = "The summary was cut off at the model's output limit, so it was not used.",
                };
            }

            _logger.LogInformation("Summarising failed: {Kind} ({Code})", failure.Kind, failure.ProviderCode ?? "-");
            return AiCompactionResult.Failed(failure) with { Notices = prompt.Notices };
        }

        var summary = text.ToString().Trim();
        if (summary.Length == 0)
        {
            return AiCompactionResult.Failed(new AiError(
                AiErrorKind.EmptyAnswer, "The model returned an empty summary.")) with { Notices = prompt.Notices };
        }

        return new AiCompactionResult(
            summary, AiCompaction.SummaryAskedLine, null, provider.Provider.Id, provider.Model, prompt.Notices);
    }

    /// <summary>Every string this request puts on the wire, taken together — the system prompt, the replayed
    /// conversation, and this turn's own message. (#672, #991)</summary>
    private static int EstimateRequest(RenderedPrompt prompt, IReadOnlyList<ChatMessage> replayed) =>
        AiTokens.Estimate(
            new[] { prompt.System, prompt.UserContent }.Concat(replayed.Select(m => m.Content)));

    /// <summary>
    /// What this turn sent: named fields, the replayed conversation, and the two prompt halves. Assembled here
    /// because this is the only place that holds the bundle, the rendered prompt and the resolved provider at
    /// once. (#665)
    /// </summary>
    private static SentContext Describe(
        AiContextBundle bundle, RenderedPrompt prompt, ChatProviderResolution provider,
        IReadOnlyList<ChatMessage> replayed, bool hasSummary)
    {
        var fields = new List<SentField>
        {
            new("Provider", provider.Provider.Id),
            new("Model", provider.Model),
            new("Request", bundle.Task.ToString()),
            new("Answer language", bundle.OutputLanguage),
            new("Book", bundle.Book.Name),
            new("Book id", bundle.Book.BookId),
            new("Reference", bundle.Citation.NormalizedReference),
            // Over the whole request, which is what was sent. Reading this off the bundle omitted the system
            // prompt, the preset template and the reader's own question — a figure captioned as the context
            // that measured a subset of it (#672) — and the replayed conversation is the part that grows without
            // the reader doing anything, so its share is named rather than folded in. (#991)
            new("Estimated context", DescribeEstimate(prompt, replayed, hasSummary)),
        };

        if (bundle.Budget.ParagraphsCovered is int covered)
            fields.Add(new SentField("Paragraphs covered", covered.ToString()));

        if (bundle.Citation.Pages.Count > 0)
            fields.Add(new SentField("Pages", string.Join(", ", bundle.Citation.Pages.Select(PageRef))));

        // What each gathered part contributed, including the ones that contributed nothing — an absence is
        // as much a part of what was sent as a presence, and harder to notice.
        foreach (var part in bundle.Budget.Parts)
        {
            var detail = string.IsNullOrWhiteSpace(part.Detail) ? part.State.ToString() : $"{part.State} — {part.Detail}";
            fields.Add(new SentField($"Part: {part.Name}", detail));
        }

        return new SentContext(fields, prompt.System, prompt.UserContent, replayed);
    }

    /// <summary>
    /// The estimate as the Sent block states it: the whole request, and how much of it the conversation
    /// accounts for. (#991)
    ///
    /// <para>The two figures answer different questions. The total is what this request costs; the history's
    /// share is what it will cost to keep asking — the number a reader watching a long conversation approach a
    /// context window needs, and cannot derive from the total.</para>
    ///
    /// <para>After a compaction the summary is named as such (#998): it is one exchange on the wire but stands for
    /// many turns, and counting it as "1 earlier turn" would misreport both.</para>
    /// </summary>
    private static string DescribeEstimate(
        RenderedPrompt prompt, IReadOnlyList<ChatMessage> replayed, bool hasSummary)
    {
        var total = EstimateRequest(prompt, replayed);
        if (replayed.Count == 0) return $"~{total:N0} tokens";

        var history = AiTokens.Estimate(replayed.Select(m => m.Content));
        var turns = replayed.Count / 2 - (hasSummary ? 1 : 0);
        var earlier = turns == 1 ? "1 earlier turn" : $"{turns} earlier turns";
        var what = !hasSummary ? earlier
            : turns == 0 ? "a summary of earlier turns"
            : $"a summary and {earlier}";
        return $"~{total:N0} tokens, ~{history:N0} of them {what}";
    }

    private static string PageRef(SnippetPageRef page)
    {
        var edition = page.Edition switch
        {
            PageEdition.Vri => "VRI",
            PageEdition.Myanmar => "Myanmar",
            PageEdition.Pts => "PTS",
            PageEdition.Thai => "Thai",
            _ => "other",
        };
        return page.Volume > 0 ? $"{edition} {page.Volume}.{page.Number}" : $"{edition} {page.Number}";
    }

    /// <summary>Records what this turn learned about the active endpoint, so Settings and the assistant read
    /// one fact rather than each guessing. Never throws: reporting is a courtesy, not part of the turn.</summary>
    private void ReportReachability(bool reachable)
    {
        try
        {
            if (_connections?.Active is { } active)
                _connections.ReportReachability(active.Id, reachable);
        }
        catch (Exception ex)
        {
            _logger.LogDebug(ex, "Could not record endpoint reachability (#673)");
        }
    }

    /// <summary>
    /// What to tell the user when the model stopped at its output limit (#601). The three cases are genuinely
    /// different situations, and the difference is invisible to the provider that detected the truncation:
    ///
    /// <list type="bullet">
    /// <item>Text was written — <b>the dangerous one</b>. Without this message a half-finished translation
    /// renders under a citation exactly like a finished one, and nothing on screen says otherwise.</item>
    /// <item>Reasoning but no answer — #601's original case. The work was done and never written down; the fix
    /// is a bigger budget or a lighter-reasoning model, not a retry.</item>
    /// <item>Neither — the cap is small enough that nothing could be produced at all.</item>
    /// </list>
    /// </summary>
    private static string TruncationMessage(bool sawText, bool sawReasoning) =>
        sawText
            ? "This answer is incomplete: the model reached its output limit and stopped part-way through."
            : sawReasoning
                ? "The model spent its whole output limit on reasoning and never wrote an answer. "
                  + "Try a higher output limit, or a model that reasons less."
                : "The model reached its output limit before writing anything.";

    /// <summary>Cancelling a source another turn already disposed is a benign race, not an error.</summary>
    private static void TryCancel(CancellationTokenSource source)
    {
        try { source.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}
