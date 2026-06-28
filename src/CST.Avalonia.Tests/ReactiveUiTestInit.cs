using System;
using System.Reactive.Concurrency;
using ReactiveUI.Builder;

namespace CST.Avalonia.Tests;

/// <summary>
/// ReactiveUI 23 requires explicit builder initialization (the app does this in App at startup), or any
/// <c>WhenAnyValue</c>/<c>ReactiveCommand</c> use throws. Unit tests that construct ReactiveObject view
/// models call <see cref="Ensure"/> once. Uses a synchronous scheduler and a swallowing exception handler
/// so background observables (e.g. a debounced live search) can't crash the test host.
/// </summary>
internal static class ReactiveUiTestInit
{
    private static readonly object Gate = new();
    private static bool _done;

    public static void Ensure()
    {
        lock (Gate)
        {
            if (_done) return;
            _done = true;
            try
            {
                RxAppBuilder.CreateReactiveUIBuilder()
                    .WithMainThreadScheduler(CurrentThreadScheduler.Instance)
                    .WithExceptionHandler(new SwallowingExceptionHandler())
                    .Build();
            }
            catch
            {
                // Already initialized elsewhere in this process - that's fine.
            }
        }
    }

    private sealed class SwallowingExceptionHandler : IObserver<Exception>
    {
        public void OnNext(Exception value) { }
        public void OnError(Exception error) { }
        public void OnCompleted() { }
    }
}
