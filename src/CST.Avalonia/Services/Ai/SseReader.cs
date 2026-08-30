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
/// <para><b>The liveness timeouts are the point of this class.</b> <see cref="System.Net.Http.HttpClient"/>'s own
/// timeout is a whole-request deadline, which is useless for a stream that is legitimately allowed to run for
/// minutes — so the client is configured with an infinite timeout and liveness is enforced here instead. A
/// provider that accepts the connection and then goes silent is otherwise indistinguishable from a slow answer,
/// and would hang until the process exits.</para>
/// </summary>
internal static class SseReader
{
    /// <summary>
    /// How long the stream may produce nothing between lines before we call it dead. The budget is per LINE,
    /// not per byte — it is armed once per read and is NOT refreshed by bytes arriving, so a single line that
    /// takes longer than this to complete is killed and reported as "the model stopped responding" even though
    /// data was flowing. That matches SSE, where nothing is actionable until a line completes, and a stream
    /// sending no newline at all must not hang us; it does mean a provider that dribbled one enormous line
    /// would be cut off, which no provider does today. (R1-4)
    /// </summary>
    internal static readonly TimeSpan DefaultIdleTimeout = TimeSpan.FromSeconds(120);

    /// <summary>
    /// A longer allowance for the FIRST line, which is the only one that includes the model's time-to-first-token.
    /// A local runner doing prompt evaluation over an injected passage on modest hardware can legitimately sit
    /// silent for minutes before emitting anything — folding that into the ordinary idle window would abandon the
    /// fully-local path exactly when it is working hardest.
    /// </summary>
    internal static readonly TimeSpan DefaultFirstEventTimeout = TimeSpan.FromMinutes(10);

    /// <summary>
    /// Read events until the stream ends. Never throws for stream failures — an abnormal end arrives as a final
    /// <see cref="SseEvent"/> carrying <see cref="SseEvent.Failure"/>, so callers can surface a partial answer
    /// rather than losing it. Genuine cancellation via <paramref name="ct"/> DOES throw, as it should.
    /// </summary>
    internal static async IAsyncEnumerable<SseEvent> ReadAsync(
        Stream stream,
        TimeSpan idleTimeout,
        TimeSpan firstEventTimeout,
        [EnumeratorCancellation] CancellationToken ct = default)
    {
        using var reader = new StreamReader(stream, Encoding.UTF8, detectEncodingFromByteOrderMarks: true);

        // Linked so a real cancellation still propagates; the deadline is rescheduled after every successful
        // read, which is what makes this an idle timeout rather than a total one.
        //
        // Benign race: the timer can fire in the window between a read returning and the reschedule below. The
        // token is then permanently cancelled and the next read reports an idle timeout even though data had just
        // arrived. It needs the stream to go quiet for the whole window and then deliver a line within
        // microseconds of expiry; the cost is one spurious "stopped responding" that a retry clears.
        using var deadline = CancellationTokenSource.CreateLinkedTokenSource(ct);

        string? name = null;
        var data = new StringBuilder();
        var sawAnyData = false;

        // The time-to-first-data allowance is measured from HERE, once, and never re-armed. It used to be
        // re-armed before every read like the idle window, which made it not a ceiling at all: a proxy that
        // sends keep-alive comments — OpenRouter's ": OPENROUTER PROCESSING" is the case this file already
        // names — reset the full allowance with each comment, so a request behind a wedged backend waited
        // FOREVER while looking healthy. The user clicked a preset, watched a counter climb, and gave up
        // without ever being told anything was wrong.
        var sinceRequest = System.Diagnostics.Stopwatch.StartNew();

        while (true)
        {
            string? line;
            AiError? failure = null;

            try
            {
                // Armed BEFORE the read, so the window in force always matches the pivot. Arming afterwards
                // left one read carrying the previous line's verdict — harmless (always the more lenient of the
                // two) but it made the timeout message name a window that had not applied.
                //
                // Two different shapes of window, deliberately. AFTER the first data line the idle window
                // slides: a stream that keeps producing is allowed to run for as long as it likes, and only a
                // GAP is fatal. BEFORE it, what remains of the first-data allowance is what is left of a fixed
                // budget from the request — anything else lets keep-alives hold the door open indefinitely.
                deadline.CancelAfter(sawAnyData ? idleTimeout : Remaining(firstEventTimeout, sinceRequest));
                line = await reader.ReadLineAsync(deadline.Token).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested)
            {
                throw;  // the caller asked to stop — not a failure
            }
            catch (OperationCanceledException)
            {
                line = null;
                // Two different facts, so two different sentences. Before any data the elapsed time IS the
                // whole wait, and the user watched all of it; after it, only the gap is news.
                failure = sawAnyData
                    ? new AiError(
                        AiErrorKind.Network,
                        $"The model stopped responding (no data for {idleTimeout.TotalSeconds:0}s).")
                    : new AiError(
                        AiErrorKind.Network,
                        $"The model sent nothing back after {firstEventTimeout.TotalSeconds:0}s.");
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
                    // Still the pivot: the first DATA line, not merely the first line. What changed is only
                    // that the pre-pivot window is now a fixed budget rather than a per-line one.
                    // The pivot from the first-event window to the ordinary idle window is the first DATA line,
                    // not merely the first line. A proxy that emits a preamble comment (OpenRouter sends
                    // ": OPENROUTER PROCESSING") and then waits on a slow backend would otherwise collapse the
                    // long time-to-first-token allowance to the short idle one — reintroducing exactly the
                    // problem the two windows exist to separate.
                    sawAnyData = true;
                    if (data.Length > 0) data.Append('\n');
                    data.Append(value);
                    break;
                // id / retry are part of SSE reconnection, which neither provider uses. Ignore.
            }
        }
    }

    /// <summary>
    /// What is left of a fixed budget. Zero once it is spent — <see cref="CancellationTokenSource.CancelAfter"/>
    /// cancels immediately on zero, which is the right answer for a budget already gone rather than an
    /// accidental renewal.
    /// </summary>
    private static TimeSpan Remaining(TimeSpan budget, System.Diagnostics.Stopwatch since)
    {
        var left = budget - since.Elapsed;
        return left > TimeSpan.Zero ? left : TimeSpan.Zero;
    }
}
