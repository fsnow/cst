using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
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
        ILogger<AiChatOrchestrator> logger)
    {
        _resolver = resolver;
        _bundler = bundler;
        _prompts = prompts;
        _settings = settings;
        _logger = logger;
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
    /// <param name="token">The linked token actually passed downstream.</param>
    private async IAsyncEnumerable<AiTurnEvent> RunCoreAsync(
        AiTurnRequest request, CancellationToken callerToken, CancellationToken token)
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
                    request.SelectionText, request.UserQuestion),
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

        // Content at Debug only — the prompt carries corpus text and the user's question. (§10)
        _logger.LogDebug("AI turn prompt for {Task}:\n{System}\n---\n{User}",
            request.Task, prompt.System, prompt.UserContent);
        _logger.LogInformation(
            "AI turn: {Task} on {BookId} via {Provider}/{Model}, ~{Tokens} context tokens, {Notices} notice(s)",
            request.Task, request.BookId, provider.Provider.Id, provider.Model,
            bundle.Budget.ApproximateTokens, prompt.Notices.Count);

        yield return AiTurnEvent.ForStarted(new AiTurnContext(
            bundle.Task, bundle.OutputLanguage, bundle.Citation, bundle.Book, prompt.Notices));

        // ---- Stream.
        var chat = new ChatRequest(
            provider.Model,
            prompt.MaxOutputTokens,
            prompt.System,
            new[] { new ChatMessage(ChatRole.User, prompt.UserContent) });

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

        if (failure is not null)
        {
            _logger.LogInformation("AI turn failed: {Kind} ({Code})", failure.Kind, failure.ProviderCode ?? "-");
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

    /// <summary>Cancelling a source another turn already disposed is a benign race, not an error.</summary>
    private static void TryCancel(CancellationTokenSource source)
    {
        try { source.Cancel(); }
        catch (ObjectDisposedException) { }
    }
}
