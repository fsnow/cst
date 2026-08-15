using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Services.Ai;
using Xunit;

namespace CST.Avalonia.Tests.Services.Ai;

/// <summary>
/// The SSE reader's liveness windows. (#583)
///
/// <para>The HTTP client runs with an infinite timeout on purpose — a finite one truncates a long generation
/// and reports it as a cancellation — so these two windows are the ONLY thing standing between the user and an
/// endless wait. That makes them worth testing directly rather than through a provider.</para>
/// </summary>
public class SseReaderTests
{
    /// <summary>
    /// A stream that hands out lines on a schedule, so a test can describe a provider that dribbles keep-alives
    /// at a wedged backend. Blocks between lines rather than returning them all at once, which is the whole
    /// point: a reader that never blocks can never time out.
    /// </summary>
    private sealed class PacedStream : Stream
    {
        private readonly IReadOnlyList<(TimeSpan After, string Line)> _script;
        private int _next;
        private byte[] _pending = Array.Empty<byte>();
        private int _offset;

        private readonly EndBehaviour _end;

        /// <summary>What the provider does once the script runs out. Only the last two can be timed out, and
        /// conflating them with <see cref="Closes"/> makes a well-behaved provider look like a stall.</summary>
        internal enum EndBehaviour
        {
            /// <summary>Closes the stream — a normal end of response.</summary>
            Closes,

            /// <summary>Goes quiet and holds the connection open — a wedged backend.</summary>
            Stalls,

            /// <summary>Replays the script forever — a proxy heartbeating at a backend that never answers.</summary>
            Repeats,
        }

        internal PacedStream(EndBehaviour end, params (TimeSpan After, string Line)[] script)
        {
            _end = end;
            _script = script;
        }

        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_offset >= _pending.Length)
            {
                if (_next >= _script.Count)
                {
                    if (_end == EndBehaviour.Closes) return 0;
                    if (_end == EndBehaviour.Repeats) _next = 0;
                    else await Task.Delay(Timeout.Infinite, cancellationToken).ConfigureAwait(false);
                }

                var (after, line) = _script[_next++];
                await Task.Delay(after, cancellationToken).ConfigureAwait(false);
                _pending = Encoding.UTF8.GetBytes(line + "\n");
                _offset = 0;
            }

            var count = Math.Min(buffer.Length, _pending.Length - _offset);
            _pending.AsSpan(_offset, count).CopyTo(buffer.Span);
            _offset += count;
            return count;
        }

        public override int Read(byte[] buffer, int offset, int count) =>
            ReadAsync(buffer.AsMemory(offset, count)).AsTask().GetAwaiter().GetResult();

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => throw new NotSupportedException();
        public override long Position { get => 0; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    private static async Task<List<SseEvent>> ReadAllAsync(
        Stream stream, TimeSpan idle, TimeSpan firstEvent, TimeSpan abandonAfter)
    {
        // A hard backstop so a REGRESSION fails as a timeout rather than hanging the suite forever — which is
        // precisely what the bug under test did to the user.
        using var abandon = new CancellationTokenSource(abandonAfter);
        var events = new List<SseEvent>();
        await foreach (var e in SseReader.ReadAsync(stream, idle, firstEvent, abandon.Token))
            events.Add(e);
        return events;
    }

    [Fact]
    public async Task Keep_alive_comments_cannot_hold_the_first_data_window_open_forever()
    {
        // The reported hang. OpenRouter emits ": OPENROUTER PROCESSING" while it waits on a backend; the
        // first-data allowance used to be re-armed before every READ, and a comment is a read. So each
        // heartbeat renewed the whole allowance and the ceiling was not a ceiling: the request waited as long
        // as the proxy cared to keep saying hello. The user clicked Explain and never got anything back.
        // Heartbeats that NEVER stop. A finite run of them would not catch this: the old code did give up
        // eventually — one window after the last heartbeat — so only an endless stream distinguishes "gives up
        // on schedule" from "gives up whenever the proxy happens to fall silent".
        var stream = new PacedStream(
            PacedStream.EndBehaviour.Repeats,
            (TimeSpan.FromMilliseconds(100), ": OPENROUTER PROCESSING"));

        var elapsed = System.Diagnostics.Stopwatch.StartNew();
        var events = await ReadAllAsync(
            stream,
            idle: TimeSpan.FromSeconds(30),          // generous: this must not be what fires
            firstEvent: TimeSpan.FromMilliseconds(400),
            abandonAfter: TimeSpan.FromSeconds(10));
        elapsed.Stop();

        var failure = Assert.Single(events).Failure;
        Assert.NotNull(failure);
        Assert.Equal(AiErrorKind.Network, failure!.Kind);
        // And it says which of the two waits ended, because the user's next move differs.
        Assert.Contains("sent nothing back", failure.Message);

        // The load-bearing assertion: it gave up on ITS OWN schedule, not the proxy's. Generous margin so a
        // loaded CI box does not fail this, but far below the 10s backstop that a re-armed window would ride
        // all the way to.
        Assert.True(elapsed.Elapsed < TimeSpan.FromSeconds(4), $"gave up only after {elapsed.Elapsed}");
    }

    [Fact]
    public async Task A_comment_before_real_data_still_buys_the_long_first_data_window()
    {
        // The behaviour the pivot exists for, and which the fix must not cost: a preamble comment must NOT
        // collapse the long time-to-first-token allowance down to the short idle one. A slow model that
        // announces itself and then thinks is working normally.
        var stream = new PacedStream(
            PacedStream.EndBehaviour.Closes,
            (TimeSpan.FromMilliseconds(50), ": OPENROUTER PROCESSING"),
            (TimeSpan.FromMilliseconds(600), "data: hello"),
            (TimeSpan.Zero, ""),
            (TimeSpan.Zero, "data: [DONE]"),
            (TimeSpan.Zero, ""));

        var events = await ReadAllAsync(
            stream,
            idle: TimeSpan.FromMilliseconds(200),     // shorter than the gap before the first data line
            firstEvent: TimeSpan.FromSeconds(20),
            abandonAfter: TimeSpan.FromSeconds(20));

        Assert.All(events, e => Assert.Null(e.Failure));
        Assert.Contains(events, e => e.Data == "hello");
    }

    [Fact]
    public async Task Once_data_is_flowing_the_idle_window_slides_and_a_long_answer_is_not_cut_off()
    {
        // The other half of the same contract: after first data, only a GAP is fatal. A stream that keeps
        // producing may run far longer than the first-data allowance — that is an answer arriving, not a fault.
        var stream = new PacedStream(
            PacedStream.EndBehaviour.Closes,
            (TimeSpan.FromMilliseconds(50), "data: one"),
            (TimeSpan.Zero, ""),
            (TimeSpan.FromMilliseconds(200), "data: two"),
            (TimeSpan.Zero, ""),
            (TimeSpan.FromMilliseconds(200), "data: three"),
            (TimeSpan.Zero, ""));

        var events = await ReadAllAsync(
            stream,
            idle: TimeSpan.FromSeconds(5),
            firstEvent: TimeSpan.FromMilliseconds(300),   // total run exceeds this comfortably
            abandonAfter: TimeSpan.FromSeconds(20));

        Assert.Equal(new[] { "one", "two", "three" }, events.Where(e => e.Failure is null).Select(e => e.Data));

    }

    [Fact]
    public async Task A_stall_after_data_reports_the_gap_rather_than_the_total()
    {
        var stream = new PacedStream(
            PacedStream.EndBehaviour.Stalls,
            (TimeSpan.FromMilliseconds(50), "data: one"),
            (TimeSpan.Zero, ""));

        var events = await ReadAllAsync(
            stream,
            idle: TimeSpan.FromMilliseconds(300),
            firstEvent: TimeSpan.FromSeconds(20),
            abandonAfter: TimeSpan.FromSeconds(20));

        Assert.Contains(events, e => e.Data == "one");        // what arrived is kept
        var failure = events.Last().Failure;
        Assert.NotNull(failure);
        Assert.Contains("stopped responding", failure!.Message);
    }
}
