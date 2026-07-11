using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging.Abstractions;
using ModelContextProtocol.Client;
using ModelContextProtocol.Protocol;
using ModelContextProtocol.Server;
using Serilog;

namespace CST.Avalonia.Services.LocalApi.Mcp
{
    /// <summary>
    /// The <c>--mcp-bridge</c> relay (#278): a TRANSPARENT stdio ↔ <c>/mcp</c> pump. An MCP chat client (Claude
    /// Desktop) spawns CST Reader with <c>--mcp-bridge</c> and pipes its stdio; this relays JSON-RPC between that
    /// stdio and the running app's loopback <c>/mcp</c> (Streamable HTTP + bearer). So the client config carries
    /// no port/token and the API can stay ephemeral. Transparent = no tool re-declaration: the client's own
    /// <c>initialize</c> bootstraps the session and the HTTP transport manages <c>Mcp-Session-Id</c> + the SSE
    /// stream around it (verified against the SDK internals — <c>ConnectAsync</c> does no handshake).
    /// </summary>
    internal static class McpBridge
    {
        // JSON-RPC "server error" code, synthesized when the bridge can't reach the app so a waiting client
        // request never hangs.
        private const int UnreachableErrorCode = -32000;

        /// <summary>
        /// Entry point for the <c>--mcp-bridge</c> process: relays this process's STDIO to the running app's
        /// <c>/mcp</c>, using the port + token from <c>local-api.json</c> in <paramref name="handshakeDirectory"/>.
        /// Captures the real stdout for the JSON-RPC transport BEFORE redirecting <see cref="Console.Out"/> away
        /// from fd 1, so stray writes in shared code can't corrupt the stream (must-have #4). Returns on stdin EOF.
        /// </summary>
        public static async Task RunFromStdioAsync(string handshakeDirectory, CancellationToken ct)
        {
            await using var clientSide = new StdioServerTransport("cst-reader", NullLoggerFactory.Instance);
            Console.SetOut(Console.Error);   // any stray Console.Out goes to stderr, never onto the JSON-RPC stdout

            var info = await ReadHandshakeWithRetryAsync(handshakeDirectory, ct).ConfigureAwait(false);
            if (info is null)
            {
                // Phase 2: the app must already be running. (Phase 3 adds launch-or-attach; Phase 4 can answer
                // the client's `initialize` with an error instead of exiting.)
                Log.Warning("--mcp-bridge: CST Reader is not running (no {File}); exiting.", LocalApiInfo.FileName);
                return;
            }

            await RunAsync(clientSide, BuildHttpOptions(info), httpClient: null, ct).ConfigureAwait(false);
        }

        /// <summary>The Streamable-HTTP transport options for the running app's <c>/mcp</c> — explicit mode
        /// (not AutoDetect, which can fall back to disabled legacy SSE) + the bearer from the handshake file.</summary>
        internal static HttpClientTransportOptions BuildHttpOptions(LocalApiInfo info) => new()
        {
            Endpoint = new Uri($"http://127.0.0.1:{info.Port}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + info.Token },
        };

        // The app may be mid-startup writing a fresh handshake file; give it a short grace window.
        private static async Task<LocalApiInfo?> ReadHandshakeWithRetryAsync(string dir, CancellationToken ct)
        {
            for (int i = 0; i < 12 && !ct.IsCancellationRequested; i++)
            {
                var info = LocalApiInfo.Read(dir);
                if (info is not null) return info;
                try { await Task.Delay(250, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return null; }
            }
            return LocalApiInfo.Read(dir);
        }

        /// <summary>
        /// Pump <see cref="JsonRpcMessage"/>s between <paramref name="clientSide"/> (stdio in production;
        /// in-memory streams in tests) and the running app's <c>/mcp</c>. Returns when either side ends
        /// (stdin EOF, or the app going away). Does not dispose <paramref name="clientSide"/> (the caller owns it).
        /// </summary>
        public static async Task RunAsync(
            ITransport clientSide, HttpClientTransportOptions httpOptions, HttpClient? httpClient, CancellationToken ct)
        {
            var httpTransport = httpClient is null
                ? new HttpClientTransport(httpOptions, NullLoggerFactory.Instance)
                : new HttpClientTransport(httpOptions, httpClient, NullLoggerFactory.Instance, ownsHttpClient: false);
            // Disposing at the end sends a DELETE to end the server session (else it lingers to the idle timeout).
            await using var serverSide = await httpTransport.ConnectAsync(ct).ConfigureAwait(false);

            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var inFlight = new ConcurrentDictionary<Task, byte>();

            // client -> server: each message on its OWN task. A sequential `await SendMessageAsync` here would
            // serialize concurrent tool calls, block `notifications/cancelled` mid-call, and can DEADLOCK on a
            // server->client request. (Fable review, must-have #1.)
            async Task ClientToServer()
            {
                try
                {
                    await foreach (var msg in clientSide.MessageReader.ReadAllAsync(cts.Token).ConfigureAwait(false))
                    {
                        var task = ForwardAsync(msg);
                        inFlight[task] = 0;
                        _ = task.ContinueWith(t => inFlight.TryRemove(t, out _), TaskScheduler.Default);
                    }
                }
                catch (OperationCanceledException) { }
            }

            // server -> client: sequential is fine (the stdio/stream send path is internally serialized).
            async Task ServerToClient()
            {
                try
                {
                    await foreach (var msg in serverSide.MessageReader.ReadAllAsync(cts.Token).ConfigureAwait(false))
                        await clientSide.SendMessageAsync(msg, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch { /* server stream ended (app gone) */ }
            }

            // Forward one client->server message; if it was a request and the send throws (stale token 401,
            // connection refused), synthesize a JsonRpcError so the client doesn't hang. (must-have #2.)
            async Task ForwardAsync(JsonRpcMessage msg)
            {
                try
                {
                    await serverSide.SendMessageAsync(msg, cts.Token).ConfigureAwait(false);
                }
                catch (OperationCanceledException) { }
                catch (Exception ex)
                {
                    if (msg is JsonRpcRequest req)
                    {
                        var error = new JsonRpcError
                        {
                            Id = req.Id,
                            Error = new JsonRpcErrorDetail
                            {
                                Code = UnreachableErrorCode,
                                Message = "CST Reader is not reachable (is it running with AI features enabled?): " + ex.Message,
                            },
                        };
                        try { await clientSide.SendMessageAsync(error, cts.Token).ConfigureAwait(false); }
                        catch { /* client side gone too */ }
                    }
                }
            }

            var c2s = ClientToServer();
            var s2c = ServerToClient();
            await Task.WhenAny(c2s, s2c).ConfigureAwait(false);
            cts.Cancel();
            try { await Task.WhenAll(inFlight.Keys.ToArray()).ConfigureAwait(false); } catch { }
        }
    }
}
