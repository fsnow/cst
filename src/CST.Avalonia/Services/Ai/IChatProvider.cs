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
    /// <para><b>Failure has two shapes, deliberately.</b> A failure before any content — unreachable host, 401,
    /// a request the provider rejected outright — throws <see cref="AiException"/>, because there is nothing on
    /// screen yet and an exception is the cleaner contract. A failure once deltas have started yields a
    /// <see cref="ChatDeltaKind.Error"/> delta and completes normally, so the caller keeps the partial answer it
    /// has already shown the user.</para>
    ///
    /// <para>Cancellation via <paramref name="ct"/> is neither: it throws
    /// <see cref="System.OperationCanceledException"/> like any other .NET async method, and is never reported as
    /// an <see cref="AiError"/>. An idle stream that stops producing without being cancelled IS an error, and
    /// surfaces as <see cref="AiErrorKind.Network"/>.</para>
    /// </summary>
    IAsyncEnumerable<ChatDelta> StreamAsync(ChatRequest request, CancellationToken ct = default);
}
