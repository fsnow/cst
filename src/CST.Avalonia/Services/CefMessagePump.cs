using System;
using System.Collections;
using System.Linq;
using System.Reactive.Linq;
using System.Reflection;
using Avalonia.Threading;
using Serilog;
using Xilium.CefGlue;
using Xilium.CefGlue.Common;
using Xilium.CefGlue.Common.Handlers;

namespace CST.Avalonia.Services
{
    /// <summary>
    /// Slows CefGlue's macOS message pump from ~1 kHz to 30 Hz. (#523)
    ///
    /// <para><b>What CEF asks for.</b> On macOS CEF does not run its own loop — CefGlue sets
    /// <c>ExternalMessagePump = true</c> (hard-coded; <c>MultiThreadedMessageLoop</c> on Windows and Linux,
    /// which is why this is macOS-only) and the host must drive it. CEF calls
    /// <c>OnScheduleMessagePumpWork(delayMs)</c> to ask for a <c>DoMessageLoopWork()</c> call.</para>
    ///
    /// <para><b>What CefGlue does.</b> <c>AvaloniaBrowserProcessHandler</c>, decompiled from the shipped
    /// <c>Xilium.CefGlue.Avalonia.dll</c> 120.6099.207 and unchanged upstream since 2019:</para>
    /// <code>
    /// if (delayMs &lt;= 0) delayMs = 1L;
    /// _current = Observable.Interval(TimeSpan.FromMilliseconds(delayMs))   // repeating
    ///     .ObserveOn(AvaloniaScheduler.Instance)
    ///     .Subscribe(_ =&gt; CefRuntime.DoMessageLoopWork());
    /// </code>
    /// <para>The 1 ms floor is the defect: the interval repeats until a later schedule call replaces it, so the
    /// UI thread wakes ~1000 times a second to ask CEF for work and be told there is none. Measured on an M4:
    /// ~3,000–3,900 context switches/second and 37–39% CPU with a book open, almost all of it system time.</para>
    ///
    /// <para><b>Why this keeps the repeating timer.</b> A single-shot pump is what CEF's own reference
    /// implementation uses, and it was tried first — it breaks the browser. CEF calls
    /// <c>OnScheduleMessagePumpWork</c> during <c>CefRuntime.Initialize</c>, before Avalonia's dispatcher loop
    /// is running; if those early ticks are dropped a one-shot chain never rearms, and CEF — which launches its
    /// child processes and drives navigation inside <c>DoMessageLoopWork</c> — silently stops. Observed: browser
    /// created, one subprocess instead of six, no navigation, no exception. <b>The repeating interval is an
    /// accidental watchdog</b>, and removing it removes the recovery. So keep the structure and fix only the
    /// floor: 33 ms, matching CEF's reference <c>kMaxTimerDelay</c> of 1000/30. Same self-healing, 30× fewer
    /// wakeups. Do not "improve" this into a bare one-shot without pairing it with a keepalive.</para>
    /// </summary>
    internal static class CefMessagePump
    {
        /// <summary>
        /// The pump period. CEF's reference pump caps its timer delay at 1000/30 ms so a scheduled tick cannot
        /// sleep too long; we use the same number as a floor as well, since the interval repeats.
        /// </summary>
        private const long PumpIntervalMs = 33;

        /// <summary>
        /// How many one-second attempts to make ONCE CEF IS UP before giving up.
        ///
        /// <para>Deliberately not a wall-clock cap. CEF initializes lazily on the first WebView, and a restored
        /// layout with no welcome tab, no book and no dictionary panel creates no WebView at startup — so CEF
        /// may not exist until the reader opens a book, which could be an hour in. A wall-clock cap would expire
        /// long before that and leave the whole session on the 1 kHz pump, silently. (fable review)</para>
        /// </summary>
        private const int MaxAttemptsAfterCefIsUp = 30;

        /// <summary>How long after the swap to re-check for a subscription re-armed by a racing schedule call.</summary>
        private const int RaceRecheckMs = 250;

        /// <summary>
        /// Set when the install loop is DONE — whether it succeeded, gave up, or threw. Named for what it
        /// actually guards; it does not mean the pump was replaced. (fable review: the old name lied.)
        /// </summary>
        private static bool _finished;

        /// <summary>
        /// Arranges for the pump to be replaced once CefGlue has initialized CEF <b>normally</b>.
        ///
        /// <para><b>Why not initialize CEF ourselves.</b> The staged-initialization seam
        /// (<c>CefRuntimeLoader._delayedInitialization</c>) does let a host pre-initialize CEF with its own
        /// handler, and it was tried — it works, and it breaks the browser for the reason in the class remarks:
        /// CEF's first pump requests arrive before the dispatcher loop is running. Letting CefGlue do exactly
        /// what it always does, and swapping the handler afterwards, removes every ordering question.</para>
        ///
        /// <para>Polls on the UI thread because there is no event for "CEF is up" that does not couple this to a
        /// particular view. The timer stops on the first success, and gives up only after
        /// <see cref="MaxAttemptsAfterCefIsUp"/> attempts made while CEF is actually running.</para>
        ///
        /// <para><b>The cost, stated plainly:</b> the swap reaches into four non-public members by name. A
        /// package upgrade that renames any of them leaves us on the 1 kHz pump — a performance regression with
        /// no error. Guarded two ways: every lookup is null-checked and the whole thing no-ops rather than
        /// throwing, and <c>CefMessagePumpSeamTests</c> pins each member so a bump fails the build. Same
        /// approach, and the same reasoning, as <see cref="CefBrowserAccess"/>.</para>
        /// </summary>
        public static void ScheduleInstall()
        {
            if (!OperatingSystem.IsMacOS()) return;   // no external pump on Windows/Linux

            int attemptsAfterCefIsUp = 0;
            DispatcherTimer? timer = null;
            timer = new DispatcherTimer(TimeSpan.FromSeconds(1), DispatcherPriority.Background, (_, _) =>
            {
                if (_finished || TryReplaceHandler())
                {
                    timer!.Stop();
                    return;
                }

                // Only count attempts once CEF actually exists. Before that we are waiting for the reader to
                // open something that creates a WebView, and that wait is legitimately unbounded.
                if (!CefRuntime.IsInitialized) return;

                if (++attemptsAfterCefIsUp >= MaxAttemptsAfterCefIsUp)
                {
                    timer!.Stop();
                    _finished = true;
                    Log.Warning("CEF pump NOT replaced after {Attempts} attempts with CEF running; continuing on " +
                                "CefGlue's 1 kHz pump. Expect elevated idle CPU on macOS (#523).",
                        MaxAttemptsAfterCefIsUp);
                }
            });
            timer.Start();
        }

        /// <summary>
        /// Swaps our handler in, primes it, then stops the outgoing 1 ms interval — in that order, so the pump
        /// is never unattended. Returns false (quietly) until CEF is up.
        /// </summary>
        private static bool TryReplaceHandler()
        {
            try
            {
                if (!CefRuntime.IsInitialized) return false;

                var app = FindLiveCefApp();
                if (app is null) return false;

                var commonHandler = BrowserProcessHandlerField(app.GetType())?.GetValue(app);
                if (commonHandler is null) return false;

                var handlerField = InnerHandlerField(commonHandler.GetType());
                if (handlerField is null) return false;

                var outgoing = handlerField.GetValue(commonHandler);

                var ours = new ThrottledPumpHandler();
                handlerField.SetValue(commonHandler, ours);   // future schedule calls come to us
                ours.Prime();                                 // start pumping BEFORE stopping the old timer

                StopOutgoingInterval(outgoing);

                // CEF may call OnScheduleMessagePumpWork from a non-UI thread. A call already inside the OLD
                // handler when we swapped can assign a fresh 1 ms subscription AFTER we disposed the one we
                // read - leaving an undisposed 1 kHz timer running beside ours while we log success. The write
                // above is a plain reflection store with no barrier, so the window is real if brief. Re-check
                // shortly after, by which time any in-flight call has landed. (fable review)
                DispatcherTimer.RunOnce(() => StopOutgoingInterval(outgoing),
                    TimeSpan.FromMilliseconds(RaceRecheckMs));

                _finished = true;
                Log.Information("CEF message pump replaced: {Interval} ms interval, was 1 ms (#523).", PumpIntervalMs);
                return true;
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "CEF pump replacement failed; continuing on CefGlue's default pump (#523).");
                _finished = true;   // an operation that threw once will throw every second; do not spam
                return true;
            }
        }

        /// <summary>
        /// The live <c>BrowserCefApp</c>. <c>CefApp</c> keeps its instances in a private static dictionary,
        /// populated when CEF first asks for the browser-process handler — so this returns null until CEF is up.
        /// </summary>
        internal static object? FindLiveCefApp()
        {
            var roots = RootsField()?.GetValue(null) as IDictionary;
            if (roots is null) return null;

            foreach (var value in roots.Values)
                if (value is not null && BrowserProcessHandlerField(value.GetType()) is not null)
                    return value;

            return null;
        }

        /// <summary><c>CefApp._roots</c> — the live app instances.</summary>
        internal static FieldInfo? RootsField() =>
            typeof(CefApp).GetField("_roots", BindingFlags.NonPublic | BindingFlags.Static);

        /// <summary><c>BrowserCefApp._browserProcessHandler</c>.</summary>
        internal static FieldInfo? BrowserProcessHandlerField(Type appType) =>
            appType.GetField("_browserProcessHandler", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// <c>CommonBrowserProcessHandler._handler</c> — the swappable slot. Declared readonly; readonly
        /// instance fields are still writable through reflection.
        /// </summary>
        internal static FieldInfo? InnerHandlerField(Type commonHandlerType) =>
            commonHandlerType.GetField("_handler", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary><c>AvaloniaBrowserProcessHandler._current</c> — the running 1 ms subscription.</summary>
        internal static FieldInfo? CurrentSubscriptionField(Type handlerType) =>
            handlerType.GetField("_current", BindingFlags.NonPublic | BindingFlags.Instance);

        /// <summary>
        /// Disposes the outgoing handler's subscription. Best-effort: if the field has moved, the old 1 ms
        /// interval keeps running alongside ours, which costs CPU but breaks nothing — so this must never throw
        /// the swap away after it has already succeeded.
        /// </summary>
        private static void StopOutgoingInterval(object? outgoing)
        {
            if (outgoing is null) return;
            try
            {
                if (CurrentSubscriptionField(outgoing.GetType())?.GetValue(outgoing) is IDisposable running)
                    running.Dispose();
                else
                    Log.Warning("CEF pump: the outgoing handler's subscription was not found; its 1 ms timer " +
                                "may still be running alongside the replacement (#523).");
            }
            catch (Exception ex)
            {
                Log.Warning(ex, "CEF pump: could not stop the outgoing 1 ms timer (#523).");
            }
        }

        /// <summary>Delay before the next pump tick, for a given CEF request. Extracted so the arithmetic is
        /// testable without CEF. A request of &lt;= 0 means "now"; anything longer than the pump period is capped,
        /// because the repeating interval is also the watchdog and must not sleep through a lost tick.</summary>
        internal static long DueDelayMs(long requested) => requested <= 0 ? 0 : Math.Min(requested, PumpIntervalMs);

        /// <summary>
        /// CefGlue's pump with a sane period: repeating (so it is still self-healing), first tick honouring what
        /// CEF asked for, and "now" meaning now.
        ///
        /// <para><b>Why it does not simply re-arm on every call.</b> Upstream disposes and recreates its
        /// subscription on every <c>OnScheduleMessagePumpWork</c>, and CEF calls that in rapid succession while
        /// it is busy. At a 1 ms period that reset is harmless. At 33 ms it is not: a burst of schedule calls
        /// spaced under 33 ms apart would perpetually postpone the first tick, starving
        /// <c>DoMessageLoopWork</c> exactly when CEF has the most to do. So a pending tick that already fires
        /// soon enough is left alone, and a request for immediate work pumps immediately rather than waiting out
        /// a fresh 33 ms. (fable review)</para>
        /// </summary>
        private sealed class ThrottledPumpHandler : BrowserProcessHandler
        {
            private readonly object _gate = new();
            private IDisposable? _current;

            /// <summary>When the pending tick is due, on <see cref="Environment.TickCount64"/>'s clock.</summary>
            private long _dueAt;

            /// <summary>Starts pumping immediately, so there is no gap between our handler being installed and
            /// the outgoing timer being stopped.</summary>
            internal void Prime() => Schedule(0);

            protected override void OnScheduleMessagePumpWork(long delayMs) => Schedule(delayMs);

            private void Schedule(long delayMs)
            {
                long due = DueDelayMs(delayMs);

                lock (_gate)
                {
                    long now = Environment.TickCount64;

                    // Leave a pending tick alone when it already fires at least as soon as this request wants.
                    // This is what stops a burst of schedule calls from pushing the tick forever into the future.
                    bool pendingIsSoonEnough = _current is not null && _dueAt <= now + due;

                    if (!pendingIsSoonEnough)
                    {
                        _current?.Dispose();
                        _dueAt = now + due;

                        // dueTime + period: the first tick honours CEF's request, then it repeats as the
                        // watchdog. Interval() could only do the latter.
                        _current = Observable.Timer(
                                TimeSpan.FromMilliseconds(due),
                                TimeSpan.FromMilliseconds(PumpIntervalMs))
                            .Subscribe(_ => OnTick());
                    }
                }

                // "Now" means now. Upstream turned this into a 1 ms wait; turning it into a 33 ms wait would add
                // latency to every urgent wake, which is the one regression this change must not cause.
                if (delayMs <= 0) Pump();
            }

            private void OnTick()
            {
                lock (_gate) { _dueAt = Environment.TickCount64 + PumpIntervalMs; }
                Pump();
            }

            /// <summary>
            /// Upstream marshals with <c>.ObserveOn(AvaloniaScheduler.Instance)</c>; AvaloniaScheduler is not in
            /// the Avalonia packages this project references, so post to the dispatcher directly — the same hop
            /// at the same priority. <c>DoMessageLoopWork</c> must run on the thread that owns the CEF loop.
            /// Normal priority deliberately: Background would risk starving the pump under sustained UI work,
            /// which is worse than the reverse.
            /// </summary>
            private static void Pump() =>
                Dispatcher.UIThread.Post(static () => CefRuntime.DoMessageLoopWork(), DispatcherPriority.Normal);
        }
    }
}
