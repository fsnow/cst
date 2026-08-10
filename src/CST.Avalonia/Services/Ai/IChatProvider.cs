using System.Collections.Generic;
using System.Threading;

namespace CST.Avalonia.Services.Ai;

/// <summary>
/// A streaming chat model, behind one interface so surface B does not care which provider is configured.
/// Implemented twice: <see cref="AnthropicMessagesProvider"/> and <see cref="OpenAiCompatibleProvider"/>.
/// (#578, AI_SURFACE_B.md §5)
/// </summary>
public interface IChatProvider
{
    /// <summary>A short id for logs and settings, e.g. <c>anthropic</c> / <c>openai-compatible</c>.</summary>
    string Id { get; }

    /// <summary>
    /// Stream a response. Deltas arrive as they are produced; the enumerable completes when the model stops.
    ///
    /// <para><b>Failure has two shapes, and the boundary is the HTTP response — not the first delta.</b> A
    /// failure while establishing the response — unreachable host, 401, a request the provider rejected
    /// outright — throws <see cref="AiException"/>, because nothing can be on screen yet. Once a successful
    /// response has been accepted, every failure yields a <see cref="ChatDeltaKind.Error"/> delta and the
    /// enumerable completes normally, so the caller keeps whatever partial answer it has already shown.</para>
    ///
    /// <para><b>A caller must therefore handle an Error delta arriving with no preceding text.</b> A stream can
    /// fail after the response is accepted but before it produces anything — a first event that is an error, a
    /// connection dropped immediately after the headers, a provider that accepts and then goes silent, or a 200
    /// carrying no events at all. That is not the same as "there is a partial answer to preserve".</para>
    ///
    /// <para>An <see cref="ChatDeltaKind.Error"/> delta is terminal: no further deltas follow it.</para>
    ///
    /// <para>Cancellation via <paramref name="ct"/> is neither: it throws
    /// <see cref="System.OperationCanceledException"/> like any other .NET async method, and is never reported as
    /// an <see cref="AiError"/>. An idle stream that stops producing without being cancelled IS an error, and
    /// surfaces as <see cref="AiErrorKind.Network"/>.</para>
    /// </summary>
    IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request, CancellationToken ct = default);
}
