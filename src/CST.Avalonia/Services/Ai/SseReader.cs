using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.CompilerServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CST.Avalonia.Services.Ai;

/// <summary>One dispatched Server-Sent Event, or the failure that ended the stream.</summary>
/// <param name="Name">The <c>event:</c> field. Null when the stream omits it, as the OpenAI shape does.</param>
/// <param name="Data">The accumulated <c>data:</c> field(s), newline-joined per the SSE spec.</param>
/// <param name="Failure">Set only on the final element, when the stream ended abnormally.</param>
internal sealed record SseEvent(string? Name, string Data, AiError? Failure = null)
{
    public static SseEvent Failed(AiError error) => new(null, string.Empty, error);
}

/// <summary>
/// Minimal Server-Sent Events reader, shared by both providers because both speak SSE and only differ in what
/// the JSON payloads mean.
///
/// <para><b>The idle timeout is the point of this class.</b> <see cref="System.Net.Http.HttpClient"/>'s own
/// timeout is a whole-request deadline, which is useless for a stream that is legitimately allowed to run for
/// minutes — so the client is configured with an infinite timeout and liveness is enforced here instead, as a
/// per-read deadline that resets on every byte. A provider that accepts the connection and then goes silent is
/// otherwise indistinguishable from a slow answer, and would hang until the process exits.</para>
/// </summary>
internal static class SseReader
{
    /// <summary>How long a stream may produce nothing at all before we call it dead.</summary>
    internal static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// Read events until the stream ends. Never throws for stream failures — an abnormal end arrives as a final
    /// <see cref="SseEvent"/> carrying <see cref="SseEvent.Failure"/>, so callers can surface a partial answer
    /// rather than losing it. Genuine cancellation via <paramref name="ct"/> DOES throw, as it should.
    /// </summary>
    internal static async IAsyncEnumerable<SseEvent> ReadAsync(
        Stream stream,
        TimeSpan idleTimeout,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        // Linked so a real cancellation still propagates; CancelAfter is rescheduled on every successful read,
        // which is what makes this an idle timeout rather than a total one.
        using var idle = CancellationTokenSource.CreateLinkedTokenSource(ct);
        idle.CancelAfter(idleTimeout);

        string? name = null;
        var data = new StringBuilder();

        while (true)
        {
            string? line;
            AiError? failure = null;

            try
            {
                line = await reader.ReadLineAsync(idle.Token).ConfigureAwait(false);
                idle.CancelAfter(idleTimeout);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;  // the caller asked to stop — not a failure
            }
            catch (OperationCanceledException)
            {
                line = null;
                failure = new AiError(
                    AiErrorKind.Network,
                    $"The model stopped responding (no data for {idleTimeout.TotalSeconds:0}s).");
            }
            catch (Exception ex) when (ex is IOException or System.Net.Http.HttpRequestException)
            {
                line = null;
                failure = new AiError(AiErrorKind.Network, "The connection to the model was lost mid-response.");
            }

            if (failure is not null)
            {
                // Dispatch what was already buffered BEFORE reporting the failure. A severed connection
                // routinely cuts the stream after a complete event but before the blank line that would have
                // dispatched it; dropping that buffer would throw away text the user is entitled to keep, which
                // is the whole reason a mid-stream failure is a delta rather than an exception. A genuinely
                // half-written event is unparseable JSON and the provider skips it.
                if (data.Length > 0)
                    yield return new SseEvent(name, data.ToString());

                yield return SseEvent.Failed(failure);
                yield break;
            }

            // End of stream. A well-behaved provider has already sent its terminator; anything buffered here is
            // a final event the stream never dispatched, so emit it rather than silently dropping content.
            if (line is null)
            {
                if (data.Length > 0)
                    yield return new SseEvent(name, data.ToString());
                yield break;
            }

            // Blank line dispatches the event being accumulated.
            if (line.Length == 0)
            {
                if (data.Length > 0)
                    yield return new SseEvent(name, data.ToString());

                name = null;
                data.Clear();
                continue;
            }

            if (line[0] == ':')
                continue;  // comment / keep-alive

            var colon = line.IndexOf(':');
            var field = colon < 0 ? line : line[..colon];
            var value = colon < 0 ? string.Empty : line[(colon + 1)..];
            if (value.StartsWith(' '))
                value = value[1..];

            switch (field)
            {
                case "event":
                    name = value;
                    break;
                case "data":
                    if (data.Length > 0) data.Append('\n');
                    data.Append(value);
                    break;
                // id / retry are part of SSE reconnection, which neither provider uses. Ignore.
            }
        }
    }
}
