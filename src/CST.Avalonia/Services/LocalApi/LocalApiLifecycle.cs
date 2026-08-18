using System;
using System.Threading;
using System.Threading.Tasks;
using CST.Avalonia.Models;
using Serilog;

namespace CST.Avalonia.Services.LocalApi
{
    /// <summary>
    /// Owns the loopback API server's lifetime and brings it in line with the AI settings on demand. (#529)
    ///
    /// <para>Before this, the settings were read <b>once at startup</b> and nothing subscribed to changes, so
    /// toggling "Enable AI Features", "Run the local API server" or "Enable MCP access" did nothing until the
    /// app was relaunched — which Settings had to apologise for in a note (#527/#528).</para>
    ///
    /// <para><b>One code path.</b> Startup and every later toggle both call <see cref="ApplyAsync"/>, which
    /// diffs the running state against what the settings now ask for and performs the minimum transition. There
    /// is deliberately no separate "start at launch" route: two paths would drift, and the launch one is simply
    /// the first apply.</para>
    ///
    /// <para><b>Transitions are serialised</b> on a semaphore, because the trigger is a checkbox and a user can
    /// flip one faster than Kestrel can bind and release a port. Without it, two overlapping applies could leave
    /// a server running that the settings say should be stopped — the failure being that AI access stays live
    /// after the reader switched it off, which is the wrong direction to fail in.</para>
    /// </summary>
    public sealed class LocalApiLifecycle : IAsyncDisposable
    {
        /// <summary>
        /// Which surfaces should be mounted. <b>Not simply on/off</b>: /v1 and /mcp are enabled independently
        /// and ride the same Kestrel host, so the state space is four-valued and the transitions between them
        /// are what this type exists to get right.
        /// </summary>
        internal readonly record struct Surfaces(bool Rest, bool Mcp)
        {
            /// <summary>The host runs when either surface is wanted.</summary>
            public bool ShouldRun => Rest || Mcp;

            /// <summary>Reads the desired surfaces from settings. Both flags already fold in the master switch
            /// (<c>AiSettings.LocalApiEnabled</c>/<c>McpEnabled</c> are <c>Enabled &amp;&amp; …</c>), so turning
            /// AI off yields (false, false) without this type knowing about the master at all.</summary>
            public static Surfaces From(AiSettings ai) => new(ai.LocalApiEnabled, ai.McpEnabled);

            public override string ToString() =>
                !ShouldRun ? "off" : $"{(Rest ? "/v1" : "")}{(Rest && Mcp ? "+" : "")}{(Mcp ? "/mcp" : "")}";
        }

        private readonly IServiceProvider? _services;
        private readonly string _appVersion;
        private readonly string _handshakeDirectory;
        private readonly ILogger _logger;
        private readonly Func<AiSettings> _readSettings;

        private readonly SemaphoreSlim _gate = new(1, 1);
        private LocalApiServer? _server;
        private Surfaces _running;   // the surfaces the LIVE server was constructed with

        public LocalApiLifecycle(
            IServiceProvider? services,
            string appVersion,
            string handshakeDirectory,
            ILogger logger,
            Func<AiSettings> readSettings)
        {
            _services = services;
            _appVersion = appVersion;
            _handshakeDirectory = handshakeDirectory;
            _logger = logger;
            _readSettings = readSettings;
        }

        /// <summary>
        /// True when the settings asked for a server and starting it threw, so AI access is enabled in the UI
        /// but non-functional. Surfaced for the Settings indicator (#316 A6-4). Cleared by any apply that
        /// succeeds or that stops the server — a stale failure flag would keep warning about a server the reader
        /// has since switched off.
        /// </summary>
        public bool StartFailed { get; private set; }

        /// <summary>The live server, or null. For tests and for anything needing the current base URL.</summary>
        public LocalApiServer? Server => _server;

        /// <summary>What the live server is currently serving, for logging and tests.</summary>
        internal Surfaces Running => _running;

        /// <summary>
        /// Brings the server in line with the current settings. Safe to call repeatedly and concurrently; calls
        /// are serialised and a call that finds nothing to change does nothing.
        /// </summary>
        /// <returns>True if a transition was performed.</returns>
        public async Task<bool> ApplyAsync(CancellationToken ct = default)
        {
            var desired = Surfaces.From(_readSettings());

            await _gate.WaitAsync(ct).ConfigureAwait(false);
            try
            {
                if (!desired.ShouldRun)
                {
                    if (_server is null) return false;
                    await StopCoreAsync().ConfigureAwait(false);
                    _logger.Information("Local API stopped: AI settings now ask for {Desired} (#529)", desired);
                    return true;
                }

                // Already serving exactly this. Applies fire on any AI settings change, most of which - the API
                // key, the model, remote-control consent - have nothing to do with which surfaces are mounted,
                // so bailing out here is what keeps an unrelated edit from bouncing a live server and handing
                // every connected client a new port and token for no reason.
                if (_server is not null && _running == desired) return false;

                // The surfaces are fixed at construction (LocalApiServer takes restApiEnabled/mcpEnabled as
                // readonly fields and maps its routes once, at build). So a change in COMPOSITION cannot be
                // applied in place - the host is rebuilt, and the port and token change with it. That is
                // tolerable precisely because discovery is the handshake file rather than a fixed port: the
                // --mcp-bridge relay re-reads local-api.json on every spawn (#278).
                if (_server is not null)
                    await StopCoreAsync().ConfigureAwait(false);

                await StartCoreAsync(desired, ct).ConfigureAwait(false);
                return true;
            }
            finally
            {
                _gate.Release();
            }
        }

        /// <summary>Stops and disposes the running server. Caller holds the gate.</summary>
        private async Task StopCoreAsync()
        {
            if (_server is null) return;
            try
            {
                await _server.StopAsync().ConfigureAwait(false);
                await _server.DisposeAsync().ConfigureAwait(false);
            }
            catch (Exception ex)
            {
                // A server that failed to stop cleanly must still be let go of, or the next start would leave
                // the old one holding its port with nothing tracking it.
                _logger.Warning(ex, "Local API did not stop cleanly; dropping the reference anyway (#529)");
            }
            finally
            {
                _server = null;
                _running = default;
                StartFailed = false;
            }
        }

        /// <summary>Builds and starts a server for <paramref name="desired"/>. Caller holds the gate.</summary>
        private async Task StartCoreAsync(Surfaces desired, CancellationToken ct)
        {
            try
            {
                System.IO.Directory.CreateDirectory(_handshakeDirectory);

                // Resolve tools through the shared factory (covered by AppCompositionTests) so a tool that is
                // registered but forgotten here cannot silently 404 an endpoint.
                var server = _services is { } sp
                    ? LocalApiServer.FromServiceProvider(
                        sp, _appVersion, _handshakeDirectory, _logger,
                        restApiEnabled: desired.Rest, mcpEnabled: desired.Mcp)
                    : new LocalApiServer(
                        _appVersion, _handshakeDirectory, _logger,
                        restApiEnabled: desired.Rest, mcpEnabled: desired.Mcp);

                await server.StartAsync(ct).ConfigureAwait(false);

                _server = server;
                _running = desired;
                StartFailed = false;
                _logger.Information("Local API started: serving {Desired} (#529)", desired);
            }
            catch (Exception ex)
            {
                // The API is ephemeral, so there is no "port in use" case - but any failure (loopback bind
                // blocked by security software, a DI fault) leaves AI shown as enabled while nothing is
                // listening and every bridge spawn fails. Record it so Settings can say so. (#316 A6-4)
                _server = null;
                _running = default;
                StartFailed = true;
                _logger.Error(ex, "Local API server failed to start - AI agent access will not work despite " +
                                  "being enabled in Settings");
            }
        }

        public async ValueTask DisposeAsync()
        {
            await _gate.WaitAsync().ConfigureAwait(false);
            try { await StopCoreAsync().ConfigureAwait(false); }
            finally { _gate.Release(); _gate.Dispose(); }
        }
    }
}
