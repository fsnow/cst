using System;

namespace CST.Avalonia.Tests
{
    /// <summary>
    /// An <see cref="IProgress{T}"/> that invokes its handler synchronously on the calling thread,
    /// unlike <see cref="Progress{T}"/> which posts the callback asynchronously (to the captured
    /// <see cref="System.Threading.SynchronizationContext"/>, or the thread pool when there is
    /// none). That async delivery races a synchronous assert made immediately after awaiting the
    /// code under test — under parallel test load the callback is delayed and the collected reports
    /// are still empty. Using this synchronous progress makes such assertions deterministic.
    /// </summary>
    public sealed class SynchronousProgress<T> : IProgress<T>
    {
        private readonly Action<T> _handler;
        public SynchronousProgress(Action<T> handler) => _handler = handler;
        public void Report(T value) => _handler(value);
    }
}
