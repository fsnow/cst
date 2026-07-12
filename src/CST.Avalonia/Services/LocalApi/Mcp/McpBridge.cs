using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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

        // After launching the app, poll for its handshake. A cold start (CEF init + a possible first-run index
        // build) can take a while, so keep a generous window — but stay under a typical MCP client's spawn
        // timeout (~60s) with margin.
        private const int LaunchPollAttempts = 90;
        private const int LaunchPollDelayMs = 500;   // ~45s total

        // While attached, poll the GUI's liveness this often. When it dies, the bridge exits (see the watcher).
        private const int AppLivenessPollMs = 1000;

        private const string NotReadyMessage =
            "CST Reader did not become ready for MCP. Make sure it is installed and that AI features are enabled " +
            "in CST Reader → Settings → AI. If it was just launched it may still be starting — try again in a moment.";

        /// <summary>
        /// Entry point for the <c>--mcp-bridge</c> process: relays this process's STDIO to the running app's
        /// <c>/mcp</c>, using the port + token from <c>local-api.json</c> in <paramref name="handshakeDirectory"/>.
        /// Captures the real stdout for the JSON-RPC transport BEFORE redirecting <see cref="Console.Out"/> away
        /// from fd 1, so stray writes in shared code can't corrupt the stream (must-have #4). Returns on stdin EOF.
        ///
        /// Launch-or-attach (#278 Phase 3): if no instance has published a handshake, <c>open</c> the app bundle —
        /// which reuses a running instance or launches a fresh windowed one — then wait for it to come up, so the
        /// user gets the UI and the agent on one instance. If it still never becomes ready (not installed, still
        /// starting, or AI features off), answer the client's <c>initialize</c> with a clear error rather than
        /// exiting silently.
        /// </summary>
        public static async Task RunFromStdioAsync(string handshakeDirectory, CancellationToken ct)
        {
            await using var clientSide = new StdioServerTransport("cst-reader", NullLoggerFactory.Instance);
            Console.SetOut(Console.Error);   // any stray Console.Out goes to stderr, never onto the JSON-RPC stdout

            var info = ReadLiveHandshake(handshakeDirectory);   // attach: a RUNNING instance already published its handshake
            if (info is null)
            {
                Log.Information("--mcp-bridge: no {File}; launching CST Reader and waiting for it to come up…", LocalApiInfo.FileName);
                TryLaunchApp();
                info = await ReadHandshakeWithRetryAsync(handshakeDirectory, ct, LaunchPollAttempts, LaunchPollDelayMs)
                    .ConfigureAwait(false);
            }

            if (info is null)
            {
                Log.Warning("--mcp-bridge: CST Reader did not become ready; erroring the client's requests.");
                await AnswerRequestsWithErrorAsync(clientSide, NotReadyMessage, ct).ConfigureAwait(false);
                return;
            }

            // Relay until stdin EOF OR the app's stream ends OR the app PROCESS goes away. The last is the key
            // case: if the user closes/kills CST Reader, the bridge must NOT dangle as a backend-less process, and
            // it must NOT resurrect a window the user deliberately closed. So it watches the GUI's pid and, when it
            // dies (clean quit, crash, or kill -9 — hence pid, not the handshake file which a kill -9 leaves stale),
            // tears the relay down and exits. Net: it's always zero or two processes, never a lone bridge. (#278)
            using var appGoneCts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            var watcher = info.Pid > 0
                ? WatchProcessLivenessAsync(info.Pid, appGoneCts, AppLivenessPollMs, appGoneCts.Token)
                : Task.CompletedTask;
            try
            {
                await RunAsync(clientSide, BuildHttpOptions(info), httpClient: null, appGoneCts.Token).ConfigureAwait(false);
            }
            finally
            {
                appGoneCts.Cancel();                              // stop the watcher if the relay ended for another reason
                try { await watcher.ConfigureAwait(false); } catch { }
            }
        }

        /// <summary>
        /// Poll <paramref name="pid"/> (the attached CST Reader GUI, from <c>local-api.json</c>); when it is no
        /// longer running, cancel <paramref name="onGone"/> so the relay unwinds and the bridge process exits.
        /// Deliberately does NOT relaunch the app — a user closing it is authoritative. Checking the pid (not the
        /// handshake file) catches every death: clean quit, crash, and <c>kill -9</c> (which leaves the file stale).
        /// </summary>
        internal static async Task WatchProcessLivenessAsync(
            int pid, CancellationTokenSource onGone, int pollMs, CancellationToken ct)
        {
            try
            {
                while (!ct.IsCancellationRequested)
                {
                    if (!IsProcessAlive(pid))
                    {
                        Log.Information("--mcp-bridge: CST Reader (pid {Pid}) is gone; shutting the bridge down.", pid);
                        onGone.Cancel();
                        return;
                    }
                    await Task.Delay(pollMs, ct).ConfigureAwait(false);
                }
            }
            catch (OperationCanceledException) { }
        }

        /// <summary>True while a process with <paramref name="pid"/> exists. <see cref="Process.GetProcessById(int)"/>
        /// throws <see cref="ArgumentException"/> when it doesn't — the canonical cross-platform liveness check.</summary>
        private static bool IsProcessAlive(int pid)
        {
            try { using var _ = Process.GetProcessById(pid); return true; }
            catch (ArgumentException) { return false; }
        }

        /// <summary>
        /// Bring up CST Reader so it publishes its handshake. macOS only: <c>open &lt;bundle&gt;</c> reuses a running
        /// instance or launches a fresh windowed one (LaunchServices single-instancing — sidesteps the #279
        /// multi-instance ambiguity). Deliberately NOT passed <c>--mcp-bridge</c>: the launched instance is the
        /// normal GUI app. Best-effort — on any failure we fall through to the not-ready error path.
        /// </summary>
        private static void TryLaunchApp()
        {
            if (!OperatingSystem.IsMacOS())
            {
                Log.Warning("--mcp-bridge: auto-launch is macOS-only; start CST Reader manually.");
                return;
            }

            var bundle = AppBundleFromExecutablePath(Environment.ProcessPath);
            if (bundle is null)
            {
                Log.Warning("--mcp-bridge: could not locate the .app bundle from {Path}; start CST Reader manually.",
                    Environment.ProcessPath);
                return;
            }

            try
            {
                var psi = new ProcessStartInfo("open") { UseShellExecute = false };
                psi.ArgumentList.Add(bundle);
                using var _ = Process.Start(psi);
                Log.Information("--mcp-bridge: `open`ed {Bundle} (reuses a running instance or launches one).", bundle);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "--mcp-bridge: failed to launch CST Reader via `open`.");
            }
        }

        /// <summary>
        /// Walk up from the bridge executable (…/CST Reader.app/Contents/MacOS/CST.Avalonia) to the enclosing
        /// <c>.app</c> bundle. Returns null outside a bundle (e.g. <c>dotnet run</c> in dev). Pure/testable.
        /// </summary>
        internal static string? AppBundleFromExecutablePath(string? executablePath)
        {
            var dir = string.IsNullOrEmpty(executablePath) ? null : Path.GetDirectoryName(executablePath);
            while (!string.IsNullOrEmpty(dir))
            {
                if (dir.EndsWith(".app", StringComparison.OrdinalIgnoreCase)) return dir;
                dir = Path.GetDirectoryName(dir);
            }
            return null;
        }

        /// <summary>
        /// The app isn't reachable: read from <paramref name="clientSide"/> and answer every JSON-RPC <em>request</em>
        /// (starting with the client's <c>initialize</c>) with a <see cref="JsonRpcError"/> carrying
        /// <paramref name="message"/>, so the chat client surfaces the reason instead of a silent spawn-timeout.
        /// Notifications (no id) are ignored. Returns on stdin EOF.
        /// </summary>
        internal static async Task AnswerRequestsWithErrorAsync(ITransport clientSide, string message, CancellationToken ct)
        {
            try
            {
                await foreach (var msg in clientSide.MessageReader.ReadAllAsync(ct).ConfigureAwait(false))
                {
                    if (msg is not JsonRpcRequest req) continue;
                    var error = new JsonRpcError
                    {
                        Id = req.Id,
                        Error = new JsonRpcErrorDetail { Code = UnreachableErrorCode, Message = message },
                    };
                    try { await clientSide.SendMessageAsync(error, ct).ConfigureAwait(false); }
                    catch { break; }   // client side gone
                }
            }
            catch (OperationCanceledException) { }
            catch { /* stdin closed */ }
        }

        /// <summary>The Streamable-HTTP transport options for the running app's <c>/mcp</c> — explicit mode
        /// (not AutoDetect, which can fall back to disabled legacy SSE) + the bearer from the handshake file.</summary>
        internal static HttpClientTransportOptions BuildHttpOptions(LocalApiInfo info) => new()
        {
            Endpoint = new Uri($"http://127.0.0.1:{info.Port}/mcp"),
            TransportMode = HttpTransportMode.StreamableHttp,
            AdditionalHeaders = new Dictionary<string, string> { ["Authorization"] = "Bearer " + info.Token },
        };

        // Poll for the handshake file while the app starts up (or finishes writing a fresh one).
        private static async Task<LocalApiInfo?> ReadHandshakeWithRetryAsync(
            string dir, CancellationToken ct, int attempts, int delayMs)
        {
            for (int i = 0; i < attempts && !ct.IsCancellationRequested; i++)
            {
                var info = ReadLiveHandshake(dir);
                if (info is not null) return info;
                try { await Task.Delay(delayMs, ct).ConfigureAwait(false); }
                catch (OperationCanceledException) { return null; }
            }
            return ReadLiveHandshake(dir);
        }

        /// <summary>
        /// Read the handshake, but treat one whose GUI process is gone as STALE (null). A crash / <c>kill -9</c>
        /// leaves <c>local-api.json</c> behind (only a clean shutdown deletes it), so without this the bridge would
        /// attach to a dead instance and the pid-watcher would immediately cancel the relay — defeating
        /// launch-or-attach in exactly the crash-recovery case. Ignoring the stale file lets attach fall through to
        /// launch, and the poll then waits for the fresh instance to overwrite it with a live pid. (#302)
        /// </summary>
        internal static LocalApiInfo? ReadLiveHandshake(string dir)
        {
            var info = LocalApiInfo.Read(dir);
            if (info is not null && info.Pid > 0 && !IsProcessAlive(info.Pid)) return null;
            return info;
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
