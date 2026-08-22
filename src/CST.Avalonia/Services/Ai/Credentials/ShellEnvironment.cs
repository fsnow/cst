using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;

namespace CST.Avalonia.Services.Ai.Credentials;

/// <summary>
/// The variables a reader's login shell exports, for the case where the app cannot see them. (#817)
///
/// <para><b>The problem.</b> <see cref="Environment.GetEnvironmentVariable(string)"/> reads this process's own
/// environment, fixed at <c>exec</c>. On macOS an app launched from Finder, the Dock or Spotlight is started by
/// launchd and inherits launchd's environment — not the login shell's. A key exported from
/// <c>~/.zshrc</c>, <c>~/.zprofile</c> or <c>~/.bash_profile</c> is therefore invisible, and "no variable is
/// set" and "the variable is set somewhere this process cannot see" look identical. That is the ordinary case
/// rather than an exotic one: a shell profile is where most people who have a vendor key have put it. Windows
/// is unaffected, because a user or system variable is inherited normally.</para>
///
/// <para><b>The fix, and its shape.</b> Run the reader's shell once as a login+interactive shell, ask it for
/// its environment, and keep the answer for the session. Everything about the design follows from the fact
/// that this is expensive, fallible, and handling other people's secrets:</para>
///
/// <list type="bullet">
/// <item><b>Nothing waits for it.</b> <see cref="TryRead"/> never blocks and answers null while a probe is in
/// flight, so a slow profile delays nothing; callers that genuinely need the answer await
/// <see cref="Completion"/>, and the UI redraws when it lands. A blocking read here would freeze the Settings
/// window for as long as the reader's <c>nvm</c> initialisation takes.</item>
/// <item><b>Only the variables this feature could be asked about are retained.</b> See
/// <see cref="ShellEnvParser"/> — a login shell's environment holds far more than provider keys.</item>
/// <item><b>Every failure degrades to the process environment</b>, which is exactly today's behaviour. There
/// is one code path, not two: what Windows does, what a shell we decline to run does, and what a probe that
/// times out does are all the same thing.</item>
/// <item><b>It runs only when an AI feature is actually in play.</b> Most launches of a Pāli text reader never
/// touch this; they should not pay for a login shell, and a reader reading their process list should not find
/// one they cannot account for.</item>
/// </list>
///
/// <para><b>Nothing here is written anywhere.</b> The snapshot lives in memory for the session and is never
/// persisted — a probe result on disk is a credential on disk. The log line records a count, never a name and
/// never a value.</para>
/// </summary>
/// <summary>
/// What happened the last time the login shell was consulted. (#820)
///
/// <para>Exists because until now the probe had nowhere to report itself. A shell skipped by name, a profile
/// that timed out, and an environment that genuinely holds no provider key all produced the same thing on
/// screen — an empty "found in your environment" section — which is #817's own complaint (absence rendered as
/// absence) one layer up. Every one of these states is something a reader can act on, and none of them was
/// distinguishable before.</para>
/// </summary>
public enum ShellEnvironmentState
{
    /// <summary>Nothing has asked for a probe, or the reader has turned the feature off.</summary>
    NotRun,

    /// <summary>Windows, where the environment is inherited normally and there is nothing to look for.</summary>
    Unsupported,

    /// <summary>A probe is in flight. Reads answer null until it lands.</summary>
    Running,

    /// <summary>The reader's shell is one whose flags do not mean what this needs — <c>nu</c>, <c>csh</c>,
    /// <c>tcsh</c>. Worth saying out loud, because it is the one failure the reader can do something about.</summary>
    ShellNotSupported,

    /// <summary>The shell did not finish inside the budget, so the probe gave up rather than retry.</summary>
    TimedOut,

    /// <summary>The shell would not answer: no such shell, refused to run, or a nonzero exit both times.</summary>
    Failed,

    /// <summary>The shell answered. <see cref="ShellEnvironmentStatus.RetainedCount"/> may still be zero,
    /// which is the honest and useful answer "your shell exports none of the variables we know about".</summary>
    Completed,
}

/// <summary>The reportable outcome of the last probe, and enough detail to say something specific.</summary>
/// <param name="ShellName">The shell's basename — <c>zsh</c>, never the full path.</param>
/// <param name="RetainedCount">How many variables of interest were found. Never which ones.</param>
public sealed record ShellEnvironmentStatus(
    ShellEnvironmentState State, string? ShellName = null, int RetainedCount = 0)
{
    public static readonly ShellEnvironmentStatus NotRun = new(ShellEnvironmentState.NotRun);
    public static readonly ShellEnvironmentStatus Unsupported = new(ShellEnvironmentState.Unsupported);
    public static readonly ShellEnvironmentStatus Running = new(ShellEnvironmentState.Running);
}

public interface IShellEnvironment
{
    /// <summary>
    /// Starts the probe if it has not started. Returns immediately, and is safe to call from anywhere as often
    /// as you like — concurrent calls collapse onto the one probe.
    /// </summary>
    void Prime();

    /// <summary>
    /// Completes when the probe has finished — success, failure and timeout alike. Already complete when
    /// nothing has primed it, so awaiting this never starts a probe: a caller asking "is the environment
    /// settled?" must not be the thing that decides to run a shell.
    ///
    /// <para>Never faults. A probe that throws is a probe that found nothing.</para>
    /// </summary>
    Task Completion { get; }

    /// <summary>
    /// The probed value of one variable, or null when the probe has not finished, did not run, failed, or did
    /// not retain that name. Never blocks, and never starts a probe.
    /// </summary>
    string? TryRead(string variableName);

    /// <summary>
    /// Raised once, when a probe has finished and its answers are readable. (#817)
    ///
    /// <para>An event rather than a continuation on <see cref="Completion"/>, because a consumer built before
    /// anything primed would see <see cref="Completion"/> already complete and conclude, permanently, that
    /// there is nothing to wait for. That consumer exists: the assistant panel is a DI singleton whose
    /// construction is not ordered against startup, and it is the surface that says "not configured".</para>
    /// </summary>
    event EventHandler? Probed;

    /// <summary>
    /// Drops the snapshot and resets, so a later <see cref="Prime"/> reads the shell again. (#820)
    ///
    /// <para>What the reader unticking the control calls. It genuinely releases the values rather than merely
    /// ceasing to consult them — an app that kept a reader's keys in memory after being told to stop reading
    /// them would be obeying the letter of the instruction and not the point of it. The cost is that
    /// re-enabling runs a second shell, since there is deliberately nothing left to restore.</para>
    /// </summary>
    void Forget();

    /// <summary>What happened last time, so a surface can say so. Never names a variable. (#820)</summary>
    ShellEnvironmentStatus Status { get; }
}

/// <inheritdoc />
public sealed class ShellEnvironment : IShellEnvironment
{
    /// <summary>
    /// Per attempt, matching OpenCode's. Long enough for a heavy profile, short enough that the retry and the
    /// give-up both happen inside the time a reader spends getting to the Settings screen.
    /// </summary>
    internal static readonly TimeSpan AttemptTimeout = TimeSpan.FromSeconds(5);

    /// <summary>
    /// Shells whose flags do not mean what this code needs them to mean, skipped by name rather than
    /// attempted. <c>nu</c> is not POSIX and has no <c>-c "env -0"</c> equivalent to offer; <c>csh</c> and
    /// <c>tcsh</c> honour <c>-l</c> only as the sole argument, so the combined form is at best ignored. There
    /// is no per-shell capability matrix beyond this list — the give-up path is the compatibility story.
    /// </summary>
    private static readonly string[] SkippedShells = { "nu", "nushell", "csh", "tcsh" };

    /// <summary>
    /// What the shell is asked to run.
    ///
    /// <para><c>env -0</c> because NUL separation is what makes a value containing a newline parse back
    /// correctly — the reason not to use plain <c>env</c>.</para>
    ///
    /// <para><b>The leading <c>printf '\0'</c> is not decoration.</b> A profile prints things — version
    /// manager notices, banners, <c>fortune</c> — and that output goes to stdout with no NUL after it, so the
    /// chatter and the FIRST assignment arrive glued together in one chunk: <c>"Now using node v20\nHOME=…"</c>.
    /// Split on NUL and that chunk's name is <c>"Now using node v20\nHOME"</c>, which is not a variable name,
    /// so the first variable of the environment is silently dropped. Which variable is first depends on the
    /// machine, so the symptom is one reader's key going missing and nobody else's. The sentinel puts a NUL
    /// between the chatter and the data, so the chatter is its own discardable chunk. (fable)</para>
    /// </summary>
    internal const string ProbeCommand = "printf '\\0'; env -0";

    private readonly Func<CancellationToken, Task<IReadOnlyCollection<string>>> _namesOfInterest;
    private readonly IShellProbeRunner _runner;
    private readonly ILogger? _logger;
    private readonly bool _supported;
    private readonly string? _shellPath;

    /// <summary>
    /// The current probe, or null when none has been asked for — replaced wholesale by
    /// <see cref="Forget"/>. (#820)
    ///
    /// <para><b>A generation, not the single <c>Lazy</c> this started as.</b> A <c>Lazy</c> created once in
    /// the constructor is single-shot by construction, which was exactly the property that made eight
    /// concurrent <see cref="Prime"/> calls collapse onto one shell. Turning the feature off and on again has
    /// to run a second shell — because turning it off RELEASES the values, and a release that kept them
    /// around to restore would make "off" a lie — so the field became a generation. The collapse guarantee
    /// still holds within one.</para>
    /// </summary>
    private Lazy<Task<IReadOnlyDictionary<string, string>>>? _probe;

    private readonly object _gate = new();
    private ShellEnvironmentStatus _status = ShellEnvironmentStatus.NotRun;

    /// <summary>
    /// Which generation is current. A probe carries the number it was started under and may only write what
    /// it learned if that is still the answer. (#820, fable)
    ///
    /// <para><b>Nothing can stop a shell that is already running</b> — there is no cancellation through a
    /// spawned process — so a probe retired by <see cref="Forget"/> keeps going for up to its whole budget and
    /// then arrives with a verdict about a world that no longer exists. Without this check it stamps that
    /// verdict over the live one, and the failure is the exact thing this feature was built to stop: the
    /// screen saying "your shell profile took too long to load" beside a list of the keys a later probe
    /// found, and saying it permanently, because the newer generation has already written and will not write
    /// again.</para>
    /// </summary>
    private long _generation;

    private static readonly IReadOnlyDictionary<string, string> Empty =
        new Dictionary<string, string>(StringComparer.Ordinal);

    /// <param name="namesOfInterest">
    /// The variables worth keeping, resolved when the probe runs rather than when this is constructed. Late by
    /// design: the provider catalogue is still loading at startup, and a set captured too early would filter
    /// out the very names the reader is about to ask about.
    /// </param>
    public ShellEnvironment(
        Func<CancellationToken, Task<IReadOnlyCollection<string>>> namesOfInterest,
        ILogger? logger = null)
        : this(namesOfInterest, new ProcessShellProbeRunner(), logger,
               supported: !OperatingSystem.IsWindows(),
               shellPath: Environment.GetEnvironmentVariable("SHELL"))
    {
    }

    /// <summary>Test seam. The real runner spawns a process; the real platform check is <c>!IsWindows</c>.</summary>
    internal ShellEnvironment(
        Func<CancellationToken, Task<IReadOnlyCollection<string>>> namesOfInterest,
        IShellProbeRunner runner,
        ILogger? logger,
        bool supported,
        string? shellPath)
    {
        _namesOfInterest = namesOfInterest ?? throw new ArgumentNullException(nameof(namesOfInterest));
        _runner = runner ?? throw new ArgumentNullException(nameof(runner));
        _logger = logger;
        _supported = supported;
        _shellPath = shellPath;

        if (!_supported) _status = ShellEnvironmentStatus.Unsupported;
    }

    /// <inheritdoc />
    public event EventHandler? Probed;

    /// <inheritdoc />
    public ShellEnvironmentStatus Status
    {
        get { lock (_gate) return _status; }
    }

    // ExecutionAndPublication so that two callers priming at once run one shell between them, not two.
    private Lazy<Task<IReadOnlyDictionary<string, string>>> NewProbe(long generation) =>
        new(() => Task.Run(async () =>
            {
                var snapshot = await ProbeAsync(generation).ConfigureAwait(false);

                // Raised after the result is in hand, so a handler that reads immediately sees it — and only
                // by the generation still in service. A retired probe announcing that it landed tells every
                // surface to go and look at a snapshot that was thrown away.
                if (IsCurrent(generation))
                {
                    try { Probed?.Invoke(this, EventArgs.Empty); } catch { /* a handler's problem, not ours */ }
                }
                return snapshot;
            }),
            LazyThreadSafetyMode.ExecutionAndPublication);

    private bool IsCurrent(long generation)
    {
        lock (_gate) return generation == _generation;
    }

    /// <inheritdoc />
    public void Prime()
    {
        if (!_supported) return;

        lock (_gate)
        {
            if (_probe is null)
            {
                _probe = NewProbe(++_generation);
                if (_status.State == ShellEnvironmentState.NotRun)
                    _status = ShellEnvironmentStatus.Running;
            }

            // Materialised INSIDE the gate. The factory only hands off to the thread pool, so it cannot
            // block on anything, and doing it outside leaves a window in which a Forget retires the
            // generation between the read and the start — launching a shell the reader has just said they
            // do not want. (fable)
            _ = _probe.Value;
        }
    }

    /// <inheritdoc />
    public void Forget()
    {
        lock (_gate)
        {
            // Dropping the generation drops this object's only reference to the snapshot, which is what makes
            // "off" true rather than merely obeyed. Keeping it to restore later would leave the reader's keys
            // in the heap of an app they have just told to stop reading them.
            _probe = null;

            // Retires whatever is in flight as well as what has landed. The running shell cannot be stopped,
            // but its verdict can be refused when it finally arrives.
            _generation++;
            _status = _supported ? ShellEnvironmentStatus.NotRun : ShellEnvironmentStatus.Unsupported;
        }

        // Surfaces that showed rows from the snapshot need to stop showing them, and this is the same signal
        // that told them to start.
        try { Probed?.Invoke(this, EventArgs.Empty); } catch { /* a handler's problem, not ours */ }
    }

    /// <inheritdoc />
    public Task Completion
    {
        get
        {
            Lazy<Task<IReadOnlyDictionary<string, string>>>? probe;
            lock (_gate) probe = _probe;
            return probe is { IsValueCreated: true } ? probe.Value : Task.CompletedTask;
        }
    }

    /// <inheritdoc />
    public string? TryRead(string variableName)
    {
        if (string.IsNullOrWhiteSpace(variableName)) return null;

        Lazy<Task<IReadOnlyDictionary<string, string>>>? probe;
        lock (_gate) probe = _probe;

        // IsValueCreated first: reading must never be what starts a shell. A caller that wants the probe says
        // so by calling Prime. A null generation is a probe that has been forgotten, or never asked for.
        if (probe is not { IsValueCreated: true }) return null;

        var task = probe.Value;
        if (!task.IsCompletedSuccessfully) return null;

        return task.Result.TryGetValue(variableName, out var value) ? value : null;
    }

    private void SetStatus(long generation, ShellEnvironmentStatus status)
    {
        lock (_gate)
        {
            if (generation != _generation) return;
            _status = status;
        }
    }

    /// <summary>
    /// The whole probe. Returns an empty snapshot for every failure rather than throwing: this task is
    /// awaited by <see cref="Completion"/>, and letting an exception escape would turn every later await into
    /// a throw — inside the chat send path, which would report a shell problem as a provider problem.
    /// </summary>
    private async Task<IReadOnlyDictionary<string, string>> ProbeAsync(long generation)
    {
        try
        {
            var shell = string.IsNullOrWhiteSpace(_shellPath) ? "/bin/sh" : _shellPath!;
            var name = Path.GetFileName(shell);

            if (SkippedShells.Contains(name, StringComparer.OrdinalIgnoreCase))
            {
                _logger?.LogDebug("Shell environment probe skipped: {Shell} is not supported", name);
                SetStatus(generation, new ShellEnvironmentStatus(ShellEnvironmentState.ShellNotSupported, name));
                return Empty;
            }

            IReadOnlyCollection<string> keep;
            try
            {
                keep = await _namesOfInterest(CancellationToken.None).ConfigureAwait(false)
                       ?? Array.Empty<string>();
            }
            catch (Exception ex)
            {
                _logger?.LogDebug(ex, "Shell environment probe skipped: the variables of interest are unknown");
                SetStatus(generation, new ShellEnvironmentStatus(ShellEnvironmentState.Failed, name));
                return Empty;
            }

            if (keep.Count == 0)
            {
                // Nothing to look for is not a failure of the shell, and reporting it as one would send a
                // reader off to debug a profile that is fine.
                SetStatus(generation, new ShellEnvironmentStatus(ShellEnvironmentState.Completed, name));
                return Empty;
            }
            var wanted = new HashSet<string>(keep, StringComparer.Ordinal);

            var started = Stopwatch.StartNew();

            // -il first, and the pair is load-bearing. For bash, -l sources .bash_profile and -i sources
            // .bashrc; only both cover the two conventions a reader may have used.
            var first = _runner.Run(new ShellProbeAttempt(shell, new[] { "-il", "-c", ProbeCommand }, AttemptTimeout));

            ShellProbeResult result;
            if (first.Succeeded)
            {
                result = first;
            }
            else if (first.TimedOut)
            {
                // Deliberately no retry. A profile that hangs once hangs twice, and the second attempt buys
                // nothing but another five seconds of a shell this app has already decided to give up on.
                _logger?.LogDebug("Shell environment probe timed out: {Shell}", name);
                SetStatus(generation, new ShellEnvironmentStatus(ShellEnvironmentState.TimedOut, name));
                return Empty;
            }
            else
            {
                // A shell that rejects -i (no tty, or a profile that exits early under it) may still answer a
                // plain login shell.
                result = _runner.Run(new ShellProbeAttempt(shell, new[] { "-l", "-c", ProbeCommand }, AttemptTimeout));
                if (!result.Succeeded)
                {
                    _logger?.LogDebug("Shell environment probe found nothing: {Shell}", name);
                    SetStatus(generation, new ShellEnvironmentStatus(ShellEnvironmentState.Failed, name));
                    return Empty;
                }
            }

            var snapshot = ShellEnvParser.Parse(result.Stdout, wanted);
            started.Stop();

            // A count, never a name and never a value. The environment this walked contains the reader's
            // whole working life; the only safe thing to say about it is how much of it we kept.
            _logger?.LogInformation(
                "Shell environment probe: {Shell}, {Elapsed}ms, {Retained} of {Wanted} variables retained",
                name, started.ElapsedMilliseconds, snapshot.Count, wanted.Count);

            SetStatus(generation, new ShellEnvironmentStatus(
                ShellEnvironmentState.Completed, name, snapshot.Count));

            return snapshot;
        }
        catch (Exception ex)
        {
            _logger?.LogDebug(ex, "Shell environment probe failed");
            SetStatus(generation, new ShellEnvironmentStatus(ShellEnvironmentState.Failed));
            return Empty;
        }
    }
}

/// <summary>One run of a shell: what to run, and how long to allow it.</summary>
internal readonly record struct ShellProbeAttempt(string ShellPath, string[] Arguments, TimeSpan Timeout);

/// <summary>
/// What came back. <see cref="TimedOut"/> is distinct from a plain failure because the two get different
/// treatment: a failure is retried once with a simpler shell, a timeout ends the probe.
/// </summary>
internal readonly record struct ShellProbeResult(bool Succeeded, bool TimedOut, byte[] Stdout)
{
    public static ShellProbeResult Failed() => new(false, false, Array.Empty<byte>());
    public static ShellProbeResult Timeout() => new(false, true, Array.Empty<byte>());
    public static ShellProbeResult Ok(byte[] stdout) => new(true, false, stdout);
}

/// <summary>The seam that spawns a real process, so everything above it is testable without one.</summary>
internal interface IShellProbeRunner
{
    ShellProbeResult Run(ShellProbeAttempt attempt);
}

/// <inheritdoc />
internal sealed class ProcessShellProbeRunner : IShellProbeRunner
{
    /// <summary>
    /// Far above any real environment — a large one is tens of kilobytes — and low enough that a dotfile
    /// stuck in a loop cannot exhaust memory. A pipe sustains gigabytes per second, so an unbounded read has
    /// five seconds in which to take the whole machine down on behalf of somebody's broken profile.
    /// </summary>
    internal const int MaxStdoutBytes = 4 * 1024 * 1024;

    /// <summary>
    /// How long to keep reading after the shell has exited. Everything it wrote is already in the pipe by
    /// then, so this is generous; what it must NOT do is wait for end-of-stream. See the comment at the wait.
    /// </summary>
    private static readonly TimeSpan DrainGrace = TimeSpan.FromMilliseconds(500);

    /// <inheritdoc />
    public ShellProbeResult Run(ShellProbeAttempt attempt)
    {
        var info = new ProcessStartInfo(attempt.ShellPath)
        {
            // stdin redirected and immediately closed. An interactive shell with an open stdin and no
            // terminal is the likeliest hang of the lot: it reaches a prompt and waits forever for input
            // nobody is going to type.
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        foreach (var argument in attempt.Arguments) info.ArgumentList.Add(argument);

        Process? process = null;
        BoundedReader? reader = null;
        try
        {
            process = Process.Start(info);
            if (process is null) return ShellProbeResult.Failed();

            process.StandardInput.Close();

            // Read on a background task rather than after WaitForExit. A profile that writes more than the
            // pipe buffer holds blocks on the write until someone drains it, and a shell blocked on write
            // never exits — which would turn every chatty profile into a five-second timeout.
            reader = new BoundedReader(process.StandardOutput.BaseStream, MaxStdoutBytes);
            var reading = reader.Start();

            // stderr drained and discarded for the same blocking reason, and never waited on. Its content is
            // the reader's profile warnings, which are none of this app's business.
            _ = Task.Run(() =>
            {
                try { process.StandardError.BaseStream.CopyTo(Stream.Null); } catch { /* closing is enough */ }
            });

            if (!process.WaitForExit((int)attempt.Timeout.TotalMilliseconds))
            {
                // entireProcessTree, because the thing that hangs is rarely the shell itself: a profile that
                // execs a version manager, or blocks on a keychain prompt, leaves the subtree behind and the
                // pipe open when only the parent is killed.
                KillTree(process);
                return ShellProbeResult.Timeout();
            }

            // A GRACE, NOT A WAIT FOR EOF, and the distinction is the whole of this method. If the profile
            // started anything in the background — `some-updater &` — that grandchild inherited the stdout
            // pipe and holds the write end open after the shell exits. Waiting for end-of-stream would then
            // block forever on a probe that has ALREADY SUCCEEDED: its output is sitting in the buffer. The
            // consequences of getting this wrong are all bad and all silent — a successful probe reported as
            // a timeout, which per the ladder means give up permanently; a read thread parked for the life of
            // the app; and that thread holding the raw, unfiltered environment, which is the one allocation
            // this whole design exists to prevent. (fable)
            reading.Wait(DrainGrace);

            var bytes = reader.Bytes();

            // Releases the buffer whether or not the read thread ever comes back, so nothing beyond the
            // keep-set outlives this call even in the inherited-pipe case.
            reader.Stop();

            return process.ExitCode == 0
                ? ShellProbeResult.Ok(bytes)
                : ShellProbeResult.Failed();
        }
        catch
        {
            // No such shell, no permission to execute it, a platform that refuses to spawn: all "no snapshot".
            if (process is not null) KillTree(process);
            return ShellProbeResult.Failed();
        }
        finally
        {
            reader?.Stop();
            process?.Dispose();
        }
    }

    private static void KillTree(Process process)
    {
        try { process.Kill(entireProcessTree: true); }
        catch { /* already gone, or never ours to kill */ }
    }

    /// <summary>
    /// Reads a stream into a capped buffer, and can be told to let go of it without waiting for the reader to
    /// notice. Disposing a pipe does not reliably unblock a read that is already parked in the kernel, so
    /// "stop" here means "release the memory", which is the part that matters.
    /// </summary>
    private sealed class BoundedReader
    {
        private readonly Stream _stream;
        private readonly int _cap;
        private readonly MemoryStream _buffer = new();
        private bool _stopped;

        public BoundedReader(Stream stream, int cap) { _stream = stream; _cap = cap; }

        public Task Start() => Task.Run(() =>
        {
            try
            {
                var chunk = new byte[8192];
                int read;
                while ((read = _stream.Read(chunk, 0, chunk.Length)) > 0)
                {
                    lock (_buffer)
                    {
                        if (_stopped) return;
                        if (_buffer.Length + read > _cap) return;
                        _buffer.Write(chunk, 0, read);
                    }
                }
            }
            catch
            {
                // A closed or broken pipe is the ordinary way this ends.
            }
        });

        public byte[] Bytes()
        {
            lock (_buffer) return _buffer.ToArray();
        }

        public void Stop()
        {
            lock (_buffer)
            {
                _stopped = true;
                _buffer.SetLength(0);
                _buffer.Capacity = 0;
            }
            // DELIBERATELY NOT DISPOSING THE STREAM HERE. FileStream.Dispose blocks until an in-flight read
            // completes, and the read we want to abandon is parked on a pipe an inherited process is holding
            // open — so disposing would wait exactly as long as the wait this method exists to avoid. The
            // thread ends on its own when that process finally exits; what matters is that the bytes are
            // already gone. (fable)
        }
    }
}

/// <summary>
/// Turns <c>env -0</c> output into the handful of variables this feature is allowed to keep. (#817)
///
/// <para><b>The keep-set is the security boundary, and it is applied here rather than by the caller.</b> A
/// login shell's environment is not a list of API keys — it holds session tokens, agent sockets, and on this
/// project's own machines an Apple app-specific password that is iCloud-wide rather than notarization-scoped.
/// Retaining the whole thing for the session would put all of it in a long-lived managed dictionary, where it
/// would survive into any heap dump. So the parse keeps only names the provider catalogue or a stored
/// connection actually declares, and everything else stops existing when this method returns.</para>
///
/// <para><b>A profile is chatty.</b> Banners, version-manager warnings, <c>fortune</c> — all of it lands on
/// stdout ahead of what <c>env</c> writes. Two things handle it, and BOTH are needed: the sentinel NUL in
/// <see cref="ShellEnvironment.ProbeCommand"/> keeps the chatter from being glued to the first assignment,
/// and the name-shape check below discards whatever chunk the chatter ends up as. Dropping either one loses
/// a variable rather than raising an error — the sentinel loses the first one, the shape check lets
/// <c>"nvm: default=v20"</c> through as a variable called <c>"nvm: default"</c>.</para>
/// </summary>
internal static class ShellEnvParser
{
    /// <param name="stdout">Raw bytes: the encoding of a value is the reader's business, not ours.</param>
    /// <param name="keep">The only names that may survive this call.</param>
    public static IReadOnlyDictionary<string, string> Parse(byte[] stdout, ISet<string> keep)
    {
        var result = new Dictionary<string, string>(StringComparer.Ordinal);
        if (stdout is null || stdout.Length == 0 || keep is null || keep.Count == 0) return result;

        var text = Encoding.UTF8.GetString(stdout);
        foreach (var chunk in text.Split('\0'))
        {
            var split = chunk.IndexOf('=');
            if (split <= 0) continue;

            var name = chunk.Substring(0, split);
            if (!IsVariableName(name)) continue;
            if (!keep.Contains(name)) continue;

            var value = chunk.Substring(split + 1);
            if (string.IsNullOrWhiteSpace(value)) continue;

            result[name] = value;
        }

        return result;
    }

    /// <summary>
    /// The shape of an environment variable name. This is what rejects the banner line that happens to
    /// contain an <c>=</c> — "Loading nvm... default=v20" parses as an assignment otherwise.
    /// </summary>
    private static bool IsVariableName(string name)
    {
        if (name.Length == 0) return false;
        if (!(char.IsAsciiLetter(name[0]) || name[0] == '_')) return false;

        for (int i = 1; i < name.Length; i++)
            if (!(char.IsAsciiLetterOrDigit(name[i]) || name[i] == '_')) return false;

        return true;
    }
}
