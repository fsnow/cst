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
        _logger.LogInformation(
            "AI turn: {Task} on {BookId} via {Provider}/{Model}, ~{Tokens} context tokens, {Notices} notice(s)",
            request.Task, request.BookId, provider.Provider.Id, provider.Model,
            bundle.Budget.ApproximateTokens, prompt.Notices.Count);

        // Read off the budget report rather than off the notice wording: the panel raises its partial-passage
        // badge from this, and a badge that depends on how a sentence is phrased stops working the first time
        // the sentence is rewritten — which is exactly what had happened.
        var passageTrimmed = bundle.Budget.Parts.Any(
            p => p.Name == BundlePartNames.Passage && p.State == BundlePartState.TrimmedForBudget);

        yield return AiTurnEvent.ForStarted(new AiTurnContext(
            bundle.Task, bundle.OutputLanguage, bundle.Citation, bundle.Book, prompt.Notices, passageTrimmed,
            Describe(bundle, prompt, provider)));

        // ---- Stream.
        var chat = new ChatRequest(
            provider.Model,
            prompt.MaxOutputTokens,
            prompt.System,
            new[] { new ChatMessage(ChatRole.User, prompt.UserContent) },
            ReasoningEffortFor(provider.Model));

        var markers = new PaliQuoteFilter();
        int? inputTokens = null, outputTokens = null;
        var sawText = false;
        var sawReasoning = false;
        AiError? failure = null;

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
                        if (visible.Length > 0)
                        {
                            sawText = true;
                            yield return AiTurnEvent.ForText(visible);
                        }
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
    /// <summary>
    /// What this turn sent, as named fields plus the two prompt halves. Assembled here because this is the
    /// only place that holds the bundle, the rendered prompt and the resolved provider at once. (#665)
    /// </summary>
    private static SentContext Describe(
        AiContextBundle bundle, RenderedPrompt prompt, ChatProviderResolution provider)
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
            new("Estimated context", $"~{bundle.Budget.ApproximateTokens:N0} tokens"),
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

        return new SentContext(fields, prompt.System, prompt.UserContent);
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
