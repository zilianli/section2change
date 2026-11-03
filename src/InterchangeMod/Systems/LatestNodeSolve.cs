#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace InterchangeMod.Systems
{
    /// <summary>
    /// Owner-thread scheduler: one running solve and at most one replacement input.
    /// Only the supplied pure managed function runs on the pool; no completion callback
    /// can enter the game world. The owner explicitly consumes current results.
    /// </summary>
    internal sealed class LatestNodeSolve<TInput, TResult> : IDisposable
    {
        internal sealed class Completion
        {
            internal Completion(long revision, TInput input, TResult result, Exception? error, bool cancelled)
            { Revision = revision; Input = input; Result = result; Error = error; Cancelled = cancelled; }
            public long Revision { get; }
            public TInput Input { get; }
            public TResult Result { get; }
            public Exception? Error { get; }
            internal bool Cancelled { get; }
        }

        private sealed class Work
        {
            public Work(long revision, TInput input)
            { Revision = revision; Input = input; Cancellation = new CancellationTokenSource(); }
            public long Revision { get; }
            public TInput Input { get; }
            public CancellationTokenSource Cancellation { get; }
        }

        private readonly Func<TInput, CancellationToken, TResult> solve;
        private readonly IEqualityComparer<TInput> comparer;
        private Work? active, pending;
        private Task<Completion>? worker;
        private long revision;
        private bool disposed;

        public LatestNodeSolve(Func<TInput, CancellationToken, TResult> solve, IEqualityComparer<TInput>? comparer = null)
        { this.solve = solve ?? throw new ArgumentNullException(nameof(solve)); this.comparer = comparer ?? EqualityComparer<TInput>.Default; }

        public bool IsBusy => worker != null || pending != null;

        public long Request(TInput input)
        {
            if (disposed) throw new ObjectDisposedException(nameof(LatestNodeSolve<TInput, TResult>));
            if (pending != null && comparer.Equals(pending.Input, input)) return pending.Revision;
            if (pending == null && active != null && !active.Cancellation.IsCancellationRequested && comparer.Equals(active.Input, input))
                return active.Revision;
            pending?.Cancellation.Dispose();
            pending = new Work(++revision, input);
            active?.Cancellation.Cancel();
            StartPending();
            return revision;
        }

        public void Invalidate()
        {
            ++revision;
            pending?.Cancellation.Dispose(); pending = null;
            active?.Cancellation.Cancel();
        }

        public bool TryTakeCompletion(out Completion completion)
        {
            completion = null!;
            if (disposed) return false;
            if (worker == null) { StartPending(); return false; }
            if (!worker.IsCompleted) return false;
            // Execute always packages errors, so an unobserved task exception cannot
            // escape on tool exit or when an old revision is deliberately discarded.
            var finished = worker.GetAwaiter().GetResult();
            bool current = finished.Revision == revision && !active!.Cancellation.IsCancellationRequested;
            active!.Cancellation.Dispose(); active = null; worker = null;
            StartPending();
            if (!current || finished.Cancelled) return false;
            completion = finished;
            return true;
        }

        private void StartPending()
        {
            if (worker != null || pending == null) return;
            active = pending; pending = null;
            var input = active;
            var compute = solve;
            worker = Task.Run(() => Execute(compute, input));
        }

        private static Completion Execute(Func<TInput, CancellationToken, TResult> compute, Work work)
        {
            try
            {
                var token = work.Cancellation.Token;
                token.ThrowIfCancellationRequested();
                var result = compute(work.Input, token);
                token.ThrowIfCancellationRequested();
                return new Completion(work.Revision, work.Input, result, null, false);
            }
            catch (OperationCanceledException) when (work.Cancellation.IsCancellationRequested)
            { return new Completion(work.Revision, work.Input, default!, null, true); }
            catch (Exception error)
            { return new Completion(work.Revision, work.Input, default!, error, false); }
        }

        public void Dispose()
        {
            if (disposed) return;
            Invalidate(); disposed = true;
            if (worker != null)
            {
                // Do not wait on the game thread. This continuation owns only the
                // CTS, and cannot reach the scheduler, ECS, terrain, or tool instance.
                var cancellation = active!.Cancellation;
                worker.ContinueWith(_ => cancellation.Dispose(), CancellationToken.None,
                    TaskContinuationOptions.ExecuteSynchronously, TaskScheduler.Default);
                active = null; worker = null;
            }
        }
    }
}
