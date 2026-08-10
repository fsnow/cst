using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace CST.Avalonia.Tests.TestSupport;

/// <summary>
/// Replays a canned HTTP response so the chat providers can be tested against recorded wire traffic — no
/// network, no API key, no spend. Also captures the outgoing request, which is half the point: the request
/// SHAPE is part of the providers' contract (Anthropic requires <c>max_tokens</c> and rejects sampling
/// parameters), and a shape regression is invisible from the response side.
/// </summary>
internal sealed class StubHttpMessageHandler : HttpMessageHandler
{
    private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;

    internal StubHttpMessageHandler(Func<HttpRequestMessage, HttpResponseMessage> respond) => _respond = respond;

    /// <summary>The last request the handler saw, with its body already read.</summary>
    internal HttpRequestMessage? LastRequest { get; private set; }

    /// <summary>Body of the last request, as sent.</summary>
    internal string LastRequestBody { get; private set; } = string.Empty;

    /// <summary>Every request URL the handler saw, in order.</summary>
    internal List<Uri> RequestedUrls { get; } = new();

    /// <summary>Replay an SSE body with a 200.</summary>
    internal static StubHttpMessageHandler Sse(string body) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new MemoryStream(Encoding.UTF8.GetBytes(body)))
            {
                Headers = { ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("text/event-stream") },
            },
        });

    /// <summary>Replay an SSE body that dies part-way through, as a dropped connection does.</summary>
    internal static StubHttpMessageHandler SseThenDrop(string body) =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new DroppingStream(Encoding.UTF8.GetBytes(body))),
        });

    /// <summary>Replay a stream that never produces anything and never completes, until cancelled.</summary>
    internal static StubHttpMessageHandler Hangs() =>
        new(_ => new HttpResponseMessage(HttpStatusCode.OK)
        {
            Content = new StreamContent(new HangingStream()),
        });

    /// <summary>Replay an error status with a JSON body.</summary>
    internal static StubHttpMessageHandler Error(HttpStatusCode status, string json, string? retryAfter = null)
    {
        return new StubHttpMessageHandler(_ =>
        {
            var response = new HttpResponseMessage(status)
            {
                Content = new StringContent(json, Encoding.UTF8, "application/json"),
            };
            if (retryAfter is not null)
                response.Headers.TryAddWithoutValidation("Retry-After", retryAfter);
            return response;
        });
    }

    protected override async Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        LastRequest = request;
        RequestedUrls.Add(request.RequestUri!);
        if (request.Content is not null)
            LastRequestBody = await request.Content.ReadAsStringAsync(cancellationToken);

        return _respond(request);
    }

    /// <summary>Yields its bytes, then faults exactly as a severed connection does.</summary>
    private sealed class DroppingStream : Stream
    {
        private readonly byte[] _payload;
        private int _position;

        internal DroppingStream(byte[] payload) => _payload = payload;

        public override int Read(byte[] buffer, int offset, int count)
        {
            if (_position >= _payload.Length)
                throw new IOException("The connection was closed by the peer.");

            var take = Math.Min(count, _payload.Length - _position);
            Array.Copy(_payload, _position, buffer, offset, take);
            _position += take;
            return take;
        }

        public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            if (_position >= _payload.Length)
                throw new IOException("The connection was closed by the peer.");

            var take = Math.Min(buffer.Length, _payload.Length - _position);
            _payload.AsSpan(_position, take).CopyTo(buffer.Span);
            _position += take;
            return ValueTask.FromResult(take);
        }

        public override bool CanRead => true;
        public override bool CanSeek => false;
        public override bool CanWrite => false;
        public override long Length => _payload.Length;
        public override long Position { get => _position; set => throw new NotSupportedException(); }
        public override void Flush() { }
        public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
        public override void SetLength(long value) => throw new NotSupportedException();
        public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();
    }

    /// <summary>Accepts the connection and then says nothing — the case an idle timeout exists to catch.</summary>
    private sealed class HangingStream : Stream
    {
        public override async ValueTask<int> ReadAsync(
            Memory<byte> buffer, CancellationToken cancellationToken = default)
        {
            await Task.Delay(Timeout.Infinite, cancellationToken);
            return 0;
        }

        public override int Read(byte[] buffer, int offset, int count)
        {
            Thread.Sleep(Timeout.Infinite);
            return 0;
        }

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
}
