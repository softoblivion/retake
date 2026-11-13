using System;
using System.Diagnostics;
using System.Threading;

namespace Retake.Player
{
    /// <summary>
    /// Keeps a native handle alive while calls on it run on other threads. Every call enters the gate first and leaves
    /// it after the native call returns; <see cref="Close"/> lets no new call in, waits a bounded time for the calls in
    /// flight and runs the release then, or, when a call is still running, at the moment the last one leaves. A handle
    /// is so never destroyed under a running call, and a call that started after the close never sees it.
    /// </summary>
    public sealed class CallGate
    {
        readonly object sync = new object();
        int inFlight;
        bool closed;
        Action? release;

        /// <summary>The number of calls inside the gate now.</summary>
        public int InFlight { get { lock (sync) return inFlight; } }

        /// <summary>True once <see cref="Close"/> was called.</summary>
        public bool Closed { get { lock (sync) return closed; } }

        /// <summary>Enters the gate for one call; false after <see cref="Close"/> (the call must not touch the handle).</summary>
        public bool TryEnter()
        {
            lock (sync)
            {
                if (closed) return false;
                inFlight++;
                return true;
            }
        }

        /// <summary>Leaves the gate after a call that <see cref="TryEnter"/> let in; the last call after a timed-out close runs the release.</summary>
        public void Exit()
        {
            Action? run = null;
            lock (sync)
            {
                if (inFlight <= 0) throw new InvalidOperationException("Exit without a matching TryEnter");
                inFlight--;
                if (inFlight == 0)
                {
                    Monitor.PulseAll(sync);
                    if (closed) { run = release; release = null; }
                }
            }
            run?.Invoke();
        }

        /// <summary>
        /// Lets no new call in and waits up to <paramref name="timeoutMs"/> for the calls in flight. When none is left
        /// the release runs at once, on this thread; else it runs on the thread of the last call to leave. A second
        /// close does nothing.
        /// </summary>
        /// <returns>True when the release ran before the return.</returns>
        public bool Close(int timeoutMs, Action release)
        {
            if (release == null) throw new ArgumentNullException(nameof(release));
            lock (sync)
            {
                if (closed) return false;
                closed = true;
                var clock = Stopwatch.StartNew();
                while (inFlight > 0)
                {
                    int left = timeoutMs - (int)clock.ElapsedMilliseconds;
                    if (left <= 0)
                    {
                        this.release = release;
                        return false;
                    }
                    Monitor.Wait(sync, left);
                }
            }
            release();
            return true;
        }
    }
}
